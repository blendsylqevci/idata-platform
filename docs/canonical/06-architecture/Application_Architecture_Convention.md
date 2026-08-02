# Application Architecture Convention (iData+ platform)

**Statusi:** **EFFECTIVE — ADR-029 ACCEPTED (PR #26 merged, squash `305aefd`, 28.07.2026, pas Fable→Codex→Human Gate). INCREMENT A = COMPLETED/MERGED (PR #27, squash `abc2b291`, 28.07.2026); HAPI AKTUAL = INCREMENT B (ACTIVE — B.1 store foundation CURRENT; B.2/B.3 NOT STARTED).**
**Autoriteti:** ky dokument është **specifikimi normativ i detajuar, SUBORDINATE ndaj ADR-029** (`docs/canonical/01-phase0/05_Decision_Gates_ADR.md`). ADR-029 mban vendimin; ky dokument mban detajet. Në konflikt fiton ADR-029.
**Data:** 27.07.2026 (v1; **corrected pre-merge 27.07.2026 · 28.07.2026** — correction passes të review-t të pavarur mbi PR #26, para merge-it; pa v2 artificial) · **Pronari:** Tech/Platform Owner + PM · **Kufiri:** ASNJË rresht i këtij dokumenti nuk është i implementuar nga PR-ja që e krijon — zero kod, zero dependency, zero migrim.

---

## 1. Layout i projekteve dhe folderëve

```
src/platform/
  Idata.Platform.Api/                  # host = composition root; ZERO business logic
  Idata.Platform.Persistence/          # DbContext/mappings/migrations; migration owner i VETËM
  Idata.Platform.DbMigrator/           # aplikuesi i vetëm i migrimeve (ekzistues)
  Modules/<Module>/
    Domain/                            # entities, aggregates, value objects, domain rules/events
    Application/
      Features/<UseCase>/              # vertical slice per use-case
    Infrastructure/                    # adapters/providers/repositories
    Presentation/
      Controllers/                     # thin controllers (target pas Increment A)
tests/platform/                        # Unit · Integration · Architecture
```

Modulet ekzistuese (`PlatformStatus`, `AccessControl`) migrojnë në këtë formë GRADUALISHT sipas incrementeve A/C — jo big-bang.

## 2. Drejtimet e varësisë (të provuara me ArchitectureTests mbi metadata reale)

Grafiku CANONICAL (i provuar nga `DependencyDirectionTests` mbi metadata reale, direkt DHE transitive):

- `Domain → zero layer tjetër i platformës`;
- `Application → vetëm Domain-i i modulit të vet` (+ abstraksione të aprovuara);
- `Infrastructure → Application + Domain të modulit + Persistence (neutral/shared)`;
- `Presentation → Application + Domain të modulit`;
- **`Persistence` = technical EF adapter + migration owner i VETËM** (formulim i rekonsiliuar në correction pass të PR #28, i drejtuar nga Pronari): **zero varësi direkte ose transitive ndaj Domain/Application të asnjë moduli** dhe zero logjikë biznesi; storage records POCO + mapping records↔domain në Infrastructure të modulit; **LEJOHET të njohë VETËM mapping/store packages të miratuara nga ADR-028** (ASP.NET Core Identity EF + OpenIddict EF — store mapping, pa OIDC protocol behavior, pa endpoint, pa token issuance/validation, pa credential lifecycle). Ky NUK është "provider exception" blanket — përjashtimi vlen vetëm për store mapping-un EKZAKT të ADR-028; `Modules/Identity/Infrastructure` mban composition/wiring; API host = composition root **pa direct identity PackageReference** (të gjitha të imponuara me architecture tests);
- **`DbMigrator → Persistence` dhe vetëm unazat e brendshme realisht të nevojshme — JO blanket dependency ndaj Infrastructure** (referenca reale e vetme sot: `Idata.Platform.Persistence`);
- `Api = composition root` — i vetmi outer host që lidh komponentët;
- **Zero reachability cross-module, direkt ose transitiv** — komunikim vetëm përmes kontratave të aprovuara.

## 3. Përgjegjësitë per-layer

| Layer | Përmban | E NDALUAR |
|---|---|---|
| Domain | entities, aggregates, value objects, domain rules/events | EF, HTTP, Identity, çdo implementim mediator-i |
| Application | use-cases, ports, commands/queries, responses, handlers | varësi jashtë Domain + abstraksioneve të aprovuara |
| Infrastructure | implementime adapters/providers/repositories | business rules; transport concerns |
| Presentation | controllers, transport mapping, HTTP contracts | DbContext/UoW/repository, business logic, tenant resolution |
| Persistence | DbContext, mappings, migrations (owner i vetëm); store mapping i miratuar nga ADR-028 (Identity/OpenIddict EF) | varësi nga Domain/Application i moduleve; logjikë biznesi; protocol/server/validation/client behavior; token issuance/validation; credential lifecycle |
| API (host) | composition root, DI wiring, pipeline, environment gating | business logic; access i drejtpërdrejtë në DB |

## 4. Feature-slice template

Një use-case = një folder `Application/Features/<UseCase>/` me tipe **TË NDARA** që mund të bashkëjetojnë në një file të vogël feature-i:

```
Features/UpdateMembershipRole/
  UpdateMembershipRoleCommand.cs      # record: input i tipizuar
  UpdateMembershipRoleResponse.cs     # record: output publik
  UpdateMembershipRoleHandler.cs      # një handler; orkestron ports
  UpdateMembershipRoleValidator.cs    # validim strukturor i input-it
```

**E NDALUAR:** një mega-class me nested types dhe business logic të përzier; "handler" që bën edhe transport mapping; validator që bën authorization.

## 5. Rregulli i vendimit për CQRS selektiv

- **Command** kur ka mutacion, invariante, audit ose idempotency.
- **Query** kur ka read model / projeksion të dallueshëm nga shkrimi.
- **JO ceremoni** për CRUD të parëndësishëm: një endpoint i thjeshtë read-only pa invariante nuk detyrohet të ndajë modele artificialisht.
- Vendimi shënohet në PR kur nuk është i dukshëm.

## 6. Mediator (i aprovuar nga ADR-029)

- Paketa: **martinothamar/Mediator** (MIT) — **JO LuckyPennySoftware/MediatR** (alternativë e refuzuar; licencim/kosto dhe drejtim jo i pranueshëm për platformën).
- API e konvencionit: **`IMediator`** (surface i vetëm i lejuar në Presentation).
- **`Mediator.Abstractions`** referohet aty ku deklarohen messages/handlers (Application).
- **`Mediator.SourceGenerator`** referohet VETËM në executable/outermost host (`Idata.Platform.Api`) — jo në module.
- **Lifetime = `Scoped`** për pilotin, sepse handler-at konsumojnë DbContext/UoW me scope kërkese.
- Versioni i saktë **zgjidhet dhe pin-ohet VETËM në Increment A**, me provenance licence/package (Central Package Management, pin ekzakt, **zero wildcard**).

## 7. Controllers + BaseApiController

**Controller i hollë** bën VETËM: model binding → `IMediator.Send` → mapping i rezultatit në HTTP/ProblemDetails. **ZERO** DbContext/UoW/repository/business logic/tenant resolution në controller.

**`BaseApiController` minimal:** trashëgon `ControllerBase`; ekspozon `IMediator` dhe standardizim rezultatesh/ProblemDetails; **zero** service locator; **zero** authorization, persistence ose transaction logic.

## 8. Pipeline behaviors — allowlist

Lejohen VETËM për shqetësime realisht horizontale: **validation · telemetry · correlation · exception mapping**.

**E NDALUAR të fshihen në pipeline generic:** tenant authorization DB-backed · authority-lock protocol · transaction boundary · idempotency · audit atomicity · fiscal invariants. Këto mbeten pronësi EKSPLICITE e handler-it/UoW-së përkatëse (shih §9) — të dukshme, të testueshme, jo magji middleware-i.

## 9. Pronësia e transaction/authorization/idempotency/audit

- **Transaction boundary:** unit-of-work i use-case-it (Application port → Infrastructure adapter); commit-i i mutation+audit+receipt mbetet ATOMIK siç e provoi F1.1.
- **Tenant authorization:** autoriteti = faza transaksionale authority-lock/validation/mutation kundër PostgreSQL (F1.1 Record); asnjë layer transporti nuk e zëvendëson.
- **Idempotency:** `command_receipt` + fingerprint sipas kontratës së F1.1 (rilexim kushtor — shih F1.1 Record v6).
- **Audit:** append-only business-scoped, i lidhur me command receipt; shkruhet në të njëjtin transaction.
- Migrimi drejt Controllers/Mediator (Increment C) **NUK i lëviz** këto pronësi.

## 10. Scalar / OpenAPI / klienti i gjeneruar

- **OpenAPI mbetet kontrata autoritative** dhe burimi i vetëm i klientit TypeScript të gjeneruar (`contracts:generate` / `drift-check` të pandryshuara).
- **Kontrata target e Increment A:** runtime `MapOpenApi()` DHE `MapScalarApiReference()` regjistrohen **VETËM brenda së njëjtës degë EKZAKTE `app.Environment.IsDevelopment()`**; jashtë Development, **as runtime OpenAPI JSON endpoint-i as Scalar UI nuk ekspozohen** (Staging = absent; Production = absent; environment i panjohur/munguar = absent, FAIL-CLOSED); kërkohen **teste negative për Staging/Production**.
- Build-time OpenAPI generation + generated TypeScript client mbeten **të pavarura** nga runtime documentation endpoints (gjenerohen build-time si sot).
- Nëse në të ardhmen kërkohet internal non-development documentation UI → **ADR/gate i ri** me authentication, authorization dhe network controls — jo zgjerim i heshtur.
- **Devijimi i mëparshëm (MapOpenApi i pakushtëzuar në runtime) = CLOSED nga Increment A** — MERGED me PR #27 (squash `abc2b291`, 28.07.2026): `MapOpenApi` + `MapScalarApiReference` tani janë brenda degës ekzakte `app.Environment.IsDevelopment()`; **environment-matrix tests** e provojnë (Development: OpenAPI/Scalar të arritshme + status 200; Staging/Production/environment i panjohur: docs endpoints **404**, statusi mbetet i arritshëm 200); regression i automatizuar në CI provon kontratën byte-identike edhe nën `DOTNET_ENVIRONMENT=Production`.
- **Zero Swagger/Swashbuckle paralel** pa ADR të ri.

## 11. Rregulli i vendimit DDD

- **Modular boundaries përdoren në gjithë platformën — por një modul NUK shpallet automatikisht bounded context.** Bounded context kërkon **domain evidence**: ubiquitous language, model authority, invariante, ownership dhe boundary map; lidhja modul↔bounded-context dokumentohet me **context map/ADR kur provohet**.
- **Tactical DDD (aggregates/domain events/value objects të plota) VETËM ku ka invariante reale** (p.sh. fiscal, GL posting, settlement). CRUD-i i thjeshtë nuk mbështillet me aggregate ceremonial.
- Vendimi modular-monolith / module-first mbetet i PANDRYSHUAR.

## 12. Migration / versioning

- Migrimet **0001/0002 NUK rishkruhen**; çdo schema change = **0003+ forward-only**.
- EF Core mbetet migration owner i vetëm; SQL i audituar brenda migrimeve për constraints/triggers.
- **GUID/UUID mbetet standard**; zero ndryshim në int IDs. **Arsyetimi:** UUID mbështet krijim të decentralizuar/OFFLINE të records pa round-trip te serveri (POS/mobile/Edge), identitet global dhe correlation gjatë sync-ut. **KUJDES — pa overclaim:** UUID **vetëm** NUK garanton idempotency/dedup; ato kërkojnë ende command receipts, outbox, unique constraints, versioning dhe teste.

## 13. Testing pyramid + architecture tests

- Unit (Application/Domain, pa DB) → Integration (kundër PostgreSQL real sintetik, DB unike per test) → Architecture (metadata reale të assemblies).
- ArchitectureTests DUHET të mbulojnë: drejtimet e varësisë; ndalimin cross-module; ndalimin e paketave të paaprovuara; kufirin e SourceGenerator-it në host; kufirin e identity provider-ave (shih follow-up-in e ADR-028 — zëvendësim i kontrolluar, JO fshirje).
- Çdo increment mban baseline-in e testeve GREEN; teste të hequra/skip/dobësuara = REJECT.

## 14. Ndalime strukturore

- **Zero `Common`/`Utils` junk drawer.** `BuildingBlocks` krijohet VETËM për abstraksion të emërtuar mirë me **të paktën dy konsumatorë realë** — përndryshe kodi rri në modulin e vet.
- Zero referencë direkte cross-module (schema/tabela/DbContext i modulit tjetër).
- Zero identity provider konkret jashtë vendimit ADR-028 dhe projekteve të aprovuara prej tij.

## 15. Rendi i implementimit — Incrementet A/B/C/D (secili me PR, CI, review dhe Human Gate)

### Increment A — SCHEMA-FREE PILOT
Vetëm `PlatformStatus`; martinothamar/Mediator (pin + provenance); Controllers + `BaseApiController` minimal; **Scalar + runtime OpenAPI VETËM nën `IsDevelopment()` (§10) — përfshirë MBYLLJEN e devijimit aktual `Program.cs:35` (MapOpenApi i pakushtëzuar) me teste negative Staging/Production**; OpenAPI/generated-client parity; architecture tests të reja; **zero migrim DB; zero Identity; zero ndryshim semantik i status endpoint-it**.

### Increment B — IDENTITY FOUNDATION
ASP.NET Core Identity + OpenIddict; schema `identity`; migrimi **0003+**; OIDC flows + security tests; projection boundary me `platform.app_user`; **zëvendësimi i kontrolluar** i architecture test-it të provider-neutrality (shih ADR-028 follow-up); **pa Identity roles për tenant RBAC**.

### Increment C — ACCESSCONTROL MIGRATION
Migrim gradual i endpoint-eve të AccessControl te Controllers/Mediator; **ruhen** authority-lock protocol, transaction boundary, idempotency dhe audit. **Baseline i testeve:** pas PR #25 = 200 teste ekzistuese; **në fillim të Increment C baseline-i RILLOGARITET nga zero** (mund të jetë > 200). Të gjitha testet baseline duhet të kalojnë; përshtatje VETËM mekanike (namespace/type/transport); çdo ndryshim semantik i një testi = **FLAMUR + review**; çdo test i hequr/skip/dobësuar = **REJECT**; OpenAPI/client parity e detyrueshme.

### Increment D — MODULET E REJA
Çdo modul i ri përdor convention-in e aprovuar; domain-first për modele/invariante; migrime forward-only; **asnjë big-bang rewrite**.

## 16. Checklist për PR-të e ardhshme (konsumohet nga review)

1. Slice vertikal i plotë (domain→application→API→tests) ose arsyetim eksplicit.
2. Varësitë sipas §2; zero cross-module; zero paketa të paaprovuara.
3. Command/Query vetëm sipas §5; tipe të ndara sipas §4.
4. Controller i hollë; zero business logic në Presentation; `IMediator` i vetmi surface.
5. Pipeline behaviors vetëm nga allowlist §8; pronësitë e §9 të paprekura.
6. OpenAPI/client parity (generate ×2, drift zero); Scalar vetëm env-gated.
7. Migrime vetëm 0003+ forward-only; GUID/UUID.
8. Testet baseline GREEN; architecture tests të përditësuara me zëvendësim të kontrolluar, kurrë me fshirje të heshtur.
9. Statuset/counts në docs të rillogaritura nga zero; zero pretendim IMPLEMENTED për punë të pa-merge-uar.
