using Idata.Platform.Modules.AccessControl.Application;
using Idata.Platform.Modules.AccessControl.Domain;
using Idata.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Idata.Platform.Modules.AccessControl.Infrastructure;

/// <summary>
/// Adapter TRANSAKSIONAL i komandës me NJË LINEARIZATION POINT DB-backed.
///
/// <para><b>Autoriteti i vetëm</b> është <see cref="TryMutateRoleAsync"/>: një
/// `UPDATE ... WHERE &lt;të gjithë predikatët&gt;` i vetëm, brenda një transaction-i
/// eksplicit, që merr row lock mbi target membership-in dhe rivlerëson NË ATË
/// MOMENT: business active · actor user active · actor membership active ·
/// permission-i aktual i aktorit `platform.users_roles:U` · target membership
/// active · target user active · `actorUserId &lt;&gt; target.user_id` (self-role
/// me USER ID) · roli i kërkuar i të njëjtit business me `is_system = false` ·
/// `version = expectedVersion` · `status = 'active'`. Nuk ka guard të dytë që
/// mund të divergjojë: pre-check-u i handler-it shërben vetëm për formësim
/// përgjigjeje.</para>
///
/// <para>Nëse UPDATE-i prek 0 rreshta, arsyeja klasifikohet me një lexim të
/// vetëm brenda TË NJËJTIT transaction (vetëm për formësim 403/404/409), pastaj
/// transaction-i RRËZOHET — zero mutation, zero version++, zero audit, zero
/// receipt. Nëse prek 1 rresht, audit + receipt shkruhen dhe COMMIT-i i vetëm
/// i bën të tria atomike.</para>
///
/// <para>Semantika lineare: disable/revokim i commit-uar PARA këtij pike e
/// refuzon mutacionin; nëse mutacioni linearizohet i pari, disable-i i mëpasëm
/// vazhdon normalisht. Dy komanda me të njëjtin version: saktësisht një fiton
/// (row lock + kushti `version = expectedVersion`).</para>
/// </summary>
public sealed class EfMembershipRoleUnitOfWork(PlatformDbContext context) : IMembershipRoleUnitOfWork
{
    private IDbContextTransaction? transaction;

    /// <summary>
    /// LINEARIZATION POINT: një statement i vetëm; `FROM prev` lexon rreshtin
    /// në snapshot-in e statement-it, ndaj `prev.role_id` është vlera E VJETËR
    /// (paraardhësi real i fituesit) — e nevojshme për audit BeforeState.
    /// </summary>
    private const string AtomicMutationSql = """
        UPDATE platform.business_membership AS t
           SET role_id = @new_role_id,
               version = t.version + 1
          FROM (SELECT id, role_id FROM platform.business_membership WHERE id = @membership_id) AS prev
         WHERE t.id = prev.id
           AND t.id = @membership_id
           AND t.business_id = @business_id
           AND t.status = 'active'
           AND t.version = @expected_version
           AND t.user_id <> @actor_user_id
           AND EXISTS (SELECT 1 FROM platform.business b
                        WHERE b.id = @business_id AND b.status = 'active')
           AND EXISTS (SELECT 1 FROM platform.app_user tu
                        WHERE tu.id = t.user_id AND tu.status = 'active')
           AND EXISTS (SELECT 1 FROM platform.app_user au
                        WHERE au.id = @actor_user_id AND au.status = 'active')
           AND EXISTS (SELECT 1
                         FROM platform.business_membership am
                         JOIN platform.role_permission rp
                           ON rp.business_id = am.business_id AND rp.role_id = am.role_id
                         JOIN platform.permission p
                           ON p.id = rp.permission_id
                        WHERE am.business_id = @business_id
                          AND am.user_id = @actor_user_id
                          AND am.status = 'active'
                          AND p.feature_key = @feature_key
                          AND p.action = @action)
           AND EXISTS (SELECT 1 FROM platform.role r
                        WHERE r.id = @new_role_id
                          AND r.business_id = @business_id
                          AND r.is_system = false)
        RETURNING t.version, prev.role_id
        """;

