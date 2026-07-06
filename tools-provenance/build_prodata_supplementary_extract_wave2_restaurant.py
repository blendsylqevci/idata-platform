#!/usr/bin/env python3
"""
PRO DATA supplementary DML/data extract — Wave 2.

Purpose:
  Close the restaurant/POS/KDS operational extraction gap found by
  outputs/prodata_backup_completeness_audit_2026-06-28.

Scope:
  - Restaurant table/floor/open-order/KDS tables with rows.
  - Restaurant/POS/KDS schema-only tables that are referenced by procedures.
  - Procedure/function/trigger snippets that explain the operational flow.

Excluded intentionally:
  - Audit/user activity large logs -> Wave 3 with sample/sanitization policy.
  - User/RBAC security tables -> Wave 3 sanitized reference only.

This script is read-only against SQL Server.
It writes compressed PSV files, schema/procedure evidence and a review document under outputs/.
"""

from __future__ import annotations

import csv
import datetime as dt
import gzip
import hashlib
import re
import shutil
import subprocess
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from extract_prodata_ddl import CONTAINER, SQLCMD, get_sa_password, safe_name  # noqa: E402


AUDIT_ROOT = Path("outputs/prodata_backup_completeness_audit_2026-06-28")
DDL_ROOT = Path("outputs/prodata_ddl_extracted_2026-06-28/databases")
OUT_ROOT = Path("outputs/prodata_supplementary_extract_wave2_restaurant_2026-06-28")
RAW_DIR = OUT_ROOT / "raw_restaurant_tables"
SCHEMA_DIR = OUT_ROOT / "schema_review"
PROC_DIR = OUT_ROOT / "procedure_review"
SAMPLES_DIR = OUT_ROOT / "sample_previews"
MANIFEST = OUT_ROOT / "EXTRACTION_MANIFEST.md"
REVIEW = OUT_ROOT / "PRODATA_Restaurant_POS_KDS_Operational_Review.md"

DB_PREFIX = {
    "PRODATA_PLANET2": "planet2",
    "PRODATA_PLANET_RESORT": "planet_resort",
}

RESTAURANT_TERMS = (
    "rest",
    "kuzhin",
    "tavolin",
    "porosi",
    "posbarazimi",
    "handheld",
)

# Tables that matter even when the restored backup has zero rows. They explain
# model affordances or missing/unused PRO DATA features for PLANET.
ZERO_ROW_REFERENCE_TABLES = {
    "tblDocPOSBarazimiMaster",
    "tblDocPOSBarazimiDetalet",
    "tblFiskalRestorantPendingOrders",
    "tblKomentetRestorant",
    "tblRestorantTavolinatKyqjet",
    "tblTMPDocPOSBarazimiDetalet",
    "tblTMPRestTableOrders",
    "tblHandHeldPorosia",
    "tblHandHeldShitja",
    "tblHandHeldTempSalesDocument",
    "tblDocPorositeShitjetMaster",
    "tblDocPorositeShitjetDetalet",
    "tblDocPorositeShitjetMasterDEL",
    "tblDocPorositeShitjetDetaletDEL",
}


@dataclass
class ExportTarget:
    database: str
    schema: str
    table: str
    row_count_expected: int
    columns: int
    module: str
    priority: str
    export_mode: str
    reason: str

    @property
    def prefix(self) -> str:
        return DB_PREFIX[self.database]

    @property
    def output_name(self) -> str:
        return f"{self.prefix}_{self.table}.psv.gz"

    @property
    def output_path(self) -> Path:
        return RAW_DIR / self.output_name


def sh(cmd: list[str], *, input_text: str | None = None, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        cmd,
        input=input_text,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=check,
    )


def load_targets() -> list[ExportTarget]:
    path = AUDIT_ROOT / "table_coverage_matrix.csv"
    with path.open("r", encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))

    targets: list[ExportTarget] = []
    for row in rows:
        if row["priority"] != "A":
            continue
        if row["module"] != "Restaurant / KDS / tables":
            continue
        is_required_now = row["action"] == "supplementary_extract_required"
        is_previous_wave2 = "prodata_supplementary_extract_wave2_restaurant_2026-06-28" in row.get("existing_export", "")
        if not (is_required_now or is_previous_wave2):
            continue
        if int(row["row_count"]) <= 0:
            continue
        targets.append(
            ExportTarget(
                database=row["database"],
                schema=row["schema"],
                table=row["table"],
                row_count_expected=int(row["row_count"]),
                columns=int(row["columns"]),
                module=row["module"],
                priority=row["priority"],
                export_mode=row["export_mode"],
                reason=row["reason"],
            )
        )
    targets.sort(key=lambda t: (t.database, -t.row_count_expected, t.table.lower()))
    return targets


