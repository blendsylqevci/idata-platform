# 05 Decision Gates / ADR — Update from PRO DATA Closure

## Accepted ADR: PRO DATA is long-running evidence baseline

Decision: PRO DATA backup evidence remains active through development, UAT, parallel run, cutover and launch.

Consequence: Every accounting/POS/inventory/RBAC module must reference the relevant evidence pack before implementation and during validation.

## Accepted ADR: Do not clone legacy schema blindly

Decision: iData+ will rebuild clean domain models and workflows. PRO DATA is source evidence for behavior/parity, not the target database architecture.

Consequence: especially for PLANET restaurant/POS/KDS, use table sessions/order/ticket/status-event/local-hub model instead of temp-table copying.

## Accepted ADR: No legacy credential migration

Decision: PRO DATA passwords, PINs, MAC/device IDs, tokens/certs and hidden secrets are not migrated.

Consequence: users are recreated/reset; RBAC is re-modeled from capability evidence.

## Decision gate: Accountant official sign-off

Open until accountant confirms:

- official VAT declaration comparison;
- KABI VAT/product rules;
- farmer tax/VAT;
- inventory valuation method;
- dairy costing and COGS treatment;
- returns/expiry/write-off policy.

## Decision gate: PLANET floor plan and hardware

Open until PLANET confirms:

- final floor plan/table coordinates;
- KDS station count and placement;
- printer routes;
- POS terminals/banks;
- local hub hardware and UPS.
