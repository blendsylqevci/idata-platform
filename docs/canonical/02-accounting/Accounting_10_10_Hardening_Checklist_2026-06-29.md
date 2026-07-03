# Accounting 10/10 Hardening Checklist

**Data:** 2026-06-29
**Status:** canonical checklist / quality gate
**Scope:** shared accounting core for PLANET + KABI
**Qëllimi:** të tregojë çfarë kemi 10/10, çfarë është gati për ndërtim, dhe çfarë mbetet si gate para se ta quajmë kontabilitetin final.

---

## 1. Verdikti i shkurtër

Kontabiliteti është shumë i fortë në nivel arkitekture dhe evidence. Për PLANET/shared-core jemi afër 9/10 si dizajn. Nuk e quaj ende 10/10 final sepse 10/10 kërkon:

- sign-off nga kontabilisti;
- krahasim me deklaratën zyrtare mujore të TVSH-së;
- parallel month i plotë;
- vendime finale për KABI-specific accounting;
- testim automatik të posting rules dhe raporteve.

Pra: **gati për Faza 0 / Accounting Core design + walking skeleton**, por **jo ende final frozen accounting**.

---

## 2. Çfarë kemi shumë të fortë

| Area | Status | Arsye |
| --- | --- | --- |
| Shared accounting core | strong | Një motor i përbashkët për PLANET/KABI, me config per business. |
| PRO DATA parity baseline | strong | Reports, raw tables, GL, tax books, posting procedures, DDL extracted. |
| Libri i Shitjes/Blerjes | strong | Extracted from backup + purchase sample from accountant. |
| GL monthly/account evidence | strong | GL extracts + reconciliation pattern. |
| Posting logic | strong | Deep dive for SHV, BLV/BLS, PGD/PGH, POS payments. |
| VAT structure | strong but needs official validation | We have tax books and reconstructed summary; official declaration still required. |
| Cash/bank/AP/AR | strong | Raw and procedure evidence exists; needs module implementation tests. |
| Audit/reversal model | strong | Evidence supports no hard delete; iData+ should improve with append-only audit. |
| RBAC boundaries | strong | Finance/accounting actions can be permissioned separately. |

---

## 3. Accounting modules required for 10/10

| Module | Must have |
| --- | --- |
| Chart of Accounts | Shared template, per-business accounts, account type, active/passive, mapping. |
| Fiscal periods | Open/close/reopen with approval, locked months, period audit. |
| Journal engine | Debit=credit invariant, source document link, idempotency, reversal. |
| Posting rules | Document type → debit/credit lines, tax mapping, subledger effects. |
| Sales | Invoice/POS → revenue, VAT output, AR/cash/card/bank, COGS where applicable. |
| Purchases | Invoice → stock/expense, VAT input, AP/cash/bank, additional costs. |
| Payments/receipts | PGD/PGH/cash/bank allocation, open positions, partner card. |
| AR/AP | Partner ledger, aging, due dates, credit limits, allocation. |
| VAT books | Libri i Shitjes, Libri i Blerjes, mixed VAT rates, K1/K2/ATK boxes. |
| VAT summary | Draft VAT declaration support, but validated with official accountant filing. |
| Cash/bank | Cash journal, bank journal, bank reconciliation if in scope. |
| Inventory accounting | Stock ledger, COGS, cost layers, adjustments, corrections. |
| Opening balances | Accounts, partners, stock, cash/bank, approved at cutover. |
| Reporting | Trial balance, balance sheet, P&L, GL card, partner card, VAT, cash/bank. |
| Audit | Every post/correction/reversal/period action audit logged. |

---

## 4. Non-negotiable accounting invariants

These are hard rules:

1. Debit must equal credit for every posted journal.
2. Every journal must link to a source document or manual journal reason.
3. No hard delete of financial documents.
4. Corrections use reversal / credit note / correction journal.
5. Posted documents cannot be edited silently.
6. Closed period cannot be changed without reopen approval.
7. Subledger totals must reconcile to GL.
8. VAT books must reconcile to VAT summary.
9. Cash/bank totals must reconcile to Finance Operations handover.
10. No JS `number` for money; use decimal/numeric.
11. SEF money scale is separate from internal accounting numeric.
12. Tenant/business separation is mandatory in every query/report/posting.

---

## 5. Posting-rule tests required

Minimum test pack:

| Flow | Expected test |
| --- | --- |
| Sales invoice SHV | Revenue + VAT output + AR/cash + COGS if stock item. |
| POS sale | Revenue + VAT + payment method + fiscal status + GL posting. |
| Purchase BLV/BLS | Stock/expense + VAT input + AP/cash/bank. |
| Mixed VAT purchase | 18% and 8% in same document, correct purchase-book columns. |
| Payment received PGH | Cash/bank debit, AR credit, allocation to invoice. |
| Payment made PGD | AP debit, cash/bank credit, allocation to supplier invoice. |
| Credit note | Reversal of revenue/VAT/AR/stock as appropriate. |
| Stock movement | Warehouse transfer without false revenue. |
| Inventory adjustment | Stock value + GL adjustment with approval. |
| COGS | Correct costing method, no negative-cost surprises. |
| Period close | Blocks backdated modification after close. |
| Manual journal | Requires reason, permission, debit=credit, audit. |

Canonical detail docs:

- `Accounting_Posting_Rules_v0_2026-06-29.md`
- `Accounting_Posting_Test_Fixtures_v0_2026-06-29.md`
- `VAT_Book_Mapping_v0_2026-06-29.md`
- `Accountant_Review_Pack_v0_2026-06-29.md`
- `Outstanding_Confirmations_Tracker_2026-06-29.md`

---

## 6. PLANET parity gates

PLANET accounting is not “done” until:

1. Chart of accounts imported/mapped and accountant approves.
2. One full parallel month is run.
3. Sales book matches PRO DATA/accountant-approved totals.
4. Purchase book matches PRO DATA/accountant-approved totals.
5. VAT output/input matches official VAT declaration.
6. GL monthly totals match or differences are explained/signed off.
7. AR/AP partner cards match selected samples.
8. Cash/card/bank reports reconcile with Finance Operations.
9. Stock/COGS reports reconcile for selected period.
10. Corrections/reversals are reviewed by accountant.

---

## 7. KABI accounting gates

KABI uses the same accounting core, but these cannot be finalized from PLANET backup:

| Gate | Needs |
| --- | --- |
| Product VAT mapping | Accountant/ATK confirmation per product. |
| Farmer tax treatment | Accountant/ATK confirmation. |
| USAID milk formula | Official formula from KABI/admin. |
| Farmer advances | Accounting treatment: advance, offset, payment cycle. |
| AUVK farmer card/report | Required shape and fields. |
| Batch costing | Milk pool, quality/yield, packaging, loss, by-products. |
| COGS method | Weighted average/FIFO/batch costing final decision. |
| Consignment/returns | Direct sale with return vs consignment accounting. |
| Gajbe/crates | Deposit/stock/customer balance accounting. |
| Van-sales settlement | Driver cash/credit/returns/stock close flow. |
| SEF/offline mobile | Certified fiscal behavior before go-live. |

Until these are answered, KABI can use shared accounting skeleton, but not final dairy-specific accounting.

---

## 8. Accounting data model recommendations

Minimum tables/entities:

- `account`
- `fiscal_year`
- `fiscal_period`
- `journal_entry`
- `journal_line`
- `posting_rule`
- `posting_rule_line`
- `document_type`
- `tax_code`
- `tax_book_entry`
- `partner_ledger_entry`
- `open_item`
- `payment_allocation`
- `cash_account`
- `bank_account`
- `bank_statement`
- `bank_statement_line`
- `inventory_ledger_entry`
- `inventory_cost_layer`
- `period_close_log`
- `manual_journal_reason`
- `accounting_audit_event`

Important: every table must carry `business_id` or derive it safely through a source document. For shared/multi-company reports, aggregation must be explicit and permissioned.

---

## 9. Report pack required

Accounting 10/10 requires these reports:

### Core

- GL account card
- Trial balance
- Balance sheet
- P&L
- Journal list
- Document posting detail

### Tax

- Libri i Shitjes
- Libri i Blerjes
- VAT monthly summary / draft declaration
- VAT by tax code
- Mixed-rate invoice detail

### AR/AP

- Customer card
- Supplier card
- AR aging
- AP aging
- Open invoices
- Payment allocation report

### Cash/Bank

- Cash journal
- Bank journal
- Bank reconciliation
- Payment-method summary
- Finance Operations handover reconciliation

### Inventory/COGS

- Stock card
- Stock value
- COGS report
- Inventory adjustment report
- Cost layer report
- Recipe/production cost report where applicable

---

## 10. 10/10 suggestions beyond baseline

These make accounting truly professional:

1. **Posting simulation mode** — preview GL lines before posting.
2. **Posting explainability** — every GL line can answer “why this account, why this amount”.
3. **Reconciliation dashboard** — GL vs AR/AP vs VAT vs stock vs cash/bank differences.
4. **Accounting lock calendar** — monthly close checklist with responsible user.
5. **Evidence-linked UAT** — each report has source evidence and expected comparison.
6. **Config versioning** — tax/account/posting rule changes are versioned by date.
7. **Backdated transaction controls** — posting into older periods requires permission and reason.
8. **Tenant accounting templates** — reusable templates without hardcoding KABI/PLANET.
9. **Automated invariant tests** — nightly checks for debit=credit, orphan journals, subledger mismatch.
10. **Accountant sign-off workflow** — accountant approves parallel month and cutover balances in-system.

---

## 11. Current score

| Area | Score now | 10/10 condition |
| --- | ---: | --- |
| PLANET evidence baseline | 9.5/10 | Official accountant VAT declaration compared. |
| Shared accounting architecture | 9/10 | Data model + posting tests implemented. |
| Posting logic understanding | 9/10 | More examples tested through automated fixtures. |
| VAT books | 8.5/10 | Accountant signs exact ATK mapping. |
| Finance/accounting separation | 9/10 | Live PLANET finance workflow confirmed. |
| KABI accounting readiness | 7/10 | Factory/accountant/ATK gates answered. |
| Overall accounting readiness | 8.5–9/10 | Parallel month + accountant sign-off + tests. |

---

## 12. Final recommendation

Do not delay the Foundation/Accounting skeleton waiting for every KABI answer. Build the accounting core as configurable shared engine now, but keep KABI-specific posting rules behind decision gates.

The next best accounting step is:

1. Use `Accounting_Posting_Rules_v0_2026-06-29.md` as the first implementation contract for the posting engine.
2. Use `Accounting_Posting_Test_Fixtures_v0_2026-06-29.md` as the minimum test pack for SHV, BLV/BLS, PGD/PGH, POS, COGS and period-close behavior.
3. Use `VAT_Book_Mapping_v0_2026-06-29.md` as the first implementation contract for Libri i Shitjes/Blerjes and VAT summary.
4. Send `Accountant_Review_Pack_v0_2026-06-29.md` to the accountant and capture answers as evidence.
5. Track every external confirmation in `Outstanding_Confirmations_Tracker_2026-06-29.md` until it is reflected in the canonical docs.
6. Define KABI-specific accounting gates separately.

This gives us progress without pretending unknown KABI-specific tax/costing decisions are solved.