def run_sql_scalar(password: str, database: str, sql: str) -> str:
    cmd = [
        "docker",
        "exec",
        "-i",
        CONTAINER,
        SQLCMD,
        "-S",
        "localhost",
        "-U",
        "sa",
        "-P",
        password,
        "-C",
        "-d",
        database,
        "-h",
        "-1",
        "-W",
        "-w",
        "65535",
    ]
    proc = sh(cmd, input_text="SET NOCOUNT ON;\n" + sql.strip() + "\n", check=False)
    if proc.returncode != 0 or re.search(r"^Msg \d+", proc.stdout, flags=re.MULTILINE):
        raise RuntimeError(f"SQL scalar failed for {database}: {proc.stderr}\n{proc.stdout[:2000]}")
    lines = [line.strip() for line in proc.stdout.splitlines() if line.strip() and not re.match(r"^-+$", line.strip())]
    return lines[-1] if lines else ""


def run_sql_text(password: str, database: str, sql: str) -> str:
    cmd = [
        "docker",
        "exec",
        "-i",
        CONTAINER,
        SQLCMD,
        "-S",
        "localhost",
        "-U",
        "sa",
        "-P",
        password,
        "-C",
        "-d",
        database,
        "-W",
        "-w",
        "65535",
        "-s",
        "|",
    ]
    proc = sh(cmd, input_text="SET NOCOUNT ON;\n" + sql.strip() + "\n", check=False)
    if proc.returncode != 0 or re.search(r"^Msg \d+", proc.stdout, flags=re.MULTILINE):
        raise RuntimeError(f"SQL text failed for {database}: {proc.stderr}\n{proc.stdout[:2000]}")
    return proc.stdout.replace("\r\n", "\n")


