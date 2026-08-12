using System.Collections.Frozen;
using System.Collections.Immutable;
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

        var synthetic = generation with
        {
            Sha256ByCanonicalPath = poisonedSha.ToFrozenDictionary(StringComparer.Ordinal),
        };
        Assert.Equal([victim], PlatformProjectCatalog.VerifyGenerationFingerprints(synthetic));

        var poisonedMvid = generation.MvidByCanonicalPath
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        poisonedMvid[victim] = Guid.Empty;
        Assert.Equal(
            [victim],
            PlatformProjectCatalog.VerifyGenerationFingerprints(
                generation with { MvidByCanonicalPath = poisonedMvid.ToFrozenDictionary(StringComparer.Ordinal) }));

        // Dimensioni i TRETË: koha e shkrimit. Build-et janë DETERMINISTË, prandaj
        // një rindërtim i burimeve të pandryshuara jep bajte identikë — vetëm koha
        // e dëshmon ngjarjen e rindërtimit që §4 e ndalon.
        var poisonedWrite = generation.WriteTimeUtcByCanonicalPath
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        poisonedWrite[victim] = DateTime.UnixEpoch;
        Assert.Equal(
            [victim],
            PlatformProjectCatalog.VerifyGenerationFingerprints(
                generation with { WriteTimeUtcByCanonicalPath = poisonedWrite.ToFrozenDictionary(StringComparer.Ordinal) }));

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

    // ═══════ C1 (Blocker C) — kontrata VerifiedFrozenTargetPath ═══════

    private static PlatformProjectCatalog.CatalogGeneration PoisonComponent(
        PlatformProjectCatalog.CatalogGeneration generation, string victim, string component)
    {
        switch (component)
        {
            case "sha":
                var sha = generation.Sha256ByCanonicalPath
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                sha[victim] = new string('0', 64);
                return generation with { Sha256ByCanonicalPath = sha.ToFrozenDictionary(StringComparer.Ordinal) };
            case "mvid":
                var mvid = generation.MvidByCanonicalPath
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                mvid[victim] = Guid.Empty;
                return generation with { MvidByCanonicalPath = mvid.ToFrozenDictionary(StringComparer.Ordinal) };
            default:
                var write = generation.WriteTimeUtcByCanonicalPath
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                write[victim] = DateTime.UnixEpoch;
                return generation with { WriteTimeUtcByCanonicalPath = write.ToFrozenDictionary(StringComparer.Ordinal) };
        }
    }

    /// <summary>
    /// Kontrata tre-dimensionale, e provuar VEÇMAS për secilin dimension mbi
    /// gjenerata SINTETIKE (zero output real i prekur): devijim VETËM në SHA,
    /// VETËM në MVID ose VETËM në mtime → `catalog-generation-drift`;
    /// target identik → GREEN; target jashtë gjeneratës → refuzim anëtarësie.
    /// </summary>
    [Theory]
    [InlineData("sha")]
    [InlineData("mvid")]
    [InlineData("mtime")]
    public void VerifiedFrozenTargetPath_kap_devijimin_e_secilit_dimension(string component)
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();
        var victim = generation.Projects.OrderBy(entry => entry.CanonicalPath, StringComparer.Ordinal).First();

        // GREEN mbi gjeneratën reale.
        var path = PlatformProjectCatalog.VerifiedFrozenTargetPath(victim, generation);
        Assert.True(File.Exists(path));

        var poisoned = PoisonComponent(generation, victim.CanonicalPath, component);
        var exception = Assert.Throws<InvalidOperationException>(
            () => PlatformProjectCatalog.VerifiedFrozenTargetPath(victim, poisoned));
        Assert.Contains("catalog-generation-drift", exception.Message, StringComparison.Ordinal);

        // Projektet e tjera të gjeneratës së helmuar mbeten GREEN (saktësi kirurgjikale).
        var other = generation.Projects.OrderBy(entry => entry.CanonicalPath, StringComparer.Ordinal).Last();
        Assert.NotEqual(victim.CanonicalPath, other.CanonicalPath);
        _ = PlatformProjectCatalog.VerifiedFrozenTargetPath(other, poisoned);
    }

    [Fact]
    public void VerifiedFrozenTargetPath_refuzon_target_jashte_gjenerates()
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();
        var outsider = generation.Projects[0] with
        {
            CanonicalPath = "src/platform/Synthetic.Outside/Synthetic.Outside.csproj",
        };
        var exception = Assert.Throws<InvalidOperationException>(
            () => PlatformProjectCatalog.VerifiedFrozenTargetPath(outsider, generation));
        Assert.Contains("jashtë gjeneratës autoritative", exception.Message, StringComparison.Ordinal);

        // Edhe identitet i manipuluar me path kanonik REAL refuzohet (anëtarësia
        // është mbi HYRJEN e plotë, jo vetëm mbi çelësin).
        var tampered = generation.Projects[0] with { AssemblyName = "Tampered.Assembly" };
        Assert.Throws<InvalidOperationException>(
            () => PlatformProjectCatalog.VerifiedFrozenTargetPath(tampered, generation));
    }

    /// <summary>
    /// C2 (Blocker A): ÇDO konsumator i production target-eve thërret KUFIRIN E
    /// VETËM `ConsumeVerifiedFrozenTarget` — provë MEKANIKE mbi IL (token-a
    /// MethodDef të të njëjtit assembly). Vetë kufiri provohet veçmas se ka
    /// rendin verify-before → consumer → verify-after.
    /// </summary>
    [Theory]
    [InlineData("Idata.Platform.ArchitectureTests.PlatformProjectCatalog", "LoadVerified")]
    [InlineData("Idata.Platform.ArchitectureTests.DynamicRuntimeAccessBoundaryTests", "ScanProduction")]
    [InlineData("Idata.Platform.ArchitectureTests.DynamicRuntimeAccessBoundaryTests", "ScanOwnerSurfaces")]
    [InlineData("Idata.Platform.ArchitectureTests.UntypedDataAccessBoundaryTests", "ScanVerified")]
    [InlineData("Idata.Platform.ArchitectureTests.IdentityRoleSurfaceTests", "Zero_role_referenca_ne_metadata_te_asnje_assembly_production")]
    [InlineData("Idata.Platform.ArchitectureTests.DependencyDirectionTests", "PlatformOutputAuthority")]
    public void Konsumatoret_e_production_target_it_thirrin_kontraten_e_ngrire(string typeName, string methodName)
        => Assert.Contains("ConsumeVerifiedFrozenTarget", CalledMethodDefinitionNames(typeName, methodName));

    /// <summary>
    /// C2 — RENDI EKZAKT i kufirit, i provuar mbi IL-in e vetë
    /// `ConsumeVerifiedFrozenTarget&lt;T&gt;`: dy thirrje REALE të verifikuesit
    /// primitiv dhe SAKTËSISHT një `Invoke` i konsumatorit MES tyre —
    /// jo thjesht prani emrash në një set.
    /// </summary>
    [Fact]
    public void Kufiri_ka_rendin_ekzakt_verify_consumer_verify()
    {
        var sequence = CalledSequenceOf(
            "Idata.Platform.ArchitectureTests.PlatformProjectCatalog", "ConsumeVerifiedFrozenTarget",
            name => name is "VerifiedFrozenTargetPath" or "Invoke")
            .ToArray();

        Assert.Equal(["VerifiedFrozenTargetPath", "Invoke", "VerifiedFrozenTargetPath"], sequence);
    }

    /// <summary>
    /// Sekuenca E RENDITUR e thirrjeve të filtruara brenda IL-it të një metode
    /// (mbi TË GJITHA overload-et me atë emër që kanë trup).
    /// </summary>
    private static IEnumerable<string> CalledSequenceOf(
        string declaringTypeFullName, string methodName, Func<string, bool> filter)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");
        using var stream = File.OpenRead(path);
        using var portableExecutable = new PEReader(stream);
        var reader = portableExecutable.GetMetadataReader();
        var sequence = new List<string>();
        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0
                || reader.GetString(method.Name) != methodName
                || UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, method.GetDeclaringType())
                    != declaringTypeFullName)
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
                if (handle.Kind == HandleKind.MethodSpecification)
                {
                    handle = reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
                }

                string? name = handle.Kind switch
                {
                    HandleKind.MethodDefinition => reader.GetString(
                        reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
                    HandleKind.MemberReference => reader.GetString(
                        reader.GetMemberReference((MemberReferenceHandle)handle).Name),
                    _ => null,
                };
                if (name is not null && filter(name))
                {
                    sequence.Add(name);
                }
            }
        }

        Assert.NotEmpty(sequence);
        return sequence;
    }

    /// <summary>Emrat e MethodDef-eve të thirrur nga një metodë e caktuar (IL walk).</summary>
    private static IReadOnlyCollection<string> CalledMethodDefinitionNames(
        string declaringTypeFullName, string methodName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");
        using var stream = File.OpenRead(path);
        using var portableExecutable = new PEReader(stream);
        var reader = portableExecutable.GetMetadataReader();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var matched = false;

        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0
                || reader.GetString(method.Name) != methodName
                || UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, method.GetDeclaringType())
                    != declaringTypeFullName)
            {
                continue;
            }

            matched = true;
            var il = portableExecutable.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            foreach (var instruction in IlOperandTable.Walk(il, out _))
            {
                if (instruction.MetadataToken == 0)
                {
                    continue;
                }

                var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(instruction.MetadataToken);
                if (handle.Kind == HandleKind.MethodSpecification)
                {
                    // Thirrjet ndaj metodave GJENERIKE (p.sh. kufiri
                    // `ConsumeVerifiedFrozenTarget<T>`) enkodohen si MethodSpec.
                    handle = reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
                }

                if (handle.Kind == HandleKind.MethodDefinition)
                {
                    names.Add(reader.GetString(
                        reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name));
                }
            }
        }

        Assert.True(matched, $"Metoda {declaringTypeFullName}.{methodName} nuk u gjet.");
        return names;
    }

    /// <summary>
    /// C1 (Shtesa E) — provë DETERMINISTIKE (jo wall-clock): fingerprint-et
    /// llogariten VETËM në API-n e verifikimit të katalogut, KURRË brenda
    /// loop-it per-finding të skanerit — skaneri ENG-006 nuk thërret asnjë
    /// API hashimi/mtime. Çifti before/after për artifact jetohet te
    /// konsumatorët (`ScanProduction`/`ScanOwnerSurfaces`), snapshot-i i ngrirë
    /// ripërdoret (Assert.Same në testet e gjeneratës) dhe post-scan drift-i
    /// vazhdon të kapet (testet e helmimit).
    /// </summary>
    [Fact]
    public void Skaneri_ENG006_nuk_llogarit_fingerprint_vete()
    {
        var called = CalledMemberNames("Idata.Platform.ArchitectureTests.DynamicRuntimeAccessScanner");
        foreach (var forbidden in (string[])["HashData", "GetLastWriteTimeUtc", "GetLastWriteTime"])
        {
            Assert.DoesNotContain(forbidden, called);
        }
    }

    /// <summary>
    /// C2 — prova BEHAVIORALE e post-check-ut mbi një artefakt SINTETIK nën
    /// rrënjën e repo-s (bin e testeve; zero prekje e output-eve reale):
    /// (a) konsumator që e NDRYSHON artefaktin gjatë konsumimit → kufiri hedh
    /// `catalog-generation-drift` PAS konsumimit dhe rezultati nuk kthehet;
    /// (b) gjeneratë e helmuar → konsumatori NUK thirret fare (pre-check).
    /// </summary>
    [Fact]
    public void Kufiri_kap_ndryshimin_gjate_konsumimit_dhe_bllokon_para_tij()
    {
        var syntheticPath = SyntheticMetadataPe.Write(malformed: false, AppContext.BaseDirectory);
        try
        {
            var canonical = Path.GetRelativePath(PlatformProjectCatalog.RepoRoot(), syntheticPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var entry = new PlatformProjectCatalog.ProjectEntry(
                "tests/platform/Synthetic.Probe/Synthetic.Probe.csproj",
                "Synthetic.Probe", "SynMalformed", canonical);
            var generation = PlatformProjectCatalog.SyntheticGenerationFor(entry);

            // GREEN: konsumim i pandryshuar kthen rezultatin pas post-check-ut.
            Assert.Equal(42, PlatformProjectCatalog.ConsumeVerifiedFrozenTarget(entry, generation, _ => 42));

            // (a) mutacion GJATË konsumimit → drift PAS konsumimit, zero kthim.
            var consumerRan = false;
            var exception = Assert.Throws<InvalidOperationException>(() =>
                PlatformProjectCatalog.ConsumeVerifiedFrozenTarget(entry, generation, path =>
                {
                    consumerRan = true;
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
                    return 0;
                }));
            Assert.True(consumerRan);
            Assert.Contains("catalog-generation-drift", exception.Message, StringComparison.Ordinal);

            // (b) gjeneratë e helmuar → pre-check ndal PARA konsumatorit.
            var freshGeneration = PlatformProjectCatalog.SyntheticGenerationFor(entry);
            var poisoned = PoisonComponent(freshGeneration, entry.CanonicalPath, "mtime");
            var invoked = false;
            Assert.Throws<InvalidOperationException>(() =>
                PlatformProjectCatalog.ConsumeVerifiedFrozenTarget(entry, poisoned, _ =>
                {
                    invoked = true;
                    return 0;
                }));
            Assert.False(invoked);
        }
        finally
        {
            File.Delete(syntheticPath);
        }
    }

    /// <summary>
    /// C2 — guard i DERIVUAR i plotësisë (jo listë e hardcoded konsumatorësh):
    /// primitivet e konsumit të production target-eve lejohen VETËM te kufiri
    /// qendror/verifikuesi primitiv. Ecet mbi IL-in e GJITHË assembly-t të
    /// testeve; çdo metodë tjetër që (a) thërret `ReadMvid`, (b) thërret
    /// `Assembly.Load`, ose (c) ndërton path targeti
    /// (`get_CanonicalTargetPath` + `Path.Combine`) = shkelje → RED.
    /// Një konsumator i RI direkt në të ardhmen kapet automatikisht.
    /// (`DynamicRuntimeAccessFixtures` përjashtohet VETËM për `Assembly.Load`:
    /// është fixture RED e vetë skanerit ENG-006, jo konsumator targeti.)
    /// </summary>
    [Fact]
    public void Vetem_kufiri_qendror_prek_primitivet_e_konsumit()
    {
        const string catalogType = "Idata.Platform.ArchitectureTests.PlatformProjectCatalog";
        var allowedAssemblyLoad = new[] { catalogType, "Idata.Platform.ArchitectureTests.DynamicRuntimeAccessFixtures" };

        var violations = new List<string>();
        var path = Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");
        using var stream = File.OpenRead(path);
        using var portableExecutable = new PEReader(stream);
        var reader = portableExecutable.GetMetadataReader();

        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            var declaringType = UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, method.GetDeclaringType());
            var topLevelType = declaringType.Split('+')[0];
            var methodName = reader.GetString(method.Name);

            var called = new List<string>();
            var il = portableExecutable.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            foreach (var instruction in IlOperandTable.Walk(il, out _))
            {
                if (instruction.MetadataToken == 0)
                {
                    continue;
                }

                var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(instruction.MetadataToken);
                if (handle.Kind == HandleKind.MethodSpecification)
                {
                    handle = reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
                }

                switch (handle.Kind)
                {
                    case HandleKind.MethodDefinition:
                        called.Add(reader.GetString(
                            reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name));
                        break;
                    case HandleKind.MemberReference:
                        called.Add(reader.GetString(
                            reader.GetMemberReference((MemberReferenceHandle)handle).Name));
                        break;
                }
            }

            if (called.Contains("ReadMvid") && topLevelType != catalogType)
            {
                violations.Add($"ReadMvid: {declaringType}.{methodName}");
            }

            if (called.Contains("Load") && !allowedAssemblyLoad.Contains(topLevelType))
            {
                violations.Add($"Assembly.Load: {declaringType}.{methodName}");
            }

            if (called.Contains("get_CanonicalTargetPath") && called.Contains("Combine")
                && topLevelType != catalogType)
            {
                violations.Add($"target-path-build: {declaringType}.{methodName}");
            }
        }

        Assert.True(violations.Count == 0, string.Join(" · ", violations.Order(StringComparer.Ordinal)));
    }

    // ═══════ C1 (Hardening D) — deep immutability e gjeneratës ═══════

    /// <summary>
    /// Asnjë strukturë e gjeneratës — as e jashtme, as NESTED — nuk mund të
    /// mutohet a downcast-ohet për të helmuar `CurrentGeneration`: hartat/setet
    /// janë `Frozen*` (mutimi përmes interface-ve hedh `NotSupportedException`),
    /// sekuencat janë `ImmutableArray` (provuar nga tipi i komponentëve).
    /// </summary>
    [Fact]
    public void Gjenerata_eshte_deep_immutable()
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();

        // Hartat kryesore: Frozen + mutimi hedh.
        Assert.IsAssignableFrom<FrozenDictionary<string, string>>(generation.Sha256ByCanonicalPath);
        Assert.IsAssignableFrom<FrozenDictionary<string, Guid>>(generation.MvidByCanonicalPath);
        Assert.IsAssignableFrom<FrozenDictionary<string, DateTime>>(generation.WriteTimeUtcByCanonicalPath);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)generation.Sha256ByCanonicalPath).Add("x", "y"));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, Guid>)generation.MvidByCanonicalPath).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, DateTime>)generation.WriteTimeUtcByCanonicalPath)
                .Remove(generation.Projects[0].CanonicalPath));

        // Sekuencat: ImmutableArray si tip komponenti (zero backing array i publikuar).
        foreach (var (property, expected) in new (string, Type)[]
        {
            ("Projects", typeof(System.Collections.Immutable.ImmutableArray<PlatformProjectCatalog.ProjectEntry>)),
            ("ProjectReferencesByCanonicalPath",
                typeof(FrozenDictionary<string, System.Collections.Immutable.ImmutableArray<string>>)),
            ("ResolvedReferencesByCanonicalPath",
                typeof(FrozenDictionary<string, System.Collections.Immutable.ImmutableArray<PlatformProjectCatalog.ResolvedReference>>)),
            ("FrameworkAssetsByCanonicalPath", typeof(FrozenDictionary<string, FrozenSet<string>>)),
        })
        {
            Assert.Equal(expected,
                typeof(PlatformProjectCatalog.CatalogGeneration).GetProperty(property)!.PropertyType);
        }

        // Nested: FrozenSet për asetet framework; PackageAssetAuthority me
        // harta/sete të ngrira edhe në vlerat e brendshme.
        Assert.All(generation.FrameworkAssetsByCanonicalPath.Values, set =>
        {
            Assert.IsAssignableFrom<FrozenSet<string>>(set);
            Assert.Throws<NotSupportedException>(() => ((ICollection<string>)set).Add("x"));
        });
        Assert.All(generation.PackageAssetsByCanonicalPath.Values, authority =>
        {
            Assert.IsAssignableFrom<FrozenDictionary<(string, string), IReadOnlySet<string>>>(
                authority.AssetsByTuple);
            Assert.IsAssignableFrom<FrozenDictionary<string, (string, string)>>(authority.TupleByAsset);
            Assert.All(authority.AssetsByTuple.Values, assets =>
            {
                Assert.IsAssignableFrom<FrozenSet<string>>(assets);
                Assert.Throws<NotSupportedException>(() => ((ICollection<string>)assets).Add("x"));
            });
        });

        // Pas gjithë tentativave të dështuara: e njëjta gjeneratë, i njëjti state.
        var after = PlatformProjectCatalog.CurrentGeneration();
        Assert.Same(generation, after);
        Assert.Same(generation.Sha256ByCanonicalPath, after.Sha256ByCanonicalPath);
        Assert.Same(generation.PackageAssetsByCanonicalPath, after.PackageAssetsByCanonicalPath);
        Assert.Empty(PlatformProjectCatalog.VerifyGenerationFingerprints(generation));
    }
}
