# ARCHITECTURE — iData+ (Current Baseline → ADR-024 Target)

**Statusi (përditësuar 04.10.2026):** target-i ADR-024 është i aprovuar dhe **i implementuar vetëm në themelet e merge-uara** (platforma .NET 10, Angular Backoffice shell minimal, kontrata OpenAPI → klient TS; F1 = ACTIVE / NOT COMPLETE; production Go = BLOCKED); **deployables e tjera dhe modulet e biznesit = NOT YET IMPLEMENTED**; walking skeleton-i TypeScript dhe provat e konceptit = evidence/oracle historik. Rendi aktual dhe hapi i radhës: `docs/INDEX.md` → Next gate.
**Autoriteti i target-it:** `docs/canonical/01-phase0/05_Decision_Gates_ADR.md` · ADR-024.
**Burimi historik i skeleton-it:** `docs/canonical/01-phase0/Repo_Assembly_Walking_Skeleton_Runbook_2026-06-30.md` + `04_Data_Model_Draft.md` (v2.2.1).

Ky dokument ndan qartë **çfarë ekziston sot** nga **çfarë do të ndërtohet**. Merge-i i ADR-024 nuk e promovon target-in në implemented; migrimi bëhet inkrementalisht, me parity dhe rollback.

## Gjendja aktuale

### Themelet e target-it — të implementuara (incremente të merge-uara)

```
src/platform/                    # .NET 10, modular monolith (Bootstrap pre-F1 + F1)
  Idata.Platform.Api/            # host = composition root; OpenAPI build-time export
  Idata.Platform.DbMigrator/     # aplikuesi i vetëm i migrimeve (0001–0003)
  Idata.Platform.Persistence/    # migration owner neutral (schemas `platform` + `identity`)
  Modules/PlatformStatus/        # pilot teknik i konventës (Increment A)
  Modules/AccessControl/         # tenant RBAC + audit business-scoped + idempotency (F1.1)
  Modules/Identity/              # ASP.NET Core Identity store + OpenIddict authority (B.1, B.2a–B.2c)
tests/platform/                  # Unit · Integration · Architecture
apps/backoffice/                 # Angular shell minimal + i18n scaffold SQ/EN/SR (Bootstrap)
packages/platform-api-client/    # klient TS i gjeneruar nga kontrata
contracts/openapi/               # spec determinist (burimi: ASP.NET Core API)
```

Kufijtë e asaj që ekziston: Identity mbulon authority-n, login/consent OAuth 2.0 code+PKCE dhe profilin OIDC provider-side (`openid`, ID token, `nonce`, UserInfo minimal) — **jo sign-in të produktit në Angular**; Backoffice-i është shell minimal, jo UI finale; asnjë modul biznesi dhe asnjë SEF production. Evidenca: Records në `docs/canonical/06-architecture/` (Bootstrap, F1.1, PlatformStatus, B.1, B.2a, B.2b, B.2c).

### Legacy dhe material historik — evidence/oracle, jo target

```
apps/
  api/        # NestJS walking skeleton
  web/        # Next.js walking skeleton
packages/
  shared-types/   # Money (decimal-safe), branded IDs, enums, invariante
  accounting/     # posting simulator (library-first, PA DB) + fixtures + teste
  database/       # skeleton + migrime/PoC
poc/              # PoC-1…PoC-5 (README/analiza, fixtures sintetike)
```

Ky runtime **nuk është target-i i produktit**. Ruhet përkohësisht për teste, PoC dhe parity; nuk zgjerohet me module të reja biznesi dhe hiqet i fundit. `packages/integrity-linter/` është tooling governance (GOV-001, advisory), jo runtime produkti.

## Target-i ADR-024

| Deployable / sipërfaqe | Target-i | Gjendja (04.10.2026) |
|---|---|---|
| Admin / Backoffice | Angular web; ERP/Accounting/WMS/OMS/Production/Finance Ops si bounded modules në një shell | Shell minimal i implementuar (Bootstrap); modulet dhe UI finale = PLANNED |
| POS | Angular UI, aplikacion desktop i instalueshëm; wrapper-i dhe matrica e OS-it mbeten OPEN (ADR-027, me evidence nga ADR-017/PoC-4b) | PLANNED |
| Mobile | React Native + Expo Development Build; SQLite lokal | PLANNED |
| Platform API | C# / ASP.NET Core (.NET 10 target), modular monolith | Themeli i implementuar: modulet PlatformStatus, AccessControl, Identity; modulet e biznesit = PLANNED |
| Workers | .NET Worker Services | PLANNED |
| Edge Hub | ASP.NET Core + PostgreSQL lokal | PLANNED |
| POS Local Host | .NET process i shpërndarë me instalimin e POS-it; domain/outbox/encrypted SQLite/keys dhe kontratë lokale IPC e tipizuar | PLANNED |
| Device Agent | .NET process për printer/skaner/peshore dhe pajisje lokale | PLANNED |
| Identity | OIDC/OAuth2 contract + një authority/login/SSO dhe kufi autorizimi provider-neutral në ASP.NET Core; provider/store = ASP.NET Core Identity + OpenIddict (ADR-028 ACCEPTED / EFFECTIVE) | Pjesërisht (Increment B dhe B.2 = ACTIVE / NOT COMPLETE): B.1, B.2a, B.2b dhe B.2c provider-side = MERGED; sign-in i produktit në Angular, full OIDC conformance/certification, MFA/recovery, refresh, logout/revocation, audit identity dhe offline auth = OPEN; B.3 = NOT STARTED / BLOCKED |
| Data | PostgreSQL qendror me module-owned schemas; SQLite në POS/mobile | PostgreSQL: schemas `platform` + `identity`, migrimet 0001–0003; SQLite = PLANNED |
| Contracts | OpenAPI → generated TypeScript clients | Implementuar: spec build-time + klient TS i gjeneruar, me drift-check në CI |

