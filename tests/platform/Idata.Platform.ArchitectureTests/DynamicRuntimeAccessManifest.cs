using System.Text.Json;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Hyrje EKZAKTE e manifestit ENG-006. Pa wildcard dhe pa përjashtim blanket:
/// identiteti është (projekt kanonik, assembly, tip, anëtar) plus arsyeja,
/// pronari dhe evidenca/fingerprint-i. **Asnjë përjashtim nuk autorizohet sot.**
/// </summary>
internal sealed record AllowedDynamicAccess(
    string CanonicalProjectPath,
    string AssemblyName,
    string DeclaringType,
    string Member,
    string Reason,
    string Owner,
    string Evidence);

/// <summary>
/// Correction C1 (Blocker A) — hyrje e LEDGER-it të provenance-ës për mbetjet
/// REALE të source-generatorëve të pin-uar. **NUK është manifest i kodit tonë**
/// (ai mbetet BOSH): është inventar EKZAKT i asaj që generatorët e aprovuar
/// emetojnë sot brenda production assemblies. Identiteti = assembly + tip i
/// NORMALIZUAR (identiteti file-local i OpenAPI-t: **literal `F` + 64 hex**
/// → `F{INPUT_HASH_64}`) + anëtar
/// + kind + detail + tool + version i DEKODUAR nga metadata + paketa e pin-uar
/// që e siguron generatorin. Çdo gjetje `generated-*` pa përputhje EKZAKTE =
/// blocking, pavarësisht kind-it.
/// </summary>
internal sealed record GeneratedResidualEntry(
    string AssemblyName,
    string NormalizedDeclaringType,
    string Member,
    string Kind,
    string Detail,
    string Tool,
    string ToolVersion,
    string PackageId);

/// <summary>
/// Manifesti i përjashtimeve ENG-006 dhe validatori i tij fail-closed.
///
/// **Manifesti production është BOSH në këtë increment** — dhe testet e
/// imponojnë boshllëkun. Çdo përjashtim i ardhshëm kërkon të paktën: path
/// kanonik, identitet assembly-je, tip, anëtar, arsye, pronar, fingerprint/
/// evidencë, review të pavarur dhe Human Gate.
/// </summary>
internal static class DynamicRuntimeAccessManifest
{
    /// <summary>BOSH me qëllim — zero akses dinamik i autorizuar në production.</summary>
    internal static readonly AllowedDynamicAccess[] Production = [];

    /// <summary>
    /// Validim FAIL-CLOSED: çdo gjetje e kodit TONË duhet të ketë hyrje EKZAKTE;
    /// gjetjet e pa-atribuueshme/të palexueshme nuk mbulohen KURRË; hyrje e
    /// vdekur, e dyfishtë, me wildcard ose me fushë bosh = problem.
    /// </summary>
    internal static IReadOnlyList<string> Validate(
        IReadOnlyList<AllowedDynamicAccess> manifest,
        IReadOnlyList<PlatformProjectCatalog.ProjectEntry> catalog,
        IReadOnlyList<DynamicRuntimeAccessScanner.Finding> findings)
        => Validate(manifest, catalog, findings, GeneratedResidualLedger);

