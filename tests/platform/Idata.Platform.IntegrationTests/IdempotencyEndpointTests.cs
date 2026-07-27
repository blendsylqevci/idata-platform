using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Idata.Platform.Modules.AccessControl.Presentation;
using Npgsql;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Idempotenca + CAS END-TO-END (§5 + §10.F): komanda e parë = NJË mutation +
/// NJË audit + NJË receipt (version 0→1); replay = i njëjti rezultat pa efekt
/// shtesë; i njëjti key me target/payload/version tjetër → 409
/// IDEMPOTENCY_CONFLICT; expectedVersion i vjetruar → 409 CONCURRENCY_CONFLICT;
/// kërkesat paralele me key të NDRYSHËM + të njëjtin expectedVersion → saktësisht
/// një 200 + një CONCURRENCY_CONFLICT, humbësi zero efekt.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class IdempotencyEndpointTests
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

    private static HttpRequestMessage Request(Guid roleId, string? idempotencyKey, long expectedVersion = 0, Guid? membership = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri($"/api/platform/memberships/{membership ?? AccessControlSeed.MembershipC:D}/role", UriKind.Relative))
        {
            Content = JsonContent.Create(new { roleId, expectedVersion }),
        };
        request.Headers.Add("X-Test-UserId", AccessControlSeed.UserA.ToString("D"));
        request.Headers.Add("X-Test-BusinessId", AccessControlSeed.BusinessA.ToString("D"));
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private static async Task<(long Audits, long Receipts, Guid Role, long Version)> StateAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(TenantIsolationEndpointTests.ConnectionStringFor(databaseName));
        await connection.OpenAsync();
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT (SELECT COUNT(*) FROM platform.audit_log),
                   (SELECT COUNT(*) FROM platform.command_receipt),
                   (SELECT role_id FROM platform.business_membership WHERE id = '{AccessControlSeed.MembershipC:D}'),
                   (SELECT version FROM platform.business_membership WHERE id = '{AccessControlSeed.MembershipC:D}')
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetGuid(2), reader.GetInt64(3));
    }

    [Fact]
    public async Task Komanda_replay_konflikt_dhe_key_i_pavlefshem()
        => await WithHostAsync(async (client, databaseName) =>
        {
            using (var missingKey = await client.SendAsync(
                Request(AccessControlSeed.RoleA2, null), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
            }

            using (var badKey = await client.SendAsync(
                Request(AccessControlSeed.RoleA2, "kk;Database=x"), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);
            }

            Assert.Equal((0L, 0L, AccessControlSeed.RoleA, 0L), await StateAsync(databaseName));

            // Komanda e parë → 200; version 0→1; 1 mutation + 1 audit + 1 receipt.
            using (var first = await client.SendAsync(
                Request(AccessControlSeed.RoleA2, "key-1"), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
                var body = await first.Content.ReadFromJsonAsync<MembershipRoleResponse>(
                    TestContext.Current.CancellationToken);
                Assert.NotNull(body);
                Assert.Equal(AccessControlSeed.MembershipC, body.MembershipId);
                Assert.Equal(AccessControlSeed.RoleA2, body.RoleId);
                Assert.Equal(1L, body.Version);
            }

            Assert.Equal((1L, 1L, AccessControlSeed.RoleA2, 1L), await StateAsync(databaseName));

            // Replay i njëjtë (key + payload + version=0) → 200 identik, zero efekt.
            using (var replay = await client.SendAsync(
                Request(AccessControlSeed.RoleA2, "key-1"), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                var body = await replay.Content.ReadFromJsonAsync<MembershipRoleResponse>(
                    TestContext.Current.CancellationToken);
                Assert.Equal(1L, body!.Version);
            }

            Assert.Equal((1L, 1L, AccessControlSeed.RoleA2, 1L), await StateAsync(databaseName));

            // I njëjti key + payload tjetër (rol tjetër) → 409 IDEMPOTENCY_CONFLICT.
            using (var conflictPayload = await client.SendAsync(
                Request(AccessControlSeed.RoleA, "key-1"), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, conflictPayload.StatusCode);
                var (_, _, _, code) = await TenantIsolationEndpointTests.StableProblemAsync(conflictPayload);
                Assert.Equal("IDEMPOTENCY_CONFLICT", code);
            }

            // I njëjti key + membership tjetër → 409, zero mutation.
            using (var conflictTarget = await client.SendAsync(
                Request(AccessControlSeed.RoleA2, "key-1", membership: AccessControlSeed.NonexistentId),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, conflictTarget.StatusCode);
            }

            Assert.Equal((1L, 1L, AccessControlSeed.RoleA2, 1L), await StateAsync(databaseName));
        });

    [Fact]
    public async Task ExpectedVersion_i_vjetruar_kthen_CONCURRENCY_CONFLICT()
        => await WithHostAsync(async (client, databaseName) =>
        {
            using (var first = await client.SendAsync(
                Request(AccessControlSeed.RoleA2, "key-a", expectedVersion: 0), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            }

            // Version tani = 1; kërkesë me expectedVersion=0 → CONCURRENCY_CONFLICT.
            using (var stale = await client.SendAsync(
                Request(AccessControlSeed.RoleA, "key-b", expectedVersion: 0), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
                var (_, _, _, code) = await TenantIsolationEndpointTests.StableProblemAsync(stale);
                Assert.Equal("CONCURRENCY_CONFLICT", code);
            }

            Assert.Equal((1L, 1L, AccessControlSeed.RoleA2, 1L), await StateAsync(databaseName));

            // Me expectedVersion korrekt (1) → sukses, version 1→2.
            using (var ok = await client.SendAsync(
                Request(AccessControlSeed.RoleA, "key-c", expectedVersion: 1), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
                var body = await ok.Content.ReadFromJsonAsync<MembershipRoleResponse>(TestContext.Current.CancellationToken);
                Assert.Equal(2L, body!.Version);
            }
        });

    [Fact]
    public async Task Host_recreation_replay_jep_te_njejtin_rezultat_pa_efekt_te_dyte()
    {
        // §10 skenari 6 (host recreation/restart fixture, JO crash-proof universal):
        // ekzekuto komandën, hidhe host-in, krijo host TË RI mbi TË NJËJTËN DB
        // sintetike, replay të njëjtin Idempotency-Key → i njëjti rezultat,
        // zero mutation/audit/receipt i dytë.
        await using var db = await PlatformTestDatabase.CreateAsync("hostrecreate");
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db.DatabaseName);
        try
        {
            (await AccessControlSeed.MigrateSeedAndCreateFactoryAsync(db.DatabaseName)).Dispose();

            long firstVersion;
            await using (var host1 = new AccessControlApiFactory())
            using (var client1 = host1.CreateClient())
            using (var first = await client1.SendAsync(
                Request(AccessControlSeed.RoleA2, "key-restart"), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
                var body = await first.Content.ReadFromJsonAsync<MembershipRoleResponse>(TestContext.Current.CancellationToken);
                firstVersion = body!.Version;
            }

            var afterFirst = await StateAsync(db.DatabaseName);
            Assert.Equal((1L, 1L, AccessControlSeed.RoleA2, 1L), afterFirst);

            // Host krejt i ri mbi të njëjtën DB → replay i të njëjtit key.
            await using (var host2 = new AccessControlApiFactory())
            using (var client2 = host2.CreateClient())
            using (var replay = await client2.SendAsync(
                Request(AccessControlSeed.RoleA2, "key-restart"), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                var body = await replay.Content.ReadFromJsonAsync<MembershipRoleResponse>(TestContext.Current.CancellationToken);
                Assert.Equal(firstVersion, body!.Version);
            }

            Assert.Equal(afterFirst, await StateAsync(db.DatabaseName)); // zero efekt i dytë
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    [Fact]
    public async Task Kerkesat_paralele_CAS_japin_nje_fitues_dhe_nje_konflikt()
        => await WithHostAsync(async (client, databaseName) =>
        {
            // Key të NDRYSHËM, i njëjti membership, i njëjti expectedVersion=0,
            // role të NDRYSHME → saktësisht një 200 + një 409 CONCURRENCY_CONFLICT.
            var tasks = new[]
            {
                client.SendAsync(Request(AccessControlSeed.RoleA2, "key-w1", expectedVersion: 0), TestContext.Current.CancellationToken),
                client.SendAsync(Request(AccessControlSeed.RoleA, "key-w2", expectedVersion: 0), TestContext.Current.CancellationToken),
            };
            var responses = await Task.WhenAll(tasks);
            try
            {
                var oks = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
                var conflicts = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
                Assert.Equal(1, oks);
                Assert.Equal(1, conflicts);

                var winner = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
                var body = await winner.Content.ReadFromJsonAsync<MembershipRoleResponse>(TestContext.Current.CancellationToken);
                Assert.Equal(1L, body!.Version);

                var loser = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
                var (_, _, _, code) = await TenantIsolationEndpointTests.StableProblemAsync(loser);
                Assert.Equal("CONCURRENCY_CONFLICT", code);

                // SAKTËSISHT një mutation + një audit + një receipt; roli = fituesi.
                var state = await StateAsync(databaseName);
                Assert.Equal(1L, state.Audits);
                Assert.Equal(1L, state.Receipts);
                Assert.Equal(1L, state.Version);
                Assert.Equal(body.RoleId, state.Role);
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
        });
}
