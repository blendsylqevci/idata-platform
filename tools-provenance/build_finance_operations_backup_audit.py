#!/usr/bin/env python3
"""
Build a dedicated Finance Operations backup-coverage audit for iData+.

This is intentionally separate from accounting/GL/VAT evidence.  The goal is to
answer: "Do we have enough from the PRO DATA backup for PLANET/KABI finance
operations (cash office, POS/card/bank reconciliation, waiter settlement,
expenses, supplier payments), and what remains process-confirmation vs missing
extract?"
"""

from __future__ import annotations

import csv
import re
import zipfile
from dataclasses import dataclass
from datetime import date
from pathlib import Path
from typing import Iterable


ROOT = Path(__file__).resolve().parents[1]
TODAY = date.today().isoformat()
OUT_DIR = ROOT / "outputs" / f"finance_operations_backup_audit_{TODAY}"

TABLE_COVERAGE = ROOT / "outputs/prodata_backup_completeness_audit_2026-06-28/table_coverage_matrix.csv"
OBJECT_INVENTORY = ROOT / "outputs/prodata_module_map_2026-06-28/prodata_module_map_object_inventory.csv"
RESTAURANT_PROC_INDEX = ROOT / "outputs/prodata_supplementary_extract_wave2_restaurant_2026-06-28/restaurant_key_procedure_index.csv"


@dataclass(frozen=True)
class TableCategory:
    key: str
    label: str
    priority: str
    why: str


TABLE_CATEGORIES: list[tuple[re.Pattern[str], TableCategory]] = [
    (re.compile(r"^tbldocpagesat(master|detalet)$", re.I), TableCategory("payments_receipts", "Payments / receipts", "A", "core payment/receipt documents")),
    (re.compile(r"^tbldocpagesat(avanset|tjeramaster|tjeradetalet)$", re.I), TableCategory("operational_cash_expense", "Operational cash / advances / other payments", "B", "cash advances or other operational payment variants")),
    (re.compile(r"^tbldocekstrakti(master|detalet|lidhjet)$", re.I), TableCategory("bank_reconciliation", "Bank statements / reconciliation", "A", "bank statement lines and links to source documents")),
    (re.compile(r"^tblbankat$", re.I), TableCategory("bank_reconciliation", "Bank setup", "A", "bank/account setup")),
    (re.compile(r"^tblmenyrapagesa$", re.I), TableCategory("payment_methods_terminals", "Payment methods / terminals", "A", "cash/card/bank/POS-terminal method setup")),
    (re.compile(r"^tblusermenyratpagesa$", re.I), TableCategory("payment_methods_terminals", "User payment-method permissions", "B", "which users can use which payment methods")),
    (re.compile(r"^tbldocpospagesat$", re.I), TableCategory("pos_financial", "POS payments", "A", "POS payment documents")),
    (re.compile(r"^tbldocposshitjet(master|detalet)$", re.I), TableCategory("pos_financial", "POS sales financial source", "A", "POS sales/tickets feed cash/card/VAT/finance")),
    (re.compile(r"^tbldocposbarazimi(master|detalet)$", re.I), TableCategory("pos_settlement_barazim", "POS settlement / barazim", "A", "settlement schema for POS closing/barazim")),
    (re.compile(r"^tbl(posarkivaditariarka|poskusuri|posoffline|posofflineresend|tmppos|tmpdocbarazimiarka)", re.I), TableCategory("pos_settlement_barazim", "POS drawer/offline/settlement support", "B", "support schema for cash drawer, change/kusur, offline, temporary settlement")),
    (re.compile(r"^tbldocblerjet(shpenzimet|master|detalet)", re.I), TableCategory("supplier_invoice_expenses", "Supplier invoices / purchase expenses", "A", "supplier invoice and purchase-expense evidence")),
    (re.compile(r"^tbl(shpenz|pages|arka|cash|kusur)", re.I), TableCategory("operational_cash_expense", "Operational cash / expenses", "B", "cash/expense keyword evidence")),
    (re.compile(r"^tbl(rest|tmprest).*", re.I), TableCategory("restaurant_finance_session", "Restaurant operational session", "B", "restaurant order/table/session may affect settlement")),
    (re.compile(r".*del$", re.I), TableCategory("audit_correction", "Deleted/correction audit", "B", "deleted/corrected document audit")),
]


