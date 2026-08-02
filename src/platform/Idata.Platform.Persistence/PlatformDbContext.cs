using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace Idata.Platform.Persistence;

/// <summary>
/// DbContext i target database-it — EF Core është MIGRATION OWNER I VETËM
/// (ADR-024 §"Një migration owner"). Schema module-owned `platform`;
/// historia e migrimeve jeton brenda të njëjtit boundary. Modelohen VETËM
/// storage records NEUTRALE (POCO persistence) — Persistence NUK varet nga
/// asnjë Domain moduli; çdo modul mapon records ↔ tipet e veta në shtresën
/// e vet Infrastructure. Zero të dhëna reale.
///
/// ADR-028 (Increment B.1): konteksti derivon nga varianti 5-generic i
/// <see cref="IdentityUserContext{TUser, TKey, TUserClaim, TUserLogin, TUserToken}"/>
/// ROLELESS (JO IdentityDbContext me role) me çelësa Guid dhe claim type
/// CUSTOM (<see cref="UserClaimRecord"/> — çelës UUID; zero persisted int
/// IDs ABSOLUT) — schema `identity` mban vetëm store foundation (Identity +
/// OpenIddict Core/EF); ZERO tabela rolesh, ZERO FK cross-schema drejt
/// `platform`, ZERO projection para B.3.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options)
    : IdentityUserContext<UserAccountRecord, Guid, UserClaimRecord, IdentityUserLogin<Guid>, IdentityUserToken<Guid>>(options)
{
    public const string Schema = "platform";
    public const string IdentitySchema = "identity";

    public DbSet<MigrationSmokeCheck> MigrationSmokeChecks => Set<MigrationSmokeCheck>();

    /// <summary>
    /// `platform.app_user` (projection boundary i F1.1) — i riemërtuar
    /// `PlatformAppUsers` që të MOS ngatërrohet KURRË me inherited
    /// <c>Users</c> (`identity.user_account`, store-i i Identity-t).
    /// AccessControl përdor VETËM këtë; `Users` i takon vetëm Identity
    /// wiring/store (guard-test diskriminues në ArchitectureTests).
    /// </summary>
    public DbSet<AppUserRecord> PlatformAppUsers => Set<AppUserRecord>();
    public DbSet<BusinessRecord> Businesses => Set<BusinessRecord>();
    public DbSet<RoleRecord> Roles => Set<RoleRecord>();
    public DbSet<PermissionRecord> Permissions => Set<PermissionRecord>();
    public DbSet<BusinessMembershipRecord> BusinessMemberships => Set<BusinessMembershipRecord>();
    public DbSet<RolePermissionRecord> RolePermissions => Set<RolePermissionRecord>();
    public DbSet<AuditLogRecord> AuditLog => Set<AuditLogRecord>();
    public DbSet<CommandReceiptRecord> CommandReceipts => Set<CommandReceiptRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ADR-028 B.1 (rregull i detyrueshëm): mapping-u bazë ROLELESS i
        // Identity-t aplikohet I PARI; remap-et tona (schema/tabela/kolona)
        // e mbishkruajnë default-in PAS tij.
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        // OpenIddict Core/EF me çelësa Guid — regjistrohet KËTU (Persistence
        // = model/migration owner i vetëm) që modeli të jetë IDENTIK në çdo
        // vend ku ndërtohen options (API host, DbMigrator, design-time,
        // teste) — ekuivalenti i dokumentuar i `options.UseOpenIddict<Guid>()`.
        modelBuilder.UseOpenIddict<Guid>();

        modelBuilder.Entity<MigrationSmokeCheck>(entity =>
        {
            entity.ToTable("migration_smoke_check");
            entity.HasKey(check => check.Id);
        });

        // ── AccessControl (F1.1) — snake_case, UUID keys, timestamptz UTC,
        //    statuse me CHECK constraints, zero float, zero PII. ──

        modelBuilder.Entity<AppUserRecord>(entity =>
        {
            entity.ToTable("app_user", table => table.HasCheckConstraint(
                "ck_app_user_status", "status IN ('active','disabled')"));
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasColumnName("id");
            entity.Property(user => user.Status).HasColumnName("status").IsRequired();
            entity.Property(user => user.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<BusinessRecord>(entity =>
        {
            entity.ToTable("business", table => table.HasCheckConstraint(
                "ck_business_status", "status IN ('active','disabled')"));
            entity.HasKey(business => business.Id);
            entity.Property(business => business.Id).HasColumnName("id");
            entity.Property(business => business.Name).HasColumnName("name").IsRequired();
            entity.Property(business => business.Status).HasColumnName("status").IsRequired();
            entity.Property(business => business.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<RoleRecord>(entity =>
        {
            entity.ToTable("role");
            entity.HasKey(role => role.Id);
            entity.Property(role => role.Id).HasColumnName("id");
            entity.Property(role => role.BusinessId).HasColumnName("business_id");
            entity.Property(role => role.Name).HasColumnName("name").IsRequired();
            entity.Property(role => role.IsSystem).HasColumnName("is_system");
            entity.Property(role => role.CreatedAt).HasColumnName("created_at");
            entity.HasOne<BusinessRecord>().WithMany().HasForeignKey(role => role.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            // Çelës i përbërë UNIK (business_id, id) — target i composite FK-ve
            // cross-tenant (vendim eksplicit ENG-003/PoC-1).
            entity.HasAlternateKey(role => new { role.BusinessId, role.Id });
            entity.HasIndex(role => new { role.BusinessId, role.Name }).IsUnique();
        });

        modelBuilder.Entity<PermissionRecord>(entity =>
        {
            entity.ToTable("permission", table => table.HasCheckConstraint(
                "ck_permission_action",
                "action IN ('R','C','U','X','P','A','E','Cl','Ro')"));
            entity.HasKey(permission => permission.Id);
            entity.Property(permission => permission.Id).HasColumnName("id");
            entity.Property(permission => permission.FeatureKey).HasColumnName("feature_key").IsRequired();
            entity.Property(permission => permission.Action).HasColumnName("action").IsRequired();
            entity.Property(permission => permission.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(permission => new { permission.FeatureKey, permission.Action }).IsUnique();
        });

        modelBuilder.Entity<BusinessMembershipRecord>(entity =>
        {
            entity.ToTable("business_membership", table => table.HasCheckConstraint(
                "ck_business_membership_status", "status IN ('active','disabled')"));
            entity.HasKey(membership => membership.Id);
            entity.Property(membership => membership.Id).HasColumnName("id");
            entity.Property(membership => membership.BusinessId).HasColumnName("business_id");
            entity.Property(membership => membership.UserId).HasColumnName("user_id");
            entity.Property(membership => membership.RoleId).HasColumnName("role_id");
            // Concurrency token DB-backed (CAS optimistic).
            entity.Property(membership => membership.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(membership => membership.Status).HasColumnName("status").IsRequired();
            entity.Property(membership => membership.CreatedAt).HasColumnName("created_at");
            entity.HasOne<BusinessRecord>().WithMany().HasForeignKey(membership => membership.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUserRecord>().WithMany().HasForeignKey(membership => membership.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            // Composite FK (ENG-003): roli DUHET t'i përkasë të njëjtit business —
            // lidhja cross-tenant refuzohet nga vetë PostgreSQL.
            entity.HasOne<RoleRecord>().WithMany()
                .HasForeignKey(membership => new { membership.BusinessId, membership.RoleId })
                .HasPrincipalKey(role => new { role.BusinessId, role.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(membership => new { membership.BusinessId, membership.UserId }).IsUnique();
        });

        modelBuilder.Entity<RolePermissionRecord>(entity =>
        {
            entity.ToTable("role_permission");
            entity.HasKey(link => new { link.BusinessId, link.RoleId, link.PermissionId });
            entity.Property(link => link.BusinessId).HasColumnName("business_id");
            entity.Property(link => link.RoleId).HasColumnName("role_id");
            entity.Property(link => link.PermissionId).HasColumnName("permission_id");
            // Composite FK (ENG-003) — grant-i cross-tenant dështon në DB.
            entity.HasOne<RoleRecord>().WithMany()
                .HasForeignKey(link => new { link.BusinessId, link.RoleId })
                .HasPrincipalKey(role => new { role.BusinessId, role.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PermissionRecord>().WithMany().HasForeignKey(link => link.PermissionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommandReceiptRecord>(entity =>
        {
            entity.ToTable("command_receipt", table => table.HasCheckConstraint(
                "ck_command_receipt_result_status", "result_status IN ('completed')"));
            entity.HasKey(receipt => receipt.Id);
            entity.Property(receipt => receipt.Id).HasColumnName("id");
            entity.Property(receipt => receipt.BusinessId).HasColumnName("business_id");
            entity.Property(receipt => receipt.OperationKey).HasColumnName("operation_key").IsRequired();
            entity.Property(receipt => receipt.IdempotencyKey).HasColumnName("idempotency_key").IsRequired();
            entity.Property(receipt => receipt.RequestFingerprint).HasColumnName("request_fingerprint").IsRequired();
            entity.Property(receipt => receipt.ResultStatus).HasColumnName("result_status").IsRequired();
            entity.Property(receipt => receipt.ResultPayload).HasColumnName("result_payload").HasColumnType("jsonb").IsRequired();
            entity.Property(receipt => receipt.CreatedAt).HasColumnName("created_at");
            entity.Property(receipt => receipt.CompletedAt).HasColumnName("completed_at");
            entity.HasOne<BusinessRecord>().WithMany().HasForeignKey(receipt => receipt.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            // Çelës alternativ (business_id, id) — target tenant-safe i FK-së audit→receipt.
            entity.HasAlternateKey(receipt => new { receipt.BusinessId, receipt.Id });
            // Kufiri i idempotencës — dy receipt me të njëjtin (tenant, operacion,
            // key) s'mund të ekzistojnë; humbësi i garës merr unique violation.
            entity.HasIndex(receipt => new { receipt.BusinessId, receipt.OperationKey, receipt.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("ux_command_receipt_idempotency");
        });

        modelBuilder.Entity<AuditLogRecord>(entity =>
        {
            entity.ToTable("audit_log");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.Id).HasColumnName("id");
            entity.Property(entry => entry.BusinessId).HasColumnName("business_id");
            entity.Property(entry => entry.UserId).HasColumnName("user_id");
            entity.Property(entry => entry.Entity).HasColumnName("entity").IsRequired();
            entity.Property(entry => entry.EntityId).HasColumnName("entity_id");
            entity.Property(entry => entry.Action).HasColumnName("action").IsRequired();
            entity.Property(entry => entry.BeforeState).HasColumnName("before_state").HasColumnType("jsonb");
            entity.Property(entry => entry.AfterState).HasColumnName("after_state").HasColumnType("jsonb");
            entity.Property(entry => entry.CommandReceiptId).HasColumnName("command_receipt_id");
            entity.Property(entry => entry.CorrelationId).HasColumnName("correlation_id");
            entity.Property(entry => entry.EventAt).HasColumnName("event_at");
            entity.HasOne<BusinessRecord>().WithMany().HasForeignKey(entry => entry.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            // Lidhja DB-enforced audit → command_receipt, TENANT-SAFE: composite
            // FK (business_id, command_receipt_id) → command_receipt(business_id, id)
            // — asnjë link cross-tenant nuk mund të ekzistojë.
            entity.HasOne<CommandReceiptRecord>().WithMany()
                .HasForeignKey(entry => new { entry.BusinessId, entry.CommandReceiptId })
                .HasPrincipalKey(receipt => new { receipt.BusinessId, receipt.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(entry => new { entry.BusinessId, entry.EventAt });
        });

        // ── Identity store foundation (ADR-028, B.1) — schema `identity`,
        //    emra tabelash të KONTROLLUAR (§3 i Identity_Authority_Store_
        //    Decision.md); ZERO AspNet*/role tables; user_token (Identity,
        //    account-level) dhe oidc_token (OpenIddict, OAuth2/OIDC
        //    operacional) kanë pronësi TË NDARË — tabela/tipe të ndryshme. ──

        modelBuilder.Entity<UserAccountRecord>(entity => entity.ToTable("user_account", IdentitySchema));
        modelBuilder.Entity<IdentityUserLogin<Guid>>(entity => entity.ToTable("user_login", IdentitySchema));
        modelBuilder.Entity<UserClaimRecord>(entity =>
        {
            // Invarianti DB (§7.2, correction R2): zero-UUID refuzohet nga
            // PostgreSQL — shprehja pa literal shifrash (anti false-positive
            // i skanerëve): repeat('0', 32)::uuid.
            entity.ToTable("user_claim", IdentitySchema, table => table.HasCheckConstraint(
                "ck_user_claim_id_non_empty", "id <> CAST(repeat('0', 32) AS uuid)"));
            // ZERO persisted int IDs (ABSOLUT): çelësi i VETËM i persistuar =
            // ClaimId (Guid, i gjeneruar në aplikacion — kurrë nga DB);
            // inherited `IdentityUserClaim<TKey>.Id` (int) INJOROHET nga EF
            // me formë STRING/metadata (zero referencë ekzekutuese IL ndaj
            // getter/setter-it — IL guard-i i CI-së e ndalon përdorimin).
            entity.HasKey(claim => claim.ClaimId);
            entity.Property(claim => claim.ClaimId).ValueGeneratedNever();
            entity.Ignore(nameof(IdentityUserClaim<Guid>.Id));
        });
        modelBuilder.Entity<IdentityUserToken<Guid>>(entity => entity.ToTable("user_token", IdentitySchema));
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreApplication<Guid>>(entity => entity.ToTable("oidc_application", IdentitySchema));
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreAuthorization<Guid>>(entity => entity.ToTable("oidc_authorization", IdentitySchema));
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreScope<Guid>>(entity => entity.ToTable("oidc_scope", IdentitySchema));
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreToken<Guid>>(entity => entity.ToTable("oidc_token", IdentitySchema));

        ApplyIdentityColumnNaming(modelBuilder);

        // PAS emërtimit mekanik snake_case: çelësi UUID i claim-it mapohet te
        // kolona KANONIKE `id` (konsistente me user_account/oidc_*).
        modelBuilder.Entity<UserClaimRecord>()
            .Property(claim => claim.ClaimId)
            .HasColumnName("id");
    }

    /// <summary>
    /// Emërtim DETERMINIST i kolonave të schema-s `identity`: emrat e
    /// property-ve REALE të Identity/OpenIddict konvertohen MEKANIKISHT në
    /// lowercase snake_case (konsistent me schema-n `platform`); emrat
    /// eksplicitë të framework-ut për indekset (`UserNameIndex`/`EmailIndex`)
    /// pastrohen → emrat default të EF-it mbi tabelat/kolonat e kontrolluara.
    /// Asnjë kolonë nuk shtohet, hiqet apo ndryshon tip — vetëm emërtim.
    /// </summary>
    private static void ApplyIdentityColumnNaming(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetSchema() != IdentitySchema)
            {
                continue;
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(null); // emri default i EF: IX_<tabelë>_<kolona>
            }
        }
    }

    /// <summary>Konvertim mekanik PascalCase → snake_case (p.sh. NormalizedUserName → normalized_user_name).</summary>
    private static string ToSnakeCase(string name)
        => string.Concat(name.Select((character, position) =>
            char.IsUpper(character)
                ? (position > 0 ? "_" : string.Empty) + char.ToLowerInvariant(character)
                : character.ToString()));
}