def stream_table_export(password: str, target: ExportTarget) -> dict[str, str]:
    RAW_DIR.mkdir(parents=True, exist_ok=True)
    sql = f"""
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ANSI_PADDING ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SELECT *
FROM [{target.schema}].[{target.table}];
"""
    cmd = [
        "docker",
        "exec",
        "-i",
        CONTAINER,
        SQLCMD,
        "-S",
        "localhost",
        "-U",
        "sa",
        "-P",
        password,
        "-C",
        "-d",
        target.database,
        "-W",
        "-w",
        "65535",
        "-s",
        "|",
    ]

    proc = subprocess.Popen(
        cmd,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    assert proc.stdin is not None
    assert proc.stdout is not None
    proc.stdin.write(sql.encode("utf-8"))
    proc.stdin.close()

    sha = hashlib.sha256()
    bytes_written = 0
    first_bytes = bytearray()
    with gzip.open(target.output_path, "wb", compresslevel=6) as gz:
        while True:
            chunk = proc.stdout.read(1024 * 1024)
            if not chunk:
                break
            if len(first_bytes) < 4096:
                first_bytes.extend(chunk[: 4096 - len(first_bytes)])
            gz.write(chunk)
            sha.update(chunk)
            bytes_written += len(chunk)

    stderr = proc.stderr.read().decode("utf-8", errors="replace") if proc.stderr else ""
    returncode = proc.wait()
    if returncode != 0:
        raise RuntimeError(f"Export failed for {target.database}.{target.table}: {stderr[:2000]}")

    preview = first_bytes.decode("utf-8", errors="replace")
    if re.search(r"^Msg \d+", preview, flags=re.MULTILINE):
        raise RuntimeError(f"SQL Server error in export output for {target.database}.{target.table}: {preview[:2000]}")

    actual_count = run_sql_scalar(
        password,
        target.database,
        f"SELECT COUNT_BIG(*) FROM [{target.schema}].[{target.table}];",
    )

    write_sample_preview(target)

    return {
        "database": target.database,
        "schema": target.schema,
        "table": target.table,
        "module": target.module,
        "priority": target.priority,
        "export_mode": target.export_mode,
        "expected_rows": str(target.row_count_expected),
        "actual_rows": actual_count,
        "columns": str(target.columns),
        "file": str(target.output_path),
        "file_size_bytes": str(target.output_path.stat().st_size),
        "raw_stdout_bytes_before_gzip": str(bytes_written),
        "sha256_uncompressed_stream": sha.hexdigest(),
        "reason": target.reason,
    }


def write_sample_preview(target: ExportTarget, max_lines: int = 14) -> None:
    SAMPLES_DIR.mkdir(parents=True, exist_ok=True)
    sample_path = SAMPLES_DIR / f"{target.prefix}_{target.table}_head.txt"
    lines: list[str] = []
    with gzip.open(target.output_path, "rt", encoding="utf-8", errors="replace") as f:
        for idx, line in enumerate(f):
            if idx >= max_lines:
                break
            lines.append(line.rstrip("\n"))
    sample_path.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")


def load_restaurant_inventory() -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for database in DB_PREFIX:
        inv = DDL_ROOT / database / "00_table_inventory.psv"
        with inv.open("r", encoding="utf-8", newline="") as f:
            reader = csv.DictReader(f, delimiter="|")
            for row in reader:
                table = row.get("Table", "")
                lower = table.lower()
                if any(term in lower for term in RESTAURANT_TERMS):
                    rows.append(
                        {
                            "database": database,
                            "schema": row.get("Schema", "dbo"),
                            "table": table,
                            "row_count": row.get("RowsEstimate", "0"),
                            "columns": row.get("Columns", "0"),
                            "reference_reason": "nonzero operational evidence"
                            if int(row.get("RowsEstimate", "0") or 0) > 0
                            else ("schema/procedure reference" if table in ZERO_ROW_REFERENCE_TABLES else "schema-only related table"),
                        }
                    )
    rows.sort(key=lambda r: (r["database"], -int(r["row_count"]), r["table"].lower()))
    return rows


def write_restaurant_inventory(rows: list[dict[str, str]]) -> None:
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    path = OUT_ROOT / "restaurant_pos_kds_table_inventory.csv"
    fields = ["database", "schema", "table", "row_count", "columns", "reference_reason"]
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def write_column_catalog(password: str, inventory: list[dict[str, str]]) -> None:
    catalog_path = OUT_ROOT / "restaurant_pos_kds_column_catalog.psv"
    all_lines: list[str] = []
    for database in DB_PREFIX:
        tables = sorted({r["table"] for r in inventory if r["database"] == database})
        table_values = ", ".join("N'" + t.replace("'", "''") + "'" for t in tables)
        sql = f"""
SELECT
    DB_NAME() AS database_name,
    s.name AS schema_name,
    t.name AS table_name,
    c.column_id,
    c.name AS column_name,
    typ.name AS data_type,
    c.max_length,
    c.precision,
    c.scale,
    c.is_nullable
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types typ ON typ.user_type_id = c.user_type_id
WHERE t.name IN ({table_values})
ORDER BY t.name, c.column_id;
"""
        text = run_sql_text(password, database, sql)
        lines = [line for line in text.splitlines() if line.strip()]
        if not all_lines:
            all_lines.extend(lines)
        else:
            # skip duplicated header/separator from later databases
            all_lines.extend(lines[2:] if len(lines) >= 2 else lines)
    catalog_path.write_text("\n".join(all_lines).rstrip() + "\n", encoding="utf-8")


def sql_blocks(text: str) -> list[str]:
    return [block.strip() for block in re.split(r"(?im)^\s*GO\s*$", text) if block.strip()]


def block_name(block: str, fallback: str) -> str:
    marker = re.search(r"--\s*Object:\s*(.+)", block)
    if marker:
        return marker.group(1).strip().replace("[", "").replace("]", "").replace(".", "_")
    create = re.search(
        r"\bCREATE\s+(?:OR\s+ALTER\s+)?(?:PROCEDURE|PROC|FUNCTION|TRIGGER|VIEW)\s+((?:\[?[A-Za-z0-9_]+\]?\.)?\[?[A-Za-z0-9_]+\]?)",
        block,
        flags=re.IGNORECASE,
    )
    if create:
        return create.group(1).strip().replace("[", "").replace("]", "").replace(".", "_")
    return fallback


def extract_schema_and_code_reviews(inventory: list[dict[str, str]]) -> list[dict[str, str]]:
    SCHEMA_DIR.mkdir(parents=True, exist_ok=True)
    PROC_DIR.mkdir(parents=True, exist_ok=True)
    rows: list[dict[str, str]] = []
    table_names = sorted({r["table"] for r in inventory})
    table_pattern = re.compile(r"\b(" + "|".join(re.escape(t) for t in table_names) + r")\b", flags=re.IGNORECASE)
    reference_terms = table_names + ["KitchenReady", "StatusPreparation", "DailyPorosiaCount", "Tavolina", "barazimi"]

    for database in DB_PREFIX:
        db_dir = DDL_ROOT / database
        prefix = DB_PREFIX[database]

        # Table definitions, indexes, triggers are useful even for zero-row tables.
        for ddl_file, folder_label in [
            ("04_tables.sql", "tables"),
            ("09_indexes.sql", "indexes"),
            ("13_triggers.sql", "triggers"),
        ]:
            path = db_dir / ddl_file
            text = path.read_text(encoding="utf-8", errors="replace")
            matches = [block for block in sql_blocks(text) if table_pattern.search(block)]
            out = SCHEMA_DIR / f"{prefix}_{folder_label}_restaurant_related.sql"
            out.write_text(("\n\nGO\n\n".join(matches)).rstrip() + "\n", encoding="utf-8")
            rows.append(
                {
                    "database": database,
                    "source_file": ddl_file,
                    "object_type": folder_label,
                    "objects_or_blocks": str(len(matches)),
                    "output_file": str(out),
                    "reason": "restaurant/POS/KDS schema reference",
                }
            )

        # Procedures and functions can encode the real workflow.
        for ddl_file, object_type in [
            ("12_stored_procedures.sql", "stored_procedure"),
            ("11_functions.sql", "function"),
        ]:
            path = db_dir / ddl_file
            text = path.read_text(encoding="utf-8", errors="replace")
            selected: list[tuple[str, str]] = []
            for idx, block in enumerate(sql_blocks(text), 1):
                lower = block.lower()
                if any(term.lower() in lower for term in reference_terms):
                    selected.append((block_name(block, f"{object_type}_{idx:04d}"), block))

            bundle = PROC_DIR / f"{prefix}_{object_type}s_restaurant_related.sql"
            bundle.write_text(("\n\nGO\n\n".join(block for _, block in selected)).rstrip() + "\n", encoding="utf-8")
            rows.append(
                {
                    "database": database,
                    "source_file": ddl_file,
                    "object_type": object_type,
                    "objects_or_blocks": str(len(selected)),
                    "output_file": str(bundle),
                    "reason": "procedure/function references restaurant operational tables",
                }
            )

            # Also write individual files for easier reading.
            for name, block in selected:
                out = PROC_DIR / f"{prefix}_{safe_name(name)}.sql"
                out.write_text(block.rstrip() + "\n", encoding="utf-8")

    ref_path = OUT_ROOT / "restaurant_code_reference_map.csv"
    fields = ["database", "source_file", "object_type", "objects_or_blocks", "output_file", "reason"]
    with ref_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)
    return rows


