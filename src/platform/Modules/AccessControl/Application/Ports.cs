using Idata.Platform.Modules.AccessControl.Domain;

namespace Idata.Platform.Modules.AccessControl.Application;

/// <summary>
/// Porta READ e grants: membership AKTIV i (business, user) — kërkohet
/// gjithashtu që user-i DHE business-i të jenë active (lifecycle nga DB,
/// jo nga claims). `null` = pa grants → deny-by-default. KUJDES: ky lexim
/// shërben VETËM për deny-uniform/formësim përgjigjeje; autoriteti FINAL i
/// mutacionit është linearization point-i i <see cref="IMembershipRoleUnitOfWork.TryMutateRoleAsync"/>.
/// </summary>
public interface IAccessControlStore
{
    Task<ActorGrants?> GetGrantsAsync(Guid businessId, Guid userId, CancellationToken cancellationToken);
}

/// <summary>Rezultati i save-it transaksional të komandës.</summary>
public enum CommandSaveOutcome
{
    Saved,
    /// <summary>
    /// Unique violation në receipt — një kërkesë konkurruese fitoi garën e
    /// idempotencës. I VETMI konflikt i arritshëm në save: mutacioni i
    /// membership-it bëhet me SQL të guarduar nën lock (jo me EF change
    /// tracking), ndaj `DbUpdateConcurrencyException` nuk ndodh këtu.
    /// </summary>
    ReceiptConflict,
}

/// <summary>
/// Arsyeja e refuzimit e vendosur NGA linearization point-i atomik (jo nga
/// pre-check-u). Formëson kontratën publike pa zbuluar ekzistencën/natyrën
/// e burimeve: `IsSystem` dhe rol/membership inekzistent japin TË NJËJTIN
/// rezultat (parity).
/// </summary>
public enum RoleMutationDenial
{
    None,
    /// <summary>Business/user/membership i aktorit jo-aktiv ose permission i revokuar → 403 uniform.</summary>
    ActorNotAuthorized,
    /// <summary>Target membership/user/rol i pa-caktueshëm (mungon, disabled, cross-tenant, IsSystem) → 404 parity.</summary>
    TargetNotAssignable,
    /// <summary>Aktori provoi të ndryshojë rolin e VET (krahasim me USER ID) → deny stabil 403.</summary>
    SelfRoleChange,
    /// <summary>`expectedVersion` ≠ version aktual në momentin e linearizimit → 409.</summary>
    VersionConflict,
}

/// <summary>Kërkesa e mutacionit — çdo predikat rivlerësohet në DB, atomikisht.</summary>
public sealed record AtomicRoleMutation(
    Guid BusinessId,
    Guid ActorUserId,
    Guid MembershipId,
    Guid RoleId,
    long ExpectedVersion);

/// <summary>Rezultati i linearization point-it (version i ri + roli paraardhës REAL).</summary>
public sealed record AtomicRoleMutationResult(
    bool Applied,
    long NewVersion,
    Guid PreviousRoleId,
    RoleMutationDenial Denial);

/// <summary>
/// Porta TRANSAKSIONALE e komandës `membership.role.update` me NJË
/// LINEARIZATION POINT DB-backed: <see cref="TryMutateRoleAsync"/> rivlerëson
/// ATOMIKISHT (nën row lock, në të njëjtin transaction) lifecycle-in e
/// business/actor-user/actor-membership, permission-in aktual të aktorit,
/// lifecycle-in e target membership/user, ndalimin self-role (me USER ID),
/// përkatësinë+`IsSystem=false` të rolit të kërkuar dhe CAS-in
/// (`version` + `status='active'`). Mutation + version++ + audit + receipt
/// commit-ohen BASHKË; çdo predikat i dështuar ose konflikt → zero mutation,
/// zero increment, zero audit, zero receipt.
/// </summary>
public interface IMembershipRoleUnitOfWork
{
    Task<CommandReceipt?> FindReceiptAsync(
        Guid businessId, string operationKey, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>LINEARIZATION POINT — autoriteti i vetëm dhe përfundimtar.</summary>
    Task<AtomicRoleMutationResult> TryMutateRoleAsync(
        AtomicRoleMutation mutation, CancellationToken cancellationToken);

    void AddAudit(AuditLogEntry entry);

    void AddReceipt(CommandReceipt receipt);

    /// <summary>Shkruan audit+receipt dhe COMMIT-on të gjithë transaction-in.</summary>
    Task<CommandSaveOutcome> SaveAsync(CancellationToken cancellationToken);

    /// <summary>Rrëzon transaction-in (refuzim ose konflikt) — zero efekt.</summary>
    Task DiscardAsync(CancellationToken cancellationToken);
}
