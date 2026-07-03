#!/usr/bin/env python3
"""
Extract SQL Server DDL from the restored PRO DATA databases running in Docker.

This script is intentionally read-only against SQL Server. It reads metadata from
sys.* catalogs and writes a DDL evidence bundle under outputs/.

It does not print SQL Server passwords or any SEF/application secrets.
"""

from __future__ import annotations

import datetime as dt
import os
import re
import subprocess
import sys
import zipfile
from pathlib import Path


CONTAINER = os.environ.get("PRODATA_MSSQL_CONTAINER", "codex-prodata-mssql")
SQLCMD = os.environ.get("PRODATA_SQLCMD", "/opt/mssql-tools18/bin/sqlcmd")
OUT_DIR = Path("outputs/prodata_ddl_extracted_2026-06-28")


def sh(cmd: list[str], *, input_text: str | None = None, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        cmd,
        input=input_text,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=check,
    )


def get_sa_password() -> str:
    env_password = os.environ.get("PRODATA_SA_PASSWORD") or os.environ.get("MSSQL_SA_PASSWORD") or os.environ.get("SA_PASSWORD")
    if env_password:
        return env_password

    proc = sh(["docker", "inspect", CONTAINER, "--format", "{{range .Config.Env}}{{println .}}{{end}}"])
    for line in proc.stdout.splitlines():
        if line.startswith("MSSQL_SA_PASSWORD=") or line.startswith("SA_PASSWORD="):
            return line.split("=", 1)[1]

    raise RuntimeError(
        "Could not find SQL Server SA password in host env or Docker container env. "
        "Set PRODATA_SA_PASSWORD and rerun."
    )


def run_sql(password: str, database: str, sql: str) -> str:
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
        "-w",
        "65535",
        "-y",
        "0",
        "-Y",
        "0",
        "-s",
        "|",
    ]
    sql_input = "\n".join(
        [
            "SET NOCOUNT ON;",
            "SET QUOTED_IDENTIFIER ON;",
            "SET ANSI_NULLS ON;",
            "SET ANSI_WARNINGS ON;",
            "SET ANSI_PADDING ON;",
            "SET CONCAT_NULL_YIELDS_NULL ON;",
            sql.strip(),
            "",
        ]
    )
    proc = sh(cmd, input_text=sql_input, check=False)
    if proc.returncode != 0:
        raise RuntimeError(f"sqlcmd failed for database={database}\nSTDERR:\n{proc.stderr}\nSTDOUT:\n{proc.stdout[:2000]}")
    if re.search(r"^Msg \d+", proc.stdout, flags=re.MULTILINE):
        raise RuntimeError(f"SQL Server returned errors for database={database}\nSTDOUT:\n{proc.stdout[:4000]}")
    return proc.stdout.replace("\r\n", "\n")


def safe_name(value: str) -> str:
    value = re.sub(r"[\[\]]", "", value)
    value = re.sub(r"[^A-Za-z0-9_.-]+", "_", value)
    return value.strip("_") or "object"


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text.rstrip() + "\n", encoding="utf-8")


def strip_sqlcmd_noise(text: str) -> str:
    lines: list[str] = []
    for line in text.splitlines():
        if line.strip() in {"ddl", "name"}:
            continue
        if re.match(r"^-+$", line.strip()):
            continue
        if re.match(r"^\(\d+ rows? affected\)$", line.strip()):
            continue
        lines.append(line.rstrip())
    return "\n".join(lines).strip() + "\n"


Q_DATABASES = r"""
SELECT name
FROM sys.databases
WHERE name LIKE N'PRODATA%'
ORDER BY name;
"""


Q_DB_INFO = r"""
SELECT
    N'DatabaseName|CompatibilityLevel|Collation|RecoveryModel|CreateDate|State|UserAccess'
UNION ALL
SELECT
    CONCAT(
        name COLLATE DATABASE_DEFAULT, N'|',
        CAST(compatibility_level AS nvarchar(20)), N'|',
        COALESCE(collation_name COLLATE DATABASE_DEFAULT, N''), N'|',
        recovery_model_desc COLLATE DATABASE_DEFAULT, N'|',
        CONVERT(nvarchar(30), create_date, 126), N'|',
        state_desc COLLATE DATABASE_DEFAULT, N'|',
        user_access_desc COLLATE DATABASE_DEFAULT
    )
FROM sys.databases
WHERE name = DB_NAME();
"""


