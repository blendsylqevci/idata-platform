# Accounting Posting Test Fixtures v0

**Data:** 2026-06-29
**Status:** canonical test-design draft
**Scope:** shared accounting core for PLANET + KABI
**Lidhur me:** `Accounting_Posting_Rules_v0_2026-06-29.md`

Ky dokument përcakton testet minimale që duhet të kalojë motori i kontabilitetit para se të themi “posting logic po punon”. Fixtures janë sjellje/teste, jo data migration. Shumat numerike finale mund të mbushen nga sample rows të PRO DATA-s gjatë implementimit.

---

## 1. Si duhet të përdoren këto fixtures

Çdo fixture duhet të ketë:

- `business_id`
- `source_document_id`
- `document_type`
- `posting_date`
- `fiscal_period_id`
- `currency`
- `lines`
- `tax breakdown`
- `payment/allocation`, nëse ka
- expected `journal_entry`
- expected `tax_book_entry`, kur vlen
- expected `subledger/open_item`, kur vlen
- expected audit event

Testet duhet të ekzekutohen në katër nivele:

1. **Unit test** — dokumenti krijon rreshtat e pritur GL.
2. **Integration test** — dokument → GL → tax book → subledger.
3. **Invariant test** — debit=credit, no orphan journal, no missing business_id.
4. **Parity test** — për muaj të zgjedhur, totals krahasohen me PRO DATA/accountant.

---

## 2. Fail conditions globale

Çdo fixture dështon nëse:

1. debit nuk barazohet me kredit;
2. mungon `business_id`;
3. mungon `source_document_id`;
4. mungon `posting_rule_version_id`;
5. krijohen duplicate journals për të njëjtin `idempotency_key`;
6. tax-book entry nuk përputhet me tax breakdown;
7. subledger nuk përputhet me GL;
8. dokumenti poston në periudhë të mbyllur pa approval;
9. përdoret konto pa mapping të qartë;
10. ndryshohet dokument financiar pa correction/reversal audit.

---

## 3. Fixture pack

### FX-001 — SHV01 faturë shitje B2B me TVSH 18%

**Evidence:** `prcPostoShitjetGL`, `SHGL1`, June 2026 sales bridge.

**Given:**

- Customer me AR account.
- Artikull me revenue account dhe VAT 18%.
- Faturë me afat pagese.

**When:** fatura postohet.

**Then expected GL:**

| Debit/Kredit | Account type | Amount |
| --- | --- | --- |
| Debit | AR / customer | gross |
| Kredit | Revenue | net |
| Kredit | VAT Output | VAT |

**Expected side effects:**

- customer open item krijohet;
- Libri i Shitjes entry krijohet;
- document posting status = `posted`;
- nëse artikulli ka stock, ngrihet COGS event i ndarë.

---

### FX-002 — SHV04 / restaurant sale posting

**Evidence:** June 2026 GL reconciliation has `SHV04` 1834 docs with 100% GL coverage. Accountant response 2026-06-30 confirms PLANET restaurant sales use 18% VAT by default.

**Given:**

- Restaurant sale document generated from service/POS flow.
- Business = PLANET, sales VAT config = `T18` / 18%.
- Payment may be separated through payment flow.

**When:** sale is posted.

**Then expected GL:**

- revenue/VAT/AR or settlement account matches posting rule;
- VAT output uses 18% / `T18` for normal PLANET restaurant sale;
- source document retains restaurant trace context;
- tax-book entry included in Libri i Shitjes.

**Fail if:** POS/restaurant context is lost and cannot be traced to waiter/table/business day, or PLANET normal restaurant sale is posted with wrong/default global VAT.

---

### FX-003 — POS sale with cash + card split

**Evidence:** `prcPostoPointofSaleGL`, `prcPOSPaketoDokumentet`, POS all-time bridge. Accountant response 2026-06-30 confirms PLANET restaurant sales use 18% VAT by default.

**Given:**

- POS order closed.
- Business = PLANET, sales VAT config = `T18` / 18%.
- Payment split: cash + card/POS terminal.
- SEF fiscal status recorded.