def md_table(rows: list[dict[str, str]], columns: list[str], limit: int | None = None) -> str:
    if limit is not None:
        rows = rows[:limit]
    if not rows:
        return "_Asnjë rresht._\n"
    out = ["| " + " | ".join(columns) + " |", "| " + " | ".join("---" for _ in columns) + " |"]
    for row in rows:
        out.append("| " + " | ".join(str(row.get(col, "")).replace("|", "\\|") for col in columns) + " |")
    return "\n".join(out) + "\n"


def write_manifest(
    rows: list[dict[str, str]],
    inventory: list[dict[str, str]],
    code_refs: list[dict[str, str]],
    key_procs: list[dict[str, str]],
    started_at: str,
    finished_at: str,
) -> None:
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    csv_path = OUT_ROOT / "wave2_restaurant_export_manifest.csv"
    fields = [
        "database",
        "schema",
        "table",
        "module",
        "priority",
        "export_mode",
        "expected_rows",
        "actual_rows",
        "columns",
        "file",
        "file_size_bytes",
        "raw_stdout_bytes_before_gzip",
        "sha256_uncompressed_stream",
        "reason",
    ]
    with csv_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)

    total_rows = sum(int(r["actual_rows"]) for r in rows)
    total_size = sum(int(r["file_size_bytes"]) for r in rows)
    nonzero_inventory = [r for r in inventory if int(r["row_count"]) > 0]
    schema_only = [r for r in inventory if int(r["row_count"]) == 0]
    lines = [
        "# PRO DATA Supplementary Extract — Wave 2 Restaurant/POS/KDS",
        "",
        f"- Started: `{started_at}`",
        f"- Finished: `{finished_at}`",
        f"- Docker container: `{CONTAINER}`",
        f"- SQLCMD path: `{SQLCMD}`",
        "- Mode: read-only DML/data extraction from restored SQL Server databases + DDL/procedure review",
        "- Scope: PLANET restaurant/POS/KDS operational evidence",
        "",
        "## Summary",
        "",
        f"- Raw tables exported: `{len(rows)}`",
        f"- Data rows exported: `{total_rows:,}`",
        f"- Compressed raw output size: `{total_size:,}` bytes",
        f"- Restaurant/POS/KDS tables in inventory: `{len(inventory)}`",
        f"- Non-zero restaurant/POS/KDS tables: `{len(nonzero_inventory)}`",
        f"- Schema-only related tables reviewed: `{len(schema_only)}`",
        f"- Code-reference bundles: `{len(code_refs)}`",
        f"- Focused key procedures indexed: `{len(key_procs)}`",
        "",
        "## Exported raw tables",
        "",
        "| Database | Table | Rows | File |",
        "| --- | --- | ---: | --- |",
    ]
    for r in rows:
        lines.append(f"| {r['database']} | {r['table']} | {int(r['actual_rows']):,} | `{r['file']}` |")
    lines.extend(
        [
            "",
            "## Evidence files",
            "",
            "- `wave2_restaurant_export_manifest.csv`",
            "- `restaurant_pos_kds_table_inventory.csv`",
            "- `restaurant_pos_kds_column_catalog.psv`",
            "- `restaurant_code_reference_map.csv`",
            "- `restaurant_key_procedure_index.csv`",
            "- `raw_restaurant_tables/*.psv.gz`",
            "- `sample_previews/*_head.txt`",
            "- `schema_review/*.sql`",
            "- `procedure_review/*.sql`",
            "- `PRODATA_Restaurant_POS_KDS_Operational_Review.md`",
        ]
    )
    MANIFEST.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")


