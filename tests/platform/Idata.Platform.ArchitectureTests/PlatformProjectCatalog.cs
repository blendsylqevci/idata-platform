using System.Text.RegularExpressions;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Katalogu KANONIK i projekteve production (correction R3 i PR #28, §3) —
/// AUTORITETI i vetëm i identitetit të projekteve për të tre guard-et
/// (kufiri IL i store-it, role-surface, inventari i lockfiles). Identiteti i
/// një projekti = path kanonik repo-relative (separator '/'), emri i
/// projektit dhe AssemblyName REAL (override eksplicit në csproj ose,
/// default-i MSBuild, basename-i i skedarit). Basename-i NUK është KURRË
/// autoritet klasifikimi. Rregulla fail-closed:
///  - path jashtë rrënjës së zbulimit (traversal/spoof) = FAIL;
///  - path kanonik i dyfishtë = FAIL;
///  - AssemblyName i dyfishtë = FAIL;
/// Owner-at e store-it njihen VETËM me path kanonik ekzakt.
/// </summary>
internal static class PlatformProjectCatalog
{
    internal sealed record ProjectEntry(string CanonicalPath, string ProjectName, string AssemblyName);

    /// <summary>Owner-at e store-it — VETËM me path kanonik ekzakt (§3).</summary>
    internal static readonly string[] StoreOwnerCanonicalPaths =
        ["src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj",
         "src/platform/Modules/Identity/Infrastructure/Idata.Platform.Modules.Identity.Infrastructure.csproj"];

    internal static bool IsStoreOwner(ProjectEntry entry)
        => StoreOwnerCanonicalPaths.Contains(entry.CanonicalPath, StringComparer.Ordinal);

    internal static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "platform")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Rrënja e repo-s (src/platform) nuk u gjet nga BaseDirectory.");
    }

    /// <summary>Katalogu production real: src/platform/**/*.csproj nën rrënjën e repo-s.</summary>
    internal static ProjectEntry[] DiscoverProduction()
    {
        var repoRoot = RepoRoot();
        return Discover(repoRoot, Path.Combine(repoRoot, "src", "platform"));
    }

    /// <summary>
    /// Zbulim + validim fail-closed mbi një pemë arbitrare (e testueshme
    /// sintetikisht): çdo shkelje identiteti hidhet si përjashtim — kurrë
    /// klasifikim i heshtur.
    /// </summary>
    internal static ProjectEntry[] Discover(string repoRoot, string searchRoot)
    {
        var canonicalRepoRoot = Path.GetFullPath(repoRoot);
        var entries = Directory.GetFiles(searchRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Canonicalize(canonicalRepoRoot, path))
            .OrderBy(entry => entry.CanonicalPath, StringComparer.Ordinal)
            .ToArray();

        var duplicatePaths = entries.GroupBy(entry => entry.CanonicalPath, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicatePaths.Length > 0)
        {
            throw new InvalidOperationException($"Path kanonik i dyfishtë: {string.Join(", ", duplicatePaths)}");
        }

        var duplicateAssemblies = entries.GroupBy(entry => entry.AssemblyName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicateAssemblies.Length > 0)
        {
            throw new InvalidOperationException($"AssemblyName i dyfishtë: {string.Join(", ", duplicateAssemblies)}");
        }

        return entries;
    }

    private static ProjectEntry Canonicalize(string canonicalRepoRoot, string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        if (!fullPath.StartsWith(canonicalRepoRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Path jashtë rrënjës së repo-s (spoof/traversal): {projectPath}");
        }

        var canonicalPath = Path.GetRelativePath(canonicalRepoRoot, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');
        var projectName = Path.GetFileNameWithoutExtension(fullPath);
        var assemblyName = ReadExplicitAssemblyName(fullPath) ?? projectName;
        return new ProjectEntry(canonicalPath, projectName, assemblyName);
    }

    /// <summary>AssemblyName real: override eksplicit në csproj, ndryshe default-i MSBuild.</summary>
    private static string? ReadExplicitAssemblyName(string projectPath)
    {
        var match = Regex.Match(File.ReadAllText(projectPath),
            @"<AssemblyName>\s*([^<]+?)\s*</AssemblyName>", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }
}
