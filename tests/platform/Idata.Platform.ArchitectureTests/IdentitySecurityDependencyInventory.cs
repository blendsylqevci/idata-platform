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
    /// Familje provider-ësh/protokolli të ndaluara (ADR-028 B.1) — skanohen
    /// mbi çdo paketë të çdo lockfile-i, pavarësisht closure-it.
    /// </summary>
    internal static readonly string[] ForbiddenFamilies =
        ["IdentityServer", "Duende", "Auth0", "Okta",
         "OpenIddict.Server", "OpenIddict.Validation", "OpenIddict.Client", "OpenIddict.AspNetCore",
         "Microsoft.AspNetCore.Identity.UI"];

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
        => IdentitySpecificFamilies.Any(family => packageName.StartsWith(family, StringComparison.OrdinalIgnoreCase));

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
                // Familjet e ndaluara: mbi ÇDO paketë, edhe jashtë closure.
                foreach (var family in ForbiddenFamilies)
                {
                    if (dependency.Name.StartsWith(family, StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add($"{canonicalProjectPath}: familje provider-i e ndaluar '{dependency.Name}'");
                    }
                }

                if (!closure.Contains(dependency.Name))
                {
                    continue; // Jashtë closure + jashtë familjeve = i palidhur, zero false positive.
                }

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
                    // Deny-by-default BRENDA closure: emër i panjohur = gjetje.
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
            }
        }

        return findings;
    }
}