PROC_KEYWORDS = [
    ("pos_settlement_barazim", r"barazim|gjendjaarka|arka|cash|kusur|nderrim|shift", "settlement/cash drawer procedures"),
    ("pos_financial", r"pospages|posshitj|shitjettotalepos|libriarkaperporosi|offlinepostopos|faturepos", "POS financial/payment reports"),
    ("payments_receipts", r"pagesat|pagesa|arketim|ndërlidh|nderlidh", "payments/receipts/allocation procedures"),
    ("bank_reconciliation", r"ekstrakt|bank", "bank statement/reconciliation procedures"),
    ("supplier_invoice_expenses", r"blerjet|shpenz", "supplier invoice/purchase expense procedures"),
    ("payment_methods_terminals", r"menyrapagesa|terminal", "payment method/terminal procedures"),
]


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def write_csv(path: Path, rows: Iterable[dict[str, object]], fieldnames: list[str]) -> None:
    with path.open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fieldnames)
        w.writeheader()
        for row in rows:
            w.writerow({k: row.get(k, "") for k in fieldnames})


def int_or_zero(value: str | int | None) -> int:
    if value is None:
        return 0
    try:
        return int(str(value).strip() or "0")
    except ValueError:
        return 0


def classify_table(name: str) -> TableCategory | None:
    for rx, category in TABLE_CATEGORIES:
        if rx.search(name):
            return category
    return None


def classify_procedure(name: str, category_hint: str = "") -> tuple[str, str, str] | None:
    hay = f"{name} {category_hint}".lower()
    for key, rx, why in PROC_KEYWORDS:
        if re.search(rx, hay):
            label = {
                "pos_settlement_barazim": "POS settlement / barazim",
                "pos_financial": "POS financial reports/payments",
                "payments_receipts": "Payments / receipts",
                "bank_reconciliation": "Bank reconciliation",
                "supplier_invoice_expenses": "Supplier invoices / expenses",
                "payment_methods_terminals": "Payment methods / terminals",
            }[key]
            return key, label, why
    return None


def evidence_status(row: dict[str, str]) -> str:
    row_count = int_or_zero(row.get("row_count"))
    data_status = (row.get("data_status") or "").lower()
    action = (row.get("action") or "").lower()
    if data_status == "exported" or "covered_by_existing_export" in action:
        return "covered_raw_data"
    if row_count == 0:
        return "covered_schema_only_zero_rows"
    if data_status in {"ddl_only", "not_exported"} or "defer" in action or "not_export" in action:
        return "supplementary_candidate"
    return "review"


def gap_severity(row: dict[str, str], category: TableCategory, status: str) -> str:
    name = row["table"].lower()
    row_count = int_or_zero(row.get("row_count"))
    if status == "covered_raw_data":
        return "covered"
    if "posbarazimi" in name and row_count == 0:
        return "process_gap_needs_planet_confirmation"
    if row_count == 0:
        return "schema_only_no_data_gap"
    if category.key in {"payment_methods_terminals", "operational_cash_expense", "supplier_invoice_expenses"}:
        return "optional_micro_extract_candidate"
    return "review_candidate"


