import type { Pool } from "pg";
import type { KeyObject } from "node:crypto";
import { POC5_IDS } from "./poc5-fixtures.js";
import { signProtoBytes } from "./poc5-sign.js";
import { AtkRejectionError, type AtkTransport } from "./poc5-transport.js";

/**
 * PoC-5 — FISCAL OUTBOX i IZOLUAR (SSOT §7): radhë e veçantë nga sync-u i
 * përgjithshëm; s'ndalet kurrë prej tij. Statuset = terminologjia EKZAKTE §7:
 * leshuar_offline / ne_pritje / fiskalizuar / deshtuar / kerkon_nderhyrje.
 * Ora INJEKTOHET (fake clock në teste — alarmi 48h/40h pa timer real).
 */

const WARNING_BEFORE_MS = 8 * 3600 * 1000; // warning kur kanë mbetur ≤8h (pra në ~40h)

export interface FiscalOutboxOpts {
  now?: () => Date;
  backoffMs?: number;
  /** Cap i radhës (modeli PoC-3 #8): kur mbushet → refuzim i qartë i kuponit të ri. */
  cap?: number;
}

export interface EnqueueInput {
  couponUuid: string;
  couponType: "Sale" | "Cancel" | "Return";
  protoBytes: Uint8Array;
  privateKey: KeyObject;
  createdAt?: Date; // fake clock për testet e afatit
}

export interface DrainResult {
  fiscalized: number;
  pending: number;
  failed: number;
  replayedSkips: number;
}

export class FiscalOutbox {
  private readonly now: () => Date;
  private readonly backoffMs: number;
  private readonly cap?: number;

  constructor(
    private readonly pool: Pool,
    opts: FiscalOutboxOpts = {},
  ) {
    this.now = opts.now ?? (() => new Date());
    this.backoffMs = opts.backoffMs ?? 50;
    this.cap = opts.cap;
  }

  /**
   * Fut kuponin në radhë (status: leshuar_offline). Firmos NË HYRJE; dështimi i
   * firmës/certifikatës → rresht 'kerkon_nderhyrje' + alert (kurrë humbje e heshtur).
   * Idempotente per coupon_uuid (replay → rreshti ekzistues, zero dublim).
   */
  async enqueue(input: EnqueueInput): Promise<{ outboxId: string; replay: boolean; status: string }> {
    const biz = POC5_IDS.biz;
    const created = input.createdAt ?? this.now();

    if (this.cap !== undefined) {
      // Cap-i numëron VETËM radhën e RIDËRGUESHME (leshuar_offline + ne_pritje):
      // deshtuar/kerkon_nderhyrje dalin nga cikli i dërgimit dhe ndiqen nga
      // ALARMET + ndërhyrja njerëzore — jo nga cap-i i radhës (vendim i
      // dokumentuar; alternativa "gjithçka ≠ fiskalizuar" u refuzua se do
      // bllokonte pajisjen nga gabime që s'i zgjidh dot vetë).
      const pending = await this.pool.query<{ n: string }>(
        `SELECT count(*)::text AS n FROM poc5_fiscal_outbox
          WHERE business_id = $1 AND status IN ('leshuar_offline','ne_pritje')`,
        [biz],
      );
      if (Number(pending.rows[0]!.n) >= this.cap) {
        throw new Error(
          `fiscal outbox plot (cap ${this.cap}): kuponi i ri refuzohet me zë — zbraz radhën përpara (kurrë fshirje e heshtur)`,
        );
      }
    }

    let payloadRef: string;
    let status = "leshuar_offline";
    let signError: string | undefined;
    try {
      const { detailsB64, signatureB64 } = signProtoBytes(input.protoBytes, input.privateKey);
      payloadRef = JSON.stringify({ details: detailsB64, signature: signatureB64 });
    } catch (e) {
      // Dështim firme/certifikate → kerkon_nderhyrje + alert (SSOT §7).
      payloadRef = "sign-failed";
      status = "kerkon_nderhyrje";
      signError = (e as Error).message;
    }

    // deadline_at NUK shkruhet nga app-i: vendoset nga TRIGGER-i në DB (created_at + 48h).
    const ins = await this.pool.query<{ id: string; status: string }>(
      `INSERT INTO poc5_fiscal_outbox
         (business_id, coupon_uuid, coupon_type, payload_ref, status, next_retry_at, created_at)
       VALUES ($1, $2, $3, $4, $5, $6, $6)
       ON CONFLICT (business_id, coupon_uuid) DO NOTHING
       RETURNING id, status`,
      [biz, input.couponUuid, input.couponType, payloadRef, status, created],
    );
    if (ins.rowCount === 0) {
      const existing = await this.pool.query<{ id: string; status: string }>(
        "SELECT id, status FROM poc5_fiscal_outbox WHERE business_id = $1 AND coupon_uuid = $2",
        [biz, input.couponUuid],
      );
      return { outboxId: existing.rows[0]!.id, replay: true, status: existing.rows[0]!.status };
    }
    const row = ins.rows[0]!;
    if (status === "kerkon_nderhyrje") {
      await this.alert(row.id, "sign_failure", `firma dështoi: ${signError ?? "e panjohur"}`);
    }
    return { outboxId: row.id, replay: false, status: row.status };
  }

