# Identity — Interactive OAuth 2.0 Authorization Code + PKCE (F1 Increment B.2b) — Record

**Statusi:** **COMPLETED / MERGED** — PR #33 u bë **squash-merge** në `main` = `a6cc98c6c4b991e868ce370450a5ed855c2e3bf0` (parent i vetëm `6554625f12f0fb1cf2b74d8e18b61d11d159da54` = squash i PR #32, mergedAt 2026-09-04T23:26:20Z = 05.09.2026 01:26:20 Europe/Belgrade) pas Fable delta-review → Codex review → **HG-PR33-MERGE** (autorizim i Pronarit); head i PR-së para merge-it `788b4e1c0ef12a10038393fa91e54e053a33fe2e`, **paritet tree i verifikuar** (`30cfa65d7b9cc94f74856006baa202951f2c36ce`); correction passes C1 (§9) dhe C2 (§10) të mbyllura brenda PR-së. B.2b = SUBSET i B.2: **B.2 dhe Increment B = ACTIVE / NOT COMPLETE; B.3 = NOT STARTED / BLOCKED; F1 = ACTIVE / NOT COMPLETE; production Go = BLOCKED; hapi pasues nuk shpallet CURRENT (kërkon goal/prompt të ri).** Zero pretendim `production-ready`, zero pretendim full OIDC sign-in, zero pretendim offline authentication. **§§1–10 më poshtë = evidencë HISTORIKE e branch-it (gjendja para merge-it — statuset IMPLEMENTED IN BRANCH / PENDING_INDEPENDENT_REVIEW, “PR OPEN — NDALO PARA MERGE” dhe dispozicioni i hapur i §10.2 janë histori, të superseduara nga §11); statuset CURRENT = ky header + §11 (closure post-merge).**
**Data:** 04.09.2026 (v1) · **closure post-merge:** 05.09.2026 (§11) · **Pronësia:** Tech (builder: Claude Code) · **Autoriteti normativ:** ADR-028 (ACCEPTED / EFFECTIVE) + ADR-029 (Application Architecture Convention) + `Identity_Authority_Store_Decision.md` §9
**Lidhje:** `Identity_Authority_Credentials_Validation_B2a_Record.md` (B.2a — authority/credentials/validim lokal) · `Identity_Store_Foundation_B1_Record.md` (B.1 store) · `Reliability_Gate.md` (§3 matrica 8-skenarëshe, §4 kolonat e detyrueshme) · `../../PM_REVIEW_PROTOCOL.md` v2.6 §5 (scope historik B.2b = v2.5; scope-i aktual = status-sync post-merge) + §8 · `../00-ssot/iData_Plus_SSOT.md` v2.40
**Kufiri i privatësisë:** vetëm fixtures SINTETIKË të gjeneruar në runtime; zero përdorues/klient/kredencial real; zero material sekret në repo; zero kredencial, token, kod ose cookie në log/output.

---

## 1. Scope i autorizuar (dhe çfarë NUK është)

**Është:** kontrata REALE **OAuth 2.0 Authorization Code + PKCE** e authority-t qendrore:

1. **Login real** kundër store-it ekzistues ASP.NET Core Identity (B.1), me lockout dhe rate limiting me kufij eksplicitë.
2. **Session cookie interaktive** në skemë të VEÇANTË, e ndarë plotësisht nga autentikimi bearer i API-ve.
3. **Consent eksplicit** në çdo kërkesë autorizimi (zero “remember forever”, zero re-consent i heshtur).
4. **Lëshim kodi autorizimi** dhe **shkëmbim në `/connect/token`** përmes pipeline-it real OpenIddict, me `code_verifier` `S256` të detyrueshëm.
5. **JOSE server-stack** mbi artefaktin e mbrojtur që konsumon token endpoint-i (mbyllje e follow-up-it të B.2a për stack-un e serverit).
6. **Access token real** që kalon te një endpoint `[Authorize]` përmes middleware-it real të validimit.

**NUK është dhe mbetet OPEN:** scope `openid`, ID token, UserInfo, nonce semantics, full OIDC sign-in, refresh token production, MFA/recovery/verification policy, logout/back-channel/session/device revocation, audit identity i qëndrueshëm, offline Edge/POS authentication (`PLANET_PILOT_BLOCKER`), B.3 projection/freshness/reconciliation, Identity roles për tenant RBAC (mbetet te AccessControl), key custody (ADR-025/026), runtime credential expiry, BFF/SPA/external IdP.

**Acceptance:** IASD §9 **E2 = IMPLEMENTED IN BRANCH / PENDING_INDEPENDENT_REVIEW VETËM për kontratën OAuth 2.0 code+PKCE**; E3 (MFA/recovery/refresh) mbetet OPEN; E5 mbetet PARTIAL (B.2b shton gate-in JOSE të serverit dhe kufijtë e sipërfaqes interaktive).

## 2. Arkitektura (ADR-029)

