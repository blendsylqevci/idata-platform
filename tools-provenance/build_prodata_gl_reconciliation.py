#!/usr/bin/env python3
"""
Isolate PRO DATA GL-posting procedures and reconcile one month end-to-end:
source documents -> tblGeneralLedger -> VAT books -> VAT monthly summary.

The script is read-only against SQL Server and writes an evidence pack under
outputs/prodata_gl_reconciliation_2026-06-28.
"""

from __future__ import annotations

import csv
import datetime as dt
import os
import re
import subprocess
import sys
import zipfile
from collections import Counter
from decimal import Decimal, InvalidOperation
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from extract_prodata_ddl import CONTAINER, SQLCMD, get_sa_password  # noqa: E402


DB = os.environ.get("PRODATA_ANALYSIS_DB", "PRODATA_PLANET2")
MONTH_START = os.environ.get("PRODATA_RECON_MONTH_START", "2026-06-01")
MONTH_END = os.environ.get("PRODATA_RECON_MONTH_END", "2026-07-01")
MONTH_LABEL = MONTH_START[:7]

DDL_PROC_FILE = Path(f"outputs/prodata_ddl_extracted_2026-06-28/databases/{DB}/12_stored_procedures.sql")
REPORT_ROOT = Path("outputs/prodata_reports_extracted_2026-06-27")
OUT_ROOT = Path("outputs/prodata_gl_reconciliation_2026-06-28")

PRIMARY_GL_PROCS = {
    "prcPostoShitjetGL": {
        "domain": "Sales / SHV",
        "target_txn": "SHV* normal sales invoices",
        "notes": "Creates normal sales GL: AR debit, revenue/discount/withholding/VAT credit lines; preserves costing entries by deleting only non-cost GL lines.",
    },
    "prcPostoBlerjetImportGL": {
        "domain": "Purchases / BLV-BLS",
        "target_txn": "BLV01/BLS01/BLI01/BLN01 and purchase/import variants",
        "notes": "Main purchase posting procedure in this backup; comment includes BLS01 example and body handles domestic/import/tax variants.",
    },
    "prcPostoBlerjetDDGL": {
        "domain": "Purchases / customs-DD",
        "target_txn": "DD/customs purchase-cost documents",
        "notes": "Purchase/customs-cost posting procedure; relevant where purchase cost is built through DD/import flow.",
    },
    "prcPostoPagesatGL": {
        "domain": "Payments / PGD-PGH",
        "target_txn": "PGD01/PGH01/PGPOS/PGKM1",
        "notes": "Creates payment allocation GL including cash/bank vs AR/AP, exchange-rate differences, discounts, and POS-payment bridge cases.",
    },
    "prcPostoPagesatAvanseGL": {
        "domain": "Advances / PGDA-PGHA",
        "target_txn": "PGDA1/PGHA1",
        "notes": "Advance payments/receipts; included because VAT books reference advances.",
    },
    "prcPostoPointofSaleGL": {
        "domain": "POS sales + POS payment",
        "target_txn": "POS sale + linked POS payment document",
        "notes": "Posts individual POS sale and linked POS payment, including VAT line and optional manual payment document.",
    },
    "prcPostoPointofSaleDepozitGL": {
        "domain": "POS deposit",
        "target_txn": "POS deposit sale/payment",
        "notes": "Posts POS deposit style documents.",
    },
    "prcPostoPOSDepozitGL": {
        "domain": "POS deposit",
        "target_txn": "POS deposit",
        "notes": "Posts POS deposit records.",
    },
    "prcPostoPOSDepozitCheckOutGL": {
        "domain": "POS deposit checkout",
        "target_txn": "POS deposit checkout",
        "notes": "Posts POS deposit checkout/barazim records.",
    },
    "prcPOSPaketoDokumentet": {
        "domain": "POS packing / period batch",
        "target_txn": "PKPOS/PKPAG packed POS sales and payments",
        "notes": "Packs POS documents by date/location/customer and posts packed GL; important for restaurant/POS batch behavior.",
    },
    "prcPOSPaketoDokumentetDIRTY": {
        "domain": "POS packing / dirty document",
        "target_txn": "Single dirty PKPOS/PKPAG re-post",
        "notes": "Targeted re-post version of POS packing procedure.",
    },
}


def sh(cmd: list[str], *, input_text: str | None = None, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        cmd,
        input=input_text,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=check,
    )


def run_sql_psv(password: str, sql: str) -> str:
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
        DB,
        "-h",
        "-1",
        "-W",
        "-w",
        "65535",
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
        raise RuntimeError(f"sqlcmd failed\nSTDERR:\n{proc.stderr}\nSTDOUT:\n{proc.stdout[:2000]}")
    if re.search(r"^Msg \d+", proc.stdout, flags=re.MULTILINE):
        raise RuntimeError(f"SQL Server returned errors\nSTDOUT:\n{proc.stdout[:4000]}")
    lines = []
    for line in proc.stdout.replace("\r\n", "\n").splitlines():
        if not line.strip():
            continue
        if re.match(r"^\(\d+ rows? affected\)$", line.strip()):
            continue
        lines.append(line.rstrip())
    return "\n".join(lines) + "\n"


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text.rstrip() + "\n", encoding="utf-8")


