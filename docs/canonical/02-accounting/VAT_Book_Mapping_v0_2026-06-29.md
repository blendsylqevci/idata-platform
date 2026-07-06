# VAT Book Mapping v0

**Data:** 2026-06-29
**Status:** canonical draft / evidence-backed mapping / partially accountant-confirmed 2026-06-30
**Scope:** Libri i Shitjes, Libri i Blerjes, VAT summary për shared accounting core
**Burimi kryesor:** PRO DATA books + `prcRPTGRDLibriShitjetADM18` + `prcRPTGRDLibriBlerjetADM18` + June 2026 GL reconciliation.

Ky dokument përcakton si duhet iData+ ta ndërtojë Librin e Shitjes, Librin e Blerjes dhe VAT summary. Qëllimi nuk është të kopjojmë query-t e PRO DATA-s 1:1, por të ruajmë **funksionin kontabël dhe raportues** që kontabilisti pret.

> **Fermerë KABI — flat-rate 8%:** trajtimi i blerjes së qumështit nga fermerët (autofaturë nga KABI, box në Librin e Blerjes, deklarim mujor, TVSH e zbritshme) ndiqet te `../03-kabi/Accountant_Confirmation_TVSH_e_Sheshte_Fermere_KABI_2026-07-05.md` (bazë: UA 03/2015 neni 76; pret sign-off kontabilisti — ACC-006).

---

## 1. Vendimi kryesor

iData+ duhet të ketë një **Tax Book Engine** të ndarë nga GL posting engine.

- **Posting Engine** krijon GL, AR/AP, cash/bank, stock/COGS.
- **Tax Book Engine** krijon rreshtat për Librin e Shitjes/Blerjes nga dokumentet e postuara + tax rules.
- **VAT Summary** del nga tax-book entries, jo nga Excel manual.
- GL, tax books dhe VAT summary duhet të rakordohen në parallel month.

Kjo është në përputhje me PRO DATA: GL krijohet nga `prcPosto...GL`, ndërsa librat tatimorë ndërtohen nga dokumentet burimore, `tblTarifat`, partnerët, artikujt, document type dhe flags.

---

## 2. Evidence baseline

| Evidence | File / source | Çfarë na tregon |
| --- | --- | --- |
| Libri i Shitjes | `outputs/prodata_reports_extracted_2026-06-27/planet2_libri_shitjes_2026_06_test.psv` | Kolonat reale të shitjes: taxable 18/8, exempt, export, reverse-charge, notes, totals. |
| Libri i Blerjes | `outputs/prodata_reports_extracted_2026-06-27/planet2_libri_blerjes_2026_06_test.psv` | Kolonat reale të blerjes: import/vendor/local/investive/non-deductible/farmer/reverse-charge buckets. |
| VAT summary | `outputs/prodata_gl_reconciliation_2026-06-28/data/tvsh_monthly_summary_planet2_2026_06.csv` | Output-input monthly VAT calculation from extracted books. |
| Tax setup | `outputs/prodata_accounting_extract/05_tax_setup_PLANET_RESORT.json`, `06_tax_setup_PLANET2.json` | `T0`, `T8`, `T18`, `TL` and VAT accounts. |
| Procedures | `prcRPTGRDLibriShitjetADM18`, `prcRPTGRDLibriBlerjetADM18` | Classification logic and relevant flags. |
| Accountant sample | `‹mostra e kontabilistit — private-local›` previously reviewed | Official-style purchase-book export target and mixed-rate support. |

Important: reconstructed VAT summary is useful evidence, but **not official tax truth** until compared with the accountant’s official VAT declaration.

Accountant update 2026-06-30:

- June 2026 is **not yet an official validation month**, because VAT for June can be declared until 2026-07-20 and late June expenses/invoices may still arrive after month-end.
- For first official validation, use a **closed earlier month** where the accountant can provide Libri i Shitjes, Libri i Blerjes and the official VAT declaration.
- Mixed-rate purchase invoices are confirmed by accountant practice.
- Accountant confirms Libri i Shitjes and Libri i Blerjes are fully harmonized with ATK.
- PLANET sales are confirmed as 18% VAT by default because PLANET operates as restaurant/luxury business; this is a PLANET-specific configuration, not a global rule.

---

## 3. Tax codes v0

