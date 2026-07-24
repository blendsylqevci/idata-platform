using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Idata.Platform.Modules.PlatformStatus.Infrastructure;

/// <summary>
/// Fabrika e context-it — përdoret nga DbMigrator-i (runtime) dhe nga
/// `dotnet ef` (design-time, përmes env/dev trust). API host-i NUK e përdor
/// për migrime: API nuk auto-migron kurrë në startup.
/// </summary>
public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
        => Create(PlatformDatabaseConnection.FromEnvironment());

    public static PlatformDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", PlatformDbContext.Schema))
            .Options;
        return new PlatformDbContext(options);
    }
}