| Shtresë | Pronësi në B.2b | Kufiri i imponuar |
|---|---|---|
| `Modules/Identity/Application` (**E RE**) | use-case-t `InteractiveSignInHandler`/`InteractiveConsentHandler`, portet `IInteractiveCredentialsVerifier`/`IAuthorizationProtocolGateway`, kontrata e përbashkët dhe modelet neutrale | zero ASP.NET Core, zero OpenIddict, zero ASP.NET Core Identity, zero Persistence, zero Mediator; zero referencë platforme (Identity nuk ka Domain — ADR-029 §14) |
| `Modules/Identity/Infrastructure` | store + protokoll + credentials + konfigurim: `SignInManager`, cookie scheme, antiforgery, rate limiter, adapterat e porteve, gate-i JOSE i serverit | owner i VETËM i OpenIddict-it dhe i store-it; i vetmi assembly me sipërfaqen sign-in `Microsoft.AspNetCore.Identity` |
| `Modules/Identity/Presentation` (**E RE**) | endpoint-et `identity/login` (GET/POST) dhe `connect/authorize` (GET/POST) + HTML minimal server-rendered | varet VETËM nga Application; `FrameworkReference Microsoft.AspNetCore.App`; zero paketë e jashtme; zero OpenIddict/Identity/IdentityModel/Persistence (guard mbi MemberRefs) |
| `Idata.Platform.Api` | composition root: `AddIdentityInteractiveAuthorization`, `UseRateLimiter`, `UseAntiforgery`, `MapIdentityInteractiveAuthorization` | zero tip OpenIddict/IdentityModel; zero logjikë transporti/biznesi |
| `Idata.Platform.Persistence` | i PAPREKUR (zero migrim, zero schema, zero protokoll) | migration owner i vetëm |

**Zero paketë e re:** `Directory.Packages.props` byte-identik; lockfile-t ndryshojnë vetëm mekanikisht me node-e `Project` (zero `resolved`/`contentHash` i ri).

## 3. Threat model i sipërfaqes interaktive

