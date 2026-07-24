namespace Idata.Platform.Modules.PlatformStatus.Domain;

/// <summary>Gjendja e arritshmërisë së target database-it (PostgreSQL).</summary>
public enum DatabaseState
{
    /// <summary>Probe-i u lidh dhe ekzekutoi query-n e gatishmërisë.</summary>
    Ready,

    /// <summary>Probe-i dështoi — gjendje e kontrolluar fail-safe, kurrë exception drejt thirrësit.</summary>
    Unavailable,
}

/// <summary>
/// Gjendja e një capability të platformës në Bootstrap-in pre-F1. Në këtë
/// increment ekziston VETËM gjendja e pakonfiguruar eksplicite — asnjë no-op
/// që maskohet si enforcement production; konfigurimi real vjen në F1.
/// </summary>
public enum CapabilityState
{
    /// <summary>Capability e deklaruar por e pakonfiguruar — pritet F1 Foundation.</summary>
    NotConfiguredF1Pending,
}

/// <summary>Capabilities e deklaruara të Bootstrap-it (contracts/smoke wiring, jo production).</summary>
public sealed record BootstrapCapabilities(
    CapabilityState Identity,
    CapabilityState Audit,
    CapabilityState Outbox,
    CapabilityState Observability)
{
    /// <summary>Gjendja e vetme e vlefshme e Bootstrap-it: gjithçka NOT_CONFIGURED / F1_PENDING.</summary>
    public static BootstrapCapabilities F1Pending { get; } = new(
        CapabilityState.NotConfiguredF1Pending,
        CapabilityState.NotConfiguredF1Pending,
        CapabilityState.NotConfiguredF1Pending,
        CapabilityState.NotConfiguredF1Pending);
}

/// <summary>Raporti i statusit të platformës — pa timestamps dhe pa të dhëna sensitive.</summary>
public sealed record PlatformStatusReport(
    PlatformHealth Status,
    DatabaseState Database,
    BootstrapCapabilities Capabilities)
{
    public static PlatformStatusReport From(DatabaseState database) => new(
        database == DatabaseState.Ready ? PlatformHealth.Ok : PlatformHealth.Degraded,
        database,
        BootstrapCapabilities.F1Pending);
}

/// <summary>Shëndeti i përgjithshëm i derivuar nga gjendjet e komponentëve.</summary>
public enum PlatformHealth
{
    Ok,
    Degraded,
}
