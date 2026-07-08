import type { Pool } from "pg";
import { POC4_IDS, POC4_HUB_ID } from "./poc4-fixtures.js";
import type { HubStore, OutboxRow } from "./poc4-hub.js";

export type CloudOutcome = "applied" | "replay";

/** Adapter i cloud apply (si ServerApply i PoC-3). */
export interface CloudApply {
  apply(event: OutboxRow): Promise<CloudOutcome>;
}

/** Aplikon eventin hub→cloud në NJË transaksion; idempotent me event_uuid. */
export class CloudApplyPg implements CloudApply {
  constructor(private readonly pool: Pool) {}

  async apply(e: OutboxRow): Promise<CloudOutcome> {
    const biz = POC4_IDS.biz;
    const cx = await this.pool.connect();
    try {
      await cx.query("BEGIN");
      const ins = await cx.query(
        `INSERT INTO poc4_cloud_event (business_id, hub_id, hub_seq, event_uuid, payload)
         VALUES ($1, $2, $3, $4, $5::jsonb)
         ON CONFLICT (business_id, event_uuid) DO NOTHING
         RETURNING id`,
        [biz, POC4_HUB_ID, e.hub_seq, e.event_uuid, JSON.stringify(e.payload)],
      );
      if (ins.rowCount === 0) {
        await cx.query(
          "UPDATE poc4_cloud_event SET replay = true WHERE business_id = $1 AND event_uuid = $2",
          [biz, e.event_uuid],
        );
        await cx.query("COMMIT");
        return "replay";
      }
      for (const item of e.payload.items) {
        await cx.query(
          `INSERT INTO poc4_cloud_order (id, business_id, order_id, device_id, article_code, qty, station_code)
           VALUES ($1, $2, $3, $4, $5, $6::numeric, $7)`,
          [item.itemId, biz, e.payload.orderId, e.payload.deviceId, item.articleCode, item.qty, item.stationCode],
        );
      }
      await cx.query("COMMIT");
      return "applied";
    } catch (err) {
      await cx.query("ROLLBACK");
      throw err;
    } finally {
      cx.release();
    }
  }
}

/**
 * Transporti hub→cloud me çelës ON/OFF ("interneti") + failpoint ndërprerjeje —
 * modeli i transporteve të PoC-3.
 */
export class ToggleCloudTransport {
  online = true;
  private sent = 0;
  constructor(
    private readonly cloud: CloudApply,
    private readonly opts: { failAfterCount?: number } = {},
  ) {}

  async send(event: OutboxRow): Promise<CloudOutcome> {
    if (!this.online) {
      const err = new Error("interneti OFF: cloud i paarritshëm") as NodeJS.ErrnoException;
      err.code = "ECONNREFUSED";
      throw err;
    }
    if (this.opts.failAfterCount !== undefined && this.sent >= this.opts.failAfterCount) {
      throw new Error("lidhja u pre në mes të sync-ut hub→cloud (failpoint)");
    }
    const outcome = await this.cloud.apply(event);
    this.sent += 1;
    return outcome;
  }
}

export interface HubSyncResult {
  applied: number;
  replayed: number;
}

/** Sync hub→cloud: pending outbox sipas hub_seq; ack (synced=true) pas konfirmimit. */
export async function syncHubToCloud(
  hub: HubStore,
  transport: ToggleCloudTransport,
): Promise<HubSyncResult> {
  const pending = await hub.pendingOutbox();
  let applied = 0;
  let replayed = 0;
  for (const event of pending) {
    const outcome = await transport.send(event); // gabimi ndal sync-un; pending mbetet (resume)
    await hub.markSynced(event.event_uuid);
    if (outcome === "replay") replayed += 1;
    else applied += 1;
  }
  return { applied, replayed };
}
