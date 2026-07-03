# Accounting Posting Rules v0

**Data:** 2026-06-29
**Status:** canonical draft / evidence-backed design / partially accountant-confirmed 2026-06-30
**Scope:** shared accounting core for PLANET + KABI
**Burimi kryesor:** PRO DATA posting deep-dive + GL reconciliation, jo supozim.

Ky dokument e kthen evidencën nga backup-i i PRO DATA-s në rregulla të qarta për motorin e postimit të iData+. Nuk është ende konfigurim final i kontabilistit; është **v0 për implementim dhe testim**, që më pas validon kontabilisti me parallel month.

---

## 1. Vendimi kryesor

iData+ duhet të ketë dy motorë të ndarë, por të rakorduar:

1. **Posting Engine** — merr dokumentin burimor dhe krijon GL / subledger / stock-cost entries.
2. **Tax Book Engine** — ndërton Librin e Shitjes, Librin e Blerjes dhe VAT summary nga dokumentet + tax rules.

PRO DATA e konfirmon këtë ndarje: procedurat `prcPosto...GL` krijojnë `tblGeneralLedger`, ndërsa `prcRPTGRDLibriShitjetADM18` dhe `prcRPTGRDLibriBlerjetADM18` raportojnë librat tatimorë nga dokumentet/tax rules.

Rregulli ynë: **nuk kopjojmë stored procedures 1:1**, por rindërtojmë një domain model të pastër, të testueshëm dhe të konfigurueshëm.

---

## 2. Invariantet e postimit

Këto janë absolute:

1. Çdo `journal_entry` duhet të ketë `debit = credit`.
2. Çdo rresht GL duhet të ketë `business_id`.
3. Çdo postim duhet të lidhet me `source_document_id` ose `manual_journal_reason_id`.
4. Çdo postim duhet të ketë `posting_rule_version_id`.
5. Çdo postim duhet të ketë `idempotency_key`, sidomos offline/POS/van-sales.
6. Dokumenti financiar nuk fshihet; korrigjohet me reversal, credit note, cancel/correct.
7. `financial posting` dhe `cost posting` mund të jenë evente të ndara, por duhet të lidhen me të njëjtin dokument/event burimor.
8. `tax_book_entry` nuk duhet të jetë Excel-only; gjenerohet nga sistemi dhe mund të eksportohet.
9. Mbyllja e periudhës bllokon backdated changes pa reopen approval.
10. Nuk përdoret JS `number` për para; accounting ruhet me `numeric/decimal`.

---

## 3. Modeli minimal i të dhënave

Këto entitete duhet të ekzistojnë ose të projektohen si pjesë e accounting core:

| Entity | Qëllimi |
| --- | --- |
| `posting_rule` | Header i rregullës: business/template, document type, effective date, status. |
| `posting_rule_line` | Debit/kredit line template: account source, amount formula, subledger effect. |
| `posting_event` | Event i idempotent që përfaqëson dokumentin ose aksionin që po postohet. |
| `journal_entry` | Header i GL entry. |
| `journal_line` | Debit/kredit lines. |
| `tax_book_entry` | Rreshti i Librit të Shitjes/Blerjes. |
| `partner_ledger_entry` | AR/AP/customer/supplier card movement. |
| `open_item` | Faturë/pagese e hapur për allocation. |
| `payment_allocation` | Lidhja pagesë ↔ faturë. |
| `inventory_ledger_entry` | Lëvizje sasie stoku. |
| `inventory_cost_layer` | Kosto / COGS / layer. |
| `posting_audit_event` | Audit për postim, anulim, reversal, reopen, manual override. |

---

## 4. Posting rules v0

### 4.1 `SHV_SALE_INVOICE` — Faturë shitje normale

**PRO DATA evidence:** `prcPostoShitjetGL`, `SHGL1`, `tblDocShitjetMaster`, `tblDocShitjetDetalet`, `tblGeneralLedger`.

**Kur aktivizohet:** kur një faturë shitje postohet.

**GL shape:**

