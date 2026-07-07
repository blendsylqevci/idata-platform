import type { Pool } from "pg";

/**
 * ID sintetike fikse për PoC-2 (asnjë e dhënë reale). UUID me letra hex — pa
 * run të gjatë shifrash që tripin skanerët e sekreteve (mësim nga PoC-1).
 * partner_id-të janë uuid pa FK: partner master table = Faza 1.
 */
export const POC2_IDS = {
  group: "bbbb0001-bbbb-4bbb-8bbb-bbbbbbbb0001",
  biz: "bbbb0002-bbbb-4bbb-8bbb-bbbbbbbb0002",
  customer: "bbbb0003-bbbb-4bbb-8bbb-bbbbbbbb0003",
  supplier: "bbbb0004-bbbb-4bbb-8bbb-bbbbbbbb0004",
  farmer: "bbbb0005-bbbb-4bbb-8bbb-bbbbbbbb0005",
  periodOpen: "bbbb0006-bbbb-4bbb-8bbb-bbbbbbbb0006", // 2027-01 open
  periodClosed: "bbbb0007-bbbb-4bbb-8bbb-bbbbbbbb0007", // 2026-12 closed (per period-lock test)
} as const;

/** Kodet e llogarive = PLACEHOLDER provisional — të njëjtat vlera me
 *  `packages/accounting/src/accounts.ts` (COA final = ACC-003 open).
 *  Dublim i qëllimshëm për të mos e bërë paketën library-first të varur nga DB
 *  (dhe anasjelltas) gjatë PoC — harmonizohet pas ADR-012/ACC-003. */
export const POC2_ACCOUNTS = {
  AR: "1210",
  AP: "4010",
  AP_FARMER: "4015",
  REVENUE: "6010",
  EXPENSE: "5010",
  VAT_OUTPUT: "4710",
  VAT_INPUT: "4720",
  VAT_FARMER_FLAT_PENDING: "4725",
} as const;

/** Datë fikse sintetike për determinizëm të evidencës. */
export const POC2_ENTRY_DATE = "2027-01-15";

/** Seed idempotent: business + dy fiscal_period (open + closed). */
export async function seedPoc2(pool: Pool): Promise<void> {
  const d = POC2_IDS;
  await pool.query(
    "INSERT INTO business_group(id, name) VALUES ($1, 'biz-demo-poc2-group') ON CONFLICT (id) DO NOTHING",
    [d.group],
  );
  await pool.query(
    "INSERT INTO business(id, group_id, name, nui) VALUES ($1, $2, 'biz-demo-poc2', 'DEMO-POC2') ON CONFLICT (id) DO NOTHING",
    [d.biz, d.group],
  );
  await pool.query(
    `INSERT INTO fiscal_period(id, business_id, period, status) VALUES
       ($1, $3, '2027-01', 'open'),
       ($2, $3, '2026-12', 'closed')
     ON CONFLICT (id) DO NOTHING`,
    [d.periodOpen, d.periodClosed, d.biz],
  );
}
