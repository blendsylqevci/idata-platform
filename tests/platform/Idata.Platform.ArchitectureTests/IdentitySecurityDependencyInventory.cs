using System.Text.Json;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Inventari i varësive identity/security (correction R3 i PR #28, §4) —
/// model CLOSURE i kufizuar deny-by-default, JO prefix-surveillance:
/// universi i mbrojtur = mbyllja tranzitive nga dy rrënjët e aprovuara të
/// store-it (ADR-028 B.1), e ndërtuar cycle-safe nga metadata REALE e
/// packages.lock.json (edges "dependencies" të çdo pakete). Rregullat:
///  - çdo paketë e arritshme nga rrënjët duhet të jetë në
///    <see cref="ClosureManifest"/> — emër i panjohur në closure = GJETJE;
///  - version ≠ manifest = GJETJE; lloj i palejuar = GJETJE;
///  - `Direct` për rrënjët VETËM te owner-at (path kanonik §3) = ndryshe GJETJE;
///  - familjet e ndaluara të provider-ëve skanohen mbi TË GJITHA paketat
///    (edhe jashtë closure) — provider komercial/protokoll i paaprovuar = GJETJE;
///  - metadata e palexueshme = GJETJE fail-closed, kurrë silent pass;
///  - paketat e palidhura jashtë closure NUK prodhojnë false positive.
/// Manifest-i statik mbulohet nga meta-testi i barazisë me closure-in REAL
/// (hyrje e re/e munguar = RED) — inventari s'mund të vdesë në heshtje.
/// </summary>
internal static class IdentitySecurityDependencyInventory
{
    /// <summary>Rrënjët e aprovuara të store-it (ADR-028 B.1) — të vetmet Direct te owner-at.</summary>
    internal static readonly IReadOnlyDictionary<string, string> ApprovedRoots =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Microsoft.AspNetCore.Identity.EntityFrameworkCore"] = "10.0.10",
            ["OpenIddict.EntityFrameworkCore"] = "7.6.0",
        };

    /// <summary>
    /// Manifest-i i plotë i closure-it (31 paketa — i derivuar nga lockfiles
    /// reale; barazia me closure-in real imponohet nga meta-testi §4).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> ClosureManifest =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Microsoft.AspNetCore.Cryptography.Internal"] = "10.0.10",
            ["Microsoft.AspNetCore.Cryptography.KeyDerivation"] = "10.0.10",
            ["Microsoft.AspNetCore.Identity.EntityFrameworkCore"] = "10.0.10",
            ["Microsoft.Bcl.Cryptography"] = "10.0.2",
            ["Microsoft.EntityFrameworkCore"] = "10.0.10",
            ["Microsoft.EntityFrameworkCore.Abstractions"] = "10.0.10",
            ["Microsoft.EntityFrameworkCore.Analyzers"] = "10.0.10",
            ["Microsoft.EntityFrameworkCore.Relational"] = "10.0.10",
            ["Microsoft.Extensions.Caching.Abstractions"] = "10.0.10",
            ["Microsoft.Extensions.Caching.Memory"] = "10.0.10",
            ["Microsoft.Extensions.Configuration"] = "10.0.10",
            ["Microsoft.Extensions.Configuration.Abstractions"] = "10.0.10",
            ["Microsoft.Extensions.Configuration.Binder"] = "10.0.10",
            ["Microsoft.Extensions.DependencyInjection"] = "10.0.10",
            ["Microsoft.Extensions.DependencyInjection.Abstractions"] = "10.0.10",
            ["Microsoft.Extensions.Diagnostics"] = "10.0.10",
            ["Microsoft.Extensions.Diagnostics.Abstractions"] = "10.0.10",
            ["Microsoft.Extensions.Identity.Core"] = "10.0.10",
            ["Microsoft.Extensions.Identity.Stores"] = "10.0.10",
            ["Microsoft.Extensions.Logging"] = "10.0.10",
            ["Microsoft.Extensions.Logging.Abstractions"] = "10.0.10",
            ["Microsoft.Extensions.Options"] = "10.0.10",
            ["Microsoft.Extensions.Options.ConfigurationExtensions"] = "10.0.10",
            ["Microsoft.Extensions.Primitives"] = "10.0.10",
            ["Microsoft.IdentityModel.Abstractions"] = "8.19.2",
            ["Microsoft.IdentityModel.Logging"] = "8.19.2",
            ["Microsoft.IdentityModel.Tokens"] = "8.19.2",
            ["OpenIddict.Abstractions"] = "7.6.0",
            ["OpenIddict.Core"] = "7.6.0",
            ["OpenIddict.EntityFrameworkCore"] = "7.6.0",
            ["OpenIddict.EntityFrameworkCore.Models"] = "7.6.0",
        };

    /// <summary>
    /// Përputhje familjeje me KUFI PIKE (§5-R4): emër EKZAKT ose
    /// `familje.`-prefiks — JO StartsWith i lirshëm (emra si
    /// "Microsoft.AspNetCore.CryptographyFake" NUK përputhen).
    /// </summary>
    internal static bool MatchesFamily(string packageName, string family)
        => packageName.Equals(family, StringComparison.OrdinalIgnoreCase)
           || packageName.StartsWith(family + ".", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Familje provider-ësh/protokolli të NDALUARA (ADR-028 B.1) — hapi 1 i
    /// rendit normativ: gjetje GJITHMONË, fitojnë mbi manifest/closure/çdo
    /// kategori tjetër; skanohen mbi çdo paketë të çdo lockfile-i.
    /// </summary>
    internal static readonly string[] ForbiddenFamilies =
        ["IdentityServer", "IdentityServer4", "Duende", "Auth0", "Okta",
         "OpenIddict.Server", "OpenIddict.Validation", "OpenIddict.Client", "OpenIddict.AspNetCore",
         "Microsoft.AspNetCore.Identity.UI"];

    /// <summary>
    /// Familje security/identity/auth që kërkojnë REVIEW (§5-R4) — hapi 3 i
    /// rendit normativ: çdo paketë e këtyre familjeve JASHTË closure-it të
    /// aprovuar = gjetje. Asnjë paketë s'kapërcehet para klasifikimit.
    /// </summary>
    internal static readonly string[] ReviewRequiredFamilies =
        ["OpenIddict", "Microsoft.AspNetCore.Identity", "Microsoft.Extensions.Identity",
         "Microsoft.AspNetCore.Authentication", "Microsoft.AspNetCore.DataProtection",
         "Microsoft.AspNetCore.Cryptography", "Microsoft.IdentityModel", "System.IdentityModel",
         "Microsoft.Bcl.Cryptography"];

    private static readonly string[] AllowedTypes = ["Direct", "Transitive", "CentralTransitive"];

    /// <summary>
    /// Familjet identity/security-SPECIFIKE brenda closure — për to `Direct`
    /// lejohet VETËM për rrënjët te owner-at kanonikë. Anëtarët e tjerë të
    /// closure (EF Core/Extensions e përbashkëta) mund të jenë Direct për
    /// arsye jashtë identity-t (qeverisen nga konventat e tjera të repo-s);
    /// emri/versioni/lloji u imponohen njëlloj. Klasifikimi mbulohet nga
    /// meta-testi i ndarjes së plotë (§4-R3).
    /// </summary>
    internal static readonly string[] IdentitySpecificFamilies =
        ["Microsoft.AspNetCore.Identity", "Microsoft.Extensions.Identity", "OpenIddict",
         "Microsoft.IdentityModel", "Microsoft.AspNetCore.Cryptography", "Microsoft.Bcl.Cryptography"];

    internal static bool IsIdentitySpecific(string packageName)
        => IdentitySpecificFamilies.Any(family => MatchesFamily(packageName, family));

    /// <summary>
    /// §2-R5 — AUTORITETI EKSPLICIT i package roots DIREKTE, i indeksuar me
    /// path kanonik repo-relative të csproj-it: emër → version i pritur.
    /// Lista është E SHKRUAR DORAZI (e versionuar/review-ueshme në git) dhe
    /// NUK derivohet nga graph-i aktual — përndryshe kontrolli do të ishte
    /// tautologjik dhe do të aprovonte çdo paketë të re. Garancia: asnjë
    /// package root i ri pa update të review-uar të këtij autoriteti — JO
    /// pretendim se kuptohet semantikisht çdo emër NuGet.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DirectPackageAuthority =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["src/platform/Idata.Platform.Api/Idata.Platform.Api.csproj"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Mediator.SourceGenerator"] = "3.0.2",
                ["Microsoft.AspNetCore.OpenApi"] = "10.0.10",
                ["Microsoft.Extensions.ApiDescription.Server"] = "10.0.10",
                ["Microsoft.OpenApi"] = "2.11.0",
                ["Scalar.AspNetCore"] = "2.16.16",
            },
            ["src/platform/Idata.Platform.DbMigrator/Idata.Platform.DbMigrator.csproj"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Microsoft.EntityFrameworkCore.Design"] = "10.0.10",
            },
            ["src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Microsoft.AspNetCore.Identity.EntityFrameworkCore"] = "10.0.10",
                ["Microsoft.EntityFrameworkCore"] = "10.0.10",
                ["Microsoft.EntityFrameworkCore.Relational"] = "10.0.10",
                ["Npgsql"] = "10.0.3",
                ["Npgsql.EntityFrameworkCore.PostgreSQL"] = "10.0.3",
                ["OpenIddict.EntityFrameworkCore"] = "7.6.0",
            },
            ["src/platform/Modules/AccessControl/Application/Idata.Platform.Modules.AccessControl.Application.csproj"]
                = new Dictionary<string, string>(StringComparer.Ordinal),
            ["src/platform/Modules/AccessControl/Domain/Idata.Platform.Modules.AccessControl.Domain.csproj"]
                = new Dictionary<string, string>(StringComparer.Ordinal),
            ["src/platform/Modules/AccessControl/Infrastructure/Idata.Platform.Modules.AccessControl.Infrastructure.csproj"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Microsoft.EntityFrameworkCore"] = "10.0.10",
                ["Microsoft.EntityFrameworkCore.Relational"] = "10.0.10",
                ["Npgsql"] = "10.0.3",
            },
            ["src/platform/Modules/AccessControl/Presentation/Idata.Platform.Modules.AccessControl.Presentation.csproj"]
                = new Dictionary<string, string>(StringComparer.Ordinal),
            ["src/platform/Modules/Identity/Infrastructure/Idata.Platform.Modules.Identity.Infrastructure.csproj"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Microsoft.AspNetCore.Identity.EntityFrameworkCore"] = "10.0.10",
                ["OpenIddict.EntityFrameworkCore"] = "7.6.0",
            },
            ["src/platform/Modules/PlatformStatus/Application/Idata.Platform.Modules.PlatformStatus.Application.csproj"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Mediator.Abstractions"] = "3.0.2",
            },
            ["src/platform/Modules/PlatformStatus/Domain/Idata.Platform.Modules.PlatformStatus.Domain.csproj"]
                = new Dictionary<string, string>(StringComparer.Ordinal),
            ["src/platform/Modules/PlatformStatus/Infrastructure/Idata.Platform.Modules.PlatformStatus.Infrastructure.csproj"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Microsoft.EntityFrameworkCore"] = "10.0.10",
                ["Microsoft.EntityFrameworkCore.Relational"] = "10.0.10",
                ["Npgsql"] = "10.0.3",
                ["Npgsql.EntityFrameworkCore.PostgreSQL"] = "10.0.3",
            },
            ["src/platform/Modules/PlatformStatus/Presentation/Idata.Platform.Modules.PlatformStatus.Presentation.csproj"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Mediator.Abstractions"] = "3.0.2",
            },
            ["tests/platform/Idata.Platform.ArchitectureTests/Idata.Platform.ArchitectureTests.csproj"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Microsoft.NET.Test.Sdk"] = "18.8.1",
                ["xunit.runner.visualstudio"] = "3.1.5",
                ["xunit.v3"] = "3.2.2",
            },
            ["tests/platform/Idata.Platform.IntegrationTests/Idata.Platform.IntegrationTests.csproj"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Microsoft.AspNetCore.Mvc.Testing"] = "10.0.10",
                ["Microsoft.NET.Test.Sdk"] = "18.8.1",
                ["xunit.runner.visualstudio"] = "3.1.5",
                ["xunit.v3"] = "3.2.2",
            },
            ["tests/platform/Idata.Platform.UnitTests/Idata.Platform.UnitTests.csproj"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Microsoft.NET.Test.Sdk"] = "18.8.1",
                ["xunit.runner.visualstudio"] = "3.1.5",
                ["xunit.v3"] = "3.2.2",
            },
        };

    /// <summary>
    /// §2B-R5: përjashtime EKSPLICITE build/analyzer-only me arsyetim — sot
    /// BOSH (asnjë paketë nuk e kërkon; të gjitha klasifikohen si Direct të
    /// autorizuara, të arritshme nga rrënjët, ose referenca projektesh).
    /// Çdo hyrje e ardhshme kërkon arsyetim + review.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> BuildOnlyExemptions =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// §4-R6 — GRAPH-i AUTORITATIV i projekteve për auditimin e një owner-i:
    /// harta AssemblyName-i-VLERËSUAR → path kanonik (nga katalogu MSBuild —
    /// JO basename), closure-i REAL i ProjectReference-ve të owner-it (i
    /// derivuar nga MSBuild) dhe autoriteti direkt për çdo path. Seti që
    /// auditohet nuk përdoret KURRË si burim autoriteti për vetveten.
    /// </summary>
    internal sealed record ProjectGraphAuthority(
        IReadOnlyDictionary<string, string> AssemblyNameToCanonicalPath,
        IReadOnlySet<string> OwnerClosureCanonicalPaths,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AuthorityByCanonicalPath);

    internal static readonly ProjectGraphAuthority EmptyProjectGraph = new(
        new Dictionary<string, string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));

    internal static List<string> AuditPackagesTotal(
        string canonicalProjectPath, JsonDocument lockfile, IReadOnlyDictionary<string, string> directAuthority)
        => AuditPackagesTotal(canonicalProjectPath, lockfile, directAuthority, EmptyProjectGraph);

    /// <summary>
    /// §2B-R5/§4-R6 — KLASIFIKIMI TOTAL fail-closed i një lockfile-i: çdo
    /// hyrje duhet të jetë saktësisht Direct-i-autorizuar (resolved +
    /// contentHash + version sipas autoritetit) · node projekti REAL i
    /// ARRITSHËM në ProjectReference closure-in e owner-it (identiteti =
    /// AssemblyName i vlerësuar → path kanonik; Ghost/known-por-i-paarritshëm
    /// = GJETJE; dependencies e node-it NUK krijojnë authority — zgjerimi
    /// bëhet VETËM me autoritetin e review-uar të atij projekti) ·
    /// Transitive/CentralTransitive (resolved + contentHash) i ARRITSHËM ·
    /// përjashtim eksplicit. Dead-authority kontrollohet EDHE kur
    /// dependencies janë bosh/mungojnë. Metadata/type/edge malformed =
    /// GJETJE deterministe, kurrë crash i pakontrolluar.
    /// </summary>
    internal static List<string> AuditPackagesTotal(
        string canonicalProjectPath, JsonDocument lockfile,
        IReadOnlyDictionary<string, string> directAuthority,
        ProjectGraphAuthority projectGraph)
    {
        var findings = new List<string>();
        var seenDirect = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!lockfile.RootElement.TryGetProperty("dependencies", out var frameworks)
            || frameworks.ValueKind != JsonValueKind.Object)
        {
            findings.Add($"{canonicalProjectPath}: seksioni 'dependencies' mungon ose s'është objekt");
        }
        else
        {
            foreach (var framework in frameworks.EnumerateObject())
            {
                if (framework.Value.ValueKind != JsonValueKind.Object)
                {
                    findings.Add($"{canonicalProjectPath}: framework '{framework.Name}' s'është objekt");
                    continue;
                }

                AuditFramework(canonicalProjectPath, framework.Value, directAuthority, projectGraph, findings, seenDirect);
            }
        }

        // §4-R6: dead-authority EDHE kur dependencies/frameworks janë bosh.
        foreach (var authorized in directAuthority.Keys)
        {
            if (!seenDirect.Contains(authorized))
            {
                findings.Add($"{canonicalProjectPath}: hyrje autoriteti E VDEKUR '{authorized}' (s'është më Direct real)");
            }
        }

        return findings;
    }

    /// <summary>Semantika NuGet: IDs krahasohen case-insensitive; kolizioni = GJETJE.</summary>
    private static Dictionary<string, TValue> NuGetCaseInsensitive<TValue>(
        string canonicalProjectPath, string what, IEnumerable<KeyValuePair<string, TValue>> source, List<string> findings)
    {
        var map = new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
        {
            if (!map.TryAdd(key, value))
            {
                findings.Add($"{canonicalProjectPath}: KOLIZION case-insensitive në {what}: '{key}'");
            }
        }

        return map;
    }

    private static void AuditFramework(
        string canonicalProjectPath, JsonElement framework,
        IReadOnlyDictionary<string, string> directAuthorityExact,
        ProjectGraphAuthority projectGraph,
        List<string> findings, HashSet<string> seenDirect)
    {
        var entries = NuGetCaseInsensitive(canonicalProjectPath, "lockfile nodes",
            framework.EnumerateObject().Select(d => new KeyValuePair<string, JsonElement>(d.Name, d.Value)), findings);
        var directAuthority = NuGetCaseInsensitive(canonicalProjectPath, "autoritetin direkt", directAuthorityExact, findings);
        var projectNameToPath = NuGetCaseInsensitive(canonicalProjectPath, "hartën e projekteve",
            projectGraph.AssemblyNameToCanonicalPath, findings);

        var expansionRoots = new List<string>();

        static bool RequireString(JsonElement node, string property, out string value)
        {
            value = string.Empty;
            if (!node.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String
                || element.GetString() is not { Length: > 0 } text)
            {
                return false;
            }

            value = text;
            return true;
        }

        // Faza 1: validimi type-aware i çdo node-i.
        foreach (var (name, node) in entries)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                findings.Add($"{canonicalProjectPath}: node '{name}' s'është objekt");
                continue;
            }

            if (!RequireString(node, "type", out var entryType))
            {
                findings.Add($"{canonicalProjectPath}: metadata e palexueshme — '{name}' pa 'type'");
                continue;
            }

            if (node.TryGetProperty("dependencies", out var edgeSet))
            {
                if (edgeSet.ValueKind != JsonValueKind.Object)
                {
                    findings.Add($"{canonicalProjectPath}: 'dependencies' e '{name}' s'është objekt");
                }
                else
                {
                    foreach (var edge in edgeSet.EnumerateObject())
                    {
                        if (edge.Value.ValueKind != JsonValueKind.String
                            || string.IsNullOrEmpty(edge.Value.GetString()))
                        {
                            findings.Add($"{canonicalProjectPath}: edge malformed '{name}' → '{edge.Name}' (version bosh/jo-string)");
                        }
                    }
                }
            }

            switch (entryType)
            {
                case "Direct":
                    seenDirect.Add(name);
                    if (!directAuthority.TryGetValue(name, out var expectedVersion))
                    {
                        findings.Add($"{canonicalProjectPath}: package root DIREKT i PAAUTORIZUAR '{name}'");
                        continue;
                    }

                    if (!RequireString(node, "resolved", out var resolved))
                    {
                        findings.Add($"{canonicalProjectPath}: metadata e palexueshme — Direct '{name}' pa 'resolved'");
                        continue;
                    }

                    if (!RequireString(node, "contentHash", out _))
                    {
                        findings.Add($"{canonicalProjectPath}: metadata e palexueshme — Direct '{name}' pa 'contentHash'");
                        continue;
                    }

                    if (resolved != expectedVersion)
                    {
                        findings.Add($"{canonicalProjectPath}: version i paautorizuar {name}={resolved} (autoriteti: {expectedVersion})");
                    }

                    expansionRoots.Add(name);
                    break;

                case "Project":
                    // §4-R6: node projekti = identitet REAL + arritshmëri REALE.
                    if (!projectNameToPath.TryGetValue(name, out var projectPath))
                    {
                        findings.Add($"{canonicalProjectPath}: node projekti FANTAZMË '{name}' (s'mapon në asnjë projekt real)");
                        continue;
                    }

                    if (!projectGraph.OwnerClosureCanonicalPaths.Contains(projectPath))
                    {
                        findings.Add($"{canonicalProjectPath}: projekt i PAARRITSHËM nga ProjectReference graph-i i owner-it: '{name}' ({projectPath})");
                        continue;
                    }

                    expansionRoots.Add(name);
                    break;

                case "Transitive" or "CentralTransitive":
                    if (!RequireString(node, "resolved", out _))
                    {
                        findings.Add($"{canonicalProjectPath}: metadata e palexueshme — {entryType} '{name}' pa 'resolved'");
                    }

                    if (!RequireString(node, "contentHash", out _))
                    {
                        findings.Add($"{canonicalProjectPath}: metadata e palexueshme — {entryType} '{name}' pa 'contentHash'");
                    }

                    break;

                default:
                    findings.Add($"{canonicalProjectPath}: lloj i palejuar '{entryType}' për '{name}'");
                    break;
            }
        }

        // Faza 2: arritshmëria. Dependencies e node-eve Project NUK krijojnë
        // authority — për to zgjerohet VETËM autoriteti i review-uar i
        // projektit përkatës; edges e paketave ndiqen normalisht.
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<(string Name, bool FromAuthority)>(
            expansionRoots.Select(root => (root, false)));
        while (stack.Count > 0)
        {
            var (name, fromAuthority) = stack.Pop();
            if (!reachable.Add(name))
            {
                continue;
            }

            var isProjectNode = entries.TryGetValue(name, out var currentNode)
                && currentNode.TryGetProperty("type", out var currentType)
                && currentType.ValueKind == JsonValueKind.String
                && currentType.GetString() == "Project";
            var isKnownProject = projectNameToPath.TryGetValue(name, out var mappedPath);

            if (isProjectNode || (!entries.ContainsKey(name) && isKnownProject))
            {
                if (!isKnownProject)
                {
                    continue; // fantazma u raportua në fazën 1
                }

                if (!projectGraph.OwnerClosureCanonicalPaths.Contains(mappedPath!))
                {
                    if (!entries.ContainsKey(name))
                    {
                        findings.Add($"{canonicalProjectPath}: projekt i PAARRITSHËM nga ProjectReference graph-i i owner-it: '{name}' ({mappedPath})");
                    }

                    continue;
                }

                if (projectGraph.AuthorityByCanonicalPath.TryGetValue(mappedPath!, out var projectAuthority))
                {
                    foreach (var authorizedRoot in projectAuthority.Keys)
                    {
                        stack.Push((authorizedRoot, true));
                    }
                }

                if (isProjectNode && currentNode.TryGetProperty("dependencies", out var projectEdges)
                    && projectEdges.ValueKind == JsonValueKind.Object)
                {
                    foreach (var edge in projectEdges.EnumerateObject())
                    {
                        if (projectNameToPath.ContainsKey(edge.Name))
                        {
                            stack.Push((edge.Name, false));
                        }
                    }
                }

                continue;
            }

            if (!entries.TryGetValue(name, out var node))
            {
                // Kandidat autoriteti i një projekti tranzitiv MUND të mungojë
                // te konsumatori (PrivateAssets/asset flow) — no-op; vetëm EDGE
                // real i munguar = GJETJE.
                if (!fromAuthority)
                {
                    findings.Add($"{canonicalProjectPath}: edge drejt node-i të MUNGUAR '{name}'");
                }

                continue;
            }

            if (node.TryGetProperty("dependencies", out var edges) && edges.ValueKind == JsonValueKind.Object)
            {
                foreach (var edge in edges.EnumerateObject())
                {
                    stack.Push((edge.Name, false));
                }
            }
        }

        // Faza 3: totaliteti — asnjë hyrje paketë pa kategori.
        foreach (var (name, node) in entries)
        {
            if (node.ValueKind == JsonValueKind.Object
                && node.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                && typeElement.GetString() is "Transitive" or "CentralTransitive"
                && !reachable.Contains(name)
                && !BuildOnlyExemptions.ContainsKey(name))
            {
                findings.Add($"{canonicalProjectPath}: paketë E PAARRITSHME nga rrënjët e autorizuara '{name}'");
            }
        }
    }

    /// <summary>
    /// Closure-i REAL i një lockfile-i: BFS cycle-safe nga rrënjët e
    /// aprovuara mbi edges "dependencies". Edge drejt emri të munguar në
    /// seksionin e framework-ut raportohet si metadata e palexueshme.
    /// </summary>
    internal static (HashSet<string> Closure, List<string> Findings) ComputeClosure(
        string project, JsonDocument lockfile)
    {
        var closure = new HashSet<string>(StringComparer.Ordinal);
        var findings = new List<string>();
        foreach (var framework in lockfile.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            var graph = framework.Value.EnumerateObject()
                .ToDictionary(dependency => dependency.Name, dependency => dependency.Value, StringComparer.Ordinal);
            var stack = new Stack<string>(ApprovedRoots.Keys.Where(graph.ContainsKey));
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (stack.Count > 0)
            {
                var name = stack.Pop();
                if (!visited.Add(name))
                {
                    continue;
                }

                if (!graph.TryGetValue(name, out var node))
                {
                    // Fail-closed: edge drejt pakete që mungon nga lockfile.
                    findings.Add($"{project}: metadata e palexueshme — '{name}' i arritshëm nga rrënjët por mungon në lockfile");
                    continue;
                }

                closure.Add(name);
                if (node.TryGetProperty("dependencies", out var edges))
                {
                    foreach (var edge in edges.EnumerateObject())
                    {
                        stack.Push(edge.Name);
                    }
                }
            }
        }

        return (closure, findings);
    }

    /// <summary>
    /// Auditi FAIL-CLOSED i një lockfile-i kundrejt closure-it + familjeve
    /// të ndaluara. Owner-i njihet VETËM nga path-i kanonik i csproj (§3).
    /// </summary>
    internal static List<string> Audit(string canonicalProjectPath, JsonDocument lockfile)
    {
        var isStoreOwner = PlatformProjectCatalog.StoreOwnerCanonicalPaths
            .Contains(canonicalProjectPath, StringComparer.Ordinal);
        var (closure, findings) = ComputeClosure(canonicalProjectPath, lockfile);

        foreach (var framework in lockfile.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var dependency in framework.Value.EnumerateObject())
            {
                // RENDI NORMATIV §5-R4 — çdo paketë KLASIFIKOHET para çdo skip-i.

                // 1. Familjet e NDALUARA: gjetje GJITHMONË — fitojnë mbi
                //    manifest, closure dhe çdo kategori tjetër.
                if (ForbiddenFamilies.Any(family => MatchesFamily(dependency.Name, family)))
                {
                    findings.Add($"{canonicalProjectPath}: familje provider-i e ndaluar '{dependency.Name}'");
                    continue;
                }

                // 2. Brenda closure-it të aprovuar: valido manifest/version/
                //    lloj/owner (deny-by-default brenda closure).
                if (closure.Contains(dependency.Name))
                {
                    if (!dependency.Value.TryGetProperty("type", out var typeElement)
                        || typeElement.GetString() is not { } dependencyType
                        || !dependency.Value.TryGetProperty("resolved", out var resolvedElement)
                        || resolvedElement.GetString() is not { } resolved)
                    {
                        findings.Add($"{canonicalProjectPath}: metadata e palexueshme për '{dependency.Name}' (type/resolved mungon)");
                        continue;
                    }

                    if (!ClosureManifest.TryGetValue(dependency.Name, out var expectedVersion))
                    {
                        findings.Add($"{canonicalProjectPath}: paketë e PA-inventarizuar në closure '{dependency.Name}'");
                        continue;
                    }

                    if (resolved != expectedVersion)
                    {
                        findings.Add($"{canonicalProjectPath}: version i panjohur {dependency.Name}={resolved} (pritej {expectedVersion})");
                    }

                    if (!AllowedTypes.Contains(dependencyType))
                    {
                        findings.Add($"{canonicalProjectPath}: lloj i palejuar '{dependencyType}' për {dependency.Name}");
                    }
                    else if (dependencyType == "Direct"
                        && IsIdentitySpecific(dependency.Name)
                        && (!isStoreOwner || !ApprovedRoots.ContainsKey(dependency.Name)))
                    {
                        findings.Add($"{canonicalProjectPath}: Direct i PAAUTORIZUAR për {dependency.Name}");
                    }

                    continue;
                }

                // 3. Familje security/identity/auth REVIEW-REQUIRED jashtë
                //    closure-it të aprovuar = gjetje.
                if (ReviewRequiredFamilies.Any(family => MatchesFamily(dependency.Name, family)))
                {
                    findings.Add($"{canonicalProjectPath}: paketë security/identity JASHTË aprovimit '{dependency.Name}'");
                    continue;
                }

                // 4. Realisht e palidhur — skip VETËM PAS klasifikimit.
            }
        }

        return findings;
    }
}
