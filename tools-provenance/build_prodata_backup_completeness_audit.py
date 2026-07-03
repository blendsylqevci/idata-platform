#!/usr/bin/env python3
"""
Build a formal PRO DATA backup completeness audit from already extracted evidence.

This script does not connect to SQL Server and does not mutate the restored backup.
It compares:
  - DDL/table inventory: outputs/prodata_ddl_extracted_2026-06-28
  - Existing report/raw exports: outputs/prodata_reports_extracted_2026-06-27

Outputs:
  - outputs/prodata_backup_completeness_audit_2026-06-28/PRODATA_Backup_Completeness_Audit.md
  - outputs/prodata_backup_completeness_audit_2026-06-28/PRODATA_Supplementary_Extract_Plan.md
  - CSV evidence files for table-level coverage and planned supplementary extracts
"""

from __future__ import annotations

import csv
import datetime as dt
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path


DDL_ROOT = Path("outputs/prodata_ddl_extracted_2026-06-28")
REPORT_ROOT = Path("outputs/prodata_reports_extracted_2026-06-27")
OUT_ROOT = Path("outputs/prodata_backup_completeness_audit_2026-06-28")
SUPPLEMENTARY_ROOTS = [
    Path("outputs/prodata_supplementary_extract_wave1_2026-06-28/raw_supplementary_tables"),
    Path("outputs/prodata_supplementary_extract_wave2_restaurant_2026-06-28/raw_restaurant_tables"),
    Path("outputs/prodata_supplementary_extract_wave3_sanitized_2026-06-28/sanitized_audit_samples"),
    Path("outputs/prodata_supplementary_extract_wave3_sanitized_2026-06-28/sanitized_reference"),
]

DB_PREFIX = {
    "PRODATA_PLANET2": "planet2",
    "PRODATA_PLANET_RESORT": "planet_resort",
    "PRODATA_TEST": "test",
}

ANALYSIS_DBS = ("PRODATA_PLANET2", "PRODATA_PLANET_RESORT")


@dataclass
class TableCoverage:
    database: str
    schema: str
    table: str
    row_count: int
    columns: int
    module: str
    priority: str
    export_mode: str
    ddl_status: str
    data_status: str
    existing_export: str
    action: str
    reason: str


def parse_psv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8", errors="replace", newline="") as f:
        reader = csv.DictReader(f, delimiter="|")
        return list(reader)


def md_table(rows: list[dict[str, str]], columns: list[str], limit: int | None = None) -> str:
    if limit is not None:
        rows = rows[:limit]
    if not rows:
        return "_Asnjë rresht._\n"
    out = ["| " + " | ".join(columns) + " |", "| " + " | ".join("---" for _ in columns) + " |"]
    for row in rows:
        out.append("| " + " | ".join(str(row.get(col, "")).replace("|", "\\|") for col in columns) + " |")
    return "\n".join(out) + "\n"


def normalize_table(name: str) -> str:
    return name.lower()


def existing_table_exports() -> dict[tuple[str, str], list[str]]:
    """Return {(database, lower_table): [paths...]} for files that appear to be table exports."""
    exports: dict[tuple[str, str], list[str]] = defaultdict(list)

    for database, prefix in DB_PREFIX.items():
        if database == "PRODATA_TEST":
            continue
        patterns = [
            f"{prefix}_*.psv",
            f"raw_critical_tables/{prefix}_*.psv.gz",
            f"raw_critical_tables/{prefix}_*.psv",
        ]
        for pattern in patterns:
            for path in REPORT_ROOT.glob(pattern):
                stem = path.name
                if stem.endswith(".psv.gz"):
                    stem = stem[:-7]
                elif stem.endswith(".psv"):
                    stem = stem[:-4]
                table = stem.removeprefix(prefix + "_")
                # Report files are not raw table exports.
                if table.startswith(("libri_", "gl_monthly", "plani_kontabel", "table_rowcounts")):
                    continue
                exports[(database, normalize_table(table))].append(str(path))

        # Plani kontabël is exported as a report-style name but maps to tblPlaniKontabel.
        plani = REPORT_ROOT / f"{prefix}_plani_kontabel.psv"
        if plani.exists():
            exports[(database, "tblplanikontabel")].append(str(plani))

    for supp_root in SUPPLEMENTARY_ROOTS:
        if not supp_root.exists():
            continue
        for path in supp_root.glob("*.psv.gz"):
            stem = path.name[:-7]
            for database, prefix in DB_PREFIX.items():
                if database == "PRODATA_TEST":
                    continue
                if stem.startswith(prefix + "_"):
                    table = stem.removeprefix(prefix + "_")
                    exports[(database, normalize_table(table))].append(str(path))
                    break

    return exports