def write_review(
    rows: list[dict[str, str]],
    inventory: list[dict[str, str]],
    code_refs: list[dict[str, str]],
    key_procs: list[dict[str, str]],
) -> None:
    nonzero = [r for r in inventory if int(r["row_count"]) > 0]
    zero_reference = [r for r in inventory if int(r["row_count"]) == 0 and r["table"] in ZERO_ROW_REFERENCE_TABLES]
    export_rows = [
        {
            "Database": r["database"],
            "Table": r["table"],
            "Rows": f"{int(r['actual_rows']):,}",
            "Meaning": restaurant_meaning(r["table"]),
        }
        for r in rows
    ]
    zero_rows = [
        {
            "Database": r["database"],
            "Table": r["table"],
            "Columns": r["columns"],
            "Why review": restaurant_meaning(r["table"]),
        }
        for r in zero_reference
    ]
    code_rows = [
        {
            "Database": r["database"],
            "Type": r["object_type"],
            "Blocks": r["objects_or_blocks"],
            "File": r["output_file"],
        }
        for r in code_refs
    ]
    key_proc_rows = [
        {
            "Database": r["database"],
            "Category": r["category"],
            "Procedure": r["procedure"],
            "Why it matters": r["reason"],
        }
        for r in key_procs
    ]

    lines = [
        "# PRO DATA Restaurant / POS / KDS Operational Review",
        "",
        f"- Generated: `{dt.datetime.now().astimezone().isoformat(timespec='seconds')}`",
        "- Source: restored PRO DATA backup + DDL/procedure extract",
        "- Scope: PLANET restaurant workflow evidence, not a migration script",
        "",
        "## Verdict",
        "",
        "Wave 2 e mbyll pjesën kryesore të evidence-it për PLANET restaurant/POS/KDS nga backup-i. PRO DATA na jep prova reale për tavolinat, porositë e hapura, queue-in e kuzhinës dhe numërimin ditor të porosive, por **nuk duhet kopjuar 1:1 si arkitekturë moderne**. Modeli i PRO DATA duket i bazuar në tabela të përkohshme/open-order (`tblTMPRestShitjet*`) dhe procedura që punojnë direkt mbi to; për iData+ duhet model më i fortë: table-session/order/order-item/kitchen-ticket/cash-shift/manager-approval/audit.",
        "",
        "## Raw tables exported",
        "",
        md_table(export_rows, ["Database", "Table", "Rows", "Meaning"]).rstrip(),
        "",
        "## Schema-only/reference tables reviewed",
        "",
        "Këto tabela kanë zero rows në backup, por janë të rëndësishme për të kuptuar çfarë ka ekzistuar ose çfarë nuk është përdorur realisht te PLANET.",
        "",
        md_table(zero_rows, ["Database", "Table", "Columns", "Why review"], limit=40).rstrip(),
        "",
        "## DDL/procedure evidence",
        "",
        md_table(code_rows, ["Database", "Type", "Blocks", "File"]).rstrip(),
        "",
        "Shënim: procedure bundle është qëllimisht i gjerë që të mos humbim evidence. Për lexim praktik, përdor këtë indeks të fokusuar:",
        "",
        md_table(key_proc_rows, ["Database", "Category", "Procedure", "Why it matters"], limit=80).rstrip(),
        "",
        "## What PRO DATA appears to do",
        "",
        "- `tblRestTavolinat` mban konfigurimin/listën e tavolinave/lokacioneve; backup-i ka 248 rreshta në PLANET2 dhe 418 në PLANET_RESORT.",
        "- `tblTMPRestShitjet` mban porosi të hapura/current për tavolinë; ka fusha financiare, TVSH, user/device, koment, `KitchenReady`, `DailyPorosiaCount`, `Porosia`, `Printed`, `Faturo`, `Fiskalizuar`.",
        "- `tblTMPRestShitjetKuzhina` është queue/status për kuzhinë; indekset mbi `KitchenReady`, `Tavolina`, `DailyPorosiaCount` tregojnë përdorim për KDS/kuzhinë.",
        "- `tblRestDailyPorosiaCount` mban numërim ditor të porosive.",
        "- `tblTMPRestShitjet_Deleted` + trigger `trg_LogDeletedOpenOrders` tregojnë se fshirjet/anulimet e open orders regjistroheshin, por kjo nuk është e mjaftueshme për standardin tonë të aprovimit me arsye/PIN.",
        "- `tblRestorantTavolinatKyqjet` është model lock/session për tavolinë/kompjuter/user, me cleanup të locks të vjetra në procedura.",
        "- `tblDocPorositeShitjetMaster/Detalet` ekzistojnë si dokumente por në backup kanë shumë pak ose zero rreshta; për workflow-in real të restorantit, evidence-i kryesor është te `tblTMPRestShitjet*`.",
        "- `tblDocPOSBarazimi*` ekziston por është zero-row në backup; pra barazimi që duam në iData+ duhet projektuar si model i ri, jo të supozohet se PLANET e ka përdorur 1:1 në PRO DATA.",
        "",
        "## What we should implement in iData+",
        "",
        "Minimumi profesional për PLANET POS/KDS duhet të jetë ky model, jo thjesht kopje e tabelave PRO DATA:",
        "",
        "- `restaurant_zone` / `floor_plan` / `restaurant_table`: pamja reale e sallave, shankut, jashtë/brenda, multi-shank/multi-depo.",
        "- `table_session`: hapje/mbyllje tavoline, kamarier përgjegjës, transfer me aprovim menaxheri, service day/business day.",
        "- `order` + `order_item`: porosia, dërgim i pjesshëm ose total në kuzhinë/bar, komente, modifiera, status për çdo rresht.",
        "- `kitchen_ticket` + `kds_station`: Grill/Bar/Pizza/Sallata/Expo, timer, ngarkesë, alert në ngjyrë, backup print nëse KDS dështon.",
        "- `payment` + `cash_shift` + `worker_settlement`: cash/kartë/bank/POS terminal, saldo e kamarierit, shpenzime, dorëzim cash, barazim me tavolinat dhe porositë.",
        "- `void/correction/approval`: anulim me arsye, zbritje me PIN, ndryshim artikulli/pije/ushqimi me menaxher, audit append-only.",
        "- `fiscal_outbox`: e ndarë nga sync engine, me status fiskalizimi dhe SEF retry.",
        "- `local_hub`: rruga kamarier → hub LAN → KDS/printer duhet të punojë pa internet.",
        "",
        "## Product rules confirmed by the user that PRO DATA does not fully prove",
        "",
        "Këto janë kërkesa të iData+, edhe nëse backup-i nuk i provon:",
        "",
        "- Kamarieri nuk e sheh shumën totale të tavolinës deri në `Shfaq faturën / Paguaj`; pasi hapet fatura, kthimi mbrapa kërkon aprovim.",
        "- Transfer tavoline te kamarier tjetër vetëm me aprovim menaxheri/pronari.",
        "- Artikull i dërguar në kuzhinë/bar nuk fshihet pa anulim me arsye + PIN/aprovim.",
        "- Shank/bar screen ka status `Porositur / Në proces / Dorëzuar`.",
        "- Pizzeria/Expo mund të ketë ekran të veçantë me alarm nëse porosia nuk merret në kohë.",
        "- Nëse grumbullohen mbi prag tavolina/porosi në pizzeria ose sektor, statusi bëhet i kuq.",
        "- Mbyllja e ndërrimit/service day ndodh automatikisht në 00:00 sipas rregullit të aprovuar, por tavolinat/porositë bartin datën e re sipas business-day logic.",
        "",
        "## Open decisions / evidence still needed from PLANET",
        "",
        "- Floor-plan real: zona, tavolina, shanka, printera, KDS screens, dhe routing per lokacion.",
        "- Numri final dhe rolet e KDS screens: backup tregon modelin, jo planin final 10-12 screens.",
        "- Lista e pajisjeve ekzistuese: printera, POS terminals, monitors, tablets, network/UPS.",
        "- Rregullat e sakta të barazimit të kamarierëve/arkës me Group Owner/kontabilistin.",
        "- A do hyjë AI Invoice Scanner në MVP apo Roadmap/Later; për momentin duhet mbajtur jashtë MVP pa vendim.",
        "",
        "## Implementation warning",
        "",
        "PRO DATA është shumë e vlefshme si baseline dhe parity evidence, por te POS/KDS modeli i tij është më shumë `legacy operational pattern` sesa arkitekturë finale. iData+ duhet të marrë konceptet që funksionojnë — tavolina, porosi, daily order count, kitchen queue, table lock, deletion evidence — dhe t'i rindërtojë me model domain të pastër, offline-first, auditueshëm dhe të gatshëm për SEF.",
    ]
    REVIEW.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")


