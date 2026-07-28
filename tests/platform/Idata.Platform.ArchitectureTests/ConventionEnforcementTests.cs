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

    /// <summary>
    /// Inventari FAIL-CLOSED i paketave identity/security (correction pass
    /// PR #28, §8): lockfiles e dy direct owners (Persistence +
    /// Identity.Infrastructure) duhet të përmbajnë EKZAKT setin e njohur
    /// emër→(version, lloj) — çdo paketë identity/security e RE ose version
    /// i panjohur = RED (jo allowlist automatik). `Direct` lejohet VETËM për
    /// dy paketat store të ADR-028.
    /// </summary>
    [Fact]
    public void Inventari_identity_security_i_lockfiles_eshte_fail_closed()
    {
        string[] surveillancePrefixes =
            ["Microsoft.AspNetCore.Identity", "Microsoft.Extensions.Identity", "OpenIddict",
             "Microsoft.IdentityModel", "Microsoft.Bcl.Cryptography",
             "IdentityServer", "Duende", "Auth0", "Okta"];
        var expected = new Dictionary<string, (string Version, string Type)>(StringComparer.Ordinal)
        {
            ["Microsoft.AspNetCore.Identity.EntityFrameworkCore"] = ("10.0.10", "Direct"),
            ["OpenIddict.EntityFrameworkCore"] = ("7.6.0", "Direct"),
            ["Microsoft.Extensions.Identity.Core"] = ("10.0.10", "Transitive"),
            ["Microsoft.Extensions.Identity.Stores"] = ("10.0.10", "Transitive"),
            ["Microsoft.IdentityModel.Abstractions"] = ("8.19.2", "Transitive"),
            ["Microsoft.IdentityModel.Logging"] = ("8.19.2", "Transitive"),
            ["Microsoft.IdentityModel.Tokens"] = ("8.19.2", "Transitive"),
            ["Microsoft.Bcl.Cryptography"] = ("10.0.2", "Transitive"),
            ["OpenIddict.Abstractions"] = ("7.6.0", "Transitive"),
            ["OpenIddict.Core"] = ("7.6.0", "Transitive"),
            ["OpenIddict.EntityFrameworkCore.Models"] = ("7.6.0", "Transitive"),
        };

        string[] lockfiles =
            [Path.Combine(PlatformRoot, "Idata.Platform.Persistence", "packages.lock.json"),
             Path.Combine(PlatformRoot, "Modules", "Identity", "Infrastructure", "packages.lock.json")];

        foreach (var lockfile in lockfiles)
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockfile));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                foreach (var dependency in framework.Value.EnumerateObject())
                {
                    if (!surveillancePrefixes.Any(prefix =>
                            dependency.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    // FAIL-CLOSED: paketë e panjohur identity/security → RED.
                    Assert.True(expected.ContainsKey(dependency.Name),
                        $"Paketë identity/security e PA-inventarizuar: {dependency.Name} ({lockfile})");
                    Assert.Equal(expected[dependency.Name].Version,
                        dependency.Value.GetProperty("resolved").GetString());
                    Assert.Equal(expected[dependency.Name].Type,
                        dependency.Value.GetProperty("type").GetString());
                    seen.Add(dependency.Name);
                }
            }

            // Inventari s'është i vdekur: i GJITHË seti i pritur ekziston.
            Assert.Equal(expected.Keys.Order().ToArray(), seen.Order().ToArray());
        }
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
