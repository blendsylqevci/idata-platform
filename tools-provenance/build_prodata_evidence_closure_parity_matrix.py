#!/usr/bin/env python3
"""
Build the PRO DATA Evidence Closure + Parity Matrix v1.

Purpose:
  Convert the extracted PRO DATA backup evidence into a canonical, phase-aware
  reference document for iData+ development through launch.

This script does not connect to SQL Server. It reads the already generated
evidence bundles and writes a closure/parity pack under outputs/.
"""

from __future__ import annotations

import csv
import datetime as dt
import zipfile
from collections import Counter, defaultdict
from pathlib import Path


OUT_ROOT = Path("outputs/prodata_evidence_closure_parity_matrix_2026-06-28")

AUDIT_MATRIX = Path("outputs/prodata_backup_completeness_audit_2026-06-28/table_coverage_matrix.csv")
AUDIT_DOC = Path("outputs/prodata_backup_completeness_audit_2026-06-28/PRODATA_Backup_Completeness_Audit.md")
DDL_MANIFEST = Path("outputs/prodata_ddl_extracted_2026-06-28/EXTRACTION_MANIFEST.md")
REPORTS_MANIFEST = Path("outputs/prodata_reports_extracted_2026-06-27/EXTRACTION_MANIFEST.md")
POSTING_DEEP_DIVE = Path("outputs/prodata_posting_deep_dive_2026-06-28/PRODATA_Posting_Logic_Deep_Dive.md")
GL_RECON = Path("outputs/prodata_gl_reconciliation_2026-06-28/PRODATA_GL_Posting_Reconciliation_June2026.md")
MODULE_MAP = Path("outputs/prodata_module_map_2026-06-28/PRODATA_Accounting_POS_Inventory_Module_Map.md")
WAVE1_MANIFEST = Path("outputs/prodata_supplementary_extract_wave1_2026-06-28/EXTRACTION_MANIFEST.md")
WAVE2_REVIEW = Path("outputs/prodata_supplementary_extract_wave2_restaurant_2026-06-28/PRODATA_Restaurant_POS_KDS_Operational_Review.md")
WAVE2_KEY_PROCS = Path("outputs/prodata_supplementary_extract_wave2_restaurant_2026-06-28/restaurant_key_procedure_index.csv")
WAVE3_REVIEW = Path("outputs/prodata_supplementary_extract_wave3_sanitized_2026-06-28/PRODATA_Audit_RBAC_Config_Sanitized_Review.md")


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


def md_table(rows: list[dict[str, str]], columns: list[str], limit: int | None = None) -> str:
    if limit is not None:
        rows = rows[:limit]
    if not rows:
        return "_Asnjë rresht._\n"
    out = ["| " + " | ".join(columns) + " |", "| " + " | ".join("---" for _ in columns) + " |"]
    for row in rows:
        out.append("| " + " | ".join(str(row.get(col, "")).replace("|", "\\|").replace("\n", " ") for col in columns) + " |")
    return "\n".join(out) + "\n"


def rel(path: Path) -> str:
    return str(path)


def audit_stats() -> dict[str, object]:
    rows = read_csv(AUDIT_MATRIX)
    total = len(rows)
    nonzero = [r for r in rows if int(r["row_count"]) > 0]
    exported = [r for r in rows if r["data_status"] == "exported"]
    a_missing = [r for r in rows if r["priority"] == "A" and r["action"] == "supplementary_extract_required"]
    b_missing = [r for r in rows if r["priority"] == "B" and r["action"] == "supplementary_review_or_extract"]
    by_module = Counter(r["module"] for r in exported)
    by_action = Counter(r["action"] for r in rows)
    return {
        "rows": rows,
        "total": total,
        "nonzero": len(nonzero),
        "exported": len(exported),
        "a_missing": len(a_missing),
        "b_missing": len(b_missing),
        "by_module": by_module,
        "by_action": by_action,
    }