def restaurant_meaning(table: str) -> str:
    meanings = {
        "tblRestTavolinat": "table/floor/location setup baseline",
        "tblRestDailyPorosiaCount": "daily order numbering/counter",
        "tblTMPRestShitjet": "open/current restaurant table order lines",
        "tblTMPRestShitjetKuzhina": "kitchen/KDS queue/status evidence",
        "tblTMPRestShitjet_Deleted": "deleted open-order audit via trigger",
        "tblDocPorositeShitjetMaster": "formal sales-order master, lightly used in backup",
        "tblDocPorositeShitjetDetalet": "formal sales-order details, lightly used in backup",
        "tblDocPOSBarazimiMaster": "POS settlement/barazim schema, zero-row in backup",
        "tblDocPOSBarazimiDetalet": "POS settlement/barazim details schema, zero-row in backup",
        "tblFiskalRestorantPendingOrders": "restaurant fiscal pending order schema",
        "tblKomentetRestorant": "restaurant comment/modifier schema",
        "tblRestorantTavolinatKyqjet": "table lock/session schema",
        "tblTMPDocPOSBarazimiDetalet": "temporary settlement details schema",
        "tblTMPRestTableOrders": "temporary table-order helper schema",
        "tblHandHeldPorosia": "handheld/mobile ordering schema",
        "tblHandHeldShitja": "handheld/mobile sale schema",
        "tblHandHeldTempSalesDocument": "handheld temp sale schema",
    }
    return meanings.get(table, "restaurant/POS/KDS related schema/evidence")


