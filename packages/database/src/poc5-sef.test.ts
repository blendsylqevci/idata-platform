import { describe, it, expect, beforeAll, afterAll } from "vitest";
import { randomUUID } from "node:crypto";
import { readFileSync } from "node:fs";
import { mkdtemp } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { getPool, closePool } from "./pool.js";
import { runMigrations } from "./migrate.js";
import { seedPoc5, POC5_IDS } from "./poc5-fixtures.js";
import { euroToItemScale, euroToCents, itemScaleToEuro, centsToEuro, toUnixSeconds } from "./poc5-money.js";
import {
  encodePosCoupon,
  encodeCitizenFromPos,
  decodePosCoupon,
  decodeCitizenCoupon,
  COUPON_TYPE,
  PAYMENT_TYPE,
  VENDORED_PROTO_PATH,
  type PosCouponInput,
} from "./poc5-proto.js";
import { generateEphemeralP256, signProtoBytes, verifyDetails, qrString, sha256Hex } from "./poc5-sign.js";
import { MockAtkServer, AtkRejectionError, liveOptIn } from "./poc5-transport.js";
import { FiscalOutbox } from "./poc5-outbox.js";
import { seedPoc3, ensureArticle, ensurePartner } from "./poc3-fixtures.js";
import { DeviceSimulator, FileLocalStore } from "./poc3-device.js";
import { ServerApplyPg } from "./poc3-server.js";
import { syncDevice, InProcessTransport } from "./poc3-sync.js";

// Checksum i PIN-uar i models.proto të vendor-uar — drift-check në çdo CI run.
// PROVENANCE (i gjurmueshëm): https://github.com/fiskalizimi/pos-csharp
//   file: models.proto · commit: a4060ef08838 (2026-06-11) · marrë: 2026-07-08
const VENDORED_PROTO_SHA256 = "fc42f76e353d23b575c989d186b05671a340f5845fc8304306bdfac05cf68efb";

// Kuponi sintetik i ndërtuar me VLERAT E SHEMBULLIT PUBLIK ZYRTAR të README-së
// (uje rugove/sendviq/buke/machiato) — ZERO identifikues realë.
function officialExampleCoupon(over: Partial<PosCouponInput> = {}): PosCouponInput {
  return {
    businessId: "60100",
    couponId: "10",
    branchId: "1",
    location: "Prishtine",
    operatorId: "Kushtrimi",
    posId: "1",
    applicationId: "1234",
    // Shembulli zyrtar përdor string 16-shifror; këtu vlerë sintetike PA run
    // 12+ shifrash (rregulli gitleaks i repo-s) — formati real vjen në Tier 2.
    verificationNo: "DEMO-1234-5678-90",
    type: COUPON_TYPE.Sale,
    timeUnixSeconds: 1727796620, // 2024-10-01T15:30:20Z — shembulli zyrtar
    items: [
      { name: "uje rugove", priceX10000: euroToItemScale("1.50"), unit: "cope", quantity: 3, totalX10000: euroToItemScale("4.50"), taxRate: "C" },
      { name: "sendviq", priceX10000: euroToItemScale("3.00"), unit: "cope", quantity: 2, totalX10000: euroToItemScale("6.00"), taxRate: "E" },
      { name: "buke", priceX10000: euroToItemScale("0.80"), unit: "cope", quantity: 4, totalX10000: euroToItemScale("3.20"), taxRate: "D" },
      { name: "machiato e madhe", priceX10000: euroToItemScale("1.50"), unit: "cope", quantity: 3, totalX10000: euroToItemScale("4.50"), taxRate: "E" },
    ],
    payments: [
      { type: PAYMENT_TYPE.Cash, amountCents: euroToCents("5.00") },
      { type: PAYMENT_TYPE.CreditCard, amountCents: euroToCents("10.00") },
      { type: PAYMENT_TYPE.Voucher, amountCents: euroToCents("3.20") },
    ],
    totalCents: euroToCents("18.20"),
    taxGroups: [
      { taxRate: "C", totalForTaxCents: euroToCents("4.50"), totalTaxCents: euroToCents("0.00") },
      { taxRate: "D", totalForTaxCents: euroToCents("2.96"), totalTaxCents: euroToCents("0.24") },
      { taxRate: "E", totalForTaxCents: euroToCents("8.89"), totalTaxCents: euroToCents("1.61") },
    ],
    totalTaxCents: euroToCents("1.85"),
    totalNoTaxCents: euroToCents("16.35"),
    referenceNo: "0",
    totalDiscountCents: euroToCents("0.00"),
    ...over,
  };
}

