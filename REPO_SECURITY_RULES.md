# Repository Security Rules — iData+

Status: mandatory. This file summarizes the confidentiality rules from `PROJECT_RULES.md`.

## Principle

This repository is technical. It is not owner-private storage. Anything committed here may be visible to current or future collaborators.

## Never commit

- Commercial/legal material: offers, contracts, appendices, prices, payment terms, discounts, maintenance pricing, negotiations.
- Owner-only/client-private documents or old master PDFs.
- SEF identifiers, fiscalization numbers, certificates, private keys, `.env`, API tokens, DB credentials.
- Raw PRO DATA backups/exports or real accountant/client documents.
- Personal data, customer/supplier ledgers, employee/payroll data, bank statements, real invoices.
- Intake/copy manifests that reveal private paths, filenames, or sensitive document names.

## Allowed

- Code, tests, migrations.
- Sanitized technical requirements.
- Sanitized evidence summaries approved for git.
- Provenance scripts without credentials or embedded raw data.

## Required gate

Before every commit/push:

1. Review staged file paths.
2. Search staged content for commercial/legal terms.
3. Search staged content for secrets/keys/tokens.
4. Keep ambiguous material out of git and ask Blend.

## Incident response

If private material is committed, stop development work and clean repository history before adding collaborators or continuing implementation.
