using System.Net;
using System.Net.Http.Json;
using Idata.Platform.DbMigrator;
using Idata.Platform.Modules.PlatformStatus.Infrastructure;
using Idata.Platform.Modules.PlatformStatus.Presentation;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Semantika e readiness-it kundrejt PostgreSQL REAL, PA false-positive:
/// vetëm DB e migruar me sentinel → OK/READY; e arritshme por e pamigruar,
/// histori pa sentinel, sentinel i hequr dhe e paarritshme → DEGRADED/UNAVAILABLE.
/// API host-i pikëzohet te DB-ja e testit përmes env-it PARA ndërtimit; probe-i
/// mbetet READ-ONLY (asnjë auto-migrim). Kolektiv serial (env global).
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class TargetDatabaseMigrationSmokeTests
{
    private static async Task MigrateAsync(string db)
    {
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db);
        try
        {
            Assert.Equal(0, await MigratorApp.RunAsync(["migrate"]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    private static async Task<(HttpStatusCode Status, PlatformStatusResponse? Body, string Raw)> QueryStatusAsync(string db)
    {
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db);
        try
        {
            await using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
            var response = await client.GetAsync(new Uri("/api/platform/status", UriKind.Relative), TestContext.Current.CancellationToken);
            var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var body = await response.Content.ReadFromJsonAsync<PlatformStatusResponse>(TestContext.Current.CancellationToken);
            return (response.StatusCode, body, raw);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    private static async Task DropSentinelAsync(string db)
    {
        var host = Environment.GetEnvironmentVariable("PGHOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";
        var user = Environment.GetEnvironmentVariable("PGUSER") ?? "idata";
        await using var connection = new NpgsqlConnection($"Host={host};Port={port};Username={user};Database={db};Timeout=5");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE platform.migration_smoke_check";
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Migrated_db_kthen_200_ok_ready_dhe_zero_detaje_lidhjeje()
    {
        await using var db = await PlatformTestDatabase.CreateAsync(nameof(Migrated_db_kthen_200_ok_ready_dhe_zero_detaje_lidhjeje));
        await MigrateAsync(db.DatabaseName);

        var (status, body, raw) = await QueryStatusAsync(db.DatabaseName);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);
        Assert.Equal("OK", body.Status);
        Assert.Equal("READY", body.Database);
        Assert.Equal("platform-v1", body.ApiVersion);
        Assert.Equal("NOT_CONFIGURED_F1_PENDING", body.Capabilities.Identity);
        // Zero connection details në përgjigje.
        foreach (var secretish in (string[])["Host=", "Password", "Username=", db.DatabaseName, "5432"])
        {
            Assert.DoesNotContain(secretish, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Db_e_arritshme_por_e_pamigruar_kthen_degraded()
    {
        await using var db = await PlatformTestDatabase.CreateAsync(nameof(Db_e_arritshme_por_e_pamigruar_kthen_degraded));

        var (status, body, _) = await QueryStatusAsync(db.DatabaseName);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);
        Assert.Equal("DEGRADED", body.Status);
        Assert.Equal("UNAVAILABLE", body.Database);
    }

    [Fact]
    public async Task Histori_e_migruar_por_pa_tabelen_sentinel_kthen_degraded()
    {
        await using var db = await PlatformTestDatabase.CreateAsync(nameof(Histori_e_migruar_por_pa_tabelen_sentinel_kthen_degraded));
        await MigrateAsync(db.DatabaseName);
        await DropSentinelAsync(db.DatabaseName);

        var (_, body, _) = await QueryStatusAsync(db.DatabaseName);

        Assert.NotNull(body);
        Assert.Equal("DEGRADED", body.Status);
        Assert.Equal("UNAVAILABLE", body.Database);
    }

    [Fact]
    public async Task Db_e_paarritshme_kthen_degraded_pa_exception()
    {
        var (status, body, raw) = await QueryStatusAsync("idata_platform_test_does_not_exist_xyz");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);
        Assert.Equal("UNAVAILABLE", body.Database);
        Assert.DoesNotContain("idata_platform_test_does_not_exist_xyz", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Migration_history_dhe_sentinel_jetojne_ne_schema_platform()
    {
        await using var db = await PlatformTestDatabase.CreateAsync(nameof(Migration_history_dhe_sentinel_jetojne_ne_schema_platform));
        await MigrateAsync(db.DatabaseName);

        var host = Environment.GetEnvironmentVariable("PGHOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";
        var user = Environment.GetEnvironmentVariable("PGUSER") ?? "idata";
        await using var connection = new NpgsqlConnection($"Host={host};Port={port};Username={user};Database={db.DatabaseName};Timeout=5");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT to_regclass('platform.__ef_migrations_history') IS NOT NULL" +
            " AND to_regclass('platform.migration_smoke_check') IS NOT NULL";
        Assert.True((bool?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
