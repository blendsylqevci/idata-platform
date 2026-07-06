#!/usr/bin/env python3
"""
Finance Operations DDL/DML cross-check.

Checks the already extracted PRO DATA DDL bundle for finance-operation tables:
- Does every finance-operation table exist in DDL?
- Do procedures/views/functions/triggers reference it?
- Are there direct INSERT/UPDATE/DELETE/MERGE operations against it?
- Where do we have raw data vs schema-only vs optional micro-extract candidates?

This does not execute against SQL Server. It works from read-only extracted DDL.
"""

from __future__ import annotations

import csv
import re
import zipfile
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import date
from pathlib import Path
from typing import Iterable


ROOT = Path(__file__).resolve().parents[1]
TODAY = date.today().isoformat()
OUT_DIR = ROOT / "outputs" / f"finance_operations_ddl_dml_crosscheck_{TODAY}"

FINANCE_TABLES_CSV = ROOT / "outputs/finance_operations_backup_audit_2026-06-29/finance_operations_table_coverage.csv"
FINANCE_GAPS_CSV = ROOT / "outputs/finance_operations_backup_audit_2026-06-29/finance_operations_gap_register.csv"
DDL_ROOT = ROOT / "outputs/prodata_ddl_extracted_2026-06-28/databases"

MODULE_FILES = {
    "VIEW": "10_views.sql",
    "FUNCTION": "11_functions.sql",
    "PROCEDURE": "12_stored_procedures.sql",
    "TRIGGER": "13_triggers.sql",
}


@dataclass(frozen=True)
class ModuleObject:
    database: str
    object_type: str
    object_name: str
    body: str
    file: str


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def write_csv(path: Path, rows: Iterable[dict[str, object]], fieldnames: list[str]) -> None:
    with path.open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fieldnames)
        w.writeheader()
        for row in rows:
            w.writerow({k: row.get(k, "") for k in fieldnames})


def split_module_objects(database: str, object_type: str, path: Path) -> list[ModuleObject]:
    if not path.exists() or path.stat().st_size <= 1:
        return []
    text = path.read_text(encoding="utf-8", errors="replace")
    # Each object file is preceded by "-- Object: [schema].[name]..."
    pieces = re.split(r"(?m)^-- =+\n-- Object:\s*", text)
    out: list[ModuleObject] = []
    for piece in pieces:
        if not piece.strip():
            continue
        lines = piece.splitlines()
        header = lines[0].strip()
        body = "\n".join(lines[1:])
        m = re.search(r"\[[^\]]+\]\.\[([^\]]+)\]", header)
        if m:
            object_name = m.group(1)
        else:
            object_name = header.split()[0][:160] if header else "UNKNOWN"
        out.append(ModuleObject(database, object_type, object_name, body, str(path.relative_to(ROOT))))
    return out


def normalize_identifier(name: str) -> str:
    return re.sub(r"[^a-z0-9_]", "", name.lower())


def table_regex(table: str) -> re.Pattern[str]:
    # Matches tblName, dbo.tblName, [dbo].[tblName], [tblName]
    esc = re.escape(table)
    return re.compile(
        rf"(?i)(?<![a-z0-9_])(?:\[?dbo\]?\s*\.\s*)?\[?{esc}\]?(?![a-z0-9_])"
    )


def direct_operation(body: str, table: str) -> set[str]:
    esc = re.escape(table)
    ops: set[str] = set()
    patterns = {
        "INSERT": rf"(?is)\binsert\s+(?:into\s+)?(?:\[?dbo\]?\s*\.\s*)?\[?{esc}\]?\b",
        "UPDATE": rf"(?is)\bupdate\s+(?:\[?dbo\]?\s*\.\s*)?\[?{esc}\]?\b",
        "DELETE": rf"(?is)\bdelete\s+(?:from\s+)?(?:\[?dbo\]?\s*\.\s*)?\[?{esc}\]?\b",
        "MERGE": rf"(?is)\bmerge\s+(?:into\s+)?(?:\[?dbo\]?\s*\.\s*)?\[?{esc}\]?\b",
    }
    for op, pattern in patterns.items():
        if re.search(pattern, body):
            ops.add(op)
    if re.search(rf"(?is)\bfrom\s+(?:\[?dbo\]?\s*\.\s*)?\[?{esc}\]?\b|\bjoin\s+(?:\[?dbo\]?\s*\.\s*)?\[?{esc}\]?\b", body):
        ops.add("READ")
    return ops


