using Idata.Platform.Persistence;
using Npgsql;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Ndihmës për një target DB SINTETIKE/efemere (prefiks `idata_platform_test_`)
/// nga env/dev trust — krijim/heqje përmes DB-së mirëmbajtëse `postgres`.
/// Siguria §6A/§9 fail-closed: emri validohet kundër allowlist-it sintetik PARA
/// CREATE/DROP; lidhja e mirëmbajtjes verifikohet me `current_database()` =
/// 'postgres'; ZERO interpolim manual — connection string-i ndërtohet nga
/// snapshot-i i VALIDUAR `PlatformDatabaseSettings` (NpgsqlConnectionStringBuilder);
/// identifikuesi quote-ohet i sigurt. Ekziston vetëm brenda testeve; legacy
/// `idata` dhe `idata_platform` nuk preken kurrë.
/// </summary>
public sealed class PlatformTestDatabase : IAsyncDisposable
{
    public string DatabaseName { get; }

    private PlatformTestDatabase(string databaseName) => DatabaseName = databaseName;

    /// <summary>Connection string i mirëmbajtjes — DB fikse `postgres`, i validuar.</summary>
    public static string MaintenanceConnectionString()
        => PlatformDatabaseConnection.Parse(
            Environment.GetEnvironmentVariable("PGHOST") ?? "localhost",
            Environment.GetEnvironmentVariable("PGPORT") ?? "5432",
            Environment.GetEnvironmentVariable("PGUSER") ?? "idata",
            "postgres").ToConnectionString();

    private static int sequence;

    public static async Task<PlatformTestDatabase> CreateAsync(string label)
    {
        // Emër i sigurt (vetëm shkronja/numra) me prefiksin sintetik. Label-i
        // kufizohet + një sekuencë atomike GLOBALE e bën emrin UNIK për çdo
        // thirrje (edhe rreshtat e [Theory] me të njëjtin emër metode), duke
        // qëndruar ≤63 karaktere — pa kolizione pool-i mes DB-ve homonime.
        var safe = new string(label.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (safe.Length > 25)
        {
            safe = safe[..25];
        }

        var name = $"idata_platform_test_{safe}_{System.Threading.Interlocked.Increment(ref sequence)}";

        // §6A fail-closed: allowlist-i sintetik PARA çdo CREATE/DROP.
        if (!SyntheticDatabaseSafety.IsSynthetic(name))
        {
            throw new InvalidOperationException(
                "Refuzim fail-closed: emri i target DB-se se testit nuk eshte sintetik.");
        }

        await using (var connection = new NpgsqlConnection(MaintenanceConnectionString()))
        {
            await connection.OpenAsync();
            await SyntheticDatabaseSafety.AssertMaintenanceAsync(connection);
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
            await ExecuteAsync(connection, $"CREATE DATABASE \"{name}\"");
        }

        return new PlatformTestDatabase(name);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
        await connection.OpenAsync();
        await SyntheticDatabaseSafety.AssertMaintenanceAsync(connection);
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
