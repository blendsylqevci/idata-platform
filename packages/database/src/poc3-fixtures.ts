import type { Pool } from "pg";

/**
 * ID sintetike fikse për PoC-3 (letra hex — pa run shifror; mësimi i PoC-1/2).
 * Kodet e artikujve/partnerëve (art-demo-…, prt-demo-…) krijohen PER-EKZEKUTIM
 * me prapashtesë të rastësishme, që ri-ekzekutimet lokale mbi të njëjtën DB të
 * mbeten deterministike — pa ndotje mes runs.
 */
export const POC3_IDS = {
  group: "cccc0001-cccc-4ccc-8ccc-cccccccc0001",
  biz: "cccc0002-cccc-4ccc-8ccc-cccccccc0002",
} as const;

export async function seedPoc3(pool: Pool): Promise<void> {
  await pool.query(
    "INSERT INTO business_group(id, name) VALUES ($1, 'biz-demo-poc3-group') ON CONFLICT (id) DO NOTHING",
    [POC3_IDS.group],
  );
  await pool.query(
    "INSERT INTO business(id, group_id, name, nui) VALUES ($1, $2, 'biz-demo-poc3', 'DEMO-POC3') ON CONFLICT (id) DO NOTHING",
    [POC3_IDS.biz, POC3_IDS.group],
  );
}

/** Krijon/siguron një artikull stoku sintetik me sasi fillestare. */
export async function ensureArticle(pool: Pool, articleCode: string, qty: string): Promise<void> {
  await pool.query(
    `INSERT INTO poc3_stock (business_id, article_code, qty)
     VALUES ($1, $2, $3::numeric)
     ON CONFLICT (business_id, article_code) DO NOTHING`,
    [POC3_IDS.biz, articleCode, qty],
  );
}

/** Krijon/siguron një partner sintetik me limit kredie. */
export async function ensurePartner(pool: Pool, code: string, creditLimit: string): Promise<void> {
  await pool.query(
    `INSERT INTO poc3_partner (business_id, code, credit_limit)
     VALUES ($1, $2, $3::numeric)
     ON CONFLICT (business_id, code) DO NOTHING`,
    [POC3_IDS.biz, code, creditLimit],
  );
}
