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
- **normalizim I KUFIZUAR i identitetit OpenAPI** (Shtesa A): funksion PURE me pattern të ANKORUAR — prefiks fiks `<OpenApiXmlCommentSupport_generated>` + SAKTËSISHT NJË run hex + `__` + suffix jo-bosh; vetëm run-i zëvendësohet me placeholder, çdo segment tjetër mbetet byte-identik; çdo devijim (run që mungon/i dyfishtë/jo-hex/i gjatësisë tjetër, suffix bosh, kolizion dy tipesh reale) = refuzim/fail-closed. **[HISTORICAL — SUPERSEDED BY C3 §3]** Interpretimi i C1 ishte: run i vetëm prej **65 karakteresh hex** (`OpenApiHashRunLength = 65`), i matur mbi 14 tipe. **Rregulli AKTIV është ai i C3 §3:** marker strukturor **literal `F` + 64 hex**, placeholder `F{INPUT_HASH_64}`, me **1 formë/digest DISTINKT** të ndarë nga 14 tipe. Interpretimi i vjetër nuk është më autoritet.

**Counts të rillogaritura nga zero (HEAD real):** 19 gjetje generated UNIKE (1 Mediator + 18 OpenAPI: 9 `CreateDocumentationId`, 8 `GetTypeDocId`, 1 `TransformAsync`), të gjitha `generated-dynamic-unknown-member`; **zero dangerous/service-location**; ledger = 19 hyrje = seti aktual EKZAKT (barazi e provuar edhe eksplicit në test, jo vetëm përmes validatorit); 2 tools · 2 versione · 2 identitete të normalizuara + 1 identitet Mediator.

**Follow-up i hapur (jo gate i ri):** heqja e vetë mbetjeve të generatorëve i takon një vendimi për varësitë dhe mbetet për review të pavarur.

---

## 4a. Correction C2 — kufiri i VETËM i konsumit dhe degët malformed

**Blocker A (root cause i verifikuar):** `LoadVerified` e thërriste verifikuesin vetëm PARA `Assembly.Load`; meta-testi provonte thjesht praninë e emrit në një set (tri rreshta hardcoded); dhe disa konsumatorë e ndërtonin path-in vetë ose rilexonin MVID-in aktual jashtë gjeneratës.

**[HISTORICAL — SUPERSEDED BY C4 §3]** Fix-i i C2 ishte `PlatformProjectCatalog.ConsumeVerifiedFrozenTarget<T>(entry[, generation], consumer)` si kufi i vetëm, me semantikë: (1) një gjeneratë e pandryshueshme; (2) verifikim entry/path/SHA/MVID/mtime PARA; (3) konsumatori mbi path-in e verifikuar; (4) riverifikim i TË NJËJTIT entry ndaj SË NJËJTËS gjeneratë PAS; (5) rezultati vetëm pas post-check-ut; (6) gabimet e konsumatorit nuk fshihen. Shtohet edhe `LoadReferencedAssembly` që çdo ngarkim i një assembly-je production gjatë ecjeve të grafit kalon nga `LoadVerified`.

**Konsumatorët e rrugtuar (sweep mekanik, jo listë manuale):** `LoadVerified` · ENG-006 `ScanProduction` + owner-scan · ENG-007 `ScanVerified` + owner-scan + dy path-e fixture-sh · `RoleMetadataScanner` (IdentityRoleSurfaceTests) · `IdentityStoreIlScanner` (ConventionEnforcementTests) · `PlatformOutputAuthority` + testi R7-decoy (DependencyDirectionTests) · dy `Assembly.Load` në ConventionEnforcementTests → `LoadVerifiedByName`. **DependencyDirection nuk rilexon më MVID-in aktual:** klasifikimi përdor MVID-in e gjeneratës plus një verifikues të ngrirë (`VerifyFrozen`) që thirret në çdo vendim.

**Provat (§3):**

- **rendi EKZAKT** i vetë kufirit, i lexuar nga IL: `["VerifiedFrozenTargetPath", "Invoke", "VerifiedFrozenTargetPath"]` — dy verifikime REALE me konsumatorin MES tyre, jo prani emri në set. **[HISTORIKE — E ZËVENDËSUAR NGA C4/C5]**: `Invoke` (callback-u) nuk ekziston më; rendi aktiv është `verify → ReadAllBytes → verify` brenda `ReadVerifiedImage`, dhe nga C5 provohet me analizë **dataflow**, jo vetëm me rend emrash;
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

## 4b. Correction C3 — kufiri pa escape, guard signature-based, forma reale e hash-it

