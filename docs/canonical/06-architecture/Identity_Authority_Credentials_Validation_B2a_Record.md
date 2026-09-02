# F1 Increment B.2a — Co-located OpenIddict Authority, Credentials and Local Token Validation — Record

**Statusi:** **IMPLEMENTED IN BRANCH / PENDING_INDEPENDENT_REVIEW** — branch `feat/f1-b2a-openiddict-authority` (nga `origin/main` = merge commit i PR #30 `8d3aa85ddefde74f8999d801f03dd2fac974cfe4`), **PR OPEN — NDALO PARA MERGE**. Merge VETËM pas Fable delta-review → Codex PM/architecture/security review (PM_REVIEW_PROTOCOL v2.3 §5/§9, maksimumi dy correction passes) → Human Gate i Pronarit → autorizim i ri. **B.2 = ACTIVE / NOT COMPLETE; B.3 projection/freshness/reconciliation = NOT STARTED / BLOCKED; Increment B dhe F1 = ACTIVE / NOT COMPLETE; production Go = BLOCKED.** Zero pretendim production-ready, bypass-free apo universal.
**Data:** 03.09.2026 · **Pronësia:** Tech (builder: Claude Code) · **Autoriteti normativ:** ADR-028 (ACCEPTED / EFFECTIVE) + `Identity_Authority_Store_Decision.md` §4/§5/§7/§9 · ADR-029 / `Application_Architecture_Convention.md` · PM_REVIEW_PROTOCOL v2.3 §5 (scope B.2a) + §8 (threat model i harness-it — FINAL) · delivery = PLANET-FIRST (ADR-030 PROPOSED), shared core **product-neutral**.
**Lidhje:** `Identity_Store_Foundation_B1_Record.md` (B.1 store foundation) · `Identity_Dynamic_Reflection_Boundary_ENG006_Record.md` (ENG-006/ENG-008) · `Identity_Untyped_Data_Access_Boundary_ENG007_Record.md` · `Reliability_Gate.md` (§8 më poshtë) · `../00-ssot/iData_Plus_SSOT.md` v2.36.

---

## 1. Kufijtë e increment-it (çfarë ËSHTË dhe çfarë NUK është)

**ËSHTË (i implementuar dhe i provuar në branch):**

- **OpenIddict Server + Validation ASP.NET Core + Validation.ServerIntegration 7.6.0** co-located në API host — authority dhe resource server në të njëjtin proces, validim LOKAL (`UseLocalServer()`), zero endpoint remote, zero introspection.
- **Owner i VETËM i protokollit = `Modules/Identity/Infrastructure`** (konfigurimi i protokollit, regjistrimi server + validation, policy e credentials, validimi i options). **API = composition root** (`Program.cs`: `AddIdentityAuthority(...)` + `UseAuthentication()`/`UseAuthorization()` para endpoint-eve) **pa asnjë referencë direkte OpenIddict/IdentityModel** (guard MemberRefs + AssemblyRefs). **Persistence = zero protokoll** (guard lockfile + AssemblyRefs). Domain/Application = zero OpenIddict.
- **Credentials policy FAIL-CLOSED në startup** (§4) mbi credentials **REALE** të `OpenIddictServerOptions` (jo vetëm mbi konfigurim).
- **Issuance + validim përmes pipeline-it REAL** (§5): token real nga `/connect/token` (klient sintetik test-only, secret i gjeneruar në runtime, VETËM në integration-test host) → middleware-i real i validimit OpenIddict mbron një probe test-only me `[Authorize]`; çdo rast negativ → 401.
- **Authorization Code + PKCE** i DEKLARUAR si flow-i i VETËM production (`AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()`), me endpoint autorizimi **FAIL-CLOSED** (handler që refuzon çdo kërkesë me `unsupported_response_type` — kurrë kod) derisa E2 (login/consent) të implementohet.
- **Zëvendësim i KONTROLLUAR** i guard-eve arkitekturore (§6) + guard-e të reja (MemberRefs, neutralitet produkti, zero material sekret).

**NUK ËSHTË (jashtë scope-it, i padeklaruar si i provuar):**

- login/consent UI, MFA/recovery/lockout, email/SMS, refresh production flow, E2 end-to-end interaktiv — **OPEN**;
- B.3 projection/freshness/reconciliation, audit identity events — **NOT STARTED / BLOCKED**;
- external IdP, OpenIddict Client, Identity roles për tenant RBAC, module PLANET/KABI, Edge/offline, SEF/ATK — **jashtë**;
- **offline Edge/POS authentication:** `UseLocalServer()` **NUK** është evidencë për të (Roadmap §3a: `PLANET_PILOT_BLOCKER`, OPEN);
- key custody/CA/KMS/secret-store/hosting provider — **NUK** zgjidhen këtu (ADR-025/026 OPEN); konfigurimi vetëm përcakton **kufirin** (PFX base64 ose path + PfxPassword nga jashtë).

## 2. Provenance zyrtare (verifikuar, jo hamendësuar)

**Paketat (CPM pin ekzakt në `src/platform/Directory.Packages.props`; Apache-2.0):** `OpenIddict.Server.AspNetCore` 7.6.0 · `OpenIddict.Validation.AspNetCore` 7.6.0 · `OpenIddict.Validation.ServerIntegration` 7.6.0 (ofron `UseLocalServer()` — paketë e veçantë, e konfirmuar nga nuspec + reflection). Transitive të reja në closure: `OpenIddict.Server`, `OpenIddict.Validation` 7.6.0, `Microsoft.IdentityModel.JsonWebTokens`, `Microsoft.IdentityModel.Protocols` 8.19.2 (manifesti i closure-it 31 → 38 paketa; barazia e imponuar nga meta-testi). **Zero** `OpenIddict.AspNetCore` (metapackage — tërheq Client), zero Client, zero DataProtection, zero Duende/IdentityServer/Auth0/Okta.

**API-të e verifikuara me reflection mbi binarët 7.6.0 (probe lokal, i hequr):** builder-ët jetojnë në `Microsoft.Extensions.DependencyInjection`: `OpenIddictServerBuilder` (`SetIssuer`, `SetTokenEndpointUris`, `SetAuthorizationEndpointUris`, `AllowAuthorizationCodeFlow`, `RequireProofKeyForCodeExchange`, `AddSigningCertificate`/`AddEncryptionCertificate`, `AddEphemeral*Key`, `AddDevelopment*Certificate`, `AddEventHandler`, `Configure`), `OpenIddictServerAspNetCoreExtensions.UseAspNetCore` (Server.AspNetCore), `OpenIddictValidationBuilder` (`AddAudiences`, `SetIssuer`, `EnableTokenEntryValidation`, `Configure`), `OpenIddictValidationServerIntegrationExtensions.UseLocalServer` (ServerIntegration), `OpenIddictValidationAspNetCoreExtensions.UseAspNetCore`; skema e validimit = `"OpenIddict.Validation.AspNetCore"`; `OpenIddictValidationOptions.TokenValidationParameters : Microsoft.IdentityModel.Tokens.TokenValidationParameters`; `OpenIddictServerOptions.SigningCredentials : List<SigningCredentials>` / `EncryptionCredentials : List<EncryptingCredentials>`; `OpenIddictServerEvents.HandleAuthorizationRequestContext` / `HandleTokenRequestContext`; `OpenIddictConstants.JsonWebTokenTypes.AccessToken = "at+jwt"`.

**Burimi zyrtar 7.6.0 (openiddict-core, tag `7.6.0`) — fakte që përcaktuan dizajnin:**

- `OpenIddictValidationOptions.TokenValidationParameters` default: `ClockSkew = TimeSpan.Zero`, `ValidateLifetime = false`, `ValidateAudience = false`, `TypeValidator` që kërkon `at+jwt` për access token (nga `ValidTypes`).
- `OpenIddictValidationHandlers.Protection.ResolveTokenValidationParameters`: klonon parametrat e options; vendos `ValidIssuers`/`IssuerSigningKeys`/`ValidTypes` — **nuk** prek `ValidateLifetime`/`ClockSkew`.
- `RestoreTokenEntryProperties` (kur token-entry validation është aktiv): rikthen nga entry-i i store-it `CreationDate`, **`ExpirationDate`**, `AuthorizationId`, `TokenId`, **`TokenType`** — entry-i është **AUTORITATIV** mbi claims-at e JWT-së.
- `ValidateExpirationDate`: refuzon kur `exp + ClockSkew < now` (`TimeProvider`).
- **Asnjë handler nuk validon `nbf`** — OpenIddict nuk e ka not-before në modelin e vet (lëshon vetë token-at). Prandaj D2 (§9).
- Subjektet e certifikatave development: `CN=OpenIddict Server Signing Certificate` / `CN=OpenIddict Server Encryption Certificate` (klasifikimi i policy-së).
- Rregullat e konfigurimit të server-it (`OpenIddictServerConfiguration`): ID0076 ≥1 grant/response type; ID0077 authorization endpoint i detyrueshëm për code flow; ID0079 token endpoint i detyrueshëm; ID0085 ≥1 encryption credential; ID0086 ≥1 signing credential asimetrik — të gjitha të plotësuara nga wiring-u.
- Tool-i i gjenerimit build-time të OpenAPI (`Microsoft.Extensions.ApiDescription.Server` → `dotnet-getdocument` → `GetDocument.Insider`): e starton `Program`-in deri te `IHostApplicationLifetime.ApplicationStarted` brenda procesit të vet, pa asnjë sinjal design-time (as env, as arg përveç `--applicationName`) — provuar me stack trace (`app.Run()` → `Host.StartAsync` → `StartupValidator`) dhe me strings mbi binarin. Prandaj D1 (§9).

## 3. Arkitektura e implementuar

**File-t production (owner):** `src/platform/Modules/Identity/Infrastructure/IdentityAuthorityOptions.cs` (konfigurim product-neutral: `Identity:Authority` — `Issuer`, `Audiences[]`, `AllowedAlgorithms[]`, `ClockSkewSeconds`, `Credentials.Development.{Signing,Encryption}` = `EphemeralKey` | `DevelopmentCertificate`, `Credentials.Signing[]`/`Encryption[]` = `{Pfx (base64) | PfxPath, PfxPassword}`; **zero default production**, zero vlerë produkti, zero sekret në repo) · `IdentityCredentialsPolicy.cs` (policy PURE: `Classify` persistent/development/ephemeral, `Evaluate`, `IsValidPersistent`, `LoadPersistentCertificate` fail-closed; `IdentityCredentialsPolicyException` me klasë + identifikues jo-sekret vetëm) · `IdentityAuthorityServiceCollectionExtensions.cs` (`AddIdentityAuthority(IServiceCollection, IConfiguration, IHostEnvironment)`; startup state i dështimeve të shtyra; `IdentityCredentialsStartupGuard : IHostedService`; predikati design-time) · csproj (3 PackageReference të reja) · `src/platform/Idata.Platform.Api/Program.cs` (thirrja + middleware).

**Wiring-u (owner):**

- **Server:** `SetIssuer` (nga konfigurimi i validuar) · `SetAuthorizationEndpointUris("connect/authorize")` · `SetTokenEndpointUris("connect/token")` · `AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()` · credentials sipas policy-së · `UseAspNetCore()` **pa passthrough** dhe **pa** `DisableTransportSecurityRequirement` · handler `HandleAuthorizationRequestContext` fail-closed (`Reject(unsupported_response_type)`).
- **Validation:** `UseLocalServer()` · `UseAspNetCore()` · `EnableTokenEntryValidation()` · `AddAudiences(...)` · `Configure`: `ClockSkew` nga konfigurimi (0..300s), `ValidateLifetime = true` (D2) · `PostConfigure<OpenIddictValidationOptions>`: `ValidAlgorithms` = algoritmet e firmës nga konfigurimi ∪ `{Alg, Enc}` të credentials REALE të enkriptimit (D3).
- **Authentication:** `AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)` + `AddAuthorization()`; `Program.cs`: `app.UseAuthentication(); app.UseAuthorization();` para `MapControllers()`/`MapAccessControl()`.
- **Startup FAIL-CLOSED (pika e VETME):** `IdentityCredentialsStartupGuard.StartAsync` → (1) dështimet e shtyra të regjistrimit (konfigurim i pavlefshëm/munguar, PFX i pangarkueshëm, persistent i skaduar/not-yet-valid/pa çelës privat, development jashtë Development, zgjedhje e panjohur, set i munguar); (2) `IdentityCredentialsPolicy.Evaluate` mbi `OpenIddictServerOptions.SigningCredentials/EncryptionCredentials` REALE. `Build()` i host-it kalon (gjenerimi build-time i OpenAPI), `StartAsync` dështon. Përjashtimi i vetëm, eksplicit dhe i testuar: host-i design-time (D1).
- **Mesazhet/log-et:** vetëm klasa e dështimit + `lloj[indeks]` (p.sh. `persistent-expired:signing[0]`, `persistent-unloadable(FormatException) (encryption[2])`); kurrë PFX, password, path, thumbprint, bajte çelësi; provuar me unit + integration tests.

**F1.1 i paprekur:** tenant RBAC DB-backed deny-by-default mbetet siç ishte; principal-i vjen nga handler-i i validimit (pa token → bosh); override-i test-only (`X-Test-*`) vetëm në test assembly; suite-i i plotë F1.1 GREEN (§10).

## 4. Matrica e credentials (§5.2 IASD) — evidencë e ekzekutuar

**Integration (host REAL `Program`, `IdentityAuthorityCredentialsStartupTests`, 19 teste, zero DB):**

| # | Environment | Konfigurim | Rezultati i pritur | Test | Rezultat |
|---|---|---|---|---|---|
| 1 | Development | Signing → EphemeralKey · Encryption → EphemeralKey | START | `Development_ephemeral_eksplicit_per_te_dy_setet_starton` | PASS |
| 2 | Development | Signing/Encryption → DevelopmentCertificate | START | `Development_certifikate_development_eksplicite_starton` | PASS |
| 3 | Development | persistent signing + EphemeralKey encryption (mixed) | START | `Development_mixed_persistent_signing_dhe_ephemeral_encryption_starton` | PASS |
| 4 | Development | vetëm signing (pa encryption) | FAIL `encryption-missing` | `Development_pa_encryption_deshton_fail_closed` | PASS |
| 5 | Development | asnjë zgjedhje (zero fallback implicit) | FAIL `signing-missing`,`encryption-missing` | `Development_pa_asnje_zgjedhje_deshton_asnje_fallback_implicit` | PASS |
| 6 | Development | zgjedhje e panjohur | FAIL `development-signing-unknown-choice` | `Development_zgjedhje_e_panjohur_deshton` | PASS |
| 7 | Production | Signing/Encryption → EphemeralKey | FAIL `development-credentials-outside-development` | `Production_ephemeral_deshton` | PASS |
| 8 | Production | Signing/Encryption → DevelopmentCertificate | FAIL (po ajo klasë) | `Production_certifikate_development_deshton` | PASS |
| 9 | Staging (i panjohur ≠ Development) | Signing/Encryption → EphemeralKey | FAIL (po ajo klasë) | `Staging_ephemeral_deshton_environment_jo_Development_trajtohet_si_production` | PASS |
| 10 | Production | persistent ×2 të vlefshme; ristart me të njëjtat | START ×2 | `Production_persistent_te_vlefshme_starton_dhe_ristarton_me_te_njejtat` | PASS |
| 11 | Production | persistent + EphemeralKey në konfigurim (mixed) | FAIL | `Production_mixed_persistent_konfigurim_plus_ephemeral_konfigurim_deshton` | PASS |
| 12 | Production | persistent të vlefshme + çelës ephemeral i regjistruar **JASHTË konfigurimit** | FAIL `non-development-ephemeral-credential:signing[1]` (faza 2 — policy mbi credentials REALE) | `Production_mixed_me_celes_ephemeral_te_regjistruar_JASHTE_konfigurimit_deshton_nga_policy_mbi_credentials_reale` | PASS |
| 13 | Production | persistent i skaduar | FAIL `persistent-expired:signing[0]` | `Production_persistent_i_skaduar_deshton` | PASS |
| 14 | Production | persistent ende-jo-i-vlefshëm | FAIL `persistent-not-yet-valid:encryption[0]` | `Production_persistent_ende_jo_i_vlefshem_deshton` | PASS |
| 15 | Production | PFX vetëm publik | FAIL `persistent-without-private-key` | `Production_persistent_pa_celes_privat_deshton` | PASS |
| 16 | Production | base64 i pavlefshëm / password i gabuar | FAIL `persistent-unloadable(...)`; mesazhi pa PFX/password | `Production_pfx_i_pavlefshem_ose_password_i_gabuar_deshton_pa_rrjedhje_materiali` | PASS |
| 17 | Production | seksioni `Identity:Authority` i munguar | FAIL `configuration:issuer-missing-or-relative`, `audiences-missing`, `allowed-algorithms-missing`, `clock-skew-missing` | `Production_konfigurim_i_munguar_deshton` | PASS |
| 18 | Production | issuer `http:` | FAIL `configuration:issuer-scheme-not-allowed` (Development e lejon) | `Production_issuer_http_deshton_ndersa_Development_e_lejon` | PASS |
| 19 | Production | clock skew 301s | FAIL `configuration:clock-skew-out-of-bounds` | `Production_clock_skew_jashte_kufijve_deshton` | PASS |

**Unit (policy PURE, `IdentityCredentialsPolicyTests`, 11 teste):** klasifikimi (persistent/development/ephemeral/simetrik; subjekt i ngjashëm ≠ development), Development (ephemeral/dev/persistent/mixed OK; set i munguar FAIL; persistent i skaduar/pa çelës privat FAIL edhe në Development), jashtë Development (vetëm persistent të vlefshëm; ephemeral/dev/**mixed** FAIL; missing/expired/not-yet-valid/pa çelës privat FAIL; kufiri `NotAfter` ekzakt), ngarkimi (source-missing/ambiguous/unloadable/password i gabuar → klasë pa material; ngarkim i suksesshëm), validimi PURE i konfigurimit (issuer absolut https — http vetëm Development; audiences/algoritme jo bosh; skew 0..300), neutraliteti (zero default), predikati design-time (FALSE në procesin e testit; TRUE vetëm për entry-in e tool-it zyrtar; pa prefix/contains matching).

## 5. Matrica e validimit të token-ave (§5.1 IASD) — evidencë e ekzekutuar

`IdentityAuthorityValidationTests` (11 teste) mbi **PostgreSQL REAL sintetik** (DB e migruar për çdo test), host `Production` me certifikata persistent sintetike runtime, klient sintetik test-only (`client_credentials` VETËM në test host — guard MemberRefs e ndalon në production), probe `/__b2a/protected` me `[Authorize(AuthenticationSchemes = OpenIddict.Validation.AspNetCore)]` në pipeline-in REAL të `Program`-it (`UseAuthentication`/`UseAuthorization`/`MapControllers`), klient HTTPS pa auto-redirect (kërkesa TLS e OpenIddict mbetet aktive):

| Rasti | Mekanizmi | Rezultati | Test |
|---|---|---|---|
| token real → probe | pipeline real (`/connect/token` → JWE at+jwt → validim lokal) | **200** me `sub` = client id | `Token_real_hap_probe_e_mbrojtur_...` |
| pa token · garbage · tampered · audience i gabuar (token real për resource tjetër) | validim | **401** (+ `WWW-Authenticate: Bearer`) | po ai |
| issuer i gabuar (host tjetër, të njëjtët çelësa, issuer tjetër — token real) | `ValidIssuers` | 200 te lëshuesi, **401** te host-i ynë | `Issuer_i_gabuar_dhe_celes_i_panjohur_401_...` |
| çelës firme i panjohur (host tjetër me signing cert tjetër, të njëjtin enkriptim/issuer) | `IssuerSigningKeys` | **401** | po ai |
| algoritëm jashtë allowlist-it (`AllowedAlgorithms=[PS256]` për token RS256) / allowlist me RS256 | `ValidAlgorithms` (D3) | **401** / **200** | `Algoritmi_jashte_allowlist_401_dhe_allowlist_me_RS256_200` |
| crafting me çelësat sintetikë të testit — kontroll i pandryshuar | besnikëria e crafting-ut | **200** | `Crafting_kontroll_typ_iss_exp_nbf_dhe_clock_skew_i_kufizuar` |
| `typ` = JWT (jo at+jwt) · `iss` i ndryshuar (çelësa realë) · çelës i panjohur · PS256 me çelës real | `ValidTypes` / issuer / signature / allowlist | **401** ×4 | po ai |
| `exp` −30s / `nbf` +30s me skew 0 | lifetime i IdentityModel (D2) | **401** / **401** | po ai |
| `exp` −30s / `nbf` +30s me skew 60 (brenda) · −120s / +120s (jashtë) | skew i kufizuar | **200** / **200** · **401** / **401** | po ai |
| skadimi i entry-t të store-it (token real, JWT valid; `ExpirationDate` −30s) me skew 0 / skew 60 / −120s me skew 60 | `RestoreTokenEntryProperties` + `ValidateExpirationDate` (entry AUTORITATIV) | **401** / **200** / **401** | `Skadimi_i_entry_it_te_store_it_eshte_autoritativ_dhe_respekton_skew_in_e_kufizuar` |
| revokim në store (`IOpenIddictTokenManager.TryRevokeAsync`) | token-entry validation | 200 para → **401** pas | `Revokimi_ne_store_refuzon_tokenin_real_401` |
| restart me të njëjtat credentials | persistent + entry në DB | token-i i vjetër **200**, i ri **200** | `Restart_me_credentials_te_njejta_pranon_tokenin_e_leshuar_para_restartit` |
| rotation me overlap: [S1,E1] → [S1,S2 | E1,E2] → [S2,E2] | credentials të shumëfishta (të rinjtë me NotAfter më të vonë) | i vjetri 200 gjatë overlap-it, i riu 200 (dekriptohet VETËM me çelësat e rinj), pas heqjes: i vjetri **401**, i riu 200 | `Rotation_me_overlap_...` |
| endpoint-i i autorizimit me klient code+PKCE të plotë | handler fail-closed | **302** te redirect_uri me `error=unsupported_response_type`, **kurrë** `code=` | `Endpoint_i_autorizimit_fail_closed_...` |
| `grant_type` = password / refresh_token / implicit | server | **400** `unsupported_grant_type` | po ai |
| client secret i gabuar | server | **401** `invalid_client`, secret-i nuk jehohet | po ai |
| Development + ephemeral: issuance + validim; OpenAPI runtime pa `connect/`, pa vlera konfigurimi | — | **200**; dokument i pastër | `Development_ephemeral_leshon_dhe_validon_...` |
| DB e paarritshme për token-entry validation (Reliability §8, skenari 7) | store i paarritshëm | **kurrë 200** (TestServer ri-hedh `PostgresException`; Kestrel → 500; zero principal) | `Db_e_paarritshme_per_token_entry_validation_nuk_kthen_kurre_200_fail_closed` |

**§5.3 — "JWKS unavailable":** N/A për validim LOKAL — `UseLocalServer()` importon konfigurimin/çelësat e server-it në proces; zero burim remote që mund të jetë i paarritshëm. Prova B (§7) tregon që pa `UseLocalServer()` OpenIddict refuzon të validojë fare (`An OAuth 2.0/OpenID Connect server configuration or an issuer URI must be registered`), pra nuk ka fallback të heshtur.

## 6. Guard-et arkitekturore — zëvendësim i KONTROLLUAR + guard-e të reja

**Të zëvendësuara (jo fshirje — coverage forcohet):** `IdentitySecurityDependencyInventory` — `ForbiddenFamilies` pa `OpenIddict.Server`/`OpenIddict.Validation` (mbeten: `OpenIddict.Client`, metapackage `OpenIddict.AspNetCore`, Identity.UI, IdentityServer/Duende/Auth0/Okta); **`ProtocolRoots`** (Server.AspNetCore, Validation.AspNetCore, Validation.ServerIntegration 7.6.0) Direct VETËM te `ProtocolOwnerCanonicalPath` (Identity.Infrastructure); `IsProtocolPackage` → paketë protokolli te owner-i i store-it (Persistence) = gjetje; `ClosureManifest` 31 → 38 (barazi e imponuar); `DirectPackageAuthority` +3 te owner-i. `ConventionEnforcementTests`: lista e ndaluar e teksteve props/csproj e riformuluar (Client/metapackage/UI/providers kudo; rrënjët e protokollit VETËM te owner-i; Persistence csproj zero Server/Validation); owner-i i protokollit mban 3 rrënjët Direct, askush tjetër; Persistence zero protokoll transitiv; rendi normativ me fixture `OpenIddict.Client` + rastet e reja (metapackage i ndaluar; `OpenIddict.Server` te Persistence = protokoll; te owner-i = kalon). `DependencyDirectionTests`: assemblies të protokollit (Server, Server.AspNetCore, Validation, Validation.AspNetCore, Validation.ServerIntegration, IdentityModel Tokens/JsonWebTokens/Protocols) të lejuara VETËM te owner-i + allowlist jo i vdekur (5 të kërkuara REALISHT të referuara); Api zero referencë direkte identity (ekzistues, i gjelbër).

**Të reja (`IdentityAuthorityBoundaryTests`, 6 teste, MemberRefs nga snapshot-et e verifikuara të katalogut — rruga e sanksionuar `ReadVerifiedImage`, call-site parity e C6 e paprekur):** lista e ndaluar përputhet me API-në reale 7.6.0 (asnjë hyrje e vdekur) · **zero** `AllowClientCredentialsFlow`/`AllowPasswordFlow`/`AllowImplicitFlow`/`AllowHybridFlow`/`AllowRefreshTokenFlow`/`AllowDeviceAuthorizationFlow`/`AllowCustomFlow`/`AllowNoneFlow`/`AcceptAnonymousClients`/`EnableDegradedMode`/`Ignore*Permissions`/`DisableAccessTokenEncryption`/`DisableTokenStorage`/`DisableTransportSecurityRequirement`/`Enable*EndpointPassthrough`/`UseIntrospection`/`SetClientId`/`SetClientSecret` në ASNJË assembly production · owner-i referon wiring-un e plotë (code+PKCE, issuer, endpoint-e, credentials, `UseAspNetCore` ×2, `UseLocalServer`, `EnableTokenEntryValidation`, `AddAudiences`, `AddAuthentication`, `AddAuthorization`) · composition root-i thërret `AddIdentityAuthority` + `UseAuthentication`/`UseAuthorization` pa referuar OpenIddict/IdentityModel · **zero `PLANET|KABI`** në `src/platform` (baseline 0; guard i gjallë me fixture) · **zero material sekret** (PEM/base64 i gjatë) në `src/platform` + `tests/platform`, zero `appsettings*.json` në host.

**ENG-006:** manifesti production mbetet **BOSH** — predikati design-time (D1) u shkrua **pa reflection** (`Environment.GetCommandLineArgs()`), pasi `Assembly.GetEntryAssembly()` u kap si `dynamic-unknown-member` nga skaneri dhe një përjashtim do të kërkonte Human Gate. ENG-007: zero raw SQL i ri.

## 7. Provat RED→GREEN (heqje kontrolli, edit-forward, SHA-256 identik pas rikthimit)

**Prova A — policy-ja e credentials (heqja e dallimit të environment-it në 3 pika: regjistrimi `development-credentials-outside-development`, regjistrimi i dev credentials jashtë Development, `Evaluate` pa shkelje non-development):**
RED = Unit **1/11 FAIL** (`Jashte_Development_ephemeral_dev_ose_mixed_deshton`) + Integration startup **5/19 FAIL** (`Production_ephemeral_deshton`, `Production_certifikate_development_deshton`, `Staging_ephemeral_deshton_...`, `Production_mixed_persistent_konfigurim_plus_ephemeral_konfigurim_deshton`, `Production_mixed_me_celes_ephemeral_te_regjistruar_JASHTE_konfigurimit_...`) — saktësisht rreshtat diskriminues; të tjerat GREEN. Rikthim: SHA-256 `fe47c8e3…b96d1c` (extensions) dhe `7f8b7e28…a2f6a` (policy) identikë para/pas (vlerat e atij çasti; file-i i policy-së u ndryshua MË PAS vetëm nga riemërtimi `Password`→`PfxPassword`, §11). GREEN = 11/11 + 19/19.

**Prova B — wiring-u i validimit (heqja e `options.UseLocalServer()`):**
RED = Arch **2 FAIL** (`IdentityAuthorityBoundaryTests.Owner_i_protokollit_referon_wiring_un_e_plote_...`, `DependencyDirectionTests.Identity_paketat_vetem_assemblies_e_aprovuara_...` — ServerIntegration nuk referohet më) + Integration validim **9/10 FAIL** (të gjitha rastet që validojnë token; e vetmja GREEN = endpoint-i i autorizimit/grant-et, që s'validon token) me arsyen zyrtare: *"An OAuth 2.0/OpenID Connect server configuration or an issuer URI must be registered. To use a local OpenIddict server, reference the 'OpenIddict.Validation.ServerIntegration' package and call ... UseLocalServer()"*. Rikthim: SHA-256 `fe47c8e3…b96d1c` identik. GREEN = 31/31 (Arch filter) + 10/10.

## 8. Reliability Gate — matrica e dështimeve për authority-n (kolonat sipas `Reliability_Gate.md` §4)

| # | Failure scenario | Expected behavior | Data safety guarantee | User-visible behavior | Recovery behavior | Test / evidencë |
|---|---|---|---|---|---|---|
| 1 | Internet down | N/A për authority co-located (validim lokal, zero thirrje remote); **offline Edge/POS auth = OPEN** (`PLANET_PILOT_BLOCKER`) | asnjë gjendje e re | 401 për token të pavlefshëm; ndryshe i paprekur | — | dokumentuar (§1) — **NOT TESTED / OPEN** |
| 2 | SEF/ATK down | jashtë scope-it të identity-t | — | — | — | N/A |
| 3 | Printer/peripheral down | jashtë scope-it | — | — | — | N/A |
| 4 | Device crash/offline | token-i mbetet i vlefshëm deri në `exp`/revokim; pa DB nuk validohet (skenari 7) | zero principal pa validim | 401 | ri-autentikim (E2 OPEN) | pjesërisht (§5 restart) |
| 5 | Sync interrupted / retry / idempotency | token-i i njëjtë i paraqitur shumë herë = i njëjti rezultat; revokimi është përfundimtar | zero dublim entry-sh (store OpenIddict) | 200/401 deterministik | — | §5 (revokim, restart) |
| 6 | Server/hub restart | credentials persistent → token-at e mëparshëm pranohen; credentials të munguara/pavlefshme → **startup FAIL** (host-i nuk ngrihet) | zero gjendje gjysmake | shërbimi s'ngrihet (fail-closed) | korrigjim konfigurimi + restart | §4 #10/#13–19, §5 restart |
| 7 | DB unavailable | token-entry validation dështon → **kurrë 200** (401/500), edhe për token valid | zero principal | 401/500 | rikthimi i DB-së; zero ri-lëshim i nevojshëm | `Db_e_paarritshme_...` |
| 8 | Disk/queue full | jashtë scope-it (pa outbox/queue në authority) | — | — | — | N/A |
| + | Credential i skaduar në runtime (pas startup-it) | **NOT TESTED** në B.2a (policy vlerësohet në startup; skadimi në runtime → firma dështon në lëshim/validim sipas IdentityModel) | — | — | rotation me overlap (§5) + restart | **OPEN — follow-up** |
| + | Clock skew | i kufizuar 0..300s nga konfigurimi; jashtë kufirit → startup FAIL | — | 401 jashtë skew-it | — | §4 #19, §5 |

## 9. Devijime / vendime për DISPOZICION (të raportuara eksplicitisht — jo të ratifikuara nga builder-i)

- **D1 — Host-i design-time i gjenerimit build-time të OpenAPI.** Fakt i provuar: `dotnet build` ekzekuton `Program` deri te `ApplicationStarted` brenda `GetDocument.Insider`, pa konfigurim identity, dhe CI-ja e bën këtë nën `DOTNET_ENVIRONMENT=Production` (regresioni i kontratës; workflow i paprekshëm në këtë increment). "Startup FAIL për konfigurim të munguar" dhe "build-time doc-gen pa konfigurim" bien në konflikt të pashmangshëm. Zgjidhja e implementuar: guard-i mbetet pika e vetme fail-closed; **VETËM** kur procesi aktual është tool-i zyrtar (entry `GetDocument.Insider`/`dotnet-getdocument`, i lexuar nga command line-i i procesit — jo environment, konfigurim apo argument aplikacioni, që janë të kontrollueshëm nga deployment-i), guard-i logon warning dhe nuk vlerëson policy-në (host-i nuk shërben trafik). Predikati është PURE dhe i testuar (FALSE për host-in real, host-et e testeve, emra të ngjashëm, bosh). Alternativat e refuzuara: (a) konfigurim identity në csproj/target (jashtë whitelist-it dhe do të lidhte build-in me konfigurim identity), (b) start-then-stop (host-i do të lidhte port-in para dështimit — garanci më e dobët), (c) ndryshim i CI-së (i ndaluar). **Kërkohet dispozicion i Pronarit** (pranim / kërkesë për follow-up p.sh. gjenerim OpenAPI pa startim host-i).
- **D2 — `TokenValidationParameters.ValidateLifetime = true`.** OpenIddict e ka default `false` dhe validon `exp` vetë nga entry-i i store-it; nuk validon `nbf`. Aktivizimi është konfigurim i validator-it REAL (IdentityModel brenda pipeline-it OpenIddict), jo validator paralel: shton kontrollin e `exp`/`nbf` të vetë JWT-së me të njëjtin skew. Token-at e lëshuar nga OpenIddict kalojnë; token-at e falsifikuar me `exp` të kaluar ose `nbf` të ardhshëm refuzohen edhe kur entry-i është valid. **Kërkohet dispozicion** (mbajtje / kthim te default-i i OpenIddict me dokumentim se `nbf` nuk validohet).
- **D3 — Allowlist-i i algoritmeve.** `ValidAlgorithms` i IdentityModel zbatohet edhe mbi `alg`/`enc` të JWE-së; allowlist-i i konfigurimit përfaqëson VETËM firmën, ndërsa algoritmet e enkriptimit derivohen nga credentials REALE të importuara (`PostConfigure` pas ServerIntegration). Provuar: `[PS256]` refuzon RS256; `[PS256, RS256]` pranon.
- **D4 — Host-et ekzistuese të testeve.** Startup-i fail-closed prishi 67 teste ekzistuese (të gjitha me të njëjtën arsye — konfigurim i munguar). Zgjidhja: `PlatformTestHostFactory` (konfigurim identity sintetik eksplicit me certifikata persistent runtime, të vlefshme në çdo environment) zëvendëson `new WebApplicationFactory<Program>()` në 4 file ekzistuese + `AccessControlApiFactory` e trashëgon. Asnjë ndryshim semantik në testet e F1.1/Increment A; suite-i i plotë GREEN.
- **D5 — Validimi i shtyrë vs eager.** Versioni i parë validonte konfigurimin eager në `AddIdentityAuthority` (dështonte `Build()`); u zëvendësua me dështime të shtyra te startup guard (D1). `IValidateOptions<IdentityAuthorityOptions>` mbetet për konsumatorët e `IOptions`.
- **D6 — `X509KeyStorageFlags.DefaultKeySet`** (jo `EphemeralKeySet`, që nuk mbështetet në macOS) për ngarkimin e PFX — portabilitet CI (Linux) + zhvillim lokal.
- **D7 — Parametër test-only `synthetic_audience`** në handler-in test-only të tokenit (parametri standard `audience` validohet nga OpenIddict kundër permissions të klientit) — vetëm në integration-test host.
- **Rreshti 2 i matricës (§4)** krijon certifikata development në store-in `CurrentUser/My` të makinës (sjellja zyrtare e `AddDevelopment*Certificate`) — efekt anësor lokal i dokumentuar.
- **Lockfiles:** u ndryshuan 5 (`Api`, `Identity.Infrastructure`, `ArchitectureTests`, `IntegrationTests`, `UnitTests`) — të gjitha të kërkuara nga `restore --locked-mode` (closure transitive përmes ProjectReference); zero ndryshim tjetër.
- **Zero file jashtë whitelist-it §4**; kompajleri nuk kërkoi asnjë file shtesë.

## 10. Verifikimi §5.5 (një run për secilin, pas kodit final, PostgreSQL REAL sintetik — docker `postgres:16-alpine`, trust, DB efemere `idata_platform_test_*`)

| Hapi | Komanda | Rezultati |
|---|---|---|
| 1 | `dotnet restore src/platform --locked-mode` | exit 0 · 13 projekte |
| 2 | `dotnet build src/platform --no-restore --no-incremental` | **0 Warning(s) · 0 Error(s)** |
| 2b | `DOTNET_ENVIRONMENT=Production dotnet build src/platform/Idata.Platform.Api --no-restore --no-incremental` + `git diff contracts/openapi` | 0/0 · **zero drift** (kontrata byte-identike; endpoint-et OpenIddict nuk hyjnë në dokument) |
| 3 | targeted (Unit `IdentityCredentialsPolicyTests` · Arch `IdentityAuthorityBoundary`+`DependencyDirection`+`ConventionEnforcement` · Integration `IdentityAuthority*`) | **11/11 · 75/75 · 30/30** |
| 4–6 | `dotnet test src/platform --no-build` (Arch + Unit + Integration, si CI) — run-i FINAL mbi kodin e Commit 2 `259f988` (pas riemërtimit §11, me build të freskët `--no-incremental`) | **UnitTests 117/117 · IntegrationTests 150/150 · ArchitectureTests 300/300 — zero skipped** |

**Zero skipped.** Counts: ArchitectureTests **300/300** (294 → +6) · UnitTests **117/117** (105 → +12 në këtë branch: 11 policy + 1 neutralitet) · IntegrationTests **150/150** (119 → +30: 19 startup + 11 validim) · 1 unit ekzistues i paprekur.

## 11. Privatësia / sekretet / linter

- **Hook-u i privatësisë i repo-s** (`scripts/precommit_confidential_guard.sh`, pre-commit — NUK u anashkalua): bllokoi commit-in e parë për pattern-e "assigned credentials" (emër kredenciali i ndjekur nga `=`/`:` dhe vlerë — p.sh. konstanta e passphrase-it sintetik dhe variabla lokale e tokenit) në 4 file testesh sintetike → u riemërtuan identifikuesit (opsioni production `Password` → **`PfxPassword`** — çelës konfigurimi `Credentials:*:{i}:PfxPassword`; teste: `PfxPassphrase`/`passphrase`/`accessToken`), zero suppression, zero `--no-verify`; build i freskët + suite-i i plotë u ri-ekzekutuan mbi kodin final (§10). **Commit 2 = `259f988`** (hook + gitleaks kaluan).
- **gitleaks** (staged, pa pipe, zero suppression; `.gitleaks.toml`/`.gitleaksignore` të paprekura): Commit 2 (kod + teste + deps + lockfiles): **exit 0 — no leaks found** (pas riformulimit të një false-positive `generic-api-key` mbi firmën e një metode të lab-it — parametra të rirenditur, ZERO suppression); Commit 3 (docs): rezultati raportohet në PR body dhe në raportin final (nuk mund të vetë-përfshihet në këtë Record).
- **integrity-linter** DOC-CORE + HISTORY-PRIV (delta + range nga `origin/main`): ekzekutohet pas Commit 3 mbi range-in `origin/main..HEAD` dhe delta-n e çdo commit-i; rezultati (ERROR/WARNING/REVIEW me shpjegimin e çdo rritjeje REVIEW) raportohet në PR body dhe në raportin final — target: ERROR = 0, WARNING = 0, REVIEW pa rritje të pashpjeguar mbi baseline-in 21 (Commit A: 24 = +3 ri-flag HISTORY-PRIV të përmbajtjes ekzistuese të sanitizuar në rreshta të modifikuar).
- Në git/PR/log: zero çelës privat, certifikatë, password, secret statik, token i plotë, authorization header, issuer/client/audience real, `.env`, path privat. Certifikatat/PFX/secret-et e testeve gjenerohen në runtime; log-u diagnostik i lab-it është OFF by default.
- Neutralitet produkti: `PLANET|KABI` në `src/platform` = **0** (guard i gjallë); konfigurimi ka zero vlerë produkti/host/issuer.

## 12. Mapping i acceptance matrix (IASD §9) — statuset e vendosura në këtë branch

| Rreshti | Statusi | Evidencë |
|---|---|---|
| B1 issuer/audience | IMPLEMENTED IN BRANCH / PENDING_INDEPENDENT_REVIEW | §5 |
| B2 signature/unknown key/unavailable | IMPLEMENTED IN BRANCH / PENDING_INDEPENDENT_REVIEW (unavailable = N/A lokal, §5.3; DB e paarritshme → kurrë 200) | §5, §8 |
| B3 rollover/expiry/skew | IMPLEMENTED IN BRANCH / PENDING_INDEPENDENT_REVIEW | §5 |
| C1 matrica e credentials | IMPLEMENTED IN BRANCH / PENDING_INDEPENDENT_REVIEW | §4 |
| C2 restart/rotation/expired-invalid-unavailable | IMPLEMENTED IN BRANCH / PENDING_INDEPENDENT_REVIEW | §4/§5 |
| E2 code+PKCE / grant-e të refuzuara | OPEN — pjesa e provuar: flow i deklaruar + autorizim fail-closed + grant-e të ndaluara të refuzuara | §5 |
| E5 architecture/keys/audit | PARTIAL — B.2a: zëvendësim i kontrolluar + guard MemberRefs + keys jashtë repo/log = IMPLEMENTED IN BRANCH; audit events = B.3 | §6 |

**Asnjë rresht nuk mbyllet** para review-it të pavarur, Human Gate-it dhe merge-it të autorizuar.
