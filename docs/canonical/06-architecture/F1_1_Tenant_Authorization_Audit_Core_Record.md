# F1.1 — Tenant Authorization & Business-Scoped Audit Core (Implementation Record)

**Statusi:** **COMPLETED / MERGED** — kaloi Fable delta-review → Codex review (PM_REVIEW_PROTOCOL) → Human Gate të Pronarit; PR #25 merged në `main` (squash `1dc06d5769797c0dabbf6f47a1561b315cb7993b`, 27.07.2026).
**Autoriteti:** ADR-024 (MERGED / ACCEPTED, PR #23, merge `e0d4cdc`) + Platform Foundation Bootstrap (COMPLETED, PR #24 merged, squash `0f12488c`). **Data:** 27.07.2026 (v7 — post merge i PR #25: statusi → COMPLETED/MERGED; asnjë ndryshim tjetër përmbajtjeje; v6 27.07.2026 — docs/governance reconciliation: gate `ENG-005` cross-writer lock-order i regjistruar në Tracker + Gates OPEN; kushtëzimi i receipt re-read i saktësuar — rilexim VETËM në rrugët VersionConflict/receipt-conflict, kurrë pas 503 transient; parakushti real PostgreSQL i testeve i dokumentuar; v5 26.07.2026; v4 26.07.2026; v3 24.07.2026). **Increment:** F1.1 — vertical slice i kufizuar i F1, JO gjithë F1.

## Çfarë ËSHTË implementuar (vetëm kjo)

Modul **AccessControl** (module-first Clean/Onion: Domain/Application/Infrastructure/Presentation) + projekt i ndarë **Persistence** = migration owner i vetëm, NEUTRAL ndaj moduleve. Një query dhe një command end-to-end mbi PostgreSQL, me tenant-scoped RBAC deny-by-default, lifecycle-aware, optimistic concurrency (CAS), audit business-scoped append-only të lidhur me komandën, idempotency, migrimin 0002 me upgrade path real nga 0001, OpenAPI + klient TypeScript të gjeneruar, dhe evidence testesh.

## Modeli (skema `platform`)

**`business` = tenant root** (pa `business_id`). Global/reference: **`app_user`**, **`permission`**. **EKZAKT 5 child tables me `business_id NOT NULL` DIREKT:** `role`, `business_membership`, `role_permission`, `audit_log`, `command_receipt`. UUID keys; timestamptz UTC; CHECK constraints për statuse/action; zero float; zero PII/sekrete.

`business_membership` fushat reale: `id, business_id, user_id, role_id, version, status, created_at`. Roli ka `business_id NOT NULL` (tenant-scoped). `role_permission`: `business_id, role_id, permission_id`.

**Persistence NEUTRAL (§8):** projekti `Idata.Platform.Persistence` modelon vetëm storage records POCO (`AppUserRecord`, `BusinessRecord`, …) dhe NUK referon asnjë Domain moduli. Kjo mbyll rrugën transitive `PlatformStatus.Infrastructure → Persistence → AccessControl.Domain`; `AccessControl.Infrastructure` mapon records ↔ tipet Domain/Application. Një migration owner i vetëm ruhet; architecture tests analizojnë EDHE transitive closure (jo vetëm ProjectReference direkte).

**Composite FK (ENG-003 / PoC-1, vendim eksplicit):** `role` ka çelës alternativ `(business_id, id)`; `business_membership`/`role_permission` lidhen me `role` përmes `(business_id, role_id) → role(business_id, id)` — cross-tenant refuzohet nga vetë PostgreSQL. **Production RLS NUK pretendohet.**

## Migration ownership + kompatibiliteti 0001 → 0002

`0001_InitialPlatformSchema` (+Designer) NUK rigjenerohet: `[Migration("0001_...")]`, Up/Down dhe modeli historik VERBATIM. Snapshot i rigjeneruar për modelin AKTUAL 0001+0002. `0002_F1AccessControlCore` (ID deterministik) përmban vetëm objektet e F1.1 + SQL i audituar brenda migrimit (triggers append-only). Provë machine-checkable (`MigrationUpgradePathTests`): upgrade real 0001→vetëm 0002, row/history/sentinel i 0001 i ruajtur, objektet e 0002 (incl. `version`, `command_receipt_id`, `correlation_id`, FK audit→receipt) ekzistojnë, no-op stabil.

## Autorizimi + lifecycle fail-closed (§4)

`ActorContext` vjen VETËM nga principal i BESUAR dhe i AUTENTIKUAR (`Identity.IsAuthenticated == true`) — claims të vlefshme në një ClaimsIdentity pa authenticationType NUK janë aktor. Grants jepen VETËM kur, në DB dhe në të njëjtin business scope: `app_user.status=active` DHE `business.status=active` DHE `business_membership.status=active` (lifecycle nga DB, JO nga claims). Target-i i PUT-it duhet gjithashtu të jetë membership aktiv + user target aktiv; disabled/cross-tenant/inekzistent → i padallueshëm (404, zero mutation).

**Kontratë e PËRKOHSHME provider-neutral e F1.1:** principal mungon/invalid, user/business/membership disabled ose permission mungon → **403 uniform `ACCESS_DENIED` PARA target lookup-it**. **Ndarja 401/403 vendoset në ADR-028/F1 Identity — JO kontratë finale OIDC.** Identity provider/store = ADR-028 OPEN; `app_user` = projection provider-neutral (email/name/mfa_enabled të target-it canonical shtyhen; Identity NUK deklarohet READY). **`roleCode`/role name/hierarchy = deferred/out-of-scope** (access-context kthen vetëm userId/businessId/permissions/identityStatus).

## Politika e aprovuar e F1.1 (self-role · IsSystem · provisioning)

**Aktori NUK ndryshon dot rolin e vet** — krahasimi bëhet me **USER ID**
(`actorUserId <> target.user_id`), jo me membership id; deny stabil, zero efekt.
**Rol me `IsSystem = true` NUK caktohet** përmes endpoint-it F1.1; përgjigjja
publike është e njëjtë me rol inekzistent (parity — nuk zbulohet se roli është
system-managed). Të dy predikatët jetojnë VETËM në rivlerësimin përfundimtar nën lock
— asnjë bypass përmes endpoint-it, handler-it apo adapterit tjetër.

**Provisioning note (i detyrueshëm):** F1.1 **NUK** e përdor endpoint-in e
role-change për bootstrap-in e rolit të parë të tenant-it. Sot rolet fillestare
vijnë vetëm nga **seed/migration i kontrolluar**. **Production provisioning
mbetet OPEN.**

**Të pa-implementuara qëllimisht (OPEN, jo "implemented"):** role hierarchy ·
permission dominance / delegation ceiling · last-admin protection · role
globale/system-wide provisioning · break-glass. Hierarkia NUK nxirret nga emrat
e roleve.

## Autoriteti i mutacionit — AUTHORITY-LOCK PROTOCOL (§1)

### Gjetja e mbyllur: stale READ COMMITTED authorization snapshot

Versioni i mëparshëm mbështetej te një `UPDATE ... WHERE EXISTS(...)` i vetëm
dhe e përshkruante atë si "një statement që rivlerëson atomikisht çdo
autoritet". **Ai pretendim ishte i gabuar dhe është hequr.** Nën READ
COMMITTED, ai statement merr row lock VETËM mbi target membership-in; nëse
bllokohet duke pritur atë lock, sub-query-t `EXISTS` mbi rreshtat e tjerë
(business, app_user, membership i aktorit, role, permission, role_permission)
mund të mbeten të vlerësuara mbi snapshot-in e statement-it. Pra një revokim
që commit-on NDËRKOHË mund të mos shihet dhe mutacioni të kalonte me autoritet
të vjetruar.

### Protokolli i zbatuar (i njëjti transaction, isolation e paprekur)

1. **Lock eksplicit i ÇDO fakti autorizimi të ndryshueshëm**, sipas rendit
   kanonik më poshtë (`FOR SHARE` — **jo** `FOR KEY SHARE` — sepse `status`,
   `is_system`, `feature_key`/`action` janë non-key updates dhe vetëm
   `FOR SHARE` i bllokon; memberships merren `FOR UPDATE`).
2. **Rivlerësim PËRFUNDIMTAR fail-closed** i të gjitha predikateve mbi vlerat
   e lexuara **nën lock**.
3. **`UPDATE` i guarduar** i target-it që përsërit `status='active'` dhe
   `version = expectedVersion` si defense-in-depth.
4. **Mutation + audit + command receipt commit-ohen bashkë.**

### LOCK ORDER KANONIK — kontratë për çdo writer të ardhshëm

```
business → business_membership (ORDER BY id) → app_user (ORDER BY id)
        → role → permission → role_permission
```

Rreshtat e dublikuar deduplikohen (aktori dhe targeti mund të jenë i njëjti
rresht). Rendi provohet me teste **VETËM për writer-in aktual** (dy mutacione
të KËTIJ writer-i s'ndërthuren në rend të kundërt — test pa 40P01 në atë
skedul). **KJO NUK ËSHTË provë universale "deadlock-free":** rendi nuk
enforce-ohet mekanikisht mbi writer-a të ardhshëm — një lifecycle writer i ri
mbi business/app_user/membership/role/permission/role_permission kërkon
**cross-writer lock-order review + test matrix PARA merge-it** (residual risk
OPEN; pa analyzer të ri dhe pa ADR të ri në këtë pass).

**Serializim PER-AKTOR (kufi operacional):** hapi 2 merr `FOR UPDATE` edhe mbi
membership-in e AKTORIT, ndaj për komandat që përdorin KËTË protokoll
mutacionet konkurrente të të njëjtit aktor serializohen edhe kur target-et
ndryshojnë (head-of-line blocking i mundshëm — për t'u vlerësuar para
production-it; characterization test:
`Lock_order_kanonik_nuk_prodhon_deadlock_mes_dy_mutacioneve`). Nuk pretendohet
throughput i matur production.

**Kufi:** rreshti `role` i rolit AKTUAL të aktorit NUK lock-ohet — lock-ohen
membership-i i aktorit, permission-i dhe grant-i ekzakt; `role` sot nuk ka
status/atribut që merr pjesë në vendimin e autorizimit të aktorit. Garancia
vlen VETËM për faktet e listuara; nëse `role` fiton atribut
authorization-relevant, ai duhet shtuar në lock protocol + order + tests.

**Target pre-lock i provuar diskriminueshëm:** target membership-i lock-ohet
nga FAZA E LOCK-EVE (jo thjesht nga UPDATE-i i guarduar) — provuar me bllokim
te një fakt më i vonshëm i rendit (roli) dhe writer konkurrent të bllokuar nga
`pg_blocking_pids` = pid-i i mutacionit
(`Target_membership_lock_ohet_nga_faza_e_lock_eve_para_guarded_update`);
heqja e target-it nga lock query e skuq testin (RED e demonstruar).

### Kufiri i garancisë

Serializimi vlen për faktet e lock-uara më sipër. Nëse një rresht mungon,
është disabled, është ndryshuar ose grant-i është hequr → **fail closed**:
zero mutation, zero version++, zero audit, zero receipt.
**DB CONTENTION ≠ CAS:** deadlock (40P01), serialization failure (40001),
lock timeout (55P03) dhe statement timeout server-side (57014) → rollback i
plotë + **503 `PLATFORM_UNAVAILABLE`** stabil (zero mutation/audit/receipt,
ZERO retry automatik, ZERO SQLSTATE/query/identifikues DB në përgjigje) — JO
409 dhe JO "expectedVersion mismatch". Pritja për çdo lock është e KUFIZUAR
(`SET LOCAL lock_timeout`; default-i production = **5 sekonda**, i mbivendosshëm në runtime mbi objektin/testet, por ENDE i palidhur me application configuration — operational tuning/config binding = **OPEN follow-up**, pa ndryshim kodi në këtë pass). Cancellation e
klientit mbetet cancellation path (propagohet, s'relabelohet). **409
`CONCURRENCY_CONFLICT` vetëm për mismatch REAL të versionit.** Teste:
`DbContentionTests` (55P03 real në HTTP → 503 → pas lirimit kërkesa e re
kalon; klasifikimi i drejtpërdrejtë i 40P01/40001/55P03/57014 + kodet
jo-transient). Retry automatik do të kërkonte ADR të veçantë.

### Idempotencë konkurrente same-key (kontrata e rikthyer)

Dy kërkesa me të njëjtin Idempotency-Key mund ta kalojnë TË DYJA lookup-in
fillestar të receipt-it para commit-it të fituesit; humbësi merr
`VersionConflict` në fazën e mutacionit. Kontrata: pas rollback-ut bëhet **NJË
rilexim deterministik i receipt-it** (lexim i freskët, pa polling, pa retry të
mutacionit) — **fingerprint identik → rezultati i ruajtur i fituesit (200 +
200, përgjigje publike IDENTIKE, saktësisht 1 mutation/audit/receipt)**;
**fingerprint tjetër → 409 `IDEMPOTENCY_CONFLICT`** (kurrë i raportuar si
CAS); **receipt që mungon realisht → 409 `CONCURRENCY_CONFLICT` i vërtetë**.
TESTED me overlap real të provuar strukturalisht (barrierë count-down pas
lookup-it; zero Task.Delay): `ConcurrentIdempotencyTests` A/B/C + 4 unit
tests të kontratës. Unique violation i receipt-it mbetet mbrojtje sekondare,
jo mekanizëm primar.

**KUFIRI KUSHTOR i rileximit (JO universal):** rileximi deterministik i
receipt-it bëhet VETËM kur (a) humbësi arrin `VersionConflict` pas
rollback-ut, ose (b) arrihet rruga e receipt unique-conflict pas `SaveAsync`.
Nëse protokolli i lock-eve përfundon MË HERËT me 40P01 / 40001 / 55P03 /
57014, transaction-i rollback-ohet, përgjigjja është **503 retryable**, efekti
është zero dhe **receipt-i NUK rilexohet në atë tentativë** — klienti mund ta
riprovojë të njëjtin Idempotency-Key. NUK pretendohet "200 replay në çdo
contention schedule", rilexim i pakushtëzuar, polling apo retry automatik i
mutacionit.

### Kufizim i PostgreSQL-it me pasojë për provisioning-un

`SELECT ... FOR SHARE` **nuk mjaftohet me privilegjin `SELECT`** — kërkon
privilegj të klasës `UPDATE` (`DELETE` nuk mjafton; verifikuar empirikisht).

**Matrica reale e privilegjeve të rolit runtime (testuar në
`AuditProtectionTests`):**

| Tabela | SELECT | UPDATE | Tjetër |
|---|---|---|---|
| `business`, `app_user`, `role`, `permission` | të gjitha kolonat | VETËM `(created_at)` (minimumi për `FOR SHARE`) | zero |
| `role_permission` | të gjitha | VETËM `(business_id)` | zero (DELETE mohohet) |
| `business_membership` | **të gjitha kolonat e mapuara** (shih më poshtë) | **VETËM `(role_id, version)`** | INSERT/DELETE/TRUNCATE mohohen |
| `audit_log`, `command_receipt` | po | jo (triggers append-only) | INSERT po |

Mohimet e provuara me `42501 insufficient_privilege`: UPDATE mbi
`business_membership.status/user_id/business_id/created_at`, INSERT/DELETE/
TRUNCATE mbi `business_membership`, UPDATE mbi `business.status`,
`app_user.status`, `role.is_system`, `permission.action`, DELETE/UPDATE mbi
`role_permission`. Komanda legjitime nën rolin runtime KALON (materializimi EF
funksionon).

**Kufi real i SELECT-it (EF Core):** `EfAccessControlStore.GetGrantsAsync`
materializon entitetin e plotë (`AsNoTracking` + `SingleOrDefaultAsync`), ndaj
SELECT mbi `business_membership` duhet të mbulojë të gjitha kolonat e mapuara
— ngushtimi pa e kaluar query-n në projection do ta prishte rrugën reale me
insufficient privilege. Ky është kufi i implementimit aktual, jo politikë e
përhershme; rivlerësohet nëse kalohet në projection.

**Residual risk i deklaruar:** edhe `UPDATE (role_id, version)` i lejon
credential-it runtime të shkruajë DIREKT ato kolona jashtë endpoint-it. NUK
pretendohet "production least privilege complete". Production DB-role
provisioning = OPEN; row-scope/RLS = OPEN (ADR-026); direct-write hardening =
OPEN. Pa SECURITY DEFINER dhe pa RLS në këtë pass.

## Optimistic concurrency (CAS) (§5)

`business_membership.version` (bigint NOT NULL, nis 0) = EF concurrency token DB-backed. PUT kërkon `expectedVersion` të tipizuar (në command/fingerprint/OpenAPI/klient/response). Konflikti CAS → **HTTP 409 `CONCURRENCY_CONFLICT`** (i dalluar nga `IDEMPOTENCY_CONFLICT`), zero mutation/audit/receipt nga humbësi; humbësi i një gare SAME-key rikuperohet përmes rileximit të receipt-it (shih "Idempotencë konkurrente same-key"), dhe DB contention/transient → 503, JO 409. Dy kërkesa paralele me key të ndryshëm + të njëjtin `expectedVersion` + role të ndryshme → saktësisht një 200 + një 409 CONCURRENCY_CONFLICT; final role/version = fituesi; saktësisht një audit + një receipt (i provuar).

*Korrigjim kontrate i datuar (04.10.2026 — insertion-only):* `roleId` dhe `expectedVersion` në body-n e PUT janë të detyrueshme edhe në runtime: mungesa ose `null`, si dhe JSON i keqformuar, tip i gabuar ose body bosh/`null` → **HTTP 400 `INVALID_REQUEST_BODY`** (ProblemDetails i kontrolluar), para handler-it, zero mutation/audit/receipt — mungesa nuk lidhet më si `Guid.Empty` ose version `0`; çdo 400 e këtij endpoint-i mbart `errorCode`. `expectedVersion: 0` eksplicit dhe vargu numerik (p.sh. `"1"`) mbeten të vlefshëm. Gabimet e kontrolluara të AccessControl dokumentohen në OpenAPI si `AccessControlProblemDetails` me `errorCode` string të detyrueshëm; skema e kërkesës në OpenAPI mbetet e pandryshuar.

*Closure i datuar (05.10.2026 — insertion-only):* korrigjimi i kontratës më sipër = **COMPLETED / MERGED** me PR #47 (squash `7cc13d3`, mergedAt 2026-10-05T13:29:43Z; tree `1e7b7c6e…` = kandidati i miratuar = tree e merge-uar). Saktësim nga C2: kërkesa pa body dhe pa Content-Type → **HTTP 400 `INVALID_REQUEST_BODY`** (prania e body-t sipas `IHttpRequestBodyDetectionFeature.CanHaveBody` të ASP.NET Core), ndërsa body real me media type të pambështetur mbetet **415**. Evidenca dhe follow-up-et jo-bllokuese: SSOT v2.43 (Decisions log, 05.10.2026) dhe [receipt-i i PR #47](https://github.com/blendsylqevci/idata-platform/pull/47#issuecomment-5995683201).

## Audit ↔ command / correlation DB-enforced (§6)

Çdo command i suksesshëm: `command_receipt` ID i krijuar PARA SaveChanges; correlation ID teknik server-generated (uuid, jo nga klienti; zero PII/path/payload); `audit_log.command_receipt_id` + `audit_log.correlation_id`; mutation + audit + receipt = NJË transaction. Lidhja DB-enforced tenant-safe: composite FK `(business_id, command_receipt_id) → command_receipt(business_id, id)` (receipt ka çelës alternativ `(business_id, id)`) — asnjë link cross-tenant/inekzistent nuk mund të ekzistojë (i provuar në DB). Audit mbetet i redaktuar (vetëm role_id before/after).

## Audit append-only + runtime-role (§8A)

`audit_log` mbrohet nga triggers BEFORE UPDATE/DELETE/TRUNCATE. Integration tests përdorin DY role sintetike: migration/admin (`idata`) për setup; **rol runtime jo-owner/jo-superuser** (pa CREATE/ALTER/DROP, pa disable-trigger, DML minimal) nën të cilin ekzekutohen komandat reale. Audit-write failure DHE receipt-write failure → rollback i plotë i transaction-it (503 i kontrolluar, zero efekt). **Formulim i saktë:** append-only vlen për DML të zakonshëm kur protections janë enabled; owner/superuser mund t'i çaktivizojë — **NUK tamper-proof.** **Production application DB-role/provider provisioning = OPEN** (ADR-026).

## Rollback testues atomik + DB safety (§9)

Rollback 0002→0001 (helper TEST-ONLY, JO production CLI): guard tripalësh (env {Development,CI} + opt-in ekzakt + DB sintetike) PARA çdo lidhjeje; DownOperations e 0002 DHE heqja e row-it të historisë ekzekutohen në NJË PostgreSQL transaction — commit vetëm pasi të dyja kalojnë. Test me failure-injection PAS DownOperations, PARA heqjes së historisë → transaction rrëzohet plotësisht: schema mbetet 0002, history {0001,0002}, sentinel i ruajtur, ZERO partial-down. `PlatformTestDatabase`: zero interpolim manual — connection string nga snapshot i VALIDUAR `PlatformDatabaseSettings` (`NpgsqlConnectionStringBuilder`); maintenance DB fikse `postgres` (verifikuar me `current_database()`); matrica negative (override `Database=`, separators/control/newline, port out-of-range, target real, prefiks pa suffix) → refuzim para çdo CREATE/DROP.

**DEVIATION e dokumentuar (EF Core 10):** targeted-migrate refuzon ID-të jo-timestamp (`0001_*`/`0002_*`) me "migration not found"; IDA e 0001 mbetet e pandryshueshme, ndaj forward-0001/rollback përdorin mekanizmin e vetë EF-it (Up/DownOperations → `IMigrationsSqlGenerator`), target i FIKSUAR.

## Reliability Gate — Failure matrix (moduli AccessControl/Audit, kritik)

Të 8 skenarët standardë. **5 dhe 7 = TESTUAR REALISHT; 6 = fixture i kufizuar (host recreation/restart), JO crash-proof universal; 8 = PARTIAL/OPEN me waiver `HG-F1.1-REL-08`** (vetëm audit/receipt-write denial + transaction rollback janë testuar realisht, JO disk/queue-full real, alerting apo recovery). Skenarët 1–4 (Internet/SEF/Printer/Device) = N/A me justifikim §5.1 (mbulohen nga shtresa sync/POS/fiscal e F2+; asnjë rrugë fiskale/offline/periferike në këtë modul; handoff konkret te F2+/sync/POS/SEF) — **PENDING PM acceptance** (Pronari nuk i ka aprovuar ende); NUK kthehen në PASS. **Waiver ≠ PASS:** skenari 8 mbetet PARTIAL/OPEN.

| Failure scenario | Expected behavior | Data safety | User-visible | Recovery | Test | Evidence |
|---|---|---|---|---|---|---|
| 1. Internet down | N/A | — | — | — | N/A — shtresa sync/outbox (PoC-3/F2) | §5.1; PENDING PM |
| 2. SEF/ATK down | N/A | — | — | — | N/A — asnjë rrugë fiskale (Step B/C NO-GO) | §5.1; PENDING PM |
| 3. Printer/peripheral down | N/A | — | — | — | N/A — asnjë periferik | §5.1; PENDING PM |
| 4. Device crash/offline | N/A | — | — | — | N/A — device/sync layer F2+ | §5.1; PENDING PM |
| 5. Retry / idempotency | Replay kthen rezultatin e ruajtur; key i njëjtë me kërkesë tjetër → 409 | Zero mutation/audit i dyfishtë (UNIQUE + fingerprint) | I njëjti 200 (replay) ose 409 uniform | Deterministik; riprovim i sigurt | `IdempotencyEndpointTests`, `AccessControlHandlerTests` | Test output (TESTUAR) |
| 6. Server / host restart | Riprovim me të njëjtin key pas host-recreation → rezultat i njëjtë | Idempotency-Key + receipt; zero efekt i dytë | Klienti merr të njëjtin 200 | Idempotency bën riprovimin të sigurt | `IdempotencyEndpointTests.Host_recreation_replay_*` (host i ri mbi të njëjtën DB) — **fixture i kufizuar, JO crash-proof universal** | Test output |
| 7. DB unavailable | 503 PLATFORM_UNAVAILABLE, zero detaje | Asnjë mutation; probe/handler fail-safe | 503 ProblemDetails stabil | Auto-recover kur DB kthehet | `ApiSurfaceTests.DB_e_paarritshme_*` | Test output (TESTUAR) |
| 8. Disk / queue full | **PARTIAL — waiver `HG-F1.1-REL-08` (24.07.2026), APROVUAR nga Pronari.** F1.1 provon rollback atomik + HTTP 503 gjatë write-denial, por JO disk/queue-full real, alerting ose recovery. Risku mbetet OPEN. Owner: Tech/Platform Owner. Mbyllet para F1 COMPLETE dhe para çdo production Go. NUK pretendohet production readiness. | Atomiciteti i transaction-it (audit/receipt-write failure → rollback i plotë) | 503 i kontrolluar | Riprovim pas lirimit; idempotency parandalon dublim | `AuditProtectionTests` (write-denial + rollback) — JO disk-full real | Test output + waiver `HG-F1.1-REL-08` (risk + afat + owner të loguar në Tracker) |

## Parakushti i testeve të integrimit (PostgreSQL)

Suita e integrimit kërkon një **PostgreSQL test/disposable të arritshëm**;
konfigurimi merret nga `PGHOST` / `PGPORT` / `PGUSER` (default-et e kodit:
`localhost` / `5432` / `idata`). CI përdor PostgreSQL service-in e vet.
Gjatë review-t lokal u verifikua me një instancë sintetike në `PGPORT=5455` —
**vetëm shembull lokal i verifikuar**, jo portë e detyrueshme dhe jo kontratë
e repo-s; çdo zhvillues mund të përdorë instancën/portën e vet sintetike.

## Test counts

Architecture 10 · Unit 90 · Integration 100 (kundër PostgreSQL real sintetik, DB unik per test): **200 target-tests** (build 0 warnings, zero skip). Client 2/2 · Backoffice 6/6 · Legacy turbo 8/8 build + 12/12 test. OpenAPI/client generate ×2 zero-diff.

## Historia dhe gitleaks

Historia aktive e degës u rindërtua nga `origin/main` që fixtures sintetike (GUID hex-shkronja) të jenë të pastra QË NGA COMMIT-I ku testet lindin — pa artefakt zero-GUID, pa commit të hequr pas-fakti. **`.gitleaks.toml` është IDENTIK me `origin/main`** (zero përjashtim detector-wide për PII); mbrojtjet e guard/gitleaks/integrity-linter të paprekura.

## Gates OPEN të paprekura

ADR-014 (pooling) · ADR-025 (encryption/key custody) · ADR-026 (hosting/secrets/providers + production credential/DB-role model) · ADR-028 (identity provider/store; ndarja 401/403) · ENG-004 (group-level audit — fallback business-scoped-only AKTIV, OPEN) · production RLS · **role hierarchy** · **permission dominance / delegation ceiling** · **last-admin protection** · role globale/system-wide (deferred) · **production role provisioning** (F1.1 = vetëm seed/migration) · break-glass · payroll · `roleCode`/hierarchy · **cross-writer authority lock-order enforcement (`ENG-005`) — OPEN** · **reliability skenari 8 (disk/queue-full) — waiver `HG-F1.1-REL-08`, PARTIAL/OPEN**. **Step B/C, PREFLIGHT, Tier 2 = NO-GO; RECERT = JO.**

## Rollback

Hiqet moduli AccessControl + migrimi 0002 pa prekur legacy oracle: fshi `src/platform/Modules/AccessControl/`, storage records + FK/version/correlation nga Persistence, migrimin 0002 (+triggers në Down), testet F1.1, endpointet nga OpenAPI/klient, assert-counts në `ci.yml` (2→1). Migrimi 0001, legacy TS dhe DB-të reale nuk preken.
