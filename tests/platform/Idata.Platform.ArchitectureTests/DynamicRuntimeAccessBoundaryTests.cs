using System.Collections.Frozen;
using Xunit;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// ENG-006 — kufiri DENY-BY-DEFAULT i aksesit dinamik reflection/runtime
/// (Pre-B2 Increment 2, §5–§6).
///
/// Kufij të DEKLARUAR (zero overclaim): mbulohen production assemblies të
/// inventarizuara nga katalogu dhe call-site-t ekzekutuese + DLR-patterns e
/// provuara; brendësia e paketave jashtë tyre NUK pretendohet e skanuar. Për
/// kodin e GJENERUAR: `[GeneratedCode]` me provenance TË LEXUESHME (tool/version
/// të dekoduar nga blob-i) kalon në VALIDIMIN e ledger-it EKZAKT të
/// provenance-ës; `[CompilerGenerated]` i vetëm = ZERO trust — dy mekanizma TË
/// NDARË, kurrë një autorizim i përbashkët. Zero pretendim "universal" ose
/// "bypass-free". ENG-006 mbetet OPEN deri në review + Human Gate.
/// </summary>
public sealed class DynamicRuntimeAccessBoundaryTests
{
    private const string Fixtures = "Idata.Platform.ArchitectureTests.DynamicRuntimeAccessFixtures";

    private static string SelfAssemblyPath()
        => Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");

    /// <summary>
    /// Correction C1 (Blocker C): ÇDO akses te një production target kalon nga
    /// API-ja autoritative <see cref="PlatformProjectCatalog.VerifiedFrozenTargetPath(PlatformProjectCatalog.ProjectEntry, PlatformProjectCatalog.CatalogGeneration)"/>
    /// — PARA skanimit (path-i merret i verifikuar) DHE PAS tij (ndryshimi
    /// gjatë scan-it nuk kalon pa u kapur).
    /// </summary>
    private static IReadOnlyList<DynamicRuntimeAccessScanner.Finding> ScanProduction(out int scanned)
        => ScanProduction(PlatformProjectCatalog.CurrentGeneration(), out scanned);

    private static IReadOnlyList<DynamicRuntimeAccessScanner.Finding> ScanProduction(
        PlatformProjectCatalog.CatalogGeneration generation, out int scanned)
    {
        var findings = new List<DynamicRuntimeAccessScanner.Finding>();
        scanned = 0;
        foreach (var entry in generation.Projects)
        {
            // C2: kufiri I VETËM — verifikim para+pas, e njëjta gjeneratë.
            findings.AddRange(PlatformProjectCatalog.ConsumeVerifiedFrozenTarget(
                entry, generation, path => DynamicRuntimeAccessScanner.Scan(path)));
            scanned++;
        }

        return findings;
    }

    private static IReadOnlyList<DynamicRuntimeAccessScanner.Finding> ScanOwnerSurfaces(
        PlatformProjectCatalog.CatalogGeneration generation)
    {
        var findings = new List<DynamicRuntimeAccessScanner.Finding>();
        foreach (var entry in generation.Projects.Where(PlatformProjectCatalog.IsStoreOwner))
        {
            findings.AddRange(PlatformProjectCatalog.ConsumeVerifiedFrozenTarget(
                entry, generation, path => DynamicRuntimeAccessScanner.ScanOwnerPublicSurface(path)));
        }

        return findings;
    }

    // ═══════════════ Politika mbi production ═══════════════

    /// <summary>
    /// Guard-i qendror: universi derivohet nga katalogu autoritativ dhe çdo akses
    /// dinamik i kodit TONË duhet të ketë hyrje EKZAKTE manifesti — i cili është
    /// BOSH, pra kodi ynë duhet të jetë i pastër.
    /// </summary>
    [Fact]
    public void Cdo_akses_dinamik_i_kodit_tone_eshte_ose_i_manifestuar_ose_gjetje()
    {
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        Assert.NotEmpty(catalog);

        var findings = ScanProduction(out var scanned);
        Assert.Equal(catalog.Length, scanned);

        var problems = DynamicRuntimeAccessManifest.Validate(
            DynamicRuntimeAccessManifest.Production, catalog, findings);
        Assert.True(problems.Count == 0, string.Join(" · ", problems));
    }

    /// <summary>§5/§6.18 — manifesti production është dhe mbetet BOSH.</summary>
    [Fact]
    public void Manifesti_production_eshte_bosh()
        => Assert.Empty(DynamicRuntimeAccessManifest.Production);

