namespace Idata.Platform.Api;

/// <summary>
/// Connection string i TARGET database-it për dev/CI LOKAL me trust auth —
/// pa asnjë kredencial (parity me shërbimin Postgres të CI-së). Konfigurimi
/// i kredencialeve/secrets reale është gate i ADR-026/F1 dhe NUK zgjidhet
/// në Bootstrap. Database-i logjik i target-it është i VEÇANTË nga legacy
/// (`idata_platform` vs `idata`); zero dual-write.
/// </summary>
public static class PlatformDatabaseConnection
{
    public const string DefaultDatabaseName = "idata_platform";

    public static string FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("PGHOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";
        var user = Environment.GetEnvironmentVariable("PGUSER") ?? "idata";
        var database = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE") ?? DefaultDatabaseName;

        return $"Host={host};Port={port};Username={user};Database={database};Timeout=3";
    }
}