def build_evidence_index() -> list[dict[str, str]]:
    items = [
        {
            "Evidence area": "Full DDL/schema",
            "Main file": rel(DDL_MANIFEST),
            "Use": "Table/view/procedure/function/trigger/index/constraint source for model review and procedure tracing",
            "Status": "closed for baseline",
        },
        {
            "Evidence area": "Core reports/raw critical tables",
            "Main file": rel(REPORTS_MANIFEST),
            "Use": "Libri i Shitjes/Blerjes, TVSH summary, GL monthly, master data, raw GL/sales/purchase/payment/stock tables",
            "Status": "closed for baseline",
        },
        {
            "Evidence area": "Accounting/POS/Inventory module map",
            "Main file": rel(MODULE_MAP),
            "Use": "Module ownership and object mapping for accounting, POS, inventory, reports",
            "Status": "closed for baseline",
        },
        {
            "Evidence area": "Posting logic deep dive",
            "Main file": rel(POSTING_DEEP_DIVE),
            "Use": "Procedure-level reference for SHV, BLV/BLS, PGD/PGH, POS payments and GL creation",
            "Status": "closed for accounting design",
        },
        {
            "Evidence area": "June 2026 GL reconciliation",
            "Main file": rel(GL_RECON),
            "Use": "Document -> GL -> tax book -> VAT summary validation pattern",
            "Status": "closed as validation template",
        },
        {
            "Evidence area": "Wave 1 accounting/costing/fiscal/setup",
            "Main file": rel(WAVE1_MANIFEST),
            "Use": "COGS, beginning balances, fiscal evidence, deleted/correction history, price history, setup tables",
            "Status": "closed",
        },
        {
            "Evidence area": "Wave 2 restaurant/POS/KDS",
            "Main file": rel(WAVE2_REVIEW),
            "Use": "PLANET tavolina, open orders, KDS queue, floor coordinates, restaurant procedures and POS/KDS domain design",
            "Status": "closed",
        },
        {
            "Evidence area": "Wave 2 key procedure index",
            "Main file": rel(WAVE2_KEY_PROCS),
            "Use": "Practical procedure map for order_create/table_state/kds_queue/settlement/void/fiscal/reporting",
            "Status": "closed",
        },
        {
            "Evidence area": "Wave 3 sanitized audit/RBAC/config",
            "Main file": rel(WAVE3_REVIEW),
            "Use": "Audit log, user activity, RBAC, user rights, config/reference design without credentials/secrets",
            "Status": "closed sanitized",
        },
        {
            "Evidence area": "Completeness audit final",
            "Main file": rel(AUDIT_DOC),
            "Use": "Proof that Priority A and B extraction/review gaps are now zero",
            "Status": "A=0, B=0",
        },
    ]
    return items


def build_phase_usage_matrix() -> list[dict[str, str]]:
    return [
        {
            "Phase": "Faza 0 — Foundation/PoCs/docs",
            "How backup is used": "SSOT/evidence closure, data model, ADRs, PoC criteria, SEF/accounting/POS risk reduction",
            "Required output": "Evidence Closure + Parity Matrix, feature status, decision gates, PoC references",
            "Go/No-Go use": "No heavy coding until closure, SEF PoC path, accounting parity target and POS/KDS model are clear",
        },
        {
            "Phase": "Foundation — multi-company/RBAC/audit",
            "How backup is used": "Wave 3 RBAC/config/audit evidence informs clean permissions and append-only audit model",
            "Required output": "RBAC matrix, approval matrix, audit event taxonomy, support/break-glass rules",
            "Go/No-Go use": "No user/password/PIN/mac migration; recreate users and validate permissions against old capability level",
        },
        {
            "Phase": "Accounting Core",
            "How backup is used": "Reports + posting deep dive + GL reconciliation define PLANET functional parity",
            "Required output": "GL, journal, AR/AP, cash/bank, VAT, Libri Shitjes/Blerjes, posting rules, period close",
            "Go/No-Go use": "Debit=credit, subledger=GL, VAT books match parallel month, accountant sign-off required",
        },
        {
            "Phase": "Inventory/COGS/master data",
            "How backup is used": "Wave 1 costing, stock, price/history and master-data evidence guide item/stock/cost design",
            "Required output": "Item cards, price history, recipes/normatives, stock movements, COGS layers, corrections",
            "Go/No-Go use": "Stock/COGS reports must reconcile for selected period before relying on financial statements",
        },
        {
            "Phase": "KABI vertical",
            "How backup is used": "Shared accounting core patterns apply; KABI-specific dairy rules remain separate decision gates",
            "Required output": "Farmer settlement, milk reception, lab/QC, production batches, LOT/expiry, van stock, mobile sales",
            "Go/No-Go use": "Do not infer KABI VAT/formula/consignment/farmer tax from PLANET backup without accountant/factory/ATK",
        },
        {
            "Phase": "PLANET POS/KDS",
            "How backup is used": "Wave 2 gives tavolina/floor coordinates, open-order, KDS queue and restaurant procedure baseline",
            "Required output": "Floor plan, table sessions, orders/items, KDS tickets/stations, cash shifts, worker settlement",
            "Go/No-Go use": "Must satisfy local hub/offline, KDS/printer backup, manager approvals and service-day rules",
        },
        {
            "Phase": "SEF/fiscalization",
            "How backup is used": "Fiscal evidence + POS/restaurant procedures inform fiscal outbox and report parity; official ATK GitHub remains protocol authority",
            "Required output": "Fiscal outbox, statuses, retry, QR/NIVF/FIC, offline 48h alarms, test transaction evidence",
            "Go/No-Go use": "Backup is supporting evidence only; certification behavior must match official fiskalizimi repos and ATK testing",
        },
        {
            "Phase": "UAT / parallel run",
            "How backup is used": "Parallel month compares iData+ outputs to PRO DATA extracts and accountant files",
            "Required output": "Comparison workbook/report: sales, purchases, VAT, GL, stock, COGS, cash/bank, AR/AP, partner/article cards",
            "Go/No-Go use": "Discrepancies must be explained, corrected, or signed off before go-live",
        },
        {
            "Phase": "Cutover / launch",
            "How backup is used": "Migration and validation source for PLANET master data, balances and selected history",
            "Required output": "Cutover checklist, opening balances, migration mapping, cleanup rules, rollback plan",
            "Go/No-Go use": "No hidden second database; old users/secrets not migrated; final balances/reports approved",
        },
    ]