def classify_key_procedure(filename: str, text: str) -> tuple[str, str] | None:
    # Use the procedure/function filename only. Searching the whole SQL text is
    # intentionally done in the broad bundle, but it makes this practical index
    # too noisy because many generic documents contain words like "porosi".
    haystack = filename.lower()
    rules: list[tuple[str, tuple[str, ...], str]] = [
        (
            "order_create",
            ("androidaddporosia", "restadditemtotable", "restpostorder", "apirestadditemtotable", "apirestpostorder"),
            "creates/adds restaurant order lines",
        ),
        (
            "kds_queue",
            ("kitchen", "kuzhine", "kuzhina", "artikujtperkuzhine", "statuspreparation"),
            "kitchen/KDS queue and status workflow",
        ),
        (
            "complete_close_fiscal",
            (
                "completeorder",
                "ruajedokumentinrest",
                "getdetailsforfiscalprinterrestporosi",
                "fiskalretrieverestorant",
                "fiskalsavefiscalisationinforestorant",
                "fiskalsaveqrcoderestorant",
                "getfiskalizimidetaletrestorant",
                "restoranttransactionqrcode",
            ),
            "closing/printing/fiscal handoff for restaurant orders",
        ),
        (
            "void_comment",
            ("apirestdeletelineid", "deleterestorantkomenti", "addnewrestorantkomenti", "getkomentetrestorant", "getkomentiitem"),
            "void/comment/delete behavior needing manager approval in iData+",
        ),
        (
            "settlement",
            ("posbarazim", "barazimarka", "barazimipos", "posgetgjendjaarkatari"),
            "POS settlement/barazim evidence or schema",
        ),
        (
            "table_state",
            (
                "gettavolinat",
                "checkintavolina",
                "retrievecurrentorder",
                "retrieveviewtablebill",
                "manageropenorders",
                "managerclosedorders",
                "tavolinatoverview",
                "getrestordernotani",
                "androidgetporosiatavoline",
                "androidgetporositepaprocesuara",
                "regdocrestshitja",
                "restorantcheckintavolina",
                "offlinegetresttavolinat",
                "offlinesaveresttavolinat",
                "openlistotransakstionetrestsales",
            ),
            "table/session/current bill visibility",
        ),
        (
            "manager_report",
            ("topitems", "rptgrdrestshitjet", "rptgrdshitjapospertavolina"),
            "manager view/reporting baseline",
        ),
    ]
    for category, needles, reason in rules:
        if any(needle in haystack for needle in needles):
            return category, reason
    return None