| Line | Debit/Kredit | Account source | Amount source |
| --- | --- | --- | --- |
| AR / Cash / Bank | Debit | customer/payment config | gross total |
| Revenue | Kredit | item/revenue account mapping | net line amount |
| VAT Output | Kredit | tax code account mapping | VAT amount |
| Discount / withholding, if any | Debit/Kredit | config | calculated adjustment |

**Subledger effects:**

- krijon AR open item nëse shitja është me afat;
- krijon cash/bank settlement nëse është e paguar menjëherë;
- krijon `tax_book_entry` për Librin e Shitjes;
- nëse artikulli ka stock/cost impact, ngre event të veçantë `INVENTORY_COGS_POSTING`.

**Rregull i rëndësishëm:** COGS nuk futet si “rresht aksidental” brenda shitjes; krijohet nga cost engine, por lidhet me të njëjtin source document.

---

### 4.2 `SHV_POS_SALE` — POS / restaurant fiscal sale

**PRO DATA evidence:** `prcPostoPointofSaleGL`, `prcPOSPaketoDokumentet`, `SHGL2`, POS all-time bridge.

**Kur aktivizohet:** kur POS sale mbyllet/fiskalizohet ose kur local hub bën batch posting.

**GL shape:**

| Line | Debit/Kredit | Account source | Amount source |
| --- | --- | --- | --- |
| Cash / POS terminal / card clearing | Debit | payment method / terminal mapping | gross paid |
| Revenue | Kredit | item/category mapping | net |
| VAT Output | Kredit | tax code mapping | VAT |

**Trace fields të detyrueshme për iData+:**

- `local_document_id`
- `cloud_document_id`
- `business_day_id`
- `device_id`
- `cashier_or_waiter_id`
- `pos_terminal_id`, nëse pagesa është kartelë
- `posting_batch_id`, nëse postohet i paketuar
- `fiscal_transaction_id` / SEF reference

**Rregull:** POS mund të ketë posting individual ose batch/master posting, por trace nuk guxon të humbet.

**Accountant confirmation 2026-06-30 — PLANET sales VAT:**

- PLANET restaurant sales use **18% VAT / `T18`** by default.
- This applies to PLANET/POS/restaurant sales as a **business configuration**.
- Do not hardcode this globally: KABI product VAT remains a separate open gate.
- Sales buckets for 8%/exempt remain available in the shared engine for other businesses/use-cases, but PLANET’s active sales config should route normal restaurant sales to 18%.

---

### 4.3 `BLV_PURCHASE_INVOICE` — Faturë blerje mall/stok

**PRO DATA evidence:** `prcPostoBlerjetImportGL`, `BLGL1`, `tblDocBlerjetMaster`, `tblDocBlerjetDetalet`, `tblDocBlerjetShpenzimetTjera`.

**Kur aktivizohet:** kur fatura hyrëse aprovohet për postim.

**GL shape:**

| Line | Debit/Kredit | Account source | Amount source |
| --- | --- | --- | --- |
| Stock / inventory | Debit | item/inventory account mapping | net stock value |
| VAT Input | Debit | tax code account mapping | deductible VAT |
| Expense/additional cost | Debit | expense/import/additional-cost config | additional cost |
| Non-deductible/capitalized VAT | Debit | config | non-deductible VAT, if applicable |
| AP / Supplier | Kredit | supplier payable account | gross total |

**Subledger effects:**

- supplier open item/AP;
- stock receipt/value layer, nëse blerja hyn në stok;
- `tax_book_entry` për Librin e Blerjes;
- support për mixed VAT rates në të njëjtën faturë.

**Rregull:** `KoeficientiTVSH`, `TVSHKapitalizuese`, `TVSHKalkuluese`, `TVSHJoeZbritshme`, import/customs dhe farmer buckets nuk duhen hardcoded; janë tax/accounting rule configuration.

**Accountant confirmation 2026-06-30 — mixed VAT purchases:**

- A single supplier invoice can contain item lines with different VAT rates, e.g. 18% and 8%.
- VAT rate is selected per item/line.
- Posting and tax-book generation must not assume one VAT rate per invoice.

---

### 4.4 `BLS_SERVICE_EXPENSE_PURCHASE` — Blerje shërbimi/shpenzimi

**PRO DATA evidence:** same purchase posting procedure family, BLS/BLV variants.