Q_OBJECT_COUNTS = r"""
SELECT N'ObjectType|Count'
UNION ALL
SELECT type_desc + N'|' + CAST(COUNT(*) AS nvarchar(30))
FROM sys.objects
WHERE is_ms_shipped = 0
GROUP BY type_desc
UNION ALL
SELECT N'TABLE_ROWS_ESTIMATE|' + CAST(SUM(p.rows) AS nvarchar(30))
FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
WHERE t.is_ms_shipped = 0;
"""


Q_TABLE_INVENTORY = r"""
;WITH inv AS (
    SELECT
        SCHEMA_NAME(t.schema_id) AS schema_name,
        t.name AS table_name,
        CAST(COALESCE(SUM(CASE WHEN p.index_id IN (0,1) THEN p.rows ELSE 0 END), 0) AS nvarchar(30)) AS rows_estimate,
        CAST((SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS nvarchar(30)) AS column_count
    FROM sys.tables t
    LEFT JOIN sys.partitions p ON p.object_id = t.object_id
    WHERE t.is_ms_shipped = 0
    GROUP BY t.object_id, t.schema_id, t.name
),
lines AS (
    SELECT 0 AS sort_key, N'Schema|Table|RowsEstimate|Columns' AS line
    UNION ALL
    SELECT 1 AS sort_key, schema_name + N'|' + table_name + N'|' + rows_estimate + N'|' + column_count AS line
    FROM inv
)
SELECT line
FROM lines
ORDER BY sort_key, line;
"""


Q_SCHEMAS = r"""
SELECT
    N'IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N''' +
    REPLACE(name, N'''', N'''''') +
    N''') EXEC(N''CREATE SCHEMA ' + QUOTENAME(name) + N' AUTHORIZATION ' + QUOTENAME(USER_NAME(principal_id)) + N''');' + CHAR(10) + N'GO'
FROM sys.schemas
WHERE schema_id >= 5
  AND name NOT IN (N'sys', N'INFORMATION_SCHEMA')
ORDER BY name;
"""


Q_USER_TYPES = r"""
;WITH alias_types AS (
    SELECT
        st.name AS base_type,
        ty.name,
        SCHEMA_NAME(ty.schema_id) AS schema_name,
        ty.max_length,
        ty.precision,
        ty.scale,
        ty.is_nullable
    FROM sys.types ty
    JOIN sys.types st ON ty.system_type_id = st.system_type_id AND st.user_type_id = st.system_type_id
    WHERE ty.is_user_defined = 1 AND ty.is_table_type = 0
)
SELECT
    N'CREATE TYPE ' + QUOTENAME(schema_name) + N'.' + QUOTENAME(name) + N' FROM ' +
    CASE
        WHEN base_type IN (N'varchar', N'char', N'varbinary', N'binary') THEN base_type + N'(' + CASE WHEN max_length = -1 THEN N'max' ELSE CAST(max_length AS nvarchar(20)) END + N')'
        WHEN base_type IN (N'nvarchar', N'nchar') THEN base_type + N'(' + CASE WHEN max_length = -1 THEN N'max' ELSE CAST(max_length / 2 AS nvarchar(20)) END + N')'
        WHEN base_type IN (N'decimal', N'numeric') THEN base_type + N'(' + CAST(precision AS nvarchar(20)) + N',' + CAST(scale AS nvarchar(20)) + N')'
        WHEN base_type IN (N'datetime2', N'time', N'datetimeoffset') THEN base_type + N'(' + CAST(scale AS nvarchar(20)) + N')'
        ELSE base_type
    END +
    CASE WHEN is_nullable = 1 THEN N' NULL;' ELSE N' NOT NULL;' END + CHAR(10) + N'GO'
FROM alias_types
ORDER BY schema_name, name;
"""