def parse_proc_definitions() -> dict[str, str]:
    text = DDL_PROC_FILE.read_text(encoding="utf-8", errors="replace")
    pattern = re.compile(
        r"(?ms)^-- =============================================================\n-- Object: \[dbo\]\.\[(?P<name>[^\]]+)\]\n(?P<body>.*?)(?=^-- =============================================================\n-- Object:|\Z)"
    )
    return {m.group("name"): m.group(0).strip() + "\n" for m in pattern.finditer(text)}


def referenced_objects(definition: str) -> list[str]:
    refs = re.findall(
        r"\b(tbl[A-Za-z0-9_]+|view[A-Za-z0-9_]+|prc[A-Za-z0-9_]+|fnc[A-Za-z0-9_]+)\b",
        definition,
        flags=re.IGNORECASE,
    )
    counts = Counter(refs)
    return [name for name, _ in counts.most_common(12)]


def gl_txn_literals(definition: str) -> list[str]:
    values = re.findall(r"'([A-Z]{2,5}GL[0-9]|SHGL2|BLGL1|PGGL1|PGGL2|PGGL4)'", definition, flags=re.IGNORECASE)
    return sorted(set(v.upper() for v in values))


def txn_literals(definition: str) -> list[str]:
    values = re.findall(r"'([A-Z]{2,5}[0-9A-Z]{2,3})'", definition)
    return sorted(set(v for v in values if any(prefix in v for prefix in ("SHV", "BLV", "BLS", "BLI", "BLN", "PGD", "PGH", "PGP", "POS", "PKP", "NOD", "NOK"))))


def isolate_procedures(definitions: dict[str, str]) -> list[dict[str, str]]:
    proc_dir = OUT_ROOT / "procedures_gl_isolated"
    proc_dir.mkdir(parents=True, exist_ok=True)
    rows: list[dict[str, str]] = []

    selected = dict(PRIMARY_GL_PROCS)
    for name, definition in definitions.items():
        lower = definition.lower()
        if "insert into [dbo].[tblgeneralledger]" in lower or "insert into tblgeneralledger" in lower:
            if name.lower().startswith("prccalculatebadcost"):
                selected.setdefault(
                    name,
                    {
                        "domain": "Costing / COGS",
                        "target_txn": "CostingEntries=1, TXN blank",
                        "notes": "Cost recalculation/re-post procedure that creates inventory/COGS entries separately from document financial posting.",
                    },
                )

    for name in sorted(selected):
        definition = definitions.get(name)
        if not definition:
            rows.append(
                {
                    "procedure": name,
                    "domain": selected[name]["domain"],
                    "target_txn": selected[name]["target_txn"],
                    "writes_gl": "MISSING_IN_DDL",
                    "deletes_gl": "",
                    "gl_txn_literals": "",
                    "txn_literals": "",
                    "top_references": "",
                    "notes": selected[name]["notes"],
                    "extracted_sql": "",
                }
            )
            continue
        out_name = proc_dir / f"{name}.sql"
        write_text(out_name, definition)
        lower = definition.lower()
        rows.append(
            {
                "procedure": name,
                "domain": selected[name]["domain"],
                "target_txn": selected[name]["target_txn"],
                "writes_gl": "yes" if "insert into [dbo].[tblgeneralledger]" in lower or "insert into tblgeneralledger" in lower else "no",
                "deletes_gl": "yes" if "delete from tblgeneralledger" in lower or "delete from [dbo].tblgeneralledger" in lower else "no",
                "gl_txn_literals": ", ".join(gl_txn_literals(definition)),
                "txn_literals": ", ".join(txn_literals(definition)[:30]),
                "top_references": ", ".join(referenced_objects(definition)),
                "notes": selected[name]["notes"],
                "extracted_sql": str(out_name.relative_to(OUT_ROOT)),
            }
        )

    map_path = OUT_ROOT / "procedure_gl_isolation_map.csv"
    with map_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=[
                "procedure",
                "domain",
                "target_txn",
                "writes_gl",
                "deletes_gl",
                "gl_txn_literals",
                "txn_literals",
                "top_references",
                "notes",
                "extracted_sql",
            ],
        )
        writer.writeheader()
        writer.writerows(rows)
    return rows


