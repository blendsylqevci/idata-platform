using Idata.Platform.Persistence;
using System.Reflection;
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
        string[] approvedSuffixes =
            [Path.Combine("Idata.Platform.Persistence", "Idata.Platform.Persistence.csproj"),
             Path.Combine("Infrastructure", "Idata.Platform.Modules.Identity.Infrastructure.csproj")];
        string[] storePackages =
            ["\"Microsoft.AspNetCore.Identity.EntityFrameworkCore\"", "\"OpenIddict.EntityFrameworkCore\""];

        foreach (var project in AllProjectFiles())
        {
            var content = File.ReadAllText(project);
            if (storePackages.Any(package => content.Contains(package, StringComparison.Ordinal)))
            {
                Assert.Contains(approvedSuffixes, suffix => project.EndsWith(suffix, StringComparison.Ordinal));
            }
        }

        // Të dyja paketat duhet të ekzistojnë realisht në të dy projektet e
        // aprovuara — allowlist-i nuk është i vdekur.
        foreach (var suffix in approvedSuffixes)
        {
            var project = Assert.Single(AllProjectFiles(), path => path.EndsWith(suffix, StringComparison.Ordinal));
            var content = File.ReadAllText(project);
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

    private static readonly string[] SurveillancePrefixes =
        ["Microsoft.AspNetCore.Identity", "Microsoft.Extensions.Identity", "OpenIddict",
         "Microsoft.IdentityModel", "System.IdentityModel",
         "Microsoft.AspNetCore.Authentication", "Microsoft.AspNetCore.DataProtection",
         "Microsoft.Bcl.Cryptography", "IdentityServer", "Duende", "Auth0", "Okta"];

    /// <summary>Registry autoritativ paketë→version (correction R2, §8).</summary>
    private static readonly IReadOnlyDictionary<string, string> IdentityPackageRegistry =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Microsoft.AspNetCore.Identity.EntityFrameworkCore"] = "10.0.10",
            ["OpenIddict.EntityFrameworkCore"] = "7.6.0",
            ["Microsoft.Extensions.Identity.Core"] = "10.0.10",
            ["Microsoft.Extensions.Identity.Stores"] = "10.0.10",
            ["Microsoft.IdentityModel.Abstractions"] = "8.19.2",
            ["Microsoft.IdentityModel.Logging"] = "8.19.2",
            ["Microsoft.IdentityModel.Tokens"] = "8.19.2",
            ["Microsoft.Bcl.Cryptography"] = "10.0.2",
            ["OpenIddict.Abstractions"] = "7.6.0",
            ["OpenIddict.Core"] = "7.6.0",
            ["OpenIddict.EntityFrameworkCore.Models"] = "7.6.0",
        };

    private static readonly string[] DirectStorePackages =
        ["Microsoft.AspNetCore.Identity.EntityFrameworkCore", "OpenIddict.EntityFrameworkCore"];

    private static readonly string[] StoreOwnerProjects =
        ["Idata.Platform.Persistence", "Idata.Platform.Modules.Identity.Infrastructure"];

    /// <summary>Zbulim DINAMIK: çdo csproj (src+tests) duhet të ketë lockfile ngjitur.</summary>
    internal static (string Project, string? Lockfile)[] DiscoverLockfiles(params string[] roots)
        => [.. roots
            .SelectMany(root => Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path =>
            {
                var lockfile = Path.Combine(Path.GetDirectoryName(path)!, "packages.lock.json");
                return (Path.GetFileNameWithoutExtension(path), File.Exists(lockfile) ? lockfile : null);
            })];

    /// <summary>
    /// Auditi FAIL-CLOSED i një lockfile-i: paketë e survejuar e panjohur =
    /// gjetje; version i panjohur = gjetje; `Direct` lejohet VETËM për dy
    /// paketat store te owner-at e aprovuar; lloj i panjohur = gjetje.
    /// </summary>
    internal static List<string> AuditLockfile(string projectName, System.Text.Json.JsonDocument document, bool isStoreOwner)
    {
        var findings = new List<string>();
        foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var dependency in framework.Value.EnumerateObject())
            {
                if (!SurveillancePrefixes.Any(prefix =>
                        dependency.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!IdentityPackageRegistry.TryGetValue(dependency.Name, out var expectedVersion))
                {
                    findings.Add($"{projectName}: paketë identity/security e PA-inventarizuar '{dependency.Name}'");
                    continue;
                }

                var resolved = dependency.Value.GetProperty("resolved").GetString();
                if (resolved != expectedVersion)
                {
                    findings.Add($"{projectName}: version i panjohur {dependency.Name}={resolved} (pritej {expectedVersion})");
                }

                var dependencyType = dependency.Value.GetProperty("type").GetString();
                switch (dependencyType)
                {
                    case "Direct" when !isStoreOwner || !DirectStorePackages.Contains(dependency.Name):
                        findings.Add($"{projectName}: direct owner i PAAUTORIZUAR për {dependency.Name}");
                        break;
                    case "Direct" or "Transitive" or "CentralTransitive":
                        break;
                    default:
                        findings.Add($"{projectName}: lloj i panjohur '{dependencyType}' për {dependency.Name}");
                        break;
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// Inventari FAIL-CLOSED mbi TË GJITHA lockfiles platform/test
    /// (correction R2, §8 — zbulim dinamik, jo listë e hardcoded): çdo
    /// csproj pa lockfile = FAIL; çdo paketë/version i panjohur = FAIL;
    /// `Direct` vetëm te owner-at; owner-at përmbajnë rrënjët e pritura +
    /// KREJT setin e registry-t (inventar jo i vdekur).
    /// </summary>
    [Fact]
    public void Inventari_identity_security_i_TE_GJITHA_lockfiles_eshte_fail_closed()
    {
        var repoRoot = Directory.GetParent(PlatformRoot)!.Parent!.FullName;
        var discovered = DiscoverLockfiles(PlatformRoot, Path.Combine(repoRoot, "tests", "platform"));

        Assert.NotEmpty(discovered);
        foreach (var (project, lockfile) in discovered)
        {
            // Projekt i ri pa lockfile nuk shpëton kurrë nga inventari.
            Assert.True(lockfile is not null, $"csproj PA packages.lock.json: {project}");
        }

        var audited = 0;
        foreach (var (project, lockfile) in discovered)
        {
            var isStoreOwner = StoreOwnerProjects.Contains(project);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockfile!));
            var findings = AuditLockfile(project, document, isStoreOwner);
            Assert.True(findings.Count == 0, string.Join(" · ", findings));

            if (isStoreOwner)
            {
                // Owner-at: rrënjët Direct + KREJT seti i registry-t prezent.
                var names = document.RootElement.GetProperty("dependencies").EnumerateObject()
                    .SelectMany(framework => framework.Value.EnumerateObject())
                    .Select(dependency => dependency.Name)
                    .ToHashSet(StringComparer.Ordinal);
                Assert.All(IdentityPackageRegistry.Keys, package => Assert.Contains(package, names));
                foreach (var direct in DirectStorePackages)
                {
                    var entry = document.RootElement.GetProperty("dependencies").EnumerateObject()
                        .SelectMany(framework => framework.Value.EnumerateObject())
                        .Single(dependency => dependency.Name == direct);
                    Assert.Equal("Direct", entry.Value.GetProperty("type").GetString());
                }
            }

            audited++;
        }

        // Count i DERIVUAR: çdo lockfile i zbuluar u auditua; të dy owner-at u gjetën.
        Assert.Equal(discovered.Length, audited);
        Assert.Equal(2, discovered.Count(pair => StoreOwnerProjects.Contains(pair.Project)));
    }

    /// <summary>Prova DISKRIMINUESE e auditit (§8): çdo shkelje sintetike kapet.</summary>
    [Fact]
    public void Auditi_i_lockfile_kap_paketa_te_reja_versione_dhe_direct_te_paautorizuar()
    {
        static System.Text.Json.JsonDocument Synthetic(string name, string version, string type)
            => System.Text.Json.JsonDocument.Parse(
                "{\"version\":2,\"dependencies\":{\"net10.0\":{\"" + name + "\":{\"type\":\"" + type
                + "\",\"resolved\":\"" + version + "\",\"contentHash\":\"sintetik\"}}}}");

        // Paketë e survejuar e RE (prefix Duende) → gjetje.
        using (var document = Synthetic("Duende.AccessTokenManagement", "1.0.0", "Transitive"))
        {
            Assert.NotEmpty(AuditLockfile("sintetik", document, isStoreOwner: false));
        }

        // Version i panjohur i paketës së njohur → gjetje.
        using (var document = Synthetic("OpenIddict.Core", "9.9.9", "Transitive"))
        {
            Assert.NotEmpty(AuditLockfile("sintetik", document, isStoreOwner: false));
        }

        // Direct te një projekt JO-owner → gjetje.
        using (var document = Synthetic("OpenIddict.EntityFrameworkCore", "7.6.0", "Direct"))
        {
            Assert.NotEmpty(AuditLockfile("sintetik", document, isStoreOwner: false));
        }

        // Direct i paketës JO-store edhe te owner → gjetje.
        using (var document = Synthetic("OpenIddict.Core", "7.6.0", "Direct"))
        {
            Assert.NotEmpty(AuditLockfile("sintetik", document, isStoreOwner: true));
        }

        // Lloj i panjohur → gjetje.
        using (var document = Synthetic("OpenIddict.Core", "7.6.0", "Project"))
        {
            Assert.NotEmpty(AuditLockfile("sintetik", document, isStoreOwner: false));
        }

        // Inverse të lejuara: zero gjetje.
        using (var document = Synthetic("OpenIddict.EntityFrameworkCore", "7.6.0", "Direct"))
        {
            Assert.Empty(AuditLockfile("sintetik", document, isStoreOwner: true));
        }

        using (var document = Synthetic("OpenIddict.Core", "7.6.0", "Transitive"))
        {
            Assert.Empty(AuditLockfile("sintetik", document, isStoreOwner: false));
        }

        using (var document = Synthetic("OpenIddict.EntityFrameworkCore", "7.6.0", "CentralTransitive"))
        {
            Assert.Empty(AuditLockfile("sintetik", document, isStoreOwner: false));
        }
    }

    /// <summary>Zbulimi dinamik kap projekt të ri PA lockfile (fail-closed).</summary>
    [Fact]
    public void Zbulimi_i_projekteve_kap_csproj_pa_lockfile()
    {
        var temporary = Directory.CreateTempSubdirectory("idata_lockfile_guard_");
        try
        {
            File.WriteAllText(Path.Combine(temporary.FullName, "Sintetik.Projekt.csproj"), "<Project />");
            var discovered = DiscoverLockfiles(temporary.FullName);
            var entry = Assert.Single(discovered);
            Assert.Equal("Sintetik.Projekt", entry.Project);
            Assert.Null(entry.Lockfile);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
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
        string[] storeOwners =
            ["Idata.Platform.Persistence", "Idata.Platform.Modules.Identity.Infrastructure"];

        var productionProjects = AllProjectFiles()
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name!)
            .Order()
            .ToArray();
        Assert.NotEmpty(productionProjects);
        Assert.All(storeOwners, owner => Assert.Contains(owner, productionProjects));

        var scanned = 0;
        foreach (var project in productionProjects)
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{project}.dll");
            // Receipt fail-closed: projekt production pa assembly të skanuar = FAIL.
            Assert.True(File.Exists(assemblyPath), $"Assembly i munguar për skanim IL: {project}");

            var isOwner = storeOwners.Contains(project);
            var findings = IdentityStoreIlScanner.Scan(assemblyPath, isOwner);
            Assert.True(findings.Count == 0,
                $"{project}: {string.Join(" · ", findings.Select(finding => $"{finding.Kind}:{finding.Detail}"))}");
            scanned++;
        }

        // Count i DERIVUAR (jo i hardcoded): çdo csproj production u skanua.
        Assert.Equal(productionProjects.Length, scanned);
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

        Assert.Contains(findings, finding =>
            finding.Kind == "claim-id-access" && finding.Detail.StartsWith("get_Id", StringComparison.Ordinal));
        Assert.Contains(findings, finding =>
            finding.Kind == "claim-id-access" && finding.Detail.StartsWith("set_Id", StringComparison.Ordinal));
        Assert.Contains(findings, finding =>
            finding.Kind == "record-type-ref" && finding.Detail == "UserAccountRecord");
        Assert.Contains(findings, finding =>
            finding.Kind == "record-type-ref" && finding.Detail == "UserClaimRecord");
        Assert.Contains(findings, finding => finding.Kind == "record-attribute-string");

        // Inverse: përdorimi i lejuar NUK gjenerohet si gjetje.
        Assert.DoesNotContain(findings, finding =>
            finding.Detail.Contains("PlatformAppUsers", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding => finding.Kind.StartsWith("unreadable", StringComparison.Ordinal));
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

    private static Assembly[] PlatformAssemblies() =>
        [Assembly.Load("Idata.Platform.Modules.PlatformStatus.Domain"),
         Assembly.Load("Idata.Platform.Modules.PlatformStatus.Application"),
         Assembly.Load("Idata.Platform.Modules.PlatformStatus.Infrastructure"),
         Assembly.Load("Idata.Platform.Modules.PlatformStatus.Presentation"),
         Assembly.Load("Idata.Platform.Modules.AccessControl.Domain"),
         Assembly.Load("Idata.Platform.Modules.AccessControl.Application"),
         Assembly.Load("Idata.Platform.Modules.AccessControl.Infrastructure"),
         Assembly.Load("Idata.Platform.Modules.AccessControl.Presentation"),
         Assembly.Load("Idata.Platform.Modules.Identity.Infrastructure"),
         Assembly.Load("Idata.Platform.Persistence"),
         Assembly.Load("Idata.Platform.DbMigrator"),
         Assembly.Load("Idata.Platform.Api")];
}
