import type { Pool } from "pg";
import { POC3_IDS } from "./poc3-fixtures.js";
import type { SyncEvent } from "./poc3-device.js";

export type ApplyOutcome = "applied" | "replay";

/** Adapter i aplikimit server-side (ADR-011 input — implementimi PoC mbi pg). */
export interface ServerApply {
  apply(event: SyncEvent): Promise<ApplyOutcome>;
}

export interface ServerApplyOpts {
  /** Katalogu i çmimeve të serverit (R2). Në PoC = config i adapterit;
   *  tabela canonical e çmimeve dizajnohet në F2/F3 — jashtë scope emrat canonical. */
  serverPrices: Map<string, string>;
  /** Failpoint VETËM për testin e tx-boundary: hidhet PAS insert-it të faturës,
   *  PARA stock/partner — rollback duhet të fshijë GJITHÇKA të eventit. */
  failpointEventUuid?: string;
}

/**
 * Zbaton një event në NJË transaksion DB të vetëm (guardrail #5):
 * sync_event + invoice + stock + partner/block + variance BASHKË —
 * dështim në mes = rollback i PLOTË i atij eventi; batch-i resumon saktë.
 * Idempotenca: UNIQUE (business_id, event_uuid) → replay = skip + shëno replay.
 */
export class ServerApplyPg implements ServerApply {
  constructor(
    private readonly pool: Pool,
    private readonly opts: ServerApplyOpts,
  ) {}

  async apply(e: SyncEvent): Promise<ApplyOutcome> {
    const biz = POC3_IDS.biz;
    const cx = await this.pool.connect();
    try {
      await cx.query("BEGIN");

      const ins = await cx.query(
        `INSERT INTO poc3_sync_event (business_id, device_id, event_uuid, device_seq, occurred_at, payload)
         VALUES ($1, $2, $3, $4, $5, $6::jsonb)
         ON CONFLICT (business_id, event_uuid) DO NOTHING
         RETURNING id`,
        [biz, e.deviceId, e.eventUuid, e.deviceSeq, e.occurredAt, JSON.stringify(e.payload)],
      );
      if (ins.rowCount === 0) {
        // Replay: eventi është aplikuar më parë → skip side-effects + shëno replay.
        await cx.query(
          "UPDATE poc3_sync_event SET replay = true WHERE business_id = $1 AND event_uuid = $2",
          [biz, e.eventUuid],
        );
        await cx.query("COMMIT");
        return "replay";
      }

      const p = e.payload;
      // R3: partner lexohet me FOR UPDATE — bllokimi/verifikimi = transaksional,
      // pa timer; edhe nën sync konkurrent rreshtat serializohen nga row-lock.
      const partner = await cx.query<{ blocked: boolean }>(
        "SELECT blocked FROM poc3_partner WHERE business_id = $1 AND code = $2 FOR UPDATE",
        [biz, p.partnerCode],
      );
      if (partner.rowCount === 0) throw new Error(`partner s'u gjet: ${p.partnerCode}`);
      const isBlocked = partner.rows[0]!.blocked;

      if (isBlocked && p.payment === "credit") {
        // R3 (pasuesi): partner tashmë i bllokuar → mall i ri me KREDI refuzohet
        // me status të qartë (cash mbetet i lejuar — SSOT §2: "vetëm cash").
        await cx.query(
          `INSERT INTO poc3_invoice (id, business_id, device_id, article_code, qty, price, partner_code, status)
           VALUES ($1, $2, $3, $4, $5::numeric, $6::numeric, $7, 'rejected_blocked')`,
          [p.invoiceId, biz, e.deviceId, p.articleCode, p.qty, p.price, p.partnerCode],
        );
        await cx.query("COMMIT");
        return "applied";
      }

      await cx.query(
        `INSERT INTO poc3_invoice (id, business_id, device_id, article_code, qty, price, partner_code, status)
         VALUES ($1, $2, $3, $4, $5::numeric, $6::numeric, $7, 'accepted')`,
        [p.invoiceId, biz, e.deviceId, p.articleCode, p.qty, p.price, p.partnerCode],
      );

      if (this.opts.failpointEventUuid === e.eventUuid) {
        throw new Error("failpoint: ndërprerje BRENDA aplikimit të eventit (tx-boundary #5)");
      }

      // R1: stoku zbritet atomikisht; NEGATIVI i përkohshëm LEJOHET (SSOT §2).
      const st = await cx.query(
        `UPDATE poc3_stock SET qty = qty - $1::numeric
          WHERE business_id = $2 AND article_code = $3
          RETURNING qty`,
        [p.qty, biz, p.articleCode],
      );
      if (st.rowCount === 0) throw new Error(`stok s'u gjet: ${p.articleCode}`);

      if (p.payment === "credit") {
        // R3 (tejkaluesi): fatura PRANOHET dhe blocked vendoset NË TË NJËJTIN tx
        // nëse balanca e re tejkalon limitin — semantikë transaksionale, pa timer.
        await cx.query(
          `UPDATE poc3_partner
              SET balance = balance + ($1::numeric * $2::numeric),
                  blocked = blocked OR (balance + ($1::numeric * $2::numeric)) > credit_limit
            WHERE business_id = $3 AND code = $4`,
          [p.qty, p.price, biz, p.partnerCode],
        );
      }

      // R2: çmimi i ngrirë i pajisjes vs katalogu i serverit → variance row.
      const serverPrice = this.opts.serverPrices.get(p.articleCode);
      if (serverPrice !== undefined) {
        const differs = await cx.query<{ d: boolean }>(
          "SELECT $1::numeric <> $2::numeric AS d",
          [p.price, serverPrice],
        );
        if (differs.rows[0]!.d) {
          await cx.query(
            `INSERT INTO poc3_price_variance (business_id, invoice_id, article_code, device_price, server_price, diff)
             VALUES ($1, $2, $3, $4::numeric, $5::numeric, $4::numeric - $5::numeric)`,
            [biz, p.invoiceId, p.articleCode, p.price, serverPrice],
          );
        }
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
