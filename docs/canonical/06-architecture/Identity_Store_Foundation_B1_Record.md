# Identity Store Foundation — Implementation Record (F1 Increment B.1)

**Statusi:** **IMPLEMENTED / PENDING_INDEPENDENT_REVIEW** — *B.1 është IMPLEMENTED në branch, por NOT MERGED*; **Increment B = ACTIVE / NOT COMPLETE** (B.1 = vetëm store foundation; **B.2 authority/OIDC flows/credentials/token validation dhe B.3 projection/freshness/reconciliation = NOT STARTED**). PASS vetëm pas Fable delta-review → Codex review (PM_REVIEW_PROTOCOL v1.9) → Human Gate → merge i aprovuar.
**Autoriteti:** ADR-028 (ACCEPTED / EFFECTIVE, PR #26 `305aefd`) + `Identity_Authority_Store_Decision.md` (subordinate; §3 schema map, §6 migrimi, §7 architecture-test follow-up, §8 rollback, §9 acceptance) + ADR-029/`Application_Architecture_Convention.md`. **Data:** 28.07.2026 (v1). **Increment:** B.1 — VETËM storage foundation; zero endpoint/protokoll OIDC, zero token issuance/validation, zero credentials/certificates, zero Identity roles, zero projection drejt `platform.app_user`, zero prekje semantike e AccessControl, **zero production readiness**.

## Çfarë ËSHTË implementuar (vetëm kjo)

- **Konteksti ROLELESS:** `PlatformDbContext : IdentityUserContext<UserAccountRecord, Guid>` (JO `IdentityDbContext` me role); `base.OnModelCreating` thirret **I PARI**, remap-et pas tij (provuar me model test — max-length/indekset e bazës mbijetojnë remap-in).
- **Tipi i llogarisë:** `UserAccountRecord : IdentityUser<Guid>` (Persistence = storage record + provider-specific mapping owner) — ZERO property të shpikura.
- **OpenIddict Core/EF store VETËM:** `AddOpenIddict().AddCore(...UseEntityFrameworkCore().UseDbContext<PlatformDbContext>().ReplaceDefaultEntities<Guid>())`; entitetet EF regjistrohen me `modelBuilder.UseOpenIddict<Guid>()` në `OnModelCreating` (ekuivalenti i dokumentuar i thirrjes options-level — shih Deviations #1).
- **Moduli:** `Modules/Identity/Infrastructure` me `AddIdentityStoreFoundation()` (`AddIdentityCore` roleless + `AddEntityFrameworkStores` → **UserOnlyStore** i provuar me test DI; zero `AddDefaultIdentity`/`AddRoles`/`RoleManager`/scheme/cookies). Domain/Application NUK krijohen bosh (Deviations #2).
- **API host (composition root):** vetëm `builder.Services.AddIdentityStoreFoundation();` — **route-table e PANDRYSHUAR** (test), **OpenAPI/TS klienti BYTE-IDENTIKE** (generate ×2 + Production regression, zero diff).
- **Migrimi `0003_IdentityStoreFoundation`** — forward-only, **IDENTITY-ONLY** (shih më poshtë).

## Paketat — pin + provenance

| Paketa | Versioni | Licenca | Vendosja | Burimi |
|---|---|---|---|---|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | **10.0.10** (pin ekzakt, CPM) | MIT | VETËM Persistence + Modules/Identity/Infrastructure | nuget.org/packages/Microsoft.AspNetCore.Identity.EntityFrameworkCore/10.0.10 |
| OpenIddict.EntityFrameworkCore | **7.6.0** (pin ekzakt, CPM) | Apache-2.0 | VETËM Persistence + Modules/Identity/Infrastructure | nuget.org/packages/OpenIddict.EntityFrameworkCore/7.6.0 · documentation.openiddict.com/integrations/entity-framework-core |

Transitive (të shpjeguara nga grafi real i `dotnet list package --include-transitive`): `Microsoft.Extensions.Identity.Core` + `Microsoft.Extensions.Identity.Stores` (nga Identity.EFCore) dhe `OpenIddict.Core` + `OpenIddict.Abstractions` (nga OpenIddict.EFCore). **ZERO** OpenIddict.AspNetCore/Server/Validation/Client, ZERO Duende/IdentityServer/Auth0/Okta, ZERO Identity UI, ZERO wildcard (të provuara me architecture tests csproj-level + assembly-level).

## Schema / table map (REALE, e provuar në PostgreSQL)

| Tabela | Tipi CLR real | Çelësi |
|---|---|---|
| `identity.user_account` | `UserAccountRecord : IdentityUser<Guid>` | `id` uuid (gjenerim on-add) |
| `identity.user_login` | `IdentityUserLogin<Guid>` | PK i përbërë (`login_provider`, `provider_key`); `user_id` uuid |
| `identity.user_claim` | `IdentityUserClaim<Guid>` | `id` **integer surrogate i framework-ut** (përjashtimi i VETËM i deklaruar — Deviations #3); `user_id` uuid |
| `identity.user_token` | `IdentityUserToken<Guid>` | PK i përbërë (`user_id` uuid, `login_provider`, `name`) — artefakte **account-level** të Identity-t |
| `identity.oidc_application` | `OpenIddictEntityFrameworkCoreApplication<Guid>` | `id` uuid |
| `identity.oidc_authorization` | `OpenIddictEntityFrameworkCoreAuthorization<Guid>` | `id` uuid |
| `identity.oidc_scope` | `OpenIddictEntityFrameworkCoreScope<Guid>` | `id` uuid |
| `identity.oidc_token` | `OpenIddictEntityFrameworkCoreToken<Guid>` | `id` uuid — artefakte **OAuth2/OIDC operacionale** të OpenIddict |

Kolonat = emrat REALË të property-ve Identity/OpenIddict të konvertuar MEKANIKISHT në lowercase snake_case (konsistent me `platform`); emrat eksplicitë të framework-ut për indekset (`UserNameIndex`/`EmailIndex`) → emra default të EF-it. **Zero `AspNet*`**, **zero tabela rolesh**, **zero FK cross-schema** (provuar në model DHE në `pg_constraint`), **pronësi e NDARË `user_token` ≠ `oidc_token`** (tipe/tabela të ndryshme, zero FK mes tyre), **zero token/credential material në schema `platform`** (platform e paprekur). Schema `platform` = e PANDRYSHUAR (lista ekzakte e tabelave e provuar para/pas).

## Migrimi 0003 — evidencë IDENTITY-ONLY (kufiri kritik §8)

- `Up()` = `EnsureSchema("identity")` + **8 CreateTable** + **10 CreateIndex** — TË GJITHA në schema `identity`; FK-të vetëm brenda `identity`. **ZERO operacion mbi `platform.*`** — provuar në 4 nivele: (1) operacionet e migrimit (test i përhershëm mbi `UpOperations`/`DownOperations`), (2) file C#, (3) SQL i gjeneruar (`dotnet ef migrations script` — seksioni 0003 = 148 rreshta, zero referencë `platform.*` jashtë history-insert, zero ALTER/DROP/DML), (4) schema reale para/pas në PostgreSQL (fresh + upgrade nga {0001,0002} me data sintetike të ruajtura).
- **0001/0002 + Designer-at e tyre = BYTE-IDENTIKE** me origin/main (`git diff` = 0 rreshta).
- **`PlatformDbContextModelSnapshot.cs` u rrit +472/−0** — VETËM shtimi i entiteteve Identity/OpenIddict; pjesa platform e pandryshuar (zero fshirje). Sjellje normale e EF-it; snapshot-i NUK hand-edit-ohet.
- `Down()` = heqje VETËM e 8 tabelave identity, VETËM nën guard-in tripalësh ekzistues mbi DB disposable/local/CI — **NUK është rollback production** (kontrata §8 e Identity doc). Schema bosh `identity` mbetet pas Down (EF s'gjeneron DropSchema — e dokumentuar; zero objekte).
- Historia e migrimeve: owner i vetëm = `Idata.Platform.Persistence`/`PlatformDbContext`; zero IdentityDbContext paralel, zero histori paralele.

## Test matrix (rezultatet reale — 241/241 GREEN, zero skip)

- **Architecture (21):** drejtimet e moduleve (+Identity.Infrastructure → vetëm Persistence); zero cross-module (AccessControl↔PlatformStatus↔Identity); **ZËVENDËSIMI i kontrolluar** i `Zero_identity_provider_konkret...` (ADR-028 §7) → allowlist EKZAKT assemblies + projekte të aprovuara, providerët/protokolli të ndaluar KUDO; zero Identity roles strukturalisht; vendosja csproj e paketave store; zero paketa protokolli.
- **Unit/model (102):** kontekst roleless me Guid; 8 tabelat ekzakte me tipet reale; platform i pandryshuar; snake_case; çelësat uuid + përjashtimi i deklaruar; base-first; pronësia e token-ave; zero FK cross-schema; operacionet 0003 identity-only; DI: UserOnlyStore real, zero role services, menaxherët OpenIddict me entitete Guid.
- **Integration (118, PostgreSQL real sintetik):** fresh 0001+0002+0003; no-op; **{0001,0002} → VETËM 0003** me histori/rreshta/sentinel/data platform të pandryshuara; rollback i guard-uar 0003→0002 (helper i RI me target fiks; identity hiqet, platform + data mbeten) + refuzim pa opt-in + refuzim target real; reapply; atomicitet (dështim i injektuar → zero partial-down); forma reale e schema-s (tabela/indekse unike/FK/pa AspNet*/pa role/pa FK cross-schema); UserManager round-trip sintetik + uniqueness reale (DuplicateUserName); user_claim/user_token round-trips sintetike; OpenIddict application/scope round-trips me Guid + uniqueness client_id; route-table pa asnjë endpoint identity/OIDC; gjithë suita ekzistuese e F1.1/Increment A GREEN (AccessControl invariants të paprekura; PlatformStatus i pandryshuar; kontrata byte-identike edhe nën Production).
- **Migration counts:** CURRENT-LATEST → 3 (fresh/no-op/down-to-zero/reapply/CI cycle; negative diskriminues i derivuar = 4); HISTORICAL 0001→0002 + rollback 0002→0001 të ruajtura semantikisht (përshtatje mekanike të FLAMURUARA — pending counts të derivuara nga seti 3-migrimesh + aplikim i shënjestruar i 0002).

## Rollback i B.1 (kod)

Hiqen: dy paketat nga props/csproj-et e aprovuara; `UserAccountRecord`/derivimi IdentityUserContext/remap-et identity/`UseOpenIddict` nga Persistence; `Modules/Identity/`; wiring nga Program.cs + slnx; testet e B.1; ky Record — dhe rikthehet forma provider-neutral e architecture test-it. **Migrimi 0003 NUK "rollback-ohet" në deployed/data-bearing env** — vetëm forward-only / restore i provuar (kontrata §8); Down i guard-uar mbetet vegël testesh mbi DB disposable. 0001/0002/platform/kontratat/klientët = të pandryshuara nga rollback-u.

## Deviations të deklaruara

1. **`UseOpenIddict<Guid>()` në nivel ModelBuilder** (brenda `OnModelCreating`) në vend të options-level: ekuivalenti i DOKUMENTUAR zyrtar i OpenIddict; garanton model IDENTIK në çdo vend ku ndërtohen options (host/DbMigrator/design-time/teste) dhe renditje deterministe me remap-et — përputhet me "Persistence = migration owner dhe provider-specific storage mapping".
2. **Vetëm `Modules/Identity/Infrastructure`** — Domain/Application NUK krijohen bosh: B.1 s'ka koncept real domain/use-case (vijnë me B.2 flows / B.3 projection); krijimi i projekteve bosh do të shkelte "vetëm projekte me përgjegjësi reale" + ndalimet anti-ceremoni (ADR-029 §14).
3. **`identity.user_claim.id` = integer surrogate** — tipi REAL i `IdentityUserClaim<TKey>` (framework); §3 i Identity doc kërkon që llojet REALE të paketave të verifikohen e të fitojnë mbi deklarimet. Të gjithë çelësat e tjerë = uuid/string composite realë; `user_id` = uuid.
4. **Rreshtat `client_secret`/`concurrency_token` në 0003.cs** të ristrukturuar me parametrin `name:` të EF-it (DDL i provuar IDENTIK me diff të script-it të plotë) — false-positive i guard-it të privatësisë mbi DDL pa vlera; guard-i NUK dobësohet.
5. **`.gitleaksignore` me NJË fingerprint commit-scoped** (commit i testeve, rregulli `idata-client-pii`): GUID sintetik all-zeros u kap nga pattern-i telefonik; u zëvendësua FORWARD-ONLY me `Guid.NewGuid()` (historia s'rishkruhet). Mekanizëm standard i gitleaks; ZERO ndryshim në `.gitleaks.toml`; **kërkon ratifikim nga Pronari në Human Gate**.
6. **Përshtatje mekanike të testeve HISTORIKE** të migrimit (FLAMUR): pending counts të derivuara nga seti 3-migrimesh; "migratori normal aplikon vetëm 0002" → aplikim i shënjestruar i 0002 me të njëjtin mekanizëm EF (migratori normal tani sjell edhe 0003); krijimi i historisë → `GetCreateIfNotExistsScript()` (varianti idempotent — i domosdoshëm për thirrje të njëpasnjëshme; defekti latent i komentit të vjetër "IF-NOT-EXISTS" u kap nga testet).
7. **`dotnet ef migrations add`** u ekzekutua me `--startup-project DbMigrator` (Persistence s'referon Design package — by design); file-t u riemërtuan në `0003_*` + `[Migration("0003_IdentityStoreFoundation")]` sipas konventës ekzistuese të 0001/0002.

## Open gates / kufij (ASNJË i mbyllur nga B.1)

Acceptance matrix §9 e Identity doc: **E1 = IMPLEMENTED në branch / PENDING_INDEPENDENT_REVIEW** (schema+migrimi+upgrade të provuara me teste të ekzekutuara; mbyllja = review + Human Gate + merge); **A1–A3, B1–B3, C1–C2, D1, E2–E3, E5-pjesa flows/keys/audit = OPEN / NOT IMPLEMENTED** (B.2/B.3); E4 baseline GREEN e provuar nga suita; pjesa architecture-test e E5 = implementuar në branch (zëvendësimi §7), pending review. ENG-005 = OPEN i pandryshuar (B.1 NUK shton writer mbi faktet e F1.1 dhe asnjë writer identity-lifecycle; round-trips e testeve shkruajnë vetëm `identity.*` sintetike në DB disposable). ADR-025/026/027 OPEN; ENG-004 fallback AKTIV; Step B/C, PREFLIGHT, Tier 2 = NO-GO; **zero pretendim production readiness; zero account lifecycle authority end-to-end; zero tenant authorization të integruar me Identity**.
