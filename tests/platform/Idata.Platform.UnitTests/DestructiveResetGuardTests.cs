using Idata.Platform.DbMigrator;
using Idata.Platform.Modules.PlatformStatus.Infrastructure;
using Npgsql;
using Xunit;

namespace Idata.Platform.UnitTests;

/// <summary>Matrica e plotë e guard-it PURE të down-to-zero (fail-closed).</summary>
public sealed class DestructiveResetGuardTests
{
    private const string OptIn = DestructiveResetGuard.OptInValue;

    [Theory]
    [InlineData(null)]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("development")] // case-sensitive me qellim — vetem vlera ekzakte
    public void Environment_i_palejuar_refuzon_edhe_me_optin_dhe_db_sintetike(string? env)
        => Assert.Equal(
            DestructiveResetVerdict.EnvironmentNotAllowed,
            DestructiveResetGuard.Evaluate(env, OptIn, "idata_platform_ci"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("YES_I_UNDERSTAND")] // substring nuk mjafton
    public void Optin_jo_ekzakt_refuzon(string? optIn)
        => Assert.Equal(
            DestructiveResetVerdict.OptInMissing,
            DestructiveResetGuard.Evaluate("Development", optIn, "idata_platform_ci"));

    [Theory]
    [InlineData("idata_platform")] // target-i real i Bootstrap-it — i mbrojtur
    [InlineData("idata")]          // legacy — i mbrojtur
    [InlineData("idata_platform_testx")] // pa underscore-in e prefiksit
    [InlineData("IDATA_PLATFORM_CI")]    // case-sensitive
    [InlineData("idata_platform_test_")] // prefiks i zhveshur — suffix bosh nuk lejohet
    [InlineData("idata_platform_test_ALIAS")] // suffix me uppercase — jashte allowlist-it
    public void Db_jo_sintetike_refuzohet_edhe_me_env_dhe_optin(string db)
        => Assert.Equal(
            DestructiveResetVerdict.TargetDatabaseNotSynthetic,
            DestructiveResetGuard.Evaluate("CI", OptIn, db));

    [Theory]
    [InlineData("Development", "idata_platform_ci")]
    [InlineData("CI", "idata_platform_ci")]
    [InlineData("CI", "idata_platform_test_lifecycle")]
    public void Te_tria_kushtet_bashke_lejojne(string env, string db)
        => Assert.Equal(
            DestructiveResetVerdict.Allowed,
            DestructiveResetGuard.Evaluate(env, OptIn, db));
}

/// <summary>
/// Matrica fail-closed e validimit të konfigurimit të target DB-së — burimi
/// I VETËM që ushqen guard-in destruktiv DHE connection string-un (Finding A).
/// </summary>
public sealed class PlatformDatabaseSettingsTests
{
    private static PlatformDatabaseSettings ParseDb(string database)
        => PlatformDatabaseConnection.Parse("localhost", "5432", "idata", database);

    [Theory]
    [InlineData("idata_platform")]
    [InlineData("idata_platform_ci")]
    [InlineData("idata_platform_test_lifecycle")]
    [InlineData("a")] // minimumi: nje shkronje e vetme
    public void Emrat_e_vlefshem_kalojne_dhe_ruhen_te_pandryshuar(string database)
        => Assert.Equal(database, ParseDb(database).Database);

    [Fact]
    public void Emri_me_63_karaktere_kalon_dhe_me_64_refuzohet()
    {
        var max = "a" + new string('b', 62); // 63 karaktere — kufiri PostgreSQL
        Assert.Equal(max, ParseDb(max).Database);
        Assert.Throws<PlatformDatabaseConfigurationException>(() => ParseDb(max + "b"));
    }

    [Theory]
    [InlineData("")]                        // bosh
    [InlineData("1data")]                   // nis me shifer — duhet shkronje
    [InlineData("_data")]                   // nis me underscore — duhet shkronje
    [InlineData("Idata_Platform")]          // uppercase jashte allowlist-it
    [InlineData("idata platform")]          // whitespace
    [InlineData(" idata_platform")]         // whitespace ne fillim
    [InlineData("idata_platform\t")]        // tab
    [InlineData("idata_platform\n")]        // newline
    [InlineData("idata_platform\u0007")]     // control char (BEL)
    [InlineData("\"idata_platform\"")]      // thonjeza
    [InlineData("'idata_platform'")]        // thonjeza teke
    [InlineData("idata=platform")]          // '=' — celes connection string-u
    [InlineData("idata;platform")]          // ';' — separator connection string-u
    [InlineData("idata_platform_test_alias;Database=idata_platform_test_target")] // injection-i origjinal
    [InlineData("idata_platform_test_alias;Host=evil.invalid")]
    [InlineData("idata_platform_test_alias;Username=evil")]
    [InlineData("idata_platform_test_alias;Timeout=30")]
    public void Emrat_jashte_allowlist_it_refuzohen_fail_closed(string database)
    {
        var ex = Assert.Throws<PlatformDatabaseConfigurationException>(() => ParseDb(database));
        Assert.Equal("database", ex.Field);
        // Zero echo i vleres se papranuar ne mesazh (asnje leak i input-it).
        Assert.DoesNotContain("evil", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(";", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("host", "local host", "5432", "idata")]
    [InlineData("host", "local;host", "5432", "idata")]
    [InlineData("port", "localhost", "abc", "idata")]
    [InlineData("port", "localhost", "0", "idata")]
    [InlineData("port", "localhost", "65536", "idata")]
    [InlineData("username", "localhost", "5432", "id;ata")]
    [InlineData("username", "localhost", "5432", "id ata")]
    public void Host_port_username_jashte_allowlist_it_refuzohen(string field, string host, string port, string user)
    {
        var ex = Assert.Throws<PlatformDatabaseConfigurationException>(
            () => PlatformDatabaseConnection.Parse(host, port, user, "idata_platform"));
        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void Connection_string_i_ndertuar_ka_sakte_nje_celes_database_me_vleren_e_validuar()
    {
        // Boundary test: string-u i prodhuar ri-parsohet me Npgsql — Database
        // efektiv eshte EKZAKT emri i validuar, pa celesa te dyte/te mbivendosur.
        var settings = PlatformDatabaseConnection.Parse("localhost", "5455", "idata", "idata_platform_test_boundary");
        var parsed = new NpgsqlConnectionStringBuilder(settings.ToConnectionString());

        Assert.Equal("idata_platform_test_boundary", parsed.Database);
        Assert.Equal("localhost", parsed.Host);
        Assert.Equal(5455, parsed.Port);
        Assert.Equal("idata", parsed.Username);
        // Nje dukuri e vetme e celesit Database ne tekstin final.
        var text = settings.ToConnectionString();
        Assert.Equal(1, text.Split("Database=").Length - 1);
    }
}
