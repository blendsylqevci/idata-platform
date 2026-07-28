# Identity Store Foundation — Implementation Record (F1 Increment B.1)

**Statusi:** **IMPLEMENTED / PENDING_INDEPENDENT_REVIEW** — *B.1 është IMPLEMENTED në branch (PR #28), por NOT MERGED*; **Increment B = ACTIVE / NOT COMPLETE** (B.1 = vetëm store foundation; **B.2 authority/OIDC flows/credentials/token validation dhe B.3 projection/freshness/reconciliation = NOT STARTED**). PASS vetëm pas Fable delta-review → Codex review (PM_REVIEW_PROTOCOL v1.9) → Human Gate → merge i aprovuar.
**Autoriteti:** ADR-028 (ACCEPTED / EFFECTIVE, PR #26 `305aefd`) + `Identity_Authority_Store_Decision.md` (subordinate; §3 schema map, §6 migrimi, §7 architecture-test follow-up, §8 rollback, §9 acceptance) + ADR-029/`Application_Architecture_Convention.md`. **Data:** 28.07.2026 (v1) · **corrected 29.07.2026** (correction pass i drejtuar nga Pronari mbi PR #28 — UUID claim key ABSOLUT, boundary reconciliation, inventory/allowlist hardening, Reliability classification; pa v2 artificial). **Increment:** B.1 — VETËM storage foundation; zero endpoint/protokoll OIDC, zero token issuance/validation, zero credentials/certificates, zero Identity roles, zero projection drejt `platform.app_user`, zero prekje semantike e AccessControl, **zero production readiness**.

## Çfarë ËSHTË implementuar (vetëm kjo)

- **Konteksti ROLELESS 5-generic:** `PlatformDbContext : IdentityUserContext<UserAccountRecord, Guid, UserClaimRecord, IdentityUserLogin<Guid>, IdentityUserToken<Guid>>` (JO `IdentityDbContext` me role); `base.OnModelCreating` thirret **I PARI**, remap-et pas tij (provuar me model test — max-length/indekset e bazës mbijetojnë remap-in).
- **Tipet e store-it (Persistence = storage mapping owner):** `UserAccountRecord : IdentityUser<Guid>` (zero property të shpikura) + **`UserClaimRecord : IdentityUserClaim<Guid>`** me property të RE `ClaimId:Guid` (i gjeneruar në aplikacion, kurrë bosh) — **ZERO persisted int IDs ABSOLUT (vendim i Pronarit, correction pass):** inherited `Id:int` i framework-ut **INJOROHET nga EF** (`Ignore`) dhe NUK persiston; çelësi i VETËM i persistuar = `ClaimId` → kolona kanonike `id` uuid (`ValueGeneratedNever`).
- **OpenIddict Core/EF store VETËM:** `AddOpenIddict().AddCore(...UseEntityFrameworkCore().UseDbContext<PlatformDbContext>().ReplaceDefaultEntities<Guid>())`; entitetet EF regjistrohen me `modelBuilder.UseOpenIddict<Guid>()` në `OnModelCreating` (ekuivalenti i dokumentuar i thirrjes options-level — Deviations #1).
- **Moduli:** `Modules/Identity/Infrastructure` me `AddIdentityStoreFoundation()` (`AddIdentityCore` roleless + `AddEntityFrameworkStores` → **UserOnlyStore me `UserClaimRecord`** — inferenca e provuar me test DI mbi tipin real; zero `AddDefaultIdentity`/`AddRoles`/`RoleManager`/scheme/cookies). Domain/Application NUK krijohen bosh (Deviations #2).
- **API host (composition root):** vetëm `builder.Services.AddIdentityStoreFoundation();` — **ZERO referencë direkte assembly/package identity** (e testuar; API konsumon store-in vetëm përmes project reference te Identity.Infrastructure) — **route-table e PANDRYSHUAR** (test), **OpenAPI/TS klienti BYTE-IDENTIKE**.
- **Kufiri `Users` ↔ `PlatformAppUsers`:** DbSet-i i `platform.app_user` u riemërtua **`PlatformAppUsers`** (zero ndryshim tabele/kolone/semantike) — inherited `Users` i takon VETËM Identity store-it (`identity.user_account`); guard-test diskriminues ndalon `context.Users` në kodin e AccessControl.
- **Migrimi `0003_IdentityStoreFoundation`** — forward-only, **IDENTITY-ONLY**, i RIGJENERUAR nga modeli final në correction pass (shih evidencën më poshtë).

## Paketat — pin + provenance + inventar transitiv FAIL-CLOSED

| Paketa | Versioni | Licenca | Lloji | Vendosja |
|---|---|---|---|---|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | **10.0.10** | MIT | **Direct** | VETËM Persistence + Modules/Identity/Infrastructure |
| OpenIddict.EntityFrameworkCore | **7.6.0** | Apache-2.0 | **Direct** | VETËM Persistence + Modules/Identity/Infrastructure |
| Microsoft.Extensions.Identity.Core / .Stores | 10.0.10 | MIT | Transitive (nga Identity.EFCore) | — |
| Microsoft.IdentityModel.Abstractions / .Logging / .Tokens | 8.19.2 | MIT | Transitive (nga Identity.EFCore→Extensions.Identity) | — |
| Microsoft.Bcl.Cryptography | 10.0.2 | MIT | Transitive | — |
| OpenIddict.Abstractions / .Core / .EntityFrameworkCore.Models | 7.6.0 | Apache-2.0 | Transitive (nga OpenIddict.EFCore) | — |

Inventari imponohet me **test FAIL-CLOSED** mbi `packages.lock.json` të dy direct owners: çdo paketë identity/security e RE ose version i panjohur = RED (jo allowlist automatik); `Direct` lejohet VETËM për dy paketat store; **API host-i NUK është direct owner** (test i dedikuar — zero referencë direkte identity; hiqet nga allowlist derisa B.2 ta autorizojë veçmas). **ZERO** OpenIddict.AspNetCore/Server/Validation/Client, ZERO Duende/IdentityServer/Auth0/Okta, ZERO Identity UI, ZERO wildcard.

## Schema / table map (REALE, e provuar në PostgreSQL)

| Tabela | Tipi CLR real | Çelësi |
|---|---|---|
| `identity.user_account` | `UserAccountRecord : IdentityUser<Guid>` | `id` uuid (gjenerim on-add) |
| `identity.user_login` | `IdentityUserLogin<Guid>` | PK i përbërë EKZAKT (`login_provider`, `provider_key`) — i PANDRYSHUAR (§4.5); `user_id` uuid |
| `identity.user_claim` | **`UserClaimRecord : IdentityUserClaim<Guid>`** | **`id` uuid = `ClaimId` (app-generated, non-empty)**; inherited `Id:int` = i INJORUAR nga EF, NUK persiston; `user_id` uuid |
| `identity.user_token` | `IdentityUserToken<Guid>` | PK i përbërë EKZAKT (`user_id` uuid, `login_provider`, `name`) — i PANDRYSHUAR (§4.5) |
| `identity.oidc_application` | `OpenIddictEntityFrameworkCoreApplication<Guid>` | `id` uuid |
| `identity.oidc_authorization` | `OpenIddictEntityFrameworkCoreAuthorization<Guid>` | `id` uuid |
| `identity.oidc_scope` | `OpenIddictEntityFrameworkCoreScope<Guid>` | `id` uuid |
| `identity.oidc_token` | `OpenIddictEntityFrameworkCoreToken<Guid>` | `id` uuid |

**ZERO persisted int IDs — ABSOLUT** (provuar në model + `information_schema`: çdo kolonë `id` = uuid; zero kolonë identifikuese `id`/`*_id` me tip int në schema `identity`). Kolonat = emrat REALË Identity/OpenIddict → snake_case mekanik; zero `AspNet*`; zero tabela rolesh; pronësi e NDARË `user_token` ≠ `oidc_token`; **zero FK cross-schema** (model + `pg_constraint`); zero token/credential material në `platform`; schema `platform` = e PANDRYSHUAR (lista ekzakte e provuar).

## Migrimi 0003 — evidencë IDENTITY-ONLY + integriteti i gjenerimit (§5.1)

- **I RIGJENERUAR në correction pass** nga modeli final me EF tooling (`dotnet ef migrations add`, startup = DbMigrator); output-i RAW u ruajt jashtë git (scratchpad) si evidencë.
- **Rregullime të dokumentuara mbi RAW (të vetmet):** (a) riemërtim file/ID → `0003_IdentityStoreFoundation` (konventa ekzistuese e 0001/0002); (b) 5 rreshta kolonash (`client_secret` ×1, `concurrency_token` ×4) → parametri `name:` i EF-it për të shmangur false-positive të guard-it të privatësisë mbi DDL pa vlera (guard-i NUK dobësohet). **Prova e paritetit semantik:** `dotnet ef migrations script` nga RAW dhe nga FINAL → **BYTE-IDENTIK (sha256 i njëjtë)**.
- `Up()` = `EnsureSchema("identity")` + **8 CreateTable** + **10 CreateIndex** — TË GJITHA në schema `identity`; **ZERO operacion mbi `platform.*`** — provuar në 4 nivele: operacionet (test i përhershëm mbi Up/DownOperations), file C#, SQL i gjeneruar (seksioni 0003 = 148 rreshta; zero `platform.*` jashtë history-insert; zero ALTER/DROP/DML), schema/data reale para/pas (upgrade {0001,0002}→vetëm 0003 me data sintetike të ruajtura).
- **0001/0002 + Designer-at = BYTE-IDENTIKE** me origin/main (`git diff` = 0). **`PlatformDbContextModelSnapshot.cs` = +469/−0 kundër origin/main** — vetëm shtim entitetesh; zero hand-edit; **zero migrim 0004**.
- `Down()` = heqje VETËM e 8 tabelave identity nën guard-in tripalësh (DB disposable) — NUK është rollback production; schema bosh `identity` mbetet pas Down (EF s'gjeneron DropSchema — e dokumentuar). Migration owner i vetëm = Persistence/PlatformDbContext; zero histori paralele.

## Test matrix (rezultatet reale — 261/261 GREEN, zero skip)

- **Architecture (38):** drejtimet e moduleve; zero cross-module; zëvendësimi §7 i ADR-028 me allowlist EKZAKT (direct owners = VETËM Persistence + Identity.Infrastructure; **API zero-direct e testuar**); inventari identity/security FAIL-CLOSED mbi lockfiles; **zero Identity roles me traversal REKURSIV cycle-safe** (base/interfaces/fields/properties/ctors/method params+returns/events/generic args/constraints/attributes/nested; publike+jo-publike) + **12 fixtures diskriminuese** (heqja e një dege → meta-test RED) + prova zero-false-positive; guard-i `Users`↔`PlatformAppUsers` + prova diskriminuese e detektorit; vendosja csproj e paketave; zero paketa protokolli.
- **Unit/model (104):** kontekst roleless 5-generic; claim entity = `UserClaimRecord` me PK të VETËM `ClaimId:Guid` uuid → kolona `id`, `ValueGeneratedNever`; **inherited `Id:int` ABSENT nga modeli EF**; çelësat kompozitë EKZAKTË të `user_login`/`user_token` (§4.5); zero int PK askund në identity; 8 tabelat me tipet reale; platform i pandryshuar; snake_case; base-first; pronësia e token-ave; zero FK cross-schema; operacionet 0003 identity-only; DI: UserOnlyStore me `UserClaimRecord`, zero role services, menaxherët OpenIddict me Guid.
- **Integration (119, PostgreSQL real sintetik):** `identity.user_claim.id` = **uuid** + zero kolonë identifikuese int; **cikli i PLOTË i claims përmes UserManager/UserOnlyStore REAL** — AddClaim ×2 · GetClaims · ReplaceClaim · RemoveClaim · GetUsersForClaim, `ClaimId` non-empty + unikë të provuar në DB, me **fresh DI scope/fresh DbContext/re-resolution** midis fazave; UserManager/user round-trips + uniqueness reale; OpenIddict application/scope round-trips me Guid; forma reale e schema-s; route-table pa endpoint identity/OIDC; provat {0001,0002}→vetëm 0003, rollback i guard-uar 0003→0002, reapply, no-op, atomicitet; gjithë suita F1.1/Increment A GREEN.
- **RED→GREEN (§12.1):** para fix-it u provuan RED me teste reale: `user_claim.id` int (schema reale) · claim type default (model) · store me default claim type (DI) · zero-int-IDs (model) · 8-tabelat me tip default (model); degët e traversal-it të roleve dhe detektori `.Users` provohen me fixtures diskriminuese (meta-teste që skuqen po u hoq dega).

## Reliability Gate — klasifikimi formal i B.1 (§5.1 i Gate-it; asnjë PASS pa test të ekzekutuar)

B.1 = storage foundation pa surface runtime authority; klasifikimi (OPEN/PARTIAL/N/A nuk numërohen PASS; zero production readiness):

| # | Skenari | Statusi për B.1 | Evidencë / justifikim | Owner · Afati |
|---|---|---|---|---|
| 1 | Internet down | **OPEN → B.2** | B.1 s'ka flows/authority runtime; sjellja offline e autorizimit vendoset me B.2/B.3 (freshness fail-closed §2.1) | Tech · para Increment B COMPLETE |
| 2 | SEF/ATK down | **N/A (propozuar — kërkon pranim PM #7)** | Mbulohet nga moduli fiskal/outbox (Step B, Gate §5.2) — identity store s'ka surface fiskale | Tech · pranimi në review |
| 3 | Printer/peripheral down | **N/A (propozuar — kërkon pranim PM #7)** | Mbulohet nga POS/Device Agent (ADR-024/027) — zero peripheral surface në B.1 | Tech · pranimi në review |
| 4 | Device crash/offline | **N/A (propozuar — kërkon pranim PM #7)** | Mbulohet nga shtresa POS/mobile/sync (PoC-3/ADR-011) — zero device surface në B.1 | Tech · pranimi në review |
| 5 | Sync interrupted/retry/idempotency | **OPEN → B.3** | Projection identity→platform me delivery idempotente/reconciliation = B.3 (§2.1 i Identity doc); B.1 s'ka sync surface | Tech · para Increment B COMPLETE |
| 6 | Server/hub restart | **PARTIAL** | E provuar: store re-resolution në scope/context të freskët + reapply/no-op i migrimeve pas down; E PAMBULUAR: restart me radhë jo-bosh të authority-t runtime (B.2) | Tech · para Increment B COMPLETE |
| 7 | DB unavailable | **PARTIAL** | E provuar: probe fail-safe i statusit (DEGRADED/UNAVAILABLE, zero detaje) + guard-et fail-closed të migratorit; E PAMBULUAR: DENY fail-closed i autorizimit me identity store të paarritshëm (B.2/B.3) | Tech · para Increment B COMPLETE |
| 8 | Disk/queue full | **OPEN** | NUK provohet nga B.1 — zero pretendim për disk-full real, alerting apo recovery | Tech · para F1 COMPLETE / production Go |
| — | Migration/store lifecycle (specifik i B.1) | **PROVUAR në branch** | fresh/no-op/upgrade/guarded-down/reapply/atomicitet + round-trips reale store (mbyllja e rreshtit = review + merge) | Tech · review i PR #28 |

## Rollback i B.1 (kod)

Hiqen: dy paketat nga props/csproj-et e aprovuara; `UserAccountRecord`/`UserClaimRecord`/derivimi 5-generic/remap-et/`UseOpenIddict` nga Persistence; `Modules/Identity/`; wiring nga Program.cs + slnx; riemërtimi `PlatformAppUsers` kthehet mekanikisht; testet e B.1; ky Record — dhe rikthehet forma provider-neutral e architecture test-it. **Migrimi 0003 në env deployed/data-bearing NUK rollback-ohet me Down** — vetëm forward-only / restore i provuar (kontrata §8 e Identity doc); Down i guard-uar = vegël testesh mbi DB disposable. 0001/0002/platform/kontratat/klientët = të paprekura.

## Deviations të deklaruara (gjendja pas correction pass-it)

1. **`UseOpenIddict<Guid>()` në nivel ModelBuilder** (OnModelCreating) — ekuivalenti i dokumentuar i options-level; model IDENTIK në çdo vend ku ndërtohen options + renditje deterministe me remap-et.
2. **Vetëm `Modules/Identity/Infrastructure`** — Domain/Application NUK krijohen bosh (asnjë koncept real B.1; vijnë me B.2/B.3) sipas ADR-029 §14.
3. **[E HEQUR në correction pass]** ish-"përjashtimi" `user_claim.id` int surrogate — **NUK aprovohet nga Pronari**; zëvendësuar me `UserClaimRecord`/`ClaimId` UUID (zero persisted int IDs ABSOLUT). Inherited `Id:int` ekziston vetëm në CLR (framework), i injoruar nga EF, kurrë i persistuar.
4. **Rreshtat `client_secret`/`concurrency_token` në 0003.cs me `name:`** — rregullim i dokumentuar i §5.1 me paritet SQL RAW↔FINAL të provuar byte-identik (sha256); guard-i i padobësuar.
5. **`.gitleaksignore` me NJË fingerprint commit-scoped = RATIFIKUAR nga Pronari** (correction pass i PR #28) — vetëm hyrja ekzistuese; zero wildcard; zero ndryshim `.gitleaks.toml` (byte-identik me origin/main); çdo hyrje e ardhshme = Human Gate i veçantë. **Follow-up i regjistruar:** pas squash-merge (kur commit-i historik s'është pjesë e main-it) hyrja shqyrtohet për heqje në PR të veçantë — pa rishkrim historie. Nëse `idata-client-pii` jep false-positive tjetër mbi GUID sintetik → follow-up i detektorit, JO zgjerim suppression-i.
6. **Përshtatjet mekanike të testeve HISTORIKE** të migrimit (FLAMUR — pending counts nga seti 3-migrimesh; aplikim i shënjestruar i 0002; `GetCreateIfNotExistsScript`).
7. **`dotnet ef migrations add/script` me `--startup-project DbMigrator`** (Persistence pa Design package by design); riemërtim në `0003_*` sipas konventës.
8. **Boundary i rekonsiliuar i Persistence** (i drejtuar nga Pronari): technical EF adapter + migration owner; njeh VETËM store mapping-un e ADR-028; zero Domain/Application/protocol/token/credentials — Convention §2/§3 i përditësuar; JO "provider exception" blanket.

## Open gates / kufij (ASNJË i mbyllur nga B.1)

Acceptance matrix §9 e Identity doc (klasifikim per-rresht, default OPEN): **E1 = IMPLEMENTED në branch (PR #28) / PENDING_INDEPENDENT_REVIEW**; **E4 = PROVUAR në branch / PENDING_INDEPENDENT_REVIEW** (baseline i plotë GREEN + zero Identity roles strukturalisht); **E5 = PARTIAL** (architecture/inventar/gitleaks të provuara; keys/audit = B.2/B.3); **A1–A3, B1–B3, C1–C2, D1, E2–E3 = OPEN / NOT IMPLEMENTED (B.2/B.3)**. ENG-005 = OPEN i pandryshuar (B.1 s'shton writer mbi faktet e F1.1 dhe asnjë writer identity-lifecycle). ADR-025/026/027 OPEN; ENG-004 fallback AKTIV; Step B/C, PREFLIGHT, Tier 2 = NO-GO; **zero pretendim "Identity complete"/"OIDC ready"/production readiness; zero account lifecycle authority end-to-end; zero tenant authorization të integruar me Identity**.
