using Idata.Platform.DbMigrator;
using Idata.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Provat e REJA të B.1 (§9 i mandatit) — kufiri 0002→0003 kundrejt
/// PostgreSQL real sintetik:
///  - DB me EKZAKT {0001,0002} → migratori NORMAL aplikon VETËM 0003;
///  - historia/rreshtat e 0001/0002 + sentinel + data e platform-ës
///    (row sintetik në platform.app_user) MBIJETOJNË të pandryshuara;
///  - rollback i GUARD-uar 0003→0002 (helper i RI me target të FIKSUAR
///    0003 — NUK ripërdoret verbërisht helper-i historik 0002→0001):
///    objektet identity hiqen, objektet/data platform mbeten;
///  - reapply 0002→0003; no-op pas 0003; historia finale = ekzakt
///    {0001,0002,0003} nga një herë secila;
///  - atomicitet: dështim i injektuar pas Down → ZERO partial-down;
///  - guard-i refuzon pa opt-in dhe kundër target-it real.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class IdentityMigrationUpgradeTests
{
    private const string Migration0001 = "0001_InitialPlatformSchema";
    private const string Migration0002 = "0002_F1AccessControlCore";
    private const string Migration0003 = "0003_IdentityStoreFoundation";

    private static PlatformDbContext CreateContext(string databaseName)
    {
        var settings = PlatformDatabaseConnection.Parse(
            Environment.GetEnvironmentVariable("PGHOST") ?? "localhost",
            Environment.GetEnvironmentVariable("PGPORT") ?? "5432",
            Environment.GetEnvironmentVariable("PGUSER") ?? "idata",
            databaseName);
        return PlatformDbContextFactory.Create(settings.ToConnectionString());
    }

    private static async Task<NpgsqlConnection> OpenVerifiedAsync(PlatformDbContext context, string databaseName)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);
        return connection;
    }

    /// <summary>Aplikim i shënjestruar forward i një migrimi (mekanizmi i EF-it).</summary>
    private static async Task ApplyExactlyAsync(string databaseName, string migrationId)
    {
        await using var context = CreateContext(databaseName);
        _ = await OpenVerifiedAsync(context, databaseName);

        var assembly = context.GetService<IMigrationsAssembly>();
        var migration = (Migration)Activator.CreateInstance(assembly.Migrations[migrationId].AsType())!;
        typeof(Migration).GetProperty(nameof(Migration.ActiveProvider))!
            .SetValue(migration, context.Database.ProviderName);

        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate(migration.UpOperations, assembly.ModelSnapshot!.Model);
        await context.GetService<IMigrationCommandExecutor>()
            .ExecuteNonQueryAsync(commands, context.GetService<IRelationalConnection>());

        // SQL i historisë = KONSTANTE e fiksuar per-migrim (anti EF1002);
        // krijimi i tabelës së historisë = IF-NOT-EXISTS (idempotent).
        await context.Database.ExecuteSqlRawAsync(context.GetService<IHistoryRepository>().GetCreateIfNotExistsScript());
        var historyInsert = migrationId switch
        {
            Migration0001 =>
                $"INSERT INTO platform.\"__ef_migrations_history\" (\"MigrationId\", \"ProductVersion\") VALUES ('{Migration0001}', '10.0.10')",
            Migration0002 =>
                $"INSERT INTO platform.\"__ef_migrations_history\" (\"MigrationId\", \"ProductVersion\") VALUES ('{Migration0002}', '10.0.10')",
            _ => throw new ArgumentOutOfRangeException(nameof(migrationId)),
        };
        await context.Database.ExecuteSqlRawAsync(historyInsert);
    }

    /// <summary>
    /// Helper i RI TEST-ONLY i rollback-ut me target të FIKSUAR **0003** —
    /// i njëjti guard tripalësh si down-to-zero, i vlerësuar PARA çdo
    /// DbContext/connection/query (refuzim → 2, zero mutacion); DownOperations
    /// e 0003 + heqja e row-it të historisë në NJË transaction PostgreSQL.
    /// NUK është rollback production (kontrata §8 e Identity doc — vetëm DB
    /// disposable/local/CI).
    /// </summary>
    private static async Task<int> GuardedRollbackTo0002Async(string databaseName, bool failAfterDown = false)
    {
        var verdict = DestructiveResetGuard.Evaluate(
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
            Environment.GetEnvironmentVariable(DestructiveResetGuard.OptInVariable),
            databaseName);
        if (verdict != DestructiveResetVerdict.Allowed)
        {
            return 2;
        }

        await using var context = CreateContext(databaseName);
        var connection = await OpenVerifiedAsync(context, databaseName);

        var assembly = context.GetService<IMigrationsAssembly>();
        var migration = (Migration)Activator.CreateInstance(assembly.Migrations[Migration0003].AsType())!;
        typeof(Migration).GetProperty(nameof(Migration.ActiveProvider))!
            .SetValue(migration, context.Database.ProviderName);
        var downSql = string.Concat(context.GetService<IMigrationsSqlGenerator>()
            .Generate(migration.DownOperations, assembly.ModelSnapshot!.Model)
            .Select(command => command.CommandText + ";\n"));

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using (var down = connection.CreateCommand())
            {
                down.Transaction = transaction;
                down.CommandText = downSql;
                await down.ExecuteNonQueryAsync();
            }

            if (failAfterDown)
            {
                throw new InvalidOperationException("Injected rollback failure.");
            }

            await using (var deleteHistory = connection.CreateCommand())
            {
                deleteHistory.Transaction = transaction;
                deleteHistory.CommandText =
                    $"DELETE FROM platform.\"__ef_migrations_history\" WHERE \"MigrationId\" = '{Migration0003}'";
                await deleteHistory.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return 0;
        }
        catch (InvalidOperationException)
        {
            await transaction.RollbackAsync();
            return 3;
        }
    }

    private static async Task<int> RunMigratorAsync(string databaseName, params string[] args)
    {
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", databaseName);
        try
        {
            return await MigratorApp.RunAsync(args);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    private static async Task<(string MigrationId, string ProductVersion)[]> HistoryAsync(string databaseName)
    {
        await using var context = CreateContext(databaseName);
        var connection = await OpenVerifiedAsync(context, databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT \"MigrationId\", \"ProductVersion\" FROM platform.\"__ef_migrations_history\" ORDER BY \"MigrationId\"";
        var rows = new List<(string, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return [.. rows];
    }

    private static async Task<T?> ScalarAsync<T>(string databaseName, string sql)
    {
        await using var context = CreateContext(databaseName);
        var connection = await OpenVerifiedAsync(context, databaseName);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return result is DBNull or null ? default : (T)result;
    }

    private static Task<bool> ObjectExistsAsync(string databaseName, string qualifiedName)
        => ScalarAsync<bool>(databaseName, $"SELECT to_regclass('{qualifiedName}') IS NOT NULL");

    /// <summary>Sentinel sintetik i të dhënave të platform-ës: një app_user row.</summary>
    private static readonly Guid SyntheticAppUserId = new("00000000-0000-4000-8000-0000000000b1");

    private static async Task SeedPlatformDataAsync(string databaseName)
    {
        await ScalarAsync<object>(databaseName,
            "INSERT INTO platform.migration_smoke_check DEFAULT VALUES RETURNING \"Id\"");
        await ScalarAsync<object>(databaseName,
            $"INSERT INTO platform.app_user (id, status, created_at) VALUES ('{SyntheticAppUserId}', 'active', now()) RETURNING id");
    }

    private static async Task AssertPlatformDataIntactAsync(string databaseName)
    {
        Assert.Equal(1L, await ScalarAsync<long>(databaseName,
            "SELECT COUNT(*) FROM platform.migration_smoke_check"));
        Assert.Equal(1L, await ScalarAsync<long>(databaseName,
            $"SELECT COUNT(*) FROM platform.app_user WHERE id = '{SyntheticAppUserId}' AND status = 'active'"));
    }

    private static readonly string[] IdentityTables =
        ["user_account", "user_login", "user_claim", "user_token",
         "oidc_application", "oidc_authorization", "oidc_scope", "oidc_token"];

    [Fact]
    public async Task Upgrade_real_0002_ne_0003_ruajn_historine_dhe_daten_e_platform_es()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("identityupgrade");

        // 1. DB me EKZAKT {0001,0002} + data sintetike platform.
        await ApplyExactlyAsync(db.DatabaseName, Migration0001);
        await ApplyExactlyAsync(db.DatabaseName, Migration0002);
        await SeedPlatformDataAsync(db.DatabaseName);

        var historyBefore = await HistoryAsync(db.DatabaseName);
        Assert.Equal([Migration0001, Migration0002], historyBefore.Select(row => row.MigrationId).ToArray());
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "2", "1", "present"));

        // 2. Migratori NORMAL aplikon VETËM 0003.
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "migrate"));

        // 3. Historia finale = ekzakt {0001,0002,0003}, nga një herë secila;
        //    rreshtat e 0001/0002 (MigrationId, ProductVersion) të pandryshuar.
        var historyAfter = await HistoryAsync(db.DatabaseName);
        Assert.Equal(
            [Migration0001, Migration0002, Migration0003],
            historyAfter.Select(row => row.MigrationId).ToArray());
        Assert.Equal(historyBefore[0], historyAfter[0]);
        Assert.Equal(historyBefore[1], historyAfter[1]);

        // 4. Data e platform-ës e PANDRYSHUAR; objektet identity ekzistojnë.
        await AssertPlatformDataIntactAsync(db.DatabaseName);
        foreach (var table in IdentityTables)
        {
            Assert.True(await ObjectExistsAsync(db.DatabaseName, $"identity.{table}"));
        }

        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "3", "0", "present"));

        // 5. No-op pas 0003 — historia identike.
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "migrate"));
        Assert.Equal(historyAfter, await HistoryAsync(db.DatabaseName));
    }

    [Fact]
    public async Task Rollback_i_guarduar_0003_ne_0002_pastaj_reapply()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("identityrollback");
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "migrate"));
        await SeedPlatformDataAsync(db.DatabaseName);

        // PA opt-in → refuzim PARA çdo lidhjeje; gjendja e paprekur.
        var previousEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var previousOptIn = Environment.GetEnvironmentVariable(DestructiveResetGuard.OptInVariable);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, null);
        try
        {
            Assert.Equal(2, await GuardedRollbackTo0002Async(db.DatabaseName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnv);
            Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, previousOptIn);
        }

        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "3", "0", "present"));

        // Guard-i refuzon target real edhe me env+opt-in (allowlist sintetik).
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "CI");
        Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, DestructiveResetGuard.OptInValue);
        try
        {
            Assert.Equal(2, await GuardedRollbackTo0002Async("idata_platform"));

            // Me guard të plotë mbi DB sintetike → rollback te 0002.
            Assert.Equal(0, await GuardedRollbackTo0002Async(db.DatabaseName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnv);
            Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, previousOptIn);
        }

        // Objektet identity HIQEN; objektet + data platform MBETEN.
        foreach (var table in IdentityTables)
        {
            Assert.False(await ObjectExistsAsync(db.DatabaseName, $"identity.{table}"));
        }

        Assert.True(await ObjectExistsAsync(db.DatabaseName, "platform.business"));
        Assert.True(await ObjectExistsAsync(db.DatabaseName, "platform.audit_log"));
        await AssertPlatformDataIntactAsync(db.DatabaseName);
        Assert.Equal(
            [Migration0001, Migration0002],
            (await HistoryAsync(db.DatabaseName)).Select(row => row.MigrationId).ToArray());
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "2", "1", "present"));

        // Reapply 0002→0003 → gjendja LATEST; data platform ende intakte.
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "migrate"));
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "3", "0", "present"));
        await AssertPlatformDataIntactAsync(db.DatabaseName);
        Assert.Equal(
            [Migration0001, Migration0002, Migration0003],
            (await HistoryAsync(db.DatabaseName)).Select(row => row.MigrationId).ToArray());
    }

    [Fact]
    public async Task Rollback_atomik_0003_deshtim_i_injektuar_nuk_le_partial_down()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("identityatomic");
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "migrate"));
        await SeedPlatformDataAsync(db.DatabaseName);

        var previousEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var previousOptIn = Environment.GetEnvironmentVariable(DestructiveResetGuard.OptInVariable);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "CI");
        Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, DestructiveResetGuard.OptInValue);
        try
        {
            Assert.Equal(3, await GuardedRollbackTo0002Async(db.DatabaseName, failAfterDown: true));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnv);
            Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, previousOptIn);
        }

        // ZERO partial-down: identity intakte, historia {0001,0002,0003}, data platform e ruajtur.
        foreach (var table in IdentityTables)
        {
            Assert.True(await ObjectExistsAsync(db.DatabaseName, $"identity.{table}"));
        }

        Assert.Equal(
            [Migration0001, Migration0002, Migration0003],
            (await HistoryAsync(db.DatabaseName)).Select(row => row.MigrationId).ToArray());
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "3", "0", "present"));
        await AssertPlatformDataIntactAsync(db.DatabaseName);
    }
}
