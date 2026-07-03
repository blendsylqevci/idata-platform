#!/usr/bin/env python3
"""
PRO DATA supplementary DML/data extract — Wave 1.

Purpose:
  Close the most important data gaps found by
  outputs/prodata_backup_completeness_audit_2026-06-28.

Scope:
  Accounting/costing/fiscal/setup evidence only:
    - Costing / COGS
    - Opening balances
    - Price/deletion/correction history
    - Fiscal evidence
    - Missing POS financial tables linked to fiscal evidence
    - Small setup/master tables needed for accounting parity

Excluded intentionally:
  - Audit/user activity large logs -> Wave 3 with sample/sanitization policy.
  - Restaurant/KDS/table workflow -> Wave 2 operational review.
  - RBAC/user security tables -> Wave 3 sanitized reference only.

This script is read-only against SQL Server.
It writes compressed PSV files and a manifest under outputs/.
"""

from __future__ import annotations

import csv
import datetime as dt
import gzip
import hashlib
import os
import re
import shutil
import subprocess
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from extract_prodata_ddl import CONTAINER, SQLCMD, get_sa_password  # noqa: E402


AUDIT_ROOT = Path("outputs/prodata_backup_completeness_audit_2026-06-28")
OUT_ROOT = Path("outputs/prodata_supplementary_extract_wave1_2026-06-28")
RAW_DIR = OUT_ROOT / "raw_supplementary_tables"
MANIFEST = OUT_ROOT / "EXTRACTION_MANIFEST.md"

INCLUDE_MODULES = {
    "Costing / COGS",
    "Opening balances",
    "History / audit",
    "Deleted/correction history",
    "Fiscal / SEF evidence",
    "Accounting documents",
    "Accounting / GL setup",
    "Master data",
    "Inventory / stock",
    "POS financial",
}

DB_PREFIX = {
    "PRODATA_PLANET2": "planet2",
    "PRODATA_PLANET_RESORT": "planet_resort",
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


def load_targets() -> list[ExportTarget]:
    path = AUDIT_ROOT / "table_coverage_matrix.csv"
    with path.open("r", encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))

    targets: list[ExportTarget] = []
    for row in rows:
        if row["priority"] != "A":
            continue
        if row["module"] not in INCLUDE_MODULES:
            continue
        # Wave 1 must be deterministic across reruns. After the completeness audit
        # learns to detect this folder, the same tables move from
        # `supplementary_extract_required` to `covered_by_existing_export`.
        # Still include them here if their evidence source is the Wave 1 folder.
        is_required_now = row["action"] == "supplementary_extract_required"
        is_previous_wave1 = "prodata_supplementary_extract_wave1_2026-06-28" in row.get("existing_export", "")
        if not (is_required_now or is_previous_wave1):
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


def sh(cmd: list[str], *, input_text: str | None = None, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        cmd,
        input=input_text,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=check,
    )


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
    if proc.returncode != 0 or re.search(r"^Msg \\d+", proc.stdout, flags=re.MULTILINE):
        raise RuntimeError(f"SQL scalar failed for {database}: {proc.stderr}\n{proc.stdout[:2000]}")
    lines = [line.strip() for line in proc.stdout.splitlines() if line.strip() and not re.match(r"^-+$", line.strip())]
    return lines[-1] if lines else ""


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
    if re.search(r"^Msg \\d+", preview, flags=re.MULTILINE):
        raise RuntimeError(f"SQL Server error in export output for {target.database}.{target.table}: {preview[:2000]}")

    actual_count = run_sql_scalar(
        password,
        target.database,
        f"SELECT COUNT_BIG(*) FROM [{target.schema}].[{target.table}];",
    )

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


def write_manifest(rows: list[dict[str, str]], started_at: str, finished_at: str) -> None:
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    csv_path = OUT_ROOT / "wave1_export_manifest.csv"
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
    lines = [
        "# PRO DATA Supplementary Extract — Wave 1",
        "",
        f"- Started: `{started_at}`",
        f"- Finished: `{finished_at}`",
        f"- Docker container: `{CONTAINER}`",
        f"- SQLCMD path: `{SQLCMD}`",
        "- Mode: read-only DML/data extraction from restored SQL Server databases",
        "- Secrets: passwords/PIN/mac identifiers are not printed; RBAC/user sensitive tables intentionally excluded from Wave 1",
        "",
        "## Scope",
        "",
        "Wave 1 covers accounting/costing/fiscal/setup evidence required before implementing accounting, COGS, opening-balance migration, fiscal evidence mapping and correction/reversal behavior.",
        "",
        "Excluded for later waves:",
        "",
        "- Wave 2: restaurant/KDS/table workflow review.",
        "- Wave 3: audit logs, user logs, RBAC/security tables with sanitization.",
        "",
        "## Summary",
        "",
        f"- Tables exported: `{len(rows)}`",
        f"- Data rows exported: `{total_rows:,}`",
        f"- Compressed output size: `{total_size:,}` bytes",
        "",
        "## Files",
        "",
        "| Database | Table | Rows | Module | File |",
        "| --- | --- | ---: | --- | --- |",
    ]
    for r in rows:
        lines.append(
            f"| {r['database']} | {r['table']} | {int(r['actual_rows']):,} | {r['module']} | `{r['file']}` |"
        )
    lines.extend(
        [
            "",
            "## Evidence CSV",
            "",
            "- `wave1_export_manifest.csv`",
            "",
            "## Completeness note",
            "",
            "After this extract, rerun `tools/build_prodata_backup_completeness_audit.py` only after its coverage logic is taught to detect this Wave 1 output folder. Until then, this bundle is the evidence that Wave 1 candidate tables were exported.",
        ]
    )
    MANIFEST.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")


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
        # Keep reruns deterministic; this folder is generated evidence.
        shutil.rmtree(OUT_ROOT)
    RAW_DIR.mkdir(parents=True, exist_ok=True)

    password = get_sa_password()
    targets = load_targets()
    rows: list[dict[str, str]] = []
    print(f"Wave 1 targets: {len(targets)}")
    for idx, target in enumerate(targets, 1):
        print(f"[{idx}/{len(targets)}] Exporting {target.database}.{target.table} ({target.row_count_expected} rows)")
        rows.append(stream_table_export(password, target))

    finished_at = dt.datetime.now().astimezone().isoformat(timespec="seconds")
    write_manifest(rows, started_at, finished_at)
    zip_path = make_zip()
    print(f"Wrote {MANIFEST}")
    print(f"Wrote {OUT_ROOT / 'wave1_export_manifest.csv'}")
    print(f"Wrote {zip_path} ({zip_path.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