Q_TABLE_TYPES = r"""
;WITH tt AS (
    SELECT tt.type_table_object_id AS object_id, SCHEMA_NAME(tt.schema_id) AS schema_name, tt.name
    FROM sys.table_types tt
    WHERE tt.is_user_defined = 1
)
SELECT
    N'CREATE TYPE ' + QUOTENAME(tt.schema_name) + N'.' + QUOTENAME(tt.name) + N' AS TABLE (' + CHAR(10) +
    STUFF((
        SELECT N',' + CHAR(10) + N'    ' +
            QUOTENAME(c.name) + N' ' +
            CASE
                WHEN ty.is_user_defined = 1 THEN QUOTENAME(SCHEMA_NAME(ty.schema_id)) + N'.' + QUOTENAME(ty.name)
                WHEN ty.name IN (N'varchar', N'char', N'varbinary', N'binary') THEN ty.name + N'(' + CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length AS nvarchar(20)) END + N')'
                WHEN ty.name IN (N'nvarchar', N'nchar') THEN ty.name + N'(' + CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length / 2 AS nvarchar(20)) END + N')'
                WHEN ty.name IN (N'decimal', N'numeric') THEN ty.name + N'(' + CAST(c.precision AS nvarchar(20)) + N',' + CAST(c.scale AS nvarchar(20)) + N')'
                WHEN ty.name IN (N'datetime2', N'time', N'datetimeoffset') THEN ty.name + N'(' + CAST(c.scale AS nvarchar(20)) + N')'
                ELSE ty.name
            END +
            CASE WHEN c.is_nullable = 1 THEN N' NULL' ELSE N' NOT NULL' END
        FROM sys.columns c
        JOIN sys.types ty ON c.user_type_id = ty.user_type_id
        WHERE c.object_id = tt.object_id
        ORDER BY c.column_id
        FOR XML PATH(N''), TYPE).value(N'.', N'nvarchar(max)'), 1, 2, N'') +
    CHAR(10) + N');' + CHAR(10) + N'GO'
FROM tt
ORDER BY tt.schema_name, tt.name;
"""


Q_TABLES = r"""
;WITH tbl AS (
    SELECT t.object_id, SCHEMA_NAME(t.schema_id) AS schema_name, t.name
    FROM sys.tables t
    WHERE t.is_ms_shipped = 0
)
SELECT
    N'-- =============================================================' + CHAR(10) +
    N'-- Table: ' + QUOTENAME(tbl.schema_name) + N'.' + QUOTENAME(tbl.name) + CHAR(10) +
    N'CREATE TABLE ' + QUOTENAME(tbl.schema_name) + N'.' + QUOTENAME(tbl.name) + N' (' + CHAR(10) +
    STUFF((
        SELECT N',' + CHAR(10) + N'    ' +
            CASE
                WHEN cc.object_id IS NOT NULL THEN
                    QUOTENAME(c.name) + N' AS ' + cc.definition + CASE WHEN cc.is_persisted = 1 THEN N' PERSISTED' ELSE N'' END
                ELSE
                    QUOTENAME(c.name) + N' ' +
                    CASE
                        WHEN ty.is_user_defined = 1 THEN QUOTENAME(SCHEMA_NAME(ty.schema_id)) + N'.' + QUOTENAME(ty.name)
                        WHEN ty.name IN (N'varchar', N'char', N'varbinary', N'binary') THEN ty.name + N'(' + CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length AS nvarchar(20)) END + N')'
                        WHEN ty.name IN (N'nvarchar', N'nchar') THEN ty.name + N'(' + CASE WHEN c.max_length = -1 THEN N'max' ELSE CAST(c.max_length / 2 AS nvarchar(20)) END + N')'
                        WHEN ty.name IN (N'decimal', N'numeric') THEN ty.name + N'(' + CAST(c.precision AS nvarchar(20)) + N',' + CAST(c.scale AS nvarchar(20)) + N')'
                        WHEN ty.name IN (N'datetime2', N'time', N'datetimeoffset') THEN ty.name + N'(' + CAST(c.scale AS nvarchar(20)) + N')'
                        ELSE ty.name
                    END +
                    CASE WHEN ic.object_id IS NOT NULL THEN N' IDENTITY(' + CAST(ic.seed_value AS nvarchar(40)) + N',' + CAST(ic.increment_value AS nvarchar(40)) + N')' ELSE N'' END +
                    CASE WHEN c.collation_name IS NOT NULL AND c.collation_name <> DATABASEPROPERTYEX(DB_NAME(), 'Collation') THEN N' COLLATE ' + c.collation_name ELSE N'' END +
                    CASE WHEN c.is_nullable = 1 THEN N' NULL' ELSE N' NOT NULL' END
            END
        FROM sys.columns c
        JOIN sys.types ty ON c.user_type_id = ty.user_type_id
        LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        WHERE c.object_id = tbl.object_id
        ORDER BY c.column_id
        FOR XML PATH(N''), TYPE).value(N'.', N'nvarchar(max)'), 1, 2, N'') +
    CHAR(10) + N');' + CHAR(10) + N'GO' + CHAR(10)
FROM tbl
ORDER BY tbl.schema_name, tbl.name;
"""


