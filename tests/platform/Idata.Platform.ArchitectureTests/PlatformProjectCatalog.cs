using System.Collections.Frozen;
using System.Collections.Immutable;
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
    /// §4-ENG-008 — argumenti që e bën ÇDO vlerësim MSBuild READ-ONLY ndaj
    /// output-eve production. Është një konstante e vetme, e konsumuar nga TË
    /// GJITHA rrugët e thirrjes, që asnjë call-site i ri të mos e harrojë.
    /// </summary>
    internal const string ReadOnlyEvaluationArgument = "-p:BuildProjectReferences=false";

    /// <summary>
    /// §4-ENG-008 — GJENERATA AUTORITATIVE dhe e PANDRYSHUESHME e katalogut.
    ///
    /// Root cause që mbyll: `ProductionCatalog` ngrinte VETËM path-et, ndërsa
    /// MVID-i lexohej nga disku në çast të mëvonshëm. Një `-t:ResolveReferences`
    /// i ndërmjetëm rindërtonte output-in production (i provuar: mtime ndryshon
    /// vetëm pa <see cref="ReadOnlyEvaluationArgument"/>), prandaj gjenerata A e
    /// katalogut përzihej me gjeneratën B të file-it → dështim i varur nga rendi.
    ///
    /// Modeli i ri: identiteti, MVID-i, fingerprint-i SHA-256, adjacency-a e
    /// ProjectReference-ve, referencat e zgjidhura dhe autoritetet
    /// package/framework ngrihen TË GJITHA në një gjeneratë të vetme, dhe
    /// fingerprint-et verifikohen PARA dhe PAS gjithë vlerësimit. Çdo devijim =
    /// fail-closed i kontrolluar, kurrë refresh i heshtur.
    /// </summary>
    /// <remarks>
    /// Correction C1 (Hardening D): DEEP-IMMUTABLE me tipe KONKRETE BCL —
    /// `ImmutableArray` për sekuencat (edhe vlerat nested), `FrozenDictionary`/
    /// `FrozenSet` për hartat/setet. Asnjë backing array/Dictionary/HashSet i
    /// mutueshëm nuk publikohet; mutimi përmes interface-ve hedh
    /// `NotSupportedException` dhe s'mund të helmojë `CurrentGeneration`.
    /// </remarks>
    internal sealed record CatalogGeneration(
        Guid GenerationId,
        ImmutableArray<ProjectEntry> Projects,
        FrozenDictionary<string, Guid> MvidByCanonicalPath,
        FrozenDictionary<string, string> Sha256ByCanonicalPath,
        FrozenDictionary<string, DateTime> WriteTimeUtcByCanonicalPath,
        FrozenDictionary<string, ImmutableArray<string>> ProjectReferencesByCanonicalPath,
        FrozenDictionary<string, ImmutableArray<ResolvedReference>> ResolvedReferencesByCanonicalPath,
        FrozenDictionary<string, PackageAssetAuthority> PackageAssetsByCanonicalPath,
        FrozenDictionary<string, FrozenSet<string>> FrameworkAssetsByCanonicalPath);

    /// <summary>
    /// Një gjeneratë e VETME për proces, e ndërtuar nën `Lazy` me
    /// `ExecutionAndPublication`: thirrësit paralelë marrin TË NJËJTIN objekt
    /// dhe asnjë vlerësim i dytë nuk nis paralelisht.
    /// </summary>
    private static readonly Lazy<CatalogGeneration> Generation =
        new(BuildGeneration, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static CatalogGeneration CurrentGeneration() => Generation.Value;

    private static CatalogGeneration BuildGeneration()
    {
        var repoRoot = RepoRoot();

        // (1) Zbulim + vlerësim identiteti PARA çdo freeze-i.
        var entries = Discover(repoRoot, Path.Combine(repoRoot, "src", "platform"));
        foreach (var entry in entries)
        {
            VerifyTargetAssembly(repoRoot, entry, approvedOutputRootRelative: "src/platform");
        }

        // (2) Fingerprint-i PARA vlerësimit të referencave.
        var before = FingerprintTargets(repoRoot, entries);

        // (3) Vlerësimi i plotë — i gjithë READ-ONLY, brenda së njëjtës gjeneratë.
        // Vlerat NESTED ngrihen që në burim (Hardening D): ImmutableArray për
        // sekuencat, PackageAssetAuthority me harta/sete të ngrira, FrozenSet
        // për asetet framework.
        var projectReferences = new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal);
        var resolvedReferences = new Dictionary<string, ImmutableArray<ResolvedReference>>(StringComparer.Ordinal);
        var packageAssets = new Dictionary<string, PackageAssetAuthority>(StringComparer.Ordinal);
        var frameworkAssets = new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            projectReferences[entry.CanonicalPath] = [.. EvaluateProjectReferencesCore(repoRoot, entry.CanonicalPath)];
            resolvedReferences[entry.CanonicalPath] = [.. EvaluateResolvedReferencesCore(repoRoot, entry.CanonicalPath)];
            packageAssets[entry.CanonicalPath] =
                EvaluatePackageAssetAuthorityCore(repoRoot, entry.CanonicalPath).ToFrozen();
            frameworkAssets[entry.CanonicalPath] =
                EvaluateFrameworkAssetAuthorityCore(repoRoot, entry.CanonicalPath).ToFrozenSet(StringComparer.Ordinal);
        }

        // (4) Fingerprint-i PAS vlerësimit — devijimi = fail-closed i kontrolluar.
        var after = FingerprintTargets(repoRoot, entries);
        var drift = before
            .Where(pair => !after.TryGetValue(pair.Key, out var value)
                || value.Sha256 != pair.Value.Sha256 || value.Mvid != pair.Value.Mvid
                || value.WriteTimeUtc != pair.Value.WriteTimeUtc)
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (drift.Length > 0)
        {
            throw new InvalidOperationException(
                "catalog-generation-drift: output-i production ndryshoi GJATË vlerësimit "
                + $"(vlerësimi duhet të jetë read-only): {string.Join(", ", drift)}");
        }

        return new CatalogGeneration(
            Guid.NewGuid(),
            [.. entries],
            before.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Mvid, StringComparer.Ordinal),
            before.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Sha256, StringComparer.Ordinal),
            before.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.WriteTimeUtc, StringComparer.Ordinal),
            projectReferences.ToFrozenDictionary(StringComparer.Ordinal),
            resolvedReferences.ToFrozenDictionary(StringComparer.Ordinal),
            packageAssets.ToFrozenDictionary(StringComparer.Ordinal),
            frameworkAssets.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>
    /// Fingerprint TRE-DIMENSIONAL i një output-i: përmbajtja (SHA-256), identiteti
    /// (MVID) DHE koha e shkrimit. Koha është e domosdoshme sepse build-et janë
    /// DETERMINISTË — një rindërtim i burimeve të pandryshuara prodhon bajte
    /// IDENTIKE, prandaj vetëm përmbajtja nuk e dëshmon dot ngjarjen e rindërtimit
    /// që §4-ENG-008 e ndalon.
    /// </summary>
    private static Dictionary<string, (string Sha256, Guid Mvid, DateTime WriteTimeUtc)> FingerprintTargets(
        string repoRoot, ProjectEntry[] entries)
    {
        var fingerprints = new Dictionary<string, (string, Guid, DateTime)>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var absolute = Path.Combine(
                repoRoot, entry.CanonicalTargetPath.Replace('/', Path.DirectorySeparatorChar));
            fingerprints[entry.CanonicalPath] = (
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(absolute))),
                ReadMvid(absolute),
                File.GetLastWriteTimeUtc(absolute));
        }

        return fingerprints;
    }

    /// <summary>
    /// §4-ENG-008 — verifikim i PASTËR i fingerprint-eve të një gjenerate kundër
    /// gjendjes REALE në disk. Kthen path-et kanonike që kanë devijuar (bosh =
    /// e shëndetshme). Ekziston si funksion i pastër që testet ta ushtrojnë me
    /// gjenerata sintetike — pa prekur asnjë output real.
    /// </summary>
    internal static string[] VerifyGenerationFingerprints(CatalogGeneration generation)
    {
        var repoRoot = RepoRoot();
        var drifted = new List<string>();
        foreach (var entry in generation.Projects)
        {
            var absolute = Path.Combine(
                repoRoot, entry.CanonicalTargetPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                drifted.Add(entry.CanonicalPath);
                continue;
            }

            var sha = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(absolute)));
            var mvid = ReadMvid(absolute);
            var writeTime = File.GetLastWriteTimeUtc(absolute);
            if (!generation.Sha256ByCanonicalPath.TryGetValue(entry.CanonicalPath, out var frozenSha)
                || !generation.MvidByCanonicalPath.TryGetValue(entry.CanonicalPath, out var frozenMvid)
                || !generation.WriteTimeUtcByCanonicalPath.TryGetValue(entry.CanonicalPath, out var frozenWrite)
                || sha != frozenSha || mvid != frozenMvid || writeTime != frozenWrite)
            {
                drifted.Add(entry.CanonicalPath);
            }
        }

        return [.. drifted.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Correction C1 (Blocker C) — API-ja E VETME autoritative përmes së cilës
    /// çdo konsumator i një production target-i (skanerët ENG-006, owner
    /// public-surface guard, `LoadVerified`) merr path-in e artefaktit.
    ///
    /// Kontrata: (1) anëtarësi EKZAKTE e `entry`-t në gjeneratën e ngrirë;
    /// (2) fingerprint-i TRE-DIMENSIONAL i file-it real — SHA-256 + MVID +
    /// WriteTimeUtc — krahasohet me vlerat E NGRIRA; (3) çdo devijim hidhet si
    /// `catalog-generation-drift`, KURRË refresh i heshtur. Konsumatorët e
    /// thërrasin PARA dhe PAS leximit/skanimit, që një ndryshim gjatë scan-it
    /// të mos kalojë pa u kapur.
    /// </summary>
    private static string VerifiedFrozenTargetPath(ProjectEntry entry, CatalogGeneration generation)
    {
        if (!generation.Projects.Contains(entry)
            || !generation.Sha256ByCanonicalPath.TryGetValue(entry.CanonicalPath, out var frozenSha)
            || !generation.MvidByCanonicalPath.TryGetValue(entry.CanonicalPath, out var frozenMvid)
            || !generation.WriteTimeUtcByCanonicalPath.TryGetValue(entry.CanonicalPath, out var frozenWrite))
        {
            throw new InvalidOperationException(
                $"Projekt jashtë gjeneratës autoritative të katalogut: {entry.CanonicalPath}");
        }

        var absolute = Path.Combine(
            RepoRoot(), entry.CanonicalTargetPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
        {
            throw new InvalidOperationException(
                $"catalog-generation-drift: output-i i ngrirë mungon në disk ({entry.CanonicalPath}).");
        }

        var sha = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(absolute)));
        var mvid = ReadMvid(absolute);
        var writeTime = File.GetLastWriteTimeUtc(absolute);
        if (sha != frozenSha || mvid != frozenMvid || writeTime != frozenWrite)
        {
            throw new InvalidOperationException(
                "catalog-generation-drift: output-i production nuk përputhet me fingerprint-in "
                + $"e ngrirë SHA-256+MVID+mtime ({entry.CanonicalPath}). Vlerësimi duhet të jetë read-only.");
        }

        return absolute;
    }

    /// <summary>
    /// Correction C4 — **snapshot DEEP-IMMUTABLE i imazhit të verifikuar**.
    /// Zëvendëson çdo API që i dorëzonte kodit arbitrar raw verified path:
    /// këtu **nuk ka path fare**, as `FileSystemInfo`, as `Stream`, as delegate,
    /// as `byte[]` i mutueshëm — vetëm identitet immutable dhe bajtet e lexuara
    /// nën verifikim, si `ImmutableArray&lt;byte&gt;`.
    /// </summary>
    internal sealed record VerifiedAssemblyImage(
        string AssemblyName,
        ImmutableArray<byte> Bytes,
        string Sha256,
        Guid Mvid);

    /// <summary>
    /// Correction C4 — OPERACIONI I VETËM me të cilin lexohet një artefakt
    /// production. Kontrata e detyruar:
    ///  (1) kap NJË `CatalogGeneration` të pandryshueshme;
    ///  (2) **verify-before**: anëtarësi + SHA-256 + MVID + mtime ndaj gjeneratës;
    ///  (3) lexon bajtet NJË herë;
    ///  (4) llogarit SHA-256 **dhe MVID nga PIKËRISHT ATO bajte**;
    ///  (5) i krahason të dyja me vlerat e NGRIRA — kjo kap edhe variantin
    ///      **ABA** (pre/post shohin A, ndërsa reader-i mori bajte B);
    ///  (6) **verify-after**: i njëjti verifikim ndaj së njëjtës gjeneratë;
    ///  (7) vetëm pastaj kthen snapshot-in immutable.
    /// Zero temp-copy, zero path jashtë.
    /// </summary>
    internal static VerifiedAssemblyImage ReadVerifiedImage(ProjectEntry entry)
        => ReadVerifiedImage(entry, CurrentGeneration());

    internal static VerifiedAssemblyImage ReadVerifiedImage(ProjectEntry entry, CatalogGeneration generation)
    {
        var verifiedPath = VerifiedFrozenTargetPath(entry, generation);
        var bytes = File.ReadAllBytes(verifiedPath);

        VerifyImageBytes(entry, generation, bytes);
        _ = VerifiedFrozenTargetPath(entry, generation);
        return new VerifiedAssemblyImage(
            entry.AssemblyName,
            [.. bytes],
            generation.Sha256ByCanonicalPath[entry.CanonicalPath],
            generation.MvidByCanonicalPath[entry.CanonicalPath]);
    }

    /// <summary>
    /// C4 — verifikim i BAJTEVE TË LEXUARA kundër gjeneratës së ngrirë (SHA-256
    /// dhe MVID i llogaritur PIKËRISHT nga ato bajte). Ky është kontrolli që kap
    /// variantin **ABA**: një lexim që kthen bajte B ndërsa verifikimet mbi
    /// file-in shohin identitetin A. Seam i veçantë që testet ta ushtrojnë
    /// drejtpërdrejt — përndryshe nuk do të ishte diskriminues.
    /// </summary>
    internal static void VerifyImageBytes(
        ProjectEntry entry, CatalogGeneration generation, ReadOnlySpan<byte> bytes)
    {
        var frozenSha = generation.Sha256ByCanonicalPath[entry.CanonicalPath];
        var frozenMvid = generation.MvidByCanonicalPath[entry.CanonicalPath];
        var readSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        var readMvid = ReadMvidFromImage(bytes);
        if (!string.Equals(readSha, frozenSha, StringComparison.Ordinal) || readMvid != frozenMvid)
        {
            throw new InvalidOperationException(
                "catalog-generation-drift: bajtet e lexuara nuk përputhen me fingerprint-in e ngrirë "
                + $"(SHA/MVID) — {entry.CanonicalPath}.");
        }
    }

    /// <summary>MVID i lexuar nga BAJTET e imazhit (jo nga file-i) — pjesë e kontrollit ABA.</summary>
    internal static Guid ReadMvidFromImage(ReadOnlySpan<byte> image)
    {
        using var stream = new MemoryStream(image.ToArray(), writable: false);
        using var portableExecutable = new System.Reflection.PortableExecutable.PEReader(stream);
        var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(portableExecutable);
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    /// <summary>
    /// Correction C3/C4 — krahasim identiteti PA publikuar path: a është
    /// `candidateFullPath` EKZAKTËSISHT output-i autoritativ i këtij projekti?
    /// Operacion i NGUSHTË catalog-owned: verify-before → krahasim → verify-after;
    /// hyn një path kandidat, del vetëm `bool`.
    /// </summary>
    internal static bool MatchesVerifiedTarget(
        ProjectEntry entry, CatalogGeneration generation, string candidateFullPath)
    {
        var verifiedPath = VerifiedFrozenTargetPath(entry, generation);
        var matches = string.Equals(
            Path.GetFullPath(candidateFullPath), Path.GetFullPath(verifiedPath), StringComparison.Ordinal);
        _ = VerifiedFrozenTargetPath(entry, generation);
        return matches;
    }

    /// <summary>
    /// Correction C3 (§2B) — numri i `CanonicalTargetPath`-eve DISTINKTE, i
    /// llogaritur BRENDA katalogut, që asnjë test të mos e lexojë vetë vetinë.
    /// </summary>
    internal static int DistinctCanonicalTargetPathCount()
        => CurrentGeneration().Projects
            .Select(entry => entry.CanonicalTargetPath)
            .Distinct(StringComparer.Ordinal)
            .Count();

    /// <summary>
    /// Correction C2 — ngarkim i një assembly-je TË REFERUAR gjatë ecjeve të
    /// grafit: nëse emri i përket katalogut production, kalon DETYRIMISHT nga
    /// <see cref="LoadVerified"/> (pra nga kufiri i ngrirë); përndryshe është
    /// assembly framework-u/pakete dhe ngarkohet normalisht. Kështu asnjë
    /// ecje grafi nuk e ngarkon një target production jashtë kufirit.
    /// </summary>
    internal static Assembly LoadReferencedAssembly(AssemblyName assemblyName)
    {
        var simpleName = assemblyName.Name ?? string.Empty;
        var entry = CurrentGeneration().Projects
            .FirstOrDefault(candidate => candidate.AssemblyName == simpleName);
        return entry is not null ? LoadVerified(entry) : Assembly.Load(assemblyName);
    }

    /// <summary>
    /// C2 — gjeneratë SINTETIKE minimale mbi një artefakt të dhënë (nën rrënjën
    /// e repo-s), për testet e kufirit të konsumit: fingerprint-et llogariten
    /// KËTU (primitivet e leximit mbeten vetëm në katalog), hartat e tjera janë
    /// bosh. Zero prekje e katalogut real.
    /// </summary>
    internal static CatalogGeneration SyntheticGenerationFor(ProjectEntry entry)
    {
        var fingerprints = FingerprintTargets(RepoRoot(), [entry]);
        var (sha, mvid, writeTime) = fingerprints[entry.CanonicalPath];
        return new CatalogGeneration(
            Guid.NewGuid(),
            [entry],
            new Dictionary<string, Guid> { [entry.CanonicalPath] = mvid }
                .ToFrozenDictionary(StringComparer.Ordinal),
            new Dictionary<string, string> { [entry.CanonicalPath] = sha }
                .ToFrozenDictionary(StringComparer.Ordinal),
            new Dictionary<string, DateTime> { [entry.CanonicalPath] = writeTime }
                .ToFrozenDictionary(StringComparer.Ordinal),
            new Dictionary<string, ImmutableArray<string>>().ToFrozenDictionary(StringComparer.Ordinal),
            new Dictionary<string, ImmutableArray<ResolvedReference>>().ToFrozenDictionary(StringComparer.Ordinal),
            new Dictionary<string, PackageAssetAuthority>().ToFrozenDictionary(StringComparer.Ordinal),
            new Dictionary<string, FrozenSet<string>>().ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>A i përket ky root pemës REALE të repo-s (pra gjeneratës së ngrirë)?</summary>
    private static bool IsRealRepoRoot(string repoRoot)
        => Path.GetFullPath(repoRoot) == Path.GetFullPath(RepoRoot());

    /// <summary>
    /// §5B-R6: kthehet gjithmonë KOPJE e re — mutimi nga thirrësi nuk mund
    /// të helmojë thirrjet pasuese. §4-ENG-008: burimi është gjenerata e ngrirë.
    /// </summary>
    internal static ProjectEntry[] DiscoverProduction() => [.. CurrentGeneration().Projects];

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

        // Correction C4: operacion i NGUSHTË catalog-owned — verify-before →
        // ngarkim → verify-after, pa i dhënë asnjë callback-u raw path.
        var generation = CurrentGeneration();
        _ = VerifiedFrozenTargetPath(entry, generation);

        var loaded = Assembly.Load(new AssemblyName(entry.AssemblyName));
        if (loaded.ManifestModule.ModuleVersionId != generation.MvidByCanonicalPath[entry.CanonicalPath])
        {
            throw new InvalidOperationException(
                $"Assembly i ngarkuar ≠ TargetPath (MVID mismatch — kopje stale/decoy?): {entry.AssemblyName}");
        }

        _ = VerifiedFrozenTargetPath(entry, generation);
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
        startInfo.ArgumentList.Add(ReadOnlyEvaluationArgument);

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
        startInfo.ArgumentList.Add(ReadOnlyEvaluationArgument);

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
    internal static string[] EvaluateProjectReferences(string repoRoot, string canonicalCsprojPath)
    {
        if (IsRealRepoRoot(repoRoot)
            && CurrentGeneration().ProjectReferencesByCanonicalPath.TryGetValue(canonicalCsprojPath, out var frozen))
        {
            return [.. frozen];
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
        if (IsRealRepoRoot(repoRoot)
            && CurrentGeneration().ResolvedReferencesByCanonicalPath.TryGetValue(canonicalCsprojPath, out var frozen))
        {
            return [.. frozen];
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
    /// <summary>
    /// §3-R8 — AUTORITETI TUPLE-BOUND i asset-eve: harta
    /// (packageId i normalizuar, resolvedVersion) → set-i EKZAKT i asset-eve
    /// të ASAJ pakete/versioni, plus harta inverse asset → tuple. Unioni i
    /// sheshtë (<see cref="Assets"/>) ekziston vetëm për diagnostikë; ai NUK
    /// përdoret më si autoritet — ndryshe metadata e paketës A mund të
    /// kombinohej me asset-in real të paketës B.
    /// </summary>
    internal sealed record PackageAssetAuthority(
        IReadOnlyDictionary<(string PackageId, string Version), IReadOnlySet<string>> AssetsByTuple,
        IReadOnlyDictionary<string, (string PackageId, string Version)> TupleByAsset)
    {
        internal IReadOnlySet<string> Assets => TupleByAsset.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Correction C1 (Hardening D): kopje me TË GJITHA strukturat — edhe vlerat
    /// nested — të ngrira (`FrozenDictionary`/`FrozenSet`); përdoret nga
    /// gjenerata e katalogut që asnjë hartë/set i brendshëm të mos mutohet.
    /// </summary>
    internal PackageAssetAuthority ToFrozen()
        => new(
            AssetsByTuple.ToFrozenDictionary(
                pair => pair.Key,
                pair => (IReadOnlySet<string>)pair.Value.ToFrozenSet(StringComparer.Ordinal)),
            TupleByAsset.ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

        /// <summary>Anëtarësi EKZAKTE në asset-set-in e TUPLE-s (id case-insensitive, version ekzakt).</summary>
        internal bool ContainsForTuple(string packageId, string version, string canonicalAssetPath)
            => AssetsByTuple.TryGetValue((packageId.ToLowerInvariant(), version), out var set)
               && set.Contains(canonicalAssetPath);

        /// <summary>A ekziston një asset me këtë emër file-i BRENDA tuple-s së dhënë?</summary>
        internal bool ContainsFileNameForTuple(string packageId, string version, string assetFileName)
            => AssetsByTuple.TryGetValue((packageId.ToLowerInvariant(), version), out var set)
               && set.Any(asset => string.Equals(Path.GetFileName(asset), assetFileName, StringComparison.Ordinal));
    }

    internal static PackageAssetAuthority EvaluatePackageAssetAuthority(string repoRoot, string canonicalCsprojPath)
    {
        if (IsRealRepoRoot(repoRoot)
            && CurrentGeneration().PackageAssetsByCanonicalPath.TryGetValue(canonicalCsprojPath, out var frozen))
        {
            return frozen;
        }

        return EvaluatePackageAssetAuthorityCore(repoRoot, canonicalCsprojPath);
    }

    private static PackageAssetAuthority EvaluatePackageAssetAuthorityCore(string repoRoot, string canonicalCsprojPath)
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

        var assetsByTuple = new Dictionary<(string, string), HashSet<string>>();
        var tupleByAsset = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
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
                                if (!File.Exists(candidate))
                                {
                                    continue;
                                }

                                var tuple = (identity[0].ToLowerInvariant(), identity[1]);
                                if (!assetsByTuple.TryGetValue(tuple, out var set))
                                {
                                    set = new HashSet<string>(StringComparer.Ordinal);
                                    assetsByTuple[tuple] = set;
                                }

                                set.Add(candidate);

                                // Membership i PAQARTË (i njëjti path te dy tuple) = fail-closed.
                                if (tupleByAsset.TryGetValue(candidate, out var existing)
                                    && existing != tuple)
                                {
                                    throw new InvalidOperationException(
                                        $"Asset i PAQARTË '{Path.GetFileName(candidate)}' i pretenduar nga "
                                        + $"{existing.Item1}/{existing.Item2} dhe {tuple.Item1}/{tuple.Item2}: {canonicalCsprojPath}");
                                }

                                tupleByAsset[candidate] = tuple;
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
        if (declaredPackageLibraries > 0 && tupleByAsset.Count == 0)
        {
            throw new InvalidOperationException(
                $"Asset-set BOSH megjithëse assets deklaron {declaredPackageLibraries} paketa: {canonicalCsprojPath}");
        }

        return new PackageAssetAuthority(
            assetsByTuple.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlySet<string>)pair.Value),
            tupleByAsset);
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
        if (IsRealRepoRoot(repoRoot)
            && CurrentGeneration().FrameworkAssetsByCanonicalPath.TryGetValue(canonicalCsprojPath, out var frozen))
        {
            return frozen;
        }

        return EvaluateFrameworkAssetAuthorityCore(repoRoot, canonicalCsprojPath);
    }

    private static IReadOnlySet<string> EvaluateFrameworkAssetAuthorityCore(string repoRoot, string canonicalCsprojPath)
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

        // §4-ENG-008: ÇDO thirrje fëmijë është READ-ONLY ndaj output-eve
        // production. Pa këtë, `-t:ResolveReferences` rindërton ProjectReference-at
        // dhe e ndryshon binarin NËN këmbët e një snapshot-i tashmë të ngrirë
        // (root cause i provuar: mtime i output-it ndryshon vetëm pa këtë flag).
        startInfo.ArgumentList.Add(ReadOnlyEvaluationArgument);

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
