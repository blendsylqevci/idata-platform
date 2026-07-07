# ARCHITECTURE — iData+ (Walking Skeleton)

**Statusi:** themel (Increment 1). Burimi: `docs/canonical/01-phase0/Repo_Assembly_Walking_Skeleton_Runbook_2026-06-30.md` + `04_Data_Model_Draft.md` (v2.2.1).

Ky dokument mban invariantet arkitekturore që hyjnë **që nga commit-i i parë** (jo retrofit) dhe zgjedhjet teknike fillestare. Zgjedhjet janë **default të sigurt dhe të zëvendësueshme** (ADR-të e hapura mbeten të hapura — shih runbook §7).

## Struktura (pnpm + turbo monorepo)

```
apps/
  api/        # NestJS — health route (Increment 2)
  web/        # Next.js — bosh që starton (Increment 2)
packages/
  shared-types/   # Money (decimal-safe), branded IDs, enums, invariante
  accounting/     # posting simulator (library-first, PA DB) + fixtures + teste
  database/       # skeleton (lidhje + migrime bosh) — Increment 2
```

## Invariantet jo-negociueshme (runbook §6)

1. **`business_id` në çdo tabelë tenant-scoped** — direkt, jo i nënkuptuar (rule==model, sipas Data Model v2.2.1). Testet e refuzojnë çdo rresht postimi pa `business_id`.
2. **Para = decimal, kurrë `float`/JS `number`.** Aritmetika monetare kalon nëpër `Money` (shih `shared-types`). **SEF money-scale mbahet ndaras** nga numeric-i i brendshëm i GL-së.
3. **Audit append-only** për veprime kritike (pa mbishkrim; korrigjim = ngjarje e re).
4. **Idempotency** — një `idempotency_key` → një postim; ripërsëritja s'krijon dyfish (offline/POS/van/postime).
5. **Tenant isolation** — modeli që në fillim; RLS si safety-net më vonë.
6. **No hard delete** për dokumente financiare → anulim / korrigjim / reversal.

## Zgjedhje teknike fillestare (të zëvendësueshme)

| Fusha | Zgjedhja | Arsyeja / alternativa |
|---|---|---|
| Package manager | **pnpm** + **turbo** | Sipas runbook §4. |
| Gjuha | **TypeScript** `strict` + `noUncheckedIndexedAccess` | Siguri tipi për financa. |
| Testet | **Vitest** | TS-native, i shpejtë; alternativë: Jest. |
| Para (decimal) | **decimal.js** brenda një `Money` value-object | Aritmetikë e saktë; alternativa: big.js, bigint minor-units. E izoluar prapa `Money` → e zëvendësueshme. |

## Çfarë NUK është këtu ende (runbook §9)

POS/KDS · logjika e prodhimit KABI (qumësht/fermerë/batch/van) · SEF full · UI finale · asete (Faza 2). DB schema/migrime për accounting vijnë **pas PoC-2**. ADR-10..17 mbeten të hapura.

## Traceability

Çdo pretendim që prek implementimin → cito Data Model / SSOT / evidence. `packages/accounting` teston invariantet kundër fixtures **sintetike** (asnjë vlerë reale klienti — shih `PROJECT_RULES.md` §3,§4).
