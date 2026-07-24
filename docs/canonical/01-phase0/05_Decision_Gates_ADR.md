# Faza 0 — Decision Gates / ADR Register (iData+)

**Versioni:** 1.4 · **Data:** 24 Korrik 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.28 §2, §11
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
| ADR-024 | **Platform Runtime & Architecture Stack Freeze** — .NET/Angular/POS i instalueshëm/RN/PostgreSQL, modular monolith, migrim inkremental | 23.07 (merge PR #23 `e0d4cdc`) | **MERGED/ACCEPTED**; **Platform Foundation Bootstrap pre-F1 = IMPLEMENTED/PENDING_INDEPENDENT_REVIEW (PR #24)**; pjesa PËRTEJ Bootstrap-it/F1 = **NOT YET IMPLEMENTED**; wrapper/OS dhe identity provider/store = OPEN |

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
| Identity | **OIDC/OAuth2 contract + provider-neutral ASP.NET Core authorization boundary** | Një authority/login/SSO, MFA, company selector, RBAC per modul, tenant authorization dhe break-glass i audituar; OpenIddict vs external IdP + store = ADR-028 OPEN |
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

**Gates që mbeten Open:** ADR-011 (PowerSync vs custom), ADR-012 (kufiri final DB-function vs C# për GL), ADR-014 (pooling), ADR-016 (EAS vs bare), ADR-017/ATK-004 (printer/hardware), ADR-025 (encryption/key custody), ADR-026 (hosting/object storage/secrets provider), ADR-027 (desktop wrapper + OS matrix), ADR-028 (identity provider/store), KDS shell/hardware, P1363 vs DER dhe sjelljet server-dependent të ATK-së. Këto nuk e rihapin runtime-in bazë ose kufijtë e deployables; vendosen në gate-in e tyre.

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

### ADR-028 · Identity Authority & Store
**Statusi:** Open — OIDC/OAuth2 contract, një authority/login/SSO, MFA, RBAC, tenant authorization dhe audited break-glass janë kërkesa të vendosura; provider-i/store nuk hamendësohen.
**Scope:** OpenIddict vs external IdP, ASP.NET Core Identity vs model tjetër store, PKCE/token validation, signing-key rotation, refresh-token reuse detection, recovery, session/device revocation, federation, HA/backup dhe incident response.
**Pronari:** Pronari + Tech. **Afati:** para F1 Identity production implementation ose provider binding.

---
## Përmbledhje statusi
- **Accepted:** 17 (ADR-002…009, 013, 015, 018…024).
- **Superseded:** 2 (ADR-001, ADR-010) — historia ruhet; target-i aktiv = ADR-024.
- **Open:** 9 (ADR-011, ADR-012, ADR-014, ADR-016, ADR-017, ADR-025, ADR-026, ADR-027, ADR-028) — secili me pronar/gate.
- ADR-024 mbyll runtime-in bazë dhe kufijtë e deployables, jo desktop wrapper/OS, identity provider/store ose gates domain/ops; implementimi fillon vetëm pas merge-it të aprovuar të PR-së së kanonizimit.