def md_table(headers: list[str], rows: list[list[object]]) -> str:
    if not rows:
        return "_No rows._"
    out = ["| " + " | ".join(headers) + " |", "| " + " | ".join(["---"] * len(headers)) + " |"]
    for row in rows:
        out.append("| " + " | ".join(str(x).replace("\n", " ").replace("|", "\\|") for x in row) + " |")
    return "\n".join(out)


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    table_rows = read_csv(TABLE_COVERAGE)
    object_rows = read_csv(OBJECT_INVENTORY)
    restaurant_proc_rows = read_csv(RESTAURANT_PROC_INDEX) if RESTAURANT_PROC_INDEX.exists() else []

    finance_tables: list[dict[str, object]] = []
    gap_rows: list[dict[str, object]] = []
    for row in table_rows:
        category = classify_table(row["table"])
        if not category:
            continue
        status = evidence_status(row)
        severity = gap_severity(row, category, status)
        item = {
            "database": row["database"],
            "schema": row["schema"],
            "table": row["table"],
            "row_count": int_or_zero(row.get("row_count")),
            "columns": int_or_zero(row.get("columns")),
            "finance_area": category.label,
            "finance_key": category.key,
            "audit_priority": category.priority,
            "evidence_status": status,
            "gap_severity": severity,
            "source_action": row.get("action", ""),
            "source_reason": row.get("reason", ""),
            "existing_export": row.get("existing_export", ""),
            "why_it_matters": category.why,
        }
        finance_tables.append(item)
        if severity != "covered":
            gap_rows.append({
                "database": item["database"],
                "table": item["table"],
                "finance_area": item["finance_area"],
                "row_count": item["row_count"],
                "evidence_status": item["evidence_status"],
                "gap_severity": item["gap_severity"],
                "recommended_action": recommended_action(item),
            })

    proc_candidates: list[dict[str, object]] = []
    seen_proc = set()
    for row in object_rows:
        object_type = (row.get("object_type") or "").upper()
        if object_type not in {"PROCEDURE", "FUNCTION", "VIEW"}:
            continue
        # Use object name only here.  The module-map submodule can be broad
        # ("POS setup/workflow") and would over-classify unrelated POS methods
        # as finance operations.
        classified = classify_procedure(row.get("object_name", ""))
        if not classified:
            continue
        key, label, why = classified
        proc_key = (row.get("database", ""), object_type, row.get("object_name", ""))
        if proc_key in seen_proc:
            continue
        seen_proc.add(proc_key)
        proc_candidates.append({
            "database": row.get("database", ""),
            "object_type": object_type,
            "schema": row.get("schema", ""),
            "object_name": row.get("object_name", ""),
            "finance_area": label,
            "finance_key": key,
            "reason": why,
            "module_map_reason": row.get("reason", ""),
        })
    for row in restaurant_proc_rows:
        classified = classify_procedure(row.get("procedure", ""), row.get("category", ""))
        if not classified:
            continue
        key, label, why = classified
        proc_key = (row.get("database", ""), "PROCEDURE_REVIEW", row.get("procedure", ""))
        if proc_key in seen_proc:
            continue
        seen_proc.add(proc_key)
        proc_candidates.append({
            "database": row.get("database", ""),
            "object_type": "PROCEDURE_REVIEW",
            "schema": "dbo",
            "object_name": row.get("procedure", ""),
            "finance_area": label,
            "finance_key": key,
            "reason": why,
            "module_map_reason": row.get("reason", ""),
        })

    finance_tables.sort(key=lambda r: (str(r["audit_priority"]), str(r["finance_key"]), str(r["database"]), str(r["table"])))
    gap_rows.sort(key=lambda r: (str(r["gap_severity"]), str(r["database"]), str(r["table"])))
    proc_candidates.sort(key=lambda r: (str(r["finance_key"]), str(r["database"]), str(r["object_name"])))

    write_csv(
        OUT_DIR / "finance_operations_table_coverage.csv",
        finance_tables,
        ["database", "schema", "table", "row_count", "columns", "finance_area", "finance_key", "audit_priority", "evidence_status", "gap_severity", "source_action", "source_reason", "existing_export", "why_it_matters"],
    )
    write_csv(
        OUT_DIR / "finance_operations_gap_register.csv",
        gap_rows,
        ["database", "table", "finance_area", "row_count", "evidence_status", "gap_severity", "recommended_action"],
    )
    write_csv(
        OUT_DIR / "finance_operations_procedure_candidates.csv",
        proc_candidates,
        ["database", "object_type", "schema", "object_name", "finance_area", "finance_key", "reason", "module_map_reason"],
    )
    write_csv(
        OUT_DIR / "finance_operations_merge_manifest.csv",
        [
            {
                "file": "FINANCE_OPERATIONS_Backup_Coverage_Audit.md",
                "target_layer": "evidence + canonical-input",
                "suggested_target": "evidence/prodata/finance-operations/FINANCE_OPERATIONS_Backup_Coverage_Audit.md",
                "purpose": "Backup coverage proof for finance operations and remaining process gaps.",
            },
            {
                "file": "FINANCE_OPERATIONS_Scope_Separation_v1.md",
                "target_layer": "canonical",
                "suggested_target": "canonical/finance-operations/FINANCE_OPERATIONS_Scope_Separation.md",
                "purpose": "Official separation between Finance Operations and Accounting.",
            },
            {
                "file": "finance_operations_table_coverage.csv",
                "target_layer": "evidence",
                "suggested_target": "evidence/prodata/finance-operations/finance_operations_table_coverage.csv",
                "purpose": "Table-level evidence coverage and status.",
            },
            {
                "file": "finance_operations_gap_register.csv",
                "target_layer": "evidence",
                "suggested_target": "evidence/prodata/finance-operations/finance_operations_gap_register.csv",
                "purpose": "Review/micro-extract/process-confirmation register.",
            },
            {
                "file": "finance_operations_procedure_candidates.csv",
                "target_layer": "evidence",
                "suggested_target": "evidence/prodata/finance-operations/finance_operations_procedure_candidates.csv",
                "purpose": "Procedure/function/view candidates for later finance-flow deep dives.",
            },
        ],
        ["file", "target_layer", "suggested_target", "purpose"],
    )

    build_audit_doc(finance_tables, gap_rows, proc_candidates)
    build_scope_doc()

    zip_path = OUT_DIR / "finance_operations_backup_audit_pack.zip"
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as z:
        for p in sorted(OUT_DIR.glob("*")):
            if p == zip_path:
                continue
            z.write(p, p.name)

    print(f"Wrote {OUT_DIR}")
    print(f"Tables: {len(finance_tables)}")
    print(f"Gaps/review items: {len(gap_rows)}")
    print(f"Procedure candidates: {len(proc_candidates)}")


