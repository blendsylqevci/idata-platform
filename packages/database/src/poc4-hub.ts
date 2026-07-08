import { execSync } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import pg from "pg";
import type { Pool } from "pg";
import { POC4_IDS, POC4_ROUTING, POC4_DEFAULT_STATION } from "./poc4-fixtures.js";

const { Pool: PgPool } = pg;

const PKG_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const COMPOSE_FILE = path.join(PKG_DIR, "docker-compose.poc4.yml");
const HUB_SCHEMA_FILE = path.join(PKG_DIR, "poc4-hub-schema.sql");
const HUB_PORT = Number(process.env.POC4_HUB_PORT ?? "5434");

/* ============================== HubStore (ADR-013) ==============================
 * GJITHË qasja e hub-it kalon përmes këtij interface (plan-B i ADR-013):
 * implementimi PoC = Postgres në container të veçantë; një implementim SQLite
 * do të zëvendësonte VETËM këtë klasë — routing/fallback/sync s'ndryshojnë.
 * =============================================================================== */

export interface OrderItemInput {
  itemId: string;
  articleCode: string;
  qty: string;
}
export interface OrderInput {
  orderId: string;
  deviceId: string;
  items: OrderItemInput[];
}
export interface TicketRow {
  id: string;
  order_item_id: string;
  station_code: string;
  status: string;
  flagged: boolean;
}
export interface OutboxRow {
  hub_seq: string;
  event_uuid: string;
  payload: {
    orderId: string;
    deviceId: string;
    items: { itemId: string; articleCode: string; qty: string; stationCode: string }[];
  };
}

export interface HubStore {
  /** Porosi → routing artikull→stacion → tickets + outbox event, NË NJË transaksion. */
  createOrder(order: OrderInput): Promise<TicketRow[]>;
  confirmTicket(ticketId: string): Promise<void>;
  /** Ticket të pa-konfirmuar më të vjetër se timeoutMs → fallback queue + flamur. */
  sweepToFallback(timeoutMs: number): Promise<number>;
  pendingOutbox(): Promise<OutboxRow[]>;
  markSynced(eventUuid: string): Promise<void>;
}

export interface PgHubStoreOpts {
  /** Cap i radhës së outbox-it (pending); kur mbushet → refuzim i qartë i porosisë së re. */
  outboxCap?: number;
}

export class PgHubStore implements HubStore {
  constructor(
    private readonly pool: Pool,
    private readonly opts: PgHubStoreOpts = {},
  ) {}

  async createOrder(order: OrderInput): Promise<TicketRow[]> {
    const biz = POC4_IDS.biz;
    const cx = await this.pool.connect();
    try {
      await cx.query("BEGIN");

      if (this.opts.outboxCap !== undefined) {
        // #8: politikë e definuar — refuzim i qartë; KURRË fshirje e heshtur e radhës.
        const pending = await cx.query<{ n: string }>(
          "SELECT count(*)::text AS n FROM poc4_outbox_cloud WHERE business_id = $1 AND synced = false",
          [biz],
        );
        if (Number(pending.rows[0]!.n) >= this.opts.outboxCap) {
          throw new Error(
            `radha e hub-it plot (cap ${this.opts.outboxCap}): porosia e re refuzohet — pritet sync me cloud`,
          );
        }
      }

      await cx.query(
        "INSERT INTO poc4_order (id, business_id, device_id) VALUES ($1, $2, $3)",
        [order.orderId, biz, order.deviceId],
      );
      const tickets: TicketRow[] = [];
      const payloadItems: OutboxRow["payload"]["items"] = [];
      for (const item of order.items) {
        await cx.query(
          "INSERT INTO poc4_order_item (id, business_id, order_id, article_code, qty) VALUES ($1, $2, $3, $4, $5::numeric)",
          [item.itemId, biz, order.orderId, item.articleCode, item.qty],
        );
        const mapped = POC4_ROUTING[item.articleCode];
        const stationCode = mapped ?? POC4_DEFAULT_STATION;
        const flagged = mapped === undefined; // i pa-mapuar → default + flamur (jo humbje, jo crash)
        const t = await cx.query<TicketRow>(
          `INSERT INTO poc4_kds_ticket (business_id, order_item_id, station_code, flagged)
           VALUES ($1, $2, $3, $4)
           RETURNING id, order_item_id, station_code, status, flagged`,
          [biz, item.itemId, stationCode, flagged],
        );
        tickets.push(t.rows[0]!);
        payloadItems.push({ itemId: item.itemId, articleCode: item.articleCode, qty: item.qty, stationCode });
      }
      // Outbox event NË TË NJËJTIN transaksion me porosinë (zero humbje).
      await cx.query(
        "INSERT INTO poc4_outbox_cloud (business_id, event_uuid, payload) VALUES ($1, gen_random_uuid(), $2::jsonb)",
        [biz, JSON.stringify({ orderId: order.orderId, deviceId: order.deviceId, items: payloadItems })],
      );
      await cx.query("COMMIT");
      return tickets;
    } catch (err) {
      await cx.query("ROLLBACK");
      throw err;
    } finally {
      cx.release();
    }
  }

