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
/// Kompatibiliteti REAL 0001 → 0002 (§6): DB sintetike me EKZAKT 0001 të
/// aplikuar → migratori normal aplikon VETËM 0002; row-i historik i 0001
/// (MigrationId, ProductVersion) i pandryshuar; sentinel/data e 0001 e
/// ruajtur; objektet e 0002 ekzistojnë; no-op i qëndrueshëm. Rollback
/// 0002→0001 VETËM me helper test-only të guard-uar (guard tripalësh PARA
/// çdo DbContext/connection/query): DownOperations e 0002 →
/// IMigrationsSqlGenerator → NJË transaction PostgreSQL (DDL + heqja e
/// row-it të historisë), JO `IMigrator` me target migration.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class MigrationUpgradePathTests
{
    private const string Migration0001 = "0001_InitialPlatformSchema";
    private const string Migration0002 = "0002_F1AccessControlCore";

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

        // §6A: allowlist sintetik + current_database() PARA çdo shkrimi.
        await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, databaseName);
        return connection;
    }

    /// <summary>
    /// Apliko EKZAKT deri te 0001 (forward). EF Core 10 nuk targeton dot ID-të
    /// jo-timestamp, ndaj përdoret mekanizmi i EF-it: UpOperations e 0001 →
    /// IMigrationsSqlGenerator → executor + INSERT i row-it të historisë me
    /// ProductVersion-in e Designer-it të 0001. Pa guard destruktiv (forward).
    /// </summary>
    private static async Task ApplyExactly0001Async(string databaseName)
    {
        await using var context = CreateContext(databaseName);
        _ = await OpenVerifiedAsync(context, databaseName);

        var assembly = context.GetService<IMigrationsAssembly>();
        var migration = (Migration)Activator.CreateInstance(assembly.Migrations[Migration0001].AsType())!;
        typeof(Migration).GetProperty(nameof(Migration.ActiveProvider))!
            .SetValue(migration, context.Database.ProviderName);

        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate(migration.UpOperations, assembly.ModelSnapshot!.Model);
        await context.GetService<IMigrationCommandExecutor>()
            .ExecuteNonQueryAsync(commands, context.GetService<IRelationalConnection>());

        // Tabela e historisë menaxhohet nga EF veçmas nga operacionet.
        // GetCreateScript-i i Npgsql-it është IF-NOT-EXISTS (idempotent), ndaj
        // ekzekutohet pa kusht, pastaj regjistrohet row-i i 0001 me
        // ProductVersion-in e Designer-it.
        await context.Database.ExecuteSqlRawAsync(context.GetService<IHistoryRepository>().GetCreateScript());
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO platform.\"__ef_migrations_history\" (\"MigrationId\", \"ProductVersion\") VALUES ('{Migration0001}', '10.0.10')");
    }

    /// <summary>
    /// Helper TEST-ONLY i rollback-ut me target të FIKSUAR 0001 — I NJËJTI
    /// guard tripalësh si down-to-zero, i vlerësuar PARA çdo
    /// DbContext/connection/query; refuzim → 2, pa asnjë mutacion.
    ///
    /// ATOMIK (§9): DownOperations e 0002 DHE heqja e row-it të historisë
    /// ekzekutohen në NJË PostgreSQL transaction; commit vetëm pasi të dyja
    /// kalojnë. `failAfterDown=true` injekton një dështim PAS DownOperations
    /// dhe PARA fshirjes së historisë → transaction-i rrëzohet plotësisht,
    /// zero partial-down.
    ///
    /// DEVIATION E DOKUMENTUAR: EF Core 10 refuzon targeted-migrate te ID-të
    /// jo-timestamp (`0001_*`/`0002_*`) — `IMigrator.MigrateAsync("0001_...")`
    /// jep "migration not found". IDA e 0001 mbetet e pandryshueshme (kërkesë
    /// e §6), ndaj rollback-i i vetëm-0002 ekzekutohet me MEKANIZMIN E VETË
    /// EF-it: DownOperations → IMigrationsSqlGenerator → executor. Target i
    /// FIKSUAR 0002 (asnjë target arbitrar; asnjë partial-down i paguard-uar).
    /// </summary>
    private static async Task<int> GuardedRollbackTo0001Async(string databaseName, bool failAfterDown = false)
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
        var migration = (Migration)Activator.CreateInstance(assembly.Migrations[Migration0002].AsType())!;
        typeof(Migration).GetProperty(nameof(Migration.ActiveProvider))!
            .SetValue(migration, context.Database.ProviderName);
        var downSql = string.Concat(context.GetService<IMigrationsSqlGenerator>()
            .Generate(migration.DownOperations, assembly.ModelSnapshot!.Model)
            .Select(command => command.CommandText + ";\n"));

        // NJË transaction: DownOperations + heqja e historisë; commit atomik.
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
                // Injektim dështimi PAS DownOperations, PARA fshirjes së historisë.
                throw new InvalidOperationException("Injected rollback failure.");
            }

            await using (var deleteHistory = connection.CreateCommand())
            {
                deleteHistory.Transaction = transaction;
                deleteHistory.CommandText =
                    $"DELETE FROM platform.\"__ef_migrations_history\" WHERE \"MigrationId\" = '{Migration0002}'";
                await deleteHistory.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return 0;
        }
        catch (InvalidOperationException)
        {
            await transaction.RollbackAsync();
            return 3; // dështim i injektuar → rollback i plotë (asnjë partial-down)
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

    [Fact]
    public async Task Upgrade_real_0001_ne_0002_ruajn_historine_dhe_daten_e_0001()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("upgradepath");

        // 1-2. DB sintetike + EKZAKT 0001.
        await ApplyExactly0001Async(db.DatabaseName);

        // 3. Ruaj (MigrationId, ProductVersion) e row-it 0001.
        var historyAt0001 = await HistoryAsync(db.DatabaseName);
        var row0001 = Assert.Single(historyAt0001);
        Assert.Equal(Migration0001, row0001.MigrationId);

        // 4. Sentinel row i 0001 — të dhëna që DUHET të mbijetojnë upgrade-in.
        await ScalarAsync<object>(db.DatabaseName,
            "INSERT INTO platform.migration_smoke_check DEFAULT VALUES RETURNING \"Id\"");

        // 5. applied=1, pending=1.
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "1", "1", "present"));

        // 6-7. Migratori NORMAL aplikon vetëm 0002.
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "migrate"));

        // 8. Historia = ekzakt {0001, 0002}, secili një herë.
        var historyAt0002 = await HistoryAsync(db.DatabaseName);
        Assert.Equal([Migration0001, Migration0002], historyAt0002.Select(row => row.MigrationId).ToArray());

        // 9. Row-i i 0001 KRYESISHT i pandryshuar (id + product version).
        Assert.Equal(row0001, historyAt0002[0]);

        // 10. Sentinel/data e 0001 e ruajtur.
        Assert.Equal(1L, await ScalarAsync<long>(db.DatabaseName, "SELECT COUNT(*) FROM platform.migration_smoke_check"));

        // 11. Objektet/invariantet e 0002 ekzistojnë: tabela, unique index,
        //     composite FK dhe triggers append-only.
        Assert.True(await ObjectExistsAsync(db.DatabaseName, "platform.business"));
        Assert.True(await ObjectExistsAsync(db.DatabaseName, "platform.command_receipt"));
        Assert.True(await ScalarAsync<bool>(db.DatabaseName,
            "SELECT COUNT(*) = 1 FROM pg_indexes WHERE schemaname='platform' AND indexname='ux_command_receipt_idempotency'"));
        Assert.True(await ScalarAsync<bool>(db.DatabaseName,
            "SELECT COUNT(*) = 3 FROM pg_trigger WHERE tgname IN ('trg_audit_log_no_update','trg_audit_log_no_delete','trg_audit_log_no_truncate')"));
        Assert.True(await ScalarAsync<bool>(db.DatabaseName,
            "SELECT COUNT(*) >= 1 FROM information_schema.table_constraints WHERE table_schema='platform' AND table_name='role_permission' AND constraint_type='FOREIGN KEY'"));

        // 12. No-op migrate → historia identike, sentinel i paprekur.
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "migrate"));
        Assert.Equal(historyAt0002, await HistoryAsync(db.DatabaseName));
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "2", "0", "present"));
    }

    [Fact]
    public async Task Rollback_i_guarduar_0002_ne_0001_dhe_reapply()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("rollbackpath");
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "migrate"));
        await ScalarAsync<object>(db.DatabaseName,
            "INSERT INTO platform.migration_smoke_check DEFAULT VALUES RETURNING \"Id\"");

        // Gjendja EKZAKTE {0001,0002} para rollback-ut.
        Assert.Equal(
            [Migration0001, Migration0002],
            (await HistoryAsync(db.DatabaseName)).Select(row => row.MigrationId).ToArray());

        // PA opt-in → refuzim PARA çdo lidhjeje; gjendja e paprekur.
        var previousEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var previousOptIn = Environment.GetEnvironmentVariable(DestructiveResetGuard.OptInVariable);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, null);
        try
        {
            Assert.Equal(2, await GuardedRollbackTo0001Async(db.DatabaseName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnv);
            Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, previousOptIn);
        }

        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "2", "0", "present"));

        // Me guard të plotë → rollback te 0001.
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "CI");
        Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, DestructiveResetGuard.OptInValue);
        try
        {
            Assert.Equal(0, await GuardedRollbackTo0001Async(db.DatabaseName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnv);
            Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, previousOptIn);
        }

        // 0001 applied · 0002 pending · sentinel i 0001 mbetet · objektet e 0002 mungojnë.
        Assert.Equal(
            [Migration0001],
            (await HistoryAsync(db.DatabaseName)).Select(row => row.MigrationId).ToArray());
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "1", "1", "present"));
        Assert.Equal(1L, await ScalarAsync<long>(db.DatabaseName, "SELECT COUNT(*) FROM platform.migration_smoke_check"));
        Assert.False(await ObjectExistsAsync(db.DatabaseName, "platform.business"));
        Assert.False(await ObjectExistsAsync(db.DatabaseName, "platform.audit_log"));
        Assert.True(await ScalarAsync<bool>(db.DatabaseName,
            "SELECT to_regproc('platform.audit_log_block_mutation') IS NULL"));

        // Reapply 0002 → kalon.
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "migrate"));
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "2", "0", "present"));
    }

    [Fact]
    public async Task Rollback_atomik_deshtim_i_injektuar_pas_down_nuk_le_partial_down()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("rollbackatomic");
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "migrate"));
        await ScalarAsync<object>(db.DatabaseName,
            "INSERT INTO platform.migration_smoke_check DEFAULT VALUES RETURNING \"Id\"");

        var previousEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var previousOptIn = Environment.GetEnvironmentVariable(DestructiveResetGuard.OptInVariable);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "CI");
        Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, DestructiveResetGuard.OptInValue);
        try
        {
            // Dështim i injektuar PAS DownOperations, PARA fshirjes së historisë.
            Assert.Equal(3, await GuardedRollbackTo0001Async(db.DatabaseName, failAfterDown: true));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnv);
            Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, previousOptIn);
        }

        // Transaction-i u rrëzua plotësisht: schema mbetet 0002, history {0001,0002},
        // sentinel i ruajtur — ZERO partial-down.
        Assert.Equal(
            [Migration0001, Migration0002],
            (await HistoryAsync(db.DatabaseName)).Select(row => row.MigrationId).ToArray());
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "2", "0", "present"));
        Assert.True(await ObjectExistsAsync(db.DatabaseName, "platform.business"));
        Assert.True(await ObjectExistsAsync(db.DatabaseName, "platform.audit_log"));
        Assert.True(await ScalarAsync<bool>(db.DatabaseName,
            "SELECT to_regproc('platform.audit_log_block_mutation') IS NOT NULL"));
        Assert.Equal(1L, await ScalarAsync<long>(db.DatabaseName, "SELECT COUNT(*) FROM platform.migration_smoke_check"));
    }

    [Fact]
    public async Task Fixtures_negative_te_sigurise_6A_refuzojne_para_cdo_shkrimi()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("safetyfixtures");
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "migrate"));

        // (a) Emër i konfiguruar sintetik, por DB efektive TJETËR → refuzim.
        await using (var context = CreateContext(db.DatabaseName))
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, "idata_platform_test_tjeter"));
        }

        // (b) Prefiks i zhveshur pa suffix → jashtë allowlist-it.
        Assert.False(SyntheticDatabaseSafety.IsSynthetic("idata_platform_test_"));

        // (c) Çelës i dytë/override `Database=` → i pamundur të arrijë shkrim
        //     (settings validation exit 2 — provuar te MigrationLifecycleTests);
        //     këtu: allowlist-i e refuzon si tekst.
        Assert.False(SyntheticDatabaseSafety.IsSynthetic("idata_platform_test_a;Database=idata_platform"));

        // (d) Target real `idata_platform` → jashtë allowlist-it sintetik.
        Assert.False(SyntheticDatabaseSafety.IsSynthetic("idata_platform"));
        Assert.False(SyntheticDatabaseSafety.IsSynthetic("idata"));

        // (e) Lidhje mirëmbajtjeje që NUK është realisht te `postgres` → refuzim
        //     PARA çdo CREATE/DROP.
        await using (var wrongMaintenance = new NpgsqlConnection(
            PlatformTestDatabase.MaintenanceConnectionString().Replace("Database=postgres", $"Database={db.DatabaseName}")))
        {
            await wrongMaintenance.OpenAsync(TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SyntheticDatabaseSafety.AssertMaintenanceAsync(wrongMaintenance));
        }

        // DB-ja sintetike kandidate mbetet e pandryshuar: gjendja e migruar intakte.
        Assert.Equal(0, await RunMigratorAsync(db.DatabaseName, "assert-state", "2", "0", "present"));
    }
}
