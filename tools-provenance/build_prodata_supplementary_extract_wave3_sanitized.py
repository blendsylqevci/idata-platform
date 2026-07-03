#!/usr/bin/env python3
"""
PRO DATA supplementary extract — Wave 3 sanitized audit/RBAC/config evidence.

Purpose:
  Close the remaining Priority A audit/user-activity evidence gaps and the
  Priority B RBAC/config/reference review gaps without exporting secrets or
  raw user-sensitive data.

Scope:
  - Priority A: Audit / user activity -> schema + row/date coverage + sanitized
    recent samples.
  - Priority B: Users/RBAC, System/config/reference, Attachments/notes/misc ->
    sanitized reference exports.

Hard rule:
  This is evidence for modeling iData+ audit/RBAC/config parity. It is NOT a
  credential migration. Passwords, PINs, MAC/device IDs, tokens, keys and secret
  values are redacted.
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
from extract_prodata_ddl import CONTAINER, SQLCMD, get_sa_password  # noqa: E402


AUDIT_ROOT = Path("outputs/prodata_backup_completeness_audit_2026-06-28")
DDL_ROOT = Path("outputs/prodata_ddl_extracted_2026-06-28/databases")
OUT_ROOT = Path("outputs/prodata_supplementary_extract_wave3_sanitized_2026-06-28")
AUDIT_SAMPLE_DIR = OUT_ROOT / "sanitized_audit_samples"
REFERENCE_DIR = OUT_ROOT / "sanitized_reference"
SUMMARY_DIR = OUT_ROOT / "summaries"
SCHEMA_DIR = OUT_ROOT / "schema_review"
MANIFEST = OUT_ROOT / "EXTRACTION_MANIFEST.md"
REVIEW = OUT_ROOT / "PRODATA_Audit_RBAC_Config_Sanitized_Review.md"

DB_PREFIX = {
    "PRODATA_PLANET2": "planet2",
    "PRODATA_PLANET_RESORT": "planet_resort",
}

SAMPLE_LIMIT_AUDIT = 500

SECRET_NAME_RE = re.compile(
    r"(password|passw|passwd|pwd|pin|mac|token|secret|cert|certificate|private|salt|hash|license|licence|apikey|api_key|connection|string|dsn)",
    re.IGNORECASE,
)
IDENTITY_NAME_RE = re.compile(
    r"(username|useri|user|fullname|full_name|assignedto|createdby|updatedby|insertedby|modifiedby|lastuserupdate|host|hostname|computer|kompjuteri|email|mail|phone|tel|mobile|deviceid|device|paisja|operator)",
    re.IGNORECASE,
)
FREE_TEXT_RE = re.compile(
    r"(description|pershkrim|pershkrimi|memo|koment|komenti|message|error|exception|activity|text|sql|query|details|detail|vlera|footer|logo)",
    re.IGNORECASE,
)
DATE_HINT_RE = re.compile(
    r"(logdt|createdat|updatedat|koha|timestamp|createddate|modifieddate|inserteddate|updateddate|lastlogin|changedate|duedate|date|data)",
    re.IGNORECASE,
)

TEXT_TYPES = {"char", "nchar", "varchar", "nvarchar", "text", "ntext", "xml"}
BINARY_TYPES = {"binary", "varbinary", "image", "timestamp", "rowversion"}
DATE_TYPES = {"date", "datetime", "datetime2", "smalldatetime", "datetimeoffset", "time"}


@dataclass
class Target:
    database: str
    schema: str
    table: str
    row_count_expected: int
    columns_expected: int
    module: str
    priority: str
    export_mode: str
    reason: str
    wave3_mode: str

    @property
    def prefix(self) -> str:
        return DB_PREFIX[self.database]

    @property
    def output_dir(self) -> Path:
        return AUDIT_SAMPLE_DIR if self.wave3_mode == "audit_sample" else REFERENCE_DIR

    @property
    def output_path(self) -> Path:
        # Exact table name after DB prefix, so the completeness audit can map it.
        return self.output_dir / f"{self.prefix}_{self.table}.psv.gz"


def sh(cmd: list[str], *, input_text: str | None = None, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        cmd,
        input=input_text,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=check,
    )


def parse_sqlcmd_psv(text: str) -> list[dict[str, str]]:
    lines = [line.rstrip("\n") for line in text.replace("\r\n", "\n").splitlines() if line.strip()]
    clean: list[str] = []
    for line in lines:
        stripped = line.strip()
        if re.fullmatch(r"[-| ]+", stripped):
            continue
        if re.match(r"^\(\d+ rows? affected\)$", stripped):
            continue
        clean.append(line)
    if not clean:
        return []
    reader = csv.DictReader(clean, delimiter="|")
    return list(reader)


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
        raise RuntimeError(f"SQL failed for {database}: {proc.stderr}\n{proc.stdout[:3000]}")
    return proc.stdout.replace("\r\n", "\n")


def load_targets() -> list[Target]:
    path = AUDIT_ROOT / "table_coverage_matrix.csv"
    with path.open("r", encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))

    targets: list[Target] = []
    for row in rows:
        if int(row["row_count"]) <= 0:
            continue

        is_a_audit = row["priority"] == "A" and row["module"] == "Audit / user activity"
        is_b_reference = row["priority"] == "B" and row["module"] in {
            "Users / RBAC",
            "System/config/reference",
            "Attachments / notes / misc",
        }
        is_previous_wave3 = "prodata_supplementary_extract_wave3_sanitized_2026-06-28" in row.get("existing_export", "")
        if not (is_a_audit or is_b_reference or is_previous_wave3):
            continue

        wave3_mode = "audit_sample" if row["module"] == "Audit / user activity" else "sanitized_reference"
        targets.append(
            Target(
                database=row["database"],
                schema=row["schema"],
                table=row["table"],
                row_count_expected=int(row["row_count"]),
                columns_expected=int(row["columns"]),
                module=row["module"],
                priority=row["priority"],
                export_mode=row["export_mode"],
                reason=row["reason"],
                wave3_mode=wave3_mode,
            )
        )
    targets.sort(key=lambda t: (t.wave3_mode, t.database, t.module, -t.row_count_expected, t.table.lower()))
    return targets


def quote_ident(name: str) -> str:
    return "[" + name.replace("]", "]]") + "]"


def get_columns(password: str, database: str, schema: str, table: str) -> list[dict[str, str]]:
    sql = f"""
