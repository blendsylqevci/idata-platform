# Repo Assembly + Walking Skeleton — Runbook

**Data:** 30 Qershor 2026 · **Për:** Claude Code / Codex · **Burimi:** `iData_Plus_SSOT.md` + `docs/canonical/`
**Rregulli:** ripërdor scaffold-in ekzistues, **mos rishkruaj**. Asnjë modul i madh (POS/KDS/KABI/SEF-full/UI). Vetëm themeli + accounting library.

---

## 1. Precondition (Pronari)
- Repo **private** e krijuar, emër neutral: `idata-platform`.
- Mjedisi: **Node 24 LTS**, `pnpm`, Docker, GitHub CLI (të verifikuara).
- Branch protection në `main` (PR + review) — aktivizo sapo të jetë commit-i i parë.

## 2. Kopjo scaffold-in ekzistues (reuse, jo rewrite)
Scaffold-i (tashmë i vendosur në rrënjë të repo-s pas canonical-split) → rrënja e repo-s:
```
README.md  AGENTS.md  CLAUDE.md  CONTRIBUTING.md  .gitignore  .nvmrc(=24)
```
Mos i ndrysho rregullat brenda tyre (guardrails të dakorduara).

## 3. Struktura e docs
```
docs/
  canonical/   # SSOT, Posting Rules v0, Posting Fixtures v0, VAT Book Mapping v0,
               # Accountant Review Pack, Evidence Traceability Matrix, Accounting Hardening,
               # Outstanding Confirmations Tracker, Finance Ops Scope+Workflow, RBAC, Data Model,
               # Execution Plan, PoC Specs, Feature Status, Parity Matrix
  evidence/    # PRO DATA extracts/manifests, reference_atk/ (template ATK + finance audit)
  archive/     # snapshot-e aprovimi jashtë repo-s, tombstone 06_SEF_Sandbox_Request, drafte të vjetra
docs-private/
  sef/         # sef-test-identifiers.secret.md (App ID/Fiscalization No) — GITIGNORED
```
**Momenti i bashkimit:** këtu futen TË DY setet canonical (workspace + Codex docs) → një burim. `docs-private/` + `*.secret.md` + `*.pem` + `.env` te `.gitignore`.

## 4. Monorepo minimal
```bash
pnpm init
pnpm add -D turbo typescript
# pnpm-workspace.yaml: apps/*  packages/*
mkdir -p apps/api apps/web packages/accounting packages/shared-types packages/database
```
- `apps/api` — NestJS bosh që starton (health route).
- `apps/web` — Next.js bosh që starton.
- `packages/shared-types` — tipat e përbashkët (money, ids, enums).
- `packages/database` — VETËM skeleton (lidhja + migrime bosh); schema vjen gradualisht, jo tani.
- `packages/accounting` — shih §5.

## 5. Accounting library-first (jo production DB)
`packages/accounting` = library e pastër TS, e testueshme, pa DB:
- **Posting simulator:** merr një document fixture → kthen `journal_entry` + `tax_book_entry` + `subledger` (vetëm in-memory).
- **Fixtures:** FX-001…FX-014 nga `Accounting_Posting_Test_Fixtures_v0`.
- **Teste në CI:**
  - `debit == credit` për çdo fixture;
  - **no-float** (decimal/numeric library, kurrë JS `number`);
  - **idempotency** (i njëjti `idempotency_key` → s'krijon dyfish);
  - çdo rresht ka `business_id` + `source_document_id` + `posting_rule_version_id`;
  - shitje/blerje → krijon `tax_book_entry`.
- DB schema/migrations për accounting = **më vonë**, hap pas hapi (pas PoC-2).

## 6. Invariantet që hyjnë nga commit-i i parë
- `business_id` në çdo entitet tenant-scoped.
- Para = **decimal/numeric**, kurrë float; **SEF money-scale ndaras** nga numeric i brendshëm.
- **Audit append-only** për veprime kritike.
- **Idempotency** për postime/offline/POS/van.
- **Tenant isolation** (RLS = safety-net më vonë, por modeli që në fillim).
- **No hard delete** për dokumente financiare → anulim/korrigjim/reversal.
*(Vendosi si konventa + lint + një `ARCHITECTURE.md` i shkurtër; mos i lër për retrofit.)*

## 7. ADR-et që MBETEN të hapura (mos i kyç gjatë skeleton-it)
- ADR-010 Supabase hosted vs self-host EU
- ADR-011 PowerSync vs custom-sync (pas PoC-3)
- ADR-012 GL posting në Postgres functions vs NestJS (provo në PoC-2)
- ADR-013 DB e hub-it lokal: Postgres vs SQLite (pas PoC-4)
- ADR-014 connection pooling · ADR-016 RN/Expo build · ADR-017 printera fiskalë hw vs software (ATK-004)
Fillo me default të sigurt, mbaji të zëvendësueshme.

## 8. Acceptance criteria (skeleton = DONE kur)
- [ ] scaffold + docs split + monorepo të commit-uara; CI jeshile (lint + typecheck + test).
- [ ] **web → api → database "ping"** funksionon (route që prek api→db dhe kthen ok).
- [ ] modeli fillestar: `business/business_group` + `user/role/permission` + `audit_log`.
- [ ] `packages/accounting`: fixtures kalojnë (debit=kredit, no-float, idempotency).
- [ ] branch protection aktiv (PR + review); asnjë sekret në repo.

## 9. Çfarë NUK bëhet ende
POS/KDS · KABI production logic (qumësht/fermerë/batch/van) · SEF full integration (vetëm studim i protokollit + PoC-5 më vonë) · UI finale · asete (Faza 2); payroll = Faza 1 (KABI/kontabilist, jashtë walking skeleton) · config i gateuar (TVSH mappings, COGS final, farmer rules) → prapa config-gates derisa të vijnë përgjigjet.

---

## Pas skeleton-it → 5 PoC-të (rendi)
1. Tenant isolation · 2. Accounting faturë→GL→ATK (mbi simulator-in) · 3. Offline sync (idempotency) · 4. Local hub LAN · 5. SEF (github.com/fiskalizimi, test server).

**Pas këtij runbook-u s'ka më planifikim — krijo repo-n dhe fillo skeleton-i.**
