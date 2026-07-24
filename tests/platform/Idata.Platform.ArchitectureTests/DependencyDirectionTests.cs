using System.Reflection;
using Xunit;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Rregullat e drejtimit të varësive (ADR-024, module-first Clean/Onion),
/// të verifikuara mbi metadata reale të assembly-ve — DIREKT DHE TRANSITIVE:
///  - Domain → asnjë shtresë tjetër e platformës;
///  - Application → vetëm Domain-i i modulit të vet;
///  - Infrastructure → Application + Domain të modulit + Persistence (shared);
///  - Persistence (migration owner i vetëm) = NEUTRAL: zero varësi ndaj
///    asnjë Domain/Application moduli (mbyll rrugën transitive
///    PlatformStatus.Infrastructure → Persistence → AccessControl.Domain);
///  - DbMigrator → Persistence (+ unazat e brendshme);
///  - API Host = composition root (i vetmi që sheh gjithçka);
///  - ZERO arritje TRANSITIVE cross-module (AccessControl ↔ PlatformStatus);
///  - ZERO identity provider konkret (ADR-028 OPEN); ZERO legacy refs.
/// </summary>
public sealed class DependencyDirectionTests
{
    private const string PlatformPrefix = "Idata.Platform";

    private static readonly Assembly PsDomain = Assembly.Load("Idata.Platform.Modules.PlatformStatus.Domain");
    private static readonly Assembly PsApplication = Assembly.Load("Idata.Platform.Modules.PlatformStatus.Application");
    private static readonly Assembly PsInfrastructure = Assembly.Load("Idata.Platform.Modules.PlatformStatus.Infrastructure");
    private static readonly Assembly PsPresentation = Assembly.Load("Idata.Platform.Modules.PlatformStatus.Presentation");
    private static readonly Assembly AcDomain = Assembly.Load("Idata.Platform.Modules.AccessControl.Domain");
    private static readonly Assembly AcApplication = Assembly.Load("Idata.Platform.Modules.AccessControl.Application");
    private static readonly Assembly AcInfrastructure = Assembly.Load("Idata.Platform.Modules.AccessControl.Infrastructure");
    private static readonly Assembly AcPresentation = Assembly.Load("Idata.Platform.Modules.AccessControl.Presentation");
    private static readonly Assembly Persistence = Assembly.Load("Idata.Platform.Persistence");
    private static readonly Assembly DbMigrator = Assembly.Load("Idata.Platform.DbMigrator");
    private static readonly Assembly ApiHost = Assembly.Load("Idata.Platform.Api");

    private static Assembly[] AllPlatformAssemblies =>
        [PsDomain, PsApplication, PsInfrastructure, PsPresentation,
         AcDomain, AcApplication, AcInfrastructure, AcPresentation,
         Persistence, DbMigrator, ApiHost];

