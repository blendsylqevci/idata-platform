using System.Net;
using System.Net.Http.Json;
using Idata.Platform.DbMigrator;
using Idata.Platform.Persistence;
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
            await using var factory = new PlatformTestHostFactory();
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

    /// <summary>
    /// DDL DESTRUKTIV mbi target-in — FAIL-CLOSED. Rendi i saktë:
    /// (1) emri parsohet/validohet me `PlatformDatabaseConnection.Parse`
    /// (NpgsqlConnectionStringBuilder; zero interpolim raw) dhe kontrollohet
    /// kundër allowlist-it sintetik — TË DYJA **PARA HAPJES SË LIDHJES**, pra
    /// një emër i injektuar ose jo-sintetik nuk arrin as të lidhet;
    /// (2) pas hapjes së lidhjes, por **PARA DDL-së**, verifikohet
    /// `current_database()` = emri i validuar. Vetëm pastaj ekzekutohet DROP-i.
    /// </summary>
    private static async Task DropSentinelAsync(string db)
    {
        // 1. Validim PARA lidhjes: konfigurim i tipizuar + allowlist sintetik.
        var settings = PlatformDatabaseConnection.Parse(
            Environment.GetEnvironmentVariable("PGHOST") ?? "localhost",
            Environment.GetEnvironmentVariable("PGPORT") ?? "5432",
            Environment.GetEnvironmentVariable("PGUSER") ?? "idata",
            db);
        if (!SyntheticDatabaseSafety.IsSynthetic(settings.Database))
        {
            throw new InvalidOperationException(
                "Refuzim fail-closed: DDL destruktiv lejohet vetem mbi DB sintetike.");
        }

        await using var connection = new NpgsqlConnection(settings.ToConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        // 2. Verifikim PAS hapjes, PARA DDL-se: DB efektive = ajo e validuar.
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, settings.Database);

        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE platform.migration_smoke_check";
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// INJECTION në fushën `database`: emri i dyfishuar me çelësa shtesë
    /// (`;Database=`, `;Host=`, `;Username=`) duhet të refuzohet nga VALIDIMI
    /// i konfigurimit — pra me `PlatformDatabaseConfigurationException` dhe
    /// `Field = "database"`, JO me një gabim DNS/auth/missing-database. Mbi
    /// implementimin e vjetër me interpolim raw, çelësi i dytë do të kishte
    /// devijuar lidhjen te DB-ja e vërtetë e sulmit; këtu as lidhja nuk hapet.
    /// </summary>
    [Theory]
    [InlineData("idata_platform_test_alias;Database=idata_platform")]
    [InlineData("idata_platform_test_alias;Host=evil.invalid")]
    [InlineData("idata_platform_test_alias;Username=evil")]
    public async Task Injection_ne_emrin_e_DB_se_refuzohet_nga_validimi_i_konfigurimit(string injected)
    {
        // DB-ja SINTETIKE që sulmi do të dëmtonte nëse çelësi i dytë do të fitonte.
        await using var attackTarget = await PlatformTestDatabase.CreateAsync("ddlattack");
        await MigrateAsync(attackTarget.DatabaseName);

        var failure = await Assert.ThrowsAsync<PlatformDatabaseConfigurationException>(
            () => DropSentinelAsync(injected));
        Assert.Equal("database", failure.Field);
        // Zero echo i vlerës së papranuar në mesazh.
        Assert.DoesNotContain(";", failure.Message, StringComparison.Ordinal);

        await AssertSentinelIntactAsync(attackTarget.DatabaseName);
    }

    /// <summary>
    /// Target JASHTË allowlist-it sintetik (DB reale ose prefiks pa suffix):
    /// refuzohet nga guard-i i harness-it me `InvalidOperationException`-in e
    /// tij specifik, PARA hapjes së lidhjes — jo nga një gabim lidhjeje.
    /// </summary>
    [Theory]
    [InlineData("idata_platform")]
    [InlineData("idata")]
    [InlineData("idata_platform_test_")]
    public async Task Target_jashte_allowlist_it_sintetik_refuzohet_nga_guard_i(string target)
    {
        await using var candidate = await PlatformTestDatabase.CreateAsync("ddlallow");
        await MigrateAsync(candidate.DatabaseName);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => DropSentinelAsync(target));
        Assert.Contains("sintetike", failure.Message, StringComparison.OrdinalIgnoreCase);

        await AssertSentinelIntactAsync(candidate.DatabaseName);
    }

    /// <summary>Provon se DB-ja sintetike kandidate mbeti e paprekur (zero DDL).</summary>
    private static async Task AssertSentinelIntactAsync(string databaseName)
    {
        var settings = PlatformDatabaseConnection.Parse(
            Environment.GetEnvironmentVariable("PGHOST") ?? "localhost",
            Environment.GetEnvironmentVariable("PGPORT") ?? "5432",
            Environment.GetEnvironmentVariable("PGUSER") ?? "idata",
            databaseName);
        await using var connection = new NpgsqlConnection(settings.ToConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);
        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT to_regclass('platform.migration_smoke_check') IS NOT NULL";
        Assert.Equal(true, await check.ExecuteScalarAsync(TestContext.Current.CancellationToken));
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

        var settings = PlatformDatabaseConnection.Parse(
            Environment.GetEnvironmentVariable("PGHOST") ?? "localhost",
            Environment.GetEnvironmentVariable("PGPORT") ?? "5432",
            Environment.GetEnvironmentVariable("PGUSER") ?? "idata",
            db.DatabaseName);
        await using var connection = new NpgsqlConnection(settings.ToConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, db.DatabaseName);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT to_regclass('platform.__ef_migrations_history') IS NOT NULL" +
            " AND to_regclass('platform.migration_smoke_check') IS NOT NULL";
        Assert.True((bool?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