**When:** local hub/cloud posting executes.

**Then expected GL:**

| Debit/Kredit | Account type | Amount |
| --- | --- | --- |
| Debit | Cash | cash amount |
| Debit | Card/POS terminal clearing | card amount |
| Kredit | Revenue | net |
| Kredit | VAT Output | VAT at 18% / `T18` |

**Expected trace:**

- local document ID;
- cloud document ID;
- fiscal transaction reference;
- POS terminal;
- business day;
- waiter/cashier;
- optional posting batch/master ID.

**Fail if:** same POS sale can be posted twice after retry, or PLANET POS sale does not follow 18% VAT config.

---

### FX-004 — BLV01 purchase invoice for stock with TVSH 18%

**Evidence:** `prcPostoBlerjetImportGL`, `BLGL1`, Libri i Blerjes extract.

**Given:**

- Supplier invoice.
- Stock item.
- VAT 18% deductible.

**When:** purchase invoice approved and posted.

**Then expected GL:**

| Debit/Kredit | Account type | Amount |
| --- | --- | --- |
| Debit | Inventory / stock | net |
| Debit | VAT Input | VAT |
| Kredit | AP / supplier | gross |

**Expected side effects:**

- supplier open item;
- inventory receipt/value layer;
- Libri i Blerjes entry.

---

### FX-005 — Mixed VAT purchase: 18% + 8% in same invoice

**Evidence:** real PLANET purchase-book sample supports mixed-rate rows; Libri i Blerjes extraction; accountant response 2026-06-30 confirms VAT is selected per purchase item/line.

**Given:**

- One supplier invoice.
- Some lines VAT 18%.
- Some lines VAT 8%.

**When:** invoice posts.

**Then expected:**

- one AP gross total;
- separate VAT input amounts by tax code;
- purchase-book columns/ATK boxes split correctly;
- no assumption “one VAT rate per document”.

**Fail if:** system forces one tax rate for the whole invoice.

---

### FX-006 — BLS service/expense purchase

**Evidence:** purchase procedure family supports expense accounts.

**Given:**

- Supplier invoice for service/expense.
- No stock item.

**When:** posted.

**Then expected GL:**

| Debit/Kredit | Account type | Amount |
| --- | --- | --- |
| Debit | Expense | net |
| Debit | VAT Input, if deductible | VAT |
| Kredit | AP / supplier or cash/bank | gross |

**Fail if:** inventory ledger is created for non-stock expense.

---

### FX-007 — PGH01 receipt from customer allocated to invoice

**Evidence:** `prcPostoPagesatGL`, `PGGL1`, payment trace.

**Given:**

- Open customer invoice.
- Receipt through cash/bank/POS terminal.

**When:** payment is allocated.

**Then expected GL:**

| Debit/Kredit | Account type | Amount |
| --- | --- | --- |
| Debit | Cash/bank/payment method | received |
| Kredit | AR/customer | allocated |

**Expected side effects:**

- open item reduced/closed;
- customer card updated;
- cash/bank journal updated;
- Finance Operations link if receipt came from daily handover.

---

### FX-008 — PGD01 payment to supplier allocated to invoice

**Evidence:** `prcPostoPagesatGL`, June 2026 payments.

**Given:**

- Open supplier invoice.
- Payment from cash/bank.

**When:** payment is allocated.

**Then expected GL:**

| Debit/Kredit | Account type | Amount |
| --- | --- | --- |
| Debit | AP/supplier | paid |
| Kredit | Cash/bank | paid |

**Expected side effects:**

- supplier open item reduced/closed;
- supplier card updated;
- payment method/bank trace retained.

---

### FX-009 — Credit note / cancellation for sale

**Evidence:** PRO DATA note buckets + deleted/correction history.

**Given:**

- Posted sale invoice.
- User requests cancellation/credit note with reason and approval.

**When:** correction is approved.

**Then expected:**

- original document remains immutable;
- correction document links to original;
- revenue/VAT/AR reversed or adjusted according to correction type;
- tax book reflects correction in correct reporting period;
- audit event records user, reason, approval.

