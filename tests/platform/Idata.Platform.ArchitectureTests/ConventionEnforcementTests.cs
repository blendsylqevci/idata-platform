using Idata.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Reflection.Metadata;
using Xunit;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Zbatimi MEKANIK i konventës së ADR-029 (Increment A — PlatformStatus
/// pilot): vendosja e paketave të mediator-it, mungesa e MediatR/pipeline
/// behaviors, thin-controller boundary, BaseApiController module-local dhe
/// mungesa e minimal API-t të vjetër. Kontrollet mbi paketa bëhen mbi
/// csproj/props REALE të repo-s (PrivateAssets=all e bën SourceGenerator-in
/// të padukshëm në metadata të assembly-ve); kontrollet strukturore mbi
/// metadata reale të assembly-ve.
/// </summary>
public sealed class ConventionEnforcementTests
{
    private static readonly string PlatformRoot = FindPlatformRoot();

    private static string FindPlatformRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "platform")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "platform");
    }

    private static string[] AllProjectFiles() =>
        Directory.GetFiles(PlatformRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

    [Fact]
    public void Mediator_SourceGenerator_vetem_ne_API_host()
    {
        foreach (var project in AllProjectFiles())
        {
            var content = File.ReadAllText(project);
            if (content.Contains("Mediator.SourceGenerator", StringComparison.Ordinal))
            {
                Assert.EndsWith("Idata.Platform.Api.csproj", project, StringComparison.Ordinal);
                Assert.Contains("<PrivateAssets>all</PrivateAssets>", content, StringComparison.Ordinal);
            }
        }

        Assert.Contains(AllProjectFiles(), project =>
            project.EndsWith("Idata.Platform.Api.csproj", StringComparison.Ordinal)
            && File.ReadAllText(project).Contains("Mediator.SourceGenerator", StringComparison.Ordinal));
    }

    [Fact]
    public void Mediator_Abstractions_vetem_ne_projektet_e_aprovuara()
    {
        string[] approvedSuffixes =
            [Path.Combine("Application", "Idata.Platform.Modules.PlatformStatus.Application.csproj"),
             Path.Combine("Presentation", "Idata.Platform.Modules.PlatformStatus.Presentation.csproj")];
        foreach (var project in AllProjectFiles())
        {
            if (File.ReadAllText(project).Contains("\"Mediator.Abstractions\"", StringComparison.Ordinal))
            {
                Assert.Contains(approvedSuffixes, suffix => project.EndsWith(suffix, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void Zero_MediatR_dhe_zero_AutoMapper_dhe_zero_Swashbuckle()
    {
        var props = File.ReadAllText(Path.Combine(PlatformRoot, "Directory.Packages.props"));
        foreach (var forbidden in (string[])["MediatR", "AutoMapper", "Swashbuckle"])
        {
            Assert.DoesNotContain(forbidden, props, StringComparison.OrdinalIgnoreCase);
            foreach (var project in AllProjectFiles())
            {
                Assert.DoesNotContain(forbidden, File.ReadAllText(project), StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// ADR-028 B.1 — vendosja e paketave identity në nivel csproj:
    /// dy paketat STORE (Identity.EFCore + OpenIddict.EFCore) lejohen VETËM
    /// në Persistence (storage mapping) dhe Modules/Identity/Infrastructure
    /// (wiring); API host-i i konsumon vetëm si composition root përmes
    /// project reference — pa PackageReference direkt.
    /// </summary>
    [Fact]
    public void Identity_store_paketat_vetem_ne_csproj_et_e_aprovuara()
    {
        string[] storePackages =
            ["\"Microsoft.AspNetCore.Identity.EntityFrameworkCore\"", "\"OpenIddict.EntityFrameworkCore\""];

        // §3-R3: owner-at njihen VETËM me path kanonik nga katalogu — jo suffix/basename.
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        foreach (var entry in catalog)
        {
            var content = File.ReadAllText(Path.Combine(repoRoot, entry.CanonicalPath.Replace('/', Path.DirectorySeparatorChar)));
            if (storePackages.Any(package => content.Contains(package, StringComparison.Ordinal)))
            {
                Assert.True(PlatformProjectCatalog.IsStoreOwner(entry),
                    $"Paketë store në csproj JO-owner: {entry.CanonicalPath}");
            }
        }

        // Të dyja paketat duhet të ekzistojnë realisht në të dy projektet e
        // aprovuara — allowlist-i nuk është i vdekur.
        foreach (var ownerPath in PlatformProjectCatalog.StoreOwnerCanonicalPaths)
        {
            var entry = Assert.Single(catalog, candidate => candidate.CanonicalPath == ownerPath);
            var content = File.ReadAllText(Path.Combine(repoRoot, entry.CanonicalPath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.All(storePackages, package => Assert.Contains(package, content, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// B.1 = STORE foundation: paketat e protokollit OIDC (Server/Validation/
    /// Client/AspNetCore metapackage), Identity UI dhe çdo provider komercial
    /// (Duende/IdentityServer/Auth0/Okta) = TË NDALUARA në props + çdo csproj.
    /// </summary>
    [Fact]
    public void Zero_paketa_identity_protokolli_ose_provider_te_paaprovuar()
    {
        string[] forbidden =
            ["OpenIddict.AspNetCore", "OpenIddict.Server", "OpenIddict.Validation", "OpenIddict.Client",
             "Microsoft.AspNetCore.Identity.UI", "IdentityServer", "Duende", "Auth0", "Okta"];

        var props = File.ReadAllText(Path.Combine(PlatformRoot, "Directory.Packages.props"));
        foreach (var name in forbidden)
        {
            Assert.DoesNotContain(name, props, StringComparison.OrdinalIgnoreCase);
            foreach (var project in AllProjectFiles())
            {
                Assert.DoesNotContain(name, File.ReadAllText(project), StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Zbulim DINAMIK me identitet kanonik (§3-R3): çdo csproj nën rrënjët e
    /// kërkimit — i identifikuar me path kanonik repo-relative përmes
    /// <see cref="PlatformProjectCatalog"/> — duhet të ketë lockfile ngjitur.
    /// </summary>
    internal static (PlatformProjectCatalog.ProjectEntry Entry, string? Lockfile)[] DiscoverLockfiles(
        string repoRoot, params string[] searchRoots)
        => [.. searchRoots
            .SelectMany(root => PlatformProjectCatalog.Discover(repoRoot, root))
            .Select(entry =>
            {
                var lockfile = Path.Combine(
                    repoRoot,
                    Path.GetDirectoryName(entry.CanonicalPath.Replace('/', Path.DirectorySeparatorChar))!,
                    "packages.lock.json");
                return (entry, File.Exists(lockfile) ? lockfile : null);
            })];

    /// <summary>
    /// Inventari FAIL-CLOSED mbi TË GJITHA lockfiles platform/test
    /// (§4-R3 — model CLOSURE deny-by-default, zero prefix-`continue`): çdo
    /// csproj pa lockfile = FAIL; çdo shkelje closure/familjeje = FAIL;
    /// owner-at (VETËM me path kanonik) mbajnë të dyja rrënjët Direct;
    /// AssemblyName unik edhe në bashkimin src+tests.
    /// </summary>
    [Fact]
    public void Inventari_identity_security_i_TE_GJITHA_lockfiles_eshte_fail_closed()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var discovered = DiscoverLockfiles(
            repoRoot, PlatformRoot, Path.Combine(repoRoot, "tests", "platform"));

        Assert.NotEmpty(discovered);
        var duplicateAssemblies = discovered.GroupBy(pair => pair.Entry.AssemblyName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        Assert.True(duplicateAssemblies.Length == 0,
            $"AssemblyName i dyfishtë në src+tests: {string.Join(", ", duplicateAssemblies)}");

        foreach (var (entry, lockfile) in discovered)
        {
            // Projekt i ri pa lockfile nuk shpëton kurrë nga inventari.
            Assert.True(lockfile is not null, $"csproj PA packages.lock.json: {entry.CanonicalPath}");
        }

        var audited = 0;
        foreach (var (entry, lockfile) in discovered)
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockfile!));
            var findings = IdentitySecurityDependencyInventory.Audit(entry.CanonicalPath, document);
            Assert.True(findings.Count == 0, string.Join(" · ", findings));

            if (PlatformProjectCatalog.IsStoreOwner(entry))
            {
                // Owner-at: të dyja rrënjët e aprovuara prezente si Direct.
                foreach (var root in IdentitySecurityDependencyInventory.ApprovedRoots.Keys)
                {
                    var dependency = document.RootElement.GetProperty("dependencies").EnumerateObject()
                        .SelectMany(framework => framework.Value.EnumerateObject())
                        .Single(candidate => candidate.Name == root);
                    Assert.Equal("Direct", dependency.Value.GetProperty("type").GetString());
                }
            }

            audited++;
        }

        // Count i DERIVUAR: çdo lockfile i zbuluar u auditua; të dy owner-at u gjetën.
        Assert.Equal(discovered.Length, audited);
        Assert.Equal(2, discovered.Count(pair => PlatformProjectCatalog.IsStoreOwner(pair.Entry)));
    }

    /// <summary>
    /// Meta-testi i PLOTËSISË së manifestit (§4-R3): closure-i REAL i
    /// llogaritur nga të gjitha lockfiles = saktësisht çelësat e
    /// <see cref="IdentitySecurityDependencyInventory.ClosureManifest"/>,
    /// me versione identike. Paketë e re në closure OSE hyrje e vdekur në
    /// manifest = RED — manifest-i s'mund të divergojë në heshtje.
    /// </summary>
    [Fact]
    public void Manifesti_i_closure_perputhet_saktesisht_me_closure_real()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var discovered = DiscoverLockfiles(
            repoRoot, PlatformRoot, Path.Combine(repoRoot, "tests", "platform"));

        var realVersions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (entry, lockfile) in discovered)
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockfile!));
            var (closure, findings) = IdentitySecurityDependencyInventory.ComputeClosure(entry.CanonicalPath, document);
            Assert.True(findings.Count == 0, string.Join(" · ", findings));
            foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                foreach (var dependency in framework.Value.EnumerateObject())
                {
                    if (closure.Contains(dependency.Name)
                        && dependency.Value.TryGetProperty("resolved", out var resolved)
                        && resolved.GetString() is { } version)
                    {
                        realVersions.TryAdd(dependency.Name, new HashSet<string>(StringComparer.Ordinal));
                        realVersions[dependency.Name].Add(version);
                    }
                }
            }
        }

        // Barazi e DYANSHME: emrat realë == manifest-i; versionet identike.
        Assert.Equal(
            realVersions.Keys.Order(StringComparer.Ordinal).ToArray(),
            IdentitySecurityDependencyInventory.ClosureManifest.Keys.Order(StringComparer.Ordinal).ToArray());
        foreach (var (package, versions) in realVersions)
        {
            var version = Assert.Single(versions);
            Assert.Equal(IdentitySecurityDependencyInventory.ClosureManifest[package], version);
        }
    }

    private const string OwnerCanonicalPath =
        "src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj";

    private const string NonOwnerCanonicalPath = "src/platform/Sintetik/Sintetik.csproj";

    /// <summary>Lockfile sintetik: rrënjë e aprovuar me një edge drejt një pakete shtesë.</summary>
    private static System.Text.Json.JsonDocument SyntheticWithRoot(
        string extraName, string extraVersion, string extraType, string rootType = "Direct")
        => System.Text.Json.JsonDocument.Parse(
            "{\"version\":2,\"dependencies\":{\"net10.0\":{"
            + "\"OpenIddict.EntityFrameworkCore\":{\"type\":\"" + rootType
            + "\",\"resolved\":\"7.6.0\",\"contentHash\":\"sintetik\",\"dependencies\":{\"" + extraName + "\":\"" + extraVersion + "\"}},"
            + "\"" + extraName + "\":{\"type\":\"" + extraType
            + "\",\"resolved\":\"" + extraVersion + "\",\"contentHash\":\"sintetik\"}}}}");

    private static System.Text.Json.JsonDocument SyntheticStandalone(string name, string version, string type)
        => System.Text.Json.JsonDocument.Parse(
            "{\"version\":2,\"dependencies\":{\"net10.0\":{\"" + name + "\":{\"type\":\"" + type
            + "\",\"resolved\":\"" + version + "\",\"contentHash\":\"sintetik\"}}}}");

    /// <summary>Prova DISKRIMINUESE e auditit closure (§4-R3): çdo shkelje sintetike kapet.</summary>
    [Fact]
    public void Auditi_i_closure_kap_cdo_shkelje_dhe_zero_false_positive_jashte_closure()
    {
        // Emër i PANJOHUR brenda closure (deny-by-default) → gjetje.
        using (var document = SyntheticWithRoot("Evil.Identity.Backdoor", "1.0.0", "Transitive"))
        {
            Assert.NotEmpty(IdentitySecurityDependencyInventory.Audit(OwnerCanonicalPath, document));
        }

        // Version ≠ manifest brenda closure → gjetje.
        using (var document = SyntheticWithRoot("OpenIddict.Core", "9.9.9", "Transitive"))
        {
            Assert.NotEmpty(IdentitySecurityDependencyInventory.Audit(OwnerCanonicalPath, document));
        }

        // Rrënjë Direct te path JO-owner → gjetje (edhe me basename owner-i s'do të ndihmonte —
        // klasifikimi bëhet VETËM me path kanonik).
        using (var document = SyntheticWithRoot("OpenIddict.Core", "7.6.0", "Transitive"))
        {
            Assert.NotEmpty(IdentitySecurityDependencyInventory.Audit(NonOwnerCanonicalPath, document));
        }

        // Direct i paketës JO-rrënjë brenda closure edhe te owner-i → gjetje.
        using (var document = SyntheticWithRoot("OpenIddict.Core", "7.6.0", "Direct"))
        {
            Assert.NotEmpty(IdentitySecurityDependencyInventory.Audit(OwnerCanonicalPath, document));
        }

        // Lloj i palejuar brenda closure → gjetje.
        using (var document = SyntheticWithRoot("OpenIddict.Core", "7.6.0", "Project"))
        {
            Assert.NotEmpty(IdentitySecurityDependencyInventory.Audit(OwnerCanonicalPath, document));
        }

        // Metadata e palexueshme: edge drejt pakete që mungon nga lockfile → gjetje.
        using (var document = System.Text.Json.JsonDocument.Parse(
            "{\"version\":2,\"dependencies\":{\"net10.0\":{\"OpenIddict.EntityFrameworkCore\":"
            + "{\"type\":\"Direct\",\"resolved\":\"7.6.0\",\"contentHash\":\"sintetik\","
            + "\"dependencies\":{\"Paketa.Fantazme\":\"1.0.0\"}}}}}"))
        {
            Assert.NotEmpty(IdentitySecurityDependencyInventory.Audit(OwnerCanonicalPath, document));
        }

        // Familje e ndaluar provider-i — edhe krejt JASHTË closure → gjetje.
        using (var document = SyntheticStandalone("Duende.AccessTokenManagement", "1.0.0", "Transitive"))
        {
            Assert.NotEmpty(IdentitySecurityDependencyInventory.Audit(NonOwnerCanonicalPath, document));
        }

        // Inverse: rrënjë Direct te owner-i kanonik + closure konform → zero gjetje.
        using (var document = SyntheticWithRoot("OpenIddict.Core", "7.6.0", "Transitive"))
        {
            Assert.Empty(IdentitySecurityDependencyInventory.Audit(OwnerCanonicalPath, document));
        }

        // Inverse: rrënjë CentralTransitive (downstream) te jo-owner → zero gjetje.
        using (var document = SyntheticWithRoot("OpenIddict.Core", "7.6.0", "Transitive", rootType: "CentralTransitive"))
        {
            Assert.Empty(IdentitySecurityDependencyInventory.Audit(NonOwnerCanonicalPath, document));
        }

        // Inverse: paketë e PALIDHUR jashtë closure → zero false positive.
        using (var document = SyntheticStandalone("Newtonsoft.Json", "13.0.3", "Transitive"))
        {
            Assert.Empty(IdentitySecurityDependencyInventory.Audit(NonOwnerCanonicalPath, document));
        }

        // Direct mbi paketë identity-SPECIFIKE jo-rrënjë (edhe te jo-owner) → gjetje.
        using (var document = SyntheticWithRoot(
            "Microsoft.Extensions.Identity.Core", "10.0.10", "Direct", rootType: "CentralTransitive"))
        {
            Assert.NotEmpty(IdentitySecurityDependencyInventory.Audit(NonOwnerCanonicalPath, document));
        }

        // Inverse: Direct mbi anëtar të PËRBASHKËT infra të closure (EF Core)
        // te jo-owner → zero gjetje (qeveriset nga konventat e tjera).
        using (var document = SyntheticWithRoot(
            "Microsoft.EntityFrameworkCore", "10.0.10", "Direct", rootType: "CentralTransitive"))
        {
            Assert.Empty(IdentitySecurityDependencyInventory.Audit(NonOwnerCanonicalPath, document));
        }
    }

    /// <summary>
    /// Meta-testi i NDARJES së plotë (§4-R3): çdo anëtar i manifest-it
    /// klasifikohet në mënyrë deterministe — familjet identity-specifike
    /// përfshijnë të gjitha paketat identity/OpenIddict/kripto (përfshirë të
    /// dyja rrënjët), dhe pjesa tjetër është vetëm infrastrukturë e
    /// përbashkët EF/Extensions.
    /// </summary>
    [Fact]
    public void Klasifikimi_identity_specifik_i_closure_eshte_i_plote_dhe_korrekt()
    {
        var manifest = IdentitySecurityDependencyInventory.ClosureManifest.Keys;
        var specific = manifest.Where(IdentitySecurityDependencyInventory.IsIdentitySpecific)
            .Order(StringComparer.Ordinal).ToArray();
        var shared = manifest.Where(name => !IdentitySecurityDependencyInventory.IsIdentitySpecific(name))
            .Order(StringComparer.Ordinal).ToArray();

        // Rrënjët + Crypto/IdentityModel/Bcl janë specifike.
        Assert.All(IdentitySecurityDependencyInventory.ApprovedRoots.Keys,
            root => Assert.Contains(root, specific));
        Assert.Contains("Microsoft.AspNetCore.Cryptography.Internal", specific);
        Assert.Contains("Microsoft.AspNetCore.Cryptography.KeyDerivation", specific);
        Assert.Contains("Microsoft.Bcl.Cryptography", specific);
        Assert.All(specific, name => Assert.Matches(
            "^(Microsoft\\.AspNetCore\\.Identity|Microsoft\\.Extensions\\.Identity|OpenIddict|"
            + "Microsoft\\.IdentityModel|Microsoft\\.AspNetCore\\.Cryptography|Microsoft\\.Bcl\\.Cryptography)", name));

        // Pjesa e përbashkët = VETËM EF Core / Extensions jo-Identity.
        Assert.All(shared, name => Assert.Matches(
            "^(Microsoft\\.EntityFrameworkCore|Microsoft\\.Extensions\\.)", name));
        Assert.Equal(manifest.Count(), specific.Length + shared.Length);
    }

    /// <summary>Zbulimi dinamik kap projekt të ri PA lockfile (fail-closed).</summary>
    [Fact]
    public void Zbulimi_i_projekteve_kap_csproj_pa_lockfile()
    {
        var temporary = Directory.CreateTempSubdirectory("idata_lockfile_guard_");
        try
        {
            File.WriteAllText(Path.Combine(temporary.FullName, "Sintetik.Projekt.csproj"), "<Project />");
            var discovered = DiscoverLockfiles(temporary.FullName, temporary.FullName);
            var pair = Assert.Single(discovered);
            Assert.Equal("Sintetik.Projekt", pair.Entry.ProjectName);
            Assert.Null(pair.Lockfile);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Identiteti kanonik i projekteve (§3-R3): basename-i NUK është KURRË
    /// autoritet — spoof me të njëjtin basename në path tjetër ≠ owner;
    /// AssemblyName i dyfishtë = FAIL; path jashtë rrënjës = FAIL; projekt i
    /// zbuluar pa assembly të skanueshëm dështon receipt-in e kufirit IL.
    /// </summary>
    [Fact]
    public void Identiteti_kanonik_i_projekteve_eshte_path_based_dhe_fail_closed()
    {
        var temporary = Directory.CreateTempSubdirectory("idata_catalog_guard_");
        try
        {
            // (a) Spoof: i njëjti basename si owner-i real, path tjetër, AssemblyName
            // eksplicit i ndryshëm (ndryshe zbulohet si duplikat) → NUK është owner.
            var spoofDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "evil"));
            File.WriteAllText(Path.Combine(spoofDirectory.FullName, "Idata.Platform.Persistence.csproj"),
                "<Project><PropertyGroup><AssemblyName>Evil.Spoof</AssemblyName></PropertyGroup></Project>");
            var spoofed = PlatformProjectCatalog.Discover(temporary.FullName, temporary.FullName);
            var spoofEntry = Assert.Single(spoofed);
            Assert.Equal("Idata.Platform.Persistence", spoofEntry.ProjectName);
            Assert.Equal("Evil.Spoof", spoofEntry.AssemblyName);
            Assert.False(PlatformProjectCatalog.IsStoreOwner(spoofEntry),
                "Spoof me basename owner-i u klasifikua si owner — basename po përdoret si autoritet.");

            // (b) Dy csproj me të njëjtin basename (default AssemblyName identik)
            // në path-e të ndryshëm → FAIL i qartë, jo klasifikim i heshtur.
            var duplicateDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "dy"));
            File.WriteAllText(Path.Combine(duplicateDirectory.FullName, "Idata.Platform.Persistence.csproj"), "<Project />");
            var secondDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "tre"));
            File.WriteAllText(Path.Combine(secondDirectory.FullName, "Idata.Platform.Persistence.csproj"), "<Project />");
            Assert.Throws<InvalidOperationException>(
                () => PlatformProjectCatalog.Discover(temporary.FullName, temporary.FullName));

            // (c) Rrënjë kërkimi jashtë rrënjës së repo-s (spoof/traversal) → FAIL.
            var outside = Directory.CreateTempSubdirectory("idata_catalog_outside_");
            try
            {
                File.WriteAllText(Path.Combine(outside.FullName, "Jashte.csproj"), "<Project />");
                Assert.Throws<InvalidOperationException>(
                    () => PlatformProjectCatalog.Discover(temporary.FullName, outside.FullName));
            }
            finally
            {
                outside.Delete(recursive: true);
            }

            // (d) Projekt i zbuluar PA assembly të skanueshëm → receipt-i i
            // kufirit IL (File.Exists mbi AssemblyName real) dështon.
            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, $"{spoofEntry.AssemblyName}.dll")),
                "Assembly sintetik ekziston — receipt-i s'mund të provohet.");
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    /// <summary>Katalogu production real: owner-at ekzaktë + unikësi e provuar.</summary>
    [Fact]
    public void Katalogu_production_permban_saktesisht_dy_owner_at_kanonike()
    {
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        Assert.NotEmpty(catalog);

        foreach (var ownerPath in PlatformProjectCatalog.StoreOwnerCanonicalPaths)
        {
            var owner = Assert.Single(catalog, entry => entry.CanonicalPath == ownerPath);
            // AssemblyName-i i pritur i owner-it — identiteti i plotë §3.
            Assert.Equal(Path.GetFileNameWithoutExtension(ownerPath), owner.AssemblyName);
        }

        Assert.Equal(2, catalog.Count(PlatformProjectCatalog.IsStoreOwner));
        // Unikësia e AssemblyName/path garantohet nga Discover (hedh në duplikat).
        Assert.Equal(catalog.Length, catalog.Select(entry => entry.CanonicalPath).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Kufiri identity-store TYPE/IL-BASED (correction R2, §5 — AUTORITETI;
    /// regex-i tekstual mbetet vetëm defense-in-depth): production
    /// assemblies zbulohen DINAMIKISHT nga `src/platform/**/*.csproj`
    /// (modul i ri s'shpëton nga lista hardcoded); çdo projekt pa assembly
    /// të skanueshëm = FAIL (receipt fail-closed). Jo-owners: zero referencë
    /// ndaj store records (typeof/generic/Set&lt;T&gt;/attribute-string),
    /// zero akses në DbSet-et identity. TË GJITHË (edhe owners): zero
    /// call/reference ekzekutues ndaj inherited `IdentityUserClaim.Id`.
    /// </summary>
    [Fact]
    public void Kufiri_identity_store_type_IL_mbi_te_gjitha_production_assemblies()
    {
        // §3-R3: autoriteti = katalogu kanonik (path + AssemblyName real),
        // KURRË basename; owner-at njihen vetëm me path kanonik ekzakt.
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        Assert.NotEmpty(catalog);
        Assert.Equal(2, catalog.Count(PlatformProjectCatalog.IsStoreOwner));

        var scanned = 0;
        foreach (var entry in catalog)
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{entry.AssemblyName}.dll");
            // Receipt fail-closed: projekt production pa assembly të skanuar = FAIL.
            Assert.True(File.Exists(assemblyPath),
                $"Assembly i munguar për skanim IL: {entry.CanonicalPath} ({entry.AssemblyName})");

            var findings = IdentityStoreIlScanner.Scan(assemblyPath, PlatformProjectCatalog.IsStoreOwner(entry));
            Assert.True(findings.Count == 0,
                $"{entry.CanonicalPath}: {string.Join(" · ", findings.Select(finding => $"{finding.Kind}:{finding.Detail}"))}");
            scanned++;
        }

        // Count i DERIVUAR (jo i hardcoded): çdo projekt production u skanua
        // saktësisht një herë (path-et kanonike janë unike nga Discover).
        Assert.Equal(catalog.Length, scanned);
    }

    /// <summary>
    /// Prova DISKRIMINUESE e skanerit IL (fixtures në KËTË test assembly):
    /// secili bypass i §5.3/§3.2/§3.3 kapet — Set&lt;record&gt;/typeof,
    /// DbSet-et identity (përfshirë inherited `Users`), attribute-string,
    /// getter DHE setter i inherited `IdentityUserClaim.Id`; inverse-t e
    /// lejuara (PlatformAppUsers) NUK raportohen.
    /// </summary>
    [Fact]
    public void Skaneri_IL_kap_cdo_bypass_dhe_lejon_perdorimin_e_sakte()
    {
        var findings = IdentityStoreIlScanner.Scan(
            Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll"),
            isStoreOwner: false);

        foreach (var accessor in (string[])["get_Users", "get_UserClaims", "get_UserLogins", "get_UserTokens"])
        {
            Assert.Contains(findings, finding =>
                finding.Kind == "identity-dbset-access"
                && finding.Detail.StartsWith(accessor, StringComparison.Ordinal));
        }

        // §5-R3: accessor-i UserPasskeys (i pranishëm në IdentityUserContext
        // të .NET 10) kapet edhe pse entiteti s'është aktivizuar në modelin B.1.
        Assert.Contains(findings, finding =>
            finding.Kind == "identity-dbset-access"
            && finding.Detail.StartsWith("get_UserPasskeys", StringComparison.Ordinal));

        Assert.Contains(findings, finding =>
            finding.Kind == "claim-id-access" && finding.Detail.StartsWith("get_Id", StringComparison.Ordinal));
        Assert.Contains(findings, finding =>
            finding.Kind == "claim-id-access" && finding.Detail.StartsWith("set_Id", StringComparison.Ordinal));
        Assert.Contains(findings, finding =>
            finding.Kind == "record-type-ref" && finding.Detail == "UserAccountRecord");
        Assert.Contains(findings, finding =>
            finding.Kind == "record-type-ref" && finding.Detail == "UserClaimRecord");
        Assert.Contains(findings, finding => finding.Kind == "record-attribute-string");

        // §5-R3: KATËR entitetet OpenIddict kapen te jo-owner (Set<T> fixtures).
        foreach (var prefix in (string[])
            ["OpenIddictEntityFrameworkCoreApplication`", "OpenIddictEntityFrameworkCoreAuthorization`",
             "OpenIddictEntityFrameworkCoreScope`", "OpenIddictEntityFrameworkCoreToken`"])
        {
            Assert.Contains(findings, finding =>
                finding.Kind == "record-type-ref" && finding.Detail.StartsWith(prefix, StringComparison.Ordinal));
        }

        // Inverse: përdorimi i lejuar NUK gjenerohet si gjetje.
        Assert.DoesNotContain(findings, finding =>
            finding.Detail.Contains("PlatformAppUsers", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding => finding.Kind.StartsWith("unreadable", StringComparison.Ordinal));

        // Inverse e OWNER-it: për owner-in tipet e store-it janë të lejuara —
        // i njëjti binar i skanuar si owner s'prodhon gjetje kufiri tipesh
        // (rregulli claim-id mbetet i zbatuar mbi të gjithë — provohet veçmas).
        var ownerFindings = IdentityStoreIlScanner.Scan(
            Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll"),
            isStoreOwner: true);
        Assert.DoesNotContain(ownerFindings, finding => finding.Kind == "record-type-ref");
        Assert.DoesNotContain(ownerFindings, finding => finding.Kind == "record-attribute-string");
        Assert.DoesNotContain(ownerFindings, finding => finding.Kind == "identity-dbset-access");
    }

    /// <summary>
    /// Meta-testi i PLOTËSISË së universit të mbrojtur (§5-R3): entitetet CLR
    /// të mapuara në skemën `identity` derivohen nga modeli REAL i
    /// PlatformDbContext (pa kontakt DB) dhe duhet të përputhen SAKTËSISHT me
    /// universin e skanerit IL. Entitet i ri në skemën identity pa mbrojtje
    /// (ose hyrje e vdekur në skaner) = RED.
    /// </summary>
    [Fact]
    public void Universi_i_mbrojtur_i_skanerit_perputhet_me_modelin_real_identity()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql("Host=localhost;Database=meta_probe;Username=meta_probe")
            .Options;
        using var context = new PlatformDbContext(options);

        var modelIdentityEntities = context.Model.GetEntityTypes()
            .Where(entity => entity.GetSchema() == "identity")
            .Select(entity => entity.ClrType.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var scannerUniverse = IdentityStoreIlScanner.ForbiddenRecordTypeNames
            .Concat(IdentityStoreIlScanner.ForbiddenRecordTypePrefixes.Select(prefix => $"{prefix}1"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(modelIdentityEntities, scannerUniverse);
    }

    /// <summary>
    /// Dispozicioni MEKANIK i UserPasskeys (§5-R3): accessor-i ekziston në
    /// kontekst (IdentityUserContext i .NET 10) dhe tipi ekziston në
    /// framework-un e pinuar, POR entiteti NUK është aktivizuar në modelin
    /// B.1 — aktivizimi është gate i detyrueshëm i B.2 (dokumentuar në
    /// B.1 Record); skaneri e mbulon accessor-in që tani (fixture më lart).
    /// </summary>
    [Fact]
    public void UserPasskeys_accessor_ekziston_por_entiteti_nuk_eshte_ne_modelin_B1()
    {
        Assert.Contains(typeof(PlatformDbContext).GetProperties(),
            property => property.Name == "UserPasskeys");
        Assert.NotNull(typeof(Microsoft.AspNetCore.Identity.IdentityUser<Guid>).Assembly
            .GetType("Microsoft.AspNetCore.Identity.IdentityUserPasskey`1"));

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql("Host=localhost;Database=meta_probe;Username=meta_probe")
            .Options;
        using var context = new PlatformDbContext(options);
        Assert.DoesNotContain(context.Model.GetEntityTypes(),
            entity => entity.ClrType.Name.Contains("Passkey", StringComparison.OrdinalIgnoreCase));

        // Mbulimi i accessor-it në inventarin e skanerit — jo listë e vdekur.
        Assert.Contains("get_UserPasskeys", IdentityStoreIlScanner.IdentityDbSetAccessors);
        Assert.Contains("set_UserPasskeys", IdentityStoreIlScanner.IdentityDbSetAccessors);
    }

    /// <summary>
    /// Kufiri REZIDUAL i reflection-it (§6-R3, gate ENG-006 — OPEN): tipi i
    /// store-it zgjidhet REALISHT në runtime nga string i ndërtuar copë-copë,
    /// ndërsa trupi IL i PoC-së nuk përmban ASNJË token TypeRef drejt
    /// records të ndaluara — pra sipërfaqet që skaneri lexon realisht
    /// (TypeRefs/MemberRefs/attribute blobs) janë bosh për këtë bypass. Ky
    /// test dokumenton mekanikisht limitin — NUK e mbyll gate-in; mbyllja e
    /// ENG-006 kërkohet para merge-it të B.2 (Tracker).
    /// </summary>
    [Fact]
    public void Reflection_dinamik_zgjidhet_ne_runtime_dhe_nuk_kapet_nga_skaneri_IL()
    {
        // Pjesa A: PoC REAL — zgjidhja runtime funksionon.
        var resolved = IdentityBoundaryFixtures.DynamicReflectionResidualPoC() as Type;
        Assert.NotNull(resolved);
        Assert.Equal("UserClaimRecord", resolved!.Name);

        // Pjesa B: prova mekanike — trupi IL i metodës PoC nuk përmban asnjë
        // token TypeRef të records të ndaluara (ato ekzistojnë në assembly
        // vetëm nga fixtures e tjera të qëllimshme).
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll"));
        using var portableExecutable = new System.Reflection.PortableExecutable.PEReader(stream);
        var reader = portableExecutable.GetMetadataReader();

        var forbiddenTokens = reader.TypeReferences
            .Where(handle =>
            {
                var name = reader.GetString(reader.GetTypeReference(handle).Name);
                return IdentityStoreIlScanner.ForbiddenRecordTypeNames.Contains(name)
                    || IdentityStoreIlScanner.ForbiddenRecordTypePrefixes.Any(prefix =>
                        name.StartsWith(prefix, StringComparison.Ordinal));
            })
            .Select(handle => System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken((EntityHandle)handle))
            .ToArray();
        Assert.NotEmpty(forbiddenTokens); // pozitivi: tokens ekzistojnë nga fixtures.

        var pocMethod = reader.MethodDefinitions
            .Select(reader.GetMethodDefinition)
            .Single(method => reader.GetString(method.Name) == "DynamicReflectionResidualPoC");
        var ilBytes = portableExecutable.GetMethodBody(pocMethod.RelativeVirtualAddress).GetILContent().ToArray();

        for (var offset = 0; offset + 4 <= ilBytes.Length; offset++)
        {
            var window = BitConverter.ToInt32(ilBytes, offset);
            Assert.DoesNotContain(window, forbiddenTokens);
        }
    }

    /// <summary>
    /// Fixtures IL (kurrë të thirrura — vetëm të kompiluaruara): bypass-et e
    /// ndaluara + inverse-t e lejuara për provën diskriminuese më lart.
    /// </summary>
    private static class IdentityBoundaryFixtures
    {
        [AttributeUsage(AttributeTargets.All)]
        internal sealed class StoreMarkerAttribute(Type marker) : Attribute
        {
            public Type Marker { get; } = marker;
        }

        [StoreMarker(typeof(UserClaimRecord))]
        internal static class AttributeBypass;

        internal static object GenericSetBypass(PlatformDbContext context) => context.Set<UserClaimRecord>();

        internal static object TypeOfBypass() => typeof(UserAccountRecord);

        internal static object InheritedUsersBypass(PlatformDbContext context) => context.Users;

        internal static object UserClaimsBypass(PlatformDbContext context) => context.UserClaims;

        internal static object UserLoginsBypass(PlatformDbContext context) => context.UserLogins;

        internal static object UserTokensBypass(PlatformDbContext context) => context.UserTokens;

        internal static int InheritedClaimIdGetBypass(UserClaimRecord claim) => claim.Id;

        internal static void InheritedClaimIdSetBypass(UserClaimRecord claim) => claim.Id = 5;

        // §5-R3: bypass-et e katër entiteteve OpenIddict — Set<T> nga jo-owner.
        internal static object OpenIddictApplicationBypass(PlatformDbContext context)
            => context.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreApplication<Guid>>();

        internal static object OpenIddictAuthorizationBypass(PlatformDbContext context)
            => context.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreAuthorization<Guid>>();

        internal static object OpenIddictScopeBypass(PlatformDbContext context)
            => context.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreScope<Guid>>();

        internal static object OpenIddictTokenBypass(PlatformDbContext context)
            => context.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken<Guid>>();

        // §5-R3: accessor-i UserPasskeys (IdentityUserContext i .NET 10).
        internal static object UserPasskeysBypass(PlatformDbContext context) => context.UserPasskeys;

        // §6-R3 (ENG-006): PoC i reflection-it dinamik — emër i ndërtuar
        // copë-copë; nuk lë TypeRef/MemberRef/attribute blob të skanueshëm.
        internal static object? DynamicReflectionResidualPoC()
        {
            var constructedName = "Idata.Platform.Persistence." + "User" + "Claim" + "Record, Idata.Platform.Persistence";
            return Type.GetType(constructedName);
        }

        internal static object AllowedPlatformAppUsers(PlatformDbContext context) => context.PlatformAppUsers;
    }

    /// <summary>
    /// Kufiri `Users` ↔ `PlatformAppUsers` (correction pass PR #28, §6):
    /// kodi i AccessControl (authorization/storage) NUK lejohet të përdorë
    /// `context.Users` (inherited — `identity.user_account` i Identity-t);
    /// përdor VETËM `PlatformAppUsers` (`platform.app_user`).
    /// </summary>
    [Fact]
    public void AccessControl_perdor_vetem_PlatformAppUsers_kurre_context_Users()
    {
        var accessControlSources = Directory.GetFiles(
                Path.Combine(PlatformRoot, "Modules", "AccessControl"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(accessControlSources);

        foreach (var source in accessControlSources)
        {
            Assert.False(UsersAccessDetector().IsMatch(File.ReadAllText(source)),
                $"AccessControl përdor `.Users` (store i Identity-t): {source}");
        }

        // Guard-i s'është i vdekur: konsumatori real i PlatformAppUsers ekziston.
        Assert.Contains(accessControlSources, source =>
            File.ReadAllText(source).Contains(".PlatformAppUsers", StringComparison.Ordinal));
    }

    /// <summary>Prova DISKRIMINUESE e detektorit: përdorimi hipotetik kapet; i sakti jo.</summary>
    [Fact]
    public void Detektori_i_Users_kap_perdorimin_e_ndaluar_dhe_lejon_te_saktin()
    {
        Assert.Matches(UsersAccessDetector(), "var hit = context.Users.Any(user => user.Id == id);");
        Assert.Matches(UsersAccessDetector(), "await context.Users.ToListAsync();");
        Assert.DoesNotMatch(UsersAccessDetector(), "var ok = context.PlatformAppUsers.Any(user => user.Id == id);");
        Assert.DoesNotMatch(UsersAccessDetector(), "await manager.GetUsersForClaimAsync(claim);");
    }

    private static System.Text.RegularExpressions.Regex UsersAccessDetector()
        => new(@"\.Users\b");

    [Fact]
    public void Zero_pipeline_behaviors_ne_asnje_assembly_te_platformes()
    {
        foreach (var assembly in PlatformAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                Assert.DoesNotContain(type.GetInterfaces(), i =>
                    (i.FullName ?? string.Empty).StartsWith("Mediator.IPipelineBehavior", StringComparison.Ordinal)
                    || (i.FullName ?? string.Empty).StartsWith("Mediator.IStreamPipelineBehavior", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void Controller_i_hollte_merr_vetem_IMediator_dhe_trashegon_bazen_module_local()
    {
        var presentation = Assembly.Load("Idata.Platform.Modules.PlatformStatus.Presentation");
        var controller = presentation.GetType(
            "Idata.Platform.Modules.PlatformStatus.Presentation.Controllers.PlatformStatusController");
        Assert.NotNull(controller);

        var constructor = Assert.Single(controller.GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal("Mediator.IMediator", parameter.ParameterType.FullName);

        var baseType = controller.BaseType;
        Assert.NotNull(baseType);
        Assert.Equal(
            "Idata.Platform.Modules.PlatformStatus.Presentation.Controllers.BaseApiController",
            baseType.FullName);
        Assert.Same(presentation, baseType.Assembly); // module-local, JO shared

        // Zero fusha të tipeve infra: probe/DbContext/UoW në controller.
        foreach (var field in controller.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            var name = field.FieldType.FullName ?? string.Empty;
            Assert.DoesNotContain("ReadinessProbe", name, StringComparison.Ordinal);
            Assert.DoesNotContain("DbContext", name, StringComparison.Ordinal);
            Assert.DoesNotContain("UnitOfWork", name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BaseApiController_ekziston_vetem_ne_PlatformStatus_Presentation()
    {
        var owners = PlatformAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name == "BaseApiController")
            .Select(type => type.Assembly.GetName().Name ?? string.Empty)
            .Distinct()
            .ToArray();
        Assert.Equal(["Idata.Platform.Modules.PlatformStatus.Presentation"], owners);
    }

    [Fact]
    public void Minimal_API_i_vjeter_i_statusit_nuk_ekziston_me()
    {
        var presentation = Assembly.Load("Idata.Platform.Modules.PlatformStatus.Presentation");
        Assert.Null(presentation.GetType(
            "Idata.Platform.Modules.PlatformStatus.Presentation.PlatformStatusEndpoints"));
        Assert.DoesNotContain(PlatformAssemblies().SelectMany(a => a.GetTypes()),
            type => type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Any(method => method.Name == "MapPlatformStatus"));
    }

    [Fact]
    public void Zero_Common_Utils_ose_BuildingBlocks_assembly()
    {
        foreach (var assembly in PlatformAssemblies())
        {
            var name = assembly.GetName().Name ?? string.Empty;
            Assert.DoesNotContain("Common", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Utils", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("BuildingBlocks", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>§3-R3: assemblies të DERIVUARA nga katalogu kanonik — jo listë e hardcoded.</summary>
    private static Assembly[] PlatformAssemblies() =>
        [.. PlatformProjectCatalog.DiscoverProduction()
            .Select(entry => Assembly.Load(entry.AssemblyName))];
}
