# PRO DATA — Source→Target Closure v2 (iData+)

**Versioni:** 2.1 · **Data:** 12 Korrik 2026 · **Pronari:** Group Owner (Human Gate) · **Statusi:** canonical/effective upon merge of this change (supersedes v1.1 — VETËM për interpretimin target/migrim/maturity, shih §9)
**Burimi i këtij versioni:** canonical docs aktuale (SSOT, tracker, roadmap, `05_Decision_Gates_ADR.md`/ADR-003, `04_Data_Model_Draft.md`, `02_RBAC_Matrix.md`, `03_Accounting_Compatibility_Matrix.md`) + **kodi/migrimet/testet tracked** + **metadata e evidence paths**. **Zero content-read nga depoja e injoruar e evidence-it private-local (ignored private-local evidence store), backup-e, MDF/LDF, DB e restauruar apo evidence tjetër private-local në këtë version** — evidence historike trajtohet si **opaque reference** (§7).

---

## 0. RREGULLI KRYESOR — NXJERRJA E MBYLLUR ≠ E IMPLEMENTUAR

> **"Extraction complete" do të thotë VETËM se evidence është siguruar.** NUK do të thotë se target capability është e implementuar, e testuar, production-ready apo e nënshkruar (signed-off). Implementimi ndodh **në fazën përkatëse sipas kolonës "Faza" dhe roadmap-it**, dhe çdo capability kalon nëpër **pipeline-in Source→Target** (§1B). **ZERO përqindje pa metodologji** — përdoren vetëm statuse të numërueshme me prova të verifikueshme.

Nxjerrja nga backup-i mbetet **E MBYLLUR** (asnjë nxjerrje e re; ky version nuk hapi asnjë burim evidence). Backup-i mbetet **golden reference baseline deri në lansim** — për parity/UAT/cutover — por statusi i çdo capability në iData+ matet VETËM me boshtet e §1.

## 1. Metodologjia — dy boshte të NDARA + një qualifier

### 1A. TREATMENT — tag-set (JO vlerë ekskluzive)

| Tag | Kuptimi |
|---|---|
| `REQ` | capability **kërkohet** në iData+ |
| `MD` | **master-data** migrohet |
| `OB` | **saldo hapëse** migrohet në cutover |
| `REF` | source/evidence përdoret **vetëm si referencë** |
| `NO` | **source data nuk migrohet** |

Kombinimet lejohen (`REQ+REF`, `REQ+MD`, `REQ+NO`, `REF+NO`, …).

> **RREGULL KRITIK:** `NO` do të thotë *"source data nuk migrohet"* — **JO** *"target capability nuk kërkohet"*. Shembull kanonik: **passwords/PIN/MAC = `NO` për migrim** (ADR-003), por **auth/user capability = `REQ`** në target.

### 1B. TARGET MATURITY — stage-i MË I LARTË I PROVUAR

`GAP → EVIDENCE → SPECIFIED → MODELED → IMPLEMENTED → TESTED → SIGNED-OFF`

| Stage | Përkufizimi (prova e kërkuar) |
|---|---|
| `GAP` | asnjë provë target |
| `EVIDENCE` | burim canonical i sanitizuar OSE **opaque evidence reference** që ekziston por **nuk është rishikuar në këtë PR** |
| `SPECIFIED` | file+seksion canonical i verifikueshëm |
| `MODELED` | entitet/fushë/invariant **konkret** në `04_Data_Model_Draft.md` |
| `IMPLEMENTED` | **kod/migrim JO-PoC** në repo (commit/PR real; implementim aktual ekzistues) |
| `TESTED` | test i ekzekutueshëm që teston **implementimin aktual** (jo vetëm simulatorin) + rezultat CI real |
| `SIGNED-OFF` | rol + datë + evidence e qëndrueshme + aprovim i verifikueshëm |

Çdo rresht ka: **maturity + proof/referencë + fazë + qualifier PoC (nëse vlen) + gap/next gate**.

> Kur një claim s'provohet dot pa hapur evidence të ndaluar → shënohet **`EVIDENCE_REFERENCE_ONLY / NOT_REVERIFIED_IN_THIS_PR`** dhe **NUK promovohet** në IMPLEMENTED/TESTED/SIGNED-OFF.

### 1C. `PROVEN-PoC` — qualifier i VEÇANTË (jo maturity stage)

