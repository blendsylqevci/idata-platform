# Identity Authority & Store — Decision Specification (ADR-028)

**Statusi:** **ACCEPTED / EFFECTIVE — ADR-028 ACCEPTED (PR #26 merged, squash `305aefd`, 28.07.2026, pas Fable→Codex→Human Gate). IMPLEMENTIMI = INCREMENT B (ACTIVE — **B.1 Identity Store Foundation = COMPLETED/MERGED, PR #28 `d6ddff2`, 02.08.2026**; Pre-B2 Increment 2 / ENG-006 + ENG-008 = **CLOSED-MERGED (PR #30, merge commit `8d3aa85`, 02.09.2026)**; **F1 Increment B.2a — Co-located OpenIddict Authority, Credentials and Local Token Validation = COMPLETED / MERGED (PR #31 squash `42993f0`, 03.09.2026, pas Fable → Codex → HG-PR31-MERGE — Record: `Identity_Authority_Credentials_Validation_B2a_Record.md`)**; **F1 Increment B.2b — Interactive OAuth 2.0 Authorization Code + PKCE = COMPLETED / MERGED** (PR #33 squash `a6cc98c`, 05.09.2026, pas Fable → Codex → HG-PR33-MERGE — Record: `Identity_Interactive_Authorization_B2b_Record.md` §11; E2 = COMPLETED / MERGED VETËM për kontratën OAuth 2.0 code+PKCE); **hapi i fundit i mbyllur = F1 Increment B.2c — OIDC Authorization Code Sign-in, provider-side = COMPLETED / MERGED** (PR #35 squash `6df3673`, 07.09.2026, pas HG-PR35-MERGE — Record: `Identity_Oidc_Code_SignIn_B2c_Record.md` §14; E2 = evidencë shtesë e merge-uar VETËM për profilin OIDC provider-side; jo sign-in i produktit në Angular; E3 = OPEN; E5 = PARTIAL); hapi pasues i B.2 nuk shpallet (kërkon goal/prompt të ri); B.2 = ACTIVE / NOT COMPLETE; B.3 projection = NOT STARTED / BLOCKED); asnjë rresht i acceptance matrix §9 s'mbyllet pa evidencë të ekzekutuar. Rendi i delivery-t = PLANET-FIRST (ADR-030 ACCEPTED / EFFECTIVE) — ky specifikim mbetet **product-neutral**: zero `PLANET`/`KABI` client/audience/scope/role.**
**Autoriteti:** ky dokument është **specifikimi SUBORDINATE ndaj ADR-028** (`docs/canonical/01-phase0/05_Decision_Gates_ADR.md`). ADR-028 mban vendimin; ky dokument mban boundary-t, schema/table map, flows, security gates, migrimin, rollback-un dhe acceptance criteria. Në konflikt fiton ADR-028.
**Data:** 27.07.2026 (v1; **corrected pre-merge 27.07.2026 · 28.07.2026** — correction passes të review-t të pavarur mbi PR #26, para merge-it; pa v2 artificial) · **Pronari:** Pronari + Tech · **Kufiri:** ZERO kod/dependency/migrim nga PR-ja që e krijon; implementimi = **Increment B** i rendit A/B/C/D (shih `Application_Architecture_Convention.md` §15). Asnjë secret/konfigurim real këtu.

---

## 1. Vendimi (përmbledhje — autoriteti i plotë te ADR-028)

- **ASP.NET Core Identity** = account/credential/recovery/MFA **store**.
- **OpenIddict** = **self-hosted OIDC/OAuth2 authority**.
- **External federation** mbetet integration option i mundshëm; **NUK** zgjidhet asnjë provider i jashtëm në heshtje.
- **GUID/UUID keys** kudo; **zero int IDs**.
- **Schema `identity` e ndarë fizikisht/logjikisht nga schema `platform`.**
- **Identity roles NUK përdoren për tenant authorization** — `platform.role`, `platform.permission`, `platform.role_permission`, `platform.business_membership` mbeten autoriteti i vetëm tenant-scoped (F1.1).
- `platform.app_user` mbetet **projection / provider-neutral mapping boundary** midis identitetit dhe autorizimit tenant.

## 2. Boundary map

```
[ identity schema ]                      [ platform schema ]
ASP.NET Core Identity + OpenIddict       AccessControl (F1.1, i pandryshuar)
  auth: kush je? (accounts, creds,         authz: çfarë të lejohet në cilin
  MFA, sessions, tokens, consents)         business? (RBAC tenant-scoped,
                                           authority-lock, audit, receipts)
            └────────── projection ──────────┘
              identity.user_account.id  →  platform.app_user.id (GUID 1:1)
```

- Rrjedha e vetme e lejuar identity→platform: krijimi/sinkronizimi i projeksionit `app_user` (id + status lifecycle) përmes kontrate të tipizuar — **jo** query direkte cross-schema në rrugët e autorizimit.
- Asnjë tabelë e `identity` nuk merr pjesë në vendimin e autorizimit tenant; asnjë tabelë e `platform` nuk mban kredenciale.

### 2.1 Kontrata normative e projection-it `identity → platform.app_user` (FAIL-CLOSED)

- **`identity.user_account` = source of truth** për account lifecycle; **`platform.app_user` = projection / provider-neutral mapping boundary** — kurrë burim paralel vendimi.
- Mbulohen TË PAKTËN ngjarjet: **create · activate/enable · disable/lock/revoke** dhe çdo status tjetër që ndikon autorizimin.
- Delivery e ngjarjeve duhet të jetë **versioned, idempotente dhe e gjurmueshme**: ngjarje **duplicate → zero efekt shtesë**; ngjarje **out-of-order/stale → NUK e kthen projection-in prapa**; **delivery failure → regjistrohet**, me retry/recovery/**reconciliation të detyrueshme**.
- **Projection absent / stale / ambiguous në rrugë autorizimi → DENY fail-closed** — kurrë "lejo se s'dimë".
- Asnjë query direkte cross-schema në authority path; asnjë credential dhe asnjë PII e panevojshme në `platform.app_user`; audit/correlation pa material sensitiv.
- **Mekanizmi konkret i delivery-t (outbox / event / worker / transport) = OPEN — vendoset në Increment B**, jo në këtë PR.

**FRESHNESS FAIL-CLOSED (autorizim vetëm nga projection i PROVUESHËM, jo thjesht ekzistues):**

- Projection-i lokal mban një **sinjal MONOTON authority-version / watermark / revocation-epoch** (ose mekanizëm ekuivalent të provueshëm); Increment B përcakton: burimin e versionit, ruajtjen lokale, krahasimin monoton, **freshness bound/lease** dhe sjelljen gjatë ndërprerjes.
- **Version i munguar, i paqartë, i pakrahasueshëm, i vjetruar, OSE burim freshness-i i paarritshëm → DENY** — kurrë silent allow.
- **Disable/revoke i commit-uar te identity authority NUK lejohet të mbetet autorizim aktiv pafundësisht** vetëm sepse delivery u mbajt — freshness bound-i e detyron DENY-in brenda kufirit.
- Pas recovery/reconciliation, projection-i **konvergon me authority-n DETERMINISTIKISHT, pa rikthyer grant-e të revokuara**.

**Fixture DISKRIMINUES i Increment B (i detyrueshëm):** (1) autorizimi fillimisht lejohet; (2) disable/revoke commit-on te authority; (3) delivery i projection-it mbahet QËLLIMISHT; (4) freshness s'provohet dot; (5) platform authorization = **DENY**; (6) delivery/reconciliation rikthehet; (7) projection-i konvergon me authority-n dhe mbetet DENY. Plus rastet: duplicate delivery · out-of-order delivery · stale authority version · version i munguar/invalid · authority/freshness source unavailable · restart/recovery/reconciliation.

- **Gjendja e kontratës sot:** authority-ja, credentials dhe validimi lokal i token-ave = **COMPLETED / MERGED në B.2a** (PR #31 `42993f0`); flow-i interaktiv OAuth 2.0 Authorization Code + PKCE (login/consent/kod/shkëmbim) = **COMPLETED / MERGED në B.2b** (PR #33 squash `a6cc98c`, 05.09.2026 — E2 i mbyllur VETËM për këtë kontratë); profili OIDC provider-side (`openid`, ID token, `nonce`, UserInfo minimal) = **COMPLETED / MERGED në B.2c** (PR #35 squash `6df3673`, 07.09.2026 — vetëm provider-side; sign-in i produktit në Angular mbetet OPEN). **Pjesët e mbetura të B.2 (full OIDC conformance/Angular sign-in, refresh production, MFA/recovery, logout/revocation, audit identity — *pointer 06.09.2026: E5 §9 e klasifikon audit identity nën B.3; mospërputhje e regjistruar, e lënë OPEN/PARTIAL pa zhvendosje të heshtur*) dhe E GJITHË B.3 (projection/freshness/reconciliation) NUK janë të implementuara** — mbeten target i Increment B.2/B.3; **OPEN / planned / documented / waiver NUK numërohen PASS** (shih edhe zgjerimin e `ENG-005` në Tracker). *(Store foundation-i B.1 është COMPLETED/MERGED me PR #28 `d6ddff2` — shih §3 dhe `Identity_Store_Foundation_B1_Record.md`.)*

## 3. Schema / table map (emrat/llojet e IMPLEMENTUARA e të provuara në B.1 — COMPLETED/MERGED (PR #28 `d6ddff2`, 02.08.2026); lifecycle/revocation/protokolli = B.2 OPEN)

| Tabela target | Roli |
|---|---|
| `identity.user_account` | llogaria (credentials hash, MFA flags, lockout, status) |
| `identity.user_login` | external/federated logins (nëse aktivizohet federation) |
| `identity.user_claim` | claims të llogarisë |
| `identity.user_token` | token artifacts të ASP.NET Core Identity për funksione **account-level** (p.sh. authenticator/recovery/confirmation/provider-token storage — sipas mapping-ut REAL të Increment B) — **JO** OAuth2/OIDC refresh/access tokens |
| `identity.oidc_application` | klientët OIDC (Backoffice, POS, mobile) |
| `identity.oidc_authorization` | authorizations/consents |
| `identity.oidc_scope` | scopes |
| `identity.oidc_token` | tokens/artifacts OPERACIONALE OAuth2/OIDC të OpenIddict — access, refresh, authorization/device artifacts sipas konfigurimit final — me lifecycle/revocation në KËTË store autoritativ |

**Zero emra default të pakontrolluar** (`AspNetUsers`, `AspNetRoles`, `User` etj.) si emra finalë — mapping-u i EF/OpenIddict konfigurohet në emrat e mësipërm; **emrat finalë dhe llojet reale të token-ave u verifikuan në B.1 kundër modelit real të paketave (shih paragrafin vijues); **statusi = COMPLETED / MERGED** (B.1, PR #28 `d6ddff2`, pas Fable→Codex→Human Gate) — nuk deklarohen lloje që paketa finale nuk i përdor.**

**Verifikim B.1 (i kryer NË branch-in e PR #28 — migrimi 0003 EKZISTON aty dhe verifikimi iu bë modelit/schema-s REALE të tij; statusi u finalizua COMPLETED/MERGED me PR #28 `d6ddff2` pas review + Human Gate; i korrigjuar në correction passes R1–R8, 29.07–01.08.2026 — `Identity_Store_Foundation_B1_Record.md`):** tabelat §3 u konfirmuan kundër modelit REAL të paketave; `user_token` = artefakte account-level të Identity-t me PK të përbërë (`user_id`/`login_provider`/`name`); `oidc_token` = OpenIddict me çelës Guid; pronësia e ndarë e provuar me teste (model + schema reale). **Zero persisted int IDs = ABSOLUT (vendim i Pronarit):** `identity.user_claim` përdor claim type CUSTOM (`UserClaimRecord : IdentityUserClaim<Guid>`) me çelës të VETËM të persistuar `ClaimId` — **non-empty i imponuar në DY shtresa**: setter-i refuzon `Guid.Empty` në APLIKACION (exception i testuar) dhe **CHECK constraint determinist `ck_user_claim_id_non_empty`** e refuzon në PostgreSQL (Guid → kolona `id` uuid). Inherited `Id:int` i framework-ut **ekziston vetëm në CLR**, është **i EF-injoruar** (mapping string/metadata — zero kolonë int identifikuese) dhe **përdorimi ekzekutues i tij ndalohet nga IL architecture guard i CI-së** — nuk pretendohet compiler-level inaccessibility për kod jashtë repo-s. **Kufiri i store-it = TYPE/METADATA statik** mbi të gjitha production assemblies — atribuim i saktë historik: **R3** = identiteti i owner-ëve me **path kanonik repo-relative** (jo basename si autoritet klasifikimi); **R4** = **AssemblyName + TargetPath të VLERËSUARA nga MSBuild** dhe verifikim **MVID** i binarit të ngarkuar (zero regex/XML raw, zero fallback); direct owners VETËM me path kanonik = Persistence + Identity.Infrastructure; API composition root pa akses direkt te records/DbSet-et identity; universi i mbrojtur = TË 8 entitetet e skemës `identity` me meta-test plotësie kundër modelit real; **R5** = garanci fail-closed e varësive: **package roots direkte = autoritet EKSPLICIT i review-ueshëm** (barazi ekzakte me setin MSBuild + lockfiles; root i panjohur/autoritet i vdekur/i paarritshëm/malformed = GJETJE) dhe **AssemblyRefs = klasifikim TOTAL**; **R6** = klasifikimi kalon te referencat e ZGJIDHURA nga MSBuild (jo emri); **R7** = autorizimi kërkon **ANËTARËSI EKZAKTE në asset-set autoritativ të derivuar VEÇMAS** — platform: `CanonicalTargetPath` ekzakt + MVID i output-it; package **[R8]**: anëtarësi në asset-set-in e **tuple-s `packageId+resolvedVersion`** nga `project.assets.json` (unioni i sheshtë i owner-it NUK është autoritet — asset real i një pakete tjetër refuzohet); framework: ref-set nga `ResolvedTargetingPack`; lockfile: adjacency DIREKTE reale për edges projektesh, range→resolved për edges paketash dhe `version`=2 e detyrueshme (metadata/path-prefix vetvetiu NUK autorizojnë). Prefix-listat = vetëm defense-in-depth — provuar me teste + fixtures diskriminuese. **Threat boundary (R7):** csproj/Directory.Build.*/targets të importuara janë build-definition e review-uar — guard-i NUK pretendon mbrojtje ndaj ekzekutimit arbitrar të MSBuild target-ëve keqdashës. **Kufij të mbetur të dokumentuar (R4):** reflektimi dinamik runtime NUK kapet nga skaneri statik — gate **ENG-006 OPEN**; untyped identity-data access (raw SQL/ADO.NET/FromSql/ExecuteSql mbi schema `identity`) nuk ndalohet nga asnjë analyzer — gate **ENG-007 OPEN** (auditi R4: zero përdorim i tillë sot; kapaciteti ekziston në shtresa jo-owner mbi `platform.`) — **pointer 03.08.2026: ENG-007 = CLOSED / COMPLETED / MERGED (PR #29, squash `8f7a22c`); ENG-006 u implementua në Pre-B2 Increment 2 (`Identity_Dynamic_Reflection_Boundary_ENG006_Record.md`) me manifest production BOSH — **pointer 02.09.2026: ENG-006 + ENG-008 = CLOSED / COMPLETED / MERGED (PR #30, merge `8d3aa85`)**. Historiku: pjesa `analyzer/policy` e ENG-007 u implementua në Pre-B2 Increment 1 (`Identity_Untyped_Data_Access_Boundary_ENG007_Record.md`), me manifest të KATËR surface-ve platform-only (përfshirë migrimin IMMUTABLE 0002 përmes çiftit të kualifikuar `MigrationBuilder::Sql`) dhe fingerprint sha256; gate-i MBETET OPEN**; gate-t ENG-006 dhe ENG-008 = **CLOSED (PR #30 `8d3aa85`, 02.09.2026)** — parakushti i B.2 u plotësua dhe B.2a = COMPLETED / MERGED (PR #31 `42993f0`, 03.09.2026); B.2b Interactive OAuth 2.0 Authorization Code + PKCE = COMPLETED / MERGED (PR #33 squash `a6cc98c`, 05.09.2026); zero pretendim "bypass-free"; lifecycle/revocation/protokolli = B.2a/B.2 sipas acceptance matrix §9.

**Pronësia e token-ave — ZERO mbivendosje (E PROVUAR në B.1; lifecycle = B.2):** testet e B.1 (model + schema reale PostgreSQL sintetike) PROVOJNË (a) **zero ownership të dyfishtë** midis `user_token` dhe `oidc_token` (pronësi e ndarë me tipe CLR të veçanta); (b) **zero token material në schema `platform`**; (c) testet e lifecycle/revocation në store-in autoritativ përkatës mbeten **B.2 OPEN** — prova ekzistuese NUK paraqitet si e ardhshme dhe e ardhshmja NUK paraqitet si e kryer.

**Arsyetimi i emërtimit (i saktë, pa overclaim):** `USER` është **fjalë e rezervuar e PostgreSQL** (SQL key words, PostgreSQL 16 — burim zyrtar), prandaj canonical përdor `identity.user_account` dhe `platform.app_user`. **NUK pretendohet** se `Users`, `Role` apo çdo emër tjetër janë të gjithë reserved — emrat e tjerë eksplicitë përdoren për qartësi domain-i, schema ownership, lowercase naming dhe shmangie të mapping-ut default të pakontrolluar. `Rolers` është emër i pavlefshëm/typo — jo alternativë.

## 4. Flows (target)

- **Authorization Code + PKCE** për të gjithë klientët interaktivë (Backoffice Angular, POS desktop renderer → local host pattern sipas ADR-024/027, mobile RN).
- **JO implicit flow; JO resource-owner password grant.**
- Refresh tokens me **rotation + reuse detection** (reuse i detektuar → revokim familjeje).
- Logout/back-channel dhe session/device revocation të mbuluara para production-it.
- Company selector / tenant switch mbetet funksion i shtresës platform (membership-based), JO claim statik identity.

## 5. Security gates (të ngrira si kërkesa — evidence në Increment B)

MFA + recovery codes · signing/encryption keys **JASHTË** repo/log/renderers (custody sipas ADR-025/ADR-026) · refresh-token rotation/reuse detection · session/device/token revocation · lockout + rate limiting · email/account recovery · key rotation · HA/backup/restore i identity store-it · **audit i ngjarjeve identity** (login, MFA, lockout, revocation, recovery — pa PII të tepërt, pa sekrete) · incident response · **break-glass mbetet gate i veçantë dhe i audituar** (ADR-008; s'implementohet këtu) · **token validation sipas kontratës §5.1**.

### 5.1 Token validation — kontratë MECHANISM-NEUTRAL (gate i rikthyer)

Çdo API që pranon token DUHET të verifikojë, pavarësisht mekanizmit:

- **issuer**-i përputhet me authority-n e konfiguruar;
- **audience/resource** kontrollohet kur kontrata e token-it e kërkon;
- **token type/usage** është i lejuar për endpoint-in;
- **autenticiteti** provohet përmes **signature / decryption / introspection** ose mekanizmit tjetër të aprovuar;
- vetëm **algoritme të lejuara** (allowlist eksplicit);
- **lifetime, not-before dhe expiry** kontrollohen;
- **clock-skew** është eksplicit, i KUFIZUAR dhe i testuar;
- **unknown issuer / audience / key / algorithm / token-type → fail-closed**;
- **expired / not-yet-valid / revoked → fail-closed**;
- **key rollover/rotation** ka teste pozitive, negative dhe **overlap**.

**NUK mandatohet JWT/JWKS si format universal** — NUK pretendohet "të gjithë tokenët janë JWT" dhe as "JWKS është gjithmonë i detyrueshëm". Mekanizmat e lejuar për VLERËSIM në Increment B (zgjedhja konkrete dokumentohet dhe provohet aty; provenance: dokumentimi zyrtar i OpenIddict për token validation në API):

1. `UseLocalServer()` kur API dhe authority janë co-located;
2. trusted OIDC discovery/JWKS mbi TLS për authority remote me çelësa asimetrikë;
3. introspection ose mekanizëm tjetër i aprovuar me garanci ekuivalente.

### 5.2 Credentials / certificates / keys — policy eksplicite

- `AddDevelopmentSigningCertificate()` · `AddDevelopmentEncryptionCertificate()` · `AddEphemeralSigningKey()` · `AddEphemeralEncryptionKey()` lejohen **VETËM nën `IsDevelopment()`** (provenance: dokumentimi zyrtar i OpenIddict për encryption & signing credentials).
- Development përcaktohet **VETËM** nga environment-i real `IHostEnvironment.IsDevelopment()`.
- Jashtë Development, startup **DËSHTON nëse ekziston ÇFARËDO development ose ephemeral credential** — **edhe kur krahas tij ekzistojnë credentials persistent** (mixed persistent + dev/ephemeral jashtë Development = **FAIL**).
- Jashtë Development, startup **DËSHTON nëse mungon persistent signing set OSE persistent encryption set** — të dyja të detyrueshme.
- **Production/Staging/environment i panjohur NUK lejohen të bien në development fallback.**
- Production credentials: jashtë repo-s/log-eve/renderer-ëve · **persistente** · me **rotacion/restart policy të provueshme** · availability për të gjitha instancat · custody/access control · backup/recovery · **expiry monitoring** · **rotation me overlap të provuar** · signing/encryption të NDARA nga TLS certificate kur kërkohet · provenance dhe gate sipas **ADR-025/ADR-026**.
- **NUK ndalohet kategorikisht çdo self-signed certificate**: një certificate privately/self-issued mund të përdoret VETËM nëse lifecycle, trust, custody, rotation dhe deployment janë të aprovuara dhe të provuara.
- Ky PR **nuk zgjedh** CA, KMS, secret store apo hosting provider (ADR-026 OPEN).

### 5.3 Account/email/phone VERIFICATION ≠ RECOVERY — gate OPEN

**Statusi: OPEN / PENDING SPECIFICATION IN INCREMENT B — as DECIDED, as IMPLEMENTED.**

- Për SECILËN klasë user/client përcaktohet në Increment B nëse kërkohet account confirmation, verified email, verified phone ose sinjal tjetër — ky PR **NUK deklaron** se çdo user duhet patjetër të ketë email/telefon.
- **Email/telefon i PAVERIFIKUAR nuk përdoret KURRË si trusted recovery/MFA destination.**
- Enrollment, resend, expiry, single-use, rate-limit, change dhe re-verification kërkojnë policy + teste.
- Audit/redaction pa PII të tepërt.
- **Verification dhe recovery janë dy koncepte të NDARA** — nuk barazohen.

## 6. Migrimi (Increment B)

- Schema `identity` hyn me migrimin **0003+ forward-only** (SQL i audituar brenda migrimit).
- **0001/0002 janë IMMUTABLE.**
- **Pronësia e persistencës — vendoset QARTË në Increment B PARA migrimit 0003:** projekti/moduli Identity; DbContext/mappings; migration assembly; **migration owner i VETËM**; marrëdhënia me `Idata.Platform.Persistence`; **zero migration owner paralel dhe zero schema drift**.
- `platform.app_user` NUK zëvendësohet — lidhet me projection (id 1:1, status sync i dokumentuar).
- Seed i klientëve OIDC vetëm sintetik/dev në teste; konfigurimet reale jashtë repo-s (ADR-021/ADR-026).

## 7. Architecture-test follow-up (i DETYRUESHËM në Increment B)

Testi ekzistues `Zero_identity_provider_konkret_ne_asnje_assembly_te_platformes` (ArchitectureTests) sot ndalon `OpenIddict`, `IdentityServer`, `Duende`, `Microsoft.AspNetCore.Identity` etj. Në Increment B ai **ZËVENDËSOHET — JO FSHIHET** — me test që:

1. lejon VETËM paketat/assemblies e aprovuara nga ADR-028 (ASP.NET Core Identity + OpenIddict);
2. i lejon VETËM në projektet e aprovuara (host + moduli/projekti identity i vendosur në Increment B);
3. **vazhdon të ndalojë** `IdentityServer`/`Duende`/`Auth0`/`Okta` dhe çdo provider tjetër të paaprovuar, kudo;
4. ndryshimi arsyetohet EKSPLICIT në PR body;
5. architecture coverage **NUK dobësohet në heshtje**.

**[HISTORICAL — PR #26 docs-only, 28.07.2026] Në atë PR docs-only testi NUK u prek.** *(Shënim i datuar B.1, 28.07.2026: zëvendësimi i kontrolluar u krye në branch-in e B.1 sipas pikave 1–5 — allowlist ekzakt assemblies + projekte të aprovuara; providerët/protokolli të ndaluar kudo; coverage i FORCUAR; pending review — shih `Identity_Store_Foundation_B1_Record.md`.)*

## 8. Rollback (Increment B) — kontrata FORWARD-ONLY

- **0001/0002 immutable; çdo schema change = 0003+ forward-only.**
- `Down` **DESTRUKTIV lejohet VETËM në DB disposable/local/CI pa data reale**, nën guard-in ekzistues fail-closed (env + opt-in + DB sintetike) — kurrë si mekanizëm operacional.
- Në environment **deployed ose data-bearing: JO destructive Down si rollback operacional.** Rollback-u aty = **compatible application rollback** (kur schema është backward-compatible), OSE **forward corrective migration**, OSE **restore nga backup/PITR i provuar** — gjithmonë plus **reconciliation e projection-it**.
- **Expand/contract** përdoret kur ndryshimi kërkon kompatibilitet ndër-versionesh.
- Kthimi i kodit: paketat Identity/OpenIddict hiqen nga projektet e aprovuara, wiring-u nga host-i, projection sync — pa prekur `platform` schema, F1.1, 0001/0002 apo klientët e gjeneruar; architecture test-i i zëvendësuar rikthehet në formën provider-neutral.
- **ZERO pretendim se rollback-u production është provuar nga incrementet e deritanishme (docs/architecture-tests-only)**; owner/gate për backup/PITR/key/provider = **ADR-025/ADR-026 OPEN**.

## 9. Acceptance matrix e Increment B (e MATSHME — çdo rresht me evidence, status, owner, closure gate)

**Rregull (i korrigjuar në correction pass të PR #28):** statusi **default = OPEN derisa të ketë evidencë të ekzekutuar**; çdo rresht **klasifikohet INDIVIDUALISHT** sipas evidencës reale (OPEN / PARTIAL / IMPLEMENTED-në-branch / N/A me justifikim); **OPEN / PARTIAL / N/A / waiver / planned / documented NUK numërohen PASS dhe NUK e mbyllin Increment B** — mbyllja e një rreshti kërkon testin/evidencën e kolonës 2 të ekzekutuar GREEN + review + Human Gate; **Increment B nuk quhet COMPLETE** para B.2/B.3.

| # | Kriteri | Evidence/test i pritshëm | Statusi sot | Owner | Closure gate |
|---|---|---|---|---|---|
| A1 | Projection: committed disable/revoke + **withheld delivery** → DENY kur freshness s'provohet (fixture 7-hapësh §2.1) | integration test kundër PostgreSQL real me delivery të mbajtur qëllimisht | OPEN | Tech | Increment B PR + review |
| A2 | Projection: duplicate / out-of-order / stale version / missing-invalid version / authority-freshness source unavailable | teste deterministike per-rast, zero silent allow | OPEN | Tech | Increment B PR + review |
| A3 | Projection: restart/recovery/reconciliation → konvergim deterministik pa rikthim grant-esh të revokuara | test recovery + reconciliation | OPEN | Tech | Increment B PR + review |
| B1 | Token validation — issuer i gabuar · audience e gabuar → refuzim | teste negative | **COMPLETED / MERGED (B.2a — PR #31 squash `42993f0`, 03.09.2026)** — teste negative REALE mbi middleware-in e validimit: issuer i gabuar (token real nga host me çelësa të njëjtë por issuer tjetër; `iss` i ndryshuar me çelësa realë) → 401; audience i gabuar → 401 (B.2a Record §5) | Tech | Increment B PR + review |
| B2 | Token validation — signature/JWKS invalid OSE unavailable · unknown signing key → fail-closed | teste negative + burim i paarritshëm | **COMPLETED / MERGED (B.2a, correction C1 — PR #31 squash `42993f0`, 03.09.2026)** — çelës firme i panjohur, tampered, garbage, `typ` i gabuar → 401; **tri politika JOSE të NDARA** (JWS-alg i brendshëm · JWE-alg key-management i jashtëm · JWE-enc), header-i i jashtëm i kontrolluar PARA unwrap-it: RSA-OAEP → 200, RSA1_5 → 401, `enc` jashtë allowlist-it → 401, header malformed/missing → 401; koherencë issuance↔validation (allowlist jo-koherent me credential-in real → startup FAIL); "JWKS unavailable" = **N/A** për validim LOKAL (`UseLocalServer`, zero burim remote — B.2a Record §5.3); store i paarritshëm për token-entry validation → kurrë 200 (Record §8) | Tech | Increment B PR + review |
| B3 | Token validation — key rollover me overlap · expired/not-yet-valid · clock-skew policy e kufizuar | teste pozitive+negative+overlap | **COMPLETED / MERGED (B.2a — PR #31 squash `42993f0`, 03.09.2026)** — rotation me overlap (i vjetri pranohet gjatë overlap-it, refuzohet pas heqjes; i riu lëshohet me çelësat e rinj); expired/not-yet-valid në JWT (validimi i lifetime-it i IdentityModel) + skadimi AUTORITATIV i entry-t të store-it; clock skew i kufizuar 0..300s nga konfigurimi (brenda → 200, jashtë → 401) — B.2a Record §5 | Tech | Increment B PR + review |
| C1 | Credentials: matrica e plotë §5.2 — Development+dev OK · Development pa set minimal FAIL · Staging/Production/unknown + ÇFARËDO dev/ephemeral = startup FAIL (edhe mixed) · persistent signing missing FAIL · persistent encryption missing FAIL · set valid SUCCESS | startup tests per-rresht | **COMPLETED / MERGED (B.2a — PR #31 squash `42993f0`, 03.09.2026)** — startup tests mbi host-in REAL (`Program`) për çdo rresht + policy PURE me unit tests; mixed i provuar edhe me çelës ephemeral të regjistruar JASHTË konfigurimit (policy mbi credentials REALE); mesazhe klasë-only pa material — B.2a Record §4 | Tech | Increment B PR + review |
| C2 | Credentials: restart me të njëjtat creds · rotation/rollover me overlap të kontrolluar · expired/invalid/unavailable → fail-closed | teste lifecycle | **COMPLETED / MERGED (B.2a — PR #31 squash `42993f0`, 03.09.2026)** — restart me të njëjtat creds (token-i i lëshuar para restartit pranohet), rotation me overlap i kontrolluar, expired/not-yet-valid/pa çelës privat/PFX i pavlefshëm/password i gabuar/burim i paarritshëm → startup FAIL me klasë (`persistent-expired`, `persistent-not-yet-valid`, `persistent-without-private-key`, `persistent-unloadable(...)`) — B.2a Record §4/§5 | Tech | Increment B PR + review |
| D1 | Verification policy për email/telefon si **gate i veçantë** per-klasë user/client (§5.3); **recovery NUK e zëvendëson verification policy** | policy e specifikuar + teste enrollment/expiry/single-use/rate-limit | OPEN | Pronari + Tech | Increment B PR + Human Gate |
| E1 | Schema `identity` me tabelat §3 (emra të verifikuar) · migrim 0003+ me upgrade real nga 0002 | migration tests | **IMPLEMENTED në B.1 — COMPLETED/MERGED (PR #28 `d6ddff2`)** (28–29.07.2026; teste të ekzekutuara GREEN, përfshirë UUID claim key pas correction pass — shih `Identity_Store_Foundation_B1_Record.md`) | Tech | Increment B PR + review |
| E2 | Authorization Code + PKCE end-to-end; implicit/password grant të REFUZUARA | teste flows | **COMPLETED / MERGED (B.2b — PR #33 squash `a6cc98c`, 05.09.2026 — VETËM kontrata OAuth 2.0 Authorization Code + PKCE; pjesa tjetër e E2 mbetet OPEN):** login real me ASP.NET Core Identity → session cookie e veçantë → consent eksplicit → kod autorizimi (i lidhur me subjekt/klient/redirect/challenge, një-përdorimsh) → shkëmbim në `/connect/token` me `code_verifier` korrekt → access token që kalon te `[Authorize]`; `plain`/verifier i munguar ose i gabuar/replay/klient ose redirect tjetër → refuzim; `openid`, ID token, UserInfo, nonce dhe refresh **nuk janë pjesë e kësaj kontrate dhe mbeten OPEN**; **C1 (04.09.2026):** matrica 8-skenarëshe e plotë me teste reale (DB e paarritshme në çdo fazë → 503 i kontrolluar; dorëzim i dyfishtë; restart real i host-it; konkurrencë e matur; rikthim i së njëjtës DB) + user-binding i antiforgery-t me sesionin (B.2b Record §5/§9); **C2 (05.09.2026):** evidencë e forcuar R3/R6/R7/R8 (Record §10); follow-up-et e mbetura R4/R7 = OPEN / NON-BLOCKING (Record §11.5). **B.2c (COMPLETED / MERGED — PR #35 squash `6df3673`, 07.09.2026 — evidencë shtesë VETËM për profilin OIDC provider-side; përjashtimi i emërtuar `oi_au_id`/`oi_tkn_id` dhe shkaktarët e rivlerësimit: B.2c Record §14.2):** `openid` → ID token i nënshkruar (`iss`, `aud` = client_id, `sub`, lifetime, `nonce` i lidhur ekzakt) i validuar KRIPTOGRAFIKISHT nga konsumatori sintetik me JWKS-in publik; UserInfo minimal (`sub`) me refuzime fail-closed (401/403) dhe 503 i kontrolluar; full OIDC conformance/Angular sign-in/refresh/logout mbeten OPEN (`Identity_Oidc_Code_SignIn_B2c_Record.md`). **B.2a (baza e provuar):** Authorization Code + PKCE = i VETMI flow production i DEKLARUAR (`RequireProofKeyForCodeExchange`), endpoint-i i autorizimit FAIL-CLOSED (`unsupported_response_type`, kurrë kod), implicit/password/refresh → `unsupported_grant_type`; **full OIDC sign-in (openid/ID token/UserInfo) = OPEN** | Tech | Increment B PR + review |
| E3 | MFA + recovery + lockout + refresh rotation/reuse detection | teste reale | OPEN | Tech | Increment B PR + review |
| E4 | Tenant RBAC F1.1 i PAPREKUR (baseline GREEN; zero Identity-role në authz) | baseline i plotë | **PROVUAR në B.1 — COMPLETED/MERGED (PR #28 `d6ddff2`)** (baseline i plotë GREEN zero skip + zero Identity roles strukturalisht + kufiri `Users`↔`PlatformAppUsers` i guard-uar; mbyllja me review + merge) | Tech | Increment B PR + review |
| E5 | Architecture test i zëvendësuar §7; zero provider i paaprovuar; keys jashtë repo/log/renderers; audit identity i redaktuar | architecture + gitleaks/guard + audit tests | **PARTIAL** (architecture replacement + inventari fail-closed + gitleaks/guard = të provuara në branch B.1; **B.2b (COMPLETED / MERGED — PR #33 squash `a6cc98c`, 05.09.2026) shton:** gate-in JOSE mbi stack-un e SERVER-it (artefakti i mbrojtur që konsumon `/connect/token` validohet për `alg`/`enc` PARA çdo dekriptimi → refuzim OAuth, kurrë 500, kurrë token) dhe kufijtë e sipërfaqes interaktive (antiforgery i lidhur me identitetin e sesionit — C1, rate limit, cookie e ndarë nga bearer-i, kufi 503 i dështimit infrastrukturor — C1); **B.2c (COMPLETED / MERGED — PR #35 `6df3673`) shton:** gate JOSE endpoint-aware te UserInfo (`invalid_token`), passthrough UserInfo owner-only me guard, token passthrough i ndaluar kudo. **B.2a = COMPLETED / MERGED (PR #31 `42993f0`) për:** zëvendësimi i KONTROLLUAR i familjeve `OpenIddict.Server`/`OpenIddict.Validation` VETËM te owner-i (Client/metapackage/Duende/IdentityServer/Auth0/Okta të ndaluara kudo), guard MemberRefs (zero grant/passthrough/TLS-off/introspection në production), keys/credentials jashtë repo/log (mesazhe klasë-only, zero material; zero appsettings) — B.2a Record §6; audit identity events = B.3 — OPEN; *pointer 06.09.2026: §2 e liston audit identity nën B.2 — mospërputhje e regjistruar, mbetet OPEN/PARTIAL, pa audit writer të ri*) | Tech | Increment B PR + review |

## 10. External IdP — kushtet e RIHAPJES (jo adoptim automatik)

External IdP **nuk adoptohet automatikisht**. ADR-028 **rihapet për vlerësim** vetëm me evidence për një a më shumë nga: (a) enterprise federation ose SAML; (b) kërkesë normative/regulatorike/kontraktuale klienti; (c) HA/operations/security të pambuluara sigurt nga target-i self-hosted; (d) matje bindëse të total cost/risk; (e) migration + rollback + exit plan.

**Reopen ≠ approval.** Çdo external provider kërkon: ADR të ri/ndryshim të aprovuar · threat model · subject/claims/tenant mapping · data residency/DPA review · revocation/audit/recovery parity · migration/rollback/exit evidence · **Human Gate**. **Asnjë package/config/schema/data replication e provider-it të jashtëm nuk lejohet në heshtje.**

## 11. Arsyetimi i alternativës së refuzuar (Duende IdentityServer)

Duende nuk zgjidhet sepse sjell **detyrime licensing/procurement/renewal për production** dhe sot **nuk është provuar asnjë capability gap** që kërkon Duende mbi OpenIddict. **Nuk hardcode-ohet asnjë prag të ardhurash apo edition specifik** — kushtet aktuale të licencës rishikohen nga **burimi zyrtar i Duende (docs/licensing)** nëse vendimi rihapet; rihapja kërkon **ADR + Human Gate**.

## 12. Çfarë mbetet OPEN edhe pas kësaj PR-je

Zgjedhja HA/backup topology konkrete (ADR-026) · key custody mekanizmi konkret (ADR-025) · federation e jashtme (vetëm sipas §10) · break-glass implementation (gate i veçantë) · production provisioning i roleve (F1.1 Record) · ndarja përfundimtare 401/403 (implementohet me Increment B) · **mekanizmi i delivery-t të projection-it (§2.1)** · **policy e verification-it per-klasë (§5.3)** · **mekanizmi konkret i token validation (§5.1) — B.2a: COMPLETED / MERGED (PR #31 squash `42993f0`, 03.09.2026 — `Identity_Authority_Credentials_Validation_B2a_Record.md`); mbetet OPEN vetëm përtej scope-it të B.2a (E2 interaktiv = mbyllur me B.2b PR #33 vetëm për kontratën code+PKCE; profili OIDC provider-side = COMPLETED / MERGED me B.2c (PR #35 `6df3673`); offline auth, B.3 = OPEN)** · **testet lifecycle/revocation të token-ave — B.2a: COMPLETED / MERGED (revokim, restart, rotation — PR #31 `42993f0`)** · **offline identity session/freshness (Edge/POS auth gjatë WAN outage) — OPEN; `UseLocalServer()` i B.2a NUK është evidencë për të; rollout i parë: `PLANET_PILOT_BLOCKER` (Roadmap §3a)** · **remote revoke/wipe — OPEN** · **skadimi i credential-it në RUNTIME (pas startup-it) — OPEN / UNKNOWN (B.2a e vlerëson policy-në vetëm në startup; sjellja pas skadimit në runtime nuk është testuar)** · **E2 login/consent interaktiv (kontrata OAuth 2.0 code+PKCE = COMPLETED / MERGED me B.2b; profili OIDC provider-side = COMPLETED / MERGED me B.2c (PR #35 `6df3673`, vetëm provider-side); full OIDC conformance/Angular sign-in OPEN), MFA/recovery, refresh production, audit identity events — OPEN/PARTIAL (B.2a = subset i B.2, COMPLETED / MERGED; **B.2b Interactive OAuth 2.0 Authorization Code + PKCE = COMPLETED / MERGED (PR #33 squash `a6cc98c`, 05.09.2026)**; **B.2c OIDC provider-side = COMPLETED / MERGED (PR #35 squash `6df3673`, 07.09.2026)**; B.2 = ACTIVE / NOT COMPLETE; B.3 = NOT STARTED / BLOCKED; pjesa e mbetur kërkon goal/prompt të ri)**. *(Shënim closure: **ENG-007** = CLOSED / MERGED me PR #29 `8f7a22c`; **ENG-006 + ENG-008** = CLOSED / MERGED me PR #30 `8d3aa85` (02.09.2026) — jashtë listës OPEN.)* *(Hequr nga kjo listë sepse B.1 i ka PROVUAR realisht në branch: emrat/llojet e tabelave §3; ndarja e pronësisë `user_token`/`oidc_token`.)*
