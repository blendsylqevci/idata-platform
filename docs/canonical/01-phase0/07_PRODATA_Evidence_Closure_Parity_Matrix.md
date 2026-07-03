# PRO DATA — Evidence Closure + Parity Matrix v1 (iData+)

**Versioni:** 1.1 · **Data:** 28 Qershor 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.8 · `prodata-legacy-reference` (memory)
**Statusi:** 🔒 **NXJERRJA NGA BACKUP-I = E MBYLLUR** (Wave 1+2+3; **1076 tabela / 223 non-zero**; Priority A & B gaps = 0). Backup-i = **golden reference baseline DERI NË LANSIM** — referencë kontrolli në çdo fazë (Accounting, Inventory, KABI, PLANET POS/KDS, SEF, UAT, Cutover); **jo vetëm input i Fazës 0, dhe jo "i mbaruar e harruar".** Evidence → **specifikim implementimi**, jo nxjerrje e mëtejshme.
**Shënim burimi:** ndërtuar nga përmbledhjet e Wave 1–3 + memory + ekstraktet tona. Codex prodhoi gjithashtu paketën e vet closure/parity (CSV: `prodata_evidence_index`, `prodata_phase_usage_matrix`, `prodata_parity_requirements`, te `<private-local>/.../prodata_evidence_closure_parity_matrix_*`) — **ato = evidence i detajuar; ky doc (07) = narrativi canonical në SSOT**; të dyja rakordohen.

---

## 1. Çfarë u nxor nga backup-i (përmbledhje)
| Wave | Domeni | Tabela/artefakte kyçe | Vëllimi |
|---|---|---|---|
| **DDL** | Strukturë e plotë | tabela, kolona, keys, **~100 procedura të indeksuara** | komplet |
| **Wave 1** | Accounting / Costing / Fiscal / Setup | `tblCalculationItemsCost*`, `tblDocBeginningBalance*`, `tblArtikujtCmimetLog`, `*DEL`, `tblFiscalisedDocuments`, POS-financial PLANET2, `tblBankat`/`tblFiscalYears`/`tblTipetMalli`/`tblNjesiteMatese` | costing = miliona rreshta (full raw gz) |
| **Wave 2** | Restaurant / POS / KDS | `tblRestTavolinat` (X/Y), `tblTMPRestShitjet`, `tblTMPRestShitjetKuzhina`, `tblRestDailyPorosiaCount`, `tblTMPRestShitjet_Deleted`, `tblDocPOSBarazimi*` | 9 raw / 804 rreshta; 80 inventar; 71 schema-only |
| **Wave 3** | Audit / User / RBAC / Config (**sanitized**) | `tblAuditLogs`, `tblUsersLog`, `tblActivities`, `tblUSERS`(+groups/rights) | 34,942 rreshta; **secrets redacted; users/hosts/devices hashed** |
| Audit | Completeness | `PRODATA_Backup_Completeness_Audit.md` | Priority A=0, B=0 |

---

## 2. Parity Matrix — mbulimi per domen
**Trajtimi:** `REQ` = kërkesë për iData+ · `MD` = master-data migrohet · `OB` = saldo hapëse në cutover · `REF` = vetëm referencë/evidence · `NO` = nuk migrohet.

| Domeni | Tabela/proc PRO DATA | Entiteti/moduli iData+ | Trajtimi |
|---|---|---|---|
| GL / Kontabilitet | skema e kontimit, ditari, `*DEL` | `journal_entry/line`, motori i postimit | REQ + REF(logjika) |
| Libra ATK + Deklaratë TVSH | Libri Blerjes/Shitjes (PLANET2) + template zyrtare | `tax_book_entry`, `vat_declaration` | REQ *(shih `03_Accounting_Compatibility_Matrix`)* |
| Costing / COGS | `tblCalculationItemsCost*` | `inventory_cost_layer/stock_valuation` (mesatare e ponderuar) | REQ + REF(rikrijim) |
| Historik çmimesh | `tblArtikujtCmimetLog` | `price` + `price_history` | REQ |
| Artikuj / njësi / tipe | `tblArtikujt*`, `tblNjesiteMatese`, `tblTipetMalli` | `article`, `uom`, `category` | MD |
| Banka / vite fiskale | `tblBankat`, `tblFiscalYears` | `bank_account`, periudha kontabël | MD |
| Saldo hapëse | `tblDocBeginningBalance*` | saldo në cutover **1 Jan 2027** | OB |
| Fiskalizim / SEF | `tblFiscalisedDocuments` | `fiscal_coupon`, fiscal outbox | REQ + REF *(SEF = github.com/fiskalizimi)* |
| POS — porosi/tavolina | `tblTMPRestShitjet`, `tblDocPorositeShitjet*`, `tblRestTavolinat`(X/Y) | `table`(pos_x/y), `table_session`, `order`, `order_item` | REQ + REF |
| KDS — kuzhinë | `tblTMPRestShitjetKuzhina`, `tblRestDailyPorosiaCount` | `kitchen_ticket`, `kds_station` | REQ + REF |
| Barazim arke/kamarieri | `tblDocPOSBarazimi*` (**bosh**) | `cash_shift`, `worker_settlement` | REQ **(net-new)** |
| Fshirje/korrigjime | `*_Deleted`+trigger, `*DEL` | `manager_approval` + audit (anulim+PIN) | REQ **(më i rreptë)** |
| RBAC / leje | `tblUSERS` (groups, rights, report/special rights, warehouse/location, POS/mobile) | matrica RBAC per-feature *(R/C/U/Anulim-Korrigjim/Print/Aprovim)* | REQ (transformim, jo schema) |
| Përdorues / auth | `tblUSERS` (passwords/PIN/MAC) | `user`, `role`, `membership` | **NO** (rikrijohen/resetohen) |
| Audit / aktivitet | `tblAuditLogs`, `tblUsersLog`, `tblActivities` (>1.7M PLANET2/RESORT) | `audit_log` (append-only) i ynë i ri | REF |
| Config | tabela config | config i **versionuar/typed** | REQ (riprojektim) |
| Procedura (~100) | stored procedures | logjika → NestJS/Postgres functions | REF |

