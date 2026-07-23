using Idata.Platform.Modules.PlatformStatus.Infrastructure;
using Microsoft.EntityFrameworkCore;

// DbMigrator — i VETMI vend ku aplikohen migrimet e target database-it
// (EF Core = migration owner i vetëm; API host-i nuk migron kurrë).
// Komanda të qarta e deterministe; çdo gjendje e panjohur del fail-closed.
var command = args.Length > 0 ? args[0] : "migrate";

await using var context = PlatformDbContextFactory.Create(PlatformDatabaseConnection.FromEnvironment());

switch (command)
{
    case "migrate":
    {
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        await context.Database.MigrateAsync();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).Count();
        Console.WriteLine($"DbMigrator: aplikoi {pending.Length} migrime te reja; historia tani = {applied}.");
        return 0;
    }

    case "status":
    {
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        Console.WriteLine($"DbMigrator: {pending.Length} migrime pending.");
        return pending.Length == 0 ? 0 : 3;
    }

    case "down-to-zero":
    {
        await context.Database.MigrateAsync("0");
        Console.WriteLine("DbMigrator: zbriti ne zero (asnje migrim i aplikuar).");
        return 0;
    }

    default:
        Console.Error.WriteLine("DbMigrator: komande e panjohur. Te lejuara: migrate | status | down-to-zero.");
        return 2;
}
