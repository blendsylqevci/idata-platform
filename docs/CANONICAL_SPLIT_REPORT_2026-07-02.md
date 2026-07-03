# Canonical Split Report

Date: 2026-07-02
Project: iData+ / idata-platform
Status: v0.1 split created from raw Claude + Codex intake.

## Result

- Copied files: revised after commercial scrub
- Total copied size: revised after commercial scrub

## Buckets

- `archive`: commercial/legal owner-only archives removed from git
- `canonical`: 31 files
- `evidence`: 21 files
- `tools-provenance`: 18 files

## What was promoted

- `docs/canonical/`: current source-of-truth documents for SSOT, Phase 0, accounting, VAT, KABI, PLANET finance, SEF/ATK, architecture, and design.
- `docs/evidence/`: selected sanitized evidence and audit outputs needed to justify canonical claims.
- `docs/archive/`: superseded drafts, old client text, prompts, and historical documents.
- `tools-provenance/`: audit/extraction scripts preserved separately from application code.

## What was intentionally NOT promoted

- `00_raw/private-sensitive/` — SEF identifiers and other secrets remain private and gitignored.
- Raw PRO DATA backups and bulk exports — PII/auth by default, private-local only.
- Bulk SQL/procedure dumps under sanitized evidence — retained in raw intake for traceability, not copied wholesale to canonical docs.
- Contract PDFs, offers, prices, payment terms, and owner-only commercial/legal material are confidential and excluded from this technical repo.

## Missing source files during copy

- None

## Required review before first commit

1. Claude review of split completeness and secret/PII flags.
2. Confirm any remaining phase0-pack minor additions:
   - `posting_run`, `vat_declaration_snapshot`, `partner_card_entry` naming in data model if not already present.
   - explicit 13-domain accountant parity target if desired.
   - feature status rows for PRO DATA extraction waves as closed evidence if desired.
3. Run git status and final secret scan before commit.
