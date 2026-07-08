import path from "node:path";
import { fileURLToPath } from "node:url";
import protobuf from "protobufjs";

/**
 * PoC-5 — encode/decode i PosCoupon/CitizenCoupon me `models.proto` ZYRTAR
 * të vendor-uar VERBATIM (packages/database/proto/sef-models.proto, byte-identik
 * me pos-csharp @ a4060ef — checksum i pin-uar në test si drift-check).
 * int64/uint64 vendosen si STRING (protobufjs→Long) — asnjë float për para.
 */

const PROTO_PATH = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
  "proto",
  "sef-models.proto",
);

export const VENDORED_PROTO_PATH = PROTO_PATH;

let rootPromise: Promise<protobuf.Root> | undefined;
function root(): Promise<protobuf.Root> {
  rootPromise ??= protobuf.load(PROTO_PATH);
  return rootPromise;
}

export const COUPON_TYPE = { Sale: 1, Cancel: 2, Return: 3 } as const;
export const PAYMENT_TYPE = { Cash: 1, CreditCard: 2, Voucher: 3 } as const;

export interface CouponItemInput {
  name: string;
  priceX10000: bigint; // ×10000 — VETËM nga euroToItemScale
  unit: string;
  quantity: number; // sasi (float në proto — JO para)
  totalX10000: bigint; // ×10000
  taxRate: "A" | "C" | "D" | "E";
  type?: string; // shembujt zyrtarë: "TT"
}

export interface PaymentInput {
  type: number; // PAYMENT_TYPE
  amountCents: bigint; // ×100
}

export interface TaxGroupInput {
  taxRate: "A" | "C" | "D" | "E";
  totalForTaxCents: bigint;
  totalTaxCents: bigint;
}

export interface PosCouponInput {
  businessId: string; // uint64 si string (NUI sintetik në teste)
  couponId: string;
  branchId: string;
  location: string;
  operatorId: string;
  posId: string;
  applicationId: string;
  verificationNo: string;
  type: number; // COUPON_TYPE
  timeUnixSeconds: number; // SEKONDA (jo ms!)
  items: CouponItemInput[];
  payments: PaymentInput[];
  totalCents: bigint;
  taxGroups: TaxGroupInput[];
  totalTaxCents: bigint;
  totalNoTaxCents: bigint;
  referenceNo: string; // "0" për Sale; CouponId origjinal për Cancel/Return
  totalDiscountCents: bigint;
}

function toPlain(c: PosCouponInput): Record<string, unknown> {
  return {
    BusinessId: c.businessId,
    CouponId: c.couponId,
    BranchId: c.branchId,
    Location: c.location,
    OperatorId: c.operatorId,
    PosId: c.posId,
    ApplicationId: c.applicationId,
    VerificationNo: c.verificationNo,
    Type: c.type,
    Time: c.timeUnixSeconds,
    Items: c.items.map((i) => ({
      Name: i.name,
      Price: i.priceX10000.toString(),
      Unit: i.unit,
      Quantity: i.quantity,
      Total: i.totalX10000.toString(),
      TaxRate: i.taxRate,
      Type: i.type ?? "TT",
    })),
    Payments: c.payments.map((p) => ({ Type: p.type, Amount: p.amountCents.toString() })),
    Total: c.totalCents.toString(),
    TaxGroups: c.taxGroups.map((t) => ({
      TaxRate: t.taxRate,
      TotalForTax: t.totalForTaxCents.toString(),
      TotalTax: t.totalTaxCents.toString(),
    })),
    TotalTax: c.totalTaxCents.toString(),
    TotalNoTax: c.totalNoTaxCents.toString(),
    ReferenceNo: c.referenceNo,
    TransactionNo: "0",
    TotalDiscount: c.totalDiscountCents.toString(),
  };
}

/** Serializon PosCoupon → bytes proto (fushat sipas models.proto — anti-FAILED VERIFICATION).
 *  Shënim: `verify()` i protobufjs s'pranon string për int64 — konvertimi string→Long
 *  bëhet nga `fromObject`; validimi real = round-trip testet + verifikimi i serverit. */
export async function encodePosCoupon(c: PosCouponInput): Promise<Uint8Array> {
  const t = (await root()).lookupType("atk.PosCoupon");
  return t.encode(t.fromObject(toPlain(c))).finish();
}

/** CitizenCoupon NGA PosCoupon-i — detajet DUHET të përputhen (ANALIZA §2). */
export async function encodeCitizenFromPos(c: PosCouponInput): Promise<Uint8Array> {
  const t = (await root()).lookupType("atk.CitizenCoupon");
  const plain = {
    BusinessId: c.businessId,
    CouponId: c.couponId,
    BranchId: c.branchId,
    PosId: c.posId,
    VerificationNo: c.verificationNo,
    Type: c.type,
    Time: c.timeUnixSeconds,
    Total: c.totalCents.toString(),
    TaxGroups: c.taxGroups.map((g) => ({
      TaxRate: g.taxRate,
      TotalForTax: g.totalForTaxCents.toString(),
      TotalTax: g.totalTaxCents.toString(),
    })),
    TotalTax: c.totalTaxCents.toString(),
    TotalNoTax: c.totalNoTaxCents.toString(),
  };
  return t.encode(t.fromObject(plain)).finish();
}

/** Decode → objekt i thjeshtë me int64 si STRING (për asserts pa float). */
export async function decodePosCoupon(bytes: Uint8Array): Promise<Record<string, unknown>> {
  const t = (await root()).lookupType("atk.PosCoupon");
  return t.toObject(t.decode(bytes), { longs: String, enums: Number }) as Record<string, unknown>;
}

export async function decodeCitizenCoupon(bytes: Uint8Array): Promise<Record<string, unknown>> {
  const t = (await root()).lookupType("atk.CitizenCoupon");
  return t.toObject(t.decode(bytes), { longs: String, enums: Number }) as Record<string, unknown>;
}
