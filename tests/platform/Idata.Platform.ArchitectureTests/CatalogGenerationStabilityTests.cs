using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Idata.Platform.ArchitectureTests;

/// <summary>
/// ENG-008 — stabiliteti deterministik i harness-it arkitekturor (Pre-B2
/// Increment 2).
///
/// **ROOT CAUSE i provuar (jo i përsëritur):** `-t:ResolveReferences` ndërton
/// ProjectReference-at si pjesë normale e target-it, prandaj RINDËRTON output-in
/// production GJATË run-it. Katalogu ngrinte vetëm path-et, ndërsa MVID-i
/// lexohej më vonë nga disku; kështu gjenerata A e katalogut përzihej me
/// gjeneratën B të file-it dhe me gjeneratën A′ të assembly-t të ngarkuar në
/// default load context → dështim FALSE i varur nga rendi i ekzekutimit.
///
/// **Fix-i:** (a) çdo thirrje fëmijë MSBuild është READ-ONLY ndaj output-eve
/// (<see cref="PlatformProjectCatalog.ReadOnlyEvaluationArgument"/>), dhe
/// (b) identiteti, MVID-i, fingerprint-i SHA-256, adjacency-a e
/// ProjectReference-ve, referencat e zgjidhura dhe autoritetet package/framework
/// ngrihen në NJË gjeneratë të vetme, me verifikim fingerprint-i PARA dhe PAS
/// gjithë vlerësimit. Devijimi pas freeze-it = fail-closed i KONTROLLUAR.
///
/// **ENG-008 është kufi i BESUESHMËRISË/evidencës, JO kufi sigurie** dhe NUK
/// është bypass production i provuar.
/// </summary>
public sealed class CatalogGenerationStabilityTests
{
    private static string RepoRoot() => PlatformProjectCatalog.RepoRoot();

    private static string OwnerPath()
        => "src/platform/Idata.Platform.Persistence/Idata.Platform.Persistence.csproj";

    // ═══════════ Gjenerata e vetme, e pandryshueshme, pavarësisht rendit ═══════════

    /// <summary>Rendi A: katalog → referenca. E njëjta gjeneratë.</summary>
    [Fact]
    public void Katalog_pastaj_ResolveReferences_japin_te_njejten_gjenerate()
    {
        var before = PlatformProjectCatalog.CurrentGeneration();
        _ = PlatformProjectCatalog.EvaluateResolvedReferences(RepoRoot(), OwnerPath());
        var after = PlatformProjectCatalog.CurrentGeneration();

        Assert.Same(before, after);
        Assert.Equal(before.GenerationId, after.GenerationId);
        Assert.Equal(before.MvidByCanonicalPath, after.MvidByCanonicalPath);
    }