| PRO DATA code | Meaning | Percent | SEF provisional code | Status |
| --- | --- | ---: | --- | --- |
| `T0` | Pa tatim / 0% | 0% | `C` | usable, confirm report bucket per case |
| `T8` | TVSH e reduktuar | 8% | `D` | usable, product mapping must be confirmed |
| `T18` | TVSH standarde | 18% | `E` | usable |
| `TL` | TVSH e liruar | 0% | `A` provisional | confirm with accountant/ATK |

Accounts observed in PRO DATA:

| Purpose | Account |
| --- | --- |
| VAT Output / TVSH në shitje | `22000` |
| VAT Input / TVSH në blerje | `13000` |
| VAT in customs/transit | `21200` |

Rregull: tax code nuk mjafton vetë. Tax-book bucket varet edhe nga document type, partner status, product/type, local/import, deductible flags, investment flag, farmer flag dhe reverse-charge flags.

---

## 4. Tax-book data model

Ruajtja duhet të jetë normalizuar. Eksporti mund të dalë në format ATK/Excel.

### 4.1 `tax_book_entry`

| Field | Purpose |
| --- | --- |
| `id` | unique row |
| `business_id` | KABI / PLANET separation |
| `business_unit_id` | lokacion/shank/depo, if applicable |
| `fiscal_period_id` | reporting period |
| `book_type` | `sales` / `purchases` |
| `source_document_id` | invoice/POS/purchase/payment/credit note |
| `source_document_type` | SHV, POS, BLV, BLS, PGH, PGD, note, etc. |
| `document_number` | transaction / invoice no |
| `document_date` | tax-book date |
| `partner_id` | customer/supplier |
| `partner_name_snapshot` | report snapshot |
| `partner_nui_snapshot` | report snapshot |
| `partner_vat_no_snapshot` | report snapshot |
| `location_name_snapshot` | report snapshot |
| `reference` | fiscal/reference/mobile invoice no |
| `created_from_posting_event_id` | trace to posting |
| `status` | draft/posted/reversed/corrected/exported |

### 4.2 `tax_book_entry_bucket`

One document can create many buckets. This is required for mixed VAT invoices.

| Field | Purpose |
| --- | --- |
| `tax_book_entry_id` | parent |
| `bucket_code` | normalized bucket identifier |
| `tax_code_id` | T0/T8/T18/TL |
| `tax_rate` | 0/8/18 |
| `base_amount` | net amount |
| `vat_amount` | VAT amount |
| `gross_amount` | base + VAT |
| `deductible_amount` | purchase VAT deductible amount |
| `non_deductible_amount` | purchase non-deductible amount |
| `is_import` | import/DUD |
| `is_investment` | capital/investive |
| `is_farmer_flat_rate` | farmer bucket |
| `is_reverse_charge` | reverse-charge |
| `is_credit_note` | note/correction |
| `is_exempt` | exempt/no VAT |
| `is_export` | export/outside country |

**Design rule:** store normalized buckets internally; export pivots them into the columns required by ATK/accountant.

---

## 5. Libri i Shitjes mapping

### 5.1 Header / identity columns

| Export column | iData+ source |
| --- | --- |
| `Muaji` | fiscal period display |
| `Data` | document/reporting date |
| `Transaksioni` | document number / transaction |
| `Emri i bleresit` | partner snapshot or `Qytetar` |
| `NUI` | partner NUI snapshot |
| `No TVSH` | partner VAT no snapshot |
| `Lokacioni` | business unit / location |
| `Referenca` | document reference |
| `Fatura Mobile` | mobile/fiscal invoice reference |

### 5.2 Sales buckets

