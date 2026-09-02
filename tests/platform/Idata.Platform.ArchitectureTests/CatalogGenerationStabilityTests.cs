using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
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
    public void Kufiri_kap_devijimin_e_secilit_dimension(string component)
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();
        var victim = generation.Projects.OrderBy(entry => entry.CanonicalPath, StringComparer.Ordinal).First();

        // GREEN mbi gjeneratën reale — leximi kalon nga operacioni catalog-owned (C4).
        Assert.NotEmpty(PlatformProjectCatalog.ReadVerifiedImage(victim, generation).Bytes);

        var poisoned = PoisonComponent(generation, victim.CanonicalPath, component);
        var exception = Assert.Throws<InvalidOperationException>(
            () => PlatformProjectCatalog.ReadVerifiedImage(victim, poisoned));
        Assert.Contains("catalog-generation-drift", exception.Message, StringComparison.Ordinal);

        // Projektet e tjera të gjeneratës së helmuar mbeten GREEN (saktësi kirurgjikale).
        var other = generation.Projects.OrderBy(entry => entry.CanonicalPath, StringComparer.Ordinal).Last();
        Assert.NotEqual(victim.CanonicalPath, other.CanonicalPath);
        Assert.NotEmpty(PlatformProjectCatalog.ReadVerifiedImage(other, poisoned).Bytes);
    }

    [Fact]
    public void Kufiri_refuzon_target_jashte_gjenerates()
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();
        var outsider = generation.Projects[0] with
        {
            CanonicalPath = "src/platform/Synthetic.Outside/Synthetic.Outside.csproj",
        };
        var exception = Assert.Throws<InvalidOperationException>(
            () => PlatformProjectCatalog.ReadVerifiedImage(outsider, generation));
        Assert.Contains("jashtë gjeneratës autoritative", exception.Message, StringComparison.Ordinal);

        // Edhe identitet i manipuluar me path kanonik REAL refuzohet (anëtarësia
        // është mbi HYRJEN e plotë, jo vetëm mbi çelësin).
        var tampered = generation.Projects[0] with { AssemblyName = "Tampered.Assembly" };
        Assert.Throws<InvalidOperationException>(
            () => PlatformProjectCatalog.ReadVerifiedImage(tampered, generation));
    }

    /// <summary>
    /// C4 (§4F): ÇDO konsumator i artefakteve production thërret VETËM një nga
    /// OPERACIONET E NGUSHTA catalog-owned (`ReadVerifiedImage`, `LoadVerified`,
    /// `MatchesVerifiedTarget`) — provë MEKANIKE mbi IL (token-a MethodDef të të
    /// njëjtit assembly). Vetë operacioni i leximit provohet veçmas se ka rendin
    /// verify-before → read → verify-after.
    /// </summary>
    [Theory]
    [InlineData("Idata.Platform.ArchitectureTests.PlatformProjectCatalog", "LoadVerified")]
    [InlineData("Idata.Platform.ArchitectureTests.DynamicRuntimeAccessBoundaryTests", "ScanProduction")]
    [InlineData("Idata.Platform.ArchitectureTests.DynamicRuntimeAccessBoundaryTests", "ScanOwnerSurfaces")]
    [InlineData("Idata.Platform.ArchitectureTests.UntypedDataAccessBoundaryTests", "ScanVerified")]
    [InlineData("Idata.Platform.ArchitectureTests.IdentityRoleSurfaceTests", "Zero_role_referenca_ne_metadata_te_asnje_assembly_production")]
    [InlineData("Idata.Platform.ArchitectureTests.DependencyDirectionTests", "MatchesAuthoritativeOutput")]
    public void Konsumatoret_e_production_target_it_thirrin_kontraten_e_ngrire(string typeName, string methodName)
        => Assert.Contains(
            CalledMethodDefinitionNames(typeName, methodName),
            // Konsumatorët e jashtëm thërrasin një nga operacionet e ngushta;
            // operacionet e vetë katalogut thërrasin verifikuesin primitiv.
            called => called is "ReadVerifiedImage" or "LoadVerified"
                or "MatchesVerifiedTarget" or "VerifiedFrozenTargetPath");

    /// <summary>
    /// C4 — RENDI EKZAKT i leximit, i provuar mbi IL-in e vetë
    /// `ReadVerifiedImage`: verify-before → `ReadAllBytes` → verify-after,
    /// jo thjesht prani emrash në një set.
    /// </summary>
    [Fact]
    public void Leximi_ka_rendin_ekzakt_verify_read_verify()
    {
        var sequence = CalledSequenceOf(
            "Idata.Platform.ArchitectureTests.PlatformProjectCatalog", "ReadVerifiedImage",
            name => name is "VerifiedFrozenTargetPath" or "ReadAllBytes")
            .ToArray();

        Assert.Equal(["VerifiedFrozenTargetPath", "ReadAllBytes", "VerifiedFrozenTargetPath"], sequence);
    }

    /// <summary>C4 — `LoadVerified` ka po ashtu verify-before → load → verify-after.</summary>
    [Fact]
    public void LoadVerified_ka_rendin_ekzakt_verify_load_verify()
    {
        var sequence = CalledSequenceOf(
            "Idata.Platform.ArchitectureTests.PlatformProjectCatalog", "LoadVerified",
            name => name is "VerifiedFrozenTargetPath" or "Load")
            .ToArray();

        Assert.Equal(["VerifiedFrozenTargetPath", "Load", "VerifiedFrozenTargetPath"], sequence);
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
                    // p.sh. helper-at gjenerikë) enkodohen si MethodSpec.
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
    /// C4 (§4C) — INTEGRITETI i leximit të verifikuar mbi një artefakt SINTETIK
    /// (zero prekje e output-eve reale): mutacion pas freeze-it, varianti ABA
    /// (identitet i jashtëm A, përmbajtje B) dhe gjeneratë e helmuar.
    /// </summary>
    [Fact]
    public void Leximi_i_verifikuar_kap_mutacionin_ABA_dhe_bllokon_para_tij()
    {
        var syntheticPath = SyntheticMetadataPe.Write(malformed: false, AppContext.BaseDirectory);
        var otherPath = SyntheticMetadataPe.Write(malformed: false, AppContext.BaseDirectory);
        try
        {
            var canonical = Path.GetRelativePath(PlatformProjectCatalog.RepoRoot(), syntheticPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var entry = new PlatformProjectCatalog.ProjectEntry(
                "tests/platform/Synthetic.Probe/Synthetic.Probe.csproj",
                "Synthetic.Probe", "SynMalformed", canonical);
            var generation = PlatformProjectCatalog.SyntheticGenerationFor(entry);

            // GREEN: leximi i pandryshuar kthen snapshot me identitetin e ngrirë.
            var image = PlatformProjectCatalog.ReadVerifiedImage(entry, generation);
            Assert.NotEmpty(image.Bytes);
            Assert.Equal(generation.Sha256ByCanonicalPath[entry.CanonicalPath], image.Sha256);
            Assert.Equal(generation.MvidByCanonicalPath[entry.CanonicalPath], image.Mvid);
            Assert.Equal(entry.AssemblyName, image.AssemblyName);

            // (a) ABA i VËRTETË (TOCTOU): verifikimet mbi file-in shohin identitetin
            // A, ndërsa reader-i kthen bajte B. Ushtrohet drejtpërdrejt mbi seam-in
            // e verifikimit të bajteve — dega është MBAJTËSE, jo defence-in-depth
            // i pa-provuar. (Të dy artefaktet janë ende TË NDRYSHËM këtu.)
            PlatformProjectCatalog.VerifyImageBytes(entry, generation, File.ReadAllBytes(syntheticPath));
            var toctou = Assert.Throws<InvalidOperationException>(
                () => PlatformProjectCatalog.VerifyImageBytes(entry, generation, File.ReadAllBytes(otherPath)));
            Assert.Contains("SHA/MVID", toctou.Message, StringComparison.Ordinal);

            // (b) MUTACION i artefaktit pas freeze-it → leximi refuzohet.
            File.SetLastWriteTimeUtc(syntheticPath, DateTime.UtcNow.AddMinutes(5));
            var mutated = Assert.Throws<InvalidOperationException>(
                () => PlatformProjectCatalog.ReadVerifiedImage(entry, generation));
            Assert.Contains("catalog-generation-drift", mutated.Message, StringComparison.Ordinal);

            // (c) Përmbajtja zëvendësohet me B dhe mtime-i kthehet te vlera A →
            // verifikimi ndaj gjeneratës mbetet fail-closed.
            var frozenWrite = generation.WriteTimeUtcByCanonicalPath[entry.CanonicalPath];
            File.WriteAllBytes(syntheticPath, File.ReadAllBytes(otherPath));
            File.SetLastWriteTimeUtc(syntheticPath, frozenWrite);
            var aba = Assert.Throws<InvalidOperationException>(
                () => PlatformProjectCatalog.ReadVerifiedImage(entry, generation));
            Assert.Contains("catalog-generation-drift", aba.Message, StringComparison.Ordinal);

            // (d) gjeneratë e helmuar → pre-check ndal PARA çdo leximi bajtesh.
            var freshGeneration = PlatformProjectCatalog.SyntheticGenerationFor(entry);
            var poisoned = PoisonComponent(freshGeneration, entry.CanonicalPath, "mtime");
            Assert.Throws<InvalidOperationException>(
                () => PlatformProjectCatalog.ReadVerifiedImage(entry, poisoned));
        }
        finally
        {
            File.Delete(syntheticPath);
            File.Delete(otherPath);
        }
    }

    /// <summary>
    /// C4 (§4A) — API-SHAPE: surface-i i katalogut nuk publikon më raw verified
    /// path, as kapacitet të hapur, as callback me path. Mbi C3 ky test do të
    /// kishte dalë RED, sepse ekzistonte
    /// `ConsumeVerifiedFrozenTarget&lt;T&gt;(…, Func&lt;string,T&gt;)`.
    /// </summary>
    [Fact]
    public void API_shape_i_katalogut_nuk_publikon_verified_path()
    {
        var catalog = typeof(PlatformProjectCatalog);
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        // (1) API-ja e vjetër me callback nuk ekziston më në asnjë formë.
        Assert.DoesNotContain(catalog.GetMethods(All), method =>
            method.Name.Contains("ConsumeVerifiedFrozenTarget", StringComparison.Ordinal));

        // (2) Verifikuesi primitiv i path-it mbetet PRIVATE.
        var primitive = catalog.GetMethod(
            "VerifiedFrozenTargetPath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(primitive);
        Assert.True(primitive!.IsPrivate);
        Assert.DoesNotContain(catalog.GetMethods(All), method =>
            method.Name == "VerifiedFrozenTargetPath" && !method.IsPrivate);

        var nonPrivate = catalog.GetMethods(All).Where(method => !method.IsPrivate).ToArray();

        // (3) Asnjë anëtar non-private nuk pranon delegate që merr verified path.
        Assert.DoesNotContain(nonPrivate, method => method.GetParameters().Any(parameter =>
            typeof(Delegate).IsAssignableFrom(parameter.ParameterType)
            && parameter.ParameterType.GetGenericArguments().Contains(typeof(string))));

        // (4) Asnjë anëtar non-private nuk kthen kapacitet të hapur mbi artefaktin.
        Assert.DoesNotContain(nonPrivate, method =>
            typeof(FileSystemInfo).IsAssignableFrom(method.ReturnType)
            || typeof(Stream).IsAssignableFrom(method.ReturnType)
            || typeof(Delegate).IsAssignableFrom(method.ReturnType));

        // (5) Snapshot-i është deep-immutable, pa byte[]/Stream/FileInfo/delegate.
        var imageType = typeof(PlatformProjectCatalog.VerifiedAssemblyImage);
        Assert.Equal(
            typeof(ImmutableArray<byte>),
            imageType.GetProperty(nameof(PlatformProjectCatalog.VerifiedAssemblyImage.Bytes))!.PropertyType);
        Assert.DoesNotContain(imageType.GetProperties(), property =>
            property.PropertyType == typeof(byte[])
            || typeof(FileSystemInfo).IsAssignableFrom(property.PropertyType)
            || typeof(Stream).IsAssignableFrom(property.PropertyType)
            || typeof(Delegate).IsAssignableFrom(property.PropertyType));

        // (6) Asnjë veti e snapshot-it nuk mbart path filesystem-i të artefaktit.
        var real = PlatformProjectCatalog.ReadVerifiedImage(PlatformProjectCatalog.DiscoverProduction()[0]);
        var repoRoot = PlatformProjectCatalog.RepoRoot();
        Assert.DoesNotContain(
            imageType.GetProperties().Where(property => property.PropertyType == typeof(string)),
            property => ((string?)property.GetValue(real))?.Contains(repoRoot, StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// C4 (§4B) — bypass-et e C3 regjistrohen si ROOT CAUSE, jo si blacklist:
    /// `T=object` + `FileInfo`, wrapper/koleksion nested, dhe capture në closure
    /// me side-effect. Të tria i shpëtonin çdo kontrolli runtime mbi `T`/rezultat,
    /// sepse path-i i ishte dhënë tashmë kodit arbitrar PARA post-check-ut. Prova
    /// e mbylljes nuk është kapja e tyre, por MUNGESA e API-së që ua jepte
    /// path-in — dhe heqja e kontrolleve runtime si kod i vdekur.
    /// </summary>
    [Fact]
    public void Bypass_et_e_C3_nuk_kane_me_surface_per_te_ekzistuar()
    {
        var catalog = typeof(PlatformProjectCatalog);
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        Assert.DoesNotContain(catalog.GetMethods(All).Where(method => !method.IsPrivate),
            method => method.GetParameters().Any(parameter =>
                parameter.ParameterType.IsGenericType
                && typeof(Delegate).IsAssignableFrom(parameter.ParameterType)
                && parameter.ParameterType.GetGenericArguments().FirstOrDefault() == typeof(string)));

        foreach (var removed in (string[])["EscapesVerifiedCapabilityType", "LeaksVerifiedPath"])
        {
            Assert.Null(catalog.GetMethod(removed, All));
        }
    }

    /// <summary>
    /// C4 (§4E) — PARITET i skanerëve: për TË NJËJTIN binar, rruga
    /// snapshot-based (production) jep rezultat SET-EQUAL me rrugën
    /// fixture-path. Provohet mbi assembly-në e testeve, ku të dyja rrugët janë
    /// legjitime dhe krahasimi është i mundur pa publikuar asnjë path production.
    /// </summary>
    [Fact]
    public void Skaneret_japin_rezultat_identik_mbi_snapshot_dhe_mbi_file()
    {
        var selfPath = Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");
        var bytes = File.ReadAllBytes(selfPath);
        var image = new PlatformProjectCatalog.VerifiedAssemblyImage(
            Path.GetFileNameWithoutExtension(selfPath),
            [.. bytes],
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            PlatformProjectCatalog.ReadMvidFromImage(bytes));

        Assert.Equal(
            DynamicRuntimeAccessScanner.ScanFixtureFile(selfPath).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal),
            DynamicRuntimeAccessScanner.Scan(image).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal));

        Assert.Equal(
            DynamicRuntimeAccessScanner.ScanOwnerPublicSurfaceFixtureFile(selfPath).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal),
            DynamicRuntimeAccessScanner.ScanOwnerPublicSurface(image).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal));

        Assert.Equal(
            UntypedDataAccessScanner.ScanFixtureFile(selfPath).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal),
            UntypedDataAccessScanner.Scan(image).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal));

        Assert.Equal(
            UntypedDataAccessScanner.ScanOwnerPublicSurfaceFixtureFile(selfPath).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal),
            UntypedDataAccessScanner.ScanOwnerPublicSurface(image).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal));

        Assert.Equal(
            IdentityStoreIlScanner.ScanFixtureFile(selfPath, isStoreOwner: false).Select(f => $"{f.Kind}|{f.Detail}").Order(StringComparer.Ordinal),
            IdentityStoreIlScanner.Scan(image, isStoreOwner: false).Select(f => $"{f.Kind}|{f.Detail}").Order(StringComparer.Ordinal));

        Assert.Equal(
            RoleMetadataScanner.ScanFixtureFile(selfPath).Select(f => $"{f.Table}|{f.Token}|{f.Detail}").Order(StringComparer.Ordinal),
            RoleMetadataScanner.Scan(image).Select(f => $"{f.Table}|{f.Token}|{f.Detail}").Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// C4 (§4F) — një artefakt PRODUCTION nuk mund të skanohet dot përmes
    /// overload-it fixture-path. Zinxhiri strukturor i provuar mekanikisht:
    ///  (1) katalogu nuk publikon asnjë path të verifikuar (API-shape test);
    ///  (2) `get_CanonicalTargetPath` lexohet VETËM brenda katalogut (guard i
    ///      primitiveve);
    ///  (3) prandaj **asnjë call-site i një overload-i fixture nuk ndodhet në një
    ///      metodë që lexon `CanonicalTargetPath`** — pra stringu që i kalohet
    ///      s'mund të jetë kurrë path-i i një artefakti production.
    /// Ky test imponon pikën (3); (1) dhe (2) imponohen nga testet e tyre.
    /// </summary>
    [Fact]
    public void Asnje_skanim_production_nuk_perdor_overload_in_fixture_path()
    {
        string[] fixtureOverloads = ["ScanFixtureFile", "ScanOwnerPublicSurfaceFixtureFile"];

        var violations = new List<string>();
        var path = Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");
        using var stream = File.OpenRead(path);
        using var portableExecutable = new PEReader(stream);
        var reader = portableExecutable.GetMetadataReader();
        var sawFixtureCall = false;

        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            var called = CalledNames(reader, portableExecutable, method);
            if (!called.Overlaps(fixtureOverloads))
            {
                continue;
            }

            sawFixtureCall = true;
            if (called.Contains("get_CanonicalTargetPath"))
            {
                violations.Add(
                    $"{UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, method.GetDeclaringType())}."
                    + reader.GetString(method.Name));
            }
        }

        Assert.True(sawFixtureCall, "asnjë call-site fixture nuk u gjet — guard-i do të ishte bosh");
        Assert.True(violations.Count == 0, string.Join(" · ", violations.Order(StringComparer.Ordinal)));
    }

    /// <summary>Emrat e thirrur nga një metodë (MethodDef/MemberRef/MethodSpec).</summary>
    private static HashSet<string> CalledNames(
        MetadataReader reader, PEReader portableExecutable, MethodDefinition method)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
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
                    names.Add(reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name));
                    break;
                case HandleKind.MemberReference:
                    names.Add(reader.GetString(reader.GetMemberReference((MemberReferenceHandle)handle).Name));
                    break;
            }
        }

        return names;
    }

    /// <summary>
    /// C4 (§4D) — snapshot-i është deep-immutable dhe determinist nën
    /// konkurrencë: 32 konsumatorë paralelë marrin të njëjtin identitet, të
    /// njëjtat bajte dhe të njëjtin rezultat skanimi.
    /// </summary>
    [Fact]
    public void Snapshot_i_verifikuar_eshte_immutable_dhe_determinist_nen_konkurrence()
    {
        var entry = PlatformProjectCatalog.DiscoverProduction()
            .Single(candidate => candidate.AssemblyName == "Idata.Platform.Persistence");
        var reference = PlatformProjectCatalog.ReadVerifiedImage(entry);

        // Tipi i bajteve është ImmutableArray<byte> — asnjë `byte[]` i publikuar.
        Assert.IsType<ImmutableArray<byte>>(reference.Bytes);
        Assert.Throws<NotSupportedException>(() => ((IList<byte>)reference.Bytes)[0] = 0xFF);

        var identities = new string[32];
        var scans = new int[32];
        Parallel.For(0, identities.Length, index =>
        {
            var image = PlatformProjectCatalog.ReadVerifiedImage(entry);
            identities[index] = $"{image.AssemblyName}|{image.Sha256}|{image.Mvid}|{image.Bytes.Length}";
            scans[index] = DynamicRuntimeAccessScanner.Scan(image).Count;
        });

        Assert.All(identities, identity => Assert.Equal(identities[0], identity));
        Assert.All(scans, count => Assert.Equal(scans[0], count));
        Assert.Equal(
            $"{reference.AssemblyName}|{reference.Sha256}|{reference.Mvid}|{reference.Bytes.Length}",
            identities[0]);
    }

    /// <summary>
    /// C3 (§2A) — familja e ngarkimit të assembly-ve, e DERIVUAR nga BCL-ja e
    /// target framework-ut: çdo metodë publike e `System.Reflection.Assembly`
    /// ose `System.Runtime.Loader.AssemblyLoadContext` që **kthen `Assembly`**
    /// dhe **pranon identity/path/bytes/stream**. Zero listë manuale emrash:
    /// `Load`, `LoadFrom`, `LoadFile`, `LoadFromAssemblyPath`, … dalin vetë nga
    /// sinjaturat, prandaj `LoadFrom` s'mund të shpëtojë si "emër tjetër".
    /// </summary>
    private static IReadOnlySet<string> DeriveAssemblyLoadingFamily()
    {
        var family = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in (Type[])[typeof(Assembly), typeof(System.Runtime.Loader.AssemblyLoadContext)])
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!typeof(Assembly).IsAssignableFrom(method.ReturnType))
                {
                    continue;
                }

                var acceptsIdentityOrBytes = method.GetParameters().Any(parameter =>
                    parameter.ParameterType == typeof(string)
                    || parameter.ParameterType == typeof(byte[])
                    || parameter.ParameterType == typeof(AssemblyName)
                    || typeof(Stream).IsAssignableFrom(parameter.ParameterType));
                if (acceptsIdentityOrBytes)
                {
                    family.Add(method.Name);
                }
            }
        }

        return family;
    }

    /// <summary>
    /// Familja e MONITORUAR duhet të jetë IDENTIKE me atë të derivuar nga BCL-ja.
    /// Një metodë e re relevante e BCL-së → RED derisa të klasifikohet.
    /// </summary>
    [Fact]
    public void Familja_e_assembly_loading_eshte_e_derivuar_nga_BCL()
    {
        var derived = DeriveAssemblyLoadingFamily();
        Assert.Contains("Load", derived);
        Assert.Contains("LoadFrom", derived);
        Assert.Contains("LoadFile", derived);
        Assert.Contains("LoadFromAssemblyPath", derived);
        Assert.Contains("LoadFromAssemblyName", derived);
        Assert.Contains("LoadFromStream", derived);

        // Metodat pa identity/path/bytes NUK hyjnë (p.sh. `GetExecutingAssembly`).
        Assert.DoesNotContain("GetExecutingAssembly", derived);

        Assert.Equal(
            derived.Order(StringComparer.Ordinal),
            MonitoredAssemblyLoadingMembers.Order(StringComparer.Ordinal));
    }

    /// <summary>Familja e monitoruar nga guard-i = ajo e derivuar (një burim i vetëm).</summary>
    private static readonly IReadOnlySet<string> MonitoredAssemblyLoadingMembers = DeriveAssemblyLoadingFamily();

    /// <summary>
    /// C3 (§2C) — përjashtimet janë mbi METODA EKZAKTE (tip + metodë), të
    /// shpjeguara dhe të provuara si të përdorura nga meta-testi i minimalitetit.
    /// </summary>
    private static readonly (string Type, string Method, string Reason)[] PrimitiveExceptions =
    [
        ("Idata.Platform.ArchitectureTests.DynamicRuntimeAccessFixtures",
         "AssemblyLoadCreateInstanceFixture",
         "Fixture RED e vetë skanerit ENG-006 (Assembly.Load→GetType→CreateInstance); nuk konsumon production target."),
    ];

    /// <summary>
    /// C3 (§2) — guard i DERIVUAR i plotësisë, tani SIGNATURE-BASED:
    ///  (a) çdo thirrje e familjes së ngarkimit të assembly-ve (e derivuar nga
    ///      BCL sipas return type + parametrave) jashtë katalogut = shkelje —
    ///      kap `LoadFrom`, `LoadFile`, `LoadFromAssemblyPath`, jo vetëm `Load`;
    ///  (b) çdo lexim i `get_CanonicalTargetPath` jashtë katalogut = shkelje,
    ///      PA kërkuar `Path.Combine` në të njëjtën metodë — kap `Path.Join`,
    ///      `GetFullPath`, konkatenim dhe helper-a indirektë;
    ///  (c) çdo thirrje e `ReadMvid` jashtë katalogut = shkelje.
    /// Përjashtimet janë mbi metoda EKZAKTE dhe provohen minimale.
    /// </summary>
    [Fact]
    public void Vetem_kufiri_qendror_prek_primitivet_e_konsumit()
    {
        var violations = ScanPrimitiveViolations(PrimitiveExceptions);
        Assert.True(violations.Count == 0, string.Join(" · ", violations.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// Minimaliteti i përjashtimeve: secili duhet të jetë REALISHT i nevojshëm
    /// (heqja e tij → shkelje e re) dhe asnjë përjashtim i vdekur nuk lejohet.
    /// </summary>
    [Fact]
    public void Perjashtimet_e_primitiveve_jane_minimale_dhe_te_perdorura()
    {
        foreach (var exception in PrimitiveExceptions)
        {
            var withoutThis = PrimitiveExceptions.Where(candidate => candidate != exception).ToArray();
            var violations = ScanPrimitiveViolations(withoutThis);
            Assert.True(
                violations.Any(violation =>
                    violation.Contains($"{exception.Type}.{exception.Method}", StringComparison.Ordinal)),
                $"Përjashtim i VDEKUR (asnjë shkelje reale pa të): {exception.Type}.{exception.Method}");
            Assert.False(string.IsNullOrWhiteSpace(exception.Reason));
        }

        // Përjashtimet janë mbi metoda ekzakte — asnjë wildcard/top-level type.
        Assert.All(PrimitiveExceptions, exception =>
        {
            Assert.DoesNotContain('*', exception.Method);
            Assert.NotEqual("*", exception.Method);
        });
    }

    private static IReadOnlyList<string> ScanPrimitiveViolations(
        (string Type, string Method, string Reason)[] exceptions)
    {
        const string catalogType = "Idata.Platform.ArchitectureTests.PlatformProjectCatalog";
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
            if (topLevelType == catalogType)
            {
                continue; // kufiri qendror + primitivet e tij
            }

            var isExcepted = exceptions.Any(exception =>
                exception.Type == topLevelType
                && (methodName == exception.Method
                    || methodName.Contains($"<{exception.Method}>", StringComparison.Ordinal)));

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

                string calledName;
                string? calledParent = null;
                switch (handle.Kind)
                {
                    case HandleKind.MethodDefinition:
                        var definition = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                        calledName = reader.GetString(definition.Name);
                        calledParent = UntypedDataAccessScanner.FullNameOfTypeDefinition(
                            reader, definition.GetDeclaringType());
                        break;
                    case HandleKind.MemberReference:
                        var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                        calledName = reader.GetString(member.Name);
                        calledParent = member.Parent.Kind == HandleKind.TypeReference
                            ? UntypedDataAccessScanner.FullNameOfTypeReference(
                                reader, (TypeReferenceHandle)member.Parent)
                            : null;
                        break;
                    default:
                        continue;
                }

                var isAssemblyLoadingFamily =
                    calledParent is "System.Reflection.Assembly" or "System.Runtime.Loader.AssemblyLoadContext"
                    && MonitoredAssemblyLoadingMembers.Contains(calledName);

                if (isAssemblyLoadingFamily && !isExcepted)
                {
                    violations.Add($"assembly-loading[{calledParent}.{calledName}]: {declaringType}.{methodName}");
                }

                if (calledName == "get_CanonicalTargetPath" && !isExcepted)
                {
                    violations.Add($"canonical-target-path: {declaringType}.{methodName}");
                }

                if (calledName == "ReadMvid" && !isExcepted)
                {
                    violations.Add($"read-mvid: {declaringType}.{methodName}");
                }
            }
        }

        return violations;
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