| Kërcënim | Kontrolli | Provë |
|---|---|---|
| User enumeration | përgjigje UNIFORME (i njëjti status dhe i njëjti trup) për subjekt të panjohur, kredencial të gabuar, llogari të bllokuar dhe llogari me faktor të dytë | test që krahason katër trupat |
| Brute force / credential stuffing | lockout i store-it (`lockoutOnFailure`) + rate limit fixed-window per burim, të dyja me kufij të konfigurueshëm dhe të validuar fail-closed | test lockout-i + test 429 |
| Rrëmbim sesioni pas ndryshimit të kredencialit | validim i stamp-it të sigurisë në ÇDO kërkesë; mospërputhje/subjekt i munguar → refuzim + sign-out (C1: DB e paarritshme → refuzim VETËM i kërkesës, pa sign-out) | test i rrotullimit të stamp-it |
| Cookie e përdorur si bearer | skema default e API-së mbetet validimi OpenIddict; cookie-ja pranohet VETËM në skemën e vet | test: cookie te `[Authorize]` → 401; te AccessControl → 403 |
| CSRF mbi login/consent | antiforgery i detyrueshëm nga middleware-i për çdo endpoint me form binding | test: POST pa token → 400, zero cookie, zero kod |
| Open redirect | return URL pranohet VETËM lokale, pa fragment/kontroll/backslash dhe me path EKZAKT të endpoint-it të autorizimit | teste unit + integrimi (4 variante) |
| XSS në consent/login | çdo vlerë dinamike enkodohet (`HtmlEncoder`); zero JavaScript, zero asset i jashtëm | test me `DisplayName` armiqësor |
| Zgjerim scope-i nga formulari | `GrantAsync` lëshon kod VETËM për scope-t e kërkesës së validuar nga serveri; formulari nuk mbart scope | dizajn + rivalidim i plotë i kërkesës në POST |
| Kod i vjedhur / i riluajtur | PKCE `S256` i detyrueshëm (server-wide, edhe kur klienti s'e kërkon), kod një-përdorimsh i lidhur me klient/redirect/challenge | teste: verifier i munguar/gabuar, replay, klient/redirect tjetër |
| Algoritëm JOSE i ndaluar mbi artefaktin e serverit | gate para çdo dekriptimi: `alg`/`enc` sipas allowlist-it, header i keqformuar → refuzim OAuth | test me payload të ri-enkriptuar `RSA1_5`/`enc` i dobët/header i keqformuar |
| Rrjedhje informacioni në gabime | të gjitha refuzimet janë klasa gjenerike; zero username/kredencial/kod/token në trup ose log | asertime `error` pa detaje; zero 500 |
| Formular i lidhur me sesion tjetër (C1) | principali i validuar i cookie-t bëhet efektiv në `HttpContext.User` për login/authorize PARA krijimit dhe validimit të antiforgery token-it → token-i lidhet me identitetin e sesionit; sesion i revokuar ose sesion tjetër → 400, zero kod | `Formulari_i_consent_it_lidhet_me_identitetin_e_sesionit_dhe_refuzohet_kur_sesioni_ndryshon` |
| DB e paarritshme gjatë protokollit/interaksionit (C1) | kufi i NGUSHTË (vetëm klasa infrastrukture: `DbException`/timeout/EF-wrap) para autentikimit për `/connect/*` + `/identity/*` → 503 gjenerik (JSON për protokollin, HTML për interaksionin), zero 500, zero detaj; dështimi infrastrukturor gjatë validimit të cookie-t refuzon VETËM kërkesën (nuk revokon sesionin) | R1/R2/R3/R8 |

## 4. RED → GREEN (prova diskriminuese)

Çdo kontroll u hoq **një nga një**, u ekzekutua testi përkatës dhe u rikthye me **edit forward** (SHA-256 byte-identik para/pas — pa checkout/restore/reset):

| # | Kontrolli i hequr | Testi | Rezultati pa kontrollin |
|---|---|---|---|
| R1 | kontrolli i sesionit te `GET /connect/authorize` | fluksi i plotë (anonim → login) | **RED** |
| R2 | `RequireProofKeyForCodeExchange()` (server-wide) | lidhja e kodit me verifier | **RED** |
| R3 | ndarja cookie ↔ bearer (cookie si skemë default) | cookie te API bearer | **RED** |
| R4 | gate-i JOSE i stack-ut server | artefakti jashtë allowlist-it | **RED** |
| R5 | vendimi i consent-it (aprovim gjithmonë) | consent i refuzuar | **RED** |

**Provë e pavlefshme e korrigjuar (raportuar):** versioni i parë i provës PKCE mbeti **GREEN** pas heqjes së `RequireProofKeyForCodeExchange()`, sepse klienti i testit e kërkonte vetë PKCE-në (`Requirements.Features.ProofKeyForCodeExchange`) — kontrolli i klientit e maskonte atë të serverit. Testi u forcua me një klient që **nuk** e kërkon PKCE-në; vetëm pas kësaj heqja e kontrollit prodhon RED.

**STRUCTURAL RED (jo behavioral):** testet e reja nuk kompilohen mbi bazën `6554625` (projektet/tipat e reja nuk ekzistojnë atje).

**C1 (04.09.2026) — dy kontrolle të reja, të provuara njësoj (heqje e izoluar në `Program.cs`, rikthim forward SHA-identik):**

| # | Kontrolli i hequr | Testi | Rezultati pa kontrollin |
|---|---|---|---|
| C1-R1 | `UseIdentityInteractiveSession()` (sesioni në `HttpContext.User` para antiforgery-t) | formulari i lidhur me identitetin | **RED** |
| C1-R2 | `UseIdentityInteractionFailureBoundary()` (503 i kontrolluar) | R3 — DB e paarritshme gjatë shkëmbimit | **RED** |

**Dështime reale gjatë C1 (të korrigjuara, jo të fshehura):** (a) validimi i cookie-t bënte sign-out edhe mbi dështim infrastrukture — sesioni humbiste gjatë një ndërprerjeje të DB-së; u nda klasa “identitet i pavlefshëm” (revokim) nga “DB e paarritshme” (refuzim vetëm i kërkesës); (b) supozimi “key ring efemer” për restart-in ishte i gabuar — default-i i framework-ut e ruan key ring-un në profilin e përdoruesit, prandaj varianti “pa ndarje” provohet me dy direktori të ndryshme të kontrolluara nga testi.

## 5. Reliability Gate — matrica e dështimeve (CURRENT pas C1)

Të gjitha testet ekzekutojnë pipeline-in REAL (`HTTP → middleware → Identity/OpenIddict → PostgreSQL sintetik`). Ndërprerja e DB-së është e PËRKOHSHME mbi TË NJËJTËN DB sintetike të testit (`ALLOW_CONNECTIONS false` + ndërprerje e sesioneve, pastaj `ALLOW_CONNECTIONS true`) — jo DROP, jo DB e re. Testet: `IdentityInteractiveAuthorizationTests` (IIA) dhe `IdentityInteractiveReliabilityTests` (IIR).

| # | Failure scenario | Expected behavior | Data safety / invariant | User-visible | Recovery | Automated test | Evidence |
|---|---|---|---|---|---|---|---|
| 1 | DB e paarritshme PARA login-it | verifikuesi kthen `Unavailable` → **HTTP 503** gjenerik | zero sesion, zero kod, zero mutim | faqe HTML “Temporarily unavailable” | rikthim i DB-së → login normal | IIA `DB_e_paarritshme_nuk_leshon_kurre_sesion_ose_kod` | run lokal + CI |
| 2 | DB e paarritshme GJATË consent-it | validimi i klientit nga OpenIddict dështon brenda kufirit → **HTTP 503** HTML gjenerik (jo exception i propaguar); validimi i cookie-t refuzon kërkesën PA revokuar sesionin | zero kod; sesioni mbetet i vlefshëm për pas rikthimit | faqe “Temporarily unavailable” | pas rikthimit të së njëjtës DB: consent page pa login të ri | i njëjti test (faza 2 + pas rikthimit) | run lokal + CI |
| 3 | DB e paarritshme gjatë shkëmbimit të kodit | `/connect/token` → **HTTP 503**, `Content-Type: application/json`, trup i verifikuar SEMANTIKISHT (C2: objekt JSON me `error` = `temporarily_unavailable`, pa `access_token`), `Cache-Control: no-store`, zero detaj | **gjendja e PROVUAR:** kërkesa që dështoi NUK e konsumoi kodin — pas rikthimit shlyhet një herë të vetme, replay refuzohet | gabim OAuth i kontrolluar | rikthim → i njëjti kod ende një-përdorimsh | IIR `R3_DB_e_paarritshme_gjate_shkembimit_te_kodit...` | run lokal + CI |
| 4 | Dërgim i dyfishtë i të njëjtit consent | çdo dorëzim rivalidohet si kërkesë autorizimi → **dy kode të ndara** me të njëjtat lidhje (klient/redirect/challenge), secili një-përdorimsh | zero zgjerim privilegji; replay i secilit refuzohet; verifier tjetër refuzohet | dy redirect-e me `state` identik | pa veprim | IIR `R4_dorezimi_i_dyfishte_i_consent_it...` | run lokal + CI |
| 5 | Replay i kodit | përdorimi i dytë refuzohet | zero token i dytë | gabim OAuth | pa veprim | IIA `Kodi_lidhet_me_verifier...` + IIR R3/R4/R6/R8 | run lokal + CI |
| 6 | Restart REAL i host-it mid-flow | **me key ring të ndarë** (të njëjtën DB/klient/përdorues/issuer/credentials, direktori çelësash e kontrolluar nga testi): sesioni, consent-i në proces dhe kodi i pashlyer vazhdojnë te host-i i ri; **me key ring TJETËR**: cookie-ja kërkon login të ri dhe formulari i vjetër — i PROVUAR real në host-in A (C2: 200 + antiforgery token jo-bosh) — refuzohet me **400 ekzakt, zero kod** (C2; jo `!= 302`), kodi DB-backed mbetet i vlefshëm | kodi i konsumuar para restart-it NUK ringjallet; kodi i pashlyer shlyhet vetëm një herë | vazhdimësi ose login i ri sipas kushtit të provuar | flow i ri pas login-it | IIR `R6_restart_mid_flow_me_key_ring_te_ndare...` + `R6_restart_mid_flow_me_key_ring_te_ndryshem...` | run lokal + CI |
| 7 | Kërkesa konkurruese (4 flow, klient/state/challenge të ndryshëm, sesione të veçanta) | **faza konkurruese** (`Task.WhenAll`): çdo flow lëshon kodin e vet — `state`-i i KTHYER nga redirect-i real krahasohet ekzakt me atë të kërkesës (C2, brenda `IssueCodeAsync`) — dhe përfundon POZITIVISHT me kodin e vet të paprekur → 4 kode, 4 state, 4 access token-a jo-boshë, të gjithë të ndryshëm; **kontrolle negative të IZOLUARA** (C2, kërkesa SEKUENCIALE pas fazës konkurruese, mbi kode të REJA të pakonsumuara): vetëm verifier i gabuar → 400 pa token; vetëm klient i gabuar → 400 pa token | zero përzierje kodesh/token-ash; binding-u i secilit shkak i izoluar | 4 redirect-e korrekte | pa veprim | IIR `R7_flow_e_konkurruese...` — mbivendosja provohet me intervale monotonike (`Stopwatch`) të kërkesave `/connect/*` të FAZËS KONKURRUESE (snapshot para kontrolleve negative); kontrollet negative NUK pretendohen konkurruese | run lokal + CI |
| 8 | Ndërprerje e përkohshme dhe rikthim i SË NJËJTËS DB | gjatë ndërprerjes: 503; pas rikthimit: sesioni ekzistues vlen (consent page pa login), flow i ri përfundon me **access token real jo-bosh** (C2) | gjendja e konsumuar para ndërprerjes NUK ringjallet | 503 → normal | automatik (pool-i i lidhjeve heq lidhjet e prishura në dështimin e parë) | IIR `R8_rikthimi_i_se_njejtes_DB...` | run lokal + CI |

**Kufiri i pretendimit për #6:** vazhdimësia e sesionit/formularit provohet VETËM nën kushtin “i njëjti key ring Data Protection” (test-only, direktori e përkohshme). Default-i i framework-ut e ruan key ring-un në profilin e përdoruesit të makinës; **custody/hosting-u production i çelësave mbetet OPEN (ADR-025/026)** — nuk pretendohet “cookie-ja mbijeton çdo deploy”.

**Invarianti i përbashkët:** asnjë dështim i pjesshëm nuk krijon sesion, kod ose token të paautorizuar; rikuperimi nuk ringjall kod apo consent të konsumuar; telemetria e kufirit mbart vetëm klasën e exception-it dhe trace id.

## 6. Evidenca

**Lokale (04.09.2026, PostgreSQL sintetik lokal):**

- `dotnet restore --locked-mode` OK; `dotnet build --no-incremental` = **0 warnings / 0 errors**.
- Para C1: Unit 161 · Integration 202 · Architecture 305. **Pas C1: Unit 161 · Integration 209 · Architecture 305 · zero skipped** (baza historike e `main`: 120 / 179 / 302 — numra referues, jo target).
- OpenAPI: kontrata `contracts/openapi/platform-v1.json` dhe klienti i gjeneruar **byte-identikë** (sipërfaqja interaktive është `ExcludeFromDescription`).
- Inventari i endpoint-eve: `connect/authorize` + `connect/token` + metadata publike default; passthrough VETËM për autorizimin; `plain` i hequr nga metodat e challenge-it; discovery pa `id_token_signing_alg_values_supported`, pa UserInfo/logout/introspection/revocation/device/PAR.

**CI (mbi HEAD-in e PR-së):** `build-test`, `gitleaks`, `integrity-linter` — rezultatet raportohen te PR-ja.

## 7. Kufijtë e mbetur OPEN

`openid`/ID token/UserInfo/nonce/full OIDC sign-in · refresh token production · MFA/recovery/email-phone verification policy · logout/back-channel/session/device revocation · audit identity i qëndrueshëm · offline Edge/POS authentication (`PLANET_PILOT_BLOCKER`) · B.3 projection/freshness/reconciliation · Identity roles për tenant RBAC · key custody ADR-025/026 · runtime credential expiry · backlog-u i regjistruar në §9.5 (convention guard i derivuar, GOV-001 parser, §4f wording, SSOT evidence-line, flake-u WebApplicationFactory) — vetëm i regjistruar, jo i implementuar.

## 8. Radha

Fable delta-review → Codex PM/Quality-Gate sipas `PM_REVIEW_PROTOCOL.md` → Human Gate → merge vetëm me autorizim të ri → Notion/Slack vetëm pas merge-it. **PR OPEN — NDALO PARA MERGE.**

## 9. Correction C1 (04.09.2026) — reliability, user-binding, vendimet e Pronarit

### 9.1 Çfarë ishte e mbipretenduar para C1 (HISTORICAL, e korrigjuar)
- Matrica shënonte #3 si “mbulohet nga #2”, #4 me replay të kodit (jo dorëzim të dyfishtë), #6 si `N/A i pjesshëm`, #7 pa provë mbivendosjeje dhe #8 me DB të RE (DROP) si “rikuperim”. Testi i vetëm i DB-së toleronte exception-in që TestServer ia propagonte thirrësit si “sjellje e pranueshme”.
- Realiteti pas C1: çdo skenar ka test ekzakt mbi pipeline-in real (§5); DB e paarritshme prodhon **HTTP 503 të kontrolluar** në çdo fazë (login, consent, shkëmbim kodi), jo exception; ndërprerja është e përkohshme mbi të njëjtën DB.

### 9.2 Çfarë u implementua/testua
- **Kufi i dështimit infrastrukturor** (`InteractiveAuthorizationPipeline.UseIdentityInteractionFailureBoundary`, Presentation; regjistruar PARA `UseAuthentication` në composition root): vetëm `/connect/*` + `/identity/*`, vetëm klasa infrastrukture (`DbException`, timeout, EF-wrap) — jo catch-all; 503 gjenerik (JSON për protokollin, HTML për interaksionin), `no-store`, log vetëm me klasën e exception-it + trace id.
- **Klasifikim i ndarë në validimin e cookie-t** (Infrastructure): identitet i pavlefshëm → refuzim + revokim; DB e paarritshme → refuzim VETËM i kërkesës (sesioni nuk revokohet; validimi i plotë rifillon pas rikthimit).
- **User-binding para antiforgery-t** (`UseIdentityInteractiveSession`, Presentation; PAS autentikimit, PARA antiforgery-t; vetëm login + authorize, kurrë `/connect/token`, kurrë API): principali i validuar i cookie-t bëhet efektiv në `HttpContext.User`, prandaj antiforgery token-i lidhet me identitetin e sesionit (subjekt + stamp). Skema default mbetet OpenIddict Validation (pohim eksplicit mbi `AuthenticationOptions` në test); cookie-ja nuk autentikon API-në bearer.
- Teste: 6 teste të reja reliability (`IdentityInteractiveReliabilityTests`), test i user-binding-ut, rikthim i testit të DB-së në kontratë 503 ekzakte + rikuperim, pohim i skemës default; helper-a test-only: ndërprerje/rikthim i DB-së sintetike, cookie container i ndarë mes host-eve, matje intervalesh, key ring test-only (`DataProtectionKeyDirectory`).
- **Klasifikim i user-binding-ut:** përmirësim i kufizuar (defense-in-depth), **jo bllokues** — nuk pretendohet shfrytëzim i provuar mbi baseline-in; provat janë regresion mbi kodin e korrigjuar.

### 9.3 Përjashtimi Mediator — vendim i datuar i Pronarit (04.09.2026)
> “Për B.2b autorizohet përjashtim i kufizuar nga kërkesa IMediator, vetëm për endpoint-et ekzistuese login/consent. Zero refaktor dhe zero paketë në C1. ADR-029 mbetet në fuqi; migrimi i AccessControl mbetet Increment C. Përjashtimi nuk autorizon të njëjtin model për module të tjera.”

Scope-i i përjashtimit: `InteractiveAuthorizationEndpoints` (login GET/POST, authorize GET/POST) → thirrje direkte te `InteractiveSignInHandler` / `InteractiveConsentHandler`. Regjistruar te ADR-029 (shënim i datuar), Convention (pointer i kufizuar), Tracker (rreshti ADR-029). Zero ADR/ID/gate i ri; rregulli i përgjithshëm `IMediator` i pandryshuar; AccessControl i paprekur (migrimi = Increment C, tashmë i planifikuar).

### 9.4 Kufijtë që mbeten jashtë B.2b
`openid`/ID token/UserInfo/full OIDC · refresh production · MFA/recovery/verification · logout/revocation · audit identity · offline auth · B.3 · Identity roles për tenant RBAC · key custody ADR-025/026 (përfshirë ruajtjen production të key ring-ut) · runtime credential expiry.

### 9.5 Backlog i regjistruar (VETËM regjistrim — zero implementim, zero PR housekeeping, zero ID i ri)
1. **Convention guard i derivuar mbi modulet Presentation**, me përjashtime kalimtare të emërtuara, të datuara dhe të lidhura me vendimet përkatëse (sot: guard-i pin-on `PlatformStatus` me emër fiks; përjashtimi B.2b i §9.3 është hyrja e parë e listës kalimtare).
2. **GOV-001 parser follow-up** — statusi ekzistues OPEN / NOT IMPLEMENTED / NOT AUTHORIZED (Tracker `GOV-001`).
3. **Runtime credential expiry** — OPEN / UNKNOWN (IASD, B.2a Record §15); scope-i i implementimit dhe çdo ndryshim production kërkojnë autorizim të veçantë.
4. **Formulimi §4f (threat model)** — ENG-006 Record C7 §4f; wording FINAL i ENG-006 mbetet CURRENT.
5. **SSOT evidence-line (HP-EVIDENCE-METRIC, reflag i shpjeguar i Commit A të PR #31)** — mbetet REVIEW i shpjeguar në baseline-in e linter-it.
6. **Flake-u `WebApplicationFactory`** (`Production_clock_skew...ObjectDisposedException`, CI post-merge i PR #31) — “observed; not reproduced on diagnostic rerun”, pa root cause të pretenduar.
7. **Klasifikimi i dështimeve infrastrukturore me mbështjellje më të thellë** (vëzhgim review-i pas C1: `IsInfrastructureFailure` sheh vetëm një nivel `InnerException`; një `DbException` i mbështjellë dy herë do të kalonte si dështim jo-infrastrukturor → 500 i papritur) — VETËM i regjistruar; zero implementim/test në C2.

## 10. Correction C2 (05.09.2026) — vetëm teste/evidencë (production diff = bosh)

### 10.1 Çfarë ndryshoi (vetëm `IdentityInteractiveLab.cs`, `IdentityInteractiveReliabilityTests.cs`, ky Record)
- **R7 — state nga përgjigjja reale:** `IssueCodeAsync` lexon `state`-in nga redirect-i i consent-it dhe e krahason EKZAKT me atë të kërkesës; mungesa ose mospërputhja dështon lëshimin. Signatura e pandryshuar (zero migrim call-site-esh); pohimi vlen për secilin nga katër flow-t konkurruese (dhe për çdo thirrës tjetër të helper-it).
- **R7 — ndarje e provuar dhe shkaqe të izoluara:** faza konkurruese (`Task.WhenAll`, 4 sesione/klientë/state/challenge) lëshon kodet dhe i përfundon POZITIVISHT me kodin e vet të paprekur (4 token-a jo-boshë, të ndryshëm); kontrollet negative bëhen PAS saj, sekuencialisht, mbi kode TË REJA të pakonsumuara: “vetëm verifier i gabuar” (klient/redirect të saktë, përdorim i parë) dhe “vetëm klient i gabuar” (verifier/redirect të saktë, përdorim i parë) → 400 pa `access_token`. Rasti i vjetër (kod i konsumuar + verifier tjetër) nuk përdoret më si provë binding-u; suksesi pozitiv nuk varet nga ripërdorimi i një kodi pas një tentative të pavlefshme. Pohimi i mbivendosjes ruhet mbi snapshot-in e intervaleve `/connect/*` të fazës konkurruese. **Dallim i deklaruar:** provohen “kode të lëshuara dhe të përfunduara nga flow konkurruese”; kërkesat negative NUK u ekzekutuan njëkohësisht dhe nuk pretendohen të tilla.
- **R6 — parakusht real dhe status ekzakt:** para ruajtjes së formularit në host-in A kërkohet 200 + fusha antiforgery me vlerë jo-bosh (formular real, jo i rindërtuar); pas nisjes së host-it B me key ring tjetër: sesioni kërkon login të ri (ruajtur) dhe formulari i ruajtur → **400 ekzakt, zero kod** (statusi real i vëzhguar = 400; pohimi i dobët `!= 302` u hoq).
- **R3:** `Content-Type: application/json`, trupi parse-ohet si JSON, objekt me `error` = `temporarily_unavailable`, pa `access_token`; 503 + `no-store` + zero detaj të ruajtura.
- **R8:** pas 200 të flow-it të ri kërkohet access token real jo-bosh; rikuperimi i së njëjtës DB dhe mosringjallja e kodit të konsumuar të ruajtura. R4 i pandryshuar.

### 10.2 Incidenti “detached HEAD” (04.09.2026, para C1) — regjistrim faktik
- **Fakti i provuar nga reflog-u:** hyrja `HEAD@{0}: checkout: moving from feat/f1-b2b-interactive-authorization to 2315a0d` në worktree-n e builder-it, pa asnjë komandë checkout të ekzekutuar nga builder-i; HEAD u ri-lidh me branch-in në të njëjtin SHA pa ndryshim tree/histori.
- **Deklarimi i autorësisë:** Fable (reviewer) ka deklaruar se checkout-i i review-t të tij në këtë worktree e shkaktoi. Reflog-u provon VETËM ngjarjen (checkout → SHA) dhe kohën, jo autorin; deklarimi nuk përbën autorizim retroaktiv.
- **Dispozicioni procedural** (reviewer-at nuk duhet të ndryshojnë checkout-in/file-t në worktree-n e builder-it gjatë punës) mbetet i Pronarit në Human Gate.

### 10.3 Çfarë NUK ndryshon në C2
Përjashtimi Mediator (§9.3), user-binding-u, kufiri 503 production, endpoint-et, grant-et, statuset e incrementeve. Vëzhgimi për mbështjellje më të thellë të exception-eve = follow-up i regjistruar (§9.5 #7), pa implementim.

## 11. Closure post-merge (05.09.2026) — B.2b = COMPLETED / MERGED

**Burimi i autorizimit:** koment i Pronarit në PR #33 — https://github.com/blendsylqevci/idata-platform/pull/33#issuecomment-5547599082 (HG-PR33-MERGE, 05.09.2026; koment i krijuar 2026-09-04T23:25:36Z para merge-it, i përditësuar 23:39:31Z pas merge-it me receipt-in post-merge — i njëjti koment, i njëjti autor). Statuset pending / “PR OPEN — NDALO PARA MERGE” / dispozicioni i hapur i §10.2 në §§1–10 janë HISTORI (gjendja para merge-it), të superseduara nga ky seksion; §§1–10 mbeten byte-identike.

### 11.1 HG-PR33-MERGE — i ekzekutuar
- **Review-t:** Fable delta-review dhe Codex PM/Quality-Gate = APPROVE mbi head-in e miratuar `788b4e1c0ef12a10038393fa91e54e053a33fe2e` (SHA-bound). Precheck-u i ekzekutimit para merge-it (PR-ja atëherë ende e hapur me mergedAt=null; head/base ekzakt, `main` ekzakt, mergeState CLEAN, build-test/gitleaks/integrity-linter SUCCESS mbi head, worktree tracked-clean) është regjistruar në komentin e autorizimit.
- **Merge:** metoda e kërkuar dhe e ekzekutuar = SQUASH (GitHub API); squash-commit `a6cc98c6c4b991e868ce370450a5ed855c2e3bf0`; parent i vetëm = base i autorizuar `6554625f12f0fb1cf2b74d8e18b61d11d159da54`; mergedAt `2026-09-04T23:26:20Z` (05.09.2026 01:26:20 Europe/Belgrade); `main` HEAD live = origin/main = squash-commit-i.
- **Paritet:** tree(squash) = tree(head i miratuar) = `30cfa65d7b9cc94f74856006baa202951f2c36ce`; diff squash↔head = bosh; diff base→squash = saktësisht **45 file, +3564/−59** (10 docs · 20 src · 15 tests), identik me base→head të miratuar; zero ndryshim jashtë PR-së.

### 11.2 Dispozicioni procedural — CLOSED / RECORDED / ZERO PRECEDENT
- **Incidenti detached HEAD (04.09.2026, §10.2) dhe commit-i i bundluar C1 (`0f74b98`, §9):** Pronari i mbylli si **CLOSED / RECORDED / ZERO PRECEDENT** — pa autorizim retroaktiv dhe pa rishkrim historie (zero amend/rebase/force-push; commit-i i bundluar mbeti siç është). Vendimi nuk autorizon checkout ose ndryshime nga reviewer-at në worktree-n e builder-it.
- **Deklarimi i autorësisë** së incidentit (§10.2) mbetet deklarim: **nuk bëhet provë e re e reflog-ut** — reflog-u provon vetëm ngjarjen (checkout → SHA) dhe kohën, jo autorin.

### 11.3 Evidenca post-merge mbi squash-in real
- **CI** run 33929505121 (workflow CI, push `main`, `a6cc98c`) = **SUCCESS**: Integration **209/209** · Unit **161/161** · Architecture **305/305**; Failed 0 / Skipped 0; build 0 warnings / 0 errors; drift-check dhe Production OpenAPI regression PASS (kontrata byte-identike). Annotation jo-bllokues: deprecation e Node.js 20 në dy actions.
- **Secret-scan** run 33929505106 (gitleaks) = **SUCCESS** mbi squash-in.
- **Linter lokal** (integrity-linter) mbi commit-in REAL squash — `--base 6554625 --head a6cc98c --docs-ref a6cc98c`: DOC-CORE + HISTORY-PRIV **EVALUATED**, exit 0, **ERROR 0 / WARNING 0 / REVIEW 21** (baseline), pa INCOMPLETE. Governance workflow **nuk ka trigger push/main**; profilet pr-body/scope pa kontekst PR = **NOT_EVALUATED** (jo PASS i ri); kontrolli pre-merge i integrity-linter mbi head-in e autorizuar ishte SUCCESS.

### 11.4 Dështime post-merge të raportuara VEÇMAS — Dependabot Updates (FAILED, të pazgjidhura)
Pesë job-e automatike “Dependabot Updates” (event `dynamic`, mbi squash-in) përfunduan **FAILURE** me `security_update_not_possible`: `qs` (run 33929513294) · `fast-uri` (run 33929512241) · `nanoid` (run 33929512660) · `@hono/node-server` (run 33929512606) · `brace-expansion` (run 33929512112). **Nuk janë dështime build-test** dhe **nuk paraqiten si të zgjidhura**: zero rerun, zero ndryshim paketash/overrides/lockfile, zero dismiss alerts, zero merge i PR #22. Shfrytëzueshmëria runtime NUK nxirret nga metadata e Dependabot; nuk citohet numër alert-esh. Dependency triage/remediation = hap i veçantë me goal/prompt të ri.

### 11.5 Follow-up-et e C2 — OPEN / NON-BLOCKING / NOT IMPLEMENTED / IMPLEMENTATION NOT AUTHORIZED (regjistruar 05.09.2026)
Verifikim VETËM statik mbi kodin e merge-uar (`a6cc98c`, `tests/platform/Idata.Platform.IntegrationTests/`), zero probe/test i ri/ndryshim testi. Shtëpia e vetme e këtyre pikave është ky §11 (zero backlog paralel në dokumente të tjera; zero ID/gate/afat i ri; §9.5 mbetet historik i pandryshuar). Regjistrimi nuk rihap review-in production, C1/C2 apo ndonjë C3 të PR #33.

1. **R4 — “verifier tjetër” mbi kodin `third`:** `IdentityInteractiveReliabilityTests.cs`, metoda `R4_dorezimi_i_dyfishte_i_consent_it_prodhon_dy_autorizime_te_ndara_nje_perdorimshe` (rreshtat 130–134): kodi `third` lëshohet më vete (`IssueCodeAsync`, challenge tjetër) dhe shlyhet me verifier të gabuar → 400. Kodet e dy dorëzimeve të të njëjtit consent shlyhen vetëm me verifier-in e saktë + replay (rreshtat 119–126), jo me verifier të gabuar/munguar. **Boshllëk i kufizuar mbulimi, jo defekt production i provuar.**
2. **R7 — negativet mbi kode të reja sekuenciale:** metoda `R7_flow_e_konkurruese_me_klient_state_dhe_challenge_te_ndryshem_mbeten_te_ndara`: pas fazës konkurruese (A, `Task.WhenAll`, rreshtat 326–338) kontrollet negative (B1 vetëm verifier i gabuar, rreshtat 356–363; B2 vetëm klient i gabuar, rreshtat 365–372) ushtrohen mbi kode TË REJA të lëshuara sekuencialisht; **nuk provohet kryqëzimi i binding-ut `code[i] + verifier[j]` mes kodeve të lëshuara në fazën konkurruese.** Matrica §5 dhe §10.1 e deklarojnë këtë kufi; nuk shndërrohet në overclaim apo C3.
3. **R7 — overlap pa filtër POST:** snapshot-i `concurrentRequests` (rreshtat 341–345) filtron `interval.Path.StartsWith("/connect/")` pa kusht metode; `RequestTimingHandler` (`IdentityInteractiveLab.cs`, rreshtat 289–300) regjistron vetëm `(Path, Start, End)` pa metodë HTTP — pra edhe GET `/connect/authorize` hyn në matjen e mbivendosjes. **Matja veçmas mbi POST = përmirësim i mundshëm, jo garanci e provuar.**

**Të paimplementuara nga ky sync (status i pandryshuar, zero autorizim):** GOV-001 parser follow-up, convention guard i derivuar (§9.5 #1), runtime credential expiry, §4f, SSOT evidence-line, flake `WebApplicationFactory`, klasifikimi më i thellë i exception-eve (§9.5 #7); dependency triage (§11.4).

### 11.6 Statuset kanonike pas merge-it
B.2b = **COMPLETED / MERGED**; hapi i fundit i mbyllur = **B.2b** (jo B.2a). IASD §9 **E2 = COMPLETED / MERGED VETËM për kontratën OAuth 2.0 Authorization Code + PKCE** — full OIDC sign-in (`openid`/ID token/UserInfo/nonce), refresh production, MFA/recovery/verification, logout/session/device revocation, offline Edge/POS auth (`PLANET_PILOT_BLOCKER`), audit identity, key custody ADR-025/026, runtime credential expiry = OPEN; **E5 = PARTIAL** (kontributi i provuar i B.2b i merge-uar). **B.2 dhe Increment B = ACTIVE / NOT COMPLETE; B.3 = NOT STARTED / BLOCKED; F1 = ACTIVE / NOT COMPLETE; production Go = BLOCKED.** Asnjë B.2c/increment tjetër nuk shpallet CURRENT apo i autorizuar. Përjashtimi `IMediator` (§9.3) mbetet vetëm për endpoint-et ekzistuese login/consent; ADR-029 dhe migrimi i AccessControl në Increment C nuk ndryshojnë. Ky closure = status-sync docs-only (14 dokumente kanonike; zero kod/test/dependency/linter/workflow); Notion/Slack = veprim i veçantë me receipt-in ekzistues si burim.
