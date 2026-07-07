/** Llojet e ngjarjeve që postohen në GL (Data Model v2.2.1 §C `posting_rule.event_type`). */
export const POSTING_EVENT_TYPES = [
  "sale",
  "purchase",
  "farmer_settlement",
  "return",
  "payment",
  "payroll",
  "expense",
] as const;
export type PostingEventType = (typeof POSTING_EVENT_TYPES)[number];

/** Trajtimi tatimor (Data Model v2.2.1 §B `tax_code.tax_treatment`). */
export const TAX_TREATMENTS = [
  "output_sale",
  "purchase_input",
  "farmer_flat_rate_8",
  "exempt_no_vat",
] as const;
export type TaxTreatment = (typeof TAX_TREATMENTS)[number];

/** Libri i TVSH-së. */
export const TAX_BOOKS = ["sales", "purchase"] as const;
export type TaxBook = (typeof TAX_BOOKS)[number];

/** Normat standarde të TVSH-së në Kosovë (string ekzakt, jo float). */
export const VAT_RATE = {
  ZERO: "0",
  REDUCED_8: "8",
  STANDARD_18: "18",
} as const;
export type VatRatePct = (typeof VAT_RATE)[keyof typeof VAT_RATE];

/** Anët e regjistrimit kontabël. */
export const DEBIT = "debit";
export const CREDIT = "credit";
export type PostingSide = typeof DEBIT | typeof CREDIT;
