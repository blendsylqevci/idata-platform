# Identity — OIDC Authorization Code Sign-in, provider-side (F1 Increment B.2c) — Record

**Statusi:** **IMPLEMENTED IN BRANCH / PENDING_INDEPENDENT_REVIEW** (B.2c = SUBSET i B.2) — branch `codex/f1-b2c-oidc-signin`, worktree i izoluar i ndërtuar mbi `main` = `eac2085` (squash i PR #34, tree `574c37ce…`); **PR OPEN — NDALO PARA MERGE**. **B.2 dhe Increment B = ACTIVE / NOT COMPLETE; B.3 = NOT STARTED / BLOCKED; F1 = ACTIVE / NOT COMPLETE; production Go = BLOCKED. B.1/B.2a/B.2b mbeten COMPLETED / MERGED.** Zero pretendim full OIDC conformance/certification, zero pretendim Angular sign-in production, zero pretendim production-ready, zero mbyllje e E2 në çdo aspekt.
**Data:** 06.09.2026 · **Pronësia:** Tech (builder: Claude Code) · **Autoriteti normativ:** ADR-028 (ACCEPTED / EFFECTIVE) + ADR-029 (Application Architecture Convention) + `Identity_Authority_Store_Decision.md` §§2.1/4/5/9/12 · burime teknike: OpenID Connect Core 1.0 §§2, 3.1, 5.3; dokumentimi OpenIddict 7.6 (claim destinations, permissions, credentials, validation)
**Lidhje:** `Identity_Interactive_Authorization_B2b_Record.md` (B.2b — login/consent/PKCE) · `Identity_Authority_Credentials_Validation_B2a_Record.md` (B.2a — authority/credentials/validim) · `Identity_Store_Foundation_B1_Record.md` (B.1) · `Reliability_Gate.md` (§3 matrica 8-skenarëshe, §4 kolonat, §5.1 aplikueshmëria) · `../../PM_REVIEW_PROTOCOL.md` v2.7 §5 + §8 · `../00-ssot/iData_Plus_SSOT.md` v2.41
**Kufiri i privatësisë:** vetëm fixtures SINTETIKË të gjeneruar në runtime (klientë, llogari, certifikata); zero përdorues/klient/kredencial real; zero material sekret në repo; zero token, kod, cookie ose PII në log/output/OpenAPI.

---

## 1. Scope ekzakt (dhe çfarë NUK është)

**Është:** zgjerimi i authority-t ekzistues nga OAuth 2.0-only (B.2b) në **profilin OIDC Authorization Code + PKCE, provider-side**, mbi login/consent + PKCE ekzistues:

1. Kërkesa me scope `openid` → login/consent ekzistues (i pandryshuar) → kod → shkëmbim në `/connect/token` → **access token + ID token i vlefshëm** (nënshkruar me credentials REALE të B.2a).
2. **`nonce`**: kur dërgohet në kërkesën OIDC, ruhet nga OpenIddict dhe lidhet EKZAKT me ID token-in e atij flow-i; mungesa lejohet për code flow (OIDC Core §3.1.2.1 — `nonce` OPTIONAL në code flow); nuk shpiket rregull universal ndalimi. `state` i pandryshuar (kontrata B.2b).
3. **UserInfo minimal** (`/connect/userinfo`, GET + POST, OIDC Core §5.3): përgjigjja e suksesit përmban VETËM `sub`; pa cache (`no-store`).
4. Claims minimale: `sub` + claims të protokollit; **zero** profile/email/phone/address/roles/permissions/business IDs/security stamp/claims të brendshëm të përdoruesit.
5. Discovery/JWKS shpallin VETËM aftësitë reale: `openid`, `id_token_signing_alg_values_supported` (algoritmi REAL i firmës), `userinfo_endpoint`, `claims_supported` = vetëm claims protokolli. *(Vlerat konkrete të vëzhguara — `RS256`, `[aud,exp,iat,iss,sub]` — janë inventar i vëzhguar; testet pinojnë NËNBASHKËSINË e lejuar dhe përputhjen me `alg`-un real të ID token-it, jo listën literale.)*

**NUK është dhe mbetet OPEN:** full OIDC conformance/certification; integrimi real Angular dhe vendimi BFF/SPA/klient; refresh rotation/reuse; MFA/verification/recovery; logout/session/device revocation; offline auth (`PLANET_PILOT_BLOCKER`); B.3 projection/freshness/reconciliation; audit identity; runtime credential expiry; custody/provisioning (ADR-025/026); implicit/hybrid/password/client-credentials/device/PAR/`offline_access` (të papranueshme); user provisioning/signup/role CRUD. Konsumatori sintetik i testit verifikon kontratën — **nuk është aplikacioni final Angular**.

**Acceptance (IASD §9):** E2 = evidencë shtesë VETËM për profilin OIDC provider-side (IMPLEMENTED IN BRANCH / PENDING_INDEPENDENT_REVIEW); E3 mbetet OPEN; E5 mbetet PARTIAL.

## 2. Kontrata OIDC / claims (e provuar, jo e deklaruar)

| Element | Kontrata e provuar | Testi |
|---|---|---|
| Flow | vetëm Authorization Code + PKCE `S256`; `response_type=code`; `grant_types_supported=[authorization_code]` | IOS `Discovery_dhe_JWKS…`, IIA (16) |
| OAuth-only | kërkesa pa `openid` → access token, **zero** `id_token`; UserInfo me këtë token → **403 `insufficient_scope`** | IOS `OAuth_only_pa_openid…` |
| ID token | JWS `RS256` (`typ: JWT`), `iss` = issuer i konfiguruar, `aud` = **client_id i klientit përkatës** (jo resource audiences e access token-it), `sub` = subjekti real, `exp/iat` brenda lifetime-it, `azp`, `at_hash`; validim KRIPTOGRAFIK nga konsumatori sintetik me JWKS-in publik (Microsoft.IdentityModel — jo lexim pa validim) | IOS `OIDC_code_flow…` |
| `nonce` | e dërguar → e barabartë ekzakt në ID token; e padërguar → ID token pa `nonce` (asgjë e shpikur) | IOS `OIDC_code_flow…`, `Nonce_i_munguar…` |
| Ndarja e flow-ve | dy flow (sub/client/state/nonce të ndryshëm): secili ID token validohet vetëm me `aud`/`sub`/`nonce` të vet; validimi me audience-in e klientit tjetër → `SecurityTokenInvalidAudienceException` (nga validatori, jo pohim tekstual) | IOS `Dy_flow_te_pavarura…` |
| Claims allowlist | ID token-i përmban `iss sub aud exp iat azp nonce at_hash` + identifikuesit e brendshëm të OpenIddict (`oi_au_id`, `oi_tkn_id`); **zero** `name/email/preferred_username/role/permissions/business_id/tenant/stamp` | IOS `AssertOnlyAllowedClaims` |
| UserInfo | GET/POST → 200 JSON `{"sub"}` VETËM, `Cache-Control: no-store`; `sub` = `sub` i ID token-it të të njëjtit flow; parametrat `sub/subject/user_id` në query/body **injorohen** — kërkesa mbetet 200 me subjektin e bearer-it (parakusht 200 i asertuar para çdo rasti) | IOS `OIDC_code_flow…`, `UserInfo_nuk_zgjedh…` |
| UserInfo refuzime | anonim / cookie-only (sesion aktiv i provuar) / ID token si bearer / token i manipuluar ose i shkurtuar / bearer jo-JWT / token i **revokuar** / token i **skaduar** → **401** + `WWW-Authenticate`, zero `sub`, zero 500; token pa `openid` → **403** `insufficient_scope`; PUT/DELETE/PATCH → **400 ekzakt** me `error` në `WWW-Authenticate`. Kërkesa PA asnjë kredencial merr challenge-in bazë `Bearer` **pa kod gabimi** (RFC 6750 §3) — `invalid_token` shfaqet vetëm kur dërgohet token i pavlefshëm. | IOS `UserInfo_refuzon…`, `Access_token_i_skaduar…`, `UserInfo_metodat…`; IAEI |
| JOSE te UserInfo | i njëjti gate i header-it të jashtëm (B.2b) mbi bearer-in: `RSA1_5`/`enc` i dobët/header pa `enc`/header i keqformuar → **401 `invalid_token`** (kurrë `invalid_grant`, kurrë 500); i njëjti inner JWS i ri-enkriptuar me algoritme të lejuara → 200 | IOS `Access_token_me_JOSE…` |
| Cookie–bearer–ID | cookie-ja e sesionit NUK autentikon UserInfo as API bearer; ID token-i NUK kalon te `[Authorize]` (401); access token-i real kalon | IOS `OIDC_code_flow…`, `UserInfo_refuzon…` |

**Vëzhgim i regjistruar (jo pretendim):** dërgimi i një `access_token` të rremë NË TRUP përkrah bearer-it të vlefshëm në header nuk refuzohet me 400 (RFC 6750 §2 “vetëm një metodë transmetimi”): header-i i validuar mbetet autoritet dhe trupi injorohet (200 me subjektin e bearer-it, kurrë subjekt tjetër). Refuzimi i dy metodave të transmetimit **nuk pretendohet i implementuar** — mbetet OPEN.

**Zbulim teknik (i dokumentuar):** principal-i i validuar nga serveri OpenIddict etiketohet me identifikuesin RFC 8693 `urn:ietf:params:oauth:token-type:access_token` (`TokenTypeIdentifiers`), jo me hint-in `access_token` — kontrolli i llojit të token-it në adapter e përdor identifikuesin; token entries në DB mbajnë të njëjtin identifikues (helper-i i revokimit i pranon të dy).

## 3. Ownership / arkitektura (ADR-029) — rruga e re UserInfo

`UserInfoController` (Identity/Presentation, thin, `[ApiController]`, `ApiExplorerSettings(IgnoreApi = true)`) → **`IMediator`** → `GetUserInfoQuery`/`GetUserInfoHandler` (Identity/Application, `Features/GetUserInfo/`) → port neutral `IUserInfoSubjectResolver` (Application) → adapter `OpenIddictUserInfoSubjectResolver` (Identity/Infrastructure).

- **BaseApiController** minimal, MODULE-LOCAL në Identity/Presentation (vetëm `IMediator`), pa varësi nga PlatformStatus dhe pa shared framework (guard: dy owner-a module-local, secili `ControllerBase` direkt).
- **Përjashtimi i datuar `IMediator` (04.09.2026)** mbetet VETËM për login/consent ekzistues; **nuk zgjerohet** te UserInfo (guard-i i controller-it të hollë mbulon edhe `UserInfoController`).
- **Autentikimi i UserInfo:** principal-i vjen VETËM nga skema e SERVER-it OpenIddict (`AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)`) — rezultati i validimit REAL të bearer-it që OpenIddict e kryen PARA passthrough-it; **jo** `HttpContext.User`, **jo** cookie, **jo** skema default e API-së, **jo** parametra, **jo** dekodim tokeni. Fail-closed: endpoint tjetër / lloj tjetër tokeni / pa `openid` / subjekt bosh → refuzim.
- **Passthrough:** `EnableUserInfoEndpointPassthrough` + `SetUserInfoEndpointUris` = **owner-only** (Identity.Infrastructure; guard `InteractionOwnerOnlyMembers`); `EnableTokenEndpointPassthrough` mbetet **I NDALUAR kudo**.
- **JOSE:** gate-i i serverit (`ValidateServerOuterHeader`) është tani **endpoint-aware**: `invalid_grant` te token endpoint-i (kodi), `invalid_token` te UserInfo (bearer) — kontrollet JOSE identike, asnjë dobësim.
- **Pipeline:** rendi i middleware-ve i pandryshuar; predicate-i i cookie-binding (`/identity/*`, `/connect/authorize`) **nuk** zgjerohet te UserInfo; kufiri 503 (`/connect/*`) mbulon UserInfo (JSON `temporarily_unavailable`, `no-store`, trup gjenerik).
- **Paketat:** `Mediator.Abstractions` (pin-i EKZISTUES 3.0.2, CPM) shtuar në Identity Application/Presentation; `Mediator.SourceGenerator` mbetet vetëm në API host; zero paketë/version tjetër; OpenIddict 7.6.0, Identity EF 10.0.10 të pandryshuara. Lockfile-et: vetëm delta e derivuar (Mediator.Abstractions 3.0.2 + `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.0 transitive + hartat e varësive të nyjeve Project) — e deklaruar paraprakisht dhe e verifikuar me `--locked-mode`.

## 4. Inventari real (host production, pa shtesa test-only)

- Endpoint-e OpenIddict: `connect/authorize`, `connect/token`, **`connect/userinfo`** (GET/POST), discovery + JWKS default; zero introspection/revocation/end-session/device/verification/PAR (404).
- Grants/response types: vetëm `authorization_code` / `code`; PKCE `S256` i vetëm; `RequireProofKeyForCodeExchange` aktiv; `DisableTokenStorage`/`DisableAccessTokenEncryption` = false.
- Passthrough: authorization (B.2b) + userinfo (B.2c) = true; token/end-session/end-user-verification = false.
- Route table MVC/minimal: `api/platform/access-context`, `api/platform/memberships/{id}/role`, `api/platform/status`, `connect/authorize`, **`connect/userinfo`**, `identity/login` — UserInfo JASHTË `platform-v1` (OpenAPI byte-identik; drift-check + Production build).
- Discovery: `userinfo_endpoint`, `scopes_supported=[openid]`, `claims_supported=[aud,exp,iat,iss,sub]`, `id_token_signing_alg_values_supported=[RS256]` (= `alg` real i ID token-it), `subject_types_supported=[public]`, `code_challenge_methods_supported=[S256]`; zero `end_session_endpoint`/refresh/`offline_access`.

## 5. RED → GREEN (heqje e izoluar e kontrollit, rikthim byte-identik)

Për çdo kontroll: kopje e file-it → heqja e VETËM e kontrollit → build + testi i synuar → rikthim nga kopja + SHA-256 identik → GREEN.

| # | Kontrolli i hequr | Testi | Rezultati | Klasifikim |
|---|---|---|---|---|
| a | kontrolli i scope-it `openid` në adapter | `OAuth_only_pa_openid…` | **RED** (1 dështim: token pa `openid` merr 200) | diskriminues |
| b | kontrolli i llojit të token-it (identifikuesi access token) në adapter | `UserInfo_refuzon…` + `OIDC_code_flow…` + `Access_token_i_skaduar…` | GREEN (3/3 kalojnë) | **jo-diskriminues** — OpenIddict e refuzon vetë ID token-in si bearer para passthrough-it; kontrolli mbetet si defense-in-depth i dokumentuar, jo si provë |
| c | destinacioni `IdentityToken` i `sub` në gateway | `OIDC_code_flow…` + `Nonce_i_munguar…` | GREEN (2/2 kalojnë) | **jo-diskriminues** — OpenIddict e përfshin gjithmonë `sub` në ID token pavarësisht destinacioneve; destinacioni eksplicit mbetet si qëllim i dokumentuar, jo mekanizmi |
| d | dega endpoint-aware e JOSE (gjithmonë `invalid_grant`) | `Access_token_me_JOSE…` | **RED** (1 dështim: UserInfo kthen `invalid_grant`) | diskriminues |
| e | `EnableUserInfoEndpointPassthrough` | `OAuth_only…` + `OIDC_code_flow…` + inventari | **RED** (3 dështime: OAuth-only 200 pa scope, flow OIDC, inventari) | diskriminues |
| f | `SetUserInfoEndpointUris` | `Discovery_dhe_JWKS…` + `OIDC_code_flow…` + inventari | **RED** (3 dështime: discovery pa `userinfo_endpoint`, flow OIDC, inventari) | diskriminues |

**Rikthimi (mbi BYTES-AT FINALË):** provat **a**, **b** dhe **c** u RI-EKZEKUTUAN pas korrigjimeve të review-t të brendshëm, sepse `IdentityUserInfo.cs` dhe `IdentityInteractiveAuthorization.cs` ndryshuan pas raundit të parë (koment/pohime); **e** dhe **f** mbeten të vlefshme sepse `IdentityAuthorityServiceCollectionExtensions.cs` nuk ndryshoi (hash identik para/pas). SHA-256 finale, identike para/pas çdo prove: `IdentityUserInfo.cs` = `8a6acc7d78028438…`; `IdentityInteractiveAuthorization.cs` = `8b9a23eeb903dc39…`; `IdentityAuthorityServiceCollectionExtensions.cs` = `725e7b745fa92bf7…` (SHA-256 të shkurtuara sipas praktikës së repo-s për identifikuesit e gjatë; vlerat e plota në log-un e provave). Build final: 0 warnings / 0 errors.

## 6. Reliability Gate — matrica e dështimeve (Reliability_Gate §§3–5.1) për rrugët e reja

Të gjitha testet ekzekutojnë pipeline-in REAL (HTTP → middleware → OpenIddict → PostgreSQL sintetik). IOS = `IdentityOidcSignInTests`; IIR = `IdentityInteractiveReliabilityTests` (B.2b, ripërdorur VETËM ku dataflow-i është i njëjtë).

| # | Skenari standard | Aplikimi te B.2c | Expected behavior | Data safety / invariant | User-visible | Recovery | Automated test | Evidence |
|---|---|---|---|---|---|---|---|---|
| 1 | Internet down | **N/A (kërkon pranim në review):** authority + API co-located; asnjë thirrje WAN në rrugët e reja; offline auth Edge/POS = OPEN (`PLANET_PILOT_BLOCKER`) | — | — | — | — | — | justifikim, jo test |
| 2 | SEF/ATK down | **N/A (kërkon pranim):** zero varësi fiskale në identity | — | — | — | — | — | justifikim |
| 3 | Printer/peripheral down | **N/A (kërkon pranim):** zero periferikë | — | — | — | — | — | justifikim |
| 4 | Device crash / offline | klienti humb përgjigjen e token endpoint-it ose të UserInfo | kodi mbetet një-përdorimsh (replay refuzohet); UserInfo është idempotente (GET/POST i përsëritur jep të njëjtin `sub`) | zero token i dytë nga i njëjti kod; zero gjendje gjysmake | gabim OAuth / 401 | flow i ri | IIR R4/R3 (kod, dataflow i njëjtë) + IOS `OIDC_code_flow…` (GET+POST të përsëritur) | run lokal + CI |
| 5 | Sync interrupted / retry / idempotency | dorëzim i dyfishtë consent-i / replay kodi / UserInfo i përsëritur | dy kode të ndara, secili një-përdorimsh; replay refuzohet; UserInfo i përsëritur = i njëjti `sub` | zero zgjerim privilegji | — | pa veprim | IIR R4/R5 + IOS `Dy_flow…`, `UserInfo_nuk_zgjedh…` | run lokal + CI |
| 6 | Server/host restart | restart REAL i host-it pas lëshimit të access + ID token (të njëjtat credentials/DB) | UserInfo → 200 me të njëjtin `sub` në host-in e ri; ID token-i validohet me JWKS-in e host-it të ri | token-at e lëshuara mbeten të vlefshme; gjendja e konsumuar nuk ringjallet (IIR R6/R8) | — | automatik | IOS `Restart_real_i_host_it…` (+ IIR R6 për sesion/kod) | run lokal + CI |
| 7 | DB unavailable | DB e paarritshme GJATË UserInfo (validimi i token entry) | **HTTP 503**, `application/json`, `error=temporarily_unavailable`, `no-store`, zero `sub`, zero detaj (Npgsql/ID); gabimi i klientit nuk maskohet si 503 (401/403 të provuara veçmas) | zero mutim; token-i mbetet i vlefshëm | 503 → normal | rikthim i SË NJËJTËS DB → 200 me `sub` | IOS `DB_e_paarritshme_gjate_UserInfo…` (+ IIR R3/R8 për kod/token) | run lokal + CI |
| 8 | Disk/queue full | **N/A (kërkon pranim):** zero radhë/outbox në rrugët e reja; storage-u i token-ave = PostgreSQL (skenari 7) | — | — | — | — | — | justifikim |

**Invarianti i përbashkët:** asnjë dështim i pjesshëm nuk prodhon ID token, `sub` ose 200 pa access token të validuar nga pipeline-i real; rikuperimi nuk ringjall kod të konsumuar; telemetria e kufirit mbart vetëm klasën e exception-it dhe trace id; zero token/PII në log, exception, përgjigje apo OpenAPI (IOS kontrollon `DumpLogs()` për access/ID token).

## 7. Testet dhe evidenca

**Struktura (kompilim/guard) — ArchitectureTests:** `Mediator_Abstractions_vetem_ne_projektet_e_aprovuara` (+ Identity Application/Presentation), `Controller_i_hollte_merr_vetem_IMediator…` (Theory: PlatformStatus + **UserInfo**), `BaseApiController_ekziston_vetem_module_local…` (dy owner-a), `IdentityAuthorityBoundaryTests` (`EnableUserInfoEndpointPassthrough`/`SetUserInfoEndpointUris` owner-only; token passthrough i ndaluar), `IdentitySecurityDependencyInventory` (autoriteti direkt: Mediator.Abstractions 3.0.2 për Identity Application/Presentation), Identity roles jashtë tenant authorization (guard ekzistues). **Unit:** `IdentityUserInfoContractTests` (handler fail-closed, fabrikat, kontrata, zero referencë transport/provider/store nga Application).

**Runtime — IntegrationTests:** `IdentityOidcSignInTests` (13 teste, §2/§6), `IdentityAuthorityEndpointInventoryTests` (userinfo në inventar, PUT/DELETE fail-closed), `ApiSurfaceTests` (route table + OpenAPI pa userinfo), `IdentityInteractiveAuthorizationTests` (zëvendësim i kontrolluar i testit #12: OAuth-only pa ID token + inventar i sinqertë; regresionet PKCE/binding/consent/antiforgery/cookie–bearer të pandryshuara), `IdentityInteractiveReliabilityTests` (R3–R8 të pandryshuara).

**Rezultatet LOKALE (cikli final mbi kodin final):** PostgreSQL sintetik i izoluar (docker `postgres:16-alpine`, porta 5433, DB efemere `idata_platform_test_*`); `dotnet restore --locked-mode` OK; build **0 warnings / 0 errors**; targeted: `IdentityOidcSignInTests` **14/14** (pas shtimit të testit të token-it të skaduar), `IdentityUserInfoContractTests` **10/10**, regresionet e prekura (IIA + IAEI + ApiSurface + IIR) **60/60** pas korrigjimeve (dy iterime zhvillimi para tyre: 7 dhe 2 dështime, shih §9); **cikli final mbi kodin final (pas korrigjimeve të review-t të brendshëm): Unit 171/171 · Architecture 306/306 · Integration 225/225 · zero skipped** (baseline `main`: 161 / 305 / 209 — numra referues, jo target); OpenAPI: kontrata `contracts/openapi/platform-v1.json` + klienti i gjeneruar **byte-identikë** (drift-check Development dhe build Production `--no-incremental` + regjenerim → zero diff). Iterimet gjatë zhvillimit (të raportuara veçmas): run-i i parë i suitës OIDC 6/13 (UserInfo 401 nga krahasimi i gabuar i llojit të token-it — korrigjuar), run-i i dytë 11/13 (POST pa Content-Type; helper i revokimit — korrigjuar në teste), run-i i plotë i parë 223/224 (pin-i i route table-it të B.1 — zëvendësim i kontrolluar), pastaj tri cikle të plota GREEN.

**CI (mbi HEAD-in e PR-së):** rezultatet reale të `build-test` / `gitleaks` / `integrity-linter` raportohen te **PR body** dhe në raportin final (nuk mund të vetë-referencohen brenda commit-it që i prodhon).

## 8. Baseline / head / diff

- Base: `main` = `eac20858f130de9198114e14dc46eb8296aa6047` (squash i PR #34), tree `574c37ce8161c005b29913f2af3c227bedb925a8`; verifikuar live (PR #34 MERGED, CI + secret-scan SUCCESS).
- Worktree i izoluar (`git worktree add`), branch `codex/f1-b2c-oidc-signin`; worktree-ja origjinale (dy shtesa të autorizuara të pakommituara: `docs/INDEX.md`, Roadmap — “Sqarim i dorëzimeve të dukshme F1”, 05.09.2026; `output/`, `tmp/`) **e paprekur** — hash-et dhe pointer-at e branch-eve të provuara në fund (raporti final).
- EXPECTED_FILE_SET i ngrirë me SHA-256 para editimit (39 hyrje: 29 ekzistuese + 10 të reja) + 1 zgjerim i deklaruar (§9.1) = 40; zero file jashtë zarfit. **Dy commit-e forward** — commit 1 (kod + teste) = `862321376a20d60c0f895b2189c420064427f629`; commit 2 (docs + ky Record) = head-i i PR-së, SHA-ja e tij mbahet në **PR body** (jo vetë-referencë e pamundur brenda commit-it docs).

## 9. Devijime (të raportuara shprehimisht)

1. **EXPECTED_FILE_SET — zgjerim i deklaruar pas ngrirjes:** `IdentityStoreFoundationTests.cs` (test route-inventory i B.1, tashmë i zëvendësuar në mënyrë të kontrolluar nga B.2b) nuk ishte në setin e ngrirë; cikli i plotë e zbuloi (pin-i i route table-it duhej `+ connect/userinfo`). File-i është brenda zarfit të prompt-it (“teste Identity/route inventory”), u shtua si hyrje e deklaruar me hash bazë dhe u zëvendësua në mënyrë të kontrolluar (jo heqje). `DependencyDirectionTests.cs` (hyrje kushtore) NUK u prek — nevoja nuk u provua.
2. **Dy prova RED→GREEN jo-diskriminuese** (§5 b, c) — të raportuara si të tilla, jo si prova: kontrolli i llojit të token-it (defense-in-depth, OpenIddict refuzon vetë ID token-in si bearer) dhe destinacioni eksplicit `IdentityToken` i `sub` (OpenIddict e përfshin gjithmonë `sub`).
3. **Zbulim teknik gjatë zhvillimit (jo devijim scope-i):** përgjigjja 401 e parë e UserInfo vinte nga krahasimi i llojit të token-it me hint-in `access_token`; OpenIddict etiketon principal-in me identifikuesin RFC 8693 — korrigjuar para provave; testi diagnostik i përkohshëm dhe instrumentimi u hoqën (worktree = vetëm zarfi).
4. **Testet e helper-it:** POST i UserInfo kërkon `application/x-www-form-urlencoded` (OpenIddict refuzon POST pa Content-Type) — helper-i dërgon formë të zbrazët; pohimi i inventarit për 400 pranon `error` në `WWW-Authenticate` (RFC 6750) përveç trupit.
5. **Kontradiktë JASHTË zarfit (kërkon dispozicion ose zgjerim të emërtuar):** `Identity_Authority_Credentials_Validation_B2a_Record.md` §16 (rresht CURRENT) ende thotë “hapi pasues nuk shpallet (kërkon goal/prompt të ri)” — gjendje e 05.09.2026, e superseduar nga ky increment. File-i **nuk është në zarfin docs të prompt-it (§7)**, prandaj NUK u prek; raportohet këtu për dispozicion (pointer i datuar insertion-only si te Record-i i B.2b, ose lënie deri në merge).
6. **ADR Register i paprekur qëllimisht:** `05_Decision_Gates_ADR.md` nuk është në zarfin docs dhe prompt-i ndalon ndryshim ADR — rreshtat ADR-028/029 mbeten pa pointer B.2c derisa increment-i të merge-ohet. Deklarohet shprehimisht (mospërputhje e pranuar, jo harresë).
7. **Review i brendshëm i pavarur para commit-it (5 verifikues read-only + refute):** gjeti dhe u korrigjuan para commit-it — dy teste false-GREEN (`UserInfo_nuk_zgjedh…` pa parakusht/pohime pozitive; blloku (21) i IIA me `Location` bosh), zgjerim i pa-scope-uar i matricës së inventarit (401/`error` lejohej edhe për token/authorize), pohim i papinuar për PUT/DELETE/PATCH, tautologji `Assert.False(IsNullOrEmpty(label))`, koment XML i vjetruar te `IdentityInteractiveAuthorization.cs`, emër i pasaktë `oi_crt_id` në allowlist (→ `oi_crt_dt`/`oi_exp_dt`), dhe hash-i i vjetruar i §5 (provat a/b/c u ri-ekzekutuan mbi bytes-at finalë). U shtua testi i mungueshëm i token-it të SKADUAR te UserInfo.
8. **Zero devijim procedural:** worktree i izoluar, zero stash/reset/restore/checkout/amend/rebase/force-push; origjinalja dhe dy shtesat e saj të pakommituara të paprekura (hash-e identike); PostgreSQL sintetik i imi (porta 5433) — asnjë proces/DB/preview ekzistues i prekur.

## 10. REMAINING (pointer, jo burim paralel statusesh — autoriteti = IASD §9/§12 + Tracker)

Integrimi real Angular dhe vendimi BFF/SPA/klienti · refresh rotation/reuse detection · MFA/verification/recovery (E3 OPEN) · logout/session/device revocation · offline auth (`PLANET_PILOT_BLOCKER`) · B.3 projection/freshness/reconciliation · audit identity (**mospërputhje e regjistruar:** IASD §2 e liston nën B.2, E5 nën B.3 — mbetet OPEN/PARTIAL, pa zhvendosje të heshtur, asnjë audit writer i ri këtu) · runtime credential expiry (UNKNOWN) · custody/provisioning dhe gates ADR-025/026 · full OIDC conformance/certification · follow-up-et e regjistruara të B.2b (§9.5, §11.5) të paimplementuara.

## 11. Radha

Fable delta-review → Codex PM/Quality-Gate sipas `PM_REVIEW_PROTOCOL.md` (delta-bounded te ky scope) → Human Gate → merge vetëm me autorizim të ri ekzakt. Maksimum dy correction passes; bllokues real i pazgjidhur pas tyre = NDALO / OWNER DISPOSITION. **PR OPEN — NDALO PARA MERGE.** Zero Notion/Slack, zero housekeeping, zero increment tjetër nga ky PR.
