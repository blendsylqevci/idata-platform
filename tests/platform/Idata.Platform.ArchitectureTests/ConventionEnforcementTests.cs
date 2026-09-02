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

    /// <summary>
    /// §4-R7: adjacency DIREKTE reale e ProjectReference-ve për çdo projekt
    /// të njohur (e vlerësuar nga MSBuild — jo closure, jo lockfile).
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlySet<string>> DirectProjectReferenceMap(
        string repoRoot, IEnumerable<string> canonicalProjectPaths)
    {
        var map = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var canonicalPath in canonicalProjectPaths.Distinct(StringComparer.Ordinal))
        {
            map[canonicalPath] = PlatformProjectCatalog.EvaluateProjectReferences(repoRoot, canonicalPath)
                .ToHashSet(StringComparer.Ordinal);
        }

        return map;
    }

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
    /// RENDI NORMATIV §5-R4 — prova diskriminuese: e ndaluara fiton mbi
    /// closure/manifest; asnjë paketë security/identity/auth nuk kapërcehet
    /// pa klasifikim (ReviewRequired jashtë aprovimit = gjetje); kufiri i
    /// përputhjes është me PIKË (emra të ngjashëm NUK kapen gabimisht).
    /// </summary>
    [Fact]
    public void Rendi_normativ_i_klasifikimit_te_paketave_eshte_fail_closed()
    {
        // (1) E NDALUAR edhe BRENDA closure-it (edge nga rrënja) — gjetja
        //     është e familjes së ndaluar, jo thjesht "e pa-inventarizuar".
        using (var document = SyntheticWithRoot("OpenIddict.Server", "7.6.0", "Transitive"))
        {
            var findings = IdentitySecurityDependencyInventory.Audit(OwnerCanonicalPath, document);
            Assert.Contains(findings, finding =>
                finding.Contains("familje provider-i e ndaluar", StringComparison.Ordinal)
                && finding.Contains("OpenIddict.Server", StringComparison.Ordinal));
        }

        // (2) ReviewRequired STANDALONE jashtë closure → gjetje (kurrë skip i heshtur).
        foreach (var package in (string[])
            ["OpenIddict.Core", "Microsoft.Extensions.Identity.Core",
             "Microsoft.AspNetCore.Authentication.JwtBearer",
             "Microsoft.AspNetCore.DataProtection.Extensions",
             "System.IdentityModel.Tokens.Jwt"])
        {
            using var document = SyntheticStandalone(package, "9.9.9", "Transitive");
            var findings = IdentitySecurityDependencyInventory.Audit(NonOwnerCanonicalPath, document);
            Assert.Contains(findings, finding =>
                finding.Contains("JASHTË aprovimit", StringComparison.Ordinal)
                && finding.Contains(package, StringComparison.Ordinal));
        }

        // (3) Inverse KUFIRI ME PIKË: emra të ngjashëm suffix-i NUK kapen.
        foreach (var package in (string[])
            ["Microsoft.AspNetCore.CryptographyFake", "OpenIddictFake.Core", "System.IdentityModelX"])
        {
            using var document = SyntheticStandalone(package, "1.0.0", "Transitive");
            Assert.Empty(IdentitySecurityDependencyInventory.Audit(NonOwnerCanonicalPath, document));
        }

        // (4) Inverse: paketë realisht e palidhur → zero gjetje (pas klasifikimit).
        using (var document = SyntheticStandalone("Serilog.Sinks.Console", "6.0.0", "Transitive"))
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

    /// <summary>
    /// §2A-R5 — AUTORITETI i package roots direkte: për ÇDO csproj real
    /// (src+tests), seti i PackageReference-ve direkte i vlerësuar nga
    /// MSBuild duhet të jetë EKZAKTËSISHT i barabartë me authority map-in
    /// eksplicit (i shkruar dorazi — JO i derivuar nga graph-i); dhe seti i
    /// projekteve të autoritetit duhet të jetë EKZAKTËSISHT seti real i
    /// projekteve (projekt i ri pa hyrje autoriteti = RED; hyrje autoriteti
    /// pa projekt real = RED).
    /// </summary>
    [Fact]
    public void Autoriteti_i_paketave_direkte_perputhet_me_MSBuild_dhe_projektet_reale()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var discovered = DiscoverLockfiles(
            repoRoot, PlatformRoot, Path.Combine(repoRoot, "tests", "platform"));

        // Barazia e projekteve: autoriteti mbulon SAKTËSISHT projektet reale.
        Assert.Equal(
            discovered.Select(pair => pair.Entry.CanonicalPath).Order(StringComparer.Ordinal).ToArray(),
            IdentitySecurityDependencyInventory.DirectPackageAuthority.Keys.Order(StringComparer.Ordinal).ToArray());

        foreach (var (entry, _) in discovered)
        {
            var evaluated = PlatformProjectCatalog.EvaluateDirectPackageReferences(repoRoot, entry.CanonicalPath);
            var authorized = IdentitySecurityDependencyInventory.DirectPackageAuthority[entry.CanonicalPath]
                .Keys.Order(StringComparer.Ordinal).ToArray();
            Assert.True(evaluated.SequenceEqual(authorized, StringComparer.Ordinal),
                $"{entry.CanonicalPath}: MSBuild direct=[{string.Join(",", evaluated)}] ≠ autoriteti=[{string.Join(",", authorized)}]");
        }
    }

    /// <summary>
    /// §2B-R5 — klasifikimi TOTAL i çdo lockfile-i real kundrejt autoritetit
    /// (Direct i autorizuar · referencë projekti · i arritshëm · përjashtim
    /// eksplicit): zero gjetje = closure reale B.1 GREEN.
    /// </summary>
    [Fact]
    public void Klasifikimi_total_i_TE_GJITHA_lockfiles_eshte_fail_closed()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var discovered = DiscoverLockfiles(
            repoRoot, PlatformRoot, Path.Combine(repoRoot, "tests", "platform"));
        Assert.NotEmpty(discovered);

        // §4-R6: graph-i AUTORITATIV — identiteti emër-i-vlerësuar → path
        // kanonik (nga katalogu MSBuild) + closure REAL ProjectReference
        // per-owner (i derivuar nga MSBuild, JO nga lockfile-i që auditohet).
        var assemblyNameToPath = discovered.ToDictionary(
            pair => pair.Entry.AssemblyName, pair => pair.Entry.CanonicalPath, StringComparer.Ordinal);

        foreach (var (entry, lockfile) in discovered)
        {
            Assert.True(
                IdentitySecurityDependencyInventory.DirectPackageAuthority.ContainsKey(entry.CanonicalPath),
                $"Projekt real PA hyrje autoriteti: {entry.CanonicalPath}");
            var projectGraph = new IdentitySecurityDependencyInventory.ProjectGraphAuthority(
                assemblyNameToPath,
                PlatformProjectCatalog.ProjectReferenceClosure(repoRoot, entry.CanonicalPath),
                IdentitySecurityDependencyInventory.DirectPackageAuthority,
                DirectProjectReferenceMap(repoRoot, assemblyNameToPath.Values));
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockfile!));
            var findings = IdentitySecurityDependencyInventory.AuditPackagesTotal(
                entry.CanonicalPath,
                document,
                IdentitySecurityDependencyInventory.DirectPackageAuthority[entry.CanonicalPath],
                projectGraph);
            Assert.True(findings.Count == 0, string.Join(" · ", findings));
        }
    }

    /// <summary>
    /// §2C-R5 — fixtures diskriminuese të klasifikimit total: provider-ë
    /// direkte jashtë autoritetit (emra realë tregtarë + sintetikë) = GJETJE;
    /// transitive i paarritshëm = GJETJE; metadata malformed = GJETJE;
    /// autoritet i vdekur = GJETJE; paketë e aprovuar jo-Identity = GREEN.
    /// </summary>
    [Fact]
    public void Klasifikimi_total_kap_direct_te_panjohur_te_paarritshem_dhe_malformed()
    {
        var emptyAuthority = new Dictionary<string, string>(StringComparer.Ordinal);

        // (1) Direct jashtë autoritetit → GJETJE (edhe pa asnjë prefix identity).
        foreach (var package in (string[])
            ["Keycloak.AuthServices.Authentication", "AspNet.Security.OAuth.GitHub", "Synthetic.Identity.Provider"])
        {
            using var document = SyntheticStandalone(package, "1.0.0", "Direct");
            var findings = IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document, emptyAuthority);
            Assert.Contains(findings, finding =>
                finding.Contains("DIREKT i PAAUTORIZUAR", StringComparison.Ordinal)
                && finding.Contains(package, StringComparison.Ordinal));
        }

        // (2) Transitive i PAARRITSHËM nga asnjë rrënjë e autorizuar → GJETJE.
        using (var document = SyntheticStandalone("Orphan.Package", "1.0.0", "Transitive"))
        {
            var findings = IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document, emptyAuthority);
            Assert.Contains(findings, finding => finding.Contains("PAARRITSHME", StringComparison.Ordinal));
        }

        // (3) Metadata malformed: hyrje pa 'type' → GJETJE.
        using (var document = System.Text.Json.JsonDocument.Parse(
            "{\"version\":2,\"dependencies\":{\"net10.0\":{\"Pa.Tip\":{\"resolved\":\"1.0.0\"}}}}"))
        {
            var findings = IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document, emptyAuthority);
            Assert.Contains(findings, finding => finding.Contains("pa 'type'", StringComparison.Ordinal));
        }

        // (4) Edge drejt node-i të munguar → GJETJE.
        using (var document = System.Text.Json.JsonDocument.Parse(
            "{\"version\":2,\"dependencies\":{\"net10.0\":{\"Rrenja.Ok\":{\"type\":\"Direct\",\"resolved\":\"1.0.0\","
            + "\"contentHash\":\"s\",\"dependencies\":{\"Fantazma\":\"1.0.0\"}}}}}"))
        {
            var findings = IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Rrenja.Ok"] = "1.0.0" });
            Assert.Contains(findings, finding => finding.Contains("MUNGUAR", StringComparison.Ordinal));
        }

        // (5) Hyrje autoriteti E VDEKUR (s'është më Direct reale) → GJETJE.
        using (var document = SyntheticStandalone("Tjeter.Paketa", "1.0.0", "Direct"))
        {
            var findings = IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Tjeter.Paketa"] = "1.0.0",
                    ["E.Vdekur"] = "9.9.9",
                });
            Assert.Contains(findings, finding => finding.Contains("E VDEKUR", StringComparison.Ordinal));
        }

        // (6) Inverse GREEN: Direct i autorizuar jo-Identity + subtree i arritshëm.
        using (var document = SyntheticWithRootNamed(
            "Npgsql", "10.0.3", "Direct", "Microsoft.Extensions.Logging.Abstractions", "10.0.10", "Transitive"))
        {
            Assert.Empty(IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Npgsql"] = "10.0.3" }));
        }

        // (7) Version ≠ autoriteti → GJETJE.
        using (var document = SyntheticStandalone("Npgsql", "9.9.9", "Direct"))
        {
            var findings = IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Npgsql"] = "10.0.3" });
            Assert.Contains(findings, finding => finding.Contains("version i paautorizuar", StringComparison.Ordinal));
        }
    }

    /// <summary>§4-R6: Ghost.Project (s'mapon në projekt real) NUK krijon authority; subtree = GJETJE.</summary>
    [Fact]
    public void Ghost_project_nuk_krijon_authority()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            "{\"version\":2,\"dependencies\":{\"net10.0\":{"
            + "\"Ghost.Project\":{\"type\":\"Project\",\"dependencies\":{\"Evil.Security.Lib\":\"1.0.0\"}},"
            + "\"Evil.Security.Lib\":{\"type\":\"CentralTransitive\",\"resolved\":\"1.0.0\",\"contentHash\":\"s\"}}}}");
        var findings = IdentitySecurityDependencyInventory.AuditPackagesTotal(
            NonOwnerCanonicalPath, document, new Dictionary<string, string>(StringComparer.Ordinal));
        Assert.NotEmpty(findings);
    }

    /// <summary>§4-R6: Direct pa contentHash (metadata e paplotë) = GJETJE fail-closed.</summary>
    [Fact]
    public void Direct_pa_contentHash_eshte_gjetje()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            "{\"version\":2,\"dependencies\":{\"net10.0\":{\"Rrenja.Ok\":{\"type\":\"Direct\",\"resolved\":\"1.0.0\"}}}}");
        var findings = IdentitySecurityDependencyInventory.AuditPackagesTotal(
            NonOwnerCanonicalPath, document,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Rrenja.Ok"] = "1.0.0" });
        Assert.NotEmpty(findings);
    }

    /// <summary>§4-R6: dead-authority kapet EDHE kur 'dependencies' është krejt bosh.</summary>
    [Fact]
    public void Dead_authority_kapet_edhe_me_dependencies_bosh()
    {
        using var document = System.Text.Json.JsonDocument.Parse("{\"version\":2,\"dependencies\":{}}");
        var findings = IdentitySecurityDependencyInventory.AuditPackagesTotal(
            NonOwnerCanonicalPath, document,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["E.Vdekur"] = "1.0.0" });
        Assert.NotEmpty(findings);
    }

    /// <summary>
    /// §4-R7 — 7 fixtures të detyrueshme mbi Project-node edges, versione,
    /// arritshmëri dhe schema; secila me graph autoritativ SINTETIK (adjacency
    /// direkte + autoritet direkt paketash të injektuara veçmas nga lockfile-i
    /// që auditohet).
    /// </summary>
    [Fact]
    public void Project_node_edges_dhe_schema_validohen_fail_closed()
    {
        const string alfa = "src/platform/Sintetik/Alfa/Alfa.csproj";
        const string beta = "src/platform/Sintetik/Beta/Beta.csproj";
        var names = new Dictionary<string, string>(StringComparer.Ordinal) { ["Alfa"] = alfa, ["Beta"] = beta };
        var authority = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            [NonOwnerCanonicalPath] = new Dictionary<string, string>(StringComparer.Ordinal),
            [alfa] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Npgsql"] = "10.0.3" },
            [beta] = new Dictionary<string, string>(StringComparer.Ordinal),
        };
        var closure = new HashSet<string>(StringComparer.Ordinal) { alfa, beta };
        IReadOnlySet<string> AlfaRefs(params string[] refs) => refs.ToHashSet(StringComparer.Ordinal);

        IdentitySecurityDependencyInventory.ProjectGraphAuthority Graph(
            IReadOnlySet<string> alfaDirectRefs, IReadOnlySet<string>? closureOverride = null)
            => new(names, closureOverride ?? closure, authority,
                new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
                {
                    [alfa] = alfaDirectRefs,
                    [beta] = AlfaRefs(),
                });

        static System.Text.Json.JsonDocument Lock(string body, string version = "\"version\":2,")
            => System.Text.Json.JsonDocument.Parse("{" + version + "\"dependencies\":{\"net10.0\":{" + body + "}}}");

        var emptyAuthority = new Dictionary<string, string>(StringComparer.Ordinal);

        // (1) Npgsql authority 10.0.3 + resolved 9.9.9 → GJETJE.
        using (var document = Lock(
            "\"Alfa\":{\"type\":\"Project\",\"dependencies\":{\"Npgsql\":\"10.0.3\"}},"
            + "\"Npgsql\":{\"type\":\"CentralTransitive\",\"resolved\":\"9.9.9\",\"contentHash\":\"s\"}"))
        {
            Assert.Contains(IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document, emptyAuthority, Graph(AlfaRefs())),
                finding => finding.Contains("resolved=9.9.9", StringComparison.Ordinal));
        }

        // (2) project package-edge drejt node-i të MUNGUAR → GJETJE.
        using (var document = Lock("\"Alfa\":{\"type\":\"Project\",\"dependencies\":{\"Npgsql\":\"10.0.3\"}}"))
        {
            Assert.Contains(IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document, emptyAuthority, Graph(AlfaRefs())),
                finding => finding.Contains("node-i të MUNGUAR", StringComparison.Ordinal));
        }

        // (3) package edge i PAAUTORIZUAR → GJETJE.
        using (var document = Lock(
            "\"Alfa\":{\"type\":\"Project\",\"dependencies\":{\"Evil.Pkg\":\"1.0.0\"}},"
            + "\"Evil.Pkg\":{\"type\":\"CentralTransitive\",\"resolved\":\"1.0.0\",\"contentHash\":\"s\"}"))
        {
            Assert.Contains(IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document, emptyAuthority, Graph(AlfaRefs())),
                finding => finding.Contains("edge pakete të PAAUTORIZUAR", StringComparison.Ordinal));
        }

        // (4) project edge FALS drejt projekti tjetër brenda closure-it → GJETJE.
        using (var document = Lock(
            "\"Alfa\":{\"type\":\"Project\",\"dependencies\":{\"Beta\":\"1.0.0\"}},"
            + "\"Beta\":{\"type\":\"Project\"}"))
        {
            Assert.Contains(IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document, emptyAuthority, Graph(AlfaRefs())),
                finding => finding.Contains("NUK është ProjectReference DIREKT", StringComparison.Ordinal));
        }

        // (5) projekt real i njohur, POR jashtë closure-it → GJETJE.
        using (var document = Lock("\"Alfa\":{\"type\":\"Project\"}"))
        {
            Assert.Contains(IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document, emptyAuthority,
                Graph(AlfaRefs(), new HashSet<string>(StringComparer.Ordinal) { beta })),
                finding => finding.Contains("PAARRITSHËM", StringComparison.Ordinal));
        }

        // (6) schema version missing / string / e pambështetur → GJETJE.
        foreach (var variant in (string[])["", "\"version\":\"2\",", "\"version\":3,"])
        {
            using var document = Lock("\"Alfa\":{\"type\":\"Project\"}", variant);
            Assert.Contains(IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document, emptyAuthority, Graph(AlfaRefs())),
                finding => finding.Contains("version", StringComparison.OrdinalIgnoreCase));
        }

        // (7) INVERSE GREEN: graph real valid (edge projekti direkt + edge pakete
        // e autorizuar me range e resolved korrekte).
        using (var document = Lock(
            "\"Alfa\":{\"type\":\"Project\",\"dependencies\":{\"Beta\":\"1.0.0\",\"Npgsql\":\"[10.0.3, )\"}},"
            + "\"Beta\":{\"type\":\"Project\"},"
            + "\"Npgsql\":{\"type\":\"CentralTransitive\",\"resolved\":\"10.0.3\",\"contentHash\":\"s\"}"))
        {
            Assert.Empty(IdentitySecurityDependencyInventory.AuditPackagesTotal(
                NonOwnerCanonicalPath, document, emptyAuthority, Graph(AlfaRefs(beta))));
        }
    }

    /// <summary>§4-R7: parser-i i range-ve mbulon VETËM formatet e deklaruara; tjetra = i pambështetur.</summary>
    [Theory]
    [InlineData("10.0.3", true, false)]
    [InlineData("[10.0.3, )", true, false)]
    [InlineData("[10.0.3,)", true, false)]
    [InlineData("[10.0.3]", true, false)]
    [InlineData("9.9.9", false, false)]
    [InlineData("[9.9.9]", false, false)]
    [InlineData("(10.0.3, 11.0.0)", false, true)]
    [InlineData("[10.0.3, 11.0.0]", false, true)]
    public void Range_parser_mbulon_vetem_formatet_e_deklaruara(string range, bool contains, bool expectedUnsupported)
    {
        var result = IdentitySecurityDependencyInventory.RangeContainsAuthorizedVersion(range, "10.0.3", out var unsupported);
        Assert.Equal(contains, result);
        Assert.Equal(expectedUnsupported, unsupported);
    }

    /// <summary>Lockfile sintetik me rrënjë të EMËRTUAR + një varësi të saj.</summary>
    private static System.Text.Json.JsonDocument SyntheticWithRootNamed(
        string rootName, string rootVersion, string rootType,
        string childName, string childVersion, string childType)
        => System.Text.Json.JsonDocument.Parse(
            "{\"version\":2,\"dependencies\":{\"net10.0\":{"
            + "\"" + rootName + "\":{\"type\":\"" + rootType + "\",\"resolved\":\"" + rootVersion
            + "\",\"contentHash\":\"s\",\"dependencies\":{\"" + childName + "\":\"" + childVersion + "\"}},"
            + "\"" + childName + "\":{\"type\":\"" + childType + "\",\"resolved\":\"" + childVersion
            + "\",\"contentHash\":\"s\"}}}}");

    /// <summary>
    /// §5B-R6 — IMMUTABILITETI i katalogut production: mutimi i array-t të
    /// kthyer nga `DiscoverProduction()` NUK helmon thirrjen pasuese (kthehet
    /// kopje e re çdo herë; vlerësimi mbetet një-për-proces në Lazy).
    /// </summary>
    [Fact]
    public void DiscoverProduction_nuk_ndikohet_nga_mutimi_i_caller_it()
    {
        var first = PlatformProjectCatalog.DiscoverProduction();
        var originalLength = first.Length;
        Assert.NotEmpty(first);
        var sentinel = first[0];

        // Mutim armiqësor i rezultatit të thirrjes së parë.
        first[0] = null!;
        Array.Clear(first);

        var second = PlatformProjectCatalog.DiscoverProduction();
        Assert.Equal(originalLength, second.Length);
        Assert.All(second, entry => Assert.NotNull(entry));
        Assert.Contains(second, entry => entry.CanonicalPath == sentinel.CanonicalPath);
        Assert.NotSame(first, second);
    }

    /// <summary>
    /// §4-R5 — FRESHNESS i Discover(): mbi TË NJËJTIN temp-root, add/change/
    /// remove i csproj-ve shihet nga thirrja pasuese; mutimi i rezultatit të
    /// kthyer nuk ndikon thirrjen tjetër. Zero cache-clear hook.
    /// </summary>
    [Fact]
    public void Discover_eshte_i_fresket_ne_cdo_thirrje_mbi_te_njejtin_root()
    {
        var temporary = Directory.CreateTempSubdirectory("idata_freshness_");
        try
        {
            var firstDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "a"));
            var firstProject = Path.Combine(firstDirectory.FullName, "Freskia.Nje.csproj");
            File.WriteAllText(firstProject, SdkProjectXml);

            // (1) Zbulimi fillestar.
            var initial = PlatformProjectCatalog.Discover(temporary.FullName, temporary.FullName);
            Assert.Single(initial);
            Assert.Equal("Freskia.Nje", initial[0].AssemblyName);

            // (2) ADD: csproj i ri shihet nga thirrja pasuese.
            var secondDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "b"));
            File.WriteAllText(Path.Combine(secondDirectory.FullName, "Freskia.Dy.csproj"), SdkProjectXml);
            Assert.Equal(2, PlatformProjectCatalog.Discover(temporary.FullName, temporary.FullName).Length);

            // (3) CHANGE: AssemblyName i ndryshuar shihet me vlerën e RE.
            File.WriteAllText(firstProject, SdkProjectXmlWith("<AssemblyName>Freskia.Ndryshuar</AssemblyName>"));
            var changed = PlatformProjectCatalog.Discover(temporary.FullName, temporary.FullName);
            Assert.Contains(changed, entry => entry.AssemblyName == "Freskia.Ndryshuar");
            Assert.DoesNotContain(changed, entry => entry.AssemblyName == "Freskia.Nje");

            // (4) REMOVE: csproj i hequr nuk kthehet më.
            File.Delete(firstProject);
            var afterRemove = PlatformProjectCatalog.Discover(temporary.FullName, temporary.FullName);
            var remaining = Assert.Single(afterRemove);
            Assert.Equal("Freskia.Dy", remaining.AssemblyName);

            // (5) Mutimi i array-t të kthyer NUK ndikon thirrjen tjetër.
            afterRemove[0] = null!;
            var fresh = PlatformProjectCatalog.Discover(temporary.FullName, temporary.FullName);
            Assert.NotNull(Assert.Single(fresh));
            Assert.Equal("Freskia.Dy", fresh[0].AssemblyName);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    /// <summary>Zbulimi dinamik kap projekt të ri PA lockfile (fail-closed).</summary>
    [Fact]
    public void Zbulimi_i_projekteve_kap_csproj_pa_lockfile()
    {
        var temporary = Directory.CreateTempSubdirectory("idata_lockfile_guard_");
        try
        {
            File.WriteAllText(Path.Combine(temporary.FullName, "Sintetik.Projekt.csproj"), SdkProjectXml);
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

    private const string SdkProjectXml =
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";

    private static string SdkProjectXmlWith(string extraProperties)
        => "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
           + extraProperties + "</PropertyGroup></Project>";

    /// <summary>
    /// Identiteti kanonik i projekteve (§2-R4 — MSBuild REAL): basename/XML
    /// raw NUK janë KURRË autoritet — spoof me basename owner-i ≠ owner;
    /// identitet i vlerësuar i dyfishtë = FAIL; path jashtë rrënjës = FAIL;
    /// projekt pa assembly të ndërtuar dështon receipt-in e verifikimit.
    /// </summary>
    [Fact]
    public void Identiteti_kanonik_i_projekteve_eshte_msbuild_evaluated_dhe_fail_closed()
    {
        var temporary = Directory.CreateTempSubdirectory("idata_catalog_guard_");
        try
        {
            // (a) Spoof: i njëjti basename si owner-i real, path tjetër, AssemblyName
            // eksplicit i ndryshëm (i VLERËSUAR nga MSBuild) → NUK është owner.
            var spoofDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "evil"));
            File.WriteAllText(Path.Combine(spoofDirectory.FullName, "Idata.Platform.Persistence.csproj"),
                SdkProjectXmlWith("<AssemblyName>Evil.Spoof</AssemblyName>"));
            var spoofed = PlatformProjectCatalog.Discover(temporary.FullName, spoofDirectory.FullName);
            var spoofEntry = Assert.Single(spoofed);
            Assert.Equal("Idata.Platform.Persistence", spoofEntry.ProjectName);
            Assert.Equal("Evil.Spoof", spoofEntry.AssemblyName);
            Assert.False(PlatformProjectCatalog.IsStoreOwner(spoofEntry),
                "Spoof me basename owner-i u klasifikua si owner — basename po përdoret si autoritet.");

            // (b) Dy csproj me të njëjtin basename (AssemblyName i vlerësuar identik)
            // në path-e të ndryshëm → FAIL i qartë, jo klasifikim i heshtur.
            var duplicateRoot = Directory.CreateDirectory(Path.Combine(temporary.FullName, "duplikat")).FullName;
            File.WriteAllText(
                Path.Combine(Directory.CreateDirectory(Path.Combine(duplicateRoot, "dy")).FullName, "Njejte.csproj"),
                SdkProjectXml);
            File.WriteAllText(
                Path.Combine(Directory.CreateDirectory(Path.Combine(duplicateRoot, "tre")).FullName, "Njejte.csproj"),
                SdkProjectXml);
            Assert.Throws<InvalidOperationException>(
                () => PlatformProjectCatalog.Discover(temporary.FullName, duplicateRoot));

            // (c) Rrënjë kërkimi jashtë rrënjës së repo-s (spoof/traversal) → FAIL.
            var outside = Directory.CreateTempSubdirectory("idata_catalog_outside_");
            try
            {
                File.WriteAllText(Path.Combine(outside.FullName, "Jashte.csproj"), SdkProjectXml);
                Assert.Throws<InvalidOperationException>(
                    () => PlatformProjectCatalog.Discover(temporary.FullName, outside.FullName));
            }
            finally
            {
                outside.Delete(recursive: true);
            }

            // (d) Projekt i zbuluar PA assembly të ndërtuar → receipt-i i
            // verifikimit (TargetPath ekzakt) dështon fail-closed.
            Assert.Throws<InvalidOperationException>(() =>
                PlatformProjectCatalog.VerifyTargetAssembly(temporary.FullName, spoofEntry, "evil"));
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    /// <summary>
    /// §2-R4: identiteti vjen nga VLERËSIMI MSBuild, jo nga XML raw — decoy
    /// XML i pa-vlerësuar injorohet; AssemblyName conditional dhe i importuar
    /// (Directory.Build.props) vlerësohen saktë; XML i pavlefshëm dhe
    /// TargetPath jashtë rrënjës = FAIL; decoy DLL me emër të pritur por
    /// identitet tjetër të brendshëm = FAIL.
    /// </summary>
    [Fact]
    public void Vleresimi_msbuild_kap_conditional_import_decoy_dhe_identitet_te_rreme()
    {
        var temporary = Directory.CreateTempSubdirectory("idata_msbuild_guard_");
        try
        {
            // (a) Decoy XML: AssemblyName me Condition false + vlera reale me
            // property expansion — regex/XML raw do të lexonte decoy-n; MSBuild
            // kthen vlerën REALE të vlerësuar.
            var decoyDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "decoy"));
            File.WriteAllText(Path.Combine(decoyDirectory.FullName, "Decoy.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"
                + "<TargetFramework>net10.0</TargetFramework>"
                + "<AssemblyName Condition=\"'false'=='true'\">Decoy.NgaXml</AssemblyName>"
                + "<Baza>Reale</Baza><AssemblyName>$(Baza).Vleresuar</AssemblyName>"
                + "</PropertyGroup></Project>");
            var decoyEntry = Assert.Single(PlatformProjectCatalog.Discover(temporary.FullName, decoyDirectory.FullName));
            Assert.Equal("Reale.Vleresuar", decoyEntry.AssemblyName);

            // (b) AssemblyName i IMPORTUAR nga Directory.Build.props — XML-i i
            // csproj-it s'e përmban fare; vetëm vlerësimi real e sheh.
            var importedDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "imported"));
            File.WriteAllText(Path.Combine(importedDirectory.FullName, "Directory.Build.props"),
                "<Project><PropertyGroup><AssemblyName>Nga.Importi</AssemblyName></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(importedDirectory.FullName, "Imported.csproj"), SdkProjectXml);
            var importedEntry = Assert.Single(PlatformProjectCatalog.Discover(temporary.FullName, importedDirectory.FullName));
            Assert.Equal("Nga.Importi", importedEntry.AssemblyName);

            // (c) XML i pavlefshëm → MSBuild exit ≠ 0 → FAIL (jo silent skip).
            var malformedDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "malformed"));
            File.WriteAllText(Path.Combine(malformedDirectory.FullName, "Keq.csproj"), "<Project><PA-MBYLLUR>");
            Assert.Throws<InvalidOperationException>(
                () => PlatformProjectCatalog.Discover(temporary.FullName, malformedDirectory.FullName));

            // (d) Non-SDK pa TargetPath të vlerësueshëm → FAIL (zero fallback basename).
            var emptyDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "bosh"));
            File.WriteAllText(Path.Combine(emptyDirectory.FullName, "Bosh.csproj"), "<Project />");
            Assert.Throws<InvalidOperationException>(
                () => PlatformProjectCatalog.Discover(temporary.FullName, emptyDirectory.FullName));

            // (e) TargetPath i vlerësuar JASHTË rrënjës së repo-s → FAIL.
            var escapeDirectory = Directory.CreateDirectory(Path.Combine(temporary.FullName, "escape"));
            var outsideOutput = Directory.CreateTempSubdirectory("idata_escape_out_");
            try
            {
                File.WriteAllText(Path.Combine(escapeDirectory.FullName, "Escape.csproj"),
                    SdkProjectXmlWith($"<OutputPath>{outsideOutput.FullName}/</OutputPath>"
                        + "<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>"));
                Assert.Throws<InvalidOperationException>(
                    () => PlatformProjectCatalog.Discover(temporary.FullName, escapeDirectory.FullName));
            }
            finally
            {
                outsideOutput.Delete(recursive: true);
            }

            // (f) Decoy DLL: file ekziston në TargetPath e pritur me EMRIN e
            // pritur, por identiteti i brendshëm i binarit është tjetër → FAIL.
            var decoyBinDirectory = Directory.CreateDirectory(
                Path.Combine(decoyDirectory.FullName, "bin", "Debug", "net10.0"));
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll"),
                Path.Combine(decoyBinDirectory.FullName, "Reale.Vleresuar.dll"));
            var mismatch = Assert.Throws<InvalidOperationException>(() =>
                PlatformProjectCatalog.VerifyTargetAssembly(temporary.FullName, decoyEntry, "decoy"));
            Assert.Contains("mismatch", mismatch.Message, StringComparison.OrdinalIgnoreCase);

            // (g) File i palexueshëm si assembly në TargetPath → FAIL.
            File.WriteAllText(Path.Combine(decoyBinDirectory.FullName, "Reale.Vleresuar.dll"), "jo-PE");
            Assert.Throws<InvalidOperationException>(() =>
                PlatformProjectCatalog.VerifyTargetAssembly(temporary.FullName, decoyEntry, "decoy"));
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

        // Identiteti i PRITUR i owner-ëve (spec eksplicite — krahasohet me
        // vlerën e VLERËSUAR nga MSBuild, jo me basename-in).
        var expectedOwners = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj"]
                = "Idata.Platform.Persistence",
            ["src/platform/Modules/Identity/Infrastructure/Idata.Platform.Modules.Identity.Infrastructure.csproj"]
                = "Idata.Platform.Modules.Identity.Infrastructure",
        };
        Assert.Equal(expectedOwners.Keys.Order(StringComparer.Ordinal),
            PlatformProjectCatalog.StoreOwnerCanonicalPaths.Order(StringComparer.Ordinal));
        foreach (var (ownerPath, expectedAssembly) in expectedOwners)
        {
            var owner = Assert.Single(catalog, entry => entry.CanonicalPath == ownerPath);
            Assert.Equal(expectedAssembly, owner.AssemblyName);
        }

        Assert.Equal(2, catalog.Count(PlatformProjectCatalog.IsStoreOwner));
        // Unikësia e AssemblyName/path/TargetPath garantohet nga Discover (hedh në duplikat).
        Assert.Equal(catalog.Length, catalog.Select(entry => entry.CanonicalPath).Distinct(StringComparer.Ordinal).Count());
        // C3 (§2B): numërimi bëhet BRENDA katalogut — testi nuk e lexon vetë vetinë.
        Assert.Equal(catalog.Length, PlatformProjectCatalog.DistinctCanonicalTargetPathCount());
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

        var generation = PlatformProjectCatalog.CurrentGeneration();
        var scanned = 0;
        foreach (var entry in catalog)
        {
            // §2-R4 + C2: skanohet EKZAKTËSISHT TargetPath-i i vlerësuar nga
            // MSBuild, i marrë NGA KUFIRI i ngrirë (verifikim para+pas skanimit)
            // — zero ndërtim direkt path-i, zero lookup sipas basename-it.
            var findings = IdentityStoreIlScanner.Scan(
                PlatformProjectCatalog.ReadVerifiedImage(entry, generation),
                PlatformProjectCatalog.IsStoreOwner(entry));
            Assert.True(findings.Count == 0,
                $"{entry.CanonicalPath}: {string.Join(" · ", findings.Select(finding => $"{finding.Kind}:{finding.Detail}"))}");
            scanned++;
        }

        // Count i DERIVUAR (jo i hardcoded): çdo projekt production u skanua
        // saktësisht një herë (path-et kanonike janë unike nga Discover).
        Assert.Equal(catalog.Length, scanned);
    }

    /// <summary>
    /// Prova DISKRIMINUESE e skanerit IL (fixtures në KËTË test assembly) —
    /// mbulon SAKTËSISHT sipërfaqet e testuara (JO "çdo bypass"; reflektimi
    /// dinamik = ENG-006 OPEN, untyped data access = ENG-007 OPEN):
    /// Set&lt;record&gt;/typeof, DbSet-et identity (përfshirë inherited
    /// `Users` + `UserPasskeys`, getter DHE setter), attribute-string,
    /// getter/setter i inherited `IdentityUserClaim.Id`; inverse-t e
    /// lejuara (PlatformAppUsers) NUK raportohen.
    /// </summary>
    [Fact]
    public void Skaneri_IL_kap_bypass_et_e_mbuluara_dhe_lejon_perdorimin_e_sakte()
    {
        var findings = IdentityStoreIlScanner.ScanFixture(
            PlatformProjectCatalog.OpenFixtureImage(Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll")),
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
        var ownerFindings = IdentityStoreIlScanner.ScanFixture(
            PlatformProjectCatalog.OpenFixtureImage(Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll")),
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
    /// AUTORITETI i accessor-ëve të DbSet-eve identity (§4-R4): derivohen
    /// VETËM nga DbSet&lt;&gt; properties të deklaruara në zinxhirin
    /// framework/base të closed IdentityUserContext&lt;...&gt; (public +
    /// non-public instance) — properties e deklaruara nga PlatformDbContext
    /// përjashtohen shprehimisht. Barazia e DYANSHME me inventarin e
    /// skanerit: accessor i munguar OSE i vdekur = RED. UserPasskeys hyn
    /// NATYRSHËM nga framework surface — jo nga listë manuale. Çdo accessor
    /// i derivuar provohet me fixture te jo-owner (getter + setter).
    /// </summary>
    [Fact]
    public void Accessoret_e_DbSet_identity_derivohen_nga_zinxhiri_framework_dhe_barazohen_me_skanerin()
    {
        var derived = new List<string>();
        for (var type = typeof(PlatformDbContext).BaseType;
             type is not null && type != typeof(DbContext);
             type = type.BaseType)
        {
            foreach (var property in type.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.PropertyType.IsGenericType
                    && property.PropertyType.GetGenericTypeDefinition() == typeof(Microsoft.EntityFrameworkCore.DbSet<>))
                {
                    if (property.GetMethod is not null)
                    {
                        derived.Add($"get_{property.Name}");
                    }

                    if (property.SetMethod is not null)
                    {
                        derived.Add($"set_{property.Name}");
                    }
                }
            }
        }

        // Barazi e DYANSHME me inventarin e skanerit IL.
        Assert.Equal(
            derived.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            IdentityStoreIlScanner.IdentityDbSetAccessors.Order(StringComparer.Ordinal).ToArray());

        // UserPasskeys vjen natyrshëm nga framework surface (jo listë manuale).
        Assert.Contains("get_UserPasskeys", derived);

        // PlatformDbContext-only DbSets NUK hyjnë në këtë univers.
        Assert.DoesNotContain(derived, accessor => accessor.Contains("PlatformAppUsers", StringComparison.Ordinal));
        Assert.DoesNotContain(IdentityStoreIlScanner.IdentityDbSetAccessors,
            accessor => accessor.Contains("PlatformAppUsers", StringComparison.Ordinal));

        // ÇDO accessor i derivuar prodhon gjetje reale te jo-owner (fixtures
        // getter + setter në IdentityBoundaryFixtures).
        var findings = IdentityStoreIlScanner.ScanFixture(
            PlatformProjectCatalog.OpenFixtureImage(Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll")),
            isStoreOwner: false);
        Assert.All(derived, accessor => Assert.Contains(findings, finding =>
            finding.Kind == "identity-dbset-access"
            && finding.Detail.StartsWith($"{accessor} ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Dispozicioni MEKANIK i UserPasskeys (§4-R4): accessor-i ekziston në
    /// kontekst (IdentityUserContext i .NET 10) dhe tipi ekziston në
    /// framework-un e pinuar, POR entiteti NUK është aktivizuar në modelin
    /// B.1 — aktivizimi = B.2 CHANGE-CONTROL (scope + migrim + teste +
    /// review i pavarur + Human Gate; meta-testi i plotësisë bëhet RED nëse
    /// entiteti aktivizohet pa zgjerim të boundary); skaneri e mbulon
    /// accessor-in që tani (fixtures më lart).
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

        // Pjesa C (§7-R4): as USER-STRINGS e metodës së PoC-së nuk e përmbajnë
        // emrin — ldstr (0x72) mbart token 0x70xxxxxx; mblidhen string-et reale
        // të ngarkuara nga trupi dhe provohet që asnjëra s'përmban emrin e
        // plotë të tipit dhe as fjalën e bashkuar "UserClaimRecord" (fragmentet
        // e ndajnë brenda fjalës — bashkimi ndodh VETËM në runtime).
        var loadedStrings = new List<string>();
        for (var offset = 0; offset + 5 <= ilBytes.Length; offset++)
        {
            if (ilBytes[offset] != 0x72)
            {
                continue;
            }

            var stringToken = BitConverter.ToInt32(ilBytes, offset + 1);
            if ((stringToken & 0x7F000000) != 0x70000000)
            {
                continue;
            }

            try
            {
                loadedStrings.Add(reader.GetUserString(
                    System.Reflection.Metadata.Ecma335.MetadataTokens.UserStringHandle(stringToken & 0x00FFFFFF)));
            }
            catch (BadImageFormatException)
            {
                // Dritare false-positive e byte-scan-it — jo string real.
            }
        }

        Assert.NotEmpty(loadedStrings); // pozitivi: fragmentet ekzistojnë si user-strings.
        Assert.DoesNotContain(loadedStrings, value =>
            value.Contains("Idata.Platform.Persistence.UserClaimRecord", StringComparison.Ordinal));
        Assert.DoesNotContain(loadedStrings, value =>
            value.Contains("UserClaimRecord", StringComparison.Ordinal));
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

        // §4-R4: SETTER-at e DbSet-eve identity — çdo accessor i derivuar nga
        // zinxhiri framework duhet të prodhojë gjetje te jo-owner.
        internal static void SetUsersBypass(PlatformDbContext context) => context.Users = null!;

        internal static void SetUserClaimsBypass(PlatformDbContext context) => context.UserClaims = null!;

        internal static void SetUserLoginsBypass(PlatformDbContext context) => context.UserLogins = null!;

        internal static void SetUserTokensBypass(PlatformDbContext context) => context.UserTokens = null!;

        internal static void SetUserPasskeysBypass(PlatformDbContext context) => context.UserPasskeys = null!;

        // §7-R4 (ENG-006): PoC i reflection-it dinamik — emri ndërtohet
        // VËRTET në runtime nga fragmente array + string.Concat (kompilatori
        // NUK i palos dot në një literal të vetëm; edhe fjala "UserClaimRecord"
        // është e ndarë në tre fragmente); zero TypeRef/MemberRef/attribute
        // blob i skanueshëm drejt tipit.
        internal static object? DynamicReflectionResidualPoC()
        {
            string[] fragments =
                ["Idata", ".Platform", ".Persistence", ".User", "Claim", "Record",
                 ", Idata", ".Platform", ".Persistence"];
            return Type.GetType(string.Concat(fragments));
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
        var presentation = LoadVerifiedByName("Idata.Platform.Modules.PlatformStatus.Presentation");
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
        var presentation = LoadVerifiedByName("Idata.Platform.Modules.PlatformStatus.Presentation");
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

    /// <summary>
    /// §2-R4: assemblies të DERIVUARA nga katalogu kanonik dhe të ngarkuara me
    /// identitet të VERIFIKUAR kundër TargetPath-it të vlerësuar (MVID match).
    /// </summary>
    private static Assembly[] PlatformAssemblies() =>
        [.. PlatformProjectCatalog.DiscoverProduction().Select(PlatformProjectCatalog.LoadVerified)];

    /// <summary>C2: ngarkim production VETËM përmes kufirit të ngrirë të katalogut.</summary>
    private static Assembly LoadVerifiedByName(string assemblyName)
        => PlatformProjectCatalog.LoadVerified(PlatformProjectCatalog.DiscoverProduction()
            .Single(entry => entry.AssemblyName == assemblyName));
}
