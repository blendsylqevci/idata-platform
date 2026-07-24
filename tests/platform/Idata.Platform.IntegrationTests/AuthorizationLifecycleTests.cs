using System.Net;
using System.Runtime.CompilerServices;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Matrica FAIL-CLOSED e authorization/lifecycle (§4), GET dhe PUT: lifecycle
/// (user/business/membership) merret NGA DB, jo nga claims; claims të pa-
/// autentikuara (IsAuthenticated=false) refuzohen; target i çaktivizuar/
/// user-i çaktivizuar → i padallueshëm nga inekzistent, zero mutation. Çdo
/// refuzim aktori → 403 uniform ACCESS_DENIED PARA target lookup-it.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class AuthorizationLifecycleTests
{
    private static async Task WithHostAsync(Func<HttpClient, string, Task> body, [CallerMemberName] string label = "")
    {
        await using var db = await PlatformTestDatabase.CreateAsync(label);
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db.DatabaseName);
        try
        {
            await using var factory = await AccessControlSeed.MigrateSeedAndCreateFactoryAsync(db.DatabaseName);
            using var client = factory.CreateClient();
            await body(client, db.DatabaseName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    private static async Task<(long Audits, long Receipts)> WriteCountersAsync(string databaseName)
    {
        await using var connection = new Npgsql.NpgsqlConnection(TenantIsolationEndpointTests.ConnectionStringFor(databaseName));
        await connection.OpenAsync();
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT (SELECT COUNT(*) FROM platform.audit_log), (SELECT COUNT(*) FROM platform.command_receipt)";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    // ── Aktori: 403 uniform për çdo dështim lifecycle/autentikimi (GET). ──

    public static TheoryData<Guid?, Guid?, bool> DeniedActors() => new()
    {
        { null, null, false },                                                  // pa claims
        { AccessControlSeed.UserA, null, false },                               // pa tenant
        { null, AccessControlSeed.BusinessA, false },                           // pa user
        { AccessControlSeed.UserA, AccessControlSeed.BusinessB, false },        // pa membership në B
        { AccessControlSeed.UserActorDisabled, AccessControlSeed.BusinessA, false }, // user disabled
        { AccessControlSeed.UserInDisabledBiz, AccessControlSeed.BusinessDisabled, false }, // business disabled
        { AccessControlSeed.UserB, AccessControlSeed.BusinessA, false },        // actor membership disabled (MembershipActorDisabled)
        { AccessControlSeed.UserA, AccessControlSeed.BusinessA, true },         // valid claims por IsAuthenticated=false
    };

    [Theory]
    [MemberData(nameof(DeniedActors))]
    public async Task GET_context_deny_403_per_cdo_deshtim_lifecycle_ose_autentikimi(
        Guid? userId, Guid? businessId, bool unauthenticated)
        => await WithHostAsync(async (client, _) =>
        {
            var request = TenantIsolationEndpointTests.ContextRequest(userId, businessId);
            if (unauthenticated)
            {
                request.Headers.Add("X-Test-Unauthenticated", "true");
            }

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var (_, _, status, code) = await TenantIsolationEndpointTests.StableProblemAsync(response);
            Assert.Equal(403, status);
            Assert.Equal("ACCESS_DENIED", code);
        });

    [Theory]
    [MemberData(nameof(DeniedActors))]
    public async Task PUT_role_deny_403_para_target_lookup_it_dhe_zero_efekt(
        Guid? userId, Guid? businessId, bool unauthenticated)
        => await WithHostAsync(async (client, databaseName) =>
        {
            var before = await WriteCountersAsync(databaseName);
            var request = TenantIsolationEndpointTests.RoleUpdateRequest(
                userId ?? Guid.Empty, businessId ?? Guid.Empty,
                AccessControlSeed.MembershipC, AccessControlSeed.RoleA2, "key-denied");
            // Rregullo header-at kur userId/businessId janë null (mos i shto Guid.Empty).
            request.Headers.Remove("X-Test-UserId");
            request.Headers.Remove("X-Test-BusinessId");
            TenantIsolationEndpointTests.AddActor(request, userId, businessId);
            if (unauthenticated)
            {
                request.Headers.Add("X-Test-Unauthenticated", "true");
            }

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var (_, _, _, code) = await TenantIsolationEndpointTests.StableProblemAsync(response);
            Assert.Equal("ACCESS_DENIED", code);
            Assert.Equal(before, await WriteCountersAsync(databaseName)); // zero mutation/audit/receipt
        });

    // ── Target lifecycle (PUT nga aktor valid me permission U). ──

    [Theory]
    [InlineData(nameof(AccessControlSeed.MembershipTargetDisabled))]     // membership disabled
    [InlineData(nameof(AccessControlSeed.MembershipTargetUserDisabled))] // target user disabled
    public async Task PUT_target_i_caktivizuar_kthen_404_pa_mutation(string targetName)
        => await WithHostAsync(async (client, databaseName) =>
        {
            var targetId = targetName == nameof(AccessControlSeed.MembershipTargetDisabled)
                ? AccessControlSeed.MembershipTargetDisabled
                : AccessControlSeed.MembershipTargetUserDisabled;
            var before = await WriteCountersAsync(databaseName);
            using var response = await client.SendAsync(
                TenantIsolationEndpointTests.RoleUpdateRequest(
                    AccessControlSeed.UserA, AccessControlSeed.BusinessA, targetId, AccessControlSeed.RoleA2, "key-target"),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var (_, _, _, code) = await TenantIsolationEndpointTests.StableProblemAsync(response);
            Assert.Equal("RESOURCE_NOT_FOUND", code);
            Assert.Equal(before, await WriteCountersAsync(databaseName));
        });

    // ── Inverse (kontrollet aktive) → sukses sipas permission. ──

    [Fact]
    public async Task Kontrollet_aktive_lejojne_lexim_dhe_shkrim_sipas_permission_it()
        => await WithHostAsync(async (client, _) =>
        {
            using (var read = await client.SendAsync(
                TenantIsolationEndpointTests.ContextRequest(AccessControlSeed.UserA, AccessControlSeed.BusinessA),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            }

            using var write = await client.SendAsync(
                TenantIsolationEndpointTests.RoleUpdateRequest(
                    AccessControlSeed.UserA, AccessControlSeed.BusinessA,
                    AccessControlSeed.MembershipC, AccessControlSeed.RoleA2, "key-inverse"),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        });

    // ── Aktor pa permission U → 403 uniform edhe për target-in e vet (para lookup-it). ──

    [Fact]
    public async Task Pa_permission_U_deny_403_uniform()
        => await WithHostAsync(async (client, databaseName) =>
        {
            var before = await WriteCountersAsync(databaseName);
            using var response = await client.SendAsync(
                TenantIsolationEndpointTests.RoleUpdateRequest(
                    AccessControlSeed.UserB, AccessControlSeed.BusinessB,
                    AccessControlSeed.MembershipB, AccessControlSeed.RoleB, "key-noperm"),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(before, await WriteCountersAsync(databaseName));
        });
}
