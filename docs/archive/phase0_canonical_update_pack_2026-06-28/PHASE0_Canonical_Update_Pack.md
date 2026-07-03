# Phase 0 Canonical Update Pack — PRO DATA Evidence Closure

- Generated: `2026-06-28T22:17:11+02:00`
- Source pack: `outputs/prodata_evidence_closure_parity_matrix_2026-06-28/`
- Purpose: ready-to-merge updates for SSOT, Phase 0 docs, accounting matrix, data model, RBAC, PoCs and ADRs.
- Status: candidate update pack; do not treat as a separate competing source of truth after merge.

## Executive decision

The best next step after DDL + Wave 1 + Wave 2 + Wave 3 is **not another blind extraction wave**.
The best step is to promote the PRO DATA closure into canonical project documentation.

Decision to merge into SSOT:

> The PRO DATA backup is the long-running golden reference baseline for PLANET/shared accounting/POS/inventory/RBAC validation through development, UAT, parallel run, cutover and launch. It is not only a Phase 0 artifact.

This does not mean iData+ should clone PRO DATA's schema or old desktop behavior. It means PRO DATA is our evidence baseline for:

- accountant parity for PLANET;
- accounting-core posting logic;
- tax-book and VAT report validation;
- POS/payment/restaurant operational parity;
- RBAC/audit capability coverage;
- migration/cutover validation.

## Evidence status

- DDL/schema extraction: closed.
- Core reports and raw critical tables: closed.
- Posting logic deep dive: closed for accounting design.
- June 2026 GL reconciliation: closed as validation pattern.
- Supplementary Wave 1 accounting/costing/fiscal/setup: closed.
- Supplementary Wave 2 restaurant/POS/KDS: closed.
- Supplementary Wave 3 sanitized audit/RBAC/config: closed.
- Backup completeness audit: Priority A gaps = 0; Priority B gaps = 0.

## Evidence index

| Evidence area | Main file | Use | Status |
| --- | --- | --- | --- |
| Full DDL/schema | outputs/prodata_ddl_extracted_2026-06-28/EXTRACTION_MANIFEST.md | Table/view/procedure/function/trigger/index/constraint source for model review and procedure tracing | closed for baseline |
| Core reports/raw critical tables | outputs/prodata_reports_extracted_2026-06-27/EXTRACTION_MANIFEST.md | Libri i Shitjes/Blerjes, TVSH summary, GL monthly, master data, raw GL/sales/purchase/payment/stock tables | closed for baseline |
| Accounting/POS/Inventory module map | outputs/prodata_module_map_2026-06-28/PRODATA_Accounting_POS_Inventory_Module_Map.md | Module ownership and object mapping for accounting, POS, inventory, reports | closed for baseline |
| Posting logic deep dive | outputs/prodata_posting_deep_dive_2026-06-28/PRODATA_Posting_Logic_Deep_Dive.md | Procedure-level reference for SHV, BLV/BLS, PGD/PGH, POS payments and GL creation | closed for accounting design |
| June 2026 GL reconciliation | outputs/prodata_gl_reconciliation_2026-06-28/PRODATA_GL_Posting_Reconciliation_June2026.md | Document -> GL -> tax book -> VAT summary validation pattern | closed as validation template |
| Wave 1 accounting/costing/fiscal/setup | outputs/prodata_supplementary_extract_wave1_2026-06-28/EXTRACTION_MANIFEST.md | COGS, beginning balances, fiscal evidence, deleted/correction history, price history, setup tables | closed |
| Wave 2 restaurant/POS/KDS | outputs/prodata_supplementary_extract_wave2_restaurant_2026-06-28/PRODATA_Restaurant_POS_KDS_Operational_Review.md | PLANET tavolina, open orders, KDS queue, floor coordinates, restaurant procedures and POS/KDS domain design | closed |
| Wave 2 key procedure index | outputs/prodata_supplementary_extract_wave2_restaurant_2026-06-28/restaurant_key_procedure_index.csv | Practical procedure map for order_create/table_state/kds_queue/settlement/void/fiscal/reporting | closed |
| Wave 3 sanitized audit/RBAC/config | outputs/prodata_supplementary_extract_wave3_sanitized_2026-06-28/PRODATA_Audit_RBAC_Config_Sanitized_Review.md | Audit log, user activity, RBAC, user rights, config/reference design without credentials/secrets | closed sanitized |
| Completeness audit final | outputs/prodata_backup_completeness_audit_2026-06-28/PRODATA_Backup_Completeness_Audit.md | Proof that Priority A and B extraction/review gaps are now zero | A=0, B=0 |