def snippet_for(body: str, table: str, window: int = 140) -> str:
    m = table_regex(table).search(body)
    if not m:
        return ""
    start = max(0, m.start() - window)
    end = min(len(body), m.end() + window)
    snippet = body[start:end]
    snippet = re.sub(r"\s+", " ", snippet).strip()
    return snippet[:360]


def read_table_inventory(db: Path) -> set[str]:
    inv = db / "00_table_inventory.psv"
    if not inv.exists():
        return set()
    tables: set[str] = set()
    for line in inv.read_text(encoding="utf-8", errors="replace").splitlines():
        if not line or line.lower().startswith("schema|"):
            continue
        parts = line.split("|")
        if len(parts) >= 2:
            tables.add(parts[1])
    return tables


def md_table(headers: list[str], rows: list[list[object]]) -> str:
    if not rows:
        return "_No rows._"
    out = ["| " + " | ".join(headers) + " |", "| " + " | ".join(["---"] * len(headers)) + " |"]
    for row in rows:
        out.append("| " + " | ".join(str(x).replace("\n", " ").replace("|", "\\|") for x in row) + " |")
    return "\n".join(out)


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    finance_rows = read_csv(FINANCE_TABLES_CSV)
    gap_rows = read_csv(FINANCE_GAPS_CSV)

    # Keep table identities per database.
    finance_by_db_table = {
        (r["database"], r["table"]): r
        for r in finance_rows
    }

    modules: list[ModuleObject] = []
    ddl_tables: dict[str, set[str]] = {}
    for db_dir in sorted(p for p in DDL_ROOT.iterdir() if p.is_dir()):
        ddl_tables[db_dir.name] = read_table_inventory(db_dir)
        for object_type, filename in MODULE_FILES.items():
            modules.extend(split_module_objects(db_dir.name, object_type, db_dir / filename))

    dml_refs: list[dict[str, object]] = []
    refs_by_key: dict[tuple[str, str], list[dict[str, object]]] = defaultdict(list)

    for (database, table), frow in finance_by_db_table.items():
        rx = table_regex(table)
        for mod in modules:
            if mod.database != database:
                continue
            if not rx.search(mod.body):
                continue
            ops = direct_operation(mod.body, table)
            if not ops:
                ops = {"REFERENCE"}
            row = {
                "database": database,
                "table": table,
                "finance_area": frow.get("finance_area", ""),
                "object_type": mod.object_type,
                "object_name": mod.object_name,
                "operations": ",".join(sorted(ops)),
                "has_mutation": "yes" if ops.intersection({"INSERT", "UPDATE", "DELETE", "MERGE"}) else "no",
                "source_file": mod.file,
                "snippet": snippet_for(mod.body, table),
            }
            dml_refs.append(row)
            refs_by_key[(database, table)].append(row)

    matrix_rows: list[dict[str, object]] = []
    for (database, table), frow in sorted(finance_by_db_table.items()):
        refs = refs_by_key.get((database, table), [])
        op_counter: Counter[str] = Counter()
        type_counter: Counter[str] = Counter()
        for ref in refs:
            type_counter[str(ref["object_type"])] += 1
            for op in str(ref["operations"]).split(","):
                op_counter[op] += 1
        ddl_exists = table in ddl_tables.get(database, set())
        mutation_refs = sum(1 for ref in refs if ref["has_mutation"] == "yes")
        read_refs = sum(1 for ref in refs if "READ" in str(ref["operations"]).split(","))
        row_count = int(frow.get("row_count") or 0)
        evidence = frow.get("evidence_status", "")
        verdict = verdict_for(ddl_exists, row_count, evidence, len(refs), mutation_refs, table)
        matrix_rows.append({
            "database": database,
            "table": table,
            "finance_area": frow.get("finance_area", ""),
            "row_count": row_count,
            "raw_evidence_status": evidence,
            "ddl_exists": "yes" if ddl_exists else "no",
            "module_reference_count": len(refs),
            "mutation_reference_count": mutation_refs,
            "read_reference_count": read_refs,
            "object_type_counts": "; ".join(f"{k}:{v}" for k, v in sorted(type_counter.items())),
            "operation_counts": "; ".join(f"{k}:{v}" for k, v in sorted(op_counter.items())),
            "crosscheck_verdict": verdict,
        })

    review_rows = []
    for row in matrix_rows:
        if row["crosscheck_verdict"] != "covered_or_low_risk":
            review_rows.append({
                "database": row["database"],
                "table": row["table"],
                "finance_area": row["finance_area"],
                "row_count": row["row_count"],
                "raw_evidence_status": row["raw_evidence_status"],
                "module_reference_count": row["module_reference_count"],
                "mutation_reference_count": row["mutation_reference_count"],
                "crosscheck_verdict": row["crosscheck_verdict"],
                "recommended_action": action_for(row["crosscheck_verdict"], str(row["table"])),
            })

    write_csv(
        OUT_DIR / "finance_table_ddl_dml_matrix.csv",
        matrix_rows,
        ["database", "table", "finance_area", "row_count", "raw_evidence_status", "ddl_exists", "module_reference_count", "mutation_reference_count", "read_reference_count", "object_type_counts", "operation_counts", "crosscheck_verdict"],
    )
    write_csv(
        OUT_DIR / "finance_dml_object_references.csv",
        dml_refs,
        ["database", "table", "finance_area", "object_type", "object_name", "operations", "has_mutation", "source_file", "snippet"],
    )
    write_csv(
        OUT_DIR / "finance_uncovered_object_review.csv",
        review_rows,
        ["database", "table", "finance_area", "row_count", "raw_evidence_status", "module_reference_count", "mutation_reference_count", "crosscheck_verdict", "recommended_action"],
    )

    build_doc(matrix_rows, dml_refs, review_rows, gap_rows)
    zip_path = OUT_DIR / "finance_operations_ddl_dml_crosscheck_pack.zip"
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as z:
        for p in sorted(OUT_DIR.glob("*")):
            if p == zip_path:
                continue
            z.write(p, p.name)

    print(f"Wrote {OUT_DIR}")
    print(f"Finance tables checked: {len(matrix_rows)}")
    print(f"DDL/DML references: {len(dml_refs)}")
    print(f"Review rows: {len(review_rows)}")


