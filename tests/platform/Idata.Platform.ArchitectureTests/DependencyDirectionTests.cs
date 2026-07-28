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
///  - ZERO arritje TRANSITIVE cross-module (AccessControl ↔ PlatformStatus
///    ↔ Identity);
///  - identity packages: VETËM assemblies e aprovuara nga ADR-028 dhe VETËM
///    në projektet e aprovuara (zëvendësimi i kontrolluar B.1 i testit të
///    vjetër provider-neutrality — ADR-028 §7); Duende/IdentityServer/
///    Auth0/Okta + OpenIddict Server/Validation/Client + Identity UI/
///    sign-in surface = TË NDALUARA kudo; ZERO legacy refs.
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
    private static readonly Assembly IdentityInfrastructure = Assembly.Load("Idata.Platform.Modules.Identity.Infrastructure");
    private static readonly Assembly Persistence = Assembly.Load("Idata.Platform.Persistence");
    private static readonly Assembly DbMigrator = Assembly.Load("Idata.Platform.DbMigrator");
    private static readonly Assembly ApiHost = Assembly.Load("Idata.Platform.Api");

    private static Assembly[] AllPlatformAssemblies =>
        [PsDomain, PsApplication, PsInfrastructure, PsPresentation,
         AcDomain, AcApplication, AcInfrastructure, AcPresentation,
         IdentityInfrastructure, Persistence, DbMigrator, ApiHost];

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
        // Identity (B.1): Infrastructure = wiring i store foundation-it mbi
        // Persistence (storage mapping owner); Domain/Application vijnë me
        // konceptet reale të B.2/B.3 — pa projekte bosh (ADR-029 §14).
        Assert.All(DirectPlatformReferencesOf(IdentityInfrastructure), name => Assert.Contains(name, (string[])
            ["Idata.Platform.Persistence"]));
    }

    [Fact]
    public void Persistence_eshte_NEUTRAL_zero_referenca_ndaj_moduleve()
    {
        // Migration owner-i i vetëm nuk njeh asnjë Domain/Application moduli.
        Assert.DoesNotContain(TransitivePlatformClosureOf(Persistence),
            name => name.Contains(".Modules.", StringComparison.Ordinal));
    }

    [Fact]
    public void Asnje_arritje_TRANSITIVE_cross_module_midis_moduleve()
    {
        foreach (var assembly in (Assembly[])[AcDomain, AcApplication, AcInfrastructure, AcPresentation])
        {
            Assert.DoesNotContain(TransitivePlatformClosureOf(assembly),
                name => name.Contains(".PlatformStatus.", StringComparison.Ordinal)
                    || name.Contains(".Identity.", StringComparison.Ordinal));
        }

        foreach (var assembly in (Assembly[])[PsDomain, PsApplication, PsInfrastructure, PsPresentation])
        {
            // Përfshin rrugën e vjetër PlatformStatus.Infrastructure → Persistence
            // → AccessControl.Domain, që tani duhet të jetë e MBYLLUR.
            Assert.DoesNotContain(TransitivePlatformClosureOf(assembly),
                name => name.Contains(".AccessControl.", StringComparison.Ordinal)
                    || name.Contains(".Identity.", StringComparison.Ordinal));
        }

        // Identity (B.1) nuk arrin ASNJË modul tjetër — as direkt, as transitiv.
        Assert.DoesNotContain(TransitivePlatformClosureOf(IdentityInfrastructure),
            name => name.Contains(".AccessControl.", StringComparison.Ordinal)
                || name.Contains(".PlatformStatus.", StringComparison.Ordinal));
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
            AcDomain, AcApplication, AcInfrastructure, AcPresentation,
            IdentityInfrastructure, Persistence])
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

    /// <summary>
    /// ZËVENDËSIMI i kontrolluar (ADR-028 §7, Increment B.1) i testit të
    /// vjetër `Zero_identity_provider_konkret_ne_asnje_assembly_te_platformes`
    /// — JO fshirje: coverage FORCOHET. Rregulli i ri:
    ///  1. çdo referencë identity-surface duhet të jetë EKZAKT në allowlist-in
    ///     e ADR-028 (store only) — kjo ndalon automatikisht OpenIddict
    ///     Server/Validation/Client (B.2+), Identity.UI, sipërfaqen sign-in
    ///     `Microsoft.AspNetCore.Identity` (cookies/SignInManager) dhe ÇDO
    ///     provider tjetër (Duende/IdentityServer/Auth0/Okta — kudo);
    ///  2. referencat e lejuara jetojnë VETËM në projektet e aprovuara:
    ///     Persistence (storage mapping), Identity.Infrastructure (wiring)
    ///     dhe API host (composition root, kur kërkohet realisht).
    /// </summary>
    [Fact]
    public void Identity_paketat_vetem_assemblies_e_aprovuara_dhe_vetem_ne_projektet_e_aprovuara()
    {
        string[] identitySurfacePrefixes =
            ["OpenIddict", "IdentityServer", "Duende", "Microsoft.AspNetCore.Identity",
             "Microsoft.Extensions.Identity", "Auth0", "Okta"];
        string[] approvedIdentityAssemblies =
            ["Microsoft.AspNetCore.Identity.EntityFrameworkCore",
             "Microsoft.Extensions.Identity.Core",
             "Microsoft.Extensions.Identity.Stores",
             "OpenIddict.Abstractions",
             "OpenIddict.Core",
             "OpenIddict.EntityFrameworkCore",
             "OpenIddict.EntityFrameworkCore.Models"];
        string[] approvedProjects =
            ["Idata.Platform.Persistence",
             "Idata.Platform.Modules.Identity.Infrastructure",
             "Idata.Platform.Api"];

        foreach (var assembly in AllPlatformAssemblies)
        {
            var owner = assembly.GetName().Name ?? string.Empty;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name ?? string.Empty;
                if (!identitySurfacePrefixes.Any(prefix =>
                        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Assert.Contains(name, approvedIdentityAssemblies);
                Assert.Contains(owner, approvedProjects);
            }
        }
    }

    /// <summary>
    /// Roleless i imponuar STRUKTURALISHT: asnjë tip i platformës nuk
    /// ekspozon/trashëgon IdentityRole/RoleManager/RoleStore — Identity
    /// roles NUK ekzistojnë as si tip, as si varësi (tenant RBAC = vetëm
    /// platform.role/permission/role_permission/business_membership, F1.1).
    /// </summary>
    [Fact]
    public void Zero_Identity_roles_ne_asnje_tip_te_platformes()
    {
        string[] forbiddenMarkers =
            ["Microsoft.AspNetCore.Identity.IdentityRole",
             "Microsoft.AspNetCore.Identity.IdentityUserRole",
             "Microsoft.AspNetCore.Identity.IdentityRoleClaim",
             "Microsoft.AspNetCore.Identity.RoleManager",
             "Microsoft.AspNetCore.Identity.EntityFrameworkCore.RoleStore",
             "Microsoft.AspNetCore.Identity.IRoleStore"];

        foreach (var assembly in AllPlatformAssemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var surfaces = new List<string>();
                for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
                {
                    surfaces.Add(baseType.FullName ?? string.Empty);
                    surfaces.AddRange(baseType.GenericTypeArguments.Select(argument => argument.FullName ?? string.Empty));
                }

                surfaces.AddRange(type.GetProperties().Select(property => property.PropertyType.FullName ?? string.Empty));
                surfaces.AddRange(type.GetConstructors()
                    .SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => parameter.ParameterType.FullName ?? string.Empty));

                foreach (var surface in surfaces)
                {
                    Assert.DoesNotContain(forbiddenMarkers, marker =>
                        surface.StartsWith(marker, StringComparison.Ordinal));
                }
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
             "Idata.Platform.Modules.Identity.Infrastructure",
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
