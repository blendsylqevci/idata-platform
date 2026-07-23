# ARCHITECTURE — iData+ (Current Baseline → ADR-024 Target)

**Statusi:** walking skeleton-i TypeScript ekziston; **target-i ADR-024 është i aprovuar, por ende NOT YET IMPLEMENTED**.
**Autoriteti i target-it:** `docs/canonical/01-phase0/05_Decision_Gates_ADR.md` · ADR-024.
**Burimi historik i skeleton-it:** `docs/canonical/01-phase0/Repo_Assembly_Walking_Skeleton_Runbook_2026-06-30.md` + `04_Data_Model_Draft.md` (v2.2.1).

Ky dokument ndan qartë **çfarë ekziston sot** nga **çfarë do të ndërtohet**. Merge-i i ADR-024 nuk e promovon target-in në implemented; migrimi bëhet inkrementalisht, me parity dhe rollback.

## Gjendja aktuale — evidence/oracle historik

```
apps/
  api/        # NestJS walking skeleton
  web/        # Next.js walking skeleton
packages/
  shared-types/   # Money (decimal-safe), branded IDs, enums, invariante
  accounting/     # posting simulator (library-first, PA DB) + fixtures + teste
  database/       # skeleton + migrime/PoC
  integrity-linter/
```

Ky runtime **nuk është target-i i produktit**. Ruhet përkohësisht për teste, PoC dhe parity; nuk zgjerohet me module të reja biznesi dhe hiqet i fundit.

## Target-i ADR-024

| Deployable / sipërfaqe | Target-i |
|---|---|
| Admin / Backoffice | Angular web; ERP/Accounting/WMS/OMS/Production/Finance Ops si bounded modules në një shell |
| POS | Angular + Electron, aplikacion desktop i instalueshëm; Windows baseline production |
| Mobile | React Native + Expo Development Build; SQLite lokal |
| Platform API | C# / ASP.NET Core (.NET 10 target), modular monolith |
| Workers | .NET Worker Services |
| Edge Hub | ASP.NET Core + PostgreSQL lokal |
| POS Local Host | .NET process i paketuar; domain/outbox/encrypted SQLite/keys, typed IPC me Electron |
| Device Agent | .NET process për printer/skaner/peshore dhe pajisje lokale |
| Identity | ASP.NET Core Identity + OpenIddict/OIDC/SSO |
| Data | PostgreSQL qendror me module-owned schemas; SQLite në POS/mobile |
| Contracts | OpenAPI → generated TypeScript clients |

### Kufijtë

- **Një monorepo**, jo repo të reja për POS/ERP/WMS/OMS.
- POS-i është deployable i ndarë nga Backoffice, por përdor të njëjtin identity/API/kontrata.
- Modular monolith fillimisht; nxjerrja në microservice kërkon evidence + ADR të ri.
- Clean/Onion zbatohet brenda moduleve; delivery bëhet me vertical slices.
- CQRS përdoret selektivisht; CRUD i thjeshtë nuk dyfishohet pa arsye.
- Asnjë UI nuk lidhet direkt me DB. Çdo shkrim kalon API/domain/invariantet.
- Electron/Angular renderer-i i POS-it nuk hap SQLite ose keys; .NET POS Local Host është trusted owner.
- Fiscal Core mbetet bounded certification surface.
- Fiscal semantics janë kontratë language-neutral me vectors versionuar: C# reference + TypeScript mobile offline duhet të kalojnë decimal/protobuf/hash equivalence.
- Shqip (default), Anglisht dhe Serbisht janë locale të detyrueshme; print/POS fiscal fixtures provohen para release-it.
- Enkriptimi at-rest/key custody është gate i detyrueshëm (ADR-025); providerët cloud/storage/secrets mbyllen në ADR-026.
- Supabase nuk është dependency qendrore e target-it; Node/pnpm mbeten për UI/mobile/tooling.

## Invariantet jo-negociueshme (runbook §6)

1. **`business_id` në çdo tabelë tenant-scoped** — direkt, jo i nënkuptuar (rule==model, sipas Data Model v2.2.1). Testet e refuzojnë çdo rresht postimi pa `business_id`.
2. **Para = decimal, kurrë float.** Aritmetika monetare e target-it kalon në pure C# domain types; legacy `Money` TS mbetet oracle vetëm deri në parity. **SEF money-scale mbahet ndaras** nga numeric-i i brendshëm i GL-së.
3. **Audit append-only** për veprime kritike (pa mbishkrim; korrigjim = ngjarje e re).
4. **Idempotency** — një `idempotency_key` → një postim; ripërsëritja s'krijon dyfish (offline/POS/van/postime).
5. **Tenant isolation** — modeli që në fillim; RLS si safety-net më vonë.
6. **No hard delete** për dokumente financiare → anulim / korrigjim / reversal.

## Rendi i migrimit

1. ADR-024 dhe sinkronizimi docs.
2. Platform Foundation Bootstrap pre-F1: solution/build/CI, një API/module smoke path, DB migration path, OpenAPI generation dhe Angular shell minimal.
3. F1 Foundation: Identity/Tenant/RBAC, audit, encryption/key custody, observability dhe recovery; pastaj zinxhiri fiskal në Fiscal bounded context të ri: Step B/ATK → Step C cross-runtime conformance → independent re-review → HUMAN GATE → private `/ca/verify` PREFLIGHT → pjesa tjetër e Tier 2; pa endpoint real para Human Gate.
4. Vertical slices modul-pas-moduli; UI dhe backend përfundojnë bashkë per capability.
5. Legacy TS hiqet vetëm pas parity tests, rollback plan, independent review dhe Human Gate; pa dual-write afatgjatë.

## Çfarë NUK është implementuar ende

Target solution/projektet .NET · Angular shell · Electron POS · Identity/OpenIddict · Edge Hub/Device Agent · schema/migrime production · module biznesi · SEF production · UI finale. Gates ADR-011/012/014/016/017/025/026 mbeten OPEN.

## Traceability

Çdo pretendim që prek implementimin → cito Data Model / SSOT / ADR-024 / evidence. Testet përdorin fixtures **sintetike**; asnjë vlerë reale klienti (shih `PROJECT_RULES.md` §3,§4).
