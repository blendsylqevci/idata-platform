import type { Pool, PoolClient } from "pg";
import { POC2_IDS, POC2_ACCOUNTS as A, POC2_ENTRY_DATE } from "./poc2-fixtures.js";

/**
 * PoC-2 — motor postimi minimal NË DB (app-level transaction variant).
 * Gjithë aritmetika monetare bëhet në PostgreSQL `numeric` (kurrë float, as JS
 * `number`): TVSH = round(bazë × normë / 100, 2). Idempotencë me
 * (business_id, idempotency_key) UNIQUE; period-lock kontrollohet para postimit.
 * SHËNIM ROUNDING: `round()` i Postgres numeric = half-up; rregulli FINAL i
 * rrumbullakosjes = Q#4b/ACC-003 (open) — vlerat e PoC s'kanë raste kufitare.
 */

export interface PostResult {
  journalEntryId: string;
  replay: boolean;
}

async function assertPeriodOpen(cx: PoolClient, businessId: string, periodId: string): Promise<void> {
  const r = await cx.query<{ status: string }>(
    "SELECT status FROM fiscal_period WHERE business_id = $1 AND id = $2",
    [businessId, periodId],
  );
  const status = r.rows[0]?.status;
  if (!status) throw new Error("fiscal_period s'u gjet për këtë business");
  if (status !== "open") {
    throw new Error(`fiscal_period është "${status}": postimi refuzohet (period lock)`);
  }
}

/** Nëse event-i ekziston → kthe journal_entry ekzistues (replay = 0 dublime). */
async function existingEntry(
  cx: PoolClient,
  businessId: string,
  idempotencyKey: string,
): Promise<string | undefined> {
  const r = await cx.query<{ id: string }>(
    `SELECT je.id
       FROM posting_event pe
       JOIN journal_entry je
         ON je.business_id = pe.business_id AND je.posting_event_id = pe.id
      WHERE pe.business_id = $1 AND pe.idempotency_key = $2`,
    [businessId, idempotencyKey],
  );
  return r.rows[0]?.id;
}

async function insertCardEntry(
  cx: PoolClient,
  businessId: string,
  partnerId: string,
  docRef: string,
  debit: string,
  credit: string,
  type: string,
): Promise<void> {
  await cx.query(
    `INSERT INTO partner_card_entry (business_id, partner_id, doc_ref, entry_date, debit, credit, running_balance, type)
     VALUES ($1, $2, $3, $4, $5::numeric, $6::numeric,
             COALESCE((SELECT pce.running_balance FROM partner_card_entry pce
                        WHERE pce.business_id = $1 AND pce.partner_id = $2
                        ORDER BY pce.created_at DESC, pce.id DESC LIMIT 1), 0)
             + $5::numeric - $6::numeric,
             $7)`,
    [businessId, partnerId, docRef, POC2_ENTRY_DATE, debit, credit, type],
  );
}

export interface Sale18Opts {
  net: string; // p.sh. "100.00" — string, kurrë number
  sourceId: string;
  idempotencyKey: string;
  /** Default periudha e hapur; kalohet e mbyllura VETËM për testin e period-lock. */
  fiscalPeriodId?: string;
  /** Failpoint VETËM për testin e reliability #5 (rollback në mes). */
  failAfterJournal?: boolean;
}