SELECT
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
WHERE s.name = N'{schema.replace("'", "''")}'
  AND t.name = N'{table.replace("'", "''")}'
ORDER BY c.column_id;
"""
    return parse_sqlcmd_psv(run_sql_text(password, database, sql))


def choose_date_column(columns: list[dict[str, str]]) -> str | None:
    date_cols = [c for c in columns if c["data_type"].lower() in DATE_TYPES or DATE_HINT_RE.search(c["column_name"])]
    if not date_cols:
        return None
    preferred = ["LogDT", "Koha", "CreatedAt", "CreatedDate", "TimeStamp", "UpdatedAt", "ModifiedDate", "LastLogin", "Data", "DueDate"]
    for name in preferred:
        for c in date_cols:
            if c["column_name"].lower() == name.lower() and c["data_type"].lower() in DATE_TYPES:
                return c["column_name"]
    for c in date_cols:
        if c["data_type"].lower() in DATE_TYPES:
            return c["column_name"]
    return date_cols[0]["column_name"]


def choose_id_column(columns: list[dict[str, str]]) -> str | None:
    for name in ["ID", "id", "ActivityID", "LogID", "ModifiedNo"]:
        for c in columns:
            if c["column_name"] == name:
                return c["column_name"]
    return columns[0]["column_name"] if columns else None


def hash_expr(col_sql: str) -> str:
    return (
        "LEFT(CONVERT(varchar(64), HASHBYTES('SHA2_256', "
        f"CONVERT(varbinary(max), CONVERT(nvarchar(max), {col_sql}))), 2), 16)"
    )


def sanitize_expr(target: Target, col: dict[str, str]) -> str:
    name = col["column_name"]
    data_type = col["data_type"].lower()
    q = quote_ident(name)
    alias = quote_ident(name)
    table_lower = target.table.lower()

    if data_type in BINARY_TYPES:
        return f"CASE WHEN {q} IS NULL THEN NULL ELSE CONCAT('[BINARY len=', CONVERT(varchar(20), DATALENGTH({q})), ']') END AS {alias}"

    if SECRET_NAME_RE.search(name):
        return f"CASE WHEN {q} IS NULL THEN NULL ELSE '[REDACTED_SECRET]' END AS {alias}"

    # System options values can contain paths, connection values, fiscal/device options, etc.
    if table_lower in {"tblsystemappoptions", "tblsystemappoptionsgrid"} and name.lower() in {"vlera", "value"}:
        return f"CASE WHEN {q} IS NULL THEN NULL ELSE CONCAT('[CONFIG_VALUE_REDACTED len=', LEN(CONVERT(nvarchar(max), {q})), ' sha=', {hash_expr(q)}, ']') END AS {alias}"

    if IDENTITY_NAME_RE.search(name):
        return f"CASE WHEN {q} IS NULL THEN NULL ELSE CONCAT('[HASH:', {hash_expr(q)}, ']') END AS {alias}"

    if data_type in TEXT_TYPES and FREE_TEXT_RE.search(name):
        return f"CASE WHEN {q} IS NULL THEN NULL ELSE CONCAT('[TEXT_REDACTED len=', LEN(CONVERT(nvarchar(max), {q})), ' sha=', {hash_expr(q)}, ']') END AS {alias}"

    if data_type in {"text", "ntext", "xml"}:
        return f"CASE WHEN {q} IS NULL THEN NULL ELSE CONCAT('[TEXT len=', LEN(CONVERT(nvarchar(max), {q})), ' sha=', {hash_expr(q)}, ']') END AS {alias}"

    return f"{q} AS {alias}"


def export_sanitized(password: str, target: Target, columns: list[dict[str, str]]) -> dict[str, str]:
    target.output_dir.mkdir(parents=True, exist_ok=True)
    select_exprs = ",\n    ".join(sanitize_expr(target, c) for c in columns)
    date_col = choose_date_column(columns)
    id_col = choose_id_column(columns)
    order_bits = []
    if date_col:
        order_bits.append(f"{quote_ident(date_col)} DESC")
    if id_col and id_col != date_col:
        order_bits.append(f"{quote_ident(id_col)} DESC")
    order_sql = " ORDER BY " + ", ".join(order_bits) if order_bits else ""
    top_sql = f"TOP ({SAMPLE_LIMIT_AUDIT}) " if target.wave3_mode == "audit_sample" else ""
    sql = f"""
