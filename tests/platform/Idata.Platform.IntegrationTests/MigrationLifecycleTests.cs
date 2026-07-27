using Idata.Platform.DbMigrator;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Cikli i plotë i migrimit i provuar MACHINE-CHECKABLE (jo grep) kundrejt
/// PostgreSQL real, përmes MigratorApp in-process + verifikuesit `assert-state`.
/// Guard-i destruktiv, env-i dhe emri i DB-së vendosen brenda testit; procesi
/// është single-threaded (env global) — shënuar me collection jo-paralele.
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class MigrationLifecycleTests
{
    private static async Task<int> RunAsync(string db, params string[] args)
    {
        var previousDb = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db);
        try
        {
            return await MigratorApp.RunAsync(args);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previousDb);
        }
    }

    private static async Task<int> RunDestructiveAsync(string db, params string[] args)
    {
        var previousEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var previousOptIn = Environment.GetEnvironmentVariable(DestructiveResetGuard.OptInVariable);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "CI");
        Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, DestructiveResetGuard.OptInValue);
        try
        {
            return await RunAsync(db, args);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnv);
            Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, previousOptIn);
        }
    }

    [Fact]
    public async Task Fresh_noop_down_reapply_kalojne_assertions_per_faze()
    {
        await using var db = await PlatformTestDatabase.CreateAsync(nameof(Fresh_noop_down_reapply_kalojne_assertions_per_faze));

        // 1. Fresh migrate → 0001+0002; applied=2, pending=0, sentinel present.
        Assert.Equal(0, await RunAsync(db.DatabaseName, "migrate"));
        Assert.Equal(0, await RunAsync(db.DatabaseName, "assert-state", "2", "0", "present"));

        // 2. No-op migrate → applied set i pandryshuar; applied=2, pending=0.
        Assert.Equal(0, await RunAsync(db.DatabaseName, "migrate"));
        Assert.Equal(0, await RunAsync(db.DatabaseName, "assert-state", "2", "0", "present"));

        // 3. Guarded down-to-zero → applied=0, pending=2, sentinel absent.
        Assert.Equal(0, await RunDestructiveAsync(db.DatabaseName, "down-to-zero"));
        Assert.Equal(0, await RunAsync(db.DatabaseName, "assert-state", "0", "2", "absent"));

        // 4. Reapply → applied=2, pending=0, sentinel rikthehet.
        Assert.Equal(0, await RunAsync(db.DatabaseName, "migrate"));
        Assert.Equal(0, await RunAsync(db.DatabaseName, "assert-state", "2", "0", "present"));
    }

    [Fact]
    public async Task Assert_state_me_pritje_te_gabuar_kthen_exit_3()
    {
        await using var db = await PlatformTestDatabase.CreateAsync(nameof(Assert_state_me_pritje_te_gabuar_kthen_exit_3));
        Assert.Equal(0, await RunAsync(db.DatabaseName, "migrate"));

        // Pritje e gabuar e count-it dhe e sentinelit → exit 3 (mismatch).
        Assert.Equal(3, await RunAsync(db.DatabaseName, "assert-state", "3", "0", "present"));
        Assert.Equal(3, await RunAsync(db.DatabaseName, "assert-state", "2", "0", "absent"));
    }

    [Fact]
    public async Task Down_pa_optin_refuzohet_dhe_nuk_e_prek_gjendjen()
    {
        await using var db = await PlatformTestDatabase.CreateAsync(nameof(Down_pa_optin_refuzohet_dhe_nuk_e_prek_gjendjen));
        Assert.Equal(0, await RunAsync(db.DatabaseName, "migrate"));

        // Pa opt-in/env → exit 2; gjendja mbetet e migruar (sentinel present).
        var previousEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var previousOptIn = Environment.GetEnvironmentVariable(DestructiveResetGuard.OptInVariable);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, null);
        try
        {
            Assert.Equal(2, await RunAsync(db.DatabaseName, "down-to-zero"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnv);
            Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, previousOptIn);
        }

        Assert.Equal(0, await RunAsync(db.DatabaseName, "assert-state", "2", "0", "present"));
    }

    [Fact]
    public async Task Down_ndaj_db_jo_sintetike_refuzohet_para_cdo_lidhjeje()
    {
        // Target = idata_platform (jo sintetik) me env+optin → guard-i refuzon
        // PARA çdo connection-i; asnjë mutacion; exit 2. (DB nuk ekziston fare
        // — po të hapej lidhja, do të dështonte ndryshe; refuzimi provon
        // se guard-i ndodh para I/O-së.)
        var previousEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var previousOptIn = Environment.GetEnvironmentVariable(DestructiveResetGuard.OptInVariable);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "CI");
        Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, DestructiveResetGuard.OptInValue);
        try
        {
            Assert.Equal(2, await RunAsync("idata_platform", "down-to-zero"));
            Assert.Equal(2, await RunAsync("idata", "down-to-zero"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", previousEnv);
            Environment.SetEnvironmentVariable(DestructiveResetGuard.OptInVariable, previousOptIn);
        }
    }

    [Fact]
    public async Task Komande_e_panjohur_kthen_exit_2()
    {
        await using var db = await PlatformTestDatabase.CreateAsync(nameof(Komande_e_panjohur_kthen_exit_2));
        Assert.Equal(2, await RunAsync(db.DatabaseName, "nuke-everything"));
        Assert.Equal(2, await RunAsync(db.DatabaseName, "migrate", "extra-arg"));
    }

    [Fact]
    public async Task Injection_ne_emrin_e_db_refuzohet_para_cdo_lidhjeje_dhe_nuk_e_prek_targetin()
    {
        // VETEM DB sintetike. Skenari i gjetjes: "alias" kalon prefix-guard-in
        // tekstual, por celesi i dyte Database= e devijon lidhjen te nje target
        // tjeter sintetik i migruar — down-to-zero do ta resetonte ate.
        await using var target = await PlatformTestDatabase.CreateAsync("injtarget");
        Assert.Equal(0, await RunAsync(target.DatabaseName, "migrate"));
        Assert.Equal(0, await RunAsync(target.DatabaseName, "assert-state", "2", "0", "present"));

        var injected = $"idata_platform_test_alias;Database={target.DatabaseName}";
        Assert.Equal(2, await RunDestructiveAsync(injected, "down-to-zero"));

        // Targeti sintetik mbetet i PAPREKUR: applied=2, pending=0, sentinel present.
        Assert.Equal(0, await RunAsync(target.DatabaseName, "assert-state", "2", "0", "present"));

        // Variantet e tjera te injection-it refuzohen PARA cdo I/O-je (host
        // fiktiv nuk kontaktohet kurre; refuzimi eshte exit 2 i menjehershem).
        Assert.Equal(2, await RunDestructiveAsync("idata_platform_test_alias;Host=evil.invalid", "down-to-zero"));
        Assert.Equal(2, await RunDestructiveAsync("idata_platform_test_alias;Username=evil", "down-to-zero"));
        Assert.Equal(2, await RunDestructiveAsync("idata_platform_test_alias;Timeout=30", "down-to-zero"));
    }
}