    /// <summary>
    /// Correction C1 (Blocker A): mbetjet generated në production duhet të
    /// përputhen EKZAKT — dy-drejtimësh — me provenance ledger-in: çdo gjetje
    /// unike konsumon saktësisht një hyrje, çdo hyrje konsumohet, zero
    /// dangerous/service-location, tool/version i DEKODUAR nga metadata për
    /// secilën. Zero assertion i llojit `StartsWith("generated-")` i vetëm.
    /// </summary>
    [Fact]
    public void Mbetjet_generated_ne_production_perputhen_ekzakt_me_ledger_in()
    {
        var findings = ScanProduction(out _);
        Assert.DoesNotContain(findings, DynamicRuntimeAccessScanner.IsNeverAllowable);

        var generated = findings
            .Where(finding => finding.Kind.StartsWith("generated-", StringComparison.Ordinal))
            .DistinctBy(finding => string.Join(
                "|", finding.Assembly, finding.DeclaringType, finding.Member,
                finding.Kind, finding.Detail, finding.GeneratedTool, finding.GeneratedToolVersion))
            .ToArray();
        Assert.NotEmpty(generated);
        Assert.All(generated, finding =>
        {
            Assert.False(string.IsNullOrWhiteSpace(finding.GeneratedTool));
            Assert.False(string.IsNullOrWhiteSpace(finding.GeneratedToolVersion));
            Assert.DoesNotContain(finding.Kind, DynamicRuntimeAccessManifest.AlwaysBlockingGeneratedKinds);
        });

        // Pariteti i plotë dy-drejtimësh (uncovered + dead + blocking) → zero problem.
        var problems = DynamicRuntimeAccessManifest.ValidateGeneratedResiduals(
            DynamicRuntimeAccessManifest.GeneratedResidualLedger, findings);
        Assert.True(problems.Count == 0, string.Join(" · ", problems));

        // Seti aktual == seti i ledger-it, EKSPLICIT (jo vetëm përmes validatorit).
        var actualKeys = generated.Select(finding =>
        {
            var normalized = DynamicRuntimeAccessManifest.TryNormalizeGeneratedTypeName(
                finding.DeclaringType, out var candidate) ? candidate : finding.DeclaringType;
            return string.Join("|", finding.Assembly, normalized, finding.Member,
                finding.Kind, finding.Detail, finding.GeneratedTool, finding.GeneratedToolVersion);
        }).Order(StringComparer.Ordinal).ToArray();
        var ledgerKeys = DynamicRuntimeAccessManifest.GeneratedResidualLedger.Select(entry =>
            string.Join("|", entry.AssemblyName, entry.NormalizedDeclaringType, entry.Member,
                entry.Kind, entry.Detail, entry.Tool, entry.ToolVersion))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(ledgerKeys, actualKeys);
        Assert.Equal(DynamicRuntimeAccessManifest.GeneratedResidualLedger.Length, generated.Length);

        // Production s'ka ASNJË gjetje jo-generated të kodit tonë.
        Assert.DoesNotContain(findings, finding =>
            !finding.Kind.StartsWith("generated-", StringComparison.Ordinal));
    }

    /// <summary>§5 — asnjë owner nuk eksporton sipërfaqe publike reflection.</summary>
    [Fact]
    public void Owner_assemblies_nuk_ekspozojne_siperfaqe_reflection()
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();
        Assert.Equal(2, generation.Projects.Count(PlatformProjectCatalog.IsStoreOwner));

