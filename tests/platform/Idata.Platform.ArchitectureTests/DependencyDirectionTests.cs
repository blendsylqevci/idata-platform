using System.Reflection;
using Xunit;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Rregullat e drejtimit të varësive (ADR-024, module-first Clean/Onion),
/// të verifikuara mbi metadata reale të assembly-ve — pa dependency shtesë:
///  - Domain → asnjë shtresë tjetër e platformës;
///  - Application → vetëm Domain;
///  - Infrastructure → vetëm Application + Domain;
///  - Presentation → vetëm Application (+ Domain si unazë e brendshme);
///  - DbMigrator → Infrastructure (+ unazat e brendshme);
///  - API Host = composition root (i vetmi që sheh gjithçka).
/// </summary>
public sealed class DependencyDirectionTests
{
    private const string PlatformPrefix = "Idata.Platform";

    private static readonly Assembly Domain =
        Assembly.Load("Idata.Platform.Modules.PlatformStatus.Domain");

    private static readonly Assembly Application =
        Assembly.Load("Idata.Platform.Modules.PlatformStatus.Application");

    private static readonly Assembly Infrastructure =
        Assembly.Load("Idata.Platform.Modules.PlatformStatus.Infrastructure");

    private static readonly Assembly Presentation =
        Assembly.Load("Idata.Platform.Modules.PlatformStatus.Presentation");

    private static readonly Assembly DbMigrator =
        Assembly.Load("Idata.Platform.DbMigrator");

    private static string[] PlatformReferencesOf(Assembly assembly) =>
        [.. assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith(PlatformPrefix, StringComparison.Ordinal))
            .Order()];

    [Fact]
    public void Domain_nuk_varet_nga_asnje_shtrese_tjeter_e_platformes()
        => Assert.Empty(PlatformReferencesOf(Domain));

    [Fact]
    public void Application_varet_vetem_nga_Domain()
        => Assert.Equal(
            ["Idata.Platform.Modules.PlatformStatus.Domain"],
            PlatformReferencesOf(Application));

    [Fact]
    public void Infrastructure_varet_vetem_nga_Application_dhe_Domain()
        => Assert.All(
            PlatformReferencesOf(Infrastructure),
            name => Assert.Contains(name, (string[])
            [
                "Idata.Platform.Modules.PlatformStatus.Application",
                "Idata.Platform.Modules.PlatformStatus.Domain",
            ]));

    [Fact]
    public void Presentation_varet_vetem_nga_Application_dhe_Domain()
        => Assert.All(
            PlatformReferencesOf(Presentation),
            name => Assert.Contains(name, (string[])
            [
                "Idata.Platform.Modules.PlatformStatus.Application",
                "Idata.Platform.Modules.PlatformStatus.Domain",
            ]));

    [Fact]
    public void Asnje_shtrese_e_brendshme_nuk_varet_nga_API_host_ose_DbMigrator()
    {
        foreach (var assembly in (Assembly[])[Domain, Application, Infrastructure, Presentation])
        {
            Assert.DoesNotContain(
                PlatformReferencesOf(assembly),
                name => name is "Idata.Platform.Api" or "Idata.Platform.DbMigrator");
        }
    }

    [Fact]
    public void DbMigrator_nuk_varet_nga_Presentation_ose_API_host()
        => Assert.DoesNotContain(
            PlatformReferencesOf(DbMigrator),
            name => name is "Idata.Platform.Api" or "Idata.Platform.Modules.PlatformStatus.Presentation");
}
