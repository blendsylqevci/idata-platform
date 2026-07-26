namespace Idata.Platform.Modules.AccessControl.Domain;

/// <summary>
/// Entitetet minimale të F1.1 (skema `platform`, vetëm vlera sintetike).
/// GLOBAL/reference: <see cref="AppUser"/> + <see cref="Permission"/>.
/// TENANT ROOT: `business` (nuk mban vetë `business_id`).
/// TENANT-SCOPED (`business_id NOT NULL` DIREKT — rregulli i Data Model §
/// "Multi-tenant") — EKZAKT 5 child tables: role, business_membership,
/// role_permission, audit_log, command_receipt.
/// Composite FK cross-tenant = vendim eksplicit sipas ENG-003/PoC-1:
/// fëmija lidhet me prindin përmes çelësit të përbërë
/// `(business_id, id)` — DB refuzon lidhje cross-tenant NË NIVEL constraint-i.
/// Rolet në F1.1 janë VETËM tenant-scoped (rolet globale = deferred);
/// `is_system` = rol tenant-scoped i menaxhuar nga sistemi, JO rol global.
/// </summary>
public static class EntityStatus
{
    public const string Active = "active";
    public const string Disabled = "disabled";
}

/// <summary>
/// Identity projection GLOBAL provider-neutral (ADR-028 OPEN). Target-i
/// canonical mbetet `id, email, name, mfa_enabled, status`; email/name/MFA/
/// provider binding SHTYHEN në F1.1 (deferral i dokumentuar — fushat NUK
/// hiqen nga target-i dhe Identity NUK deklarohet READY).
/// </summary>
public sealed class AppUser
{
    public Guid Id { get; set; }
    public string Status { get; set; } = EntityStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Tenant root — çdo entitet tenant-scoped varet nga ky id.</summary>
public sealed class Business
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = EntityStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Rol TENANT-SCOPED (`business_id NOT NULL`); rolet globale = deferred.</summary>
public sealed class Role
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Reference GLOBALE: feature key + veprim nga vokabulari canonical i
/// RBAC Matrix (R/C/U/X/P/A dhe, vetëm kur kërkohet, E/Cl/Ro).
/// </summary>
public sealed class Permission
{
    public Guid Id { get; set; }
    public string FeatureKey { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Anëtarësia e user-it në një business me NJË rol tenant-scoped.
/// Composite FK `(business_id, role_id) → role(business_id, id)` (ENG-003).
/// </summary>
public sealed class BusinessMembership
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    /// <summary>Version DB-backed (CAS optimistic) — rritet vetëm nga update fitues.</summary>
    public long Version { get; set; }
    public string Status { get; set; } = EntityStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Lidhja rol→permission me `business_id` DIREKT + composite FK te role —
/// një rresht cross-tenant refuzohet nga vetë DB-ja.
/// </summary>
public sealed class RolePermission
{
    public Guid BusinessId { get; set; }
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}

/// <summary>
/// Audit BUSINESS-SCOPED append-only (ENG-004 fallback AKTIV — group-level
/// events shtyhen). Before/After = JSON i REDAKTUAR (vetëm çelësa teknikë,
/// p.sh. role_id) — KURRË PII, sekrete apo payload i papërpunuar.
/// Append-only vlen për DML të zakonshëm nën rol të kufizuar runtime
/// (triggers refuzojnë UPDATE/DELETE/TRUNCATE); NUK pretendohet tamper-proof
/// ndaj owner/superuser — provisioning i rolit production mbetet OPEN.
/// </summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid UserId { get; set; }
    public string Entity { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    /// <summary>Lidhja audit → command_receipt (DB-enforced, tenant-safe).</summary>
    public Guid CommandReceiptId { get; set; }
    /// <summary>Correlation teknik server-generated (jo nga klienti; zero PII).</summary>
    public Guid CorrelationId { get; set; }
    public DateTimeOffset EventAt { get; set; }
}

/// <summary>
/// Receipt idempotence për komandat e shkruara — unik në
/// `(business_id, operation_key, idempotency_key)`. Fingerprint-i lidh
/// key-n me target/payload/actor konkret; rezultati ruhet i tipizuar dhe
/// i REDAKTUAR (kurrë payload i papërpunuar, PII apo sekrete).
/// </summary>
public sealed class CommandReceipt
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string ResultStatus { get; set; } = string.Empty;
    public string ResultPayload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
