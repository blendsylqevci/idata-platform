using Idata.Platform.Modules.AccessControl.Domain;

namespace Idata.Platform.Modules.AccessControl.Application;

/// <summary>
/// Porta READ e grants: membership AKTIV i (business, user) — kërkohet
/// gjithashtu që user-i DHE business-i të jenë active (lifecycle nga DB,
/// jo nga claims). `null` = pa grants → deny-by-default. KUJDES: ky lexim
/// shërben VETËM për deny-uniform/formësim përgjigjeje; autoriteti FINAL i
/// mutacionit është faza transaksionale authority-lock/validation/mutation e
/// <see cref="IMembershipRoleUnitOfWork.TryMutateRoleAsync"/>.
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
/// Arsyeja e refuzimit e vendosur NGA faza transaksionale authority-lock/
/// validation/mutation (jo nga pre-check-u). Formëson kontratën publike pa
/// zbuluar ekzistencën/natyrën e burimeve: `IsSystem` dhe rol/membership
/// inekzistent japin TË NJËJTIN rezultat (parity).
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
    /// <summary>
    /// `expectedVersion` ≠ version aktual i lexuar NËN LOCK → kandidat për
    /// 409 CAS. KUJDES: humbësi i një gare same-Idempotency-Key merr
    /// gjithashtu këtë denial — prandaj handler-i bën NJË rilexim
    /// deterministik të receipt-it pas rollback-ut para se ta raportojë
    /// si CONCURRENCY_CONFLICT.
    /// </summary>
    VersionConflict,
    /// <summary>
    /// Contention/dështim transient i DB-së (deadlock 40P01, serialization
    /// 40001, lock timeout 55P03, statement timeout server-side 57014) —
    /// NUK është CAS dhe NUK raportohet si version mismatch. Rollback i
    /// plotë → 503 PLATFORM_UNAVAILABLE stabil, zero retry automatik.
    /// Cancellation e klientit NUK klasifikohet këtu (propagohet si
    /// OperationCanceledException).
    /// </summary>
    TransientFailure,
}

/// <summary>Kërkesa e mutacionit — çdo predikat rivlerësohet në DB nën lock.</summary>
public sealed record AtomicRoleMutation(
    Guid BusinessId,
    Guid ActorUserId,
    Guid MembershipId,
    Guid RoleId,
    long ExpectedVersion);

/// <summary>Rezultati i fazës së mutacionit (version i ri + roli paraardhës REAL).</summary>
public sealed record AtomicRoleMutationResult(
    bool Applied,
    long NewVersion,
    Guid PreviousRoleId,
    RoleMutationDenial Denial);

/// <summary>
/// Porta TRANSAKSIONALE e komandës `membership.role.update`.
/// <see cref="TryMutateRoleAsync"/> është një fazë transaksionale
/// authority-lock/validation/mutation: lock-on faktet mutable të
/// autorizimit të LISTUARA në protokoll (business, memberships, users,
/// requested role, permission, role_permission), rivlerëson predikatet mbi
/// vlerat nën lock dhe vetëm pastaj mutaton. Garancia mbulon VETËM faktet e
/// listuara e të lock-uara; një fakt i ri autorizimi duhet shtuar në lock
/// protocol + lock order + regression matrix. Rezultati bëhet i
/// dukshëm/durable VETËM në commit-in e transaction-it (pas
/// <see cref="SaveAsync"/>), kur mutation + audit + receipt janë ruajtur
/// bashkë; çdo refuzim/konflikt → zero mutation, zero increment, zero audit,
/// zero receipt. NUK pretendohet atomicitet universal nga forma e një
/// statement-i dhe as deadlock-freedom universale për writer-a të tjerë.
/// </summary>
public interface IMembershipRoleUnitOfWork
{
    Task<CommandReceipt?> FindReceiptAsync(
        Guid businessId, string operationKey, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Faza transaksionale authority-lock / validation / mutation.</summary>
    Task<AtomicRoleMutationResult> TryMutateRoleAsync(
        AtomicRoleMutation mutation, CancellationToken cancellationToken);

    void AddAudit(AuditLogEntry entry);

    void AddReceipt(CommandReceipt receipt);

    /// <summary>Shkruan audit+receipt dhe COMMIT-on të gjithë transaction-in.</summary>
    Task<CommandSaveOutcome> SaveAsync(CancellationToken cancellationToken);

    /// <summary>Rrëzon transaction-in (refuzim ose konflikt) — zero efekt.</summary>
    Task DiscardAsync(CancellationToken cancellationToken);
}
