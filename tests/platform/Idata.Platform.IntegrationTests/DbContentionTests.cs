using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Idata.Platform.Modules.AccessControl.Application;
using Idata.Platform.Modules.AccessControl.Infrastructure;
using Idata.Platform.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// DB CONTENTION ≠ CAS: deadlock (40P01), serialization failure (40001),
/// lock timeout (55P03) dhe statement timeout server-side (57014) NUK janë
/// "expectedVersion mismatch" — ato japin rollback të plotë + 503
/// PLATFORM_UNAVAILABLE stabil, me zero mutation/audit/receipt, zero retry
/// automatik dhe ZERO SQLSTATE/query/identifikues DB në përgjigje.
/// 409 CONCURRENCY_CONFLICT mbetet VETËM për mismatch real të versionit.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class DbContentionTests
{
    /// <summary>Factory që ngushton lock_timeout-in e command path-it REAL.</summary>
    private sealed class TightLockTimeoutFactory : AccessControlApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IMembershipRoleUnitOfWork>(sp =>
                    new EfMembershipRoleUnitOfWork(sp.GetRequiredService<PlatformDbContext>())
                    {
                        LockTimeout = TimeSpan.FromMilliseconds(300),
                    });
            });
        }
    }

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

    private static HttpRequestMessage RoleUpdate(string key)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri($"/api/platform/memberships/{AccessControlSeed.MembershipC:D}/role", UriKind.Relative))
        {
            Content = JsonContent.Create(new { roleId = AccessControlSeed.RoleA2, expectedVersion = 0L }),
        };
        request.Headers.Add("X-Test-UserId", AccessControlSeed.UserA.ToString("D"));
        request.Headers.Add("X-Test-BusinessId", AccessControlSeed.BusinessA.ToString("D"));
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static async Task<NpgsqlConnection> OpenAsync(string databaseName)
    {
        var connection = new NpgsqlConnection(TenantIsolationEndpointTests.ConnectionStringFor(databaseName));
        await connection.OpenAsync();
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);
        return connection;
    }

    // ══════ Lock i mbajtur → 55P03 në rrugën REALE → 503 → recovery ══════

    [Fact]
    public async Task Lock_i_mbajtur_jep_503_pa_efekt_dhe_kerkesa_e_re_kalon_pas_lirimit()
        => await WithSeedAsync(async databaseName =>
        {
            using var factory = new TightLockTimeoutFactory();
            using var client = factory.CreateClient();

            // Një connection tjetër mban FOR UPDATE mbi target membership-in
            // (transaction i hapur, pa commit) → command path-i me lock_timeout
            // të kufizuar DUHET të dështojë me 55P03 → 503.
            await using var holder = await OpenAsync(databaseName);
            await using (var holderTx = await holder.BeginTransactionAsync(TestContext.Current.CancellationToken))
            {
                await using (var hold = holder.CreateCommand())
                {
                    hold.Transaction = holderTx;
                    hold.CommandText =
                        $"SELECT 1 FROM platform.business_membership WHERE id = '{AccessControlSeed.MembershipC:D}' FOR UPDATE";
                    await hold.ExecuteScalarAsync(TestContext.Current.CancellationToken);
                }

                var blocked = await client.SendAsync(
                    RoleUpdate("contention-1"), TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
                var body = await blocked.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                using (var problem = JsonDocument.Parse(body))
                {
                    Assert.Equal("PLATFORM_UNAVAILABLE", problem.RootElement.GetProperty("errorCode").GetString());
                }

                // ZERO detaje DB të publikuara: pa SQLSTATE, pa emra tabelash,
                // pa terma lidhjeje, pa emrin e DB-së sintetike.
                foreach (var leaked in (string[])
                         ["55P03", "40P01", "lock_timeout", "business_membership", "Host=", databaseName])
                {
                    Assert.DoesNotContain(leaked, body, StringComparison.OrdinalIgnoreCase);
                }

                // Zero efekt dhe zero retry i heshtur: gjendja e paprekur.
                await AssertUntouchedAsync(databaseName);

                await holderTx.RollbackAsync(TestContext.Current.CancellationToken);
            }

            // Pas lirimit të lock-ut, një kërkesë E RE (e dërguar nga klienti,
            // jo retry automatik) kalon normalisht — dështimi s'është ngjitës.
            var retry = await client.SendAsync(
                RoleUpdate("contention-1"), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        });

    private static async Task AssertUntouchedAsync(string databaseName)
    {
        await using var connection = await OpenAsync(databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT (SELECT version FROM platform.business_membership WHERE id = '{AccessControlSeed.MembershipC:D}'),
                   (SELECT COUNT(*) FROM platform.audit_log),
                   (SELECT COUNT(*) FROM platform.command_receipt)
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(0L, reader.GetInt64(1));
        Assert.Equal(0L, reader.GetInt64(2));
    }

    // ══════ Klasifikimi i SQLSTATE-ve — mapping i drejtpërdrejtë ══════

    /// <summary>
    /// Të katër SQLSTATE-t transient (përfshirë deadlock-un 40P01, të cilit
    /// nuk i ndërtohet harness i brishtë kur mapping-u testohet drejtpërdrejt)
    /// → transient contention → 503; kodet jo-transient NUK klasifikohen.
    /// </summary>
    [Theory]
    [InlineData("40P01", true)]  // deadlock_detected
    [InlineData("40001", true)]  // serialization_failure
    [InlineData("55P03", true)]  // lock_not_available / lock_timeout
    [InlineData("57014", true)]  // query_canceled (statement timeout server-side)
    [InlineData("23505", false)] // unique_violation → rrugë idempotence, JO 503
    [InlineData("42501", false)] // insufficient_privilege → gabim real, JO 503
    public void Klasifikimi_i_transient_contention_mbulon_sakte_SQLSTATE_et(string sqlState, bool transient)
        => Assert.Equal(
            transient,
            EfMembershipRoleUnitOfWork.IsTransientContention(
                new PostgresException("test", "ERROR", "ERROR", sqlState)));
}
