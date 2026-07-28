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
         Assembly.Load("Idata.Platform.Persistence"),
         Assembly.Load("Idata.Platform.DbMigrator"),
         Assembly.Load("Idata.Platform.Api")];
}
