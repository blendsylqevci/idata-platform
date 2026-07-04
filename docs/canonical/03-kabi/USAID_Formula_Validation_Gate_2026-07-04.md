# USAID Formula Validation Gate — KABI Milk Settlement

**Date:** 2026-07-04  
**Status:** Phase 0 evidence gate — technical validation complete; business confirmation pending  
**Confidentiality:** Sanitized canonical document. No farmer names, no real totals, no raw workbook data.

## 1. Purpose

This document records the technical validation of the KABI milk-settlement formula extracted from the private May 2026 workbook. The goal is not to declare the production rule final; the goal is to prove that we understand and can reproduce the workbook calculation exactly for the active farmer rows used in the validation.

This is the gate before designing the real KABI module **Pranim + Shlyerje Fermeri**.

## 2. Source hierarchy

1. Private workbook `E Qumshti Maj 2026.xls` — evidence source, private-local only.
2. Formula cells in `Analizat Maj` — authoritative for what the May workbook calculates.
3. KABI technologist / factory confirmation — required for official business rule.
4. iData+ implementation — must be configurable and tested against fixtures.

## 3. Workbook sheets and roles

| Sheet | Role in workflow | Canonical interpretation |
|---|---|---|
| `Qum Maj` | Daily milk quantities by farmer/day | Daily intake source; monthly total is pulled into settlement. |
| `Analizat Maj` | Quality/fat formula and settlement calculation | USAID formula validation source. |
| `Lista e Fermerav` | Farmer list/card/payment-style summary | Master/list evidence; not automatically the same as USAID formula settlement. |

## 4. Population counts

The workbook contains multiple populations and they must not be mixed:

- `Analizat Maj`: 55 formula blocks.
- Empty formula blocks: 9.
- Active farmer rows with milk in the May validation: 46.
- `Lista e Fermerav`: farmer/master-list style sheet with more rows than the active settlement population.

Therefore, the validation statement is:

> Technical validation passed for active May rows: gross and net matched the workbook for **46/46 active farmer rows**.

## 5. Formula decoded from Excel

The formula is based on class liters, fat percentage, a fat unit, class multipliers, and a per-liter deduction.

```text
base_by_class = liters_in_class × fat_pct × fat_unit
gross = Σ(base_by_class × class_multiplier)
deduction = total_liters × service_fee_rate
net_pay = gross - deduction
price_per_liter = net_pay / total_liters
```

Validated May workbook parameters — **real values are NOT stored in git** (business/pricing logic). They are kept in `evidence-private-local/` and, in production, injected as tenant configuration / secure config:

| Parameter | Value (canonical) | Status |
|---|---|---|
| `FAT_UNIT` | verified private value | Verified from formulas; real value private-local. |
| `CLASS_MULTIPLIERS` (E, I, II, III) | verified private values | Verified from formulas; real values private-local. |
| `SERVICE_FEE_RATE` | verified private value | Verified from formulas; real value private-local; business meaning pending KABI. |

Important: workbook header labels differ from the actual formula values in some places. The formulas are the technical evidence; the labels — and the real parameter values — require KABI confirmation and are held private-local.

## 6. Classification and laboratory workflow

The workbook does not fully calculate the final quality class automatically. The current reference logic can recommend a class from NBC/NQS thresholds, but the production system must support technologist/QC control.

Mandatory workflow rule:

> For each farmer delivery, KABI takes an individual milk sample. The sample is checked by the technologist/laboratory and linked to that farmer's intake. The analysis drives acceptance/rejection, quality class, settlement formula inputs, and traceability.

For Punki/collection routes, the expected safe rule is sample-per-farmer before mixing. If the real process differs, this must be confirmed and documented as an exception.

## 7. Technical validation status

| Check | Result |
|---|---|
| Decimal reference engine | Passed. |
| Synthetic unit tests | Passed: 8/8. |
| Private workbook validation | Passed for active rows: 46/46 gross and net. |
| No real farmer names in canonical docs | Required and satisfied for this document. |
| No real monetary totals in canonical docs | Required and satisfied for this document. |
| Raw workbook in git | Forbidden. Must remain private-local. |

## 8. Open business confirmations

These are not technical formula bugs; they are required before production sign-off:

1. Is the USAID formula the official payment basis, or is it a control/reporting calculation while another farmer list is payment-final?
2. Who approves class E/I/II/III: technologist, lab rule, or manual override?
3. Are the May parameters stable across months or configurable per period?
4. What is the official rounding rule for gross, deduction, net pay, and price-per-liter?
5. What exactly does the per-liter deduction represent?
6. How are advances, cheese/product offsets, input loans, and cash payments applied against the monthly settlement?
7. How are unregistered farmers / farmer tax treatment handled for accounting and VAT reporting?

## 9. Phase placement

| Phase | Use of this validation |
|---|---|
| Phase 0 | Evidence gate: formula decoded and technically validated against private workbook. |
| KABI MVP | Implement milk intake, sample, lab analysis, settlement run, farmer card, monthly report. |
| Accounting Core | Settlement creates supplier/farmer obligation, offsets advances, and posts payment/accounting entries after accountant sign-off. |

## 10. Implementation requirements for the future module

- Use Decimal/decimal math. No floating point for money or settlement.
- Store formula parameters by effective period, not hardcoded forever.
- Store farmer intake rows separately from settlement runs.
- Store individual milk sample and lab analysis per farmer delivery.
- Support technologist override with reason and audit log.
- Settlement run must be repeatable, versioned, auditable, and lockable after approval.
- Private farmer data and raw workbook evidence remain outside git.

## 11. Current gate conclusion

The formula package is ready as **Phase 0 technical evidence**. It is not yet production-final until KABI confirms the business rule and the accountant confirms accounting/tax treatment.