**Fail if:** original posted invoice is silently modified.

---

### FX-010 — COGS posting separated from sale financial posting

**Evidence:** `CostingEntries=1`, `prcCalculateBADCost`, June SHV cost split.

**Given:**

- Sale of stock item.
- Cost layer exists.

**When:** sale posts and cost engine runs.

**Then expected GL:**

| Debit/Kredit | Account type | Amount |
| --- | --- | --- |
| Debit | COGS | calculated cost |
| Kredit | Inventory | calculated cost |

**Expected:**

- COGS journal linked to source sale;
- financial sale posting remains separate;
- cost is reproducible from cost layer.

---

### FX-011 — Inventory adjustment with approval

**Evidence:** inventory adjustment raw tables + Wave 1.

**Given:**

- Stock discrepancy.
- User has permission to request adjustment.
- Manager/admin approval exists.

**When:** adjustment posts.

**Then expected:**

- inventory quantity/value changes;
- GL adjustment lines balance;
- reason and approval are mandatory;
- report shows adjustment.

**Fail if:** stock value changes without approval/audit.

---

### FX-012 — Period close blocks backdated posting/edit

**Evidence:** accounting hardening requirement; fiscal-period model.

**Given:**

- Fiscal period is closed.

**When:** user tries to post/edit a document dated inside closed period.

**Then expected:**

- operation blocked;
- only reopen approval or correction in allowed period can proceed;
- audit event created for attempted/approved reopen.

---

### FX-013 — Manual journal

**Evidence:** accounting core requirement; manual posting must remain edge case.

**Given:**

- User with accountant permission.
- Debit/kredit lines entered manually.

**When:** manual journal is submitted.

**Then expected:**

- debit=credit required;
- reason required;
- attachment optional but supported;
- no tax-book entry unless explicitly configured;
- visible in audit and GL reports.

**Fail if:** manual journal is used to bypass standard document posting without reason.

---

### FX-014 — Finance Operations handover to accounting

**Evidence:** Finance Operations audit + workflow spec + payment procedures.

**Given:**

- PLANET business day closed.
- Worker settlements completed.
- Cash/card/POS terminal totals approved.

**When:** finance handover is submitted to accounting.

**Then expected:**

- cash journal and card clearing totals reconcile to worker settlements;
- differences require reason/approval;
- accountant can post/confirm cash/bank entries;
- GL, Finance Ops and POS totals agree.

**Fail if:** finance operator can change accounting totals after handover without audit.

---

## 4. Parallel-month parity fixtures

Pas implementimit të fixtures bazë, bëhet test paralel me një muaj real:

| Area | Compare |
| --- | --- |
| Sales | document totals → GL → Libri i Shitjes |
| Purchases | document totals → GL → Libri i Blerjes |
| VAT | VAT output/input → VAT summary → accountant declaration |
| Payments | PGH/PGD totals → AR/AP cards → cash/bank |
| POS | POS sales/payments → Finance Ops → GL |
| Stock/COGS | sale quantities → cost layers → COGS GL |

Qëllimi nuk është që çdo kolonë e vjetër të kopjohet verbatim, por që **kontabilisti të mos humbë funksionin, raportin, kontrollin dhe rakordimin** që ka në PRO DATA.

---

## 5. Minimal implementation order

1. Build posting simulator with in-memory fixtures.
2. Implement `posting_rule` + `posting_rule_line`.
3. Implement GL invariant checks.
4. Implement SHV + BLV/BLS fixtures.
5. Implement PGH/PGD allocation fixtures.
6. Implement POS fixture with idempotency.
7. Implement tax-book output for sales/purchases.
8. Implement COGS fixture as separate event.
9. Add period-close fixture.
10. Run monthly parity pack.

---

## 6. Gate before calling accounting “stable”

Accounting Core is not stable until:

- all fixtures pass in CI;
- posting simulator can explain every GL line;
- one selected PLANET month reconciles against backup/accountant files;
- accountant signs off COA/account mappings and VAT books;
- KABI-specific gates are either closed or explicitly deferred behind config.