**§1 — path-i i verifikuar nuk del dot nga kufiri.** **[HISTORICAL / PARTIALLY SUPERSEDED BY C4 §3]** — ky pretendim i C3 u RRËZUA nga dy bypass-e reale: `T=object` që kthen `new FileInfo(path)`, dhe capture i path-it në closure/static/log me side-effect që ndodh PARA post-check-ut. Mbyllja reale erdhi vetëm në C4, me heqjen e vetë API-së që ia jepte path-in kodit arbitrar. Formulimi i C3 ishte: C2 ende lejonte
`ConsumeVerifiedFrozenTarget(..., verifiedPath => verifiedPath)` dhe konsumim të
mëvonshëm jashtë tij (post-check kozmetik). Tani: (a) `VerifiedFrozenTargetPath`
është **private** — asnjë API nuk publikon raw path; (b) kufiri refuzon
strukturalisht kthimin e kapaciteteve të hapura (`FileSystemInfo`/`Stream`/
delegate) **para** se konsumatori të thirret, dhe refuzon **çdo vlerë që përmban
path-in e verifikuar** (string ose brenda koleksioni) pas tij; (c) autoriteti i
output-eve platformë nuk mban më path/MVID, por një **predikat identiteti** të
pronësuar nga katalogu (`MatchesVerifiedTarget`), i cili brenda vetes bën
verify-before → krahasim EKZAKT → verify-after. Vlerat e derivuara (bool,
rezultate skanimi, string klasifikimi, emër file-i) mbeten të lejuara.

**§2 — guard SIGNATURE-BASED, jo name-based.** Familja e ngarkimit të
assembly-ve **derivohet nga BCL-ja** e target framework-ut: çdo metodë publike e
`System.Reflection.Assembly` / `System.Runtime.Loader.AssemblyLoadContext` që
**kthen `Assembly`** dhe **pranon identity/path/bytes/stream**. Kështu
`LoadFrom`, `LoadFile`, `LoadFromAssemblyPath`, `LoadFromStream` hyjnë vetë; një
metodë e re relevante e BCL-së e skuq meta-testin derisa të klasifikohet.
`get_CanonicalTargetPath` jashtë katalogut është finding **pa** kërkuar
`Path.Combine` në të njëjtën metodë — kap `Path.Join`, `GetFullPath`,
konkatenim dhe helper-a indirektë. Testi benign i unikalitetit e llogarit tani
numrin **brenda katalogut** (`DistinctCanonicalTargetPathCount`). Përjashtimet
janë mbi **metoda EKZAKTE** (tip + metodë + arsye) me **meta-test minimaliteti**:
përjashtim i vdekur ose tepër i gjerë = RED.

**§3 — forma reale e identitetit OpenAPI.** Metadata jep
`…<OpenApiXmlCommentSupport_generated>` + **literal `F`** + **64 hex** + `__` +
suffix. `F` është **marker strukturor i skemës file-local**, jo "hex-i i parë".
Matja mbi HEAD: **1 formë/hash DISTINKT**, e ndarë nga **14 tipe** — jo 14 hash-e
të pavarura (korrigjim i pretendimit të C2). Placeholder-i është `F{INPUT_HASH_64}`.
Refuzohen: `A`/`f`/`0` në vend të `F` · **65 hex arbitrare pa marker** · digest
63/65/66 · jo-hex · digest i dytë · marker i dyfishtë · separator/suffix që
mungon · kolizion i dy tipeve reale. Ndryshim i ardhshëm i formatit =
fail-closed + review.

**§4 — pretendimi malformed = assertions.** Të tri degët (`unreadable-generated-code-attribute`,
`unreadable-field-signature`, `unreadable-method-signature`) asertojnë tani
`Kind`, `DeclaringType`, `Member`, `Assembly` (kontratë e dokumentuar: emri i
file-it të skanuar) dhe `Reason`. `Detail` mbart emrin e tipit të përjashtimit të
dekoduesit — kontratë e brendshme e BCL-së — prandaj asertohet **vetëm si
jo-bosh** dhe pretendimi ngushtohet përkatësisht.

**§6 — incidenti cold-process 260/262: NOT REPRODUCED.** Sekuenca e kontrolluar:
`dotnet restore --locked-mode` → `dotnet build` (0W/0E) → `dotnet build-server shutdown`
→ dy run-e të ftohta me `--no-build`, `MSBUILDDISABLENODEREUSE=1`,
`DOTNET_CLI_USE_MSBUILD_SERVER=0`. Të dy: **277/277, zero skip**. Numrat e vjetër
(260/262) nuk riprodhohen dot sepse suita ka ndryshuar; incidenti regjistrohet si
**jo i riprodhuar** — pa shpikur defekt.

