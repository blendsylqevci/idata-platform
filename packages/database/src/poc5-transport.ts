import type { KeyObject } from "node:crypto";
import { verifyDetails } from "./poc5-sign.js";
import { decodePosCoupon, COUPON_TYPE } from "./poc5-proto.js";

/**
 * PoC-5 — transporti drejt "ATK"-së me DY implementime (ANALIZA §9):
 *  - MockAtkServer: lokal, për CI — zbaton rregullat e analizës (verifikim firme
 *    → 'FAILED VERIFICATION'; Cancel/Return pa ReferenceNo → refuzim).
 *  - LiveAtkTransport: test-serveri REAL — opt-in i DYFISHTË (rregulli B) +
 *    redaction i detyrueshëm (rregulli E): ASNJË log i raw request/response/QR/
 *    firmave/ID-ve; gabimet e serverit kthehen të shkurtuara e të pastruara.
 */

export interface PosCouponRequest {
  details: string; // base64(PosCoupon proto)
  signature: string; // base64(ECDSA sig)
}

export interface FiscalizeResult {
  transactionId: string;
}

/** Refuzim biznesi nga serveri (jo problem rrjeti) — çon në 'deshtuar'. */
export class AtkRejectionError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "AtkRejectionError";
  }
}

export interface AtkTransport {
  sendPosCoupon(req: PosCouponRequest): Promise<FiscalizeResult>;
}

export interface MockAtkOpts {
  publicKey: KeyObject; // çelësi publik i POS-it të "regjistruar" (efemeral në teste)
  /** Dështime rrjeti të simuluara para se të pranojë (për retry/backoff). */
  failNetworkTimes?: number;
}

/** Simulatori lokal i serverit të fiskalizimit (rregullat e ANALIZA §4/§6). */
export class MockAtkServer implements AtkTransport {
  private txCounter = 0;
  private networkFailuresLeft: number;
  readonly seenCouponIds = new Map<string, string>(); // CouponId → transactionId
  /** Sa herë u ri-dërgua një CouponId tashmë i pranuar (skenari #5 crash-pas-send). */
  duplicateSends = 0;

  constructor(private readonly opts: MockAtkOpts) {
    this.networkFailuresLeft = opts.failNetworkTimes ?? 0;
  }

  async sendPosCoupon(req: PosCouponRequest): Promise<FiscalizeResult> {
    if (this.networkFailuresLeft > 0) {
      this.networkFailuresLeft -= 1;
      const err = new Error("rrjeti dështoi (simulim)") as NodeJS.ErrnoException;
      err.code = "ECONNRESET";
      throw err;
    }
    // 1) Verifikimi i firmës — si serveri real: dështon → FAILED VERIFICATION.
    if (!verifyDetails(req.details, req.signature, this.opts.publicKey)) {
      throw new AtkRejectionError("FAILED VERIFICATION");
    }
    // 2) Rregullat e kuponit (ANALIZA §4).
    const coupon = await decodePosCoupon(Buffer.from(req.details, "base64"));
    const cid = String(coupon.CouponId);
    // Ri-dërgim i të njëjtit kupon (crash pas send, para ACK) → mock-u sillet si
    // server IDEMPOTENT (i njëjti transaction_id). ⚠️ SUPOZIM i flamuruar:
    // sjellja REALE e serverit ndaj dublimit dokumentohet empirikisht në Tier 2.
    const seen = this.seenCouponIds.get(cid);
    if (seen !== undefined) {
      this.duplicateSends += 1;
      return { transactionId: seen };
    }
    const type = Number(coupon.Type ?? 0);
    const refNo = String(coupon.ReferenceNo ?? "0");
    if ((type === COUPON_TYPE.Cancel || type === COUPON_TYPE.Return) && refNo === "0") {
      throw new AtkRejectionError("coupon rejected: missing ReferenceNo for Cancel/Return");
    }
    this.txCounter += 1;
    const txId = String(1000 + this.txCounter);
    this.seenCouponIds.set(cid, txId);
    return { transactionId: txId };
  }
}

/** Opt-in i DYFISHTË (rregulli B): RUN_SEF_LIVE=1 ∧ SEF_ENV=test ∧ URL = test-serveri.
 *  Kontrolli i URL-së = ORIGIN i saktë (jo startsWith — bllokon p.sh.
 *  "fiskalizimi-test.atk-ks.org.evil.com"). */
export function liveOptIn(): { enabled: boolean; reason: string; baseUrl: string } {
  const baseUrl = process.env.SEF_TEST_BASE_URL ?? "";
  if (process.env.RUN_SEF_LIVE !== "1") {
    return { enabled: false, reason: "RUN_SEF_LIVE≠1", baseUrl };
  }
  if (process.env.SEF_ENV !== "test") {
    return { enabled: false, reason: "SEF_ENV≠test", baseUrl };
  }
  let origin: string;
  try {
    origin = new URL(baseUrl).origin;
  } catch {
    return { enabled: false, reason: "SEF_TEST_BASE_URL i pavlefshëm", baseUrl };
  }
  if (origin !== "https://fiskalizimi-test.atk-ks.org") {
    return { enabled: false, reason: "SEF_TEST_BASE_URL≠test-server (prodhimi i NDALUAR)", baseUrl };
  }
  return { enabled: true, reason: "opt-in i dyfishtë OK", baseUrl };
}

/** Pastron çdo mesazh gabimi serveri: pa payload, pa ID, i shkurtuar (rregulli E). */
function sanitizeServerError(status: number, body: string): string {
  const short = body.replace(/[A-Za-z0-9+/=]{40,}/g, "[redacted-b64]").slice(0, 160);
  return `ATK test-server HTTP ${status}: ${short}`;
}

/** Transporti LIVE — VETËM test-serveri; asnjë logim i raw payload-eve (rregulli E). */
export class LiveAtkTransport implements AtkTransport {
  private readonly baseUrl: string;

  constructor() {
    const gate = liveOptIn();
    if (!gate.enabled) {
      throw new Error(`LiveAtkTransport i çaktivizuar: ${gate.reason}`);
    }
    this.baseUrl = gate.baseUrl;
  }

  async sendPosCoupon(req: PosCouponRequest): Promise<FiscalizeResult> {
    const res = await fetch(`${this.baseUrl}/pos/coupon`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ details: req.details, signature: req.signature }),
    });
    const text = await res.text();
    if (!res.ok) {
      // Redaction: kurrë echo i details/signature; trupi i shkurtuar + i pastruar.
      throw new AtkRejectionError(sanitizeServerError(res.status, text));
    }
    const parsed = JSON.parse(text) as { transaction_id?: number | string };
    if (parsed.transaction_id === undefined) {
      throw new AtkRejectionError(sanitizeServerError(res.status, "pa transaction_id në përgjigje"));
    }
    return { transactionId: String(parsed.transaction_id) };
  }
}
