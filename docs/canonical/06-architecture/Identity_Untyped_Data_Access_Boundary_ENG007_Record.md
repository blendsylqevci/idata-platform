# Identity Untyped Data-Access Boundary — Implementation Record (ENG-007, Pre-B2 Increment 1)

**Statusi:** **IMPLEMENTED_POLICY / PENDING_INDEPENDENT_REVIEW** — policy-ja arkitekturore ekziston dhe imponohet në ArchitectureTests/CI në branch-in `feat/f1-eng007-untyped-data-access-boundary`, **NOT MERGED**. Gate-i `ENG-007` në Outstanding Confirmations Tracker mbetet **OPEN**: mbyllja formale kërkon Fable delta-review → Codex review (PM_REVIEW_PROTOCOL) → Human Gate i Pronarit → merge.
**Autoriteti:** `ENG-007` (Tracker, regjistruar 01.08.2026 me PR #28 correction R4) + ADR-028/`Identity_Authority_Store_Decision.md` + ADR-029/`Application_Architecture_Convention.md`.
**Data:** 02.08.2026 · **Increment:** Pre-B2 Increment 1 — architecture-policy VETËM.

---

## 1. Çfarë ËSHTË ky increment — dhe çfarë NUK është

**ËSHTË:** një policy fail-closed **deny-by-default** që inventarizon çdo raw-data sink në ÇDO production assembly dhe e refuzon çdo sink që nuk ka hyrje EKZAKTE në një manifest të review-uar.

**NUK ËSHTË** (asnjë prej këtyre nuk implementohet këtu):

- ndarje e `PlatformDbContext`;
- runtime DB roles, GRANT/REVOKE, connection/credential split;
- production provisioning;
- schema ose migrim i ri;
- ENG-006 (reflection/dynamic invocation);
- B.2/B.3, identity protocol, token issuance/validation.

**Deklarime të ndershme (zero overclaim):**

- **ENG-007 policy ≠ DB-level isolation.** Kjo policy imponohet mbi kodin e ndërtuar; serveri PostgreSQL nuk e njeh dhe nuk e zbaton. Izolimi në nivel DB mbetet vendim i ADR-026 — **OPEN**.
- **Reflection/dynamic invocation NUK mbulohet** — **ENG-006 OPEN**, me demonstrim mekanik në këtë suitë (§8).
- **Production Go = BLOCKED.** Asnjë pretendim "universal", "bypass-free" apo "production-ready".
- Gate OPEN ≠ PASS. Ky Record nuk mbyll asnjë gate.

---

## 2. Threat boundary (i deklaruar)

| Brenda kufirit (pretendohet) | Jashtë kufirit (NUK pretendohet) |
|---|---|
| Çdo production assembly i zbuluar nga `PlatformProjectCatalog` skanohet; asnjë projekt nuk shpëton nga një listë e hardcoded. | Sjellja e serverit DB: grants, roles, RLS, kufizime lidhjeje. |
| Çdo raw sink i referuar në metadata duhet të atribuohet në një trup metode të dekoduar; ndryshe = `unattributed-sink-token` (INCOMPLETE). | Kod jashtë repo-s ose assembly të ngarkuara në runtime nga burime të pakontrolluara. |
| Atribuimi i state machine-ve async provohet nga `AsyncStateMachineAttribute`; atribuim i paprovueshëm = fail-closed. | Reflection/dynamic invocation (`Type.GetType` + invoke) — **ENG-006 OPEN**. |
| Anëtar i PANJOHUR mbi një tip me kapacitet raw = gjetje (nuk kërkohet listë shteruese anëtarësh). | `calli`/function pointers: raportohen si `indirect-call-site` fail-closed, POR targeti nuk zgjidhet dot statikisht. |
| Sipërfaqja publike e DEKLARUAR e owner-ave nuk pranon/dorëzon kapacitet raw. | Sipërfaqja e TRASHËGUAR nga framework-u (p.sh. `DbContext.Database`) — qeveriset nga skaneri i sink-eve në çdo call-site, jo nga guard-i i sipërfaqes. |
| Skaneri lexon binarët REALË (metadata + IL) të `CanonicalTargetPath`-eve të vlerësuara nga MSBuild. | Ekzekutimi arbitrar i një MSBuild target-i keqdashës (kufi i trashëguar nga R7 i PR #28). |

**Kufi i shprehur i sipërfaqes publike:** dorëzimi i kontekstit **TË TIPIZUAR** `PlatformDbContext` NUK trajtohet si kapacitet raw. Dalja raw prej tij (`GetDbConnection`) është vetë sink i skanuar dhe i manifestuar në çdo vend ku ndodh. Kjo është zgjedhje e deklaruar, jo harresë: trajtimi i kundërt do të kërkonte ndryshim production-kodi, i cili është JASHTË scope-it të këtij increment-i.

---

## 3. Inventari autoritativ i projekteve (zero listë e dytë manuale)

Universi = `PlatformProjectCatalog.DiscoverProduction()` — identiteti i çdo projekti është **path kanonik repo-relative + AssemblyName i vlerësuar nga MSBuild + CanonicalTargetPath i verifikuar me MVID** (trashëguar nga correction R4–R8 të PR #28). Skaneri ENG-007 nuk mban asnjë listë të vetën:

- `12` projekte production të zbuluara dhe të skanuara (count i DERIVUAR nga katalogu, jo i hardcoded);
- meta-test: `catalog.Length == numri i skanuar` dhe çdo hyrje e manifestit i përket një projekti REAL të katalogut;
- projekt/assembly jashtë katalogut në manifest = `unknown-project` / `assembly-mismatch` (fixture RED 10).

---

## 4. Taksonomia e sink-eve (§4 — deny-by-default)

Dy shtresa komplementare, të dyja me barazi **EKZAKTE** (asnjë `StartsWith`/`Contains`):

**(A) Tipet me kapacitet raw** — çdo referencë ndaj tyre dhe çdo anëtar i tyre (i njohur ose i panjohur) = gjetje:

- ADO.NET: `DbConnection`, `DbCommand`, `DbBatch`, `DbBatchCommand`, `DbDataReader`, `DbTransaction`, `DbDataAdapter`, `DbProviderFactory`, `IDbConnection`, `IDbCommand`, `IDataReader`, `IDbTransaction`, `IDbDataAdapter`;
- Npgsql: `NpgsqlConnection`, `NpgsqlCommand`, `NpgsqlBatch`, `NpgsqlBatchCommand`, `NpgsqlDataSource`, `NpgsqlMultiHostDataSource`, `NpgsqlDataReader`, `NpgsqlTransaction`, `NpgsqlBinaryImporter`, `NpgsqlBinaryExporter`, `NpgsqlRawCopyStream`, `NpgsqlCopyTextReader`, `NpgsqlCopyTextWriter`.

**(B) Emrat e anëtarëve = ekzekutim raw**, pavarësisht tipit deklarues (mbulon extension methods të EF Core relational dhe variantet async/generic): `CreateCommand`, `CreateDbCommand`, `CreateBatch`, `CreateDbBatch`, `CommandText`/`get_CommandText`/`set_CommandText`, `ExecuteReader(Async)`, `ExecuteScalar(Async)`, `ExecuteNonQuery(Async)`, `ExecuteDbDataReader(Async)`, `FromSql`, `FromSqlRaw`, `FromSqlInterpolated`, `ExecuteSql(Async)`, `ExecuteSqlRaw(Async)`, `ExecuteSqlInterpolated(Async)`, `SqlQuery`, `SqlQueryRaw`, `GetDbConnection`, `SetDbConnection`, `GetDbTransaction`, `BeginTextImport`, `BeginBinaryImport`, `BeginRawBinaryCopy`, `BeginTextExport`, `BeginBinaryExport`.

**Rregullat e imponuara:**

1. çdo sink production është gjetje, përveç hyrjes EKZAKTE në manifest;
2. anëtar/simbol i panjohur mbi tip raw = gjetje, **kurrë `continue`**;
3. autoriteti NUK është kërkimi i literalit `identity.` (fixture RED 2 e ndërton emrin nga fragmente);
4. autoriteti është aftësia për ekzekutim raw, edhe kur SQL-i ndërtohet në runtime;
5. stringu `identity.*` pa sink ekzekutues NUK është vetvetiu gjetje (fixture GREEN 20);
6. parametrizimi mbron nga injection, **NUK** autorizon akses (fixture RED 3 është e parametrizuar dhe mbetet gjetje);
7. helper-wrapper/delegate nuk e fsheh sink-un — sink-u kapet TE helper-i (fixtures RED 8/9);
8. reflection/dynamic invocation raportohet si kufi **ENG-006 OPEN**, kurrë si PASS;
9. çdo dështim leximi/dekodimi = INCOMPLETE/fail-closed.

**Kufi PIKE i provuar:** `NpgsqlConnection` NUK e kap `NpgsqlConnectionStringBuilder` (ndërtues konfigurimi, jo ekzekutim) — pikërisht tipi që `Idata.Platform.Persistence` përdor sot; test i dedikuar.

**Mekanika e skanimit:** metadata (`MemberReference`/`TypeReference`/`TypeSpecification`/`MethodSpecification` + sinjatura fushash/metodash) për PLOTËSINË, plus dekodim IL me tabelë **të plotë** operand-esh ECMA-335 për ATRIBUIM. Dekoduesi provohet veçmas: operandi 8-bajtësh i `ldc.i8` që "duket" si token `call` NUK raportohet si token; opcode i pavlefshëm dhe operand i cunguar ndalojnë dekodimin me gabim të raportuar.

---

## 5. Manifesti i përjashtimeve — SAKTËSISHT tri surface

Manifesti është **statik, i shkruar dorazi** dhe i validuar në të dyja drejtimet. Statusi i owner-it (Persistence / Modules.Identity.Infrastructure) **NUK** jep blanket exemption.

| # | Projekt kanonik | Assembly | Burimi i review-uar | Tipi i kualifikuar | Anëtarët | Fingerprint sha256 |
|---|---|---|---|---|---|---|
| 1 | `src/platform/Idata.Platform.DbMigrator/…csproj` | `Idata.Platform.DbMigrator` | `src/platform/Idata.Platform.DbMigrator/MigratorApp.cs` | `Idata.Platform.DbMigrator.MigratorApp` | `EffectiveDatabaseMatchesAsync`, `SentinelExistsAsync` | `7fd6303327ed87a7…debfe73c` |
| 2 | `src/platform/Modules/PlatformStatus/Infrastructure/…csproj` | `Idata.Platform.Modules.PlatformStatus.Infrastructure` | `…/NpgsqlDatabaseReadinessProbe.cs` | `…Infrastructure.NpgsqlDatabaseReadinessProbe` | `CheckAsync` | `50e1b4cf8402e401…169411ad` |
| 3 | `src/platform/Modules/AccessControl/Infrastructure/…csproj` | `Idata.Platform.Modules.AccessControl.Infrastructure` | `…/EfMembershipRoleUnitOfWork.cs` | `…Infrastructure.EfMembershipRoleUnitOfWork` | `TryMutateRoleAsync`, `ReadMembershipsAsync`, `ReadUserStatusesAsync`, `ScalarAsync`, `CreateCommand` | `ceed8e39cfe00316…17ce1e65` |

**Gjithsej 8 hyrje / 3 surface / 3 assemblies / 3 tipe.** Fingerprint-et e plota 64-karakterëshe jetojnë në `RawDataAccessManifest.cs` — çdo përditësim i tyre është diff i dukshëm për reviewer-in.

**Arsyet e shkruara (të plota në manifest):** surface 1 dhe 2 = guard/probe **READ-ONLY** mbi schema `platform` (`current_database()`, sentinel `platform.migration_smoke_check`) — zero DML, zero `identity`; surface 3 = protokolli atomik i lock-eve i F1.1 me rend kanonik (ENG-005), i cili kërkon `FOR UPDATE` që EF LINQ nuk e shpreh — komanda konstante të parametrizuara mbi tabela `platform.`, pa konkatenim inputi të jashtëm. **Pronari i të treve:** Tech/Platform Owner.

**Invariantet e manifestit (të gjitha të testuara me manifeste sintetike):**

- pa wildcard/prefix (`*`/`?` në çdo fushë = `wildcard-entry`);
- pa fushë bosh (arsye/pronar i pashkruar = `empty-field`);
- fingerprint jo-hex/jo-64 = `malformed-fingerprint`; mospërputhje me file-in real = `fingerprint-mismatch`;
- hyrje e dyfishtë = `duplicate-entry`; hyrje e pakonsumuar = `dead-entry`;
- sink pa hyrje = `uncovered-sink`;
- gjetje `unreadable-*`/`undecodable-*`/`unattributed-*`/`indirect-*` = `unallowable-finding` — **NUK mbulohen KURRË** nga manifesti.

**Atribuimi async:** të tri surface-t janë metoda `async`; sinku fizik jeton në `MoveNext` të një tipi compiler-generated. Lidhja me metodën burimore bëhet nga `AsyncStateMachineAttribute` (metadata reale). Pa këtë, manifesti do të ishte i pakonsumueshëm — prandaj testi e verifikon shprehimisht dhe kërkon që **asnjë gjetje të mos mbetet e ngjitur te një tip compiler-generated**.

---

## 6. Owner public-surface guard (§6)

Sipërfaqja publike/`protected` e DEKLARUAR e owner-ave nuk lejohet:

- të **pranojë** parametra me tipe raw (`DbConnection`/`DbCommand`/Npgsql*), përfshirë brenda delegate-ve/generics (grafi i tipeve zbërthehet në komponentë EKZAKTË);
- të **dorëzojë** kthim ose fushë publike me tip raw;
- të pranojë parametra me emra nga fjalori SQL/schema/tabelë (`sql`, `rawSql`, `commandText`, `query`, `schema`, `table`, …).

**Rezultati mbi production:** `0` gjetje për të dy owner-at.
**Shtresa name-based deklarohet HAPUR si e tillë** — autoritet mbetet grafi i tipeve; `connectionString`/`database` NUK janë në fjalor (konfigurim i validuar, jo SQL/schema).

---

## 7. Fixtures RED/GREEN + meta-teste

Të gjitha sintetike, në assembly-në e testeve; asnjëra nuk ekzekutohet ndaj një DB-je.

**RED (kapen me atribuim të saktë):** 1 ADO.NET jo-owner + konstant `identity.` · 2 schema nga fragmente · 3 mixed-case/quoted/interpolated/**parameterized** · 4 `FromSqlRaw`/`FromSqlInterpolated` · 5 `ExecuteSqlRaw`/`ExecuteSqlInterpolated` · 6 `SqlQueryRaw`/`SqlQuery` · 7 `NpgsqlCommand`/`NpgsqlBatch`/`NpgsqlDataSource`/`BeginBinaryImport` · 8 indirektim përmes helper-i (sink-u raportohet TE helper-i) · 9 alias `ldftn` (method-group) **dhe** lambda pa state machine → `unattributed-compiler-generated` fail-closed · 10 projekt/assembly jashtë katalogut · 11 metadata jo-PE e palexueshme + burim manifesti i munguar · 12 mutim i surface-it pa update fingerprint-i · 13 duplicate/dead/wildcard/empty/uncovered/unallowable · 14 escape hatch publik i owner-it · 15 anëtar i PANJOHUR mbi tip raw (`GetSchema`).

**GREEN (zero gjetje ENG-007):** 16 tri surface-t ekzistuese me identitet+fingerprint ekzakt (barazi e plotë manifest ↔ gjetje reale) · 17 EF LINQ i tipizuar · 18 API publik i tipizuar i ndërmjetësuar · 19 store Identity i tipizuar · 20 string `identity.*` pa sink · 21 reflection dinamik → **ENG-006 OPEN**, jo provë PASS.

**Meta-teste:**

- **çdo familje sink-esh është MBAJTËSE** — heqja e familjes nga taksonomia e zhduk zbulimin përkatës (6 rreshta: FromSql·, ExecuteSql·, SqlQuery·, ADO.NET `CommandText`, Npgsql import, `GetDbConnection` mbi surface-in production);
- **rregulli "i panjohur = gjetje" është mbajtës** — `GetSchema` NUK është në listën e emrave; heqja e tipit raw prind e humbet zbulimin, çka provon se kapej NGA anëtarësia në tip;
- manifesti production është ekzakt, pa wildcard, pa hyrje të pakonsumuar;
- skanimi mbulon çdo projekt production të zbuluar nga katalogu.

**Prova RED→GREEN e guard-it qendror (e ekzekutuar, jo e commit-uar):** ndryshimi i një anëtari të vetëm të manifestit (`CreateCommand` → `CreateCommandRedProbe`) i skuqi menjëherë `Cdo_raw_sink_ne_production_eshte_ose_i_manifestuar_ose_gjetje` dhe `Tri_surface_t_ekzistuese_konsumojne_saktesisht_gjetjet_reale` (`dead-entry` + `uncovered-sink`); rikthimi u bë me edit forward dhe u verifikua **byte-identik** (sha256 i file-it i pandryshuar). Asnjë gjendje RED nuk u commit-ua.

---

## 8. Kufiri ENG-006 — demonstrim mekanik në KËTË suitë

Fixture-i `DynamicRawAccessResidualPoC` e ndërton emrin e tipit raw VËRTET në runtime nga fragmente array + `string.Concat` dhe e zgjidh me `Type.GetType`. Testi provon **të dyja** anët:

1. zgjidhja runtime **funksionon** (tipi i kthyer është pikërisht tipi raw i synuar);
2. skaneri statik ENG-007 prodhon **zero** gjetje për të.

Ky rezultat klasifikohet **ENG-006 OPEN** — nuk është dhe nuk paraqitet si provë që ENG-007 mbulon reflektimin.

---

## 9. Evidence — LOKALE vs CI (të ndara, pa përzierje)

**LOKALE (macOS, kjo makinë), nën `HG-PRE-B2-ENG007-LOCAL-DISK-WAIVER` të Pronarit:**

- `Idata.Platform.ArchitectureTests`: **155/155 PASS, zero skip** (prej të cilave **45** janë testet e reja ENG-007);
- prova RED→GREEN e §7 dhe rikthimi byte-identik.

**NUK ekzekutohet lokalisht (sipas waiver-it):** Docker/PostgreSQL, suita e plotë .NET build/test, integration suite, contracts generate/drift-check, legacy turbo, Angular Backoffice. Këto merren **VETËM** nga GitHub CI dhe raportohen veçmas. Asnjë prej tyre nuk pretendohet PASS pa ekzekutim real.

**Nga CI (kërkohet 3/3 SUCCESS):** `build-test`, `secret-scan`, `governance-advisory`.

---

## 10. Rollback

Hiqen gjashtë file-at e rinj të testeve (`RawDataSinkTaxonomy.cs`, `IlOperandTable.cs`, `UntypedDataAccessScanner.cs`, `RawDataAccessManifest.cs`, `UntypedDataAccessFixtures.cs`, `UntypedDataAccessBoundaryTests.cs`) dhe kthehen rreshtat e status-sync-ut në docs. **Zero prekje** e production source, schema, migrimeve, `ci.yml`, dependency-ve ose lockfile-ve — prandaj rollback-u është plotësisht lokal ndaj testeve/docs dhe nuk kërkon asnjë veprim në DB.

---

## 11. Gates / risqe OPEN pas këtij increment-i

- **`ENG-007` = OPEN** (policy IMPLEMENTED, pending independent review + Human Gate). Afati i rishikuar: CLOSED **PARA nisjes së çdo pune implementuese të B.2** dhe definitivisht PARA çdo production Go.
- **`ENG-006` = OPEN** (reflection/dynamic invocation) — i njëjti afat i rishikuar.
- **`ADR-026` = OPEN** — DB roles/provisioning/secrets; **DB-level isolation = NOT IMPLEMENTED**.
- **`ADR-025`/`ADR-027` = OPEN**; **`ENG-004` fallback AKTIV**; **`ENG-005` = OPEN**.
- **B.2 / B.3 = NOT STARTED / BLOCKED**; identity protocol, token issuance/validation, projection = jashtë scope-it.
- **Step B/C, PREFLIGHT, Tier 2 = NO-GO**; **production Go = BLOCKED**.

**Rendi i mbetur:** Fable delta-review → Codex PM/architecture/security review → Human Gate i Pronarit → merge vetëm me autorizim të ri → Notion + Slack pas merge-it → goal/prompt i RI për Pre-B2 Increment 2 (ENG-006).