SET NOCOUNT ON;
SELECT {top_sql}
    {select_exprs}
FROM {quote_ident(target.schema)}.{quote_ident(target.table)}
{order_sql};
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
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    assert proc.stdin is not None and proc.stdout is not None
    proc.stdin.write(sql.encode("utf-8"))
    proc.stdin.close()

    sha = hashlib.sha256()
    raw_bytes = 0
    with gzip.open(target.output_path, "wb", compresslevel=6) as gz:
        while True:
            chunk = proc.stdout.read(1024 * 1024)
            if not chunk:
                break
            gz.write(chunk)
            sha.update(chunk)
            raw_bytes += len(chunk)
    stderr = proc.stderr.read().decode("utf-8", errors="replace") if proc.stderr else ""
    returncode = proc.wait()
    if returncode != 0:
        raise RuntimeError(f"Export failed for {target.database}.{target.table}: {stderr[:3000]}")

    sample_rows = count_export_rows(target.output_path)
    return {
        "database": target.database,
        "schema": target.schema,
        "table": target.table,
        "module": target.module,
        "priority": target.priority,
        "wave3_mode": target.wave3_mode,
        "expected_total_rows": str(target.row_count_expected),
        "exported_rows": str(sample_rows),
        "columns": str(len(columns)),
        "date_column": date_col or "",
        "file": str(target.output_path),
        "file_size_bytes": str(target.output_path.stat().st_size),
        "raw_stdout_bytes_before_gzip": str(raw_bytes),
        "sha256_uncompressed_stream": sha.hexdigest(),
        "reason": target.reason,
    }


def count_export_rows(path: Path) -> int:
    with gzip.open(path, "rt", encoding="utf-8", errors="replace") as f:
        lines = [line for line in f.read().splitlines() if line.strip()]
    # sqlcmd has header + separator + data.
    if len(lines) <= 2:
        return 0
    return max(0, len(lines) - 2)


