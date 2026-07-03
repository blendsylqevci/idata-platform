#!/usr/bin/env python3
"""
Deep-dive PRO DATA VAT-book procedures and posting trace to tblGeneralLedger.

Outputs an evidence pack under outputs/prodata_posting_deep_dive_2026-06-28.
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
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from extract_prodata_ddl import CONTAINER, SQLCMD, get_sa_password  # noqa: E402


DB = os.environ.get("PRODATA_ANALYSIS_DB", "PRODATA_PLANET2")
DDL_PROC_FILE = Path(f"outputs/prodata_ddl_extracted_2026-06-28/databases/{DB}/12_stored_procedures.sql")
OUT_ROOT = Path("outputs/prodata_posting_deep_dive_2026-06-28")
PROC_NAMES = ["prcRPTGRDLibriShitjetADM18", "prcRPTGRDLibriBlerjetADM18"]


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


def extract_proc_definition(proc_name: str) -> str:
    text = DDL_PROC_FILE.read_text(encoding="utf-8", errors="replace")
    marker = f"-- Object: [dbo].[{proc_name}]"
    start = text.find(marker)
    if start == -1:
        raise RuntimeError(f"Procedure marker not found: {proc_name}")
    next_marker = text.find("\n-- =============================================================\n-- Object:", start + len(marker))
    if next_marker == -1:
        next_marker = len(text)
    return text[start:next_marker].strip() + "\n"


def extract_references(definition: str) -> Counter[str]:
    # Focus on user objects that carry business meaning.
    refs = re.findall(r"\b(tbl[A-Za-z0-9_]+|view[A-Za-z0-9_]+|prc[A-Za-z0-9_]+|fnc[A-Za-z0-9_]+)\b", definition, flags=re.IGNORECASE)
    return Counter(refs)


def write_reference_map(definitions: dict[str, str]) -> None:
    rows: list[dict[str, str]] = []
    for proc_name, definition in definitions.items():
        for ref, count in sorted(extract_references(definition).items(), key=lambda x: (-x[1], x[0].lower())):
            rows.append({"procedure": proc_name, "referenced_object": ref, "count": str(count)})
    path = OUT_ROOT / "procedure_reference_map.csv"
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=["procedure", "referenced_object", "count"])
        writer.writeheader()
        writer.writerows(rows)


Q_GL_TXN_SUMMARY = r"""
;WITH x AS (
    SELECT
        ISNULL(TXN, N'<NULL>') AS TXN,
        COUNT(*) AS Lines,
        COUNT(DISTINCT Transaksioni) AS Documents,
        CAST(SUM(ISNULL(Debi, 0)) AS numeric(18,2)) AS Debi,
        CAST(SUM(ISNULL(Kredi, 0)) AS numeric(18,2)) AS Kredi
    FROM dbo.tblGeneralLedger
    GROUP BY ISNULL(TXN, N'<NULL>')
),
lines AS (
    SELECT 0 AS sort_key, CAST(N'TXN|Lines|Documents|Debi|Kredi|Diff' AS nvarchar(max)) AS line
    UNION ALL
    SELECT
        1 AS sort_key,
        CAST(CONCAT(TXN, N'|', Lines, N'|', Documents, N'|', Debi, N'|', Kredi, N'|', CAST(Debi-Kredi AS numeric(18,2))) AS nvarchar(max)) AS line
    FROM x
)
SELECT line
FROM lines
ORDER BY sort_key, line;
"""


Q_SALES_TRACE = r"""
;WITH docs AS (
    SELECT TOP (10)
        bm.ID,
        bm.IDTransaksioni,
        bm.TXN,
        CONVERT(nvarchar(10), bm.Data, 120) AS Data,
        bm.Bleresi,
        CAST(SUM(ISNULL(bd.Vlera_pa_TVSH,0)) AS numeric(18,2)) AS VleraPaTVSH,
        CAST(SUM(ISNULL(bd.TVSH_V,0)) AS numeric(18,2)) AS TVSH,
        CAST(SUM(ISNULL(bd.Vlera_shit,0)) AS numeric(18,2)) AS VleraMeTVSH
    FROM dbo.tblDocShitjetMaster bm
    JOIN dbo.tblDocShitjetDetalet bd ON bd.ID = bm.ID
    WHERE EXISTS (SELECT 1 FROM dbo.tblGeneralLedger gl WHERE gl.Transaksioni = bm.IDTransaksioni)
    GROUP BY bm.ID, bm.IDTransaksioni, bm.TXN, bm.Data, bm.Bleresi
    ORDER BY bm.Data DESC, bm.ID DESC
),
gl AS (
    SELECT
        d.IDTransaksioni,
        gl.TXN AS GL_TXN,
        gl.Tipi,
        CONVERT(nvarchar(10), gl.Data, 120) AS GLData,
        gl.Kontoja,
        pk.Description AS KontoEmertimi,
        gl.Subjekti,
        gl.TransaksioniNderlidhes,
        CAST(ISNULL(gl.Debi,0) AS numeric(18,2)) AS Debi,
        CAST(ISNULL(gl.Kredi,0) AS numeric(18,2)) AS Kredi,
        gl.DRCR,
        gl.CostingEntries
    FROM docs d
    JOIN dbo.tblGeneralLedger gl ON gl.Transaksioni = d.IDTransaksioni
    LEFT JOIN dbo.tblPlaniKontabel pk ON pk.Account = gl.Kontoja
)
SELECT N'Doc|DocTXN|DocDate|CustomerID|DocNoVAT|DocVAT|DocTotal|GL_TXN|Tipi|GLDate|Account|AccountName|Subject|LinkedTxn|Debi|Kredi|DRCR|CostingEntries'
UNION ALL
SELECT CAST(CONCAT(
    d.IDTransaksioni, N'|', d.TXN, N'|', d.Data, N'|', ISNULL(CAST(d.Bleresi AS nvarchar(30)), N''), N'|',
    d.VleraPaTVSH, N'|', d.TVSH, N'|', d.VleraMeTVSH, N'|',
    ISNULL(g.GL_TXN,N''), N'|', ISNULL(CAST(g.Tipi AS nvarchar(30)), N''), N'|', ISNULL(g.GLData,N''), N'|',
    ISNULL(g.Kontoja,N''), N'|', ISNULL(REPLACE(g.KontoEmertimi,N'|',N'/'),N''), N'|',
    ISNULL(CAST(g.Subjekti AS nvarchar(30)), N''), N'|', ISNULL(g.TransaksioniNderlidhes,N''), N'|',
    g.Debi, N'|', g.Kredi, N'|', ISNULL(g.DRCR,N''), N'|', ISNULL(CAST(g.CostingEntries AS nvarchar(10)),N'')
) AS nvarchar(max))
FROM docs d
LEFT JOIN gl g ON g.IDTransaksioni = d.IDTransaksioni
ORDER BY 1;
"""


Q_PURCHASE_TRACE = r"""
;WITH docs AS (
    SELECT TOP (10)
        bm.ID,
        bm.IDTransaksioni,
        bm.TXN,
        CONVERT(nvarchar(10), bm.Data, 120) AS Data,
        bm.Furnitori,
        CAST(SUM(ISNULL(bd.Vlera_furn,0)) AS numeric(18,2)) AS VleraPaTVSH,
        CAST(SUM(ISNULL(bd.TVSH_V,0)) AS numeric(18,2)) AS TVSH,
        CAST(SUM(ISNULL(bd.VleraMeTVSH,0)) AS numeric(18,2)) AS VleraMeTVSH
    FROM dbo.tblDocBlerjetMaster bm
    JOIN dbo.tblDocBlerjetDetalet bd ON bd.ID = bm.ID
    WHERE EXISTS (SELECT 1 FROM dbo.tblGeneralLedger gl WHERE gl.Transaksioni = bm.IDTransaksioni)
    GROUP BY bm.ID, bm.IDTransaksioni, bm.TXN, bm.Data, bm.Furnitori
    ORDER BY bm.Data DESC, bm.ID DESC
),
gl AS (
    SELECT
        d.IDTransaksioni,
        gl.TXN AS GL_TXN,
        gl.Tipi,
        CONVERT(nvarchar(10), gl.Data, 120) AS GLData,
        gl.Kontoja,
        pk.Description AS KontoEmertimi,
        gl.Subjekti,
        gl.TransaksioniNderlidhes,
        CAST(ISNULL(gl.Debi,0) AS numeric(18,2)) AS Debi,
        CAST(ISNULL(gl.Kredi,0) AS numeric(18,2)) AS Kredi,
        gl.DRCR,
        gl.CostingEntries
    FROM docs d
    JOIN dbo.tblGeneralLedger gl ON gl.Transaksioni = d.IDTransaksioni
    LEFT JOIN dbo.tblPlaniKontabel pk ON pk.Account = gl.Kontoja
)
SELECT N'Doc|DocTXN|DocDate|SupplierID|DocNoVAT|DocVAT|DocTotal|GL_TXN|Tipi|GLDate|Account|AccountName|Subject|LinkedTxn|Debi|Kredi|DRCR|CostingEntries'
UNION ALL
SELECT CAST(CONCAT(
    d.IDTransaksioni, N'|', d.TXN, N'|', d.Data, N'|', ISNULL(CAST(d.Furnitori AS nvarchar(30)), N''), N'|',
    d.VleraPaTVSH, N'|', d.TVSH, N'|', d.VleraMeTVSH, N'|',
    ISNULL(g.GL_TXN,N''), N'|', ISNULL(CAST(g.Tipi AS nvarchar(30)), N''), N'|', ISNULL(g.GLData,N''), N'|',
    ISNULL(g.Kontoja,N''), N'|', ISNULL(REPLACE(g.KontoEmertimi,N'|',N'/'),N''), N'|',
    ISNULL(CAST(g.Subjekti AS nvarchar(30)), N''), N'|', ISNULL(g.TransaksioniNderlidhes,N''), N'|',
    g.Debi, N'|', g.Kredi, N'|', ISNULL(g.DRCR,N''), N'|', ISNULL(CAST(g.CostingEntries AS nvarchar(10)),N'')
) AS nvarchar(max))
FROM docs d
LEFT JOIN gl g ON g.IDTransaksioni = d.IDTransaksioni
ORDER BY 1;
"""


Q_PAYMENT_TRACE = r"""
;WITH docs AS (
    SELECT TOP (10)
        pm.ID,
        pm.IDTransaksioni,
        pm.TXN,
        CONVERT(nvarchar(10), pm.Data, 120) AS Data,
        pm.Subjekti,
        pm.Kontoja AS PaymentAccount,
        CAST(SUM(ISNULL(pd.Pagesa,0)) AS numeric(18,2)) AS Pagesa,
        CAST(SUM(ISNULL(pd.Rabati,0)) AS numeric(18,2)) AS Rabati,
        MAX(pd.IDTransaksion) AS LinkedDoc
    FROM dbo.tblDocPagesatMaster pm
    JOIN dbo.tblDocPagesatDetalet pd ON pd.ID = pm.ID
    WHERE EXISTS (SELECT 1 FROM dbo.tblGeneralLedger gl WHERE gl.Transaksioni = pm.IDTransaksioni)
    GROUP BY pm.ID, pm.IDTransaksioni, pm.TXN, pm.Data, pm.Subjekti, pm.Kontoja
    ORDER BY pm.Data DESC, pm.ID DESC
),
gl AS (
    SELECT
        d.IDTransaksioni,
        gl.TXN AS GL_TXN,
        gl.Tipi,
        CONVERT(nvarchar(10), gl.Data, 120) AS GLData,
        gl.Kontoja,
        pk.Description AS KontoEmertimi,
        gl.Subjekti,
        gl.TransaksioniNderlidhes,
        CAST(ISNULL(gl.Debi,0) AS numeric(18,2)) AS Debi,
        CAST(ISNULL(gl.Kredi,0) AS numeric(18,2)) AS Kredi,
        gl.DRCR,
        gl.CostingEntries
    FROM docs d
    JOIN dbo.tblGeneralLedger gl ON gl.Transaksioni = d.IDTransaksioni
    LEFT JOIN dbo.tblPlaniKontabel pk ON pk.Account = gl.Kontoja
)
SELECT N'Doc|DocTXN|DocDate|SubjectID|PaymentAccount|Payment|Discount|DetailLinkedDoc|GL_TXN|Tipi|GLDate|Account|AccountName|Subject|LinkedTxn|Debi|Kredi|DRCR'
UNION ALL
SELECT CAST(CONCAT(
    d.IDTransaksioni, N'|', d.TXN, N'|', d.Data, N'|', ISNULL(CAST(d.Subjekti AS nvarchar(30)), N''), N'|',
    ISNULL(d.PaymentAccount,N''), N'|', d.Pagesa, N'|', d.Rabati, N'|', ISNULL(d.LinkedDoc,N''), N'|',
    ISNULL(g.GL_TXN,N''), N'|', ISNULL(CAST(g.Tipi AS nvarchar(30)), N''), N'|', ISNULL(g.GLData,N''), N'|',
    ISNULL(g.Kontoja,N''), N'|', ISNULL(REPLACE(g.KontoEmertimi,N'|',N'/'),N''), N'|',
    ISNULL(CAST(g.Subjekti AS nvarchar(30)), N''), N'|', ISNULL(g.TransaksioniNderlidhes,N''), N'|',
    g.Debi, N'|', g.Kredi, N'|', ISNULL(g.DRCR,N'')
) AS nvarchar(max))
FROM docs d
LEFT JOIN gl g ON g.IDTransaksioni = d.IDTransaksioni
ORDER BY 1;
"""


Q_DOC_TO_GL_COVERAGE = r"""
;WITH gl_docs AS (
    SELECT DISTINCT Transaksioni FROM dbo.tblGeneralLedger
),
sales AS (
    SELECT N'Sales tblDocShitjetMaster' AS DocType, COUNT(*) AS Docs,
           SUM(CASE WHEN gl.Transaksioni IS NULL THEN 0 ELSE 1 END) AS DocsWithGL
    FROM dbo.tblDocShitjetMaster bm
    LEFT JOIN gl_docs gl ON gl.Transaksioni = bm.IDTransaksioni
),
purchases AS (
    SELECT N'Purchases tblDocBlerjetMaster' AS DocType, COUNT(*) AS Docs,
           SUM(CASE WHEN gl.Transaksioni IS NULL THEN 0 ELSE 1 END) AS DocsWithGL
    FROM dbo.tblDocBlerjetMaster bm
    LEFT JOIN gl_docs gl ON gl.Transaksioni = bm.IDTransaksioni
),
payments AS (
    SELECT N'Payments tblDocPagesatMaster' AS DocType, COUNT(*) AS Docs,
           SUM(CASE WHEN gl.Transaksioni IS NULL THEN 0 ELSE 1 END) AS DocsWithGL
    FROM dbo.tblDocPagesatMaster pm
    LEFT JOIN gl_docs gl ON gl.Transaksioni = pm.IDTransaksioni
),
pos_sales AS (
    SELECT N'POS tblDocPOSShitjetMaster' AS DocType, COUNT(*) AS Docs,
           SUM(CASE WHEN gl.Transaksioni IS NULL THEN 0 ELSE 1 END) AS DocsWithGL
    FROM dbo.tblDocPOSShitjetMaster pm
    LEFT JOIN gl_docs gl ON gl.Transaksioni = pm.IDTransaksioni
),
all_rows AS (
    SELECT * FROM sales
    UNION ALL SELECT * FROM purchases
    UNION ALL SELECT * FROM payments
    UNION ALL SELECT * FROM pos_sales
),
lines AS (
    SELECT 0 AS sort_key, CAST(N'DocType|Docs|DocsWithGL|PctWithGL' AS nvarchar(max)) AS line
    UNION ALL
    SELECT 1, CAST(CONCAT(DocType, N'|', Docs, N'|', DocsWithGL, N'|', CAST(CASE WHEN Docs = 0 THEN 0 ELSE DocsWithGL * 100.0 / Docs END AS numeric(18,2))) AS nvarchar(max))
    FROM all_rows
)
SELECT line FROM lines ORDER BY sort_key, line;
"""


def write_query_outputs(password: str) -> None:
    queries = [
        ("gl_txn_summary.psv", Q_GL_TXN_SUMMARY),
        ("doc_to_gl_coverage.psv", Q_DOC_TO_GL_COVERAGE),
        ("trace_sales_to_gl.psv", Q_SALES_TRACE),
        ("trace_purchases_to_gl.psv", Q_PURCHASE_TRACE),
        ("trace_payments_to_gl.psv", Q_PAYMENT_TRACE),
    ]
    for filename, sql in queries:
        try:
            out = run_sql_psv(password, sql)
            if filename.startswith("trace_"):
                out = move_header_first(out, "Doc|")
            write_text(OUT_ROOT / "data" / filename, out)
        except Exception as exc:
            raise RuntimeError(f"{filename}: {exc}") from exc


def move_header_first(text: str, header_prefix: str) -> str:
    lines = [line for line in text.splitlines() if line.strip()]
    header_idx = next((idx for idx, line in enumerate(lines) if line.startswith(header_prefix)), None)
    if header_idx is None or header_idx == 0:
        return "\n".join(lines) + "\n"
    header = lines.pop(header_idx)
    return "\n".join([header] + lines) + "\n"


def csv_first_rows(path: Path, limit: int = 12) -> list[list[str]]:
    rows: list[list[str]] = []
    with path.open("r", encoding="utf-8") as f:
        reader = csv.reader(f, delimiter="|")
        for idx, row in enumerate(reader):
            rows.append(row)
            if idx >= limit:
                break
    return rows


def markdown_table(rows: list[list[str]]) -> str:
    if not rows:
        return ""
    header = rows[0]
    body = rows[1:]
    out = ["| " + " | ".join(header) + " |", "| " + " | ".join("---" for _ in header) + " |"]
    for row in body:
        padded = row + [""] * (len(header) - len(row))
        out.append("| " + " | ".join(x.replace("|", "/") for x in padded[: len(header)]) + " |")
    return "\n".join(out)


def make_markdown(definitions: dict[str, str]) -> None:
    now = dt.datetime.now().astimezone().isoformat(timespec="seconds")
    ref_counter: dict[str, Counter[str]] = {proc: extract_references(defn) for proc, defn in definitions.items()}

    def top_refs(proc: str, n: int = 20) -> str:
        rows = [["Referenced object", "Count"]]
        rows += [[k, str(v)] for k, v in ref_counter[proc].most_common(n)]
        return markdown_table(rows)

    lines = [
        "# PRO DATA Posting Logic Deep Dive",
        "",
        f"- Generated: `{now}`",
        f"- Database: `{DB}`",
        "- Scope: `prcRPTGRDLibriShitjetADM18`, `prcRPTGRDLibriBlerjetADM18`, and trace from sales/purchases/payments to `tblGeneralLedger`.",
        "- Status: evidence analysis for iData+ accounting parity; not a migration plan.",
        "",
        "## Executive verdict",
        "",
        "PRO DATA nuk e ndërton librin e shitjes/blerjes direkt nga GL. Raportet ATK ndërtohen kryesisht nga dokumentet burimore (`tblDocShitjet*`, `tblDocBlerjet*`, POS tables, avanse/pagesa/ekstrakte) dhe përdorin `tblTarifat`, `tblSubjektet`, `tblArtikujt`, `tblTipetMalli`, `tblNjesiteOrganizative` për kategorizim. `tblGeneralLedger` është libri financiar/posting ledger që mban debit/kredit dhe lidhje me dokumentin përmes `Transaksioni` / `TransaksioniNderlidhes`.",
        "",
        "Për iData+: duhet t'i ndajmë qartë dy gjëra: (1) **posting engine** që krijon GL nga dokumentet, dhe (2) **tax-book/reporting engine** që llogarit Librin e Shitjes/Blerjes nga dokumentet e postuara + tax rules. Kjo është më e pastër se të kopjojmë stored procedures 1:1.",
        "",
        "## Procedure deep dive — Libri i Shitjes",
        "",
        "- Procedure: `prcRPTGRDLibriShitjetADM18`.",
        "- Inputs: `@dt1`, `@dt2`, `@username`.",
        "- Main sources: POS sales, normal sales invoices, fiscal handover/delivery-note connections, advances and advance application.",
        "- Key tax buckets: total sales, domestic taxable 18%, domestic taxable 8%, VAT output 18%, VAT output 8%, exports, exempt supply, reverse-charge buckets, notes/debit-credit buckets.",
        "- Important classification fields: `fd.TVSH_P`, `fd.TVSH_V`, `fm.TXN`, `s1.NumriUnikIdentifikues`, `s1.noTVSH`, `s1.VATExcluded`, `s1.SubjektJashtem`, `tm.Tipi`.",
        "",
        "Top referenced objects:",
        "",
        top_refs("prcRPTGRDLibriShitjetADM18"),
        "",
        "## Procedure deep dive — Libri i Blerjes",
        "",
        "- Procedure: `prcRPTGRDLibriBlerjetADM18`.",
        "- Inputs: `@dt1`, `@dt2`, `@username`.",
        "- Main sources: purchase invoices, goods-receipt confirmations, farmer purchases, advances, notes, and reverse-charge-style buckets.",
        "- Key tax buckets: purchases/imports no VAT, non-deductible VAT, imports 18/8, domestic purchases 18/8, investment purchases 18/8, farmer purchases 8%, debit/credit note buckets, reverse-charge buckets.",
        "- Important classification fields: `bd.TVSH_P`, `bd.TVSH_V`, `bd.KoeficientiTVSH`, `bd.TVSHKapitalizuese`, `bd.TVSHKalkuluese`, `bm.DUD`, `bm.TXN`, `s.fermer`, `t.TVSHJoeZbritshme`, `tm.Tipi`.",
        "",
        "Top referenced objects:",
        "",
        top_refs("prcRPTGRDLibriBlerjetADM18"),
        "",
        "## Posting trace model",
        "",
        "### Source document keys",
        "",
        "- Sales: `tblDocShitjetMaster.IDTransaksioni` → `tblGeneralLedger.Transaksioni`.",
        "- Purchases: `tblDocBlerjetMaster.IDTransaksioni` → `tblGeneralLedger.Transaksioni`.",
        "- Payments: `tblDocPagesatMaster.IDTransaksioni` → `tblGeneralLedger.Transaksioni`; detail lines also reference paid document through `tblDocPagesatDetalet.IDTransaksion`.",
        "- Bank statement payments: `tblDocEkstraktiLidhjet.IDTransaksion` links a bank-statement position to original documents.",
        "- GL has `TransaksioniNderlidhes` for related/linked document context.",
        "",
        "### GL columns that matter for iData+",
        "",
        "- `Transaksioni`: source document ID/code.",
        "- `TXN`: document type.",
        "- `Tipi`: internal posting type/classification.",
        "- `Kontoja`: chart-of-account code.",
        "- `Subjekti`: customer/supplier/partner.",
        "- `Debi`, `Kredi`, `DRCR`: double-entry values.",
        "- `TransaksioniNderlidhes`: linked transaction/document.",
        "- `CostingEntries`: likely cost/COGS entries flag.",
        "",
        "## Evidence — document to GL coverage",
        "",
        markdown_table(csv_first_rows(OUT_ROOT / "data" / "doc_to_gl_coverage.psv", 20)),
        "",
        "## Evidence — GL TXN summary",
        "",
        markdown_table(csv_first_rows(OUT_ROOT / "data" / "gl_txn_summary.psv", 25)),
        "",
        "## Evidence files",
        "",
        "- `procedures/prcRPTGRDLibriShitjetADM18.sql`",
        "- `procedures/prcRPTGRDLibriBlerjetADM18.sql`",
        "- `procedure_reference_map.csv`",
        "- `data/gl_txn_summary.psv`",
        "- `data/doc_to_gl_coverage.psv`",
        "- `data/trace_sales_to_gl.psv`",
        "- `data/trace_purchases_to_gl.psv`",
        "- `data/trace_payments_to_gl.psv`",
        "",
        "## iData+ design implications",
        "",
        "1. **Posting rules engine është i detyrueshëm.** Dokumentet duhet të krijojnë GL automatikisht, me debit=kredit, dhe çdo document type duhet të ketë mapping të qartë.",
        "2. **Tax-book engine nuk duhet të jetë Excel/report-only.** Librat ATK duhet të ndërtohen nga dokumentet dhe tax rules, jo nga manipulim manual.",
        "3. **Për blerje duhet mixed-rate dhe partial deductible logic.** `KoeficientiTVSH`, `TVSHKapitalizuese`, `TVSHKalkuluese`, `TVSHJoeZbritshme` tregojnë që sistemi i vjetër mbështet më shumë se një rast të thjeshtë.",
        "4. **Farmer purchases janë kategori reale në procedure.** Për KABI, kjo është gate kritike: `s.fermer=1`, bucket i fermerëve 8%, dhe trajtimi me kontabilist/ATK duhet të mbyllet para finalizimit.",
        "5. **Sales tax buckets varen nga partneri dhe artikulli.** `VATExcluded`, `SubjektJashtem`, `TVSH_P`, `tm.Tipi`, `TXN` duhet të jenë config/rule-driven.",
        "6. **Payments duhet të kenë allocation/linking.** `tblDocPagesatDetalet.IDTransaksion` dhe `tblDocEkstraktiLidhjet.IDTransaksion` tregojnë lidhje pagesë ↔ faturë; iData+ duhet ta ketë këtë si core.",
        "",
        "## Next recommended deep-dive",
        "",
        "- Izolo stored procedures që krijojnë GL entries për `SHV01`, `BLV01/BLS01`, `PGD01/PGH01`, `PSPP1/PSPG1`.",
        "- Krahaso për një muaj: totals nga document tables → GL totals → Libri i Shitjes/Blerjes → TVSH summary.",
        "- Shto në Accounting Compatibility Matrix kolonat e sakta të reportit të shitjes/blerjes dhe mapping-un me ATK boxes.",
        "",
    ]
    write_text(OUT_ROOT / "PRODATA_Posting_Logic_Deep_Dive.md", "\n".join(lines))


def make_zip() -> None:
    zip_path = OUT_ROOT.parent / "prodata_posting_deep_dive_2026-06-28.zip"
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for path in sorted(OUT_ROOT.rglob("*")):
            if path.is_file():
                zf.write(path, path.relative_to(OUT_ROOT.parent))


def main() -> int:
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    (OUT_ROOT / "procedures").mkdir(parents=True, exist_ok=True)
    definitions: dict[str, str] = {}
    for proc_name in PROC_NAMES:
        definition = extract_proc_definition(proc_name)
        definitions[proc_name] = definition
        write_text(OUT_ROOT / "procedures" / f"{proc_name}.sql", definition)
    write_reference_map(definitions)
    password = get_sa_password()
    write_query_outputs(password)
    make_markdown(definitions)
    make_zip()
    print(f"Done. Output: {OUT_ROOT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