def recommended_action(item: dict[str, object]) -> str:
    table = str(item["table"]).lower()
    status = str(item["evidence_status"])
    if table.endswith("del"):
        return "Keep as deleted/correction audit design signal; export only if a later parity test needs deleted-document history for this exact table."
    if "posbarazimi" in table:
        return "Do not infer real PLANET settlement from backup; design iData+ Finance Operations settlement and validate live with PLANET finance/manager."
    if table == "tblusermenyratpagesa":
        return "Optional sanitized micro-extract if we need exact PRO DATA user-payment-method permissions/defaults; otherwise model fresh RBAC."
    if table == "tbldocblerjetshpenzimet tjera".replace(" ", "") or "blerjetshpenzimet" in table:
        return "Optional micro-extract: only 4 rows, useful for purchase additional-cost/expense parity."
    if table == "tbldocpagesatavanset":
        return "Optional micro-extract: only 1 row, useful for advance/prepayment semantics."
    if status == "covered_schema_only_zero_rows":
        return "Keep DDL as design signal; no real PLANET data to migrate/compare."
    return "Review during canonical merge; extract only if the finance workflow needs this exact historical evidence."


def build_audit_doc(finance_tables: list[dict[str, object]], gap_rows: list[dict[str, object]], proc_candidates: list[dict[str, object]]) -> None:
    by_status: dict[str, int] = {}
    by_area: dict[str, int] = {}
    for row in finance_tables:
        by_status[str(row["evidence_status"])] = by_status.get(str(row["evidence_status"]), 0) + 1
        by_area[str(row["finance_area"])] = by_area.get(str(row["finance_area"]), 0) + 1

    covered = [r for r in finance_tables if r["evidence_status"] == "covered_raw_data"]
    schema_only = [r for r in finance_tables if r["evidence_status"] == "covered_schema_only_zero_rows"]
    micro = [r for r in gap_rows if r["gap_severity"] == "optional_micro_extract_candidate"]
    process_gaps = [r for r in gap_rows if r["gap_severity"] == "process_gap_needs_planet_confirmation"]

    key_table_rows = []
    for name in [
        "tblDocPagesatMaster",
        "tblDocPagesatDetalet",
        "tblDocEkstraktiMaster",
        "tblDocEkstraktiDetalet",
        "tblDocEkstraktiLidhjet",
        "tblMenyraPagesa",
        "tblBankat",
        "tblDocPOSPagesat",
        "tblDocPOSBarazimiMaster",
        "tblDocPOSBarazimiDetalet",
        "tblDocBlerjetShpenzimetTjera",
        "tblDocPagesatAvanset",
        "tblUserMenyratPagesa",
    ]:
        matches = [r for r in finance_tables if str(r["table"]).lower() == name.lower()]
        for r in matches:
            key_table_rows.append([
                r["database"],
                r["table"],
                r["row_count"],
                r["finance_area"],
                r["evidence_status"],
                r["gap_severity"],
            ])

    top_proc_rows = [[r["database"], r["object_type"], r["object_name"], r["finance_area"], r["reason"]] for r in proc_candidates[:60]]

    doc = f"""# Finance Operations Backup Coverage Audit — PRO DATA → iData+

**Data:** {TODAY}
**Status:** Evidence audit / canonical input
**Scope:** financat operative (arka, barazimi, cash/card/bank, POS payments, expenses, supplier payments), jo GL/TVSH si kontabilitet zyrtar.
**Burime:** `table_coverage_matrix.csv`, `prodata_module_map_object_inventory.csv`, `restaurant_key_procedure_index.csv`, DDL/procedure review dhe raw exports ekzistuese.

## Vendimi i qartë

Backup-i i PRO DATA-s është i mjaftueshëm për të projektuar **Finance Operations** në iData+ dhe për të mos humbur funksionet kryesore të PLANET-it: pagesa/arkëtime, cash/card/bank, ekstrakte bankare, POS payments, shitje POS, fatura hyrëse dhe lidhja me dokumente.

Ky audit është shtresë shtesë mbi **Evidence Closure Matrix** ku extraction/review gap për Priority A dhe B është tashmë `A=0`, `B=0`. Pra këtu nuk po kërkojmë “a mungon backup-i kryesor?”, por po ndajmë saktë çfarë është evidence reale, çfarë është schema-only, dhe çfarë është proces biznesi që duhet konfirmuar me PLANET.

Por backup-i **nuk jep evidence reale 1:1 për barazimin operativ të PLANET-it**, sepse `tblDocPOSBarazimiMaster` dhe `tblDocPOSBarazimiDetalet` ekzistojnë në DDL/procedura por janë zero-row/schema-only në backup. Kjo nuk është gabim ekstraktimi; është sinjal që barazimi që duam në iData+ duhet të projektohet si proces modern dhe të validohet live me vajzën e financave / Donatin / menaxherët.

Pra ndarja 10/10 është:

- **Finance Operations:** kontroll ditor i parasë, POS terminaleve, saldo/float, kamarierëve, shpenzimeve, dorëzimit cash, raportit ditor, pagesave furnitorëve.
- **Accounting:** GL, TVSH, libra ATK, postime zyrtare, mbyllje periudhe, pasqyra dhe raportim ligjor.

Finance Operations prodhon evidence të pastër; Accounting e konsumon, e poston dhe e mbyll zyrtarisht.

## Përmbledhje coverage

{md_table(["Status", "Numër objektesh"], [[k, v] for k, v in sorted(by_status.items())])}

{md_table(["Finance area", "Objekte"], [[k, v] for k, v in sorted(by_area.items())])}

## Tabelat kyçe të financave operative

{md_table(["DB", "Tabela", "Rows", "Area", "Evidence", "Gap/Severity"], key_table_rows)}

## Çfarë kemi të mbuluar mirë nga backup-i

1. **Pagesa / arkëtime:** `tblDocPagesatMaster` + `tblDocPagesatDetalet` kanë volum të madh dhe janë eksportuar si raw evidence.
2. **Bankë / reconciliation:** `tblDocEkstraktiMaster`, `tblDocEkstraktiDetalet`, `tblDocEkstraktiLidhjet` janë të mbuluara; kjo është baza për bank reconciliation dhe lidhje pagesë ↔ dokument.
3. **Mënyrat e pagesës:** `tblMenyraPagesa` dhe `tblBankat` japin konfigurimin bazë për cash/card/bank/POS terminal.
4. **POS financial flow:** `tblDocPOSShitjet*` dhe `tblDocPOSPagesat` janë evidence reale për shitje/pagesa POS.
5. **Blerje/furnitorë/shpenzime:** `tblDocBlerjet*` janë të mbuluara; `tblDocBlerjetShpenzimetTjera` është micro-candidate për ekstrakt opsional sepse ka pak rreshta.
6. **Audit/korrigjime:** `_DEL` dhe procedurat e review tregojnë që iData+ duhet të ketë correction/reversal/audit, jo delete të lirë.

## Çfarë NUK duhet ta supozojmë nga backup-i

- `tblDocPOSBarazimi*` nuk ka rreshta realë në backup. Nuk e përdorim si “kështu e bënte PLANET-i barazimin”. E përdorim vetëm si sinjal që PRO DATA kishte koncept të barazimit.
- Workflow i vajzës së financave nuk del 100% nga backup-i: kush e hap ditën, kush merr cash, si bëhet saldo/float, si ndahet cash nga POS bank/card, si regjistrohen shpenzimet, si merret Donati cash nga zyra, kush korrigjon diferencat.
- KABI finance operations nuk del nga PLANET backup-i: van-sales barazimi, fermerë/avans, gajbe, ambulant, cash-credit per shofer duhet të validohen me KABI.

## Gap register / review items

{md_table(["DB", "Tabela", "Area", "Rows", "Status", "Severity", "Recommended action"], [[r["database"], r["table"], r["finance_area"], r["row_count"], r["evidence_status"], r["gap_severity"], r["recommended_action"]] for r in gap_rows[:80]])}

## Optional micro-extract candidates

Këto nuk janë bllokues, por nëse duam “zero doubt” për financat operative, ia vlen t'i nxjerrim/sanitizojmë:

{md_table(["DB", "Tabela", "Rows", "Pse"], [[r["database"], r["table"], r["row_count"], r["recommended_action"]] for r in micro])}

## Procedure candidates për review finance-ops

{md_table(["DB", "Type", "Object", "Area", "Reason"], top_proc_rows)}

## Konkluzioni praktik

**Nuk shoh Priority A/B finance extract që mungon si blocker.** Ajo që mungon nuk është “file i harruar”, por **vendim/proces biznesi** për Finance Operations modern:

1. të projektohet ekrani i barazimit të kamarierit/arkës;
2. të projektohet cash office / zyra e parave;
3. të projektohet POS terminal reconciliation;
4. të projektohen shpenzimet operative dhe dorëzimet cash;
5. të lidhet Finance Operations me Accounting pa i përzier rolet.

Kjo duhet të hyjë në canonical docs si modul i ndarë: **Finance Operations**, dhe jo të fshihet brenda “Kontabiliteti”.
"""
    (OUT_DIR / "FINANCE_OPERATIONS_Backup_Coverage_Audit.md").write_text(doc, encoding="utf-8")