/** (i) Varianti APP-LEVEL: faturë shitje 18% → GL + libër shitje + kartelë AR. */
export async function postSale18App(pool: Pool, o: Sale18Opts): Promise<PostResult> {
  const biz = POC2_IDS.biz;
  const periodId = o.fiscalPeriodId ?? POC2_IDS.periodOpen;
  const cx = await pool.connect();
  try {
    await cx.query("BEGIN");
    await assertPeriodOpen(cx, biz, periodId);

    const existing = await existingEntry(cx, biz, o.idempotencyKey);
    if (existing) {
      await cx.query("ROLLBACK");
      return { journalEntryId: existing, replay: true };
    }

    const ev = await cx.query<{ id: string }>(
      `INSERT INTO posting_event (business_id, source_entity, source_id, event_type, status, idempotency_key)
       VALUES ($1, 'invoice', $2, 'sale', 'pending', $3) RETURNING id`,
      [biz, o.sourceId, o.idempotencyKey],
    );
    const eventId = ev.rows[0]!.id;

    const je = await cx.query<{ id: string }>(
      `INSERT INTO journal_entry (business_id, posting_event_id, fiscal_period_id, entry_date, source_doc, status)
       VALUES ($1, $2, $3, $4, $5, 'posted') RETURNING id`,
      [biz, eventId, periodId, POC2_ENTRY_DATE, o.sourceId],
    );
    const entryId = je.rows[0]!.id;

    if (o.failAfterJournal) {
      throw new Error("failpoint: ndërprerje e simuluar në mes të postimit (reliability #5)");
    }

    // Rreshtat GL — TVSH llogaritet në SQL numeric (asnjë float në TS).
    await cx.query(
      `WITH amt AS (SELECT $3::numeric AS net, round($3::numeric * 18 / 100, 2) AS vat)
       INSERT INTO journal_line (business_id, journal_entry_id, account_code, debit, credit, partner_id)
       SELECT $1::uuid, $2::uuid, '${A.AR}',        net + vat, 0,   $4::uuid   FROM amt
       UNION ALL
       SELECT $1::uuid, $2::uuid, '${A.REVENUE}',   0,         net, NULL::uuid FROM amt
       UNION ALL
       SELECT $1::uuid, $2::uuid, '${A.VAT_OUTPUT}', 0,        vat, NULL::uuid FROM amt`,
      [biz, entryId, o.net, POC2_IDS.customer],
    );

    const tbe = await cx.query<{ id: string }>(
      `INSERT INTO tax_book_entry (business_id, book, fiscal_period_id, partner_id, doc_type,
                                   entry_date, total_base, total_vat, status, posting_event_id)
       VALUES ($1, 'shitje', $2, $3, 'SHV', $4, $5::numeric, round($5::numeric * 18 / 100, 2), 'posted', $6)
       RETURNING id`,
      [biz, periodId, POC2_IDS.customer, POC2_ENTRY_DATE, o.net, eventId],
    );
    await cx.query(
      `INSERT INTO tax_book_bucket (business_id, tax_book_entry_id, declaration_box, rate, base, vat)
       VALUES ($1, $2, '[12]', '18', $3::numeric, round($3::numeric * 18 / 100, 2))`,
      [biz, tbe.rows[0]!.id, o.net],
    );

    const gross = await cx.query<{ g: string }>(
      "SELECT ($1::numeric + round($1::numeric * 18 / 100, 2))::text AS g",
      [o.net],
    );
    await insertCardEntry(cx, biz, POC2_IDS.customer, o.sourceId, gross.rows[0]!.g, "0", "invoice");

    await cx.query("UPDATE posting_event SET status = 'posted' WHERE business_id = $1 AND id = $2", [
      biz,
      eventId,
    ]);
    await cx.query("COMMIT");
    return { journalEntryId: entryId, replay: false };
  } catch (err) {
    await cx.query("ROLLBACK");
    throw err;
  } finally {
    cx.release();
  }
}

/** (ii) Varianti FUNKSION DB (plpgsql) — i njëjti postim përmes `poc2_post_sale18`. */
export async function postSale18DbFn(
  pool: Pool,
  o: { net: string; sourceId: string; idempotencyKey: string },
): Promise<PostResult> {
  const before = await pool.query<{ n: string }>(
    "SELECT count(*)::text AS n FROM posting_event WHERE business_id = $1 AND idempotency_key = $2",
    [POC2_IDS.biz, o.idempotencyKey],
  );
  const r = await pool.query<{ id: string }>(
    "SELECT poc2_post_sale18($1, $2, $3, $4, $5, $6::numeric, $7::date) AS id",
    [POC2_IDS.biz, POC2_IDS.periodOpen, o.sourceId, o.idempotencyKey, POC2_IDS.customer, o.net, POC2_ENTRY_DATE],
  );
  return { journalEntryId: r.rows[0]!.id, replay: before.rows[0]!.n !== "0" };
}

export interface PurchaseMixedOpts {
  net18: string;
  net8: string;
  sourceId: string;
  idempotencyKey: string;
}

