using System.Net;
using System.Net.Http.Json;
using Idata.Platform.Modules.PlatformStatus.Infrastructure;
using Idata.Platform.Modules.PlatformStatus.Presentation;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Prova e target migration path-it kundrejt PostgreSQL REAL (env/dev trust;
/// CI e siguron shërbimin dhe DbMigrator-i ekzekutohet PARA testeve).
/// Testet DËSHTOJNË — nuk anashkalohen në heshtje — nëse DB-ja mungon.
/// </summary>
public sealed class TargetDatabaseMigrationSmokeTests
{
    [Fact]
    public async Task Historia_e_migrimeve_dhe_tabela_teknike_jetojne_ne_schema_platform()
    {
        await using var connection = new NpgsqlConnection(PlatformDatabaseConnection.FromEnvironment());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT to_regclass('platform.__ef_migrations_history') IS NOT NULL" +
            " AND to_regclass('platform.migration_smoke_check') IS NOT NULL";
        var present = (bool?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        Assert.True(present, "Schema/historia e migrimeve mungon — ekzekuto DbMigrator-in para testeve.");
    }

    [Fact]
    public async Task Api_me_probe_real_kunder_target_db_raporton_ready()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/platform/status", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PlatformStatusResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("READY", payload.Database);
        Assert.Equal("OK", payload.Status);
    }
}
