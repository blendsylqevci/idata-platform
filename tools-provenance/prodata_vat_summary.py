#!/usr/bin/env python3
from __future__ import annotations

import csv
import sys
from collections import defaultdict
from decimal import Decimal, InvalidOperation
from pathlib import Path


SALES_TAX_COL = "#TVSH Totale"
SALES_NET_COL = "#Vlera totale pa TVSH"
SALES_GROSS_COL = "#Vlera Totale me TVSH"

PURCHASE_TAX_COL = "#TVSH Totale"
PURCHASE_NET_COL = "#Vlera totale pa TVSH"
PURCHASE_GROSS_COL = "#Vlera Totale me TVSH"


def parse_decimal(value: str) -> Decimal:
    value = (value or "").strip()
    if not value:
        return Decimal("0")
    if value.startswith("."):
        value = "0" + value
    if value.startswith("-."):
        value = value.replace("-.", "-0.", 1)
    try:
        return Decimal(value)
    except InvalidOperation:
        return Decimal("0")


def month_from_row(row: dict[str, str]) -> str:
    data = (row.get("Data") or "").strip()
    if len(data) >= 7 and data[4] == "-":
        return data[:7]
    muaji = (row.get("Muaji") or "").strip()
    return muaji or "UNKNOWN"


def read_psv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8", errors="replace", newline="") as f:
        reader = csv.reader(f, delimiter="|")
        rows = list(reader)
    if len(rows) < 3:
        return []
    header = [c.strip() for c in rows[0]]
    out: list[dict[str, str]] = []
    for raw in rows[2:]:
        if not raw or len(raw) != len(header):
            continue
        row = {header[i]: raw[i].strip() for i in range(len(header))}
        if not any(row.values()):
            continue
        out.append(row)
    return out


def add_rows(
    summary: dict[tuple[str, str], dict[str, Decimal]],
    business: str,
    report_type: str,
    path: Path,
) -> None:
    rows = read_psv(path)
    for row in rows:
        key = (business, month_from_row(row))
        item = summary[key]
        if report_type == "sales":
            item["sales_net"] += parse_decimal(row.get(SALES_NET_COL, "0"))
            item["sales_vat"] += parse_decimal(row.get(SALES_TAX_COL, "0"))
            item["sales_gross"] += parse_decimal(row.get(SALES_GROSS_COL, "0"))
            item["sales_rows"] += Decimal("1")
        elif report_type == "purchases":
            item["purchase_net"] += parse_decimal(row.get(PURCHASE_NET_COL, "0"))
            item["purchase_vat"] += parse_decimal(row.get(PURCHASE_TAX_COL, "0"))
            item["purchase_gross"] += parse_decimal(row.get(PURCHASE_GROSS_COL, "0"))
            item["purchase_rows"] += Decimal("1")
        else:
            raise ValueError(f"Unknown report_type: {report_type}")


def money(value: Decimal) -> str:
    return str(value.quantize(Decimal("0.01")))


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: prodata_vat_summary.py <input_dir> <output_csv>", file=sys.stderr)
        return 2

    input_dir = Path(sys.argv[1])
    output_csv = Path(sys.argv[2])

    files = [
        ("PLANET2", "sales", input_dir / "planet2_libri_shitjes_2023_2026.psv"),
        ("PLANET2", "purchases", input_dir / "planet2_libri_blerjes_2023_2026.psv"),
        ("PLANET_RESORT", "sales", input_dir / "planet_resort_libri_shitjes_2023_2026.psv"),
        ("PLANET_RESORT", "purchases", input_dir / "planet_resort_libri_blerjes_2023_2026.psv"),
    ]

    summary: dict[tuple[str, str], dict[str, Decimal]] = defaultdict(
        lambda: defaultdict(Decimal)
    )

    for business, report_type, path in files:
        if not path.exists():
            raise FileNotFoundError(path)
        add_rows(summary, business, report_type, path)

    output_csv.parent.mkdir(parents=True, exist_ok=True)
    with output_csv.open("w", encoding="utf-8", newline="") as f:
        fieldnames = [
            "business",
            "month",
            "sales_rows",
            "sales_net",
            "sales_vat",
            "sales_gross",
            "purchase_rows",
            "purchase_net",
            "purchase_vat",
            "purchase_gross",
            "vat_net_payable_output_minus_input",
        ]
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        for (business, month), item in sorted(summary.items()):
            writer.writerow(
                {
                    "business": business,
                    "month": month,
                    "sales_rows": int(item["sales_rows"]),
                    "sales_net": money(item["sales_net"]),
                    "sales_vat": money(item["sales_vat"]),
                    "sales_gross": money(item["sales_gross"]),
                    "purchase_rows": int(item["purchase_rows"]),
                    "purchase_net": money(item["purchase_net"]),
                    "purchase_vat": money(item["purchase_vat"]),
                    "purchase_gross": money(item["purchase_gross"]),
                    "vat_net_payable_output_minus_input": money(
                        item["sales_vat"] - item["purchase_vat"]
                    ),
                }
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
