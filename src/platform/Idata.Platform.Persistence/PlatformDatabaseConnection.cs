using Npgsql;

namespace Idata.Platform.Persistence;

/// <summary>
/// Konfigurimi I VETËM i validuar i target PostgreSQL-it (dev/CI trust,
/// pa kredenciale — secrets = gate ADR-026/F1). Vlerat nga env kalojnë
/// VALIDIM FAIL-CLOSED përpara çdo përdorimi: karaktere jashtë allowlist-it
/// (p.sh. `;`, `=`, hapësira, thonjëza) REFUZOHEN — asnjë mundësi injection
/// i çelësave shtesë të connection string-ut. Guard-i destruktiv dhe lidhja
/// reale konsumojnë të NJËJTIN objekt të validuar; string-u final ndërtohet
/// me NpgsqlConnectionStringBuilder (escaping korrekt si shtresë e dytë).
/// </summary>
public sealed record PlatformDatabaseSettings(string Host, int Port, string Username, string Database)
{
    public string ToConnectionString() => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Username = Username,
        Database = Database,
        Timeout = 3,
    }.ConnectionString;
}

/// <summary>Refuzim fail-closed i konfigurimit — kodi i fushës, KURRË vlera e papranuar.</summary>
public sealed class PlatformDatabaseConfigurationException(string field)
    : Exception($"Konfigurim i pavlefshem i target database-it: fusha '{field}' nuk kalon validimin e allowlist-it.")
{
    public string Field { get; } = field;
}

public static class PlatformDatabaseConnection
{
    public const string DefaultDatabaseName = "idata_platform";

    /// <summary>
    /// Parse PURE + validim fail-closed (i testueshëm pa env):
    ///  - host: shkronja/shifra/pikë/vizë (hostname ose IP);
    ///  - port: numerik 1–65535;
    ///  - username: shkronja/shifra/underscore/vizë;
    ///  - database: identifikues i vogël PostgreSQL — nis me shkronjë, [a-z0-9_], ≤63.
    /// Çdo devijim → <see cref="PlatformDatabaseConfigurationException"/> —
    /// pa lidhje, pa mutacion dhe pa echo të vlerës së papranuar.
    /// </summary>
    public static PlatformDatabaseSettings Parse(string host, string port, string username, string database)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(host, @"^[A-Za-z0-9.-]+\z"))
        {
            throw new PlatformDatabaseConfigurationException("host");
        }

        if (!int.TryParse(port, out var portNumber) || portNumber is < 1 or > 65535)
        {
            throw new PlatformDatabaseConfigurationException("port");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[A-Za-z0-9_-]+\z"))
        {
            throw new PlatformDatabaseConfigurationException("username");
        }

        // Emër PostgreSQL i lejuar në Bootstrap: ASCII lowercase, [a-z0-9_],
        // nis me shkronjë, maksimumi 63 karaktere. `;`/`=`/whitespace/quote/
        // control chars bien automatikisht jashtë allowlist-it (anti-injection).
        if (!System.Text.RegularExpressions.Regex.IsMatch(database, @"^[a-z][a-z0-9_]{0,62}\z"))
        {
            throw new PlatformDatabaseConfigurationException("database");
        }

        return new PlatformDatabaseSettings(host, portNumber, username, database);
    }

    /// <summary>Konfigurimi i validuar nga env — i njëjti burim për guard dhe lidhje.</summary>
    public static PlatformDatabaseSettings SettingsFromEnvironment() => Parse(
        Environment.GetEnvironmentVariable("PGHOST") ?? "localhost",
        Environment.GetEnvironmentVariable("PGPORT") ?? "5432",
        Environment.GetEnvironmentVariable("PGUSER") ?? "idata",
        Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE") ?? DefaultDatabaseName);

    /// <summary>Emri i DB-së nga i NJËJTI konfigurim i validuar (për guard-e PURE).</summary>
    public static string DatabaseNameFromEnvironment() => SettingsFromEnvironment().Database;

    public static string FromEnvironment() => SettingsFromEnvironment().ToConnectionString();
}