def write_schema_catalog(targets: list[Target], columns_by_key: dict[tuple[str, str], list[dict[str, str]]]) -> None:
    SCHEMA_DIR.mkdir(parents=True, exist_ok=True)
    path = SCHEMA_DIR / "wave3_column_catalog.csv"
    fields = ["database", "schema", "table", "column_id", "column_name", "data_type", "max_length", "precision", "scale", "is_nullable", "sanitization"]
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        for target in targets:
            for col in columns_by_key[(target.database, target.table)]:
                writer.writerow(
                    {
                        "database": target.database,
                        "schema": target.schema,
                        "table": target.table,
                        "column_id": col["column_id"],
                        "column_name": col["column_name"],
                        "data_type": col["data_type"],
                        "max_length": col["max_length"],
                        "precision": col["precision"],
                        "scale": col["scale"],
                        "is_nullable": col["is_nullable"],
                        "sanitization": sanitization_label(target, col),
                    }
                )


def sanitization_label(target: Target, col: dict[str, str]) -> str:
    name = col["column_name"]
    data_type = col["data_type"].lower()
    if data_type in BINARY_TYPES:
        return "binary_length_only"
    if SECRET_NAME_RE.search(name):
        return "redacted_secret"
    if target.table.lower() in {"tblsystemappoptions", "tblsystemappoptionsgrid"} and name.lower() in {"vlera", "value"}:
        return "config_value_redacted_hash_len"
    if IDENTITY_NAME_RE.search(name):
        return "pseudonymized_hash"
    if data_type in TEXT_TYPES and FREE_TEXT_RE.search(name):
        return "text_redacted_hash_len"
    return "kept"


def write_audit_coverage(password: str, targets: list[Target], columns_by_key: dict[tuple[str, str], list[dict[str, str]]]) -> None:
    SUMMARY_DIR.mkdir(parents=True, exist_ok=True)
    summary_rows: list[dict[str, str]] = []
    monthly_rows: list[dict[str, str]] = []
    for target in targets:
        if target.wave3_mode != "audit_sample":
            continue
        columns = columns_by_key[(target.database, target.table)]
        date_col = choose_date_column(columns)
        if date_col:
            sql = f"""
SELECT
  COUNT_BIG(*) AS total_rows,
  CONVERT(nvarchar(30), MIN({quote_ident(date_col)}), 126) AS min_date,
  CONVERT(nvarchar(30), MAX({quote_ident(date_col)}), 126) AS max_date
FROM {quote_ident(target.schema)}.{quote_ident(target.table)};
"""
            rows = parse_sqlcmd_psv(run_sql_text(password, target.database, sql))
            row = rows[0] if rows else {}
            summary_rows.append(
                {
                    "database": target.database,
                    "table": target.table,
                    "total_rows": row.get("total_rows", str(target.row_count_expected)),
                    "date_column": date_col,
                    "min_date": row.get("min_date", ""),
                    "max_date": row.get("max_date", ""),
                }
            )
            month_sql = f"""
SELECT
  CONVERT(char(7), {quote_ident(date_col)}, 120) AS month,
  COUNT_BIG(*) AS rows
FROM {quote_ident(target.schema)}.{quote_ident(target.table)}
WHERE {quote_ident(date_col)} IS NOT NULL
GROUP BY CONVERT(char(7), {quote_ident(date_col)}, 120)
ORDER BY month;
"""
            for mr in parse_sqlcmd_psv(run_sql_text(password, target.database, month_sql)):
                monthly_rows.append(
                    {
                        "database": target.database,
                        "table": target.table,
                        "date_column": date_col,
                        "month": mr.get("month", ""),
                        "rows": mr.get("rows", ""),
                    }
                )
        else:
            summary_rows.append(
                {
                    "database": target.database,
                    "table": target.table,
                    "total_rows": str(target.row_count_expected),
                    "date_column": "",
                    "min_date": "",
                    "max_date": "",
                }
            )

    with (SUMMARY_DIR / "audit_activity_date_coverage.csv").open("w", newline="", encoding="utf-8") as f:
        fields = ["database", "table", "total_rows", "date_column", "min_date", "max_date"]
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        writer.writerows(summary_rows)

    with (SUMMARY_DIR / "audit_activity_monthly_counts.csv").open("w", newline="", encoding="utf-8") as f:
        fields = ["database", "table", "date_column", "month", "rows"]
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        writer.writerows(monthly_rows)


def md_table(rows: list[dict[str, str]], columns: list[str], limit: int | None = None) -> str:
    if limit is not None:
        rows = rows[:limit]
    if not rows:
        return "_Asnjë rresht._\n"
    out = ["| " + " | ".join(columns) + " |", "| " + " | ".join("---" for _ in columns) + " |"]
    for row in rows:
        out.append("| " + " | ".join(str(row.get(col, "")).replace("|", "\\|") for col in columns) + " |")
    return "\n".join(out) + "\n"


