import { describe, it, expect, beforeAll, afterAll } from "vitest";
import { getPool, closePool } from "./pool.js";
import { runMigrations } from "./migrate.js";
import { seedPocTenants, DEMO_IDS } from "./poc-seed.js";

// PoC-1: izolim tenant në nivel DB (composite FK) + RLS si safety-net (test-scoped).
describe("PoC-1 tenant isolation — DB (composite FK + RLS safety-net)", () => {
  beforeAll(async () => {
    await runMigrations(); // 0001 + 0002
    await seedPocTenants(getPool());
  });
  afterAll(async () => {
    await closePool();
  });

  it("composite FK: role_permission me role të biznesit TJETËR → refuzohet nga DB", async () => {
    // (planet, roleKabi) s'ekziston në role(business_id, id) → FK violation.
    await expect(
      getPool().query(
        "INSERT INTO role_permission(business_id, role_id, permission_id) VALUES ($1, $2, $3)",
        [DEMO_IDS.bizPlanet, DEMO_IDS.roleKabi, DEMO_IDS.perm],
      ),
    ).rejects.toThrow(/foreign key|violates/i);
  });

  it("composite FK: same-tenant role_permission → lejohet", async () => {
    await getPool().query(
      `INSERT INTO role_permission(business_id, role_id, permission_id) VALUES ($1, $2, $3)
       ON CONFLICT (business_id, role_id, permission_id) DO NOTHING`,
      [DEMO_IDS.bizKabi, DEMO_IDS.roleKabi, DEMO_IDS.perm],
    );
    const r = await getPool().query(
      "SELECT 1 FROM role_permission WHERE business_id = $1 AND role_id = $2",
      [DEMO_IDS.bizKabi, DEMO_IDS.roleKabi],
    );
    expect(r.rowCount ?? 0).toBeGreaterThan(0);
  });

  it("composite FK: business_membership me role të biznesit TJETËR → refuzohet nga DB", async () => {
    await expect(
      getPool().query(
        "INSERT INTO business_membership(business_id, user_id, role_id) VALUES ($1, $2, $3)",
        [DEMO_IDS.bizPlanet, DEMO_IDS.userKabi, DEMO_IDS.roleKabi],
      ),
    ).rejects.toThrow(/foreign key|violates/i);
  });

  it("RLS safety-net (test-scoped, JO canonical): kontekst i gabuar → 0 rreshta pa filtër app-i", async () => {
    const client = await getPool().connect();
    try {
      await client.query("BEGIN");
      // Politikë RLS VETËM brenda këtij testi (rollback në fund; production RLS = gated F1/ADR).
      await client.query("ALTER TABLE role ENABLE ROW LEVEL SECURITY");
      await client.query("ALTER TABLE role FORCE ROW LEVEL SECURITY");
      await client.query(
        `CREATE POLICY poc_tenant_isolation ON role
           USING (business_id = current_setting('app.current_business_id', true)::uuid)`,
      );
      // `idata` është superuser (anashkalon RLS). SET ROLE te një rol jo-superuser
      // brenda transaksionit që RLS të aplikohet realisht (gjithçka rollback në fund).
      await client.query("CREATE ROLE poc_tenant_role NOLOGIN");
      await client.query("GRANT USAGE ON SCHEMA public TO poc_tenant_role");
      await client.query("GRANT SELECT ON role TO poc_tenant_role");
      await client.query("SET LOCAL ROLE poc_tenant_role");

      // Kontekst = KABI → vetëm role të KABI-t.
      await client.query("SELECT set_config('app.current_business_id', $1, true)", [DEMO_IDS.bizKabi]);
      const kabi = await client.query<{ business_id: string }>("SELECT business_id FROM role");
      expect(kabi.rows.length).toBeGreaterThan(0);
      expect(kabi.rows.every((r) => r.business_id === DEMO_IDS.bizKabi)).toBe(true);
      expect(kabi.rows.some((r) => r.business_id === DEMO_IDS.bizPlanet)).toBe(false);

      // Kontekst = PLANET → vetëm PLANET.
      await client.query("SELECT set_config('app.current_business_id', $1, true)", [DEMO_IDS.bizPlanet]);
      const planet = await client.query<{ business_id: string }>("SELECT business_id FROM role");
      expect(planet.rows.length).toBeGreaterThan(0);
      expect(planet.rows.every((r) => r.business_id === DEMO_IDS.bizPlanet)).toBe(true);

      // Kontekst i gabuar (uuid që s'ekziston si business) → 0 rreshta (deny by default edhe në DB).
      await client.query("SELECT set_config('app.current_business_id', $1, true)", [
        "aaaa000f-aaaa-4aaa-8aaa-aaaaaaaa000f",
      ]);
      const none = await client.query("SELECT business_id FROM role");
      expect(none.rowCount).toBe(0);

      await client.query("RESET ROLE");
    } finally {
      await client.query("ROLLBACK"); // heq RLS + policy + rolin test — s'mbetet asgjë canonical
      client.release();
    }
  });
});