- **PROVEN-PoC ≠ IMPLEMENTED production. PROVEN-PoC ≠ TESTED production. PoC PASS ≠ production readiness.**
- PoC provon **feasibility/invariant**; capability finale vazhdon pipeline-in në fazën e vet.
- **Migrimet `0002`–`0006` janë PoC sipas header-ave realë të tyre** (0002 = PoC-1/ENG-003 · 0003 = PoC-2 · 0004 = PoC-3 · 0005 = PoC-4a · 0006 = PoC-5); **vetëm `0001` është skeleton JO-PoC (Increment 1)**. Asnjë provë nga 0002/0003, funksionet `poc2_*` apo testet PoC **nuk e promovon** capability-n në IMPLEMENTED/TESTED production.
- Testet e librarisë `packages/accounting` provojnë **feasibility të librarisë/simulatorit** (rregullat në izolim) — **JO** target capability të integruar production.

## 2. Çfarë u nxor nga backup-i — EVIDENCE HISTORIKE (opaque)

Përmbledhja Wave 1–3 (struktura/DDL; accounting/costing/fiscal/setup; restaurant POS/KDS; audit/user/RBAC/config i sanitizuar) dhe metrikat e nxjerrjes qëndrojnë **siç janë regjistruar historikisht** (v1.1 i këtij dokumenti + SSOT Decisions Log historik + auditi i plotësisë private-local). **Historical evidence remains opaque, not reread and not reproduced here.** Statusi në këtë version: `EVIDENCE_REFERENCE_ONLY / NOT_REVERIFIED_IN_THIS_PR` (§7, §9).

## 3. Parity Matrix v2 — Source→Target me dy boshte

*(Proof-et = file/migrim/test **tracked** në repo ose file+seksion canonical — të verifikueshme pa hapur evidence të ndaluar. "0001…0006" = `packages/database/migrations/…`; **`0001` = skeleton JO-PoC; `0002`–`0006` = migrime PoC (PoC-1…PoC-5 sipas header-ave)** — provat prej tyre mbeten në qualifier-in PROVEN-PoC, §1C.)*

