#!/usr/bin/env python3
from __future__ import annotations

import csv
import shutil
from datetime import datetime
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATE = "2026-06-28"
OUT = ROOT / "outputs" / f"phase0_canonical_update_pack_{DATE}"
CLOSURE = ROOT / "outputs" / f"prodata_evidence_closure_parity_matrix_{DATE}"


def read_csv(name: str) -> list[dict[str, str]]:
    path = CLOSURE / name
    with path.open(newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def md_table(rows: list[dict[str, str]], headers: list[str]) -> str:
    if not rows:
        return "_No rows._"
    lines = [
        "| " + " | ".join(headers) + " |",
        "| " + " | ".join(["---"] * len(headers)) + " |",
    ]
    for row in rows:
        lines.append("| " + " | ".join(row.get(h, "").replace("\n", " ") for h in headers) + " |")
    return "\n".join(lines)


def write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text.rstrip() + "\n", encoding="utf-8")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)

    evidence = read_csv("prodata_evidence_index.csv")
    phase_usage = read_csv("prodata_phase_usage_matrix.csv")
    parity = read_csv("prodata_parity_requirements.csv")

    generated_at = datetime.now().astimezone().isoformat(timespec="seconds")

    main_doc = f"""# Phase 0 Canonical Update Pack — PRO DATA Evidence Closure

- Generated: `{generated_at}`
- Source pack: `outputs/prodata_evidence_closure_parity_matrix_{DATE}/`
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

{md_table(evidence, ["Evidence area", "Main file", "Use", "Status"])}

## Phase usage matrix

{md_table(phase_usage, ["Phase", "How backup is used", "Required output", "Go/No-Go use"])}

## Parity requirements matrix

{md_table(parity, ["Domain", "Evidence", "Implement in iData+", "Parity test", "Phase", "Boundary"])}

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
"""

    ssot = """# SSOT Update Snippet — PRO DATA Golden Baseline

## Add to SSOT: Evidence baseline decision

**Decision:** PRO DATA backup is the golden reference baseline for PLANET and shared iData+ accounting/POS/inventory/RBAC validation from Phase 0 through launch.

**Scope of use:**

- Phase 0: documentation, data model, ADRs, PoC criteria.
- Accounting Core: posting logic, GL, AR/AP, cash/bank, VAT books, period close.
- Inventory/COGS: item cards, warehouses, stock movements, corrections, price/cost history.
- PLANET POS/KDS: table sessions, real floor plan, order flow, KDS queue, cash shift and payment behavior.
- RBAC/Audit: permission surface, approval gates, append-only audit taxonomy.
- UAT/Parallel Run: compare iData+ reports against PRO DATA/accountant-approved outputs.
- Cutover/Launch: validate migration, opening balances, selected history and final reports.

**Rule:** use PRO DATA as source-faithful evidence and parity baseline, not as a schema to clone blindly.

## Add to SSOT: Closed evidence

- DDL/schema: closed.
- Module map: closed.
- Posting logic deep dive: closed.
- June 2026 GL reconciliation: closed as validation pattern.
- Wave 1 accounting/costing/fiscal/setup: closed.
- Wave 2 restaurant/POS/KDS: closed.
- Wave 3 sanitized audit/RBAC/config: closed.
- Completeness audit: Priority A = 0, Priority B = 0.

## Add to SSOT: Still open / decision-gated

- KABI product VAT mapping.
- Farmer VAT/tax treatment.
- USAID milk price formula.
- KABI dairy batch/yield/loss/by-product/catch-weight final accounting.
- Consignment/returns/expiry/write-off policy.
- Final PLANET floor plan, exact KDS placement/screen count, printer routing and hardware inventory.
- Official monthly VAT declaration comparison/sign-off.
- ATK/SEF certification behavior from official test phase.
"""

    accounting = """# 03 Accounting Compatibility Matrix — Update from PRO DATA Closure

## Add section: PLANET accountant parity target

The accounting core must make the PLANET accountant feel no loss versus PRO DATA for the core accounting workflow.

Minimum parity domains:

1. Chart of accounts and account grouping.
2. Fiscal years and accounting periods.
3. Document types and numbering.
4. Sales invoices, POS sales and fiscalized documents.
5. Purchase invoices and purchase book.
6. Payments and cash/bank movements.
7. AR/AP partner cards and aging.
8. GL journals and journal lines.
9. VAT/tax codes and VAT books.
10. VAT summary/declaration support.
11. Inventory movements and stock valuation links.
12. COGS/costing evidence where applicable.
13. Corrections, reversals and deleted/cancelled document evidence.

## Add section: Required parity tests

- Document -> GL bridge for SHV, BLV/BLS, PGD/PGH, POS payments.
- Debit = credit invariant for every posted document.
- Subledger totals = GL control accounts.
- Sales book totals match source documents for selected period.
- Purchase book totals match source documents and accountant sample.
- VAT input/output summary matches PRO DATA/accountant-approved result for parallel month.
- Cash/card/bank/POS terminal totals reconcile by business day.
- COGS/stock movements reconcile for selected period before relying on financial statements.

## Add section: Evidence references

- `outputs/prodata_posting_deep_dive_2026-06-28/PRODATA_Posting_Logic_Deep_Dive.md`
- `outputs/prodata_gl_reconciliation_2026-06-28/PRODATA_GL_Posting_Reconciliation_June2026.md`
- `outputs/prodata_reports_extracted_2026-06-27/EXTRACTION_MANIFEST.md`
- `outputs/prodata_supplementary_extract_wave1_2026-06-28/EXTRACTION_MANIFEST.md`
- `outputs/prodata_evidence_closure_parity_matrix_2026-06-28/PRODATA_Evidence_Closure_Parity_Matrix_v1.md`

## Boundary

KABI uses the same accounting core, but KABI-specific dairy posting rules remain decision-gated until confirmed by KABI/accountant/ATK/factory.
"""

    data_model = """# 04 Data Model Draft — Update from PRO DATA Closure

## Add / confirm shared accounting entities

- `business_group`
- `business`
- `branch`
- `cost_center`
- `fiscal_year`
- `accounting_period`
- `account`
- `journal_entry`
- `journal_line`
- `document_type`
- `posting_rule`
- `posting_run`
- `tax_code`
- `tax_book_entry`
- `vat_declaration_snapshot`
- `partner`
- `partner_card_entry`
- `payment`
- `payment_allocation`
- `cash_register`
- `bank_account`
- `bank_reconciliation`

## Add / confirm inventory and costing entities

- `item`
- `item_category`
- `item_unit`
- `item_price_history`
- `warehouse`
- `warehouse_location`
- `stock_movement`
- `stock_movement_line`
- `stock_adjustment`
- `inventory_count`
- `recipe`
- `recipe_line`
- `cost_layer`
- `stock_valuation_snapshot`
- `opening_balance`

## Add / confirm PLANET POS/KDS entities

- `service_day`
- `restaurant_zone`
- `restaurant_floor_plan`
- `restaurant_table`
- `table_session`
- `order`
- `order_item`
- `order_send_batch`
- `kds_station`
- `kitchen_ticket`
- `kitchen_ticket_status_event`
- `cash_shift`
- `worker_settlement`
- `payment_terminal`
- `manager_approval`
- `void_reason`
- `printer_route`

## Add / confirm RBAC/audit/config entities

- `role`
- `permission`
- `role_permission`
- `user_role`
- `user_location_access`
- `user_warehouse_access`
- `approval_policy`
- `approval_request`
- `audit_event`
- `break_glass_session`
- `config_setting`
- `config_change_event`

## Add / confirm fiscal/SEF entities

- `fiscal_device`
- `fiscal_pos`
- `fiscal_outbox`
- `fiscal_status_event`
- `fiscal_payload_archive`
- `offline_fiscal_ticket`

## Important design rules

- Keep write path through backend/domain layer.
- Keep audit append-only.
- Keep financial correction through reversal/correction/credit-note, not hard delete.
- Preserve offline timestamp and sync timestamp separately.
- Keep service day/business day separate from calendar timestamp for PLANET.
- Do not model PLANET restaurant flow as legacy temp tables; model it as table sessions, orders, tickets, status events and fiscal/payment handoff.
"""

    rbac = """# 02 RBAC Matrix — Update from Wave 3 Sanitized Evidence

## Add action set

Every feature/module should be tagged with explicit permissions:

- `read`
- `create`
- `update`
- `correct_void`
- `print`
- `approve`
- `export`
- `admin`

For financial documents, avoid normal `delete`; use `correct_void`, reversal, credit note or deactivation depending on document type.

## Add approval gates

Manager/admin approval required for:

- discount outside configured limits;
- price override;
- void/cancel after kitchen/fiscal/payment stage;
- table transfer between waiters where policy requires it;
- stock correction;
- write-off/scrap;
- period reopen;
- fiscal retry/manual intervention where required;
- cash shift adjustment;
- worker settlement correction.

## Add migration rule

Legacy PRO DATA users, passwords, PINs, MAC/device identifiers and hidden credentials are **not migrated**. Users are recreated/reset in iData+.

Legacy RBAC is used only as capability evidence to avoid missing controls.

## Add audit rule

Every critical action must produce append-only audit evidence:

- actor;
- business/branch;
- device/session;
- original event timestamp;
- sync timestamp when offline;
- before/after hashes or structured diff;
- reason;
- approval actor when applicable.
"""

    poc = """# 01 PoC Specs — Update from PRO DATA Closure

## PoC Accounting Parity

Goal: prove that iData+ can reproduce PLANET accounting outputs for selected period using PRO DATA evidence.

Pass criteria:

- sales/purchase/payment/POS documents post to GL;
- debit = credit;
- sales and purchase books match selected-period source totals;
- VAT output/input summary reconciles to PRO DATA/accountant-approved result;
- AR/AP and cash/bank totals reconcile for selected period;
- discrepancies are documented as bug, accepted difference or accountant decision.

Evidence:

- posting deep dive;
- GL reconciliation;
- raw reports/tables;
- accountant sample files.

## PoC PLANET POS/KDS local operation

Goal: prove restaurant flow works as domain model, not copied temp-table architecture.

Pass criteria:

- waiter opens table and adds items;
- can send all or selected items to station;
- kitchen/bar sees status event flow;
- timers and delay states work;
- manager approval controls void/transfer/discount;
- bill/payment/fiscal handoff is tracked;
- local hub path works without internet.

Evidence:

- Wave 2 restaurant/POS/KDS extract;
- floor coordinates from `tblRestTavolinat`;
- KDS/open order evidence;
- final PLANET floor-plan/hardware still required.

## PoC RBAC/Audit

Goal: prove critical actions cannot bypass role/approval/audit rules.

Pass criteria:

- old capability coverage represented in new RBAC matrix;
- no legacy credentials migrated;
- risky actions require approval;
- append-only audit created for every critical event.

Evidence:

- Wave 3 sanitized RBAC/config/audit extract.

## PoC SEF

Goal: prove SEF flow against official test phase, not legacy assumptions.

Pass criteria:

- protocol matches official `github.com/fiskalizimi`;
- fiscal outbox handles queued/sent/failed/offline statuses;
- offline 48h alarm and retry path visible;
- test transaction evidence stored privately.
"""

    adr = """# 05 Decision Gates / ADR — Update from PRO DATA Closure

## Accepted ADR: PRO DATA is long-running evidence baseline

Decision: PRO DATA backup evidence remains active through development, UAT, parallel run, cutover and launch.

Consequence: Every accounting/POS/inventory/RBAC module must reference the relevant evidence pack before implementation and during validation.

## Accepted ADR: Do not clone legacy schema blindly

Decision: iData+ will rebuild clean domain models and workflows. PRO DATA is source evidence for behavior/parity, not the target database architecture.

Consequence: especially for PLANET restaurant/POS/KDS, use table sessions/order/ticket/status-event/local-hub model instead of temp-table copying.

## Accepted ADR: No legacy credential migration

Decision: PRO DATA passwords, PINs, MAC/device IDs, tokens/certs and hidden secrets are not migrated.

Consequence: users are recreated/reset; RBAC is re-modeled from capability evidence.

## Decision gate: Accountant official sign-off

Open until accountant confirms:

- official VAT declaration comparison;
- KABI VAT/product rules;
- farmer tax/VAT;
- inventory valuation method;
- dairy costing and COGS treatment;
- returns/expiry/write-off policy.

## Decision gate: PLANET floor plan and hardware

Open until PLANET confirms:

- final floor plan/table coordinates;
- KDS station count and placement;
- printer routes;
- POS terminals/banks;
- local hub hardware and UPS.
"""

    feature = """# Feature Status Register — Update from PRO DATA Closure

| Feature / Evidence | Status | Notes |
| --- | --- | --- |
| PRO DATA DDL/schema extraction | Closed / Evidence | Full baseline available. |
| PRO DATA posting logic deep dive | Closed / Evidence | Use for accounting-core design. |
| PRO DATA GL reconciliation template | Closed / Evidence | Use as UAT/parallel validation method. |
| Wave 1 accounting/costing/fiscal/setup | Closed / Evidence | Use for inventory/COGS/opening-balance/fiscal support. |
| Wave 2 PLANET POS/KDS | Closed / Evidence | Use for POS/KDS/floor/table/KDS design. |
| Wave 3 RBAC/audit/config | Closed sanitized / Evidence | Use for RBAC/audit/config; do not migrate secrets. |
| PLANET accounting parity | MVP / Accounting Core | Must satisfy accountant parity target. |
| KABI accounting core reuse | MVP / KABI | Uses shared accounting core. |
| KABI dairy-specific accounting | Requires accountant/factory/ATK | Not inferred from PLANET backup. |
| PLANET POS/KDS final hardware/floor plan | Requires PLANET | Needed before final implementation. |
| Legacy users/passwords/PIN/MAC migration | Out of scope | Recreate/reset users. |
| AI Invoice Scanner | Roadmap / Later unless approved | Useful but not Phase 0 blocker. |
"""

    manifest_rows = [
        ["PHASE0_Canonical_Update_Pack.md", "overview", "docs/canonical/ or review folder", "Main handoff file"],
        ["SSOT_update_snippet.md", "SSOT", "merge into SSOT", "Canonical project decision"],
        ["03_Accounting_Compatibility_Matrix_update.md", "accounting", "merge into accounting matrix", "PLANET parity target"],
        ["04_Data_Model_Draft_update.md", "data model", "merge into data model", "Entities and domain rules"],
        ["02_RBAC_Matrix_update.md", "RBAC", "merge into RBAC matrix", "Permission/action/audit rules"],
        ["01_PoC_Specs_update.md", "PoC", "merge into PoC specs", "Pass/fail criteria"],
        ["05_Decision_Gates_ADR_update.md", "ADR", "merge into ADR", "Accepted decisions and open gates"],
        ["Feature_Status_Register_update.md", "feature status", "merge into feature register", "Status cleanup"],
    ]
    with (OUT / "docs_merge_manifest.csv").open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["file", "doc_area", "target", "purpose"])
        writer.writerows(manifest_rows)

    write(OUT / "PHASE0_Canonical_Update_Pack.md", main_doc)
    write(OUT / "SSOT_update_snippet.md", ssot)
    write(OUT / "03_Accounting_Compatibility_Matrix_update.md", accounting)
    write(OUT / "04_Data_Model_Draft_update.md", data_model)
    write(OUT / "02_RBAC_Matrix_update.md", rbac)
    write(OUT / "01_PoC_Specs_update.md", poc)
    write(OUT / "05_Decision_Gates_ADR_update.md", adr)
    write(OUT / "Feature_Status_Register_update.md", feature)

    archive_base = OUT.with_suffix("")
    shutil.make_archive(str(archive_base), "zip", OUT)

    print(f"Generated {OUT}")
    print(f"Generated {archive_base}.zip")


if __name__ == "__main__":
    main()
