# 01 PoC Specs — Update from PRO DATA Closure

## PoC Accounting Parity

Goal: prove that iData+ can reproduce PLANET accounting outputs for selected period using PRO DATA evidence.

Pass criteria:

- sales/purchase/payment/POS documents post to GL;
- debit = credit;
- sales and purchase books match selected-period source totals;
- VAT output/input summary reconciles to PRO DATA/accountant-approved result;
- AR/AP and cash/bank totals reconcile for selected period;
- discrepancies are documented as bug, accepted difference or accountant decision.

Evidence:

- posting deep dive;
- GL reconciliation;
- raw reports/tables;
- accountant sample files.

## PoC PLANET POS/KDS local operation

Goal: prove restaurant flow works as domain model, not copied temp-table architecture.

Pass criteria:

- waiter opens table and adds items;
- can send all or selected items to station;
- kitchen/bar sees status event flow;
- timers and delay states work;
- manager approval controls void/transfer/discount;
- bill/payment/fiscal handoff is tracked;
- local hub path works without internet.

Evidence:

- Wave 2 restaurant/POS/KDS extract;
- floor coordinates from `tblRestTavolinat`;
- KDS/open order evidence;
- final PLANET floor-plan/hardware still required.

## PoC RBAC/Audit

Goal: prove critical actions cannot bypass role/approval/audit rules.

Pass criteria:

- old capability coverage represented in new RBAC matrix;
- no legacy credentials migrated;
- risky actions require approval;
- append-only audit created for every critical event.

Evidence:

- Wave 3 sanitized RBAC/config/audit extract.

## PoC SEF

Goal: prove SEF flow against official test phase, not legacy assumptions.

Pass criteria:

- protocol matches official `github.com/fiskalizimi`;
- fiscal outbox handles queued/sent/failed/offline statuses;
- offline 48h alarm and retry path visible;
- test transaction evidence stored privately.
