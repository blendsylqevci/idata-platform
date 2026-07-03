#!/usr/bin/env python3
"""
Finance Operations micro-extract.

Exports only the tiny/specific finance-operation candidates that remained after
the DDL/DML cross-check:
  - tblDocBlerjetShpenzimetTjera (PLANET2: 4 rows)
  - tblDocPagesatAvanset (PLANET2: 1 row)
  - tblUserMenyratPagesa (PLANET2 + PLANET_RESORT)

Mode: read-only SQL Server access through the already restored Docker database.
Output: sanitized PSV.gz files. Usernames and free-text fields are hashed or
redacted, not exported raw.
"""

from __future__ import annotations

import csv
import gzip
import hashlib
import re
import subprocess
import sys
import zipfile
from dataclasses import dataclass
from datetime import date
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from extract_prodata_ddl import CONTAINER, SQLCMD, get_sa_password  # noqa: E402


ROOT = Path(__file__).resolve().parents[1]
TODAY = date.today().isoformat()
OUT_DIR = ROOT / "outputs" / f"finance_operations_micro_extract_{TODAY}"
DATA_DIR = OUT_DIR / "sanitized_micro_tables"

TEXT_TYPES = {"char", "nchar", "varchar", "nvarchar", "text", "ntext", "xml"}
BINARY_TYPES = {"binary", "varbinary", "image", "timestamp", "rowversion"}
DATE_TYPES = {"date", "datetime", "datetime2", "smalldatetime", "datetimeoffset", "time"}

SECRET_NAME_RE = re.compile(r"(password|passw|passwd|pwd|pin|mac|token|secret|cert|private|salt|hash|license|apikey|api_key)", re.I)
IDENTITY_NAME_RE = re.compile(r"(username|useri|user|createdby|modifiedby|updatedby|insertedby|operator|host|device|paisja|email|mail|phone|tel|mobile)", re.I)
FREE_TEXT_RE = re.compile(r"(pershkrim|pershkrimi|referenca|koment|komenti|memo|description|note|message|text)", re.I)


@dataclass(frozen=True)
class Target:
    database: str
    schema: str
    table: str
    expected_rows: int
    reason: str

    @property
    def prefix(self) -> str:
        return {
            "PRODATA_PLANET2": "planet2",
            "PRODATA_PLANET_RESORT": "planet_resort",
        }[self.database]

    @property
    def output_path(self) -> Path:
        return DATA_DIR / f"{self.prefix}_{self.table}.sanitized.psv.gz"


TARGETS = [
    Target("PRODATA_PLANET2", "dbo", "tblDocBlerjetShpenzimetTjera", 4, "purchase additional-cost/expense parity"),
    Target("PRODATA_PLANET2", "dbo", "tblDocPagesatAvanset", 1, "advance/prepayment semantics"),
    Target("PRODATA_PLANET2", "dbo", "tblUserMenyratPagesa", 2102, "user payment-method permissions/defaults"),
    Target("PRODATA_PLANET_RESORT", "dbo", "tblUserMenyratPagesa", 4423, "user payment-method permissions/defaults"),
]


def sh(cmd: list[str], *, input_text: str | None = None, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        cmd,
        input=input_text,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=check,
    )


def quote_ident(name: str) -> str:
    return "[" + name.replace("]", "]]") + "]"


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
    return list(csv.DictReader(clean, delimiter="|"))


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


def get_columns(password: str, target: Target) -> list[dict[str, str]]:
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
WHERE s.name = N'{target.schema.replace("'", "''")}'
  AND t.name = N'{target.table.replace("'", "''")}'
ORDER BY c.column_id;
"""
    return parse_sqlcmd_psv(run_sql_text(password, target.database, sql))


def hash_expr(col_sql: str) -> str:
    return (
        "LEFT(CONVERT(varchar(64), HASHBYTES('SHA2_256', "
        f"CONVERT(varbinary(max), CONVERT(nvarchar(max), {col_sql}))), 2), 16)"
    )


def sanitize_expr(col: dict[str, str]) -> str:
    name = col["column_name"]
    data_type = col["data_type"].lower()
    q = quote_ident(name)
    alias = quote_ident(name)

    if data_type in BINARY_TYPES:
        return f"CASE WHEN {q} IS NULL THEN NULL ELSE CONCAT('[BINARY len=', CONVERT(varchar(20), DATALENGTH({q})), ']') END AS {alias}"
    if SECRET_NAME_RE.search(name):
        return f"CASE WHEN {q} IS NULL THEN NULL ELSE '[REDACTED_SECRET]' END AS {alias}"
    if IDENTITY_NAME_RE.search(name):
        return f"CASE WHEN {q} IS NULL THEN NULL ELSE CONCAT('[HASH:', {hash_expr(q)}, ']') END AS {alias}"
    if data_type in TEXT_TYPES and FREE_TEXT_RE.search(name):
        return f"CASE WHEN {q} IS NULL THEN NULL ELSE CONCAT('[TEXT_REDACTED len=', LEN(CONVERT(nvarchar(max), {q})), ' sha=', {hash_expr(q)}, ']') END AS {alias}"
    if data_type in {"text", "ntext", "xml"}:
        return f"CASE WHEN {q} IS NULL THEN NULL ELSE CONCAT('[TEXT len=', LEN(CONVERT(nvarchar(max), {q})), ' sha=', {hash_expr(q)}, ']') END AS {alias}"
    return f"{q} AS {alias}"


def count_rows(path: Path) -> int:
    if not path.exists():
        return 0
    with gzip.open(path, "rt", encoding="utf-8", errors="replace") as f:
        lines = [line.strip() for line in f if line.strip()]
    # sqlcmd output is header + separator + rows.
    return max(0, len(lines) - 2)


def export_target(password: str, target: Target) -> dict[str, str]:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    columns = get_columns(password, target)
    select_exprs = ",\n    ".join(sanitize_expr(c) for c in columns)
    order_col = "ID" if any(c["column_name"].lower() == "id" for c in columns) else columns[0]["column_name"]
    sql = f"""
