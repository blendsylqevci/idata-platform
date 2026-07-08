import type { DeviceSimulator, SyncEvent } from "./poc3-device.js";
import type { ApplyOutcome, ServerApply } from "./poc3-server.js";

/** Adapter i transportit (ADR-011 input — në PoC: in-process → ServerApply). */
export interface SyncTransport {
  send(event: SyncEvent): Promise<ApplyOutcome>;
}

/** Transport in-process me failpoints për testet e ndërprerjes (#5). */
export class InProcessTransport implements SyncTransport {
  private sent = 0;
  constructor(
    private readonly server: ServerApply,
    private readonly opts: {
      /** Prit lidhjen PARA se të dërgohet eventi i (k+1)-të. */
      failAfterCount?: number;
      /** Prit lidhjen PAS aplikimit të eventit me këtë seq (apliku, por pa ACK). */
      applyThenFailOnSeq?: number;
    } = {},
  ) {}

  async send(event: SyncEvent): Promise<ApplyOutcome> {
    if (this.opts.failAfterCount !== undefined && this.sent >= this.opts.failAfterCount) {
      throw new Error("lidhja u pre PARA dërgimit (failpoint transporti)");
    }
    const outcome = await this.server.apply(event);
    this.sent += 1;
    if (this.opts.applyThenFailOnSeq === event.deviceSeq) {
      throw new Error("lidhja u pre PAS aplikimit, PARA ack (failpoint transporti)");
    }
    return outcome;
  }
}

/** Transport që dështon N herët e para (për testin e retry/backoff). */
export class FlakyTransport implements SyncTransport {
  attempts = 0;
  constructor(
    private readonly server: ServerApply,
    private failuresLeft: number,
  ) {}
  async send(event: SyncEvent): Promise<ApplyOutcome> {
    this.attempts += 1;
    if (this.failuresLeft > 0) {
      this.failuresLeft -= 1;
      const err = new Error("rrjeti flaky: dështim i përkohshëm") as NodeJS.ErrnoException;
      err.code = "ECONNRESET";
      throw err;
    }
    return this.server.apply(event);
  }
}

export interface SyncOptions {
  maxRetries?: number;
  backoffMs?: number;
  /** Injektohet në teste (regjistron vonesat, pa sleep real). */
  delayFn?: (ms: number) => Promise<void>;
}

export interface SyncResult {
  applied: number;
  replayed: number;
}

/**
 * Motori i sync-ut (përmes adapter-ëve — PA vendor; ADR-011 open):
 * - rendit outbox-in sipas DEVICE_SEQ (rendi NUK varet nga occurred_at);
 * - dërgon event-per-event; ACK + heqje nga outbox VETËM pas konfirmimit
 *   (resume-safe: ndërprerje në mes → mbeten të padërguarat);
 * - retry me backoff eksponencial; replay → skip (idempotencë në DB).
 */
export async function syncDevice(
  device: DeviceSimulator,
  transport: SyncTransport,
  opts: SyncOptions = {},
): Promise<SyncResult> {
  const maxRetries = opts.maxRetries ?? 3;
  const backoffMs = opts.backoffMs ?? 50;
  const delayFn = opts.delayFn ?? ((ms) => new Promise((r) => setTimeout(r, ms)));

  const events = [...device.outbox].sort((a, b) => a.deviceSeq - b.deviceSeq);
  let applied = 0;
  let replayed = 0;

  for (const event of events) {
    let attempt = 0;
    // Retry me backoff; nëse shterohen tentativat → hidhet gabimi dhe outbox-i
    // mban eventin aktual + pasuesit (resume në sync-un e ardhshëm).
    for (;;) {
      try {
        const outcome = await transport.send(event);
        device.ack(event.eventUuid);
        if (outcome === "replay") replayed += 1;
        else applied += 1;
        break;
      } catch (err) {
        attempt += 1;
        if (attempt > maxRetries) throw err;
        await delayFn(backoffMs * 2 ** (attempt - 1));
      }
    }
  }
  return { applied, replayed };
}