/** Faturë blerje MIXED 18% + 8% (FX-005): një AP gross, dy buckets [43]+[45]. */
export async function postPurchaseMixedApp(pool: Pool, o: PurchaseMixedOpts): Promise<PostResult> {
  const biz = POC2_IDS.biz;
  const cx = await pool.connect();
  try {
    await cx.query("BEGIN");
    await assertPeriodOpen(cx, biz, POC2_IDS.periodOpen);

    const existing = await existingEntry(cx, biz, o.idempotencyKey);
    if (existing) {
      await cx.query("ROLLBACK");
      return { journalEntryId: existing, replay: true };
    }

    const ev = await cx.query<{ id: string }>(
      `INSERT INTO posting_event (business_id, source_entity, source_id, event_type, status, idempotency_key)
       VALUES ($1, 'invoice', $2, 'purchase', 'pending', $3) RETURNING id`,
      [biz, o.sourceId, o.idempotencyKey],
    );
    const eventId = ev.rows[0]!.id;

    const je = await cx.query<{ id: string }>(
      `INSERT INTO journal_entry (business_id, posting_event_id, fiscal_period_id, entry_date, source_doc, status)
       VALUES ($1, $2, $3, $4, $5, 'posted') RETURNING id`,
      [biz, eventId, POC2_IDS.periodOpen, POC2_ENTRY_DATE, o.sourceId],
    );
    const entryId = je.rows[0]!.id;

    // Dr shpenzim/stok per rresht; Dr TVSH input per normë; Cr AP gross total.
    await cx.query(
      `WITH amt AS (
         SELECT $3::numeric AS n18, round($3::numeric * 18 / 100, 2) AS v18,
                $4::numeric AS n8,  round($4::numeric * 8 / 100, 2)  AS v8
       )
       INSERT INTO journal_line (business_id, journal_entry_id, account_code, debit, credit, partner_id)
       SELECT $1::uuid, $2::uuid, '${A.EXPENSE}',   n18, 0, NULL::uuid FROM amt
       UNION ALL SELECT $1::uuid, $2::uuid, '${A.EXPENSE}',   n8,  0, NULL::uuid FROM amt
       UNION ALL SELECT $1::uuid, $2::uuid, '${A.VAT_INPUT}', v18, 0, NULL::uuid FROM amt
       UNION ALL SELECT $1::uuid, $2::uuid, '${A.VAT_INPUT}', v8,  0, NULL::uuid FROM amt
       UNION ALL SELECT $1::uuid, $2::uuid, '${A.AP}', 0, n18 + v18 + n8 + v8, $5::uuid FROM amt`,
      [biz, entryId, o.net18, o.net8, POC2_IDS.supplier],
    );

    const tbe = await cx.query<{ id: string }>(
      `WITH amt AS (
         SELECT $5::numeric AS n18, round($5::numeric * 18 / 100, 2) AS v18,
                $6::numeric AS n8,  round($6::numeric * 8 / 100, 2)  AS v8
       )
       INSERT INTO tax_book_entry (business_id, book, fiscal_period_id, partner_id, doc_type,
                                   entry_date, total_base, total_vat, status, posting_event_id)
       SELECT $1, 'blerje', $2, $3, 'BLV', $4, n18 + n8, v18 + v8, 'posted', $7 FROM amt
       RETURNING id`,
      [biz, POC2_IDS.periodOpen, POC2_IDS.supplier, POC2_ENTRY_DATE, o.net18, o.net8, eventId],
    );
    // Mixed-VAT: dy buckets në të njëjtin header (kurrë "një normë per faturë").
    await cx.query(
      `INSERT INTO tax_book_bucket (business_id, tax_book_entry_id, declaration_box, rate, base, vat)
       VALUES
         ($1, $2, '[43]', '18', $3::numeric, round($3::numeric * 18 / 100, 2)),
         ($1, $2, '[45]', '8',  $4::numeric, round($4::numeric * 8 / 100, 2))`,
      [biz, tbe.rows[0]!.id, o.net18, o.net8],
    );

    const gross = await cx.query<{ g: string }>(
      `SELECT ($1::numeric + round($1::numeric * 18 / 100, 2)
             + $2::numeric + round($2::numeric * 8 / 100, 2))::text AS g`,
      [o.net18, o.net8],
    );
    await insertCardEntry(cx, biz, POC2_IDS.supplier, o.sourceId, "0", gross.rows[0]!.g, "invoice");

    await cx.query("UPDATE posting_event SET status = 'posted' WHERE business_id = $1 AND id = $2", [
      biz,
      eventId,
    ]);
    await cx.query("COMMIT");
    return { journalEntryId: entryId, replay: false };
  } catch (err) {
    await cx.query("ROLLBACK");
    throw err;
  } finally {
    cx.release();
  }
}

/** Krijon posting_run DRAFT për shlyerjen e fermerit (settlement). Draft NUK poston. */
export async function createFarmerRun(pool: Pool): Promise<string> {
  const r = await pool.query<{ id: string }>(
    `INSERT INTO posting_run (business_id, fiscal_period_id, type, status)
     VALUES ($1, $2, 'settlement', 'draft') RETURNING id`,
    [POC2_IDS.biz, POC2_IDS.periodOpen],
  );
  return r.rows[0]!.id;
}