    /// <summary>Rendi B (i kundërt): referenca → katalog. E njëjta gjeneratë.</summary>
    [Fact]
    public void ResolveReferences_pastaj_katalog_japin_te_njejten_gjenerate()
    {
        _ = PlatformProjectCatalog.EvaluateResolvedReferences(RepoRoot(), OwnerPath());
        var first = PlatformProjectCatalog.CurrentGeneration();
        var catalog = PlatformProjectCatalog.DiscoverProduction();
        var second = PlatformProjectCatalog.CurrentGeneration();

        Assert.Same(first, second);
        Assert.Equal(catalog.Length, second.Projects.Length);
        Assert.Equal(
            catalog.Select(entry => entry.CanonicalPath).Order(StringComparer.Ordinal),
            second.Projects.Select(entry => entry.CanonicalPath).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Vlerësimi i një projekti të VETËM (skenari që më parë e prishte suitën)
    /// nuk e ndryshon asnjë output: fingerprint-et mbeten identikë me ata të
    /// ngrirë. Kjo është prova mekanike që evaluation-i është read-only.
    /// </summary>
    [Fact]
    public void Vleresimi_i_nje_projekti_te_vetem_nuk_prek_asnje_output()
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();
        Assert.Empty(PlatformProjectCatalog.VerifyGenerationFingerprints(generation));

        foreach (var entry in generation.Projects)
        {
            _ = PlatformProjectCatalog.EvaluateResolvedReferences(RepoRoot(), entry.CanonicalPath);
            _ = PlatformProjectCatalog.EvaluateProjectReferences(RepoRoot(), entry.CanonicalPath);
            _ = PlatformProjectCatalog.EvaluateFrameworkAssetAuthority(RepoRoot(), entry.CanonicalPath);
            _ = PlatformProjectCatalog.EvaluatePackageAssetAuthority(RepoRoot(), entry.CanonicalPath);
        }

        Assert.Empty(PlatformProjectCatalog.VerifyGenerationFingerprints(generation));
        Assert.Same(generation, PlatformProjectCatalog.CurrentGeneration());
    }

    /// <summary>Thirrës PARALELË marrin TË NJËJTIN objekt gjenerate.</summary>
    [Fact]
    public void Thirresit_paralele_marrin_te_njejten_gjenerate()
    {
        var observed = new PlatformProjectCatalog.CatalogGeneration[32];
        Parallel.For(0, observed.Length, index =>
        {
            observed[index] = PlatformProjectCatalog.CurrentGeneration();
            _ = PlatformProjectCatalog.DiscoverProduction();
        });

        var first = observed[0];
        Assert.All(observed, generation =>
        {
            Assert.Same(first, generation);
            Assert.Equal(first.GenerationId, generation.GenerationId);
        });
    }

    /// <summary>
    /// Permutime me SEED-E FIKSE të raportuara: rendi i thirrjeve nuk ndikon as
    /// gjeneratën as rezultatet. Zero varësi nga rendi i test-runner-it.
    /// </summary>
    [Theory]
    [InlineData(1337)]
    [InlineData(20260803)]
    [InlineData(42)]
    public void Permutimet_me_seed_fiks_japin_rezultate_identike(int seed)
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();
        var order = generation.Projects.Select(entry => entry.CanonicalPath).ToArray();
        var random = new Random(seed);
        for (var index = order.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (order[index], order[swap]) = (order[swap], order[index]);
        }

        foreach (var canonicalPath in order)
        {
            var references = PlatformProjectCatalog.EvaluateResolvedReferences(RepoRoot(), canonicalPath);
            Assert.Equal(
                generation.ResolvedReferencesByCanonicalPath[canonicalPath].Length, references.Length);
            Assert.Equal(
                generation.ProjectReferencesByCanonicalPath[canonicalPath],
                PlatformProjectCatalog.EvaluateProjectReferences(RepoRoot(), canonicalPath));
        }

        Assert.Same(generation, PlatformProjectCatalog.CurrentGeneration());
        Assert.Empty(PlatformProjectCatalog.VerifyGenerationFingerprints(generation));
    }

    // ═══════════ Fail-closed i KONTROLLUAR mbi devijim pas freeze-it ═══════════

    /// <summary>
    /// Detektori i devijimit është MBAJTËS: mbi një gjeneratë SINTETIKE me
    /// fingerprint të gabuar për një projekt të vetëm, verifikuesi e raporton
    /// SAKTËSISHT atë projekt. Zero output real nuk preket.
    /// </summary>
    [Fact]
    public void Devijimi_pas_freeze_it_raportohet_i_kontrolluar()
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();
        var victim = generation.Projects.Select(entry => entry.CanonicalPath)
            .Order(StringComparer.Ordinal).First();

        var poisonedSha = generation.Sha256ByCanonicalPath
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        poisonedSha[victim] = new string('0', 64);

        var synthetic = generation with { Sha256ByCanonicalPath = poisonedSha };
        Assert.Equal([victim], PlatformProjectCatalog.VerifyGenerationFingerprints(synthetic));

        var poisonedMvid = generation.MvidByCanonicalPath
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        poisonedMvid[victim] = Guid.Empty;
        Assert.Equal(
            [victim],
            PlatformProjectCatalog.VerifyGenerationFingerprints(generation with { MvidByCanonicalPath = poisonedMvid }));

        // Dimensioni i TRETË: koha e shkrimit. Build-et janë DETERMINISTË, prandaj
        // një rindërtim i burimeve të pandryshuara jep bajte identikë — vetëm koha
        // e dëshmon ngjarjen e rindërtimit që §4 e ndalon.
        var poisonedWrite = generation.WriteTimeUtcByCanonicalPath
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        poisonedWrite[victim] = DateTime.UnixEpoch;
        Assert.Equal(
            [victim],
            PlatformProjectCatalog.VerifyGenerationFingerprints(
                generation with { WriteTimeUtcByCanonicalPath = poisonedWrite }));