| Export bucket | Normalized bucket code | Rule v0 |
| --- | --- | --- |
| `#Shitjet totale` | `sales_total` | total sales net/base, all relevant sale buckets |
| `#Furnizimet e perjashtuara pa te drejte kreditimi` | `sales_exempt_no_credit` | VAT 0 with non-creditable/exempt classification |
| `Shitjet brenda vendit me ngarkese te kundert te TVSH-se` | `sales_domestic_reverse_charge` | reverse-charge domestic supply |
| `#Furnizimet e trajtuara si eksporte me te drejte kreditimi` | `sales_export_like_creditable` | VAT-excluded/export-like supply with right of credit |
| `$Shitjet e sherbimeve jashte vendit` | `sales_services_abroad` | services abroad / external subject |
| `#Exportet` | `sales_exports` | export bucket |
| `#Shitjet e tatueshme 18%` | `sales_taxable_18_base` | domestic taxable sale at 18% |
| `#TVSH e llogaritur / Shitjet 18%` | `sales_taxable_18_vat` | VAT output for 18% |
| `#Shitjet e tatueshme 8%` | `sales_taxable_8_base` | domestic taxable sale at 8% |
| `#TVSH e llogaritur / Shitjet 8%` | `sales_taxable_8_vat` | VAT output for 8% |
| `#Nota debitore e leshuar dhe nota kreditore e pranuar 18%` | `sales_note_18_base` | debit/credit note at 18% |
| `#TVSH e llogaritur / Note Debit - Krediti 18%` | `sales_note_18_vat` | VAT for note at 18% |
| `#Nota debitore e leshuar dhe nota kreditore e pranuar 8%` | `sales_note_8_base` | debit/credit note at 8% |
| `#TVSH e llogaritur / Note Debit - Krediti 8%` | `sales_note_8_vat` | VAT for note at 8% |
| `#Blerjet qe i nenshtrohen ngarkeses se kundert` | `sales_reverse_charge_purchase_base` | reverse-charge related bucket shown in sales report |
| `#TVSH e llogaritur / Blerjet qe i nenshtrohen ngarkeses se kundert` | `sales_reverse_charge_purchase_vat` | VAT for reverse-charge bucket |

### 5.3 Sales totals

| Export total | Rule v0 |
| --- | --- |
| `#Vlera totale pa TVSH` | sum of base buckets for the row |
| `#TVSH Totale` | sum of VAT output buckets for the row |
| `#Vlera Totale me TVSH` | base + VAT |

### 5.4 Sales classification inputs

Sales bucket placement should be driven by:

- tax code/rate;
- document type: SHV, POS, note/correction;
- partner `VATExcluded`;
- partner external/foreign flag;
- product/service type;
- reverse-charge flag;
- export/service abroad flag;
- location/business unit;
- fiscalization/mobile invoice reference.

### 5.5 PLANET sales VAT default — accountant-confirmed

For PLANET restaurant sales, accountant response 2026-06-30 confirms:

```text
PLANET sales default VAT = 18% / T18
```

Implementation rule:

- make this a **business-level/product-category configuration** for PLANET;
- do not hardcode it globally;
- KABI products remain a separate VAT mapping gate.

---

## 6. Libri i Blerjes mapping

### 6.1 Header / identity columns

| Export column | iData+ source |
| --- | --- |
| `Muaji` | fiscal period display |
| `Transaksioni` | document number / transaction |
| `Data` | document/reporting date |
| `No Fature/DUD` | supplier invoice no or customs DUD |
| `Furnitori` | supplier snapshot |
| `NUI` | supplier NUI snapshot |
| `Numri i TVSH-se` | supplier VAT no snapshot |
| `Lokacioni` | business unit / warehouse/location |

### 6.2 Purchase buckets

