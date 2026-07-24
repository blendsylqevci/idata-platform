using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Idata.Platform.Persistence;

/// <summary>
/// Fabrika e context-it — përdoret nga DbMigrator-i (runtime), nga API host-i
/// (vetëm query; kurrë migrime) dhe nga `dotnet ef` (design-time, përmes
/// env/dev trust). API host-i NUK e përdor për migrime: API nuk auto-migron
/// kurrë në startup.
/// </summary>
public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
        => Create(PlatformDatabaseConnection.FromEnvironment());

    /// <summary>Konfigurim I VETËM i options — historia e migrimeve brenda schema-s.</summary>
    public static void Configure(DbContextOptionsBuilder builder, string connectionString)
        => builder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", PlatformDbContext.Schema));

    public static PlatformDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        Configure(options, connectionString);
        return new PlatformDbContext(options.Options);
    }
}
