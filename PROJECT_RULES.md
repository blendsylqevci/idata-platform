# iData+ Project Rules

Status: mandatory project guardrails
Applies to: Codex, Claude Code, human collaborators, scripts, commits, PRs, Slack/Notion/GitHub handoff.

## 1. Prime rule: GitHub is visible to collaborators

Treat every file in this repository as visible to any current or future collaborator. A private GitHub repository is not owner-private storage.

If a file would create commercial, legal, privacy, tax, or client-relationship risk if seen by a collaborator, it must not be committed.

When unsure: leave it out of git and ask Blend.

## 2. Never commit these categories

The following are forbidden in GitHub, including docs, screenshots, CSV manifests, archives, prompts, examples, and commit messages:

- Offers, prices, discounts, payment terms, instalments, maintenance prices, commercial negotiations, or internal owner agreements.
- Contracts, legal drafts, signed PDFs, client appendices, old master PDFs, approval drafts, and confidential proposal material.
- SEF application IDs, fiscalization numbers, certificates, private keys, CSR/private key material, POS passwords, API tokens, `.env`, DB credentials, SSH keys, OAuth secrets.
- Raw PRO DATA backups, raw exports, raw DDL/DML dumps with customer/vendor/personnel data, sample previews with real rows, accountant/client files unless explicitly sanitized and approved.
- Personal data or sensitive business data: names, phones, addresses, employee/payroll data, customer ledgers, supplier ledgers, bank statements, real invoices, screenshots containing sensitive values.
- Slack/Notion screenshots or messages containing any of the above.

## 3. Allowed in git

Allowed only when sanitized and useful for implementation:

- Technical source code and tests.
- Public or non-sensitive architecture docs.
- Canonical requirements without commercial/legal owner-only terms.
- Sanitized evidence summaries that do not expose client-private details.
- Provenance scripts without credentials and without embedded raw data.
- Synthetic fixtures or explicitly anonymized examples.

## 4. Evidence handling

Real evidence is valuable but not automatically safe for git.

Default rule:

- Raw evidence = private-local, outside git.
- PRO DATA raw output = PII by default, outside git.
- Contract/commercial/legal evidence = owner-private, outside git.
- Sanitized evidence may enter git only after privacy review.

If exact real numbers are needed for testing, prefer synthetic fixtures or private-local evidence referenced by a non-sensitive summary.

## 5. Required privacy gate before every commit or push

Before staging/committing/pushing, run a privacy review over staged content:

1. Check file paths: no `00_raw/`, `03_contracts/`, `docs-private/`, `private-sensitive/`, `evidence-private-local/`, contract/archive/client-docs paths.
2. Search staged content for commercial/legal terms and amounts.
3. Search staged content for secrets and keys.
4. Search staged content for raw data indicators: names, invoices, phone numbers, bank/ledger exports, `.bak`, `.sql`, `.csv`, `.xlsx`, `.prnx` unless explicitly approved and sanitized.
5. If any hit is ambiguous, stop and ask Blend before commit.

No agent may say “it is probably fine” for privacy-sensitive material.

## 6. Source hierarchy

For business and legal truth:

1. ATK / law / official SEF technical material
2. Accountant confirmations and declared VAT books
3. PRO DATA backup evidence and extracted procedures
4. KABI / PLANET real operations from factory and restaurant
5. AI analysis as support, never as source of truth by itself

For implementation behavior:

- tests + code become the executable truth.

Every canonical claim that affects implementation should cite evidence, source, or reason.

## 7. Agent roles

- Claude Code may build and edit code/docs only inside the approved scope.
- Codex acts as PM/architect/quality gate and must verify privacy, source hierarchy, tests, and consistency.
- No agent may move private/local evidence into git without explicit approval.
- No agent may rewrite commercial/legal documents into git. Contracts live outside the technical repository.

## 8. Conflict and decision logging

If documents conflict:

1. Do not silently choose.
2. Log the conflict.
3. Resolve by source hierarchy.
4. Record the source and reason in canonical docs or ADR.

## 9. Branch and history rules

- `main` must stay protected.
- Force-push is forbidden except for explicit privacy/security incident cleanup approved by Blend.
- If force-push is needed for cleanup, disable rules only temporarily, force-with-lease, then restore protection immediately.

## 10. Practical rule

This repository is for building the product. It is not the owner's filing cabinet.