  async confirmTicket(ticketId: string): Promise<void> {
    await this.pool.query(
      "UPDATE poc4_kds_ticket SET status = 'konfirmuar', confirmed_at = now() WHERE id = $1",
      [ticketId],
    );
  }

  async sweepToFallback(timeoutMs: number): Promise<number> {
    // SSOT §6 (~30s) → config i testueshëm. Ticket pa konfirmim brenda afatit →
    // fallback queue ("printeri" i simuluar) + flamurohet.
    const r = await this.pool.query(
      `WITH stale AS (
         SELECT id FROM poc4_kds_ticket
          WHERE business_id = $1 AND status <> 'konfirmuar'
            AND created_at < now() - ($2::text || ' milliseconds')::interval
       ), ins AS (
         INSERT INTO poc4_fallback_queue (business_id, ticket_id, reason)
         SELECT $1, id, 'stacioni s''konfirmoi brenda afatit' FROM stale
         ON CONFLICT (ticket_id) DO NOTHING
         RETURNING ticket_id
       )
       UPDATE poc4_kds_ticket SET flagged = true
        WHERE id IN (SELECT ticket_id FROM ins)
       RETURNING id`,
      [POC4_IDS.biz, String(timeoutMs)],
    );
    return r.rowCount ?? 0;
  }

  async pendingOutbox(): Promise<OutboxRow[]> {
    const r = await this.pool.query<OutboxRow>(
      `SELECT hub_seq::text, event_uuid, payload
         FROM poc4_outbox_cloud
        WHERE business_id = $1 AND synced = false
        ORDER BY hub_seq`,
      [POC4_IDS.biz],
    );
    return r.rows;
  }

  async markSynced(eventUuid: string): Promise<void> {
    await this.pool.query(
      "UPDATE poc4_outbox_cloud SET synced = true, synced_at = now() WHERE business_id = $1 AND event_uuid = $2",
      [POC4_IDS.biz, eventUuid],
    );
  }
}

/* ========================= Harness i containerit poc4-hub =========================
 * Testet menaxhojnë lifecycle-in VETËM të containerit "poc4-hub" përmes compose-it
 * poc4 (përjashtim i kontrolluar) — KURRË containerin kryesor 5433.
 * ================================================================================= */

function composeCmd(args: string): void {
  execSync(`docker compose -f "${COMPOSE_FILE}" ${args}`, { stdio: "pipe" });
}

export function hubPool(): Pool {
  return new PgPool({
    host: "localhost",
    port: HUB_PORT,
    user: "idata",
    database: "idata",
    max: 5,
  });
}

/** Ngre hub-in (nëse s'është) dhe pret readiness me retry-connect. */
export async function ensureHubUp(timeoutMs = 60_000): Promise<void> {
  composeCmd("up -d");
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const p = hubPool();
    try {
      await p.query("SELECT 1");
      await p.end();
      return;
    } catch {
      await p.end().catch(() => {});
      if (Date.now() > deadline) throw new Error("poc4-hub s'u bë gati brenda afatit");
      await new Promise((r) => setTimeout(r, 1000));
    }
  }
}

/** Apliko skemën e hub-it (idempotente; jashtë zinxhirit të migrimeve). */
export async function applyHubSchema(pool: Pool): Promise<void> {
  await pool.query(readFileSync(HUB_SCHEMA_FILE, "utf8"));
}

/** kill -9 i containerit poc4-hub (skenari #6 — crash procesi; rryma reale = 4b). */
export function killHubMinus9(): void {
  composeCmd("kill -s SIGKILL hub");
}

export async function restartHub(): Promise<void> {
  await ensureHubUp();
}