Q_DEFAULTS = r"""
SELECT
    N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) +
    N' ADD CONSTRAINT ' + QUOTENAME(dc.name) + N' DEFAULT ' + dc.definition +
    N' FOR ' + QUOTENAME(c.name) + N';' + CHAR(10) + N'GO'
FROM sys.default_constraints dc
JOIN sys.tables t ON t.object_id = dc.parent_object_id
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE t.is_ms_shipped = 0
ORDER BY SCHEMA_NAME(t.schema_id), t.name, c.column_id;
"""


Q_CHECKS = r"""
SELECT
    N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) +
    CASE WHEN cc.is_not_trusted = 1 THEN N' WITH NOCHECK' ELSE N' WITH CHECK' END +
    N' ADD CONSTRAINT ' + QUOTENAME(cc.name) + N' CHECK ' + cc.definition + N';' + CHAR(10) +
    CASE WHEN cc.is_disabled = 1 THEN N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' NOCHECK CONSTRAINT ' + QUOTENAME(cc.name) + N';' + CHAR(10) ELSE N'' END +
    N'GO'
FROM sys.check_constraints cc
JOIN sys.tables t ON t.object_id = cc.parent_object_id
WHERE t.is_ms_shipped = 0
ORDER BY SCHEMA_NAME(t.schema_id), t.name, cc.name;
"""


Q_KEYS = r"""
;WITH key_cols AS (
    SELECT
        kc.object_id AS constraint_object_id,
        STUFF((
            SELECT N', ' + QUOTENAME(c.name) + CASE WHEN ic.is_descending_key = 1 THEN N' DESC' ELSE N' ASC' END
            FROM sys.index_columns ic
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE ic.object_id = kc.parent_object_id
              AND ic.index_id = kc.unique_index_id
              AND ic.is_included_column = 0
            ORDER BY ic.key_ordinal
            FOR XML PATH(N''), TYPE).value(N'.', N'nvarchar(max)'), 1, 2, N'') AS cols
    FROM sys.key_constraints kc
)
SELECT
    N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) +
    N' ADD CONSTRAINT ' + QUOTENAME(kc.name) + N' ' +
    CASE WHEN kc.type = N'PK' THEN N'PRIMARY KEY ' ELSE N'UNIQUE ' END +
    i.type_desc COLLATE DATABASE_DEFAULT + N' (' + key_cols.cols + N');' + CHAR(10) + N'GO'
FROM sys.key_constraints kc
JOIN sys.tables t ON t.object_id = kc.parent_object_id
JOIN sys.indexes i ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
JOIN key_cols ON key_cols.constraint_object_id = kc.object_id
WHERE t.is_ms_shipped = 0
ORDER BY SCHEMA_NAME(t.schema_id), t.name, kc.type, kc.name;
"""


