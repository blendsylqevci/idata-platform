# F1.1 — Tenant Authorization & Business-Scoped Audit Core (Implementation Record)

**Statusi:** **IMPLEMENTED / PENDING_INDEPENDENT_REVIEW** — PASS vetëm pas Fable delta-review → Codex review (PM_REVIEW_PROTOCOL) → Human Gate i Pronarit → merge i aprovuar.
**Autoriteti:** ADR-024 (MERGED / ACCEPTED, PR #23, merge `e0d4cdc`) + Platform Foundation Bootstrap (COMPLETED, PR #24 merged, squash `0f12488c`). **Data:** 24.07.2026 (v2 — post correction pass i review-t të pavarur). **Increment:** F1.1 — vertical slice i kufizuar i F1, JO gjithë F1.

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

## Optimistic concurrency (CAS) (§5)

`business_membership.version` (bigint NOT NULL, nis 0) = EF concurrency token DB-backed. PUT kërkon `expectedVersion` të tipizuar (në command/fingerprint/OpenAPI/klient/response). Konflikti CAS → **HTTP 409 `CONCURRENCY_CONFLICT`** (i dalluar nga `IDEMPOTENCY_CONFLICT`), zero mutation/audit/receipt nga humbësi. Dy kërkesa paralele me key të ndryshëm + të njëjtin `expectedVersion` + role të ndryshme → saktësisht një 200 + një 409 CONCURRENCY_CONFLICT; final role/version = fituesi; saktësisht një audit + një receipt (i provuar).

## Audit ↔ command / correlation DB-enforced (§6)

Çdo command i suksesshëm: `command_receipt` ID i krijuar PARA SaveChanges; correlation ID teknik server-generated (uuid, jo nga klienti; zero PII/path/payload); `audit_log.command_receipt_id` + `audit_log.correlation_id`; mutation + audit + receipt = NJË transaction. Lidhja DB-enforced tenant-safe: composite FK `(business_id, command_receipt_id) → command_receipt(business_id, id)` (receipt ka çelës alternativ `(business_id, id)`) — asnjë link cross-tenant/inekzistent nuk mund të ekzistojë (i provuar në DB). Audit mbetet i redaktuar (vetëm role_id before/after).

## Audit append-only + runtime-role (§8A)

`audit_log` mbrohet nga triggers BEFORE UPDATE/DELETE/TRUNCATE. Integration tests përdorin DY role sintetike: migration/admin (`idata`) për setup; **rol runtime jo-owner/jo-superuser** (pa CREATE/ALTER/DROP, pa disable-trigger, DML minimal) nën të cilin ekzekutohen komandat reale. Audit-write failure DHE receipt-write failure → rollback i plotë i transaction-it (503 i kontrolluar, zero efekt). **Formulim i saktë:** append-only vlen për DML të zakonshëm kur protections janë enabled; owner/superuser mund t'i çaktivizojë — **NUK tamper-proof.** **Production application DB-role/provider provisioning = OPEN** (ADR-026).

## Rollback testues atomik + DB safety (§9)

Rollback 0002→0001 (helper TEST-ONLY, JO production CLI): guard tripalësh (env {Development,CI} + opt-in ekzakt + DB sintetike) PARA çdo lidhjeje; DownOperations e 0002 DHE heqja e row-it të historisë ekzekutohen në NJË PostgreSQL transaction — commit vetëm pasi të dyja kalojnë. Test me failure-injection PAS DownOperations, PARA heqjes së historisë → transaction rrëzohet plotësisht: schema mbetet 0002, history {0001,0002}, sentinel i ruajtur, ZERO partial-down. `PlatformTestDatabase`: zero interpolim manual — connection string nga snapshot i VALIDUAR `PlatformDatabaseSettings` (`NpgsqlConnectionStringBuilder`); maintenance DB fikse `postgres` (verifikuar me `current_database()`); matrica negative (override `Database=`, separators/control/newline, port out-of-range, target real, prefiks pa suffix) → refuzim para çdo CREATE/DROP.

**DEVIATION e dokumentuar (EF Core 10):** targeted-migrate refuzon ID-të jo-timestamp (`0001_*`/`0002_*`) me "migration not found"; IDA e 0001 mbetet e pandryshueshme, ndaj forward-0001/rollback përdorin mekanizmin e vetë EF-it (Up/DownOperations → `IMigrationsSqlGenerator`), target i FIKSUAR.

## Reliability Gate — Failure matrix (moduli AccessControl/Audit, kritik)

Të 8 skenarët standardë. **5 dhe 7 = TESTUAR REALISHT; 6 = fixture i kufizuar (host recreation/restart), JO crash-proof universal; 8 = PARTIAL/OPEN për disk/queue-full** (vetëm audit/receipt-write denial + transaction rollback janë testuar realisht, JO disk full). Skenarët 1–4 (Internet/SEF/Printer/Device) = N/A me justifikim §5.1 (mbulohen nga shtresa sync/POS/fiscal e F2+; asnjë rrugë fiskale/offline/periferike në këtë modul) — **pending PM acceptance**.

| Failure scenario | Expected behavior | Data safety | User-visible | Recovery | Test | Evidence |
|---|---|---|---|---|---|---|
| 1. Internet down | N/A | — | — | — | N/A — shtresa sync/outbox (PoC-3/F2) | §5.1; pending PM |
| 2. SEF/ATK down | N/A | — | — | — | N/A — asnjë rrugë fiskale (Step B/C NO-GO) | §5.1; pending PM |
| 3. Printer/peripheral down | N/A | — | — | — | N/A — asnjë periferik | §5.1; pending PM |
| 4. Device crash/offline | N/A | — | — | — | N/A — device/sync layer F2+ | §5.1; pending PM |
| 5. Retry / idempotency | Replay kthen rezultatin e ruajtur; key i njëjtë me kërkesë tjetër → 409 | Zero mutation/audit i dyfishtë (UNIQUE + fingerprint) | I njëjti 200 (replay) ose 409 uniform | Deterministik; riprovim i sigurt | `IdempotencyEndpointTests`, `AccessControlHandlerTests` | Test output (TESTUAR) |
| 6. Server / host restart | Riprovim me të njëjtin key pas host-recreation → rezultat i njëjtë | Idempotency-Key + receipt; zero efekt i dytë | Klienti merr të njëjtin 200 | Idempotency bën riprovimin të sigurt | `IdempotencyEndpointTests.Host_recreation_replay_*` (host i ri mbi të njëjtën DB) — **fixture i kufizuar, JO crash-proof universal** | Test output |
| 7. DB unavailable | 503 PLATFORM_UNAVAILABLE, zero detaje | Asnjë mutation; probe/handler fail-safe | 503 ProblemDetails stabil | Auto-recover kur DB kthehet | `ApiSurfaceTests.DB_e_paarritshme_*` | Test output (TESTUAR) |
| 8. Disk / queue full | **PARTIAL/OPEN** — vetëm write-denial + rollback i provuar (JO disk-full real) | Atomiciteti i transaction-it (audit/receipt-write failure → rollback) | 503 i kontrolluar | Riprovim pas lirimit; idempotency parandalon dublim | `AuditProtectionTests` (write-denial + rollback) | Test output. **OPEN:** disk/queue capacity failure real — Owner: Tech/Platform Owner; gate: F1 observability/reliability hardening; JO i simuluar këtu |

## Test counts

Architecture 10 · Unit 80 · Integration 51 (kundër PostgreSQL real sintetik): **141 target-tests** (build 0 warnings, zero skip). Client 2/2 · Backoffice 6/6 · Legacy turbo 8/8 build + 12/12 test. OpenAPI/client generate ×2 zero-diff.

## Historia dhe gitleaks

Historia aktive e degës u rindërtua nga `origin/main` që fixtures sintetike (GUID hex-shkronja) të jenë të pastra QË NGA COMMIT-I ku testet lindin — pa artefakt zero-GUID, pa commit të hequr pas-fakti. **`.gitleaks.toml` është IDENTIK me `origin/main`** (zero përjashtim detector-wide për PII); mbrojtjet e guard/gitleaks/integrity-linter të paprekura.

## Gates OPEN të paprekura

ADR-014 (pooling) · ADR-025 (encryption/key custody) · ADR-026 (hosting/secrets/providers + production credential/DB-role model) · ADR-028 (identity provider/store; ndarja 401/403) · ENG-004 (group-level audit — fallback business-scoped-only AKTIV, OPEN) · production RLS · role globale/system-wide (deferred) · break-glass · payroll · `roleCode`/hierarchy. **Step B/C, PREFLIGHT, Tier 2 = NO-GO; RECERT = JO.**

## Rollback

Hiqet moduli AccessControl + migrimi 0002 pa prekur legacy oracle: fshi `src/platform/Modules/AccessControl/`, storage records + FK/version/correlation nga Persistence, migrimin 0002 (+triggers në Down), testet F1.1, endpointet nga OpenAPI/klient, assert-counts në `ci.yml` (2→1). Migrimi 0001, legacy TS dhe DB-të reale nuk preken.
