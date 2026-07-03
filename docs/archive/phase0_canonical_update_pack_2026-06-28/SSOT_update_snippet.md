# SSOT Update Snippet — PRO DATA Golden Baseline

## Add to SSOT: Evidence baseline decision

**Decision:** PRO DATA backup is the golden reference baseline for PLANET and shared iData+ accounting/POS/inventory/RBAC validation from Phase 0 through launch.

**Scope of use:**

- Phase 0: documentation, data model, ADRs, PoC criteria.
- Accounting Core: posting logic, GL, AR/AP, cash/bank, VAT books, period close.
- Inventory/COGS: item cards, warehouses, stock movements, corrections, price/cost history.
- PLANET POS/KDS: table sessions, real floor plan, order flow, KDS queue, cash shift and payment behavior.
- RBAC/Audit: permission surface, approval gates, append-only audit taxonomy.
- UAT/Parallel Run: compare iData+ reports against PRO DATA/accountant-approved outputs.
- Cutover/Launch: validate migration, opening balances, selected history and final reports.

**Rule:** use PRO DATA as source-faithful evidence and parity baseline, not as a schema to clone blindly.

## Add to SSOT: Closed evidence

- DDL/schema: closed.
- Module map: closed.
- Posting logic deep dive: closed.
- June 2026 GL reconciliation: closed as validation pattern.
- Wave 1 accounting/costing/fiscal/setup: closed.
- Wave 2 restaurant/POS/KDS: closed.
- Wave 3 sanitized audit/RBAC/config: closed.
- Completeness audit: Priority A = 0, Priority B = 0.

## Add to SSOT: Still open / decision-gated

- KABI product VAT mapping.
- Farmer VAT/tax treatment.
- USAID milk price formula.
- KABI dairy batch/yield/loss/by-product/catch-weight final accounting.
- Consignment/returns/expiry/write-off policy.
- Final PLANET floor plan, exact KDS placement/screen count, printer routing and hardware inventory.
- Official monthly VAT declaration comparison/sign-off.
- ATK/SEF certification behavior from official test phase.