Q_DOC_GL_BRIDGE = f"""
DECLARE @d1 date = '{MONTH_START}';
DECLARE @d2 date = '{MONTH_END}';

;WITH source_docs AS (
    SELECT
        N'Sales' AS Source,
        bm.TXN,
        bm.IDTransaksioni AS DocKey,
        bm.IDTransaksioni AS GLKey,
        CAST(SUM(ISNULL(bd.Vlera_pa_TVSH,0)) AS numeric(18,5)) AS Net,
        CAST(SUM(ISNULL(bd.TVSH_V,0)) AS numeric(18,5)) AS VAT,
        CAST(SUM(ISNULL(bd.Vlera_shit,0)) AS numeric(18,5)) AS Gross,
        CAST(0 AS numeric(18,5)) AS DiscountOrRabati
    FROM dbo.tblDocShitjetMaster bm
    JOIN dbo.tblDocShitjetDetalet bd ON bd.ID = bm.ID
    WHERE bm.Data >= @d1 AND bm.Data < @d2
    GROUP BY bm.TXN, bm.IDTransaksioni

    UNION ALL
    SELECT
        N'Purchases' AS Source,
        bm.TXN,
        bm.IDTransaksioni AS DocKey,
        bm.IDTransaksioni AS GLKey,
        CAST(SUM(ISNULL(bd.Vlera_furn,0)) AS numeric(18,5)) AS Net,
        CAST(SUM(ISNULL(bd.TVSH_V,0)) AS numeric(18,5)) AS VAT,
        CAST(SUM(ISNULL(bd.VleraMeTVSH,0)) AS numeric(18,5)) AS Gross,
        CAST(0 AS numeric(18,5)) AS DiscountOrRabati
    FROM dbo.tblDocBlerjetMaster bm
    JOIN dbo.tblDocBlerjetDetalet bd ON bd.ID = bm.ID
    WHERE (CASE WHEN bm.TXN IN ('BLS01','BLV01') THEN bm.DataFatures ELSE bm.Data END) >= @d1
      AND (CASE WHEN bm.TXN IN ('BLS01','BLV01') THEN bm.DataFatures ELSE bm.Data END) < @d2
    GROUP BY bm.TXN, bm.IDTransaksioni

    UNION ALL
    SELECT
        N'Payments' AS Source,
        pm.TXN,
        pm.IDTransaksioni AS DocKey,
        pm.IDTransaksioni AS GLKey,
        CAST(SUM(ISNULL(pd.Pagesa,0)) AS numeric(18,5)) AS Net,
        CAST(0 AS numeric(18,5)) AS VAT,
        CAST(SUM(ISNULL(pd.Pagesa,0)) AS numeric(18,5)) AS Gross,
        CAST(SUM(ISNULL(pd.Rabati,0)) AS numeric(18,5)) AS DiscountOrRabati
    FROM dbo.tblDocPagesatMaster pm
    JOIN dbo.tblDocPagesatDetalet pd ON pd.ID = pm.ID
    WHERE pm.Data >= @d1 AND pm.Data < @d2
    GROUP BY pm.TXN, pm.IDTransaksioni

    UNION ALL
    SELECT
        N'POS Sales' AS Source,
        pm.TXN,
        pm.IDTransaksioni AS DocKey,
        ISNULL(pm.MasterDokumenti, pm.IDTransaksioni) AS GLKey,
        CAST(SUM(ISNULL(pd.Vlera_pa_TVSH,0)) AS numeric(18,5)) AS Net,
        CAST(SUM(ISNULL(pd.TVSH_V,0)) AS numeric(18,5)) AS VAT,
        CAST(SUM(ISNULL(pd.Vlera_shit,0)) AS numeric(18,5)) AS Gross,
        CAST(SUM(ISNULL(pd.Rabati_V,0)) AS numeric(18,5)) AS DiscountOrRabati
    FROM dbo.tblDocPOSShitjetMaster pm
    JOIN dbo.tblDocPOSShitjetDetalet pd ON pd.ID = pm.ID
    WHERE pm.Data >= @d1 AND pm.Data < @d2
    GROUP BY pm.TXN, pm.IDTransaksioni, pm.MasterDokumenti

    UNION ALL
    SELECT
        N'POS Payments' AS Source,
        pp.TXN,
        pp.IDTransaksioni AS DocKey,
        ISNULL(pp.MasterDokumenti, pp.IDTransaksioni) AS GLKey,
        CAST(SUM(ISNULL(pp.Vlera,0)) AS numeric(18,5)) AS Net,
        CAST(0 AS numeric(18,5)) AS VAT,
        CAST(SUM(ISNULL(pp.Vlera,0)) AS numeric(18,5)) AS Gross,
        CAST(0 AS numeric(18,5)) AS DiscountOrRabati
    FROM dbo.tblDocPOSPagesat pp
    WHERE pp.Data >= @d1 AND pp.Data < @d2
    GROUP BY pp.TXN, pp.IDTransaksioni, pp.MasterDokumenti
),
gl_by_key AS (
    SELECT
        Transaksioni AS GLKey,
        COUNT(*) AS GLLines,
        CAST(SUM(ISNULL(Debi,0)) AS numeric(18,5)) AS GLDebiAll,
        CAST(SUM(ISNULL(Kredi,0)) AS numeric(18,5)) AS GLKrediAll,
        CAST(SUM(CASE WHEN ISNULL(CostingEntries,0)=0 THEN ISNULL(Debi,0) ELSE 0 END) AS numeric(18,5)) AS GLDebiNoCost,
        CAST(SUM(CASE WHEN ISNULL(CostingEntries,0)=0 THEN ISNULL(Kredi,0) ELSE 0 END) AS numeric(18,5)) AS GLKrediNoCost,
        CAST(SUM(CASE WHEN ISNULL(CostingEntries,0)=1 THEN ISNULL(Debi,0) ELSE 0 END) AS numeric(18,5)) AS GLDebiCost,
        CAST(SUM(CASE WHEN ISNULL(CostingEntries,0)=1 THEN ISNULL(Kredi,0) ELSE 0 END) AS numeric(18,5)) AS GLKrediCost
    FROM dbo.tblGeneralLedger
    GROUP BY Transaksioni
),
agg AS (
    SELECT
        sd.Source,
        ISNULL(sd.TXN, N'<NULL>') AS DocTXN,
        COUNT(DISTINCT sd.DocKey) AS Docs,
        COUNT(DISTINCT sd.GLKey) AS GLKeys,
        COUNT(DISTINCT CASE WHEN gl.GLKey IS NOT NULL THEN sd.DocKey END) AS DocsWithGL,
        CAST(SUM(sd.Net) AS numeric(18,2)) AS DocNet,
        CAST(SUM(sd.VAT) AS numeric(18,2)) AS DocVAT,
        CAST(SUM(sd.Gross) AS numeric(18,2)) AS DocGrossOrPayment,
        CAST(SUM(sd.DiscountOrRabati) AS numeric(18,2)) AS DiscountOrRabati,
        SUM(ISNULL(gl.GLLines,0)) AS GLLines,
        CAST(SUM(ISNULL(gl.GLDebiAll,0)) AS numeric(18,2)) AS GLDebiAll,
        CAST(SUM(ISNULL(gl.GLKrediAll,0)) AS numeric(18,2)) AS GLKrediAll,
        CAST(SUM(ISNULL(gl.GLDebiNoCost,0)) AS numeric(18,2)) AS GLDebiNoCost,
        CAST(SUM(ISNULL(gl.GLKrediNoCost,0)) AS numeric(18,2)) AS GLKrediNoCost,
        CAST(SUM(ISNULL(gl.GLDebiCost,0)) AS numeric(18,2)) AS GLDebiCost,
        CAST(SUM(ISNULL(gl.GLKrediCost,0)) AS numeric(18,2)) AS GLKrediCost
    FROM source_docs sd
    LEFT JOIN gl_by_key gl ON gl.GLKey = sd.GLKey
    GROUP BY sd.Source, ISNULL(sd.TXN, N'<NULL>')
),
lines AS (
    SELECT
        0 AS sort_key,
        CAST(N'Source|DocTXN|Docs|GLKeys|DocsWithGL|PctDocsWithGL|DocNet|DocVAT|DocGrossOrPayment|DiscountOrRabati|GLLines|GLDebiAll|GLKrediAll|GLDiffAll|GLDebiNoCost|GLKrediNoCost|GLDiffNoCost|GLDebiCost|GLKrediCost|GLDiffCost' AS nvarchar(max)) AS line
    UNION ALL
    SELECT
        1 AS sort_key,
        CAST(CONCAT(
            Source, N'|', DocTXN, N'|', Docs, N'|', GLKeys, N'|', DocsWithGL, N'|',
            CAST(CASE WHEN Docs=0 THEN 0 ELSE DocsWithGL*100.0/Docs END AS numeric(18,2)), N'|',
            DocNet, N'|', DocVAT, N'|', DocGrossOrPayment, N'|', DiscountOrRabati, N'|',
            GLLines, N'|', GLDebiAll, N'|', GLKrediAll, N'|', CAST(GLDebiAll-GLKrediAll AS numeric(18,2)), N'|',
            GLDebiNoCost, N'|', GLKrediNoCost, N'|', CAST(GLDebiNoCost-GLKrediNoCost AS numeric(18,2)), N'|',
            GLDebiCost, N'|', GLKrediCost, N'|', CAST(GLDebiCost-GLKrediCost AS numeric(18,2))
        ) AS nvarchar(max)) AS line
    FROM agg
)
SELECT line FROM lines ORDER BY sort_key, line;
"""


