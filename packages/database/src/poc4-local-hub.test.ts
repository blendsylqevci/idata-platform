import { describe, it, expect, beforeAll, afterAll } from "vitest";
import { randomUUID } from "node:crypto";
import { performance } from "node:perf_hooks";
import pg from "pg";
import type { Pool } from "pg";
import { getPool, closePool } from "./pool.js";
import { runMigrations } from "./migrate.js";
import { seedPoc4Cloud, POC4_IDS } from "./poc4-fixtures.js";
import {
  PgHubStore,
  hubPool,
  ensureHubUp,
  applyHubSchema,
  killHubMinus9,
  restartHub,
} from "./poc4-hub.js";
import { CloudApplyPg, ToggleCloudTransport, syncHubToCloud } from "./poc4-sync.js";

const { Pool: PgPool } = pg;

// Pajisje/porosi UNIKE per ekzekutim (hub-volume lokal persiston mes runs;
// CI = gjithmonë i pastër). Kodet e artikujve janë fikse (routing config).
const RUN = randomUUID().slice(0, 8);
const DEVP = `dev-demo-${RUN}`;
const DEV = (n: string) => `${DEVP}-${n}`;

let hp: Pool; // pool-i aktual i hub-it (ri-krijohet pas kill/restart në #6)
const store = () => new PgHubStore(hp);

function order(deviceId: string, articleCode: string, qty = "1.00") {
  return {
    orderId: randomUUID(),
    deviceId,
    items: [{ itemId: randomUUID(), articleCode, qty }],
  };
}

async function hubCounts(): Promise<{ orders: number; tickets: number; pending: number }> {
  const o = await hp.query<{ n: string }>(
    "SELECT count(*)::text AS n FROM poc4_order WHERE device_id LIKE $1",
    [`${DEVP}%`],
  );
  const t = await hp.query<{ n: string }>(
    `SELECT count(*)::text AS n FROM poc4_kds_ticket k
      JOIN poc4_order_item i ON i.id = k.order_item_id
      JOIN poc4_order po ON po.id = i.order_id
     WHERE po.device_id LIKE $1`,
    [`${DEVP}%`],
  );
  const p = await hp.query<{ n: string }>(
    "SELECT count(*)::text AS n FROM poc4_outbox_cloud WHERE synced = false AND payload->>'deviceId' LIKE $1",
    [`${DEVP}%`],
  );
  return { orders: Number(o.rows[0]!.n), tickets: Number(t.rows[0]!.n), pending: Number(p.rows[0]!.n) };
}

async function cloudItemCount(): Promise<number> {
  const r = await getPool().query<{ n: string }>(
    "SELECT count(*)::text AS n FROM poc4_cloud_order WHERE business_id = $1 AND device_id LIKE $2",
    [POC4_IDS.biz, `${DEVP}%`],
  );
  return Number(r.rows[0]!.n);
}

