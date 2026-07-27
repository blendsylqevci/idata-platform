using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Idata.Platform.Modules.AccessControl.Application;
using Idata.Platform.Modules.AccessControl.Presentation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Provat e fazës authority-lock/validation/mutation (§1–§3): fixtures DISKRIMINUESE
/// (ndryshon VETËM predikati nën provë), race-t lifecycle/permission TOCTOU
/// me barrierë deterministike, dhe CAS-i real me dy unit-of-work mbi lidhje
/// të ndara. Çdo refuzim provohet me ZERO efekt (rol/version të pandryshuara,
/// audit=0, receipt=0), jo vetëm me statusin HTTP.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class AtomicAuthorizationTests
{
    // ── Barriera TOCTOU: dekorator test-only i store-it që ekzekuton një
    //    mutacion konkurrues PAS leximit fillestar (pre-check) dhe PARA fazës
    //    së mutacionit. Jetёn e ka VETËM në test assembly. ──
    private sealed class BarrierStore(IAccessControlStore inner, Func<Task> afterRead) : IAccessControlStore
    {
        public async Task<ActorGrants?> GetGrantsAsync(Guid businessId, Guid userId, CancellationToken ct)
        {
            var grants = await inner.GetGrantsAsync(businessId, userId, ct);
            await afterRead(); // ndryshimi konkurrues commit-ohet këtu
            return grants;
        }
    }

    private sealed class BarrierFactory(Func<Task> afterRead) : AccessControlApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IAccessControlStore>(sp =>
                    new BarrierStore(
                        ActivatorUtilities.CreateInstance<
                            Modules.AccessControl.Infrastructure.EfAccessControlStore>(sp),
                        afterRead));
            });
        }
    }

    private static async Task WithSeedAsync(
        Func<string, Task> body, [CallerMemberName] string label = "")
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

    private static HttpRequestMessage RoleUpdate(
        Guid actorUser, Guid membershipId, Guid roleId, string key, long expectedVersion = 0,
        Guid? actorBusiness = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put, new Uri($"/api/platform/memberships/{membershipId:D}/role", UriKind.Relative))
        {
            Content = JsonContent.Create(new { roleId, expectedVersion }),
        };
        request.Headers.Add("X-Test-UserId", actorUser.ToString("D"));
        request.Headers.Add("X-Test-BusinessId", (actorBusiness ?? AccessControlSeed.BusinessA).ToString("D"));
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    /// <summary>Gjendja provuese: rol+version i targetit, numri i audit/receipt.</summary>
    private static async Task<(Guid Role, long Version, long Audits, long Receipts)> StateAsync(
        string databaseName, Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(TenantIsolationEndpointTests.ConnectionStringFor(databaseName));
        await connection.OpenAsync();
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT (SELECT role_id FROM platform.business_membership WHERE id = '{membershipId:D}'),
                   (SELECT version FROM platform.business_membership WHERE id = '{membershipId:D}'),
                   (SELECT COUNT(*) FROM platform.audit_log),
                   (SELECT COUNT(*) FROM platform.command_receipt)
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    private static async Task ExecuteAsync(string databaseName, string sql)
    {
        await using var connection = new NpgsqlConnection(TenantIsolationEndpointTests.ConnectionStringFor(databaseName));
        await connection.OpenAsync();
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    // ══════════════════ §3.A — aktor me VETËM user-status disabled ══════════════════

    [Fact]
    public async Task Aktor_me_user_disabled_por_membership_dhe_rol_valid_refuzohet_403()
        => await WithSeedAsync(async databaseName =>
        {
            // Kontroll POZITIV: i njëjti rol/permission/target funksionon me aktor aktiv.
            await using (var okFactory = new AccessControlApiFactory())
            using (var okClient = okFactory.CreateClient())
            using (var ok = await okClient.SendAsync(
                RoleUpdate(AccessControlSeed.UserA, AccessControlSeed.MembershipC, AccessControlSeed.RoleA2, "pos-1"),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            }

            var afterPositive = await StateAsync(databaseName, AccessControlSeed.MembershipC);

            // DISKRIMINUESE: aktori ka membership AKTIV + rol me users_roles:U në
            // business AKTIV; ndryshon VETËM app_user.status = disabled.
            await using var factory = new AccessControlApiFactory();
            using var client = factory.CreateClient();
            using var response = await client.SendAsync(
                RoleUpdate(AccessControlSeed.UserActorDisabled, AccessControlSeed.MembershipC,
                    AccessControlSeed.RoleA, "disabled-user", expectedVersion: afterPositive.Version),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var (_, _, _, code) = await TenantIsolationEndpointTests.StableProblemAsync(response);
            Assert.Equal("ACCESS_DENIED", code);
            Assert.Equal(afterPositive, await StateAsync(databaseName, AccessControlSeed.MembershipC));
        });

    // ══════════════════ §3.B — self-role (krahasim me USER ID) ══════════════════

    [Fact]
    public async Task Aktori_nuk_ndryshon_dot_rolin_e_VET_edhe_me_permission_U()
        => await WithSeedAsync(async databaseName =>
        {
            var before = await StateAsync(databaseName, AccessControlSeed.MembershipA);
            Assert.Equal(AccessControlSeed.RoleA, before.Role);

            // Aktor aktiv, membership aktiv, business aktiv, users_roles:U, rol
            // target i zakonshëm valid — i vetmi ndryshim: targeti është VETJA.
            await using var factory = new AccessControlApiFactory();
            using var client = factory.CreateClient();
            using var response = await client.SendAsync(
                RoleUpdate(AccessControlSeed.UserA, AccessControlSeed.MembershipA, AccessControlSeed.RoleA2, "self-1"),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var (_, _, _, code) = await TenantIsolationEndpointTests.StableProblemAsync(response);
            Assert.Equal("ACCESS_DENIED", code);
            Assert.Equal(before, await StateAsync(databaseName, AccessControlSeed.MembershipA));
        });

    // ══════════════════ §3.C — rol me IsSystem=true ══════════════════

    [Fact]
    public async Task Rol_IsSystem_nuk_caktohet_dhe_nuk_zbulohet_ne_pergjigje()
        => await WithSeedAsync(async databaseName =>
        {
            var before = await StateAsync(databaseName, AccessControlSeed.MembershipC);

            await using var factory = new AccessControlApiFactory();
            using var client = factory.CreateClient();
            // Fixture IDENTIKE me një assignment valid; ndryshon VETËM IsSystem=true.
            using var system = await client.SendAsync(
                RoleUpdate(AccessControlSeed.UserA, AccessControlSeed.MembershipC, AccessControlSeed.RoleSystemA, "sys-1"),
                TestContext.Current.CancellationToken);
            // Rol krejt inekzistent — PARITY: e njëjta përgjigje publike.
            using var missing = await client.SendAsync(
                RoleUpdate(AccessControlSeed.UserA, AccessControlSeed.MembershipC, AccessControlSeed.NonexistentId, "sys-2"),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, system.StatusCode);
            Assert.Equal(
                await TenantIsolationEndpointTests.StableProblemAsync(missing),
                await TenantIsolationEndpointTests.StableProblemAsync(system));
            var body = await system.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("system", body, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, await StateAsync(databaseName, AccessControlSeed.MembershipC));
        });

    // ══════════════════ §3.D — TOCTOU lifecycle/permission ══════════════════

    public static TheoryData<string> RaceMutations() =>
    [
        "actor-user-disable",
        "actor-membership-disable",
        "business-disable",
        "actor-permission-revoke",
        "target-membership-disable",
        "target-user-disable",
    ];

    [Theory]
    [MemberData(nameof(RaceMutations))]
    public async Task Ndryshimi_konkurrues_pas_leximit_fillestar_refuzon_mutacionin(string mutation)
        => await WithSeedAsync(async databaseName =>
        {
            var before = await StateAsync(databaseName, AccessControlSeed.MembershipC);

            var sql = mutation switch
            {
                "actor-user-disable" =>
                    $"UPDATE platform.app_user SET status='disabled' WHERE id='{AccessControlSeed.UserA:D}'",
                "actor-membership-disable" =>
                    $"UPDATE platform.business_membership SET status='disabled' WHERE id='{AccessControlSeed.MembershipA:D}'",
                "business-disable" =>
                    $"UPDATE platform.business SET status='disabled' WHERE id='{AccessControlSeed.BusinessA:D}'",
                "actor-permission-revoke" =>
                    $"DELETE FROM platform.role_permission WHERE business_id='{AccessControlSeed.BusinessA:D}' AND role_id='{AccessControlSeed.RoleA:D}' AND permission_id='{AccessControlSeed.PermissionUpdate:D}'",
                "target-membership-disable" =>
                    $"UPDATE platform.business_membership SET status='disabled' WHERE id='{AccessControlSeed.MembershipC:D}'",
                _ =>
                    $"UPDATE platform.app_user SET status='disabled' WHERE id='{AccessControlSeed.UserC:D}'",
            };

            var fired = 0;
            await using var factory = new BarrierFactory(async () =>
            {
                // Vetëm një herë, PAS pre-check-ut dhe PARA fazës së mutacionit.
                if (Interlocked.Increment(ref fired) == 1)
                {
                    await ExecuteAsync(databaseName, sql);
                }
            });
            using var client = factory.CreateClient();
            using var response = await client.SendAsync(
                RoleUpdate(AccessControlSeed.UserA, AccessControlSeed.MembershipC, AccessControlSeed.RoleA2, "race-1"),
                TestContext.Current.CancellationToken);

            Assert.Equal(1, fired);
            // Kontrata publike: aktor i prekur → 403; target i prekur → 404.
            var expected = mutation.StartsWith("target", StringComparison.Ordinal)
                ? HttpStatusCode.NotFound
                : HttpStatusCode.Forbidden;
            Assert.Equal(expected, response.StatusCode);

            // ZERO efekt: rol/version të pandryshuara, audit=0, receipt=0.
            var after = await StateAsync(databaseName, AccessControlSeed.MembershipC);
            Assert.Equal(before.Role, after.Role);
            Assert.Equal(before.Version, after.Version);
            Assert.Equal(0L, after.Audits);
            Assert.Equal(0L, after.Receipts);
        });

    // ══════════════════ §3.E — CAS real me dy unit-of-work ══════════════════

    [Fact]
    public async Task Dy_unit_of_work_me_te_njejtin_version_vetem_njeri_fiton()
        => await WithSeedAsync(async databaseName =>
        {
            var connectionString = TenantIsolationEndpointTests.ConnectionStringFor(databaseName);
            await using var contextA = Persistence.PlatformDbContextFactory.Create(connectionString);
            await using var contextB = Persistence.PlatformDbContextFactory.Create(connectionString);
            var uowA = new Modules.AccessControl.Infrastructure.EfMembershipRoleUnitOfWork(contextA);
            var uowB = new Modules.AccessControl.Infrastructure.EfMembershipRoleUnitOfWork(contextB);

            var mutationA = new AtomicRoleMutation(
                AccessControlSeed.BusinessA, AccessControlSeed.UserA,
                AccessControlSeed.MembershipC, AccessControlSeed.RoleA2, ExpectedVersion: 0);
            var mutationB = mutationA with { RoleId = AccessControlSeed.RoleA };

            // A hyn i pari në protokollin e lock-eve → mban lock-et e autoritetit
            // dhe FOR UPDATE mbi target.
            var resultA = await uowA.TryMutateRoleAsync(mutationA, TestContext.Current.CancellationToken);
            Assert.True(resultA.Applied);
            Assert.Equal(1L, resultA.NewVersion);
            Assert.Equal(AccessControlSeed.RoleA, resultA.PreviousRoleId); // paraardhësi REAL

            // B niset ndërsa A s'ka commit-uar: bllokohet mbi lock-et e A-së.
            // Bllokimi PROVOHET nga vetë PostgreSQL (`pg_blocking_pids`), JO me
            // Task.Delay — B rivlerëson vetëm pasi A commit-on, kur `version=0`
            // s'vlen më.
            await contextB.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            var pidB = await PostgresLockObserver.BackendPidAsync(
                (NpgsqlConnection)contextB.Database.GetDbConnection());
            var taskB = Task.Run(() => uowB.TryMutateRoleAsync(mutationB, CancellationToken.None));
            await using (var observer = new NpgsqlConnection(connectionString))
            {
                await observer.OpenAsync(TestContext.Current.CancellationToken);
                await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(observer, databaseName);
                await PostgresLockObserver.WaitUntilBlockedAsync(observer, pidB);
            }

            AddCommandRecords(uowA, AccessControlSeed.MembershipC, AccessControlSeed.RoleA2, resultA.NewVersion);
            Assert.Equal(CommandSaveOutcome.Saved, await uowA.SaveAsync(TestContext.Current.CancellationToken));

            var resultB = await taskB;
            Assert.False(resultB.Applied);
            Assert.Equal(RoleMutationDenial.VersionConflict, resultB.Denial);
            await uowB.DiscardAsync(TestContext.Current.CancellationToken);

            // Saktësisht një mutation, një audit, një receipt; fituesi = A.
            var state = await StateAsync(databaseName, AccessControlSeed.MembershipC);
            Assert.Equal(AccessControlSeed.RoleA2, state.Role);
            Assert.Equal(1L, state.Version);
            Assert.Equal(1L, state.Audits);
            Assert.Equal(1L, state.Receipts);
        });

    private static void AddCommandRecords(
        Modules.AccessControl.Infrastructure.EfMembershipRoleUnitOfWork uow,
        Guid membershipId, Guid roleId, long version)
    {
        var receiptId = Guid.CreateVersion7();
        uow.AddAudit(new Modules.AccessControl.Domain.AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            BusinessId = AccessControlSeed.BusinessA,
            UserId = AccessControlSeed.UserA,
            Entity = Modules.AccessControl.Domain.AuditEntities.BusinessMembership,
            EntityId = membershipId,
            Action = Modules.AccessControl.Domain.PermissionActions.Update,
            BeforeState = "{}",
            AfterState = "{}",
            CommandReceiptId = receiptId,
            CorrelationId = Guid.CreateVersion7(),
            EventAt = DateTimeOffset.UtcNow,
        });
        uow.AddReceipt(new Modules.AccessControl.Domain.CommandReceipt
        {
            Id = receiptId,
            BusinessId = AccessControlSeed.BusinessA,
            OperationKey = Modules.AccessControl.Domain.CommandOperations.MembershipRoleUpdate,
            IdempotencyKey = $"cas-{version}",
            RequestFingerprint = "fp",
            ResultStatus = Modules.AccessControl.Domain.CommandReceiptStatus.Completed,
            ResultPayload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        });
        _ = roleId;
    }
}