| Export bucket | Normalized bucket code | Rule v0 |
| --- | --- | --- |
| `#Blerjet dhe importet pa TVSH` | `purchase_no_vat_base` | no VAT, non-investive |
| `#Blerjet dhe importet investive pa TVSH` | `purchase_no_vat_investment_base` | no VAT, investment/capital |
| `#Blerjet dhe importet me TVSH jo te zbritshme` | `purchase_non_deductible_vat_base` | taxable purchase where VAT not deductible |
| `#Blerjet dhe importet investive me TVSH jo te zbritshme` | `purchase_non_deductible_vat_investment_base` | investment purchase with non-deductible VAT |
| `#Importet e tatueshme 18%` | `purchase_import_18_base` | import/DUD, 18%, non-investive |
| `#TVSH e zbritshme / Importet 18%` | `purchase_import_18_deductible_vat` | deductible VAT for import 18% |
| `#Importet e tatueshme 8%` | `purchase_import_8_base` | import/DUD, 8%, non-investive |
| `#TVSH e zbritshme / Importet 8%` | `purchase_import_8_deductible_vat` | deductible VAT for import 8% |
| `#Importet investive te tatueshme 18%` | `purchase_import_investment_18_base` | import/DUD, investment, 18% |
| `#TVSH e zbritshme / Investive 18%` | `purchase_import_investment_18_deductible_vat` | deductible VAT for investment import 18% |
| `#Importet investive te tatueshme 8%` | `purchase_import_investment_8_base` | import/DUD, investment, 8% |
| `#TVSH e zbritshme / Investive 8%` | `purchase_import_investment_8_deductible_vat` | deductible VAT for investment import 8% |
| `#Blerjet e tatueshme vendore 18%` | `purchase_local_18_base` | local taxable purchase 18% |
| `#TVSH e zbritshme / Blerjet Vendore 18%` | `purchase_local_18_deductible_vat` | deductible VAT local 18% |
| `#Blerjet e tatueshme vendore 8%` | `purchase_local_8_base` | local taxable purchase 8% |
| `#TVSH e zbritshme / Blerjet Vendore 8%` | `purchase_local_8_deductible_vat` | deductible VAT local 8% |
| `#Blerjet investive te tatueshme vendore 18%` | `purchase_local_investment_18_base` | local investment purchase 18% |
| `#TVSH e zbritshme / Investive Vendore 18%` | `purchase_local_investment_18_deductible_vat` | deductible VAT local investment 18% |
| `#Blerjet investive te tatueshme vendore 8%` | `purchase_local_investment_8_base` | local investment purchase 8% |
| `#TVSH e zbritshme / Investive Vendore 8%` | `purchase_local_investment_8_deductible_vat` | deductible VAT local investment 8% |
| `#Blerjet nga fermeret vendore 8%` | `purchase_farmer_flat_rate_8_base` | farmer flat-rate bucket, KABI critical gate |
| `TVSH e zbritshme / Blerjet nga fermeret vendore 8%` | `purchase_farmer_flat_rate_8_deductible_vat` | farmer flat-rate deductible VAT, confirm with accountant/ATK |
| `#Nota debitore e pranuar dhe nota kreditore e leshuar 18%` | `purchase_note_18_base` | purchase debit/credit note 18% |
| `#TVSH e zbritshme / Note Debiti 18%` | `purchase_note_18_deductible_vat` | VAT for purchase note 18% |
| `#Nota debitore e pranuar dhe nota kreditore e leshuar 8%` | `purchase_note_8_base` | purchase debit/credit note 8% |
| `#TVSH e zbritshme / Note Debiti 8%` | `purchase_note_8_deductible_vat` | VAT for purchase note 8% |
| `#Kreditimi i TVSH-se ne lidhje me ngarkesen e kundert` | `purchase_reverse_charge_credit_base` | reverse-charge credit base |
| `#TVSH e llogaritur / Kreditimi i TVSH-se ne lidhje me ngarkesen e kundert` | `purchase_reverse_charge_credit_vat` | VAT for reverse-charge credit |

### 6.3 Purchase totals

| Export total | Rule v0 |
| --- | --- |
| `#Vlera totale pa TVSH` | sum base buckets |
| `#TVSH Totale` | sum deductible/calculated VAT buckets for report row |
| `#Vlera Totale me TVSH` | base + VAT |

### 6.4 Purchase classification inputs

Purchase bucket placement should be driven by:

- tax code/rate;
- document type: BLV/BLS/BLI/BLN/notes;
- supplier taxpayer/VAT status;
- `is_import` / DUD;
- `is_investment` / capital item classification;
- deductible vs non-deductible VAT flag;
- `KoeficientiTVSH`;
- `TVSHKapitalizuese`;
- `TVSHKalkuluese`;
- `TVSHJoeZbritshme`;
- farmer flag;
- reverse-charge flag;
- additional costs/import costs;
- location/business unit.

---

## 7. Mixed VAT rule

Mixed VAT invoices are mandatory.

Example from real PLANET purchase book:

- same purchase invoice can have 18% taxable local purchases and 8% taxable local purchases;
- report row must show both buckets;
- AP/GL can have one supplier gross total, while tax-book buckets split base/VAT.

Implementation rule:

```text
One document -> one tax_book_entry header -> many tax_book_entry_bucket rows.
```

Never assume one document = one VAT rate.

Accountant response 2026-06-30 confirms this behavior in practice: purchase invoice items can carry different VAT rates, and the applicable VAT rate is selected per item.