## Phase usage matrix

| Phase | How backup is used | Required output | Go/No-Go use |
| --- | --- | --- | --- |
| Faza 0 — Foundation/PoCs/docs | SSOT/evidence closure, data model, ADRs, PoC criteria, SEF/accounting/POS risk reduction | Evidence Closure + Parity Matrix, feature status, decision gates, PoC references | No heavy coding until closure, SEF PoC path, accounting parity target and POS/KDS model are clear |
| Foundation — multi-company/RBAC/audit | Wave 3 RBAC/config/audit evidence informs clean permissions and append-only audit model | RBAC matrix, approval matrix, audit event taxonomy, support/break-glass rules | No user/password/PIN/mac migration; recreate users and validate permissions against old capability level |
| Accounting Core | Reports + posting deep dive + GL reconciliation define PLANET functional parity | GL, journal, AR/AP, cash/bank, VAT, Libri Shitjes/Blerjes, posting rules, period close | Debit=credit, subledger=GL, VAT books match parallel month, accountant sign-off required |
| Inventory/COGS/master data | Wave 1 costing, stock, price/history and master-data evidence guide item/stock/cost design | Item cards, price history, recipes/normatives, stock movements, COGS layers, corrections | Stock/COGS reports must reconcile for selected period before relying on financial statements |
| KABI vertical | Shared accounting core patterns apply; KABI-specific dairy rules remain separate decision gates | Farmer settlement, milk reception, lab/QC, production batches, LOT/expiry, van stock, mobile sales | Do not infer KABI VAT/formula/consignment/farmer tax from PLANET backup without accountant/factory/ATK |
| PLANET POS/KDS | Wave 2 gives tavolina/floor coordinates, open-order, KDS queue and restaurant procedure baseline | Floor plan, table sessions, orders/items, KDS tickets/stations, cash shifts, worker settlement | Must satisfy local hub/offline, KDS/printer backup, manager approvals and service-day rules |
| SEF/fiscalization | Fiscal evidence + POS/restaurant procedures inform fiscal outbox and report parity; official ATK GitHub remains protocol authority | Fiscal outbox, statuses, retry, QR/NIVF/FIC, offline 48h alarms, test transaction evidence | Backup is supporting evidence only; certification behavior must match official fiskalizimi repos and ATK testing |
| UAT / parallel run | Parallel month compares iData+ outputs to PRO DATA extracts and accountant files | Comparison workbook/report: sales, purchases, VAT, GL, stock, COGS, cash/bank, AR/AP, partner/article cards | Discrepancies must be explained, corrected, or signed off before go-live |
| Cutover / launch | Migration and validation source for PLANET master data, balances and selected history | Cutover checklist, opening balances, migration mapping, cleanup rules, rollback plan | No hidden second database; old users/secrets not migrated; final balances/reports approved |

## Parity requirements matrix

