using Idata.Platform.Modules.AccessControl.Application;
using Idata.Platform.Modules.AccessControl.Domain;
using Idata.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Idata.Platform.Modules.AccessControl.Infrastructure;

/// <summary>
/// Adapter TRANSAKSIONAL i komandës `membership.role.update` me
/// <b>EXPLICIT AUTHORITY-LOCK PROTOCOL</b> brenda një transaction-i të vetëm
/// (isolation e paprekur: READ COMMITTED).
///
/// <para><b>Pse duhet lock eksplicit.</b> Nën READ COMMITTED, një
/// `UPDATE ... WHERE EXISTS(...)` merr row lock VETËM mbi target-in. Nëse ai
/// statement bllokohet duke pritur lock-un e target-it, sub-query-t `EXISTS`
/// mbi rreshtat e tjerë (business, app_user, business_membership i aktorit,
/// role, permission, role_permission) mund të mbeten të vlerësuara mbi
/// snapshot-in e statement-it — pra një revokim që commit-on NDËRKOHË mund të
/// mos shihet. Prandaj çdo FAKT AUTORIZIMI që është i ndryshueshëm lock-ohet
/// EKSPLICIT përpara vlerësimit përfundimtar. NUK pretendohet se "një
/// statement i vetëm rivlerëson atomikisht çdo autoritet".</para>
///
/// <para><b>Protokolli</b> (i njëjti transaction):
/// (1) lock i të gjithë fakteve mutable të autorizimit sipas rendit kanonik;
/// (2) rivlerësim PËRFUNDIMTAR fail-closed i TË GJITHA predikateve mbi vlerat
/// e lexuara NËN LOCK; (3) `UPDATE` i target-it që përsërit `status='active'`
/// dhe `version = expectedVersion` si defense-in-depth; (4) audit + command
/// receipt + mutation commit-ohen BASHKË.</para>
///
/// <para><b>LOCK ORDER KANONIK — kontratë për çdo writer të ardhshëm:</b>
/// <c>business → business_membership (ORDER BY id) → app_user (ORDER BY id)
/// → role → permission → role_permission</c>. Rreshtat e dublikuar
/// deduplikohen; memberships merren `FOR UPDATE` (target-i mutohet, aktori
/// mbrohet nga revokimi konkurrent), pjesa tjetër `FOR SHARE`.
/// <c>FOR SHARE</c> — JO <c>FOR KEY SHARE</c> — sepse `status`, `is_system`,
/// `feature_key`/`action` janë non-key updates dhe vetëm FOR SHARE i bllokon.</para>
///
/// <para><b>Kufiri i garancisë:</b> serializimi vlen për faktet e lock-uara më
/// lart. Deadlock/lock-timeout/gabim DB → rollback i plotë dhe përgjigje
/// fail-closed; ZERO retry i heshtur (retry do të kërkonte ADR të veçantë).</para>
/// </summary>
public sealed class EfMembershipRoleUnitOfWork(PlatformDbContext context) : IMembershipRoleUnitOfWork
{
    private IDbContextTransaction? transaction;

    // ── Hapi 1 i rendit kanonik: business (FOR SHARE). ──
    private const string LockBusinessSql = """
        SELECT status FROM platform.business WHERE id = @business_id FOR SHARE
        """;

    // ── Hapi 2: memberships e aktorit DHE të target-it, ORDER BY id, FOR UPDATE.
    //    Një query i vetëm → dedup natyror kur aktori është vetë targeti. ──
    private const string LockMembershipsSql = """
        SELECT id, user_id, role_id, version, status
          FROM platform.business_membership
         WHERE business_id = @business_id
           AND (id = @membership_id OR user_id = @actor_user_id)
         ORDER BY id
           FOR UPDATE
        """;

    // ── Hapi 3: app_user i aktorit dhe i target-it, ORDER BY id, FOR SHARE. ──
    private const string LockUsersSql = """
        SELECT id, status FROM platform.app_user
         WHERE id = ANY(@user_ids) ORDER BY id FOR SHARE
        """;

    // ── Hapi 4: roli i kërkuar (FOR SHARE) — `is_system` është non-key. ──
    private const string LockRoleSql = """
        SELECT is_system FROM platform.role
         WHERE id = @role_id AND business_id = @business_id FOR SHARE
        """;