**GL shape:**

| Line | Debit/Kredit | Account source | Amount source |
| --- | --- | --- | --- |
| Expense | Debit | expense category / item account | net |
| VAT Input | Debit | tax code mapping | deductible VAT |
| AP / Cash / Bank | Kredit | supplier/payment config | gross |

**Rregull:** Nëse nuk prek stokun, nuk krijon inventory ledger. Nëse ka faturë me foto/PDF, attachment lidhet me source document, jo me GL direkt.

---

### 4.5 `PGH_RECEIPT_FROM_CUSTOMER` — Arkëtim nga klienti

**PRO DATA evidence:** `prcPostoPagesatGL`, `PGGL1`, `tblDocPagesatMaster`, `tblDocPagesatDetalet`.

**GL shape:**

| Line | Debit/Kredit | Account source | Amount source |
| --- | --- | --- | --- |
| Cash / Bank / POS terminal | Debit | payment method mapping | received amount |
| AR / Customer | Kredit | customer receivable account | allocated amount |

**Subledger effects:**

- payment allocation te një ose disa fatura;
- customer card update;
- finance operations handover link kur pagesa vjen nga cash/card/barazim.

**Rregull:** Payment pa allocation lejohet vetëm si advance/unapplied payment me status të qartë.

---

### 4.6 `PGD_PAYMENT_TO_SUPPLIER` — Pagesë te furnitori

**PRO DATA evidence:** `prcPostoPagesatGL`, `PGGL1`.

**GL shape:**

| Line | Debit/Kredit | Account source | Amount source |
| --- | --- | --- | --- |
| AP / Supplier | Debit | supplier payable account | allocated amount |
| Cash / Bank | Kredit | payment method/bank account | paid amount |

**Subledger effects:**

- supplier card update;
- supplier invoice allocation;
- cash/bank journal update;
- optional finance operations approval if cash-office payment.

---

### 4.7 `ADVANCE_PAYMENT_OR_RECEIPT`

**PRO DATA evidence:** `prcPostoPagesatAvanseGL`, VAT-book procedures reference advances.

**Përdorim:** pagesë/arkëtim paraprak që ende nuk është e lidhur me faturë finale.

**Rregull v0:** modelojmë si open advance item, jo si “pagesë normale”. VAT behavior duhet të konfirmohet me kontabilistin sipas rastit.

**Open gate:** saktësimi i TVSH-së për avanse dhe përdorimi te PLANET/KABI.

---

### 4.8 `INVENTORY_COGS_POSTING`

**PRO DATA evidence:** `prcCalculateBADCost`, `prcCalculateBADCostTotal`, `CostingEntries=1`, `TXN` blank.

**Kur aktivizohet:** pas shitjes ose pas cost recalculation kur artikulli ka stock/cost effect.

**GL shape:**

| Line | Debit/Kredit | Account source | Amount source |
| --- | --- | --- | --- |
| COGS | Debit | item/category cost account | calculated cost |
| Inventory | Kredit | item/inventory account | calculated cost |

**Rregull:** Kostoja duhet të jetë e riprodhueshme dhe e shpjegueshme: nga cost layer/weighted average/batch costing, jo nga vlerë manuale pa gjurmë.

---

### 4.9 `INVENTORY_ADJUSTMENT`

**PRO DATA evidence:** raw inventory adjustment tables + Wave 1.

**Kur aktivizohet:** korrigjim stoku me arsye dhe aprovim.

**GL shape:**

| Line | Debit/Kredit | Account source | Amount source |
| --- | --- | --- | --- |
| Inventory | Debit/Kredit | inventory account | adjustment value |
| Inventory adjustment gain/loss | Debit/Kredit | config | balancing value |

**Rregull:** çdo adjustment kërkon reason, user, approval dhe audit.

---

### 4.10 `CREDIT_NOTE_CANCEL_CORRECTION`

**PRO DATA evidence:** document DEL/deleted/correction tables, sales/purchase/payment procedures, tax-book buckets for notes.

**Kur aktivizohet:** anulim, kthim, notë kredit/debit, reversal.

**Rregull:** nuk modifikohet posting-u historik pa gjurmë; krijohet dokument korrektues që e shpjegon ndryshimin.