Q_GL_BY_TXN_ACCOUNT = f"""
DECLARE @d1 date = '{MONTH_START}';
DECLARE @d2 date = '{MONTH_END}';

;WITH agg AS (
    SELECT
        COALESCE(CAST(NULLIF(gl.TXN,N'') AS nvarchar(20)), N'<BLANK>') AS GL_TXN,
        ISNULL(gl.Kontoja,N'') AS Account,
        ISNULL(pk.Description,N'') AS AccountName,
        ISNULL(CAST(gl.CostingEntries AS nvarchar(5)), N'') AS CostingEntries,
        COUNT(*) AS Lines,
        COUNT(DISTINCT gl.Transaksioni) AS Documents,
        CAST(SUM(ISNULL(gl.Debi,0)) AS numeric(18,2)) AS Debi,
        CAST(SUM(ISNULL(gl.Kredi,0)) AS numeric(18,2)) AS Kredi
    FROM dbo.tblGeneralLedger gl
    LEFT JOIN dbo.tblPlaniKontabel pk ON pk.Account = gl.Kontoja
    WHERE gl.Data >= @d1 AND gl.Data < @d2
      AND (
        gl.TXN IN ('SHGL1','SHGL2','BLGL1','PGGL1','PGGL2','PGGL4')
        OR ISNULL(gl.TXN,N'') = N''
      )
    GROUP BY COALESCE(CAST(NULLIF(gl.TXN,N'') AS nvarchar(20)), N'<BLANK>'), ISNULL(gl.Kontoja,N''), ISNULL(pk.Description,N''), ISNULL(CAST(gl.CostingEntries AS nvarchar(5)), N'')
),
lines AS (
    SELECT 0 AS sort_key, CAST(N'GL_TXN|Account|AccountName|CostingEntries|Lines|Documents|Debi|Kredi|Diff' AS nvarchar(max)) AS line
    UNION ALL
    SELECT 1 AS sort_key,
        CAST(CONCAT(GL_TXN, N'|', Account, N'|', REPLACE(AccountName,N'|',N'/'), N'|', CostingEntries, N'|',
            Lines, N'|', Documents, N'|', Debi, N'|', Kredi, N'|', CAST(Debi-Kredi AS numeric(18,2))) AS nvarchar(max)) AS line
    FROM agg
)
SELECT line FROM lines ORDER BY sort_key, line;
"""


