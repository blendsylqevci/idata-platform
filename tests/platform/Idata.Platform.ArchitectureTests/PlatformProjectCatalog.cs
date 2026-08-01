using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Katalogu KANONIK i projekteve (correction R4 i PR #28, §2) — AUTORITETI i
/// vetëm i identitetit të projekteve për të gjithë guard-et arkitekturorë.
/// Identiteti i një projekti = path kanonik repo-relative (separator '/') +
/// AssemblyName i VLERËSUAR REALISHT nga MSBuild + TargetPath kanonik i
/// vlerësuar — përmes `dotnet msbuild -getProperty:AssemblyName,TargetPath`
/// (ProcessStartInfo me ArgumentList, pa shell, SDK i pin-uar nga
/// global.json i repo-s, timeout i kufizuar me kill të process tree).
/// AS regex/XML raw, AS basename NUK janë KURRË autoritet dhe nuk ka
/// fallback: çdo dështim evaluation-i, output bosh/malformed, AssemblyName
/// bosh, TargetPath bosh/jashtë rrënjëve të aprovuara, mismatch i identitetit
/// të assembly-t apo duplikat = FAIL i qartë me diagnostikë repo-relative.
/// </summary>
internal static class PlatformProjectCatalog
{
    internal sealed record ProjectEntry(
        string CanonicalPath, string ProjectName, string AssemblyName, string CanonicalTargetPath);

    /// <summary>Owner-at e store-it — VETËM me path kanonik ekzakt (§3-R3).</summary>
    internal static readonly string[] StoreOwnerCanonicalPaths =
        ["src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj",
         "src/platform/Modules/Identity/Infrastructure/Idata.Platform.Modules.Identity.Infrastructure.csproj"];

    internal static bool IsStoreOwner(ProjectEntry entry)
        => StoreOwnerCanonicalPaths.Contains(entry.CanonicalPath, StringComparer.Ordinal);

    /// <summary>Konfigurimi real i build-it të testeve (assembly-t që skanohen).</summary>
    private static readonly string BuildConfiguration =
        typeof(PlatformProjectCatalog).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

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

    /// <summary>
    /// Katalogu production real (i vlerësuar NJËHERË për proces — vlerësim i
    /// freskët MSBuild në çdo run testesh; zero cache në disk): zbulim nën
    /// src/platform + verifikim i plotë i TargetPath/identitetit të assembly.
    /// </summary>
    private static readonly Lazy<ProjectEntry[]> ProductionCatalog = new(() =>
    {
        var repoRoot = RepoRoot();
        var entries = Discover(repoRoot, Path.Combine(repoRoot, "src", "platform"));
        foreach (var entry in entries)
        {
            VerifyTargetAssembly(repoRoot, entry, approvedOutputRootRelative: "src/platform");
        }

        return entries;
    });

    internal static ProjectEntry[] DiscoverProduction() => ProductionCatalog.Value;

    /// <summary>
    /// Zbulim + vlerësim MSBuild + validim fail-closed mbi një pemë arbitrare
    /// (e testueshme sintetikisht): çdo shkelje identiteti hidhet si
    /// përjashtim — kurrë klasifikim i heshtur, kurrë fallback.
    /// </summary>
    /// <summary>
    /// Memoizim VETËM in-process (një vlerësim i freskët MSBuild për çift
    /// rrënjësh për run testesh — determinist; zero cache në disk).
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string), ProjectEntry[]> DiscoveryCache = new();

    internal static ProjectEntry[] Discover(string repoRoot, string searchRoot)
        => DiscoveryCache.GetOrAdd(
            (Path.GetFullPath(repoRoot), Path.GetFullPath(searchRoot)),
            key => DiscoverCore(key.Item1, key.Item2));

    private static ProjectEntry[] DiscoverCore(string repoRoot, string searchRoot)
    {
        var canonicalRepoRoot = Path.GetFullPath(repoRoot);
        var entries = Directory.GetFiles(searchRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Evaluate(canonicalRepoRoot, path))
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
            throw new InvalidOperationException($"AssemblyName i vlerësuar i dyfishtë: {string.Join(", ", duplicateAssemblies)}");
        }

        var duplicateTargets = entries.GroupBy(entry => entry.CanonicalTargetPath, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicateTargets.Length > 0)
        {
            throw new InvalidOperationException($"TargetPath i vlerësuar i dyfishtë: {string.Join(", ", duplicateTargets)}");
        }

        return entries;
    }

    /// <summary>
    /// Verifikimi i identitetit të assembly-t (§2-R4): TargetPath ekziston,
    /// qëndron nën rrënjën e aprovuar build-output dhe emri i lexuar nga VETË
    /// binari përputhet me AssemblyName-in e vlerësuar nga MSBuild.
    /// </summary>
    internal static void VerifyTargetAssembly(string repoRoot, ProjectEntry entry, string approvedOutputRootRelative)
    {
        var absoluteTarget = Path.Combine(repoRoot, entry.CanonicalTargetPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absoluteTarget))
        {
            throw new InvalidOperationException(
                $"TargetPath i vlerësuar mungon (build i munguar?): {entry.CanonicalPath} → {entry.CanonicalTargetPath}");
        }

        if (!entry.CanonicalTargetPath.StartsWith(approvedOutputRootRelative + "/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"TargetPath jashtë rrënjës së aprovuar '{approvedOutputRootRelative}': {entry.CanonicalTargetPath}");
        }

        string binaryName;
        try
        {
            binaryName = AssemblyName.GetAssemblyName(absoluteTarget).Name ?? string.Empty;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"TargetPath i palexueshëm si assembly ({exception.GetType().Name}): {entry.CanonicalTargetPath}");
        }

        if (!string.Equals(binaryName, entry.AssemblyName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Identitet assembly mismatch: MSBuild='{entry.AssemblyName}' vs binar='{binaryName}' ({entry.CanonicalTargetPath})");
        }
    }

    /// <summary>
    /// Ngarkon assembly-n e një entry PËR REFLECTION duke GARANTUAR se binari
    /// i ngarkuar është EKZAKT ai i TargetPath-it të vlerësuar: krahasohet
    /// MVID i modulit të ngarkuar me MVID-in e lexuar nga file-i i TargetPath.
    /// Kopje stale/decoy me të njëjtin emër = FAIL.
    /// </summary>
    internal static Assembly LoadVerified(ProjectEntry entry)
    {
        var repoRoot = RepoRoot();
        VerifyTargetAssembly(repoRoot, entry, approvedOutputRootRelative: "src/platform");
        var absoluteTarget = Path.Combine(repoRoot, entry.CanonicalTargetPath.Replace('/', Path.DirectorySeparatorChar));

        Guid fileMvid;
        using (var stream = File.OpenRead(absoluteTarget))
        using (var portableExecutable = new System.Reflection.PortableExecutable.PEReader(stream))
        {
            var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(portableExecutable);
            fileMvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        }

        var loaded = Assembly.Load(new AssemblyName(entry.AssemblyName));
        if (loaded.ManifestModule.ModuleVersionId != fileMvid)
        {
            throw new InvalidOperationException(
                $"Assembly i ngarkuar ≠ TargetPath (MVID mismatch — kopje stale/decoy?): {entry.AssemblyName}");
        }

        return loaded;
    }

    private static ProjectEntry Evaluate(string canonicalRepoRoot, string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        if (!fullPath.StartsWith(canonicalRepoRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Path jashtë rrënjës së repo-s (spoof/traversal): {Relative(canonicalRepoRoot, fullPath)}");
        }

        var canonicalPath = Relative(canonicalRepoRoot, fullPath);
        var (assemblyName, targetPath) = EvaluateWithMsBuild(canonicalRepoRoot, fullPath, canonicalPath);

        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            throw new InvalidOperationException($"MSBuild ktheu AssemblyName BOSH për: {canonicalPath}");
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException($"MSBuild ktheu TargetPath BOSH për: {canonicalPath}");
        }

        var fullTarget = Path.GetFullPath(targetPath);
        if (!fullTarget.StartsWith(canonicalRepoRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"TargetPath i vlerësuar jashtë rrënjës së repo-s: {canonicalPath} → {Relative(canonicalRepoRoot, fullTarget)}");
        }

        return new ProjectEntry(
            canonicalPath,
            Path.GetFileNameWithoutExtension(fullPath),
            assemblyName,
            Relative(canonicalRepoRoot, fullTarget));
    }

    private static string Relative(string canonicalRepoRoot, string fullPath)
        => Path.GetRelativePath(canonicalRepoRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// Vlerësimi REAL MSBuild (§2-R4): `dotnet msbuild <csproj> -nologo
    /// -getProperty:AssemblyName,TargetPath -p:Configuration=<konfigurimi real>`
    /// — output JSON i strukturuar; ekzekutim pa shell; working dir = rrënja e
    /// repo-s (global.json i pin-uar vendos SDK-në); timeout me kill tree.
    /// </summary>
    private static (string AssemblyName, string TargetPath) EvaluateWithMsBuild(
        string repoRoot, string absoluteProjectPath, string canonicalPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repoRoot,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(absoluteProjectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-getProperty:AssemblyName,TargetPath");
        startInfo.ArgumentList.Add($"-p:Configuration={BuildConfiguration}");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"MSBuild nuk u nis dot për: {canonicalPath}");
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"MSBuild timeout (120s) për: {canonicalPath}");
        }

        var output = stdout.GetAwaiter().GetResult();
        var error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"MSBuild exit {process.ExitCode} për {canonicalPath}: {Sanitize(repoRoot, error, output)}");
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var properties = document.RootElement.GetProperty("Properties");
            return (
                properties.GetProperty("AssemblyName").GetString() ?? string.Empty,
                properties.GetProperty("TargetPath").GetString() ?? string.Empty);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Output MSBuild bosh/malformed për {canonicalPath} ({exception.GetType().Name})");
        }
    }

    /// <summary>Diagnostikë e sanitizuar: path-et absolute → repo-relative.</summary>
    private static string Sanitize(string repoRoot, string error, string output)
    {
        var text = string.IsNullOrWhiteSpace(error) ? output : error;
        var trimmed = text.Length > 500 ? text[..500] : text;
        return trimmed.Replace(repoRoot + Path.DirectorySeparatorChar, string.Empty).Replace(repoRoot, ".");
    }
}