  /** Drain: dërgo të gjitha të duhurat (leshuar_offline/ne_pritje me retry të pjekur). */
  async drain(transport: AtkTransport): Promise<DrainResult> {
    const biz = POC5_IDS.biz;
    const now = this.now();
    const due = await this.pool.query<{ id: string; payload_ref: string; attempts: number }>(
      `SELECT id, payload_ref, attempts FROM poc5_fiscal_outbox
        WHERE business_id = $1
          AND status IN ('leshuar_offline','ne_pritje')
          AND next_retry_at <= $2
        ORDER BY created_at`,
      [biz, now],
    );
    const result: DrainResult = { fiscalized: 0, pending: 0, failed: 0, replayedSkips: 0 };
    for (const row of due.rows) {
      if (row.payload_ref === "sign-failed") continue; // s'duhet të ndodhë (status tjetër), mbrojtje
      const payload = JSON.parse(row.payload_ref) as { details: string; signature: string };
      try {
        const res = await transport.sendPosCoupon(payload);
        await this.pool.query(
          `UPDATE poc5_fiscal_outbox
              SET status = 'fiskalizuar', transaction_id = $2::bigint, fiscalized_at = $3
            WHERE id = $1`,
          [row.id, res.transactionId, this.now()],
        );
        result.fiscalized += 1;
      } catch (e) {
        if (e instanceof AtkRejectionError) {
          // Refuzim biznesi (p.sh. FAILED VERIFICATION / pa ReferenceNo) → deshtuar + alert.
          await this.pool.query(
            "UPDATE poc5_fiscal_outbox SET status = 'deshtuar', attempts = attempts + 1 WHERE id = $1",
            [row.id],
          );
          await this.alert(row.id, "server_rejection", e.message.slice(0, 200));
          result.failed += 1;
        } else {
          // Gabim rrjeti/ATK down → ne_pritje me backoff (radha S'NDALET, provon prapë).
          const attempts = row.attempts + 1;
          const nextRetry = new Date(now.getTime() + this.backoffMs * 2 ** (attempts - 1));
          await this.pool.query(
            `UPDATE poc5_fiscal_outbox
                SET status = 'ne_pritje', attempts = $2, next_retry_at = $3
              WHERE id = $1`,
            [row.id, attempts, nextRetry],
          );
          result.pending += 1;
        }
      }
    }
    return result;
  }

  /** Alarmi i afatit 48h: warning kur mbeten ≤8h (~40h); missed kur kalon afati pa
   *  fiskalizim. Përfshin EDHE 'deshtuar' — refuzimi i serverit s'e shuan detyrimin
   *  ligjor 48h; kuponi i deshtuar pa u rifiskalizuar brenda afatit = missed. */
  async checkDeadlines(): Promise<{ warnings: number; missed: number }> {
    const biz = POC5_IDS.biz;
    const now = this.now();
    const warnBefore = new Date(now.getTime() + WARNING_BEFORE_MS);
    const rows = await this.pool.query<{ id: string; deadline_at: Date }>(
      `SELECT id, deadline_at FROM poc5_fiscal_outbox
        WHERE business_id = $1 AND status IN ('leshuar_offline','ne_pritje','kerkon_nderhyrje','deshtuar')`,
      [biz],
    );
    let warnings = 0;
    let missed = 0;
    for (const r of rows.rows) {
      if (r.deadline_at.getTime() <= now.getTime()) {
        await this.alert(r.id, "deadline_missed", "afati 48h KALOI pa fiskalizim — ndërhyrje urgjente");
        missed += 1;
      } else if (r.deadline_at.getTime() <= warnBefore.getTime()) {
        await this.alert(r.id, "deadline_warning", "≤8h deri në afatin 48h — fiskalizo urgjentisht");
        warnings += 1;
      }
    }
    return { warnings, missed };
  }

  private async alert(outboxId: string, type: string, message: string): Promise<void> {
    await this.pool.query(
      `INSERT INTO poc5_fiscal_alert (business_id, outbox_id, alert_type, message)
       VALUES ($1, $2, $3, $4)
       ON CONFLICT (business_id, outbox_id, alert_type) DO NOTHING`,
      [POC5_IDS.biz, outboxId, type, message],
    );
  }
}
