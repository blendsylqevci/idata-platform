using System.Data.Common;
using System.Reflection;
using Npgsql;
using Xunit;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// ENG-007 — Identity untyped data-access boundary (Pre-B2 Increment 1).
///
/// Policy fail-closed DENY-BY-DEFAULT mbi inventarin autoritativ të production
/// assemblies (<see cref="PlatformProjectCatalog"/>): çdo sink i NJOHUR nga
/// taksonomia e mbështetur/versionuar është GJETJE, përveç hyrjeve EKZAKTE në
/// <see cref="RawDataAccessManifest.Production"/>; anëtar i PANJOHUR mbi një tip
/// raw të njohur mbetet gjetje fail-closed. Mbulimi i taksonomisë provohet me
/// fixtures dhe meta-teste — NUK pretendohet mbulim universal i çdo API-je të
/// ardhshme të ekosistemit.
///
/// Kufij të deklaruar HAPUR (zero overclaim): kjo është architecture-policy,
/// JO DB-level isolation; reflection/dynamic invocation mbetet **ENG-006 OPEN**;
/// ADR-026 (DB roles/provisioning) mbetet OPEN; production Go mbetet BLOCKED.
/// </summary>
public sealed class UntypedDataAccessBoundaryTests
{
    private static string SelfAssemblyPath()
        => Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");

    private static string AbsoluteTargetPath(PlatformProjectCatalog.ProjectEntry entry)
        => Path.Combine(
            PlatformProjectCatalog.RepoRoot(),
            entry.CanonicalTargetPath.Replace('/', Path.DirectorySeparatorChar));

    private static IReadOnlyList<UntypedDataAccessScanner.Finding> ScanProduction(out int scanned)
    {
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        var findings = new List<UntypedDataAccessScanner.Finding>();
        scanned = 0;
        foreach (var entry in catalog)
        {
            findings.AddRange(UntypedDataAccessScanner.Scan(AbsoluteTargetPath(entry)));
            scanned++;
        }

        return findings;
    }

    // ═══════════════════ §3–§5 — politika mbi production ═══════════════════

    /// <summary>
    /// Guard-i qendror: universi është i DERIVUAR nga katalogu autoritativ
    /// (zero listë e dytë manuale) dhe çdo sink i gjetur duhet të ketë hyrje
    /// EKZAKTE në manifest. Gjetje e pa-atribuueshme/e palexueshme = FAIL
    /// gjithmonë, edhe nëse dikush do të shtonte hyrje manifesti për të.
    /// </summary>
    [Fact]
    public void Cdo_raw_sink_ne_production_eshte_ose_i_manifestuar_ose_gjetje()
    {
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        Assert.NotEmpty(catalog);

        var findings = ScanProduction(out var scanned);
        Assert.Equal(catalog.Length, scanned);

        var problems = RawDataAccessManifest.Validate(
            RawDataAccessManifest.Production, catalog, PlatformProjectCatalog.RepoRoot(), findings);

        Assert.True(problems.Count == 0, string.Join(" · ", problems));
    }

