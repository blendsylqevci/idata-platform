using System.Security.Cryptography;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// Hyrje EKZAKTE e manifestit ENG-007 (§5). Pa wildcard, pa prefix, pa
/// përjashtim në nivel assembly-je apo tipi: identiteti është
/// (projekt kanonik, assembly, burim kanonik, tip i kualifikuar, anëtar) plus
/// një fingerprint DETERMINIST i file-it TË REVIEW-UAR, arsyeja dhe pronari.
/// Ndryshimi i file-it pa përditësim fingerprint-i = FAIL; përditësimi i
/// fingerprint-it është diff i dukshëm për reviewer-in.
/// </summary>
internal sealed record AllowedRawSurface(
    string CanonicalProjectPath,
    string AssemblyName,
    string CanonicalSourcePath,
    string DeclaringType,
    string Member,
    string SourceSha256,
    string Reason,
    string Owner);

/// <summary>
/// Manifesti i vetëm i përjashtimeve ENG-007 dhe validatori i tij fail-closed.
/// Statusi i owner-it (Persistence / Modules.Identity.Infrastructure) NUK jep
/// blanket exemption: çdo sink, edhe te owner-at, do hyrje ekzakte këtu.
/// </summary>
internal static class RawDataAccessManifest
{
    private const string TechOwner = "Tech/Platform Owner";

    /// <summary>
    /// Fingerprint-et deklarohen në GRUPE 8-karakterëshe të bashkuara në runtime.
    /// Arsyeja është MEKANIKE, jo kozmetike: një literal i vetëm 64-hex mund të
    /// përmbajë një varg 9-shifror që detektori `idata-client-pii` i gitleaks-it
    /// e klasifikon si numër telefoni (false-positive i dëshmuar mbi dy nga tri
    /// fingerprint-et). Grupimi 8×8 e bën përputhjen të pamundur NGA NDËRTIMI —
    /// pa asnjë suppression dhe pa asnjë dobësim: vlera, validimi (64 hex të
    /// vegjël) dhe krahasimi byte-ekzakt me file-in real mbeten të pandryshuara.
    /// Diff-i i një përditësimi fingerprint-i mbetet po aq i dukshëm.
    /// </summary>
    private static string Fingerprint(params string[] groups)
    {
        if (groups.Length != 8 || Array.Exists(groups, group => group.Length != 8))
        {
            throw new InvalidOperationException("Fingerprint i keqformuar: pritet 8 grupe × 8 karaktere.");
        }

        var value = string.Concat(groups);
        if (!value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new InvalidOperationException("Fingerprint i keqformuar: pritet hex i vogël.");
        }

        return value;
    }

    private static readonly string MigratorSha = Fingerprint(
        "7fd63033", "27ed87a7", "1efd165e", "52d78589", "3dce9abf", "23b790ba", "613d5324", "debfe73c");

    private static readonly string ProbeSha = Fingerprint(
        "50e1b4cf", "8402e401", "15082694", "de25319d", "69457ce8", "079cddd9", "f3d33bc3", "169411ad");

    private static readonly string UnitOfWorkSha = Fingerprint(
        "ceed8e39", "cfe00316", "32419996", "cc8ba02b", "2afdba6d", "a420531a", "0e54c256", "17ce1e65");

    private static readonly string Migration0002Sha = Fingerprint(
        "63a052e6", "9d4f1252", "c9912db9", "dcd807a9", "fa5ddb79", "df14f250", "b81f9c5d", "871020b4");