| Domeni | Source (PRO DATA) | Target (iData+) | Treatment | Target Maturity | PROVEN-PoC | Proof | Faza | Gap / next gate |
|---|---|---|---|---|---|---|---|---|
| GL / Kontabilitet | skema e kontimit, ditari, `*DEL` | `journal_entry/line` + motori i postimit | `REQ+REF+NO` (historia s'migrohet) | **MODELED** (target model) | **PO** (rregullat në librari `packages/accounting` + rrjedha DB PoC-2 — **jo production TESTED**) | `04_Data_Model_Draft.md` §C/§L; 0003 (**PoC-2**: journal/APPEND-ONLY/DEFERRABLE debit=kredit = feasibility); `packages/accounting/src/posting.test.ts` (feasibility librarie) | F2 | posting engine production + integrimi (ADR-012 Open; `poc2_*` = PoC → DROP/supersede) |
| Libra ATK + Deklaratë TVSH | Libri Blerjes/Shitjes (PLANET2) + template zyrtare | `tax_book_entry` + buckets, `vat_declaration` | `REQ+REF+NO` | **MODELED** (struktura header→buckets) | **PO** (buckets/mixed-VAT rrjedha DB PoC-2; validimi baseline Maj 2026 = `ACC-010` closed, vlerat private-local) | `04_Data_Model_Draft.md` §C; 0003 (**PoC-2**); `03_Accounting_Compatibility_Matrix.md` (box-maps) | F2 | `vat_declaration`/eksporti production = **pending F2**; rounding → `ACC-003` |
| Costing / COGS | `tblCalculationItemsCost*` | `inventory_cost_layer / stock_valuation` | `REQ+REF+NO` | **MODELED** | — | `04_Data_Model_Draft.md` §D | F3 | implementim + rikrijim kundër baseline (UAT) |
| Historik çmimesh | `tblArtikujtCmimetLog` | `price` + `price_history` | `REQ+REF+NO` (log-u historik s'migrohet; çmimet AKTIVE = MD te rreshti i artikujve) | **MODELED** | — | `04_Data_Model_Draft.md` §B | F2/F3 | implementim |
| Artikuj / njësi / tipe | `tblArtikujt*`, `tblNjesiteMatese`, `tblTipetMalli` | `article`, `uom`, `category` | `MD+REQ` | **MODELED** | — | `04_Data_Model_Draft.md` §B; katalogët zyrtarë SEF 31/53 → `ATK_Compliance_Gate.md` §2b (follow-up F1/F5) | **F3** (article/product master) · **F9** (migrimi MD) | migrimi real i MD = delivery gate ADR-003 (mapping/cleansing/dedup) |
| Banka / vite fiskale | `tblBankat`, `tblFiscalYears` | `bank_account`, `fiscal_period` | `MD` | **MODELED** | **PO** (`fiscal_period`+period-lock si feasibility në 0003 = **PoC-2**; invarianti production i period-lock **JO i implementuar ende**) | `04_Data_Model_Draft.md` §C; 0003 (**PoC-2**) | F2 + F9 | period-lock production (F2) + migrimi MD në cutover |
| Saldo hapëse | `tblDocBeginningBalance*` | saldo hapëse në cutover **1 Jan 2027** | `OB` | **SPECIFIED** | — | ADR-003; `Cutover_Rollback_Runbook.md` §3–4 | F9 (cutover) | delivery gates: mapping/cleansing/dedup/freeze/dry-run/reconciliation/sign-off |
| Fiskalizim / SEF | `tblFiscalisedDocuments` | `fiscal_coupon`, fiscal outbox | `REQ+REF+NO` | **SPECIFIED** | **PO** (PR #16 = **evidence historike mock**; 0006 = **PoC-5**, tabela `poc5_*`) — **post-audit 12.07: Tier 1 overall PARTIAL/REOPENED · compliance FAIL (4 CONTRADICTS) · Tier 2 NOT IMPLEMENTED/NO-GO** | SSOT §7; `ATK_Compliance_Gate.md` v2.0 + Ledger | F5 | Step B (T2-01..10, `ATK-012`) → Step C → Tier 2; print layer |
| POS — porosi/tavolina | `tblTMPRestShitjet`, `tblDocPorositeShitjet*`, `tblRestTavolinat`(X/Y) | `table`(pos_x/y), `table_session`, `order`, `order_item` | `REQ+REF+NO` | **MODELED** | — | `04_Data_Model_Draft.md` §G | **F7 (bazë) / F8 (POS i plotë)** | implementim POS |
| KDS — kuzhinë | `tblTMPRestShitjetKuzhina`, `tblRestDailyPorosiaCount` | `kitchen_ticket`, `kds_station` | `REQ+REF+NO` | **MODELED** | — | `04_Data_Model_Draft.md` §G | F8 | implementim KDS |
| Barazim arke/kamarieri | `tblDocPOSBarazimi*` | `cash_shift`, `worker_settlement` | `REQ+REF+NO` (capability e kërkuar; source vetëm referencë; historia s'migrohet — modeli ynë **net-new**) | **MODELED** | — | `04_Data_Model_Draft.md` §G | F8 | implementim + reconciliation ditore (runbook §9) |
| Fshirje/korrigjime → aprovim | `*_Deleted`+trigger, `*DEL` | `manager_approval` + audit (anulim+PIN+arsye) | `REQ+REF+NO` (**më i rreptë se source**) | **MODELED** | — | `04_Data_Model_Draft.md` §G (`manager_approval`); invarianti bazë i audit-it → rreshti "Audit — invarianti DB" | F1/F8 | approval workflow JO TESTED ende — implementim + teste |
| RBAC — skeleton i skemës | `tblUSERS` (groups/rights/report/special/warehouse/POS) | `role`, `role_permission`, `permission`, `business_membership` | `REQ+REF+NO` (privilegjet legacy = vetëm referencë; s'migrohen 1:1) | **IMPLEMENTED** (**ngushtë: vetëm skema skeleton**) | — | 0001 (**skeleton JO-PoC**); `02_RBAC_Matrix.md` (spec e matricës) | F1/F2 | matrica e plotë per-feature + **API enforcement production = F1** |
| RBAC — izolimi cross-tenant | — | composite FK cross-tenant + deny-by-default + RLS | `REQ` | **MODELED** (invarianti composite-FK = konventë e `04_Data_Model_Draft.md`/ENG-003) | **PO** (PoC-1 PASS, PR #10 — feasibility në 0002 **PoC-1/ENG-003** + `tenant-isolation.test.ts`, demo API + RLS **test-scoped**) | `04_Data_Model_Draft.md` (konventa globale business_id/composite FK); roadmap ENG-003 (closed) | F1 | enforcement production (API + RLS policy reale) = F1 |
| Përdorues / auth | `tblUSERS` (passwords/PIN/MAC) | `app_user` + `business_membership` | `REQ+NO` (**capability REQ; source data NO** — shembulli kanonik i §1A) | **IMPLEMENTED** (**ngushtë: skema skeleton 0001**) | — | 0001 (**skeleton JO-PoC**): `app_user` = **identitet GLOBAL pa `business_id`** (email/name/mfa/status); lidhja tenant = **`business_membership.business_id`**; `role`/`role_permission` = tenant-scoped | F1 | **auth stack production = F1** (ADR-010 vetëm ku provider/hosting është relevant); users rikrijohen/resetohen — kurrë migrim kredencialesh |
| Audit — invarianti DB | `tblAuditLogs`, `tblUsersLog`, `tblActivities` | `audit_log` append-only | `REF+NO` (historiku NUK migrohet) | **TESTED** (vlen VETËM për invariantin append-only: `audit_log` refuzon UPDATE/DELETE) | — | 0001 trigger `audit_log_no_mutate` (**skeleton JO-PoC**) + `schema.test.ts` "audit_log është append-only" (CI real) | F1 | — |
| Audit — producers/retention | — | event producers + retention/arkivim + lidhja me approval workflow | `REQ` | **MODELED** | — | `04_Data_Model_Draft.md` §G; SSOT §9 (LMDhP retention/minimizim) | F1/F8 | JO TESTED ende — implementim producers + retention policy + teste |
| Config | tabela config legacy | config i **versionuar/typed** | `REQ` (riprojektim) | **GAP** | — | — (kërkesa e regjistruar; s'ka entitet konkret ende) | F1/F2 | modelim config i versionuar |
| Procedura legacy (stored procedures) | stored procedures | logjika → NestJS/Postgres functions | `REF+NO` | **EVIDENCE** (`EVIDENCE_REFERENCE_ONLY / NOT_REVERIFIED_IN_THIS_PR`) | — | referencë opaque historike (§7) | F2–F8 (sipas domenit) | rilexim i targetuar per-fazë (jo nxjerrje e re) |

## 4. Capability Coverage Map — WMS/OMS (kufiri i qartë)

> **Ky është Capability Coverage Map — JO pretendim se iData+ është WMS/OMS universal i implementuar.** Guardrails: PLANET `order` (dine-in POS) **≠** `sales_order` universal; `delivery_note` (fletëdërgesë dalëse e lidhur me `invoice`) **≠** `goods_receipt`; **logjika e prodhimit KABI NUK inferohet nga backup-i i PLANET-it** (vjen nga fabrika — SSOT §5); `OPS-001`/RESORT mbetet **OPEN**; `business_unit` nuk ngurtësohet këtu. Statuset e lejuara pa vendim: `GAP` / `LATER` / `DECISION_REQUIRED`.

| Capability | Kufiri (çfarë ËSHTË / çfarë S'ËSHTË) | Ku specifikohet | Treatment | Target Maturity | PROVEN-PoC | Proof | Faza | Gap / next gate |
|---|---|---|---|---|---|---|---|---|
| `sales_order` — PLANET dine-in | Porosia POS në tavolinë (`order`/`order_item`) = e modeluar. **≠ `sales_order` universal** | `04_Data_Model_Draft.md` §G | `REQ+REF+NO` | **MODELED** | — | §G `order/order_item/table_session` | **F7 (bazë) / F8 (POS/KDS i plotë)** | implementim POS |
| `sales_order` — KABI MTO | **MTO kërkon Sales Order PARA `production_order`** (SSOT §5: *"MTO — porosi speciale — kërkon Sales Order para urdhrit"*); entiteti s'është i modeluar ende | SSOT §5 (KABI, MTS/MTO) | `REQ` | **GAP** | — | SSOT §5 | **F6** | **decision gate** — modelimi i `sales_order` MTO para F6 |
| `sales_order` — OMS universal | Order-to-invoice wholesale/OMS përtej PLANET dine-in + KABI MTO | — | `REF+NO` (vetëm referencë source; pa kërkesë të vendosur) | **GAP** | — | — | **LATER** | **Human Gate** — vendim kërkese (s'ndalohet përgjithmonë) |
| `purchase` — procesimi invoice-based | Blerja sot = **`invoice` type=purchase** | `04_Data_Model_Draft.md` §F | `REQ+REF+NO` | **MODELED** | — | §F `invoice(purchase)` | F2/F3 | implementim |
| `purchase_order` — workflow PO | Porosi PARA faturës, approval, matching — **s'është i modeluar** | — | `REF+NO` (pa kërkesë të vendosur) | **GAP** | — | — | **LATER** | **Human Gate** — vendim kërkese për PO workflow |
| `goods_receipt` — pranimi si movement | Pranimi ekziston si **`stock_movement(type=in)`** | `04_Data_Model_Draft.md` §D | `REQ+REF+NO` | **MODELED** | — | §D `stock_movement` | F2/F3 | implementim |
| `goods_receipt` — dokumenti GR | **Dokument GR më vete s'ka** — `delivery_note` ekzistuese është dalëse (shitje), **JO** GR | `04_Data_Model_Draft.md` §F (`delivery_note` — dalëse) | `REQ` | **GAP** | — | — | F2/F3 | **DECISION_REQUIRED** — dokument GR më vete apo movement+invoice (vendim modelimi F2/F3) |
| `reservation / allocation` | S'ekziston asnjë entitet rezervimi/alokimi stoku | — | `NO` (source s'migrohet; kërkesa target e pavendosur) | **GAP** | — | — | **LATER** | **Human Gate / requirement decision** (s'ndalohet përgjithmonë) |
| `bin / putaway` — warehouse-level | `warehouse` ekziston (depo/restorant/shank/vehicle) | `04_Data_Model_Draft.md` §D (`warehouse`) | `REQ` | **MODELED** | — | §D | F3 | implementim |
| `bin / putaway` — bin-level | **Pa nivel bin/lokacion brenda depos** | — | `NO` (pa kërkesë të vendosur) | **GAP** | — | — | **LATER** | **Human Gate** për bin-level |
| `pick / pack / ship` — KABI van fulfillment | **Van distribution pipeline KABI** (`van_load`, `driver_route`, `proof_of_delivery`, `driver_day_close`) — **van-flow, JO pick/pack/ship universal magazine** | `04_Data_Model_Draft.md` §F | `REQ+REF+NO` | **MODELED** | — | §F (ngarkim−shitje−kthim=mbetje + variance) | **F5** (van sales/loading/POD/day-close) | implementim van-flow |
| `pick / pack / ship` — universal | Pick/pack/ship universal magazine | — | `NO` (pa kërkesë të vendosur) | **GAP** | — | — | **LATER** | **Human Gate** për pick/pack universal |
| `wave picking` | S'ekziston | — | `NO` (source s'migrohet; kërkesa target e pavendosur) | **GAP** | — | — | **LATER** | **Human Gate / requirement decision** |
| `cycle count` — adjustment hook | `stock_movement(type=adjust)` + `stock_balance` ekzistojnë | `04_Data_Model_Draft.md` §D | `REQ+REF+NO` | **MODELED** | — | §D | F3 | implementim |
| `cycle count` — procesi/dokumenti i numërimit | **Proces/dokument numërimi fizik s'është modeluar** (kërkohet së paku për saldot hapëse në cutover) | runbook §4 (T-1 saldot) | `REQ` | **GAP** | — | — | F3 + F9 | modelim i dokumentit të numërimit para cutover |
| `assembly / production` | **Prodhimi KABI**: recipe/BOM me version+approved_by+locked + cost layers; regjistri i pasterizimit; CCP gating; MTO/MTS advanced. **Burimi = fabrika/on-site (SSOT §5) — JO backup-i i PLANET-it.** PLANET kitchen prep **≠** assembly | `04_Data_Model_Draft.md` §D (recipe/BOM), §E (KABI); SSOT §5 | `REQ` | **MODELED** | — | §D/§E; SSOT §5 (CCP/pasterizim; MTS/MTO) | **F3** (recipe/BOM/cost layers) · **F4** (regjistri i pasterizimit — minimumi) · **F6** (CCP gating i plotë + MTO/MTS advanced) | implementim per fazë + batch costing (ADR-005, me kontabilistin); MTO → `sales_order` KABI (F6) |

## 5. ADR-003 — RIAFIRMUAR (i mbyllur, NUK rihapet)

**ADR-003 = Accepted (27.06.2026):** **fillim i pastër**; **master-data migrohet**; **saldo hapëse migrohet** (cutover 1 Jan 2027); **pa histori transaksionale**; **users/passwords/PIN/MAC/secrets NUK migrohen** (rikrijohen/resetohen); **historia transaksionale = REF/NO**.

**OPEN mbeten VETËM delivery gates** (jo vendimi): **mapping source→target · cleansing · dedup · freeze window · dry-run · reconciliation · sign-off** — jetojnë në F9 + `Cutover_Rollback_Runbook.md`. **Nuk ka dhe nuk krijohet vendim OPEN "saldo hapëse vs selected history"** — ai vendim është i mbyllur. Çdo ndryshim i ADR-003 kërkon **Proposed ADR të ri + analizë ndikimi + Human Gate** (JO në këtë PR).

## 6. Legacy PRO DATA custody ≠ iData+ product DR (ADR-023)

**Dy kontrolle TË NDARA — nuk bashkohen:**

**A. Legacy PRO DATA backup custody** (burimi historik deri në cutover):
- **Burimi autoritativ = `.bak`**; MDF/LDF të container-it **NUK janë** burimi autoritativ.
- Kontrolle të kërkuara: **kopje e dytë e enkriptuar** · **hash manifest** · **isolated restore drill**.
- **Statusi: REQUIRED / OPEN / EVIDENCE PENDING** — evidence jepet **privatisht nga Pronari**; **ky PR nuk pretendon se e verifikoi asgjë** (asnjë filename/path/hash/inventar privat këtu; asnjë status "verified" pa evidence private të Pronarit).
- Regjistrimi operacional → `Cutover_Rollback_Runbook.md` (seksioni përkatës).

**B. iData+ PostgreSQL DR** = **ADR-023**: PITR + backup + restore drill i **produktit të ri** — track më vete, i pandryshuar këtu.

## 7. Evidence historike — OPAQUE / READ-ONLY

Content-read i **NDALUAR** (folderi i evidence-it historik të extraction-it = **gitignored, private-local**; verifikuar në këtë PR **vetëm me metadata**: `test -e` = ekzistojnë; `git ls-files` = jo në repo):

> **Dy artefakte historike të extraction-it (auditi i plotësisë dhe matrica v1) ruhen private-local; opaque references; not reverified and not reproduced in this PR.** E njëjta vlen për pjesën tjetër të folderit dhe paketën parity të Codex-it (private-local, opaque, pa path/emra këtu).

Rregull: **nuk hapen, nuk editohen, nuk citohen vlera/rreshta prej tyre, nuk rishkruhet evidence historike** — vetëm ekzistencë me metadata. **Historical evidence remains opaque, not reread and not reproduced here.** Statuset e tyre: `EVIDENCE_REFERENCE_ONLY / NOT_REVERIFIED_IN_THIS_PR`.

## 8. Phase Usage — si përdoret baseline-i (i pandryshuar në parim)

| Faza | Përdorimi i PRO DATA |
|---|---|
| Faza 0 | data model + parity scoping + opening-balance scoping (**kryer**) |
| Accounting Core (F2) | rakordim **1:1** GL / libra / Deklaratë TVSH kundër baseline-it |
| Stok + COGS (F2/F3) | rikrijim COGS + cost layers kundër evidence historike |
| KABI (F5) | parity vetëm ku ka; **logjika dairy NUK inferohet nga PRO DATA** |
| PLANET POS/KDS (F7/F8) | parity operacional (porosi/floor-plan/queue) |
| SEF (F5) | parity i fiscal docs |
| UAT | test kundër baseline-it (numrat tanë duhet të përputhen) |
| Cutover (1 Jan 2027, F9) | saldo hapëse + kontroll final + delivery gates ADR-003 |

## 9. Scope i supersede-it (v2 mbi v1.1)

**v2 supersedes VETËM:** interpretimin **target** (çfarë do të thotë "mbulohet"), interpretimin e **migrimit** (tag-set §1A, jo vlera ekskluzive) dhe interpretimin **maturity/readiness** (§1B/§1C) — sipas **ADR-003** dhe canonical docs aktuale. **Nëse interpretimi historik bie ndesh me ADR-003 → ADR-003 fiton** (pa hapur evidence për ta verifikuar).

**v2 NUK supersede:** provenance-in e nxjerrjes, metrikat e nxjerrjes si **evidence historike**, as ekzistencën e artefakteve historike (§2, §7).

**Korrigjime stale nga v1.1 (before → after):**
- Burimi "(memory)" → **hequr**; burimet e v2 = canonical + tracked + metadata (header).
- Path private/non-portable i paketës Codex → **referencë opaque pa path** (§7).
- "Mbetet **një muaj real** për validim" → **stale**: `ACC-010` = **closed** (validuar me Maj 2026, muaj i deklaruar; vlerat ekzakte private-local; rounding-rule i hapur → `ACC-003`).
- "**Të gjitha** entitetet e konfirmuara janë shtuar" → zëvendësuar me **lista të verifikueshme** per-rresht (§3, kolona Proof).
- Çdo lexim "extraction complete ⇒ implementation" → **i pavlefshëm** (§0).
- "RESORT — pikë për konfirmim" → jeton si **`OPS-001` (OPEN)** në tracker (s'dyfishohet këtu).

## 10. Lidhjet

ADR-003 + delivery gates → `05_Decision_Gates_ADR.md` · saldo/cutover → `Cutover_Rollback_Runbook.md` · box-maps → `03_Accounting_Compatibility_Matrix.md` · RBAC → `02_RBAC_Matrix.md` · entitetet → `04_Data_Model_Draft.md` · statuset e PoC-ve → roadmap §4 · `ACC-004` (answered — parity scope) / `ACC-010` (closed) / `OPS-001` (OPEN) → tracker.

## Mundësi GOLD — Source→Target Closure Lint

**Status: APPROVED FOR PLANNING · NOT IMPLEMENTED · NON-BLOCKING për Tier 2** — implementimi kërkon **PR të veçantë tooling/code pas PR #19**. **Prioriteti: HIGH-ROI.**

**Qëllimi (linter docs-only mbi këtë dokument + docs e sinkronizuara):** validon vocabulary-n **Treatment** (vetëm REQ/MD/OB/REF/NO + kombinime); **ndalon IMPLEMENTED/TESTED kur proof-i është PoC/simulator**; zbulon qeliza të detyrueshme bosh; kontrollon stage të përzier/overclaim; kontrollon phase references kundër roadmap-it; kontrollon version/status drift ndërmjet SSOT/roadmap/PM/INDEX; kontrollon ekzistencën e **7 delivery gates** (mapping/cleansing/dedup/freeze/dry-run/reconciliation/sign-off); **nuk lexon asnjë evidence private**.

**Problemi që zgjidh:** manual review lejoi PoC→production overclaim, phase drift, missing `dedup` dhe version drift.

**Kosto/kompleksitet:** mesatare; zero ndikim runtime; risku kryesor = false positives nëse parser-i bëhet shumë rigid.

**Faza:** PR i veçantë pas merge-it të PR #19, paralel me governance/full-corpus work; **nuk bllokon PRE-TIER2** — bëhet required CI vetëm nëse Human Gate e promovon më vonë.

**Acceptance evidence për PR-në e ardhshme:** linter-i duhet të ketë **fixtures/tests që kapin të gjitha gabimet e gjetura në review-n e PR #19** (PoC-migrim si "IMPLEMENTED", TESTED nga simulator, LATER në kolonën Treatment, qeliza bosh, dedup i munguar, phase drift, version drift).

**Kriter shtesë pranimi — PER-COMMIT HISTORY SCAN (jo vetëm net-diff):**
- Linter-i kontrollon **ÇDO commit në `base..HEAD` veçmas**, jo vetëm diff-in neto.
- Për çdo commit skanon **added-lines** për: private paths/filenames · evidence metrics · operational-state claims · forbidden Treatment values · **PoC→IMPLEMENTED/TESTED promotion** · mixed maturity stages · qeliza bosh · phase/version/gate drift.
- **Një vlerë e shtuar në commit A dhe e hequr në commit B = FAIL** (gabimi "introduced-then-removed" mbetet në histori).
- Raporton vetëm **file/rresht/kategori** — kurrë materiale private.
- Fixtures riprodhojnë **sintetikisht** gabimin "introduced-then-removed".
- Implementimi mbetet **PR tooling i VEÇANTË pas PR #19**; asnjë script/config/workflow/test në PR #19.

*(Ky PR vetëm e regjistron mundësinë canonical — asnjë script/config/workflow/test nuk krijohet këtu.)*