    /// <summary>Klasifikim VETËM për formësim përgjigjeje (i njëjti transaction).</summary>
    private const string DenialClassificationSql = """
        SELECT
          EXISTS (SELECT 1 FROM platform.business b
                   WHERE b.id = @business_id AND b.status = 'active')                    AS business_ok,
          EXISTS (SELECT 1 FROM platform.app_user au
                   WHERE au.id = @actor_user_id AND au.status = 'active')                AS actor_user_ok,
          EXISTS (SELECT 1 FROM platform.business_membership am
                   WHERE am.business_id = @business_id AND am.user_id = @actor_user_id
                     AND am.status = 'active')                                           AS actor_membership_ok,
          EXISTS (SELECT 1
                    FROM platform.business_membership am
                    JOIN platform.role_permission rp
                      ON rp.business_id = am.business_id AND rp.role_id = am.role_id
                    JOIN platform.permission p ON p.id = rp.permission_id
                   WHERE am.business_id = @business_id AND am.user_id = @actor_user_id
                     AND am.status = 'active'
                     AND p.feature_key = @feature_key AND p.action = @action)             AS actor_permission_ok,
          EXISTS (SELECT 1
                    FROM platform.business_membership t
                    JOIN platform.app_user tu ON tu.id = t.user_id
                   WHERE t.id = @membership_id AND t.business_id = @business_id
                     AND t.status = 'active' AND tu.status = 'active')                    AS target_ok,
          EXISTS (SELECT 1 FROM platform.business_membership t
                   WHERE t.id = @membership_id AND t.business_id = @business_id
                     AND t.user_id = @actor_user_id)                                      AS target_is_self,
          EXISTS (SELECT 1 FROM platform.role r
                   WHERE r.id = @new_role_id AND r.business_id = @business_id
                     AND r.is_system = false)                                             AS role_ok,
          EXISTS (SELECT 1 FROM platform.business_membership t
                   WHERE t.id = @membership_id AND t.business_id = @business_id
                     AND t.status = 'active' AND t.version = @expected_version)           AS version_ok
        """;

    public async Task<CommandReceipt?> FindReceiptAsync(
        Guid businessId, string operationKey, string idempotencyKey, CancellationToken cancellationToken)
    {
        var record = await context.CommandReceipts.AsNoTracking()
            .SingleOrDefaultAsync(
                receipt => receipt.BusinessId == businessId
                    && receipt.OperationKey == operationKey
                    && receipt.IdempotencyKey == idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? null : ToDomain(record);
    }

    public async Task<AtomicRoleMutationResult> TryMutateRoleAsync(
        AtomicRoleMutation mutation, CancellationToken cancellationToken)
    {
        transaction ??= await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
            command.CommandText = AtomicMutationSql;
            AddMutationParameters(command, mutation);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new AtomicRoleMutationResult(
                    Applied: true,
                    NewVersion: reader.GetInt64(0),
                    PreviousRoleId: reader.GetGuid(1),
                    Denial: RoleMutationDenial.None);
            }
        }