def build_parity_requirements() -> list[dict[str, str]]:
    return [
        {
            "Domain": "Accounting / GL",
            "Evidence": "Posting deep dive, GL reconciliation, raw GL, chart of accounts, document tables",
            "Implement in iData+": "Shared accounting core with double-entry GL, posting rules, document numbering, journals, period close",
            "Parity test": "Document -> GL bridge, debit=credit, subledger=GL, month totals match PRO DATA/accountant-approved result",
            "Phase": "Accounting Core",
            "Boundary": "KABI uses same core but dairy-specific posting rules stay decision-gated",
        },
        {
            "Domain": "VAT / ATK books",
            "Evidence": "Libri Shitjes, Libri Blerjes, TVSH monthly summary, purchase book sample from accountant",
            "Implement in iData+": "Libri i Shitjes/Blerjes, VAT declaration support, K1/K2 and box mapping, exportable accountant reports",
            "Parity test": "Parallel month VAT output/input and tax books compare to PRO DATA and official accountant declaration",
            "Phase": "Accounting Core / UAT",
            "Boundary": "Derived VAT summary is not official tax truth until accountant confirms",
        },
        {
            "Domain": "Sales/POS payments",
            "Evidence": "POS raw tables, POS payments, posting procedures, fiscalized docs",
            "Implement in iData+": "POS sales/payment handoff, cash/card/bank/POS terminal payments, fiscal queue, GL postings",
            "Parity test": "POS totals, payment methods, fiscal status and GL postings reconcile per day/month",
            "Phase": "Accounting Core / PLANET POS",
            "Boundary": "SEF protocol comes from official fiskalizimi repos, not legacy assumptions",
        },
        {
            "Domain": "Inventory / stock / COGS",
            "Evidence": "Wave 1 COGS tables, stock movements, inventory adjustments, item master, recipes/normatives",
            "Implement in iData+": "Stock ledger, warehouses, recipes, FEFO/LOT where needed, COGS layers and correction/reversal logic",
            "Parity test": "Stock movements and COGS for selected period reconcile to PRO DATA baseline where applicable",
            "Phase": "Inventory/COGS",
            "Boundary": "KABI batch/yield/dairy costing requires factory/accountant confirmation",
        },
        {
            "Domain": "PLANET POS/KDS operations",
            "Evidence": "Wave 2 tavolina, open orders, KDS queue, restaurant procedure index",
            "Implement in iData+": "Table sessions, real floor plan, partial send to kitchen/bar, KDS stations, timers, printer backup",
            "Parity test": "Operational walkthrough: waiter order -> KDS/bar -> ready/delivered -> bill/payment -> fiscal/outbox",
            "Phase": "PLANET POS/KDS",
            "Boundary": "Do not copy PRO DATA temp-table model; rebuild clean offline-first domain model",
        },
        {
            "Domain": "RBAC / approvals",
            "Evidence": "Wave 3 user groups, group rights, report rights, special rights, location/warehouse rights",
            "Implement in iData+": "Module/entity/action permission matrix with read/create/update/correct-void/print/approve/export/admin",
            "Parity test": "Old capability level represented without migrating old credentials; approval gates tested on risky actions",
            "Phase": "Foundation",
            "Boundary": "No password/PIN/MAC migration; recreate/reset users",
        },
        {
            "Domain": "Audit trail",
            "Evidence": "Wave 3 audit/user/activity coverage and sanitized samples",
            "Implement in iData+": "Append-only audit events with actor, device, offline timestamp, sync timestamp, reason, approval actor and before/after hashes",
            "Parity test": "Every critical financial/POS/inventory/security action creates searchable audit record",
            "Phase": "Foundation / All modules",
            "Boundary": "Legacy audit text is reference only; privacy-sensitive content remains redacted",
        },
        {
            "Domain": "Config/reference",
            "Evidence": "Wave 3 system options/localization/reference tables",
            "Implement in iData+": "Typed, versioned config with secrets in secret manager and admin-change audit",
            "Parity test": "Required business settings exist as explicit typed config, not hidden free text",
            "Phase": "Foundation / Module setup",
            "Boundary": "Do not blindly import legacy hidden config values",
        },
        {
            "Domain": "Migration / cutover",
            "Evidence": "Master data, opening balances, reports, raw tables, sanitized RBAC evidence",
            "Implement in iData+": "Migration transforms for allowed data only; cleanup and validation reports",
            "Parity test": "Opening balances, master-data counts, selected history and reports signed off",
            "Phase": "Cutover",
            "Boundary": "No user credentials/secrets; KABI old program not migrated unless explicitly decided",
        },
    ]


