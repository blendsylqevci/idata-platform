# AGENTS.md — iData+ Agent Rules

Mandatory: read `PROJECT_RULES.md` and `docs/INDEX.md` before making changes.

## Non-negotiable privacy rule

GitHub is visible to collaborators. Do not commit owner-private, commercial, legal, client-private, SEF-secret, credential, or raw evidence material.

Forbidden examples:

- prices, offers, instalments, maintenance terms, discounts, contracts;
- SEF IDs, fiscalization numbers, certificates, private keys, `.env`, credentials;
- raw PRO DATA backups/exports, real invoices, customer/supplier/person data;
- manifests or screenshots that reveal private file names, values, or paths.

If unsure, leave it out of git and ask Blend.

## Work rules

- Keep canonical docs source-based and deduplicated.
- Use source hierarchy from `PROJECT_RULES.md`.
- Do not treat AI output as source of truth.
- Do not move files from `00_raw/` into git without classification and privacy review.
- Do not edit or commit contracts/commercial documents in this repo.
- Before commit/push, run staged privacy checks.

## Implementation rules

- Main language for business UI/docs is Albanian unless otherwise specified.
- Money/accounting: no float; use decimal/numeric.
- Tenant isolation is a first-class invariant.
- Audit and idempotency are first-class invariants.
- Tests must encode accounting and privacy invariants as early as possible.
