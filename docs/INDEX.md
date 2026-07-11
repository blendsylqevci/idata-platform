# iData+ Project Documentation Index

Date: 2026-07-10 (canonical split origjinal: 2026-07-02)
Status: Canonical split v0.1, prepared from `00_raw/claude` and `00_raw/codex`; mirëmbahet me docs-PR.

## Mandatory project rules

Before any agent or collaborator changes this repo, read root `PROJECT_RULES.md`, `AGENTS.md`, and `CLAUDE.md`. These rules are mandatory and override convenience. GitHub is visible to collaborators; commercial/legal/private/raw evidence stays outside git.

## Source hierarchy

1. ATK / law / official SEF technical material
2. Accountant confirmations and declared VAT books
3. PRO DATA backup evidence and extracted procedures
4. KABI / PLANET real operations from factory and restaurant
5. AI analysis as support, never as source of truth by itself

During implementation, code plus tests become the truth for system behavior.

## Folder map

- `docs/canonical/` — current working source of truth for requirements, decisions, phase 0, accounting, KABI, PLANET finance, SEF/ATK, architecture, and design.
- `docs/canonical/08-roadmap/` — delivery roadmap Fazat 1–10 + cutover/rollback runbook (detajon SSOT §3, s'e zëvendëson).
- `docs/evidence/` — supporting proof, reconciliation, audit outputs, and intake audit material. Evidence supports canonical claims but is not itself the active spec unless referenced.
- `docs/archive/` — old drafts, superseded packs, prompts, old client text, and provenance that must not compete with canonical.
- `tools-provenance/` — extraction/audit scripts used to produce evidence. These are not app code.
- `00_raw/` — original intake. Keep for traceability; do not treat as canonical.
- `00_raw/private-sensitive/` — private secrets such as SEF test identifiers. This remains gitignored.
- Commercial/legal files, prices, offers, contracts, payment terms, and owner-only agreements are excluded from this technical repository and kept only in private owner/iCode storage.

## Critical rules

- Raw PRO DATA exports/backups are PII by default and must not go into git.
- SEF identifiers, private keys, `.env`, DB credentials, and certificates must not be copied into public docs.
- If two docs conflict, log the conflict and decide by the source hierarchy above.
- Canonical docs should cite evidence or reason when a claim matters for implementation.
- Do not use archived prompts or old master PDFs as active scope without promoting and deduplicating them.

## Key canonical entry points

- `docs/canonical/00-ssot/iData_Plus_SSOT.md`
- `docs/canonical/00-ssot/Feature_Status_Register.md`
- `docs/canonical/00-ssot/Outstanding_Confirmations_Tracker_2026-06-29.md`
- `docs/canonical/01-phase0/00_Phase0_Execution_Plan.md`
- `docs/canonical/01-phase0/Repo_Assembly_Walking_Skeleton_Runbook_2026-06-30.md`
- `docs/canonical/01-phase0/07_PRODATA_Evidence_Closure_Parity_Matrix.md` — v2 Source→Target Closure (dy boshte + capability map)
- `docs/canonical/02-accounting/Accounting_Posting_Rules_v0_2026-06-29.md`
- `docs/canonical/02-accounting/VAT_Book_Mapping_v0_2026-06-29.md`
- `docs/canonical/03-kabi/KABI_Logjika_Operacionale_Kontabiliteti_Draft.md`
- `docs/canonical/04-planet-finance/Finance_Operations_Workflow_Spec_2026-06-29.md`
- `docs/canonical/05-sef-atk/ATK_SEF_Offline_Faturimi_Findings.md`
- `docs/canonical/05-sef-atk/ATK_Compliance_Gate.md`
- `docs/canonical/05-sef-atk/ATK_Source_Register.md`
- `docs/canonical/05-sef-atk/Kupon_Print_Spec.md`
- `docs/canonical/06-architecture/Reliability_Gate.md`
- `docs/canonical/08-roadmap/Project_Delivery_Roadmap_Phases_1_10.md`
- `docs/canonical/08-roadmap/Cutover_Rollback_Runbook.md`

## Next gate (rendi aktual — 10.07.2026)

*(**PRO DATA Source→Target Closure v2 = kryer** me merge të këtij ndryshimi — `07_PRODATA_Evidence_Closure_Parity_Matrix.md` v2.0.)*

1. **ATK full-corpus/LAPT closure (P0 / PRE-TIER2):** full-corpus re-audit i `ATK_Compliance_Gate.md` kundrejt `ATK_Source_Register.md` + harta LAPT (`ATK-011`).
   *Blockers PRE-TIER2 = PRO DATA Closure v2 (kryer) + re-audit + ATK-011.* `ATK-005` triagjohet/**fiksohet** para Tier 2 (përgjigjet server-dependent merren GJATË Tier 2); `ATK-006`/`ATK-010` = certification gates të trackuara — **jo blockers automatikë** të Tier 2 (përveç nëse përgjigjja ndryshon scope-in/sigurinë).
2. **PREFLIGHT privat** (wrapper i maskuar — pjesë e Tier 2, i gated).
3. **PoC-5 Tier 2 LIVE** — VETËM pas hapave 1–2 + aprovimit të veçantë të Pronarit (Human Gate).
4. **Paralel:** PoC-4b — dita e hardware-it (checklist në `poc/poc4-local-hub/README.md`).

*(Gate historik i split-it — kaluar: pa humbje kërkesash; pa PII/sekrete të promovuara; pending items të trackuara; kontratat jashtë git.)*

- `canonical/03-kabi/USAID_Formula_Validation_Gate_2026-07-04.md` — sanitized Phase 0 evidence gate for KABI USAID milk settlement formula.
