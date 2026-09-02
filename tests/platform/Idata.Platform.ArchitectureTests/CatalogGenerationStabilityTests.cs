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

    private static string SelfAssemblyPath()
        => Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");

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
        Assert.True(PlatformProjectCatalog.ReadVerifiedImage(victim, generation).Length > 0);

        var poisoned = PoisonComponent(generation, victim.CanonicalPath, component);
        var exception = Assert.Throws<InvalidOperationException>(
            () => PlatformProjectCatalog.ReadVerifiedImage(victim, poisoned));
        Assert.Contains("catalog-generation-drift", exception.Message, StringComparison.Ordinal);

        // Projektet e tjera të gjeneratës së helmuar mbeten GREEN (saktësi kirurgjikale).
        var other = generation.Projects.OrderBy(entry => entry.CanonicalPath, StringComparer.Ordinal).Last();
        Assert.NotEqual(victim.CanonicalPath, other.CanonicalPath);
        Assert.True(PlatformProjectCatalog.ReadVerifiedImage(other, poisoned).Length > 0);
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
            Assert.True(image.Length > 0);
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

        // (5) Snapshot-i nuk publikon bajte të nxjerrshme, path, stream apo delegate.
        // C5: `Bytes` u HOQ krejt — backing-u i `ImmutableArray<byte>` nxirrej me
        // `ImmutableCollectionsMarshal.AsArray`. Forma e plotë provohet te
        // `Snapshot_i_verifikuar_eshte_opak_dhe_i_pafalsifikueshem`.
        var imageType = typeof(PlatformProjectCatalog.VerifiedAssemblyImage);
        Assert.Null(imageType.GetProperty("Bytes"));
        Assert.DoesNotContain(imageType.GetProperties(), property =>
            property.PropertyType == typeof(byte[])
            || property.PropertyType == typeof(ImmutableArray<byte>)
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
        // C5: imazhi i verifikuar NUK ndërtohet më nga testi (konstruktori është
        // privat dhe i pafalsifikueshëm). Të dyja rrugët ndërtohen realisht:
        // snapshot-i përmes `ReadVerifiedImage` mbi një gjeneratë SINTETIKE të
        // vetë binarit të testeve, dhe fixture-ja përmes fabrikës catalog-owned.
        var selfPath = Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");
        var canonicalTarget = Path.GetRelativePath(PlatformProjectCatalog.RepoRoot(), selfPath)
            .Replace(Path.DirectorySeparatorChar, '/');
        var selfEntry = new PlatformProjectCatalog.ProjectEntry(
            "tests/platform/Idata.Platform.ArchitectureTests/Idata.Platform.ArchitectureTests.csproj",
            "Idata.Platform.ArchitectureTests", "Idata.Platform.ArchitectureTests", canonicalTarget);
        var image = PlatformProjectCatalog.ReadVerifiedImage(
            selfEntry, PlatformProjectCatalog.SyntheticGenerationFor(selfEntry));
        var fixtureImage = PlatformProjectCatalog.OpenFixtureImage(selfPath);

        Assert.Equal(
            DynamicRuntimeAccessScanner.ScanFixture(fixtureImage).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal),
            DynamicRuntimeAccessScanner.Scan(image).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal));

        Assert.Equal(
            DynamicRuntimeAccessScanner.ScanOwnerPublicSurfaceFixture(fixtureImage).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal),
            DynamicRuntimeAccessScanner.ScanOwnerPublicSurface(image).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal));

        Assert.Equal(
            UntypedDataAccessScanner.ScanFixture(fixtureImage).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal),
            UntypedDataAccessScanner.Scan(image).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal));

        Assert.Equal(
            UntypedDataAccessScanner.ScanOwnerPublicSurfaceFixture(fixtureImage).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal),
            UntypedDataAccessScanner.ScanOwnerPublicSurface(image).Select(f => $"{f.Kind}|{f.DeclaringType}|{f.Member}|{f.Detail}").Order(StringComparer.Ordinal));

        Assert.Equal(
            IdentityStoreIlScanner.ScanFixture(PlatformProjectCatalog.OpenFixtureImage(selfPath), isStoreOwner: false).Select(f => $"{f.Kind}|{f.Detail}").Order(StringComparer.Ordinal),
            IdentityStoreIlScanner.Scan(image, isStoreOwner: false).Select(f => $"{f.Kind}|{f.Detail}").Order(StringComparer.Ordinal));

        Assert.Equal(
            RoleMetadataScanner.ScanFixture(fixtureImage).Select(f => $"{f.Table}|{f.Token}|{f.Detail}").Order(StringComparer.Ordinal),
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
        string[] fixtureOverloads = ["ScanFixture", "ScanOwnerPublicSurfaceFixture"];

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

        // C5: asnjë koleksion bajtesh nuk publikohet fare — as `ImmutableArray<byte>`,
        // backing-u i së cilës nxirrej me `ImmutableCollectionsMarshal.AsArray`.
        Assert.Null(typeof(PlatformProjectCatalog.VerifiedAssemblyImage).GetProperty("Bytes"));
        Assert.True(reference.Length > 0);

        var identities = new string[32];
        var scans = new int[32];
        Parallel.For(0, identities.Length, index =>
        {
            var image = PlatformProjectCatalog.ReadVerifiedImage(entry);
            identities[index] = $"{image.AssemblyName}|{image.Sha256}|{image.Mvid}|{image.Length}";
            scans[index] = DynamicRuntimeAccessScanner.Scan(image).Count;
        });

        Assert.All(identities, identity => Assert.Equal(identities[0], identity));
        Assert.All(scans, count => Assert.Equal(scans[0], count));
        Assert.Equal(
            $"{reference.AssemblyName}|{reference.Sha256}|{reference.Mvid}|{reference.Length}",
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

    // ═══════════════════════ CORRECTION C5 — §2 / A ═══════════════════════

    /// <summary>
    /// C5 (§2) — snapshot-i i verifikuar është OPAK dhe I PAFALSIFIKUESHËM.
    ///
    /// **Root cause i C4:** `VerifiedAssemblyImage` ishte *positional record*
    /// me konstruktor `internal` dhe me veti `ImmutableArray&lt;byte&gt; Bytes`.
    /// Kjo lejonte njëkohësisht ndërtim direkt me SHA/MVID të falsifikuar,
    /// `with { … }` mbi identitetin, dhe nxjerrjen e backing-ut `byte[]`.
    /// Testet e C4 kontrollonin vetëm mutimin përmes `IList&lt;byte&gt;`, prandaj
    /// nuk ishin diskriminuese.
    /// </summary>
    [Fact]
    public void Snapshot_i_verifikuar_eshte_opak_dhe_i_pafalsifikueshem()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var imageType = typeof(PlatformProjectCatalog.VerifiedAssemblyImage);

        // (1) sealed, JO-record: zero clone/PrintMembers/EqualityContract/IEquatable<self>.
        Assert.True(imageType.IsSealed);
        Assert.True(imageType.IsClass);
        Assert.Null(imageType.GetMethod("<Clone>$", All));
        Assert.Null(imageType.GetMethod("PrintMembers", All));
        Assert.Null(imageType.GetProperty("EqualityContract", All));
        Assert.DoesNotContain(
            imageType.GetInterfaces(),
            contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IEquatable<>)
                && contract.GetGenericArguments()[0] == imageType);

        // (2) çdo konstruktor është PRIVAT — asnjë thirrës jashtë katalogut.
        Assert.NotEmpty(imageType.GetConstructors(All));
        Assert.DoesNotContain(imageType.GetConstructors(All), constructor => !constructor.IsPrivate);

        // (3) zero setter / init — asnjë `with`, asnjë mutim i identitetit.
        Assert.DoesNotContain(imageType.GetProperties(All), property => property.SetMethod is not null);
        Assert.DoesNotContain(imageType.GetFields(All), field => !field.IsPrivate);

        // (4) asnjë anëtar non-privat nuk publikon backing-un e bajteve.
        var leaking = new[]
        {
            typeof(ImmutableArray<byte>), typeof(byte[]), typeof(Memory<byte>),
            typeof(ReadOnlyMemory<byte>),
        };
        foreach (var property in imageType.GetProperties(All))
        {
            Assert.DoesNotContain(property.PropertyType, leaking);
            Assert.False(typeof(FileSystemInfo).IsAssignableFrom(property.PropertyType));
            Assert.False(typeof(Stream).IsAssignableFrom(property.PropertyType));
            Assert.False(typeof(Delegate).IsAssignableFrom(property.PropertyType));
        }

        foreach (var method in imageType.GetMethods(All).Where(candidate => !candidate.IsPrivate))
        {
            Assert.DoesNotContain(method.ReturnType, leaking);
            Assert.False(typeof(FileSystemInfo).IsAssignableFrom(method.ReturnType));
            Assert.False(typeof(Delegate).IsAssignableFrom(method.ReturnType));
        }
    }

    /// <summary>
    /// C5 (§2.7/§2.8) — backing-u nuk nxirret dot, dhe çdo konsum merr storage
    /// TË RI, read-only e të pavarur: `GetBuffer()` hedh (jo publicly-visible),
    /// shkrimi refuzohet, dhe mutimi i kopjes së konsumatorit A nuk prek as
    /// snapshot-in as konsumatorin B.
    /// </summary>
    [Fact]
    public void Konsumi_i_snapshot_it_merr_storage_te_ri_dhe_te_pavarur()
    {
        var entry = PlatformProjectCatalog.DiscoverProduction()
            .Single(candidate => candidate.AssemblyName == "Idata.Platform.Persistence");
        var image = PlatformProjectCatalog.ReadVerifiedImage(entry);

        using var first = image.OpenImage();
        using var second = image.OpenImage();
        Assert.NotSame(first, second);
        Assert.False(first.CanWrite);
        Assert.Equal(image.Length, first.Length);

        var firstBuffer = Assert.IsType<MemoryStream>(first);
        var secondBuffer = Assert.IsType<MemoryStream>(second);
        Assert.Throws<UnauthorizedAccessException>(() => firstBuffer.GetBuffer());

        // Konsumatori A mutn kopjen e vet …
        var copyOfFirst = firstBuffer.ToArray();
        var original = copyOfFirst[0];
        copyOfFirst[0] ^= 0xFF;

        // … dhe as konsumatori B, as snapshot-i, as leximi i radhës nuk preken.
        Assert.Equal(original, secondBuffer.ToArray()[0]);
        using var third = image.OpenImage();
        Assert.Equal(original, ((MemoryStream)third).ToArray()[0]);
        Assert.Equal(image.Sha256, PlatformProjectCatalog.ReadVerifiedImage(entry).Sha256);
    }

    /// <summary>
    /// C5 (§2.5) — CALL-SITE PARITY: konstruktori privat thirret VETËM nga
    /// fabrika catalog-owned, dhe fabrika VETËM nga `ReadVerifiedImage`. Kështu
    /// asnjë kod tjetër — test përfshirë — nuk mund të prodhojë një snapshot
    /// të "verifikuar" pa kaluar nga verifikimi real.
    /// </summary>
    [Fact]
    public void Fabrika_e_snapshot_it_ka_call_site_parity_ekzakte()
    {
        Assert.Equal(
            ["Idata.Platform.ArchitectureTests.PlatformProjectCatalog+VerifiedAssemblyImage.CreateVerified"],
            CallersOfMember("VerifiedAssemblyImage", ".ctor").Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Idata.Platform.ArchitectureTests.PlatformProjectCatalog.ReadVerifiedImage"],
            CallersOfMember("VerifiedAssemblyImage", "CreateVerified").Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Idata.Platform.ArchitectureTests.PlatformProjectCatalog+FixtureAssemblyImage.CreateFixture"],
            CallersOfMember("FixtureAssemblyImage", ".ctor").Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Idata.Platform.ArchitectureTests.PlatformProjectCatalog.OpenFixtureImage"],
            CallersOfMember("FixtureAssemblyImage", "CreateFixture").Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// C5 (§2.9/§3.9) — imazhi fixture është tip NOMINALISHT I VEÇANTË: pa
    /// trashëgimi, pa konvertim dhe pa cast të vlefshëm drejt snapshot-it të
    /// verifikuar. Skaneri production nuk pranon kapacitet fixture dhe anasjelltas.
    /// </summary>
    [Fact]
    public void Imazhi_fixture_eshte_nominalisht_i_veçante_nga_snapshot_i_verifikuar()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var verified = typeof(PlatformProjectCatalog.VerifiedAssemblyImage);
        var fixture = typeof(PlatformProjectCatalog.FixtureAssemblyImage);

        Assert.NotEqual(verified, fixture);
        Assert.False(verified.IsAssignableFrom(fixture));
        Assert.False(fixture.IsAssignableFrom(verified));
        Assert.Equal(typeof(object), verified.BaseType);
        Assert.Equal(typeof(object), fixture.BaseType);
        Assert.True(fixture.IsSealed);

        foreach (var type in (Type[])[verified, fixture])
        {
            Assert.DoesNotContain(
                type.GetMethods(All),
                method => method.Name is "op_Implicit" or "op_Explicit");
        }

        // Cast-i përmes `object` dështon në runtime — tipat janë të palidhur.
        var fixtureImage = PlatformProjectCatalog.OpenFixtureImage(SelfAssemblyPath());
        Assert.Throws<InvalidCastException>(
            () => (PlatformProjectCatalog.VerifiedAssemblyImage)(object)fixtureImage);

        // Sinjaturat: rruga production pranon VETËM snapshot-in e verifikuar.
        foreach (var (type, production, fixtureEntry) in (( Type, string, string )[])
        [
            (typeof(DynamicRuntimeAccessScanner), "Scan", "ScanFixture"),
            (typeof(UntypedDataAccessScanner), "Scan", "ScanFixture"),
            (typeof(IdentityStoreIlScanner), "Scan", "ScanFixture"),
        ])
        {
            Assert.Contains(
                type.GetMethods(All).Where(method => method.Name == production),
                method => method.GetParameters()[0].ParameterType == verified);
            Assert.All(
                type.GetMethods(All).Where(method => method.Name == production),
                method => Assert.NotEqual(fixture, method.GetParameters()[0].ParameterType));
            Assert.All(
                type.GetMethods(All).Where(method => method.Name == fixtureEntry),
                method =>
                {
                    Assert.Equal(fixture, method.GetParameters()[0].ParameterType);
                    Assert.NotEqual(verified, method.ReturnType);
                });
        }
    }

    // ═══════════════════════ CORRECTION C5 — §3 / B ═══════════════════════

    /// <summary>
    /// C5 (§3.4–3.6) — autoriteti që identifikon artefaktet production është
    /// **i derivuar** nga e njëjta gjeneratë e ngrirë, me PARITET DY-DREJTIMËSH
    /// dhe zero listë manuale emrash/path-esh/hash-esh.
    /// </summary>
    [Fact]
    public void Autoriteti_i_artefakteve_production_derivohet_nga_gjenerata()
    {
        var generation = PlatformProjectCatalog.CurrentGeneration();
        var authority = PlatformProjectCatalog.ProductionArtifacts(generation);

        // → drejtimi 1: çdo projekt i gjeneratës është i mbuluar në të tria dimensionet.
        Assert.All(generation.Projects, entry =>
        {
            Assert.Contains(
                generation.Sha256ByCanonicalPath[entry.CanonicalPath],
                (IReadOnlySet<string>)authority.Sha256Digests);
            Assert.Contains(
                generation.MvidByCanonicalPath[entry.CanonicalPath],
                (IReadOnlySet<Guid>)authority.Mvids);
        });

        // ← drejtimi 2: asnjë element i autoritetit nuk ekziston pa projekt burim.
        Assert.All(authority.Sha256Digests, digest =>
            Assert.Contains(digest, generation.Sha256ByCanonicalPath.Values));
        Assert.All(authority.Mvids, mvid => Assert.Contains(mvid, generation.MvidByCanonicalPath.Values));
        Assert.Equal(generation.Projects.Length, authority.TargetFullPaths.Count);
        Assert.Equal(
            generation.Sha256ByCanonicalPath.Values.Distinct(StringComparer.Ordinal).Count(),
            authority.Sha256Digests.Count);
        Assert.Equal(generation.MvidByCanonicalPath.Values.Distinct().Count(), authority.Mvids.Count);

        // Zero listë manuale: asnjë string literal me emër/path/hash artefakti
        // brenda `ProductionArtifacts` — autoriteti vjen tërësisht nga gjenerata.
        Assert.Empty(StringLiteralsOf(
            "Idata.Platform.ArchitectureTests.PlatformProjectCatalog", "ProductionArtifacts"));
    }

    /// <summary>
    /// C5 (§3.7/§3.8) — një artefakt PRODUCTION refuzohet FAIL-CLOSED në rrugën
    /// fixture, pavarësisht si arrihet.
    ///
    /// **Root cause i C4 (i provuar):** `ScanFixtureFile(Path.Combine(
    /// AppContext.BaseDirectory, "Idata.Platform.Persistence.dll"))` skanonte
    /// një artefakt production — build-i e vendos një kopje BYTE-IDENTIKE pranë
    /// testeve — dhe të gjithë guard-et mbeteshin GREEN.
    /// </summary>
    [Fact]
    public void Artefaktet_production_refuzohen_ne_rrugen_fixture()
    {
        var authority = PlatformProjectCatalog.ProductionArtifacts();

        // (1) path-i i target-it production, drejtpërdrejt nga autoriteti i derivuar.
        var target = authority.TargetFullPaths.Order(StringComparer.Ordinal).First();
        Reject(target, "path i target-it production");

        // (2) kopja në AppContext.BaseDirectory (rruga e saktë e bypass-it të C4).
        var adjacent = Path.Combine(AppContext.BaseDirectory, "Idata.Platform.Persistence.dll");
        Assert.True(File.Exists(adjacent), "premisa: kopja production ndodhet pranë testeve");
        Reject(adjacent, "kopje pranë testeve");

        // (3) helper me një nivel dhe (4) me disa nivele indirection.
        Reject(OneLevel(adjacent), "helper një nivel");
        Reject(ManyLevels(adjacent), "helper disa nivele");

        // (5) kopje e RIEMËRTUAR dhe (6) bajte production nën direktori fixture.
        var renamed = Path.Combine(Path.GetTempPath(), $"fixture-{Guid.NewGuid():N}.dll");
        var nested = Path.Combine(AppContext.BaseDirectory, $"Synthetic.Fixture.{Guid.NewGuid():N}.dll");
        try
        {
            File.Copy(adjacent, renamed);
            File.Copy(adjacent, nested);
            Reject(renamed, "kopje e riemërtuar në temp");
            Reject(nested, "bajte production nën direktori fixture");
        }
        finally
        {
            File.Delete(renamed);
            File.Delete(nested);
        }

        // (7) fixture SINTETIKE e autorizuar mbetet GREEN.
        var synthetic = SyntheticMetadataPe.Write(malformed: false, AppContext.BaseDirectory);
        try
        {
            var image = PlatformProjectCatalog.OpenFixtureImage(synthetic);
            Assert.Equal(Path.GetFileNameWithoutExtension(synthetic), image.AssemblyName);
            Assert.NotNull(DynamicRuntimeAccessScanner.ScanFixture(image));
        }
        finally
        {
            File.Delete(synthetic);
        }

        // Vetë-assembly-ja e testeve nuk është artefakt production → e lejuar.
        Assert.NotNull(PlatformProjectCatalog.OpenFixtureImage(SelfAssemblyPath()));

        // (8) SECILI dimension i autoritetit është I DOMOSDOSHËM. Ndaj gjeneratës
        // reale të tria janë reciprokisht të tepërta — bajtet identike nënkuptojnë
        // MVID identik — prandaj izolohen me nën-autoritete të derivuara nga E
        // NJËJTA gjeneratë. Heqja e cilitdo degë e bën këtë bllok RED.
        var pathOnly = authority with
        {
            Sha256Digests = FrozenSet<string>.Empty,
            Mvids = FrozenSet<Guid>.Empty,
        };
        var shaOnly = authority with
        {
            TargetFullPaths = FrozenSet<string>.Empty,
            Mvids = FrozenSet<Guid>.Empty,
        };
        var mvidOnly = authority with
        {
            TargetFullPaths = FrozenSet<string>.Empty,
            Sha256Digests = FrozenSet<string>.Empty,
        };

        Assert.Contains("path i target-it", RejectionReason(target, pathOnly), StringComparison.Ordinal);
        Assert.Contains("SHA-256", RejectionReason(adjacent, shaOnly), StringComparison.Ordinal);
        Assert.Contains("MVID", RejectionReason(adjacent, mvidOnly), StringComparison.Ordinal);

        static string RejectionReason(
            string path, PlatformProjectCatalog.ProductionArtifactAuthority authority)
            => Assert.Throws<InvalidOperationException>(
                () => PlatformProjectCatalog.OpenFixtureImage(path, authority)).Message;

        static string OneLevel(string path) => Path.Combine(
            Path.GetDirectoryName(path)!, Path.GetFileName(path));

        static string ManyLevels(string path) => Path.GetFullPath(Path.Join(
            Path.GetDirectoryName(path)!, "..", Path.GetFileName(Path.GetDirectoryName(path)!),
            Path.GetFileName(path)));

        static void Reject(string path, string vector)
        {
            var failure = Assert.Throws<InvalidOperationException>(
                () => PlatformProjectCatalog.OpenFixtureImage(path));
            Assert.Contains("fixture-production-artifact", failure.Message, StringComparison.Ordinal);
        }
    }

    // ═══════════════════════ CORRECTION C5 — §4 / C ═══════════════════════

    /// <summary>
    /// C5 (§4) — DATAFLOW REAL, jo prani emri dhe jo vetëm rend: provohet mbi
    /// IL-in e `ReadVerifiedImage` se **pikërisht vlera e prodhuar nga leximi
    /// real** kalon në `VerifyImageBytes` DHE në fabrikën e snapshot-it, dhe se
    /// kthimi ndodh vetëm pas post-verifikimit.
    ///
    /// **Root cause i C4 (i provuar):** testi ABA e thërriste `VerifyImageBytes`
    /// DIREKT; heqja e wiring-ut real brenda `ReadVerifiedImage` linte suitën
    /// 282/282 GREEN. Testohej seam-i, jo dataflow-i.
    /// </summary>
    [Fact]
    public void ReadVerifiedImage_verifikon_pikerisht_bajtet_e_lexuara()
    {
        var flow = AnalyzeReadVerifiedImageDataflow();

        Assert.Equal(1, flow.ReadCallCount);
        Assert.True(flow.ReadResultStoredInLocal, "rezultati i leximit real nuk ruhet në një local");
        Assert.True(flow.VerifyReceivesReadValue,
            "VerifyImageBytes NUK merr vlerën e prodhuar nga leximi real");
        Assert.True(flow.FactoryReceivesReadValue,
            "snapshot-i NUK ndërtohet nga bajtet e validuara");
        Assert.True(flow.PreVerifyIndex >= 0 && flow.PreVerifyIndex < flow.ReadIndex,
            "mungon verify-before para leximit");
        Assert.True(flow.ReadIndex < flow.VerifyIndex, "verifikimi i bajteve nuk vjen pas leximit");
        Assert.True(flow.VerifyIndex < flow.PostVerifyIndex, "mungon post-verify pas verifikimit të bajteve");
        Assert.True(flow.PostVerifyIndex < flow.FactoryIndex,
            "snapshot-i ndërtohet para post-verifikimit");
        Assert.True(flow.FactoryIndex < flow.ReturnIndex, "kthimi nuk vjen pas ndërtimit të snapshot-it");
    }

    private readonly record struct VerifiedReadDataflow(
        int ReadCallCount, bool ReadResultStoredInLocal, bool VerifyReceivesReadValue,
        bool FactoryReceivesReadValue, int PreVerifyIndex, int ReadIndex, int VerifyIndex,
        int PostVerifyIndex, int FactoryIndex, int ReturnIndex);

    /// <summary>
    /// Interpretim abstrakt i IL-it të `ReadVerifiedImage`: stack me token-a
    /// "taint", ku e vetmja vlerë e njollosur është rezultati i `ReadAllBytes`.
    /// Taint-i përhapet përmes `stloc`/`ldloc` dhe përmes thirrjeve konvertuese
    /// (p.sh. `ReadOnlySpan&lt;byte&gt;.op_Implicit`). Çdo degëzim ose opcode i
    /// panjohur = NDALESË e qartë, kurrë supozim i heshtur.
    /// </summary>
    private static VerifiedReadDataflow AnalyzeReadVerifiedImageDataflow()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");
        using var stream = File.OpenRead(path);
        using var portableExecutable = new PEReader(stream);
        var reader = portableExecutable.GetMetadataReader();

        byte[]? body = null;
        foreach (var handle in reader.MethodDefinitions)
        {
            var candidate = reader.GetMethodDefinition(handle);
            if (candidate.RelativeVirtualAddress == 0
                || reader.GetString(candidate.Name) != "ReadVerifiedImage"
                || UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, candidate.GetDeclaringType())
                    != "Idata.Platform.ArchitectureTests.PlatformProjectCatalog")
            {
                continue;
            }

            var il = portableExecutable.GetMethodBody(candidate.RelativeVirtualAddress).GetILBytes()!;
            if (CalleeNamesOf(reader, il).Contains("ReadAllBytes"))
            {
                Assert.Null(body); // vetëm NJË overload bën leximin real
                body = il;
            }
        }

        Assert.NotNull(body);
        var instructions = IlOperandTable.WalkAll(body!, out var decodeError);
        Assert.Null(decodeError);

        const int Untainted = 0;
        const int Tainted = 1;
        var stack = new List<int>();
        var locals = new Dictionary<int, int>();
        var readCalls = 0;
        var readIndex = -1;
        var storedInLocal = false;
        var verifyIndex = -1;
        var factoryIndex = -1;
        var preVerifyIndex = -1;
        var postVerifyIndex = -1;
        var returnIndex = -1;
        var verifyTakesRead = false;
        var factoryTakesRead = false;

        void Push(int value) => stack.Add(value);

        int Pop()
        {
            Assert.NotEmpty(stack);
            var value = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return value;
        }

        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            var opcode = instruction.Opcode;

            switch (opcode)
            {
                case 0x00: // nop
                    continue;
                case >= 0x02 and <= 0x05: // ldarg.0-3
                case 0x0E: // ldarg.s
                case 0x0F: // ldarga.s
                case 0x14: // ldnull
                case >= 0x15 and <= 0x1F: // ldc.i4.m1 .. ldc.i4.s
                case 0x20: // ldc.i4
                case 0x21: // ldc.i8
                case 0x22: // ldc.r4
                case 0x23: // ldc.r8
                case 0x72: // ldstr
                case 0x7E: // ldsfld
                case 0xD0: // ldtoken
                case 0xFE09: // ldarg
                    Push(Untainted);
                    continue;
                case >= 0x06 and <= 0x09: // ldloc.0-3
                    Push(locals.GetValueOrDefault(opcode - 0x06, Untainted));
                    continue;
                case 0x11: // ldloc.s
                case 0x12: // ldloca.s
                case 0xFE0C: // ldloc
                    Push(locals.GetValueOrDefault(instruction.Operand, Untainted));
                    continue;
                case >= 0x0A and <= 0x0D: // stloc.0-3
                    locals[opcode - 0x0A] = Pop();
                    continue;
                case 0x13: // stloc.s
                case 0xFE0E: // stloc
                    locals[instruction.Operand] = Pop();
                    continue;
                case 0x25: // dup
                    Push(stack[^1]);
                    continue;
                case 0x26: // pop
                    Pop();
                    continue;
                case 0x74: // castclass
                case 0x75: // isinst
                case 0x79: // unbox
                case 0x8C: // box
                case 0x8D: // newarr
                case 0xA5: // unbox.any
                case 0x7B: // ldfld
                    Push(Pop());
                    continue;
                case 0x2A: // ret
                    returnIndex = index;
                    continue;
                case 0x2B: // br.s — kërcimi i pakushtëzuar që Debug-u emeton para `ret`
                case 0x38: // br
                {
                    var delta = opcode == 0x2B ? (sbyte)instruction.Operand : instruction.Operand;
                    var target = instruction.Offset + (opcode == 0x2B ? 2 : 5) + delta;
                    Assert.True(target > instruction.Offset,
                        $"kërcim PRAPA @IL_{instruction.Offset:X4} — metoda nuk është straight-line.");
                    Assert.Empty(stack);
                    continue;
                }
                case 0x28: // call
                case 0x6F: // callvirt
                case 0x73: // newobj
                    break;
                default:
                    Assert.Fail(
                        $"opcode 0x{opcode:X2} @IL_{instruction.Offset:X4} nuk mbulohet nga analiza "
                        + "dataflow — metoda duhet të mbetet straight-line; rishikoje testin dhe kodin.");
                    continue;
            }

            var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(instruction.Operand);
            if (handle.Kind == HandleKind.MethodSpecification)
            {
                handle = reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
            }

            var name = handle.Kind switch
            {
                HandleKind.MethodDefinition =>
                    reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
                HandleKind.MemberReference =>
                    reader.GetString(reader.GetMemberReference((MemberReferenceHandle)handle).Name),
                _ => string.Empty,
            };

            var (popCount, pushesValue) = CalleeStackShape(reader, handle, isNewObject: opcode == 0x73);
            var arguments = new List<int>();
            for (var popped = 0; popped < popCount; popped++)
            {
                arguments.Add(Pop());
            }

            var carriesRead = arguments.Contains(Tainted);
            switch (name)
            {
                case "ReadAllBytes":
                    readCalls++;
                    readIndex = index;
                    break;
                case "VerifyImageBytes":
                    verifyIndex = index;
                    verifyTakesRead = carriesRead;
                    break;
                case "CreateVerified":
                    factoryIndex = index;
                    factoryTakesRead = carriesRead;
                    break;
                case "VerifiedFrozenTargetPath":
                    if (preVerifyIndex < 0)
                    {
                        preVerifyIndex = index;
                    }
                    else
                    {
                        postVerifyIndex = index;
                    }

                    break;
            }

            if (pushesValue)
            {
                Push(name == "ReadAllBytes" ? Tainted : carriesRead ? Tainted : Untainted);
            }

            if (name == "ReadAllBytes" && index + 1 < instructions.Count)
            {
                var next = instructions[index + 1].Opcode;
                storedInLocal = next is (>= 0x0A and <= 0x0D) or 0x13 or 0xFE0E;
            }
        }

        return new VerifiedReadDataflow(
            readCalls, storedInLocal, verifyTakesRead, factoryTakesRead,
            preVerifyIndex, readIndex, verifyIndex, postVerifyIndex, factoryIndex, returnIndex);
    }

    /// <summary>
    /// Forma e stack-ut të një thirrjeje: sa vlera konsumon dhe a shtyn vlerë.
    /// Lexohet nga sinjatura REALE (blob), jo nga emri. Për `newobj` `this`-i
    /// nuk konsumohet dhe objekti i ri shtyhet gjithmonë.
    /// </summary>
    private static (int PopCount, bool PushesValue) CalleeStackShape(
        MetadataReader reader, EntityHandle handle, bool isNewObject)
    {
        var signature = handle.Kind switch
        {
            HandleKind.MethodDefinition =>
                reader.GetMethodDefinition((MethodDefinitionHandle)handle).Signature,
            HandleKind.MemberReference =>
                reader.GetMemberReference((MemberReferenceHandle)handle).Signature,
            _ => default,
        };

        Assert.False(signature.IsNil);
        var blob = reader.GetBlobReader(signature);
        var header = blob.ReadSignatureHeader();
        if (header.IsGeneric)
        {
            blob.ReadCompressedInteger();
        }

        var parameterCount = blob.ReadCompressedInteger();
        var element = blob.ReadCompressedInteger();
        while (element is 0x1F or 0x20) // CMOD_REQD / CMOD_OPT para return type
        {
            blob.ReadCompressedInteger();
            element = blob.ReadCompressedInteger();
        }

        var returnsVoid = element == 0x01; // ELEMENT_TYPE_VOID
        var popCount = parameterCount + (isNewObject || !header.IsInstance ? 0 : 1);
        return (popCount, isNewObject || !returnsVoid);
    }

    /// <summary>Emrat e thirrur brenda një trupi IL (MethodDef/MemberRef/MethodSpec).</summary>
    private static HashSet<string> CalleeNamesOf(MetadataReader reader, byte[] il)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instruction in IlOperandTable.Walk(il, out _))
        {
            if (instruction.MetadataToken == 0)
            {
                continue;
            }

            var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(
                instruction.MetadataToken);
            if (handle.Kind == HandleKind.MethodSpecification)
            {
                handle = reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
            }

            if (handle.Kind == HandleKind.MethodDefinition)
            {
                names.Add(reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)handle).Name));
            }
            else if (handle.Kind == HandleKind.MemberReference)
            {
                names.Add(reader.GetString(reader.GetMemberReference((MemberReferenceHandle)handle).Name));
            }
        }

        return names;
    }

    /// <summary>
    /// Thirrësit e një anëtari të tipit të ngulitur, si `Tip.Metodë` dhe **NJË
    /// ZË PËR ÇDO MethodDefinition** — pa `Distinct`. Kështu edhe një overload i
    /// dytë me TË NJËJTIN emër numërohet veçmas: parity-a matet mbi definicione
    /// reale, jo mbi emra.
    /// </summary>
    private static IReadOnlyList<string> CallersOfMember(string nestedTypeName, string memberName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");
        using var stream = File.OpenRead(path);
        using var portableExecutable = new PEReader(stream);
        var reader = portableExecutable.GetMetadataReader();
        var target = $"Idata.Platform.ArchitectureTests.PlatformProjectCatalog+{nestedTypeName}";
        var callers = new List<string>();

        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            var il = portableExecutable.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            var matched = false;
            foreach (var instruction in IlOperandTable.Walk(il, out _))
            {
                if (instruction.MetadataToken == 0)
                {
                    continue;
                }

                var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(
                    instruction.MetadataToken);
                if (handle.Kind == HandleKind.MethodSpecification)
                {
                    handle = reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
                }

                if (handle.Kind != HandleKind.MethodDefinition)
                {
                    continue;
                }

                var callee = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                if (reader.GetString(callee.Name) == memberName
                    && UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, callee.GetDeclaringType())
                        == target)
                {
                    matched = true;
                }
            }

            if (matched)
            {
                callers.Add(
                    UntypedDataAccessScanner.FullNameOfTypeDefinition(reader, method.GetDeclaringType())
                    + "." + reader.GetString(method.Name));
            }
        }

        Assert.NotEmpty(callers);
        return callers;
    }

    /// <summary>Literalet `ldstr` brenda një metode — provë e mungesës së listave manuale.</summary>
    private static IReadOnlyList<string> StringLiteralsOf(string declaringTypeFullName, string methodName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Idata.Platform.ArchitectureTests.dll");
        using var stream = File.OpenRead(path);
        using var portableExecutable = new PEReader(stream);
        var reader = portableExecutable.GetMetadataReader();
        var literals = new List<string>();
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
            foreach (var instruction in IlOperandTable.WalkAll(il, out _).Where(candidate => candidate.Opcode == 0x72))
            {
                literals.Add(reader.GetUserString(
                    System.Reflection.Metadata.Ecma335.MetadataTokens.UserStringHandle(instruction.Operand)));
            }
        }

        Assert.True(matched, $"Metoda {declaringTypeFullName}.{methodName} nuk u gjet.");
        return literals;
    }
}