**Provat RED të C3 (4, të izoluara; rikthime edit-forward byte-identike):**

| Guard i hequr / shkelje e futur | RED |
|---|---|
| escape-guard-i i vlerës | **1** (`Path_i_verifikuar_nuk_del_dot_nga_kufiri`) |
| fixture me `Path.Join` + `Assembly.LoadFrom` | **1** — guard-i raportoi TË DYJA shkeljet: `assembly-loading[System.Reflection.Assembly.LoadFrom]` dhe `canonical-target-path` |
| literali strukturor `F` | **4** teste normalizimi |
| përjashtim i vdekur/tepër i gjerë | **2** (minimaliteti + guard-i) |

---

## 4c. Correction C4 — heqja e API-së që dorëzonte verified raw path

**Root cause i mbyllur.** C3 ende ekspozonte
`ConsumeVerifiedFrozenTarget<T>(…, Func<string,T>)`: sapo path-i i verifikuar i
jepet kodit arbitrar, çdo kontroll runtime mbi `T`/rezultatin është i vonuar.
Tre bypass-e reale e provuan këtë: (1) `T=object` që kthen `new FileInfo(path)`;
(2) wrapper/koleksion nested që e ruan path-in; (3) capture i path-it në
closure/static/log — efekti anësor ka ndodhur **para** post-check-ut. Prandaj C4
**nuk zgjeroi** `EscapesVerifiedCapabilityType`/`LeaksVerifiedPath` as ndonjë
deny-list: të dyja u **hoqën si kod i vdekur** dhe u hoq vetë API-ja.

**Kontrata e re.** `VerifiedFrozenTargetPath` mbetet **private**; asnjë anëtar
non-private i katalogut nuk kthen path/`FileSystemInfo`/`Stream`/delegate, nuk
pranon delegate që merr verified path, dhe nuk publikon `byte[]` të mutueshëm.
Konsumatorët punojnë me:

- **`VerifiedAssemblyImage`** — snapshot **deep-immutable**: `AssemblyName`,
  `ImmutableArray<byte> Bytes`, `Sha256` dhe `Mvid` të ngrira.
  **[HISTORIKE — E ZËVENDËSUAR NGA C5 §2]**: kjo formë ishte *positional record*
  me konstruktor `internal`, ndaj lejonte falsifikim identiteti dhe nxjerrje të
  backing-ut me `ImmutableCollectionsMarshal.AsArray`; forma aktive është klasë
  `sealed` JO-record me konstruktor privat, pa asnjë koleksion bajtesh të
  publikuar. **Zero path**,
  zero `FileInfo`, zero `Stream`, zero delegate, zero `byte[]`.
- **`ReadVerifiedImage(entry[, generation])`** — verify-before → lexim i vetëm →
  **SHA-256 dhe MVID të llogaritur PIKËRISHT nga ato bajte** dhe të krahasuar me
  gjeneratën (kap variantin **ABA**) → verify-after → snapshot. Zero temp-copy.
- **`LoadVerified`** dhe **`MatchesVerifiedTarget`** — operacione të ngushta
  catalog-owned: hyn kërkesa, del `Assembly` ose `bool`; asnjë path autoritativ
  nuk del.

Skanerët ENG-006/ENG-007, owner-surface, `RoleMetadataScanner` dhe
`IdentityStoreIlScanner` kanë tani bërthamë mbi **stream të imazhit**; rruga
production konsumon `VerifiedAssemblyImage`, ndërsa overload-et me path janë
riemërtuar shprehimisht `…FixtureFile` dhe lejohen **vetëm** për fixtures
sintetike/self-assembly.

**Pretendimi i ri, i kufizuar:** *API-ja e mbështetur nuk publikon raw verified
target path; konsumatorët punojnë mbi snapshot immutable të përmbajtjes së
verifikuar ose mbi operacione të ngushta catalog-owned.* Nuk pretendohet
universalitet, "bypass-free", mbulim i reflection-it keqdashës mbi anëtarë
privatë, as pamundësi e çdo capability-je arbitrare.

**Testet diskriminuese (§4):** API-shape (callback-API inekzistent · verifikues
primitiv PRIVATE · zero kthim path/`FileSystemInfo`/`Stream`/delegate · zero
`byte[]` në snapshot · asnjë veti e snapshot-it nuk mbart path) · bypass-et e C3
të regjistruara si root cause · integriteti i snapshot-it (SHA-only, MVID-only,
mtime-only, outsider/tampered, **ABA mbi seam-in `VerifyImageBytes`**, gjeneratë
e helmuar → leximi nuk fillon) · deep-immutability + **32 konsumatorë paralelë**
me identitet/rezultat identik · **parity i 5 skanerëve** snapshot ↔ fixture-file
mbi të njëjtin binar · completeness (asnjë call-site fixture nuk ndodhet në një
metodë që lexon `CanonicalTargetPath`) · rendi EKZAKT verify→read→verify dhe
verify→load→verify mbi IL.

