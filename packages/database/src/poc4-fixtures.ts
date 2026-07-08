import type { Pool } from "pg";

/** ID sintetike fikse për PoC-4 (letra hex — pa run shifror). */
export const POC4_IDS = {
  group: "dddd0001-dddd-4ddd-8ddd-dddddddd0001",
  biz: "dddd0002-dddd-4ddd-8ddd-dddddddd0002",
} as const;

export const POC4_HUB_ID = "hub-demo-01";

/** Routing artikull→stacion (config i hub-it në PoC; tabela reale e routing = F7/F8). */
export const POC4_ROUTING: Record<string, string> = {
  "art-demo-g1": "demo-grill",
  "art-demo-g2": "demo-grill",
  "art-demo-b1": "demo-bar",
};
/** Artikull i PA-mapuar → stacion default + flamurohet (sjellje e definuar). */
export const POC4_DEFAULT_STATION = "demo-grill";

/** Seed në CLOUD DB (business për FK-të e poc4_cloud_*). */
export async function seedPoc4Cloud(pool: Pool): Promise<void> {
  await pool.query(
    "INSERT INTO business_group(id, name) VALUES ($1, 'biz-demo-poc4-group') ON CONFLICT (id) DO NOTHING",
    [POC4_IDS.group],
  );
  await pool.query(
    "INSERT INTO business(id, group_id, name, nui) VALUES ($1, $2, 'biz-demo-poc4', 'DEMO-POC4') ON CONFLICT (id) DO NOTHING",
    [POC4_IDS.biz, POC4_IDS.group],
  );
}
