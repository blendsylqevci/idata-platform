import {
  Money,
  asBusinessId,
  asPartnerId,
  asSourceDocumentId,
  asIdempotencyKey,
} from "@idata/shared-types";
import type { SourceDocument } from "./domain.js";

// ⚠️ Vlera SINTETIKE — asnjë e dhënë reale klienti (PROJECT_RULES §3,§4).
const BIZ = asBusinessId("biz-demo-0001");
const CUSTOMER = asPartnerId("partner-customer-01");
const SUPPLIER = asPartnerId("partner-supplier-01");
const FARMER = asPartnerId("partner-farmer-01");

/** Shitje 100.00 @ 18% → gross 118.00. */
export const FX_SALE_18: SourceDocument = {
  id: asSourceDocumentId("doc-sale-18"),
  businessId: BIZ,
  eventType: "sale",
  date: "2027-01-15",
  currency: "EUR",
  partnerId: CUSTOMER,
  idempotencyKey: asIdempotencyKey("idem-sale-18"),
  lines: [{ net: Money.of("100.00"), vatRatePct: "18", taxTreatment: "output_sale" }],
};

/** Blerje 200.00 @ 18% → gross 236.00. */
export const FX_PURCHASE_18: SourceDocument = {
  id: asSourceDocumentId("doc-purchase-18"),
  businessId: BIZ,
  eventType: "purchase",
  date: "2027-01-16",
  currency: "EUR",
  partnerId: SUPPLIER,
  idempotencyKey: asIdempotencyKey("idem-purchase-18"),
  lines: [{ net: Money.of("200.00"), vatRatePct: "18", taxTreatment: "purchase_input" }],
};

/** Blerje mixed: 100.00 @18% + 50.00 @8% → dy buckets. */
export const FX_PURCHASE_MIXED: SourceDocument = {
  id: asSourceDocumentId("doc-purchase-mixed"),
  businessId: BIZ,
  eventType: "purchase",
  date: "2027-01-16",
  currency: "EUR",
  partnerId: SUPPLIER,
  idempotencyKey: asIdempotencyKey("idem-purchase-mixed"),
  lines: [
    { net: Money.of("100.00"), vatRatePct: "18", taxTreatment: "purchase_input" },
    { net: Money.of("50.00"), vatRatePct: "8", taxTreatment: "purchase_input" },
  ],
};

/** Shlyerje fermeri flat 8% (deductibility pending — ACC-006). */
export const FX_FARMER_FLAT: SourceDocument = {
  id: asSourceDocumentId("doc-farmer-flat"),
  businessId: BIZ,
  eventType: "farmer_settlement",
  date: "2027-01-31",
  currency: "EUR",
  partnerId: FARMER,
  idempotencyKey: asIdempotencyKey("idem-farmer-flat"),
  lines: [{ net: Money.of("500.00"), vatRatePct: "8", taxTreatment: "farmer_flat_rate_8" }],
};

/** Kthim shitjeje 20.00 @ 18% (credit note). */
export const FX_RETURN: SourceDocument = {
  id: asSourceDocumentId("doc-return-18"),
  businessId: BIZ,
  eventType: "return",
  date: "2027-01-20",
  currency: "EUR",
  partnerId: CUSTOMER,
  idempotencyKey: asIdempotencyKey("idem-return-18"),
  lines: [{ net: Money.of("20.00"), vatRatePct: "18", taxTreatment: "output_sale" }],
};

/** Të gjitha fixtures për iterim në teste. */
export const ALL_FIXTURES: readonly SourceDocument[] = [
  FX_SALE_18,
  FX_PURCHASE_18,
  FX_PURCHASE_MIXED,
  FX_FARMER_FLAT,
  FX_RETURN,
];
