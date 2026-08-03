# iData+ Project Documentation Index

Date: 2026-07-28 (canonical split origjinal: 2026-07-02)
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
- `docs/canonical/01-phase0/05_Decision_Gates_ADR.md` — ADR Register (v1.7); ADR-024 = MERGED/ACCEPTED; Bootstrap = COMPLETED (PR #24 `0f12488c`); F1.1 = COMPLETED (PR #25 `1dc06d5`); **ADR-028 + ADR-029 = ACCEPTED / EFFECTIVE (PR #26 merged, squash `305aefd`, 28.07.2026)**; Increment A = COMPLETED (PR #27 `abc2b291`); 19 Accepted · 2 Superseded · 8 Open · 0 Proposed · 29 unikë
- `docs/canonical/01-phase0/07_PRODATA_Evidence_Closure_Parity_Matrix.md` — v2 Source→Target Closure (dy boshte + capability map)
- `docs/canonical/02-accounting/Accounting_Posting_Rules_v0_2026-06-29.md`
- `docs/canonical/02-accounting/VAT_Book_Mapping_v0_2026-06-29.md`
- `docs/canonical/03-kabi/KABI_Logjika_Operacionale_Kontabiliteti_Draft.md`
- `docs/canonical/04-planet-finance/Finance_Operations_Workflow_Spec_2026-06-29.md`
- `docs/canonical/05-sef-atk/ATK_SEF_Offline_Faturimi_Findings.md`
- `docs/canonical/05-sef-atk/ATK_Compliance_Gate.md`
- `docs/canonical/05-sef-atk/ATK_Source_Register.md` — v2.4 source graph (CORE/EXT/LIN/CONF, bounded registered corpus)
- `docs/canonical/05-sef-atk/ATK_SEF_Requirements_Ledger.md` — v1.7: 132 source-rows · 46 closure keys · U11 i sinkronizuar me F1→F5 · disposition substantiv 1–123 (burimi i statusit)
- `docs/canonical/05-sef-atk/Kupon_Print_Spec.md`
- `docs/canonical/06-architecture/Reliability_Gate.md`
- `docs/canonical/06-architecture/Platform_Foundation_Bootstrap_Record.md` — Bootstrap pre-F1 (COMPLETED, PR #24)
- `docs/canonical/06-architecture/F1_1_Tenant_Authorization_Audit_Core_Record.md` — F1.1 increment (AccessControl + audit + idempotency + migrimi 0002; **COMPLETED / MERGED — PR #25, squash `1dc06d5`, 27.07.2026**)
- `docs/canonical/06-architecture/Application_Architecture_Convention.md` — **konventa EFFECTIVE subordinate ndaj ADR-029 (ACCEPTED, PR #26 `305aefd`)**; **Increment A = COMPLETED/MERGED (PR #27 `abc2b291`)**; hapi aktual = **Increment B (ACTIVE — B.1 store foundation CURRENT)**
- `docs/canonical/06-architecture/PlatformStatus_Architecture_Pilot_Record.md` — **Increment A pilot** (mediator/controller/Scalar; **COMPLETED / MERGED — PR #27, squash `abc2b291`, 28.07.2026**)
- `docs/canonical/06-architecture/Identity_Authority_Store_Decision.md` — **specifikim subordinate ndaj ADR-028 (ACCEPTED / EFFECTIVE)**; implementimi = **Increment B (ACTIVE — B.1 Identity Store Foundation CURRENT; B.2/B.3 NOT STARTED)**; acceptance matrix §9
- `docs/canonical/06-architecture/Identity_Store_Foundation_B1_Record.md` — **Increment B.1 store foundation** (Identity roleless + OpenIddict Core/EF; schema `identity`; migrimi 0003; **COMPLETED / MERGED — PR #28, squash `d6ddff2`, 02.08.2026**)
- `docs/canonical/06-architecture/Identity_Untyped_Data_Access_Boundary_ENG007_Record.md` — **Pre-B2 Increment 1 — ENG-007** (architecture-policy fail-closed mbi raw SQL/ADO.NET/Npgsql/EF-raw; manifest me katër surface platform-only + fingerprint; **COMPLETED / MERGED — PR #29, squash `8f7a22c`, 02.08.2026**; gate `ENG-007` = CLOSED)
- `docs/canonical/06-architecture/Identity_Dynamic_Reflection_Boundary_ENG006_Record.md` — **Pre-B2 Increment 2 — ENG-006 + ENG-008** (kufi deny-by-default i reflection/runtime access me manifest production BOSH + harness determinist i katalogut; **IMPLEMENTED / PENDING_INDEPENDENT_REVIEW — NOT MERGED**; gates `ENG-006` dhe `ENG-008` mbeten OPEN)
- `docs/canonical/08-roadmap/Project_Delivery_Roadmap_Phases_1_10.md`
- `docs/canonical/08-roadmap/Cutover_Rollback_Runbook.md`

## Next gate (rendi aktual — 03.08.2026: PR #29 MERGED `8f7a22c`; Pre-B2 Increment 1 / ENG-007 = CLOSED; hapi aktual = **Pre-B2 Increment 2 — ENG-008 harness determinist + ENG-006 dynamic reflection boundary**, IMPLEMENTED / PENDING_INDEPENDENT_REVIEW / NOT MERGED; B.2/B.3 = NOT STARTED / BLOCKED derisa ENG-006 dhe ENG-008 të jenë CLOSED)

*(**Re-audit-i + harta LAPT = të dorëzuara për review të pavarur** me këtë ndryshim; `ATK-011` = **OPEN / PENDING_INDEPENDENT_REVIEW** (disposition-pass i përpunuar; mbyllja pas review-t të pavarur + Human Gate). **Verdikti: regression lokale 66 PASS/1 LIVE SKIP · Tier 1 PARTIAL/REOPENED · compliance FAIL · Tier 2 NOT IMPLEMENTED/NO-GO** — Gate v2.0.)*

0. **GOV-001 baseline advisory — COMPLETED me merge-in e PR #21.** `DOC-CORE` + `HISTORY-PRIV` aktive advisory; 14 profile NOT_IMPLEMENTED; `HG-LINTER-REQUIRED` mbetet OPEN.
1. **ADR-024 — MERGED / ACCEPTED (PR #23, merge `e0d4cdc`):** target-i .NET/Angular/POS i instalueshëm/RN/PostgreSQL i kanonizuar; stack-u historik SUPERSEDED; desktop wrapper/OS (`ADR-027`) mbetet OPEN; identity provider/store (`ADR-028`) = **ACCEPTED / EFFECTIVE (28.07.2026, PR #26 `305aefd`; implementimi = Increment B ACTIVE — B.1 store foundation CURRENT; B.2/B.3 NOT STARTED)**; **target ≠ implemented — NOT YET IMPLEMENTED përtej incrementeve të merge-uara**.
2. **Platform Foundation Bootstrap pre-F1 — COMPLETED (PR #24 MERGED, squash `0f12488c`, 24.07.2026):** solution/CI + API/module smoke path + PostgreSQL dev migration path + OpenAPI client generation + Angular shell minimal — merged në main pas Fable→Codex→Human Gate. Evidence: `docs/canonical/06-architecture/Platform_Foundation_Bootstrap_Record.md`.
3. **F1 Foundation në stack-un e ri — GATE AKTUAL / ACTIVE / NOT COMPLETE:** Identity/Tenant/RBAC + audit + encryption/key custody + observability + recovery + i18n SQ/EN/SR. F1.1 = COMPLETED (PR #25 `1dc06d5`); Architecture Freeze = COMPLETED/MERGED (PR #26 `305aefd`; ADR-028/029 ACCEPTED/EFFECTIVE); Increment A PlatformStatus pilot = COMPLETED/MERGED (PR #27 `abc2b291`); hapi aktual = **Increment B — Identity Foundation (ACTIVE / NOT COMPLETE): B.1 Identity Store Foundation = COMPLETED / MERGED (PR #28, squash `d6ddff2`, 02.08.2026); pastaj Pre-B2 Increment 1 — ENG-007 untyped data-access boundary = IMPLEMENTED_POLICY / PENDING_INDEPENDENT_REVIEW / NOT MERGED; B.2 authority/flows dhe B.3 projection = NOT STARTED / BLOCKED derisa ENG-006 + ENG-007 të mbyllen me Human Gate**; Increment C AccessControl migration / D module = **NOT STARTED** (ENG-004 fallback AKTIV; ENG-005 OPEN). Step B/C, PREFLIGHT, Tier 2 = NO-GO; RECERT = JO.
4. **Step B — code hardening (`ATK-012`, P0/PRE-TIER2) në Fiscal bounded context të ri:** T2-01..T2-10.
5. **Step C — Fiscal Semantics Conformance Pack:** golden vectors zyrtarë + C#↔TypeScript offline equivalence + fake-HTTP/negative tests.
6. **Independent re-review** i deltës + gates.
7. **HUMAN GATE** — autorizimi për thirrjen e PARË reale (asnjë `/ca/*`, `/pos/*`, `/citizen/*` para tij).
8. **Private `/ca/verify` PREFLIGHT** → vetëm pas suksesit, pjesa tjetër e **Tier 2**; certifikimi/go-live = NO-GO më vete.
9. **Paralel:** PoC-4b — dita e hardware-it (checklist në `poc/poc4-local-hub/README.md`).

Foundation Bootstrap + F1 janë prerequisites teknike, **jo hallka të reja brenda zinxhirit fiskal**; rendi Step B→C→review→Human Gate→preflight→Tier 2 mbetet i pandryshuar.

*(Gate historik i split-it — kaluar: pa humbje kërkesash; pa PII/sekrete të promovuara; pending items të trackuara; kontratat jashtë git.)*

- `canonical/03-kabi/USAID_Formula_Validation_Gate_2026-07-04.md` — sanitized Phase 0 evidence gate for KABI USAID milk settlement formula.