def load_tables() -> list[dict[str, str]]:
    tables: list[dict[str, str]] = []
    for database in ANALYSIS_DBS:
        inv = DDL_ROOT / "databases" / database / "00_table_inventory.psv"
        if not inv.exists():
            continue
        for row in parse_psv(inv):
            table = row.get("table_name") or row.get("table") or row.get("Table") or row.get("object_name") or ""
            schema = row.get("schema_name") or row.get("schema") or row.get("Schema") or "dbo"
            row_count = row.get("row_count") or row.get("rows") or row.get("RowsEstimate") or row.get("rows_estimate") or "0"
            columns = row.get("columns") or row.get("Columns") or row.get("column_count") or "0"
            if not table or table == "----------":
                continue
            try:
                rows_int = int(row_count)
            except ValueError:
                rows_int = 0
            try:
                cols_int = int(columns)
            except ValueError:
                cols_int = 0
            tables.append(
                {
                    "database": database,
                    "schema": schema,
                    "table": table,
                    "row_count": str(rows_int),
                    "columns": str(cols_int),
                }
            )
    return tables


def classify(table: str, row_count: int) -> tuple[str, str, str, str]:
    """Return module, priority, export_mode, reason."""
    t = table.lower()

    if row_count == 0:
        return "Schema-only / unused in backup", "C", "schema_only", "zero rows in restored backup"

    # Highest-risk financial/accounting artifacts.
    if t in {
        "tblgeneralledger",
        "tblplanikontabel",
        "tblplanikontabeltipet",
        "tbltarifat",
        "tblbankat",
        "tblfiscalyears",
    }:
        return "Accounting / GL setup", "A", "full_raw", "core accounting setup or ledger"

    if re.search(r"tbldoc(shitjet|blerjet|pagesat)(master|detalet)$", t):
        return "Accounting documents", "A", "full_raw", "source document table for posting/parity"

    if re.search(r"tbldoc(ekstrakti)(master|detalet|lidhjet)$", t):
        return "Bank / payment reconciliation", "A", "full_raw", "bank statement/payment linking"

    if re.search(r"tbldoc(beginningbalance)(master|kontot|subjektet|stoqet)$", t):
        return "Opening balances", "A", "full_raw", "opening balances needed for migration/cutover"

    if re.search(r"tbldocpos(shitjetmaster|shitjetdetalet|pagesat)$", t):
        return "POS financial", "A", "full_raw", "POS sale/payment financial handoff"

    if re.search(r"tbldocposbarazimi", t):
        return "POS settlement", "A", "full_raw", "POS/barazim settlement model"

    if t in {"tblcalculationitemscost", "tblcalculationitemscostok", "tblcalculationitemscostbad"}:
        return "Costing / COGS", "A", "full_or_month_slice", "COGS/cost-layer evidence, large table"

    if re.search(r"tbldoc(levizjet|inventoryadjustment)(master|detalet)$", t):
        return "Inventory / stock", "A", "full_raw", "stock movement/inventory adjustment"

    if t in {
        "tblartikujt",
        "tblartikujtcmimet",
        "tblartikujtnormativi",
        "tblartikujtposkategoria",
        "tblsubjektet",
        "tblmenyrapagesa",
        "tbltipetmalli",
        "tblnjesitematese",
        "tblnjesiteorganizative",
    }:
        return "Master data", "A", "full_raw", "ERP/POS/accounting master data"

    if t in {"tblartikujtcmimetlog", "tbldeletedlog"}:
        return "History / audit", "A", "full_raw", "price/deletion history required for audit parity"

    if t in {"tblauditlogs", "tbluserslog", "tblactivities"}:
        return "Audit / user activity", "A", "sample_plus_schema", "large operational audit trail; use samples and schema first"

    if t in {"tblfiscaliseddocuments", "tblfiskalrestorantpendingorders"}:
        return "Fiscal / SEF evidence", "A", "full_raw", "fiscalization/POS fiscal evidence"

    if re.search(r"rest|kuzhin|tavolin|porosi", t):
        return "Restaurant / KDS / tables", "A", "full_raw", "restaurant operational model"

    if re.search(r"del$", t) and re.search(r"tbldoc|memo|rest|ekstrakt", t):
        return "Deleted/correction history", "A", "full_raw", "deleted/corrected document history"

    if re.search(r"users|rights|role|group|warehouseapp|lokacionet|raportet", t):
        return "Users / RBAC", "B", "sanitized_or_reference", "permissions model only; never migrate passwords/PIN/mac identifiers"

    if re.search(r"system|config|option|lokalizimi|favorite|tasks|shtetet|qytetet|crm", t):
        return "System/config/reference", "B", "schema_plus_selected", "configuration/reference data"

    if re.search(r"tmp", t):
        return "Temporary/work tables", "C", "schema_only", "temporary table; review only if procedure depends on it"

    if re.search(r"memo|attached|files|eshop|translation", t):
        return "Attachments / notes / misc", "B", "schema_plus_selected", "may contain document notes/files/translations"

    if row_count >= 10000:
        return "High-volume unclassified", "B", "review_then_extract", "large nonzero table not yet classified"

    return "Other / low-volume", "C", "schema_only_or_sample", "low-volume table; review if linked by important procedures"


