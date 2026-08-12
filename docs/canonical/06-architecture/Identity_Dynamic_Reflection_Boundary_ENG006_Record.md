# Dynamic Reflection Boundary & Deterministic Harness — Implementation Record (ENG-006 + ENG-008, Pre-B2 Increment 2)

**Statusi:** **IMPLEMENTED / PENDING_INDEPENDENT_REVIEW** — të dyja kufijtë ekzistojnë dhe imponohen në ArchitectureTests/CI në **PR #30** (branch `feat/f1-eng006-eng008-preb2-boundary`), **NOT MERGED**.

- **`ENG-006` = OPEN / IMPLEMENTATION IN THIS PR / PENDING_INDEPENDENT_REVIEW** — kufi **SIGURIE**.
- **`ENG-008` = OPEN / IMPLEMENTATION IN THIS PR / PENDING_INDEPENDENT_REVIEW** — kufi **BESUESHMËRIE / evidence-harness**; **NUK është bypass production i provuar**.

Mbyllja formale kërkon Fable delta-review → Codex review → Human Gate i Pronarit → merge.
**Autoriteti:** `ENG-006` (Tracker, 29.07.2026) + `ENG-008` (Tracker, 02.08.2026) + ADR-028/ADR-029.
**Data:** 03.08.2026 · **Baza:** `main` = `8f7a22c` (PR #29 MERGED; ENG-007 = CLOSED).

---

## 1. Objektivi dhe kufiri

**ËSHTË:** (a) një harness arkitekturor **determinist**, ku evidenca nuk varet nga rendi i ekzekutimit; (b) një policy **deny-by-default** për akses dinamik reflection/runtime nga production assemblies, me **manifest production BOSH**.

**NUK ËSHTË:** B.2/B.3 · Identity protocol/credentials/token issuance/projection · DB-level isolation · modul biznesi · punë fiskalizimi · ndryshim i asnjë `src/` production.

**ENG-008 u implementua dhe u provua PARA ENG-006**, sepse evidenca e ENG-006 nuk është e besueshme mbi një harness me race output/MVID.

---

## 2. ENG-008 — root cause i PROVUAR (jo i përsëritur)

`-t:ResolveReferences` ndërton ProjectReference-at si pjesë normale e target-it, prandaj **rindërton output-in production GJATË run-it**. Katalogu ngrinte vetëm path-et, ndërsa MVID-i lexohej më vonë nga disku; kështu gjenerata **A** e katalogut përzihej me gjeneratën **B** të file-it dhe me gjeneratën **A′** të assembly-t të ngarkuar në default load context → dështim FALSE i varur nga rendi.

**Prova mekanike (e ekzekutuar):** me një burim Persistence të prekur, `-t:ResolveReferences` **pa** flag e ndryshoi `mtime` e `Idata.Platform.Persistence.dll` (`01:59:10 → 01:59:43`); **me** `-p:BuildProjectReferences=false` `mtime` mbeti i pandryshuar, ndërsa provenance-a mbeti e plotë (335 `ReferencePath`, 327 me `NuGetPackageId/Version`, `ReferenceSourceTarget=ProjectReference`, `MSBuildSourceProjectFile`).

**Zbulim i dytë, i rëndësishëm:** build-et janë **deterministë** — një rindërtim i burimeve të pandryshuara prodhon bajte IDENTIKE. Prandaj një fingerprint vetëm-përmbajtje **nuk e dëshmon dot** ngjarjen e rindërtimit. Fingerprint-i u bë **tre-dimensional**: SHA-256 + MVID + **koha e shkrimit**.

### Modeli atomic-generation

1. Zbulim + vlerësim identiteti (MSBuild) **para** çdo freeze-i.
2. Fingerprint-i i të gjitha `TargetPath`-eve — **pass 1**.
3. Vlerësim i plotë brenda së njëjtës gjeneratë: adjacency e ProjectReference-ve, referenca të zgjidhura, autoritete package/framework — i gjithë **READ-ONLY**.
4. Fingerprint-i — **pass 2**. Çdo devijim → `catalog-generation-drift` (**fail-closed i kontrolluar**, kurrë refresh i heshtur).
5. Gjeneratë e VETME për proces (`Lazy` me `ExecutionAndPublication`) — thirrësit paralelë marrin TË NJËJTIN objekt.
6. `LoadVerified` krahason assembly-n e ngarkuar me **MVID-in e NGRIRË**; identiteti exact-path/MVID mbetet po aq i fortë (decoy/stale kapet njësoj), por dy gjenerata nuk përzihen dot.
7. `-p:BuildProjectReferences=false` është **një konstante e vetme**, e konsumuar nga të gjitha rrugët e thirrjes.

**Zero** nga këto: hardcoded test order · `Task.Delay` · retry i MVID mismatch · refresh selektiv · dobësim i MVID · test-orderer · repo `output/`/`tmp/` për izolim · cache-clear hook global. Prova mekanike mbi metadata: asnjë metodë e `PlatformProjectCatalog` nuk thërret `Delay`, `Sleep`, `GetTempPath` ose `GetTempFileName`.

**Kosto e pranuar:** freeze-i eager i 12 projekteve e rrit suitën arkitekturore nga ~31s në ~60s lokalisht. Kompensim: determinizëm i plotë.

---

## 3. ENG-006 — taksonomia DANGEROUS / SAFE EXACT / UNKNOWN

Scanner i **NDARË** nga `IdentityStoreIlScanner` (ai mbron universin e tipeve; ky mbron rrugën e zgjidhjes dinamike).

- **DANGEROUS** — zgjidhje/thirrje në runtime: `Type.GetType/GetMethod(s)/GetProperty(ies)/GetField(s)/GetConstructor(s)/GetMember(s)/InvokeMember/MakeGenericType` · `Assembly|Module.GetType` · `Assembly.Load*`/`CreateInstance` · `AssemblyLoadContext.LoadFrom*` · `Activator.CreateInstance` · `MethodBase/MethodInfo/ConstructorInfo.Invoke` · `PropertyInfo/FieldInfo.GetValue/SetValue` · `MethodInfo.MakeGenericMethod/CreateDelegate` · `Delegate.DynamicInvoke` · DLR `Binder.*` + `CallSite`/`CallSite\`1` · service location me `System.Type`.
- **SAFE EXACT** — çifte të emërtuara dhe të review-uara, të derivuara nga call-site-t aktuale: `Type.GetTypeFromHandle` (`typeof(...)`), `MethodBase.GetMethodFromHandle` / `FieldInfo.GetFieldFromHandle` (materializim i një **tokeni të kompiluar**, që kompajleri i emeton për lambda/expression-tree), plus lexim metadata (`get_FullName`, `get_Name`, `GetName`, …).
- **UNKNOWN mbi një familje të monitoruar** → gjetje fail-closed. *(I dëshmuar realisht: `CallSite\`1.Target` nuk është në listën DANGEROUS dhe kapet pikërisht nga ky rregull.)*

`System.Object.GetType()` **NUK** ndalohet globalisht — zinxhiri kapet te hapi i dytë real. `GetService`/`GetRequiredService` diskriminohen **me sinjaturë**: vetëm overload-i me `System.Type` është gjetje; varianti gjenerik `<T>` mbetet i lejuar.

**Owner public-surface guard:** asnjë anëtar publik/`protected` i owner-ave — metodë, accessor **apo FUSHË (correction C1)** — nuk lejohet të pranojë a dorëzojë tipe nga seti autoritativ I VETËM `DynamicRuntimeAccessTaxonomy.OwnerForbiddenSurfaceTypes` në kod. Ky Record **NUK mban listë të kopjuar** (Shtesa D, forma A): autoriteti është vetëm seti në kod; docstring-u i skanerit bën po ashtu vetëm pointer. **Rezultati mbi production: 0 gjetje.**

### Manifesti production — BOSH

Zero përjashtim i autorizuar sot; testi e imponon boshllëkun. Çdo përjashtim i ardhshëm kërkon path kanonik, identitet assembly-je, tip, anëtar, arsye, pronar, fingerprint/evidencë, review të pavarur dhe Human Gate.

### Dy defekte reale të gjetura dhe të ndrequra gjatë implementimit

1. **Prindi i një `TypeSpecification`** merrej si çdo tip brenda tij, përfshirë argumentet gjenerike — kështu `EqualityComparer<Type>` (që kompajleri e emeton për `record.GetHashCode`) lexohej gabimisht si "anëtar mbi `System.Type`". Fix: prindi = tipi **deklarues i jashtëm**.
2. **`GetMethodFromHandle`/`GetFieldFromHandle`** klasifikoheshin si UNKNOWN, ndërsa janë ekuivalenti i `typeof(...)` për metoda/fusha.

Efekti i matur: **99 → 19** gjetje production.

---

## 4. Kufi i DEKLARUAR: kodi i gjeneruar brenda assembly-ve

Të 19 gjetjet e mbetura i përkasin kodit **të gjeneruar** brenda production assemblies — `Mediator.Internals.*` (source generator i Mediator) dhe `Microsoft.AspNetCore.OpenApi.Generated.*` (source generator i OpenAPI XML comments). Ky kod nuk shkruhet nga ne dhe nuk ndryshohet dot pa prekur varësitë, çka është **jashtë scope-it** të këtij increment-i.

**Correction C1 — mbyllja fail-closed e self-exemption (Blocker A):** klasifikimi i mëparshëm kishte katër dobësi të konfirmuara: (a) validatori e vetë-autorizonte prefiksin `generated-*` me një skip të heshtur (formulimi i saktë: *silent self-exemption u hoq — generated findings nuk autorizohen nga prefiksi dhe kalojnë në exact provenance-ledger validation*; degëzimi `continue` ekziston ende, por drejton te blloku i ledger-it, kurrë në heshtje); (b) `CompilerGenerated`/`GeneratedCode` pranoheshin vetëm nga emri i atributit; (c) nested types trashëgonin trust nga parent-i; (d) çdo lambda/closure e jona (të cilës kompajleri i vë vetë `CompilerGenerated`) mund të fshihte `Type.GetType`. **Modeli i ri:**

- `[CompilerGenerated]` i vetëm **nuk jep KURRË trust** — kod i tillë mbetet kind i thjeshtë blocking ose `unattributed-compiler-generated` fail-closed (lambda-t/local functions/LINQ tona provohen VEÇMAS me fixtures);
- `[GeneratedCode(tool, version)]` **dekodohet REALISHT nga blob-i i metadata-s** (blob i palexueshëm = gjetje fail-closed) dhe klasifikon `generated-<kind>` me tool/version të bartur në gjetje — POR kjo **nuk është përjashtim**;
- **nested type nuk trashëgon asgjë** nga parent-i (barazi EKZAKTE e emrit të plotë, zero ecje `+`);
- çdo gjetje `generated-*` kontrollohet kundër një **PROVENANCE LEDGER-i EKZAKT** (`GeneratedResidualLedger` në `DynamicRuntimeAccessManifest.cs` — i NDARË nga manifesti ynë production, i cili mbetet BOSH): çelësi = assembly + tip i NORMALIZUAR + anëtar + kind + detail + tool + version; **pariteti është DY-DREJTIMËSH** (çdo gjetje unike konsumon saktësisht një hyrje; çdo hyrje konsumohet; duplicate/unused/unmatched = fail-closed);
- `generated-dynamic-dangerous` dhe `generated-dynamic-service-location` janë **GJITHMONË blocking** — as nuk pranohen në ledger;
- çdo hyrje ledger-i lidhet MEKANIKISHT me **paketën e PIN-uar** në lockfile-t realë (tool-i i takon paketës; major.minor i versionit të dekoduar përputhet me versionin e zgjidhur): `Mediator.SourceGenerator/3.0.0.0` ↔ paketa `Mediator.SourceGenerator@3.0.2` (Direct); `Microsoft.AspNetCore.OpenApi.SourceGenerators/10.0.10.0` ↔ paketa `Microsoft.AspNetCore.OpenApi@10.0.10` (Direct);
- **normalizim I KUFIZUAR i identitetit OpenAPI** (Shtesa A): funksion PURE me pattern të ANKORUAR — prefiks fiks `<OpenApiXmlCommentSupport_generated>` + SAKTËSISHT NJË run hex + `__` + suffix jo-bosh; vetëm run-i zëvendësohet me placeholder, çdo segment tjetër mbetet byte-identik; çdo devijim (run që mungon/i dyfishtë/jo-hex/i gjatësisë tjetër, suffix bosh, kolizion dy tipesh reale) = refuzim/fail-closed. **DEVIATION e deklaruar:** review-i supozoi run 64-hex; matja mekanike mbi metadata (14/14 tipe) = **65** — sipas klauzolës 'rillogarit nga zero, mos hardcode numrat e reviewer-it', parametri është vlera empirike (`OpenApiHashRunLength = 65`), me meta-teste që refuzojnë 63/64/66.

**Counts të rillogaritura nga zero (HEAD real):** 19 gjetje generated UNIKE (1 Mediator + 18 OpenAPI: 9 `CreateDocumentationId`, 8 `GetTypeDocId`, 1 `TransformAsync`), të gjitha `generated-dynamic-unknown-member`; **zero dangerous/service-location**; ledger = 19 hyrje = seti aktual EKZAKT (barazi e provuar edhe eksplicit në test, jo vetëm përmes validatorit); 2 tools · 2 versione · 2 identitete të normalizuara + 1 identitet Mediator.

**Follow-up i hapur (jo gate i ri):** heqja e vetë mbetjeve të generatorëve i takon një vendimi për varësitë dhe mbetet për review të pavarur.

---

## 4a. Correction C2 — kufiri i VETËM i konsumit dhe degët malformed

**Blocker A (root cause i verifikuar):** `LoadVerified` e thërriste verifikuesin vetëm PARA `Assembly.Load`; meta-testi provonte thjesht praninë e emrit në një set (tri rreshta hardcoded); dhe disa konsumatorë e ndërtonin path-in vetë ose rilexonin MVID-in aktual jashtë gjeneratës.

**Fix:** `PlatformProjectCatalog.ConsumeVerifiedFrozenTarget<T>(entry[, generation], consumer)` — KUFIRI I VETËM me semantikë të detyruar: (1) një gjeneratë e pandryshueshme; (2) verifikim entry/path/SHA/MVID/mtime PARA; (3) konsumatori mbi path-in e verifikuar; (4) riverifikim i TË NJËJTIT entry ndaj SË NJËJTËS gjeneratë PAS; (5) rezultati vetëm pas post-check-ut; (6) gabimet e konsumatorit nuk fshihen. Shtohet edhe `LoadReferencedAssembly` që çdo ngarkim i një assembly-je production gjatë ecjeve të grafit kalon nga `LoadVerified`.

**Konsumatorët e rrugtuar (sweep mekanik, jo listë manuale):** `LoadVerified` · ENG-006 `ScanProduction` + owner-scan · ENG-007 `ScanVerified` + owner-scan + dy path-e fixture-sh · `RoleMetadataScanner` (IdentityRoleSurfaceTests) · `IdentityStoreIlScanner` (ConventionEnforcementTests) · `PlatformOutputAuthority` + testi R7-decoy (DependencyDirectionTests) · dy `Assembly.Load` në ConventionEnforcementTests → `LoadVerifiedByName`. **DependencyDirection nuk rilexon më MVID-in aktual:** klasifikimi përdor MVID-in e gjeneratës plus një verifikues të ngrirë (`VerifyFrozen`) që thirret në çdo vendim.

**Provat (§3):**

- **rendi EKZAKT** i vetë kufirit, i lexuar nga IL: `["VerifiedFrozenTargetPath", "Invoke", "VerifiedFrozenTargetPath"]` — dy verifikime REALE me konsumatorin MES tyre, jo prani emri në set;
- **guard i DERIVUAR i plotësisë:** ecje mbi IL-in e GJITHË assembly-t të testeve — çdo metodë jashtë katalogut që thërret `ReadMvid`, `Assembly.Load`, ose ndërton path targeti (`get_CanonicalTargetPath` + `Path.Combine`) = shkelje → RED. Një konsumator i ri direkt kapet automatikisht. Përjashtim i vetëm i deklaruar: `DynamicRuntimeAccessFixtures` për `Assembly.Load` (fixture RED e vetë skanerit, jo konsumator targeti);
- **provë BEHAVIORALE** mbi një artefakt SINTETIK (bin i testeve, zero output real): konsumator që e ndryshon artefaktin gjatë konsumimit → `catalog-generation-drift` PAS konsumimit dhe rezultati NUK kthehet; gjeneratë e helmuar → konsumatori NUK thirret fare;
- drift SHA-only / MVID-only / mtime-only mbeten të provuara veçmas nga C1.

**Blocker B — degët malformed me teste REALE:** u shtua `SyntheticMetadataPe` që emeton një PE me `MetadataBuilder`/Ecma335 me blobs të keqformuar, duke goditur EKZAKTËSISHT `unreadable-generated-code-attribute` (blob `[GeneratedCode]`), `unreadable-field-signature` dhe `unreadable-method-signature` (kjo e fundit = baseline më i vjetër i së njëjtës familje, jo root cause i C1). Varianti VALID i të njëjtit skelet → **zero gjetje `unreadable-*`** (zero false-positive). Fixture-i jo-PE mbulon vetëm `unreadable-assembly` dhe **nuk citohet** si evidencë për këto degë.

**Provat RED të C2 (5, të izoluara; rikthime edit-forward byte-identike):**

| Guard i hequr | RED |
|---|---|
| post-check-u i kufirit | **2** (rendi + prova behaviorale) |
| rend i përmbysur (konsumator para verifikimeve) | **2** |
| një konsumator kthehet te path-i direkt (RoleSurface) | **2** (guard-i i derivuar + theory-a e konsumatorëve) |
| dega `unreadable-generated-code-attribute` | **1** |
| dega `unreadable-field-signature` | **1** |

---

## 5. Fixtures dhe mapping fix → fixture → RED → GREEN

**RED (kapen me kind + declaring type + anëtar ekzakt + assembly + metodë burimore + arsye):** PoC-i historik `Type.GetType` me emër të ndërtuar në runtime · `GetType→GetProperty→GetValue` · `Assembly.Load→GetType→Activator.CreateInstance` · `GetMethod→Invoke` · `Type.InvokeMember` · `PropertyInfo.GetValue/SetValue` · `FieldInfo.GetValue/SetValue` · `MakeGenericType`/`MakeGenericMethod` · `Delegate.DynamicInvoke`/`CreateDelegate` · `dynamic` me emetim REAL `Binder.InvokeMember` + `CallSite\`1.Create` (+ `CallSite\`1.Target` si UNKNOWN) · `IServiceProvider.GetService(Type)` dhe `GetRequiredService(Type)` · atribuim i një metode `async` te metoda BURIMORE · assembly jo-PE = fail-closed · escape hatch publik i owner-it.

**GREEN:** homonime SINTETIKE me `FullName` tjetër · operacione SAFE EXACT të tipizuara (`typeof`, emër/assembly) · API i tipizuar i ndërmjetësuar · manifest production BOSH i asertuar.

**Meta-teste:** heqja e secilës familje të monitoruar e zhduk zbulimin përkatës (8 rreshta); rregulli UNKNOWN dhe SAFE-lista provohen mbajtëse; validatori i manifestit raporton dead/duplicate/wildcard/empty/uncovered/unallowable/unknown-project.

**Provat RED të ekzekutuara (asnjë e commit-uar; rikthim me edit forward + verifikim byte-identik):**

| Guard i hequr | RED |
|---|---|
| `-p:BuildProjectReferences=false` (ENG-008) | **8 teste** — përfshirë drift-in, permutimet me seed dhe identitetin MVID |
| çdo familje e monitoruar (ENG-006) | fixture-i i saj bëhet i pazbuluar (8 rreshta meta-testi) |

**PoC-i historik i ENG-006 nuk u fshi:** statusi i tij ndryshon në *“caught by ENG-006 guard”* sepse një test e provon realisht — skaneri statik i tipeve nuk e sheh, ndërsa ENG-006 e kap te call-site-i.

### Correction C1 — fixtures/prova shtesë (mapping finding → fix → fixture → RED → GREEN)

| Finding | Fix | Fixtures | RED e provuar (heqje dege → teste RED, rikthim byte-identik) |
|---|---|---|---|
| Generated self-exemption | provenance ledger + zero `StartsWith→continue` | 11 forma: `<>c`, `<>c__DisplayClass`, local function, LINQ-lambda, `[CompilerGenerated]` manual, `[GeneratedCode("UnknownTool","9.9")]`, tool REAL + tip jashtë ledger-i, nested i pashënuar, generated dangerous, generated service-location, generated unknown jashtë ledger-i; inverse GREEN = output-i real i generatorëve që konsumon ledger-in EKZAKT | validator i anashkaluar → **3 teste RED** |
| Lambda attribution | dega `unattributed` fail-closed | 4 format e kompajlerit | dega e neutralizuar → **1 test RED** |
| Owner public fields | field-scan me dekodim fail-closed | `LeakedType` (static readonly `Type`), `LeakedProvider` (instance `IServiceProvider`), `LeakedDelegates` (generic me `Delegate`), `LeakedFields` (array `FieldInfo[]`); GREEN: `SafeCount`/`SafeLabel`/`SafeId` | dega field-scan e fikur → **4 teste RED** |
| Fingerprint consumer | `VerifiedFrozenTargetPath` para+pas çdo skanimi | drift sintetik SHA-only/MVID-only/mtime-only + outsider + gjeneratë e helmuar kundër `ScanProduction`/owner-scan | krahasimi 3-dimensional i dobësuar → **4 teste RED** |
| Deep immutability | `ImmutableArray`/`FrozenDictionary`/`FrozenSet` deri në vlerat nested | mutim përmes interface-ve hedh; 32 thirrës paralelë; tipe komponentësh të provuar | `FrameworkAssets` → `HashSet` → **1 test RED** |

**Meta-teste normalizimi:** forma ekzakte → normalizohet; 63/64/66 → refuzohet; jo-hex → refuzohet; dy runs → refuzohet; run pa marker → i paprekur; prefix/suffix byte-identikë; kolizion → fail-closed; ledger bosh me gjetje / hyrje e vdekur / duplikat / kind i palejuar / tool-version mismatch → fail.

**Kontrata e konsumatorëve (Blocker C):** `VerifiedFrozenTargetPath(entry, generation)` = API e VETME — anëtarësi ekzakte + SHA-256 + MVID + WriteTimeUtc kundër vlerave TË NGRIRA, dështimi = `catalog-generation-drift`, kurrë refresh i heshtur; konsumohet PARA dhe PAS çdo skanimi nga ENG-006 `ScanProduction`, owner-scan dhe `LoadVerified` (provë MEKANIKE mbi IL me token-a MethodDef + provë sjelljeje me gjenerata të helmuara).

**Performanca (Shtesa E):** provë deterministike — skaneri ENG-006 nuk thërret asnjë API hashimi/mtime (IL-prova `Skaneri_ENG006_nuk_llogarit_fingerprint_vete`); fingerprint-et llogariten vetëm në çiftin before/after per artifact (2 × SHA+MVID+mtime për artifact për konsumator; 24 verifikime për skanim të plotë 12-projektësh); snapshot-i i ngrirë ripërdoret (`Assert.Same`); drift-i post-scan vazhdon të kapet. **Matje wall-clock:** arch e plotë PARA C1: 57–59s ×3 · PAS C1: 57–59s ×3 (**rritje ~0%**, shumë nën 2×); targeted ENG-006: ~15–16s (63 teste) · ENG-008: ~15–16s (20 teste); build inkremental ~1s i pandryshuar.

---

## 6. Evidence — LOKALE vs CI (të ndara)

**LOKALE**, nën `HG-PRE-B2-ENG006-ENG008-LOCAL-DISK-WAIVER`:

- `dotnet build src/platform/Idata.Platform.slnx` → **0 warnings, 0 errors**;
- baseline mekanik para editimit: **178/178**;
- pas increment-it: **237/237, zero skip** ×3; pas correction C1: **262/262**; pas **correction C2: 270/270, zero skip**, **tri ekzekutime radhazi** (+8 teste C2);
- targeted (të rimatura pas C2): ENG-008 **27/27**, ENG-006 **65/65** (para C2: 21/63 — vlera e mëparshme 20 për ENG-008 ishte e pasaktë; para C1: 12/47);
- permutime me **seed-e fikse 1337 / 20260803 / 42** → rezultate identike, e njëjta gjeneratë;
- disk: min **6.69 GiB**, max **6.78 GiB** (kurrë nën 2.0 GiB; zero ENOSPC).

**NUK u ekzekutua lokalisht:** Docker/PostgreSQL, IntegrationTests, UnitTests, contracts/drift, legacy checks, Angular. **Evidencë vetëm nga CI.**

---

## 7. Threat boundary

**Pretendohet:** production assemblies të inventarizuara nga katalogu autoritativ; call-site-t ekzekutuese në IL + DLR-patterns e provuara; atribuim deri te metoda burimore me fail-closed kur nuk provohet; sipërfaqja publike e DEKLARUAR e owner-ave.

**NUK pretendohet:** brendësia e paketave jashtë platform assemblies · kod i gjeneruar nga source-generator-ë (kufi i deklaruar §4) · sjellja e serverit DB · `calli`/function pointers si target i zgjidhur · mbulim universal i çdo API-je të ardhshme. Një literal string pa call-site **nuk** është gjetje. **Zero pretendim "universal", "bypass-free" ose "production-ready".**

---

## 8. Rollback

Hiqen file-at e rinj të testeve (`CatalogGenerationStabilityTests.cs`, `DynamicRuntimeAccessTaxonomy.cs`, `DynamicRuntimeAccessScanner.cs`, `DynamicRuntimeAccessManifest.cs`, `DynamicRuntimeAccessFixtures.cs`, `DynamicRuntimeAccessBoundaryTests.cs`) dhe kthehen ndryshimet e `PlatformProjectCatalog.cs` + rreshtat e status-sync-ut. **Zero prekje** e `src/`, migrimeve, schema-s, `ci.yml`, dependency-ve ose lockfile-ve — rollback-u është plotësisht lokal ndaj testeve/docs.

---

## 9. Gates / risqe OPEN

- **`ENG-006` = OPEN** (implementim në këtë PR, pending independent review + Human Gate).
- **`ENG-008` = OPEN** (implementim në këtë PR) — kufi besueshmërie, **jo** bypass production i provuar.
- **`ENG-007` = CLOSED / MERGED** (`8f7a22c`) — threat boundary-t e tij të pandryshuara.
- **`ADR-026` = OPEN**; **DB-level isolation = NOT IMPLEMENTED**; policy/analyzer **≠** DB-level isolation.
- `ADR-025`/`ADR-027` OPEN · `ENG-004` fallback AKTIV · `ENG-005` OPEN.
- **Follow-up:** reflection i kodit të gjeneruar brenda assembly-ve (§4).
- **B.2 / B.3 = NOT STARTED / BLOCKED**; Step B/C, SEF/ATK, PREFLIGHT, Tier 2 = NO-GO; **production Go = BLOCKED**.

**Radha:** Fable delta-review → Codex PM/architecture/security review → Human Gate i Pronarit → merge vetëm me autorizim të ri → Notion + Slack pas merge-it → vetëm pastaj, me goal/prompt të ri, B.2.
