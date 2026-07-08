import { randomUUID } from "node:crypto";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";

/**
 * PoC-3 "pajisja" = simulator në Node me store lokal të PERSISTUAR në file
 * (vendim i para-marrë: PA app RN/Expo — ADR-016 open; DB lokale reale
 * SQLite/Realm vjen me atë ADR). Store-i VETËM në temp/test dir — kurrë në repo.
 */

export interface Poc3InvoicePayload {
  invoiceId: string;
  articleCode: string;
  qty: string; // string decimal — kurrë float
  price: string; // çmimi i NGRIRË në pajisje (R2)
  partnerCode: string;
  payment: "cash" | "credit";
}

export interface SyncEvent {
  eventUuid: string;
  deviceId: string;
  deviceSeq: number;
  occurredAt: string; // ora e pajisjes — INFORMATIVE; rendi = deviceSeq
  type: "invoice";
  payload: Poc3InvoicePayload;
}

export interface DeviceState {
  deviceId: string;
  nextSeq: number;
  outbox: SyncEvent[];
  frozenPrices: Record<string, string>;
  capacity: number;
}

/** Adapter i store-it lokal (ADR-011 input — zëvendësohet nga SQLite/vendor më vonë). */
export interface LocalStore {
  load(deviceId: string): DeviceState | undefined;
  save(state: DeviceState): void;
}

/** Implementim PoC: një JSON file per pajisje, në dir të përkohshëm testi. */
export class FileLocalStore implements LocalStore {
  constructor(private readonly dir: string) {}
  private pathFor(deviceId: string): string {
    return join(this.dir, `${deviceId}.json`);
  }
  load(deviceId: string): DeviceState | undefined {
    const p = this.pathFor(deviceId);
    if (!existsSync(p)) return undefined;
    return JSON.parse(readFileSync(p, "utf8")) as DeviceState;
  }
  save(state: DeviceState): void {
    writeFileSync(this.pathFor(state.deviceId), JSON.stringify(state));
  }
}

export interface CreateInvoiceOpts {
  articleCode: string;
  qty: string;
  partnerCode: string;
  payment: "cash" | "credit";
  occurredAt?: string;
}

export class DeviceSimulator {
  private constructor(
    private readonly store: LocalStore,
    private state: DeviceState,
  ) {}

  /** Pajisje e re (regjistrohet në store që në krijim). */
  static create(
    store: LocalStore,
    opts: { deviceId: string; frozenPrices: Record<string, string>; capacity?: number },
  ): DeviceSimulator {
    const state: DeviceState = {
      deviceId: opts.deviceId,
      nextSeq: 1,
      outbox: [],
      frozenPrices: opts.frozenPrices,
      capacity: opts.capacity ?? 100,
    };
    store.save(state);
    return new DeviceSimulator(store, state);
  }

  /** RIKRIJIM NGA FILE pas "crash" (#4): asgjë nga memoria — vetëm store-i. */
  static loadFrom(store: LocalStore, deviceId: string): DeviceSimulator {
    const state = store.load(deviceId);
    if (!state) throw new Error(`s'ka store të persistuar për pajisjen ${deviceId}`);
    return new DeviceSimulator(store, state);
  }

  get deviceId(): string {
    return this.state.deviceId;
  }
  get nextSeq(): number {
    return this.state.nextSeq;
  }
  get outbox(): readonly SyncEvent[] {
    return [...this.state.outbox];
  }

  /** Faturë OFFLINE: UUID + device_seq monotonic; çmim i ngrirë; 100% pa server. */
  createInvoice(o: CreateInvoiceOpts): SyncEvent {
    if (this.state.outbox.length >= this.state.capacity) {
      // #8: kur outbox mbushet → refuzo KRIJIMIN e ri me gabim të qartë.
      // KURRË fshirje e heshtur e eventeve të vjetra (ato janë dokumente).
      throw new Error(
        `outbox plot (cap ${this.state.capacity}): krijimi i faturës refuzohet — sync-o para se të vazhdosh`,
      );
    }
    const price = this.state.frozenPrices[o.articleCode];
    if (!price) throw new Error(`s'ka çmim të ngrirë për ${o.articleCode}`);
    const event: SyncEvent = {
      eventUuid: randomUUID(),
      deviceId: this.state.deviceId,
      deviceSeq: this.state.nextSeq,
      occurredAt: o.occurredAt ?? new Date().toISOString(),
      type: "invoice",
      payload: {
        invoiceId: randomUUID(),
        articleCode: o.articleCode,
        qty: o.qty,
        price,
        partnerCode: o.partnerCode,
        payment: o.payment,
      },
    };
    this.state.nextSeq += 1;
    this.state.outbox.push(event);
    this.store.save(this.state);
    return event;
  }

  /** ACK pas konfirmimit të serverit: hiq nga outbox + persisto (resume-safe). */
  ack(eventUuid: string): void {
    this.state.outbox = this.state.outbox.filter((e) => e.eventUuid !== eventUuid);
    this.store.save(this.state);
  }

  /** VETËM për testin e rendit: përzien outbox-in në store (rendi i mbërritjes ≠ seq). */
  shuffleOutboxForTest(): void {
    this.state.outbox = [...this.state.outbox].reverse();
    this.store.save(this.state);
  }
}