def action_for(priority: str, export_mode: str, row_count: int, existing: list[str]) -> str:
    if existing:
        return "covered_by_existing_export"
    if row_count == 0:
        return "no_data_schema_only"
    if priority == "A":
        return "supplementary_extract_required"
    if priority == "B":
        return "supplementary_review_or_extract"
    return "defer_with_reason"


def build_coverage() -> list[TableCoverage]:
    exports = existing_table_exports()
    coverage: list[TableCoverage] = []
    for row in load_tables():
        database = row["database"]
        table = row["table"]
        row_count = int(row["row_count"])
        columns = int(row["columns"])
        module, priority, export_mode, reason = classify(table, row_count)
        existing = exports.get((database, normalize_table(table)), [])
        data_status = "exported" if existing else ("zero_rows" if row_count == 0 else "not_exported")
        action = action_for(priority, export_mode, row_count, existing)
        coverage.append(
            TableCoverage(
                database=database,
                schema=row["schema"],
                table=table,
                row_count=row_count,
                columns=columns,
                module=module,
                priority=priority,
                export_mode=export_mode,
                ddl_status="extracted",
                data_status=data_status,
                existing_export="; ".join(existing),
                action=action,
                reason=reason,
            )
        )
    coverage.sort(key=lambda r: (r.database, {"A": 0, "B": 1, "C": 2}[r.priority], r.action, -r.row_count, r.table.lower()))
    return coverage


def write_csvs(coverage: list[TableCoverage]) -> None:
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    all_path = OUT_ROOT / "table_coverage_matrix.csv"
    plan_path = OUT_ROOT / "supplementary_extract_candidates.csv"
    fields = [
        "database",
        "schema",
        "table",
        "row_count",
        "columns",
        "module",
        "priority",
        "export_mode",
        "ddl_status",
        "data_status",
        "action",
        "reason",
        "existing_export",
    ]
    with all_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        for r in coverage:
            writer.writerow(r.__dict__)

    with plan_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        for r in coverage:
            if r.action in {"supplementary_extract_required", "supplementary_review_or_extract"}:
                writer.writerow(r.__dict__)


def to_dicts(rows: list[TableCoverage]) -> list[dict[str, str]]:
    return [
        {
            "Database": r.database,
            "Table": r.table,
            "Rows": f"{r.row_count:,}",
            "Module": r.module,
            "Priority": r.priority,
            "Mode": r.export_mode,
            "Action": r.action,
            "Reason": r.reason,
        }
        for r in rows
    ]