def write_key_procedure_index() -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for path in sorted(PROC_DIR.glob("*.sql")):
        if path.name.endswith("_restaurant_related.sql"):
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        classified = classify_key_procedure(path.name, text)
        if not classified:
            continue
        category, reason = classified
        database = "PRODATA_PLANET_RESORT" if path.name.startswith("planet_resort_") else "PRODATA_PLANET2"
        procedure = path.stem
        procedure = procedure.removeprefix("planet_resort_").removeprefix("planet2_")
        rows.append(
            {
                "database": database,
                "category": category,
                "procedure": procedure,
                "file": str(path),
                "reason": reason,
            }
        )
    rows.sort(key=lambda r: (r["database"], r["category"], r["procedure"].lower()))
    path = OUT_ROOT / "restaurant_key_procedure_index.csv"
    fields = ["database", "category", "procedure", "file", "reason"]
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)
    return rows


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
    started_at = dt.datetime.now().astimezone().isoformat(timespec="seconds")
    if OUT_ROOT.exists():
        shutil.rmtree(OUT_ROOT)
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    RAW_DIR.mkdir(parents=True, exist_ok=True)

    password = get_sa_password()
    targets = load_targets()
    inventory = load_restaurant_inventory()

    print(f"Wave 2 raw targets: {len(targets)}")
    rows: list[dict[str, str]] = []
    for idx, target in enumerate(targets, 1):
        print(f"[{idx}/{len(targets)}] Exporting {target.database}.{target.table} ({target.row_count_expected} rows)")
        rows.append(stream_table_export(password, target))

    write_restaurant_inventory(inventory)
    write_column_catalog(password, inventory)
    code_refs = extract_schema_and_code_reviews(inventory)
    key_procs = write_key_procedure_index()

    finished_at = dt.datetime.now().astimezone().isoformat(timespec="seconds")
    write_manifest(rows, inventory, code_refs, key_procs, started_at, finished_at)
    write_review(rows, inventory, code_refs, key_procs)
    zip_path = make_zip()

    print(f"Wrote {MANIFEST}")
    print(f"Wrote {REVIEW}")
    print(f"Wrote {OUT_ROOT / 'wave2_restaurant_export_manifest.csv'}")
    print(f"Wrote {OUT_ROOT / 'restaurant_pos_kds_table_inventory.csv'}")
    print(f"Wrote {OUT_ROOT / 'restaurant_pos_kds_column_catalog.psv'}")
    print(f"Wrote {OUT_ROOT / 'restaurant_code_reference_map.csv'}")
    print(f"Wrote {OUT_ROOT / 'restaurant_key_procedure_index.csv'}")
    print(f"Wrote {zip_path} ({zip_path.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