Q_POS_ALLTIME_BRIDGE = """
;WITH source_docs AS (
    SELECT
        N'POS Sales' AS Source,
        pm.TXN,
        pm.IDTransaksioni AS DocKey,
        ISNULL(pm.MasterDokumenti, pm.IDTransaksioni) AS GLKey,
        MIN(pm.Data) AS Data,
        CAST(SUM(ISNULL(pd.Vlera_pa_TVSH,0)) AS numeric(18,5)) AS Net,
        CAST(SUM(ISNULL(pd.TVSH_V,0)) AS numeric(18,5)) AS VAT,
        CAST(SUM(ISNULL(pd.Vlera_shit,0)) AS numeric(18,5)) AS Gross
    FROM dbo.tblDocPOSShitjetMaster pm
    JOIN dbo.tblDocPOSShitjetDetalet pd ON pd.ID = pm.ID
    GROUP BY pm.TXN, pm.IDTransaksioni, pm.MasterDokumenti

    UNION ALL
    SELECT
        N'POS Payments' AS Source,
        pp.TXN,
        pp.IDTransaksioni AS DocKey,
        ISNULL(pp.MasterDokumenti, pp.IDTransaksioni) AS GLKey,
        MIN(pp.Data) AS Data,
        CAST(SUM(ISNULL(pp.Vlera,0)) AS numeric(18,5)) AS Net,
        CAST(0 AS numeric(18,5)) AS VAT,
        CAST(SUM(ISNULL(pp.Vlera,0)) AS numeric(18,5)) AS Gross
    FROM dbo.tblDocPOSPagesat pp
    GROUP BY pp.TXN, pp.IDTransaksioni, pp.MasterDokumenti
),
gl_by_key AS (
    SELECT
        Transaksioni AS GLKey,
        COUNT(*) AS GLLines,
        CAST(SUM(ISNULL(Debi,0)) AS numeric(18,5)) AS GLDebiAll,
        CAST(SUM(ISNULL(Kredi,0)) AS numeric(18,5)) AS GLKrediAll,
        CAST(SUM(CASE WHEN ISNULL(CostingEntries,0)=0 THEN ISNULL(Debi,0) ELSE 0 END) AS numeric(18,5)) AS GLDebiNoCost,
        CAST(SUM(CASE WHEN ISNULL(CostingEntries,0)=0 THEN ISNULL(Kredi,0) ELSE 0 END) AS numeric(18,5)) AS GLKrediNoCost,
        CAST(SUM(CASE WHEN ISNULL(CostingEntries,0)=1 THEN ISNULL(Debi,0) ELSE 0 END) AS numeric(18,5)) AS GLDebiCost,
        CAST(SUM(CASE WHEN ISNULL(CostingEntries,0)=1 THEN ISNULL(Kredi,0) ELSE 0 END) AS numeric(18,5)) AS GLKrediCost
    FROM dbo.tblGeneralLedger
    GROUP BY Transaksioni
),
agg AS (
    SELECT
        sd.Source,
        ISNULL(sd.TXN, N'<NULL>') AS DocTXN,
        COUNT(DISTINCT sd.DocKey) AS Docs,
        COUNT(DISTINCT sd.GLKey) AS GLKeys,
        COUNT(DISTINCT CASE WHEN gl.GLKey IS NOT NULL THEN sd.DocKey END) AS DocsWithGL,
        CONVERT(nvarchar(10), MIN(sd.Data), 120) AS MinDate,
        CONVERT(nvarchar(10), MAX(sd.Data), 120) AS MaxDate,
        CAST(SUM(sd.Net) AS numeric(18,2)) AS DocNet,
        CAST(SUM(sd.VAT) AS numeric(18,2)) AS DocVAT,
        CAST(SUM(sd.Gross) AS numeric(18,2)) AS DocGrossOrPayment,
        SUM(ISNULL(gl.GLLines,0)) AS GLLines,
        CAST(SUM(ISNULL(gl.GLDebiAll,0)) AS numeric(18,2)) AS GLDebiAll,
        CAST(SUM(ISNULL(gl.GLKrediAll,0)) AS numeric(18,2)) AS GLKrediAll,
        CAST(SUM(ISNULL(gl.GLDebiNoCost,0)) AS numeric(18,2)) AS GLDebiNoCost,
        CAST(SUM(ISNULL(gl.GLKrediNoCost,0)) AS numeric(18,2)) AS GLKrediNoCost,
        CAST(SUM(ISNULL(gl.GLDebiCost,0)) AS numeric(18,2)) AS GLDebiCost,
        CAST(SUM(ISNULL(gl.GLKrediCost,0)) AS numeric(18,2)) AS GLKrediCost
    FROM source_docs sd
    LEFT JOIN gl_by_key gl ON gl.GLKey = sd.GLKey
    GROUP BY sd.Source, ISNULL(sd.TXN, N'<NULL>')
),
lines AS (
    SELECT
        0 AS sort_key,
        CAST(N'Source|DocTXN|Docs|GLKeys|DocsWithGL|PctDocsWithGL|MinDate|MaxDate|DocNet|DocVAT|DocGrossOrPayment|GLLines|GLDebiAll|GLKrediAll|GLDiffAll|GLDebiNoCost|GLKrediNoCost|GLDiffNoCost|GLDebiCost|GLKrediCost|GLDiffCost' AS nvarchar(max)) AS line
    UNION ALL
    SELECT
        1 AS sort_key,
        CAST(CONCAT(
            Source, N'|', DocTXN, N'|', Docs, N'|', GLKeys, N'|', DocsWithGL, N'|',
            CAST(CASE WHEN Docs=0 THEN 0 ELSE DocsWithGL*100.0/Docs END AS numeric(18,2)), N'|',
            MinDate, N'|', MaxDate, N'|',
            DocNet, N'|', DocVAT, N'|', DocGrossOrPayment, N'|',
            GLLines, N'|', GLDebiAll, N'|', GLKrediAll, N'|', CAST(GLDebiAll-GLKrediAll AS numeric(18,2)), N'|',
            GLDebiNoCost, N'|', GLKrediNoCost, N'|', CAST(GLDebiNoCost-GLKrediNoCost AS numeric(18,2)), N'|',
            GLDebiCost, N'|', GLKrediCost, N'|', CAST(GLDebiCost-GLKrediCost AS numeric(18,2))
        ) AS nvarchar(max)) AS line
    FROM agg
)
SELECT line FROM lines ORDER BY sort_key, line;
"""