def write_manifest(rows: list[dict[str, str]], started_at: str, finished_at: str) -> None:
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    csv_path = OUT_ROOT / "wave3_sanitized_manifest.csv"
    fields = [
        "database",
        "schema",
        "table",
        "module",
        "priority",
        "wave3_mode",
        "expected_total_rows",
        "exported_rows",
        "columns",
        "date_column",
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

    audit_rows = [r for r in rows if r["wave3_mode"] == "audit_sample"]
    ref_rows = [r for r in rows if r["wave3_mode"] == "sanitized_reference"]
    lines = [
        "# PRO DATA Supplementary Extract — Wave 3 Sanitized Audit/RBAC/Config",
        "",
        f"- Started: `{started_at}`",
        f"- Finished: `{finished_at}`",
        f"- Docker container: `{CONTAINER}`",
        f"- SQLCMD path: `{SQLCMD}`",
        "- Mode: read-only, sanitized exports only",
        "- Secrets: password/PIN/MAC/token/key/cert values redacted; usernames/hosts/devices pseudonymized",
        "",
        "## Summary",
        "",
        f"- Audit/user activity tables sampled: `{len(audit_rows)}`",
        f"- Sanitized reference tables exported: `{len(ref_rows)}`",
        f"- Total exported/sampled rows: `{sum(int(r['exported_rows']) for r in rows):,}`",
        "",
        "## Files",
        "",
        "- `wave3_sanitized_manifest.csv`",
        "- `schema_review/wave3_column_catalog.csv`",
        "- `summaries/audit_activity_date_coverage.csv`",
        "- `summaries/audit_activity_monthly_counts.csv`",
        "- `sanitized_audit_samples/*.psv.gz`",
        "- `sanitized_reference/*.psv.gz`",
        "- `PRODATA_Audit_RBAC_Config_Sanitized_Review.md`",
    ]
    MANIFEST.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")


def read_csv(path: Path) -> list[dict[str, str]]:
    if not path.exists():
        return []
    with path.open("r", encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


def write_review(rows: list[dict[str, str]]) -> None:
    audit_coverage = read_csv(SUMMARY_DIR / "audit_activity_date_coverage.csv")
    manifest_rows = [
        {
            "Database": r["database"],
            "Table": r["table"],
            "Mode": r["wave3_mode"],
            "Rows exported": r["exported_rows"],
            "Total rows": r["expected_total_rows"],
        }
        for r in rows
    ]
    coverage_rows = [
        {
            "Database": r["database"],
            "Table": r["table"],
            "Rows": r["total_rows"],
            "Date column": r["date_column"],
            "Min": r["min_date"],
            "Max": r["max_date"],
        }
        for r in audit_coverage
    ]
    rbac_tables = sorted({r["table"] for r in rows if r["module"] == "Users / RBAC"})
    config_tables = sorted({r["table"] for r in rows if r["module"] == "System/config/reference"})
    misc_tables = sorted({r["table"] for r in rows if r["module"] == "Attachments / notes / misc"})

    lines = [
        "# PRO DATA Audit/RBAC/Config — Sanitized Review",
        "",
        f"- Generated: `{dt.datetime.now().astimezone().isoformat(timespec='seconds')}`",
        "- Source: restored PRO DATA backup",
        "- Scope: audit/user activity + RBAC/user rights + config/reference evidence",
        "",
        "## Verdict",
        "",
        "Wave 3 e mbyll boshllëkun më të ndjeshëm të backup-it: audit/user activity dhe RBAC/config. Këto të dhëna **nuk përdoren për migrim kredencialesh**; përdoren për të kuptuar çfarë kontrollesh, role, leje, audit trail dhe konfigurime ka pasur PRO DATA, që iData+ të mos dalë më e varfër në kontroll operacional.",
        "",
        "## Sanitization policy",
        "",
        "- Password, PIN, MAC/device identifiers, token/key/certificate/connection values: `REDACTED_SECRET`.",
        "- Username/fullname/host/computer/device/operator fields: pseudonymized hash.",
        "- Audit descriptions, memo/comment/config values: redacted as length + hash, jo raw text.",
        "- Binary/logo/file-like fields: only byte length.",
        "- IDs, dates, booleans, numeric permission flags and foreign keys are kept where safe.",
        "",
        "## Exported evidence",
        "",
        md_table(manifest_rows, ["Database", "Table", "Mode", "Rows exported", "Total rows"], limit=120).rstrip(),
        "",
        "## Audit/user activity coverage",
        "",
        md_table(coverage_rows, ["Database", "Table", "Rows", "Date column", "Min", "Max"]).rstrip(),
        "",
        "## RBAC/user-rights evidence captured",
        "",
        ", ".join(f"`{t}`" for t in rbac_tables) or "_None_",
        "",
        "Key observation: PRO DATA has a broad permission surface: groups, group rights, report rights, special rights, user-location/user-warehouse mappings, POS/mobile flags inside `tblUSERS`, and role/right tables. iData+ should not copy the table shape, but it must preserve the capability level through a modern permission matrix.",
        "",
        "## Config/reference evidence captured",
        "",
        ", ".join(f"`{t}`" for t in config_tables) or "_None_",
        "",
        "Config values are sanitized because legacy ERP options can contain paths, device/server hints, or operational secrets. For implementation, use this as a checklist of configurable domains, not as values to blindly migrate.",
        "",
        "## Attachments/notes/misc evidence captured",
        "",
        ", ".join(f"`{t}`" for t in misc_tables) or "_None_",
        "",
        "Text/notes were redacted. If business users later need historical memo migration, that should be a separate client-approved migration decision.",
        "",
        "## What iData+ must implement",
        "",
        "- New users must be recreated/reset; **do not migrate PRO DATA passwords, PINs, MAC/device IDs or secrets**.",
        "- RBAC must support module/entity/action permissions: read/create/update/correct-void/print/approve/export/admin.",
        "- POS/KDS actions need manager approval policies: void, discount, transfer table, reveal/close bill, stock corrections, price override.",
        "- Audit log must be append-only with actor, role, action, entity, entity_id, business_id, source device, original offline timestamp, sync timestamp, reason, approval actor, before/after hashes.",
        "- Break-glass support must require owner approval, reason, time limit and audit record.",
        "- System config should be typed and versioned, with sensitive config in secrets manager, not database free text.",
        "",
        "## Remaining caution",
        "",
        "Wave 3 gives enough evidence for audit/RBAC/config design. It does not mean we should migrate legacy users or hidden settings. For iData+, the safe path is: map capabilities, design clean RBAC, recreate users, import only explicit non-secret configuration approved by owner/admin.",
    ]
    REVIEW.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")


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
    AUDIT_SAMPLE_DIR.mkdir(parents=True, exist_ok=True)
    REFERENCE_DIR.mkdir(parents=True, exist_ok=True)
    SUMMARY_DIR.mkdir(parents=True, exist_ok=True)
    SCHEMA_DIR.mkdir(parents=True, exist_ok=True)

    password = get_sa_password()
    targets = load_targets()
    columns_by_key: dict[tuple[str, str], list[dict[str, str]]] = {}
    rows: list[dict[str, str]] = []

    print(f"Wave 3 targets: {len(targets)}")
    for idx, target in enumerate(targets, 1):
        columns = get_columns(password, target.database, target.schema, target.table)
        columns_by_key[(target.database, target.table)] = columns
        print(f"[{idx}/{len(targets)}] Exporting {target.database}.{target.table} ({target.wave3_mode})")
        rows.append(export_sanitized(password, target, columns))

    write_schema_catalog(targets, columns_by_key)
    write_audit_coverage(password, targets, columns_by_key)
    finished_at = dt.datetime.now().astimezone().isoformat(timespec="seconds")
    write_manifest(rows, started_at, finished_at)
    write_review(rows)
    zip_path = make_zip()

    print(f"Wrote {MANIFEST}")
    print(f"Wrote {REVIEW}")
    print(f"Wrote {OUT_ROOT / 'wave3_sanitized_manifest.csv'}")
    print(f"Wrote {SCHEMA_DIR / 'wave3_column_catalog.csv'}")
    print(f"Wrote {SUMMARY_DIR / 'audit_activity_date_coverage.csv'}")
    print(f"Wrote {SUMMARY_DIR / 'audit_activity_monthly_counts.csv'}")
    print(f"Wrote {zip_path} ({zip_path.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
