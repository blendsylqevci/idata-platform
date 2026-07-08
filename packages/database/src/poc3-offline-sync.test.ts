import { describe, it, expect, beforeAll, afterAll } from "vitest";
import { randomUUID } from "node:crypto";
import { mkdtemp } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import pg from "pg";
import { getPool, closePool } from "./pool.js";
import { runMigrations } from "./migrate.js";
import { seedPoc3, ensureArticle, ensurePartner, POC3_IDS } from "./poc3-fixtures.js";
import { DeviceSimulator, FileLocalStore } from "./poc3-device.js";
import { ServerApplyPg } from "./poc3-server.js";
import { syncDevice, InProcessTransport, FlakyTransport } from "./poc3-sync.js";

const { Pool: PgPool } = pg;

// Kode sintetike UNIKE per ekzekutim → ri-ekzekutimet lokale mbi të njëjtën DB
// mbeten deterministike (CI = gjithmonë DB e pastër).
const RUN = randomUUID().slice(0, 8);
const ART = (n: string) => `art-demo-${RUN}-${n}`;
const PRT = (n: string) => `prt-demo-${RUN}-${n}`;
const DEV = (n: string) => `dev-demo-${RUN}-${n}`;

let storeDir: string;
let store: FileLocalStore;

const SERVER_PRICES = new Map<string, string>(); // katalogu i çmimeve të serverit (R2)

function newServer(over: Partial<ConstructorParameters<typeof ServerApplyPg>[1]> = {}) {
  return new ServerApplyPg(getPool(), { serverPrices: SERVER_PRICES, ...over });
}
const noDelay = () => Promise.resolve();

async function serverEventCount(deviceId: string): Promise<number> {
  const r = await getPool().query<{ n: string }>(
    "SELECT count(*)::text AS n FROM poc3_sync_event WHERE business_id = $1 AND device_id = $2",
    [POC3_IDS.biz, deviceId],
  );
  return Number(r.rows[0]!.n);
}

