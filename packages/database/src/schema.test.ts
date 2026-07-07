import { describe, it, expect, beforeAll, afterAll } from "vitest";
import { getPool, closePool } from "./pool.js";
import { ping } from "./ping.js";
import { runMigrations } from "./migrate.js";

// Kërkon Postgres lokal të ngritur (docker compose up) + env PG* (shih .env.example).
describe("database skeleton (kërkon Postgres)", () => {
  beforeAll(async () => {
    await runMigrations();
  });
  afterAll(async () => {
    await closePool();
  });

  it("ping bën round-trip real në DB", async () => {
    const r = await ping(getPool());
    expect(r.ok).toBe(true);
    expect(r.echo).toBe("pong");
    expect(typeof r.ts).toBe("string");
  });

  it("migrimet janë idempotente (re-run → asnjë i ri)", async () => {
    const res = await runMigrations();
    expect(res.applied).toEqual([]);
    expect(res.skipped).toContain("0001_initial_skeleton.sql");
  });

  it("business_id është NOT NULL DIREKT në tabelat tenant-scoped", async () => {
    const q = await getPool().query<{ table_name: string; is_nullable: string }>(
      `SELECT table_name, is_nullable
         FROM information_schema.columns
        WHERE column_name = 'business_id'
          AND table_name IN ('role','role_permission','business_membership','audit_log')
        ORDER BY table_name`,
    );
    const map = Object.fromEntries(q.rows.map((r) => [r.table_name, r.is_nullable]));
    expect(map).toEqual({
      audit_log: "NO",
      business_membership: "NO",
      role: "NO",
      role_permission: "NO",
    });
  });

  it("permission NUK ka business_id (katalog global)", async () => {
    const q = await getPool().query(
      `SELECT 1 FROM information_schema.columns
        WHERE table_name = 'permission' AND column_name = 'business_id'`,
    );
    expect(q.rowCount).toBe(0);
  });

  it("audit_log është append-only: UPDATE dhe DELETE refuzohen", async () => {
    const pool = getPool();
    // Seed SINTETIK (biz-demo-*) për të pasur një rresht audit_log me business_id valid.
    const grp = await pool.query<{ id: string }>(
      "INSERT INTO business_group(name) VALUES ('biz-demo-group') RETURNING id",
    );
    const groupId = grp.rows[0]!.id;
    const biz = await pool.query<{ id: string }>(
      "INSERT INTO business(group_id, name, nui) VALUES ($1, 'biz-demo-0001', 'DEMO-NUI') RETURNING id",
      [groupId],
    );
    const businessId = biz.rows[0]!.id;
    const row = await pool.query<{ id: string }>(
      "INSERT INTO audit_log(business_id, entity, action) VALUES ($1, 'demo', 'create') RETURNING id",
      [businessId],
    );
    const auditId = row.rows[0]!.id;

    await expect(
      pool.query("UPDATE audit_log SET action = 'tamper' WHERE id = $1", [auditId]),
    ).rejects.toThrow(/append-only/);
    await expect(
      pool.query("DELETE FROM audit_log WHERE id = $1", [auditId]),
    ).rejects.toThrow(/append-only/);

    // Rreshti mbetet i pandryshuar.
    const check = await pool.query<{ action: string }>(
      "SELECT action FROM audit_log WHERE id = $1",
      [auditId],
    );
    expect(check.rows[0]!.action).toBe("create");
  });
});
