# Faza 0 — Decision Gates / ADR Register (iData+)

**Versioni:** 1.7 · **Data:** 28 Korrik 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.32 §2, §11
**Format:** ADR (Architecture Decision Record). Status: **Accepted** (mbyllur) / **Open** (vendoset në gate-in e caktuar) / **Proposed** / **Superseded** (ruhet si histori, por nuk është më vendimi aktiv).
Çdo vendim i ri → kjo skedar + një rresht te SSOT Decisions Log.

## Të mbyllura (Accepted / Superseded)
| ADR | Vendimi | Data | Shënim |
|---|---|---|---|
| ADR-001 | **SUPERSEDED nga ADR-024** — stack-u historik Fortified Hybrid (NestJS+Next.js+RN+Postgres+Supabase) | 27.06 | Evidencë e walking skeleton-it ekzistues; JO target-i aktiv |
| ADR-002 | **Mobile = native React Native/Expo; PWA e përjashtuar** | 27.06 | offline+BT+hub |
| ADR-003 | **Migrim = fillim i pastër; cutover 1 Jan 2027** (vetëm saldo hapëse + master-data) | 27.06 | §8 |
| ADR-004 | **Payroll → Faza 1** (RISHIKUAR v2.17 — KABI/kontabilist detyrueshëm; dikur Faza 2, 27.06) | 05.07 | §4 |
| ADR-005 | **COGS = default mesatare e ponderuar** + batch costing KABI (finalizim me kontabilistin) | 27.06 | §4 |
| ADR-006 | **Node = 24 LTS**, fiksuar me `.nvmrc` | 27.06 | Active LTS→2028 |
| ADR-007 | **RBAC pa "Fshirje" për financiare** → Anulim/Korrigjim + Aprovim | 27.06 | §9 |
| ADR-008 | **Ndarje rolesh + iCode Support = break-glass** | 27.06 | §9 |
| ADR-009 | **Konsignacion = shitje direkte me të drejtë kthimi** (qumështore) | 27.06 | njohja kontabël → pending |
| ADR-010 | **SUPERSEDED nga ADR-024** — Supabase nuk është më dependency qendrore e target-it | 23.07 | PostgreSQL + OIDC/OAuth2 contract; identity provider/store mbeten gate ADR-028 |
| ADR-013 | **Edge Hub = ASP.NET Core + PostgreSQL lokal; POS/mobile = SQLite lokal** | 23.07 | Pranuar përmes ADR-024; hardware validation mbetet gate |
| ADR-015 | **Backup / PITR / restore drill** i detyrueshëm para Fazës 1 | 28.06 | I promovuar/mbyllur nga ADR-023 |
| ADR-018 | **Repo neutral `idata-platform` + monorepo ekzistuese** | 23.07 | E zbatuar; ADR-024 e ruan të njëjtën repo |
| ADR-019 | **SEF Test Phase AKTIV** — "Testim para Aplikimit" pranuar; test server `fiskalizimi-test.atk-ks.org` | 28.06 | §7 / file 06 |
| ADR-020 | **Fiskalizimi GitHub = referencë e detyrueshme** (github.com/fiskalizimi: `models.proto`, ECDSA P-256, QR, **tax A/C/D/E**, **money per-field** items €0.0001/totals cents, **CouponType Sale=1/Cancel=2/Return=3**); **PoC SEF s'nis pa analizë** | 28.06 | file 06 · **shih shënimin korrigjues 12.07.2026 më poshtë** |
| ADR-021 | **Secret management** — secrets store; asnjë sekret/SEF-ID/çelës në repo; audit redaction | 28.06 | §9 |
| ADR-022 | **Branch protection + GitHub 2FA** (PR-based, review i detyrueshëm, pa force-push) | 28.06 | §9 |
| ADR-023 | **Backup / PITR / restore drill** i detyrueshëm para Fazës 1 (promovon ADR-015) | 28.06 | §2 |
| ADR-024 | **Platform Runtime & Architecture Stack Freeze** — .NET/Angular/POS i instalueshëm/RN/PostgreSQL, modular monolith, migrim inkremental | 23.07 (merge PR #23 `e0d4cdc`) | **MERGED/ACCEPTED**; Bootstrap pre-F1 = COMPLETED (PR #24, squash `0f12488c`); **F1.1 = COMPLETED/MERGED (PR #25, squash `1dc06d5`, 27.07.2026)**; F1 = ACTIVE / NOT COMPLETE; **Increment A = COMPLETED/MERGED (PR #27 `abc2b291`, 28.07.2026)**; pjesa përtej incrementeve të merge-uara = **NOT YET IMPLEMENTED**; wrapper/OS = ADR-027 OPEN; identity = ADR-028 **ACCEPTED / EFFECTIVE** (28.07, PR #26 `305aefd`; implementimi = Increment B ACTIVE — **B.1 = COMPLETED/MERGED, PR #28 `d6ddff2`, 02.08.2026**; Pre-B2 Increment 1 / ENG-007 = CLOSED-MERGED, PR #29 `8f7a22c`; hapi aktual = Pre-B2 Increment 2 — ENG-006 + ENG-008, IMPLEMENTED / PENDING_INDEPENDENT_REVIEW; B.2/B.3 NOT STARTED / BLOCKED) |
| ADR-028 | **Identity Authority & Store** — ASP.NET Core Identity store + OpenIddict self-hosted authority; schema `identity` e ndarë; Identity roles ≠ tenant RBAC; `app_user` = projection | 28.07 (merge PR #26 `305aefd`) | **ACCEPTED / EFFECTIVE** pas Fable→Codex→Human Gate; **implementimi = Increment B (ACTIVE — B.1 = COMPLETED/MERGED, PR #28 `d6ddff2`, 02.08.2026; hapi aktual = Pre-B2 Increment 1 ENG-007 IMPLEMENTED_POLICY / PENDING_INDEPENDENT_REVIEW; B.2/B.3 NOT STARTED / BLOCKED derisa ENG-006 + ENG-007 të mbyllen)**; specifikimi subordinate = `Identity_Authority_Store_Decision.md` |
| ADR-029 | **Application Architecture Convention** — module-first Clean/Onion, vertical slices, CQRS selektiv, martinothamar/Mediator (Scoped), thin Controllers + BaseApiController, Scalar vetëm në Development, zero Common/Utils, 0003+ forward-only | 28.07 (merge PR #26 `305aefd`) | **ACCEPTED / EFFECTIVE** pas Fable→Codex→Human Gate; **Increment A = COMPLETED/MERGED (PR #27 `abc2b291`); B.1 = COMPLETED/MERGED (PR #28 `d6ddff2`); hapi aktual = Pre-B2 Increment 1 — ENG-007 (IMPLEMENTED_POLICY / PENDING_INDEPENDENT_REVIEW)**; specifikimi subordinate = `Application_Architecture_Convention.md` |

**Shënim korrigjues i datuar (12.07.2026 — full-corpus re-audit; NUK rishkruhet historia e ADR-020):** formulimi origjinal "items €0.0001/totals cents" është **i pasaktë për `CouponItem.Total`** — kontrata e saktë: **unit `Price` = ×10000; item `Total` + coupon totals/payments/tax = ×100 (cents)** (Gate v2.0 §3; T2-01). Gjithashtu **org-u zyrtar ka konflikte të regjistruara**: firma **C# P1363 vs Go ASN.1 DER** (CONF-02 — encoding = OPEN deri në server evidence); **PHP proto i vjetër inkompatibil**; **Ruby pa proto**; **Swagger v0.9 vs main pas tag-ut** (CONF-03/04) — **asnjë implementim i vetëm s'është kanonik në heshtje**; referenca e plotë = `../05-sef-atk/ATK_Source_Register.md` §4–§5.

### ADR-024 · Platform Runtime & Architecture Stack Freeze

**Statusi:** **Accepted nga Pronari / Human Gate më 23.07.2026; effective me merge-in e PR-së së kanonizimit.**
**Kufiri i pretendimit:** ky është **target-i i aprovuar**. Merge-i docs-only **NUK** do të thotë se target stack-u është implementuar, migruar, testuar ose production-ready.

**Konteksti.** Walking skeleton-i aktual provoi monorepo-n, CI-në dhe disa invariante/PoC në TypeScript, por nuk është produkti final. Para moduleve të biznesit duhet të shmanget portimi i tyre dy herë dhe duhet të caktohet një arkitekturë afatgjatë, e testueshme dhe e ekstraktueshme pa hyrë menjëherë në microservices.

**Vendimi — runtime dhe deployables:**

| Sipërfaqja | Target-i i aprovuar | Kufiri |
|---|---|---|
| Admin / Backoffice | **Angular web** | ERP, Accounting, WMS, OMS, Production dhe Finance Ops janë module të izoluara brenda një shell-i; jo sisteme/repo të pavarura |
| POS | **Angular UI**, aplikacion desktop i instalueshëm | Renderer-i Angular është i ndarë nga Backoffice; wrapper-i dhe Windows/macOS/Linux mbeten OPEN në ADR-027, pas provave ADR-017/PoC-4b |
| Mobile | **React Native + Expo Development Build** | KABI terren + PLANET kamarier; SQLite lokal |
| KDS | Klient i fokusuar LAN/kiosk | Shell-i final varet nga PoC-4b, floor-plan dhe hardware-i |
| API / domain | **C# + ASP.NET Core (.NET 10 target)** + pure C# domain libraries | Platform API; logjika e biznesit nuk jeton në UI |
| Background | **.NET Worker Services** | Outbox, retry, jobs, integration processing |
| Edge | **ASP.NET Core Edge Hub + PostgreSQL lokal** | Autoritet operacional i lokacionit gjatë ndërprerjes; sinkronizohet me cloud |
| POS Local Host | **.NET process i shpërndarë me instalimin e POS-it** | Zotëron domain/outbox/SQLite/keys dhe ekspozon kontratë lokale IPC të tipizuar; renderer-i nuk hap DB/keys; wrapper-i vendoset në ADR-027 |
| Pajisje | **.NET Device Agent** | Printerë, skanerë, peshore dhe integrime lokale; pa DB access nga UI |
| Identity | **OIDC/OAuth2 contract + provider-neutral ASP.NET Core authorization boundary** | Një authority/login/SSO, MFA, company selector, RBAC per modul, tenant authorization dhe break-glass i audituar; provider/store = **ADR-028 ACCEPTED / EFFECTIVE** (PR #26 merged, squash `305aefd`, 28.07.2026); implementimi Identity = **Increment B ACTIVE (B.1 = COMPLETED/MERGED `d6ddff2`; hapi aktual = Pre-B2 Increment 1 ENG-007; B.2/B.3 NOT STARTED / BLOCKED)** |
| Të dhënat | **PostgreSQL qendror** + module-owned schemas; **SQLite e enkriptuar** në POS/mobile | Cloud = source-of-record afatgjatë; POS SQLite zotërohet nga local host-i, mobile SQLite nga data layer-i; asnjë renderer/UI nuk hap DB/keys direkt |
| Kontratat | **OpenAPI → generated TypeScript clients** | I vetmi kufi i tipizuar UI/API; DTO-të nuk kopjohen me dorë |
| Offline | **Versioned outbox + idempotency + retry/replay** | Sync vendor-i nuk kyçet në këtë ADR |

**Vendimi — forma arkitekturore:**

1. **Modular Monolith fillimisht, jo microservices.** Çdo modul ka ownership të qartë të modelit/schema-s/API-së dhe nuk shkruan direkt në tabelat e modulit tjetër. Nxjerrja në service/repo tjetër kërkon evidence operacionale + ADR të ri.
2. **Module-first; Clean/Onion brenda modulit.** Domain nuk varet nga Infrastructure/UI. Application orkestron use-cases; adapters lidhin DB/API/device.
3. **Vertical slices për delivery.** Një capability përfundon domain→application→API→UI→tests, në vend të "krejt backend-i pastaj krejt frontend-i".
4. **CQRS selektiv.** Commands dhe queries ndahen kur ka rregulla/invariante, audit, performancë ose modele leximi të ndryshme; CRUD i thjeshtë nuk dyfishohet pa arsye.
5. **Një migration owner.** EF Core menaxhon migrimet normale; constraints/triggers/functions PostgreSQL shkruhen si SQL i audituar brenda migrimeve. Nuk lejohen dy mekanizma konkurrues production.
6. **Fiscal Core mbetet bounded certification surface** brenda së njëjtës monorepo; kufiri final i paketës së certifikimit mbetet `ATK-010`.
7. **Supabase hiqet si dependency qendrore e target-it.** Node/pnpm mbeten për Angular, React Native dhe tooling; nuk janë më authority i backend-it të biznesit. Ky vendim nuk zgjedh identity provider-in.
8. **Fiscal Semantics Conformance Contract është language-neutral authority.** Rregullat/vectors versionohen një herë; C# është reference/server implementation, ndërsa implementimi i kufizuar TypeScript në mobile offline duhet të japë rezultate decimal/protobuf/hash byte-identike. OpenAPI ndan DTO-të, jo logjikën. Step C e provon këtë kontratë në të dy runtime-et para çdo Human Gate fiskal.
9. **Shumëgjuhësia është kërkesë themelore, jo polish i vonshëm:** Shqip = default, Anglisht dhe Serbisht = të detyrueshme. POS-i zgjedh gjuhën e kuponit/faturës fiskale; resource keys, fallback, font/layout dhe print fixtures hyjnë nga F1 dhe mbyllen para KABI/SEF release. **Atribuimi:** CORE-02 / UA 01/2026 §32.2.9, f.27 kërkon shumëgjuhësi sipas legjislacionit për gjuhët zyrtare (SQ/SR në Ledger U11); EN është target i detyrueshëm i Pronarit/produktit. Ledger U11 mbetet MISSING derisa implementimi/testet ta provojnë.
10. **Enkriptimi at-rest është rezultat i detyrueshëm.** Mekanizmi/key custody mbyllen në ADR-025 para se të futen të dhëna reale në POS/mobile/Edge/cloud; renderer-at nuk marrin çelësa. Providerët cloud/storage/secrets mbyllen veçmas në ADR-026.

**Migrimi (strangler / incremental, jo big-bang):**

1. Kanonizo ADR-024 dhe ndalo para merge-it për review + Human Gate.
2. Ndërto **Platform Foundation Bootstrap** pre-F1: solution/build/CI, një API/module smoke path, PostgreSQL dev migration path, OpenAPI client generation dhe Angular shell minimal. Identity/audit/outbox/observability në këtë bootstrap janë vetëm contracts/smoke wiring; production enforcement mbetet deliverable i F1.
3. Mbyll F1 Foundation në stack-un e ri (Identity/Tenant/RBAC, audit, encryption/key-custody baseline, observability dhe recovery), pastaj ndiq zinxhirin fiskal në Fiscal bounded context të ri: **Step B / ATK-012 → Step C / cross-runtime conformance → independent re-review → HUMAN GATE → private `/ca/verify` PREFLIGHT → pjesa tjetër e Tier 2**. Asnjë endpoint real para Human Gate-it fiskal.
4. Ndërto vertical slices modul-pas-moduli: Master Data/Tax → Accounting → Inventory/WMS → OMS/Sales/Purchasing → Production/Recipes/KABI → POS/KDS/PLANET → Finance Ops/reports.
5. Ruaj dokumentet canonical, governance/linter-in dhe PoC/testet ekzistuese si evidence/oracle. Legacy Nest/Next/TS hiqet **i fundit**, vetëm pas parity tests, rollback plan, independent review dhe Human Gate. Pa dual-write afatgjatë.

**Gates që mbeten Open:** ADR-011 (PowerSync vs custom), ADR-012 (kufiri final DB-function vs C# për GL), ADR-014 (pooling), ADR-016 (EAS vs bare), ADR-017/ATK-004 (printer/hardware), ADR-025 (encryption/key custody), ADR-026 (hosting/object storage/secrets provider), ADR-027 (desktop wrapper + OS matrix), KDS shell/hardware, P1363 vs DER dhe sjelljet server-dependent të ATK-së. Këto nuk e rihapin runtime-in bazë ose kufijtë e deployables; vendosen në gate-in e tyre. ADR-028 dhe ADR-029 **NUK janë gates OPEN** — **ACCEPTED / EFFECTIVE** (PR #26 `305aefd`, 28.07.2026); shih seksionin "Të pranuara më 28.07.2026".

*(**HISTORICAL — anotim i datuar, pa rishkrim historie:** në tekstin origjinal të ADR-024 (23.07.2026), ADR-028 renditej ndër gates OPEN; më 27.07.2026 ADR-028 kaloi në PROPOSED / OWNER-DIRECTED TARGET / PENDING_INDEPENDENT_REVIEW. Kjo hyrje ruan gjurmën e atij tranzicioni.)*

**Pasojat:** do të ketë përkohësisht dy runtime-e në repo dhe kosto portimi/testesh; në këmbim ruhet evidence ekzistuese, shmanget rewrite big-bang dhe çdo modul mund të mbyllet me parity të matshme. Asnjë capability e vjetër nuk promovohet automatikisht në IMPLEMENTED/TESTED në target.

## Të hapura (Open — vendosen në Faza 0)
### ADR-011 · Sync: PowerSync vs custom
**Konteksti:** offline-first, 60–80 konkurrentë, hub lokal. **Opsione:** PowerSync (gati) vs custom outbox+CDC. **Rekomandim:** vendoset **pas PoC-3**. **Pronari:** Claude Code. **Afati:** pas PoC-3.

### ADR-012 · GL posting: PostgreSQL invariants/functions vs C# transaction
**Konteksti:** atomicitet i postimit (debit=kredit). **Opsione:** (a) invarianti/postimi kritik në PostgreSQL functions/constraints, orkestrim ASP.NET Core; (b) orkestrim C# me transaksion dhe vetëm constraints minimale në DB. **Rekomandim:** hibrid, i matur me testet e target-it. **Pronari:** Tech + PM. **Afati:** para Accounting Core freeze.
**Rider (input nga PoC-2):** Vendimi final përfshin migrimin **DROP/supersede** të funksionit PoC `poc2_post_sale18` dhe konsolidimin e kodeve të llogarive në **NJË burim config** (sot 3 kopje të qëllimshme: `packages/accounting/src/accounts.ts`, `packages/database/src/poc2-fixtures.ts`, plpgsql) — pas ACC-003 kalohen në parametra bind. *(Kusht i vendimit të ardhshëm, jo vendim vetë; ADR-012 mbetet Open.)*

### ADR-014 · Connection pooling
**Opsione:** PgBouncer/Supavisor; pool sizing për 60–80 konkurrentë + hub. **Rekomandim:** pooler i dedikuar. **Afati:** Faza 0.

### ADR-016 · RN/Expo build strategy
**Opsione:** EAS (managed) vs bare workflow. **Konsiderata:** printer BT, native modules, App Store/Play Store. **Rekomandim:** EAS fillimisht, bare nëse duhen module native të thella. **Afati:** Faza 0.

### ADR-017 · Printera fiskalë: hardware vs SEF software
**Konteksti:** Codex listoi drajverë fiskalë (Tremol/Datecs); me SEF software-fiskalizim mund të mjaftojë printer termik + nënshkrim software. **Veprim:** **konfirmo gjatë test phase AKTIVE (kanali SEF u hap), PARA blerjes së pajisjeve.** **Pronari:** Pronari→ATK. **Afati:** test phase. (= SSOT pyetja #8)

### ADR-025 · Data-at-Rest Encryption & Key Custody
**Statusi:** Open — rezultati "të dhënat reale enkriptohen at-rest" është i detyrueshëm; mekanizmi dhe lifecycle-i i çelësave nuk hamendësohen.
**Scope:** POS/mobile SQLite (SQLCipher ose ekuivalent i provuar + OS keystore), Edge/cloud PostgreSQL (disk/volume encryption + selective field/pgcrypto vetëm ku justifikohet), audit-log encryption/integrity, backup/object storage, rotation/recovery/revocation dhe zero keys në renderer/repo/log.
**Pronari:** Pronari + Tech. **Afati:** vendim para F1 data-bearing implementation; evidence para pilotit të parë me të dhëna reale.

### ADR-026 · Hosting, Object Storage & Secrets Providers
**Statusi:** Open — ADR-010 mbylli Supabase si dependency qendrore, jo zgjedhjen e providerëve zëvendësues.
**Scope:** rajoni/rezidenca dhe DPA, compute/PostgreSQL, object storage, secrets/KMS, backup/PITR, queue/realtime, observability, portability/exit plan dhe environment separation.
**Pronari:** Pronari + Tech. **Afati:** para mjedisit të parë jo-lokal / para lidhjes së providerit në kod.

### ADR-027 · POS Desktop Packaging & Supported OS Matrix
**Statusi:** Open — POS-i i veçantë dhe i instalueshëm, Angular UI, .NET POS Local Host dhe .NET Device Agent janë të vendosura; **Electron vs wrapper tjetër** dhe **Windows/macOS/Linux** nuk zgjidhen në ADR-024.
**Scope:** installer/code-signing, update+rollback, offline boot/recovery, lifecycle + IPC boundary, sandbox/least privilege, RAM/footprint dhe përputhshmëria me printerë/skanerë/peshore/driverë. Evidence lidhet me ADR-017, HW-001 dhe PoC-4b.
**Pronari:** Pronari + Tech. **Afati:** para implementimit të shell-it production të POS-it.

---

## Të pranuara më 28.07.2026 (ADR-028 / ADR-029 — detajet e vendimit; ACCEPTED / EFFECTIVE me merge-in e PR #26 `305aefd`)

### ADR-028 · Identity Authority & Store
**Statusi:** **ACCEPTED / EFFECTIVE (28.07.2026 — PR #26 merged, squash `305aefd`, pas Fable→Codex→Human Gate). Implementimi = Increment B (ACTIVE — B.1 Identity Store Foundation = COMPLETED/MERGED, PR #28 squash `d6ddff2`, 02.08.2026; hapi aktual = Pre-B2 Increment 1 — ENG-007 untyped data-access boundary, IMPLEMENTED_POLICY / PENDING_INDEPENDENT_REVIEW; B.2 authority/flows dhe B.3 projection = NOT STARTED / BLOCKED derisa ENG-006 + ENG-007 të mbyllen me Human Gate).** (Historia: Open deri më 27.07; PROPOSED 27–28.07; kërkesat e vendosura — OIDC/OAuth2 contract, një authority/login/SSO, MFA, RBAC, tenant authorization, audited break-glass — mbeten të pandryshuara.)
**Vendimi i pranuar (i drejtuar nga Pronari):** **ASP.NET Core Identity** = account/credential/recovery/MFA store; **OpenIddict** = self-hosted OIDC/OAuth2 authority; external federation = integration option (asnjë provider i jashtëm i zgjedhur në heshtje); **GUID/UUID keys, zero int IDs**; schema `identity` e ndarë fizikisht/logjikisht nga `platform`; **Identity roles NUK përdoren për tenant authorization** — `platform.role`/`permission`/`role_permission`/`business_membership` mbeten autoriteti tenant-scoped; `platform.app_user` = projection/provider-neutral mapping boundary; tabela eksplicite `identity.user_account` / `user_login` / `user_claim` / `user_token` / `oidc_application` / `oidc_authorization` / `oidc_scope` / `oidc_token` (zero `AspNetUsers`/`User`/`Rolers` si emra finalë të pakontrolluar; emrat finalë verifikohen në Increment B para migrimit 0003).
**Siguria e ngrirë:** Authorization Code + PKCE (jo implicit, jo password grant) · **token validation mechanism-neutral** (issuer, audience/resource kur kërkohet, token type/usage, autenticitet me signature/decryption/introspection, vetëm algoritme të lejuara, lifetime/not-before/expiry, clock-skew i kufizuar e i testuar, unknown/expired/revoked → fail-closed, key rollover me overlap tests — **pa mandat JWT/JWKS universal**; specifikimi §5.1 i dokumentit subordinate) · MFA + recovery codes · refresh-token rotation/reuse detection · signing/encryption keys jashtë repo/log/renderers · **ÇDO dev/ephemeral credential jashtë Development = startup failure (edhe krahas persistent); persistent signing OSE encryption set mungues jashtë Development = startup failure** · session/device/token revocation · lockout/rate limiting · email/account recovery · key rotation · HA/backup/restore · audit i ngjarjeve identity · incident response · break-glass = gate i veçantë i audituar. **Verification ≠ recovery** = gate OPEN / PENDING SPECIFICATION IN INCREMENT B (subordinate §5.3). **External IdP:** rihapje vetëm me evidence (federation/SAML, kërkesë normative, HA/ops gap, cost/risk, migration+exit plan) + ADR + Human Gate — reopen ≠ approval (subordinate §10).
**Specifikimi subordinate:** `docs/canonical/06-architecture/Identity_Authority_Store_Decision.md` (boundary/schema map, flows, migrimi 0003+, rollback, acceptance, follow-up-i i DETYRUESHËM i architecture test-it `Zero_identity_provider_konkret...` — ZËVENDËSIM i kontrolluar në Increment B, jo fshirje; në PR-në docs-only testi nuk preket).
**Pronari:** Pronari + Tech. **Afati:** implementimi = Increment B (pas Increment A), vetëm pas Human Gate + merge të kësaj PR-je docs-only.

### ADR-029 · Application Architecture Convention (module-first Clean/Onion + mediator + thin controllers)
**Statusi:** **ACCEPTED / EFFECTIVE (28.07.2026 — PR #26 merged, squash `305aefd`, pas Fable→Codex→Human Gate). Increment A = COMPLETED/MERGED (PR #27 `abc2b291`, 28.07.2026); B.1 = COMPLETED/MERGED (PR #28 `d6ddff2`, 02.08.2026); hapi aktual = Pre-B2 Increment 1 — ENG-007 (IMPLEMENTED_POLICY / PENDING_INDEPENDENT_REVIEW).**
**Vendimi:** (1) modular monolith — jo microservices në këtë fazë; (2) Clean/Onion **module-first** — jo layer-a gjigantë globalë; struktura `Modules/<Module>/{Domain,Application/Features/<UseCase>,Infrastructure,Presentation/Controllers}` + `Idata.Platform.Persistence` (migration owner i vetëm) + `Idata.Platform.Api` (composition root); (3) vertical feature slices brenda çdo moduli; (4) module-first kudo — një modul NUK është automatikisht bounded context (BC kërkon evidencë domain-i + context map); tactical DDD vetëm ku ka invariant real; zero ceremoni për CRUD trivial; (5) **CQRS selektiv** (command për mutacion/invariante; query për read model; zero ceremoni për CRUD trivial); (6) mediator = **martinothamar/Mediator (MIT)** me `IMediator`, `Mediator.Abstractions` në deklarime, `Mediator.SourceGenerator` vetëm në host, lifetime **Scoped**, version i pin-uar vetëm në Increment A me provenance; (7) **thin Controllers** + **BaseApiController minimal** (ControllerBase + IMediator + ProblemDetails; zero service locator/authorization/persistence/transaction); (8) pipeline behaviors VETËM validation/telemetry/correlation/exception-mapping — kurrë tenant authorization DB-backed, authority-lock, transaction boundary, idempotency, audit atomicity apo fiscal invariants; (9) **Scalar** = API reference UI mbi OpenAPI **VETËM nën `IHostEnvironment.IsDevelopment()`; Staging/Production/environment i panjohur = ABSENT fail-closed** (UI internal jo-development në të ardhmen = ADR/gate i ri me authn/authz/network controls); OpenAPI mbetet kontrata autoritative + burimi i klientit TS; zero Swagger paralel pa ADR; (10) zero `Common`/`Utils`; `BuildingBlocks` vetëm me ≥2 konsumatorë realë; (11) GUID/UUID standard, zero int IDs; (12) migrimet 0001/0002 të pandryshueshme — schema changes = 0003+ forward-only.
**Arsyetimi:** F1.1 provoi module-first Clean/Onion me handler të thirrur direkt; para Increment-eve të ardhshme duhet konventë e vetme e ekzekutueshme (transport→mediator→handler) që shmang portim të dyfishtë dhe drift stilistik, pa e fshehur autoritetin DB-backed në middleware.
**Alternativat e refuzuara:** LuckyPennySoftware/MediatR (licencim/drejtim i papranueshëm) · microservices tani (kosto operacionale pa evidence) · CQRS universal ceremonial · fat controllers / minimal-API pa konventë të vetme · Swagger UI paralel.
**Risqet:** mediator shton një indirection (kufizohet me allowlist §8 të Convention-it) · migrimi C i AccessControl mund të prekë teste (politika: baseline i plotë GREEN, ndryshime vetëm mekanike, çdo devijim semantik = flamur) · Scoped lifetime kërkon kujdes në background workers (vendoset per-host në Increment A).
**Rendi i implementimit:** **A** schema-free pilot (PlatformStatus) → **B** identity foundation (ADR-028) → **C** AccessControl migration → **D** module të reja — secili me PR/CI/review/Human Gate; detajet = Convention §15.
**Specifikimi subordinate:** `docs/canonical/06-architecture/Application_Architecture_Convention.md` — specifikimi normativ i detajuar; SSOT/Roadmap/INDEX janë vetëm consumers/pointers.
**Pronari:** Pronari + Tech + PM. **Afati:** effective vetëm pas Human Gate + merge; implementimi nis me Increment A me goal/prompt të ri.
---
## Përmbledhje statusi
- **Accepted:** 19 (ADR-002…009, 013, 015, 018…024, 028, 029).
- **Superseded:** 2 (ADR-001, ADR-010) — historia ruhet; target-i aktiv = ADR-024.
- **Open:** 8 (ADR-011, ADR-012, ADR-014, ADR-016, ADR-017, ADR-025, ADR-026, ADR-027) — secili me pronar/gate; ADR-025/026/027 NUK mbyllen nga pranimi i ADR-028/029.
- **Proposed:** 0.
- **Total unikë:** 29 (ADR-001…ADR-029; i rillogaritur mekanikisht 28.07.2026).
- ADR-024 mbyll runtime-in bazë dhe kufijtë e deployables, jo desktop wrapper/OS, identity provider/store ose gates domain/ops; implementimi fillon vetëm pas merge-it të aprovuar të PR-së së kanonizimit.