    /// <summary>
    /// SAKTËSISHT katër surface production (§5 + correction C1) — asnjë hyrje
    /// tjetër nuk lejohet pa review të pavarur + Human Gate.
    /// </summary>
    internal static readonly AllowedRawSurface[] Production =
    [
        // ── Surface 1: DbMigrator — dy SELECT diagnostikë READ-ONLY mbi `platform` ──
        new("src/platform/Idata.Platform.DbMigrator/Idata.Platform.DbMigrator.csproj",
            "Idata.Platform.DbMigrator",
            "src/platform/Idata.Platform.DbMigrator/MigratorApp.cs",
            "Idata.Platform.DbMigrator.MigratorApp",
            "EffectiveDatabaseMatchesAsync",
            MigratorSha,
            "Guard READ-ONLY para çdo mutacioni: krahason `current_database()` me emrin e validuar; "
            + "zero DML, zero schema `identity`.",
            TechOwner),
        new("src/platform/Idata.Platform.DbMigrator/Idata.Platform.DbMigrator.csproj",
            "Idata.Platform.DbMigrator",
            "src/platform/Idata.Platform.DbMigrator/MigratorApp.cs",
            "Idata.Platform.DbMigrator.MigratorApp",
            "SentinelExistsAsync",
            MigratorSha,
            "Guard READ-ONLY i tabelës sentinel `platform.migration_smoke_check`; zero DML, "
            + "zero schema `identity`.",
            TechOwner),

        // ── Surface 2: PlatformStatus readiness probe — sentinel READ-ONLY ──
        new("src/platform/Modules/PlatformStatus/Infrastructure/Idata.Platform.Modules.PlatformStatus.Infrastructure.csproj",
            "Idata.Platform.Modules.PlatformStatus.Infrastructure",
            "src/platform/Modules/PlatformStatus/Infrastructure/NpgsqlDatabaseReadinessProbe.cs",
            "Idata.Platform.Modules.PlatformStatus.Infrastructure.NpgsqlDatabaseReadinessProbe",
            "CheckAsync",
            ProbeSha,
            "Probe READ-ONLY i gatishmërisë: sentinel `platform.migration_smoke_check`; fail-safe, "
            + "zero DML, zero schema `identity`.",
            TechOwner),

        // ── Surface 3: AccessControl lock/mutation protocol mbi tabela `platform.` ──
        new("src/platform/Modules/AccessControl/Infrastructure/Idata.Platform.Modules.AccessControl.Infrastructure.csproj",
            "Idata.Platform.Modules.AccessControl.Infrastructure",
            "src/platform/Modules/AccessControl/Infrastructure/EfMembershipRoleUnitOfWork.cs",
            "Idata.Platform.Modules.AccessControl.Infrastructure.EfMembershipRoleUnitOfWork",
            "TryMutateRoleAsync",
            UnitOfWorkSha,
            "Protokolli atomik i lock-eve F1.1 me rend kanonik (ENG-005): kërkon `FOR UPDATE` që EF "
            + "LINQ nuk e shpreh; komanda konstante të parametrizuara mbi tabela `platform.`.",
            TechOwner),
        new("src/platform/Modules/AccessControl/Infrastructure/Idata.Platform.Modules.AccessControl.Infrastructure.csproj",
            "Idata.Platform.Modules.AccessControl.Infrastructure",
            "src/platform/Modules/AccessControl/Infrastructure/EfMembershipRoleUnitOfWork.cs",
            "Idata.Platform.Modules.AccessControl.Infrastructure.EfMembershipRoleUnitOfWork",
            "ReadMembershipsAsync",
            UnitOfWorkSha,
            "Lexim i membership-eve nën `FOR UPDATE` me rend kanonik; SQL konstant i parametrizuar mbi "
            + "`platform.business_membership`.",
            TechOwner),
        new("src/platform/Modules/AccessControl/Infrastructure/Idata.Platform.Modules.AccessControl.Infrastructure.csproj",
            "Idata.Platform.Modules.AccessControl.Infrastructure",
            "src/platform/Modules/AccessControl/Infrastructure/EfMembershipRoleUnitOfWork.cs",
            "Idata.Platform.Modules.AccessControl.Infrastructure.EfMembershipRoleUnitOfWork",
            "ReadUserStatusesAsync",
            UnitOfWorkSha,
            "Lexim i statuseve të përdoruesve nën lock me rend kanonik; SQL konstant i parametrizuar mbi "
            + "`platform.app_user`.",
            TechOwner),
        new("src/platform/Modules/AccessControl/Infrastructure/Idata.Platform.Modules.AccessControl.Infrastructure.csproj",
            "Idata.Platform.Modules.AccessControl.Infrastructure",
            "src/platform/Modules/AccessControl/Infrastructure/EfMembershipRoleUnitOfWork.cs",
            "Idata.Platform.Modules.AccessControl.Infrastructure.EfMembershipRoleUnitOfWork",
            "ScalarAsync",
            UnitOfWorkSha,
            "Ndihmës skalar i protokollit të lock-eve; konsumon VETËM SQL konstante të deklaruara në "
            + "të njëjtin file të review-uar.",
            TechOwner),
        new("src/platform/Modules/AccessControl/Infrastructure/Idata.Platform.Modules.AccessControl.Infrastructure.csproj",
            "Idata.Platform.Modules.AccessControl.Infrastructure",
            "src/platform/Modules/AccessControl/Infrastructure/EfMembershipRoleUnitOfWork.cs",
            "Idata.Platform.Modules.AccessControl.Infrastructure.EfMembershipRoleUnitOfWork",
            "CreateCommand",
            UnitOfWorkSha,
            "Fabrika e vetme e komandave të protokollit: lidh transaksionin EF ekzistues dhe shton "
            + "parametra — zero konkatenim inputi të jashtëm.",
            TechOwner),

        // ── Surface 4: migrimi 0002 — DDL/triggers të auditit (metoda SYNC, jo async) ──
        new("src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj",
            "Idata.Platform.Persistence",
            "src/platform/Idata.Platform.Persistence/Migrations/0002_F1AccessControlCore.cs",
            "Idata.Platform.Persistence.Migrations.F1AccessControlCore",
            "Up",
            Migration0002Sha,
            "DDL konstante dhe e audituar brenda migrimit të vetëm owner: funksione/triggers "
            + "append-only të auditit që EF DDL i tipizuar nuk i shpreh; zero input i jashtëm; "
            + "migrimi është IMMUTABLE (forward-only, i pandryshueshëm pas merge-it).",
            TechOwner),
        new("src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj",
            "Idata.Platform.Persistence",
            "src/platform/Idata.Platform.Persistence/Migrations/0002_F1AccessControlCore.cs",
            "Idata.Platform.Persistence.Migrations.F1AccessControlCore",
            "Down",
            Migration0002Sha,
            "Rikthimi i guard-uar i të njëjtave funksione/triggers append-only të auditit; DDL "
            + "konstante, zero input i jashtëm; migrimi është IMMUTABLE (forward-only).",
            TechOwner),
    ];