---

## 8. VAT summary v0

For each business and fiscal period:

| Field | Formula v0 |
| --- | --- |
| `sales_net` | sum sales base buckets |
| `sales_vat` | sum VAT output buckets |
| `sales_gross` | sales_net + sales_vat |
| `purchase_net` | sum purchase base buckets |
| `purchase_vat` | sum deductible/calculated input VAT buckets |
| `purchase_gross` | purchase_net + purchase_vat |
| `vat_net_payable_output_minus_input` | sales_vat - purchase_vat |

June 2026 PLANET2 evidence:

| Metric | Value |
| --- | ---: |
| Sales rows | X,XXX |
| Sales net | XX,XXX.XX |
| Sales VAT | XX,XXX.XX |
| Sales gross | XX,XXX.XX |
| Purchase rows | 188 |
| Purchase net | XX,XXX.XX |
| Purchase VAT | XX,XXX.XX |
| Purchase gross | XX,XXX.XX |
| VAT output-input | -XX,XXX.XX |

Negative means draft VAT credit/refund position for that reconstructed month. It remains useful as technical evidence, but accountant response 2026-06-30 confirms that June 2026 is not yet declared and cannot be official sign-off evidence until after the legal declaration window. First official validation should use an already-declared earlier month, then repeat for June after declaration.

---

## 9. Validation rules

VAT Book Mapping v0 is accepted only when:

1. Libri i Shitjes export matches PRO DATA for selected PLANET month.
2. Libri i Blerjes export matches PRO DATA/accountant sample for selected month.
3. Mixed 18% + 8% purchase invoice maps to correct columns.
4. VAT summary equals sales VAT - purchase VAT from tax-book entries.
5. GL VAT accounts reconcile to VAT books with explained differences.
6. Corrections/notes appear in note buckets, not silent edits.
7. Closed period prevents silent tax-book changes.
8. Every tax-book row links to source document and posting event.
9. Accountant validates official VAT declaration comparison.
10. KABI product/farmer VAT rules remain gated until confirmed.

---

## 10. Open gates

| Gate | Why it matters | Owner |
| --- | --- | --- |
| Official VAT declaration comparison | Reconstructed summary is not official truth | Accountant |
| KABI product VAT map | Dairy products may differ by product/package | Accountant/ATK |
| Farmer flat-rate / box 51 treatment | Critical for milk purchases from farmers | Accountant/ATK |
| `TL -> A` exempt mapping | SEF/tax classification must be exact | Accountant/ATK |
| Non-deductible/capitalized VAT | Affects purchase book and GL | Accountant |
| Reverse-charge handling | Affects both books | Accountant |
| Note/correction reporting period | Legal/accounting treatment | Accountant |
| Rounding rule | Row vs invoice vs report totals | Accountant/ATK |
| Import/customs flow | Needs real examples if used | Accountant/client |

---

## 11. Implementation notes

1. Use PostgreSQL `numeric`, not float.
2. Use TypeScript decimal library for calculation, never JS `number` for money.
3. Keep raw document totals, tax-book bucket totals, GL totals and export rounded totals separately traceable.
4. Tax-book rows should be immutable after export; corrections create new rows/statuses.
5. Export format can mirror PRO DATA/ATK style, but internal model must remain normalized.
6. Per-business configuration is required; do not hardcode KABI/PLANET differences.
7. Every bucket must have an explanation: “why this bucket, why this amount”.

---

## 12. Link with posting fixtures

These fixtures must create tax-book entries:

- FX-001 SHV01 sale 18%
- FX-002 SHV04 restaurant sale
- FX-003 POS sale cash/card
- FX-004 BLV purchase 18%
- FX-005 mixed VAT purchase 18% + 8%
- FX-006 BLS expense purchase
- FX-009 credit note/correction
- FX-014 Finance Operations handover, indirectly through cash/card reconciliation

The next implementation artifact should be `VAT_Book_Test_Fixtures_v0`, or these VAT cases can be added as a section inside the accounting test suite.

> **Shënim konfidencialiteti:** shifrat reale të klientit janë hequr me qëllim (rregullore privacy). Vlerat reale ruhen te `evidence-private-local/` (jashtë git). Këtu mbetet vetëm struktura/logjika e rakordimit dhe hartat e kutive të deklaratës.