Q_FOREIGN_KEYS = r"""
;WITH fk_cols AS (
    SELECT
        fk.object_id,
        STUFF((
            SELECT N', ' + QUOTENAME(pc.name)
            FROM sys.foreign_key_columns fkc
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            WHERE fkc.constraint_object_id = fk.object_id
            ORDER BY fkc.constraint_column_id
            FOR XML PATH(N''), TYPE).value(N'.', N'nvarchar(max)'), 1, 2, N'') AS parent_cols,
        STUFF((
            SELECT N', ' + QUOTENAME(rc.name)
            FROM sys.foreign_key_columns fkc
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fkc.constraint_object_id = fk.object_id
            ORDER BY fkc.constraint_column_id
            FOR XML PATH(N''), TYPE).value(N'.', N'nvarchar(max)'), 1, 2, N'') AS ref_cols
    FROM sys.foreign_keys fk
)
SELECT
    N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(pt.schema_id)) + N'.' + QUOTENAME(pt.name) +
    CASE WHEN fk.is_not_trusted = 1 THEN N' WITH NOCHECK' ELSE N' WITH CHECK' END +
    N' ADD CONSTRAINT ' + QUOTENAME(fk.name) + N' FOREIGN KEY (' + fk_cols.parent_cols + N') REFERENCES ' +
    QUOTENAME(SCHEMA_NAME(rt.schema_id)) + N'.' + QUOTENAME(rt.name) + N' (' + fk_cols.ref_cols + N')' +
    CASE WHEN fk.delete_referential_action_desc <> N'NO_ACTION' THEN N' ON DELETE ' + REPLACE(fk.delete_referential_action_desc COLLATE DATABASE_DEFAULT, N'_', N' ') ELSE N'' END +
    CASE WHEN fk.update_referential_action_desc <> N'NO_ACTION' THEN N' ON UPDATE ' + REPLACE(fk.update_referential_action_desc COLLATE DATABASE_DEFAULT, N'_', N' ') ELSE N'' END +
    N';' + CHAR(10) +
    CASE WHEN fk.is_disabled = 1 THEN N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(pt.schema_id)) + N'.' + QUOTENAME(pt.name) + N' NOCHECK CONSTRAINT ' + QUOTENAME(fk.name) + N';' + CHAR(10) ELSE N'' END +
    N'GO'
FROM sys.foreign_keys fk
JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
JOIN fk_cols ON fk_cols.object_id = fk.object_id
WHERE pt.is_ms_shipped = 0
ORDER BY SCHEMA_NAME(pt.schema_id), pt.name, fk.name;
"""


Q_INDEXES = r"""
;WITH idx_cols AS (
    SELECT
        i.object_id,
        i.index_id,
        STUFF((
            SELECT N', ' + QUOTENAME(c.name) + CASE WHEN ic.is_descending_key = 1 THEN N' DESC' ELSE N' ASC' END
            FROM sys.index_columns ic
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE ic.object_id = i.object_id
              AND ic.index_id = i.index_id
              AND ic.is_included_column = 0
              AND ic.key_ordinal > 0
            ORDER BY ic.key_ordinal
            FOR XML PATH(N''), TYPE).value(N'.', N'nvarchar(max)'), 1, 2, N'') AS key_cols,
        STUFF((
            SELECT N', ' + QUOTENAME(c.name)
            FROM sys.index_columns ic
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE ic.object_id = i.object_id
              AND ic.index_id = i.index_id
              AND ic.is_included_column = 1
            ORDER BY ic.index_column_id
            FOR XML PATH(N''), TYPE).value(N'.', N'nvarchar(max)'), 1, 2, N'') AS include_cols
    FROM sys.indexes i
)
SELECT
    N'CREATE ' +
    CASE WHEN i.is_unique = 1 THEN N'UNIQUE ' ELSE N'' END +
    i.type_desc COLLATE DATABASE_DEFAULT + N' INDEX ' + QUOTENAME(i.name) + N' ON ' +
    QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) +
    CASE WHEN NULLIF(idx_cols.key_cols, N'') IS NOT NULL THEN N' (' + idx_cols.key_cols + N')' ELSE N'' END +
    CASE WHEN NULLIF(idx_cols.include_cols, N'') IS NOT NULL THEN N' INCLUDE (' + idx_cols.include_cols + N')' ELSE N'' END +
    CASE WHEN i.has_filter = 1 THEN N' WHERE ' + i.filter_definition ELSE N'' END +
    N';' + CHAR(10) + N'GO'
FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
JOIN idx_cols ON idx_cols.object_id = i.object_id AND idx_cols.index_id = i.index_id
WHERE t.is_ms_shipped = 0
  AND i.index_id > 0
  AND i.is_primary_key = 0
  AND i.is_unique_constraint = 0
  AND i.is_hypothetical = 0
  AND i.name IS NOT NULL
ORDER BY SCHEMA_NAME(t.schema_id), t.name, i.name;
"""


