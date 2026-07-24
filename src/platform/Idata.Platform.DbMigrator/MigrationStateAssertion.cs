namespace Idata.Platform.DbMigrator;

/// <summary>
/// Krahasim PURE dhe determinist i gjendjes së migrimeve — provë
/// machine-checkable (jo grep mbi tekst human-readable). Çdo mismatch
/// kthehet si mesazh i strukturuar "fusha expected=X actual=Y" pa asnjë
/// detaj lidhjeje.
/// </summary>
public static class MigrationStateAssertion
{
    public static IReadOnlyList<string> Check(
        int expectedApplied,
        int expectedPending,
        bool expectedSentinelPresent,
        IReadOnlyCollection<string> appliedIds,
        IReadOnlyCollection<string> assemblyIds,
        bool sentinelPresent)
    {
        var errors = new List<string>();
        var applied = appliedIds.ToHashSet(StringComparer.Ordinal);
        var assembly = assemblyIds.ToHashSet(StringComparer.Ordinal);
        var pending = assembly.Except(applied, StringComparer.Ordinal).ToArray();

        if (applied.Count != expectedApplied)
        {
            errors.Add($"applied-count expected={expectedApplied} actual={applied.Count}");
        }

        if (pending.Length != expectedPending)
        {
            errors.Add($"pending-count expected={expectedPending} actual={pending.Length}");
        }

        // Konsistencë me setin e assembly-t: çdo migrim i aplikuar duhet të
        // njihet nga assembly-i (asnjë ID e panjohur/fantazmë në histori).
        foreach (var unknown in applied.Except(assembly, StringComparer.Ordinal).Order())
        {
            errors.Add($"applied-id-unknown-to-assembly actual={unknown}");
        }

        if (sentinelPresent != expectedSentinelPresent)
        {
            errors.Add($"sentinel expected={(expectedSentinelPresent ? "present" : "absent")} actual={(sentinelPresent ? "present" : "absent")}");
        }

        return errors;
    }
}
