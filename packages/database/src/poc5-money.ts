/**
 * PoC-5 — konvertimet MONEY PER-FIELD në kufirin SEF (ANALIZA §3):
 *   - fushat e ITEM-ave (CouponItem.Price/Total)            → ×10000 (€0.0001)
 *   - totalet/pagesat/tax-groups (Total, Payment.Amount, …) → ×100   (cents)
 *
 * RREGULLI I KUFIRIT (PoC-2 ↔ PoC-5): kontabiliteti mbetet NUMERIC (string
 * decimal); konvertimi ndodh VETËM këtu. Asnjë float/JS number për para —
 * hyrja është string decimal, dalja bigint. Përzierja e shkallëve DËSHTON.
 */

const DEC_RE = /^\d+(\.\d+)?$/;

function parse(numeric: string, maxDp: number, label: string): bigint {
  if (typeof numeric !== "string") {
    throw new Error(`${label}: pritet NUMERIC si string decimal (kurrë number/float)`);
  }
  if (!DEC_RE.test(numeric)) {
    throw new Error(`${label}: format i pavlefshëm ose negativ: '${numeric}' (kuponat s'kanë vlera negative)`);
  }
  const [intPart, fracPart = ""] = numeric.split(".") as [string, string?];
  if ((fracPart ?? "").length > maxDp) {
    throw new Error(
      `${label}: '${numeric}' ka ${(fracPart ?? "").length} shifra dhjetore — max ${maxDp} për këtë shkallë (përzierje shkallësh?)`,
    );
  }
  const frac = (fracPart ?? "").padEnd(maxDp, "0");
  return BigInt(intPart + frac);
}

/** €→ item-scale ×10000 (CouponItem.Price / CouponItem.Total). Max 4 dp. */
export function euroToItemScale(numeric: string): bigint {
  return parse(numeric, 4, "euroToItemScale(×10000)");
}

/** €→ cents ×100 (coupon Total / Payment.Amount / TaxGroup.* / TotalTax / TotalNoTax / TotalDiscount). Max 2 dp. */
export function euroToCents(numeric: string): bigint {
  return parse(numeric, 2, "euroToCents(×100)");
}

function format(v: bigint, dp: number, label: string): string {
  if (typeof v !== "bigint" || v < 0n) throw new Error(`${label}: pritet bigint jo-negativ`);
  const s = v.toString().padStart(dp + 1, "0");
  return `${s.slice(0, -dp)}.${s.slice(-dp)}`;
}

/** item-scale ×10000 → € (string decimal me 4 dp). */
export function itemScaleToEuro(v: bigint): string {
  return format(v, 4, "itemScaleToEuro");
}

/** cents ×100 → € (string decimal me 2 dp). */
export function centsToEuro(v: bigint): string {
  return format(v, 2, "centsToEuro");
}

/**
 * Unix SEKONDA për PosCoupon.Time — mbrojtje nga bug-u klasik JS
 * (Date.now() = MILISEKONDA). Pranon Date ose epoch-ms; kthen sekonda.
 */
export function toUnixSeconds(input: Date | number): number {
  const ms = input instanceof Date ? input.getTime() : input;
  if (!Number.isFinite(ms)) throw new Error("toUnixSeconds: input i pavlefshëm");
  return Math.floor(ms / 1000);
}