SELECT
    {select_exprs}
FROM {quote_ident(target.schema)}.{quote_ident(target.table)}
ORDER BY {quote_ident(order_col)};
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
    proc.stdin.write(("SET NOCOUNT ON;\n" + sql.strip() + "\n").encode("utf-8"))
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

    exported_rows = count_rows(target.output_path)
    return {
        "database": target.database,
        "schema": target.schema,
        "table": target.table,
        "expected_rows": str(target.expected_rows),
        "exported_rows": str(exported_rows),
        "columns": str(len(columns)),
        "file": str(target.output_path.relative_to(ROOT)),
        "file_size_bytes": str(target.output_path.stat().st_size),
        "sha256_uncompressed_stream": sha.hexdigest(),
        "sanitization": "identity/free-text/secret fields redacted or hashed",
        "reason": target.reason,
    }


def write_csv(path: Path, rows: list[dict[str, str]], fieldnames: list[str]) -> None:
    with path.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fieldnames)
        w.writeheader()
        for row in rows:
            w.writerow({k: row.get(k, "") for k in fieldnames})


def md_table(rows: list[dict[str, str]], headers: list[str]) -> str:
    if not rows:
        return "_No rows._"
    out = ["| " + " | ".join(headers) + " |", "| " + " | ".join(["---"] * len(headers)) + " |"]
    for row in rows:
        out.append("| " + " | ".join(str(row.get(h, "")).replace("|", "\\|") for h in headers) + " |")
    return "\n".join(out)


def build_review(rows: list[dict[str, str]]) -> None:
    doc = f"""# Finance Operations Micro Extract — Sanitized

**Data:** {TODAY}
**Status:** supplementary evidence / sanitized
**Scope:** vetëm micro-candidates të konfirmuara nga Finance Operations DDL/DML cross-check.

## Çfarë u nxor

{md_table(rows, ["database", "table", "expected_rows", "exported_rows", "columns", "sanitization", "reason"])}

## Pse ka rëndësi

- `tblUserMenyratPagesa` tregon lidhjen user → mënyrë pagese; kjo ndihmon RBAC/payment-method design pa migruar user/password/PIN.
- `tblDocBlerjetShpenzimetTjera` tregon si PRO DATA lidhte shpenzime/kosto shtesë me blerjet.
- `tblDocPagesatAvanset` tregon semantikën e avancit/parapagimit, që mund të ndihmojë si reference për Finance Operations dhe KABI farmer advances.

## Kufiri i përdorimit

Këto janë evidence të sanitizuara për dizajn/parity, jo migrim final. Për iData+ përdoret modeli ynë i ri me audit/approval/RBAC; këto tabela thjesht mbyllin dyshimin që diçka e vogël financiare mbeti pa u parë.
"""
    (OUT_DIR / "FINANCE_OPERATIONS_Micro_Extract_Review.md").write_text(doc, encoding="utf-8")


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    password = get_sa_password()
    rows = [export_target(password, t) for t in TARGETS]
    write_csv(
        OUT_DIR / "finance_micro_extract_manifest.csv",
        rows,
        ["database", "schema", "table", "expected_rows", "exported_rows", "columns", "file", "file_size_bytes", "sha256_uncompressed_stream", "sanitization", "reason"],
    )
    build_review(rows)
    zip_path = OUT_DIR / "finance_operations_micro_extract_pack.zip"
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as z:
        for p in sorted(OUT_DIR.rglob("*")):
            if p == zip_path or p.is_dir():
                continue
            z.write(p, p.relative_to(OUT_DIR))
    print(f"Wrote {OUT_DIR}")
    for row in rows:
        print(f"{row['database']}.{row['table']}: {row['exported_rows']} rows")


if __name__ == "__main__":
    main()
