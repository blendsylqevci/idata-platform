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

    /// <summary>
    /// §3-R4: ngarkimi i një projekti VETËM me path kanonik nga katalogu
    /// MSBuild-real, me identitet të verifikuar kundër TargetPath (MVID).
    /// Referencat eksplicite per-layer mbeten, por NUK janë inventari global.
    /// </summary>
    private static Assembly ByCanonicalPath(string canonicalPath)
        => PlatformProjectCatalog.LoadVerified(PlatformProjectCatalog.DiscoverProduction()
            .Single(entry => entry.CanonicalPath == canonicalPath));

    private static readonly Assembly PsDomain =
        ByCanonicalPath("src/platform/Modules/PlatformStatus/Domain/Idata.Platform.Modules.PlatformStatus.Domain.csproj");
    private static readonly Assembly PsApplication =
        ByCanonicalPath("src/platform/Modules/PlatformStatus/Application/Idata.Platform.Modules.PlatformStatus.Application.csproj");
    private static readonly Assembly PsInfrastructure =
        ByCanonicalPath("src/platform/Modules/PlatformStatus/Infrastructure/Idata.Platform.Modules.PlatformStatus.Infrastructure.csproj");
    private static readonly Assembly PsPresentation =
        ByCanonicalPath("src/platform/Modules/PlatformStatus/Presentation/Idata.Platform.Modules.PlatformStatus.Presentation.csproj");
    private static readonly Assembly AcDomain =
        ByCanonicalPath("src/platform/Modules/AccessControl/Domain/Idata.Platform.Modules.AccessControl.Domain.csproj");
    private static readonly Assembly AcApplication =
        ByCanonicalPath("src/platform/Modules/AccessControl/Application/Idata.Platform.Modules.AccessControl.Application.csproj");
    private static readonly Assembly AcInfrastructure =
        ByCanonicalPath("src/platform/Modules/AccessControl/Infrastructure/Idata.Platform.Modules.AccessControl.Infrastructure.csproj");
    private static readonly Assembly AcPresentation =
        ByCanonicalPath("src/platform/Modules/AccessControl/Presentation/Idata.Platform.Modules.AccessControl.Presentation.csproj");
    private static readonly Assembly IdentityInfrastructure =
        ByCanonicalPath("src/platform/Modules/Identity/Infrastructure/Idata.Platform.Modules.Identity.Infrastructure.csproj");
    private static readonly Assembly Persistence =
        ByCanonicalPath("src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj");
    private static readonly Assembly DbMigrator =
        ByCanonicalPath("src/platform/Idata.Platform.DbMigrator/Idata.Platform.DbMigrator.csproj");
    private static readonly Assembly ApiHost =
        ByCanonicalPath("src/platform/Idata.Platform.Api/Idata.Platform.Api.csproj");

    /// <summary>§3-R4: inventari GLOBAL = i DERIVUAR nga katalogu, jo lista e emërtuar.</summary>
    private static Assembly[] AllPlatformAssemblies =>
        [.. PlatformProjectCatalog.DiscoverProduction().Select(PlatformProjectCatalog.LoadVerified)];

    private sealed record ProjectClassification(string CanonicalPath, string Category, string? Module, string? Layer);

    private static readonly string[] KnownLayers = ["Domain", "Application", "Infrastructure", "Presentation"];

    /// <summary>
    /// §3-R4: klasifikimi dependency-direction i ÇDO projekti të katalogut —
    /// projekt që s'klasifikohet dot = FAIL i qartë (moduli/projekti i 13-të
    /// s'humbet kurrë në heshtje).
    /// </summary>
    private static ProjectClassification Classify(PlatformProjectCatalog.ProjectEntry entry)
    {
        var segments = entry.CanonicalPath.Split('/');
        if (segments is ["src", "platform", "Modules", var module, var layer, _] && KnownLayers.Contains(layer))
        {
            return new ProjectClassification(entry.CanonicalPath, "module-layer", module, layer);
        }

        return entry.CanonicalPath switch
        {
            "src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj"
                => new ProjectClassification(entry.CanonicalPath, "shared-persistence", null, null),
            "src/platform/Idata.Platform.DbMigrator/Idata.Platform.DbMigrator.csproj"
                => new ProjectClassification(entry.CanonicalPath, "migration-host", null, null),
            "src/platform/Idata.Platform.Api/Idata.Platform.Api.csproj"
                => new ProjectClassification(entry.CanonicalPath, "composition-root", null, null),
            _ => throw new InvalidOperationException(
                $"Projekt i PAKLASIFIKUAR nga dependency-direction guards: {entry.CanonicalPath}"),
        };
    }

    /// <summary>
    /// Meta-testi i BARAZISË §3-R4: catalog == evaluated assemblies ==
    /// assemblies të skanuara == projekte të klasifikuara — një projekt i ri
    /// që del nga cilido set e bën testin RED.
    /// </summary>
    [Fact]
    public void Universi_global_katalog_assemblies_skanime_klasifikime_jane_te_barabarta()
    {
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        Assert.NotEmpty(catalog);

        // Çdo projekt klasifikohet (throw = RED me diagnostikë të qartë).
        var classified = catalog.Select(Classify).ToArray();
        Assert.Equal(catalog.Length, classified.Length);
        Assert.Equal(
            catalog.Select(entry => entry.CanonicalPath).Order(StringComparer.Ordinal),
            classified.Select(c => c.CanonicalPath).Order(StringComparer.Ordinal));

        // Assemblies e vlerësuara == assemblies e ngarkuara/skanuara.
        var evaluatedNames = catalog.Select(entry => entry.AssemblyName).Order(StringComparer.Ordinal).ToArray();
        var loadedNames = AllPlatformAssemblies
            .Select(assembly => assembly.GetName().Name ?? string.Empty)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(evaluatedNames, loadedNames);

        // Referencat e emërtuara per-layer mbulojnë SAKTËSISHT të njëjtin set —
        // modul/projekt i ri i paklasifikuar në asserts eksplicite = RED.
        var namedFieldNames = ((Assembly[])
            [PsDomain, PsApplication, PsInfrastructure, PsPresentation,
             AcDomain, AcApplication, AcInfrastructure, AcPresentation,
             IdentityInfrastructure, Persistence, DbMigrator, ApiHost])
            .Select(assembly => assembly.GetName().Name ?? string.Empty)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(evaluatedNames, namedFieldNames);
    }

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

    private static readonly string[] IdentitySurfacePrefixes =
        ["OpenIddict", "IdentityServer", "Duende", "Microsoft.AspNetCore.Identity",
         "Microsoft.Extensions.Identity", "Microsoft.IdentityModel", "Auth0", "Okta"];

    /// <summary>
    /// ZËVENDËSIMI i kontrolluar (ADR-028 §7, Increment B.1) i testit të
    /// vjetër `Zero_identity_provider_konkret_ne_asnje_assembly_te_platformes`
    /// — JO fshirje: coverage FORCOHET. Rregulli i ri (i shtrënguar në
    /// correction pass të PR #28):
    ///  1. çdo referencë identity-surface duhet të jetë EKZAKT në allowlist-in
    ///     e ADR-028 (store only) — kjo ndalon automatikisht OpenIddict
    ///     Server/Validation/Client (B.2+), Identity.UI, sipërfaqen sign-in
    ///     `Microsoft.AspNetCore.Identity` (cookies/SignInManager) dhe ÇDO
    ///     provider tjetër (Duende/IdentityServer/Auth0/Okta — kudo);
    ///  2. direct owners = VETËM Persistence (storage mapping) dhe
    ///     Identity.Infrastructure (wiring) — **API host-i NUK është owner**
    ///     direkt identity derisa B.2 ta autorizojë veçmas (test i dedikuar
    ///     më poshtë).
    /// </summary>
    [Fact]
    public void Identity_paketat_vetem_assemblies_e_aprovuara_dhe_vetem_ne_projektet_e_aprovuara()
    {
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
             "Idata.Platform.Modules.Identity.Infrastructure"];

        foreach (var assembly in AllPlatformAssemblies)
        {
            var owner = assembly.GetName().Name ?? string.Empty;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name ?? string.Empty;
                if (!IdentitySurfacePrefixes.Any(prefix =>
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
    /// API host = composition root PA pronësi direkte identity: konsumon
    /// store foundation-in VETËM përmes project reference te
    /// Identity.Infrastructure; ZERO referencë direkte assembly-je
    /// identity-surface (derisa B.2 ta autorizojë veçmas me gate të ri).
    /// </summary>
    [Fact]
    public void Api_composition_root_ka_zero_referenca_direkte_identity()
    {
        Assert.Contains("Idata.Platform.Modules.Identity.Infrastructure",
            DirectPlatformReferencesOf(ApiHost));

        foreach (var reference in ApiHost.GetReferencedAssemblies())
        {
            var name = reference.Name ?? string.Empty;
            Assert.DoesNotContain(IdentitySurfacePrefixes, prefix =>
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }

    // Testi strukturor "zero Identity roles" u ZËVENDËSUA (correction pass
    // PR #28) me traversal REKURSIV të plotë + fixtures diskriminuese —
    // shih `IdentityRoleSurfaceTests` + `RoleSurfaceScanner` (coverage
    // strikt më i gjerë: fields/methods/events/attributes/nested/constraints,
    // publike dhe jo-publike; zero fshirje coverage-i).

    [Fact]
    public void Cdo_referece_Idata_eshte_brenda_setit_te_njohur_te_platformes()
    {
        // §3-R4: seti i njohur = i DERIVUAR nga katalogu (jo listë e hardcoded).
        var known = PlatformProjectCatalog.DiscoverProduction()
            .Select(entry => entry.AssemblyName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var assembly in AllPlatformAssemblies)
        {
            foreach (var name in DirectPlatformReferencesOf(assembly))
            {
                Assert.Contains(name, known);
            }
        }
    }
}