export async function approveFarmerRun(pool: Pool, runId: string): Promise<void> {
  await pool.query(
    "UPDATE posting_run SET status = 'approved' WHERE business_id = $1 AND id = $2",
    [POC2_IDS.biz, runId],
  );
}

export interface FarmerActOpts {
  runId: string;
  net: string; // baza e aktit të fermerit
  sourceId: string;
  idempotencyKey: string;
}

/**
 * Poston aktin e blerjes së fermerit (flat 8% → bucket [51]) VETËM nëse run-i
 * është approved/locked (Data Model §C :54 — "Draft NUK poston GL/AP").
 * TVSH flat → llogari PENDING (deductibility = ACC-006 pa sign-off);
 * tax_book_entry.status = 'pending_accountant_signoff'.
 */
export async function postFarmerAct(pool: Pool, o: FarmerActOpts): Promise<PostResult> {
  const biz = POC2_IDS.biz;
  const cx = await pool.connect();
  try {
    await cx.query("BEGIN");
    await assertPeriodOpen(cx, biz, POC2_IDS.periodOpen);

    const run = await cx.query<{ status: string }>(
      "SELECT status FROM posting_run WHERE business_id = $1 AND id = $2",
      [biz, o.runId],
    );
    const runStatus = run.rows[0]?.status;
    if (!runStatus) throw new Error("posting_run s'u gjet");
    if (runStatus !== "approved" && runStatus !== "locked") {
      throw new Error(`posting_run është "${runStatus}": draft NUK poston GL/AP (Data Model §C)`);
    }

    const existing = await existingEntry(cx, biz, o.idempotencyKey);
    if (existing) {
      await cx.query("ROLLBACK");
      return { journalEntryId: existing, replay: true };
    }

    const ev = await cx.query<{ id: string }>(
      `INSERT INTO posting_event (business_id, source_entity, source_id, event_type, posting_run_id, status, idempotency_key)
       VALUES ($1, 'farmer_act', $2, 'farmer_settlement', $3, 'pending', $4) RETURNING id`,
      [biz, o.sourceId, o.runId, o.idempotencyKey],
    );
    const eventId = ev.rows[0]!.id;

    const je = await cx.query<{ id: string }>(
      `INSERT INTO journal_entry (business_id, posting_event_id, fiscal_period_id, entry_date, source_doc, status)
       VALUES ($1, $2, $3, $4, $5, 'posted') RETURNING id`,
      [biz, eventId, POC2_IDS.periodOpen, POC2_ENTRY_DATE, o.sourceId],
    );
    const entryId = je.rows[0]!.id;

    await cx.query(
      `WITH amt AS (SELECT $3::numeric AS net, round($3::numeric * 8 / 100, 2) AS vat)
       INSERT INTO journal_line (business_id, journal_entry_id, account_code, debit, credit, partner_id)
       SELECT $1::uuid, $2::uuid, '${A.EXPENSE}',                 net, 0,         NULL::uuid FROM amt
       UNION ALL SELECT $1::uuid, $2::uuid, '${A.VAT_FARMER_FLAT_PENDING}', vat, 0, NULL::uuid FROM amt
       UNION ALL SELECT $1::uuid, $2::uuid, '${A.AP_FARMER}',     0,   net + vat, $4::uuid   FROM amt`,
      [biz, entryId, o.net, POC2_IDS.farmer],
    );

    const tbe = await cx.query<{ id: string }>(
      `INSERT INTO tax_book_entry (business_id, book, fiscal_period_id, partner_id, doc_type,
                                   entry_date, total_base, total_vat, status, posting_event_id)
       VALUES ($1, 'blerje', $2, $3, 'AKT_FERMER', $4, $5::numeric,
               round($5::numeric * 8 / 100, 2), 'pending_accountant_signoff', $6)
       RETURNING id`,
      [biz, POC2_IDS.periodOpen, POC2_IDS.farmer, POC2_ENTRY_DATE, o.net, eventId],
    );
    await cx.query(
      `INSERT INTO tax_book_bucket (business_id, tax_book_entry_id, declaration_box, rate, base, vat)
       VALUES ($1, $2, '[51]', 'flat-8', $3::numeric, round($3::numeric * 8 / 100, 2))`,
      [biz, tbe.rows[0]!.id, o.net],
    );

    const gross = await cx.query<{ g: string }>(
      "SELECT ($1::numeric + round($1::numeric * 8 / 100, 2))::text AS g",
      [o.net],
    );
    await insertCardEntry(cx, biz, POC2_IDS.farmer, o.sourceId, "0", gross.rows[0]!.g, "settlement");

    await cx.query("UPDATE posting_event SET status = 'posted' WHERE business_id = $1 AND id = $2", [
      biz,
      eventId,
    ]);
    await cx.query(
      "UPDATE posting_run SET status = 'posted' WHERE business_id = $1 AND id = $2",
      [biz, o.runId],
    );
    await cx.query("COMMIT");
    return { journalEntryId: entryId, replay: false };
  } catch (err) {
    await cx.query("ROLLBACK");
    throw err;
  } finally {
    cx.release();
  }
}