    // ── Hapi 5: permission-i `users_roles:U` (FOR SHARE) — feature_key/action
    //    janë non-key dhe mund të ndryshojnë. ──
    private const string LockPermissionSql = """
        SELECT id FROM platform.permission
         WHERE feature_key = @feature_key AND action = @action FOR SHARE
        """;

    // ── Hapi 6: grant-i EKZAKT i aktorit (FOR SHARE) — DELETE i tij bllokohet. ──
    private const string LockGrantSql = """
        SELECT 1 FROM platform.role_permission
         WHERE business_id = @business_id
           AND role_id = @actor_role_id
           AND permission_id = @permission_id
           FOR SHARE
        """;

    // ── Hapi 8: mutacioni i guarduar (defense-in-depth: status + version). ──
    private const string GuardedUpdateSql = """
        UPDATE platform.business_membership
           SET role_id = @role_id, version = version + 1
         WHERE id = @membership_id
           AND business_id = @business_id
           AND status = 'active'
           AND version = @expected_version
        RETURNING version
        """;

    private sealed record MembershipRow(Guid Id, Guid UserId, Guid RoleId, long Version, string Status);

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

        try
        {
            // ═══ (1) LOCK-ET, sipas rendit kanonik ═══

            // business
            var businessStatus = await ScalarAsync<string>(connection, LockBusinessSql, cancellationToken,
                ("business_id", mutation.BusinessId));
            if (!string.Equals(businessStatus, RecordStatus.Active, StringComparison.Ordinal))
            {
                return Denied(RoleMutationDenial.ActorNotAuthorized);
            }

            // memberships (aktor + target), ORDER BY id, FOR UPDATE
            var memberships = await ReadMembershipsAsync(connection, mutation, cancellationToken)
                .ConfigureAwait(false);
            // Unique index-i `(business_id, user_id)` garanton së shumti një
            // membership për (business, user); nëse ai invariant do të shkelej
            // ndonjëherë, refuzojmë fail-closed në vend që të rrëzohemi.
            var actorCandidates = memberships.Where(row => row.UserId == mutation.ActorUserId).ToArray();
            if (actorCandidates.Length > 1)
            {
                return Denied(RoleMutationDenial.ActorNotAuthorized);
            }

            var actorMembership = actorCandidates.SingleOrDefault();
            var targetMembership = memberships.SingleOrDefault(row => row.Id == mutation.MembershipId);

            if (actorMembership is null
                || !string.Equals(actorMembership.Status, RecordStatus.Active, StringComparison.Ordinal))
            {
                return Denied(RoleMutationDenial.ActorNotAuthorized);
            }

            // app_user (aktor + target), ORDER BY id, FOR SHARE. Target-i mund të
            // mos ekzistojë ende — atëherë lock-ohet vetëm aktori dhe validimi
            // përfundimtar e refuzon më poshtë.
            var userIds = targetMembership is null
                ? new[] { mutation.ActorUserId }
                : new[] { mutation.ActorUserId, targetMembership.UserId }.Distinct().ToArray();
            var userStatuses = await ReadUserStatusesAsync(connection, userIds, cancellationToken)
                .ConfigureAwait(false);
            if (!userStatuses.TryGetValue(mutation.ActorUserId, out var actorUserStatus)
                || !string.Equals(actorUserStatus, RecordStatus.Active, StringComparison.Ordinal))
            {
                return Denied(RoleMutationDenial.ActorNotAuthorized);
            }

            // role (i kërkuar)
            var roleIsSystem = await ScalarAsync<bool?>(connection, LockRoleSql, cancellationToken,
                ("role_id", mutation.RoleId), ("business_id", mutation.BusinessId));

            // permission
            var permissionId = await ScalarAsync<Guid?>(connection, LockPermissionSql, cancellationToken,
                ("feature_key", PlatformFeatures.UsersRoles), ("action", PermissionActions.Update));
            if (permissionId is not { } permission)
            {
                return Denied(RoleMutationDenial.ActorNotAuthorized);
            }

            // role_permission (grant-i EKZAKT i rolit AKTUAL të aktorit)
            var grant = await ScalarAsync<int?>(connection, LockGrantSql, cancellationToken,
                ("business_id", mutation.BusinessId),
                ("actor_role_id", actorMembership.RoleId),
                ("permission_id", permission));
            if (grant is null)
            {
                return Denied(RoleMutationDenial.ActorNotAuthorized);
            }

            // ═══ (2) RIVLERËSIM PËRFUNDIMTAR mbi vlerat NËN LOCK ═══
            // Aktori u validua më lart (business/user/membership/permission/grant).

            if (targetMembership is null
                || !string.Equals(targetMembership.Status, RecordStatus.Active, StringComparison.Ordinal)
                || !userStatuses.TryGetValue(targetMembership.UserId, out var targetUserStatus)
                || !string.Equals(targetUserStatus, RecordStatus.Active, StringComparison.Ordinal))
            {
                return Denied(RoleMutationDenial.TargetNotAssignable);
            }

            // Self-role: krahasim me USER ID (jo membership id).
            if (targetMembership.UserId == mutation.ActorUserId)
            {
                return Denied(RoleMutationDenial.SelfRoleChange);
            }

            // Rol inekzistent / cross-tenant / `is_system` → i njëjti rezultat (parity).
            if (roleIsSystem is not false)
            {
                return Denied(RoleMutationDenial.TargetNotAssignable);
            }

            if (targetMembership.Version != mutation.ExpectedVersion)
            {
                return Denied(RoleMutationDenial.VersionConflict);
            }

            // ═══ (3) MUTACIONI I GUARDUAR ═══
            var newVersion = await ScalarAsync<long?>(connection, GuardedUpdateSql, cancellationToken,
                ("role_id", mutation.RoleId),
                ("membership_id", mutation.MembershipId),
                ("business_id", mutation.BusinessId),
                ("expected_version", mutation.ExpectedVersion));

            return newVersion is { } version
                ? new AtomicRoleMutationResult(true, version, targetMembership.RoleId, RoleMutationDenial.None)
                : Denied(RoleMutationDenial.VersionConflict);
        }
        catch (PostgresException ex) when (IsLockOrSerializationFailure(ex))
        {
            // Deadlock / lock timeout / serialization → rollback i plotë,
            // përgjigje fail-closed; ZERO retry i heshtur.
            await DiscardAsync(cancellationToken).ConfigureAwait(false);
            return Denied(RoleMutationDenial.VersionConflict);
        }
    }

    private static bool IsLockOrSerializationFailure(PostgresException ex)
        => ex.SqlState is PostgresErrorCodes.DeadlockDetected
            or PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.LockNotAvailable
            or PostgresErrorCodes.QueryCanceled;

    private static AtomicRoleMutationResult Denied(RoleMutationDenial denial)
        => new(false, 0, Guid.Empty, denial);

    private async Task<IReadOnlyList<MembershipRow>> ReadMembershipsAsync(
        NpgsqlConnection connection, AtomicRoleMutation mutation, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, LockMembershipsSql,
            ("business_id", mutation.BusinessId),
            ("membership_id", mutation.MembershipId),
            ("actor_user_id", mutation.ActorUserId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<MembershipRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new MembershipRow(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetInt64(3), reader.GetString(4)));
        }

        return rows;
    }

    private async Task<Dictionary<Guid, string>> ReadUserStatusesAsync(
        NpgsqlConnection connection, Guid[] userIds, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, LockUsersSql, ("user_ids", userIds));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var statuses = new Dictionary<Guid, string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            statuses[reader.GetGuid(0)] = reader.GetString(1);
        }

        return statuses;
    }

    private async Task<T?> ScalarAsync<T>(
        NpgsqlConnection connection, string sql, CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = CreateCommand(connection, sql, parameters);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? default : (T)result;
    }

    private NpgsqlCommand CreateCommand(
        NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction)transaction!.GetDbTransaction();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
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

    /// <summary>
    /// Shkruan audit + receipt dhe COMMIT-on mutation-in bashkë me ta.
    /// Mutation-i i membership-it bëhet me SQL të guarduar (jo me EF change
    /// tracking), ndaj `DbUpdateConcurrencyException` nuk është rrugë e
    /// arritshme këtu; i vetmi konflikt i mundshëm është unique violation i
    /// receipt-it (gara e idempotencës).
    /// </summary>
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
