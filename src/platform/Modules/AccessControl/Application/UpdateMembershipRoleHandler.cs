using System.Text.Json;
using Idata.Platform.Modules.AccessControl.Domain;

namespace Idata.Platform.Modules.AccessControl.Application;

public enum UpdateMembershipRoleOutcome
{
    /// <summary>Idempotency-Key mungon ose s'kalon validimin → 400.</summary>
    InvalidIdempotencyKey,
    /// <summary>Deny uniform (aktor i pa-autorizuar/i revokuar, ose self-role) → 403.</summary>
    AccessDenied,
    /// <summary>Target membership/user/rol i pa-caktueshëm (mungon, disabled, cross-tenant, IsSystem) → 404 identik.</summary>
    TargetNotFound,
    /// <summary>I njëjti key me target/payload/version tjetër → 409; zero mutation.</summary>
    IdempotencyConflict,
    /// <summary>expectedVersion ≠ version aktual (CAS) → 409; zero mutation.</summary>
    ConcurrencyConflict,
    /// <summary>Sukses ose replay idempotent — i njëjti rezultat publik.</summary>
    Updated,
}

public sealed record UpdateMembershipRoleCommand(
    ActorContext Actor,
    Guid MembershipId,
    Guid RoleId,
    long ExpectedVersion,
    string? IdempotencyKey);

public sealed record UpdateMembershipRoleResult(
    UpdateMembershipRoleOutcome Outcome,
    Guid? MembershipId = null,
    Guid? RoleId = null,
    long? Version = null);