| Domain | Evidence | Implement in iData+ | Parity test | Phase | Boundary |
| --- | --- | --- | --- | --- | --- |
| Accounting / GL | Posting deep dive, GL reconciliation, raw GL, chart of accounts, document tables | Shared accounting core with double-entry GL, posting rules, document numbering, journals, period close | Document -> GL bridge, debit=credit, subledger=GL, month totals match PRO DATA/accountant-approved result | Accounting Core | KABI uses same core but dairy-specific posting rules stay decision-gated |
| VAT / ATK books | Libri Shitjes, Libri Blerjes, TVSH monthly summary, purchase book sample from accountant | Libri i Shitjes/Blerjes, VAT declaration support, K1/K2 and box mapping, exportable accountant reports | Parallel month VAT output/input and tax books compare to PRO DATA and official accountant declaration | Accounting Core / UAT | Derived VAT summary is not official tax truth until accountant confirms |
| Sales/POS payments | POS raw tables, POS payments, posting procedures, fiscalized docs | POS sales/payment handoff, cash/card/bank/POS terminal payments, fiscal queue, GL postings | POS totals, payment methods, fiscal status and GL postings reconcile per day/month | Accounting Core / PLANET POS | SEF protocol comes from official fiskalizimi repos, not legacy assumptions |
| Inventory / stock / COGS | Wave 1 COGS tables, stock movements, inventory adjustments, item master, recipes/normatives | Stock ledger, warehouses, recipes, FEFO/LOT where needed, COGS layers and correction/reversal logic | Stock movements and COGS for selected period reconcile to PRO DATA baseline where applicable | Inventory/COGS | KABI batch/yield/dairy costing requires factory/accountant confirmation |
| PLANET POS/KDS operations | Wave 2 tavolina, open orders, KDS queue, restaurant procedure index | Table sessions, real floor plan, partial send to kitchen/bar, KDS stations, timers, printer backup | Operational walkthrough: waiter order -> KDS/bar -> ready/delivered -> bill/payment -> fiscal/outbox | PLANET POS/KDS | Do not copy PRO DATA temp-table model; rebuild clean offline-first domain model |
| RBAC / approvals | Wave 3 user groups, group rights, report rights, special rights, location/warehouse rights | Module/entity/action permission matrix with read/create/update/correct-void/print/approve/export/admin | Old capability level represented without migrating old credentials; approval gates tested on risky actions | Foundation | No password/PIN/MAC migration; recreate/reset users |
| Audit trail | Wave 3 audit/user/activity coverage and sanitized samples | Append-only audit events with actor, device, offline timestamp, sync timestamp, reason, approval actor and before/after hashes | Every critical financial/POS/inventory/security action creates searchable audit record | Foundation / All modules | Legacy audit text is reference only; privacy-sensitive content remains redacted |
| Config/reference | Wave 3 system options/localization/reference tables | Typed, versioned config with secrets in secret manager and admin-change audit | Required business settings exist as explicit typed config, not hidden free text | Foundation / Module setup | Do not blindly import legacy hidden config values |
| Migration / cutover | Master data, opening balances, reports, raw tables, sanitized RBAC evidence | Migration transforms for allowed data only; cleanup and validation reports | Opening balances, master-data counts, selected history and reports signed off | Cutover | No user credentials/secrets; KABI old program not migrated unless explicitly decided |

## Merge order

1. Merge `SSOT_update_snippet.md` into the main SSOT.
2. Merge `03_Accounting_Compatibility_Matrix_update.md`.
3. Merge `04_Data_Model_Draft_update.md`.
4. Merge `02_RBAC_Matrix_update.md`.
5. Merge `01_PoC_Specs_update.md`.
6. Merge `05_Decision_Gates_ADR_update.md`.
7. Merge `Feature_Status_Register_update.md`.
8. Add `docs_merge_manifest.csv` to the raw-to-canonical cleanup checklist.

## Non-negotiable boundaries

- Do not infer KABI VAT, farmer tax, USAID milk formula, dairy costing, consignment, expiry/write-off policy or AUVK reporting solely from PLANET backup.
- Do not migrate PRO DATA passwords, PINs, MAC/device identifiers, tokens, certs or hidden secret values.
- Do not copy PRO DATA temp-table restaurant architecture 1:1. Use it to understand behavior, then rebuild as an offline-first domain model.
- Official ATK/SEF behavior comes from ATK test phase and `github.com/fiskalizimi`; PRO DATA is only supporting fiscal evidence.
- Derived VAT summaries must be compared to accountant files, not treated as official tax truth without sign-off.
