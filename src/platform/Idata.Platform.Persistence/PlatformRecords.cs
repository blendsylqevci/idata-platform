namespace Idata.Platform.Persistence;

/// <summary>
/// Storage records NEUTRALE të platform DB-së — POCO-t e persistencës që
/// EF Core i mapon te tabelat e schema-s `platform`. Persistence është
/// migration owner-i i vetëm dhe NUK varet nga asnjë Domain moduli; çdo
/// modul (p.sh. AccessControl) mapon këto records ↔ tipet e veta Domain/
/// Application në shtresën e vet Infrastructure. Zero të dhëna reale.
/// </summary>
public static class RecordStatus
{
    public const string Active = "active";
    public const string Disabled = "disabled";
}

/// <summary>Tabelë teknike minimale — provon path-in schema/migration.</summary>
public sealed class MigrationSmokeCheck
{
    public int Id { get; set; }
}

public sealed class AppUserRecord
{
    public Guid Id { get; set; }
    public string Status { get; set; } = RecordStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BusinessRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = RecordStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RoleRecord
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PermissionRecord
{
    public Guid Id { get; set; }
    public string FeatureKey { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BusinessMembershipRecord
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    /// <summary>Concurrency token DB-backed (CAS) — rritet vetëm nga update fitues.</summary>
    public long Version { get; set; }
    public string Status { get; set; } = RecordStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RolePermissionRecord
{
    public Guid BusinessId { get; set; }
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}

public sealed class AuditLogRecord
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid UserId { get; set; }
    public string Entity { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    /// <summary>Lidhja DB-enforced audit → command_receipt (tenant-safe).</summary>
    public Guid CommandReceiptId { get; set; }
    /// <summary>Correlation teknik server-generated (jo nga klienti; zero PII).</summary>
    public Guid CorrelationId { get; set; }
    public DateTimeOffset EventAt { get; set; }
}

public sealed class CommandReceiptRecord
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
