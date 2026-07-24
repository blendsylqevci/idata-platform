namespace Idata.Platform.Modules.AccessControl.Application;

/// <summary>
/// Kontrata PROVIDER-NEUTRAL e aktorit (ADR-028 OPEN — asnjë provider/store/
/// token konkret). Tenant-i aktiv vjen VETËM nga ky kontekst i besuar i
/// shtresës së autentikimit — KURRË nga request body/query: kërkesa nuk
/// zgjedh dot arbitrarisht `business_id`.
/// </summary>
public sealed record ActorContext(Guid? UserId, Guid? ActiveBusinessId, string? CorrelationId)
{
    public static readonly ActorContext Anonymous = new(null, null, null);
}

/// <summary>Grants e aktorit brenda tenant-it aktiv — burim i vetëm DB-ja.</summary>
public sealed record ActorGrants(
    Guid MembershipId,
    Guid RoleId,
    IReadOnlyCollection<string> PermissionKeys);
