import { describe, it, expect, beforeAll, afterAll } from "vitest";
import { randomUUID } from "node:crypto";
import pg from "pg";
import { getPool, closePool } from "./pool.js";
import { runMigrations } from "./migrate.js";
import { seedPoc2, POC2_IDS, POC2_ACCOUNTS as A, POC2_ENTRY_DATE } from "./poc2-fixtures.js";
import {
  postSale18App,
  postSale18DbFn,
  postPurchaseMixedApp,
  createFarmerRun,
  approveFarmerRun,
  postFarmerAct,
  exportSalesBook,
  exportPurchaseBook,
} from "./posting.js";

const { Pool: PgPool } = pg;

// Çelësa idempotency FIKSË për dokumentet bazë → ri-ekzekutimi i testeve mbi të
// njëjtën DB = replay pa dublime (vetë kjo është pjesë e asaj që provohet).
const IDEM = {
  saleApp: "poc2-sale-app",
  saleFn: "poc2-sale-fn",
  purchaseMixed: "poc2-purchase-mixed",
} as const;

// Akti i fermerit = unik per ekzekutim (cikli draft→approve→post provohet i plotë çdo herë).
const FARMER_SOURCE = `akt-fermer-${randomUUID()}`;
const FARMER_IDEM = `poc2-farmer-${randomUUID()}`;

async function linesOf(idem: string) {
  const r = await getPool().query<{ account_code: string; debit: string; credit: string }>(
    `SELECT jl.account_code, jl.debit::text, jl.credit::text
       FROM posting_event pe
       JOIN journal_entry je ON je.business_id = pe.business_id AND je.posting_event_id = pe.id
       JOIN journal_line jl ON jl.business_id = je.business_id AND jl.journal_entry_id = je.id
      WHERE pe.business_id = $1 AND pe.idempotency_key = $2
      ORDER BY jl.account_code, jl.debit, jl.credit`,
    [POC2_IDS.biz, idem],
  );
  return r.rows;
}

async function bucketsOf(idem: string) {
  const r = await getPool().query<{
    declaration_box: string;
    rate: string;
    base: string;
    vat: string;
  }>(
    `SELECT b.declaration_box, b.rate, b.base::text, b.vat::text
       FROM posting_event pe
       JOIN tax_book_entry tbe ON tbe.business_id = pe.business_id AND tbe.posting_event_id = pe.id
       JOIN tax_book_bucket b ON b.business_id = tbe.business_id AND b.tax_book_entry_id = tbe.id
      WHERE pe.business_id = $1 AND pe.idempotency_key = $2
      ORDER BY b.declaration_box`,
    [POC2_IDS.biz, idem],
  );
  return r.rows;
}

