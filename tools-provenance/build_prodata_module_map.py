#!/usr/bin/env python3
"""
Build an Accounting/POS/Inventory module map from extracted PRO DATA DDL.

Input: outputs/prodata_ddl_extracted_2026-06-28
Output: outputs/prodata_module_map_2026-06-28

This is an evidence map, not a direct migration plan.
"""

from __future__ import annotations

import csv
import datetime as dt
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path


DDL_ROOT = Path("outputs/prodata_ddl_extracted_2026-06-28")
OUT_ROOT = Path("outputs/prodata_module_map_2026-06-28")
PRIMARY_DB = "PRODATA_PLANET2"


@dataclass
class DbObject:
    database: str
    object_type: str
    schema: str
    name: str
    rows_estimate: str = ""
    columns: str = ""


def norm(text: str) -> str:
    return text.lower()


PATTERNS: list[tuple[str, str, str, int, str]] = [
    ("Accounting", "Chart of Accounts / GL", r"plani|kontabel|generalledger|ledger|konto|llogar|ditar|bilanc|balance|urdhresafin", 95, "COA/GL/accounting keyword"),
    ("Accounting", "VAT / Tax / ATK books", r"tarif|tvsh|tatim|adm18|libri|rptgrdlibri|deklarat|k1|k2", 92, "VAT/tax/reporting keyword"),
    ("Accounting", "Sales posting / AR", r"shitj|fatur|kupon", 88, "sales/fiscal document keyword"),
    ("Accounting", "Purchases posting / AP", r"blerj|furnitor", 88, "purchase/supplier document keyword"),
    ("Accounting", "Payments / Cash / Bank", r"pages|ekstrakt|bank|arka|kasa|kusur|menyrapagesa", 88, "payment/cash/bank keyword"),
    ("Accounting", "Opening balances / Subledgers", r"beginningbalance|saldo|subjektet|partner|kartel", 84, "opening balance/subledger keyword"),
    ("POS", "POS sales / tickets", r"posshitj|pospages|posbarazim|posarkiva|posoffline|possync|poskusur|giftcard|depozit", 95, "POS document keyword"),
    ("POS", "POS menu/categories/payment", r"poskategoria|menyrapagesa|tavolin|kamarier|kuzhin|barazim", 90, "POS setup/workflow keyword"),
    ("Inventory", "Items / catalog / pricing", r"artikuj|cmimet|barcode|klasifik|brend|paketim|photo|variant|katalog", 94, "item/catalog keyword"),
    ("Inventory", "Stock movements / warehouses", r"levizj|inventory|stoq|stok|depo|lokacion|binmovement|receiveinventory|pranimmalli|pick", 94, "stock movement/warehouse keyword"),
    ("Inventory", "Recipes / production / LOT", r"normativ|prodhim|lot|serial|sn|ambalazh|packages|plani", 90, "recipe/production/LOT keyword"),
    ("Sales/Field", "Orders / routes / agents", r"porosi|agjent|marshut|vizit|target", 78, "commercial/field workflow keyword"),
    ("Admin/System", "Users / security / config", r"user|role|login|config|param|setting|sync|audit|activity", 70, "system/admin keyword"),
]