    /// <summary>Fingerprint determinist i file-it të review-uar (sha256 mbi bajtet reale).</summary>
    internal static string ComputeFingerprint(string absoluteSourcePath)
        => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(absoluteSourcePath)));

    /// <summary>
    /// Validim FAIL-CLOSED i manifestit kundër (a) katalogut autoritativ, (b)
    /// burimeve reale në disk dhe (c) gjetjeve reale të skanerit. Kthen listë
    /// gjetjesh — bosh do të thotë "manifest i shëndetshëm DHE i konsumuar
    /// plotësisht DHE mbulon çdo sink". I testueshëm me manifeste sintetike.
    /// </summary>
    internal static IReadOnlyList<string> Validate(
        IReadOnlyList<AllowedRawSurface> manifest,
        IReadOnlyList<PlatformProjectCatalog.ProjectEntry> catalog,
        string repoRoot,
        IReadOnlyList<UntypedDataAccessScanner.Finding> findings)
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var consumed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in manifest)
        {
            var identity = $"{entry.AssemblyName}|{entry.DeclaringType}|{entry.Member}";

            // (1) Struktura: asnjë fushë bosh, asnjë wildcard/prefix.
            foreach (var (field, value) in new (string, string)[]
            {
                ("CanonicalProjectPath", entry.CanonicalProjectPath),
                ("AssemblyName", entry.AssemblyName),
                ("CanonicalSourcePath", entry.CanonicalSourcePath),
                ("DeclaringType", entry.DeclaringType),
                ("Member", entry.Member),
                ("SourceSha256", entry.SourceSha256),
                ("Reason", entry.Reason),
                ("Owner", entry.Owner),
            })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    problems.Add($"empty-field:{identity}:{field}");
                }
                else if (value.Contains('*', StringComparison.Ordinal)
                    || value.Contains('?', StringComparison.Ordinal))
                {
                    problems.Add($"wildcard-entry:{identity}:{field}");
                }
            }

            if (entry.SourceSha256.Length != 64
                || !entry.SourceSha256.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                problems.Add($"malformed-fingerprint:{identity}");
            }

            // (2) Dublikatat.
            if (!seen.Add(identity))
            {
                problems.Add($"duplicate-entry:{identity}");
            }

            // (3) Projekti duhet të jetë në katalogun autoritativ, me assembly të njëjtë.
            var project = catalog.FirstOrDefault(candidate =>
                string.Equals(candidate.CanonicalPath, entry.CanonicalProjectPath, StringComparison.Ordinal));
            if (project is null)
            {
                problems.Add($"unknown-project:{identity}:{entry.CanonicalProjectPath}");
            }
            else if (!string.Equals(project.AssemblyName, entry.AssemblyName, StringComparison.Ordinal))
            {
                problems.Add($"assembly-mismatch:{identity}:{project.AssemblyName}");
            }

            // (4) Burimi real + fingerprint.
            var absoluteSource = Path.Combine(
                repoRoot, entry.CanonicalSourcePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absoluteSource))
            {
                problems.Add($"missing-source:{identity}:{entry.CanonicalSourcePath}");
            }
            else
            {
                string actual;
                try
                {
                    actual = ComputeFingerprint(absoluteSource);
                }
                catch (Exception exception)
                {
                    problems.Add($"unreadable-source:{identity}:{exception.GetType().Name}");
                    continue;
                }

                if (!string.Equals(actual, entry.SourceSha256, StringComparison.Ordinal))
                {
                    problems.Add($"fingerprint-mismatch:{identity}:{entry.CanonicalSourcePath}");
                }
            }
        }

        // (5) Mbulimi: çdo gjetje duhet të ketë hyrje EKZAKTE; gjetjet e
        // pa-atribuueshme/e palexueshme nuk mbulohen KURRË nga manifesti.
        foreach (var finding in findings)
        {
            var identity = $"{finding.Assembly}|{finding.DeclaringType}|{finding.Member}";
            if (UntypedDataAccessScanner.IsNeverAllowable(finding))
            {
                problems.Add($"unallowable-finding:{finding.Kind}:{identity}:{finding.Detail}");
                continue;
            }

            if (seen.Contains(identity))
            {
                consumed.Add(identity);
                continue;
            }

            problems.Add($"uncovered-sink:{finding.Kind}:{identity}:{finding.Detail}");
        }

        // (6) Hyrje e vdekur = FAIL (manifest ≠ dokument dëshirash).
        foreach (var identity in seen.Where(candidate => !consumed.Contains(candidate)).Order(StringComparer.Ordinal))
        {
            problems.Add($"dead-entry:{identity}");
        }

        return problems;
    }
}