describe("PoC-2 accounting posting — DB (kërkon Postgres)", () => {
  beforeAll(async () => {
    await runMigrations(); // 0001+0002+0003
    await seedPoc2(getPool());
    // Dokumentet bazë (idempotente — replay i sigurt në çdo ri-ekzekutim):
    await postSale18App(getPool(), { net: "100.00", sourceId: "shv-demo-app", idempotencyKey: IDEM.saleApp });
    await postSale18DbFn(getPool(), { net: "100.00", sourceId: "shv-demo-fn", idempotencyKey: IDEM.saleFn });
    await postPurchaseMixedApp(getPool(), {
      net18: "100.00",
      net8: "50.00",
      sourceId: "blv-demo-mixed",
      idempotencyKey: IDEM.purchaseMixed,
    });
  });
  afterAll(async () => {
    await closePool();
  });

  it("debit = kredit në ÇDO journal_entry (asnjë entry i pabalancuar)", async () => {
    const r = await getPool().query(
      `SELECT je.id FROM journal_entry je
        JOIN journal_line jl ON jl.business_id = je.business_id AND jl.journal_entry_id = je.id
       WHERE je.business_id = $1
       GROUP BY je.id
      HAVING SUM(jl.debit) <> SUM(jl.credit)`,
      [POC2_IDS.biz],
    );
    expect(r.rowCount).toBe(0);
  });

  it("shitja 18%: TVSH = 18.00 ekzakt; GL = Dr AR 118 / Cr REV 100 / Cr VAT 18", async () => {
    const lines = await linesOf(IDEM.saleApp);
    expect(lines).toEqual([
      { account_code: A.AR, debit: "118.00", credit: "0.00" },
      { account_code: A.VAT_OUTPUT, debit: "0.00", credit: "18.00" },
      { account_code: A.REVENUE, debit: "0.00", credit: "100.00" },
    ]);
    const buckets = await bucketsOf(IDEM.saleApp);
    expect(buckets).toEqual([{ declaration_box: "[12]", rate: "18", base: "100.00", vat: "18.00" }]);
  });

  it("FX-005 blerje MIXED: 2 buckets [43]=100/18 dhe [45]=50/4; AP gross 172.00", async () => {
    const buckets = await bucketsOf(IDEM.purchaseMixed);
    expect(buckets).toEqual([
      { declaration_box: "[43]", rate: "18", base: "100.00", vat: "18.00" },
      { declaration_box: "[45]", rate: "8", base: "50.00", vat: "4.00" },
    ]);
    const lines = await linesOf(IDEM.purchaseMixed);
    const ap = lines.find((l) => l.account_code === A.AP);
    expect(ap?.credit).toBe("172.00");
    const vatInputs = lines.filter((l) => l.account_code === A.VAT_INPUT).map((l) => l.debit);
    expect(vatInputs.sort()).toEqual(["18.00", "4.00"].sort());
  });

  it("dual-variant (ADR-012 input): app-level dhe funksioni DB japin rezultat identik", async () => {
    const [la, lf] = [await linesOf(IDEM.saleApp), await linesOf(IDEM.saleFn)];
    expect(la).toEqual(lf);
    const [ba, bf] = [await bucketsOf(IDEM.saleApp), await bucketsOf(IDEM.saleFn)];
    expect(ba).toEqual(bf);
  });

  it("akti i fermerit: draft NUK poston GL/AP; approved poston → [51] flat-8 + status pending_accountant_signoff", async () => {
    const runId = await createFarmerRun(getPool());

    // DRAFT → refuzohet me rregullin e Data Model §C :54.
    await expect(
      postFarmerAct(getPool(), { runId, net: "500.00", sourceId: FARMER_SOURCE, idempotencyKey: FARMER_IDEM }),
    ).rejects.toThrow(/draft NUK poston/);
    const noGl = await getPool().query(
      "SELECT 1 FROM posting_event WHERE business_id = $1 AND idempotency_key = $2",
      [POC2_IDS.biz, FARMER_IDEM],
    );
    expect(noGl.rowCount).toBe(0); // zero gjurmë: pa event, pa GL, pa AP

    // APPROVED → poston.
    await approveFarmerRun(getPool(), runId);
    const res = await postFarmerAct(getPool(), {
      runId,
      net: "500.00",
      sourceId: FARMER_SOURCE,
      idempotencyKey: FARMER_IDEM,
    });
    expect(res.replay).toBe(false);

    const lines = await linesOf(FARMER_IDEM);
    expect(lines).toEqual([
      { account_code: A.AP_FARMER, debit: "0.00", credit: "540.00" },
      { account_code: A.VAT_FARMER_FLAT_PENDING, debit: "40.00", credit: "0.00" },
      { account_code: A.EXPENSE, debit: "500.00", credit: "0.00" },
    ]);
    const buckets = await bucketsOf(FARMER_IDEM);
    expect(buckets).toEqual([{ declaration_box: "[51]", rate: "flat-8", base: "500.00", vat: "40.00" }]);

    const tbe = await getPool().query<{ status: string }>(
      `SELECT tbe.status FROM posting_event pe
        JOIN tax_book_entry tbe ON tbe.business_id = pe.business_id AND tbe.posting_event_id = pe.id
       WHERE pe.business_id = $1 AND pe.idempotency_key = $2`,
      [POC2_IDS.biz, FARMER_IDEM],
    );
    expect(tbe.rows[0]?.status).toBe("pending_accountant_signoff");
  });

  it("libri i blerjes: [67] = v18 + v8 EKZAKT; [51] merr VETËM akte fermeri", async () => {
    const book = await exportPurchaseBook(getPool());
    // [67] = v18+v8 — barazi numerike EKZAKTE në SQL (kurrë float në TS).
    const eq = await getPool().query<{ ok: boolean }>(
      `SELECT (SELECT COALESCE(SUM(vat), 0) FROM tax_book_bucket b
                JOIN tax_book_entry t ON t.business_id = b.business_id AND t.id = b.tax_book_entry_id
               WHERE b.business_id = $1 AND t.book = 'blerje' AND b.declaration_box = '[43]')
            + (SELECT COALESCE(SUM(vat), 0) FROM tax_book_bucket b
                JOIN tax_book_entry t ON t.business_id = b.business_id AND t.id = b.tax_book_entry_id
               WHERE b.business_id = $1 AND t.book = 'blerje' AND b.declaration_box = '[45]')
            = $2::numeric AS ok`,
      [POC2_IDS.biz, book.box67_total_deductible_vat],
    );
    expect(eq.rows[0]?.ok).toBe(true);
    expect(book.box67_total_deductible_vat).toBe("22.00"); // 18.00 + 4.00 (dokumentet fikse)
    expect(book.box43_base).toBe("100.00");
    expect(book.box45_base).toBe("50.00");

    // [51] përmban vetëm AKT_FERMER (asnjë doc_type tjetër nuk bie aty).
    const only51 = await getPool().query(
      `SELECT DISTINCT tbe.doc_type FROM tax_book_bucket b
        JOIN tax_book_entry tbe ON tbe.business_id = b.business_id AND tbe.id = b.tax_book_entry_id
       WHERE b.business_id = $1 AND b.declaration_box = '[51]'`,
      [POC2_IDS.biz],
    );
    expect(only51.rows).toEqual([{ doc_type: "AKT_FERMER" }]);
  });

  it("libri i shitjes: [12] = 200.00 (2 shitje × 100) dhe [30] = 36.00", async () => {
    const book = await exportSalesBook(getPool());
    expect(book.box12_base).toBe("200.00");
    expect(book.box30_total_vat).toBe("36.00");
  });

  it("kartela e partnerit (AR/AP minimal): klienti Dr 118; furnitori Cr 172; fermeri Cr 540", async () => {
    const cards = await getPool().query<{ partner_id: string; debit: string; credit: string; type: string }>(
      `SELECT partner_id, debit::text, credit::text, type FROM partner_card_entry
        WHERE business_id = $1 AND doc_ref IN ('shv-demo-app', 'blv-demo-mixed', $2)
        ORDER BY created_at`,
      [POC2_IDS.biz, FARMER_SOURCE],
    );
    const byPartner = Object.fromEntries(cards.rows.map((c) => [c.partner_id, c]));
    expect(byPartner[POC2_IDS.customer]).toMatchObject({ debit: "118.00", credit: "0.00", type: "invoice" });
    expect(byPartner[POC2_IDS.supplier]).toMatchObject({ debit: "0.00", credit: "172.00", type: "invoice" });
    expect(byPartner[POC2_IDS.farmer]).toMatchObject({ debit: "0.00", credit: "540.00", type: "settlement" });
  });

  it("period lock: postim në fiscal_period të MBYLLUR → refuzohet, zero gjurmë", async () => {
    const idem = `poc2-locked-${randomUUID()}`;
    await expect(
      postSale18App(getPool(), {
        net: "10.00",
        sourceId: "shv-locked",
        idempotencyKey: idem,
        fiscalPeriodId: POC2_IDS.periodClosed,
      }),
    ).rejects.toThrow(/period lock/);
    const r = await getPool().query(
      "SELECT 1 FROM posting_event WHERE business_id = $1 AND idempotency_key = $2",
      [POC2_IDS.biz, idem],
    );
    expect(r.rowCount).toBe(0);
  });

  it("reliability #6 (restart/replay): ri-postimi i të njëjtit event → idempotent, 0 dublime", async () => {
    const before = await getPool().query<{ n: string }>(
      "SELECT count(*)::text AS n FROM journal_entry WHERE business_id = $1",
      [POC2_IDS.biz],
    );
    const r1 = await postSale18App(getPool(), { net: "100.00", sourceId: "shv-demo-app", idempotencyKey: IDEM.saleApp });
    const r2 = await postSale18DbFn(getPool(), { net: "100.00", sourceId: "shv-demo-fn", idempotencyKey: IDEM.saleFn });
    const r3 = await postPurchaseMixedApp(getPool(), {
      net18: "100.00",
      net8: "50.00",
      sourceId: "blv-demo-mixed",
      idempotencyKey: IDEM.purchaseMixed,
    });
    expect(r1.replay && r2.replay && r3.replay).toBe(true);
    const after = await getPool().query<{ n: string }>(
      "SELECT count(*)::text AS n FROM journal_entry WHERE business_id = $1",
      [POC2_IDS.biz],
    );
    expect(after.rows[0]!.n).toBe(before.rows[0]!.n);
  });

  it("reliability #5 (interrupted): dështim në mes → rollback, ZERO gjendje gjysmake", async () => {
    const idem = `poc2-failpoint-${randomUUID()}`;
    await expect(
      postSale18App(getPool(), {
        net: "77.00",
        sourceId: "shv-failpoint",
        idempotencyKey: idem,
        failAfterJournal: true,
      }),
    ).rejects.toThrow(/failpoint/);
    // Asgjë e pjesshme: pa event, pa entry, pa rreshta, pa tax book, pa kartelë.
    const ev = await getPool().query(
      "SELECT 1 FROM posting_event WHERE business_id = $1 AND idempotency_key = $2",
      [POC2_IDS.biz, idem],
    );
    expect(ev.rowCount).toBe(0);
    const je = await getPool().query(
      "SELECT 1 FROM journal_entry WHERE business_id = $1 AND source_doc = 'shv-failpoint'",
      [POC2_IDS.biz],
    );
    expect(je.rowCount).toBe(0);
    const pce = await getPool().query(
      "SELECT 1 FROM partner_card_entry WHERE business_id = $1 AND doc_ref = 'shv-failpoint'",
      [POC2_IDS.biz],
    );
    expect(pce.rowCount).toBe(0);
  });

  it("reliability #7 (DB unavailable): DSN i gabuar → gabim i pastër, asnjë shkrim", async () => {
    const badPool = new PgPool({
      host: "localhost",
      port: 59987, // port pa Postgres — simulim BRENDA test scope (container-i s'preket)
      user: "idata",
      database: "idata",
      connectionTimeoutMillis: 1200,
    });
    const idem = `poc2-dbdown-${randomUUID()}`;
    await expect(
      postSale18App(badPool, { net: "10.00", sourceId: "shv-dbdown", idempotencyKey: idem }),
    ).rejects.toThrow();
    await badPool.end();
    const r = await getPool().query(
      "SELECT 1 FROM posting_event WHERE business_id = $1 AND idempotency_key = $2",
      [POC2_IDS.biz, idem],
    );
    expect(r.rowCount).toBe(0);
  });

  it("journal_entry/journal_line janë APPEND-ONLY (UPDATE/DELETE refuzohen)", async () => {
    const je = await getPool().query<{ id: string }>(
      "SELECT id FROM journal_entry WHERE business_id = $1 LIMIT 1",
      [POC2_IDS.biz],
    );
    const jl = await getPool().query<{ id: string }>(
      "SELECT id FROM journal_line WHERE business_id = $1 LIMIT 1",
      [POC2_IDS.biz],
    );
    await expect(
      getPool().query("UPDATE journal_entry SET source_doc = 'tamper' WHERE id = $1", [je.rows[0]!.id]),
    ).rejects.toThrow(/append-only/);
    await expect(
      getPool().query("DELETE FROM journal_line WHERE id = $1", [jl.rows[0]!.id]),
    ).rejects.toThrow(/append-only/);
  });

  it("trigger DEFERRABLE: entry i pabalancuar refuzohet NË COMMIT të transaksionit", async () => {
    const cx = await getPool().connect();
    const idem = `poc2-unbalanced-${randomUUID()}`;
    try {
      await cx.query("BEGIN");
      const ev = await cx.query<{ id: string }>(
        `INSERT INTO posting_event (business_id, source_entity, source_id, event_type, status, idempotency_key)
         VALUES ($1, 'invoice', 'shv-unbalanced', 'sale', 'pending', $2) RETURNING id`,
        [POC2_IDS.biz, idem],
      );
      const je = await cx.query<{ id: string }>(
        `INSERT INTO journal_entry (business_id, posting_event_id, fiscal_period_id, entry_date, source_doc)
         VALUES ($1, $2, $3, $4, 'shv-unbalanced') RETURNING id`,
        [POC2_IDS.biz, ev.rows[0]!.id, POC2_IDS.periodOpen, POC2_ENTRY_DATE],
      );
      // Vetëm njëra anë → i pabalancuar. INSERT kalon; COMMIT duhet të dështojë.
      await cx.query(
        `INSERT INTO journal_line (business_id, journal_entry_id, account_code, debit, credit)
         VALUES ($1, $2, '1210', 10.00, 0)`,
        [POC2_IDS.biz, je.rows[0]!.id],
      );
      await expect(cx.query("COMMIT")).rejects.toThrow(/pabalancuar/);
    } finally {
      cx.release();
    }
    // Transaksioni u rrëzua në commit → zero gjurmë.
    const r = await getPool().query(
      "SELECT 1 FROM posting_event WHERE business_id = $1 AND idempotency_key = $2",
      [POC2_IDS.biz, idem],
    );
    expect(r.rowCount).toBe(0);
  });
});
