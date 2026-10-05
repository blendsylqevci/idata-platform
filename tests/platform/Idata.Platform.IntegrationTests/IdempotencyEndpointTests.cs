using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Idata.Platform.Modules.AccessControl.Presentation;
using Microsoft.AspNetCore.Hosting;
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

    /// <summary>
    /// Kërkesë me body JSON BRUTO: fusha që mungon mungon realisht në wire
    /// (jo vlerë default e serializuar nga klienti i testit).
    /// </summary>
    private static HttpRequestMessage RawRequest(string json, string idempotencyKey)
        => ContentRequest(new StringContent(json, Encoding.UTF8, "application/json"), idempotencyKey);

    /// <summary>
    /// Kërkesë me përmbajtje të çfarëdoshme në wire — ose PA body fare (`null`: as
    /// Content-Type, as Content-Length, as Transfer-Encoding).
    /// </summary>
    private static HttpRequestMessage ContentRequest(HttpContent? content, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri($"/api/platform/memberships/{AccessControlSeed.MembershipC:D}/role", UriKind.Relative))
        {
            Content = content,
        };
        request.Headers.Add("X-Test-UserId", AccessControlSeed.UserA.ToString("D"));
        request.Headers.Add("X-Test-BusinessId", AccessControlSeed.BusinessA.ToString("D"));
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    /// <summary>400 i kontrolluar për fusha të detyrueshme që mungojnë/janë null.</summary>
    private static async Task AssertInvalidRequestBodyAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var (type, title, status, code) = await TenantIsolationEndpointTests.StableProblemAsync(response);
        Assert.Equal("https://docs.idata.plus/problems/invalid-request-body", type);
        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.Equal(400, status);
        Assert.Equal("INVALID_REQUEST_BODY", code);
        await AssertProblemWireShapeAsync(response);
    }

    /// <summary>
    /// Forma në wire e ProblemDetails të kontrolluar (e njëjtë me helper-in e main-it):
    /// saktësisht këto fusha, në këtë rend; detail/instance null NUK emetohen.
    /// </summary>
    private static async Task AssertProblemWireShapeAsync(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            ["type", "title", "status", "errorCode"],
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(System.Text.Json.JsonValueKind.String, document.RootElement.GetProperty("errorCode").ValueKind);
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

    [Fact]
    public async Task Body_pa_expectedVersion_kthen_400_dhe_zero_mutation_edhe_kur_versioni_aktual_eshte_0()
        => await WithHostAsync(async (client, databaseName) =>
        {
            // MembershipC është në version=0: mungesa NUK guxon të lidhet si 0.
            Assert.Equal((0L, 0L, AccessControlSeed.RoleA, 0L), await StateAsync(databaseName));

            using (var response = await client.SendAsync(
                RawRequest($$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}"}""", "key-missing-version"),
                TestContext.Current.CancellationToken))
            {
                await AssertInvalidRequestBodyAsync(response);
            }

            Assert.Equal((0L, 0L, AccessControlSeed.RoleA, 0L), await StateAsync(databaseName));
        });

    [Fact]
    public async Task Body_pa_roleId_kthen_400_jo_404_dhe_zero_mutation()
        => await WithHostAsync(async (client, databaseName) =>
        {
            // Mungesa e roleId NUK bëhet Guid.Empty → RESOURCE_NOT_FOUND/404.
            using (var response = await client.SendAsync(
                RawRequest("""{"expectedVersion":0}""", "key-missing-role"), TestContext.Current.CancellationToken))
            {
                await AssertInvalidRequestBodyAsync(response);
            }

            Assert.Equal((0L, 0L, AccessControlSeed.RoleA, 0L), await StateAsync(databaseName));
        });

    [Fact]
    public async Task Fusha_te_detyrueshme_null_ose_objekt_bosh_kthejne_400_te_kontrolluar_dhe_zero_mutation()
        => await WithHostAsync(async (client, databaseName) =>
        {
            string[] bodies =
            [
                "{}",
                $$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}","expectedVersion":null}""",
                """{"roleId":null,"expectedVersion":0}""",
            ];
            var index = 0;
            foreach (var body in bodies)
            {
                using var response = await client.SendAsync(
                    RawRequest(body, $"key-null-{index++}"), TestContext.Current.CancellationToken);
                await AssertInvalidRequestBodyAsync(response);
            }

            Assert.Equal((0L, 0L, AccessControlSeed.RoleA, 0L), await StateAsync(databaseName));
        });

    [Fact]
    public async Task Input_i_keqformuar_ose_tip_i_gabuar_kthen_400_te_kontrolluar_dhe_zero_mutation()
        => await WithHostAsync(async (client, databaseName) =>
        {
            // JSON i keqformuar / tip i gabuar: i njëjti 400 i kontrolluar që deklaron
            // kontrata publike (INVALID_REQUEST_BODY), jo 400 i framework-ut pa errorCode.
            string[] bodies =
            [
                """{"roleId":""",
                """{"roleId":"jo-uuid","expectedVersion":0}""",
                $$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}","expectedVersion":"abc"}""",
                $$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}","expectedVersion":1.5}""",
            ];
            var index = 0;
            foreach (var body in bodies)
            {
                using var response = await client.SendAsync(
                    RawRequest(body, $"key-bad-{index++}"), TestContext.Current.CancellationToken);
                await AssertInvalidRequestBodyAsync(response);
            }

            Assert.Equal((0L, 0L, AccessControlSeed.RoleA, 0L), await StateAsync(databaseName));
        });

    /// <summary>I njëjti host i testit, environment Production (pa developer exception page).</summary>
    private sealed class ProductionAccessControlApiFactory : AccessControlApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
        }
    }

    /// <summary>DB sintetike e re (migrim + seed) dhe host-i i environment-it të dhënë.</summary>
    private static async Task WithEnvironmentHostAsync(
        string environment, Func<HttpClient, string, Task> body, [CallerMemberName] string label = "")
    {
        await using var db = await PlatformTestDatabase.CreateAsync(label);
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db.DatabaseName);
        try
        {
            await using var development = await AccessControlSeed.MigrateSeedAndCreateFactoryAsync(db.DatabaseName);
            await using var production = environment == "Production" ? new ProductionAccessControlApiFactory() : null;
            using var client = (production ?? development).CreateClient();
            await body(client, db.DatabaseName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    /// <summary>
    /// Matrica e çdo rruge 400 të UpdateMembershipRole: body (mungesë, null, JSON i
    /// keqformuar, tip i gabuar, body bosh, literal null, array) dhe Idempotency-Key.
    /// </summary>
    private static (string Label, string Body, string? Key, string ExpectedCode)[] BadRequestMatrix()
    {
        var validBody = $$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}","expectedVersion":0}""";
        return
        [
            ("roleId mungon", """{"expectedVersion":0}""", "key-m1", "INVALID_REQUEST_BODY"),
            ("expectedVersion mungon", $$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}"}""", "key-m2", "INVALID_REQUEST_BODY"),
            ("objekt bosh", "{}", "key-m3", "INVALID_REQUEST_BODY"),
            ("roleId null", """{"roleId":null,"expectedVersion":0}""", "key-m4", "INVALID_REQUEST_BODY"),
            ("expectedVersion null", $$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}","expectedVersion":null}""", "key-m5", "INVALID_REQUEST_BODY"),
            ("JSON i cunguar", """{"roleId":""", "key-m6", "INVALID_REQUEST_BODY"),
            ("jo JSON", "jo-json", "key-m7", "INVALID_REQUEST_BODY"),
            ("roleId jo-uuid", """{"roleId":"jo-uuid","expectedVersion":0}""", "key-m8", "INVALID_REQUEST_BODY"),
            ("roleId numer", """{"roleId":123,"expectedVersion":0}""", "key-m9", "INVALID_REQUEST_BODY"),
            ("expectedVersion varg jo-numerik", $$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}","expectedVersion":"abc"}""", "key-m10", "INVALID_REQUEST_BODY"),
            ("expectedVersion decimal", $$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}","expectedVersion":1.5}""", "key-m11", "INVALID_REQUEST_BODY"),
            ("expectedVersion boolean", $$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}","expectedVersion":true}""", "key-m12", "INVALID_REQUEST_BODY"),
            ("expectedVersion jashte int64", $$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}","expectedVersion":1e20}""", "key-m13", "INVALID_REQUEST_BODY"),
            ("body bosh", "", "key-m14", "INVALID_REQUEST_BODY"),
            ("literal null", "null", "key-m15", "INVALID_REQUEST_BODY"),
            ("array JSON", "[]", "key-m16", "INVALID_REQUEST_BODY"),
            ("Idempotency-Key mungon", validBody, null, "INVALID_IDEMPOTENCY_KEY"),
            ("Idempotency-Key i pavlefshem", validBody, "kk;Database=x", "INVALID_IDEMPOTENCY_KEY"),
        ];
    }

    [Fact]
    public async Task Cdo_400_i_UpdateMembershipRole_perputhet_me_skemen_publike_ne_Development_dhe_Production()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("cdo400");
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db.DatabaseName);
        try
        {
            await using var development = await AccessControlSeed.MigrateSeedAndCreateFactoryAsync(db.DatabaseName);
            using var developmentClient = development.CreateClient();
            await using var production = new ProductionAccessControlApiFactory();
            using var productionClient = production.CreateClient();

            // Skema publike e 400-ës nga dokumenti OpenAPI runtime (Development).
            using var openApi = JsonDocument.Parse(await developmentClient.GetStringAsync(
                new Uri("/openapi/platform-v1.json", UriKind.Relative), TestContext.Current.CancellationToken));
            var schemaRef = openApi.RootElement.GetProperty("paths").GetProperty("/api/platform/memberships/{membershipId}/role")
                .GetProperty("put").GetProperty("responses").GetProperty("400").GetProperty("content")
                .GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString()!;
            var schema = openApi.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schemaRef.Split('/')[^1]);
            var required = schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).ToArray();
            Assert.Contains("errorCode", required);
            Assert.Equal("string", schema.GetProperty("properties").GetProperty("errorCode").GetProperty("type").GetString());

            var violations = new List<string>();
            foreach (var (environment, client) in new[] { ("Development", developmentClient), ("Production", productionClient) })
            {
                foreach (var (label, body, key, expectedCode) in BadRequestMatrix())
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Put,
                        new Uri($"/api/platform/memberships/{AccessControlSeed.MembershipC:D}/role", UriKind.Relative))
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    };
                    request.Headers.Add("X-Test-UserId", AccessControlSeed.UserA.ToString("D"));
                    request.Headers.Add("X-Test-BusinessId", AccessControlSeed.BusinessA.ToString("D"));
                    if (key is not null)
                    {
                        request.Headers.Add("Idempotency-Key", key);
                    }

                    using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
                    var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                    var mediaType = response.Content.Headers.ContentType?.MediaType ?? "(asnje)";
                    string? errorCode = null;
                    var missing = required.ToList();
                    if (mediaType == "application/problem+json")
                    {
                        using var problem = JsonDocument.Parse(raw);
                        if (problem.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            missing = required.Where(name => !problem.RootElement.TryGetProperty(name, out _)).ToList();
                            errorCode = problem.RootElement.TryGetProperty("errorCode", out var code) && code.ValueKind == JsonValueKind.String
                                ? code.GetString()
                                : null;
                        }
                    }

                    if (response.StatusCode != HttpStatusCode.BadRequest || missing.Count > 0 || errorCode != expectedCode)
                    {
                        violations.Add(
                            $"{environment} · {label}: status={(int)response.StatusCode} content-type={mediaType} " +
                            $"bytes={raw.Length} errorCode={errorCode ?? "MUNGON"} (pritej {expectedCode})");
                    }
                }
            }

            Assert.True(violations.Count == 0, "400 jashtë kontratës publike:\n" + string.Join("\n", violations));

            // Kontroll: media type jo-JSON NUK është rrugë 400 — mbetet 415 si në main.
            foreach (var client in new[] { developmentClient, productionClient })
            {
                using var textRequest = new HttpRequestMessage(
                    HttpMethod.Put,
                    new Uri($"/api/platform/memberships/{AccessControlSeed.MembershipC:D}/role", UriKind.Relative))
                {
                    Content = new StringContent("roleId", Encoding.UTF8, "text/plain"),
                };
                textRequest.Headers.Add("X-Test-UserId", AccessControlSeed.UserA.ToString("D"));
                textRequest.Headers.Add("X-Test-BusinessId", AccessControlSeed.BusinessA.ToString("D"));
                textRequest.Headers.Add("Idempotency-Key", "key-text");
                using var textResponse = await client.SendAsync(textRequest, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.UnsupportedMediaType, textResponse.StatusCode);
            }

            Assert.Equal((0L, 0L, AccessControlSeed.RoleA, 0L), await StateAsync(db.DatabaseName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    /// <summary>
    /// Rrugë dhe Idempotency-Key të vlefshme, PA body dhe PA Content-Type → 400
    /// INVALID_REQUEST_BODY (jo 415). Edhe pa Idempotency-Key i njëjti 400: handler-i
    /// e refuzon key-n i pari, pra 400 i body-t provon që HandleAsync nuk arrihet.
    /// </summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Kerkesa_pa_body_dhe_pa_Content_Type_kthen_400_INVALID_REQUEST_BODY(string environment)
        => await WithEnvironmentHostAsync(environment, async (client, databaseName) =>
        {
            using (var response = await client.SendAsync(
                ContentRequest(null, "key-pa-body"), TestContext.Current.CancellationToken))
            {
                await AssertInvalidRequestBodyAsync(response);
            }

            using (var response = await client.SendAsync(
                ContentRequest(null, null), TestContext.Current.CancellationToken))
            {
                await AssertInvalidRequestBodyAsync(response);
            }

            Assert.Equal((0L, 0L, AccessControlSeed.RoleA, 0L), await StateAsync(databaseName));
        });

    /// <summary>
    /// Prania e body-t vendoset nga framing-u HTTP, jo nga Content-Type apo vetëm nga
    /// Content-Length: body real pa Content-Type → 415; body chunked (pa Content-Length)
    /// bosh → 400 i kontrolluar; body JSON chunked i vlefshëm → përpunohet normalisht.
    /// </summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Prania_e_body_vendoset_nga_framing_HTTP_jo_nga_Content_Type_apo_Content_Length(string environment)
        => await WithEnvironmentHostAsync(environment, async (client, databaseName) =>
        {
            var json = $$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}","expectedVersion":0}""";

            using (var response = await client.SendAsync(
                ContentRequest(new ByteArrayContent(Encoding.UTF8.GetBytes(json)), "key-pa-content-type"),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
            }

            var emptyChunked = RawRequest("", "key-chunked-bosh");
            emptyChunked.Headers.TransferEncodingChunked = true;
            using (var response = await client.SendAsync(emptyChunked, TestContext.Current.CancellationToken))
            {
                await AssertInvalidRequestBodyAsync(response);
            }

            Assert.Equal((0L, 0L, AccessControlSeed.RoleA, 0L), await StateAsync(databaseName));

            var chunked = RawRequest(json, "key-chunked");
            chunked.Headers.TransferEncodingChunked = true;
            using (var response = await client.SendAsync(chunked, TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadFromJsonAsync<MembershipRoleResponse>(
                    TestContext.Current.CancellationToken);
                Assert.Equal(1L, body!.Version);
            }

            Assert.Equal((1L, 1L, AccessControlSeed.RoleA2, 1L), await StateAsync(databaseName));
        });

    [Fact]
    public async Task ExpectedVersion_0_eksplicit_dhe_varg_numerik_mbeten_te_pranueshem_dhe_ekuivalent()
        => await WithHostAsync(async (client, databaseName) =>
        {
            // 0 EKSPLICIT (numër) mbetet i vlefshëm → version 0→1.
            using (var explicitZero = await client.SendAsync(
                RawRequest($$"""{"roleId":"{{AccessControlSeed.RoleA2:D}}","expectedVersion":0}""", "key-zero"),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, explicitZero.StatusCode);
                var body = await explicitZero.Content.ReadFromJsonAsync<MembershipRoleResponse>(
                    TestContext.Current.CancellationToken);
                Assert.Equal(1L, body!.Version);
            }

            // Varg numerik i vjetruar "0" → i njëjti 409 si numri 0.
            using (var staleString = await client.SendAsync(
                RawRequest($$"""{"roleId":"{{AccessControlSeed.RoleA:D}}","expectedVersion":"0"}""", "key-stale-string"),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.Conflict, staleString.StatusCode);
                var (_, _, _, code) = await TenantIsolationEndpointTests.StableProblemAsync(staleString);
                Assert.Equal("CONCURRENCY_CONFLICT", code);
                await AssertProblemWireShapeAsync(staleString);
            }

            // Varg numerik korrekt "1" → sukses njësoj si numri 1 → version 1→2.
            using (var stringVersion = await client.SendAsync(
                RawRequest($$"""{"roleId":"{{AccessControlSeed.RoleA:D}}","expectedVersion":"1"}""", "key-string"),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, stringVersion.StatusCode);
                var body = await stringVersion.Content.ReadFromJsonAsync<MembershipRoleResponse>(
                    TestContext.Current.CancellationToken);
                Assert.Equal(2L, body!.Version);
            }

            Assert.Equal((2L, 2L, AccessControlSeed.RoleA, 2L), await StateAsync(databaseName));
        });
}
