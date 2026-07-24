using Idata.Platform.Modules.AccessControl.Domain;

namespace Idata.Platform.Modules.AccessControl.Application;

/// <summary>
/// Porta READ e grants: membership AKTIV i (business, user) — kërkohet
/// gjithashtu që user-i DHE business-i të jenë active (lifecycle nga DB,
/// jo nga claims). `null` = pa grants → deny-by-default.
/// </summary>
public interface IAccessControlStore
{
    Task<ActorGrants?> GetGrantsAsync(Guid businessId, Guid userId, CancellationToken cancellationToken);
}

/// <summary>Rezultati i save-it transaksional të komandës.</summary>
public enum CommandSaveOutcome
{
    Saved,
    /// <summary>Unique violation në receipt — një kërkesë konkurruese fitoi garën e idempotencës.</summary>
    ReceiptConflict,
    /// <summary>Concurrency token i membership-it lëvizi mes read-it dhe save-it (CAS race).</summary>
    ConcurrencyConflict,
}

/// <summary>
/// Porta TRANSAKSIONALE e komandës `membership.role.update`: mutation +
/// audit + receipt ruhen me NJË save atomik (një transaction DB) — rollback
/// i cilitdo prej të treve rrëzon të gjithë; asnjë receipt "completed" i
/// rremë dhe asnjë mutation pa audit nuk mund të mbetet. Membership-i i
/// gjetur mbahet i TRACKED brenda unit-of-work-ut; ndryshimi i rolit +
/// version-it aplikohet nga <see cref="ApplyRoleChange"/> (CAS DB-backed).
/// </summary>
public interface IMembershipRoleUnitOfWork
{
    Task<CommandReceipt?> FindReceiptAsync(
        Guid businessId, string operationKey, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Membership AKTIV me (business_id, membership_id) DHE user target AKTIV;
    /// kthen snapshot-in domain (incl. Version/RoleId) dhe e mban rekordin të
    /// tracked për mutation. `null` = inekzistent/disabled/user-disabled
    /// (i padallueshëm sipas kontratës së target lookup-it).
    /// </summary>
    Task<BusinessMembership?> FindActiveMembershipAsync(
        Guid businessId, Guid membershipId, CancellationToken cancellationToken);

    /// <summary>Kërkim DIREKT me (business_id, role_id) — kurrë globalisht.</summary>
    Task<Role?> FindRoleAsync(Guid businessId, Guid roleId, CancellationToken cancellationToken);

    /// <summary>Aplikon rolin e ri + version-in e ri te membership-i i tracked (CAS).</summary>
    void ApplyRoleChange(Guid newRoleId, long newVersion);

    void AddAudit(AuditLogEntry entry);

    void AddReceipt(CommandReceipt receipt);

    Task<CommandSaveOutcome> SaveAsync(CancellationToken cancellationToken);
}
