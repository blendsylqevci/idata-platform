namespace Idata.Platform.Modules.PlatformStatus.Infrastructure;

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

    /// <summary>Vetëm emri i DB-së nga env — për guard-e PURE para çdo lidhjeje.</summary>
    public static string DatabaseNameFromEnvironment()
        => Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE") ?? DefaultDatabaseName;

    public static string FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("PGHOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";
        var user = Environment.GetEnvironmentVariable("PGUSER") ?? "idata";
        var database = DatabaseNameFromEnvironment();

        return $"Host={host};Port={port};Username={user};Database={database};Timeout=3";
    }
}
