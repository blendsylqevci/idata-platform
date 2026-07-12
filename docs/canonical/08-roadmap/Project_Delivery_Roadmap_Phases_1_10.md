# iData+ — Project Delivery Roadmap, Fazat 1–10

**Status:** canonical / approved (kopja kanonike e roadmap-it — miratuar nga Pronari; merged PR #9, përditësime me PR review)  
**Data:** 2026-07-07 · **Përditësuar:** 2026-07-10  
**Burime kryesore:** SSOT §3/§8/§12, Feature Status Register, Outstanding Confirmations Tracker, ADR Register, 5 PoC Specs, PRO DATA Evidence Closure, KABI Operational Evidence Update, Accountant Confirmation Pack, Increment 1 PR #4/#6, security PR #8  
**Rregull i hekurt:** ky roadmap **nuk krijon numërim të ri fazash**. Ndjek 1:1 SSOT §3. Nëse ndryshon numërimi, duhet vendim formal në SSOT + Decisions Log.

---

## 0. Gjendja nga ku nisim

Increment 1 është mbyllur teknikisht:

- PR #4: Hapi A — monorepo foundation + shared-types + accounting library-first.
- PR #6: Hapi B — `packages/database`, `apps/api`, `apps/web`, ping real `web → api → db`.
- PR #8: security dependency bump — NestJS 11, Vitest 3, `multer` patch përmes `pnpm.overrides`; Dependabot alerts ranë nga 21 në 6.
- Faza 0 ka bazë të fortë dokumentimi, data model, evidence nga PRO DATA, SEF test phase active, formula USAID e validuar dhe canonical docs të pastruara.

Ky dokument është ura mes “kemi bazën” dhe “dimë çka ndërtojmë, në çfarë rendi, me cilat porta, dhe çka ndodh nëse diçka dështon”.

---

## 1. Parimet operative

1. **SSOT fiton.** Ky dokument nuk e zëvendëson SSOT-in; e detajon §3.
2. **Done = test/evidencë, jo opinion.** Çdo fazë ka kriter pranimi të matshëm.
3. **Gates njerëzore nuk zëvendësohen me kod.** Kontabilisti, ATK, KABI, PLANET Finance dhe hardware kanë afate të qarta.
4. **KABI live i pari.** PLANET ndërtohet paralel vetëm aty ku ndan bërthamë të përbashkët.
5. **Cutover 1 Janar 2027** është data planifikuese; Dhjetori ruhet për test paralel dhe ngrirje.
6. **Pa të dhëna sensitive.** Roadmap-i nuk përmban çmime, kontrata, emra privatë, ID SEF, shuma reale, raw Excel/PRO DATA.
7. **PRO DATA = golden baseline deri në lansim.** Nuk bëjmë më extract të ri, por e përdorim për parity/UAT/cutover.
8. **Nëse gate kalon afatin pa përgjigje, vendimi nuk shpiket.** Aktivizohet Plan-B ose shtyrje formale.
9. **Dependabot nuk auto-merge-ohet.** Çdo security bump kalon CI + gitleaks + review; `pnpm.overrides` dhe Security tab kontrollohen javore.
10. **Reliability Gate.** Çdo modul kritik ka failure matrix + teste për 8 skenarët standardë të dështimit para se të quhet gati (`../06-architecture/Reliability_Gate.md`); waiver vetëm me vendim të shkruar të pronarit me risk + afat.

---

## 2. Critical Confirmation Calendar

Këto janë datat më të vona të pranueshme që ruajnë shansin real për cutover më 2027-01-01. Datat janë planifikuese dhe duhet të rishikohen çdo javë.

| ID / Gate | Status aktual | Çka duhet konfirmuar | Owner | Bllokon | Data më e vonë | Nëse vonohet |
|---|---|---|---:|---|---:|---|
| SSOT-VERSION | Closed (PR #11, 07.07) | Header-i i SSOT të përputhet me Decisions Log aktual para PR roadmap | Codex/PM | PR roadmap | 2026-07-08 | PR roadmap nuk hapet pa bump/version consistency |
| ENG-001 | Open | Boundary NestJS service/repository; controller-i nuk duhet të mbetet varësi direkte për Accounting Core | Tech + Codex | Faza 2 | 2026-07-24 | accounting endpoints mbeten thin/prototype; nuk ngrihet API contract final |
| ENG-002 | Open | Custom migration runner vs tool standard për migrime production-grade | Tech + Codex | Faza 2 / Faza 9 | 2026-07-24 | custom runner lejohet vetëm për skeleton/PoC; jo për prod migration freeze |
| ENG-003 | Closed (PR #10, 07.07) | Composite FK cross-tenant, p.sh. `role_permission (business_id, role_id) REFERENCES role (business_id, id)` | Tech | PoC-1 / Faza 1 | 2026-07-18 | PoC-1 nuk kalon PASS; tenant isolation mbetet e paprovuar në DB |
| ENG-004 | Open | `audit_log` group-level events vs `business_id NOT NULL` skeleton | Blend + Tech | Faza 1 audit model | 2026-07-20 | vazhdohet vetëm me business-scoped audit; group-level events shtyhen si model i veçantë |
| SEC-DEP | Active / recurring | Dependabot governance: no auto-merge, CI/gitleaks/review, weekly Security tab + `pnpm.overrides` review | Blend + Tech | Security process | Weekly | security alerts nuk mbyllen si “done” pa review; override i vjetër hiqet kur upstream e mbulon |
| ADR-010 | Open | Hosted vs self-host EU për cloud DB/services | Blend + Tech | Faza 1/PoC infra | 2026-07-15 | përdor Postgres lokal/neutral deri në vendim; s'kyçet cloud vendor në kod |
| ADR-011 | Open | Sync engine: PowerSync vs custom sync | Tech + PM | PoC-3, offline, mobile/hub | 2026-07-31 | PoC-3 bëhet me adapter abstraction; nuk kyçet vendor deri në vendim |
| ADR-014 | Open | Connection pooling strategy | Tech | Faza 1/2 load path | 2026-07-20 | vazhdo me dev pool vetëm për PoC; mos bëj load commitments |
| ADR-012 | Open | GL posting: DB functions vs NestJS transaction/hybrid | Codex + Tech | PoC-2, Accounting Core | 2026-07-24 | PoC-2 teston të dy variantet minimalisht; pa Accounting Core freeze |
| ADR-013 | Open | Local hub DB: Postgres vs SQLite/embedded | Tech + ops | PoC-4, PLANET/KABI local hub | 2026-08-10 | hub PoC ndërtohet me interface; hardware/DB final nuk blihet |
| ADR-016 | Open | RN/Expo build strategy | Tech | Mobile/offline apps | 2026-08-10 | mobile PoC ruhet minimal; build pipeline final shtyhet |
| ADR-017 | Open | Printera fiskalë: hardware vs SEF software | iCode + ATK/vendor | Hardware procurement, SEF | 2026-08-15 | nuk blihet printer fiskal pa vendim; përdoret printer termik test |
| ACC-003 | Sent/Open | Tax codes/accounts/rounding final | Kontabilisti | Faza 2 VAT/books | 2026-07-24 | përdor config provisional; s'lejohet go-live/sign-off |
| ACC-006 | Partially answered | TVSH flat-rate 8% baza ligjore u konfirmua; mbetet posting/libër blerje/autofaturë sign-off | Kontabilisti | Faza 2/4/5 KABI | 2026-07-24 | KABI settlement mbetet draft pa GL/TVSH final |
| ACC-007 | Sent/Open | COGS valuation/yield/batch costing | Kontabilisti + KABI | Faza 3 | 2026-08-05 | Phase 3 nis me model configurable; COGS production freeze shtyhet |
| ACC-008 | Sent/Open | Kthime/write-off/note krediti/periudha | Kontabilisti | Faza 2/3/5/8 | 2026-08-05 | lejohet vetëm reversal conservative + approval, jo automation final |
| Payroll gate | Open | Payroll engine i plotë vs master-data + kontribute | Blend + kontabilisti | Faza 1 | 2026-07-20 | në Faza 1 ndërtohet vetëm master-data + kontribute minimale |
| ATK-002 | **REOPENED (12.07)** — local regression evidence only (66 PASS/1 LIVE SKIP); **Tier 1 overall PARTIAL/REOPENED; compliance FAIL; Tier 2 NO-GO** (Gate v2 + ATK-012) | SEF PoC teknik me GitHub fiskalizimi | iCode dev | Faza 5 | 2026-08-15 | nuk fillon aplikimi formal; KABI live rrezikohet |
| ATK-012 | Open — **P0/PRE-TIER2 Gate Schedule** | Step B code hardening: T2-01..T2-10 (Gate v2 §4) | iCode dev | Step B | **DATE TBD — Human Gate** (milestone blocker: PARA çdo preflight/Tier 2) | pa të s'ka thirrje reale; Tier 2 mbetet NO-GO |
| ATK-013 | Open — P0 Gate Schedule | Paketa ligjore/aplikueshmëria (final-consumer/B2B/mixed/kohortat/Vendimi19/cash) | ATK/jurist/Pronari | para F5 freeze | **DATE TBD — Human Gate** (milestone: para KABI live) | klasifikimi i subjekteve/pagesave i pavendosur → risk ligjor live |
| ACC-012 | Open — P0 Gate Schedule | Fatura/libra/retention/e-invoice/cash+TVSH umbrella (Ledger V01–V08, L02–L04/L08) | Kontabilisti/jurist | F2 | **DATE TBD — Human Gate** (milestone: para F2 freeze) | faturimi/librat jo-konform → bllokon Accounting Core |
| PRIV-001 | Open — P0 Gate Schedule | Privacy governance 06/L-082 (P01–P10: ROPA/DPIA/DPO/breach/transfers) | Jurist/AIP + iCode | para certifikimit/go-live | **DATE TBD — Human Gate** (milestone: para go-live; Tier2-blocker vetëm nëse prek privatësinë e Tier 2) | go-live pa bazë ligjore përpunimi → risk AIP |
| ATK-003 | Open | Aplikim/certifikim final SEF | iCode + ATK | KABI live / PLANET live | 2026-10-15 start | shtyrje e go-live fiskal; as KABI as PLANET nuk dalin live me faturim jo-konform |
| ATK-004 | Open | Printer hardware vs SEF software + printer termik | iCode + ATK/vendor | Hardware procurement | 2026-08-15 | ndalo blerjen e printerëve/pajisjeve fiskale |
| HW-001 | Open | Inventar pajisjesh ekzistuese | Klienti | Faza 4/5/7/8 | 2026-08-15 | buxhet/hardware test shtyhet; PoC me pajisje test |
| KABI lab units | Partially answered | Njësitë/pragjet për proteinë/SNF/somatike/ujë | Teknologu | Faza 4 | 2026-08-10 | ruaj si config pending; lab-gate pa auto-class final |
| FIN-001 | Open | PLANET finance ownership: kush e kontrollon arken/zyren/kontabilistin | PLANET Finance | Faza 7/8 | 2026-09-01 | Finance Ops mbetet draft; POS live nuk lejohet |
| FIN-002 | Open | Një ditë reale finance: saldo, POS terminale, cash, shpenzime, handover | PLANET Finance | Faza 7/8 | 2026-09-01 | barazimi ditor dhe payment reconciliation mbeten draft |
| FIN-003 | Open | Floor-plan, KDS, shank, printer routing | PLANET Ops | Faza 7/8 | 2026-09-01 | KDS/POS UI mbetet generic; nuk porositet hardware final |
| OPS-001 | Open | RESORT: lokacion/business_unit/subjekt? | Group Owner + kontabilist | Data model PLANET | 2026-08-01 | ndalo modelimin `business_unit` në DB/app |
| Cutover OB | Future gate | Saldot hapëse: stok/AR/AP/arka/banka/gajbe | Klienti + kontabilisti | Faza 9/10 | 2026-12-15 | cutover shtyhet ose kufizohet me module pa saldo |
| Training freeze | Future gate | Përdoruesit + role + pajisje për trajnim | Group Owner | UAT/parallel run | 2026-11-15 | dhjetori nuk ngrin; go-live rrezikohet |

---

## 3. Critical Path i fazave

Ky plan ruan dhjetorin për parallel run, UAT, trajnim, freeze dhe cutover rehearsal. Datat janë planifikuese; nëse një fazë rrëshqet, Go/No-Go duhet të rivlerësohet, jo të fshihet rreziku.

| Faza | Owner kryesor | Kohëzgjatje e vlerësuar | Dritarja e synuar | Data më e vonë e nisjes | Milestone / alarm |
|---|---|---:|---|---:|---|
| F1 Foundation | Tech Lead + PM | 3–4 javë | 2026-07-08 → 2026-08-05 | 2026-07-15 | PoC-1 duhet të nisë brenda korrikut |
| F2 Accounting Core | Accounting Lead + Tech | 4–5 javë | 2026-08-01 → 2026-09-05 | 2026-08-05 | Pa F2, as KABI as PLANET nuk kanë GL/TVSH |
| F3 Stok + Recetura + COGS | Inventory/Accounting Lead | 3–4 javë | 2026-08-20 → 2026-09-20 | 2026-08-25 | COGS/LOT/FEFO para KABI MVP |
| F4 KABI Operacional MVP | KABI Product Lead + Tech | 4 javë | 2026-09-05 → 2026-10-05 | 2026-09-10 | Farmer intake/lab/tank/settlement duhet të hyjë në UAT |
| F5 Offline + SEF → ★ KABI LIVE | iCode + KABI Ops + ATK | 4–6 javë | 2026-10-01 → 2026-11-15 | 2026-10-10 | ★ KABI LIVE synohet në nëntor; pa SEF nuk ka live |
| F6 KABI Advanced | KABI Ops + PM | 2–4 javë, pas stabilizimit | Pas KABI LIVE | Pas 2 javëve stabile | Shtyhet nëse MVP nuk është stabil |
| F7 PLANET Foundation | PLANET Product Lead + Tech | 3–4 javë | 2026-09-15 → 2026-10-20 | 2026-09-20 | Floor-plan/finance gates duhet të mbyllen |
| F8 PLANET POS/KDS → ★ PLANET LIVE | PLANET Ops + Finance + Tech | 4–6 javë | 2026-10-15 → 2026-11-30 | 2026-10-25 | ★ PLANET LIVE para dhjetorit ose pilot i kufizuar |
| F9 Hardening + Cutover + Pilot | PM + Accounting + Ops | 4 javë | 2026-12-01 → 2026-12-31 | 2026-12-01 | Dhjetori është freeze/parallel run, jo feature sprint |
| F10 Lansim | Group Owner + PM | 1 javë + hypercare | 2027-01-01 → 2027-01-07 | 2027-01-01 | Cutover vetëm nëse Go/No-Go PASS |

---

## 4. PoC Track pas Increment 1

Këto PoC janë ura mes walking skeleton dhe moduleve reale. Secili PoC mbyll një rrezik arkitekturor para se të hyjë në fazë me kosto të lartë.

| PoC | Owner | Rendi | Çka provon | Gates/ADR që mbyll | Acceptance criteria |
|---|---|---:|---|---|---|
| PoC-1 Tenant Isolation | Tech + Codex review | 1 | `business_id`, RBAC, audit dhe ndarje tenant në DB/API | ENG-003, F1 tenant isolation | test automatik që user i një biznesi nuk lexon/shkruan dot biznes tjetër; composite FK rule e provuar |
| PoC-2 Accounting Posting | Accounting Lead + Codex | 2 | dokument → posting → GL → tax book me invariant debit=kredit | ADR-012, ACC-003, ACC-006 | fixture 18/8/mixed/farmer-draft kalon; posting idempotent; period lock test |
| PoC-3 Offline Sync | Mobile/Sync Lead | 3 | outbox, retry, idempotency, conflict handling | ADR-011, ADR-016 | 5 dokumente offline sync-ohen pa humbje/dublim; konflikt kontrollohet me audit |
| PoC-4 Local Hub | Infra/Hub Lead | 4 | LAN hub, printer fallback, local queue | ADR-013, HW-001 | web/mobile → hub → printer/test queue punon pa internet; recovery pas restart |
| PoC-5 SEF TEST | iCode SEF Lead | 5 | fiskalizim test me protokollin zyrtar | ATK-002, ADR-017 | kupon test nënshkruhet/dërgohet në server test; QR/transaction id ruhen; retry i dështimit kontrollohet |

**Statusi real (jo vetëm plan) — përditësuar 10.07.2026:**

- **PoC-1 Tenant Isolation — PASS sipas kritereve të PoC-ut** (koncept i provuar; PR #10)
- **PoC-2 Accounting Posting — PASS sipas kritereve të PoC-ut** (koncept i provuar; PR #12)
- **PoC-3 Offline Sync — PASS sipas kritereve të PoC-ut** (koncept i provuar; PR #14)
- **PoC-4 Local Hub — PARTIAL** (4a semantika ✅ PR #15; **4b hardware pending**)
- **PoC-5 SEF — post-audit 12.07.2026:** PR #16 = **evidence historike mock** (regression 66 PASS/1 LIVE SKIP); **Tier 1 overall PARTIAL/REOPENED · compliance FAIL** (4 CONTRADICTS) · **Tier 2 NOT IMPLEMENTED/NO-GO**. **"PoC-5 PASS" = parakusht i ardhshëm, EKSPLICITISHT NOT MET sot** (Gate v2.0 + Ledger; Step B/C para çdo hapi live)
- **Production-readiness ≠ PoC PASS:** arrihet përmes **gates F1–F10** (jo automatikisht me F1+) — follow-ups të regjistruara (`ATK_Compliance_Gate.md` §4, `Reliability_Gate.md` §5.2) + gates e certifikimit (Gate §2e/§3).

**Rregull:** PoC-të nuk janë demo UI. Janë prova teknike me test/evidencë. Nëse një PoC dështon, faza që varet prej tij nuk kalon në implementim të plotë.

---

## 5. Roadmap sipas SSOT §3

### Faza 1 — Foundation

**Qëllimi:** të krijohet bazamenti i përdorshëm dhe i sigurt: auth, multi-company, RBAC, audit, monitorim, backup discipline dhe vendimi i payroll-it minimal.

**Owner / kohëzgjatje / data më e vonë:** Tech Lead + PM · 3–4 javë · nisje jo më vonë se 2026-07-15  
**Feature buckets:** Shared MVP, Accounting MVP foundations, Requires-ADR.

**Scope brenda**

- Auth bazë dhe strukturë përdoruesish, pa workflow kompleks biznesi.
- `business`, `app_user`, membership, roles/permissions, company switcher.
- 2FA për role kritike.
- RBAC per-feature me verbe: Lexim/Krijim/Modifikim/Anulim-Korrigjim/Shtypje/Aprovim.
- Audit log append-only në DB dhe në aplikacion.
- Break-glass model për iCode Support.
- Monitoring minimal: health checks, logs, error boundaries.
- Backup/PITR/restore drill minimal për mjediset dev/staging.
- Payroll gate: vendim për full payroll engine vs master-data + kontribute.

**Jashtë scope**

- Accounting postings reale.
- KABI/PLANET module.
- SEF integration.
- RLS production policy, derisa ADR-et dhe PoC-1 të vendosin kufirin final.

**Prerequisites**

- Increment 1 i mbyllur.
- ADR-010, ADR-014 me vendim ose workaround të dokumentuar.
- ADR-011, ADR-013, ADR-016 si gates të dizajnit offline/mobile/hub; nuk bllokojnë skeleton-in, por bllokojnë ngrirjen e PoC-3/PoC-4/mobile build.
- ENG-003: composite FK cross-tenant për tenant-scoped child tables.
- ENG-004: vendim për `audit_log` group-level events vs business-scoped-only audit.
- Payroll scope decision.

**Deliverables**

- Login/role/membership skeleton.
- Business isolation në API.
- Audit log append-only.
- 2FA për admin/kontabilist ose vendim i qartë për rollout.
- Restore drill dokumentuar.

**Acceptance criteria**

- User i KABI-t nuk lexon dot rreshta të PLANET në API.
- Role pa permission nuk mund të kryejë veprim.
- Audit log shkruhet për login, role change, approval, break-glass.
- Audit log refuzon update/delete.
- Composite FK / business_id invariant provon që `role_permission` nuk mund të lidhë role të biznesit tjetër.
- Group-level audit events janë ose të modeluara qartë, ose të bllokuara me vendim të dokumentuar.
- Restore drill krijon DB test nga backup dhe verifikohet me checksum/test.
- **Reliability Gate PASS** për modulet e fazës.

**Evidence**

- Test automatik për tenant isolation.
- Test RBAC deny/allow.
- Log i restore drill.
- Screenshot minimal i role/company switcher.

**Risk / Plan-B**

- Nëse auth vendor/stack s'vendoset, përdoret auth skeleton lokal për PoC dhe vendimi shtyhet maksimum deri para PoC-1.
- Nëse payroll scope s'mbyllet, Faza 1 përfshin vetëm employee master-data + kontribute minimale, jo payroll engine të plotë.

---

### Faza 2 — Accounting Core

**Qëllimi:** motori financiar i përbashkët për KABI dhe PLANET: GL, posting, AR/AP, VAT books, period close, cash/bank dhe opening balances.

**Owner / kohëzgjatje / data më e vonë:** Accounting Lead + Tech Lead · 4–5 javë · nisje jo më vonë se 2026-08-05  
**Feature buckets:** Accounting MVP, Shared MVP, Requires-accountant, Requires-ATK.

**Scope brenda**

- Chart of Accounts i konsoliduar me zgjerime per business.
- Journal entry/journal line; debit=kredit invariant.
- Posting rules engine dhe posting run.
- AR/AP, open items, partner cards.
- Cash/bank/payment methods; bank reconciliation bazë.
- Libri i Shitjes, Libri i Blerjes, VAT declaration snapshot.
- Period open/close/lock.
- Opening balances import.
- Financial statements bazë.
- Fixed assets/amortization vetëm nëse nuk shtyn MVP; përndryshe si sub-scope i veçantë Faza 2.

**Jashtë scope**

- Full KABI production costing.
- PLANET KDS/POS.
- SEF production certification.
- AI invoice scanner.

**Prerequisites / Gates**

- ACC-003: rounding, tax accounts, tax codes final.
- ACC-006: farmer flat-rate posting sign-off për KABI.
- ACC-007: COGS method për lidhje me Phase 3.
- ACC-008: correction/write-off/credit note policy.
- ACC-010/011: closed-month validation dhe ATK book format janë bazë evidence.
- ADR-012: GL posting architecture.
- ENG-001: service/repository boundary për NestJS para se Accounting Core API të ngrijë.
- ENG-002: vendim migration runner custom vs tool standard para migrimeve production-grade.
- **Dizajn concurrency-safe për `partner_card_entry.running_balance`** (lock ose llogaritje në lexim, jo read-modify-write i pambrojtur) — gjetje e PoC-2.

**Deliverables**

- Accounting package + API endpoints minimale.
- Posting simulator i lidhur me DB.
- VAT book export i normalizuar.
- **ATK VAT Excel Export Compliance Pack** — eksporti 1:1 me template-t zyrtare Excel të ATK-së (të marra nga kontabilisti), i validuar me sign-off (lidhet me ACC-011).
- Period close workflow.
- Opening balance import template.

**Acceptance criteria**

- Çdo posting debit=kredit.
- Sales invoice → GL + AR + Libri i Shitjes.
- Purchase invoice → GL + AP + Libri i Blerjes.
- Farmer purchase document → correct flat-rate bucket vetëm pas ACC-006.
- Period close bllokon backdated posting pa approval.
- VAT summary përputhet me muajin e mbyllur të validuar, brenda rounding-rule të aprovuar.
- **Reliability Gate PASS** për modulet e fazës.

**Evidence**

- Fixtures accounting 18%/8%/mixed VAT/farmer flat-rate.
- Export ATK books.
- Test period close.
- Accountant sign-off checklist.

**Risk / Plan-B**

- Nëse ACC-006 vonohet, farmer posting mbetet `pending_accountant_signoff`; KABI intake mund të ecë pa final AP posting.
- Nëse COGS nuk konfirmohet, stock valuation përdor weighted average draft dhe nuk mbyllet P&L final.

---

### Faza 3 — Stok + Recetura + COGS

**Qëllimi:** të ndërtohet bërthama e inventarit, recetave, LOT/FEFO dhe costing që i shërben KABI-t dhe më vonë PLANET-it.

**Owner / kohëzgjatje / data më e vonë:** Inventory/Accounting Lead · 3–4 javë · nisje jo më vonë se 2026-08-25  
**Feature buckets:** Shared MVP, KABI MVP, KABI Advanced gates, Requires-accountant.

**Scope brenda**

- Article/product master me versionim dhe status aktiv/pasiv.
- Multi-depo: fabrika, depo, automjet; shank/kuzhinë futen në Faza 7/8 për PLANET.
- Stock movements: hyrje, dalje, transfer, korrigjim, write-off.
- LOT/batch, FEFO, expiry date.
- Recipe/BOM versioning, effective dates, approval.
- Cost layers, weighted average, batch costing hooks.
- Catch-weight support.
- Packaging/gajbe/karton si konfigurim.

**Jashtë scope**

- Full KDS/POS PLANET.
- Full production yield variance UI.
- AI scanner.

**Prerequisites / Gates**

- ACC-007 COGS/valuation.
- ACC-008 write-off/kthime policy.
- KABI-003 product fields answered; lista reale mbetet private-local.
- KABI-004 recetura/normativ answered; recetat reale private-local.

**Deliverables**

- Inventory schema + stock ledger.
- Recipe versioning.
- LOT/expiry/FEFO tests.
- COGS posting hooks.
- Stock count and approval flow.

**Acceptance criteria**

- Çdo stock movement ka audit, business_id, source document dhe status.
- Transferi depo→automjet ruan LOT/expiry.
- FEFO propozon LOT-in më të vjetër të vlefshëm.
- Write-off kërkon arsye + approval.
- Stock movement i aprovuar krijon posting/COGS kur aplikohet.
- **Reliability Gate PASS** për modulet e fazës.

**Evidence**

- Inventory fixtures.
- LOT/FEFO tests.
- COGS sample posting.
- Stock count reconciliation report.

**Risk / Plan-B**

- Nëse COGS final vonohet, prodhimi përdor cost estimate dhe P&L final bllokohet deri te sign-off.

---

### Faza 4 — KABI Operacional MVP

**Qëllimi:** të mbulohet zinxhiri real i KABI-t nga fermeri te qumështi, laboratori, tanku, batch-i dhe stoku i gatshëm.

**Owner / kohëzgjatje / data më e vonë:** KABI Product Lead + Tech Lead · 4 javë · nisje jo më vonë se 2026-09-10  
**Feature buckets:** KABI MVP, Accounting MVP integration, Requires-factory, Requires-accountant.

**Scope brenda**

- Farmer/partner master, contracts, NIF/certifikatë status.
- Milk intake: fermer / punëtor / Punki.
- Mostër e detyrueshme për çdo pranim.
- Lab analysis: pamje/aromë, pH/aciditet, temperaturë, dendësi, yndyrë, proteinë/SNF, ujë, antibiotik.
- Antibiotik pozitiv → reject gate.
- Tank, tank movement, milk pool contribution.
- Pasteurization log minimum.
- Batch/LOT + expiry.
- Farmer settlement draft me formula USAID si bazë zyrtare pagese.
- Avanse dhe zbritje në shlyerje.

**Jashtë scope**

- Full advanced production yield.
- Recall UI i plotë.
- Route optimization.
- SEF/offline live; kjo vjen në Faza 5.

**Prerequisites / Gates**

- KABI-001 formula USAID zyrtare (closed v2.17 — formula është bazë zyrtare pagese); mbetet vetëm rounding/posting via ACC-003/ACC-006.
- KABI-002 lab/QC answered; njësitë e dyshimta konfirmohen me teknologun.
- ACC-006 farmer flat-rate posting sign-off për AP/TVSH final.
- Hardware për peshore/Lacto Scan nëse kërkohet integrim real.

**Deliverables**

- Farmer intake workflow.
- Lab/QC gate.
- Tank/pool traceability.
- Batch creation.
- Settlement run draft/approval/lock.

**Acceptance criteria**

- Pa lab approval nuk ka transfer në tank dhe nuk ka shlyerje.
- Çdo batch lidhet mbrapa me tank/pool dhe fermerë përmes contribution chain.
- Settlement nuk krijon AP final pa approved/locked state.
- Formula USAID llogarit sipas parametrave private-config, jo hardcode në UI.
- Raporti i grumbullimit printohet me sasi/cilësi/financa pa humbur traceability.
- **Reliability Gate PASS** për modulet e fazës.

**Evidence**

- Test pranim→mostër→lab→tank→batch.
- Test antibiotic reject.
- Settlement fixture me parametra sintetikë.
- Traceability report.
- Artefaktet workspace `Moduli_Pranim_Shlyerje` dhe `USAID_Formula` përdoren si evidence/prototype për UX dhe formulë; nuk futen në GitHub canonical pa sanitizim dhe review.

**Risk / Plan-B**

- Nëse lab integration me pajisje vonohet, rezultatet futen manualisht me audit.
- Nëse ACC-006 vonohet, settlement bëhet operacional por nuk postohet AP/TVSH final.

---

### Faza 5 — Offline + SEF → ★ KABI LIVE

**Qëllimi:** KABI të punojë live në terren me offline-first, van sales, SEF/fiskalizim dhe rakordim ditor.

**Owner / kohëzgjatje / data më e vonë:** iCode SEF Lead + KABI Ops + ATK/vendor · 4–6 javë · nisje jo më vonë se 2026-10-10  
**Feature buckets:** KABI MVP, SEF/ATK, Offline MVP, Requires-ATK, Requires-hardware.

**Scope brenda**

- Mobile outbox dhe sync idempotent.
- Fiscal outbox i ndarë nga business sync.
- SEF PoC → aplikim formal → certifikim.
- Van warehouse, loading/unloading, POD.
- Faturim në vend, cash/kredi, credit limit.
- Driver day close dhe variance report.
- Returns/refusal/partial delivery correction.
- FEFO në terren.
- Hardware test: PDA/printer/terminal/UPS/hub sipas vendimeve.

**Jashtë scope**

- KABI Advanced yield/recall UI.
- PLANET KDS/POS.

**Prerequisites / Gates**

- PoC-3 offline sync PASS.
- PoC-5 SEF PASS *(parakusht i ardhshëm — **NOT MET sot**: Tier 1 PARTIAL/REOPENED, compliance FAIL, Tier 2 NO-GO; Gate v2 + ATK-012)*.
- ATK-002, ATK-003, ATK-004.
- ADR-011, ADR-016, ADR-017.
- HW-001.
- Faza 2/3/4 MVP components të gatshme.

**Deliverables**

- KABI van-sales app/workflow.
- SEF certified fiscal path.
- Offline retry/48h alarm.
- Driver reconciliation.
- KABI operational go-live package.

**Acceptance criteria**

- 5 fatura offline sync-ohen pa dublim.
- Kuponi/fatura fiskalizohet sipas rregullave SEF.
- Driver loading - sales - returns - remaining stock rakordohet.
- Kthim/refuzim LOT krijon dokument korrekt dhe kartelë klienti të saktë.
- KABI mund të operojë një ditë pune me sistemin e ri dhe të rakordohet me kontabilitetin.
- **Paketa e certifikimit ATK** e plotë: manual përdoruesi, deklarata e pajtueshmërisë, përshkrimi i zgjidhjes + lista e teknologjive (`ATK_Compliance_Gate.md`).
- **Regjistrim + certifikatë PER INSTALIM** (çdo instancë SEF/POS — UA N34.3/KT N31.4); ATK ka të drejtë verifikimi — sistemi **gati për verifikim** (go-live checklist).
- **Drain scheduler automatik** i fiscal outbox (jo thirrje manuale) + **hard gate para mbylljes së pagesës**: ONLINE = ATK accepted + `transaction_id` i persistuar + kupon i dorëzueshëm; OFFLINE (përjashtim) = payload durable në outbox + kupon "OFFLINE" + retry/deadline/alert aktive.
- **Reliability Gate PASS** për modulet e fazës.

**Evidence**

- SEF transaction IDs në test/prod sipas fazës.
- Offline sync logs.
- Driver end-of-day report.
- KABI Go/No-Go checklist.

**Risk / Plan-B**

- Nëse SEF certifikimi nuk mbyllet, KABI live shtyhet; nuk lejohet live me faturim jo-konform.
- Nëse hardware nuk konfirmohet, përdoren pajisje test dhe go-live shtyhet.

---

### Faza 6 — KABI Advanced

**Qëllimi:** të shtohen funksione të avancuara që rrisin kontrollin, analizën dhe efikasitetin pas stabilizimit të KABI MVP.

**Owner / kohëzgjatje / data më e vonë:** KABI Ops + PM · 2–4 javë pas stabilizimit · nuk nis para 2 javëve stabile pas KABI LIVE  
**Feature buckets:** KABI Advanced, Roadmap, Requires-factory.

**Scope brenda**

- Yield variance plan-vs-real.
- Full pasteurization CCP gating.
- Recall traceability UI.
- Scrap/by-products/Grade B.
- MTO/MTS.
- Raft-control GPS/foto.
- Konsignacion/return-rights advanced, nëse konfirmohet si nevojë.
- KPI dashboards prodhim/terren.

**Jashtë scope**

- Çdo gjë që rrezikon stabilitetin e KABI live pa fitim të qartë.

**Prerequisites**

- KABI LIVE stabil minimum 2–4 javë.
- Data reale e mjaftueshme për KPI.
- Feedback nga përdoruesit.

**Acceptance criteria**

- Recall nga LOT tregon fermerë/tank/batch/klientë.
- Yield variance raporton dallimin plan-real.
- Scrap posting i ndarë nga humbja normale.
- KPI për menaxhment përdoret në dashboard.

**Risk / Plan-B**

- Nëse përdoruesit ende luftojnë me MVP, advanced shtyhet. Stabiliteti fiton mbi feature creep.

---

### Faza 7 — PLANET Foundation

**Qëllimi:** të përgatitet bërthama e PLANET-it: menu, tavolina, POS bazë, stock/recipes, finance operations dhe hub.

**Owner / kohëzgjatje / data më e vonë:** PLANET Product Lead + Tech Lead · 3–4 javë · nisje jo më vonë se 2026-09-20  
**Feature buckets:** PLANET MVP, Finance Ops MVP, Shared MVP, Requires-PLANET-finance.

**Scope brenda**

- Menu/article master për PLANET.
- Tavolina/zona/floor-plan.
- POS basic open/close table.
- Stock/recipes for bar/kitchen.
- Supplier purchase and invoice capture basic.
- Finance Operations skeleton: cash shift, float assignment, waiter settlement, handover.
- Local hub PoC production-like.

**Jashtë scope**

- Full KDS live.
- Split-bill advanced.
- AI invoice scanner.

**Prerequisites / Gates**

- FIN-001 and FIN-002 meeting with PLANET finance.
- FIN-003 floor-plan/KDS/shank/printer routing.
- OPS-001 RESORT structure.
- ADR-013 local hub DB decision/workaround.
- HW-001 hardware inventory.
- PoC-4 local hub PASS.

**Deliverables**

- PLANET POS base.
- Menu/table/floor configuration.
- Finance operations flow.
- Stock recipe deduction basic.

**Acceptance criteria**

- Tavolina hapet/mbyllet dhe ruan session.
- Order basic zbrit stock sipas recetës.
- Barazim kamarier/shank ka cash/card/bank breakdown.
- Hub LAN PoC funksionon pa internet.
- **Regjistrim + certifikatë PER INSTALIM** i instancave PLANET (UA N34.3/KT N31.4); sistemi **gati për verifikim** nga ATK (go-live checklist).
- **Reliability Gate PASS** për modulet e fazës.

**Risk / Plan-B**

- Pa floor-plan real, UI mbetet generic dhe nuk kalon UAT.
- Pa finance live validation, settlement ekranet nuk ngrihen final.

---

### Faza 8 — PLANET POS/KDS → ★ PLANET LIVE

**Qëllimi:** PLANET të dalë live me POS/KDS, multi-shank, finance settlement dhe LAN/offline-safe operations.

**Owner / kohëzgjatje / data më e vonë:** PLANET Ops + PLANET Finance + Tech Lead · 4–6 javë · nisje jo më vonë se 2026-10-25  
**Feature buckets:** PLANET MVP, KDS/POS MVP, Finance Ops MVP, Requires-ATK, Requires-hardware.

**Scope brenda**

- POS full: tavolina, total hidden/lock, split/transfer/merge, discount PIN, anulim me arsye.
- KDS per station 10–12 ekrane, status e re/në punë/gati, timers, queue monitor.
- Multi-shank/bar, printer routing, backup printer.
- Business day 00:00 dhe worker settlement.
- Finance Operations daily report.
- Real-time delayed order alerts.
- PLANET SEF/fiscal behavior sipas vendimeve.

**Jashtë scope**

- Events/bankets.
- AI scanner/smart notifications premium, përveç nëse vendoset ndryshe.

**Prerequisites / Gates**

- Faza 7 accepted.
- FIN-001/002/003 closed.
- Hardware installed/tested.
- Staff training.
- SEF/fiscal certification path për PLANET **i detyrueshëm për live**.

**Acceptance criteria**

- Porosia nga pajisja e kamarierit shkon në KDS të duhur në LAN pa internet.
- Printer backup aktivizohet sipas rregullit.
- Z-report/worker settlement rakordon me cash/card/bank.
- Table lifecycle kuptohet nga kamarier/menaxher/finance.
- PLANET mund të operojë një ditë pune dhe të rakordohet pa PRO DATA.
- **Hard gate POS→kupon (UA N6.1) — semantika e saktë, test end-to-end:** para mbylljes së pagesës — ONLINE = ATK accepted + `transaction_id` i persistuar + kupon i dorëzueshëm/printueshëm; OFFLINE (vetëm përjashtim) = payload durable në fiscal outbox + kupon "OFFLINE" + retry/deadline/alert aktive.
- **Drain scheduler automatik** aktiv edhe në PLANET.
- **Reliability Gate PASS** për modulet e fazës.

**Risk / Plan-B**

- Nëse KDS/hub dështon, fallback printer + manual flow aktivizohet; live shtyhet deri në 2 ditë stabile test.

---

### Faza 9 — Hardening + Cutover + Certifikim + Pilot

**Qëllimi:** të ngrijë sistemi për go-live final: security, load, backup, opening balances, UAT, training, pilot dhe certifikime.

**Owner / kohëzgjatje / data më e vonë:** PM + Accounting Lead + Ops Leads · 4 javë · nisje jo më vonë se 2026-12-01  
**Feature buckets:** Hardening, Cutover, Security, UAT, Requires-accountant, Requires-ATK.

**Scope brenda**

- Security audit + privacy scan.
- Load test dhe offline stress test.
- Backup/PITR restore drill.
- Opening balances import rehearsal.
- UAT scripts për KABI/PLANET/accounting.
- Training plan + attendance.
- SEF final certification evidence.
- Cutover dry-run.
- Pilot me Go/No-Go.

**Jashtë scope**

- Feature të reja të mëdha.
- Ndryshime arkitekture pa vendim formal.

**Prerequisites**

- KABI live path stabil ose pilot.
- PLANET live path stabil ose pilot.
- Accounting books reconciled.
- Hardware ready.

**Acceptance criteria**

- Restore drill kalon.
- Opening balances rakordohen me dokumente të nënshkruara.
- UAT critical flows PASS.
- Training completed.
- **Paketa e certifikimit ATK e dorëzuar/aprovuar**: manual përdoruesi, deklarata e pajtueshmërisë, përshkrimi i zgjidhjes + lista e teknologjive; klienti ka njoftuar ATK-në për zgjidhjen/zhvilluesin/mirëmbajtësin (UA N35.10).
- Go/No-Go board miraton cutover.

**Risk / Plan-B**

- Nëse critical UAT dështon, cutover shtyhet; PRO DATA mbetet source-of-record.
- Nëse SEF/hardware dështon, live fiskal shtyhet.

---

### Faza 10 — Lansim

**Qëllimi:** prodhimi i plotë, monitorim, rakordim ditor dhe handover.

**Owner / kohëzgjatje / data më e vonë:** Group Owner + PM + Hypercare Team · 1 javë + hypercare · nisje 2027-01-01 vetëm me Go/No-Go PASS  
**Feature buckets:** Launch, Hypercare, Operations.

**Scope brenda**

- Go-live execution.
- Daily reconciliation.
- Incident management.
- Rollback window.
- Handover docs.
- Support hypercare.

**Acceptance criteria**

- Sistemi përdoret si source-of-record.
- Çdo ditë rakordohet: shitje, blerje, TVSH, stok, cash/bank, SEF.
- Nuk ka bllokues P0/P1 të hapur.
- Handover i nënshkruar nga pronari/kontabilisti/operacionet.

**Risk / Plan-B**

- Nëse ditët e para nuk rakordojnë, aktivizohet rollback/hybrid fallback sipas runbook-ut.

---

## 6. Rendi aktual i punës (përditësuar 12.07.2026 — pa cikël)

*(**Full-corpus re-audit + harta LAPT = KRYER** me këtë ndryshim — `ATK_Source_Register.md` v2.0 + `ATK_SEF_Requirements_Ledger.md` + Gate v2.0; `ATK-011` = **OPEN / PENDING_INDEPENDENT_REVIEW** (disposition-pass i përpunuar; mbyllja pas review-t të pavarur + Human Gate).)*

1. **Step A — ky docs correction PR** (kanonizimi i verdiktit + kontratave).
2. **Step B — PRE-Tier2 code hardening (`ATK-012`, P0):** mbyllja e **T2-01..T2-10** (Gate v2 §4).
3. **Step C — official golden vectors + fake-HTTP/negative tests** (kundër C#/Go @ pins).
4. **Independent re-review** i deltës + i të gjitha gates të Tier 2.
5. **HUMAN GATE** — autorizimi eksplicit i Pronarit **për thirrjen e PARË reale** (asnjë `/ca/*`, `/ca/signcsr`, `/pos/*`, `/citizen/*` para tij).
6. **Private `/ca/verify` PREFLIGHT** (wrapper evidence-safe i ndërtuar në Step B/C — sot NUK ekziston) → **vetëm pas suksesit vazhdon pjesa tjetër e Tier 2**.
7. **Certifikimi / go-live = NO-GO më vete** (blockers shtesë — Gate v2 §5).
8. **Paralel:** PoC-4b ecën i pavarur kur vjen hardware-i.

*(Rendi origjinal i 07.07.2026 — HISTORIK: review Fable/Codex → aprovim → PR docs → 5 PoC-të me radhë. Sekuenca është ekzekutuar; statusi final është te §4 "Statusi real": **PoC-4b dhe PoC-5 Tier 2 mbeten pending**.)*
