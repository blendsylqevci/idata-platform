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

            // Kodi i GJENERUAR raportohet veçmas si kufi i deklaruar — nuk hyn në
            // manifestin e kodit tonë dhe nuk e bllokon atë.
            if (finding.Kind.StartsWith("generated-", StringComparison.Ordinal))
            {
                continue;
            }

            if (seen.Contains(identity))
            {
                consumed.Add(identity);
                continue;
            }

            problems.Add($"uncovered-dynamic-access:{finding.Kind}:{identity}:{finding.Detail}");
        }

        foreach (var identity in seen.Where(candidate => !consumed.Contains(candidate))
            .Order(StringComparer.Ordinal))
        {
            problems.Add($"dead-entry:{identity}");
        }

        return problems;
    }
}
