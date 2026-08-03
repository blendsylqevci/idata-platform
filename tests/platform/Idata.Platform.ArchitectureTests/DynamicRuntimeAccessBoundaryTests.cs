using Xunit;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// ENG-006 — kufiri DENY-BY-DEFAULT i aksesit dinamik reflection/runtime
/// (Pre-B2 Increment 2, §5–§6).
///
/// Kufij të DEKLARUAR (zero overclaim): mbulohen production assemblies të
/// inventarizuara nga katalogu dhe call-site-t ekzekutuese + DLR-patterns e
/// provuara; brendësia e paketave jashtë tyre NUK pretendohet e skanuar; kodi i
/// GJENERUAR brenda assembly-ve klasifikohet VEÇMAS me metadata reale
/// (`GeneratedCode`/`CompilerGenerated`) sepse nuk e shkruajmë ne dhe nuk
/// ndryshohet dot pa prekur varësitë. Zero pretendim "universal" ose
/// "bypass-free". ENG-006 mbetet OPEN deri në review + Human Gate.
/// </summary>
public sealed class DynamicRuntimeAccessBoundaryTests
{
    private const string Fixtures = "Idata.Platform.ArchitectureTests.DynamicRuntimeAccessFixtures";

    private static string SelfAssemblyPath()
        => Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");

    private static string AbsoluteTargetPath(PlatformProjectCatalog.ProjectEntry entry)
        => Path.Combine(
            PlatformProjectCatalog.RepoRoot(),
            entry.CanonicalTargetPath.Replace('/', Path.DirectorySeparatorChar));

    private static IReadOnlyList<DynamicRuntimeAccessScanner.Finding> ScanProduction(out int scanned)
    {
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        var findings = new List<DynamicRuntimeAccessScanner.Finding>();
        scanned = 0;
        foreach (var entry in catalog)
        {
            findings.AddRange(DynamicRuntimeAccessScanner.Scan(AbsoluteTargetPath(entry)));
            scanned++;
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
    /// Kufi i DEKLARUAR: mbetja e vetme në production është kod i GJENERUAR
    /// (source-generator/kompajler), i identifikuar MEKANIKISHT nga metadata —
    /// jo nga një listë namespace-esh. Raportohet dhe numërohet, por nuk hyn në
    /// manifestin e kodit tonë.
    /// </summary>
    [Fact]
    public void Mbetja_ne_production_eshte_vetem_kod_i_gjeneruar()
    {
        var findings = ScanProduction(out _);
        Assert.All(findings, finding =>
            Assert.StartsWith("generated-", finding.Kind, StringComparison.Ordinal));
        Assert.DoesNotContain(findings, DynamicRuntimeAccessScanner.IsNeverAllowable);
    }

    /// <summary>§5 — asnjë owner nuk eksporton sipërfaqe publike reflection.</summary>
    [Fact]
    public void Owner_assemblies_nuk_ekspozojne_siperfaqe_reflection()
    {
        var owners = PlatformProjectCatalog.DiscoverProduction()
            .Where(PlatformProjectCatalog.IsStoreOwner).ToArray();
        Assert.Equal(2, owners.Length);

        foreach (var owner in owners)
        {
            var findings = DynamicRuntimeAccessScanner.ScanOwnerPublicSurface(AbsoluteTargetPath(owner));
            Assert.True(findings.Count == 0,
                $"{owner.CanonicalPath}: {string.Join(" · ", findings.Select(f => $"{f.Kind}:{f.DeclaringType}.{f.Member}({f.Detail})"))}");
        }
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

        Assert.Empty(DynamicRuntimeAccessManifest.Validate([entry], catalog, [finding]));
        Assert.Contains(DynamicRuntimeAccessManifest.Validate([entry], catalog, []),
            problem => problem.StartsWith("dead-entry:", StringComparison.Ordinal));
        Assert.Contains(DynamicRuntimeAccessManifest.Validate([entry, entry], catalog, [finding]),
            problem => problem.StartsWith("duplicate-entry:", StringComparison.Ordinal));
        Assert.Contains(DynamicRuntimeAccessManifest.Validate([entry with { Member = "*" }], catalog, []),
            problem => problem.StartsWith("wildcard-entry:", StringComparison.Ordinal));
        Assert.Contains(DynamicRuntimeAccessManifest.Validate([entry with { Reason = "  " }], catalog, [finding]),
            problem => problem.StartsWith("empty-field:", StringComparison.Ordinal));
        Assert.Contains(DynamicRuntimeAccessManifest.Validate([], catalog, [finding]),
            problem => problem.StartsWith("uncovered-dynamic-access:", StringComparison.Ordinal));
        Assert.Contains(
            DynamicRuntimeAccessManifest.Validate([entry], catalog,
                [finding, finding with { Kind = "unattributed-compiler-generated" }]),
            problem => problem.StartsWith("unallowable-finding:", StringComparison.Ordinal));
        Assert.Contains(
            DynamicRuntimeAccessManifest.Validate(
                [entry with { CanonicalProjectPath = "src/platform/Nope/Nope.csproj" }], catalog, [finding]),
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
}