def module_query(object_type: str) -> str:
    if object_type == "views":
        where = "o.type = N'V' AND o.is_ms_shipped = 0"
    elif object_type == "procedures":
        where = "o.type IN (N'P', N'PC', N'RF', N'X') AND o.is_ms_shipped = 0"
    elif object_type == "functions":
        where = "o.type IN (N'FN', N'IF', N'TF', N'FS', N'FT') AND o.is_ms_shipped = 0"
    elif object_type == "triggers":
        where = "o.type IN (N'TR', N'TA') AND o.is_ms_shipped = 0"
    else:
        raise ValueError(object_type)

    parent_select = (
        "CASE WHEN o.parent_object_id <> 0 THEN N' ON ' + QUOTENAME(OBJECT_SCHEMA_NAME(o.parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(o.parent_object_id)) ELSE N'' END"
    )
    return f"""
SELECT
    N'-- =============================================================' + CHAR(10) +
    N'-- Object: ' + QUOTENAME(SCHEMA_NAME(o.schema_id)) + N'.' + QUOTENAME(o.name) +
    {parent_select} + CHAR(10) +
    COALESCE(sm.definition, N'-- Definition unavailable/encrypted: ' + QUOTENAME(SCHEMA_NAME(o.schema_id)) + N'.' + QUOTENAME(o.name)) +
    CHAR(10) + N'GO' + CHAR(10)
FROM sys.objects o
LEFT JOIN sys.sql_modules sm ON sm.object_id = o.object_id
WHERE {where}
ORDER BY SCHEMA_NAME(o.schema_id), o.name;
"""


Q_SYNONYMS = r"""
SELECT
    N'CREATE SYNONYM ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) +
    N' FOR ' + base_object_name + N';' + CHAR(10) + N'GO'
FROM sys.synonyms
ORDER BY SCHEMA_NAME(schema_id), name;
"""


Q_SEQUENCES = r"""
SELECT
    N'CREATE SEQUENCE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) +
    N' AS ' + TYPE_NAME(system_type_id) +
    N' START WITH ' + CAST(current_value AS nvarchar(80)) +
    N' INCREMENT BY ' + CAST(increment AS nvarchar(80)) +
    CASE WHEN is_cycling = 1 THEN N' CYCLE' ELSE N' NO CYCLE' END +
    N';' + CHAR(10) + N'GO'
FROM sys.sequences
ORDER BY SCHEMA_NAME(schema_id), name;
"""


DDL_QUERIES: list[tuple[str, str]] = [
    ("00_database_info.psv", Q_DB_INFO),
    ("00_object_counts.psv", Q_OBJECT_COUNTS),
    ("00_table_inventory.psv", Q_TABLE_INVENTORY),
    ("01_schemas.sql", Q_SCHEMAS),
    ("02_user_types.sql", Q_USER_TYPES),
    ("03_table_types.sql", Q_TABLE_TYPES),
    ("04_tables.sql", Q_TABLES),
    ("05_default_constraints.sql", Q_DEFAULTS),
    ("06_check_constraints.sql", Q_CHECKS),
    ("07_primary_unique_keys.sql", Q_KEYS),
    ("08_foreign_keys.sql", Q_FOREIGN_KEYS),
    ("09_indexes.sql", Q_INDEXES),
    ("10_views.sql", module_query("views")),
    ("11_functions.sql", module_query("functions")),
    ("12_stored_procedures.sql", module_query("procedures")),
    ("13_triggers.sql", module_query("triggers")),
    ("14_synonyms.sql", Q_SYNONYMS),
    ("15_sequences.sql", Q_SEQUENCES),
]