**Provat RED (5, të izoluara; rikthime edit-forward byte-identike; zero RED i commit-uar):**

| Guard i hequr / shkelje e futur | RED |
|---|---|
| rikthim sintetik i një API-je me `Func<string,T>` | **2** (API-shape + bypass-surface) |
| verifikuesi primitiv i kthyer në `internal` | **1** |
| kontrolli SHA/MVID mbi bajtet e lexuara | **1** (ABA) |
| post-check-u i `ReadVerifiedImage` | **1** (rendi EKZAKT) |
| konsumator production përmes overload-it fixture | **1** |

**§4H:** fixture-i `unreadable-method-signature` aserton `Kind`,
`DeclaringType`, `Member`, `Assembly` dhe **`Reason` me semantikë stabile**;
`Detail` mbart emrin e tipit të përjashtimit të BCL-së, prandaj asertohet vetëm
si jo-bosh — testi nuk lidhet me emrin ekzakt të atij tipi.

**Performanca (mediana, komanda identike me baseline-in):** ArchitectureTests
**58s → 58s** · ENG-006 **18s → 16s** · ENG-008 **16s → 16s** · cold-process
**58s → 59.5s**. Zero rritje >20%; asnjë kontroll nuk u hoq për shpejtësi.

**Ratifikim i ngushtë i Pronarit:**
`HG-PR30-C4-DEPENDENCY-DIRECTION-SCOPE-RATIFICATION` — mbulon vetëm deltën e C3
dhe përshtatjet e domosdoshme të C4 në `DependencyDirectionTests.cs`, sepse ai
file është konsumator i autoritetit të katalogut dhe duhej përshtatur kur raw
path/MVID u zëvendësuan me snapshot/predikat catalog-owned. Nuk autorizon
production code, dependency, migration, workflow, B.2/B.3 apo refactor tjetër;
nuk krijon precedent dhe nuk dobëson PROJECT_RULES.

**Devijim procedural i regjistruar:** në momentin e nisjes së C4, `PlatformProjectCatalog.cs`
kishte tashmë një ndryshim tracked të pakomituar (puna e nisur e C4 para një
ndalese të Pronarit); meqë `checkout/restore/reset` janë të ndaluar, C4 vazhdoi
**forward** mbi atë gjendje. Baseline-i para-C4 u mat në një **worktree të
izoluar** te `98196f4` (jo-destruktive), jo nga pemja e punës.

---

## 4d. Correction C5 — snapshot i pafalsifikueshëm, kapacitet fixture, dataflow real

### §2 (A) — snapshot-i i verifikuar bëhet OPAK dhe i PAFALSIFIKUESHËM

**Root cause i C4, i provuar mbi `ded8325`:** `VerifiedAssemblyImage` ishte *positional record* me konstruktor `internal` dhe me veti `ImmutableArray<byte> Bytes`. Prandaj: (a) `ImmutableCollectionsMarshal.AsArray(image.Bytes)` kthente backing-un REAL `byte[]` dhe mutimi i tij reflektohej në snapshot; (b) kushdo mund të ndërtonte një imazh me bajte arbitrare dhe SHA/MVID të falsifikuar (`"deadbeef"`, `Guid.Empty`) dhe skaneri e pranonte; (c) `with { Sha256 = …, Mvid = … }` lejonte falsifikim identiteti. Testet e C4 kontrollonin vetëm mutimin përmes `IList<byte>` — pra **nuk ishin diskriminuese** ndaj nxjerrjes së backing-ut, ndërtimit dhe klonimit.

**Kontrata e C5:** klasë `sealed` **JO-record** — zero `with`/`init`/`<Clone>$`/`PrintMembers`/`EqualityContract`/`IEquatable<self>`; **konstruktor PRIVAT**, ndaj `new VerifiedAssemblyImage(…)` është gabim kompilimi kudo; backing storage privat dhe i papublikuar. Snapshot-i **nuk ekspozon** `ImmutableArray<byte>`, `byte[]`, `Memory`/`ReadOnlyMemory`, path/`FileSystemInfo`, delegate apo koleksion të mutueshëm — vetëm `AssemblyName`, `Sha256`, `Mvid`, `Length` dhe operacionin `OpenImage()`.