def write_csv(path: Path, rows: list[dict[str, str]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not rows:
        return
    fields = list(rows[0].keys())
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def build_markdown(
    stats: dict[str, object],
    evidence_index: list[dict[str, str]],
    phase_usage: list[dict[str, str]],
    parity_reqs: list[dict[str, str]],
) -> str:
    now = dt.datetime.now().astimezone().isoformat(timespec="seconds")
    by_action = stats["by_action"]
    by_module = stats["by_module"]
    module_rows = [
        {"Module": module, "Covered tables": str(count)}
        for module, count in by_module.most_common()
    ]
    action_rows = [
        {"Action": action, "Tables": str(count)}
        for action, count in sorted(by_action.items())
    ]
    summary_rows = [
        {"Metric": "Tables in closure matrix", "Value": str(stats["total"])},
        {"Metric": "Non-zero tables", "Value": str(stats["nonzero"])},
        {"Metric": "Tables covered by evidence/export", "Value": str(stats["exported"])},
        {"Metric": "Priority A gaps", "Value": str(stats["a_missing"])},
        {"Metric": "Priority B gaps", "Value": str(stats["b_missing"])},
    ]

    sections = [
        "# PRO DATA Evidence Closure + Parity Matrix v1",
        "",
        f"- Generated: `{now}`",
        "- Scope: PRO DATA backup evidence for PLANET/RESORT and shared iData+ accounting/POS/inventory/RBAC design",
        "- Status: canonical evidence pack candidate; intended for `docs/canonical/` + `docs/evidence/` later",
        "- Core correction: backup is **not only Phase 0 evidence**. It is the golden reference baseline through development, UAT, parallel run, cutover and launch.",
        "",
        "## Executive verdict",
        "",
        "The PRO DATA backup is now sufficiently extracted, reviewed and sanitized to serve as the long-running evidence baseline for iData+. Extraction/review is closed for Priority A and Priority B gaps: `A=0`, `B=0`.",
        "",
        "This does **not** mean every legacy behavior should be copied. It means we have enough source-faithful evidence to design, implement and validate iData+ without guessing what PLANET’s existing accounting/POS/RBAC system did.",
        "",
        "Golden rule: use PRO DATA as a reference for parity and validation, not as a schema to clone blindly.",
        "",
        "## Closure summary",
        "",
        md_table(summary_rows, ["Metric", "Value"]).rstrip(),
        "",
        "## Coverage by action",
        "",
        md_table(action_rows, ["Action", "Tables"]).rstrip(),
        "",
        "## Covered evidence by module",
        "",
        md_table(module_rows, ["Module", "Covered tables"], limit=30).rstrip(),
        "",
        "## Evidence index",
        "",
        md_table(evidence_index, ["Evidence area", "Main file", "Use", "Status"]).rstrip(),
        "",
        "## Phase usage matrix",
        "",
        md_table(phase_usage, ["Phase", "How backup is used", "Required output", "Go/No-Go use"]).rstrip(),
        "",
        "## Parity requirements matrix",
        "",
        md_table(parity_reqs, ["Domain", "Evidence", "Implement in iData+", "Parity test", "Phase", "Boundary"]).rstrip(),
        "",
        "## What can now be considered closed",
        "",
        "- DDL/schema extraction and object inventory.",
        "- Core accounting/report evidence: sales book, purchase book, VAT summary, GL monthly, chart of accounts, raw critical tables.",
        "- Posting logic investigation for sales, purchases, payments, POS and GL procedures.",
        "- June 2026 reconciliation method for document -> GL -> tax-book validation.",
        "- Supplementary Wave 1: COGS/costing, opening balances, fiscal evidence, deleted/correction history, setup/master gaps.",
        "- Supplementary Wave 2: restaurant/POS/KDS operational evidence and procedure index.",
        "- Supplementary Wave 3: audit/user activity, RBAC, users/permissions and system config, sanitized.",
        "",
        "## What is explicitly not closed by the PLANET backup",
        "",
        "- KABI product VAT mapping, farmer VAT/tax treatment, USAID milk price formula, farmer advance rules and AUVK report shape.",
        "- KABI dairy production costing: batch/yield/loss/by-product/catch-weight final accounting treatment.",
        "- KABI consignment/returns/expiry policy and vehicle settlement details.",
        "- Official ATK/SEF certification behavior; official `github.com/fiskalizimi` and ATK test phase remain the authority.",
        "- PLANET final floor plan, exact KDS screen count/placement, printer routing and hardware inventory.",
        "- Official monthly VAT declaration filed by accountant; derived summaries must be compared, not treated as official truth.",
        "",
        "## Migration and privacy boundaries",
        "",
        "- Do not migrate PRO DATA passwords, POSPIN, RESTPIN, MAC/device identifiers, tokens, certificates or hidden connection/config values.",
        "- Users must be recreated/reset in iData+.",
        "- Legacy RBAC is capability evidence; iData+ gets a clean permission matrix.",
        "- Legacy audit logs are validation/reference evidence; iData+ must create its own append-only audit trail.",
        "- Sanitized exports are safe for documentation/evidence; raw sensitive tables should remain controlled and not become public docs.",
        "",
        "## How this should be used by Claude Code / Codex during development",
        "",
        "1. Before implementing a module, check the parity row for that domain.",
        "2. Use the referenced evidence bundle to confirm behavior before inventing logic.",
        "3. If behavior is PLANET/shared-core, use PRO DATA as baseline.",
        "4. If behavior is KABI-specific, keep it as a decision gate unless confirmed by KABI/accountant/ATK/factory.",
        "5. During UAT, compare iData+ reports against PRO DATA/accountant-approved outputs.",
        "6. During cutover, use backup outputs for migration validation, not as a mutable source system.",
        "",
        "## Next best step",
        "",
        "Move from extraction to implementation specification: update/merge the Phase 0 canonical docs with this closure pack, especially `03_Accounting_Compatibility_Matrix.md`, `04_Data_Model_Draft.md`, `02_RBAC_Matrix.md`, `01_PoC_Specs.md`, and the SSOT. After that, start the monorepo/walking skeleton with the five PoCs and keep PRO DATA as validation evidence through launch.",
    ]
    return "\n".join(sections).rstrip() + "\n"


def make_zip() -> Path:
    zip_path = OUT_ROOT.with_suffix(".zip")
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        for path in sorted(OUT_ROOT.rglob("*")):
            if path.is_file():
                z.write(path, path.relative_to(OUT_ROOT.parent))
    return zip_path


def main() -> None:
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    stats = audit_stats()
    evidence_index = build_evidence_index()
    phase_usage = build_phase_usage_matrix()
    parity_reqs = build_parity_requirements()

    write_csv(OUT_ROOT / "prodata_evidence_index.csv", evidence_index)
    write_csv(OUT_ROOT / "prodata_phase_usage_matrix.csv", phase_usage)
    write_csv(OUT_ROOT / "prodata_parity_requirements.csv", parity_reqs)

    md = build_markdown(stats, evidence_index, phase_usage, parity_reqs)
    doc = OUT_ROOT / "PRODATA_Evidence_Closure_Parity_Matrix_v1.md"
    doc.write_text(md, encoding="utf-8")
    zip_path = make_zip()

    print(f"Wrote {doc}")
    print(f"Wrote {OUT_ROOT / 'prodata_evidence_index.csv'}")
    print(f"Wrote {OUT_ROOT / 'prodata_phase_usage_matrix.csv'}")
    print(f"Wrote {OUT_ROOT / 'prodata_parity_requirements.csv'}")
    print(f"Wrote {zip_path} ({zip_path.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
