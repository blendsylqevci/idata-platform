using System.Text.Json;
using Idata.Platform.Modules.AccessControl.Domain;

namespace Idata.Platform.Modules.AccessControl.Application;

public enum UpdateMembershipRoleOutcome
{
    /// <summary>Idempotency-Key mungon ose s'kalon validimin → 400.</summary>
    InvalidIdempotencyKey,
    /// <summary>Deny uniform PARA çdo resource lookup-i → 403.</summary>
    AccessDenied,
    /// <summary>Membership/rol inekzistent OSE i tenant-it tjetër OSE i çaktivizuar → 404 identik.</summary>
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
/// COMMAND `PUT /api/platform/memberships/{membershipId}/role` — rendi
/// fail-closed: (1) validim i Idempotency-Key PURE; (2) deny-by-default +
/// permission `platform.users_roles:U` PARA resource lookup-it (403 uniform);
/// (3) replay/konflikt sipas receipt-it; (4) lookup DIREKT me
/// `(business_id, id)` — cross-tenant/disabled ⇒ i padallueshëm nga
/// inekzistent (404 identik); (5) CAS: `expectedVersion` = version aktual;
/// (6) mutation (rol + version++) + audit i redaktuar (me command_receipt_id
/// + correlation server-generated) + receipt në NJË transaction. Humbësi i
/// garës së idempotencës merr rezultatin e fituesit; humbësi i garës CAS
/// merr 409 CONCURRENCY_CONFLICT pa efekt.
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

        var grants = await store.GetGrantsAsync(businessId, actorUserId, cancellationToken).ConfigureAwait(false);
        if (grants is null || !grants.PermissionKeys.Contains(RequiredPermission))
        {
            return new(UpdateMembershipRoleOutcome.AccessDenied); // uniform, PARA lookup-it
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

        var membership = await unitOfWork
            .FindActiveMembershipAsync(businessId, command.MembershipId, cancellationToken)
            .ConfigureAwait(false);
        if (membership is null)
        {
            return new(UpdateMembershipRoleOutcome.TargetNotFound);
        }

        var role = await unitOfWork
            .FindRoleAsync(businessId, command.RoleId, cancellationToken)
            .ConfigureAwait(false);
        if (role is null)
        {
            return new(UpdateMembershipRoleOutcome.TargetNotFound);
        }

        // CAS eksplicit: expectedVersion duhet të përputhet me version-in aktual.
        if (membership.Version != command.ExpectedVersion)
        {
            return new(UpdateMembershipRoleOutcome.ConcurrencyConflict);
        }

        var now = clock.GetUtcNow();
        var previousRoleId = membership.RoleId;
        var newVersion = membership.Version + 1;
        var receiptId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();

        unitOfWork.ApplyRoleChange(role.Id, newVersion);

        // Audit i REDAKTUAR — vetëm identifikues teknikë + lidhja me komandën;
        // kurrë PII/payload. command_receipt_id krijohet PARA SaveChanges.
        unitOfWork.AddAudit(new AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            UserId = actorUserId,
            Entity = AuditEntities.BusinessMembership,
            EntityId = membership.Id,
            Action = PermissionActions.Update,
            BeforeState = JsonSerializer.Serialize(new RoleStateSnapshot(previousRoleId)),
            AfterState = JsonSerializer.Serialize(new RoleStateSnapshot(role.Id)),
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
            ResultPayload = JsonSerializer.Serialize(new ReceiptPayload(membership.Id, role.Id, newVersion)),
            CreatedAt = now,
            CompletedAt = now,
        });

        var outcome = await unitOfWork.SaveAsync(cancellationToken).ConfigureAwait(false);
        switch (outcome)
        {
            case CommandSaveOutcome.Saved:
                return new(UpdateMembershipRoleOutcome.Updated, membership.Id, role.Id, newVersion);

            case CommandSaveOutcome.ConcurrencyConflict:
                // Gara CAS: një kërkesë tjetër ndryshoi version-in mes read-it dhe
                // save-it — tx i rrëzuar, ZERO efekt.
                return new(UpdateMembershipRoleOutcome.ConcurrencyConflict);

            default:
                // Gara e idempotencës: fituesi e ka shkruar receipt-in — humbësi
                // (tx i rrëzuar, ZERO efekt të dytë) lexon rezultatin e fituesit.
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
