# Identity Authority & Store — Decision Specification (ADR-028)

**Statusi:** **PROPOSED / OWNER-DIRECTED TARGET / PENDING_INDEPENDENT_REVIEW — EFFECTIVE ONLY AFTER HUMAN GATE + MERGE.**
**Autoriteti:** ky dokument është **specifikimi SUBORDINATE ndaj ADR-028** (`docs/canonical/01-phase0/05_Decision_Gates_ADR.md`). ADR-028 mban vendimin; ky dokument mban boundary-t, schema/table map, flows, security gates, migrimin, rollback-un dhe acceptance criteria. Në konflikt fiton ADR-028.
**Data:** 27.07.2026 (v1) · **Pronari:** Pronari + Tech · **Kufiri:** ZERO kod/dependency/migrim nga PR-ja që e krijon; implementimi = **Increment B** i rendit A/B/C/D (shih `Application_Architecture_Convention.md` §15). Asnjë secret/konfigurim real këtu.

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

## 3. Schema / table map (emra target — verifikohen në Increment B PARA migrimit 0003)

| Tabela target | Roli |
|---|---|
| `identity.user_account` | llogaria (credentials hash, MFA flags, lockout, status) |
| `identity.user_login` | external/federated logins (nëse aktivizohet federation) |
| `identity.user_claim` | claims të llogarisë |
| `identity.user_token` | token store (recovery/MFA/refresh artefakte të Identity-t) |
| `identity.oidc_application` | klientët OIDC (Backoffice, POS, mobile) |
| `identity.oidc_authorization` | authorizations/consents |
| `identity.oidc_scope` | scopes |
| `identity.oidc_token` | tokens e lëshuara (lifecycle/revocation) |

**Zero emra default të pakontrolluar** (`AspNetUsers`, `AspNetRoles`, `User`, `Rolers` etj.) si emra finalë — mapping-u i EF/OpenIddict konfigurohet në emrat e mësipërm; **emrat finalë verifikohen në Increment B para migrimit 0003**.

## 4. Flows (target)

- **Authorization Code + PKCE** për të gjithë klientët interaktivë (Backoffice Angular, POS desktop renderer → local host pattern sipas ADR-024/027, mobile RN).
- **JO implicit flow; JO resource-owner password grant.**
- Refresh tokens me **rotation + reuse detection** (reuse i detektuar → revokim familjeje).
- Logout/back-channel dhe session/device revocation të mbuluara para production-it.
- Company selector / tenant switch mbetet funksion i shtresës platform (membership-based), JO claim statik identity.

## 5. Security gates (të ngrira si kërkesa — evidence në Increment B)

MFA + recovery codes · signing/encryption keys **JASHTË** repo/log/renderers (custody sipas ADR-025/ADR-026) · refresh-token rotation/reuse detection · session/device/token revocation · lockout + rate limiting · email/account recovery · key rotation · HA/backup/restore i identity store-it · **audit i ngjarjeve identity** (login, MFA, lockout, revocation, recovery — pa PII të tepërt, pa sekrete) · incident response · **break-glass mbetet gate i veçantë dhe i audituar** (ADR-008; s'implementohet këtu).

## 6. Migrimi (Increment B)

- Schema `identity` hyn me migrimin **0003+ forward-only** (owner i vetëm mbetet Persistence/EF; SQL i audituar brenda migrimit).
- **0001/0002 NUK rishkruhen.**
- `platform.app_user` NUK zëvendësohet — lidhet me projection (id 1:1, status sync i dokumentuar).
- Seed i klientëve OIDC vetëm sintetik/dev në teste; konfigurimet reale jashtë repo-s (ADR-021/ADR-026).

## 7. Architecture-test follow-up (i DETYRUESHËM në Increment B)

Testi ekzistues `Zero_identity_provider_konkret_ne_asnje_assembly_te_platformes` (ArchitectureTests) sot ndalon `OpenIddict`, `IdentityServer`, `Duende`, `Microsoft.AspNetCore.Identity` etj. Në Increment B ai **ZËVENDËSOHET — JO FSHIHET** — me test që:

1. lejon VETËM paketat/assemblies e aprovuara nga ADR-028 (ASP.NET Core Identity + OpenIddict);
2. i lejon VETËM në projektet e aprovuara (host + moduli/projekti identity i vendosur në Increment B);
3. **vazhdon të ndalojë** `IdentityServer`/`Duende`/`Auth0`/`Okta` dhe çdo provider tjetër të paaprovuar, kudo;
4. ndryshimi arsyetohet EKSPLICIT në PR body;
5. architecture coverage **NUK dobësohet në heshtje**.

**Në PR-në docs-only aktuale testi NUK preket.**

## 8. Rollback (Increment B)

Hiqet migrimi 0003+ (Down i audituar), paketat Identity/OpenIddict nga projektet e aprovuara, wiring-u në host dhe projection sync — pa prekur `platform` schema, F1.1, migrimet 0001/0002 apo klientët e gjeneruar. Architecture test-i i zëvendësuar rikthehet në formën provider-neutral. Rollback-u provohet si pjesë e acceptance të Increment B.

## 9. Acceptance criteria (Increment B — matëse, jo deklarative)

1. Schema `identity` me tabelat e §3 (emra të verifikuar), migrim 0003+ me upgrade path real nga 0002.
2. Authorization Code + PKCE end-to-end kundër instance sintetike; implicit/password grant të provuara TË REFUZUARA.
3. MFA + recovery + lockout + rotation/reuse detection me teste reale.
4. Projection `app_user` 1:1 me teste (krijim/status sync; zero shkrim direkt cross-schema në rrugët authz).
5. Tenant RBAC i F1.1 i PAPREKUR (baseline i plotë i testeve GREEN; zero Identity-role në vendimet e autorizimit).
6. Architecture test i zëvendësuar sipas §7; zero provider i paaprovuar.
7. Keys jashtë repo/log/renderers; zero sekret në git (gitleaks/guard).
8. Audit i ngjarjeve identity i pranishëm dhe i redaktuar.

## 10. Çfarë mbetet OPEN edhe pas kësaj PR-je

Zgjedhja HA/backup topology konkrete (ADR-026) · key custody mekanizmi konkret (ADR-025) · federation e jashtme (opsion, pa provider të zgjedhur) · break-glass implementation (gate i veçantë) · production provisioning i roleve (F1.1 Record) · ndarja përfundimtare 401/403 në kontratën publike (implementohet me Increment B, dokumentohet aty).
