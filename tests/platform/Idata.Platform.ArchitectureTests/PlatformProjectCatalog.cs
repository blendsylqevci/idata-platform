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

    /// <summary>
    /// §5B-R6: kthehet gjithmonë KOPJE e re — mutimi nga thirrësi nuk mund
    /// të helmojë thirrjet pasuese (vlerësimi mbetet një-për-proces në Lazy).
    /// </summary>
    internal static ProjectEntry[] DiscoverProduction() => [.. ProductionCatalog.Value];

    /// <summary>
    /// Zbulim + vlerësim MSBuild + validim fail-closed mbi një pemë arbitrare
    /// (e testueshme sintetikisht): çdo shkelje identiteti hidhet si
    /// përjashtim — kurrë klasifikim i heshtur, kurrë fallback.
    /// </summary>
    /// <summary>
    /// §4-R5: `Discover` kryen zbulim + vlerësim MSBuild TË FRESKËT në ÇDO
    /// thirrje (zero cache sipas path-it — add/change/remove i csproj-ve
    /// shihet menjëherë nga thirrja pasuese) dhe kthen array të RI që
    /// thirrësi s'mund ta ndikojë për thirrjen tjetër. Përjashtimi i VETËM i
    /// dokumentuar: <see cref="ProductionCatalog"/> mbetet Lazy
    /// (një-vlerësim-për-proces) sepse pema production nuk ndryshon gjatë
    /// një test-run-i — pemët sintetike NUK kalojnë asnjëherë andej.
    /// </summary>
    internal static ProjectEntry[] Discover(string repoRoot, string searchRoot)
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
    /// <summary>
    /// §R6 fix — çaktivizim i node-reuse + MSBuild-server për çdo thirrje
    /// fëmijë: kur `dotnet msbuild` ekzekutohet nga BRENDA test-host-it me
    /// ridrejtim stdout/stderr, node-i worker i shkëputur (reuse) TRASHËGON
    /// handle-t e pipe-it dhe, edhe pasi procesi kryesor del, e mban të hapur
    /// skajin e shkrimit — `ReadToEnd` s'merr kurrë EOF dhe procesi ngec me
    /// CPU~0. Çaktivizimi i reuse-it + serverit e zgjidh në rrënjë.
    /// </summary>
    private static ProcessStartInfo NewMsBuildStartInfo(string repoRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repoRoot,
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        return startInfo;
    }

    private static (string AssemblyName, string TargetPath) EvaluateWithMsBuild(
        string repoRoot, string absoluteProjectPath, string canonicalPath)
    {
        var startInfo = NewMsBuildStartInfo(repoRoot);
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add("-nodeReuse:false");
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

    /// <summary>
    /// §2-R5: seti REAL i PackageReference-ve DIREKTE të një csproj, i
    /// vlerësuar nga MSBuild (`-getItem:PackageReference`) — i njëjti motor
    /// procesi si vlerësimi i identitetit; dështim/output i palexueshëm =
    /// përjashtim fail-closed, kurrë set bosh i heshtur.
    /// </summary>
    internal static string[] EvaluateDirectPackageReferences(string repoRoot, string canonicalCsprojPath)
    {
        var absoluteProjectPath = Path.Combine(
            Path.GetFullPath(repoRoot), canonicalCsprojPath.Replace('/', Path.DirectorySeparatorChar));
        var startInfo = NewMsBuildStartInfo(Path.GetFullPath(repoRoot));
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        startInfo.ArgumentList.Add(absoluteProjectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-getItem:PackageReference");
        startInfo.ArgumentList.Add($"-p:Configuration={BuildConfiguration}");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"MSBuild nuk u nis dot për PackageReference: {canonicalCsprojPath}");
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"MSBuild timeout (120s) për PackageReference: {canonicalCsprojPath}");
        }

        var output = stdout.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"MSBuild exit {process.ExitCode} për PackageReference {canonicalCsprojPath}: "
                + Sanitize(Path.GetFullPath(repoRoot), stderr.GetAwaiter().GetResult(), output));
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("Items", out var items)
                || !items.TryGetProperty("PackageReference", out var references))
            {
                return [];
            }

            return [.. references.EnumerateArray()
                .Select(item => item.GetProperty("Identity").GetString()
                    ?? throw new InvalidOperationException($"PackageReference pa Identity: {canonicalCsprojPath}"))
                .Order(StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException(
                $"Output MSBuild PackageReference bosh/malformed për {canonicalCsprojPath} ({exception.GetType().Name})");
        }
    }

    /// <summary>
    /// §4-R6: edges REALE të ProjectReference të një csproj (vlerësim MSBuild
    /// `-getItem:ProjectReference`) → path-e kanonike repo-relative; path
    /// jashtë rrënjës = FAIL.
    /// </summary>
    /// <summary>
    /// Memoizim VETËM per-proces dhe VETËM për path-et nën rrënjën REALE të
    /// repo-s (i njëjti përjashtim i dokumentuar si <see cref="ProductionCatalog"/>:
    /// pema production nuk ndryshon gjatë një test-run-i). Pemët sintetike
    /// nuk kalojnë kurrë këtej — ato thërrasin Discover(...) të freskët.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> ProductionProjectRefsMemo = new(StringComparer.Ordinal);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ResolvedReference[]> ProductionResolvedRefsMemo = new(StringComparer.Ordinal);

    internal static string[] EvaluateProjectReferences(string repoRoot, string canonicalCsprojPath)
    {
        if (Path.GetFullPath(repoRoot) == Path.GetFullPath(RepoRoot()))
        {
            return [.. ProductionProjectRefsMemo.GetOrAdd(canonicalCsprojPath,
                path => EvaluateProjectReferencesCore(repoRoot, path))];
        }

        return EvaluateProjectReferencesCore(repoRoot, canonicalCsprojPath);
    }

    private static string[] EvaluateProjectReferencesCore(string repoRoot, string canonicalCsprojPath)
    {
        var canonicalRepoRoot = Path.GetFullPath(repoRoot);
        var absoluteProjectPath = Path.Combine(
            canonicalRepoRoot, canonicalCsprojPath.Replace('/', Path.DirectorySeparatorChar));
        var projectDirectory = Path.GetDirectoryName(absoluteProjectPath)!;

        using var document = RunMsBuildGetJson(canonicalRepoRoot, canonicalCsprojPath,
            ["msbuild", absoluteProjectPath, "-nologo", "-getItem:ProjectReference", $"-p:Configuration={BuildConfiguration}"]);
        if (!document.RootElement.TryGetProperty("Items", out var items)
            || !items.TryGetProperty("ProjectReference", out var references))
        {
            return [];
        }

        return [.. references.EnumerateArray()
            .Select(item =>
            {
                var identity = item.GetProperty("Identity").GetString()
                    ?? throw new InvalidOperationException($"ProjectReference pa Identity: {canonicalCsprojPath}");
                var full = Path.GetFullPath(Path.IsPathRooted(identity)
                    ? identity
                    : Path.Combine(projectDirectory, identity));
                if (!full.StartsWith(canonicalRepoRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"ProjectReference jashtë rrënjës së repo-s: {canonicalCsprojPath} → {identity}");
                }

                return Relative(canonicalRepoRoot, full);
            })
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// §4-R6: closure-i TRANZITIV real i ProjectReference-ve të një owner-i
    /// (BFS mbi edges e vlerësuara nga MSBuild; NUK e përfshin vetë owner-in).
    /// </summary>
    internal static IReadOnlySet<string> ProjectReferenceClosure(string repoRoot, string ownerCanonicalPath)
    {
        var closure = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(EvaluateProjectReferences(repoRoot, ownerCanonicalPath));
        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            if (!closure.Add(path))
            {
                continue;
            }

            foreach (var edge in EvaluateProjectReferences(repoRoot, path))
            {
                queue.Enqueue(edge);
            }
        }

        return closure;
    }

    /// <summary>
    /// §3-R6: referencë e ZGJIDHUR realisht nga MSBuild reference resolution
    /// (pas target-it `ResolveReferences`) — provenance e plotë strukturore.
    /// </summary>
    internal sealed record ResolvedReference(
        string FullPath,
        string AssemblySimpleName,
        string? FusionName,
        string? ReferenceSourceTarget,
        string? ResolvedFrom,
        string? SourceProjectFile,
        string? NuGetPackageId,
        string? NuGetPackageVersion,
        string? FrameworkReferenceName,
        string? OriginalItemSpec);

    /// <summary>
    /// §3-R6: seti REAL i ReferencePath-eve të një csproj, i marrë PAS
    /// target-it `ResolveReferences` (pa target items janë BOSH — verifikuar
    /// empirikisht; komanda dështon fail-closed në çdo devijim). Emri i
    /// thjeshtë i assembly-t merret nga FusionName (identiteti real), jo nga
    /// basename-i i file-it.
    /// </summary>
    internal static ResolvedReference[] EvaluateResolvedReferences(string repoRoot, string canonicalCsprojPath)
    {
        if (Path.GetFullPath(repoRoot) == Path.GetFullPath(RepoRoot()))
        {
            return [.. ProductionResolvedRefsMemo.GetOrAdd(canonicalCsprojPath,
                path => EvaluateResolvedReferencesCore(repoRoot, path))];
        }

        return EvaluateResolvedReferencesCore(repoRoot, canonicalCsprojPath);
    }

    private static ResolvedReference[] EvaluateResolvedReferencesCore(string repoRoot, string canonicalCsprojPath)
    {
        var canonicalRepoRoot = Path.GetFullPath(repoRoot);
        var absoluteProjectPath = Path.Combine(
            canonicalRepoRoot, canonicalCsprojPath.Replace('/', Path.DirectorySeparatorChar));

        using var document = RunMsBuildGetJson(canonicalRepoRoot, canonicalCsprojPath,
            ["msbuild", absoluteProjectPath, "-nologo", "-t:ResolveReferences",
             "-getItem:ReferencePath", $"-p:Configuration={BuildConfiguration}"]);
        if (!document.RootElement.TryGetProperty("Items", out var items)
            || !items.TryGetProperty("ReferencePath", out var references)
            || references.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"ReferencePath BOSH pas ResolveReferences për: {canonicalCsprojPath} (target-i s'u ekzekutua?)");
        }

        static string? Metadata(JsonElement item, string name)
            => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 } text ? text : null;

        return [.. references.EnumerateArray().Select(item =>
        {
            var fullPath = item.GetProperty("Identity").GetString()
                ?? throw new InvalidOperationException($"ReferencePath pa Identity: {canonicalCsprojPath}");
            var fusion = Metadata(item, "FusionName");
            var simpleName = fusion?.Split(',')[0].Trim()
                ?? throw new InvalidOperationException(
                    $"ReferencePath pa FusionName (identitet i paverifikueshëm): {canonicalCsprojPath} → {Path.GetFileName(fullPath)}");
            return new ResolvedReference(
                Path.GetFullPath(fullPath),
                simpleName,
                fusion,
                Metadata(item, "ReferenceSourceTarget"),
                Metadata(item, "ResolvedFrom"),
                Metadata(item, "MSBuildSourceProjectFile"),
                Metadata(item, "NuGetPackageId"),
                Metadata(item, "NuGetPackageVersion"),
                Metadata(item, "FrameworkReferenceName"),
                Metadata(item, "OriginalItemSpec"));
        })];
    }

    /// <summary>
    /// §3-R7 — ASSET-SET-i AUTORITATIV i paketave për një owner/TFM, i
    /// derivuar nga autoriteti i RESTORE-it (`obj/project.assets.json`):
    /// për çdo hyrje `targets/&lt;tfm&gt;/&lt;id&gt;/&lt;version&gt;` me `type=package`,
    /// path-et e `compile`/`runtime` zgjidhen kundër `packageFolders`.
    /// Seti ndërtohet VEÇMAS nga kandidati që klasifikohet; asnjë shkrim në
    /// cache-in global. Assets-file i munguar/malformed = përjashtim
    /// fail-closed. Kthen path-e absolute kanonike.
    /// </summary>
    internal static IReadOnlySet<string> EvaluatePackageAssetAuthority(string repoRoot, string canonicalCsprojPath)
    {
        var canonicalRepoRoot = Path.GetFullPath(repoRoot);
        var projectDirectory = Path.GetDirectoryName(Path.Combine(
            canonicalRepoRoot, canonicalCsprojPath.Replace('/', Path.DirectorySeparatorChar)))!;
        var assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            throw new InvalidOperationException(
                $"project.assets.json mungon (restore i munguar?): {canonicalCsprojPath}");
        }

        var assets = new HashSet<string>(StringComparer.Ordinal);
        var declaredPackageLibraries = 0;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            var folders = document.RootElement.GetProperty("packageFolders").EnumerateObject()
                .Select(folder => Path.GetFullPath(folder.Name))
                .ToArray();
            if (folders.Length == 0)
            {
                throw new InvalidOperationException($"packageFolders bosh: {canonicalCsprojPath}");
            }

            foreach (var target in document.RootElement.GetProperty("targets").EnumerateObject())
            {
                foreach (var library in target.Value.EnumerateObject())
                {
                    if (!library.Value.TryGetProperty("type", out var type)
                        || type.GetString() != "package")
                    {
                        continue;
                    }

                    declaredPackageLibraries++;
                    var identity = library.Name.Split('/');
                    if (identity.Length != 2)
                    {
                        throw new InvalidOperationException(
                            $"Identitet biblioteke i pavlefshëm '{library.Name}' në assets: {canonicalCsprojPath}");
                    }

                    foreach (var section in (string[])["compile", "runtime"])
                    {
                        if (!library.Value.TryGetProperty(section, out var items)
                            || items.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        foreach (var item in items.EnumerateObject())
                        {
                            if (item.Name.EndsWith("_._", StringComparison.Ordinal))
                            {
                                continue; // placeholder i NuGet-it, jo asset real
                            }

                            foreach (var folder in folders)
                            {
                                var candidate = Path.GetFullPath(Path.Combine(
                                    folder, identity[0].ToLowerInvariant(), identity[1],
                                    item.Name.Replace('/', Path.DirectorySeparatorChar)));
                                if (File.Exists(candidate))
                                {
                                    assets.Add(candidate);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"project.assets.json malformed për {canonicalCsprojPath} ({exception.GetType().Name})");
        }

        // Projekt pa asnjë paketë (vetëm ProjectReference) → set legjitimisht
        // bosh; POR paketa të deklaruara ME zero assets të zgjidhura =
        // fail-closed (restore i korruptuar/asset-e të munguara në disk).
        if (declaredPackageLibraries > 0 && assets.Count == 0)
        {
            throw new InvalidOperationException(
                $"Asset-set BOSH megjithëse assets deklaron {declaredPackageLibraries} paketa: {canonicalCsprojPath}");
        }

        return assets;
    }

    /// <summary>
    /// §3-R7 — ASSET-SET-i AUTORITATIV i framework-ut për një owner: nga
    /// `ResolvedTargetingPack` (evaluated per-owner) merret direktoria e
    /// targeting pack-ut dhe si autoritet merren VETËM reference assemblies
    /// nën `ref/&lt;tfm&gt;` — jo çdo file nën packs-root (analyzers/data janë
    /// jashtë). Zero ResolvedTargetingPack / direktori e munguar = fail-closed.
    /// </summary>
    internal static IReadOnlySet<string> EvaluateFrameworkAssetAuthority(string repoRoot, string canonicalCsprojPath)
    {
        var canonicalRepoRoot = Path.GetFullPath(repoRoot);
        var absoluteProjectPath = Path.Combine(
            canonicalRepoRoot, canonicalCsprojPath.Replace('/', Path.DirectorySeparatorChar));

        using var document = RunMsBuildGetJson(canonicalRepoRoot, canonicalCsprojPath,
            ["msbuild", absoluteProjectPath, "-nologo", "-t:ResolveReferences",
             "-getItem:ResolvedTargetingPack", $"-p:Configuration={BuildConfiguration}"]);
        if (!document.RootElement.TryGetProperty("Items", out var items)
            || !items.TryGetProperty("ResolvedTargetingPack", out var packs)
            || packs.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"ResolvedTargetingPack BOSH për: {canonicalCsprojPath} (framework authority e paderivueshme)");
        }

        var assets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pack in packs.EnumerateArray())
        {
            if (!pack.TryGetProperty("PackageDirectory", out var directoryElement)
                || directoryElement.GetString() is not { Length: > 0 } packDirectory)
            {
                throw new InvalidOperationException(
                    $"ResolvedTargetingPack pa PackageDirectory: {canonicalCsprojPath}");
            }

            var referenceRoot = Path.Combine(Path.GetFullPath(packDirectory), "ref");
            if (!Directory.Exists(referenceRoot))
            {
                throw new InvalidOperationException(
                    $"Targeting pack pa direktori 'ref': {canonicalCsprojPath} → {pack.GetProperty("Identity").GetString()}");
            }

            foreach (var assembly in Directory.EnumerateFiles(referenceRoot, "*.dll", SearchOption.AllDirectories))
            {
                assets.Add(Path.GetFullPath(assembly));
            }
        }

        if (assets.Count == 0)
        {
            throw new InvalidOperationException($"Asset-set i framework-ut BOSH: {canonicalCsprojPath}");
        }

        return assets;
    }

    /// <summary>§2-R7: MVID i një file-i assembly (fail-closed në lexim të pamundur).</summary>
    internal static Guid ReadMvid(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var portableExecutable = new System.Reflection.PortableExecutable.PEReader(stream);
        var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(portableExecutable);
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    /// <summary>Motori i përbashkët i thirrjeve MSBuild me output JSON (fail-closed).</summary>
    private static JsonDocument RunMsBuildGetJson(string repoRoot, string canonicalCsprojPath, string[] arguments)
    {
        var startInfo = NewMsBuildStartInfo(repoRoot);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
            if (argument == "msbuild")
            {
                startInfo.ArgumentList.Add("-nodeReuse:false");
            }
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"MSBuild nuk u nis dot për: {canonicalCsprojPath}");
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(180)))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"MSBuild timeout (180s) për: {canonicalCsprojPath}");
        }

        var output = stdout.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"MSBuild exit {process.ExitCode} për {canonicalCsprojPath}: "
                + Sanitize(repoRoot, stderr.GetAwaiter().GetResult(), output));
        }

        try
        {
            return JsonDocument.Parse(output);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Output MSBuild bosh/malformed për {canonicalCsprojPath} ({exception.GetType().Name})");
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