    /// <summary>Referencat DIREKTE `Idata.Platform.*` të një assembly.</summary>
    private static string[] DirectPlatformReferencesOf(Assembly assembly) =>
        [.. assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith(PlatformPrefix, StringComparison.Ordinal))
            .Order()];

    /// <summary>
    /// Mbyllja TRANSITIVE e referencave `Idata.Platform.*` (BFS) — kap varësitë
    /// që kalojnë përmes shtresave të ndërmjetme (p.sh. Persistence).
    /// </summary>
    private static HashSet<string> TransitivePlatformClosureOf(Assembly root)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<Assembly>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var reference in current.GetReferencedAssemblies())
            {
                var name = reference.Name ?? string.Empty;
                if (!name.StartsWith(PlatformPrefix, StringComparison.Ordinal) || !visited.Add(name))
                {
                    continue;
                }

                queue.Enqueue(Assembly.Load(name));
            }
        }

        return visited;
    }

    [Fact]
    public void Domain_et_nuk_varen_nga_asnje_shtrese_tjeter_e_platformes()
    {
        Assert.Empty(DirectPlatformReferencesOf(PsDomain));
        Assert.Empty(DirectPlatformReferencesOf(AcDomain));
    }

    [Fact]
    public void Application_et_varen_vetem_nga_Domain_i_modulit_te_vet()
    {
        Assert.Equal(["Idata.Platform.Modules.PlatformStatus.Domain"], DirectPlatformReferencesOf(PsApplication));
        Assert.All(DirectPlatformReferencesOf(AcApplication),
            name => Assert.Equal("Idata.Platform.Modules.AccessControl.Domain", name));
    }

    [Fact]
    public void Infrastructure_et_varen_vetem_nga_Application_Domain_dhe_Persistence()
    {
        Assert.All(DirectPlatformReferencesOf(PsInfrastructure), name => Assert.Contains(name, (string[])
            ["Idata.Platform.Modules.PlatformStatus.Application", "Idata.Platform.Modules.PlatformStatus.Domain", "Idata.Platform.Persistence"]));
        Assert.All(DirectPlatformReferencesOf(AcInfrastructure), name => Assert.Contains(name, (string[])
            ["Idata.Platform.Modules.AccessControl.Application", "Idata.Platform.Modules.AccessControl.Domain", "Idata.Platform.Persistence"]));
    }

    [Fact]
    public void Persistence_eshte_NEUTRAL_zero_referenca_ndaj_moduleve()
    {
        // Migration owner-i i vetëm nuk njeh asnjë Domain/Application moduli.
        Assert.DoesNotContain(TransitivePlatformClosureOf(Persistence),
            name => name.Contains(".Modules.", StringComparison.Ordinal));
    }

    [Fact]
    public void Asnje_arritje_TRANSITIVE_cross_module_midis_AccessControl_dhe_PlatformStatus()
    {
        foreach (var assembly in (Assembly[])[AcDomain, AcApplication, AcInfrastructure, AcPresentation])
        {
            Assert.DoesNotContain(TransitivePlatformClosureOf(assembly),
                name => name.Contains(".PlatformStatus.", StringComparison.Ordinal));
        }

        foreach (var assembly in (Assembly[])[PsDomain, PsApplication, PsInfrastructure, PsPresentation])
        {
            // Përfshin rrugën e vjetër PlatformStatus.Infrastructure → Persistence
            // → AccessControl.Domain, që tani duhet të jetë e MBYLLUR.
            Assert.DoesNotContain(TransitivePlatformClosureOf(assembly),
                name => name.Contains(".AccessControl.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Presentation_et_varen_vetem_nga_Application_dhe_Domain()
    {
        Assert.All(DirectPlatformReferencesOf(PsPresentation), name => Assert.Contains(name, (string[])
            ["Idata.Platform.Modules.PlatformStatus.Application", "Idata.Platform.Modules.PlatformStatus.Domain"]));
        Assert.All(DirectPlatformReferencesOf(AcPresentation), name => Assert.Contains(name, (string[])
            ["Idata.Platform.Modules.AccessControl.Application", "Idata.Platform.Modules.AccessControl.Domain"]));
    }

    [Fact]
    public void Asnje_shtrese_e_brendshme_nuk_varet_nga_API_host_ose_DbMigrator()
    {
        foreach (var assembly in (Assembly[])[
            PsDomain, PsApplication, PsInfrastructure, PsPresentation,
            AcDomain, AcApplication, AcInfrastructure, AcPresentation, Persistence])
        {
            Assert.DoesNotContain(TransitivePlatformClosureOf(assembly),
                name => name is "Idata.Platform.Api" or "Idata.Platform.DbMigrator");
        }
    }

    [Fact]
    public void DbMigrator_nuk_varet_nga_Presentation_ose_API_host()
        => Assert.DoesNotContain(TransitivePlatformClosureOf(DbMigrator),
            name => name is "Idata.Platform.Api"
                or "Idata.Platform.Modules.PlatformStatus.Presentation"
                or "Idata.Platform.Modules.AccessControl.Presentation");

    [Fact]
    public void Zero_identity_provider_konkret_ne_asnje_assembly_te_platformes()
    {
        string[] forbidden =
            ["OpenIddict", "IdentityServer", "Duende", "Microsoft.AspNetCore.Identity",
             "Microsoft.Extensions.Identity", "Auth0", "Okta"];
        foreach (var assembly in AllPlatformAssemblies)
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name ?? string.Empty;
                Assert.DoesNotContain(forbidden, prefix =>
                    name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void Cdo_referece_Idata_eshte_brenda_setit_te_njohur_te_platformes()
    {
        string[] known =
            ["Idata.Platform.Api", "Idata.Platform.DbMigrator", "Idata.Platform.Persistence",
             "Idata.Platform.Modules.AccessControl.Domain", "Idata.Platform.Modules.AccessControl.Application",
             "Idata.Platform.Modules.AccessControl.Infrastructure", "Idata.Platform.Modules.AccessControl.Presentation",
             "Idata.Platform.Modules.PlatformStatus.Domain", "Idata.Platform.Modules.PlatformStatus.Application",
             "Idata.Platform.Modules.PlatformStatus.Infrastructure", "Idata.Platform.Modules.PlatformStatus.Presentation"];
        foreach (var assembly in AllPlatformAssemblies)
        {
            foreach (var name in DirectPlatformReferencesOf(assembly))
            {
                Assert.Contains(name, known);
            }
        }
    }
}