def classify(obj: DbObject) -> tuple[str, str, int, str]:
    hay = norm(f"{obj.name} {obj.object_type}")

    if "pos" in hay:
        if re.search(r"artikujtposkategoria|poskategoria", hay):
            return "POS", "POS menu/categories/KDS routing", 96, "explicit POS category object"
        if re.search(r"posshitj|pospages|posbarazim|posarkiva|posoffline|possync|poskusur|giftcard|depozit", hay):
            return "POS", "POS financial document + GL/AR/cash handoff", 96, "explicit POS financial/workflow object"

    if re.search(r"normativ|recept", hay):
        return "Inventory", "Recipes / production / costing", 96, "explicit recipe/normative object"

    if re.search(r"lot|serial|sn", hay):
        return "Inventory", "LOT / serial / traceability", 94, "explicit LOT/serial object"

    matches: list[tuple[int, str, str, str]] = []
    for module, submodule, pattern, confidence, reason in PATTERNS:
        if re.search(pattern, hay):
            matches.append((confidence, module, submodule, reason))
    if not matches:
        return "Other/Review", "Needs manual review", 40, "no strong keyword match"

    matches.sort(reverse=True)
    confidence, module, submodule, reason = matches[0]

    # POS financial objects are intentionally cross-domain. Keep POS primary if the object name
    # explicitly starts with POS, but preserve the accounting signal in the submodule.
    if module == "POS" and re.search(r"shitj|pages|barazim|arka", hay):
        submodule = submodule + " + accounting handoff"
    if module == "Accounting" and re.search(r"^tbldocpos|pos", hay):
        module = "POS"
        submodule = "POS financial document + GL/AR/cash handoff"
        confidence = max(confidence, 92)
        reason = "POS object with accounting impact"

    return module, submodule, confidence, reason


def parse_table_inventory(db_dir: Path, database: str) -> list[DbObject]:
    path = db_dir / "00_table_inventory.psv"
    objects: list[DbObject] = []
    with path.open("r", encoding="utf-8") as f:
        for idx, line in enumerate(f):
            line = line.rstrip("\n")
            if idx == 0 or not line.strip():
                continue
            parts = line.split("|")
            if len(parts) != 4:
                continue
            schema, table, rows, columns = parts
            objects.append(DbObject(database, "TABLE", schema, table, rows, columns))
    return objects


def parse_objects_from_sql(db_dir: Path, database: str, filename: str, object_type: str) -> list[DbObject]:
    path = db_dir / filename
    if not path.exists():
        return []
    text = path.read_text(encoding="utf-8", errors="replace")
    objects: list[DbObject] = []
    for match in re.finditer(r"-- Object: \[([^\]]+)\]\.\[([^\]]+)\]", text):
        schema, name = match.groups()
        objects.append(DbObject(database, object_type, schema, name))
    return objects


def load_objects() -> list[DbObject]:
    objects: list[DbObject] = []
    for db_dir in sorted((DDL_ROOT / "databases").iterdir()):
        if not db_dir.is_dir():
            continue
        database = db_dir.name
        objects.extend(parse_table_inventory(db_dir, database))
        objects.extend(parse_objects_from_sql(db_dir, database, "10_views.sql", "VIEW"))
        objects.extend(parse_objects_from_sql(db_dir, database, "11_functions.sql", "FUNCTION"))
        objects.extend(parse_objects_from_sql(db_dir, database, "12_stored_procedures.sql", "PROCEDURE"))
        objects.extend(parse_objects_from_sql(db_dir, database, "13_triggers.sql", "TRIGGER"))
    return objects


def write_csv(rows: list[dict[str, str]]) -> None:
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    path = OUT_ROOT / "prodata_module_map_object_inventory.csv"
    fieldnames = [
        "database",
        "object_type",
        "schema",
        "object_name",
        "rows_estimate",
        "columns",
        "module",
        "submodule",
        "confidence",
        "reason",
    ]
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def key_objects(rows: list[dict[str, str]], module: str, limit: int = 40) -> list[dict[str, str]]:
    selected = [r for r in rows if r["database"] == PRIMARY_DB and r["module"] == module]
    selected.sort(key=lambda r: (int(r["confidence"]), int(r["rows_estimate"] or "0") if r["rows_estimate"].isdigit() else 0), reverse=True)
    return selected[:limit]


def rows_by_name(rows: list[dict[str, str]], names: list[str]) -> list[dict[str, str]]:
    wanted = {n.lower() for n in names}
    selected = [r for r in rows if r["database"] == PRIMARY_DB and r["object_name"].lower() in wanted]
    selected.sort(key=lambda r: names.index(next(n for n in names if n.lower() == r["object_name"].lower())))
    return selected


