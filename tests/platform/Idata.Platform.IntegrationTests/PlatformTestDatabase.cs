using Npgsql;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Ndihmës për një target DB SINTETIKE/efemere (prefiks `idata_platform_test_`)
/// nga env/dev trust — krijim/heqje përmes DB-së mirëmbajtëse `postgres`.
/// Ekziston vetëm brenda testeve; legacy `idata` dhe `idata_platform` nuk
/// preken kurrë.
/// </summary>
public sealed class PlatformTestDatabase : IAsyncDisposable
{
    public string DatabaseName { get; }

    private PlatformTestDatabase(string databaseName) => DatabaseName = databaseName;

    private static string MaintenanceConnectionString()
    {
        var host = Environment.GetEnvironmentVariable("PGHOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";
        var user = Environment.GetEnvironmentVariable("PGUSER") ?? "idata";
        return $"Host={host};Port={port};Username={user};Database=postgres;Timeout=5";
    }

    public static async Task<PlatformTestDatabase> CreateAsync(string label)
    {
        // Emër i sigurt (vetëm shkronja/numra/underscore) me prefiksin sintetik.
        var safe = new string(label.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var name = $"idata_platform_test_{safe}";
        await using (var connection = new NpgsqlConnection(MaintenanceConnectionString()))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
            await ExecuteAsync(connection, $"CREATE DATABASE \"{name}\"");
        }

        return new PlatformTestDatabase(name);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