**Efektet mund të përfshijnë:**

- reverse revenue/VAT/AR;
- reverse AP/VAT input/stock;
- reverse or adjust COGS;
- update tax books in the correct reporting period according to accounting rule.

---

### 4.11 `MANUAL_JOURNAL`

**Përdorim:** vetëm për raste që nuk kanë dokument standard.

**Rregulla:**

- permission i veçantë;
- reason i detyrueshëm;
- debit=credit;
- audit;
- attachment optional;
- period lock rules;
- nuk përdoret për të anashkaluar dokumentet standarde.

---

## 5. Account source resolution

Çdo `posting_rule_line` duhet ta marrë konton nga një burim i qartë:

| Source type | Shembull |
| --- | --- |
| `fixed_account` | TVSH output default, rounding difference. |
| `tax_code_account` | VAT input/output nga tax code. |
| `item_account` | revenue/inventory/COGS nga artikulli/kategoria. |
| `partner_account` | AR/AP nga customer/supplier config. |
| `payment_method_account` | cash, card clearing, bank, POS terminal. |
| `warehouse_account` | inventory account by warehouse/business unit, nëse nevojitet. |
| `expense_category_account` | shpenzime furnitorësh/operacionale. |
| `system_option_account` | discounts, withholding, rounding, exchange difference. |

Nëse konto nuk gjendet, postimi duhet të bllokohet me gabim të qartë: “Mungon account mapping për X”.

---

## 6. Tax-book rule model

Librat tatimorë nuk dalin vetëm nga GL. Duhet të lexojnë dokumentet e postuara + tax classification.

Minimum fields:

- `tax_code`
- `tax_rate`
- `tax_bucket`
- `deductible_flag`
- `non_deductible_flag`
- `investment_flag`
- `import_flag`
- `local_purchase_flag`
- `farmer_flat_rate_flag`
- `reverse_charge_flag`
- `exempt_flag`
- `credit_note_flag`
- `business_id`
- `reporting_period_id`

Për PLANET kemi evidence të fortë nga Libri i Shitjes/Blerjes. Për KABI, product VAT mapping dhe farmer treatment mbeten gate me kontabilistin/ATK.

---

## 7. Open gates para finalizimit

| Gate | Kush e mbyll |
| --- | --- |
| Exact account mapping për iData+ COA | Kontabilisti + iCode |
| Product VAT mapping për KABI | Kontabilisti/ATK |
| Farmer flat-rate / blerje nga fermerët | Kontabilisti/ATK |
| Non-deductible/capitalized VAT rules | Kontabilisti |
| POS terminal clearing accounts | PLANET finance + kontabilisti |
| COGS method final | Kontabilisti + owner |
| Opening balances | Kontabilisti + klienti |
| Bank reconciliation MVP scope | Kontabilisti + finance operator |
| Advance VAT treatment | Kontabilisti |

---

## 8. Acceptance criteria

Posting Rules v0 quhet i pranueshëm vetëm kur:

1. test fixtures kalojnë për SHV, POS, BLV/BLS, PGH/PGD;
2. çdo fixture prodhon debit=credit;
3. çdo GL line ka `business_id`, `source_document_id`, `posting_rule_version_id`;
4. every posted document is idempotent;
5. sales/purchase fixtures krijojnë tax-book entries;
6. payment fixtures krijojnë allocation;
7. COGS fixtures janë të ndara nga financial posting por të lidhura me source document;
8. period-close fixture bllokon backdated edit;
9. reports reconcile: document totals → GL → tax books;
10. accountant review pack mund të lexojë “pse kjo konto / pse kjo shumë”.

---

## 9. Hapi tjetër

Ky dokument duhet përdorur bashkë me:

- `Accounting_Posting_Test_Fixtures_v0_2026-06-29.md`
- `Accounting_10_10_Hardening_Checklist_2026-06-29.md`
- `PRODATA_Evidence_Traceability_Matrix_2026-06-29.md`

Në development, hapi i parë nuk është “UI i kontabilitetit”; hapi i parë është një **posting simulator** që merr fixture document dhe kthen expected GL/tax-book/subledger lines.
