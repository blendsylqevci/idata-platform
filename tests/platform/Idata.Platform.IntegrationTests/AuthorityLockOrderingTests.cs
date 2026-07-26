using System.Runtime.CompilerServices;
using Idata.Platform.Modules.AccessControl.Application;
using Idata.Platform.Modules.AccessControl.Domain;
using Idata.Platform.Modules.AccessControl.Infrastructure;
using Idata.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Provat DETERMINISTIKE të <b>authority-lock protocol</b>-it kundër
/// PostgreSQL real: për ÇDO fakt autorizimi të ndryshueshëm provohen DY rende
/// të vërtetuara nga vetë serveri (`pg_blocking_pids`), pa asnjë
/// <c>Task.Delay</c> si "provë" bllokimi.
///
/// <para><b>A. REVOCATION-FIRST</b> — revokuesi merr lock-un e vet i pari;
/// mutacioni niset dhe DB-ja konfirmon se po pret; revokimi commit-on;
/// mutacioni vazhdon dhe REFUZOHET; rol/version të pandryshuara, audit=0,
/// receipt=0.</para>
///
/// <para><b>B. MUTATION-FIRST</b> — mutacioni merr authority locks; revokuesi
/// niset dhe DB-ja konfirmon se po pret; mutacioni commit-on saktësisht një
/// mutation + audit + receipt; pastaj revokimi vazhdon.</para>
///
/// Ky është testi që do të ishte i pamundur me `UPDATE ... WHERE EXISTS(...)`
/// të vetëm nën READ COMMITTED: aty faktet e autorizimit lexohen nga
/// snapshot-i i statement-it dhe një revokim që commit-on ndërkohë NUK shihet.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class AuthorityLockOrderingTests
{
    /// <summary>Faktet MUTABLE të autorizimit që protokolli duhet të lock-ojë.</summary>
    public static TheoryData<string> MutableAuthorities() =>
    [
        "business.status",
        "actor-user.status",
        "target-user.status",
        "actor-membership.status",
        "actor-membership.role_id",     // rol pa grant
        "target-membership.status",
        "target-membership.user_id",    // ripikëzohet te user i çaktivizuar
        "role_permission.delete",       // grant-i EKZAKT hiqet
        "permission.action",            // feature_key/action ndryshon
        "role.is_system",               // roli i kërkuar bëhet system-managed
    ];

    private static string RevokeSql(string authority) => authority switch
    {
        "business.status" =>
            $"UPDATE platform.business SET status='disabled' WHERE id='{AccessControlSeed.BusinessA:D}'",
        "actor-user.status" =>
            $"UPDATE platform.app_user SET status='disabled' WHERE id='{AccessControlSeed.UserA:D}'",
        "target-user.status" =>
            $"UPDATE platform.app_user SET status='disabled' WHERE id='{AccessControlSeed.UserC:D}'",
        "actor-membership.status" =>
            $"UPDATE platform.business_membership SET status='disabled' WHERE id='{AccessControlSeed.MembershipA:D}'",
        "actor-membership.role_id" =>
            $"UPDATE platform.business_membership SET role_id='{AccessControlSeed.RoleA2:D}' WHERE id='{AccessControlSeed.MembershipA:D}'",
        "target-membership.status" =>
            $"UPDATE platform.business_membership SET status='disabled' WHERE id='{AccessControlSeed.MembershipC:D}'",
        // Ripikëzimi te vetë aktori është i PAMUNDUR në DB: unique index-i
        // `(business_id, user_id)` e ndalon, sepse aktori ka tashmë një
        // membership në atë business. Prandaj provohet ripikëzimi te një user
        // i ÇAKTIVIZUAR — pra fakti `user_id` i target-it është vërtet mutable.
        "target-membership.user_id" =>
            $"UPDATE platform.business_membership SET user_id='{AccessControlSeed.UserDisabledUnattached:D}' WHERE id='{AccessControlSeed.MembershipC:D}'",
        "role_permission.delete" =>
            $"DELETE FROM platform.role_permission WHERE business_id='{AccessControlSeed.BusinessA:D}' AND role_id='{AccessControlSeed.RoleA:D}' AND permission_id='{AccessControlSeed.PermissionUpdate:D}'",
        "permission.action" =>
            $"UPDATE platform.permission SET action='X' WHERE id='{AccessControlSeed.PermissionUpdate:D}'",
        _ =>
            $"UPDATE platform.role SET is_system=true WHERE id='{AccessControlSeed.RoleA2:D}'",
    };

    /// <summary>Deny-ja e pritur: aktor-side → 403-klasë; target-side → 404-klasë.</summary>
    private static RoleMutationDenial ExpectedDenial(string authority) => authority switch
    {
        "business.status" or "actor-user.status" or "actor-membership.status"
            or "actor-membership.role_id" or "role_permission.delete" or "permission.action"
            => RoleMutationDenial.ActorNotAuthorized,
        _ => RoleMutationDenial.TargetNotAssignable,
    };

    private static AtomicRoleMutation Mutation() => new(
        AccessControlSeed.BusinessA, AccessControlSeed.UserA,
        AccessControlSeed.MembershipC, AccessControlSeed.RoleA2, ExpectedVersion: 0);

    private static async Task WithSeedAsync(Func<string, Task> body, [CallerMemberName] string label = "")
    {
        await using var db = await PlatformTestDatabase.CreateAsync(label);
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db.DatabaseName);
        try
        {
            (await AccessControlSeed.MigrateSeedAndCreateFactoryAsync(db.DatabaseName)).Dispose();
            await body(db.DatabaseName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    private static async Task<NpgsqlConnection> OpenAsync(string databaseName)
    {
        var connection = new NpgsqlConnection(TenantIsolationEndpointTests.ConnectionStringFor(databaseName));
        await connection.OpenAsync();
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);
        return connection;
    }

    private static async Task<(Guid Role, long Version, long Audits, long Receipts)> StateAsync(string databaseName)
    {
        await using var connection = await OpenAsync(databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT (SELECT role_id FROM platform.business_membership WHERE id='{AccessControlSeed.MembershipC:D}'),
                   (SELECT version FROM platform.business_membership WHERE id='{AccessControlSeed.MembershipC:D}'),
                   (SELECT COUNT(*) FROM platform.audit_log),
                   (SELECT COUNT(*) FROM platform.command_receipt)
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    private static void AddCommandRecords(EfMembershipRoleUnitOfWork uow, long version, Guid previousRole)
    {
        var receiptId = Guid.CreateVersion7();
        uow.AddAudit(new AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            BusinessId = AccessControlSeed.BusinessA,
            UserId = AccessControlSeed.UserA,
            Entity = AuditEntities.BusinessMembership,
            EntityId = AccessControlSeed.MembershipC,
            Action = PermissionActions.Update,
            BeforeState = $"{{\"RoleId\":\"{previousRole:D}\"}}",
            AfterState = $"{{\"RoleId\":\"{AccessControlSeed.RoleA2:D}\"}}",
            CommandReceiptId = receiptId,
            CorrelationId = Guid.CreateVersion7(),
            EventAt = DateTimeOffset.UtcNow,
        });
        uow.AddReceipt(new CommandReceipt
        {
            Id = receiptId,
            BusinessId = AccessControlSeed.BusinessA,
            OperationKey = CommandOperations.MembershipRoleUpdate,
            IdempotencyKey = $"lock-order-{version}",
            RequestFingerprint = "fp",
            ResultStatus = CommandReceiptStatus.Completed,
            ResultPayload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        });
    }

    // ═════════════════ A. REVOCATION-FIRST ═════════════════

    [Theory]
    [MemberData(nameof(MutableAuthorities))]
    public async Task Revocation_first_mutacioni_pret_pastaj_REFUZOHET(string authority)
        => await WithSeedAsync(async databaseName =>
        {
            var before = await StateAsync(databaseName);

            // 1. Revokuesi merr lock-un e vet i pari (pa commit).
            await using var revoker = await OpenAsync(databaseName);
            await using var revokeTx = await revoker.BeginTransactionAsync();
            await using (var revoke = revoker.CreateCommand())
            {
                revoke.Transaction = revokeTx;
                revoke.CommandText = RevokeSql(authority);
                await revoke.ExecuteNonQueryAsync();
            }

            // 2. Mutacioni niset dhe DB-ja konfirmon se po PRET lock-un.
            await using var context = PlatformDbContextFactory.Create(
                TenantIsolationEndpointTests.ConnectionStringFor(databaseName));
            await context.Database.OpenConnectionAsync();
            var mutationPid = await PostgresLockObserver.BackendPidAsync(
                (NpgsqlConnection)context.Database.GetDbConnection());
            var uow = new EfMembershipRoleUnitOfWork(context);
            var mutationTask = Task.Run(() => uow.TryMutateRoleAsync(Mutation(), CancellationToken.None));

            await using (var observer = await OpenAsync(databaseName))
            {
                await PostgresLockObserver.WaitUntilBlockedAsync(observer, mutationPid);
            }

            // 3. Revokimi commit-on → mutacioni zhbllokohet dhe rivlerëson.
            await revokeTx.CommitAsync();
            var result = await mutationTask;

            // 4. REFUZIM me arsyen e saktë + ZERO efekt.
            Assert.False(result.Applied);
            Assert.Equal(ExpectedDenial(authority), result.Denial);
            await uow.DiscardAsync(TestContext.Current.CancellationToken);

            var after = await StateAsync(databaseName);
            Assert.Equal(before.Role, after.Role);
            Assert.Equal(before.Version, after.Version);
            Assert.Equal(0L, after.Audits);
            Assert.Equal(0L, after.Receipts);
        });

    // ═════════════════ B. MUTATION-FIRST ═════════════════

    [Theory]
    [MemberData(nameof(MutableAuthorities))]
    public async Task Mutation_first_revokuesi_pret_dhe_mutacioni_kalon(string authority)
        => await WithSeedAsync(async databaseName =>
        {
            // 1. Mutacioni merr authority locks (pa commit).
            await using var context = PlatformDbContextFactory.Create(
                TenantIsolationEndpointTests.ConnectionStringFor(databaseName));
            await context.Database.OpenConnectionAsync();
            var uow = new EfMembershipRoleUnitOfWork(context);
            var result = await uow.TryMutateRoleAsync(Mutation(), TestContext.Current.CancellationToken);
            Assert.True(result.Applied);
            Assert.Equal(1L, result.NewVersion);
            Assert.Equal(AccessControlSeed.RoleA, result.PreviousRoleId);

            // 2. Revokuesi niset dhe DB-ja konfirmon se po PRET lock-un tonë.
            await using var revoker = await OpenAsync(databaseName);
            var revokerPid = await PostgresLockObserver.BackendPidAsync(revoker);
            var revokeTask = Task.Run(async () =>
            {
                await using var revokeTx = await revoker.BeginTransactionAsync();
                await using var revoke = revoker.CreateCommand();
                revoke.Transaction = revokeTx;
                revoke.CommandText = RevokeSql(authority);
                await revoke.ExecuteNonQueryAsync();
                await revokeTx.CommitAsync();
            });

            await using (var observer = await OpenAsync(databaseName))
            {
                await PostgresLockObserver.WaitUntilBlockedAsync(observer, revokerPid);

                // 3. Mutacioni commit-on: SAKTËSISHT një mutation + audit + receipt.
                AddCommandRecords(uow, result.NewVersion, result.PreviousRoleId);
                Assert.Equal(CommandSaveOutcome.Saved, await uow.SaveAsync(TestContext.Current.CancellationToken));

                // 4. Vetëm tani revokuesi vazhdon.
                await PostgresLockObserver.WaitUntilUnblockedAsync(observer, revokerPid);
            }

            await revokeTask;

            var after = await StateAsync(databaseName);
            Assert.Equal(AccessControlSeed.RoleA2, after.Role);
            Assert.Equal(1L, after.Version);
            Assert.Equal(1L, after.Audits);
            Assert.Equal(1L, after.Receipts);
        });

    // ═════════════════ LOCK ORDER — pa deadlock ═════════════════

    /// <summary>
    /// Dy mutacione konkurrente mbi target-e TË NDRYSHME brenda të njëjtit
    /// business marrin lock-et në TË NJËJTIN rend kanonik (business →
    /// memberships → users → role → permission → role_permission). Prandaj
    /// nuk mund të ndërthuren në rend të kundërt: njëri përfundon, tjetri
    /// pret dhe pastaj vazhdon — ZERO deadlock (asnjë 40P01).
    /// </summary>
    [Fact]
    public async Task Lock_order_kanonik_nuk_prodhon_deadlock_mes_dy_mutacioneve()
        => await WithSeedAsync(async databaseName =>
        {
            var connectionString = TenantIsolationEndpointTests.ConnectionStringFor(databaseName);
            await using var contextA = PlatformDbContextFactory.Create(connectionString);
            await using var contextB = PlatformDbContextFactory.Create(connectionString);
            var uowA = new EfMembershipRoleUnitOfWork(contextA);
            var uowB = new EfMembershipRoleUnitOfWork(contextB);

            // A mbi MembershipC; B mbi MembershipTargetUserDisabled — target-e
            // të ndryshme, i njëjti business ⇒ i njëjti rend lock-esh.
            var mutationA = Mutation();
            var mutationB = new AtomicRoleMutation(
                AccessControlSeed.BusinessA, AccessControlSeed.UserA,
                AccessControlSeed.MembershipTargetUserDisabled, AccessControlSeed.RoleA2, ExpectedVersion: 0);

            var resultA = await uowA.TryMutateRoleAsync(mutationA, TestContext.Current.CancellationToken);
            Assert.True(resultA.Applied);

            // B niset ndërsa A mban lock-et e përbashkëta (business/users/role/
            // permission/grant) → bllokohet, e provuar nga serveri.
            await contextB.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            var pidB = await PostgresLockObserver.BackendPidAsync(
                (NpgsqlConnection)contextB.Database.GetDbConnection());
            var taskB = Task.Run(() => uowB.TryMutateRoleAsync(mutationB, CancellationToken.None));

            await using (var observer = await OpenAsync(databaseName))
            {
                await PostgresLockObserver.WaitUntilBlockedAsync(observer, pidB);
                AddCommandRecords(uowA, resultA.NewVersion, resultA.PreviousRoleId);
                Assert.Equal(CommandSaveOutcome.Saved, await uowA.SaveAsync(TestContext.Current.CancellationToken));
            }

            // B vazhdon pa deadlock; targeti i tij ka user të çaktivizuar →
            // refuzim i pritur, por ASNJË 40P01.
            var resultB = await taskB;
            Assert.False(resultB.Applied);
            Assert.Equal(RoleMutationDenial.TargetNotAssignable, resultB.Denial);
            await uowB.DiscardAsync(TestContext.Current.CancellationToken);

            var after = await StateAsync(databaseName);
            Assert.Equal(AccessControlSeed.RoleA2, after.Role);
            Assert.Equal(1L, after.Version);
            Assert.Equal(1L, after.Audits);
            Assert.Equal(1L, after.Receipts);
        });
}