def write_sql_outputs(password: str) -> None:
    data_dir = OUT_ROOT / "data"
    data_dir.mkdir(parents=True, exist_ok=True)
    write_text(data_dir / "month_document_to_gl_bridge_2026_06.psv", run_sql_psv(password, Q_DOC_GL_BRIDGE))
    write_text(data_dir / "month_gl_by_txn_account_2026_06.psv", run_sql_psv(password, Q_GL_BY_TXN_ACCOUNT))
    write_text(data_dir / "pos_alltime_to_gl_bridge.psv", run_sql_psv(password, Q_POS_ALLTIME_BRIDGE))


def parse_decimal(value: str) -> Decimal:
    value = (value or "").strip()
    if not value or set(value) <= {"-"}:
        return Decimal("0")
    if value.startswith("."):
        value = "0" + value
    if value.startswith("-."):
        value = value.replace("-.", "-0.", 1)
    try:
        return Decimal(value)
    except InvalidOperation:
        return Decimal("0")


def read_psv_dicts(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8", errors="replace", newline="") as f:
        rows = list(csv.reader(f, delimiter="|"))
    if not rows:
        return []
    header = rows[0]
    out = []
    for row in rows[1:]:
        if not row or all(not cell.strip() for cell in row):
            continue
        if all(set(cell.strip()) <= {"-"} for cell in row):
            continue
        if len(row) < len(header):
            row = row + [""] * (len(header) - len(row))
        out.append(dict(zip(header, row)))
    return out


def money_sum(rows: list[dict[str, str]], column: str) -> Decimal:
    return sum((parse_decimal(row.get(column, "")) for row in rows), Decimal("0"))


def write_tax_book_comparison() -> list[dict[str, str]]:
    sales_file = REPORT_ROOT / "planet2_libri_shitjes_2026_06_test.psv"
    purchases_file = REPORT_ROOT / "planet2_libri_blerjes_2026_06_test.psv"
    tvsh_file = REPORT_ROOT / "tvsh_monthly_summary_2023_2026.csv"

    sales = read_psv_dicts(sales_file)
    purchases = read_psv_dicts(purchases_file)

    summary_rows = [
        {
            "Report": "Libri i Shitjes",
            "Rows": str(len(sales)),
            "Net": f"{money_sum(sales, '#Vlera totale pa TVSH'):.2f}",
            "VAT": f"{money_sum(sales, '#TVSH Totale'):.2f}",
            "Gross": f"{money_sum(sales, '#Vlera Totale me TVSH'):.2f}",
            "Tax18Base": f"{money_sum(sales, '#Shitjet e tatueshme 18%'):.2f}",
            "Tax18VAT": f"{money_sum(sales, '#TVSH e llogaritur / Shitjet 18%'):.2f}",
            "Tax8Base": f"{money_sum(sales, '#Shitjet e tatueshme 8%'):.2f}",
            "Tax8VAT": f"{money_sum(sales, '#TVSH e llogaritur / Shitjet 8%'):.2f}",
            "FarmerBase": "0.00",
            "FarmerVAT": "0.00",
            "SourceFile": str(sales_file),
        },
        {
            "Report": "Libri i Blerjes",
            "Rows": str(len(purchases)),
            "Net": f"{money_sum(purchases, '#Vlera totale pa TVSH'):.2f}",
            "VAT": f"{money_sum(purchases, '#TVSH Totale'):.2f}",
            "Gross": f"{money_sum(purchases, '#Vlera Totale me TVSH'):.2f}",
            "Tax18Base": f"{money_sum(purchases, '#Blerjet e tatueshme vendore 18%'):.2f}",
            "Tax18VAT": f"{money_sum(purchases, '#TVSH e zbritshme / Blerjet Vendore 18%'):.2f}",
            "Tax8Base": f"{money_sum(purchases, '#Blerjet e tatueshme vendore 8%'):.2f}",
            "Tax8VAT": f"{money_sum(purchases, '#TVSH e zbritshme / Blerjet Vendore 8%'):.2f}",
            "FarmerBase": f"{money_sum(purchases, '#Blerjet nga fermeret vendore 8%'):.2f}",
            "FarmerVAT": f"{money_sum(purchases, 'TVSH e zbritshme / Blerjet nga fermeret vendore 8%'):.2f}",
            "SourceFile": str(purchases_file),
        },
    ]

    with (OUT_ROOT / "data" / "tax_book_june_2026_summary.csv").open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=[
                "Report",
                "Rows",
                "Net",
                "VAT",
                "Gross",
                "Tax18Base",
                "Tax18VAT",
                "Tax8Base",
                "Tax8VAT",
                "FarmerBase",
                "FarmerVAT",
                "SourceFile",
            ],
        )
        writer.writeheader()
        writer.writerows(summary_rows)

    tvsh_rows = []
    with tvsh_file.open("r", encoding="utf-8", errors="replace", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            if row.get("business") == "PLANET2" and row.get("month") == MONTH_LABEL:
                tvsh_rows.append(row)
    with (OUT_ROOT / "data" / "tvsh_monthly_summary_planet2_2026_06.csv").open("w", newline="", encoding="utf-8") as f:
        if tvsh_rows:
            writer = csv.DictWriter(f, fieldnames=list(tvsh_rows[0].keys()))
            writer.writeheader()
            writer.writerows(tvsh_rows)
    return summary_rows


def read_psv_table(path: Path) -> list[list[str]]:
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    return [line.split("|") for line in lines if line.strip()]


def table_from_csv(path: Path, limit: int | None = None) -> str:
    with path.open("r", encoding="utf-8", errors="replace", newline="") as f:
        rows = list(csv.reader(f))
    if limit:
        rows = rows[:limit]
    if not rows:
        return ""
    header = rows[0]
    body = rows[1:]
    out = ["| " + " | ".join(header) + " |", "| " + " | ".join(["---"] * len(header)) + " |"]
    for row in body:
        out.append("| " + " | ".join(row) + " |")
    return "\n".join(out)


def table_from_psv(path: Path, limit: int | None = None) -> str:
    rows = read_psv_table(path)
    if limit:
        rows = rows[:limit]
    if not rows:
        return ""
    header = rows[0]
    body = rows[1:]
    out = ["| " + " | ".join(header) + " |", "| " + " | ".join(["---"] * len(header)) + " |"]
    for row in body:
        out.append("| " + " | ".join(row) + " |")
    return "\n".join(out)


def write_report(proc_rows: list[dict[str, str]], tax_rows: list[dict[str, str]]) -> None:
    bridge = OUT_ROOT / "data" / "month_document_to_gl_bridge_2026_06.psv"
    gl_by_account = OUT_ROOT / "data" / "month_gl_by_txn_account_2026_06.psv"
    pos_alltime = OUT_ROOT / "data" / "pos_alltime_to_gl_bridge.psv"
    tvsh = OUT_ROOT / "data" / "tvsh_monthly_summary_planet2_2026_06.csv"

    relevant_proc_rows = [r for r in proc_rows if r["procedure"] in PRIMARY_GL_PROCS or "Costing" in r["domain"]]
    proc_table_lines = [
        "| Procedure | Domain | Target TXN | GL TXN literals | Notes |",
        "| --- | --- | --- | --- | --- |",
    ]
    for row in relevant_proc_rows:
        proc_table_lines.append(
            "| "
            + " | ".join(
                [
                    f"`{row['procedure']}`",
                    row["domain"],
                    row["target_txn"],
                    row["gl_txn_literals"] or "-",
                    row["notes"],
                ]
            )
            + " |"
        )

    report = [
        "# PRO DATA GL Posting Procedure Isolation + June 2026 Reconciliation",
        "",
        f"- Generated: `{dt.datetime.now().astimezone().isoformat(timespec='seconds')}`",
        f"- Database: `{DB}`",
        f"- Month: `{MONTH_LABEL}` (`{MONTH_START}` inclusive → `{MONTH_END}` exclusive)",
        "- Scope: SHV, BLV/BLS, PGD/PGH, POS payments, document → GL → VAT books → VAT summary.",
        "- Status: evidence analysis for iData+ accounting parity; not a migration script.",
        "",
        "## Executive verdict",
        "",
        "PRO DATA e ka posting-un financiar të ndarë nga raportimi TVSH. Procedurat `prcPosto...GL` krijojnë rreshtat në `tblGeneralLedger`; procedurat `prcRPTGRDLibriShitjetADM18` dhe `prcRPTGRDLibriBlerjetADM18` i lexojnë dokumentet burimore për Librin e Shitjes/Blerjes. Për iData+ kjo e konfirmon modelin: **document posting engine** + **tax-book/reporting engine** të ndara, por të rakorduara me të njëjtat dokumente.",
        "",
        "Gjetja më e rëndësishme: shitja normale (`SHV*`) krijon financial GL përmes `prcPostoShitjetGL`, ndërsa COGS/stoku del nga cost engine me `CostingEntries=1` dhe zakonisht `TXN` bosh. Pra në iData+ nuk duhet ta fusim COGS si “rresht aksidental” brenda shitjes; duhet të jetë posting i kontrolluar, por i lidhur me dokumentin.",
        "",
        "## 1. Procedurat e izoluara që krijojnë GL",
        "",
        "\n".join(proc_table_lines),
        "",
        "Shiko SQL-të e izoluara në `procedures_gl_isolated/`. Këto janë kandidatët realë ku duhet të lexojmë logjikën e posting rules për ta rindërtuar në iData+.",
        "",
        "## 2. Krahasimi dokumente → GL për qershor 2026",
        "",
        table_from_psv(bridge),
        "",
        "Lexim i shpejtë:",
        "",
        "- `DocsWithGL` tregon mbulimin dokument → GL.",
        "- `GLDebiNoCost/GLKrediNoCost` janë financial postings pa COGS.",
        "- `GLDebiCost/GLKrediCost` janë COGS/stok ku `CostingEntries=1`.",
        "- Për POS, `GLKey` mund të jetë `MasterDokumenti`, sepse PRO DATA i paketon POS dokumentet në `PKPOS/PKPAG`.",
        "",
        "## 3. GL për qershor 2026 sipas TXN + konto",
        "",
        table_from_psv(gl_by_account, limit=80),
        "",
        "Ky file ka më shumë rreshta se sa shfaqen këtu: `data/month_gl_by_txn_account_2026_06.psv`.",
        "",
        "## 4. POS all-time bridge",
        "",
        "Në `PLANET2`, `tblDocPOS*` nuk ka rreshta në qershor 2026; POS rows ekzistojnë 2024-07 → 2025-10. Prandaj POS e kontrollojmë si bridge veçmas, që të mos humbet logjika e POS payments.",
        "",
        table_from_psv(pos_alltime),
        "",
        "## 5. Libri i Shitjes/Blerjes → TVSH summary",
        "",
        table_from_csv(OUT_ROOT / "data" / "tax_book_june_2026_summary.csv"),
        "",
        "TVSH monthly summary për PLANET2 2026-06:",
        "",
        table_from_csv(tvsh),
        "",
        "## 6. Posting logic që duhet ta mbajmë për iData+",
        "",
        "### SHV / shitje normale",
        "",
        "- Burimi: `tblDocShitjetMaster` + `viewDetailedSalesIncludindFD` / detajet.",
        "- Procedura: `prcPostoShitjetGL`.",
        "- Debit kryesor: konto e arkëtueshme (`KontojaeArketueshme`, p.sh. AR).",
        "- Kredit kryesor: konto e të hyrave nga detajet (`dcd.Kontoja`).",
        "- TVSH: konto nga `tblTarifat.Account`.",
        "- Rabat/akcizë/tatim në burim trajtohen me konto nga `tblSystemAppOptions`.",
        "- COGS/stok: jo brenda financial posting; vjen nga cost engine me `CostingEntries=1`.",
        "",
        "### BLV/BLS / blerje",
        "",
        "- Burimi: `tblDocBlerjetMaster`, `tblDocBlerjetDetalet`, dhe për disa raste `tblDocBlerjetShpenzimetTjera`.",
        "- Procedura kryesore: `prcPostoBlerjetImportGL`.",
        "- Debit: mall/stok ose shpenzim, TVSH input, shpenzime të tjera/import/capitalized VAT sipas flags.",
        "- Kredit: furnitor/AP (`KontojaePagueshme`).",
        "- Për iData+ duhet mixed-rate, non-deductible VAT, import/customs, shpenzime të tjera, dhe farmer flat-rate bucket.",
        "",
        "### PGD/PGH / pagesa",
        "",
        "- Burimi: `tblDocPagesatMaster` + `tblDocPagesatDetalet`.",
        "- Procedura: `prcPostoPagesatGL`.",
        "- `PGH01`: zakonisht arkëtim nga klienti → debi arkë/bankë, kredi AR.",
        "- `PGD01`: pagesë te furnitori → debi AP, kredi arkë/bankë.",
        "- Mbështet rabat, valutë, exchange-rate differences, dhe lidhje me dokumentin e paguar përmes `IDTransaksion`.",
        "",
        "### POS payments",
        "",
        "- Ka dy rrugë: individuale (`prcPostoPointofSaleGL`) dhe e paketuar (`prcPOSPaketoDokumentet`).",
        "- POS sales/payment mund të lidhen me `MasterDokumenti` (`PKPOS/PKPAG`), jo vetëm me `IDTransaksioni` origjinal.",
        "- Për PLANET, kjo është shumë e rëndësishme: local hub/POS duhet të ruajë ID origjinale dhe posting batch/master ID pa humbur trace.",
        "",
        "## 7. Çka duhet të bëjmë në iData+",
        "",
        "1. Ndërto `posting_rules` të konfigurueshme: document type → debit/credit lines → account source → tax behavior → subledger effect.",
        "2. Ndaj `financial posting` nga `cost posting`, por lidhi me të njëjtin document/event ID.",
        "3. Ndërto `tax book engine` nga dokumentet e postuara + tax rules, jo nga GL vetëm.",
        "4. Për POS, ruaj `device/local document id`, `cloud document id`, `batch/master posting id`, dhe `fiscal transaction id` si lidhje të ndara.",
        "5. Në parallel month, krahaso: dokumente → GL → Libri Shitjes/Blerjes → TVSH summary → deklaratë kontabilisti.",
        "",
        "## Evidence files",
        "",
        "- `procedure_gl_isolation_map.csv`",
        "- `procedures_gl_isolated/*.sql`",
        "- `data/month_document_to_gl_bridge_2026_06.psv`",
        "- `data/month_gl_by_txn_account_2026_06.psv`",
        "- `data/pos_alltime_to_gl_bridge.psv`",
        "- `data/tax_book_june_2026_summary.csv`",
        "- `data/tvsh_monthly_summary_planet2_2026_06.csv`",
    ]
    write_text(OUT_ROOT / "PRODATA_GL_Posting_Reconciliation_June2026.md", "\n".join(report))


def zip_output() -> None:
    zip_path = OUT_ROOT.with_suffix(".zip")
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for path in OUT_ROOT.rglob("*"):
            if path.is_file():
                zf.write(path, path.relative_to(OUT_ROOT.parent))


def main() -> None:
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    definitions = parse_proc_definitions()
    proc_rows = isolate_procedures(definitions)
    password = get_sa_password()
    write_sql_outputs(password)
    tax_rows = write_tax_book_comparison()
    write_report(proc_rows, tax_rows)
    zip_output()
    print(f"Done. Output: {OUT_ROOT}")


if __name__ == "__main__":
    main()