def extract_database(password: str, db: str) -> dict[str, int]:
    db_dir = OUT_DIR / "databases" / safe_name(db)
    db_dir.mkdir(parents=True, exist_ok=True)
    sizes: dict[str, int] = {}
    for filename, sql in DDL_QUERIES:
        try:
            out = strip_sqlcmd_noise(run_sql(password, db, sql))
        except Exception as exc:
            raise RuntimeError(f"{db}/{filename}: {exc}") from exc
        target = db_dir / filename
        write_text(target, out)
        sizes[filename] = target.stat().st_size

    combined_parts: list[str] = []
    for filename, _ in DDL_QUERIES:
        if filename.endswith(".sql"):
            combined_parts.append(f"-- FILE: {filename}\n")
            combined_parts.append((db_dir / filename).read_text(encoding="utf-8"))
            combined_parts.append("\n")
    write_text(db_dir / "PRODATA_FULL_DDL.sql", "\n".join(combined_parts))
    sizes["PRODATA_FULL_DDL.sql"] = (db_dir / "PRODATA_FULL_DDL.sql").stat().st_size
    return sizes


def make_manifest(databases: list[str], extracted: dict[str, dict[str, int]]) -> None:
    now = dt.datetime.now().astimezone().isoformat(timespec="seconds")
    lines = [
        "# PRO DATA DDL Extraction Manifest",
        "",
        f"- Extracted at: `{now}`",
        f"- Docker container: `{CONTAINER}`",
        f"- SQLCMD path: `{SQLCMD}`",
        "- Mode: read-only metadata extraction from restored SQL Server databases",
        "- Passwords/secrets: not printed, not stored in this bundle",
        "",
        "## Databases",
        "",
    ]
    for db in databases:
        lines.append(f"### `{db}`")
        lines.append("")
        for filename, size in sorted(extracted[db].items()):
            lines.append(f"- `{filename}` — {size:,} bytes")
        lines.append("")

    lines.extend(
        [
            "## Notes",
            "",
            "- This bundle is DDL/evidence for analysis and parity mapping; it is not a production migration plan.",
            "- Stored procedure/function/view bodies are extracted from `sys.sql_modules` when available.",
            "- Table definitions are generated from SQL Server system catalogs; validate before using as executable migration scripts.",
            "- Use `PRODATA_FULL_DDL.sql` per database for a single-file view, or the numbered files for category-by-category review.",
            "",
        ]
    )
    write_text(OUT_DIR / "EXTRACTION_MANIFEST.md", "\n".join(lines))


def make_zip() -> Path:
    zip_path = OUT_DIR.parent / "prodata_ddl_extracted_2026-06-28.zip"
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for path in sorted(OUT_DIR.rglob("*")):
            if path.is_file():
                zf.write(path, path.relative_to(OUT_DIR.parent))
    return zip_path


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    password = get_sa_password()
    db_text = strip_sqlcmd_noise(run_sql(password, "master", Q_DATABASES))
    databases = [line.strip() for line in db_text.splitlines() if line.strip() and not line.startswith("(")]
    if not databases:
        raise RuntimeError("No databases matching PRODATA% were found.")

    extracted: dict[str, dict[str, int]] = {}
    for db in databases:
        print(f"Extracting DDL for {db} ...", flush=True)
        extracted[db] = extract_database(password, db)

    make_manifest(databases, extracted)
    zip_path = make_zip()
    print("")
    print("Done.")
    print(f"Output: {OUT_DIR}")
    print(f"Zip: {zip_path}")
    print("Databases: " + ", ".join(databases))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1)