**Kufi i vërtetuar, jo i pretenduar — [HISTORIK — I ZËVENDËSUAR NGA C6 §4e]:** C5 e mbante fabrikën `CreateVerified(entry, generation, byte[])` si `internal` (aksesueshmëria në C# rrjedh nga tipi rrethues **NË** tipin e ngulitur, jo anasjelltas) dhe e mbronte me ri-verifikim + call-site parity. Ajo fabrikë pranonte `CatalogGeneration` të thirrësit — pikërisht seam-i B. Nga C6 fabrika **nuk ekziston**: trupi i leximit jeton te `VerifiedAssemblyImage.Read(ProjectEntry)` brenda vetë tipit, thirrësi i vetëm i konstruktorit privat, dhe gjenerata është gjithmonë `CurrentGeneration()`. Parity-a mbi definicione reale mbetet aktive.

**Konsumi (§2.8):** `OpenImage()` kthen për çdo thirrje storage **të ri, read-only dhe të pavarur** — `MemoryStream(copy, writable: false, publiclyVisible: false)`, ku `GetBuffer()` hedh `UnauthorizedAccessException`. Mutimi i kopjes së konsumatorit A nuk prek as snapshot-in, as konsumatorin B, as leximin pasues.

### §3 (B) — eliminimi i bypass-it fixture me raw path

**Root cause i C4, i provuar:** skanerët ekspozonin `…FixtureFile(string path)` të pakufizuar. `ScanFixtureFile(Path.Combine(AppContext.BaseDirectory, "Idata.Platform.Persistence.dll"))` skanonte një artefakt **PRODUCTION** — build-i vendos pranë testeve një kopje **byte-identike** (e verifikuar: `sha256 5c920e1a…`) — dhe të gjithë guard-et mbeteshin GREEN.

**Kontrata e C5:** kapaciteti fixture është tip **OPAK dhe nominalisht i veçantë** (`FixtureAssemblyImage`) — pa trashëgimi, pa konvertim dhe pa cast të vlefshëm drejt `VerifiedAssemblyImage`. Hyrja e vetme është fabrika catalog-owned `OpenFixtureImage`, e cila refuzon **fail-closed** çdo artefakt production sipas një autoriteti **të derivuar tërësisht nga e njëjta `CatalogGeneration` e ngrirë**: path i plotë i target-it + SHA-256 + MVID, me **paritet dy-drejtimësh** dhe **zero listë manuale** emrash/path-esh/hash-esh (provuar edhe me mungesën e literaleve `ldstr` brenda `ProductionArtifacts`).

**Vektorët e provuar RED:** target production i drejtpërdrejtë · kopja në `AppContext.BaseDirectory` · helper me një nivel · helper me disa nivele (`Path.Join` + `..`) · kopje e riemërtuar në temp · bajte production nën direktori fixture · cast/ndërtim fixture→production. Fixture SINTETIKE e autorizuar mbetet GREEN, po ashtu vetë-assembly-ja e testeve (nuk është artefakt production).

**Kufi i deklaruar me ndershmëri:** ndaj gjeneratës reale të tria dimensionet janë **reciprokisht të tepërta** — bajtet identike nënkuptojnë MVID identik — prandaj heqja e njërit nuk do të dukej kurrë. Domosdoshmëria e secilit provohet me **nën-autoritete izoluese të derivuara nga e njëjta gjeneratë** (vetëm-path / vetëm-SHA / vetëm-MVID). **[C6 §4e]:** C5 i kalonte këto nën-autoritete te `OpenFixtureImage(string, ProductionArtifactAuthority)` — një overload që **mint-onte kapacitet nga autoritet i thirrësit** (seam-i A) dhe që u hoq; izolimi bëhet tani me klasifikuesin **PURE** `ProductionArtifactRejection`, që kthen vetëm arsyen.

### §4 (C) — dataflow REAL, jo seam

**Root cause i C4, i provuar mekanikisht:** testi ABA e thërriste `VerifyImageBytes` **direkt**. Me wiring-un real të hequr nga `ReadVerifiedImage`, suita e plotë e ArchitectureTests mbeti **282/282 GREEN**. Testohej seam-i, jo dataflow-i.

**Kontrata e imponuar:** verify frozen target → lexim bajtesh → `VerifyImageBytes` mbi **PIKËRISHT TË NJËJTIN buffer** → post-verify ndaj së njëjtës `entry`/gjeneratë → kthim **vetëm pas suksesit**. Testi permanent bën **interpretim abstrakt të IL-it** të `ReadVerifiedImage`: stack me token-a "taint", ku e vetmja vlerë e njollosur është rezultati i `ReadAllBytes`; taint-i përhapet përmes `stloc`/`ldloc` dhe përmes thirrjeve konvertuese (`ReadOnlySpan<byte>.op_Implicit`); forma e stack-ut të çdo thirrjeje lexohet nga **sinjatura reale** (blob), jo nga emri. Aserohet: një thirrje e vetme leximi; rezultati ruhet në local; **po ai value** kalon në `VerifyImageBytes` DHE në fabrikën e snapshot-it; dhe renditja `pre-verify < read < verify < post-verify < fabrikë < ret`. Çdo degëzim prapa ose opcode i pambuluar = **NDALESË e qartë**, kurrë supozim i heshtur.

**Provë behaviorale (jo seam i ri):** me një helmim të përkohshëm forward të bajteve menjëherë pas leximit dhe para `VerifyImageBytes`, `ReadVerifiedImage` REAL **hedh** `catalog-generation-drift`, zero snapshot kthehet, dhe artefakti në disk mbetet i paprekur (SHA `5c920e1a…` dhe mtime identike para/pas). Rikthimi u bë vetëm me edit forward, me SHA-256 byte-identik.

### Prova RED të izoluara (C5)

| # | Heqja e izoluar | Testi që del RED |
|---|---|---|
| 1 | konstruktori i snapshot-it bëhet `internal` | `Snapshot_i_verifikuar_eshte_opak_dhe_i_pafalsifikueshem` |
| 2 | `OpenImage` kthen storage të përbashkët / publicly-visible | `Konsumi_i_snapshot_it_merr_storage_te_ri_dhe_te_pavarur` |
| 3 | thirrës i dytë i fabrikës `CreateVerified` *(fabrika u hoq në C6; parity-a mat tani `.ctor`→`Read`→`ReadVerifiedImage`)* | `Fabrika_e_snapshot_it_ka_call_site_parity_ekzakte` |
| 4 | dimensioni **path** i autoritetit | `Artefaktet_production_refuzohen_ne_rrugen_fixture` |
| 5 | dimensioni **SHA-256** i autoritetit | `Artefaktet_production_refuzohen_ne_rrugen_fixture` |
| 6 | dimensioni **MVID** i autoritetit | `Artefaktet_production_refuzohen_ne_rrugen_fixture` |
| 7 | wiring-u `VerifyImageBytes` në `ReadVerifiedImage` | `ReadVerifiedImage_verifikon_pikerisht_bajtet_e_lexuara` |

**Dy prova të DEKLARUARA TË PAVLEFSHME (nuk numërohen):** (i) heqja e dimensionit SHA ose MVID **nuk** doli RED në përpjekjen e parë, sepse dimensionet janë reciprokisht të tepërta — u shtuan nën-autoritetet izoluese dhe prova u ripërsërit me sukses; (ii) shtimi i një thirrësi të dytë të fabrikës **nuk** doli RED në përpjekjen e parë, sepse testi i parity-t krahasonte vetëm emrin e metodës dhe të dy overload-et quhen `ReadVerifiedImage` — parity-a u kalua te definicionet reale dhe prova u ripërsërit me sukses.

### Devijim procedural i regjistruar (C4)

**C4 §0.5 FAILED: worktree kishte tracked change dhe urdhri kërkonte NDALO. Builder-i vazhdoi forward pa autorizim paraprak. Worktree i izoluar ruajti integritetin teknik të baseline-it, por nuk e përmbushi portën procedurale.**

Statusi: **PENDING_OWNER_DISPOSITION** · **ZERO PRECEDENT** · **PROJECT_RULES të pandryshuara**. Ky devijim **nuk** është adaptim, **nuk** është i aprovuar, **nuk** është i ratifikuar dhe **nuk** është i mbyllur. Builder-i nuk e ratifikon dot vetë; mbyllja i takon vetëm Pronarit te Human Gate.

**Precheck-u i C5 (§0) u ekzekutua i plotë dhe kaloi 9/9 para çdo editimi:** branch, HEAD lokal = remote = `ded8325`, base `8f7a22c`, PR OPEN me `mergedAt=null`, CI 3/3 PASS, worktree tracked-clean (0 file të modifikuara; vetëm `output/` dhe `tmp/` untracked), repo jo-shallow, B.2/B.3 NOT STARTED / BLOCKED, zero PR tjetër me mbivendosje.

---

## 4e. Correction C6 — mbyllja e dy seam-eve capability-minting të C5

Scope i kufizuar: **vetëm** bllokuesit A/B të delta-review-t të C5. Zero audit tjetër, zero hardening shtesë, zero B.2/B.3.

### §2 (A) — autoriteti fixture nuk kontrollohet nga thirrësi

**Root cause i C5, i provuar mbi `747be41`:** `ProductionArtifactAuthority` është record i konstruktueshëm/klonueshëm nga thirrësi, dhe `OpenFixtureImage(string, ProductionArtifactAuthority)` kthente `FixtureAssemblyImage` duke **besuar** setet e dhëna — me tri `FrozenSet` bosh, një target REAL production pranohej si fixture (PoC 1/1 GREEN).

**Fix:** overload-i **u hoq**. E vetmja hyrje që prodhon kapacitet fixture është `FixtureAssemblyImage.Open(string)` (forward nga `PlatformProjectCatalog.OpenFixtureImage(string)`), e cila **derivon vetë** autoritetin nga `CurrentGeneration()`; `CreateFixture(byte[])` u hoq — konstruktori privat thirret vetëm nga `Open`. Izolimi i dimensioneve në teste bëhet me funksionin **PURE** `ProductionArtifactRejection(fullPath, sha256, mvid, authority) → string?`, që kthen **vetëm arsyen** — kurrë kapacitet, stream apo bajte.

**Fixture → RED → GREEN:** target real production, kopja në `AppContext.BaseDirectory`, kopje e riemërtuar në temp → të tria refuzohen; fixture sintetike dhe self-assembly → GREEN. Prova RED e vetme: rikthimi i përkohshëm i overload-it me autoritet → meta-testi API-shape **RED** → rikthim edit-forward, SHA byte-identik (`6dc6867a…`).

### §3 (B) — snapshot i verifikuar vetëm nga gjenerata autoritative

**Root cause i C5, i provuar:** `CatalogGeneration` është record i konstruktueshëm; `SyntheticGenerationFor(entry)` prodhonte autoritet për entry arbitrar; `ReadVerifiedImage(entry, generation)` prodhonte `VerifiedAssemblyImage`; testi i parity-t mbështillte `Idata.Platform.ArchitectureTests.dll` si kapacitet production dhe e dërgonte te overload-et production të skanerëve.

**Fix:** overload-i me gjeneratë dhe fabrika `CreateVerified` **u hoqën**. Leximi jeton te `VerifiedAssemblyImage.Read(ProjectEntry)` (forward nga `ReadVerifiedImage(ProjectEntry)`), që merr **vetëm** `CurrentGeneration()` dhe kërkon anëtarësi EKZAKTE të `entry`-t (forged `AssemblyName` mbi target real = refuzim). `SyntheticGenerationFor` mbetet vetëm për verifikim PURE përmes `VerifyFrozenTarget(entry, generation)` (void) dhe `VerifyImageBytes` — asnjëri nuk prodhon kapacitet. `ScanProduction`/`ScanOwnerSurfaces` të ENG-006 nuk pranojnë më gjeneratë; testi i helmimit u riorientua te verifikuesi PURE.

**Fixture → RED → GREEN:** `ArchitectureTests.dll` nuk bëhet dot `VerifiedAssemblyImage` (refuzim anëtarësie) edhe pse verifikimi PURE ndaj gjeneratës sintetike kalon; forged `AssemblyName` mbi target real → refuzim; entry real → GREEN; të gjashtë skanerët production japin rezultat identik mbi dy snapshot-e të pavarur të së njëjtës entry reale. Prova RED e vetme: rikthimi i përkohshëm i overload-it me gjeneratë → meta-testi API-shape **RED** → rikthim edit-forward, SHA byte-identik.

### Meta-testi API-shape (A+B)

`Asnje_API_nuk_mint_kapacitet_nga_autoritet_apo_gjenerate_e_thirresit` skanon **të gjithë** assembly-n e testeve (pa tipet/metodat e gjeneruara nga kompajleri): çdo metodë non-privat që pranon `ProductionArtifactAuthority` ose `CatalogGeneration` duhet të mbetet PURE (kurrë kapacitet/`Stream`/`Assembly`/`FileSystemInfo`/delegate); seti i plotë i metodave që mint-ojnë kapacitet është **EKZAKT** — `OpenFixtureImage(String)`, `FixtureAssemblyImage.Open(String)`, `ReadVerifiedImage(ProjectEntry)`, `VerifiedAssemblyImage.Read(ProjectEntry)` — dhe konstruktorët e të dy tipeve janë privatë.

### §4 (C) — regresion i konfirmuar, jo workstream

Dataflow-i mbetet i pandryshuar në thelb: `CurrentGeneration → verify-before → ReadAllBytes → VerifyImageBytes mbi po atë buffer → post-verify → newobj (konstruktori privat) → ret`. Testi u **ritargetua** mekanikisht te `VerifiedAssemblyImage.Read` (fabrika = `newobj`); interpretuesi fitoi `ldind.*`/`ldobj` si pop→push, sepse `FrozenDictionary` indexer-i kthen `ref readonly` dhe lookup-et janë tani inline. Testet C mbeten GREEN.

### Devijim i deklaruar nga kufiri i scope-it (§1)

Prompti lejonte **maksimumi dy** file të tjera nën ArchitectureTests "vetëm nëse kompajleri provon se janë call-site mekanikë të domosdoshëm". Kompajleri provoi **tre**: `ConventionEnforcementTests.cs` (1 call-site), `IdentityRoleSurfaceTests.cs` (1), `DynamicRuntimeAccessBoundaryTests.cs` (2 call-site + testi i helmimit që dërgonte gjeneratë të thirrësit në skanimin production — vetë forma e seam-it B). Të gjitha janë `ReadVerifiedImage(entry, generation)` → `ReadVerifiedImage(entry)`, të pashmangshme nga kontrata §3.2. Builder-i vazhdoi dhe e raporton **shprehimisht**; dispozicioni i takon Pronarit te Human Gate.

**Counts (një run i vetëm, sipas §5):** ENG-006 **69/69** · ENG-008 **44/44** · ArchitectureTests **291/291, zero skip** (70 s; +15% ndaj 61 s të C5 në run të vetëm, nën pragun 20%, me dy teste të reja që lexojnë katalogun — pa benchmark të veçantë).

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

**Kontrata e konsumatorëve (Blocker C) — [HISTORIKE — E ZËVENDËSUAR NGA C4/C5 §4c–§4d]:** formulimi i C1 ishte *`VerifiedFrozenTargetPath(entry, generation)` = API e VETME … konsumohet PARA dhe PAS çdo skanimi nga ENG-006 `ScanProduction`, owner-scan dhe `LoadVerified`*. Ajo kontratë **nuk është më aktive**: nga C4 e këtej `VerifiedFrozenTargetPath` është **private** dhe asnjë konsumator nuk merr path-in; çiftin verify-before/verify-after e jetojnë vetë operacionet e ngushta të katalogut (`ReadVerifiedImage`, `LoadVerified`, `MatchesVerifiedTarget`). E pandryshuar mbetet vetëm baza e verifikimit: anëtarësi ekzakte + SHA-256 + MVID + WriteTimeUtc kundër vlerave TË NGRIRA, dështimi = `catalog-generation-drift`, kurrë refresh i heshtur.

**Performanca (Shtesa E):** provë deterministike — skaneri ENG-006 nuk thërret asnjë API hashimi/mtime (IL-prova `Skaneri_ENG006_nuk_llogarit_fingerprint_vete`); fingerprint-et llogariten vetëm në çiftin before/after per artifact (2 × SHA+MVID+mtime për artifact për konsumator; 24 verifikime për skanim të plotë 12-projektësh); snapshot-i i ngrirë ripërdoret (`Assert.Same`); drift-i post-scan vazhdon të kapet. **Matje wall-clock:** arch e plotë PARA C1: 57–59s ×3 · PAS C1: 57–59s ×3 (**rritje ~0%**, shumë nën 2×); targeted ENG-006: ~15–16s (63 teste) · ENG-008: ~15–16s (20 teste); build inkremental ~1s i pandryshuar.

---

## 6. Evidence — LOKALE vs CI (të ndara)

**LOKALE**, nën `HG-PRE-B2-ENG006-ENG008-LOCAL-DISK-WAIVER`:

- `dotnet build src/platform/Idata.Platform.slnx` → **0 warnings, 0 errors**;
- baseline mekanik para editimit: **178/178**;
- pas increment-it: **237/237** ×3; C1 **262/262**; C2 **270/270**; C3 **277/277**; C4 **282/282**; C5 **289/289** ×3 + dy cold-run; pas **correction C6: 291/291, zero skip** (një run, sipas §5 të C6);
- targeted (të rimatura pas C6, një run): ENG-008 **44/44**, ENG-006 **69/69** (pas C5: 42/69; pas C4: 35/69; pas C3: 30/69; pas C2: 27/65; para C1: 12/47);
- **performanca ndaj baseline-it të matur në worktree të izoluar te `ded8325`, me komanda IDENTIKE** (mediana e 3 ekzekutimeve): ArchitectureTests **63s → 61s**, ENG-006 **19s → 18s**, ENG-008 **18s → 18s**, cold-process **64.5s → 63.5s** — zero regresion, jo më pak edhe pse suita u rrit me 7 teste;
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