describe("PoC-4a local hub SEMANTIKA (Docker; hardware = 4b) — kërkon Postgres + docker", () => {
  beforeAll(async () => {
    await runMigrations(); // cloud: 0001..0005
    await seedPoc4Cloud(getPool());
    await ensureHubUp(); // ngre VETËM containerin poc4-hub (5434) — 5433 s'preket
    hp = hubPool();
    await applyHubSchema(hp);
  }, 120_000);

  afterAll(async () => {
    await hp?.end();
    await closePool();
  });

  it("#1 internet down: 10 porosi nga 2 pajisje → hub → stacioni i SAKTË, me cloud OFF", async () => {
    const transport = new ToggleCloudTransport(new CloudApplyPg(getPool()));
    transport.online = false; // "interneti" OFF

    // dev-w1: 3× grill (g1) + 2× bar (b1); dev-w2: 2× grill (g2) + 3× bar (b1).
    for (let i = 0; i < 3; i++) await store().createOrder(order(DEV("w1"), "art-demo-g1"));
    for (let i = 0; i < 2; i++) await store().createOrder(order(DEV("w1"), "art-demo-b1"));
    for (let i = 0; i < 2; i++) await store().createOrder(order(DEV("w2"), "art-demo-g2"));
    for (let i = 0; i < 3; i++) await store().createOrder(order(DEV("w2"), "art-demo-b1"));

    const byStation = await hp.query<{ station_code: string; n: string }>(
      `SELECT k.station_code, count(*)::text AS n
         FROM poc4_kds_ticket k
         JOIN poc4_order_item i ON i.id = k.order_item_id
         JOIN poc4_order po ON po.id = i.order_id
        WHERE po.device_id LIKE $1
        GROUP BY k.station_code ORDER BY k.station_code`,
      [`${DEVP}%`],
    );
    expect(byStation.rows).toEqual([
      { station_code: "demo-bar", n: "5" },
      { station_code: "demo-grill", n: "5" },
    ]);
    expect(await cloudItemCount()).toBe(0); // asgjë s'ka prekur cloud-in
    expect((await hubCounts()).pending).toBe(10);

    // Tentativa e sync-ut me internet OFF → gabim i pastër; pending i paprekur.
    await expect(syncHubToCloud(store(), transport)).rejects.toThrow(/interneti OFF/);
    expect((await hubCounts()).pending).toBe(10);
  });

  it("routing: artikull i PA-mapuar → stacion default + i flamuruar (jo humbje, jo crash)", async () => {
    const tickets = await store().createOrder(order(DEV("w1"), "art-demo-unknown"));
    expect(tickets.length).toBe(1);
    expect(tickets[0]!.station_code).toBe("demo-grill"); // default
    expect(tickets[0]!.flagged).toBe(true); // i flamuruar për shqyrtim
  });

  it("#3 fallback printeri: ticket i pa-konfirmuar brenda afatit (config 2s) → fallback queue + flamur; i konfirmuari JO", async () => {
    const t1 = (await store().createOrder(order(DEV("w2"), "art-demo-g1")))[0]!;
    const t2 = (await store().createOrder(order(DEV("w2"), "art-demo-b1")))[0]!;
    await store().confirmTicket(t2.id); // stacioni e konfirmoi në kohë

    // Backdate 3s (determinizëm pa timer real; afati i testueshëm = 2s).
    await hp.query("UPDATE poc4_kds_ticket SET created_at = now() - interval '3 seconds' WHERE id = ANY($1::uuid[])", [
      [t1.id, t2.id],
    ]);
    // Filtri device-prefix = VETËM për izolim testesh të ri-ekzekutueshme mbi të
    // njëjtin volume (default = business-wide).
    const moved = await store().sweepToFallback(2000, DEVP);
    expect(moved).toBe(1); // vetëm i pa-konfirmuari

    const fb = await hp.query("SELECT 1 FROM poc4_fallback_queue WHERE ticket_id = $1", [t1.id]);
    expect(fb.rowCount).toBe(1);
    const fbConfirmed = await hp.query("SELECT 1 FROM poc4_fallback_queue WHERE ticket_id = $1", [t2.id]);
    expect(fbConfirmed.rowCount).toBe(0);
    const flag = await hp.query<{ flagged: boolean }>("SELECT flagged FROM poc4_kds_ticket WHERE id = $1", [t1.id]);
    expect(flag.rows[0]!.flagged).toBe(true);
  });

  it(
    "#6 hub restart: docker kill -9 me radhë JO-bosh (lidhjet e mbyllura para kill — crash mbi WAL me porosi të pashkruara në cloud) → zero humbje",
    async () => {
      const before = await hubCounts();
      // Guard i plotë: ka porosi + tickets + radhë JO-bosh në momentin e crash-it.
      expect(before.orders).toBeGreaterThan(0);
      expect(before.tickets).toBeGreaterThan(0);
      expect(before.pending).toBeGreaterThan(0);

      await hp.end(); // mbyll lidhjet tona para kill (pool i vjetër s'vlen më)
      killHubMinus9(); // SIGKILL containerit poc4-hub (≈ crash procesi; rryma reale = 4b)
      await restartHub();
      hp = hubPool();

      const after = await hubCounts();
      expect(after).toEqual(before); // zero humbje: orders + tickets + outbox të paprekur
    },
    120_000,
  );

  it("rikthim interneti: sync hub→cloud → çdo porosi SAKTËSISHT një herë; replay i plotë → 0 dublime", async () => {
    const pendingBefore = (await hubCounts()).pending; // 10 + unmapped(1) + fallback(2) = 13 (scoped këtij run-i)
    const transport = new ToggleCloudTransport(new CloudApplyPg(getPool()));
    const res = await syncHubToCloud(store(), transport);
    // Sync-u është business-wide: mund të drenojë edhe leftovers nga një run i
    // NDËRPRERË më parë (vetë-shërim). Ekzaktësia matet mbi counts e SCOPED:
    expect(res.applied + res.replayed).toBeGreaterThanOrEqual(pendingBefore);
    expect((await hubCounts()).pending).toBe(0); // radha e këtij run-i u zbraz
    expect(await cloudItemCount()).toBe(pendingBefore); // SAKTËSISHT një herë secili (scoped)

    // Replay i PLOTË i të gjitha eventeve (p.sh. hub-i humbi ack-t) → vetëm replay, 0 dublime.
    const all = await hp.query<{ hub_seq: string; event_uuid: string; payload: never }>(
      "SELECT hub_seq::text, event_uuid, payload FROM poc4_outbox_cloud WHERE payload->>'deviceId' LIKE $1 ORDER BY hub_seq",
      [`${DEVP}%`],
    );
    const cloud = new CloudApplyPg(getPool());
    for (const ev of all.rows) {
      expect(await cloud.apply(ev)).toBe("replay");
    }
    expect(await cloudItemCount()).toBe(pendingBefore); // i pandryshuar
    const dup = await getPool().query(
      `SELECT event_uuid FROM poc4_cloud_event WHERE business_id = $1
        GROUP BY event_uuid HAVING count(*) > 1`,
      [POC4_IDS.biz],
    );
    expect(dup.rowCount).toBe(0);
  });

  it("#5 sync i ndërprerë në mes (pas k=1 nga 3) → resume → saktësisht një herë secili", async () => {
    for (let i = 0; i < 3; i++) await store().createOrder(order(DEV("w1"), "art-demo-b1"));
    const cloudBefore = await cloudItemCount();

    const cut = new ToggleCloudTransport(new CloudApplyPg(getPool()), { failAfterCount: 1 });
    await expect(syncHubToCloud(store(), cut)).rejects.toThrow(/lidhja u pre në mes/);
    expect((await hubCounts()).pending).toBe(2); // 1 u aplikua + ack; 2 mbeten

    const res = await syncHubToCloud(store(), new ToggleCloudTransport(new CloudApplyPg(getPool())));
    expect(res).toEqual({ applied: 2, replayed: 0 });
    expect(await cloudItemCount()).toBe(cloudBefore + 3); // 3 saktësisht një herë
  });

  it("#5b cut PAS aplikimit pa ACK (modeli PoC-3) → resume → replay skip, 0 dublime", async () => {
    for (let i = 0; i < 2; i++) await store().createOrder(order(DEV("w2"), "art-demo-g1"));
    const cloudBefore = await cloudItemCount();
    const pending = await store().pendingOutbox();
    expect(pending.length).toBe(2);
    const firstSeq = Number(pending[0]!.hub_seq);

    // Eventi i parë APLIKOHET në cloud por lidhja bie PARA ack → mbetet pending.
    await expect(
      syncHubToCloud(
        store(),
        new ToggleCloudTransport(new CloudApplyPg(getPool()), { applyThenFailOnSeq: firstSeq }),
      ),
    ).rejects.toThrow(/PAS aplikimit/);
    expect((await hubCounts()).pending).toBe(2); // asnjë ack — të dyja ende pending

    const res = await syncHubToCloud(store(), new ToggleCloudTransport(new CloudApplyPg(getPool())));
    expect(res).toEqual({ applied: 1, replayed: 1 }); // i pari -> replay skip; i dyti -> applied
    expect(await cloudItemCount()).toBe(cloudBefore + 2); // 0 dublime
  });

  it("#7 hub DB i paarritshëm nga API e pajisjes: gabim SPECIFIK lidhjeje; retry me hub të kthyer OK", async () => {
    const badPool = new PgPool({
      host: "localhost",
      port: 59986, // port pa Postgres — brenda test scope
      user: "idata",
      database: "idata",
      connectionTimeoutMillis: 1200,
    });
    const badStore = new PgHubStore(badPool);
    let caught: unknown;
    try {
      await badStore.createOrder(order(DEV("w2"), "art-demo-g1"));
    } catch (e) {
      caught = e;
    } finally {
      await badPool.end();
    }
    expect(caught).toBeDefined();
    const err = caught as NodeJS.ErrnoException & { errors?: NodeJS.ErrnoException[] };
    const codes = [err.code, ...(err.errors?.map((x) => x.code) ?? [])].filter(Boolean) as string[];
    const connCodes = ["ECONNREFUSED", "ECONNRESET", "EHOSTUNREACH", "ENETUNREACH", "ETIMEDOUT"];
    expect(
      codes.some((c) => connCodes.includes(c)) ||
        /ECONNREFUSED|timeout|unreachable|connect/i.test(String(err.message ?? "")),
    ).toBe(true);

    // Retry i pajisjes me hub-in e kthyer → OK.
    const tickets = await store().createOrder(order(DEV("w2"), "art-demo-g1"));
    expect(tickets[0]!.station_code).toBe("demo-grill");
  });

  it("#8 radha e hub-it plot (cap): refuzim i qartë, zero fshirje; recovery pas sync-ut", async () => {
    // Zbraz pending-un e mbetur nga #7-retry që baseline i cap-it të jetë i qartë.
    await syncHubToCloud(store(), new ToggleCloudTransport(new CloudApplyPg(getPool())));

    const capped = new PgHubStore(hp, { outboxCap: 2 });
    await capped.createOrder(order(DEV("w1"), "art-demo-b1"));
    await capped.createOrder(order(DEV("w1"), "art-demo-b1"));
    const beforeReject = await hubCounts();
    await expect(capped.createOrder(order(DEV("w1"), "art-demo-b1"))).rejects.toThrow(
      /radha e hub-it plot \(cap 2\)/,
    );
    // Porosia e refuzuar s'la ASNJË gjurmë: as outbox, as orders, as tickets (tx rollback).
    expect(await hubCounts()).toEqual(beforeReject);
    expect(beforeReject.pending).toBe(2);

    // Recovery: sync-i zbraz radhën → krijimi rifillon.
    await syncHubToCloud(capped, new ToggleCloudTransport(new CloudApplyPg(getPool())));
    expect((await hubCounts()).pending).toBe(0);
    const t = await capped.createOrder(order(DEV("w1"), "art-demo-b1"));
    expect(t.length).toBe(1);
    await syncHubToCloud(capped, new ToggleCloudTransport(new CloudApplyPg(getPool()))); // drain final
  });

  it("latenca (proxy in-process — JO kriteri LAN <2s; ai matet në 4b)", async () => {
    const t0 = performance.now();
    const tickets = await store().createOrder(order(DEV("w2"), "art-demo-g2"));
    const visible = await hp.query("SELECT 1 FROM poc4_kds_ticket WHERE id = $1", [tickets[0]!.id]);
    const ms = performance.now() - t0;
    expect(visible.rowCount).toBe(1);
    // Raportohet si proxy; NUK pretendohet përmbushja e kriterit LAN (<2s) — ajo = 4b.
    console.log(`[poc4a] latenca proxy in-process porosi→ticket i dukshëm: ${ms.toFixed(1)} ms`);
    await syncHubToCloud(store(), new ToggleCloudTransport(new CloudApplyPg(getPool()))); // drain
  });
});