const keys = generateEphemeralP256(); // çelësa EFEMERALË — kurrë nga skedarë sekretë

describe("PoC-5 SEF — Tier 1 CI-safe (mock + çelësa efemeralë; kërkon Postgres)", () => {
  beforeAll(async () => {
    await runMigrations(); // 0001..0006
    await seedPoc5(getPool());
  });
  afterAll(async () => {
    await closePool();
  });

  it("drift-check: models.proto i vendor-uar = byte-identik me upstream (sha256 i pin-uar)", () => {
    expect(sha256Hex(readFileSync(VENDORED_PROTO_PATH))).toBe(VENDORED_PROTO_SHA256);
  });

  it("money per-field: tabela zyrtare në të dy drejtimet (items ×10000; totals cents)", () => {
    // Nga tabela zyrtare e README (ANALIZA §3):
    expect(euroToItemScale("1.00")).toBe(10000n);
    expect(euroToItemScale("0.01")).toBe(100n);
    expect(euroToItemScale("12.3456")).toBe(123456n);
    expect(euroToItemScale("0.0001")).toBe(1n);
    expect(euroToCents("1.00")).toBe(100n);
    expect(euroToCents("0.01")).toBe(1n);
    expect(euroToCents("12.34")).toBe(1234n);
    // Drejtimi i kundërt:
    expect(itemScaleToEuro(15000n)).toBe("1.5000");
    expect(itemScaleToEuro(123456n)).toBe("12.3456");
    expect(centsToEuro(1820n)).toBe("18.20");
    expect(centsToEuro(1n)).toBe("0.01");
    // Round-trip:
    expect(itemScaleToEuro(euroToItemScale("4.50"))).toBe("4.5000");
    expect(centsToEuro(euroToCents("16.35"))).toBe("16.35");
  });

  it("money: përzierja e shkallëve DËSHTON (kufiri i fortë)", () => {
    expect(() => euroToCents("1.005")).toThrow(/max 2/); // 3 dp në cents = përzierje
    expect(() => euroToItemScale("1.00001")).toThrow(/max 4/); // 5 dp = e pavlefshme
    // @ts-expect-error — number i ndaluar në kufirin monetar
    expect(() => euroToCents(1.0)).toThrow(/kurrë number/);
    expect(() => euroToItemScale("-1.00")).toThrow(/negativ/);
  });

  it("DETYRUESHËM — PosCoupon.Time = Unix SEKONDA (mbrojtje nga Date.now()=ms)", async () => {
    const msEpoch = Date.UTC(2024, 9, 1, 15, 30, 20); // epoch-ms (13 shifra — pa literal këtu)
    expect(msEpoch / 1000).toBe(1727796620); // literal i pin-uar në SEKONDA (shembulli zyrtar)
    expect(toUnixSeconds(msEpoch)).toBe(1727796620);
    expect(toUnixSeconds(new Date(msEpoch))).toBe(1727796620);
    // Serializimi real: fusha Time del në SEKONDA (10 shifra), JO milisekonda (13).
    const bytes = await encodePosCoupon(officialExampleCoupon({ timeUnixSeconds: toUnixSeconds(msEpoch) }));
    const decoded = await decodePosCoupon(bytes);
    expect(String(decoded.Time)).toBe("1727796620");
    expect(String(decoded.Time)).not.toBe(String(msEpoch)); // jo ms
    expect(String(decoded.Time).length).toBe(10);
  });

  it("proto round-trip me vlerat e shembullit zyrtar (anti-FAILED VERIFICATION, ana lokale)", async () => {
    const bytes = await encodePosCoupon(officialExampleCoupon());
    const d = await decodePosCoupon(bytes);
    expect(String(d.Total)).toBe("1820");
    expect(String(d.TotalTax)).toBe("185");
    expect(String(d.TotalNoTax)).toBe("1635");
    const items = d.Items as { Price: string; Total: string; TaxRate: string }[];
    expect(items[0]).toMatchObject({ Price: "15000", Total: "45000", TaxRate: "C" });
    expect(items[2]).toMatchObject({ Price: "8000", Total: "32000", TaxRate: "D" });
    // CitizenCoupon NGA i njëjti input → detajet e përbashkëta IDENTIKE.
    const citizen = await decodeCitizenCoupon(await encodeCitizenFromPos(officialExampleCoupon()));
    expect(String(citizen.Total)).toBe(String(d.Total));
    expect(String(citizen.TotalTax)).toBe(String(d.TotalTax));
    expect(String(citizen.TotalNoTax)).toBe(String(d.TotalNoTax));
    expect(String(citizen.CouponId)).toBe(String(d.CouponId));
  });

  it("firma P1363 (raw r||s) = EKZAKT 64 byte për P-256; DER dallohet dhe s'ngatërrohet", async () => {
    const bytes = await encodePosCoupon(officialExampleCoupon({ couponId: "150" }));
    // P1363 (default-i ynë eksplicit): gjithmonë 64 byte (r 32 + s 32).
    const p1363 = signProtoBytes(bytes, keys.privateKey, "ieee-p1363");
    expect(Buffer.from(p1363.signatureB64, "base64").length).toBe(64);
    expect(verifyDetails(p1363.detailsB64, p1363.signatureB64, keys.publicKey, "ieee-p1363")).toBe(true);
    // DER (default i Node-it — prandaj dsaEncoding EKSPLICIT): ≠64 byte.
    const der = signProtoBytes(bytes, keys.privateKey, "der");
    expect(Buffer.from(der.signatureB64, "base64").length).not.toBe(64);
    expect(verifyDetails(der.detailsB64, der.signatureB64, keys.publicKey, "der")).toBe(true);
    // Kryqëzimi i formateve dështon të verifikohet — dallim i qartë P1363 vs DER.
    expect(verifyDetails(der.detailsB64, der.signatureB64, keys.publicKey, "ieee-p1363")).toBe(false);
    expect(verifyDetails(p1363.detailsB64, p1363.signatureB64, keys.publicKey, "der")).toBe(false);
  });

  it("sign/verify P-256 + formati QR = base64(proto)|base64(sig)", async () => {
    const bytes = await encodePosCoupon(officialExampleCoupon());
    const { detailsB64, signatureB64 } = signProtoBytes(bytes, keys.privateKey);
    expect(verifyDetails(detailsB64, signatureB64, keys.publicKey)).toBe(true);
    // Tamper → verifikimi dështon.
    const tampered = detailsB64.slice(0, -4) + (detailsB64.endsWith("AAAA") ? "BBBB" : "AAAA");
    expect(verifyDetails(tampered, signatureB64, keys.publicKey)).toBe(false);
    const qr = qrString(detailsB64, signatureB64);
    expect(qr).toMatch(/^[A-Za-z0-9+/]+=*\|[A-Za-z0-9+/]+=*$/);
    expect(qr.split("|").length).toBe(2);
  });

  it("mock ATK: Sale valid → transaction_id; firmë e prishur → FAILED VERIFICATION", async () => {
    const mock = new MockAtkServer({ publicKey: keys.publicKey });
    const bytes = await encodePosCoupon(officialExampleCoupon({ couponId: "101" }));
    const p = signProtoBytes(bytes, keys.privateKey);
    const ok = await mock.sendPosCoupon({ details: p.detailsB64, signature: p.signatureB64 });
    expect(Number(ok.transactionId)).toBeGreaterThan(0);
    const wrongKeys = generateEphemeralP256(); // firmos me çelës TJETËR → server refuzon
    const bad = signProtoBytes(bytes, wrongKeys.privateKey);
    await expect(mock.sendPosCoupon({ details: bad.detailsB64, signature: bad.signatureB64 })).rejects.toThrow(
      /FAILED VERIFICATION/,
    );
  });

  it("CouponType + ReferenceNo: Return/Cancel PA ReferenceNo → refuzohen; ME → pranohen", async () => {
    const mock = new MockAtkServer({ publicKey: keys.publicKey });
    const send = async (c: PosCouponInput) => {
      const p = signProtoBytes(await encodePosCoupon(c), keys.privateKey);
      return mock.sendPosCoupon({ details: p.detailsB64, signature: p.signatureB64 });
    };
    await expect(
      send(officialExampleCoupon({ couponId: "201", type: COUPON_TYPE.Return, referenceNo: "0" })),
    ).rejects.toThrow(/missing ReferenceNo/);
    await expect(
      send(officialExampleCoupon({ couponId: "202", type: COUPON_TYPE.Cancel, referenceNo: "0" })),
    ).rejects.toThrow(/missing ReferenceNo/);
    const ret = await send(officialExampleCoupon({ couponId: "203", type: COUPON_TYPE.Return, referenceNo: "101" }));
    expect(Number(ret.transactionId)).toBeGreaterThan(0);
  });

  it("outbox: leshuar_offline → (rrjet down ×2, backoff) ne_pritje → fiskalizuar; idempotencë replay", async () => {
    let fakeNow = new Date("2027-01-10T08:00:00Z");
    const outbox = new FiscalOutbox(getPool(), { now: () => fakeNow, backoffMs: 60_000 });
    const couponUuid = randomUUID();
    const bytes = await encodePosCoupon(officialExampleCoupon({ couponId: "301" }));
    // Dështim rrjeti i SHËNJESTRUAR vetëm për kuponin TONË (leftovers nga runs
    // të ndërprera shërohen nga inner-i OK pa na konsumuar dështimet).
    const inner = new MockAtkServer({ publicKey: keys.publicKey });
    const myDetails = Buffer.from(bytes).toString("base64");
    let targetedFailsLeft = 2;
    const mock = {
      async sendPosCoupon(req: { details: string; signature: string }) {
        if (req.details === myDetails && targetedFailsLeft > 0) {
          targetedFailsLeft -= 1;
          const err = new Error("rrjeti dështoi (targeted)") as NodeJS.ErrnoException;
          err.code = "ECONNRESET";
          throw err;
        }
        return inner.sendPosCoupon(req);
      },
    };

    const e1 = await outbox.enqueue({ couponUuid, couponType: "Sale", protoBytes: bytes, privateKey: keys.privateKey });
    expect(e1).toMatchObject({ replay: false, status: "leshuar_offline" });
    const e2 = await outbox.enqueue({ couponUuid, couponType: "Sale", protoBytes: bytes, privateKey: keys.privateKey });
    expect(e2).toMatchObject({ replay: true }); // zero dublim në radhë

    // Drain-i është business-wide (mund të prekë edhe leftovers nga runs të
    // ndërprera) → EKZAKTËSIA matet mbi RRESHTIN TONË (row-scoped), jo mbi counts.
    const myRow = async () =>
      (
        await getPool().query<{ status: string; attempts: number; transaction_id: string | null }>(
          "SELECT status, attempts, transaction_id::text FROM poc5_fiscal_outbox WHERE business_id = $1 AND coupon_uuid = $2",
          [POC5_IDS.biz, couponUuid],
        )
      ).rows[0]!;

    // Tentativa 1: rrjeti down → ne_pritje me backoff (attempts=1).
    await outbox.drain(mock);
    expect(await myRow()).toMatchObject({ status: "ne_pritje", attempts: 1 });

    // Para pjekjes së retry-t: drain s'e prek (backoff respektohet — attempts mbetet 1).
    await outbox.drain(mock);
    expect((await myRow()).attempts).toBe(1);

    // Avanco orën fake → retry 2 (down; attempts=2) → retry 3 (ok) → fiskalizuar.
    fakeNow = new Date(fakeNow.getTime() + 2 * 60_000);
    await outbox.drain(mock);
    expect((await myRow()).attempts).toBe(2);
    fakeNow = new Date(fakeNow.getTime() + 5 * 60_000);
    await outbox.drain(mock);
    const done = await myRow();
    expect(done.status).toBe("fiskalizuar");
    expect(Number(done.transaction_id)).toBeGreaterThan(0);

    // Pas fiskalizimit: drain i ri s'e prek më rreshtin (statusi final e përjashton).
    const txBefore = done.transaction_id;
    await outbox.drain(mock);
    expect((await myRow()).transaction_id).toBe(txBefore);
  });

  it("refuzim biznesi nga serveri → deshtuar + alert server_rejection", async () => {
    const outbox = new FiscalOutbox(getPool(), { backoffMs: 10 });
    const mock = new MockAtkServer({ publicKey: keys.publicKey });
    const couponUuid = randomUUID();
    // Return pa ReferenceNo → serveri e refuzon si biznes-rregull.
    const bytes = await encodePosCoupon(
      officialExampleCoupon({ couponId: "401", type: COUPON_TYPE.Return, referenceNo: "0" }),
    );
    const enq = await outbox.enqueue({ couponUuid, couponType: "Return", protoBytes: bytes, privateKey: keys.privateKey });
    const r = await outbox.drain(mock);
    expect(r.failed).toBe(1);
    const st = await getPool().query<{ status: string }>(
      "SELECT status FROM poc5_fiscal_outbox WHERE id = $1",
      [enq.outboxId],
    );
    expect(st.rows[0]!.status).toBe("deshtuar");
    const alert = await getPool().query(
      "SELECT 1 FROM poc5_fiscal_alert WHERE business_id = $1 AND outbox_id = $2 AND alert_type = 'server_rejection'",
      [POC5_IDS.biz, enq.outboxId],
    );
    expect(alert.rowCount).toBe(1);
  });

  it("dështim firme → kerkon_nderhyrje + alert sign_failure (kurrë humbje e heshtur)", async () => {
    const outbox = new FiscalOutbox(getPool(), { backoffMs: 10 });
    const couponUuid = randomUUID();
    const bytes = await encodePosCoupon(officialExampleCoupon({ couponId: "501" }));
    // Çelës i pavlefshëm për firmosje (publiku në vend të privatit) → sign hedh.
    const enq = await outbox.enqueue({
      couponUuid,
      couponType: "Sale",
      protoBytes: bytes,
      privateKey: keys.publicKey as never,
    });
    expect(enq.status).toBe("kerkon_nderhyrje");
    const alert = await getPool().query(
      "SELECT 1 FROM poc5_fiscal_alert WHERE business_id = $1 AND outbox_id = $2 AND alert_type = 'sign_failure'",
      [POC5_IDS.biz, enq.outboxId],
    );
    expect(alert.rowCount).toBe(1);
  });

  it("alarmi 48h me fake clock: warning në ~40h (≤8h të mbetura); missed pas 48h", async () => {
    const t0 = new Date("2027-02-01T00:00:00Z");
    let fakeNow = t0;
    const outbox = new FiscalOutbox(getPool(), { now: () => fakeNow, backoffMs: 10 });
    const couponUuid = randomUUID();
    const bytes = await encodePosCoupon(officialExampleCoupon({ couponId: "601" }));
    const enq = await outbox.enqueue({ couponUuid, couponType: "Sale", protoBytes: bytes, privateKey: keys.privateKey, createdAt: t0 });

    // GARANCIA NË DB: deadline_at = created_at + 48h EKZAKT (vendosur nga trigger-i)…
    const dl = await getPool().query<{ ok: boolean }>(
      "SELECT deadline_at = created_at + interval '48 hours' AS ok FROM poc5_fiscal_outbox WHERE id = $1",
      [enq.outboxId],
    );
    expect(dl.rows[0]!.ok).toBe(true);
    // …dhe as UPDATE i qëllimshëm s'e ndryshon dot (trigger-i e rillogarit).
    await getPool().query("UPDATE poc5_fiscal_outbox SET deadline_at = now() + interval '10 years' WHERE id = $1", [
      enq.outboxId,
    ]);
    const dl2 = await getPool().query<{ ok: boolean }>(
      "SELECT deadline_at = created_at + interval '48 hours' AS ok FROM poc5_fiscal_outbox WHERE id = $1",
      [enq.outboxId],
    );
    expect(dl2.rows[0]!.ok).toBe(true);

    fakeNow = new Date(t0.getTime() + 39 * 3600 * 1000); // 39h: ende pa warning
    await outbox.checkDeadlines();
    const w0 = await getPool().query(
      "SELECT 1 FROM poc5_fiscal_alert WHERE outbox_id = $1 AND alert_type = 'deadline_warning'",
      [enq.outboxId],
    );
    expect(w0.rowCount).toBe(0);

    fakeNow = new Date(t0.getTime() + 40 * 3600 * 1000); // 40h: warning PARA afatit
    await outbox.checkDeadlines();
    const w1 = await getPool().query(
      "SELECT 1 FROM poc5_fiscal_alert WHERE outbox_id = $1 AND alert_type = 'deadline_warning'",
      [enq.outboxId],
    );
    expect(w1.rowCount).toBe(1);

    fakeNow = new Date(t0.getTime() + 49 * 3600 * 1000); // 49h: afati KALOI
    await outbox.checkDeadlines();
    const m1 = await getPool().query(
      "SELECT 1 FROM poc5_fiscal_alert WHERE outbox_id = $1 AND alert_type = 'deadline_missed'",
      [enq.outboxId],
    );
    expect(m1.rowCount).toBe(1);

    // Shërim (edhe brenda-suite hygiene): fiskalizohet me sukses pas rikthimit.
    const okMock = new MockAtkServer({ publicKey: keys.publicKey });
    const r = await outbox.drain(okMock);
    expect(r.fiscalized).toBeGreaterThanOrEqual(1);
  });

  it("#8 fiscal outbox cap: kuponi i ri refuzohet me zë; recovery pas drain", async () => {
    let fakeNow = new Date("2027-03-01T00:00:00Z");
    const outbox = new FiscalOutbox(getPool(), { now: () => fakeNow, backoffMs: 10, cap: 2 });
    const okMock = new MockAtkServer({ publicKey: keys.publicKey });
    // Zbraz pending-un ekzistues që baseline i cap-it të jetë i qartë.
    await new FiscalOutbox(getPool(), { now: () => fakeNow, backoffMs: 10 }).drain(okMock);

    const mk = async (id: string) =>
      outbox.enqueue({
        couponUuid: randomUUID(),
        couponType: "Sale",
        protoBytes: await encodePosCoupon(officialExampleCoupon({ couponId: id })),
        privateKey: keys.privateKey,
      });
    await mk("701");
    await mk("702");
    await expect(mk("703")).rejects.toThrow(/fiscal outbox plot \(cap 2\)/);
    const r = await outbox.drain(okMock);
    expect(r.fiscalized).toBe(2);
    const again = await mk("704"); // recovery: pas zbrazjes krijimi rifillon
    expect(again.status).toBe("leshuar_offline");
    await outbox.drain(okMock);
  });

  it("IZOLIMI dy-drejtimësh (SSOT §7): fiscal outbox drain-on ndërsa radha e PoC-3 është E BLLOKUAR — dhe anasjelltas", async () => {
    // Përgatit PoC-3: pajisje me cap 1 (radha PLOT) + transport offline (i bllokuar).
    await seedPoc3(getPool());
    const run = randomUUID().slice(0, 8);
    await ensureArticle(getPool(), `art-demo-${run}-i`, "50.00");
    await ensurePartner(getPool(), `prt-demo-${run}-i`, "100000.00");
    const store = new FileLocalStore(await mkdtemp(join(tmpdir(), "poc5-iso-")));
    const dev = DeviceSimulator.create(store, {
      deviceId: `dev-demo-${run}-i`,
      frozenPrices: { [`art-demo-${run}-i`]: "2.00" },
      capacity: 1,
    });
    dev.createInvoice({ articleCode: `art-demo-${run}-i`, qty: "1.00", partnerCode: `prt-demo-${run}-i`, payment: "cash" });
    expect(() =>
      dev.createInvoice({ articleCode: `art-demo-${run}-i`, qty: "1.00", partnerCode: `prt-demo-${run}-i`, payment: "cash" }),
    ).toThrow(/outbox plot/); // radha e sync-ut = PLOT (e bllokuar edhe nga rrjeti më poshtë)

    // (a) Me PoC-3 të bllokuar → FISCAL outbox punon normalisht.
    const fiscal = new FiscalOutbox(getPool(), { backoffMs: 10 });
    const okMock = new MockAtkServer({ publicKey: keys.publicKey });
    const enq = await fiscal.enqueue({
      couponUuid: randomUUID(),
      couponType: "Sale",
      protoBytes: await encodePosCoupon(officialExampleCoupon({ couponId: "801" })),
      privateKey: keys.privateKey,
    });
    const fr = await fiscal.drain(okMock);
    expect(fr.fiscalized).toBeGreaterThanOrEqual(1);
    const fst = await getPool().query<{ status: string }>("SELECT status FROM poc5_fiscal_outbox WHERE id = $1", [
      enq.outboxId,
    ]);
    expect(fst.rows[0]!.status).toBe("fiskalizuar"); // fiscal S'NDALET nga sync-u i bllokuar

    // (b) Anasjelltas: FISCAL i ngecur (rrjeti i tij down → ne_pritje) → PoC-3 sync punon.
    const stuckMock = new MockAtkServer({ publicKey: keys.publicKey, failNetworkTimes: 99 });
    const stuck = await fiscal.enqueue({
      couponUuid: randomUUID(),
      couponType: "Sale",
      protoBytes: await encodePosCoupon(officialExampleCoupon({ couponId: "802" })),
      privateKey: keys.privateKey,
    });
    await new FiscalOutbox(getPool(), { backoffMs: 10 }).drain(stuckMock);
    const sst = await getPool().query<{ status: string }>("SELECT status FROM poc5_fiscal_outbox WHERE id = $1", [
      stuck.outboxId,
    ]);
    expect(sst.rows[0]!.status).toBe("ne_pritje"); // fiskal i ngecur…

    const res = await syncDevice(dev, new InProcessTransport(new ServerApplyPg(getPool(), { serverPrices: new Map() })), {
      delayFn: () => Promise.resolve(),
    });
    expect(res.applied).toBe(1); // …ndërsa sync-u i përgjithshëm ecën i PAPENGUAR

    // Higjienë: shëro të ngecurin me orë të ARDHME (kalon backoff-in deterministikisht).
    await new FiscalOutbox(getPool(), {
      backoffMs: 10,
      now: () => new Date(Date.now() + 60_000),
    }).drain(okMock);
    const healed = await getPool().query<{ status: string }>(
      "SELECT status FROM poc5_fiscal_outbox WHERE id = $1",
      [stuck.outboxId],
    );
    expect(healed.rows[0]!.status).toBe("fiskalizuar");
  });

  it("#5 crash PAS send, PARA ACK: ri-dërgimi s'krijon dublim lokal; mock idempotent (supozim — realja=Tier 2)", async () => {
    const outbox = new FiscalOutbox(getPool(), { backoffMs: 10, now: () => new Date(Date.now() + 120_000) });
    const inner = new MockAtkServer({ publicKey: keys.publicKey });
    const couponUuid = randomUUID();
    const bytes = await encodePosCoupon(officialExampleCoupon({ couponId: "901" }));
    await outbox.enqueue({ couponUuid, couponType: "Sale", protoBytes: bytes, privateKey: keys.privateKey });

    // Transporti: serveri E PRANON, por lidhja bie PARA ACK-ut tonë → rreshti mbetet për retry.
    let crashOnce = true;
    const crashAfterSend = {
      async sendPosCoupon(req: { details: string; signature: string }) {
        const res = await inner.sendPosCoupon(req);
        if (crashOnce) {
          crashOnce = false;
          const err = new Error("lidhja u pre PAS send, PARA ACK") as NodeJS.ErrnoException;
          err.code = "ECONNRESET";
          throw err;
        }
        return res;
      },
    };
    await outbox.drain(crashAfterSend);
    const mid = await getPool().query<{ status: string; attempts: number }>(
      "SELECT status, attempts FROM poc5_fiscal_outbox WHERE business_id = $1 AND coupon_uuid = $2",
      [POC5_IDS.biz, couponUuid],
    );
    expect(mid.rows[0]).toMatchObject({ status: "ne_pritje", attempts: 1 });

    // Resume: ri-dërgim i të njëjtit kupon → mock-u (si server idempotent) i njëjti tx.
    await new FiscalOutbox(getPool(), { backoffMs: 10, now: () => new Date(Date.now() + 300_000) }).drain(crashAfterSend);
    const done = await getPool().query<{ status: string }>(
      "SELECT status FROM poc5_fiscal_outbox WHERE business_id = $1 AND coupon_uuid = $2",
      [POC5_IDS.biz, couponUuid],
    );
    expect(done.rows[0]!.status).toBe("fiskalizuar");
    expect(inner.duplicateSends).toBe(1); // dublimi u pa NGA SERVERI — jo rresht i dytë lokal
    const rows = await getPool().query(
      "SELECT 1 FROM poc5_fiscal_outbox WHERE business_id = $1 AND coupon_uuid = $2",
      [POC5_IDS.biz, couponUuid],
    );
    expect(rows.rowCount).toBe(1); // një rresht i vetëm — zero dublim lokal
  });

  it("#6 restart me outbox jo-bosh: instancë e RE drain-on radhën e persistuar → resume", async () => {
    const couponUuid = randomUUID();
    const bytes = await encodePosCoupon(officialExampleCoupon({ couponId: "902" }));
    // "Procesi A" fut në radhë dhe VDES (instanca humbet).
    await new FiscalOutbox(getPool(), { backoffMs: 10 }).enqueue({
      couponUuid,
      couponType: "Sale",
      protoBytes: bytes,
      privateKey: keys.privateKey,
    });
    // "Procesi B" (restart): instancë krejt e re — radha vjen NGA DB, jo nga memoria.
    const fresh = new FiscalOutbox(getPool(), { backoffMs: 10, now: () => new Date(Date.now() + 60_000) });
    await fresh.drain(new MockAtkServer({ publicKey: keys.publicKey }));
    const st = await getPool().query<{ status: string }>(
      "SELECT status FROM poc5_fiscal_outbox WHERE business_id = $1 AND coupon_uuid = $2",
      [POC5_IDS.biz, couponUuid],
    );
    expect(st.rows[0]!.status).toBe("fiskalizuar");
  });

  it("#7 DB e outbox-it e paarritshme: gabim SPECIFIK lidhjeje; zero shkrime të pjesshme", async () => {
    const pgMod = await import("pg");
    const badPool = new pgMod.default.Pool({
      host: "localhost",
      port: 59985, // port pa Postgres — brenda test scope
      user: "idata",
      database: "idata",
      connectionTimeoutMillis: 1200,
    });
    const badOutbox = new FiscalOutbox(badPool, { backoffMs: 10 });
    const couponUuid = randomUUID();
    const bytes = await encodePosCoupon(officialExampleCoupon({ couponId: "903" }));
    let caught: unknown;
    try {
      await badOutbox.enqueue({ couponUuid, couponType: "Sale", protoBytes: bytes, privateKey: keys.privateKey });
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
    // Verifikim me pool-in e SHËNDOSHË: asnjë gjurmë e kuponit.
    const r = await getPool().query(
      "SELECT 1 FROM poc5_fiscal_outbox WHERE business_id = $1 AND coupon_uuid = $2",
      [POC5_IDS.biz, couponUuid],
    );
    expect(r.rowCount).toBe(0);
  });

  it("Tier 2 LIVE gating (rregulli B): opt-in i dyfishtë i çaktivizuar në CI — skip me arsye të qartë", () => {
    const gate = liveOptIn();
    expect(gate.enabled).toBe(false); // CI/lokalisht pa env → GJITHMONË skip
    expect(gate.reason.length).toBeGreaterThan(0);
  });
});

// ═══ TIER 2 — LIVE kundër test-serverit REAL të ATK-së (opt-in i DYFISHTË) ═══
// KURRË në CI. Ekzekutohet VETËM lokalisht pasi Blendi mbush .env.sef.local
// JASHTË chat-it dhe konfirmon "done" (rregulli C) + aprovimi i tij i veçantë.
const LIVE = liveOptIn();
if (!LIVE.enabled) {
  console.log(`[poc5] Tier 2 LIVE: SKIPPED — ${LIVE.reason} (opt-in i dyfishtë: RUN_SEF_LIVE=1 ∧ SEF_ENV=test ∧ URL=test-server)`);
}

describe.skipIf(!LIVE.enabled)("PoC-5 SEF — Tier 2 LIVE (test-serveri ATK; opt-in i dyfishtë)", () => {
  it("vend-mbajtës: onboarding PKI + PosCoupon real (zbatohet VETËM me aprovimin e veçantë të Blendit)", () => {
    // Trupi real i Tier 2 (onboarding /ca/verify→CSR→/ca/signcsr me çelës në
    // .sef-local/, dërgimi real me LiveAtkTransport, RETURN/CANCEL me/pa
    // ReferenceNo, dublimi i coupon_uuid, crash-pas-send) shtohet PAS
    // aprovimit të veçantë — HARD gate i kësaj faze.
    throw new Error("Tier 2 s'duhet të ekzekutohet pa aprovimin e veçantë të Blendit");
  });
});