        // Gjenerata reale mbetet e shëndetshme.
        Assert.Empty(PlatformProjectCatalog.VerifyGenerationFingerprints(generation));
    }

    /// <summary>
    /// `LoadVerified` konsumon MVID-in e NGRIRË: një projekt jashtë gjeneratës
    /// autoritative refuzohet me diagnostikë të vetën, jo si "decoy".
    /// </summary>
    [Fact]
    public void LoadVerified_refuzon_projekt_jashte_gjenerates_autoritative()
    {
        var real = PlatformProjectCatalog.DiscoverProduction()[0];
        var outsider = real with { CanonicalPath = "src/platform/Synthetic.Outside/Synthetic.Outside.csproj" };

        var exception = Assert.Throws<InvalidOperationException>(
            () => PlatformProjectCatalog.LoadVerified(outsider));
        Assert.Contains("jashtë gjeneratës autoritative", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Kontrollet ekzistuese exact-path/MVID mbeten të plota: çdo assembly i
    /// ngarkuar përputhet me MVID-in e ngrirë të TargetPath-it të vet.
    /// </summary>
    [Fact]
    public void Identiteti_exact_path_dhe_MVID_mbetet_i_imponuar()
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();
        foreach (var entry in generation.Projects)
        {
            var loaded = PlatformProjectCatalog.LoadVerified(entry);
            Assert.Equal(generation.MvidByCanonicalPath[entry.CanonicalPath],
                loaded.ManifestModule.ModuleVersionId);
            Assert.Equal(entry.AssemblyName, loaded.GetName().Name);
        }
    }

    // ═══════════ Higjiena e fix-it: zero sleep, zero temp, read-only ═══════════

    /// <summary>
    /// Argumenti read-only është një konstante e VETME, e konsumuar nga të gjitha
    /// rrugët e thirrjes — jo e kopjuar nëpër call-site.
    /// </summary>
    [Fact]
    public void Argumenti_read_only_eshte_i_vetem_dhe_ekzakt()
        => Assert.Equal("-p:BuildProjectReferences=false", PlatformProjectCatalog.ReadOnlyEvaluationArgument);

    /// <summary>
    /// Prova MEKANIKE mbi metadata (jo mbi emrin e testit) se fix-i i ENG-008 nuk
    /// përdor as `Task.Delay`/`Thread.Sleep` (fshehje race-i me kohë) as
    /// direktori temp (izolim jashtë kontrollit): asnjë prej këtyre anëtarëve nuk
    /// thirret nga asnjë metodë e deklaruar në `PlatformProjectCatalog`.
    /// </summary>
    [Fact]
    public void Fix_i_ENG008_nuk_perdor_sleep_retry_apo_temp()
    {
        var forbidden = new[] { "Delay", "Sleep", "GetTempPath", "GetTempFileName" };
        var called = CalledMemberNames(typeof(PlatformProjectCatalog).FullName!);

        foreach (var member in forbidden)
        {
            Assert.DoesNotContain(member, called);
        }
    }

    /// <summary>
    /// Mbledh emrat e anëtarëve TË JASHTËM të thirrur nga metodat e deklaruara në
    /// një tip të caktuar, duke ecur mbi IL me tabelën e plotë të operand-eve.
    /// </summary>
    private static IReadOnlyCollection<string> CalledMemberNames(string declaringTypeFullName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");
        using var stream = File.OpenRead(path);
        using var portableExecutable = new PEReader(stream);
        var reader = portableExecutable.GetMetadataReader();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            var owner = UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, method.GetDeclaringType());
            if (!owner.StartsWith(declaringTypeFullName, StringComparison.Ordinal))
            {
                continue;
            }

            var il = portableExecutable.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            foreach (var instruction in IlOperandTable.Walk(il, out _))
            {
                if (instruction.MetadataToken == 0)
                {
                    continue;
                }

                var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(instruction.MetadataToken);
                if (handle.Kind == HandleKind.MemberReference)
                {
                    names.Add(reader.GetString(reader.GetMemberReference((MemberReferenceHandle)handle).Name));
                }
            }
        }

        Assert.NotEmpty(names);
        return names;
    }
}