    internal static IReadOnlyList<string> Validate(
        IReadOnlyList<AllowedDynamicAccess> manifest,
        IReadOnlyList<PlatformProjectCatalog.ProjectEntry> catalog,
        IReadOnlyList<DynamicRuntimeAccessScanner.Finding> findings,
        IReadOnlyList<GeneratedResidualEntry> ledger)
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var consumed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in manifest)
        {
            var identity = $"{entry.AssemblyName}|{entry.DeclaringType}|{entry.Member}";
            foreach (var (field, value) in new (string, string)[]
            {
                ("CanonicalProjectPath", entry.CanonicalProjectPath),
                ("AssemblyName", entry.AssemblyName),
                ("DeclaringType", entry.DeclaringType),
                ("Member", entry.Member),
                ("Reason", entry.Reason),
                ("Owner", entry.Owner),
                ("Evidence", entry.Evidence),
            })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    problems.Add($"empty-field:{identity}:{field}");
                }
                else if (value.Contains('*', StringComparison.Ordinal))
                {
                    problems.Add($"wildcard-entry:{identity}:{field}");
                }
            }

            if (!seen.Add(identity))
            {
                problems.Add($"duplicate-entry:{identity}");
            }

            if (!catalog.Any(project =>
                string.Equals(project.CanonicalPath, entry.CanonicalProjectPath, StringComparison.Ordinal)
                && string.Equals(project.AssemblyName, entry.AssemblyName, StringComparison.Ordinal)))
            {
                problems.Add($"unknown-project:{identity}:{entry.CanonicalProjectPath}");
            }
        }

        foreach (var finding in findings)
        {
            var identity = $"{finding.Assembly}|{finding.DeclaringType}|{finding.Member}";
            if (DynamicRuntimeAccessScanner.IsNeverAllowable(finding))
            {
                problems.Add($"unallowable-finding:{finding.Kind}:{identity}:{finding.Detail}");
                continue;
            }

            // Correction C1 (Blocker A): ZERO self-exemption — gjetjet mbi kod me
            // [GeneratedCode] NUK anashkalohen; ato kontrollohen kundër ledger-it
            // EKZAKT të provenance-ës në ValidateGeneratedResiduals.
            if (finding.Kind.StartsWith("generated-", StringComparison.Ordinal))
            {
                continue; // trajtohet nga blloku i ledger-it më poshtë, kurrë i heshtur
            }

            if (seen.Contains(identity))
            {
                consumed.Add(identity);
                continue;
            }

            problems.Add($"uncovered-dynamic-access:{finding.Kind}:{identity}:{finding.Detail}");
        }

        problems.AddRange(ValidateGeneratedResiduals(ledger, findings));

        foreach (var identity in seen.Where(candidate => !consumed.Contains(candidate))
            .Order(StringComparer.Ordinal))
        {
            problems.Add($"dead-entry:{identity}");
        }

        return problems;
    }

    // ═════════ Correction C1 — provenance ledger i mbetjeve generated ═════════

    private const string MediatorTool = "Mediator.SourceGenerator";
    private const string MediatorToolVersion = "3.0.0.0";
    private const string OpenApiTool =
        "Microsoft.AspNetCore.OpenApi.SourceGenerators, Version=10.0.10.0, Culture=neutral, "
        + "PublicKeyToken=adb9793829ddae60";
    private const string OpenApiToolVersion = "10.0.10.0";

    private const string OpenApiHelper =
        "Microsoft.AspNetCore.OpenApi.Generated.<OpenApiXmlCommentSupport_generated>"
        + GeneratedHashPlaceholder + "__DocumentationCommentIdHelper";
    private const string OpenApiTransformer =
        "Microsoft.AspNetCore.OpenApi.Generated.<OpenApiXmlCommentSupport_generated>"
        + GeneratedHashPlaceholder + "__XmlCommentOperationTransformer";

    private static GeneratedResidualEntry OpenApiEntry(string type, string member, string detail) => new(
        "Idata.Platform.Api", type, member, "generated-dynamic-unknown-member", detail,
        OpenApiTool, OpenApiToolVersion, "Microsoft.AspNetCore.OpenApi");

    /// <summary>
    /// Mbetjet EKZAKTE të generatorëve të pin-uar mbi HEAD-in aktual — të
    /// rillogaritura NGA ZERO me skanim real (jo numra të trashëguar). Kind-et e
    /// lejuara në ledger janë vetëm jo-ekzekutuese: `generated-dynamic-dangerous`
    /// dhe `generated-dynamic-service-location` s'pranohen KURRË këtu (validatori
    /// i refuzon si hyrje të pavlefshme dhe i bllokon si gjetje).
    /// </summary>
    internal static readonly GeneratedResidualEntry[] GeneratedResidualLedger =
    [
        new("Idata.Platform.Api", "Mediator.Internals.ContainerMetadata", ".ctor",
            "generated-dynamic-unknown-member",
            "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope",
            MediatorTool, MediatorToolVersion, "Mediator.SourceGenerator"),

        OpenApiEntry(OpenApiHelper, "CreateDocumentationId", "System.Reflection.MethodBase.GetGenericArguments"),
        OpenApiEntry(OpenApiHelper, "CreateDocumentationId", "System.Reflection.MethodBase.GetParameters"),
        OpenApiEntry(OpenApiHelper, "CreateDocumentationId", "System.Reflection.MethodBase.get_IsConstructor"),
        OpenApiEntry(OpenApiHelper, "CreateDocumentationId", "System.Reflection.MethodBase.get_IsGenericMethod"),
        OpenApiEntry(OpenApiHelper, "CreateDocumentationId", "System.Reflection.MethodBase.get_IsStatic"),
        OpenApiEntry(OpenApiHelper, "CreateDocumentationId", "System.Reflection.MethodInfo.get_ReturnType"),
        OpenApiEntry(OpenApiHelper, "CreateDocumentationId", "System.Reflection.MethodInfo.op_Equality"),
        OpenApiEntry(OpenApiHelper, "CreateDocumentationId", "System.Reflection.PropertyInfo.GetIndexParameters"),
        OpenApiEntry(OpenApiHelper, "CreateDocumentationId", "System.Reflection.PropertyInfo.op_Equality"),
        OpenApiEntry(OpenApiHelper, "GetTypeDocId", "System.Reflection.MethodBase.op_Inequality"),
        OpenApiEntry(OpenApiHelper, "GetTypeDocId", "System.Type.GetGenericArguments"),
        OpenApiEntry(OpenApiHelper, "GetTypeDocId", "System.Type.GetGenericTypeDefinition"),
        OpenApiEntry(OpenApiHelper, "GetTypeDocId", "System.Type.get_DeclaringMethod"),
        OpenApiEntry(OpenApiHelper, "GetTypeDocId", "System.Type.get_GenericParameterPosition"),
        OpenApiEntry(OpenApiHelper, "GetTypeDocId", "System.Type.get_IsGenericParameter"),
        OpenApiEntry(OpenApiHelper, "GetTypeDocId", "System.Type.get_IsGenericType"),
        OpenApiEntry(OpenApiHelper, "GetTypeDocId", "System.Type.get_IsGenericTypeDefinition"),
        OpenApiEntry(OpenApiTransformer, "TransformAsync", "System.Reflection.MethodBase.GetParameters"),
    ];

    /// <summary>Kind-e që s'lejohen KURRË në ledger dhe bllokojnë GJITHMONË si gjetje.</summary>
    internal static readonly IReadOnlySet<string> AlwaysBlockingGeneratedKinds =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "generated-dynamic-dangerous",
            "generated-dynamic-service-location",
        };

    /// <summary>
    /// Correction C3 (§3) — forma REALE e emërtimit file-local të OpenAPI
    /// source-generator-it, e verifikuar mekanikisht mbi metadata e HEAD-it:
    /// pas marker-it vjen **literali strukturor `F`** dhe pastaj digest-i
    /// volatil prej **64 karakteresh hex**. `F` NUK është "hex-i i parë" i
    /// hash-it: është marker i skemës file-local. Matja mbi HEAD: **1 formë/hash
    /// distinkt**, e ndarë nga **14 tipe** (jo 14 hash-e të pavarura).
    /// </summary>
    internal const char OpenApiFileLocalMarker = 'F';

    internal const int OpenApiDigestLength = 64;

    internal const string GeneratedHashPlaceholder = "F{INPUT_HASH_64}";

    private const string OpenApiGeneratedMarker = "<OpenApiXmlCommentSupport_generated>";

    /// <summary>
    /// Correction C1 (Shtesa A) + C3 (§3) — normalizim I KUFIZUAR, funksion PURE
    /// me pattern të ANKORUAR (zero `Replace` i përgjithshëm, zero regex global).
    /// Pranohet VETËM forma STRUKTURORE:
    /// `…&lt;OpenApiXmlCommentSupport_generated&gt;` + literal
    /// <see cref="OpenApiFileLocalMarker"/> (`F`) + SAKTËSISHT
    /// <see cref="OpenApiDigestLength"/> (64) karaktere hex + `__` + suffix
    /// jo-bosh. Zëvendësohet vetëm segmenti `F`+digest me
    /// <see cref="GeneratedHashPlaceholder"/>; çdo segment tjetër mbetet
    /// BYTE-IDENTIK. Refuzohen: marker-i që mungon ose dyfishohet · karakter
    /// prijës tjetër (p.sh. `A`) ose `f` i vogël · digest 63/65/66 ose jo-hex ·
    /// run i dytë 64-hex · separator `__` që mungon · suffix bosh. Emri kthehet
    /// i PAPREKUR dhe rrjedhimisht nuk përputhet me asnjë hyrje ledger-i
    /// (fail-closed).
    /// </summary>
    internal static bool TryNormalizeGeneratedTypeName(string typeName, out string normalized)
    {
        normalized = typeName;
        var marker = typeName.IndexOf(OpenApiGeneratedMarker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return false; // s'ka formën OpenAPI — emri nuk preket kurrë
        }

        if (typeName.IndexOf(OpenApiGeneratedMarker, marker + 1, StringComparison.Ordinal) >= 0)
        {
            return false; // dy marker-a = formë e paparashikuar → refuzim
        }

        var hashStart = marker + OpenApiGeneratedMarker.Length;
        if (hashStart >= typeName.Length || typeName[hashStart] != OpenApiFileLocalMarker)
        {
            return false; // marker-i strukturor `F` mungon (p.sh. `A…` ose `f…`)
        }

        var digestStart = hashStart + 1;
        var index = digestStart;
        while (index < typeName.Length && Uri.IsHexDigit(typeName[index]))
        {
            index++;
        }

        if (index - digestStart != OpenApiDigestLength)
        {
            return false; // digest 63/65/66/jo-hex = refuzim fail-closed
        }

        if (index + 2 > typeName.Length || typeName[index] != '_' || typeName[index + 1] != '_')
        {
            return false; // separator-i `__` mungon
        }

        var suffix = typeName[(index + 2)..];
        if (suffix.Length == 0)
        {
            return false; // suffix bosh
        }

        if (ContainsHexRun(suffix, OpenApiDigestLength))
        {
            return false; // më shumë se një digest-run = refuzim
        }

        normalized = string.Concat(typeName.AsSpan(0, hashStart), GeneratedHashPlaceholder, "__", suffix);
        return true;
    }

    private static bool ContainsHexRun(string value, int length)
    {
        var run = 0;
        foreach (var character in value)
        {
            run = Uri.IsHexDigit(character) ? run + 1 : 0;
            if (run >= length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Correction C1 (Blocker A) — pariteti DY-DREJTIMËSH i mbetjeve generated
    /// kundër ledger-it: çdo gjetje UNIKE konsumon saktësisht një hyrje; çdo
    /// hyrje konsumohet saktësisht një herë; kind-et gjithmonë-blocking as nuk
    /// pranohen në ledger as nuk mbulohen; kolizioni i normalizimit (dy tipe
    /// REALE → i njëjti identitet) = fail-closed.
    /// </summary>
    internal static IReadOnlyList<string> ValidateGeneratedResiduals(
        IReadOnlyList<GeneratedResidualEntry> ledger,
        IReadOnlyList<DynamicRuntimeAccessScanner.Finding> findings)
    {
        var problems = new List<string>();

        static string Key(string assembly, string type, string member, string kind, string detail, string tool, string version)
            => string.Join("|", assembly, type, member, kind, detail, tool, version);

        var entriesByKey = new Dictionary<string, GeneratedResidualEntry>(StringComparer.Ordinal);
        foreach (var entry in ledger)
        {
            if (AlwaysBlockingGeneratedKinds.Contains(entry.Kind))
            {
                problems.Add($"invalid-ledger-kind:{entry.Kind}:{entry.NormalizedDeclaringType}.{entry.Member}");
                continue;
            }

            foreach (var (field, value) in new (string, string)[]
            {
                ("AssemblyName", entry.AssemblyName), ("NormalizedDeclaringType", entry.NormalizedDeclaringType),
                ("Member", entry.Member), ("Kind", entry.Kind), ("Detail", entry.Detail),
                ("Tool", entry.Tool), ("ToolVersion", entry.ToolVersion), ("PackageId", entry.PackageId),
            })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    problems.Add($"empty-ledger-field:{entry.NormalizedDeclaringType}.{entry.Member}:{field}");
                }
                else if (value.Contains('*', StringComparison.Ordinal))
                {
                    problems.Add($"wildcard-ledger-field:{entry.NormalizedDeclaringType}.{entry.Member}:{field}");
                }
            }

            var key = Key(entry.AssemblyName, entry.NormalizedDeclaringType, entry.Member,
                entry.Kind, entry.Detail, entry.Tool, entry.ToolVersion);
            if (!entriesByKey.TryAdd(key, entry))
            {
                problems.Add($"duplicate-ledger-entry:{entry.NormalizedDeclaringType}.{entry.Member}:{entry.Detail}");
            }
        }

        // Gjetjet generated: unike sipas identitetit të plotë; normalizim + kolizion.
        var generated = findings
            .Where(finding => finding.Kind.StartsWith("generated-", StringComparison.Ordinal))
            .DistinctBy(finding => Key(finding.Assembly, finding.DeclaringType, finding.Member,
                finding.Kind, finding.Detail, finding.GeneratedTool, finding.GeneratedToolVersion))
            .ToArray();

        var rawByNormalized = new Dictionary<string, string>(StringComparer.Ordinal);
        var consumedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in generated)
        {
            if (AlwaysBlockingGeneratedKinds.Contains(finding.Kind))
            {
                problems.Add(
                    $"generated-blocking:{finding.Kind}:{finding.DeclaringType}.{finding.Member}:{finding.Detail}");
                continue;
            }

            var normalizedType = TryNormalizeGeneratedTypeName(finding.DeclaringType, out var candidate)
                ? candidate
                : finding.DeclaringType;

            if (normalizedType != finding.DeclaringType)
            {
                if (rawByNormalized.TryGetValue(normalizedType, out var existingRaw)
                    && existingRaw != finding.DeclaringType)
                {
                    problems.Add($"normalization-collision:{normalizedType}");
                    continue;
                }

                rawByNormalized[normalizedType] = finding.DeclaringType;
            }

            var key = Key(finding.Assembly, normalizedType, finding.Member,
                finding.Kind, finding.Detail, finding.GeneratedTool, finding.GeneratedToolVersion);
            if (!entriesByKey.ContainsKey(key))
            {
                problems.Add(
                    $"uncovered-generated-residual:{finding.Kind}:{finding.Assembly}|{normalizedType}|"
                    + $"{finding.Member}:{finding.Detail}:tool={finding.GeneratedTool}:version={finding.GeneratedToolVersion}");
                continue;
            }

            if (!consumedKeys.Add(key))
            {
                problems.Add($"double-consumed-ledger-entry:{normalizedType}.{finding.Member}:{finding.Detail}");
            }
        }

        foreach (var pair in entriesByKey.Where(pair => !consumedKeys.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            problems.Add(
                $"dead-generated-entry:{pair.Value.NormalizedDeclaringType}.{pair.Value.Member}:{pair.Value.Detail}");
        }

        return problems;
    }

    /// <summary>
    /// Paketat e PIN-uara nga lockfile-t REALË të repo-s (id → versione të
    /// zgjidhura) — burimi mekanik me të cilin meta-testi lidh tool/version-in e
    /// çdo hyrjeje ledger-i. Vetëm LEXIM; zero prekje e lockfile-ve.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlySet<string>> PinnedPackageVersions()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var pinned = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var lockfile in Directory.EnumerateFiles(
            Path.Combine(repoRoot, "src"), "packages.lock.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(lockfile));
            if (!document.RootElement.TryGetProperty("dependencies", out var frameworks))
            {
                throw new InvalidOperationException($"Lockfile pa 'dependencies': {lockfile}");
            }

            foreach (var framework in frameworks.EnumerateObject())
            {
                foreach (var package in framework.Value.EnumerateObject())
                {
                    if (package.Value.TryGetProperty("resolved", out var resolved)
                        && resolved.GetString() is { Length: > 0 } version)
                    {
                        (pinned.TryGetValue(package.Name, out var set)
                            ? set
                            : pinned[package.Name] = new HashSet<string>(StringComparer.Ordinal)).Add(version);
                    }
                }
            }
        }

        return pinned.ToDictionary(
            pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value, StringComparer.OrdinalIgnoreCase);
    }
}
