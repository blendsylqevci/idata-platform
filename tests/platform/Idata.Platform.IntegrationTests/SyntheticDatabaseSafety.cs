using System.Text.RegularExpressions;
using Npgsql;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Siguria §6A (klasa F16) e test harness-it: PARA çdo apply/write —
/// (1) emri i pritur duhet të jetë në allowlist-in SINTETIK
/// (`idata_platform_ci` EKZAKT ose `idata_platform_test_` + suffix jo-bosh
/// `[a-z0-9_]+`); (2) `SELECT current_database()` duhet të kthejë EKZAKT
/// emrin e pritur. Mismatch/gabim → përjashtim fail-closed PARA çdo
/// migrimi/DML — pa echo të emrit të DB-së në mesazh.
/// </summary>
public static class SyntheticDatabaseSafety
{
    public static bool IsSynthetic(string databaseName)
        => databaseName == "idata_platform_ci"
           || Regex.IsMatch(databaseName, @"^idata_platform_test_[a-z0-9_]+\z");

    /// <summary>Fail-closed: allowlist + barazi me DB-në efektive të lidhjes.</summary>
    public static async Task AssertSyntheticTargetAsync(NpgsqlConnection openConnection, string expectedDatabase)
    {
        if (!IsSynthetic(expectedDatabase))
        {
            throw new InvalidOperationException(
                "Refuzim fail-closed: target DB jashte allowlist-it sintetik te testeve.");
        }

        var effective = await CurrentDatabaseAsync(openConnection);
        if (!string.Equals(effective, expectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refuzim fail-closed: DB-ja efektive e lidhjes nuk perputhet me target-in e pritur.");
        }
    }

    /// <summary>Lidhja e mirëmbajtjes duhet të jetë EKZAKT te `postgres`.</summary>
    public static async Task AssertMaintenanceAsync(NpgsqlConnection openConnection)
    {
        var effective = await CurrentDatabaseAsync(openConnection);
        if (!string.Equals(effective, "postgres", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refuzim fail-closed: lidhja e mirembajtjes nuk eshte te DB-ja 'postgres'.");
        }
    }

    public static async Task<string?> CurrentDatabaseAsync(NpgsqlConnection openConnection)
    {
        await using var command = openConnection.CreateCommand();
        command.CommandText = "SELECT current_database()";
        return await command.ExecuteScalarAsync() as string;
    }
}