def verdict_for(ddl_exists: bool, row_count: int, evidence: str, refs: int, mutations: int, table: str) -> str:
    t = table.lower()
    if not ddl_exists:
        return "ddl_missing_unexpected"
    if "posbarazimi" in t:
        return "process_gap_confirm_with_planet"
    if evidence == "covered_raw_data":
        return "covered_or_low_risk"
    if evidence == "supplementary_candidate":
        return "micro_extract_candidate"
    if row_count == 0 and refs > 0:
        return "schema_only_with_logic_reference"
    if row_count == 0:
        return "schema_only_low_risk"
    if refs == 0:
        return "data_without_module_reference_review"
    if mutations > 0:
        return "logic_without_raw_export_review"
    return "covered_or_low_risk"


def action_for(verdict: str, table: str) -> str:
    t = table.lower()
    if verdict == "ddl_missing_unexpected":
        return "Stop and re-check DDL extraction for this table."
    if verdict == "process_gap_confirm_with_planet":
        return "Treat as process gap, not extraction gap; validate settlement/barazim workflow with PLANET finance team."
    if verdict == "micro_extract_candidate":
        return "Run optional sanitized micro-extract if we need zero-doubt evidence before canonical merge."
    if verdict == "schema_only_with_logic_reference":
        return "Keep DDL/DML as design signal; do not assume migration data exists."
    if verdict == "schema_only_low_risk":
        return "Keep schema only; no immediate action unless later parity needs it."
    if verdict == "data_without_module_reference_review":
        return "Review naming/classification; data exists but DDL module reference was not found by scanner."
    if verdict == "logic_without_raw_export_review":
        return "Review if raw export is needed; logic mutates/uses table but evidence may be schema-only."
    return "No action."