/// <summary>
/// COMMAND `PUT /api/platform/memberships/{membershipId}/role`.
///
/// <para><b>Autoriteti i vetëm</b> është
/// <see cref="IMembershipRoleUnitOfWork.TryMutateRoleAsync"/>, i cili zbaton një
/// <b>authority-lock protocol</b> brenda një transaction-i: (1) lock eksplicit
/// i ÇDO fakti mutable të autorizimit sipas rendit kanonik (business →
/// memberships → users → role → permission → role_permission); (2) rivlerësim
/// PËRFUNDIMTAR fail-closed mbi vlerat e lexuara NËN LOCK; (3) `UPDATE` i
/// guarduar që përsërit `status='active'` + `version = expectedVersion`.
/// NUK pretendohet se një statement i vetëm rivlerëson atomikisht çdo autoritet
/// — nën READ COMMITTED sub-query-t `EXISTS` mund të mbeten në snapshot-in e
/// statement-it, prandaj lock-et janë eksplicite.</para>
///
/// <para>Hapat 1–3 më poshtë (key, aktor-prezent, grants) janë VETËM
/// pre-check për deny-uniform dhe për të mos hapur transaction pa nevojë —
/// ata NUK janë autoritet dhe nuk mund të divergjojnë prej tij, sepse çdo
/// predikat i tyre ri-provohet në DB para mutacionit.</para>
///
/// <para>Mutation + version++ + audit (me `command_receipt_id` +
/// correlation server-generated) + receipt commit-ohen BASHKË. Çdo refuzim
/// ose konflikt → zero mutation, zero version++, zero audit, zero receipt.</para>
/// </summary>
public sealed class UpdateMembershipRoleHandler(
    IAccessControlStore store,
    IMembershipRoleUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public static readonly string RequiredPermission =
        PermissionKey.Of(PlatformFeatures.UsersRoles, PermissionActions.Update);

    public async Task<UpdateMembershipRoleResult> HandleAsync(
        UpdateMembershipRoleCommand command, CancellationToken cancellationToken)
    {
        if (!IdempotencyKeyRules.IsValid(command.IdempotencyKey))
        {
            return new(UpdateMembershipRoleOutcome.InvalidIdempotencyKey);
        }

        if (command.Actor.UserId is not { } actorUserId
            || command.Actor.ActiveBusinessId is not { } businessId)
        {
            return new(UpdateMembershipRoleOutcome.AccessDenied);
        }

        // Pre-check (JO autoritet): deny-uniform pa hapur transaction.
        var grants = await store.GetGrantsAsync(businessId, actorUserId, cancellationToken).ConfigureAwait(false);
        if (grants is null || !grants.PermissionKeys.Contains(RequiredPermission))
        {
            return new(UpdateMembershipRoleOutcome.AccessDenied);
        }

        var idempotencyKey = command.IdempotencyKey!;
        var fingerprint = RequestFingerprint.ForMembershipRoleUpdate(
            businessId, actorUserId, command.MembershipId, command.RoleId, command.ExpectedVersion);

        var existing = await unitOfWork
            .FindReceiptAsync(businessId, CommandOperations.MembershipRoleUpdate, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return FromReceipt(existing, fingerprint);
        }

        // ── LINEARIZATION POINT: autoriteti i vetëm dhe përfundimtar. ──
        var mutation = await unitOfWork.TryMutateRoleAsync(
            new AtomicRoleMutation(
                businessId, actorUserId, command.MembershipId, command.RoleId, command.ExpectedVersion),
            cancellationToken).ConfigureAwait(false);

        if (!mutation.Applied)
        {
            await unitOfWork.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return new(mutation.Denial switch
            {
                RoleMutationDenial.TargetNotAssignable => UpdateMembershipRoleOutcome.TargetNotFound,
                RoleMutationDenial.VersionConflict => UpdateMembershipRoleOutcome.ConcurrencyConflict,
                // ActorNotAuthorized + SelfRoleChange → deny stabil 403 uniform.
                _ => UpdateMembershipRoleOutcome.AccessDenied,
            });
        }

        var now = clock.GetUtcNow();
        var receiptId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();

        // Audit i REDAKTUAR — vetëm identifikues teknikë + lidhja me komandën;
        // BeforeState = paraardhësi REAL i fituesit (nga RETURNING).
        unitOfWork.AddAudit(new AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            UserId = actorUserId,
            Entity = AuditEntities.BusinessMembership,
            EntityId = command.MembershipId,
            Action = PermissionActions.Update,
            BeforeState = JsonSerializer.Serialize(new RoleStateSnapshot(mutation.PreviousRoleId)),
            AfterState = JsonSerializer.Serialize(new RoleStateSnapshot(command.RoleId)),
            CommandReceiptId = receiptId,
            CorrelationId = correlationId,
            EventAt = now,
        });

        unitOfWork.AddReceipt(new CommandReceipt
        {
            Id = receiptId,
            BusinessId = businessId,
            OperationKey = CommandOperations.MembershipRoleUpdate,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            ResultStatus = CommandReceiptStatus.Completed,
            ResultPayload = JsonSerializer.Serialize(
                new ReceiptPayload(command.MembershipId, command.RoleId, mutation.NewVersion)),
            CreatedAt = now,
            CompletedAt = now,
        });

        var outcome = await unitOfWork.SaveAsync(cancellationToken).ConfigureAwait(false);
        switch (outcome)
        {
            case CommandSaveOutcome.Saved:
                return new(
                    UpdateMembershipRoleOutcome.Updated, command.MembershipId, command.RoleId, mutation.NewVersion);

            default:
                // Gara e idempotencës: transaction-i u rrëzua i tëri (zero efekt);
                // fituesi e ka shkruar receipt-in — lexohet rezultati i tij.
                var winner = await unitOfWork
                    .FindReceiptAsync(businessId, CommandOperations.MembershipRoleUpdate, idempotencyKey, cancellationToken)
                    .ConfigureAwait(false);
                return winner is null
                    ? new(UpdateMembershipRoleOutcome.IdempotencyConflict)
                    : FromReceipt(winner, fingerprint);
        }
    }

    private static UpdateMembershipRoleResult FromReceipt(CommandReceipt receipt, string fingerprint)
    {
        if (!string.Equals(receipt.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new(UpdateMembershipRoleOutcome.IdempotencyConflict);
        }

        var payload = JsonSerializer.Deserialize<ReceiptPayload>(receipt.ResultPayload);
        return payload is null
            ? new(UpdateMembershipRoleOutcome.IdempotencyConflict)
            : new(UpdateMembershipRoleOutcome.Updated, payload.MembershipId, payload.RoleId, payload.Version);
    }

    private sealed record RoleStateSnapshot(Guid RoleId);

    private sealed record ReceiptPayload(Guid MembershipId, Guid RoleId, long Version);
}
