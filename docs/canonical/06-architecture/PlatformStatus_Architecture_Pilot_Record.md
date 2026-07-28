# PlatformStatus Architecture Pilot — Implementation Record (F1 Increment A)

**Statusi:** **IMPLEMENTED / PENDING_INDEPENDENT_REVIEW** — *Increment A është IMPLEMENTED në branch, por NOT COMPLETE / NOT MERGED*; PASS vetëm pas Fable delta-review → Codex review (PM_REVIEW_PROTOCOL v1.8) → Human Gate → merge i aprovuar.
**Autoriteti:** ADR-029 (ACCEPTED / EFFECTIVE, PR #26 `305aefd`) + `Application_Architecture_Convention.md` (subordinate). **Data:** 28.07.2026 (v1). **Increment:** A — pilot SCHEMA-FREE; zero Identity/OpenIddict, zero migrim/schema/DB change, zero prekje semantike e AccessControl, zero module biznesi.

## Çfarë ËSHTË implementuar (vetëm kjo)

- **Vertical query slice:** `Modules/PlatformStatus/Application/Features/GetPlatformStatus/` — `GetPlatformStatusQuery : IQuery<PlatformStatusReport>` + `GetPlatformStatusHandler : IQueryHandler<...>` mbi portin EKZISTUES `IDatabaseReadinessProbe`; semantika e readiness probe IDENTIKE; `PlatformStatusReport` mbetet rezultati i Application (zero response i dytë artificial; zero command për query read-only; zero validator ceremonial).
- **Thin MVC Controller:** `Presentation/Controllers/PlatformStatusController` — `[ApiController]`; route EKZAKTE `GET /api/platform/status`; `[EndpointName("GetPlatformStatus")]`; summary identike; `[Tags("Idata.Platform.Api")]` + `[Produces("application/json")]` për byte-identitet; konstruktor VETËM `IMediator`; veprimi = binding → `Send(query)` → `PlatformStatusResponse.From(...)` → 200.
- **`BaseApiController` MODULE-LOCAL** (PlatformStatus/Presentation/Controllers): `ControllerBase` + `protected IMediator` — ASGJË tjetër (zero service locator, zero ProblemDetails helper i papërdorur, zero authorization/persistence/transaction).
- **Minimal API i statusit i HEQUR:** `PlatformStatusEndpoints.cs` + `MapPlatformStatus` nuk ekzistojnë më (provuar me architecture test); `AddControllers()`/`MapControllers()` në host — `MapControllers` JASHTË çdo environment branch. AccessControl minimal endpoints TË PAPREKURA (migrimi i tyre = Increment C).
- **Scalar + runtime OpenAPI vetëm në Development** (shih kufirin kritik më poshtë).

## Paketat — pin + provenance

| Paketa | Versioni | Licenca | Vendosja | Burimi |
|---|---|---|---|---|
| Mediator.Abstractions | **3.0.2** (pin ekzakt, CPM) | MIT | VETËM PlatformStatus Application (query/handler) + Presentation (IMediator) | nuget.org/packages/Mediator.Abstractions/3.0.2 · github.com/martinothamar/Mediator |
| Mediator.SourceGenerator | **3.0.2** | MIT | VETËM `Idata.Platform.Api.csproj`; `PrivateAssets=all`; `IncludeAssets=runtime; build; native; contentfiles; analyzers` | nuget.org/packages/Mediator.SourceGenerator/3.0.2 |
| Scalar.AspNetCore | **2.16.16** | MIT | VETËM API host | nuget.org/packages/Scalar.AspNetCore/2.16.16 |

Zero wildcard; lockfiles të rifreskuara deterministikisht (`--force-evaluate`). Tri lockfile-delta jashtë modulit të pilotit (heqje e metadata-s fantazmë të trashëguar) mbulohen nga **`HG-PR27-LOCKFILE-SCOPE-WAIVER`** (shih seksionin përkatës). **Zero MediatR/LuckyPennySoftware · zero AutoMapper · zero Swagger/Swashbuckle · zero pipeline behaviors** (të provuara me architecture tests).

## Pse Mediator lifetime = SCOPED

Handler-at konsumojnë porte/DbContext me scope kërkese (konventa e ADR-029 §6); default-i i paketës është Singleton — konfigurohet EKSPLICIT `options.ServiceLifetime = ServiceLifetime.Scoped` + assembly scope me `GetPlatformStatusQuery`. **Prova mekanike:** integration test (same-scope `Same`, cross-scope `NotSame` — as Singleton, as Transient) + dispatch real përmes mediator-it të gjeneruar.

## Mapping: minimal API i vjetër → query/controller i ri

| E vjetra | E reja |
|---|---|
| `PlatformStatusEndpoints.MapPlatformStatus()` (MapGet + TypedResults.Ok) | `PlatformStatusController.GetAsync` (thin action) |
| `AddScoped<GetPlatformStatusHandler>()` direkt në DI | zgjidhje përmes mediator-it SCOPED (`AddMediator`) |
| `Application/GetPlatformStatusHandler.HandleAsync(ct)` | `Features/GetPlatformStatus/GetPlatformStatusHandler.Handle(query, ct)` (IQueryHandler) |
| Kontrata HTTP/OpenAPI | **IDENTIKE** — route/operationId/summary/tag/schema/200 |

## KUFIRI KRITIK: AddOpenApi i PAKUSHTËZUAR

`builder.Services.AddOpenApi("platform-v1", ...)` + operation transformer-i ekzistues (`IdempotencyKeyRequiredTransformer`) + document transformer-i i ri (`CanonicalContractOrderTransformer`) janë **JASHTË çdo environment branch** — gjenerimi build-time (`OpenApiGenerateDocuments` + `CopyPlatformOpenApiContract` në `Api.csproj`) i konsumon këto shërbime. **VETËM** `app.MapOpenApi(...)` dhe `app.MapScalarApiReference(...)` guardohen me `app.Environment.IsDevelopment()`. Matrica runtime (e testuar): Development → openapi 200 / scalar 200 / status 200; Staging, Production, environment i panjohur → openapi 404 / scalar 404 / status 200 (fail-closed).

## Byte-identiteti i kontratës

Kalimi në MVC solli dy devijime që u MBYLLËN pa ndryshim semantik: (a) MVC reklamonte `text/plain`+`text/json` shtesë → `[Produces("application/json")]`; (b) paths/tags zhvendoseshin në fund → `CanonicalContractOrderTransformer` (renditje STABILE, deterministe: tag-u i host-it para `AccessControlEndpoints`). **Evidence:** `contracts/openapi/platform-v1.json` + klienti TS i gjeneruar = **ZERO DIFF kundër origin/main**; gjenerim ×2 + nën `DOTNET_ENVIRONMENT=Production` (shih PR body për komandat).

## Kontrata JSON — kufiri i regjistruar (§9 i mandatit)

- Sot ka **ZERO konfigurim JSON eksplicit** në host.
- MVC përdor `Microsoft.AspNetCore.Mvc.JsonOptions`; minimal API përdor `Microsoft.AspNetCore.Http.Json.JsonOptions` — **dy option objects TË NDARA**. Çdo konfigurim i ardhshëm duhet aplikuar KONSISTENT në të dyja, OSE kontrata mbrohet me të njëjtat raw integration tests.
- Kontrata mbrohet me teste **RAW exact-name** (READY + UNAVAILABLE): set ekzakt top-level `{apiVersion, capabilities, database, status}`, nested `{audit, identity, observability, outbox}`, kinds + vlera ekzakte, zero property shtesë, zero PascalCase. `[JsonPropertyName]` NUK u përdor — default-et + testet e mbyllin kontratën; lejohet si hardening i ardhshëm vetëm me OpenAPI/TS zero-diff.

## BaseApiController — Increment C promotion gate

Në Increment A baza mbetet **module-local** (një konsumator real). **Increment C sjell potencialisht konsumatorin e dytë → kjo aktivizon REVIEW, JO promovim automatik.** Promovimi bëhet vetëm nëse provohet sjellje identike, e ripërdorshme nga ≥2 module reale; vetëm një property `IMediator` NUK mjafton për inheritance ceremonial; është e vlefshme që çdo modul të mbajë controller-in e vet pa shared base. Nëse promovohet: abstraksion Presentation NEUTRAL i emërtuar qartë + dependency graph i përditësuar + architecture tests + review + Human Gate. **AccessControl NUK lejohet KURRË të referojë PlatformStatus.Presentation** (guard ekzistues). Zero `Common`/`Utils`/shared `BuildingBlocks`/BaseApiController në API host tani.

## HG-PR27-LOCKFILE-SCOPE-WAIVER (APROVUAR nga Pronari)

- **Waiver ID:** `HG-PR27-LOCKFILE-SCOPE-WAIVER` — **aprovuar nga Pronari**.
- Lejon VETËM tri lockfile-delta EKZISTUESE të PR #27: `src/platform/Idata.Platform.Persistence/packages.lock.json` · `src/platform/Idata.Platform.DbMigrator/packages.lock.json` · `src/platform/Modules/AccessControl/Infrastructure/packages.lock.json`.
- Delta u krijua kur `restore --force-evaluate` hoqi **metadata fantazmë të trashëguar** (`idata.platform.modules.accesscontrol.domain` — mbetje e grafit të vjetër para neutralizimit të Persistence në F1.1). **E provuar mekanikisht:** diff-i i tri file-ve = VETËM heqje të asaj metadata-je (zero shtim); **zero ndryshim `.csproj`, kod, skemë ose varësi reale**; `dotnet restore --locked-mode` kalon.
- **REVERT-i NDALOHET** — do të rikthente metadata të pasaktë.
- Formulimi i saktë: *"Zero ndryshim semantik në Persistence/DbMigrator/AccessControl.Infrastructure; vetëm tri lockfile-delta të autorizuara nga waiver-i i Pronarit."* (Pretendime si "të paprekura"/"zero lockfile changes" janë të PASAKTA dhe nuk përdoren.)
- **Kufi i njohur (JO defekt i provuar i CI-së):** locked restore kaloi, por nuk përdoret si provë universale që çdo metadata e vjetruar është pastruar. Vlerësimi i një force-evaluate drift-check deterministik mbetet **follow-up OPEN** para ndryshimit të ardhshëm të package graph-it; **Owner: Tech/Platform**.

## Incidenti Docker (vetëm faktet e provuara)

1. Tentativa `open -a Docker` priti **7 minuta** dhe përfundoi me **exit 143** (timeout).
2. Gjatë asaj tentative **nuk u pretendua asnjë PASS** i testeve DB-dependent.
3. Rikuperimi u bë me **`docker desktop start`**.
4. Container-i PostgreSQL sintetik (host port **5455**) u verifikua **ready** (`pg_isready`).
5. **Vetëm pas rikuperimit** u ekzekutua suita e plotë.
6. Rezultati lokal: **219/219, zero skip** (rikonfirmuar edhe nga correction pass-i).
7. **CI mbi të njëjtin HEAD** e konfirmoi suitën (build-test SUCCESS).

## Deviations të deklaruara

1. **Commit i kuq `6efe01c`:** `git add` abortoi në pathspec-in e parë (file i fshirë tashmë i staged me `git rm`) pa shtuar pjesën tjetër — commit-i kapi VETËM 2 fshirjet dhe s'buildohet i izoluar. Amend/rebase të ndaluara nga mandati → kompletim FORWARD-ONLY me `48e16bf`; `6efe01c + 48e16bf` = një njësi logjike.
2. **Bashkimi slice+gating në `48e16bf`:** `Program.cs` mbarte të dy ndryshimet në një file; ndarja pas kompletimit do të kërkonte histori të rishkruar — e ndaluar. I dokumentuar këtu + PR body.
3. **Tri lockfile-t jashtë §12 fillestar** — të mbuluara nga `HG-PR27-LOCKFILE-SCOPE-WAIVER` (më lart).

## Rollback

Hiqet piloti pa prekur asgjë tjetër: rikthe `Program.cs` (AddScoped handler + MapPlatformStatus + MapOpenApi i pakushtëzuar), rikthe `PlatformStatusEndpoints.cs` + handler-in e vjetër, fshi `Features/GetPlatformStatus/`, `Controllers/`, `CanonicalContractOrderTransformer.cs`, hyrjet Mediator/Scalar nga props/csproj/lockfiles, testet e pilotit (`PlatformStatusPilotTests`, `ConventionEnforcementTests`) dhe këtë record; kontrata/klienti mbeten të pandryshuara (ishin byte-identike). AccessControl/Persistence/DbMigrator/migrimet e paprekura nga rollback-u.

## Test evidence (recount në PR body nga output real)

Unit (handler READY/UNAVAILABLE/capabilities + kontrata e përgjigjes — përshtatje mekanike) · Integration pilot 11 (raw JSON ×2 · environment matrix ×4 · route table ×3 · dispatch ×1 · Scoped ×1) · Architecture +8 (vendosja e paketave, zero MediatR/behaviors, thin controller, base module-local, minimal-API i hequr, zero Common/Utils) · guard-et ekzistuese identity/cross-module të PANDRYSHUARA. Test counts totale rillogariten nga zero në verifikimin final (PR body).
