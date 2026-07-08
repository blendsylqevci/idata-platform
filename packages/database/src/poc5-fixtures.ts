import type { Pool } from "pg";

/** ID sintetike fikse për PoC-5 (letra hex — pa run shifror). ZERO vlera reale. */
export const POC5_IDS = {
  group: "eeee0001-eeee-4eee-8eee-eeeeeeee0001",
  biz: "eeee0002-eeee-4eee-8eee-eeeeeeee0002",
} as const;

export async function seedPoc5(pool: Pool): Promise<void> {
  await pool.query(
    "INSERT INTO business_group(id, name) VALUES ($1, 'biz-demo-poc5-group') ON CONFLICT (id) DO NOTHING",
    [POC5_IDS.group],
  );
  await pool.query(
    "INSERT INTO business(id, group_id, name, nui) VALUES ($1, $2, 'biz-demo-poc5', 'DEMO-POC5') ON CONFLICT (id) DO NOTHING",
    [POC5_IDS.biz, POC5_IDS.group],
  );
}
