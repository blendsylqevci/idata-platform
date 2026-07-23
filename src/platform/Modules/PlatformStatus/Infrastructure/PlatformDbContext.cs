using Microsoft.EntityFrameworkCore;

namespace Idata.Platform.Modules.PlatformStatus.Infrastructure;

/// <summary>
/// DbContext i target database-it — EF Core është MIGRATION OWNER I VETËM
/// (ADR-024 §"Një migration owner"). Schema module-owned `platform`;
/// historia e migrimeve jeton brenda të njëjtit boundary. Asnjë tabelë
/// biznesi dhe asnjë e dhënë reale në Bootstrap.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options)
    : DbContext(options)
{
    public const string Schema = "platform";

    public DbSet<MigrationSmokeCheck> MigrationSmokeChecks => Set<MigrationSmokeCheck>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<MigrationSmokeCheck>(entity =>
        {
            entity.ToTable("migration_smoke_check");
            entity.HasKey(check => check.Id);
        });
    }
}

/// <summary>
/// Tabelë TEKNIKE minimale — ekziston vetëm për të provuar mekanikisht
/// path-in schema/migration (fresh/no-op/down/reapply). Nuk është tabelë
/// biznesi dhe nuk mban asnjë të dhënë reale.
/// </summary>
public sealed class MigrationSmokeCheck
{
    public int Id { get; set; }
}
