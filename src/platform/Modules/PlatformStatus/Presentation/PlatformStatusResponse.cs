using Idata.Platform.Modules.PlatformStatus.Domain;

namespace Idata.Platform.Modules.PlatformStatus.Presentation;

/// <summary>
/// Kontrata HTTP e statusit — string-e stabile machine-readable, pa
/// timestamps nondeterministic, pa path-e host-i dhe pa të dhëna sensitive.
/// </summary>
public sealed record PlatformStatusResponse(
    string Status,
    string ApiVersion,
    string Database,
    PlatformCapabilitiesResponse Capabilities)
{
    /// <summary>Versioni i kontratës së API-t (statik/determinist).</summary>
    public const string CurrentApiVersion = "platform-v1";

    public static PlatformStatusResponse From(PlatformStatusReport report) => new(
        report.Status switch
        {
            PlatformHealth.Ok => "OK",
            _ => "DEGRADED",
        },
        CurrentApiVersion,
        report.Database switch
        {
            DatabaseState.Ready => "READY",
            _ => "UNAVAILABLE",
        },
        PlatformCapabilitiesResponse.From(report.Capabilities));
}

/// <summary>
/// Gjendjet e capabilities të Bootstrap-it — eksplicite NOT_CONFIGURED /
/// F1_PENDING; asnjë "allow all" i maskuar si enforcement production.
/// </summary>
public sealed record PlatformCapabilitiesResponse(
    string Identity,
    string Audit,
    string Outbox,
    string Observability)
{
    internal const string NotConfiguredF1Pending = "NOT_CONFIGURED_F1_PENDING";

    public static PlatformCapabilitiesResponse From(BootstrapCapabilities capabilities) => new(
        Map(capabilities.Identity),
        Map(capabilities.Audit),
        Map(capabilities.Outbox),
        Map(capabilities.Observability));

    private static string Map(CapabilityState state) => state switch
    {
        CapabilityState.NotConfiguredF1Pending => NotConfiguredF1Pending,
        _ => NotConfiguredF1Pending,
    };
}
