# Identity — Interactive OAuth 2.0 Authorization Code + PKCE (F1 Increment B.2b) — Record

**Statusi:** **IMPLEMENTED IN BRANCH / PENDING_INDEPENDENT_REVIEW** (B.2b = SUBSET i B.2) — branch `feat/f1-b2b-interactive-authorization`, i ndërtuar mbi `main` = `6554625` (squash i PR #32); **PR OPEN — NDALO PARA MERGE**. **B.2 dhe Increment B = ACTIVE / NOT COMPLETE; B.3 = NOT STARTED / BLOCKED; F1 = ACTIVE / NOT COMPLETE; production Go = BLOCKED.** Zero pretendim `production-ready`, zero pretendim full OIDC sign-in, zero pretendim offline authentication.
**Data:** 04.09.2026 · **Pronësia:** Tech (builder: Claude Code) · **Autoriteti normativ:** ADR-028 (ACCEPTED / EFFECTIVE) + ADR-029 (Application Architecture Convention) + `Identity_Authority_Store_Decision.md` §9
**Lidhje:** `Identity_Authority_Credentials_Validation_B2a_Record.md` (B.2a — authority/credentials/validim lokal) · `Identity_Store_Foundation_B1_Record.md` (B.1 store) · `Reliability_Gate.md` (§3 matrica 8-skenarëshe, §4 kolonat e detyrueshme) · `../../PM_REVIEW_PROTOCOL.md` v2.5 §5 + §8 · `../00-ssot/iData_Plus_SSOT.md` v2.39
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
| Rrëmbim sesioni pas ndryshimit të kredencialit | validim i stamp-it të sigurisë në ÇDO kërkesë; mospërputhje ose DB e paarritshme → refuzim + sign-out | test i rrotullimit të stamp-it |
| Cookie e përdorur si bearer | skema default e API-së mbetet validimi OpenIddict; cookie-ja pranohet VETËM në skemën e vet | test: cookie te `[Authorize]` → 401; te AccessControl → 403 |
| CSRF mbi login/consent | antiforgery i detyrueshëm nga middleware-i për çdo endpoint me form binding | test: POST pa token → 400, zero cookie, zero kod |
| Open redirect | return URL pranohet VETËM lokale, pa fragment/kontroll/backslash dhe me path EKZAKT të endpoint-it të autorizimit | teste unit + integrimi (4 variante) |
| XSS në consent/login | çdo vlerë dinamike enkodohet (`HtmlEncoder`); zero JavaScript, zero asset i jashtëm | test me `DisplayName` armiqësor |
| Zgjerim scope-i nga formulari | `GrantAsync` lëshon kod VETËM për scope-t e kërkesës së validuar nga serveri; formulari nuk mbart scope | dizajn + rivalidim i plotë i kërkesës në POST |
| Kod i vjedhur / i riluajtur | PKCE `S256` i detyrueshëm (server-wide, edhe kur klienti s'e kërkon), kod një-përdorimsh i lidhur me klient/redirect/challenge | teste: verifier i munguar/gabuar, replay, klient/redirect tjetër |
| Algoritëm JOSE i ndaluar mbi artefaktin e serverit | gate para çdo dekriptimi: `alg`/`enc` sipas allowlist-it, header i keqformuar → refuzim OAuth | test me payload të ri-enkriptuar `RSA1_5`/`enc` i dobët/header i keqformuar |
| Rrjedhje informacioni në gabime | të gjitha refuzimet janë klasa gjenerike; zero username/kredencial/kod/token në trup ose log | asertime `error` pa detaje; zero 500 |

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

## 5. Reliability Gate — matrica e dështimeve

| # | Failure scenario | Expected behavior | Data safety | User-visible | Recovery | Automated test | Evidence |
|---|---|---|---|---|---|---|---|
| 1 | DB e paarritshme PARA login-it | verifikimi kthen `Unavailable` → 503 i kontrolluar | zero sesion, zero kod, zero mutim | faqe gjenerike “retry later” | rikthimi i DB-së → login normal | `DB_e_paarritshme_nuk_leshon_kurre_sesion_ose_kod` | run lokal + CI |
| 2 | DB e paarritshme GJATË consent-it | validimi i sesionit dhe/ose validimi i klientit dështon → zero faqe consent-i, zero kod | zero kod i lëshuar | ridrejtim te login ose gabim i kontrolluar | rikthim → flow i ri | i njëjti test (faza 2) | run lokal + CI |
| 3 | DB e paarritshme gjatë shkëmbimit të kodit | token endpoint-i nuk gjen/valido entry-n → zero token | kodi mbetet i pakonsumuar ose i konsumuar pa token; zero gjendje gjysmake | gabim OAuth | rikthim → kodi skadon vetë | mbulohet nga #2 + një-përdorimshmëria | run lokal |
| 4 | Dërgim i dyfishtë i consent-it | çdo dërgim = kërkesë e re autorizimi, e rivaliduar plotësisht | çdo kod i lidhur me klient/redirect/challenge dhe një-përdorimsh | ridrejtim normal | pa veprim | `Kodi_lidhet_me_verifier...` (replay) | run lokal + CI |
| 5 | Replay i kodit të autorizimit | përdorimi i dytë refuzohet | zero token i dytë | gabim OAuth | pa veprim | i njëjti test | run lokal + CI |
| 6 | Restart mid-flow | cookie-ja mbetet e vlefshme (data protection e host-it); kodi/entry-t mbeten në DB | zero token i paautorizuar | rifillim normal | flow i ri | **N/A i pjesshëm** — restart-i i procesit mbulohet nga `IdentityAuthorityStartupBoundaryTests` (B.2a) dhe nga rrotullimi i stamp-it; mbetet OPEN si test i dedikuar | shënim i mëposhtëm |
| 7 | Dy kërkesa paralele me klient/state/challenge të ndryshëm | secila kërkesë ka kodin dhe `state`-in e vet, të palidhur | zero përzierje kodesh | dy redirect-e korrekte | pa veprim | i njëjti test (kode të shumëfishtë, klientë të ndryshëm) | run lokal + CI |
| 8 | Rikuperim i DB-së dhe kërkesë e re | flow-i i plotë funksionon sërish; sesionet e vjetra mbeten të refuzuara nëse stamp-i ndryshoi | zero ringjallje e kodit/consent-it të konsumuar | login normal | automatik | fluksi i plotë + rrotullimi i stamp-it | run lokal + CI |

**Shënim §5.1 për #6:** skenari **nuk** është PASS — është `N/A i pjesshëm` me mbulim të deklaruar (kufiri i startup-it fail-closed i B.2a + revokimi me stamp) dhe mbetet **OPEN** për një test të dedikuar restart-mid-flow; pranimi i N/A-së kërkon konfirmim eksplicit në review (kontrolli #7 i PM protokollit).

**Invarianti i përbashkët:** asnjë dështim i pjesshëm nuk krijon sesion, kod ose token të paautorizuar; rikuperimi nuk ringjall kod apo consent të konsumuar; telemetria mbart vetëm klasë arsyeje dhe correlation id.

## 6. Evidenca

**Lokale (04.09.2026, PostgreSQL sintetik lokal):**

- `dotnet restore --locked-mode` OK; `dotnet build --no-incremental` = **0 warnings / 0 errors**.
- **Unit 161 · Integration 202 · Architecture 305 · zero skipped** (baza: 120 / 179 / 302).
- OpenAPI: kontrata `contracts/openapi/platform-v1.json` dhe klienti i gjeneruar **byte-identikë** (sipërfaqja interaktive është `ExcludeFromDescription`).
- Inventari i endpoint-eve: `connect/authorize` + `connect/token` + metadata publike default; passthrough VETËM për autorizimin; `plain` i hequr nga metodat e challenge-it; discovery pa `id_token_signing_alg_values_supported`, pa UserInfo/logout/introspection/revocation/device/PAR.

**CI (mbi HEAD-in e PR-së):** `build-test`, `gitleaks`, `integrity-linter` — rezultatet raportohen te PR-ja.

## 7. Kufijtë e mbetur OPEN

`openid`/ID token/UserInfo/nonce/full OIDC sign-in · refresh token production · MFA/recovery/email-phone verification policy · logout/back-channel/session/device revocation · audit identity i qëndrueshëm · offline Edge/POS authentication (`PLANET_PILOT_BLOCKER`) · B.3 projection/freshness/reconciliation · Identity roles për tenant RBAC · key custody ADR-025/026 · runtime credential expiry · test i dedikuar restart-mid-flow (Reliability #6) · GOV-001 parser follow-up (statusi ekzistues, i paprekur nga ky increment).

## 8. Radha

Fable delta-review → Codex PM/Quality-Gate sipas `PM_REVIEW_PROTOCOL.md` → Human Gate → merge vetëm me autorizim të ri → Notion/Slack vetëm pas merge-it. **PR OPEN — NDALO PARA MERGE.**
