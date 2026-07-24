namespace Idata.Platform.DbMigrator;

/// <summary>Arsyet fikse të refuzimit — kurrë connection string/path/kredencial në output.</summary>
public enum DestructiveResetVerdict
{
    Allowed,
    EnvironmentNotAllowed,
    OptInMissing,
    TargetDatabaseNotSynthetic,
}

/// <summary>
/// Guard PURE (pa asnjë I/O) i operacionit destruktiv down-to-zero — vlerësohet
/// PARA krijimit të DbContext-it, PARA çdo connection-i dhe PARA çdo query.
/// Lejohet VETËM kur plotësohen TË TRIA kushtet:
///  1. DOTNET_ENVIRONMENT = Development ose CI;
///  2. opt-in EKZAKT i deklaruar (vlera e plotë, jo substring);
///  3. target DB qartësisht sintetike/efemere (idata_platform_ci ose
///     prefiks idata_platform_test_).
/// </summary>
public static class DestructiveResetGuard
{
    public const string OptInVariable = "IDATA_ALLOW_DESTRUCTIVE_MIGRATION_RESET";
    public const string OptInValue = "YES_I_UNDERSTAND_DATA_WILL_BE_DELETED";
    public const string CiDatabaseName = "idata_platform_ci";
    public const string TestDatabasePrefix = "idata_platform_test_";

    public static DestructiveResetVerdict Evaluate(
        string? dotnetEnvironment,
        string? optInValue,
        string targetDatabaseName)
    {
        if (dotnetEnvironment is not ("Development" or "CI"))
        {
            return DestructiveResetVerdict.EnvironmentNotAllowed;
        }

        if (!string.Equals(optInValue, OptInValue, StringComparison.Ordinal))
        {
            return DestructiveResetVerdict.OptInMissing;
        }

        var synthetic = string.Equals(targetDatabaseName, CiDatabaseName, StringComparison.Ordinal)
            || targetDatabaseName.StartsWith(TestDatabasePrefix, StringComparison.Ordinal);
        return synthetic
            ? DestructiveResetVerdict.Allowed
            : DestructiveResetVerdict.TargetDatabaseNotSynthetic;
    }
}
