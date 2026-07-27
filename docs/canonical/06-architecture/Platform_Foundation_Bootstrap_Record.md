# Platform Foundation Bootstrap — Implementation Record (pre-F1)

**Statusi:** **COMPLETED / MERGED** — kaloi Fable delta-review → Codex review (PM_REVIEW_PROTOCOL) → Human Gate të Pronarit; PR #24 merged në `main` (squash `0f12488cdd776a9d51b841cc25a0ac2766ff0bab`, 24.07.2026).
**Autoriteti:** ADR-024 (MERGED / ACCEPTED, PR #23, merge `e0d4cdc`) · **Data:** 24.07.2026 (v3 — post merge i PR #24) · **Increment:** Bootstrap pre-F1 — JO F1, JO migrim big-bang, JO module biznesi.

## Struktura reale

```
global.json                      # SDK 10.0.302 i pin-uar (rollForward: disable)
.config/dotnet-tools.json        # dotnet-ef 10.0.10 (tool lokal)
src/platform/
  Idata.Platform.slnx            # formati slnx i SDK 10 (i vetmi solution file)
  Directory.Build.props          # nullable, implicit usings, warnings-as-errors, deterministic, lockfiles
  Directory.Packages.props       # Central Package Management — pins ekzakte, zero wildcard
  Idata.Platform.Api/            # host = composition root; OpenAPI build-time export
  Idata.Platform.DbMigrator/     # aplikuesi i vetëm i migrimeve; migrate/status/applied-ids/assert-state + down-to-zero (test-only, guarded)
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

> **HISTORICAL BOOTSTRAP SCOPE (shënim 27.07.2026, docs-only):** kjo deklaratë përshkruan
> saktë çfarë PR #24 NUK implementoi — dhe mbetet e saktë si histori. ADR-029 dhe ADR-028
> qeverisin target-in e incrementeve të ARDHSHME (mediator martinothamar/Mediator,
> Controllers, Scalar, Identity/OpenIddict) vetëm pasi të kalojnë review, Human Gate dhe
> merge. Historia e Bootstrap-it mbetet e pandryshuar.

## Command matrix (root)

| Komanda | Çfarë bën |
|---|---|
| `pnpm platform:restore` | `dotnet restore src/platform --locked-mode` |
| `pnpm platform:build` | build me warnings-as-errors (+ gjeneron spec-in OpenAPI) |
| `pnpm platform:test` | unit + architecture + integration (kërkon target DB) |
| `pnpm platform:migrate` / `:status` | DbMigrator migrate / status |
| `pnpm platform:migrate:reset:test-only` | down-to-zero **TEST-ONLY / GUARDED** — fail-closed pa env {Development,CI} + opt-in ekzakt + DB sintetike; skripti NUK e fsheh opt-in-in |
| `pnpm platform:assert-state` (përmes DbMigrator `assert-state`) | verifikim machine-checkable i applied/pending/sentinel |
| `pnpm contracts:generate` | spec build-time + klient TS i gjeneruar |
| `pnpm contracts:drift-check` | rigjenerim + `git diff --exit-code` |
| `pnpm backoffice:build` / `:test` | Angular shell **përmes Turbo** (`--filter=@idata/backoffice`) — ndërton `@idata/platform-api-client` para app-it (fresh-checkout-safe) |
| `pnpm verify:all` | legacy + target + contracts bashkë |

## API smoke path

`Angular shell → @idata/platform-api-client (i gjeneruar) → GET /api/platform/status
(operationId GetPlatformStatus) → GetPlatformStatusHandler (Application) →
NpgsqlDatabaseReadinessProbe (Infrastructure, fail-safe) → PostgreSQL idata_platform`.
Përgjigje e tipizuar pa timestamps/paths/sekrete; `READY` kthehet VETËM kur serveri është i arritshëm DHE i gjithë seti i migrimeve i aplikuar (pending=0) DHE tabela sentinel `platform.migration_smoke_check` ekziston; DB e arritshme por e pamigruar, sentinel i hequr ose e paarritshme → `DEGRADED/UNAVAILABLE` (pa false-positive; probe READ-ONLY, pa auto-migrim, pa detaje lidhjeje). Identity/audit/outbox/observability raportohen EKSPLICIT `NOT_CONFIGURED_F1_PENDING` — asnjë no-op që maskohet si enforcement; zero rrugë biznesi/fiskale (e testuar).

## Database

EF Core = migration owner i vetëm; schema module-owned `platform` me historinë
(`__ef_migrations_history`) brenda boundary; migrimi i vetëm `0001_InitialPlatformSchema`
me VETËM tabelën teknike `migration_smoke_check`. API nuk auto-migron. Cikli i provuar
**machine-checkable** me verifikuesin `assert-state <applied> <pending> <present|absent>`
(krahasim PURE kundër setit të assembly-t, jo grep mbi tekst): fresh(1/0/present) →
no-op(1/0/present) → guarded-down(0/1/absent) → reapply(1/0/present); mismatch → exit
jo-zero (lokal me Docker + në CI). Down-to-zero është **TEST-ONLY / GUARDED**: guard PURE
para çdo DbContext/connection/query — kërkon env {Development,CI} + opt-in ekzakt
`IDATA_ALLOW_DESTRUCTIVE_MIGRATION_RESET` + DB sintetike (`idata_platform_ci` EKZAKT ose
`idata_platform_test_` + suffix JO-bosh `[a-z0-9_]+`); pa njërën → exit 2, zero mutacion,
zero detaje në output. Guard-i dhe lidhja konsumojnë të NJËJTIN konfigurim të validuar
(shih "Siguria" më poshtë).
**Separimi:** target dev `idata_platform` / CI `idata_platform_ci` ≠ legacy `idata`;
runner-i TS + migrimet 0001…0006 legacy të PAPREKURA (oracle); zero dual-write.

## Contracts

Spec-i gjenerohet në build-time me `Microsoft.Extensions.ApiDescription.Server`
(pa server/pa network) → `contracts/openapi/platform-v1.json` (OpenAPI 3.1.1, determinist —
SHA identike mes builds). Klienti: `openapi-typescript` 7.13.0 (tipet) + `openapi-fetch`
0.17.0 (typed CALLABLE client) — pins ekzakte në lockfile; rigjenerimi = zero diff;
asnjë DTO manual dhe asnjë client fetch i dorës.

## Angular shell + i18n scaffold

Angular 22.0.8 project-local (zgjedhje tooling e pin-uar, jo vendim i ri ADR); layout
minimal accessible; status page loading/success/error + retry (loading dhe retry të
testuara realisht me deferred promise + klik); dev proxy `/api`; vitest 4.1.10 (runner-i
zyrtar i builder-it). Klienti i ri `@idata/platform-api-client` është pin-uar ekzakt
(typescript 5.9.3, vitest 3.2.7); root scripts `backoffice:*` përdorin Turbo që ndërtojnë
klientin para app-it (fresh-checkout-safe). i18n MINIMAL: resource keys neutrale me
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

## Siguria (correction pass i review-t të pavarur, 24.07.2026)

**Connection-string injection (DbMigrator/down-to-zero) — MBYLLUR fail-closed.**
Konfigurimi i target DB-së parsohet NJË herë në `PlatformDatabaseSettings` me validim
allowlist fail-closed (host/port/username/database; database: nis me shkronjë,
`[a-z0-9_]`, ≤63; regex me ankorim `\z` — jo `$`, që në .NET lejon `\n` fundor; `;`,
`=`, whitespace, thonjëza, control chars → refuzim). I NJËJTI objekt i validuar ushqen
guard-in destruktiv, connection-string builder-in (`NpgsqlConnectionStringBuilder`,
zero interpolim manual) dhe DbContext factory-n — zero mundësi që guard-i të shohë DB
tjetër nga ajo ku lidhemi. Vlerë e papranuar → exit 2 PARA çdo DbContext/connection/query,
zero mutacion, zero echo i vlerës në output. E provuar: regression test integrimi
(`;Database=` devijimi origjinal + variantet `;Host=`/`;Username=`/`;Timeout=`, target-i
sintetik i paprekur 1/0/present) — RED i demonstruar mbi kodin e vjetër para fix-it —
plus matrica unit e validimit dhe boundary test i builder-it (re-parse: saktësisht një
çelës `Database` me vlerën e validuar).

**js-yaml 4.2.0 → 4.3.0 (GHSA-52cp-r559-cp3m / CVE-2026-59869) — pin i PËRKOHSHËM.**
Rruga transitore: `@idata/platform-api-client → openapi-typescript 7.13.0 →
@redocly/openapi-core 1.34.17 → js-yaml 4.2.0` (tooling i gjenerimit OpenAPI — dev-time,
jo runtime prodhimi). Remediation: pnpm override i shënjestruar
`"@redocly/openapi-core@1.34.17>js-yaml": "4.3.0"` (patch-i i parë 4.x). Verifikuar:
zero `js-yaml 4.2.0` në lockfile; `pnpm why -r` tregon vetëm 4.3.0 përmes rrugës redocly;
gjenerimi i kontratës ×2 zero-diff; build/test i client-it dhe backoffice kalojnë.
**Owner: Tech/Platform Owner. Hiqet kur** upstream (`@redocly/openapi-core` ose
`openapi-typescript`) konsumon js-yaml të patch-uar dhe override-i bëhet i tepërt.
Ky pin NUK është pretendim "zero HIGH në repo" — mbulon vetëm këtë advisory specifik.

## Rollback

Hiqet target bootstrap pa prekur legacy oracle: fshi `global.json`, `.config/dotnet-tools.json`,
`src/platform/`, `tests/platform/`, `apps/backoffice/`, `packages/platform-api-client/`,
`contracts/`, hapat "Target stack ADR-024" në `ci.yml`, script-et `platform:*`/`contracts:*`/
`backoffice:*`/`verify:all` në `package.json` dhe rreshtat përkatës në `.gitignore`/`pnpm-lock.yaml`.
Legacy TS (apps/api, apps/web, packages/*) nuk preket nga rollback-u.