---

## 2.1 Phase Usage — si përdoret backup-i (golden baseline deri në lansim)
| Faza | Përdorimi i PRO DATA |
|---|---|
| Faza 0 | data model + parity scoping + opening-balance scoping |
| Accounting Core | rakordim **1:1** GL / libra / Deklaratë TVSH kundër baseline-it |
| Stok + COGS | rikrijim COGS + cost layers kundër `tblCalculationItemsCost*` |
| KABI | parity vetëm ku ka; **logjika dairy NUK inferohet nga PRO DATA** (shih §5) |
| PLANET POS/KDS | parity operacional (porosi/floor-plan/queue) |
| SEF | parity i fiscal docs |
| UAT | **test kundër baseline-it** — numrat tanë duhet të përputhen me PRO DATA |
| Cutover (1 Jan 2027) | saldo hapëse + kontroll final |

## 3. Çfarë bëhet KËRKESË për iData+ (nga evidence)
- Kontabilitet ATK i plotë + **auto-Deklaratë TVSH** (box-maps blerje+shitje të marra).
- **COGS** me cost layers (mesatare e ponderuar) + **historik çmimesh** per artikull.
- **Saldo hapëse** në cutover (nga `tblDocBeginningBalance`).
- **Fiscal evidence/SEF** (parity me `tblFiscalisedDocuments`).
- POS: porosi të hapura per tavolinë + **floor-plan me X/Y** + KDS queue + numërim ditor.
- **Barazim arke/kamarieri** (model i ynë net-new) + **anulim me PIN/arsye/aprovim + audit**.
- **RBAC i gjerë** (groups/rights/report/special/warehouse-location/POS-mobile) → matricë moderne per-feature.
- **Audit append-only**, **approval workflow**, **break-glass**, **config i versionuar/typed**.

## 4. Çfarë NUK migrohet (clean start)
- **Passwords, PIN, MAC/device IDs, secrets** → asnjëherë (users rikrijohen/resetohen).
- **Historiku i transaksioneve** → jo (vetëm saldo hapëse).
- **Audit logs** (>1.7M) → jo në prodhim (referencë).
- **Tabela TMP/working** + **stored procedures** + **schema legacy** → jo (referencë).

## 5. Çfarë mbetet vetëm REFERENCË
Logjika e procedurave, struktura legacy POS/KDS, audit-i historik, dhe sjellja e sistemit të vjetër — për të kuptuar operacionet + parity, **jo për kopjim 1:1**.

**⚠ KABI nuk inferohet nga PRO DATA:** PRO DATA është sistem **restoranti** (PLANET/RESORT). **Logjika e qumështores** (fermerë/Punki/QC/prodhim/COGS dairy) **NUK del nga backup-i** — vjen nga fabrika/on-site (SSOT §5 KABI). Parity-ja e PRO DATA vlen për **PLANET + bërthamën kontabël**, jo për operacionet specifike të KABI-t.

## 6. Validime të dizajnit tonë (nga evidence)
- **Barazimi/saldo per kamarier = net-new** (`tblDocPOSBarazimi*` bosh).
- **Anulim+PIN+arsye+aprovim+audit > PRO DATA** (që ruante fshirjet pa kontroll).
- **Floor-plan ka precedent** (`tblRestTavolinat.LokacioniX/Y`).
- **RBAC real shumë i gjerë** → justifikon matricën tonë per-feature.
- **Audit masiv (1.7M)** → justifikon audit append-only + retention/arkivim që në dizajn.

## 7. Lidhja me Data Model + Phase 0
- **Data Model** (`04_Data_Model_Draft`): të gjitha entitetet e konfirmuara janë shtuar (table_session, kitchen_ticket, kds_station, cash_shift, worker_settlement, manager_approval, cost_layer, price_history).
- **Accounting** (`03_Accounting_Compatibility_Matrix`): detajet 1:1 të librave/deklaratës.
- **RBAC** (`02_RBAC_Matrix`): transformimi i lejeve PRO DATA → matrica jonë.
- **PoC** (`01_PoC_Specs`): PoC-2 (accounting faturë→GL→ATK) + reconciliation POS↔fiskal↔shitje e mbështetur nga kjo evidence.

## 8. Deklaratë mbylljeje (Closure)
Backup-i është **shfrytëzuar mjaftueshëm; nxjerrja NDALET.** Por mbetet **golden reference baseline deri në lansim** — referencë kontrolli/parity në çdo fazë + UAT + cutover (jo "i mbaruar e harruar"). Inputet e mbetura për ta mbyllur 100% kontabilitetin **nuk** janë nga backup-i, por:
1. **Një muaj REAL i plotësuar** nga kontabilisti (blerje+shitje+Deklaratë) për validim end-to-end (SSOT pyetja #4).
2. Rakordimi përfundimtar i listave të sakta tabela/procedura me **manifest-et e Codex** (Codex).

## 9. Pikë për konfirmim
- **"RESORT"** shfaqet te audit-i (PLANET2/RESORT) — a është njësi/lokacion i tretë apo subjekt tjetër? Konfirmo (lidhet me modelin `business`/`business_unit`).
