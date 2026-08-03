# Dynamic Reflection Boundary & Deterministic Harness — Implementation Record (ENG-006 + ENG-008, Pre-B2 Increment 2)

**Statusi:** **IMPLEMENTED / PENDING_INDEPENDENT_REVIEW** — të dyja kufijtë ekzistojnë dhe imponohen në ArchitectureTests/CI në branch-in `feat/f1-eng006-eng008-preb2-boundary`, **NOT MERGED**.

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

**Owner public-surface guard:** owner-at nuk lejohen të eksportojnë `Type`/`Assembly`/`Module`/`MemberInfo`/`MethodBase`/`MethodInfo`/`ConstructorInfo`/`PropertyInfo`/`FieldInfo`/`Delegate`/`Expression`/`IServiceProvider`/`AssemblyLoadContext`. **Rezultati mbi production: 0 gjetje.**

### Manifesti production — BOSH

Zero përjashtim i autorizuar sot; testi e imponon boshllëkun. Çdo përjashtim i ardhshëm kërkon path kanonik, identitet assembly-je, tip, anëtar, arsye, pronar, fingerprint/evidencë, review të pavarur dhe Human Gate.

### Dy defekte reale të gjetura dhe të ndrequra gjatë implementimit

1. **Prindi i një `TypeSpecification`** merrej si çdo tip brenda tij, përfshirë argumentet gjenerike — kështu `EqualityComparer<Type>` (që kompajleri e emeton për `record.GetHashCode`) lexohej gabimisht si "anëtar mbi `System.Type`". Fix: prindi = tipi **deklarues i jashtëm**.
2. **`GetMethodFromHandle`/`GetFieldFromHandle`** klasifikoheshin si UNKNOWN, ndërsa janë ekuivalenti i `typeof(...)` për metoda/fusha.

Efekti i matur: **99 → 19** gjetje production.

---

## 4. Kufi i DEKLARUAR: kodi i gjeneruar brenda assembly-ve

Të 19 gjetjet e mbetura i përkasin kodit **të gjeneruar** brenda production assemblies — `Mediator.Internals.*` (source generator i Mediator) dhe `Microsoft.AspNetCore.OpenApi.Generated.*` (source generator i OpenAPI XML comments). Ky kod nuk shkruhet nga ne dhe nuk ndryshohet dot pa prekur varësitë, çka është **jashtë scope-it** të këtij increment-i.

Klasifikimi është **MEKANIK, i derivuar nga metadata** (`System.CodeDom.Compiler.GeneratedCodeAttribute` / `CompilerGeneratedAttribute`) — **jo një listë namespace-esh** dhe **jo auto-allowlist**: gjetjet e tilla marrin prefiksin `generated-`, raportohen dhe numërohen, por nuk hyjnë në manifestin e kodit tonë. **Kod i shkruar prej nesh që shton reflection nuk është i gjeneruar, prandaj bllokon menjëherë.**

**Follow-up i hapur (jo gate i ri):** vlerësimi i reflection-it të source-generator-ëve i takon një vendimi për varësitë/gjeneratorët dhe mbetet për review të pavarur.

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

---

## 6. Evidence — LOKALE vs CI (të ndara)

**LOKALE**, nën `HG-PRE-B2-ENG006-ENG008-LOCAL-DISK-WAIVER`:

- `dotnet build src/platform/Idata.Platform.slnx` → **0 warnings, 0 errors**;
- baseline mekanik para editimit: **178/178**;
- pas increment-it: **237/237, zero skip**, **tri ekzekutime radhazi**;
- targeted: ENG-008 **12/12**, ENG-006 **47/47**;
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