def md_table(rows: list[dict[str, str]], columns: list[str]) -> str:
    if not rows:
        return "_Nuk u gjetën objekte në këtë kategori._\n"
    out = []
    out.append("| " + " | ".join(columns) + " |")
    out.append("| " + " | ".join("---" for _ in columns) + " |")
    for r in rows:
        out.append("| " + " | ".join(str(r.get(c, "")).replace("|", "\\|") for c in columns) + " |")
    return "\n".join(out) + "\n"


def make_markdown(rows: list[dict[str, str]]) -> None:
    now = dt.datetime.now().astimezone().isoformat(timespec="seconds")
    counts = Counter((r["database"], r["module"]) for r in rows)
    type_counts = Counter((r["database"], r["object_type"]) for r in rows)

    critical_names = [
        "tblPlaniKontabel",
        "tblPlaniKontabelTipet",
        "tblGeneralLedger",
        "tblTarifat",
        "tblDocShitjetMaster",
        "tblDocShitjetDetalet",
        "tblDocBlerjetMaster",
        "tblDocBlerjetDetalet",
        "tblDocPagesatMaster",
        "tblDocPagesatDetalet",
        "tblDocEkstraktiMaster",
        "tblDocEkstraktiDetalet",
        "tblDocEkstraktiLidhjet",
        "tblDocPOSShitjetMaster",
        "tblDocPOSShitjetDetalet",
        "tblDocPOSPagesat",
        "tblArtikujt",
        "tblArtikujtCmimet",
        "tblArtikujtNormativi",
        "tblArtikujtPOSKategoria",
        "tblDocLevizjetMaster",
        "tblDocLevizjetDetalet",
        "tblDocInventoryAdjustmentMaster",
        "tblDocInventoryAdjustmentDetalet",
        "tblSubjektet",
        "tblMenyraPagesa",
        "tblBankat",
    ]

    critical_procs = [
        "prcRPTGRDLibriShitjetADM18",
        "prcRPTGRDLibriShitjetADM18Simple",
        "prcRPTGRDLibriBlerjetADM18",
        "prcRPTGRDLibriBlerjetADM18Simple",
    ]

    summary_rows = []
    for db in sorted({r["database"] for r in rows}):
        for module in ["Accounting", "POS", "Inventory", "Sales/Field", "Admin/System", "Other/Review"]:
            summary_rows.append(
                {
                    "Database": db,
                    "Module": module,
                    "Objects": str(counts.get((db, module), 0)),
                }
            )

    type_summary = []
    for db in sorted({r["database"] for r in rows}):
        for object_type in ["TABLE", "VIEW", "FUNCTION", "PROCEDURE", "TRIGGER"]:
            type_summary.append(
                {
                    "Database": db,
                    "Object type": object_type,
                    "Count": str(type_counts.get((db, object_type), 0)),
                }
            )

    lines: list[str] = [
        "# PRO DATA — Accounting / POS / Inventory Module Map",
        "",
        f"- Generated: `{now}`",
        f"- Source DDL bundle: `outputs/prodata_ddl_extracted_2026-06-28/`",
        f"- Primary analysis database: `{PRIMARY_DB}`",
        "- Purpose: map real PRO DATA objects into accounting/POS/inventory domains for iData+ parity planning.",
        "- Status: evidence map; not a migration plan and not final iData+ schema.",
        "",
        "## Executive verdict",
        "",
        "PRO DATA ka një ndarje shumë të qartë praktike: dokumentet tregtare dhe POS ruhen në tabela `tblDoc...`, ndërsa kontabiliteti final përmbledhet në `tblGeneralLedger`, me `tblPlaniKontabel` si plan kontabël. Kjo na jep bazë të fortë për parity: iData+ nuk duhet ta kopjojë skemën 1:1, por duhet të mbulojë të njëjtat artefakte pune për kontabilistin: GL, libra ATK, shitje, blerje, pagesa, bankë/arkë, stok, artikuj, çmime dhe raporte.",
        "",
        "## Object summary by module",
        "",
        md_table(summary_rows, ["Database", "Module", "Objects"]),
        "## Object summary by type",
        "",
        md_table(type_summary, ["Database", "Object type", "Count"]),
        "## Critical objects for parity",
        "",
        "Këto janë objektet që duhet të futen në `Accounting Compatibility Matrix` si burime reference/parity. `RowsEstimate` është nga `PRODATA_PLANET2`.",
        "",
        md_table(rows_by_name(rows, critical_names), ["object_type", "object_name", "rows_estimate", "columns", "module", "submodule", "confidence"]),
        "## Critical reporting procedures",
        "",
        md_table(rows_by_name(rows, critical_procs), ["object_type", "object_name", "module", "submodule", "confidence"]),
        "## Accounting map",
        "",
        "### Core accounting / GL",
        "",
        "- `tblPlaniKontabel` — plani kontabël. Ky është reference për strukturën e llogarive që kontabilisti e njeh në PRO DATA.",
        "- `tblPlaniKontabelTipet` — tipat/grupimet e llogarive.",
        "- `tblGeneralLedger` — libri kryesor / journal lines. Ky është burimi më i rëndësishëm për parity: debit/kredit, dokument, datë, llogari, lidhje me dokumente.",
        "- `tblDocBeginningBalance*` — saldo hapëse për konta, subjekte dhe stok.",
        "",
        "### Sales / AR / VAT output",
        "",
        "- `tblDocShitjetMaster` + `tblDocShitjetDetalet` — faturat e shitjes jo-POS dhe rreshtat e tyre.",
        "- `tblDocPOSShitjetMaster` + `tblDocPOSShitjetDetalet` — kuponët/faturat POS që kanë ndikim në shitje, arkë, TVSH dhe GL.",
        "- `prcRPTGRDLibriShitjetADM18` — procedura reale e raportit `Libri i Shitjes` për TVSH/ATK.",
        "",
        "### Purchases / AP / VAT input",
        "",
        "- `tblDocBlerjetMaster` + `tblDocBlerjetDetalet` — faturat hyrëse dhe rreshtat.",
        "- `tblDocBlerjetShpenzimetTjera` — shpenzime të tjera të lidhura me blerjen.",
        "- `prcRPTGRDLibriBlerjetADM18` — procedura reale e raportit `Libri i Blerjes` për TVSH/ATK.",
        "",
        "### Payments / cash / bank",
        "",
        "- `tblDocPagesatMaster` + `tblDocPagesatDetalet` — pagesa/arkëtime të dokumenteve.",
        "- `tblDocEkstraktiMaster`, `tblDocEkstraktiDetalet`, `tblDocEkstraktiLidhjet` — ekstrakte bankare dhe lidhja me pagesa/dokumente.",
        "- `tblMenyraPagesa` — metoda pagesash; te PLANET kjo duhet të përdoret për cash/kartë/bankë/POS terminale.",
        "- `tblBankat` — bankat/llogaritë bankare.",
        "",
        "### Tax / VAT setup",
        "",
        "- `tblTarifat` — tarifat e TVSH-së / tatimit. Në iData+ kjo nuk duhet të hardcode-ohet; duhet konfigurim për artikull/subjekt dhe mapping te SEF/ATK.",
        "",
        "## POS map",
        "",
        "- `tblDocPOSShitjetMaster` / `tblDocPOSShitjetDetalet` — dokumenti i shitjes POS.",
        "- `tblDocPOSPagesat` — pagesat POS.",
        "- `tblDocPOSBarazimiMaster` / `tblDocPOSBarazimiDetalet` — barazimi POS; në iData+ duhet zgjeruar për kamarier, ndërrim, cash/POS terminal, shpenzime, saldo dhe barazim ditor.",
        "- `tblPOSArkivaDitariArka`, `tblPOSOfflineResend`, `tblPOSSyncEntitetet`, `tblPOSKusuri` — sinjale se PRO DATA ka pasur mekanizma POS arkiv/sync/offline/kusur, por duhet verifikuar sa janë përdorur realisht.",
        "- `tblArtikujtPOSKategoria` — kategori POS; në iData+ lidhet me menu, KDS sector routing, printer/KDS routing.",
        "",
        "## Inventory map",
        "",
        "- `tblArtikujt` — artikujt/master data kryesor.",
        "- `tblArtikujtCmimet` + `tblArtikujtCmimetLog` — çmimet dhe historiku i çmimeve. Kjo përputhet me kërkesën tonë që çdo ndryshim çmimi të ruhet.",
        "- `tblArtikujtNormativi` — receptura/normativa; për PLANET përdoret për food-cost/stok kuzhine, për KABI do zgjerohet me yield/batch costing.",
        "- `tblDocLevizjetMaster` + `tblDocLevizjetDetalet` — lëvizje stoku/depo.",
        "- `tblDocInventoryAdjustmentMaster` + `tblDocInventoryAdjustmentDetalet` — korrigjime inventari.",
        "- `tblArtikujtLOT`, `tblArtikujtSN`, `tblDocInventoryAdjustmentLOT*` — struktura LOT/SN ekziston, por row counts tregojnë përdorim të kufizuar/zero te PLANET2; për KABI duhet dizajn më i fortë batch/LOT/expiry.",
        "",
        "## What this means for iData+",
        "",
        "1. **Nuk kopjojmë skemën PRO DATA 1:1.** E përdorim për të kuptuar artefaktet, raportet dhe sjelljen e kontabilistit.",
        "2. **Core Accounting duhet të mbulojë minimumin e PRO DATA:** plan kontabël, GL, shitje, blerje, pagesa, ekstrakte bankare, TVSH, libra ATK, kartela, stok/COGS.",
        "3. **POS i PLANET-it kërkon model më të avancuar se PRO DATA:** KDS, tavolina reale, local hub, waiter workflow, barazim sipas kamarierit, PIN approvals, printer backup.",
        "4. **Inventory duhet të ndahet në dy nivele:** shared inventory core për PLANET/KABI + KABI dairy layer për qumësht, batch, LOT, expiry, yield, farmer traceability.",
        "5. **Raportet ATK nuk duhet të bëhen me Excel logic.** Motori kontabël duhet t'i llogarisë me decimal/numeric dhe pastaj t'i eksportojë në formatin që kontabilisti pret.",
        "",
        "## Recommended next actions",
        "",
        "- Shto këtë map te evidence/canonical docs si `PRODATA module map`.",
        "- Hap `03_Accounting_Compatibility_Matrix.md` dhe shto objektet kritike si baseline reference.",
        "- Bëj deep-dive të procedurave `prcRPTGRDLibriShitjetADM18` dhe `prcRPTGRDLibriBlerjetADM18` për kolonat/ATK box mapping.",
        "- Bëj trace nga `tblDocShitjet*` / `tblDocBlerjet*` / `tblDocPagesat*` drejt `tblGeneralLedger` për të kuptuar posting rules të PRO DATA-s.",
        "- Mos e përdor `PRODATA_TEST` si source kryesor; përdore vetëm për krahasim/arkeologji.",
        "",
        "## Full classified inventory",
        "",
        "Shih CSV: `prodata_module_map_object_inventory.csv`.",
        "",
    ]

    write_path = OUT_ROOT / "PRODATA_Accounting_POS_Inventory_Module_Map.md"
    write_path.write_text("\n".join(lines), encoding="utf-8")


def main() -> int:
    OUT_ROOT.mkdir(parents=True, exist_ok=True)
    objects = load_objects()
    rows: list[dict[str, str]] = []
    for obj in objects:
        module, submodule, confidence, reason = classify(obj)
        rows.append(
            {
                "database": obj.database,
                "object_type": obj.object_type,
                "schema": obj.schema,
                "object_name": obj.name,
                "rows_estimate": obj.rows_estimate,
                "columns": obj.columns,
                "module": module,
                "submodule": submodule,
                "confidence": str(confidence),
                "reason": reason,
            }
        )
    rows.sort(key=lambda r: (r["database"], r["module"], r["object_type"], r["object_name"]))
    write_csv(rows)
    make_markdown(rows)
    print(f"Done. Output: {OUT_ROOT}")
    print(f"Objects classified: {len(rows)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
