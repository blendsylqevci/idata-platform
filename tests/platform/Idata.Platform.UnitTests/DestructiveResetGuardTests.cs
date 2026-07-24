using Idata.Platform.DbMigrator;
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
