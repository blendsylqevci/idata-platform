# PoC-1 — Tenant Isolation (raport evidence)

**Qëllimi:** KABI s'i sheh të dhënat e PLANET dhe anasjelltas — izolim i fortë multi-company.
**Burimi i specit:** `docs/canonical/01-phase0/01_PoC_Specs.md` (PoC-1) + roadmap PoC Track / gate **ENG-003**.
**Verdikti:** **PASS** në tri nivele — API (deny by default + membership real), composite FK në DB, dhe RLS si safety-net.

> Auth real, Company Switcher UI dhe politika RLS canonical **NUK** janë pjesë e këtij PoC (Faza 1 / gated). Shih "Kufizime" më poshtë.

## Si riprodhohet

```bash
docker compose -f packages/database/docker-compose.yml up -d      # Postgres lokal
export PGHOST=localhost PGPORT=5433 PGUSER=idata PGDATABASE=idata
pnpm --filter @idata/database build && pnpm --filter @idata/database migrate   # 0001 + 0002
pnpm exec turbo run build typecheck test --force                 # 5 workspaces + testet PoC
```

Seed sintetik (`packages/database/src/poc-seed.ts`, ID fikse, JO auth real):
2 business (`biz-demo-kabi`, `biz-demo-planet`), 3 user testi
(`user-demo-kabi`, `user-demo-planet`, `user-demo-group-owner`), 1 role/biznes,
membership-e: kabi→kabi, planet→planet, **owner→TË DY** (2 membership eksplicite;
JO përmes `business_group` — semantika group-level = ENG-004/Faza 1).

## Niveli 1 — API (deny by default, membership real, kontekst eksplicit)

Kontekst me header-a sintetikë `X-User-Id` + `X-Business-Id` (pa auth real). Endpoint: `GET /poc/roles`.

```text
1) KABI user + business=KABI    -> HTTP 200  roles=[kabi-role]  (business_id = KABI, KURRË PLANET)
2) KABI user + business=PLANET  -> HTTP 403  {"error":"Forbidden","message":"S'ka membership në këtë business."}
3) KABI user PA X-Business-Id    -> HTTP 403  {"error":"Forbidden","message":"Kontekst tenant i pavlefshëm ..."}
4) group-owner + business=KABI   -> HTTP 200  roles=[kabi-role]   (vetëm KABI)
5) group-owner + business=PLANET -> HTTP 200  roles=[planet-role] (vetëm PLANET)
6) group-owner PA X-Business-Id  -> HTTP 403  (kurrë "all tenants" implicit)
```

Përfundim: user-i i KABI-t **kurrë** s'merr rresht me `business_id` të PLANET-it; pa kontekst valid → mohim; group-owner sheh çdo biznes **vetëm** me kontekst eksplicit per-kërkesë. (Teste: `apps/api/src/poc/tenant-isolation.e2e.test.ts`, 6/6.)

## Niveli 2 — Composite FK (izolim në nivel DB, migrimi 0002 / ENG-003)

`role_permission` dhe `business_membership` kanë `FOREIGN KEY (business_id, role_id) REFERENCES role (business_id, id)`.
INSERT me role të biznesit tjetër dështon **në nivel DB**, jo vetëm në aplikacion:

```text
INSERT INTO role_permission(business_id, role_id, permission_id)
VALUES (<PLANET biz>, <KABI role>, <perm>);

ERROR:  insert or update on table "role_permission" violates foreign key
        constraint "role_permission_business_role_fk"
DETAIL:  Key (business_id, role_id)=(aaaa0003…PLANET, aaaa0007…KABI-role) is not present in table "role".
```

(Teste: `packages/database/src/tenant-isolation.test.ts` — cross-tenant `role_permission` **refuzohet**, cross-tenant `business_membership` **refuzohet**, same-tenant **lejohet**.)

## Niveli 3 — RLS si safety-net (test-scoped, JO canonical)

Brenda testit (transaksion që bën **ROLLBACK**; `SET ROLE` te një rol jo-superuser sepse `idata` është superuser dhe e anashkalon RLS-në), me politikë
`USING (business_id = current_setting('app.current_business_id')::uuid)`:

- kontekst = KABI → `SELECT * FROM role` kthen **vetëm** role të KABI-t;
- kontekst = PLANET → **vetëm** PLANET;
- kontekst i gabuar (uuid që s'ekziston) → **0 rreshta**, edhe pa filtrat e aplikacionit.

Politika RLS **production** mbetet e gated (Faza 1 / ADR) — këtu provohet vetëm mekanizmi si rrjetë sigurie.

## PASS/FAIL kundër `01_PoC_Specs` (PoC-1)

| Kriter (spec) | Rezultat | Provuar te |
|---|---|---|
| User i KABI-t **kurrë** s'merr rresht me `business_id` të PLANET (API + RLS) | **PASS** | API (skenari 1/2) + RLS (niveli 3) |
| Pa `business_id` valid → mohim (deny by default) | **PASS** | API (skenari 3/6) |
| Group Owner sheh të dyja vetëm përmes konteksti të kontrolluar per-kërkesë | **PASS** | API (skenari 4/5/6) |
| Composite FK cross-tenant e provuar me test që dështon siç duhet | **PASS** | DB FK (niveli 2) |
| Migrimi 0002: fresh apply + re-run no-op (idempotencë) | **PASS** | `applied=2` → re-run `applied=0` |
| `build/typecheck/test --force` jeshile (5 ws + testet e reja) | **PASS** | 34/34 teste |

**FAIL = çdo rrjedhje ndër-tenant.** Asnjë e vërejtur.

## Kufizime të njohura (jashtë scope-it të PoC-1)

- **Company Switcher UI** → Faza 1 (këtu provohet vetëm sjellja e kontrolluar per-kërkesë, pa UI).
- **Auth real** (login/session/2FA/Supabase) → Faza 1; konteksti këtu vjen nga header-a sintetikë.
- **Politika RLS canonical** → e gated (F1/ADR); RLS këtu vetëm test-scoped (rollback).
- **`business_group` group-level semantics** → ENG-004/Faza 1 (owner-i modelohet me 2 membership eksplicite, jo përmes grupit).
- **`business_unit`/RESORT** → OPS-001, jashtë scope.