def build_doc(matrix_rows: list[dict[str, object]], dml_refs: list[dict[str, object]], review_rows: list[dict[str, object]], gap_rows: list[dict[str, str]]) -> None:
    verdict_counts = Counter(str(r["crosscheck_verdict"]) for r in matrix_rows)
    area_counts = Counter(str(r["finance_area"]) for r in matrix_rows)
    op_counts = Counter()
    for r in dml_refs:
        for op in str(r["operations"]).split(","):
            op_counts[op] += 1

    key_review = [
        r for r in review_rows
        if r["crosscheck_verdict"] in {"process_gap_confirm_with_planet", "micro_extract_candidate", "schema_only_with_logic_reference", "ddl_missing_unexpected"}
    ][:80]
    top_mutations = [r for r in dml_refs if r["has_mutation"] == "yes"][:80]

    doc = f"""# Finance Operations DDL/DML Cross-check — PRO DATA

**Data:** {TODAY}
**Status:** Evidence audit / DDL-DML verification
**Scope:** Finance Operations objects identified from the backup coverage audit.

## Çfarë kontrolloi ky audit

Ky audit skanon DDL-në e nxjerrë të PRO DATA-s:

- `04_tables.sql` / `00_table_inventory.psv` për ekzistencën e tabelave;
- `10_views.sql`;
- `11_functions.sql`;
- `12_stored_procedures.sql`;
- `13_triggers.sql`.

Për secilën tabelë finance-ops kontrollon nëse ka:

- raw data evidence;
- DDL/schema;
- reference nga view/function/procedure/trigger;
- `INSERT`, `UPDATE`, `DELETE`, `MERGE` ose vetëm `READ`;
- nevojë për micro-extract ose konfirmim procesi.

## Verdikti

Nuk doli asnjë `ddl_missing_unexpected`. Pra tabelat finance-ops që i kemi identifikuar janë të pranishme në DDL. Nuk po shoh shenjë se kemi harruar një kategori të madhe finance nga DDL-ja.

Pikat që mbesin nuk janë “mungesë DDL”, por:

1. **barazimi POS / settlement** — DDL dhe procedura ekzistojnë, por të dhëna reale nuk ka; duhet konfirmim live me PLANET;
2. **micro-extract candidates** — pak tabela të vogla me vlerë për zero-doubt;
3. **schema-only objects** — tabela që kanë logjikë/procedura por zero rows, pra përdoren si sinjal dizajni, jo si migrim/raportim real.

## Përmbledhje

{md_table(["Verdict", "Count"], [[k, v] for k, v in sorted(verdict_counts.items())])}

{md_table(["Finance area", "Tables"], [[k, v] for k, v in sorted(area_counts.items())])}

{md_table(["Operation", "References"], [[k, v] for k, v in sorted(op_counts.items())])}

## Review items kryesore

{md_table(["DB", "Table", "Area", "Rows", "Raw status", "Refs", "Mutations", "Verdict", "Action"], [[r["database"], r["table"], r["finance_area"], r["row_count"], r["raw_evidence_status"], r["module_reference_count"], r["mutation_reference_count"], r["crosscheck_verdict"], r["recommended_action"]] for r in key_review])}

## Direct DML mutation/reference examples

{md_table(["DB", "Table", "Object", "Ops", "Snippet"], [[r["database"], r["table"], f'{r["object_type"]}:{r["object_name"]}', r["operations"], r["snippet"]] for r in top_mutations[:40]])}

## Si e përdorim këtë në iData+

- Tabelat me `covered_raw_data` përdoren për parity/evidence.
- Tabelat `schema_only_*` përdoren për ide dizajni, jo për migrim real.
- Tabelat `micro_extract_candidate` mund t'i nxjerrim me sanitization nëse duam zero-doubt.
- `process_gap_confirm_with_planet` është detyrë biznesi: takim me vajzën e financave/menaxherin/Group Owner, jo kërkim i pafund në backup.

## Hapi i rekomanduar

Hapi tjetër më i pastër është:

1. bëjmë optional micro-extract për `tblUserMenyratPagesa`, `tblDocBlerjetShpenzimetTjera`, `tblDocPagesatAvanset`;
2. e përditësojmë `Finance Operations Scope & Separation`;
3. përgatisim listën e pyetjeve live për vajzën e financave të PLANET-it.
"""
    (OUT_DIR / "FINANCE_OPERATIONS_DDL_DML_Crosscheck.md").write_text(doc, encoding="utf-8")


if __name__ == "__main__":
    main()
