import type { Pool } from "pg";

/**
 * ID sintetike fikse për PoC-1 (asnjë e dhënë reale; identitete testi, JO auth).
 * `email` i app_user-it është string sintetik (jo email-formë), me qëllim.
 */
// UUID sintetike me letra hex (pa run të gjatë shifrash) me qëllim, që të mos
// ngatërrohen me identifikues 12-shifror as me numra telefoni nga skanerët e sekreteve.
export const DEMO_IDS = {
  group: "aaaa0001-aaaa-4aaa-8aaa-aaaaaaaa0001",
  bizKabi: "aaaa0002-aaaa-4aaa-8aaa-aaaaaaaa0002",
  bizPlanet: "aaaa0003-aaaa-4aaa-8aaa-aaaaaaaa0003",
  userKabi: "aaaa0004-aaaa-4aaa-8aaa-aaaaaaaa0004",
  userPlanet: "aaaa0005-aaaa-4aaa-8aaa-aaaaaaaa0005",
  userOwner: "aaaa0006-aaaa-4aaa-8aaa-aaaaaaaa0006",
  roleKabi: "aaaa0007-aaaa-4aaa-8aaa-aaaaaaaa0007",
  rolePlanet: "aaaa0008-aaaa-4aaa-8aaa-aaaaaaaa0008",
  perm: "aaaa0009-aaaa-4aaa-8aaa-aaaaaaaa0009",
} as const;

/**
 * Seed SINTETIK, idempotent (ON CONFLICT DO NOTHING) për PoC-1:
 * 2 business (kabi/planet), 3 user testi, 1 role per business, 1 permission,
 * dhe membership-e: kabi→kabi, planet→planet, **owner→TË DY** (2 membership
 * eksplicite; JO përmes business_group — semantika group-level = ENG-004/Faza 1).
 */
export async function seedPocTenants(pool: Pool): Promise<void> {
  const d = DEMO_IDS;
  await pool.query(
    "INSERT INTO business_group(id, name) VALUES ($1, 'biz-demo-group') ON CONFLICT (id) DO NOTHING",
    [d.group],
  );
  await pool.query(
    `INSERT INTO business(id, group_id, name, nui) VALUES
       ($1, $3, 'biz-demo-kabi', 'DEMO-KABI'),
       ($2, $3, 'biz-demo-planet', 'DEMO-PLANET')
     ON CONFLICT (id) DO NOTHING`,
    [d.bizKabi, d.bizPlanet, d.group],
  );
  await pool.query(
    `INSERT INTO app_user(id, email, name) VALUES
       ($1, 'user-demo-kabi', 'KABI Demo User'),
       ($2, 'user-demo-planet', 'PLANET Demo User'),
       ($3, 'user-demo-group-owner', 'Group Owner Demo')
     ON CONFLICT (id) DO NOTHING`,
    [d.userKabi, d.userPlanet, d.userOwner],
  );
  await pool.query(
    `INSERT INTO role(id, business_id, name) VALUES
       ($1, $3, 'kabi-role'),
       ($2, $4, 'planet-role')
     ON CONFLICT (id) DO NOTHING`,
    [d.roleKabi, d.rolePlanet, d.bizKabi, d.bizPlanet],
  );
  await pool.query(
    "INSERT INTO permission(id, feature_key, action) VALUES ($1, 'poc.read', 'R') ON CONFLICT (id) DO NOTHING",
    [d.perm],
  );
  // Membership same-tenant. owner → TË DY business-et (kontekst eksplicit per-kërkesë në API).
  await pool.query(
    `INSERT INTO business_membership(business_id, user_id, role_id) VALUES
       ($1, $3, $5),
       ($2, $4, $6),
       ($1, $7, $5),
       ($2, $7, $6)
     ON CONFLICT (business_id, user_id, role_id) DO NOTHING`,
    [d.bizKabi, d.bizPlanet, d.userKabi, d.userPlanet, d.roleKabi, d.rolePlanet, d.userOwner],
  );
}
