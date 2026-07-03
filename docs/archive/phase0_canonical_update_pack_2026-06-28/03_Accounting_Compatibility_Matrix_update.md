# 03 Accounting Compatibility Matrix — Update from PRO DATA Closure

## Add section: PLANET accountant parity target

The accounting core must make the PLANET accountant feel no loss versus PRO DATA for the core accounting workflow.

Minimum parity domains:

1. Chart of accounts and account grouping.
2. Fiscal years and accounting periods.
3. Document types and numbering.
4. Sales invoices, POS sales and fiscalized documents.
5. Purchase invoices and purchase book.
6. Payments and cash/bank movements.
7. AR/AP partner cards and aging.
8. GL journals and journal lines.
9. VAT/tax codes and VAT books.
10. VAT summary/declaration support.
11. Inventory movements and stock valuation links.
12. COGS/costing evidence where applicable.
13. Corrections, reversals and deleted/cancelled document evidence.

## Add section: Required parity tests

- Document -> GL bridge for SHV, BLV/BLS, PGD/PGH, POS payments.
- Debit = credit invariant for every posted document.
- Subledger totals = GL control accounts.
- Sales book totals match source documents for selected period.
- Purchase book totals match source documents and accountant sample.
- VAT input/output summary matches PRO DATA/accountant-approved result for parallel month.
- Cash/card/bank/POS terminal totals reconcile by business day.
- COGS/stock movements reconcile for selected period before relying on financial statements.

## Add section: Evidence references

- `outputs/prodata_posting_deep_dive_2026-06-28/PRODATA_Posting_Logic_Deep_Dive.md`
- `outputs/prodata_gl_reconciliation_2026-06-28/PRODATA_GL_Posting_Reconciliation_June2026.md`
- `outputs/prodata_reports_extracted_2026-06-27/EXTRACTION_MANIFEST.md`
- `outputs/prodata_supplementary_extract_wave1_2026-06-28/EXTRACTION_MANIFEST.md`
- `outputs/prodata_evidence_closure_parity_matrix_2026-06-28/PRODATA_Evidence_Closure_Parity_Matrix_v1.md`

## Boundary

KABI uses the same accounting core, but KABI-specific dairy posting rules remain decision-gated until confirmed by KABI/accountant/ATK/factory.
