# Platform Foundation Bootstrap — Implementation Record (pre-F1)

**Statusi:** **IMPLEMENTED / PENDING_INDEPENDENT_REVIEW** — PASS vetëm pas Fable delta-review → Codex review (PM_REVIEW_PROTOCOL) → Human Gate i Pronarit → merge i aprovuar.
**Autoriteti:** ADR-024 (MERGED / ACCEPTED, PR #23, merge `e0d4cdc`) · **Data:** 24.07.2026 · **Increment:** Bootstrap pre-F1 — JO F1, JO migrim big-bang, JO module biznesi.

## Struktura reale

```
global.json                      # SDK 10.0.302 i pin-uar (rollForward: disable)
.config/dotnet-tools.json        # dotnet-ef 10.0.10 (tool lokal)
src/platform/
  Idata.Platform.slnx            # formati slnx i SDK 10 (i vetmi solution file)
  Directory.Build.props          # nullable, implicit usings, warnings-as-errors, deterministic, lockfiles
  Directory.Packages.props       # Central Package Management — pins ekzakte, zero wildcard
  Idata.Platform.Api/            # host = composition root; OpenAPI build-time export
  Idata.Platform.DbMigrator/     # i vetmi aplikues i migrimeve (migrate/status/down-to-zero)
  Modules/PlatformStatus/        # modul TEKNIK (jo biznesi)
    Domain/ Application/ Infrastructure/ Presentation/
tests/platform/                  # UnitTests · IntegrationTests · ArchitectureTests
apps/backoffice/                 # Angular 22.0.8 shell (standalone/strict/zoneless)
packages/platform-api-client/    # klient TS i GJENERUAR nga kontrata
contracts/openapi/platform-v1.json  # spec determinist — burimi: ASP.NET Core API
```

## Dependency direction (e provuar me ArchitectureTests mbi metadata reale)

Domain → asgjë · Application → Domain · Infrastructure → Application+Domain ·
Presentation → Application(+Domain) · DbMigrator → Infrastructure · Api = composition root.
Pa MediatR/AutoMapper/broker/Redis/identity provider/UI kit; pa "Common/Utils" junk drawer;
query/handler i thjeshtë (pa CQRS ceremonial).

## Command matrix (root)

| Komanda | Çfarë bën |
|---|---|
| `pnpm platform:restore` | `dotnet restore src/platform --locked-mode` |
| `pnpm platform:build` | build me warnings-as-errors (+ gjeneron spec-in OpenAPI) |
| `pnpm platform:test` | unit + architecture + integration (kërkon target DB) |
| `pnpm platform:migrate` / `:status` / `:down` | DbMigrator migrate / status / down-to-zero |
| `pnpm contracts:generate` | spec build-time + klient TS i gjeneruar |
| `pnpm contracts:drift-check` | rigjenerim + `git diff --exit-code` |
| `pnpm backoffice:build` / `:test` | Angular shell |
| `pnpm verify:all` | legacy + target + contracts bashkë |

## API smoke path

`Angular shell → @idata/platform-api-client (i gjeneruar) → GET /api/platform/status
(operationId GetPlatformStatus) → GetPlatformStatusHandler (Application) →
NpgsqlDatabaseReadinessProbe (Infrastructure, fail-safe) → PostgreSQL idata_platform`.
Përgjigje e tipizuar pa timestamps/paths/sekrete; DB e paarritshme → `DEGRADED/UNAVAILABLE`
e kontrolluar. Identity/audit/outbox/observability raportohen EKSPLICIT
`NOT_CONFIGURED_F1_PENDING` — asnjë no-op që maskohet si enforcement; zero rrugë biznesi/fiskale (e testuar).

## Database

EF Core = migration owner i vetëm; schema module-owned `platform` me historinë
(`__ef_migrations_history`) brenda boundary; migrimi i vetëm `0001_InitialPlatformSchema`
me VETËM tabelën teknike `migration_smoke_check`. API nuk auto-migron. Cikli i provuar
mekanikisht: fresh apply → no-op → down-to-zero → reapply (lokal me Docker + në CI).
**Separimi:** target `idata_platform` ≠ legacy `idata`; runner-i TS + migrimet 0001…0006
legacy të PAPREKURA (oracle); zero dual-write.

## Contracts

Spec-i gjenerohet në build-time me `Microsoft.Extensions.ApiDescription.Server`
(pa server/pa network) → `contracts/openapi/platform-v1.json` (OpenAPI 3.1.1, determinist —
SHA identike mes builds). Klienti: `openapi-typescript` 7.13.0 (tipet) + `openapi-fetch`
0.17.0 (typed CALLABLE client) — pins ekzakte në lockfile; rigjenerimi = zero diff;
asnjë DTO manual dhe asnjë client fetch i dorës.

## Angular shell + i18n scaffold

Angular 22.0.8 project-local (zgjedhje tooling e pin-uar, jo vendim i ri ADR); layout
minimal accessible; status page loading/success/error + retry; dev proxy `/api`;
vitest 4.1.10 (runner-i zyrtar i builder-it). i18n MINIMAL: resource keys neutrale me
paritet sq (default) / en / sr — **JO** U11/full-i18n; pa përkthime ligjore/fiskale.

## Çfarë ËSHTË implementuar (vetëm kjo)

Status-sync post-merge; solution/build/CI .NET i pin-uar; NJË vertical smoke slice teknik;
EF/PostgreSQL migration path; OpenAPI determinist + klient TS i gjeneruar; Angular shell +
i18n scaffold; CI që verifikon të dy stack-et; ky record.

## Çfarë MBETET F1+ (të pahapura këtu)

Identity/Tenant/RBAC production · audit/outbox/observability/recovery production ·
encryption/key custody (ADR-025) · providers (ADR-026) · POS/wrapper (ADR-027) ·
identity provider/store (ADR-028) · module biznesi · Edge/Device Agent/mobile/KDS ·
fiscal domain/SEF · Step B/C · preflight/Tier 2. Gates OPEN të ADR-024 të paprekura.

## Rollback

Hiqet target bootstrap pa prekur legacy oracle: fshi `global.json`, `.config/dotnet-tools.json`,
`src/platform/`, `tests/platform/`, `apps/backoffice/`, `packages/platform-api-client/`,
`contracts/`, hapat "Target stack ADR-024" në `ci.yml`, script-et `platform:*`/`contracts:*`/
`backoffice:*`/`verify:all` në `package.json` dhe rreshtat përkatës në `.gitignore`/`pnpm-lock.yaml`.
Legacy TS (apps/api, apps/web, packages/*) nuk preket nga rollback-u.