        var findings = ScanOwnerSurfaces(generation);
        Assert.True(findings.Count == 0,
            string.Join(" · ", findings.Select(f => $"{f.Kind}:{f.DeclaringType}.{f.Member}({f.Detail})")));
    }

    // ═══════════════ §6 — fixtures RED me locator të plotë ═══════════════

    [Theory]
    [InlineData("RuntimeBuiltTypeNameFixture", "dynamic-dangerous", "System.Type.GetType")]
    [InlineData("ContextPropertyProbeFixture", "dynamic-dangerous", "System.Type.GetProperty")]
    [InlineData("ContextPropertyProbeFixture", "dynamic-dangerous", "System.Reflection.PropertyInfo.GetValue")]
    [InlineData("AssemblyLoadCreateInstanceFixture", "dynamic-dangerous", "System.Reflection.Assembly.Load")]
    [InlineData("AssemblyLoadCreateInstanceFixture", "dynamic-dangerous", "System.Reflection.Assembly.GetType")]
    [InlineData("AssemblyLoadCreateInstanceFixture", "dynamic-dangerous", "System.Activator.CreateInstance")]
    [InlineData("MethodInvokeFixture", "dynamic-dangerous", "System.Type.GetMethod")]
    [InlineData("MethodInvokeFixture", "dynamic-dangerous", "System.Reflection.MethodBase.Invoke")]
    [InlineData("InvokeMemberFixture", "dynamic-dangerous", "System.Type.InvokeMember")]
    [InlineData("PropertyGetSetFixture", "dynamic-dangerous", "System.Reflection.PropertyInfo.GetValue")]
    [InlineData("PropertyGetSetFixture", "dynamic-dangerous", "System.Reflection.PropertyInfo.SetValue")]
    [InlineData("FieldGetSetFixture", "dynamic-dangerous", "System.Reflection.FieldInfo.GetValue")]
    [InlineData("FieldGetSetFixture", "dynamic-dangerous", "System.Reflection.FieldInfo.SetValue")]
    [InlineData("MakeGenericTypeFixture", "dynamic-dangerous", "System.Type.MakeGenericType")]
    [InlineData("MakeGenericMethodFixture", "dynamic-dangerous", "System.Reflection.MethodInfo.MakeGenericMethod")]
    [InlineData("DynamicInvokeFixture", "dynamic-dangerous", "System.Delegate.DynamicInvoke")]
    [InlineData("CreateDelegateFixture", "dynamic-dangerous", "System.Reflection.MethodInfo.CreateDelegate")]
    [InlineData("DynamicDispatchFixture", "dynamic-dangerous", "Microsoft.CSharp.RuntimeBinder.Binder.InvokeMember")]
    [InlineData("DynamicDispatchFixture", "dynamic-dangerous", "System.Runtime.CompilerServices.CallSite`1.Create")]
    [InlineData("DynamicDispatchFixture", "dynamic-unknown-member", "System.Runtime.CompilerServices.CallSite`1.Target")]
    [InlineData("ServiceLocatorRuntimeTypeFixture", "dynamic-service-location", "System.IServiceProvider.GetService(System.Type)")]
    [InlineData("ServiceLocatorRequiredRuntimeTypeFixture", "dynamic-service-location", "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService(System.Type)")]
    [InlineData("AsyncReflectionFixture", "dynamic-dangerous", "System.Type.GetProperty")]
    [InlineData("AsyncReflectionFixture", "dynamic-dangerous", "System.Reflection.PropertyInfo.GetValue")]
    public void Fixtures_RED_kapen_me_locator_te_plote(string member, string kind, string detail)
    {
        var findings = DynamicRuntimeAccessScanner.Scan(SelfAssemblyPath());
        var finding = Assert.Single(findings, candidate =>
            candidate.DeclaringType == Fixtures
            && candidate.Member == member
            && candidate.Kind == kind
            && candidate.Detail == detail);

        Assert.Equal("Idata.Platform.ArchitectureTests", finding.Assembly);
        Assert.False(string.IsNullOrWhiteSpace(finding.Reason));
        Assert.DoesNotContain('<', finding.DeclaringType);
    }

    /// <summary>
    /// §6.12 — atribuimi i një metode `async` shkon te METODA BURIMORE, jo te
    /// `MoveNext` pa kontekst.
    /// </summary>
    [Fact]
    public void Atribuimi_async_shkon_te_metoda_burimore()
    {
        var findings = DynamicRuntimeAccessScanner.Scan(SelfAssemblyPath());
        Assert.Contains(findings, finding =>
            finding.DeclaringType == Fixtures && finding.Member == "AsyncReflectionFixture");
        Assert.DoesNotContain(findings, finding =>
            finding.Member == "MoveNext" && finding.DeclaringType.StartsWith(Fixtures, StringComparison.Ordinal));
    }

    /// <summary>§6.13 — assembly jo-PE = fail-closed, kurrë PASS i heshtur.</summary>
    [Fact]
    public void Assembly_i_palexueshem_eshte_fail_closed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eng006-not-pe-{Guid.NewGuid():N}.bin");
        File.WriteAllText(path, "jo PE");
        try
        {
            var single = Assert.Single(DynamicRuntimeAccessScanner.Scan(path));
            Assert.Equal("unreadable-assembly", single.Kind);
            Assert.True(DynamicRuntimeAccessScanner.IsNeverAllowable(single));

            Assert.Equal("unreadable-assembly",
                Assert.Single(DynamicRuntimeAccessScanner.ScanOwnerPublicSurface(path)).Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>§6.17 — escape hatch publik i owner-it kapet.</summary>
    [Fact]
    public void Escape_hatch_publik_i_owner_it_kapet()
    {
        var findings = DynamicRuntimeAccessScanner.ScanOwnerPublicSurface(SelfAssemblyPath());
        var hatch = typeof(OwnerReflectionEscapeHatchFixtures).FullName;

        foreach (var (member, detail) in ((string Member, string Detail)[])
        [
            ("ResolveType", "System.Type"),
            ("FindMethod", "System.Reflection.MethodInfo"),
            ("Locator", "System.IServiceProvider"),
        ])
        {
            Assert.Contains(findings, finding =>
                finding.Kind == "owner-public-reflection-surface"
                && finding.DeclaringType == hatch && finding.Member == member && finding.Detail == detail);
        }

        // GREEN 16 — API i tipizuar nuk raportohet.
        Assert.DoesNotContain(findings, finding =>
            finding.DeclaringType == typeof(TypedMediatedOwnerApiFixture).FullName);
    }

    // ═══════════════ §6 — GREEN ═══════════════

    [Theory]
    [InlineData("SafeTypedMetadataFixture")]
    [InlineData("SyntheticReflectionHomonymsFixture")]
    [InlineData("TypedMediatedApiFixture")]
    public void Fixtures_GREEN_nuk_prodhojne_gjetje(string member)
    {
        var findings = DynamicRuntimeAccessScanner.Scan(SelfAssemblyPath());
        Assert.DoesNotContain(findings, finding =>
            finding.DeclaringType == Fixtures && finding.Member == member);
    }

    /// <summary>§6.14 — homonimet sintetike kanë FullName tjetër → zero gjetje.</summary>
    [Fact]
    public void Homonimet_sintetike_nuk_jane_ne_familjet_e_monitoruara()
    {
        foreach (var name in (string[])
        [
            "Idata.Platform.ArchitectureTests.SyntheticReflection+Type",
            "Idata.Platform.ArchitectureTests.SyntheticReflection+Activator",
        ])
        {
            Assert.False(DynamicRuntimeAccessTaxonomy.IsMonitoredType(name));
        }

        var findings = DynamicRuntimeAccessScanner.Scan(SelfAssemblyPath());
        Assert.DoesNotContain(findings, finding =>
            finding.DeclaringType.StartsWith(
                "Idata.Platform.ArchitectureTests.SyntheticReflection", StringComparison.Ordinal));
    }

    /// <summary>
    /// §5 — `object.GetType()` NUK ndalohet globalisht (do të ishte
    /// false-positive masiv); zinxhiri kapet te hapi i dytë real.
    /// </summary>
    [Fact]
    public void Object_GetType_nuk_ndalohet_globalisht()
    {
        Assert.False(DynamicRuntimeAccessTaxonomy.IsMonitoredType("System.Object"));
        Assert.True(DynamicRuntimeAccessTaxonomy.IsSafeExact("System.Type", "GetTypeFromHandle"));
        Assert.True(DynamicRuntimeAccessTaxonomy.IsSafeExact("System.Reflection.MethodBase", "GetMethodFromHandle"));
    }

    // ═══════════════ Meta-teste: çdo degë është MBAJTËSE ═══════════════

    [Theory]
    [InlineData("System.Type", "RuntimeBuiltTypeNameFixture", "System.Type.GetType")]
    [InlineData("System.Reflection.Assembly", "AssemblyLoadCreateInstanceFixture", "System.Reflection.Assembly.Load")]
    [InlineData("System.Activator", "AssemblyLoadCreateInstanceFixture", "System.Activator.CreateInstance")]
    [InlineData("System.Reflection.PropertyInfo", "PropertyGetSetFixture", "System.Reflection.PropertyInfo.GetValue")]
    [InlineData("System.Reflection.FieldInfo", "FieldGetSetFixture", "System.Reflection.FieldInfo.GetValue")]
    [InlineData("System.Delegate", "DynamicInvokeFixture", "System.Delegate.DynamicInvoke")]
    [InlineData("Microsoft.CSharp.RuntimeBinder.Binder", "DynamicDispatchFixture", "Microsoft.CSharp.RuntimeBinder.Binder.InvokeMember")]
    [InlineData("System.IServiceProvider", "ServiceLocatorRuntimeTypeFixture", "System.IServiceProvider.GetService(System.Type)")]
    public void Heqja_e_familjes_se_monitoruar_e_zhduk_zbulimin(string monitoredType, string member, string detail)
    {
        bool Detected(DynamicRuntimeAccessScanner.AccessTaxonomy? taxonomy)
            => DynamicRuntimeAccessScanner.Scan(SelfAssemblyPath(), taxonomy)
                .Any(finding => finding.Member == member && finding.Detail == detail);

        Assert.True(Detected(null), $"pritej zbulim: {member}/{detail}");

        var reduced = DynamicRuntimeAccessTaxonomy.MonitoredTypeFullNames
            .Where(name => name != monitoredType).ToHashSet(StringComparer.Ordinal);
        Assert.False(
            Detected(new DynamicRuntimeAccessScanner.AccessTaxonomy(
                reduced,
                DynamicRuntimeAccessTaxonomy.DangerousMemberNames,
                DynamicRuntimeAccessTaxonomy.SafeExactMembers)),
            $"heqja e familjes NUK e ndryshoi zbulimin (jo-diskriminues): {monitoredType}");
    }

    /// <summary>
    /// Rregulli UNKNOWN është mbajtës: `CallSite`1.Target` NUK është në listën e
    /// DANGEROUS, prandaj kapet SEPSE tipi prind është i monitoruar. Heqja e
    /// SAFE-listës e kthen `GetTypeFromHandle` në gjetje — provë se safe-lista
    /// është vendim eksplicit, jo mungesë mbulimi.
    /// </summary>
    [Fact]
    public void Rregullat_unknown_dhe_safe_jane_mbajtese()
    {
        Assert.DoesNotContain("Target", DynamicRuntimeAccessTaxonomy.DangerousMemberNames);
        var findings = DynamicRuntimeAccessScanner.Scan(SelfAssemblyPath());
        Assert.Contains(findings, finding =>
            finding.Kind == "dynamic-unknown-member"
            && finding.Detail == "System.Runtime.CompilerServices.CallSite`1.Target");

        var withoutSafe = DynamicRuntimeAccessScanner.Scan(
            SelfAssemblyPath(),
            new DynamicRuntimeAccessScanner.AccessTaxonomy(
                DynamicRuntimeAccessTaxonomy.MonitoredTypeFullNames,
                DynamicRuntimeAccessTaxonomy.DangerousMemberNames,
                new HashSet<(string, string)>()));
        Assert.Contains(withoutSafe, finding =>
            finding.Detail == "System.Type.GetTypeFromHandle");
    }

    /// <summary>
    /// Validatori i manifestit është fail-closed: hyrje e vdekur, e dyfishtë, me
    /// wildcard, me fushë bosh, sink i pambuluar dhe gjetje e pa-atribuueshme
    /// raportohen të gjitha.
    /// </summary>
    [Fact]
    public void Validatori_i_manifestit_eshte_fail_closed()
    {
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        var project = catalog.First(entry => PlatformProjectCatalog.IsStoreOwner(entry));
        var entry = new AllowedDynamicAccess(
            project.CanonicalPath, project.AssemblyName, "Some.Type", "SomeMember",
            "arsye sintetike", "Tech/Platform Owner", "evidencë sintetike");
        var finding = new DynamicRuntimeAccessScanner.Finding(
            project.AssemblyName, "dynamic-dangerous", "Some.Type", "SomeMember", "detaj", "arsye");

        Assert.Empty(DynamicRuntimeAccessManifest.Validate([entry], catalog, [finding], []));
        Assert.Contains(DynamicRuntimeAccessManifest.Validate([entry], catalog, [], []),
            problem => problem.StartsWith("dead-entry:", StringComparison.Ordinal));
        Assert.Contains(DynamicRuntimeAccessManifest.Validate([entry, entry], catalog, [finding], []),
            problem => problem.StartsWith("duplicate-entry:", StringComparison.Ordinal));
        Assert.Contains(DynamicRuntimeAccessManifest.Validate([entry with { Member = "*" }], catalog, [], []),
            problem => problem.StartsWith("wildcard-entry:", StringComparison.Ordinal));
        Assert.Contains(DynamicRuntimeAccessManifest.Validate([entry with { Reason = "  " }], catalog, [finding], []),
            problem => problem.StartsWith("empty-field:", StringComparison.Ordinal));
        Assert.Contains(DynamicRuntimeAccessManifest.Validate([], catalog, [finding], []),
            problem => problem.StartsWith("uncovered-dynamic-access:", StringComparison.Ordinal));
        Assert.Contains(
            DynamicRuntimeAccessManifest.Validate([entry], catalog,
                [finding, finding with { Kind = "unattributed-compiler-generated" }], []),
            problem => problem.StartsWith("unallowable-finding:", StringComparison.Ordinal));
        Assert.Contains(
            DynamicRuntimeAccessManifest.Validate(
                [entry with { CanonicalProjectPath = "src/platform/Nope/Nope.csproj" }], catalog, [finding], []),
            problem => problem.StartsWith("unknown-project:", StringComparison.Ordinal));
    }

    /// <summary>
    /// ENG-006 është i NDARË nga `IdentityStoreIlScanner`: PoC-i historik i
    /// reflektimit dinamik — që skaneri statik i tipeve NUK e kap — tani KAPET
    /// nga guard-i ENG-006 te call-site-i i tij.
    /// </summary>
    [Fact]
    public void PoC_i_reflektimit_dinamik_tani_kapet_nga_ENG006()
    {
        // Skaneri statik i tipeve nuk sheh asnjë referencë tipi te PoC-i.
        var identityFindings = IdentityStoreIlScanner.Scan(SelfAssemblyPath(), isStoreOwner: false);
        Assert.DoesNotContain(identityFindings, finding =>
            finding.Detail.Contains("RuntimeBuiltTypeName", StringComparison.Ordinal));

        // ENG-006 e kap te CALL-SITE-i, pavarësisht se emri ndërtohet në runtime.
        var findings = DynamicRuntimeAccessScanner.Scan(SelfAssemblyPath());
        Assert.Contains(findings, finding =>
            finding.DeclaringType == Fixtures
            && finding.Member == "RuntimeBuiltTypeNameFixture"
            && finding.Detail == "System.Type.GetType");
    }

    // ═══════ C1/A — format e kodit tonë: asnjë trust nga kompajleri ═══════

    /// <summary>
    /// Lambda-t (non-capturing, capturing, LINQ) dhe local function-et e KODIT
    /// TONË mbeten BLOCKING: `[CompilerGenerated]` i emetuar nga kompajleri nuk
    /// jep trust dhe atribuimi i paprovueshëm është fail-closed.
    /// </summary>
    [Fact]
    public void Format_e_kompajlerit_ne_kodin_tone_mbeten_blocking()
    {
        var findings = DynamicRuntimeAccessScanner.Scan(SelfAssemblyPath());
        var lambdaFindings = findings.Where(finding =>
            finding.Detail == "dynamic-dangerous:System.Type.GetType"
            && finding.DeclaringType.StartsWith(Fixtures, StringComparison.Ordinal)).ToArray();

        // 1. non-capturing lambda → klasa `+<>c`.
        Assert.Contains(lambdaFindings, finding =>
            finding.DeclaringType == $"{Fixtures}+<>c"
            && finding.Member.Contains("NonCapturingLambdaFixture", StringComparison.Ordinal));

        // 2. capturing lambda → `+<>c__DisplayClass…`.
        Assert.Contains(lambdaFindings, finding =>
            finding.DeclaringType.StartsWith($"{Fixtures}+<>c__DisplayClass", StringComparison.Ordinal)
            && finding.Member.Contains("CapturingLambdaFixture", StringComparison.Ordinal));

        // 4. lambda brenda LINQ (capturing) → `+<>c__DisplayClass…`.
        Assert.Contains(lambdaFindings, finding =>
            finding.DeclaringType.StartsWith($"{Fixtures}+<>c__DisplayClass", StringComparison.Ordinal)
            && finding.Member.Contains("LinqLambdaFixture", StringComparison.Ordinal));

        // 3. local function → metodë `<M>g__…|…` mbi vetë tipin.
        Assert.Contains(findings, finding =>
            finding.DeclaringType == Fixtures
            && finding.Member.StartsWith("<LocalFunctionFixture>g__", StringComparison.Ordinal)
            && finding.Detail == "dynamic-dangerous:System.Type.GetType");

        // TË GJITHA janë fail-closed (kurrë të lejueshme) dhe asnjëra `generated-*`.
        var all = lambdaFindings.Concat(findings.Where(finding =>
            finding.Member.StartsWith("<LocalFunctionFixture>g__", StringComparison.Ordinal))).ToArray();
        Assert.NotEmpty(all);
        Assert.All(all, finding =>
        {
            Assert.Equal("unattributed-compiler-generated", finding.Kind);
            Assert.True(DynamicRuntimeAccessScanner.IsNeverAllowable(finding));
        });
    }

    /// <summary>
    /// 5–8: klasat manuale me atribute dhe nested-i i pashënuar — zero trust:
    /// `[CompilerGenerated]` i vetëm → kind i thjeshtë; nested nën parent
    /// generated → kind i thjeshtë (asnjë trashëgim); `[GeneratedCode]` → kind
    /// `generated-*` me tool/version TË DEKODUAR, që pastaj bllokohet nga ledger-i.
    /// </summary>
    [Fact]
    public void Klasat_manuale_me_atribute_nuk_veteperjashtohen()
    {
        var findings = DynamicRuntimeAccessScanner.Scan(SelfAssemblyPath());

        // 5. [CompilerGenerated] i vetëm → kind i thjeshtë, JO generated.
        var compilerGenerated = Assert.Single(findings, finding =>
            finding.DeclaringType == typeof(ManualCompilerGeneratedFixture).FullName);
        Assert.Equal("dynamic-dangerous", compilerGenerated.Kind);
        Assert.Equal("", compilerGenerated.GeneratedTool);

        // 8. nested i pashënuar nën parent me [GeneratedCode] → zero trashëgim.
        var nested = Assert.Single(findings, finding =>
            finding.DeclaringType == "Idata.Platform.ArchitectureTests.GeneratedParentFixture+UnmarkedChild");
        Assert.Equal("dynamic-dangerous", nested.Kind);
        Assert.Equal("", nested.GeneratedTool);

        // 6. [GeneratedCode("UnknownTool","9.9")] → generated-* me tool të dekoduar…
        var unknownTool = Assert.Single(findings, finding =>
            finding.DeclaringType == typeof(ManualUnknownToolFixture).FullName);
        Assert.Equal("generated-dynamic-dangerous", unknownTool.Kind);
        Assert.Equal("UnknownTool", unknownTool.GeneratedTool);
        Assert.Equal("9.9", unknownTool.GeneratedToolVersion);

        // …9. dhe dangerous mbetet GJITHMONË blocking, pavarësisht ledger-it.
        Assert.Contains(
            DynamicRuntimeAccessManifest.ValidateGeneratedResiduals(
                DynamicRuntimeAccessManifest.GeneratedResidualLedger, [unknownTool]),
            problem => problem.StartsWith("generated-blocking:generated-dynamic-dangerous:", StringComparison.Ordinal));

        // 7+9+10+11. tool/version REAL i generatorit — as ai s'vetë-autorizon:
        var real = findings.Where(finding =>
            finding.DeclaringType == typeof(ManualRealToolFixture).FullName).ToArray();
        var dangerous = Assert.Single(real, finding => finding.Member == "ProbeDangerous");
        Assert.Equal("generated-dynamic-dangerous", dangerous.Kind);
        var serviceLocation = Assert.Single(real, finding => finding.Member == "ProbeServiceLocation");
        Assert.Equal("generated-dynamic-service-location", serviceLocation.Kind);
        var unknownMember = Assert.Single(real, finding => finding.Member == "ProbeUnknown");
        Assert.Equal("generated-dynamic-unknown-member", unknownMember.Kind);
        Assert.Equal("Mediator.SourceGenerator", unknownMember.GeneratedTool);

        var problems = DynamicRuntimeAccessManifest.ValidateGeneratedResiduals(
            DynamicRuntimeAccessManifest.GeneratedResidualLedger, real);
        Assert.Contains(problems, problem =>
            problem.StartsWith("generated-blocking:generated-dynamic-dangerous:", StringComparison.Ordinal));
        Assert.Contains(problems, problem =>
            problem.StartsWith("generated-blocking:generated-dynamic-service-location:", StringComparison.Ordinal));
        Assert.Contains(problems, problem =>
            problem.StartsWith("uncovered-generated-residual:generated-dynamic-unknown-member:", StringComparison.Ordinal));
    }

    // ═══════ C1/A — meta-testet e normalizimit të kufizuar ═══════

    private const string OpenApiMarkerPrefix =
        "Microsoft.AspNetCore.OpenApi.Generated.<OpenApiXmlCommentSupport_generated>";

    /// <summary>
    /// Forma EKZAKTE normalizohet; prefiksi/suffix-i mbeten byte-identikë.
    /// Gjatësia e run-it është ajo e MATUR nga metadata reale
    /// (<see cref="DynamicRuntimeAccessManifest.OpenApiHashRunLength"/> = 65) —
    /// deviation e deklaruar nga supozimi 64 i review-t.
    /// </summary>
    [Fact]
    public void Normalizimi_pranon_vetem_formen_ekzakte_te_matur()
    {
        var hash = new string('a', DynamicRuntimeAccessManifest.OpenApiHashRunLength);
        var raw = $"{OpenApiMarkerPrefix}{hash}__DocumentationCommentIdHelper";
        Assert.True(DynamicRuntimeAccessManifest.TryNormalizeGeneratedTypeName(raw, out var normalized));
        Assert.Equal(
            $"{OpenApiMarkerPrefix}{DynamicRuntimeAccessManifest.GeneratedHashPlaceholder}__DocumentationCommentIdHelper",
            normalized);
        Assert.StartsWith(OpenApiMarkerPrefix, normalized, StringComparison.Ordinal);
        Assert.EndsWith("__DocumentationCommentIdHelper", normalized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(66)]
    public void Normalizimi_refuzon_gjatesi_te_gabuar(int hexLength)
    {
        var raw = $"{OpenApiMarkerPrefix}{new string('b', hexLength)}__Suffix";
        Assert.False(DynamicRuntimeAccessManifest.TryNormalizeGeneratedTypeName(raw, out var untouched));
        Assert.Equal(raw, untouched);
    }

    [Fact]
    public void Normalizimi_refuzon_devijimet_e_formes()
    {
        var hash = new string('c', DynamicRuntimeAccessManifest.OpenApiHashRunLength);

        // karakter jo-hex brenda run-it
        var nonHex = $"{OpenApiMarkerPrefix}{new string('c', 32)}g{new string('c', DynamicRuntimeAccessManifest.OpenApiHashRunLength - 33)}__Suffix";
        Assert.False(DynamicRuntimeAccessManifest.TryNormalizeGeneratedTypeName(nonHex, out var untouched));
        Assert.Equal(nonHex, untouched);

        // dy hash-runs (i dyti në suffix)
        var twoRuns = $"{OpenApiMarkerPrefix}{hash}__Suffix{new string('d', DynamicRuntimeAccessManifest.OpenApiHashRunLength)}";
        Assert.False(DynamicRuntimeAccessManifest.TryNormalizeGeneratedTypeName(twoRuns, out _));

        // suffix bosh / separator që mungon
        Assert.False(DynamicRuntimeAccessManifest.TryNormalizeGeneratedTypeName(
            $"{OpenApiMarkerPrefix}{hash}__", out _));
        Assert.False(DynamicRuntimeAccessManifest.TryNormalizeGeneratedTypeName(
            $"{OpenApiMarkerPrefix}{hash}_Suffix", out _));

        // hash-run pa marker-in OpenAPI → emri NUK preket kurrë
        var elsewhere = $"Some.Namespace.T{hash}__Whatever";  // hash-run PA marker
        Assert.False(DynamicRuntimeAccessManifest.TryNormalizeGeneratedTypeName(elsewhere, out var same));
        Assert.Equal(elsewhere, same);
    }

    /// <summary>Dy tipe REALE me hash të ndryshëm → i njëjti identitet = fail-closed.</summary>
    [Fact]
    public void Kolizioni_i_normalizimit_eshte_fail_closed()
    {
        var ledgerEntry = DynamicRuntimeAccessManifest.GeneratedResidualLedger
            .First(entry => entry.Member == "TransformAsync");
        DynamicRuntimeAccessScanner.Finding Fake(char digit) => new(
            ledgerEntry.AssemblyName, ledgerEntry.Kind,
            $"{OpenApiMarkerPrefix}{new string(digit, DynamicRuntimeAccessManifest.OpenApiHashRunLength)}__XmlCommentOperationTransformer",
            ledgerEntry.Member, ledgerEntry.Detail, "sintetike", ledgerEntry.Tool, ledgerEntry.ToolVersion);

        var problems = DynamicRuntimeAccessManifest.ValidateGeneratedResiduals(
            [ledgerEntry], [Fake('1'), Fake('2')]);
        Assert.Contains(problems, problem =>
            problem.StartsWith("normalization-collision:", StringComparison.Ordinal));
    }

    /// <summary>Ledger bosh me gjetje ekzistuese / hyrje e vdekur / duplikat / kind i palejuar → fail.</summary>
    [Fact]
    public void Ledger_i_provenances_eshte_fail_closed_ne_te_dyja_drejtimet()
    {
        var entry = DynamicRuntimeAccessManifest.GeneratedResidualLedger[0];
        var finding = new DynamicRuntimeAccessScanner.Finding(
            entry.AssemblyName, entry.Kind, entry.NormalizedDeclaringType, entry.Member,
            entry.Detail, "sintetike", entry.Tool, entry.ToolVersion);

        Assert.Empty(DynamicRuntimeAccessManifest.ValidateGeneratedResiduals([entry], [finding]));
        Assert.Contains(DynamicRuntimeAccessManifest.ValidateGeneratedResiduals([], [finding]),
            problem => problem.StartsWith("uncovered-generated-residual:", StringComparison.Ordinal));
        Assert.Contains(DynamicRuntimeAccessManifest.ValidateGeneratedResiduals([entry], []),
            problem => problem.StartsWith("dead-generated-entry:", StringComparison.Ordinal));
        Assert.Contains(DynamicRuntimeAccessManifest.ValidateGeneratedResiduals([entry, entry], [finding]),
            problem => problem.StartsWith("duplicate-ledger-entry:", StringComparison.Ordinal));
        Assert.Contains(
            DynamicRuntimeAccessManifest.ValidateGeneratedResiduals(
                [entry with { Kind = "generated-dynamic-dangerous" }], []),
            problem => problem.StartsWith("invalid-ledger-kind:", StringComparison.Ordinal));
        Assert.Contains(
            DynamicRuntimeAccessManifest.ValidateGeneratedResiduals(
                [entry with { Tool = " " }], [finding]),
            problem => problem.StartsWith("empty-ledger-field:", StringComparison.Ordinal));
        Assert.Contains(
            DynamicRuntimeAccessManifest.ValidateGeneratedResiduals(
                [entry with { Detail = "System.*" }], [finding]),
            problem => problem.StartsWith("wildcard-ledger-field:", StringComparison.Ordinal));

        // Tool/version i ndryshëm nga i dekoduari → unmatched → blocking.
        Assert.Contains(
            DynamicRuntimeAccessManifest.ValidateGeneratedResiduals(
                [entry], [finding with { GeneratedToolVersion = "0.0.0.0" }]),
            problem => problem.StartsWith("uncovered-generated-residual:", StringComparison.Ordinal));
    }

    /// <summary>
    /// Çdo hyrje ledger-i lidhet MEKANIKISHT me një paketë të PIN-uar në
    /// lockfile-t realë: tool-i i takon paketës (emër i thjeshtë == packageId
    /// ose fillon me packageId + '.') dhe major.minor i versionit të dekoduar
    /// përputhet me një version të zgjidhur të asaj pakete.
    /// </summary>
    [Fact]
    public void Cdo_hyrje_ledger_i_lidhet_me_paketen_e_pinuar()
    {
        var pinned = DynamicRuntimeAccessManifest.PinnedPackageVersions();
        static string MajorMinor(string version)
        {
            var parts = version.Split('.');
            return $"{parts[0]}.{parts[1]}";
        }

        Assert.All(DynamicRuntimeAccessManifest.GeneratedResidualLedger, entry =>
        {
            Assert.True(pinned.TryGetValue(entry.PackageId, out var resolvedVersions),
                $"Paketa '{entry.PackageId}' nuk është e pin-uar në asnjë lockfile.");

            var simpleToolName = entry.Tool.Split(',')[0].Trim();
            Assert.True(
                string.Equals(simpleToolName, entry.PackageId, StringComparison.OrdinalIgnoreCase)
                || simpleToolName.StartsWith(entry.PackageId + ".", StringComparison.OrdinalIgnoreCase),
                $"Tool-i '{simpleToolName}' nuk i takon paketës '{entry.PackageId}'.");

            Assert.Contains(MajorMinor(entry.ToolVersion), resolvedVersions!.Select(MajorMinor));
        });
    }

    // ═══════ C1/B — owner public FIELDS ═══════

    [Theory]
    [InlineData("LeakedType", "System.Type")]
    [InlineData("LeakedProvider", "System.IServiceProvider")]
    [InlineData("LeakedDelegates", "System.Delegate")]
    [InlineData("LeakedFields", "System.Reflection.FieldInfo")]
    public void Fushat_publike_qe_dorezojne_reflection_kapen(string member, string detail)
    {
        var findings = DynamicRuntimeAccessScanner.ScanOwnerPublicSurface(SelfAssemblyPath());
        Assert.Contains(findings, finding =>
            finding.Kind == "owner-public-reflection-surface"
            && finding.DeclaringType == typeof(OwnerReflectionFieldEscapeFixtures).FullName
            && finding.Member == member
            && finding.Detail == detail);
    }

    /// <summary>Inverse GREEN — fushat skalar/të tipizuara të sigurta s'raportohen.</summary>
    [Fact]
    public void Fushat_e_sigurta_nuk_raportohen()
    {
        var findings = DynamicRuntimeAccessScanner.ScanOwnerPublicSurface(SelfAssemblyPath());
        foreach (var safeMember in (string[])["SafeCount", "SafeLabel", "SafeId"])
        {
            Assert.DoesNotContain(findings, finding =>
                finding.DeclaringType == typeof(OwnerReflectionFieldEscapeFixtures).FullName
                && finding.Member == safeMember);
        }
    }

    // ═══════ C1/C — konsumatorët ENG-006 përdorin fingerprint-in e ngrirë ═══════

    // ═══════ C2 (Blocker B) — degët fail-closed të metadata-ve të palexueshme ═══════

    /// <summary>
    /// Degët e C1 `unreadable-generated-code-attribute` dhe
    /// `unreadable-field-signature` (plus `unreadable-method-signature`,
    /// baseline më i vjetër i së njëjtës familje) tani kanë teste REALE mbi një
    /// PE SINTETIKE me blobs të keqformuar — fixture-i jo-PE mbulon vetëm
    /// `unreadable-assembly` dhe NUK citohet si evidencë për këto degë.
    /// </summary>
    [Fact]
    public void Bloget_e_keqformuara_te_metadata_ve_godasin_deget_fail_closed()
    {
        var malformedPath = SyntheticMetadataPe.Write(malformed: true);
        try
        {
            var scanFindings = DynamicRuntimeAccessScanner.Scan(malformedPath);
            var attribute = Assert.Single(scanFindings, finding =>
                finding.Kind == "unreadable-generated-code-attribute");
            Assert.Equal(SyntheticMetadataPe.HostTypeFullName, attribute.DeclaringType);
            Assert.True(DynamicRuntimeAccessScanner.IsNeverAllowable(attribute));

            var surfaceFindings = DynamicRuntimeAccessScanner.ScanOwnerPublicSurface(malformedPath);
            var field = Assert.Single(surfaceFindings, finding =>
                finding.Kind == "unreadable-field-signature");
            Assert.Equal(SyntheticMetadataPe.HostTypeFullName, field.DeclaringType);
            Assert.Equal("ProbeField", field.Member);
            Assert.True(DynamicRuntimeAccessScanner.IsNeverAllowable(field));

            var method = Assert.Single(surfaceFindings, finding =>
                finding.Kind == "unreadable-method-signature");
            Assert.Equal(SyntheticMetadataPe.HostTypeFullName, method.DeclaringType);
            Assert.Equal("ProbeMethod", method.Member);
            Assert.True(DynamicRuntimeAccessScanner.IsNeverAllowable(method));
        }
        finally
        {
            File.Delete(malformedPath);
        }
    }

    /// <summary>Metadata VALIDE ekuivalente → GREEN, zero false-positive.</summary>
    [Fact]
    public void Metadata_valide_ekuivalente_nuk_prodhon_asnje_gjetje_unreadable()
    {
        var validPath = SyntheticMetadataPe.Write(malformed: false);
        try
        {
            var scanFindings = DynamicRuntimeAccessScanner.Scan(validPath);
            Assert.DoesNotContain(scanFindings, finding =>
                finding.Kind.StartsWith("unreadable-", StringComparison.Ordinal));

            var surfaceFindings = DynamicRuntimeAccessScanner.ScanOwnerPublicSurface(validPath);
            Assert.DoesNotContain(surfaceFindings, finding =>
                finding.Kind.StartsWith("unreadable-", StringComparison.Ordinal));

            // Blob-i i vlefshëm dekodohet realisht (tool/version i lexuar).
            Assert.DoesNotContain(scanFindings, DynamicRuntimeAccessScanner.IsNeverAllowable);
        }
        finally
        {
            File.Delete(validPath);
        }
    }

    /// <summary>
    /// `ScanProduction` dhe owner-scan konsumojnë REALISHT
    /// `VerifiedFrozenTargetPath`: një gjeneratë me mtime të helmuar i bën të
    /// dy të dështojnë me `catalog-generation-drift` — para se të skanohet gjë.
    /// </summary>
    [Fact]
    public void Skanimet_ENG006_konsumojne_fingerprint_in_e_ngrire()
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();
        var victim = generation.Projects.Select(entry => entry.CanonicalPath)
            .Order(StringComparer.Ordinal).First();
        var ownerVictim = generation.Projects.First(PlatformProjectCatalog.IsStoreOwner).CanonicalPath;

        PlatformProjectCatalog.CatalogGeneration Poison(string canonicalPath)
        {
            var writes = generation.WriteTimeUtcByCanonicalPath
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            writes[canonicalPath] = DateTime.UnixEpoch;
            return generation with
            {
                WriteTimeUtcByCanonicalPath = writes.ToFrozenDictionary(StringComparer.Ordinal),
            };
        }

        var scanException = Assert.Throws<InvalidOperationException>(
            () => ScanProduction(Poison(victim), out _));
        Assert.Contains("catalog-generation-drift", scanException.Message, StringComparison.Ordinal);

        var ownerException = Assert.Throws<InvalidOperationException>(
            () => ScanOwnerSurfaces(Poison(ownerVictim)));
        Assert.Contains("catalog-generation-drift", ownerException.Message, StringComparison.Ordinal);
    }
}