    /// <summary>
    /// §5 — manifesti production përmban SAKTËSISHT surface-t e review-uara, pa
    /// wildcard, pa përjashtim blanket në nivel assembly-je/tipi, me arsye dhe
    /// pronar të shkruar për çdo hyrje. Counts janë DERIVUARA nga manifesti.
    /// </summary>
    [Fact]
    public void Manifesti_production_permban_saktesisht_surface_t_e_review_uara_pa_wildcard()
    {
        var manifest = RawDataAccessManifest.Production;

        Assert.Equal(10, manifest.Length);
        Assert.Equal(4, manifest.Select(entry => entry.CanonicalSourcePath)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(4, manifest.Select(entry => entry.AssemblyName)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(4, manifest.Select(entry => entry.DeclaringType)
            .Distinct(StringComparer.Ordinal).Count());

        foreach (var entry in manifest)
        {
            Assert.DoesNotContain('*', entry.DeclaringType);
            Assert.DoesNotContain('*', entry.Member);
            Assert.False(string.IsNullOrWhiteSpace(entry.Reason));
            Assert.False(string.IsNullOrWhiteSpace(entry.Owner));
            Assert.Equal(64, entry.SourceSha256.Length);
        }

        // Surface-t e vetme të lejuara — të deklaruara shprehimisht.
        Assert.Equal(
            [
                "src/platform/Idata.Platform.DbMigrator/MigratorApp.cs",
                "src/platform/Idata.Platform.Persistence/Migrations/0002_F1AccessControlCore.cs",
                "src/platform/Modules/AccessControl/Infrastructure/EfMembershipRoleUnitOfWork.cs",
                "src/platform/Modules/PlatformStatus/Infrastructure/NpgsqlDatabaseReadinessProbe.cs",
            ],
            manifest.Select(entry => entry.CanonicalSourcePath).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// §5 — fingerprint-i determinist i çdo file-i të review-uar përputhet me
    /// burimin REAL. Ndryshimi i file-it pa përditësim fingerprint-i = RED.
    /// </summary>
    [Fact]
    public void Fingerprint_i_secilit_surface_te_manifestuar_perputhet_me_burimin_real()
    {
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        foreach (var entry in RawDataAccessManifest.Production)
        {
            var absolute = Path.Combine(
                repoRoot, entry.CanonicalSourcePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(absolute), entry.CanonicalSourcePath);
            Assert.Equal(entry.SourceSha256, RawDataAccessManifest.ComputeFingerprint(absolute));
        }
    }

    /// <summary>
    /// §5 — atribuimi i state machine-ve async zgjidhet DERI TE METODA BURIMORE
    /// (jo `MoveNext`): surface-t 1–3 janë metoda `async`, prandaj pa këtë
    /// zgjidhje manifesti do të ishte i pakonsumueshëm. Kundërprova: `Up`/`Down`
    /// të migrimit 0002 janë SYNC dhe atribuohen direkt.
    /// </summary>
    [Fact]
    public void Atribuimi_i_state_machine_ve_async_zgjidhet_te_metoda_burimore()
    {
        var findings = ScanProduction(out _);

        foreach (var (type, member) in ((string Type, string Member)[])
        [
            ("Idata.Platform.DbMigrator.MigratorApp", "EffectiveDatabaseMatchesAsync"),
            ("Idata.Platform.DbMigrator.MigratorApp", "SentinelExistsAsync"),
            ("Idata.Platform.Modules.PlatformStatus.Infrastructure.NpgsqlDatabaseReadinessProbe", "CheckAsync"),
            ("Idata.Platform.Modules.AccessControl.Infrastructure.EfMembershipRoleUnitOfWork", "TryMutateRoleAsync"),
        ])
        {
            Assert.Contains(findings, finding =>
                finding.DeclaringType == type && finding.Member == member);
        }

        // Asnjë gjetje nuk mbetet e ngjitur te një tip compiler-generated.
        Assert.DoesNotContain(findings, finding =>
            finding.DeclaringType.Contains("+<", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding => UntypedDataAccessScanner.IsNeverAllowable(finding));

        // Kundërprovë: `Up`/`Down` të migrimit 0002 janë metoda SYNC/direkte —
        // atribuimi i tyre NUK kalon nga asnjë state machine, prandaj emri i
        // tipit deklarues është vetë klasa e migrimit.
        foreach (var member in (string[])["Up", "Down"])
        {
            var migration = Assert.Single(findings, finding =>
                finding.DeclaringType == "Idata.Platform.Persistence.Migrations.F1AccessControlCore"
                && finding.Member == member);
            Assert.Equal("qualified-raw-member", migration.Kind);
            Assert.DoesNotContain('<', migration.DeclaringType);
        }
    }

    /// <summary>
    /// Meta-test i mbulimit: skanimi prek ÇDO projekt production të zbuluar nga
    /// katalogu (count i DERIVUAR, jo i hardcoded) dhe çdo assembly i katalogut
    /// është i lexueshëm.
    /// </summary>
    [Fact]
    public void Skanimi_mbulon_cdo_projekt_production_te_zbuluar_nga_katalogu()
    {
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in catalog)
        {
            var findings = UntypedDataAccessScanner.Scan(AbsoluteTargetPath(entry));
            Assert.DoesNotContain(findings, finding => finding.Kind == "unreadable-assembly");
            Assert.True(visited.Add(entry.CanonicalPath), entry.CanonicalPath);
        }

        Assert.Equal(catalog.Length, visited.Count);
        Assert.All(RawDataAccessManifest.Production, entry =>
            Assert.Contains(entry.CanonicalProjectPath, visited));
    }

    // ═══════════════════ §6 — owner public-surface guard ═══════════════════

    /// <summary>
    /// §6 — asnjë owner (Persistence, Modules/Identity/Infrastructure) nuk
    /// ekspozon sipërfaqe publike që PRANON ose DOREZON kapacitet raw.
    /// </summary>
    [Fact]
    public void Owner_assemblies_nuk_ekspozojne_raw_escape_hatch_publik()
    {
        var owners = PlatformProjectCatalog.DiscoverProduction()
            .Where(PlatformProjectCatalog.IsStoreOwner).ToArray();
        Assert.Equal(2, owners.Length);

        foreach (var owner in owners)
        {
            var findings = UntypedDataAccessScanner.ScanOwnerPublicSurface(AbsoluteTargetPath(owner));
            Assert.True(findings.Count == 0,
                $"{owner.CanonicalPath}: {string.Join(" · ", findings.Select(f => $"{f.Kind}:{f.DeclaringType}.{f.Member}({f.Detail})"))}");
        }
    }

    /// <summary>
    /// RED 14 — guard-i i sipërfaqes publike KAP escape hatch-et e mbuluara:
    /// pranim i `DbConnection`/`NpgsqlConnection`, dorëzim i `DbCommand`/
    /// `NpgsqlCommand`, dhe parametër me emër SQL nga fjalori §6.
    /// </summary>
    [Fact]
    public void Guard_i_siperfaqes_publike_kap_escape_hatch_et_e_mbuluara()
    {
        var findings = UntypedDataAccessScanner.ScanOwnerPublicSurface(SelfAssemblyPath());
        var hatch = typeof(OwnerPublicEscapeHatchFixtures).FullName!;

        Assert.Contains(findings, finding =>
            finding.Kind == "owner-public-raw-parameter" && finding.DeclaringType == hatch
            && finding.Member == nameof(OwnerPublicEscapeHatchFixtures.OpenCommand));
        Assert.Contains(findings, finding =>
            finding.Kind == "owner-public-raw-return" && finding.DeclaringType == hatch
            && finding.Member == nameof(OwnerPublicEscapeHatchFixtures.OpenCommand));
        Assert.Contains(findings, finding =>
            finding.Kind == "owner-public-raw-parameter" && finding.DeclaringType == hatch
            && finding.Member == nameof(OwnerPublicEscapeHatchFixtures.CommandLocator));
        Assert.Contains(findings, finding =>
            finding.Kind == "owner-public-sql-parameter" && finding.DeclaringType == hatch
            && finding.Member == nameof(OwnerPublicEscapeHatchFixtures.RunStatement));

        // GREEN 18 — API publik i tipizuar nuk raportohet.
        Assert.DoesNotContain(findings, finding =>
            finding.DeclaringType == typeof(TypedOwnerMediatedApiFixture).FullName);
    }

    // ═══════════════════ §7 — fixtures RED 1–9, 15 ═══════════════════

    [Theory]
    // RED 1 — ADO.NET jo-owner + konstant `identity.`
    [InlineData(nameof(RawSinkFixtures.AdoNetIdentityConstantAsync), "raw-type-member", "DbCommand.set_CommandText")]
    [InlineData(nameof(RawSinkFixtures.AdoNetIdentityConstantAsync), "raw-type-member", "DbConnection.CreateCommand")]
    // RED 2 — schema/tabelë nga fragmente (asnjë literal i plotë `identity.`)
    [InlineData(nameof(RawSinkFixtures.FragmentBuiltSchemaAsync), "raw-type-member", "DbCommand.ExecuteScalarAsync")]
    // RED 3 — mixed-case/quoted/interpolated/parameterized
    [InlineData(nameof(RawSinkFixtures.MixedCaseQuotedParameterizedAsync), "raw-type-member", "System.Data.Common.DbCommand.set_CommandText")]
    [InlineData(nameof(RawSinkFixtures.MixedCaseQuotedParameterizedAsync), "raw-type-member", "Npgsql.NpgsqlCommand.get_Parameters")]
    // RED 4 — FromSqlRaw / FromSqlInterpolated
    [InlineData(nameof(RawSinkFixtures.FromSqlRawFixture), "raw-execution-member", "FromSqlRaw")]
    [InlineData(nameof(RawSinkFixtures.FromSqlInterpolatedFixture), "raw-execution-member", "FromSqlInterpolated")]
    // RED 5 — ExecuteSqlRaw / ExecuteSqlInterpolated
    [InlineData(nameof(RawSinkFixtures.ExecuteSqlRawFixture), "raw-execution-member", "ExecuteSqlRaw")]
    [InlineData(nameof(RawSinkFixtures.ExecuteSqlInterpolatedFixture), "raw-execution-member", "ExecuteSqlInterpolated")]
    // RED 6 — SqlQueryRaw / SqlQuery
    [InlineData(nameof(RawSinkFixtures.SqlQueryRawFixture), "raw-execution-member", "SqlQueryRaw")]
    [InlineData(nameof(RawSinkFixtures.SqlQueryFixture), "raw-execution-member", "SqlQuery")]
    // RED 7 — NpgsqlCommand / NpgsqlBatch / NpgsqlDataSource / import
    [InlineData(nameof(RawSinkFixtures.NpgsqlCommandFixtureAsync), "raw-type-member", "Npgsql.NpgsqlCommand..ctor")]
    [InlineData(nameof(RawSinkFixtures.NpgsqlBatchFixtureAsync), "raw-type-member", "Npgsql.NpgsqlBatch..ctor")]
    [InlineData(nameof(RawSinkFixtures.NpgsqlBatchFixtureAsync), "raw-type-member", "Npgsql.NpgsqlBatchCommand..ctor")]
    [InlineData(nameof(RawSinkFixtures.NpgsqlDataSourceFixtureAsync), "raw-type-member", "NpgsqlDataSource.CreateCommand")]
    [InlineData(nameof(RawSinkFixtures.NpgsqlImportFixture), "raw-type-member", "NpgsqlConnection.BeginBinaryImport")]
    // RED 15 — anëtar i PANJOHUR mbi tip raw (deny-by-default, jo `continue`)
    [InlineData(nameof(RawSinkFixtures.UnknownSinkMemberFixture), "raw-type-member", "System.Data.Common.DbConnection.GetSchema")]
    public void Fixtures_RED_kapen_me_atribuim_te_sakte(string member, string kind, string detail)
    {
        var findings = UntypedDataAccessScanner.Scan(SelfAssemblyPath());
        Assert.Contains(findings, finding =>
            finding.DeclaringType == typeof(RawSinkFixtures).FullName
            && finding.Member == member
            && finding.Kind == kind
            && finding.Detail.Contains(detail, StringComparison.Ordinal));
    }

    /// <summary>
    /// RED 8 + RED 9 — indirektimi përmes helper-i dhe aliasi `ldftn` nuk e
    /// fshehin sink-un: helper-i raportohet si vendi REAL i sink-ut, dhe
    /// method-group delegate kapet te metoda që e krijon.
    /// </summary>
    [Fact]
    public void Sink_ne_helper_dhe_alias_delegate_nuk_fshihet()
    {
        var findings = UntypedDataAccessScanner.Scan(SelfAssemblyPath());
        var fixtures = typeof(RawSinkFixtures).FullName;

        // RED 8 — sink-u kapet TE HELPER-i (jo i humbur te thirrësi).
        Assert.Contains(findings, finding =>
            finding.DeclaringType == fixtures && finding.Member == "RawHelperAsync"
            && finding.Kind == "raw-type-member");

        // RED 9 — `ldftn` mbi sink kapet te metoda që e merr aliasin.
        Assert.Contains(findings, finding =>
            finding.DeclaringType == fixtures
            && finding.Member == nameof(RawSinkFixtures.DelegateAliasFixture)
            && finding.Kind == "raw-type-member"
            && finding.Detail.Contains("DbConnection.CreateCommand", StringComparison.Ordinal));
    }

    /// <summary>
    /// Atribuimi i paprovueshëm është FAIL-CLOSED (§5): sink-u brenda një
    /// lambda-je statike përfundon në një tip compiler-generated pa state
    /// machine → `unattributed-compiler-generated`, kurrë hamendje/PASS.
    /// </summary>
    [Fact]
    public void Kod_compiler_generated_pa_state_machine_eshte_fail_closed()
    {
        var findings = UntypedDataAccessScanner.Scan(SelfAssemblyPath());
        var unattributed = findings.Where(finding => finding.Kind == "unattributed-compiler-generated").ToArray();

        Assert.NotEmpty(unattributed);
        Assert.Contains(unattributed, finding =>
            finding.DeclaringType.Contains("+<>c", StringComparison.Ordinal)
            && finding.Detail.Contains("CreateCommand", StringComparison.Ordinal));
        Assert.All(unattributed, finding => Assert.True(UntypedDataAccessScanner.IsNeverAllowable(finding)));
    }

    // ═══════════════════ §7 — fixtures RED 10–13 (manifesti) ═══════════════════

    private static AllowedRawSurface ValidProductionEntry()
        => RawDataAccessManifest.Production[0];

    /// <summary>RED 10 — projekt/assembly jashtë authority map-it të katalogut.</summary>
    [Fact]
    public void Validatori_kap_projekt_dhe_assembly_jashte_katalogut()
    {
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var template = ValidProductionEntry();

        var unknownProject = template with
        {
            CanonicalProjectPath = "src/platform/Synthetic.Rogue/Synthetic.Rogue.csproj",
        };
        var problems = RawDataAccessManifest.Validate(
            [unknownProject], catalog, repoRoot,
            [new UntypedDataAccessScanner.Finding(
                unknownProject.AssemblyName, "raw-type-member",
                unknownProject.DeclaringType, unknownProject.Member, "sintetik")]);
        Assert.Contains(problems, problem => problem.StartsWith("unknown-project:", StringComparison.Ordinal));

        var wrongAssembly = template with { AssemblyName = "Synthetic.Rogue" };
        var assemblyProblems = RawDataAccessManifest.Validate(
            [wrongAssembly], catalog, repoRoot,
            [new UntypedDataAccessScanner.Finding(
                wrongAssembly.AssemblyName, "raw-type-member",
                wrongAssembly.DeclaringType, wrongAssembly.Member, "sintetik")]);
        Assert.Contains(assemblyProblems, problem => problem.StartsWith("assembly-mismatch:", StringComparison.Ordinal));
    }

    /// <summary>RED 11 — metadata e palexueshme dhe burim manifesti i munguar.</summary>
    [Fact]
    public void Metadata_e_palexueshme_dhe_burimi_i_munguar_jane_fail_closed()
    {
        var notAnAssembly = Path.Combine(Path.GetTempPath(), $"eng007-not-a-pe-{Guid.NewGuid():N}.bin");
        File.WriteAllText(notAnAssembly, "kjo nuk eshte PE");
        try
        {
            var findings = UntypedDataAccessScanner.Scan(notAnAssembly);
            var single = Assert.Single(findings);
            Assert.Equal("unreadable-assembly", single.Kind);
            Assert.True(UntypedDataAccessScanner.IsNeverAllowable(single));

            var ownerFindings = UntypedDataAccessScanner.ScanOwnerPublicSurface(notAnAssembly);
            Assert.Equal("unreadable-assembly", Assert.Single(ownerFindings).Kind);
        }
        finally
        {
            File.Delete(notAnAssembly);
        }

        var template = ValidProductionEntry();
        var missingSource = template with
        {
            CanonicalSourcePath = "src/platform/Idata.Platform.DbMigrator/MigratorApp.Missing.cs",
        };
        var problems = RawDataAccessManifest.Validate(
            [missingSource], PlatformProjectCatalog.DiscoverProduction(), PlatformProjectCatalog.RepoRoot(),
            [new UntypedDataAccessScanner.Finding(
                missingSource.AssemblyName, "raw-type-member",
                missingSource.DeclaringType, missingSource.Member, "sintetik")]);
        Assert.Contains(problems, problem => problem.StartsWith("missing-source:", StringComparison.Ordinal));
    }

    /// <summary>
    /// RED 12 — mutimi i surface-it të përjashtuar PA përditësim fingerprint-i:
    /// çdo devijim byte i file-it të review-uar e bën manifestin të pavlefshëm.
    /// </summary>
    [Fact]
    public void Mutimi_i_surface_it_te_perjashtuar_pa_update_fingerprint_i_eshte_gjetje()
    {
        var template = ValidProductionEntry();
        var mutated = template with
        {
            SourceSha256 = new string('a', 64), // fingerprint i vjetruar/i gabuar
        };

        var problems = RawDataAccessManifest.Validate(
            [mutated], PlatformProjectCatalog.DiscoverProduction(), PlatformProjectCatalog.RepoRoot(),
            [new UntypedDataAccessScanner.Finding(
                mutated.AssemblyName, "raw-type-member", mutated.DeclaringType, mutated.Member, "sintetik")]);

        Assert.Contains(problems, problem => problem.StartsWith("fingerprint-mismatch:", StringComparison.Ordinal));

        // Fingerprint-i i keqformuar kapet veçmas nga mospërputhja.
        var malformed = template with { SourceSha256 = "JO-HEX" };
        var malformedProblems = RawDataAccessManifest.Validate(
            [malformed], PlatformProjectCatalog.DiscoverProduction(), PlatformProjectCatalog.RepoRoot(),
            [new UntypedDataAccessScanner.Finding(
                malformed.AssemblyName, "raw-type-member", malformed.DeclaringType, malformed.Member, "sintetik")]);
        Assert.Contains(malformedProblems, problem => problem.StartsWith("malformed-fingerprint:", StringComparison.Ordinal));
    }

    /// <summary>RED 13 — hyrje e dyfishtë, e vdekur, me wildcard ose bosh.</summary>
    [Fact]
    public void Duplicate_dead_wildcard_dhe_uncovered_kapen_nga_validatori()
    {
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        var template = ValidProductionEntry();
        var consumingFinding = new UntypedDataAccessScanner.Finding(
            template.AssemblyName, "raw-type-member", template.DeclaringType, template.Member, "sintetik");

        // Dublikatë (dy hyrje identike) — një konsumohet, dublikata raportohet.
        var duplicate = RawDataAccessManifest.Validate(
            [template, template], catalog, repoRoot, [consumingFinding]);
        Assert.Contains(duplicate, problem => problem.StartsWith("duplicate-entry:", StringComparison.Ordinal));

        // Hyrje e VDEKUR — asnjë gjetje reale nuk e konsumon.
        var dead = RawDataAccessManifest.Validate([template], catalog, repoRoot, []);
        Assert.Contains(dead, problem => problem.StartsWith("dead-entry:", StringComparison.Ordinal));

        // Wildcard/blanket exemption.
        var wildcard = RawDataAccessManifest.Validate(
            [template with { Member = "*" }], catalog, repoRoot, []);
        Assert.Contains(wildcard, problem => problem.StartsWith("wildcard-entry:", StringComparison.Ordinal));

        // Fushë bosh (arsye/pronar i pashkruar).
        var empty = RawDataAccessManifest.Validate(
            [template with { Reason = "  " }], catalog, repoRoot, [consumingFinding]);
        Assert.Contains(empty, problem => problem.StartsWith("empty-field:", StringComparison.Ordinal));

        // Sink i pambuluar — manifest bosh përballë një gjetjeje reale.
        var uncovered = RawDataAccessManifest.Validate([], catalog, repoRoot, [consumingFinding]);
        Assert.Contains(uncovered, problem => problem.StartsWith("uncovered-sink:", StringComparison.Ordinal));

        // Gjetje e pa-atribuueshme NUK mbulohet KURRË nga manifesti.
        var unallowable = RawDataAccessManifest.Validate(
            [template], catalog, repoRoot,
            [consumingFinding,
             new UntypedDataAccessScanner.Finding(
                 template.AssemblyName, "unattributed-compiler-generated",
                 template.DeclaringType, template.Member, "sintetik")]);
        Assert.Contains(unallowable, problem => problem.StartsWith("unallowable-finding:", StringComparison.Ordinal));
    }

    // ═══════════════════ §7 — fixtures GREEN 16–21 ═══════════════════

    [Theory]
    [InlineData(nameof(RawSinkFixtures.TypedLinqFixture))]              // GREEN 17
    [InlineData(nameof(RawSinkFixtures.TypedIdentityStoreFixture))]     // GREEN 19
    [InlineData(nameof(RawSinkFixtures.IdentitySchemaLiteralFixture))]  // GREEN 20
    [InlineData(nameof(RawSinkFixtures.DynamicRawAccessResidualPoC))]   // GREEN 21
    public void Fixtures_GREEN_nuk_prodhojne_gjetje_ENG007(string member)
    {
        var findings = UntypedDataAccessScanner.Scan(SelfAssemblyPath());
        Assert.DoesNotContain(findings, finding =>
            finding.DeclaringType == typeof(RawSinkFixtures).FullName && finding.Member == member);
    }

    /// <summary>
    /// GREEN 16 — surface-t ekzistuese me identitet DHE fingerprint ekzakt janë
    /// të mbuluara plotësisht: çdo gjetje production konsumohet nga një hyrje e
    /// vetme, dhe asnjë hyrje nuk mbetet e vdekur.
    /// </summary>
    [Fact]
    public void Surface_t_e_manifestuara_konsumojne_saktesisht_gjetjet_reale()
    {
        var findings = ScanProduction(out _);
        var identities = findings
            .Select(finding => $"{finding.Assembly}|{finding.DeclaringType}|{finding.Member}")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        var manifestIdentities = RawDataAccessManifest.Production
            .Select(entry => $"{entry.AssemblyName}|{entry.DeclaringType}|{entry.Member}")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(manifestIdentities, identities);
    }

    /// <summary>
    /// GREEN 21 — reflection dinamik: emri i tipit raw ndërtohet VËRTET në
    /// runtime, prandaj skaneri statik nuk e sheh. Ky rezultat klasifikohet
    /// **ENG-006 OPEN** — NUK është provë se ENG-007 mbulon reflektimin.
    /// </summary>
    [Fact]
    public void Reflection_dinamik_mbetet_ENG006_OPEN_jo_prove_ENG007_PASS()
    {
        // Zgjidhja REALE në runtime funksionon…
        var resolved = RawSinkFixtures.DynamicRawAccessResidualPoC() as Type;
        Assert.NotNull(resolved);
        Assert.Equal("System.Data.Common.DbCommand", resolved!.FullName);

        // …ndërsa skaneri statik nuk prodhon asnjë gjetje për të.
        var findings = UntypedDataAccessScanner.Scan(SelfAssemblyPath());
        Assert.DoesNotContain(findings, finding =>
            finding.DeclaringType == typeof(RawSinkFixtures).FullName
            && finding.Member == nameof(RawSinkFixtures.DynamicRawAccessResidualPoC));
    }

    // ═══════════════════ §7 — meta-teste ═══════════════════

    /// <summary>
    /// Meta-test: ÇDO familje sink-esh është MBAJTËSE. Heqja e familjes nga
    /// taksonomia e bën zbulimin përkatës të zhduket — pra guard-i nuk kalon
    /// aksidentalisht nga një rrugë tjetër.
    /// </summary>
    [Theory]
    [InlineData("self", nameof(RawSinkFixtures.FromSqlRawFixture), "FromSqlRaw",
        "", "FromSql,FromSqlRaw,FromSqlInterpolated")]
    [InlineData("self", nameof(RawSinkFixtures.ExecuteSqlRawFixture), "ExecuteSqlRaw",
        "", "ExecuteSql,ExecuteSqlAsync,ExecuteSqlRaw,ExecuteSqlRawAsync,ExecuteSqlInterpolated,ExecuteSqlInterpolatedAsync")]
    [InlineData("self", nameof(RawSinkFixtures.SqlQueryRawFixture), "SqlQueryRaw",
        "", "SqlQuery,SqlQueryRaw")]
    [InlineData("self", nameof(RawSinkFixtures.AdoNetIdentityConstantAsync), "System.Data.Common.DbCommand.set_CommandText",
        "System.Data.Common.DbCommand", "CommandText,get_CommandText,set_CommandText")]
    [InlineData("self", nameof(RawSinkFixtures.NpgsqlImportFixture), "BeginBinaryImport",
        "Npgsql.NpgsqlConnection", "BeginTextImport,BeginBinaryImport,BeginRawBinaryCopy,BeginTextExport,BeginBinaryExport")]
    [InlineData("probe", "CheckAsync", "GetDbConnection",
        "", "GetDbConnection,SetDbConnection,GetDbTransaction")]
    public void Heqja_e_cdo_familje_sink_esh_e_zhduk_zbulimin_perkates(
        string target, string member, string detail, string typesToRemove, string membersToRemove)
    {
        var assemblyPath = target == "self"
            ? SelfAssemblyPath()
            : AbsoluteTargetPath(PlatformProjectCatalog.DiscoverProduction().Single(entry =>
                entry.AssemblyName == "Idata.Platform.Modules.PlatformStatus.Infrastructure"));

        bool Detected(UntypedDataAccessScanner.SinkTaxonomy? taxonomy)
            => UntypedDataAccessScanner.Scan(assemblyPath, taxonomy).Any(finding =>
                finding.Member == member && finding.Detail.Contains(detail, StringComparison.Ordinal));

        Assert.True(Detected(null), $"pritej zbulim me taksonominë e plotë: {member}/{detail}");

        var reducedTypes = RawDataSinkTaxonomy.RawCapabilityTypeFullNames
            .Except(Split(typesToRemove), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var reducedMembers = RawDataSinkTaxonomy.RawExecutionMemberNames
            .Except(Split(membersToRemove), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

        Assert.False(
            Detected(new UntypedDataAccessScanner.SinkTaxonomy(
                reducedTypes, reducedMembers, RawDataSinkTaxonomy.QualifiedRawExecutionMembers)),
            $"heqja e familjes NUK e ndryshoi zbulimin (guard jo-mbajtës): {member}/{detail}");
    }

    private static string[] Split(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Meta-test: rregulli "anëtar i PANJOHUR mbi tip raw = gjetje" është vetë
    /// mbajtës — heqja e tipit raw nga taksonomia e humbet zbulimin, çka provon
    /// se `GetSchema` kapej NGA anëtarësia në tip, jo nga një listë emrash.
    /// </summary>
    [Fact]
    public void Rregulli_unknown_eshte_finding_eshte_mbajtes()
    {
        bool Detected(UntypedDataAccessScanner.SinkTaxonomy? taxonomy)
            => UntypedDataAccessScanner.Scan(SelfAssemblyPath(), taxonomy).Any(finding =>
                finding.Member == nameof(RawSinkFixtures.UnknownSinkMemberFixture)
                && finding.Detail.Contains("GetSchema", StringComparison.Ordinal));

        Assert.True(Detected(null));
        Assert.DoesNotContain("GetSchema", RawDataSinkTaxonomy.RawExecutionMemberNames);

        var withoutDbConnection = RawDataSinkTaxonomy.RawCapabilityTypeFullNames
            .Where(name => name != "System.Data.Common.DbConnection").ToHashSet(StringComparer.Ordinal);
        Assert.False(Detected(new UntypedDataAccessScanner.SinkTaxonomy(
            withoutDbConnection, RawDataSinkTaxonomy.RawExecutionMemberNames,
            RawDataSinkTaxonomy.QualifiedRawExecutionMembers)));
    }

    /// <summary>
    /// Kufi PIKE i taksonomisë: `NpgsqlConnection` NUK e kap
    /// `NpgsqlConnectionStringBuilder` (ndërtues konfigurimi, jo ekzekutim raw)
    /// — pikërisht tipi që Persistence përdor sot.
    /// </summary>
    [Fact]
    public void Taksonomia_ka_kufi_pike_dhe_nuk_kap_ndertuesin_e_connection_string_ut()
    {
        Assert.Contains("Npgsql.NpgsqlConnection", RawDataSinkTaxonomy.RawCapabilityTypeFullNames);
        Assert.DoesNotContain("Npgsql.NpgsqlConnectionStringBuilder", RawDataSinkTaxonomy.RawCapabilityTypeFullNames);
        Assert.False(RawDataSinkTaxonomy.IsRawCapabilityType("Npgsql.NpgsqlConnectionStringBuilder"));
        Assert.False(RawDataSinkTaxonomy.IsRawCapabilityType("Npgsql.NpgsqlParameter"));

        // Komponentët nxirren me barazi ekzakte, jo me `Contains`.
        Assert.Equal(
            ["System.Func", "Npgsql.NpgsqlConnectionStringBuilder", "System.Int32"],
            UntypedDataAccessScanner.ExtractTypeComponents(
                "System.Func<Npgsql.NpgsqlConnectionStringBuilder,System.Int32>").ToArray());
    }

    // ═══════════════════ Dekoduesi IL — saktësia e operandëve ═══════════════════

    /// <summary>
    /// Dekoduesi IL është AUTORITETI i atribuimit, prandaj provohet veçmas:
    /// operandët e gjatë nuk keqlexohen si token-a, opcode-t e pavlefshëm dhe
    /// operandët e cunguar NDALOJNË dekodimin me gabim (kurrë heshtje).
    /// </summary>
    [Fact]
    public void Dekoduesi_IL_konsumon_operandet_sakte_dhe_deshton_fail_closed()
    {
        // `ldc.i8` me bajte që "duken" si token `call` + `ret` → ZERO token.
        var longOperand = new byte[] { 0x21, 0x28, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x2A };
        var decoded = IlOperandTable.Walk(longOperand, out var noError);
        Assert.Null(noError);
        Assert.Empty(decoded);

        // `call <token>` + `ret` → saktësisht një token.
        var call = new byte[] { 0x28, 0x11, 0x22, 0x33, 0x0A, 0x2A };
        var callDecoded = IlOperandTable.Walk(call, out var callError);
        Assert.Null(callError);
        Assert.Equal(0x0A332211, Assert.Single(callDecoded).MetadataToken);

        // `switch` me dy degë konsumohet i plotë.
        var @switch = new byte[]
        {
            0x45, 0x02, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x2A,
        };
        var switchDecoded = IlOperandTable.Walk(@switch, out var switchError);
        Assert.Null(switchError);
        Assert.Empty(switchDecoded);

        // `calli` = thirrje indirekte → gjetje fail-closed, kurrë PASS.
        var calli = new byte[] { 0x29, 0x01, 0x00, 0x00, 0x11, 0x2A };
        var calliDecoded = IlOperandTable.Walk(calli, out var calliError);
        Assert.Null(calliError);
        Assert.True(Assert.Single(calliDecoded).IsIndirectCall);

        // Opcode i pavlefshëm dhe operand i cunguar → gabim i raportuar.
        IlOperandTable.Walk([0x24], out var invalidOpcode);
        Assert.Contains("i pavlefshëm", invalidOpcode);

        IlOperandTable.Walk([0x28, 0x01, 0x02], out var truncated);
        Assert.Contains("i cunguar", truncated);

        IlOperandTable.Walk([0xFE], out var truncatedPrefix);
        Assert.Contains("i cunguar", truncatedPrefix);
    }

    // ═══════ C1/A — `MigrationBuilder::Sql` si ÇIFT i kualifikuar ═══════

    private const string MigrationBuilderSql =
        "Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder.Sql";

    /// <summary>
    /// Sink-u kapet VETËM nga çifti (tip prind + anëtar): `MigrationBuilder`
    /// nuk është tip raw dhe `Sql` nuk është emër global. Gjetja mban EMRIN E
    /// PLOTË të tipit prind dhe atribuohet te metoda reale.
    /// </summary>
    [Fact]
    public void Sink_i_kualifikuar_MigrationBuilder_Sql_kapet_me_cift_type_plus_member()
    {
        // Kufiri i rregullit është i lidhur me tipin prind, jo me emrin.
        Assert.True(RawDataSinkTaxonomy.IsQualifiedRawMember(
            "Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder", "Sql"));
        Assert.False(RawDataSinkTaxonomy.IsQualifiedRawMember(
            "Idata.Platform.ArchitectureTests.UnrelatedSqlHost", "Sql"));
        Assert.DoesNotContain("Sql", RawDataSinkTaxonomy.RawExecutionMemberNames);
        Assert.False(RawDataSinkTaxonomy.IsRawCapabilityType(
            "Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder"));

        var findings = UntypedDataAccessScanner.Scan(SelfAssemblyPath());
        var finding = Assert.Single(findings, candidate =>
            candidate.DeclaringType == typeof(RawSinkFixtures).FullName
            && candidate.Member == nameof(RawSinkFixtures.MigrationBuilderSqlFixture)
            && candidate.Kind == "qualified-raw-member");
        Assert.Equal(MigrationBuilderSql, finding.Detail);
    }

    /// <summary>
    /// `Up` dhe `Down` të migrimit 0002 raportohen dhe konsumohen nga hyrjet
    /// EKZAKTE të manifestit — pa asnjë ndryshim në vetë migrimin (IMMUTABLE):
    /// fingerprint-i i deklaruar përputhet me file-in real.
    /// </summary>
    [Fact]
    public void Up_dhe_Down_i_migrimit_0002_konsumohen_nga_manifesti_ekzakt()
    {
        const string migrationType = "Idata.Platform.Persistence.Migrations.F1AccessControlCore";
        const string migrationSource =
            "src/platform/Idata.Platform.Persistence/Migrations/0002_F1AccessControlCore.cs";

        var findings = ScanProduction(out _);
        foreach (var member in (string[])["Up", "Down"])
        {
            Assert.Contains(findings, finding =>
                finding.Assembly == "Idata.Platform.Persistence"
                && finding.DeclaringType == migrationType
                && finding.Member == member
                && finding.Kind == "qualified-raw-member"
                && finding.Detail == MigrationBuilderSql);

            var entry = Assert.Single(RawDataAccessManifest.Production, candidate =>
                candidate.DeclaringType == migrationType && candidate.Member == member);
            Assert.Equal(migrationSource, entry.CanonicalSourcePath);
            Assert.Equal("Idata.Platform.Persistence", entry.AssemblyName);
        }

        // Migrimi mbetet i paprekur: fingerprint-i i deklaruar == file-i real.
        var absolute = Path.Combine(
            PlatformProjectCatalog.RepoRoot(),
            migrationSource.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(
            RawDataAccessManifest.Production.First(entry => entry.DeclaringType == migrationType).SourceSha256,
            RawDataAccessManifest.ComputeFingerprint(absolute));
    }

    /// <summary>
    /// GREEN — anëtar homonim `Sql` mbi tipe TË TJERA nuk raportohet: as tipi
    /// REAL i EF-it `SqlOperation` (property `set_Sql`), as një tip krejt i
    /// palidhur brenda assembly-së.
    /// </summary>
    [Fact]
    public void Anetar_homonim_Sql_mbi_tip_tjeter_nuk_eshte_gjetje()
    {
        var findings = UntypedDataAccessScanner.Scan(SelfAssemblyPath());

        foreach (var member in (string[])
        [
            nameof(RawSinkFixtures.SqlOperationPropertyFixture),
            nameof(RawSinkFixtures.UnrelatedSqlMemberFixture),
        ])
        {
            Assert.DoesNotContain(findings, finding =>
                finding.DeclaringType == typeof(RawSinkFixtures).FullName && finding.Member == member);
        }

        Assert.DoesNotContain(findings, finding =>
            finding.DeclaringType == typeof(UnrelatedSqlHost).FullName);
    }

    /// <summary>
    /// Meta-test: rregulli i KUALIFIKUAR është MBAJTËS — heqja e VETËM tij e
    /// zhduk zbulimin te fixture-i dhe te të dyja metodat production.
    /// </summary>
    [Fact]
    public void Heqja_e_rregullit_te_kualifikuar_e_zhduk_zbulimin_e_MigrationBuilder_Sql()
    {
        var empty = new UntypedDataAccessScanner.SinkTaxonomy(
            RawDataSinkTaxonomy.RawCapabilityTypeFullNames,
            RawDataSinkTaxonomy.RawExecutionMemberNames,
            new HashSet<(string, string)>());

        bool DetectedIn(string assemblyPath, UntypedDataAccessScanner.SinkTaxonomy? taxonomy)
            => UntypedDataAccessScanner.Scan(assemblyPath, taxonomy)
                .Any(finding => finding.Detail == MigrationBuilderSql);

        Assert.True(DetectedIn(SelfAssemblyPath(), null));
        Assert.False(DetectedIn(SelfAssemblyPath(), empty));

        var persistence = AbsoluteTargetPath(PlatformProjectCatalog.DiscoverProduction()
            .Single(entry => entry.AssemblyName == "Idata.Platform.Persistence"));
        Assert.True(DetectedIn(persistence, null));
        Assert.False(DetectedIn(persistence, empty));
    }

    // ═══════ C1/B — familja IDataAdapter / NpgsqlDataAdapter ═══════

    [Theory]
    [InlineData(nameof(RawSinkFixtures.NpgsqlDataAdapterConstructorFixture), "Npgsql.NpgsqlDataAdapter..ctor")]
    [InlineData(nameof(RawSinkFixtures.DataAdapterInterfaceFillFixture), "System.Data.IDataAdapter.Fill")]
    [InlineData(nameof(RawSinkFixtures.DataAdapterFillSchemaFixture), "System.Data.Common.DataAdapter.FillSchema")]
    [InlineData(nameof(RawSinkFixtures.DataAdapterUpdateFixture), "System.Data.Common.DataAdapter.Update")]
    public void Familja_DataAdapter_kapet_me_atribuim_te_sakte(string member, string detail)
    {
        var findings = UntypedDataAccessScanner.Scan(SelfAssemblyPath());
        Assert.Contains(findings, finding =>
            finding.DeclaringType == typeof(RawSinkFixtures).FullName
            && finding.Member == member
            && finding.Kind == "raw-type-member"
            && finding.Detail == detail);
    }

    /// <summary>
    /// GREEN — emrat gjenerikë `Fill`/`FillSchema`/`Update` NUK janë shtuar
    /// globalisht. Prova nuk është emri i testit por METADATA REALE: assembly-ja
    /// përmban një `System.Array::Fill` (referencë e vërtetë) dhe ai NUK
    /// raportohet; njësoj një anëtar homonim brenda assembly-së.
    /// </summary>
    [Fact]
    public void Emrat_gjenerike_te_adapter_it_nuk_jane_globale()
    {
        foreach (var name in (string[])["Fill", "FillSchema", "Update"])
        {
            Assert.DoesNotContain(name, RawDataSinkTaxonomy.RawExecutionMemberNames);
        }

        var findings = UntypedDataAccessScanner.Scan(SelfAssemblyPath());
        Assert.DoesNotContain(findings, finding =>
            finding.Detail.StartsWith("System.Array.", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding =>
            finding.DeclaringType == typeof(RawSinkFixtures).FullName
            && finding.Member == nameof(RawSinkFixtures.UnrelatedFillMemberFixture));
    }

    /// <summary>
    /// Meta-test: secili tip i familjes adapter është MBAJTËS — heqja e tij e
    /// zhduk saktësisht zbulimin që varet prej tij.
    /// </summary>
    [Theory]
    [InlineData("Npgsql.NpgsqlDataAdapter", "Npgsql.NpgsqlDataAdapter..ctor")]
    [InlineData("System.Data.IDataAdapter", "System.Data.IDataAdapter.Fill")]
    [InlineData("System.Data.Common.DataAdapter", "System.Data.Common.DataAdapter.FillSchema")]
    [InlineData("System.Data.Common.DataAdapter", "System.Data.Common.DataAdapter.Update")]
    public void Heqja_e_tipit_te_adapter_it_e_zhduk_zbulimin_perkates(string rawType, string detail)
    {
        bool Detected(UntypedDataAccessScanner.SinkTaxonomy? taxonomy)
            => UntypedDataAccessScanner.Scan(SelfAssemblyPath(), taxonomy)
                .Any(finding => finding.Detail == detail);

        Assert.True(Detected(null));

        var reduced = RawDataSinkTaxonomy.RawCapabilityTypeFullNames
            .Where(name => name != rawType).ToHashSet(StringComparer.Ordinal);
        Assert.False(Detected(new UntypedDataAccessScanner.SinkTaxonomy(
            reduced, RawDataSinkTaxonomy.RawExecutionMemberNames,
            RawDataSinkTaxonomy.QualifiedRawExecutionMembers)));
    }

    /// <summary>
    /// Sot NUK ka përdorim production të `DataAdapter`, prandaj manifesti
    /// production mbetet BOSH për këtë familje — zero hyrje e re, zero gjetje.
    /// </summary>
    [Fact]
    public void Familja_DataAdapter_nuk_ka_asnje_hyrje_manifesti_production()
    {
        Assert.DoesNotContain(RawDataAccessManifest.Production, entry =>
            entry.DeclaringType.Contains("Adapter", StringComparison.Ordinal)
            || entry.Member.Contains("Adapter", StringComparison.Ordinal));

        var findings = ScanProduction(out _);
        Assert.DoesNotContain(findings, finding =>
            finding.Detail.Contains("DataAdapter", StringComparison.Ordinal));
    }

    // ═══════ C2 — familja provider: factory + command builder ═══════

    [Theory]
    [InlineData(nameof(RawSinkFixtures.NpgsqlFactoryInstanceFixture), "Npgsql.NpgsqlFactory.Instance")]
    [InlineData(nameof(RawSinkFixtures.NpgsqlCommandBuilderFixture), "Npgsql.NpgsqlCommandBuilder..ctor")]
    [InlineData(nameof(RawSinkFixtures.DbCommandBuilderInsertFixture), "System.Data.Common.DbCommandBuilder.GetInsertCommand")]
    public void Familja_provider_kapet_me_atribuim_te_sakte(string member, string detail)
    {
        var findings = UntypedDataAccessScanner.Scan(SelfAssemblyPath());
        var finding = Assert.Single(findings, candidate =>
            candidate.DeclaringType == typeof(RawSinkFixtures).FullName
            && candidate.Member == member
            && candidate.Kind == "raw-type-member"
            && candidate.Detail == detail);

        // Locator-i i plotë asertohet, jo vetëm prania e një gjetjeje.
        Assert.Equal("Idata.Platform.ArchitectureTests", finding.Assembly);
        Assert.False(UntypedDataAccessScanner.IsNeverAllowable(finding));
    }

    /// <summary>
    /// Meta-test: secili tip i ri i familjes provider është MBAJTËS — heqja e tij
    /// e zhduk saktësisht zbulimin që varet prej tij. Emrat e anëtarëve
    /// (`Instance`, `GetInsertCommand`) NUK janë dhe nuk duhet të bëhen globalë.
    /// </summary>
    [Theory]
    [InlineData("Npgsql.NpgsqlFactory", "Npgsql.NpgsqlFactory.Instance", "Instance")]
    [InlineData("Npgsql.NpgsqlCommandBuilder", "Npgsql.NpgsqlCommandBuilder..ctor", ".ctor")]
    [InlineData("System.Data.Common.DbCommandBuilder", "System.Data.Common.DbCommandBuilder.GetInsertCommand", "GetInsertCommand")]
    public void Heqja_e_tipit_provider_e_zhduk_zbulimin_perkates(string rawType, string detail, string memberName)
    {
        Assert.DoesNotContain(memberName, RawDataSinkTaxonomy.RawExecutionMemberNames);

        bool Detected(UntypedDataAccessScanner.SinkTaxonomy? taxonomy)
            => UntypedDataAccessScanner.Scan(SelfAssemblyPath(), taxonomy)
                .Any(finding => finding.Detail == detail);

        Assert.True(Detected(null));

        var reduced = RawDataSinkTaxonomy.RawCapabilityTypeFullNames
            .Where(name => name != rawType).ToHashSet(StringComparer.Ordinal);
        Assert.False(Detected(new UntypedDataAccessScanner.SinkTaxonomy(
            reduced, RawDataSinkTaxonomy.RawExecutionMemberNames,
            RawDataSinkTaxonomy.QualifiedRawExecutionMembers)));
    }

    /// <summary>
    /// GREEN — homonime SINTETIKE: emri i thjeshtë përputhet, `FullName` jo.
    /// Provon se taksonomia lidhet me emrin E PLOTË, jo me emrin e thjeshtë.
    /// </summary>
    [Fact]
    public void Homonimet_sintetike_te_familjes_provider_nuk_jane_gjetje()
    {
        foreach (var syntheticName in (string[])
        [
            "Idata.Platform.ArchitectureTests.SyntheticProviders+NpgsqlFactory",
            "Idata.Platform.ArchitectureTests.SyntheticProviders+NpgsqlCommandBuilder",
        ])
        {
            Assert.False(RawDataSinkTaxonomy.IsRawCapabilityType(syntheticName));
        }

        var findings = UntypedDataAccessScanner.Scan(SelfAssemblyPath());
        Assert.DoesNotContain(findings, finding =>
            finding.DeclaringType == typeof(RawSinkFixtures).FullName
            && finding.Member == nameof(RawSinkFixtures.SyntheticProviderHomonymsFixture));
        Assert.DoesNotContain(findings, finding =>
            finding.DeclaringType.StartsWith(
                "Idata.Platform.ArchitectureTests.SyntheticProviders", StringComparison.Ordinal));
    }

    // ─── Meta-test strukturor: parity e familjes provider ───

    private static readonly Type[] ProviderFamilyBases =
        [typeof(DbProviderFactory), typeof(DbCommandBuilder), typeof(DbDataAdapter)];

    /// <summary>
    /// Derivon nga një assembly REALE çdo tip publik KONKRET që është assignable
    /// te ndonjëra prej bazave provider-family. Zero listë manuale.
    /// </summary>
    private static string[] DeriveProviderFamily(Assembly assembly)
        => [.. assembly.GetExportedTypes()
            .Where(type => !type.IsAbstract && !type.IsInterface && type.IsPublic
                && ProviderFamilyBases.Any(family => family.IsAssignableFrom(type)))
            .Select(type => type.FullName!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// MBYLLJA STRUKTURORE e listës manuale për tri familjet provider: universi
    /// derivohet nga assembly-ja REALE e Npgsql-it e zgjidhur nga dependency
    /// graph-i i pin-uar (tipi `NpgsqlConnection` → assembly-ja e tij e ngarkuar),
    /// jo nga një listë e shkruar dorazi.
    ///
    /// Testi NUK auto-allowlistson asgjë dhe NUK e zgjeron skanimin në gjithë
    /// ekosistemin: një tip i ri provider në një version të ardhshëm të Npgsql-it
    /// e bën këtë test RED derisa të klasifikohet SHPREHIMISHT në taksonomi, me
    /// fixture dhe review — dështimi është kërkesë për review, jo defekt.
    /// </summary>
    [Fact]
    public void Familja_provider_e_Npgsql_it_eshte_e_klasifikuar_plotesisht()
    {
        var assembly = typeof(NpgsqlConnection).Assembly;
        Assert.Equal("Npgsql", assembly.GetName().Name);

        // (1) Bazat duhet të jenë vetë në taksonomi.
        foreach (var family in ProviderFamilyBases)
        {
            Assert.Contains(family.FullName!, RawDataSinkTaxonomy.RawCapabilityTypeFullNames);
        }

        var derived = DeriveProviderFamily(assembly);
        Assert.NotEmpty(derived);

        // (2) Çdo tip konkret provider i derivuar duhet të ekzistojë me FullName EKZAKT.
        var unclassified = derived.Where(name => !RawDataSinkTaxonomy.IsRawCapabilityType(name)).ToArray();
        Assert.True(unclassified.Length == 0,
            "Tip(e) provider TË PAKLASIFIKUAR në taksonomi — kërkohet klasifikim EKSPLICIT "
            + $"+ fixture + review, JO auto-allowlist: {string.Join(", ", unclassified)}");

        // (3) Minimumi i pritur për versionin aktual të pin-uar.
        foreach (var expected in (string[])
            ["Npgsql.NpgsqlFactory", "Npgsql.NpgsqlCommandBuilder", "Npgsql.NpgsqlDataAdapter"])
        {
            Assert.Contains(expected, derived);
        }
    }

    /// <summary>
    /// (4)+(5) Prova që kontrolli i parity-t është MBAJTËS: mbi të njëjtin
    /// universi të derivuar, një taksonomi së cilës i mungon një tip provider e
    /// raporton atë si TË PAKLASIFIKUAR. Kështu një tip i ri i një versioni të
    /// ardhshëm skuq testin, në vend që të kalojë në heshtje.
    /// </summary>
    [Fact]
    public void Kontrolli_i_parity_t_provider_eshte_mbajtes()
    {
        var derived = DeriveProviderFamily(typeof(NpgsqlConnection).Assembly);
        Assert.Contains("Npgsql.NpgsqlFactory", derived);

        var reduced = RawDataSinkTaxonomy.RawCapabilityTypeFullNames
            .Where(name => name != "Npgsql.NpgsqlFactory").ToHashSet(StringComparer.Ordinal);
        var unclassified = derived.Where(name => !reduced.Contains(name)).ToArray();

        Assert.Equal(["Npgsql.NpgsqlFactory"], unclassified);
    }
}