def build_scope_doc() -> None:
    doc = f"""# Finance Operations Scope & Separation — iData+

**Data:** {TODAY}
**Status:** Canonical draft për Fazën 0
**Qëllimi:** ndarja e saktë mes financave operative dhe kontabilitetit, që PLANET/KABI të mos përzihen në role, ekrane dhe përgjegjësi.

## 1. Ndarja kryesore

### Finance Operations

Përdoret nga vajza/personi i financave, menaxheri, Donati/owner dhe rolet operative. Qëllimi është kontrolli i parasë dhe operacionit ditor:

- sa cash është në zyrë;
- sa saldo/float iu dha secilit kamarier/arkatar;
- sa cash duhet të dorëzojë secili;
- sa pagesa janë me kartë/POS bank;
- cilat shpenzime u paguan nga arka;
- kush mori para nga zyra, kur dhe pse;
- barazim i kamarierit me tavolina/porosi/pagesa;
- mbyllja e ditës/ndërrimit;
- raport ditor për pronarin/menaxhmentin.

### Accounting

Përdoret nga kontabilisti dhe financa zyrtare. Qëllimi është evidenca ligjore:

- GL / Libri Kryesor;
- Libri i Shitjes dhe Libri i Blerjes;
- deklarata mujore TVSH;
- AR/AP, kartela klienti/furnitori;
- mbyllja e periudhës;
- raportet ATK/pasqyra financiare;
- postime, reversal, correction, audit zyrtar.

**Rregulli:** Finance Operations nuk zëvendëson kontabilitetin. Finance Operations krijon evidence operative; Accounting e poston/mbyll ligjërisht.

## 2. Rolet kryesore

| Rol | Çfarë sheh/bën | Çfarë nuk bën |
| --- | --- | --- |
| Finance Operator / Vajza e financave | cash office, barazime, POS terminale, shpenzime, dorëzim cash, raporte ditore | nuk hap/mbyll periudhë kontabël pa rol kontabilisti |
| Finance Manager / Owner | aprovon diferenca, sheh raport grupi, merr para me arsye, audit | nuk ndryshon dokumente fiskale pa correction workflow |
| Kamarier | tavolinat/porositë e veta, pagesë/mbyllje sipas lejeve | nuk bën zbritje/anulim/transfer pa aprovim |
| Arkatar/Shank | pagesa, cash drawer, shank/bankak, dorëzim | nuk poston GL direkt |
| Kontabilist | GL, libra ATK, TVSH, AR/AP, mbyllje periudhe | nuk bën barazim operativ pa evidence nga financa |
| Tenant Admin | konfigurime, role, lokacione, payment methods | nuk ka qasje të pakufizuar pa audit/break-glass |

## 3. Ekranet që duhet të ekzistojnë

### PLANET

1. **Finance Daily Dashboard** — cash/card/bank, tavolina të hapura/mbyllura, barazime pending, diferenca.
2. **Cash Office / Zyra e Parave** — cash në zyrë, hyrje/dalje, kush mori para, arsye, aprovime.
3. **Waiter Settlement / Barazimi i Kamarierit** — tavolinat, porositë, pagesat cash/card, zbritje, anulime, saldo/float, cash që duhet dorëzuar.
4. **Shank/Banak Settlement** — banak i brendshëm/jashtë, printer/POS terminal/lokacion, cash/card total.
5. **POS Terminal Reconciliation** — terminal bankar, batch/settlement banke, pagesat e lidhura me fatura/tavolina.
6. **Expenses / Cash Out** — shpenzime nga arka, foto/faturë, kategori, aprovim, lidhje me kontabilitet.
7. **Supplier Payments** — pagesa furnitorëve, borxhe, pagesa pjesore/të plota.
8. **Bank Deposit / Cash Transfer** — cash nga zyrë në bankë ose transfer arkash.
9. **Audit & What happened?** — çdo ndryshim, anulim, zbritje, diferencë, kush/kur/pse.

### KABI

1. **Driver/Van Settlement** — ngarkim mëngjesi, shitje, kthime, cash, kredi, mbetje fizike, diferenca.
2. **Farmer Advances / Avanse** — avanse, inpute, zbritje nga kartela mujore.
3. **Cash Collection from Field** — cash nga shoferë/ambulant, fatura me afat, borxhe.
4. **Mobile POS/Card reconciliation** — terminale mobile, offline queue, fiscal status.
5. **Daily Factory Cash Office** — cash, pagesa furnitorë/fermerë, shpenzime operative.

## 4. Data model minimal

| Entity | Qëllimi |
| --- | --- |
| `service_day` | dita operative; për PLANET mund të mbyllet automatikisht në 00:00 sipas vendimit |
| `cash_register` | arka/shank/banak/zyra/automjet |
| `cash_shift` | ndërrim/turn për rol ose arkë |
| `cash_count` | numërim fizik i cash-it |
| `worker_settlement` | barazimi i kamarierit/shoferit/arkatarit |
| `settlement_line` | rresht: tavolinë, faturë, pagesë, shpenzim, diferencë |
| `payment` | pagesë cash/card/bank/credit/advance |
| `payment_method` | cash, kartë, bankë, POS terminal, borxh |
| `payment_terminal` | terminal bankar/POS ID, lokacion, bankë |
| `cash_handover` | dorëzim/marrje cash me arsye dhe aprovim |
| `cash_expense` | shpenzim nga arka me kategori/foto/aprovim |
| `supplier_payment` | pagesë furnitori, pjesore ose totale |
| `bank_deposit` | depozitim cash në bankë |
| `finance_approval` | PIN/aprovim për veprime të ndjeshme |
| `finance_audit_event` | audit append-only për finance operations |

## 5. Integrimi me kontabilitetin

Finance Operations krijon ngjarje operative:

```text
tavolinë/faturë/pagesë/shpenzim/barazim
        ↓
finance event + audit + attachment
        ↓
accounting posting queue
        ↓
GL / AR / AP / VAT / cash-bank
```

Kontabilisti nuk duhet të ribëjë manualisht ditën. Ai/ajo kontrollon raportin, sheh diferencat, poston/mbyll periudhën dhe nxjerr raportet zyrtare.

## 6. Pyetjet që duhet t'i bëjmë vajzës së financave / PLANET

1. Kush e hap dhe kush e mbyll ditën operative?
2. A ka saldo/float për kamarierët? Sa dhe si regjistrohet?
3. Kush e pranon cash-in nga kamarierët dhe kur?
4. Çfarë ndodh në 00:00: mbyllet ndërrimi automatikisht apo vazhdon sipas eventit?
5. Sa POS terminale bankare ka dhe cilit shank/lokacion i takojnë?
6. A regjistrohen tips/bakshish? Nëse po, ku shkojnë?
7. Cilat shpenzime paguhen nga arka dhe kush i aprovon?
8. Kush mund të korrigjojë barazimin pas mbylljes?
9. Çfarë raporti ditor e do Donati: cash, kartë, shpenzime, diferenca, tavolina, kamarierë?
10. Si bëhet depozitimi i cash-it në bankë?
11. Si lidhen pagesat me furnitorë: nga arka, banka apo borxh?
12. A ka banak jashtë që aktivizohet/çaktivizohet sipas sezonit/eventit?

## 7. Vendimi për dokumentim

Ky modul duhet të ketë dokument të vetin canonical:

`canonical/finance-operations/FINANCE_OPERATIONS_Scope_Separation.md`

Nuk duhet të përzihet me:

- `Accounting Core`
- `SEF/Fiscal Outbox`
- `POS/KDS workflow`

Por duhet të lidhet me të tria përmes events, audit dhe posting queue.
"""
    (OUT_DIR / "FINANCE_OPERATIONS_Scope_Separation_v1.md").write_text(doc, encoding="utf-8")


if __name__ == "__main__":
    main()