### Kufijtë

- **Një monorepo**, jo repo të reja për POS/ERP/WMS/OMS.
- POS-i është deployable i ndarë nga Backoffice, por përdor të njëjtin identity/API/kontrata.
- Modular monolith fillimisht; nxjerrja në microservice kërkon evidence + ADR të ri.
- Clean/Onion zbatohet brenda moduleve; delivery bëhet me vertical slices.
- CQRS përdoret selektivisht; CRUD i thjeshtë nuk dyfishohet pa arsye.
- Asnjë UI nuk lidhet direkt me DB. Çdo shkrim kalon API/domain/invariantet.
- Angular renderer-i i POS-it nuk hap SQLite ose keys; .NET POS Local Host është trusted owner. Wrapper-i vetëm menaxhon lifecycle/IPC sipas ADR-027.
- Fiscal Core mbetet bounded certification surface.
- Fiscal semantics janë kontratë language-neutral me vectors versionuar: C# reference + TypeScript mobile offline duhet të kalojnë decimal/protobuf/hash equivalence.
- Shqip (default), Anglisht dhe Serbisht janë locale të detyrueshme; print/POS fiscal fixtures provohen para release-it. **Atribuimi:** CORE-02 / UA 01/2026 §32.2.9, f.27 kërkon shumëgjuhësi sipas legjislacionit për gjuhët zyrtare (SQ/SR në Ledger U11); EN është target i detyrueshëm i Pronarit/produktit.
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

1. ADR-024 dhe sinkronizimi docs. — *COMPLETED (PR #23).*
2. Platform Foundation Bootstrap pre-F1: solution/build/CI, një API/module smoke path, DB migration path, OpenAPI generation dhe Angular shell minimal. — *COMPLETED (PR #24).*
3. F1 Foundation: Identity/Tenant/RBAC, audit, encryption/key custody, observability dhe recovery; pastaj zinxhiri fiskal në Fiscal bounded context të ri: Step B/ATK → Step C cross-runtime conformance → independent re-review → HUMAN GATE → private `/ca/verify` PREFLIGHT → pjesa tjetër e Tier 2; pa endpoint real para Human Gate. — *F1 = ACTIVE / NOT COMPLETE (të merge-uara: F1.1, Increment A, B.1, B.2a, B.2b, B.2c provider-side; Increment B dhe B.2 = ACTIVE / NOT COMPLETE — pjesa e mbetur e B.2 = OPEN; B.3 = NOT STARTED / BLOCKED; Increment C/D = NOT STARTED); production Go = BLOCKED; Step B = NOT STARTED / NO-GO.*
4. Vertical slices modul-pas-moduli; UI dhe backend përfundojnë bashkë per capability.
5. Legacy TS hiqet vetëm pas parity tests, rollback plan, independent review dhe Human Gate; pa dual-write afatgjatë.

Rendi i delivery-t i produkteve = **PLANET-FIRST** (ADR-030 ACCEPTED / EFFECTIVE; Roadmap §3a) — overlay ekzekutimi, pa ndryshim të identitetit të fazave F1–F10 apo të këtij rendi teknik.

## Çfarë NUK është implementuar ende

Deployables e planifikuara: POS (Angular UI + POS Local Host; desktop wrapper dhe OS matrix — ADR-027) · KDS (klient LAN/kiosk; shell-i dhe hardware-i = gate OPEN) · Mobile · Workers · Edge Hub · Device Agent. Përtej themeleve: module biznesi · UI finale e Backoffice-it · sign-in i produktit në Angular (BFF/SPA/klienti real) · pjesa e mbetur e Identity (full OIDC conformance/certification, MFA/recovery, refresh, logout/revocation, audit identity, offline auth; B.3 projection/freshness = NOT STARTED / BLOCKED) · mjedis dhe migrime production (ADR-026 OPEN; migrimet 0001–0003 provohen në CI) · SEF production. Gates ADR-011/012/014/016/017/025/026/027 mbeten OPEN (ADR-028/029/030 = ACCEPTED / EFFECTIVE).

## Traceability

Çdo pretendim që prek implementimin → cito Data Model / SSOT / ADR-024 / evidence. Testet përdorin fixtures **sintetike**; asnjë vlerë reale klienti (shih `PROJECT_RULES.md` §3,§4).