def write_markdown(coverage: list[TableCoverage]) -> None:
    now = dt.datetime.now().astimezone().isoformat(timespec="seconds")
    total_tables = len(coverage)
    nonzero = [r for r in coverage if r.row_count > 0]
    exported = [r for r in coverage if r.data_status == "exported"]
    a_missing = [r for r in coverage if r.priority == "A" and r.action == "supplementary_extract_required"]
    b_missing = [r for r in coverage if r.priority == "B" and r.action == "supplementary_review_or_extract"]

    by_db = Counter(r.database for r in coverage)
    by_action = Counter(r.action for r in coverage)
    by_module_missing = Counter(r.module for r in a_missing + b_missing)

    summary_rows = [
        {"Metric": "Tables in DDL inventory", "Value": str(total_tables)},
        {"Metric": "Tables with non-zero rows", "Value": str(len(nonzero))},
        {"Metric": "Tables already covered by existing data export", "Value": str(len(exported))},
        {"Metric": "Priority A tables still needing supplementary extract", "Value": str(len(a_missing))},
        {"Metric": "Priority B tables needing review/extract decision", "Value": str(len(b_missing))},
    ]

    db_rows = [{"Database": db, "Tables": str(count)} for db, count in sorted(by_db.items())]
    action_rows = [{"Action": action, "Tables": str(count)} for action, count in sorted(by_action.items())]
    module_rows = [{"Module": module, "Missing/Review tables": str(count)} for module, count in by_module_missing.most_common()]

    top_a = sorted(a_missing, key=lambda r: r.row_count, reverse=True)
    top_b = sorted(b_missing, key=lambda r: r.row_count, reverse=True)

    audit = [
        "# PRO DATA Backup Completeness Audit",
        "",
        f"- Generated: `{now}`",
        "- Scope: `PRODATA_PLANET2` + `PRODATA_PLANET_RESORT`",
        "- Inputs: DDL inventory, table row counts, existing report/raw exports",
        "- Status: quality-gate evidence, not a migration script",
        "",
        "## Executive verdict",
        "",
        "Backup-i nuk duhet të quhet më `100% i nxjerrë` vetëm pse kemi raportet kryesore, raw critical tables dhe DDL. DDL-ja është nxjerrë gjerë, por data coverage ende ka boshllëqe të matshme në tabela me vlerë për costing, audit, historik çmimesh, saldo hapëse, fiskalizim, restaurant/POS workflow dhe RBAC.",
        "",
        "Kjo nuk e zhvlerëson punën e deritanishme; e vendos në kontroll. Nga ky moment statusi korrekt është: **PRO DATA backup is materially extracted for accounting/report parity, but not yet completeness-certified for the whole iData+ platform.**",
        "",
        "## Summary",
        "",
        md_table(summary_rows, ["Metric", "Value"]).rstrip(),
        "",
        "## Tables by database",
        "",
        md_table(db_rows, ["Database", "Tables"]).rstrip(),
        "",
        "## Coverage by action",
        "",
        md_table(action_rows, ["Action", "Tables"]).rstrip(),
        "",
        "## Missing/review modules",
        "",
        md_table(module_rows, ["Module", "Missing/Review tables"]).rstrip(),
        "",
        "## Priority A gaps — must not be ignored",
        "",
        "Këto tabela kanë rows reale, nuk janë të mbuluara nga export-et ekzistuese dhe kanë ndikim të drejtpërdrejtë në iData+.",
        "",
        md_table(to_dicts(top_a), ["Database", "Table", "Rows", "Module", "Mode", "Reason"], limit=80).rstrip(),
        "",
        "## Priority B gaps — review/extract decision",
        "",
        "Këto nuk janë gjithmonë core-finance, por mund të ndikojnë në UX, permissions, konfigurime, historik, notes ose migrim.",
        "",
        md_table(to_dicts(top_b), ["Database", "Table", "Rows", "Module", "Mode", "Reason"], limit=80).rstrip(),
        "",
        "## What is already covered",
        "",
        "- Full DDL: tables, views, functions, stored procedures, triggers, keys/indexes/constraints.",
        "- Report evidence: Libri i Shitjes, Libri i Blerjes, TVSH monthly summary, GL monthly by account.",
        "- Core raw evidence: GL, sales, purchases, payments, selected bank/ekstrakt, POS sales/payments where present, inventory adjustments, stock movements for PLANET_RESORT, master data for articles/prices/recipes/POS categories/partners/VAT/payment methods.",
        "- Posting evidence: procedures and reconciliations for SHV, BLV/BLS/BLI, PGD/PGH, POS payments, COGS/costing.",
        "",
        "## What is not yet completeness-certified",
        "",
        "- Costing tables are not fully exported/analyzed as raw evidence despite very high row counts.",
        "- Audit/user/activity logs are not exported/analyzed deeply.",
        "- Price history and deleted/correction history need evidence extraction.",
        "- Beginning balances need explicit extraction coverage.",
        "- Restaurant/POS table/floor/KDS-related tables need a separate operational review.",
        "- RBAC/user rights should be extracted carefully as model evidence, but credentials/PIN/mac identifiers must not be migrated.",
        "",
        "## New rule",
        "",
        "A backup area can be called `closed` only when every table/procedure is tagged as one of: `covered`, `zero_rows`, `not_needed_with_reason`, `supplementary_extracted`, or `blocked`. Until then, the correct status is `analysis in progress`.",
        "",
        "## Evidence files",
        "",
        "- `table_coverage_matrix.csv` — every table with DDL/data/action status.",
        "- `supplementary_extract_candidates.csv` — exact A/B candidate list for next extract.",
    ]
    (OUT_ROOT / "PRODATA_Backup_Completeness_Audit.md").write_text("\n".join(audit).rstrip() + "\n", encoding="utf-8")

    plan_rows = to_dicts(top_a + top_b)
    plan = [
        "# PRO DATA Supplementary Extract Plan",
        "",
        f"- Generated: `{now}`",
        "- Depends on: `PRODATA_Backup_Completeness_Audit.md`",
        "- Goal: close the extraction gaps before using the backup as a complete iData+ baseline.",
        "",
        "## Execution principle",
        "",
        "The supplementary extract should be read-only, reproducible, and split by risk. Sensitive user/security fields must be sanitized. Passwords, PINs, mac identifiers and private credentials must not be exported for migration.",
        "",
        "## Extract waves",
        "",
        "### Wave 1 — Must extract before accounting/costing implementation",
        "",
        "- `tblCalculationItemsCost*` — COGS/cost layers; export monthly slices first, then full gz if needed.",
        "- `tblDocBeginningBalance*` — opening balances for GL/subledger/stock.",
        "- `tblArtikujtCmimetLog` — price history.",
        "- `tblDeletedLog` + financial `*DEL` tables — correction/deletion behavior.",
        "- `tblFiscalisedDocuments` where rows exist — fiscal/POS evidence.",
        "",
        "### Wave 2 — Must review before PLANET POS/KDS design freezes",
        "",
        "- `tblRestTavolinat`, `tblRestDailyPorosiaCount`, `tblTMPRestShitjet*`, `tblDocPOSBarazimi*`.",
        "- Any restaurant/KDS/order tables with rows or referenced by procedures.",
        "- POS archival/sync/offline tables if non-zero or referenced by procedures.",
        "",
        "### Wave 3 — Audit/RBAC/config model evidence",
        "",
        "- `tblAuditLogs`, `tblUsersLog`, `tblActivities`: sample + schema + period slices, not necessarily full raw first.",
        "- `tblUSERS`, groups/rights/roles/user-location/payment-method rights: sanitized/reference export only.",
        "- `tblSystemAppOptions*`, report permissions, warehouse/location user mappings: selected config export.",
        "",
        "## Candidate list",
        "",
        md_table(plan_rows, ["Database", "Table", "Rows", "Module", "Priority", "Mode", "Reason"]).rstrip(),
        "",
        "## Output target for the supplementary run",
        "",
        "- `outputs/prodata_supplementary_extract_2026-06-28/EXTRACTION_MANIFEST.md`",
        "- `outputs/prodata_supplementary_extract_2026-06-28/raw_supplementary_tables/*.psv.gz`",
        "- `outputs/prodata_supplementary_extract_2026-06-28/sanitized_reference/*.psv`",
        "- `outputs/prodata_supplementary_extract_2026-06-28/procedure_dependency_review/*.csv`",
        "",
        "## Acceptance criteria",
        "",
        "- Every Priority A table is either exported, zero-row, or has a written reason why it is not needed.",
        "- Every Priority B table has an owner decision: extract / sample / schema-only / ignore with reason.",
        "- Large tables have row counts and sample/month-slice validation before full export.",
        "- Sensitive user/security data is not exported into public evidence bundles.",
        "- The completeness audit is regenerated after supplementary extraction and A gaps become zero or explicitly closed.",
    ]
    (OUT_ROOT / "PRODATA_Supplementary_Extract_Plan.md").write_text("\n".join(plan).rstrip() + "\n", encoding="utf-8")


def main() -> None:
    coverage = build_coverage()
    write_csvs(coverage)
    write_markdown(coverage)
    print(f"Wrote {OUT_ROOT}/PRODATA_Backup_Completeness_Audit.md")
    print(f"Wrote {OUT_ROOT}/PRODATA_Supplementary_Extract_Plan.md")
    print(f"Wrote {OUT_ROOT}/table_coverage_matrix.csv")
    print(f"Wrote {OUT_ROOT}/supplementary_extract_candidates.csv")


if __name__ == "__main__":
    main()
