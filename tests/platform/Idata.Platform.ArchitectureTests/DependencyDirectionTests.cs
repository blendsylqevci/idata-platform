using System.Diagnostics;
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

                queue.Enqueue(PlatformProjectCatalog.LoadReferencedAssembly(new AssemblyName(name)));
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

    /// <summary>§3-R5: DEFENSE-IN-DEPTH — NUK është më autoriteti (autoriteti = klasifikimi TOTAL më poshtë).</summary>
    private static readonly string[] IdentitySurfacePrefixes =
        ["OpenIddict", "IdentityServer", "Duende", "Microsoft.AspNetCore.Identity",
         "Microsoft.Extensions.Identity", "Microsoft.IdentityModel", "Auth0", "Okta"];

    // ── §3-R5: KLASIFIKIMI TOTAL i AssemblyRefs (autoriteti kryesor) ──

    /// <summary>Rrënja e shared-framework-ut REAL të runtime-it (autoriteti i kategorisë 'framework').</summary>
    private static readonly string SharedFrameworkRoot =
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", ".."));

    /// <summary>
    /// §3D-R6: referencat eksplicite tani janë STRUKTURORE (owner + package
    /// id + version ekzakt + asset-i i pritur + arsyetim) dhe sot BOSH —
    /// aliasi i mëparshëm name-only `Mediator` u HOQ sepse provenance-ja
    /// reale MSBuild e lidh MEKANIKISHT me Mediator.Abstractions 3.0.2
    /// (NuGetPackageId/Version në ReferencePath). Çdo hyrje e ardhshme
    /// kërkon arsyetim + review; meta-testi më poshtë e bën RED çdo hyrje
    /// të vdekur/të paplotë/spoofed.
    /// </summary>
    internal sealed record ExplicitReferenceApproval(
        string OwnerCanonicalPath, string PackageId, string PackageVersion, string ExpectedAssetFileName, string Rationale);

    internal static readonly IReadOnlyDictionary<string, ExplicitReferenceApproval> ExplicitApprovedResolvedReferences =
        new Dictionary<string, ExplicitReferenceApproval>(StringComparer.Ordinal);

    /// <summary>
    /// §3-R6: shtresa NAME-BASED (klasifikuesi i R5) mbetet VETËM si
    /// defense-in-depth — AUTORITETI tani është provenance-based
    /// (`Cdo_ReferencePath_dhe_AssemblyRef_klasifikohet_nga_provenance_per_owner`).
    /// Aliasi `Mediator` këtu është vetëm shtresa dytësore; në AUTORITET ai
    /// lidhet MEKANIKISHT me Mediator.Abstractions 3.0.2 pa naked name
    /// (shih `ExplicitApprovedResolvedReferences`, sot bosh).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExplicitApprovedReferences =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Mediator"] = "defense-in-depth: assembly i paketës së aprovuar Mediator.Abstractions 3.0.2 (autoriteti = provenance)",
        };

    /// <summary>
    /// §3D-R6 meta-test: çdo hyrje eksplicite duhet të lidhet me asset REAL
    /// të paketës së deklaruar në autoritetin e owner-it — alias i vdekur,
    /// i paplotë ose spoofed = RED. (Sot seti është bosh — testi provon
    /// mekanizmin e validimit, jo një listë.)
    /// </summary>
    /// <summary>
    /// §5-R7 (W-2 i MBYLLUR) — validator-i PURE i alias-eve eksplicite tani
    /// USHTROHET me approvals sintetikë (jo vetëm me set bosh): owner i
    /// panjohur · paketë jashtë autoritetit direkt · version i gabuar ·
    /// arsyetim bosh · asset i munguar/gabuar → GJETJE; approval plotësisht
    /// valid → zero gjetje. Seti PRODUCTION mbetet bosh (fail-closed).
    /// </summary>
    [Fact]
    public void Validator_i_alias_eve_eksplicite_kap_cdo_devijim()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var catalogByPath = PlatformProjectCatalog.DiscoverProduction()
            .ToDictionary(entry => entry.CanonicalPath, StringComparer.Ordinal);
        const string statusApplication =
            "src/platform/Modules/PlatformStatus/Application/Idata.Platform.Modules.PlatformStatus.Application.csproj";
        var assetSet = PlatformProjectCatalog.EvaluatePackageAssetAuthority(repoRoot, statusApplication);
        var authority = SyntheticAuthority(catalogByPath, packageAssets: assetSet);

        // (a) owner i panjohur → GJETJE.
        Assert.Contains(ValidateExplicitApproval(
            new ExplicitReferenceApproval("src/platform/Fantazme/Fantazme.csproj", "Mediator.Abstractions", "3.0.2", "Mediator.dll", "arsye"),
            authority), finding => finding.Contains("owner i panjohur", StringComparison.Ordinal));

        // (b) version i gabuar → GJETJE.
        Assert.Contains(ValidateExplicitApproval(
            new ExplicitReferenceApproval(statusApplication, "Mediator.Abstractions", "9.9.9", "Mediator.dll", "arsye"),
            authority), finding => finding.Contains("≠ autoriteti", StringComparison.Ordinal));

        // (c) paketë jashtë autoritetit direkt të owner-it → GJETJE.
        Assert.Contains(ValidateExplicitApproval(
            new ExplicitReferenceApproval(statusApplication, "Npgsql", "10.0.3", "Npgsql.dll", "arsye"),
            authority), finding => finding.Contains("jashtë autoritetit direkt", StringComparison.Ordinal));

        // (d) asset i munguar/gabuar → GJETJE.
        Assert.Contains(ValidateExplicitApproval(
            new ExplicitReferenceApproval(statusApplication, "Mediator.Abstractions", "3.0.2", "Nuk.Ekziston.dll", "arsye"),
            authority), finding => finding.Contains("jashtë asset-set-it të tuple-s", StringComparison.Ordinal));

        // (e) arsyetim bosh → GJETJE.
        Assert.Contains(ValidateExplicitApproval(
            new ExplicitReferenceApproval(statusApplication, "Mediator.Abstractions", "3.0.2", "Mediator.dll", "   "),
            authority), finding => finding.Contains("arsyetim bosh", StringComparison.Ordinal));

        // (f) INVERSE GREEN: approval plotësisht valid.
        Assert.Empty(ValidateExplicitApproval(
            new ExplicitReferenceApproval(statusApplication, "Mediator.Abstractions", "3.0.2", "Mediator.dll",
                "assembly i paketës së aprovuar (package id ≠ assembly name)"),
            authority));
    }

    [Fact]
    public void Referencat_eksplicite_jane_te_lidhura_me_asset_real_ose_boshe()
    {
        foreach (var (assemblyName, approval) in ExplicitApprovedResolvedReferences)
        {
            Assert.False(string.IsNullOrWhiteSpace(approval.Rationale), $"Alias pa arsyetim: {assemblyName}");
            Assert.True(
                IdentitySecurityDependencyInventory.DirectPackageAuthority.TryGetValue(approval.OwnerCanonicalPath, out var authority)
                && authority.TryGetValue(approval.PackageId, out var version)
                && version == approval.PackageVersion,
                $"Alias '{assemblyName}' jashtë autoritetit të owner-it: {approval.PackageId} {approval.PackageVersion}");
        }

        Assert.Empty(ExplicitApprovedResolvedReferences);
    }

    // ── §3-R6: PROVENANCE per-owner nga MSBuild reference resolution ──

    /// <summary>Rrënja REALE e packs (reference-pack/shared-framework) e dotnet-it që po ekzekuton testet.</summary>
    private static readonly string DotnetPacksRoot = DerivePacksRoot();

    private static string DerivePacksRoot()
    {
        // dotnet root derivohet nga vendndodhja REALE e shared-framework-ut
        // që po ekzekuton testet: <root>/shared/Microsoft.NETCore.App/<v>/
        // System.Private.CoreLib.dll → <root>/packs.
        var coreLib = typeof(object).Assembly.Location;
        var dotnetRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(coreLib)!, "..", "..", ".."));
        var packs = Path.Combine(dotnetRoot, "packs");
        return Directory.Exists(packs)
            ? Path.GetFullPath(packs)
            : throw new InvalidOperationException($"Rrënja e packs mungon: {packs}");
    }

    private static readonly string NuGetPackagesRoot =
        Path.GetFullPath(Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"));

    /// <summary>
    /// §1-R7 — AUTORITETI i një owner-i, i derivuar VEÇMAS nga kandidati që
    /// klasifikohet: katalogu + ProjectReference closure + paketat e aprovuara
    /// + **asset-set-et EKZAKTE** (compile/runtime nga `project.assets.json`;
    /// reference assemblies nga `ResolvedTargetingPack/ref`) + output-et
    /// autoritative të projekteve platformë (path + MVID). **Threat boundary
    /// i deklaruar:** csproj/Directory.Build.*/targets të importuara janë
    /// build-definition e review-uar (guard-i NUK pretendon mbrojtje ndaj
    /// ekzekutimit arbitrar të një MSBuild target-i keqdashës); POR asnjë
    /// ReferencePath candidate NUK vetë-autorizohet nga metadata e vet —
    /// duhet të jetë anëtar EKZAKT i një authority-set-i të derivuar veçmas.
    /// </summary>
    internal sealed record OwnerAuthority(
        IReadOnlyDictionary<string, PlatformProjectCatalog.ProjectEntry> CatalogByCanonicalPath,
        IReadOnlySet<string> ProjectClosure,
        IReadOnlyDictionary<string, string> ApprovedPackages,
        PlatformProjectCatalog.PackageAssetAuthority PackageAssets,
        IReadOnlySet<string> FrameworkAssetSet,
        IReadOnlyDictionary<string, Func<string, bool>> PlatformOutputs);

    /// <summary>
    /// §2/§3-R7 — klasifikimi PER-OWNER nga ANËTARËSIA në authority-set-e
    /// (metadata vetëm e drejton kërkimin, nuk autorizon):
    /// (1) **platform**: source-csproj kanonik në katalog DHE në closure ·
    /// FullPath kanonik **EKZAKT** = `CanonicalTargetPath` i atij projekti ·
    /// FusionName simple-name == AssemblyName i vlerësuar · **MVID i file-it
    /// == MVID i output-it autoritativ** (metadata e saktë + path/MVID i
    /// gabuar = GJETJE); (2) **package**: id+version të autorizuara TË ATIJ
    /// OWNER-I DHE FullPath **anëtar EKZAKT** i compile/runtime asset-set-it
    /// të restore-it (path nën `&lt;nuget&gt;/&lt;id&gt;/&lt;version&gt;/` NUK mjafton);
    /// (3) **framework**: FullPath **anëtar EKZAKT** i ref-set-it të
    /// targeting pack-ut (prefix nën packs-root NUK mjafton); (4) **explicit**
    /// alias strukturor i validuar. Çdo gjë tjetër = null → GJETJE.
    /// </summary>
    internal static string? ClassifyResolvedReference(
        PlatformProjectCatalog.ResolvedReference reference, string repoRoot, OwnerAuthority authority)
    {
        var candidatePath = Path.GetFullPath(reference.FullPath);

        // (1) PLATFORM — anëtarësi EKZAKT në output-et autoritative.
        if (reference.ReferenceSourceTarget == "ProjectReference")
        {
            if (reference.SourceProjectFile is null)
            {
                return null;
            }

            var canonicalRepoRoot = Path.GetFullPath(repoRoot);
            var sourceFull = Path.GetFullPath(reference.SourceProjectFile);
            if (!sourceFull.StartsWith(canonicalRepoRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return null;
            }

            var canonicalSource = Path.GetRelativePath(canonicalRepoRoot, sourceFull).Replace(Path.DirectorySeparatorChar, '/');
            if (!authority.CatalogByCanonicalPath.TryGetValue(canonicalSource, out var entry)
                || !authority.ProjectClosure.Contains(canonicalSource)
                || !string.Equals(entry.AssemblyName, reference.AssemblySimpleName, StringComparison.Ordinal))
            {
                return null;
            }

            // Correction C3: autoriteti NUK publikon raw path — jep një
            // PREDIKAT identiteti të pronësuar nga katalogu, që brenda vetes bën
            // verify-before → krahasim EKZAKT → verify-after. Klasifikimi as
            // nuk e sheh path-in autoritativ, as nuk rilexon MVID-in aktual;
            // devijim ose palexueshmëri = i paklasifikueshëm (fail-closed).
            if (!authority.PlatformOutputs.TryGetValue(entry.AssemblyName, out var matchesAuthoritativeOutput)
                || !matchesAuthoritativeOutput(candidatePath))
            {
                return null;
            }

            return "platform";
        }

        // (2) PACKAGE — anëtarësi EKZAKT në asset-set-in e restore-it.
        if (reference.NuGetPackageId is { } packageId && reference.FrameworkReferenceName is null)
        {
            if (reference.NuGetPackageVersion is not { } packageVersion)
            {
                return null;
            }

            if (!authority.ApprovedPackages.TryGetValue(packageId, out var approvedVersion)
                || !string.Equals(approvedVersion, packageVersion, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // §3-R8: anëtarësi EKZAKTE në asset-set-in e TUPLE-s
            // (packageId+resolvedVersion) — jo në unionin e sheshtë të owner-it.
            return authority.PackageAssets.ContainsForTuple(packageId, packageVersion, candidatePath)
                ? "package"
                : null;
        }

        // (3) FRAMEWORK — anëtarësi EKZAKT në ref-set-in e targeting pack-ut.
        if (reference.FrameworkReferenceName is not null)
        {
            return authority.FrameworkAssetSet.Contains(candidatePath) ? "framework" : null;
        }

        // (4) ALIAS EKSPLICIT strukturor (i validuar nga ValidateExplicitApproval).
        if (ExplicitApprovedResolvedReferences.TryGetValue(reference.AssemblySimpleName, out var approval)
            && ValidateExplicitApproval(approval, authority).Count == 0
            && Path.GetFileName(candidatePath) == approval.ExpectedAssetFileName
            && authority.PackageAssets.ContainsForTuple(approval.PackageId, approval.PackageVersion, candidatePath))
        {
            return "explicit";
        }

        return null;
    }

    /// <summary>
    /// §5-R7 (W-2) — validator PURE i një alias-i eksplicit: owner-i duhet të
    /// ekzistojë në autoritetin direkt të paketave, package id+version të
    /// përputhen me atë autoritet, arsyetimi të mos jetë bosh dhe
    /// `ExpectedAssetFileName` të ekzistojë realisht në asset-set-in e
    /// paketave. Kthen listë gjetjesh (bosh = i vlefshëm).
    /// </summary>
    internal static List<string> ValidateExplicitApproval(
        ExplicitReferenceApproval approval, OwnerAuthority authority)
    {
        var findings = new List<string>();
        if (string.IsNullOrWhiteSpace(approval.Rationale))
        {
            findings.Add($"alias '{approval.PackageId}': arsyetim bosh");
        }

        if (!IdentitySecurityDependencyInventory.DirectPackageAuthority
                .TryGetValue(approval.OwnerCanonicalPath, out var ownerDirect))
        {
            findings.Add($"alias '{approval.PackageId}': owner i panjohur '{approval.OwnerCanonicalPath}'");
            return findings;
        }

        if (!ownerDirect.TryGetValue(approval.PackageId, out var authorizedVersion))
        {
            findings.Add($"alias '{approval.PackageId}': paketë jashtë autoritetit direkt të owner-it");
        }
        else if (!string.Equals(authorizedVersion, approval.PackageVersion, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add($"alias '{approval.PackageId}': version {approval.PackageVersion} ≠ autoriteti {authorizedVersion}");
        }

        // §3-R8: asset-i duhet të ekzistojë BRENDA asset-set-it të TUPLE-s
        // (packageId+version) të vetë approval-it — jo diku në unionin e owner-it.
        if (string.IsNullOrWhiteSpace(approval.ExpectedAssetFileName)
            || !authority.PackageAssets.ContainsFileNameForTuple(
                approval.PackageId, approval.PackageVersion, approval.ExpectedAssetFileName))
        {
            findings.Add($"alias '{approval.PackageId}': asset '{approval.ExpectedAssetFileName}' jashtë "
                + $"asset-set-it të tuple-s {approval.PackageId}/{approval.PackageVersion}");
        }

        return findings;
    }

    /// <summary>§2/§3-R7: ndërton autoritetin REAL të një owner-i (asset-set-e nga restore/targeting pack).</summary>
    private static OwnerAuthority BuildOwnerAuthority(
        string repoRoot, string ownerCanonicalPath,
        IReadOnlyDictionary<string, PlatformProjectCatalog.ProjectEntry> catalogByPath,
        IReadOnlyDictionary<string, string> assemblyNameToPath)
        => new(
            catalogByPath,
            PlatformProjectCatalog.ProjectReferenceClosure(repoRoot, ownerCanonicalPath),
            OwnerApprovedPackages(repoRoot, ownerCanonicalPath, assemblyNameToPath),
            PlatformProjectCatalog.EvaluatePackageAssetAuthority(repoRoot, ownerCanonicalPath),
            PlatformProjectCatalog.EvaluateFrameworkAssetAuthority(repoRoot, ownerCanonicalPath),
            PlatformOutputAuthority(repoRoot, catalogByPath));

    /// <summary>
    /// §2-R7 + C3: autoriteti i output-eve platformë = PREDIKATE identiteti të
    /// pronësuara nga katalogu. Asnjë raw path/MVID nuk publikohet këtu:
    /// predikati kryen verify-before → krahasim EKZAKT → verify-after, dhe
    /// kthen thjesht `bool`.
    /// </summary>
    private static IReadOnlyDictionary<string, Func<string, bool>> PlatformOutputAuthority(
        string repoRoot, IReadOnlyDictionary<string, PlatformProjectCatalog.ProjectEntry> catalogByPath)
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();
        var outputs = new Dictionary<string, Func<string, bool>>(StringComparer.Ordinal);
        foreach (var entry in catalogByPath.Values)
        {
            var frozenEntry = entry;
            outputs[entry.AssemblyName] = candidate => MatchesAuthoritativeOutput(frozenEntry, generation, candidate);
        }

        return outputs;
    }

    /// <summary>Predikati i identitetit — drift/palexueshmëri = fail-closed (false).</summary>
    private static bool MatchesAuthoritativeOutput(
        PlatformProjectCatalog.ProjectEntry entry,
        PlatformProjectCatalog.CatalogGeneration generation,
        string candidateFullPath)
    {
        try
        {
            return PlatformProjectCatalog.MatchesVerifiedTarget(entry, generation, candidateFullPath);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Paketat e APROVUARA të NJË owner-i (nga lockfile-i i tij që kalon klasifikimin total §2).</summary>
    private static Dictionary<string, string> OwnerApprovedPackages(
        string repoRoot, string ownerCanonicalPath,
        IReadOnlyDictionary<string, string> assemblyNameToPath)
    {
        var lockfilePath = Path.Combine(
            repoRoot,
            Path.GetDirectoryName(ownerCanonicalPath.Replace('/', Path.DirectorySeparatorChar))!,
            "packages.lock.json");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockfilePath));
        var projectGraph = new IdentitySecurityDependencyInventory.ProjectGraphAuthority(
            assemblyNameToPath,
            PlatformProjectCatalog.ProjectReferenceClosure(repoRoot, ownerCanonicalPath),
            IdentitySecurityDependencyInventory.DirectPackageAuthority,
            ConventionEnforcementTests.DirectProjectReferenceMap(repoRoot, assemblyNameToPath.Values));
        var findings = IdentitySecurityDependencyInventory.AuditPackagesTotal(
            ownerCanonicalPath, document,
            IdentitySecurityDependencyInventory.DirectPackageAuthority[ownerCanonicalPath],
            projectGraph);
        Assert.True(findings.Count == 0,
            $"Lockfile me gjetje s'aprovon asnjë paketë për {ownerCanonicalPath}: {string.Join(" · ", findings)}");

        var approved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var dependency in framework.Value.EnumerateObject())
            {
                if (dependency.Value.TryGetProperty("type", out var typeElement)
                    && typeElement.GetString() is "Direct" or "Transitive" or "CentralTransitive"
                    && dependency.Value.TryGetProperty("resolved", out var resolved)
                    && resolved.GetString() is { } version)
                {
                    approved[dependency.Name] = version;
                }
            }
        }

        return approved;
    }

    /// <summary>
    /// §3-R6 — AUTORITETI: ÇDO ReferencePath i ZGJIDHUR realisht i ÇDO
    /// projekti production klasifikohet nga PROVENANCE per-owner; dhe ÇDO
    /// AssemblyRef i emetuar nga binari duhet të mapojë në SAKTËSISHT NJË
    /// referencë të zgjidhur (missing/ambiguous = GJETJE). Zero fallback te
    /// emri; referencat e papërdorura auditohen njëlloj.
    /// </summary>
    [Fact]
    public void Cdo_ReferencePath_dhe_AssemblyRef_klasifikohet_nga_provenance_per_owner()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        var catalogByPath = catalog.ToDictionary(entry => entry.CanonicalPath, StringComparer.Ordinal);
        var discovered = ConventionEnforcementTests.DiscoverLockfiles(
            repoRoot, Path.Combine(repoRoot, "src", "platform"), Path.Combine(repoRoot, "tests", "platform"));
        var assemblyNameToPath = discovered.ToDictionary(
            pair => pair.Entry.AssemblyName, pair => pair.Entry.CanonicalPath, StringComparer.Ordinal);

        var findings = new List<string>();
        foreach (var entry in catalog)
        {
            var authority = BuildOwnerAuthority(repoRoot, entry.CanonicalPath, catalogByPath, assemblyNameToPath);
            var resolved = PlatformProjectCatalog.EvaluateResolvedReferences(repoRoot, entry.CanonicalPath);

            foreach (var reference in resolved)
            {
                if (ClassifyResolvedReference(reference, repoRoot, authority) is null)
                {
                    findings.Add($"{entry.CanonicalPath} → referencë e PAKLASIFIKUESHME nga provenance: "
                        + $"{reference.AssemblySimpleName} ({reference.ResolvedFrom})");
                }
            }

            // TOTALITETI (§2.5-R7): çdo AssemblyRef i emetuar ↔ SAKTËSISHT një
            // referencë e zgjidhur E KLASIFIKUAR — identitet i plotë (simple
            // name + path autoritativ i klasifikuar), jo thjesht numërim emrash.
            var loaded = PlatformProjectCatalog.LoadVerified(entry);
            foreach (var emitted in loaded.GetReferencedAssemblies())
            {
                var name = emitted.Name ?? string.Empty;
                var matches = resolved.Count(candidate =>
                    string.Equals(candidate.AssemblySimpleName, name, StringComparison.Ordinal)
                    && ClassifyResolvedReference(candidate, repoRoot, authority) is not null);
                if (matches != 1)
                {
                    findings.Add($"{entry.CanonicalPath} → AssemblyRef '{name}' mapon në {matches} referenca të zgjidhura e të klasifikuara (pritej 1)");
                }
            }
        }

        Assert.True(findings.Count == 0, string.Join(" · ", findings));
    }

    /// <summary>
    /// §3-R5: çdo AssemblyRef duhet të jetë SAKTËSISHT njëra nga: (1) assembly
    /// platforme nga katalogu MSBuild-real; (2) assembly i furnizuar nga
    /// package graph-i i aprovuar në autoritetin §2 (ids të paketave të
    /// klasifikuara — të ankoruara te authority map-i i review-uar, JO te
    /// vetë seti i referencave); (3) referencë eksplicite e aprovuar me
    /// arsyetim; (4) assembly i shared-framework-ut real (zgjidhet dhe
    /// verifikohet vendndodhja nën rrënjën e framework-ut). Çdo gjë tjetër
    /// (përfshirë HintPath custom/të panjohur) = null → GJETJE. Zero
    /// `continue` para klasifikimit; prefix-et s'janë autoritet.
    /// </summary>
    internal static string? ClassifyAssemblyReference(
        string referenceName,
        IReadOnlySet<string> platformAssemblyNames,
        IReadOnlySet<string> approvedPackageIds,
        IReadOnlyDictionary<string, string> explicitApproved)
    {
        if (platformAssemblyNames.Contains(referenceName))
        {
            return "platform";
        }

        if (approvedPackageIds.Contains(referenceName))
        {
            return "package";
        }

        if (explicitApproved.ContainsKey(referenceName))
        {
            return "explicit";
        }

        try
        {
            var loaded = PlatformProjectCatalog.LoadReferencedAssembly(new AssemblyName(referenceName));
            if (!string.IsNullOrEmpty(loaded.Location)
                && Path.GetFullPath(loaded.Location)
                    .StartsWith(SharedFrameworkRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return "framework";
            }
        }
        catch (Exception)
        {
            // I pazgjidhshëm → i paklasifikuar (gjetje te thirrësi).
        }

        return null;
    }

    /// <summary>
    /// Ids e paketave të APROVUARA: bashkimi i hyrjeve të çdo lockfile-i që
    /// kalon klasifikimin TOTAL §2 (i ankoruar te DirectPackageAuthority i
    /// review-uar — lockfile me gjetje NUK kontribuon asgjë, fail-closed).
    /// </summary>
    private static HashSet<string> ApprovedPackageIds()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var discovered = ConventionEnforcementTests.DiscoverLockfiles(
            repoRoot,
            Path.Combine(repoRoot, "src", "platform"),
            Path.Combine(repoRoot, "tests", "platform"));
        var assemblyNameToPath = discovered.ToDictionary(
            pair => pair.Entry.AssemblyName, pair => pair.Entry.CanonicalPath, StringComparer.Ordinal);

        var approved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entry, lockfile) in discovered)
        {
            var projectGraph = new IdentitySecurityDependencyInventory.ProjectGraphAuthority(
                assemblyNameToPath,
                PlatformProjectCatalog.ProjectReferenceClosure(repoRoot, entry.CanonicalPath),
                IdentitySecurityDependencyInventory.DirectPackageAuthority,
                ConventionEnforcementTests.DirectProjectReferenceMap(repoRoot, assemblyNameToPath.Values));
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockfile!));
            var findings = IdentitySecurityDependencyInventory.AuditPackagesTotal(
                entry.CanonicalPath, document,
                IdentitySecurityDependencyInventory.DirectPackageAuthority[entry.CanonicalPath],
                projectGraph);
            if (findings.Count > 0)
            {
                continue; // fail-closed: lockfile me gjetje s'aprovon asnjë id.
            }

            foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                foreach (var dependency in framework.Value.EnumerateObject())
                {
                    if (dependency.Value.TryGetProperty("type", out var typeElement)
                        && typeElement.GetString() is "Direct" or "Transitive" or "CentralTransitive")
                    {
                        approved.Add(dependency.Name);
                    }
                }
            }
        }

        return approved;
    }

    /// <summary>
    /// §3-R5 — testi AUTORITET: ÇDO AssemblyRef i ÇDO assembly platforme
    /// klasifikohet; referencë e paklasifikueshme = GJETJE (zero fall-through).
    /// </summary>
    [Fact]
    public void Cdo_AssemblyRef_i_platformes_klasifikohet_totalisht()
    {
        var platformNames = PlatformProjectCatalog.DiscoverProduction()
            .Select(entry => entry.AssemblyName)
            .ToHashSet(StringComparer.Ordinal);
        var approvedPackages = ApprovedPackageIds();
        Assert.NotEmpty(approvedPackages);

        var findings = new List<string>();
        foreach (var assembly in AllPlatformAssemblies)
        {
            var owner = assembly.GetName().Name ?? string.Empty;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name ?? string.Empty;
                if (ClassifyAssemblyReference(name, platformNames, approvedPackages, ExplicitApprovedReferences) is null)
                {
                    findings.Add($"{owner} → referencë e PAKLASIFIKUESHME '{name}'");
                }
            }
        }

        Assert.True(findings.Count == 0, string.Join(" · ", findings));
    }

    /// <summary>§3-R5 — fixtures diskriminuese të klasifikuesit.</summary>
    [Fact]
    public void Klasifikuesi_i_AssemblyRefs_kap_te_panjohurat_dhe_lejon_te_njohurat()
    {
        var platformNames = PlatformProjectCatalog.DiscoverProduction()
            .Select(entry => entry.AssemblyName)
            .ToHashSet(StringComparer.Ordinal);
        var approvedPackages = ApprovedPackageIds();

        // Të panjohura (provider të jashtëm / HintPath custom / sintetikë) → GJETJE.
        foreach (var unknown in (string[])
            ["Keycloak.AuthServices.Authentication", "Sintetik.Provider.Client", "CustomVendor.HintPath.Lib"])
        {
            Assert.Null(DependencyDirectionTests.ClassifyAssemblyReference(
                unknown, platformNames, approvedPackages, ExplicitApprovedReferences));
        }

        // Të njohura → kategoria e saktë.
        Assert.Equal("platform", ClassifyAssemblyReference(
            "Idata.Platform.Persistence", platformNames, approvedPackages, ExplicitApprovedReferences));
        Assert.Equal("package", ClassifyAssemblyReference(
            "Npgsql", platformNames, approvedPackages, ExplicitApprovedReferences));
        Assert.Equal("framework", ClassifyAssemblyReference(
            "System.Runtime", platformNames, approvedPackages, ExplicitApprovedReferences));

        // Kategoria 'explicit' funksionon vetëm me hyrje të deklaruar + arsyetim.
        var syntheticExplicit = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Vendor.I.Aprovuar"] = "arsyetim sintetik i testit",
        };
        Assert.Equal("explicit", ClassifyAssemblyReference(
            "Vendor.I.Aprovuar", platformNames, approvedPackages, syntheticExplicit));
        Assert.Null(ClassifyAssemblyReference(
            "Vendor.I.Aprovuar", platformNames, approvedPackages, ExplicitApprovedReferences));
    }

    /// <summary>
    /// §3-R5 — owner assemblies të DERIVUARA: StoreOwnerCanonicalPaths →
    /// entry i katalogut → AssemblyName i vlerësuar nga MSBuild; secili path
    /// zgjidhet SAKTËSISHT një herë; zero duplikate; asnjë literal basename.
    /// </summary>
    private static string[] DerivedStoreOwnerAssemblyNames()
        => [.. PlatformProjectCatalog.StoreOwnerCanonicalPaths
            .Select(path => PlatformProjectCatalog.DiscoverProduction()
                .Single(entry => entry.CanonicalPath == path).AssemblyName)];

    /// <summary>
    /// §2A/§6-R6 — fixture END-TO-END: një DLL sintetike me AssemblyName=Npgsql
    /// referencohet nga një projekt konsumator me `<Reference><HintPath>` REAL
    /// (jashtë folder-it të paketës NuGet) + TypeRef real; resolution-i REAL i
    /// MSBuild-it e zgjidh, dhe klasifikuesi i provenance-s e refuzon EDHE pse
    /// (a) emri i thjeshtë është 'Npgsql' dhe (b) 'Npgsql' është në setin e
    /// paketave të aprovuara — sepse path-i nuk është nën folder-in id/version
    /// të paketës reale. Emri/versioni ≠ autoritet; path/provenance vendos.
    /// </summary>
    [Fact]
    public void Provenance_end_to_end_HintPath_Npgsql_custom_eshte_gjetje()
    {
        var temporary = Directory.CreateTempSubdirectory("idata_prov_e2e_");
        try
        {
            // (1) DLL sintetike me identitet assembly = 'Npgsql'.
            var fakeDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "fake")).FullName;
            File.WriteAllText(Path.Combine(fakeDirectory, "Fake.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
                + "<AssemblyName>Npgsql</AssemblyName><AssemblyVersion>1.0.0.0</AssemblyVersion>"
                + "<Deterministic>true</Deterministic></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(fakeDirectory, "Marker.cs"),
                "namespace Npgsql { public sealed class SyntheticMarker { } }");
            RunDotnet(fakeDirectory, ["build", "-c", "Debug", "-nodeReuse:false"]);
            var fakeDll = Path.Combine(fakeDirectory, "bin", "Debug", "net10.0", "Npgsql.dll");
            Assert.True(File.Exists(fakeDll), "DLL-ja sintetike Npgsql nuk u ndërtua.");

            // (2) Konsumator me HintPath REAL drejt DLL-së custom + TypeRef real.
            var consumerDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "consumer")).FullName;
            File.WriteAllText(Path.Combine(consumerDirectory, "Consumer.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>"
                + "<ItemGroup><Reference Include=\"Npgsql\"><HintPath>" + fakeDll + "</HintPath></Reference></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(consumerDirectory, "Use.cs"),
                "public static class Use { public static object M() => new Npgsql.SyntheticMarker(); }");

            // (3) Resolution REAL i MSBuild-it.
            var resolved = ResolveConsumerReferences(Path.Combine(consumerDirectory, "Consumer.csproj"));
            var npgsql = Assert.Single(resolved, reference =>
                string.Equals(reference.AssemblySimpleName, "Npgsql", StringComparison.Ordinal));
            Assert.Equal(fakeDll, npgsql.FullPath); // provenance = HintPath custom, jo NuGet.

            // (4) Klasifikimi: refuzohet edhe me emër+version 'të aprovuar'.
            var repoRoot = PlatformProjectCatalog.RepoRoot();
            var catalogByPath = PlatformProjectCatalog.DiscoverProduction()
                .ToDictionary(entry => entry.CanonicalPath, StringComparer.Ordinal);
            var approvedWithNpgsql = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Npgsql"] = "1.0.0" };
            Assert.Null(ClassifyResolvedReference(
                npgsql, repoRoot, SyntheticAuthority(catalogByPath, approvedPackages: approvedWithNpgsql)));
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    /// <summary>
    /// §2A/§6-R6 — fixtures diskriminuese PURE mbi provenance (records
    /// sintetikë): Mediator nga HintPath jashtë asset-it real, framework nga
    /// path jashtë packs-root, dhe platform decoy me source-project jo-real
    /// = të gjitha GJETJE; inverse GREEN provohen nga testi i autoritetit real.
    /// </summary>
    /// <summary>§R7: authority SINTETIK i injektueshëm për fixtures pure.</summary>
    private static OwnerAuthority SyntheticAuthority(
        IReadOnlyDictionary<string, PlatformProjectCatalog.ProjectEntry> catalogByPath,
        IReadOnlySet<string>? closure = null,
        IReadOnlyDictionary<string, string>? approvedPackages = null,
        PlatformProjectCatalog.PackageAssetAuthority? packageAssets = null,
        IReadOnlySet<string>? frameworkAssets = null,
        IReadOnlyDictionary<string, Func<string, bool>>? platformOutputs = null)
        => new(
            catalogByPath,
            closure ?? new HashSet<string>(StringComparer.Ordinal),
            approvedPackages ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            packageAssets ?? new PlatformProjectCatalog.PackageAssetAuthority(
                new Dictionary<(string, string), IReadOnlySet<string>>(),
                new Dictionary<string, (string, string)>(StringComparer.Ordinal)),
            frameworkAssets ?? new HashSet<string>(StringComparer.Ordinal),
            platformOutputs ?? new Dictionary<string, Func<string, bool>>(StringComparer.Ordinal));

    [Fact]
    public void R7_RED_platform_decoy_me_metadata_te_sakte_por_path_tjeter()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        var catalogByPath = catalog.ToDictionary(entry => entry.CanonicalPath, StringComparer.Ordinal);
        const string persistencePath = "src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj";
        var closure = new HashSet<string>(StringComparer.Ordinal) { persistencePath };
        var entry = catalog.Single(candidate => candidate.CanonicalPath == persistencePath);
        var generation = PlatformProjectCatalog.CurrentGeneration();
        var outputs = new Dictionary<string, Func<string, bool>>(StringComparer.Ordinal)
        {
            // C3: i njëjti predikat identiteti si në production — zero raw path në test.
            [entry.AssemblyName] = candidate => MatchesAuthoritativeOutput(entry, generation, candidate),
        };
        var authority = SyntheticAuthority(catalogByPath, closure, platformOutputs: outputs);

        // Metadata KRESH e saktë (ProjectReference + source-csproj real në closure +
        // simple name i saktë), POR FullPath = decoy jashtë output-it autoritativ.
        var decoy = Path.Combine(Path.GetTempPath(), "idata-syn-decoy", "Idata.Platform.Persistence.dll");
        Assert.Null(ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                decoy, "Idata.Platform.Persistence", "Idata.Platform.Persistence, Version=1.0.0.0",
                "ProjectReference", null, Path.Combine(repoRoot, persistencePath.Replace('/', Path.DirectorySeparatorChar)),
                null, null, null, decoy),
            repoRoot, authority));

        // INVERSE GREEN (C4, §3.4): asnjë path nuk nxirret më nga katalogu.
        // Merret ResolvedReference-i REAL i vlerësuar nga MSBuild për projektin
        // që e referon Persistence-in dhe klasifikohet ai — pra vetë të dhënat
        // reale të build-it, jo një path i publikuar nga autoriteti.
        const string apiPath = "src/platform/Idata.Platform.Api/Idata.Platform.Api.csproj";
        var realProjectReference = PlatformProjectCatalog
            .EvaluateResolvedReferences(repoRoot, apiPath)
            .Single(reference => reference.ReferenceSourceTarget == "ProjectReference"
                && reference.AssemblySimpleName == entry.AssemblyName);
        Assert.Equal("platform", ClassifyResolvedReference(realProjectReference, repoRoot, authority));
    }

    /// <summary>
    /// §2/§4A-R8 — CROSS-PACKAGE SWAP: metadata e paketës A (Npgsql/10.0.3)
    /// e kombinuar me asset-in REAL të paketës B (Microsoft.EntityFrameworkCore.dll)
    /// TË TË NJËJTIT owner. Të dyja pjesët janë individualisht reale, POR
    /// tuple-ja (packageId+version → asset) është e rreme → duhet GJETJE.
    /// Inverse: i njëjti metadata me asset-in e VET → "package".
    /// </summary>
    [Fact]
    public void Cross_package_swap_refuzohet_sipas_tuple_s_packageId_version()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var catalogByPath = PlatformProjectCatalog.DiscoverProduction()
            .ToDictionary(entry => entry.CanonicalPath, StringComparer.Ordinal);
        const string persistence = "src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj";
        var approved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Npgsql"] = "10.0.3",
            ["Microsoft.EntityFrameworkCore"] = "10.0.10",
        };
        var assets = PlatformProjectCatalog.EvaluatePackageAssetAuthority(repoRoot, persistence);
        var authority = SyntheticAuthority(catalogByPath, approvedPackages: approved, packageAssets: assets);

        // Asset REAL i paketës TJETËR (EF Core) — i pranishëm në asset-set-in e owner-it.
        var efAsset = assets.Assets.First(asset =>
            asset.EndsWith($"{Path.DirectorySeparatorChar}Microsoft.EntityFrameworkCore.dll", StringComparison.Ordinal));
        var npgsqlAsset = assets.Assets.First(asset =>
            asset.EndsWith($"{Path.DirectorySeparatorChar}Npgsql.dll", StringComparison.Ordinal));

        // (a) SWAP: metadata Npgsql/10.0.3 + asset EF Core → GJETJE.
        Assert.Null(ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                efAsset, "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore, Version=10.0.0.0",
                "ResolveAssemblyReference", "{HintPathFromItem}", null, "Npgsql", "10.0.3", null, efAsset),
            repoRoot, authority));

        // (b) SWAP i kundërt: metadata EF Core/10.0.10 + asset Npgsql → GJETJE.
        Assert.Null(ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                npgsqlAsset, "Npgsql", "Npgsql, Version=10.0.3.0",
                "ResolveAssemblyReference", "{HintPathFromItem}", null, "Microsoft.EntityFrameworkCore", "10.0.10", null, npgsqlAsset),
            repoRoot, authority));

        // (c) VERSION SWAP: paketë e saktë, version i pa-autorizuar për atë asset → GJETJE.
        Assert.Null(ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                npgsqlAsset, "Npgsql", "Npgsql, Version=9.9.9.0",
                "ResolveAssemblyReference", "{HintPathFromItem}", null, "Npgsql", "9.9.9", null, npgsqlAsset),
            repoRoot, authority));

        // (d) INVERSE GREEN: tuple e saktë (Npgsql/10.0.3 → Npgsql.dll).
        Assert.Equal("package", ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                npgsqlAsset, "Npgsql", "Npgsql, Version=10.0.3.0",
                "ResolveAssemblyReference", "{HintPathFromItem}", null, "Npgsql", "10.0.3", null, npgsqlAsset),
            repoRoot, authority));

        // (e) INVERSE GREEN: tuple e saktë e paketës tjetër.
        Assert.Equal("package", ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                efAsset, "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore, Version=10.0.0.0",
                "ResolveAssemblyReference", "{HintPathFromItem}", null, "Microsoft.EntityFrameworkCore", "10.0.10", null, efAsset),
            repoRoot, authority));
    }

    /// <summary>
    /// §4B-R8 (W-2) — approval-i duhet lidhur me tuple-n EKZAKTE: asset i
    /// paketës TJETËR (edhe pse real dhe i të njëjtit owner) = GJETJE.
    /// </summary>
    [Fact]
    public void Explicit_approval_lidhet_me_asset_in_e_tuple_s_se_vet()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var catalogByPath = PlatformProjectCatalog.DiscoverProduction()
            .ToDictionary(entry => entry.CanonicalPath, StringComparer.Ordinal);
        const string persistence = "src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj";
        var assets = PlatformProjectCatalog.EvaluatePackageAssetAuthority(repoRoot, persistence);
        var authority = SyntheticAuthority(catalogByPath, packageAssets: assets);

        // (a) CROSS-SWAP: Npgsql/10.0.3 me asset të EF Core → GJETJE.
        Assert.Contains(ValidateExplicitApproval(
            new ExplicitReferenceApproval(persistence, "Npgsql", "10.0.3",
                "Microsoft.EntityFrameworkCore.dll", "arsye sintetike"),
            authority), finding => finding.Contains("asset", StringComparison.OrdinalIgnoreCase));

        // (b) INVERSE GREEN: Npgsql/10.0.3 me asset-in e vet.
        Assert.Empty(ValidateExplicitApproval(
            new ExplicitReferenceApproval(persistence, "Npgsql", "10.0.3", "Npgsql.dll", "arsye sintetike"),
            authority));
    }

    [Fact]
    public void R7_RED_package_asset_jashte_compile_set_por_brenda_package_root()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var catalogByPath = PlatformProjectCatalog.DiscoverProduction()
            .ToDictionary(entry => entry.CanonicalPath, StringComparer.Ordinal);
        var approved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Npgsql"] = "10.0.3" };
        // Asset-set-i AUTORITATIV REAL i Persistence (nga project.assets.json).
        var assetSet = PlatformProjectCatalog.EvaluatePackageAssetAuthority(
            repoRoot, "src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj");
        var authority = SyntheticAuthority(catalogByPath, approvedPackages: approved, packageAssets: assetSet);

        // Brenda folder-it id/version të paketës, POR JO asset compile i deklaruar.
        var outsideAsset = Path.Combine(NuGetPackagesRoot, "npgsql", "10.0.3", "README.md");
        Assert.Null(ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                outsideAsset, "Npgsql", "Npgsql, Version=10.0.3.0",
                "ResolveAssemblyReference", "{HintPathFromItem}", null, "Npgsql", "10.0.3", null, outsideAsset),
            repoRoot, authority));

        // INVERSE GREEN: asset REAL i compile-set-it.
        var realAsset = assetSet.Assets.First(asset =>
            asset.Contains($"{Path.DirectorySeparatorChar}npgsql{Path.DirectorySeparatorChar}10.0.3{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && asset.EndsWith("Npgsql.dll", StringComparison.Ordinal));
        Assert.Equal("package", ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                realAsset, "Npgsql", "Npgsql, Version=10.0.3.0",
                "ResolveAssemblyReference", "{HintPathFromItem}", null, "Npgsql", "10.0.3", null, realAsset),
            repoRoot, authority));
    }

    [Fact]
    public void R7_RED_framework_asset_jashte_ref_set_por_brenda_packs_root()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var catalogByPath = PlatformProjectCatalog.DiscoverProduction()
            .ToDictionary(entry => entry.CanonicalPath, StringComparer.Ordinal);

        // Ref-set-i AUTORITATIV REAL (nga ResolvedTargetingPack i owner-it).
        var frameworkSet = PlatformProjectCatalog.EvaluateFrameworkAssetAuthority(
            repoRoot, "src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj");
        var authority = SyntheticAuthority(catalogByPath, frameworkAssets: frameworkSet);

        // Nën packs-root, POR në analyzers/ — jashtë ref-set-it të targeting pack-ut.
        var analyzerAsset = Path.Combine(DotnetPacksRoot, "Microsoft.NETCore.App.Ref", "10.0.10",
            "analyzers", "dotnet", "cs", "Microsoft.Interop.ComInterfaceGenerator.dll");
        Assert.Null(ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                analyzerAsset, "Microsoft.Interop.ComInterfaceGenerator",
                "Microsoft.Interop.ComInterfaceGenerator, Version=10.0.0.0",
                "ResolveAssemblyReference", "{RawFileName}", null, null, null, "Microsoft.NETCore.App", analyzerAsset),
            repoRoot, authority));

        // INVERSE GREEN: reference assembly REAL nga ref-set-i.
        var realRef = frameworkSet.First(asset => asset.EndsWith("System.Runtime.dll", StringComparison.Ordinal));
        Assert.Equal("framework", ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                realRef, "System.Runtime", "System.Runtime, Version=10.0.0.0",
                "ResolveAssemblyReference", "{RawFileName}", null, null, null, "Microsoft.NETCore.App", realRef),
            repoRoot, authority));
    }

    [Fact]
    public void ClassifyResolvedReference_refuzon_provenance_te_rreme()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var catalogByPath = PlatformProjectCatalog.DiscoverProduction()
            .ToDictionary(entry => entry.CanonicalPath, StringComparer.Ordinal);
        var approved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mediator.Abstractions"] = "3.0.2",
            ["Npgsql"] = "10.0.3",
        };
        var authority = SyntheticAuthority(catalogByPath, approvedPackages: approved);
        // Path sintetik "custom" i ndërtuar në runtime (zero literal absolut në burim).
        static string Custom(string fileName) => Path.Combine(Path.GetTempPath(), "idata-syn-custom", fileName);

        // Mediator nga HintPath custom (jo NuGetPackageId) → null.
        Assert.Null(ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                Custom("Mediator.dll"), "Mediator", "Mediator, Version=3.0.0.0",
                "ResolveAssemblyReference", "{HintPathFromItem}", null, null, null, null, Custom("Mediator.dll")),
            repoRoot, authority));

        // System.Runtime nga path jashtë packs-root → null.
        Assert.Null(ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                Custom("System.Runtime.dll"), "System.Runtime", "System.Runtime, Version=10.0.0.0",
                "ResolveAssemblyReference", "{RawFileName}", null, null, null, "Microsoft.NETCore.App", Custom("System.Runtime.dll")),
            repoRoot, authority));

        // Platform decoy: ProjectReference me source-project që s'ekziston në katalog → null.
        Assert.Null(ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                Path.Combine(repoRoot, "src", "platform", "Idata.Platform.Persistence", "bin", "Debug", "net10.0", "Idata.Platform.Persistence.dll"),
                "Idata.Platform.Persistence", "Idata.Platform.Persistence, Version=1.0.0.0",
                "ProjectReference", null, Path.Combine(repoRoot, "src", "platform", "Fake", "Fake.csproj"),
                null, null, null, null),
            repoRoot, authority));

        // Package i aprovuar me emër+version por PATH jashtë folder-it të paketës → null.
        Assert.Null(ClassifyResolvedReference(
            new PlatformProjectCatalog.ResolvedReference(
                Custom("Npgsql.dll"), "Npgsql", "Npgsql, Version=10.0.3.0",
                "ResolveAssemblyReference", "{HintPathFromItem}", null, "Npgsql", "10.0.3", null, Custom("Npgsql.dll")),
            repoRoot, authority));
    }

    /// <summary>Ndërton një projekt sintetik në disk (jashtë repo-s) me `dotnet build`, node-reuse OFF.</summary>
    private static void RunDotnet(string workingDirectory, string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start(), "dotnet nuk u nis dot.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(180_000), "dotnet timeout (180s).");
        Assert.True(process.ExitCode == 0,
            $"dotnet {string.Join(' ', arguments)} exit {process.ExitCode}: {stdout.GetAwaiter().GetResult()} {stderr.GetAwaiter().GetResult()}");
    }

    /// <summary>Resolution REAL i referencave për një csproj sintetik (jashtë repo-s), node-reuse OFF.</summary>
    private static PlatformProjectCatalog.ResolvedReference[] ResolveConsumerReferences(string absoluteCsprojPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(absoluteCsprojPath)!,
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        foreach (var argument in (string[])
            ["msbuild", "-nodeReuse:false", absoluteCsprojPath, "-nologo", "-restore",
             "-t:ResolveReferences", "-getItem:ReferencePath", "-p:Configuration=Debug"])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start(), "dotnet msbuild nuk u nis dot.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(180_000), "dotnet msbuild timeout (180s).");
        var output = stdout.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, $"msbuild exit {process.ExitCode}: {stderr.GetAwaiter().GetResult()}");

        using var document = System.Text.Json.JsonDocument.Parse(output);
        return [.. document.RootElement.GetProperty("Items").GetProperty("ReferencePath").EnumerateArray()
            .Select(item =>
            {
                string? Meta(string name) => item.TryGetProperty(name, out var value)
                    && value.ValueKind == System.Text.Json.JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text ? text : null;
                var fusion = Meta("FusionName");
                return new PlatformProjectCatalog.ResolvedReference(
                    Path.GetFullPath(item.GetProperty("Identity").GetString()!),
                    fusion?.Split(',')[0].Trim() ?? Path.GetFileNameWithoutExtension(item.GetProperty("Identity").GetString()!),
                    fusion, Meta("ReferenceSourceTarget"), Meta("ResolvedFrom"), Meta("MSBuildSourceProjectFile"),
                    Meta("NuGetPackageId"), Meta("NuGetPackageVersion"), Meta("FrameworkReferenceName"), Meta("OriginalItemSpec"));
            })];
    }

    [Fact]
    public void Owner_assemblies_derivohen_nga_katalogu_pa_literale()
    {
        var owners = DerivedStoreOwnerAssemblyNames();
        Assert.Equal(2, owners.Length);
        Assert.Equal(2, owners.Distinct(StringComparer.Ordinal).Count());
        // Ndryshimi i AssemblyName kapet nga spec-i i pritur në
        // ConventionEnforcementTests.Katalogu_production_permban_saktesisht_dy_owner_at_kanonike
        // (barazi me literale të pritura) — këtu derivimi mbetet pa literale.
    }

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
        // §3-R5: owner-at DERIVOHEN nga path-et kanonike përmes katalogut
        // MSBuild-real — zero literal basename/emri (heqja e `approvedProjects`).
        var approvedProjects = DerivedStoreOwnerAssemblyNames();

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
