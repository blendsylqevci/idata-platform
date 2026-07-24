using Idata.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Idata.Platform.DbMigrator;

/// <summary>
/// Komandat e DbMigrator-it — të thirrshme edhe nga testet (in-process).
/// RENDI I DETYRUAR: parsimi + guard-et PURE ekzekutohen PARA krijimit të
/// DbContext-it, PARA çdo connection-i dhe PARA çdo query/mutacioni.
/// Exit codes fikse: 0 = sukses · 2 = refuzim/komandë e panjohur (fail-closed)
/// · 3 = assertion mismatch. Zero connection string/path/kredencial në output.
/// </summary>
public static class MigratorApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        var command = args.Length > 0 ? args[0] : "migrate";

        // Argumente shtesë të palejuara → fail-closed (assert-state ka aritet fikse).
        var expectedArity = command switch
        {
            "assert-state" => 4,
            "migrate" or "status" or "down-to-zero" or "applied-ids" => 1,
            _ => -1,
        };
        if (expectedArity < 0 || args.Length > Math.Max(expectedArity, 1) || (command == "assert-state" && args.Length != 4))
        {
            Console.Error.WriteLine(
                "DbMigrator: komande/argumente te panjohura. Te lejuara: migrate | status | applied-ids | assert-state <applied> <pending> <present|absent> | down-to-zero.");
            return 2;
        }

        // Konfigurimi I VETËM i validuar (fail-closed) — i njëjti objekt ushqen
        // guard-in destruktiv DHE connection string-un: zero mundësi që guard-i
        // të shohë një DB tjetër nga ajo ku lidhemi (anti connection-string
        // injection; vlera të papranuara → exit 2 pa asnjë lidhje).
        PlatformDatabaseSettings settings;
        try
        {
            settings = PlatformDatabaseConnection.SettingsFromEnvironment();
        }
        catch (PlatformDatabaseConfigurationException ex)
        {
            Console.Error.WriteLine($"DbMigrator: konfigurim i pavlefshem i target DB-se (fusha '{ex.Field}') — refuzim fail-closed.");
            return 2;
        }

        if (command == "down-to-zero")
        {
            // GUARD-i PURE — para çdo context/connection/query.
            var verdict = DestructiveResetGuard.Evaluate(
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
                Environment.GetEnvironmentVariable(DestructiveResetGuard.OptInVariable),
                settings.Database);
            if (verdict != DestructiveResetVerdict.Allowed)
            {
                Console.Error.WriteLine(verdict switch
                {
                    DestructiveResetVerdict.EnvironmentNotAllowed =>
                        "DbMigrator: down-to-zero refuzohet — DOTNET_ENVIRONMENT duhet Development ose CI.",
                    DestructiveResetVerdict.OptInMissing =>
                        $"DbMigrator: down-to-zero refuzohet — mungon opt-in-i eksplicit {DestructiveResetGuard.OptInVariable}.",
                    _ =>
                        $"DbMigrator: down-to-zero refuzohet — target DB nuk eshte ne allowlist-in sintetik ({DestructiveResetGuard.CiDatabaseName} | {DestructiveResetGuard.TestDatabasePrefix}*).",
                });
                return 2;
            }
        }

        await using var context = PlatformDbContextFactory.Create(settings.ToConnectionString());

        // §6A (klasa F16): DB-ja EFEKTIVE e lidhjes duhet te jete EKZAKT ajo e
        // konfiguruar/validuar — SELECT current_database() para cdo veprimi.
        // Perjashtim i vetem: `migrate` mbi DB inekzistente (3D000) — krijimi
        // behet nga MigrateAsync me te NJEJTIN connection string te validuar
        // dhe verifikohet PAS krijimit. Mismatch → exit 2, zero mutacion,
        // zero emra/connection string ne output.
        var verifyAfterCreate = false;
        try
        {
            if (!await EffectiveDatabaseMatchesAsync(context, settings.Database))
            {
                Console.Error.WriteLine("DbMigrator: DB-ja efektive e lidhjes NUK perputhet me konfigurimin — refuzim fail-closed.");
                return 2;
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName && command == "migrate")
        {
            verifyAfterCreate = true; // DB-ja krijohet nga migrate; verifikohet me poshte
        }
        catch (NpgsqlException)
        {
            if (command is "down-to-zero")
            {
                Console.Error.WriteLine("DbMigrator: lidhja per verifikimin e target DB-se deshtoi — down-to-zero refuzohet fail-closed.");
                return 2;
            }

            throw; // komandat e tjera raportojne deshtimin e lidhjes si me pare
        }

        switch (command)
        {
            case "migrate":
            {
                var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
                await context.Database.MigrateAsync();
                if (verifyAfterCreate && !await EffectiveDatabaseMatchesAsync(context, settings.Database))
                {
                    Console.Error.WriteLine("DbMigrator: DB-ja efektive pas krijimit NUK perputhet me konfigurimin — gjendje e refuzuar.");
                    return 2;
                }

                var applied = (await context.Database.GetAppliedMigrationsAsync()).Count();
                Console.WriteLine($"DbMigrator: aplikoi {pending.Length} migrime te reja; historia tani = {applied}.");
                return 0;
            }

            case "status":
            {
                var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
                Console.WriteLine($"DbMigrator: {pending.Length} migrime pending.");
                return pending.Length == 0 ? 0 : 3;
            }

            case "applied-ids":
            {
                // Output machine-readable: një ID migrimi për rresht, i renditur.
                foreach (var id in (await context.Database.GetAppliedMigrationsAsync()).Order(StringComparer.Ordinal))
                {
                    Console.WriteLine(id);
                }

                return 0;
            }

            case "assert-state":
            {
                if (!int.TryParse(args[1], out var expectedApplied)
                    || !int.TryParse(args[2], out var expectedPending)
                    || args[3] is not ("present" or "absent"))
                {
                    Console.Error.WriteLine("DbMigrator: assert-state <applied:int> <pending:int> <present|absent>.");
                    return 2;
                }

                var appliedIds = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
                var assemblyIds = context.Database.GetMigrations().ToArray();
                var sentinelPresent = await SentinelExistsAsync(context);

                var errors = MigrationStateAssertion.Check(
                    expectedApplied, expectedPending, args[3] == "present",
                    appliedIds, assemblyIds, sentinelPresent);
                if (errors.Count > 0)
                {
                    foreach (var error in errors)
                    {
                        Console.Error.WriteLine($"ASSERT-FAIL {error}");
                    }

                    return 3;
                }

                Console.WriteLine($"ASSERT-OK applied={expectedApplied} pending={expectedPending} sentinel={args[3]}");
                return 0;
            }

            case "down-to-zero":
            {
                await context.Database.MigrateAsync("0");
                Console.WriteLine("DbMigrator: zbriti ne zero (asnje migrim i aplikuar).");
                return 0;
            }

            default:
                return 2; // e mbuluar nga ariteti me lart; mbrojtje e dyfishte
        }
    }

    /// <summary>Krahasim READ-ONLY i `current_database()` me emrin e validuar.</summary>
    private static async Task<bool> EffectiveDatabaseMatchesAsync(PlatformDbContext context, string expected)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_database()";
        var effective = await command.ExecuteScalarAsync() as string;
        return string.Equals(effective, expected, StringComparison.Ordinal);
    }

    /// <summary>Kontroll READ-ONLY i tabelës sentinel — pa asnjë mutacion.</summary>
    private static async Task<bool> SentinelExistsAsync(PlatformDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass('platform.migration_smoke_check') IS NOT NULL";
        return await command.ExecuteScalarAsync() is true;
    }
}