export interface SalesBookExport {
  rows: { doc_type: string; declaration_box: string; rate: string; base: string; vat: string }[];
  box12_base: string; // shitjet e tatueshme 18% (bazë)
  box30_total_vat: string; // TVSH totale e shitjeve
}

/** Libri i Shitjes nga tax_book_bucket: [12] bazë 18% · [30] = Σ TVSH output. */
export async function exportSalesBook(pool: Pool): Promise<SalesBookExport> {
  const rows = await pool.query<SalesBookExport["rows"][number]>(
    `SELECT tbe.doc_type, b.declaration_box, b.rate, b.base::text, b.vat::text
       FROM tax_book_bucket b
       JOIN tax_book_entry tbe
         ON tbe.business_id = b.business_id AND tbe.id = b.tax_book_entry_id
      WHERE b.business_id = $1 AND tbe.book = 'shitje'
      ORDER BY tbe.created_at, b.declaration_box`,
    [POC2_IDS.biz],
  );
  const totals = await pool.query<{ box12: string; box30: string }>(
    `SELECT COALESCE(SUM(base) FILTER (WHERE declaration_box = '[12]'), 0)::text AS box12,
            COALESCE(SUM(vat), 0)::text AS box30
       FROM tax_book_bucket b
       JOIN tax_book_entry tbe
         ON tbe.business_id = b.business_id AND tbe.id = b.tax_book_entry_id
      WHERE b.business_id = $1 AND tbe.book = 'shitje'`,
    [POC2_IDS.biz],
  );
  return {
    rows: rows.rows,
    box12_base: totals.rows[0]!.box12,
    box30_total_vat: totals.rows[0]!.box30,
  };
}

export interface PurchaseBookExport {
  rows: { doc_type: string; declaration_box: string; rate: string; base: string; vat: string }[];
  box43_base: string;
  box45_base: string;
  box51_base: string;
  /** [67] = TVSH e zbritshme = v18 + v8. TVSH flat e fermerit ([51]) NUK
   *  numërohet këtu: deductibility = pending sign-off (ACC-006). */
  box67_total_deductible_vat: string;
}

/** Libri i Blerjes nga tax_book_bucket: [43]/[45]/[51]; [67] = v18+v8 (pa [51], ACC-006). */
export async function exportPurchaseBook(pool: Pool): Promise<PurchaseBookExport> {
  const rows = await pool.query<PurchaseBookExport["rows"][number]>(
    `SELECT tbe.doc_type, b.declaration_box, b.rate, b.base::text, b.vat::text
       FROM tax_book_bucket b
       JOIN tax_book_entry tbe
         ON tbe.business_id = b.business_id AND tbe.id = b.tax_book_entry_id
      WHERE b.business_id = $1 AND tbe.book = 'blerje'
      ORDER BY tbe.created_at, b.declaration_box`,
    [POC2_IDS.biz],
  );
  const totals = await pool.query<{
    box43: string;
    box45: string;
    box51: string;
    box67: string;
  }>(
    `SELECT COALESCE(SUM(base) FILTER (WHERE declaration_box = '[43]'), 0)::text AS box43,
            COALESCE(SUM(base) FILTER (WHERE declaration_box = '[45]'), 0)::text AS box45,
            COALESCE(SUM(base) FILTER (WHERE declaration_box = '[51]'), 0)::text AS box51,
            COALESCE(SUM(vat) FILTER (WHERE declaration_box IN ('[43]','[45]')), 0)::text AS box67
       FROM tax_book_bucket b
       JOIN tax_book_entry tbe
         ON tbe.business_id = b.business_id AND tbe.id = b.tax_book_entry_id
      WHERE b.business_id = $1 AND tbe.book = 'blerje'`,
    [POC2_IDS.biz],
  );
  const t = totals.rows[0]!;
  return {
    rows: rows.rows,
    box43_base: t.box43,
    box45_base: t.box45,
    box51_base: t.box51,
    box67_total_deductible_vat: t.box67,
  };
}