        var denial = await ClassifyDenialAsync(connection, mutation, cancellationToken).ConfigureAwait(false);
        return new AtomicRoleMutationResult(false, 0, Guid.Empty, denial);
    }

    private async Task<RoleMutationDenial> ClassifyDenialAsync(
        NpgsqlConnection connection, AtomicRoleMutation mutation, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction)transaction!.GetDbTransaction();
        command.CommandText = DenialClassificationSql;
        AddMutationParameters(command, mutation);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return RoleMutationDenial.ActorNotAuthorized; // fail-closed
        }

        var businessOk = reader.GetBoolean(0);
        var actorUserOk = reader.GetBoolean(1);
        var actorMembershipOk = reader.GetBoolean(2);
        var actorPermissionOk = reader.GetBoolean(3);
        var targetOk = reader.GetBoolean(4);
        var targetIsSelf = reader.GetBoolean(5);
        var roleOk = reader.GetBoolean(6);
        var versionOk = reader.GetBoolean(7);

        // Rendi ruan kontratën publike: aktori vlerësohet PARA se të zbulohet
        // ndonjë gjë për targetin; `IsSystem` bashkohet me "not found" (parity).
        if (!businessOk || !actorUserOk || !actorMembershipOk || !actorPermissionOk)
        {
            return RoleMutationDenial.ActorNotAuthorized;
        }

        if (!targetOk)
        {
            return RoleMutationDenial.TargetNotAssignable;
        }

        if (targetIsSelf)
        {
            return RoleMutationDenial.SelfRoleChange;
        }

        if (!roleOk)
        {
            return RoleMutationDenial.TargetNotAssignable;
        }

        // versionOk=false → CAS klasik. versionOk=true por UPDATE-i preku 0
        // rreshta ⇒ gjendja lëvizi mes statement-it dhe klasifikimit: i njëjti
        // 409, sepse klienti duhet të rilexojë version-in dhe të riprovojë.
        _ = versionOk;
        return RoleMutationDenial.VersionConflict;
    }

    private static void AddMutationParameters(NpgsqlCommand command, AtomicRoleMutation mutation)
    {
        command.Parameters.AddWithValue("business_id", mutation.BusinessId);
        command.Parameters.AddWithValue("actor_user_id", mutation.ActorUserId);
        command.Parameters.AddWithValue("membership_id", mutation.MembershipId);
        command.Parameters.AddWithValue("new_role_id", mutation.RoleId);
        command.Parameters.AddWithValue("expected_version", mutation.ExpectedVersion);
        command.Parameters.AddWithValue("feature_key", PlatformFeatures.UsersRoles);
        command.Parameters.AddWithValue("action", PermissionActions.Update);
    }

    public void AddAudit(AuditLogEntry entry) => context.AuditLog.Add(new AuditLogRecord
    {
        Id = entry.Id,
        BusinessId = entry.BusinessId,
        UserId = entry.UserId,
        Entity = entry.Entity,
        EntityId = entry.EntityId,
        Action = entry.Action,
        BeforeState = entry.BeforeState,
        AfterState = entry.AfterState,
        CommandReceiptId = entry.CommandReceiptId,
        CorrelationId = entry.CorrelationId,
        EventAt = entry.EventAt,
    });

    public void AddReceipt(CommandReceipt receipt) => context.CommandReceipts.Add(new CommandReceiptRecord
    {
        Id = receipt.Id,
        BusinessId = receipt.BusinessId,
        OperationKey = receipt.OperationKey,
        IdempotencyKey = receipt.IdempotencyKey,
        RequestFingerprint = receipt.RequestFingerprint,
        ResultStatus = receipt.ResultStatus,
        ResultPayload = receipt.ResultPayload,
        CreatedAt = receipt.CreatedAt,
        CompletedAt = receipt.CompletedAt,
    });

    public async Task<CommandSaveOutcome> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return CommandSaveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            await DiscardAsync(cancellationToken).ConfigureAwait(false);
            return CommandSaveOutcome.ConcurrencyConflict;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation
            && string.Equals(violation.ConstraintName, "ux_command_receipt_idempotency", StringComparison.Ordinal))
        {
            // Gara e idempotencës: fituesi shkroi receipt-in — ky transaction
            // rrëzohet i tëri (zero mutation/audit/receipt).
            await DiscardAsync(cancellationToken).ConfigureAwait(false);
            return CommandSaveOutcome.ReceiptConflict;
        }
    }

    public async Task DiscardAsync(CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            await transaction.DisposeAsync().ConfigureAwait(false);
            transaction = null;
        }

        context.ChangeTracker.Clear();
    }

    private static CommandReceipt ToDomain(CommandReceiptRecord record) => new()
    {
        Id = record.Id,
        BusinessId = record.BusinessId,
        OperationKey = record.OperationKey,
        IdempotencyKey = record.IdempotencyKey,
        RequestFingerprint = record.RequestFingerprint,
        ResultStatus = record.ResultStatus,
        ResultPayload = record.ResultPayload,
        CreatedAt = record.CreatedAt,
        CompletedAt = record.CompletedAt,
    };
}