describe("PoC-3 offline sync — outbox → sync pa humbje, pa dublim (kërkon Postgres)", () => {
  beforeAll(async () => {
    await runMigrations(); // 0001..0004
    await seedPoc3(getPool());
    await ensurePartner(getPool(), PRT("x"), "1000000.00"); // partneri gjenerik cash i testeve
    storeDir = await mkdtemp(join(tmpdir(), "poc3-store-")); // VETËM temp dir — kurrë në repo
    store = new FileLocalStore(storeDir);
  });
  afterAll(async () => {
    await closePool();
  });

  it("#1 internet down: 5 fatura KREJT offline → outbox i plotë, zero kontakt me server", async () => {
    await ensureArticle(getPool(), ART("a"), "100.00");
    SERVER_PRICES.set(ART("a"), "5.00");
    const dev = DeviceSimulator.create(store, {
      deviceId: DEV("offline"),
      frozenPrices: { [ART("a")]: "5.00" },
    });
    for (let i = 0; i < 5; i++) {
      dev.createInvoice({ articleCode: ART("a"), qty: "1.00", partnerCode: PRT("x"), payment: "cash" });
    }
    expect(dev.outbox.length).toBe(5);
    expect(new Set(dev.outbox.map((e) => e.eventUuid)).size).toBe(5); // UUID unike
    expect(dev.outbox.map((e) => e.deviceSeq)).toEqual([1, 2, 3, 4, 5]); // seq monotonic
    expect(await serverEventCount(DEV("offline"))).toBe(0); // asnjë kontakt me server
  });

  it("#4 device crash: rikrijim NGA FILE → outbox + device_seq të paprekur → sync i plotë", async () => {
    await ensureArticle(getPool(), ART("b"), "100.00");
    await ensurePartner(getPool(), PRT("b"), "10000.00");
    SERVER_PRICES.set(ART("b"), "4.00");
    {
      const dev = DeviceSimulator.create(store, {
        deviceId: DEV("crash"),
        frozenPrices: { [ART("b")]: "4.00" },
      });
      for (let i = 0; i < 5; i++) {
        dev.createInvoice({ articleCode: ART("b"), qty: "2.00", partnerCode: PRT("b"), payment: "cash" });
      }
    } // "crash": referenca humbet — asgjë s'mbetet në memorie

    const revived = DeviceSimulator.loadFrom(store, DEV("crash"));
    expect(revived.outbox.length).toBe(5);
    expect(revived.nextSeq).toBe(6);

    const res = await syncDevice(revived, new InProcessTransport(newServer()), { delayFn: noDelay });
    expect(res).toEqual({ applied: 5, replayed: 0 });
    expect(revived.outbox.length).toBe(0);
    expect(await serverEventCount(DEV("crash"))).toBe(5);
  });

  it("rendi: occurred_at qëllimisht i çrregullt + outbox i përzier → aplikohet sipas DEVICE_SEQ", async () => {
    await ensureArticle(getPool(), ART("ord"), "100.00");
    SERVER_PRICES.set(ART("ord"), "3.00");
    const dev = DeviceSimulator.create(store, {
      deviceId: DEV("order"),
      frozenPrices: { [ART("ord")]: "3.00" },
    });
    // Ora e pajisjes GABIM/çrregullt me qëllim: seq1 ka orën më të re, seq3 më të vjetrën.
    dev.createInvoice({ articleCode: ART("ord"), qty: "1.00", partnerCode: PRT("x"), payment: "cash", occurredAt: "2027-01-20T10:00:00Z" });
    dev.createInvoice({ articleCode: ART("ord"), qty: "1.00", partnerCode: PRT("x"), payment: "cash", occurredAt: "2027-01-10T10:00:00Z" });
    dev.createInvoice({ articleCode: ART("ord"), qty: "1.00", partnerCode: PRT("x"), payment: "cash", occurredAt: "2027-01-01T10:00:00Z" });
    dev.shuffleOutboxForTest(); // rendi i mbërritjes ≠ seq

    await syncDevice(dev, new InProcessTransport(newServer()), { delayFn: noDelay });

    // Rendi real i aplikimit (id bigserial) duhet të ndjekë device_seq — jo orën.
    const r = await getPool().query<{ device_seq: string }>(
      "SELECT device_seq::text FROM poc3_sync_event WHERE business_id = $1 AND device_id = $2 ORDER BY id",
      [POC3_IDS.biz, DEV("order")],
    );
    expect(r.rows.map((x) => x.device_seq)).toEqual(["1", "2", "3"]);
  });

  it("#6 replay i plotë i dyfishtë: i njëjti batch dy herë → 0 dublime, replay të shënuara", async () => {
    await ensureArticle(getPool(), ART("rp"), "100.00");
    SERVER_PRICES.set(ART("rp"), "2.00");
    const dev = DeviceSimulator.create(store, {
      deviceId: DEV("replay"),
      frozenPrices: { [ART("rp")]: "2.00" },
    });
    for (let i = 0; i < 3; i++) {
      dev.createInvoice({ articleCode: ART("rp"), qty: "1.00", partnerCode: PRT("x"), payment: "cash" });
    }
    const recorded = [...dev.outbox]; // kopja e batch-it PARA sync-ut

    await syncDevice(dev, new InProcessTransport(newServer()), { delayFn: noDelay });
    const stockAfterFirst = await getPool().query<{ qty: string }>(
      "SELECT qty::text FROM poc3_stock WHERE business_id = $1 AND article_code = $2",
      [POC3_IDS.biz, ART("rp")],
    );

    // Replay i PLOTË i batch-it (p.sh. server humbi ACK-t; pajisja ri-dërgon gjithçka).
    const server = newServer();
    for (const ev of recorded) {
      expect(await server.apply(ev)).toBe("replay");
    }
    expect(await serverEventCount(DEV("replay"))).toBe(3); // 0 dublime
    const inv = await getPool().query<{ n: string }>(
      "SELECT count(*)::text AS n FROM poc3_invoice WHERE business_id = $1 AND device_id = $2",
      [POC3_IDS.biz, DEV("replay")],
    );
    expect(inv.rows[0]!.n).toBe("3"); // asnjë faturë e dyfishuar
    const stockAfterReplay = await getPool().query<{ qty: string }>(
      "SELECT qty::text FROM poc3_stock WHERE business_id = $1 AND article_code = $2",
      [POC3_IDS.biz, ART("rp")],
    );
    expect(stockAfterReplay.rows[0]!.qty).toBe(stockAfterFirst.rows[0]!.qty); // stoku i pandryshuar
    const flags = await getPool().query<{ n: string }>(
      "SELECT count(*)::text AS n FROM poc3_sync_event WHERE business_id = $1 AND device_id = $2 AND replay = true",
      [POC3_IDS.biz, DEV("replay")],
    );
    expect(flags.rows[0]!.n).toBe("3"); // replay i shënuar
  });

  it("#5 ndërprerje PARA dërgimit (pas k=2 nga n=5) → resume → secili SAKTËSISHT një herë", async () => {
    await ensureArticle(getPool(), ART("int"), "100.00");
    SERVER_PRICES.set(ART("int"), "1.00");
    const dev = DeviceSimulator.create(store, {
      deviceId: DEV("interrupt"),
      frozenPrices: { [ART("int")]: "1.00" },
    });
    for (let i = 0; i < 5; i++) {
      dev.createInvoice({ articleCode: ART("int"), qty: "1.00", partnerCode: PRT("x"), payment: "cash" });
    }

    await expect(
      syncDevice(dev, new InProcessTransport(newServer(), { failAfterCount: 2 }), {
        maxRetries: 0,
        delayFn: noDelay,
      }),
    ).rejects.toThrow(/lidhja u pre PARA/);
    expect(await serverEventCount(DEV("interrupt"))).toBe(2);
    expect(dev.outbox.length).toBe(3); // 2 të ack-uara, 3 mbeten

    const res = await syncDevice(dev, new InProcessTransport(newServer()), { delayFn: noDelay });
    expect(res).toEqual({ applied: 3, replayed: 0 });
    expect(await serverEventCount(DEV("interrupt"))).toBe(5);
    const dup = await getPool().query(
      `SELECT event_uuid FROM poc3_sync_event WHERE business_id = $1 AND device_id = $2
       GROUP BY event_uuid HAVING count(*) > 1`,
      [POC3_IDS.biz, DEV("interrupt")],
    );
    expect(dup.rowCount).toBe(0); // saktësisht një herë secili
  });

  it("#5b ndërprerje PAS aplikimit pa ACK → resume → replay skip, 0 dublime", async () => {
    await ensureArticle(getPool(), ART("ack"), "100.00");
    SERVER_PRICES.set(ART("ack"), "1.00");
    const dev = DeviceSimulator.create(store, {
      deviceId: DEV("noack"),
      frozenPrices: { [ART("ack")]: "1.00" },
    });
    for (let i = 0; i < 3; i++) {
      dev.createInvoice({ articleCode: ART("ack"), qty: "1.00", partnerCode: PRT("x"), payment: "cash" });
    }

    // Eventi seq=2 APLIKOHET në server por lidhja bie PARA ack → mbetet në outbox.
    await expect(
      syncDevice(dev, new InProcessTransport(newServer(), { applyThenFailOnSeq: 2 }), {
        maxRetries: 0,
        delayFn: noDelay,
      }),
    ).rejects.toThrow(/PAS aplikimit/);
    expect(await serverEventCount(DEV("noack"))).toBe(2); // seq1 + seq2 të aplikuara
    expect(dev.outbox.length).toBe(2); // seq2 (pa ack) + seq3

    const res = await syncDevice(dev, new InProcessTransport(newServer()), { delayFn: noDelay });
    expect(res).toEqual({ applied: 1, replayed: 1 }); // seq2 -> replay skip; seq3 -> applied
    expect(await serverEventCount(DEV("noack"))).toBe(3);
    const inv = await getPool().query<{ n: string }>(
      "SELECT count(*)::text AS n FROM poc3_invoice WHERE business_id = $1 AND device_id = $2",
      [POC3_IDS.biz, DEV("noack")],
    );
    expect(inv.rows[0]!.n).toBe("3"); // 0 dublime
  });

  it("tx-boundary (#5 guardrail): dështim BRENDA aplikimit → rollback i PLOTË i atij eventi, batch resumon", async () => {
    await ensureArticle(getPool(), ART("tx"), "100.00");
    SERVER_PRICES.set(ART("tx"), "1.00");
    const dev = DeviceSimulator.create(store, {
      deviceId: DEV("txb"),
      frozenPrices: { [ART("tx")]: "1.00" },
    });
    const e1 = dev.createInvoice({ articleCode: ART("tx"), qty: "1.00", partnerCode: PRT("x"), payment: "cash" });
    const e2 = dev.createInvoice({ articleCode: ART("tx"), qty: "1.00", partnerCode: PRT("x"), payment: "cash" });
    void e1;

    await expect(
      syncDevice(dev, new InProcessTransport(newServer({ failpointEventUuid: e2.eventUuid })), {
        maxRetries: 0,
        delayFn: noDelay,
      }),
    ).rejects.toThrow(/tx-boundary/);

    // Eventi 2: ZERO gjurmë (sync_event + invoice u rrëzuan bashkë; stoku vetëm -1 nga e1).
    const ev2 = await getPool().query(
      "SELECT 1 FROM poc3_sync_event WHERE business_id = $1 AND event_uuid = $2",
      [POC3_IDS.biz, e2.eventUuid],
    );
    expect(ev2.rowCount).toBe(0);
    const inv2 = await getPool().query(
      "SELECT 1 FROM poc3_invoice WHERE id = $1",
      [e2.payload.invoiceId],
    );
    expect(inv2.rowCount).toBe(0);
    const stock = await getPool().query<{ qty: string }>(
      "SELECT qty::text FROM poc3_stock WHERE business_id = $1 AND article_code = $2",
      [POC3_IDS.biz, ART("tx")],
    );
    expect(stock.rows[0]!.qty).toBe("99.00"); // vetëm e1 e aplikuar

    // Pa failpoint → resumon saktë.
    const res = await syncDevice(dev, new InProcessTransport(newServer()), { delayFn: noDelay });
    expect(res).toEqual({ applied: 1, replayed: 0 });
    expect(stockText(await getPool().query("SELECT qty::text AS q FROM poc3_stock WHERE business_id = $1 AND article_code = $2", [POC3_IDS.biz, ART("tx")]))).toBe("98.00");
  });

  it("R1 + CONCURRENT: dy pajisje shesin të njëjtin stok PARALELISHT → 0 dublime, stok negativ deterministik, variance stoku", async () => {
    await ensureArticle(getPool(), ART("c"), "10.00");
    await ensurePartner(getPool(), PRT("ca"), "100000.00");
    await ensurePartner(getPool(), PRT("cb"), "100000.00");
    SERVER_PRICES.set(ART("c"), "5.00");
    const devA = DeviceSimulator.create(store, { deviceId: DEV("conc-a"), frozenPrices: { [ART("c")]: "5.00" } });
    const devB = DeviceSimulator.create(store, { deviceId: DEV("conc-b"), frozenPrices: { [ART("c")]: "5.00" } });
    devA.createInvoice({ articleCode: ART("c"), qty: "7.00", partnerCode: PRT("ca"), payment: "cash" });
    devB.createInvoice({ articleCode: ART("c"), qty: "8.00", partnerCode: PRT("cb"), payment: "cash" });

    // Sync PARALEL mbi të njëjtin stok (guardrail i detyrueshëm).
    await Promise.all([
      syncDevice(devA, new InProcessTransport(newServer()), { delayFn: noDelay }),
      syncDevice(devB, new InProcessTransport(newServer()), { delayFn: noDelay }),
    ]);

    const stock = await getPool().query<{ qty: string }>(
      "SELECT qty::text FROM poc3_stock WHERE business_id = $1 AND article_code = $2",
      [POC3_IDS.biz, ART("c")],
    );
    expect(stock.rows[0]!.qty).toBe("-5.00"); // 10 - 7 - 8 — DETERMINISTIK, të dyja të pranuara
    const inv = await getPool().query<{ n: string }>(
      "SELECT count(*)::text AS n FROM poc3_invoice WHERE business_id = $1 AND article_code = $2 AND status = 'accepted'",
      [POC3_IDS.biz, ART("c")],
    );
    expect(inv.rows[0]!.n).toBe("2");
    // Raporti i variance-s së stokut = stoku negativ (asnjë konflikt i pazgjidhur).
    const variance = await getPool().query(
      "SELECT article_code, qty::text FROM poc3_stock WHERE business_id = $1 AND qty < 0 AND article_code = $2",
      [POC3_IDS.biz, ART("c")],
    );
    expect(variance.rowCount).toBe(1);
  });

  it("R2 çmim i ngrirë: serveri ndryshon çmimin gjatë offline → fatura me çmimin e ngrirë + poc3_price_variance", async () => {
    await ensureArticle(getPool(), ART("p"), "100.00");
    SERVER_PRICES.set(ART("p"), "5.00");
    const dev = DeviceSimulator.create(store, {
      deviceId: DEV("price"),
      frozenPrices: { [ART("p")]: "5.00" }, // i ngrirë në pajisje
    });
    const ev = dev.createInvoice({ articleCode: ART("p"), qty: "2.00", partnerCode: PRT("x"), payment: "cash" });

    SERVER_PRICES.set(ART("p"), "6.00"); // çmimi ndryshon NË SERVER ndërsa pajisja është offline

    await syncDevice(dev, new InProcessTransport(newServer()), { delayFn: noDelay });

    const inv = await getPool().query<{ price: string; status: string }>(
      "SELECT price::text, status FROM poc3_invoice WHERE id = $1",
      [ev.payload.invoiceId],
    );
    expect(inv.rows[0]).toEqual({ price: "5.00", status: "accepted" }); // pranohet siç u lëshua
    const pv = await getPool().query<{ device_price: string; server_price: string; diff: string }>(
      "SELECT device_price::text, server_price::text, diff::text FROM poc3_price_variance WHERE business_id = $1 AND invoice_id = $2",
      [POC3_IDS.biz, ev.payload.invoiceId],
    );
    expect(pv.rows).toEqual([{ device_price: "5.00", server_price: "6.00", diff: "-1.00" }]);
  });

  it("R3 kredi (transaksionale, pa timer): tejkaluesi pranohet + blocked NË TË NJËJTIN tx; pasuesi kreditor refuzohet; cash lejohet", async () => {
    await ensureArticle(getPool(), ART("k"), "100.00");
    await ensurePartner(getPool(), PRT("k"), "100.00"); // limit 100
    SERVER_PRICES.set(ART("k"), "12.00");
    const dev = DeviceSimulator.create(store, {
      deviceId: DEV("credit"),
      frozenPrices: { [ART("k")]: "12.00" },
    });
    // seq1: 10 × 12.00 = 120.00 > limit 100 → PRANOHET + bllokon në të njëjtin tx.
    const e1 = dev.createInvoice({ articleCode: ART("k"), qty: "10.00", partnerCode: PRT("k"), payment: "credit" });
    // seq2 (device_seq më i lartë): kreditor → REFUZOHET me status të qartë.
    const e2 = dev.createInvoice({ articleCode: ART("k"), qty: "1.00", partnerCode: PRT("k"), payment: "credit" });
    // seq3: cash → lejohet edhe i bllokuar (SSOT §2: "vetëm cash").
    const e3 = dev.createInvoice({ articleCode: ART("k"), qty: "1.00", partnerCode: PRT("k"), payment: "cash" });

    await syncDevice(dev, new InProcessTransport(newServer()), { delayFn: noDelay });

    const p = await getPool().query<{ blocked: boolean; balance: string }>(
      "SELECT blocked, balance::text FROM poc3_partner WHERE business_id = $1 AND code = $2",
      [POC3_IDS.biz, PRT("k")],
    );
    expect(p.rows[0]).toEqual({ blocked: true, balance: "120.00" }); // vetëm e1 (kreditore e pranuar)
    const statuses = await getPool().query<{ id: string; status: string }>(
      "SELECT id, status FROM poc3_invoice WHERE id = ANY($1::uuid[]) ORDER BY created_at",
      [[e1.payload.invoiceId, e2.payload.invoiceId, e3.payload.invoiceId]],
    );
    const byId = Object.fromEntries(statuses.rows.map((r) => [r.id, r.status]));
    expect(byId[e1.payload.invoiceId]).toBe("accepted");
    expect(byId[e2.payload.invoiceId]).toBe("rejected_blocked");
    expect(byId[e3.payload.invoiceId]).toBe("accepted");
    // Stoku u zbrit vetëm nga e1 (10) + e3 (1) — jo nga e2 e refuzuar.
    const stock = await getPool().query<{ qty: string }>(
      "SELECT qty::text FROM poc3_stock WHERE business_id = $1 AND article_code = $2",
      [POC3_IDS.biz, ART("k")],
    );
    expect(stock.rows[0]!.qty).toBe("89.00");
  });

  it("retry me backoff: 2 dështime kalimtare → sukses në tentativën e tretë, vonesa eksponenciale", async () => {
    await ensureArticle(getPool(), ART("rt"), "100.00");
    SERVER_PRICES.set(ART("rt"), "1.00");
    const dev = DeviceSimulator.create(store, {
      deviceId: DEV("retry"),
      frozenPrices: { [ART("rt")]: "1.00" },
    });
    dev.createInvoice({ articleCode: ART("rt"), qty: "1.00", partnerCode: PRT("x"), payment: "cash" });

    const delays: number[] = [];
    const flaky = new FlakyTransport(newServer(), 2);
    const res = await syncDevice(dev, flaky, {
      maxRetries: 3,
      backoffMs: 10,
      delayFn: (ms) => {
        delays.push(ms);
        return Promise.resolve();
      },
    });
    expect(res).toEqual({ applied: 1, replayed: 0 });
    expect(flaky.attempts).toBe(3);
    expect(delays).toEqual([10, 20]); // backoff eksponencial
  });

  it("#7 server DB unavailable: matcher specifik lidhjeje, outbox i paprekur, retry me DB të kthyer ia del", async () => {
    await ensureArticle(getPool(), ART("d"), "100.00");
    SERVER_PRICES.set(ART("d"), "1.00");
    const dev = DeviceSimulator.create(store, {
      deviceId: DEV("dbdown"),
      frozenPrices: { [ART("d")]: "1.00" },
    });
    dev.createInvoice({ articleCode: ART("d"), qty: "1.00", partnerCode: PRT("x"), payment: "cash" });

    const badPool = new PgPool({
      host: "localhost",
      port: 59987, // port pa Postgres — simulim BRENDA test scope (container-i s'preket)
      user: "idata",
      database: "idata",
      connectionTimeoutMillis: 1200,
    });
    let caught: unknown;
    try {
      await syncDevice(
        dev,
        new InProcessTransport(new ServerApplyPg(badPool, { serverPrices: SERVER_PRICES })),
        { maxRetries: 1, delayFn: noDelay },
      );
    } catch (e) {
      caught = e;
    } finally {
      await badPool.end();
    }
    expect(caught).toBeDefined();
    const err = caught as NodeJS.ErrnoException & { errors?: NodeJS.ErrnoException[] };
    const codes = [err.code, ...(err.errors?.map((x) => x.code) ?? [])].filter(Boolean) as string[];
    const connCodes = ["ECONNREFUSED", "ECONNRESET", "EHOSTUNREACH", "ENETUNREACH", "ETIMEDOUT"];
    const isConnectionError =
      codes.some((c) => connCodes.includes(c)) ||
      /ECONNREFUSED|timeout|unreachable|connect/i.test(String(err.message ?? ""));
    expect(isConnectionError).toBe(true);

    expect(dev.outbox.length).toBe(1); // outbox i PAPREKUR
    const res = await syncDevice(dev, new InProcessTransport(newServer()), { delayFn: noDelay });
    expect(res).toEqual({ applied: 1, replayed: 0 }); // retry me DB të rikthyer ia del
  });

  it("#8 outbox full (cap N): krijimi i ri refuzohet me gabim të qartë; asnjë fshirje e heshtur", async () => {
    const dev = DeviceSimulator.create(store, {
      deviceId: DEV("cap"),
      frozenPrices: { [ART("a")]: "5.00" },
      capacity: 3,
    });
    for (let i = 0; i < 3; i++) {
      dev.createInvoice({ articleCode: ART("a"), qty: "1.00", partnerCode: PRT("x"), payment: "cash" });
    }
    const firstUuid = dev.outbox[0]!.eventUuid;
    expect(() =>
      dev.createInvoice({ articleCode: ART("a"), qty: "1.00", partnerCode: PRT("x"), payment: "cash" }),
    ).toThrow(/outbox plot \(cap 3\)/);
    expect(dev.outbox.length).toBe(3);
    expect(dev.outbox[0]!.eventUuid).toBe(firstUuid); // i pari ende aty — zero fshirje të heshtura
  });
});

function stockText(r: { rows: { q?: string }[] }): string {
  return r.rows[0]?.q ?? "";
}
