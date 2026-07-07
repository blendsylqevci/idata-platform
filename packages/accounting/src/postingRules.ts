import { Money } from "@idata/shared-types";
import {
  asJournalEntryId,
  asTaxBookEntryId,
  asPostingRuleVersionId,
} from "@idata/shared-types";
import type { PartnerId, PostingSide, TaxBook } from "@idata/shared-types";
import { ACCOUNTS } from "./accounts.js";
import type {
  SourceDocument,
  SourceDocumentLine,
  JournalLine,
  JournalEntry,
  TaxBookBucket,
  TaxBookEntry,
  SubledgerEntry,
  PostingResult,
} from "./domain.js";

/** Versioni i rregullave të postimit (Data Model §C `posting_rule` / `posting_rule_version_id`). */
export const POSTING_RULE_VERSION = asPostingRuleVersionId("prv-0.0.1");

function vatOf(net: Money, ratePct: string): Money {
  return net.mul(ratePct).mul("0.01").round(2);
}

function line(
  doc: SourceDocument,
  accountCode: string,
  side: PostingSide,
  amount: Money,
  partnerId?: PartnerId,
): JournalLine {
  return {
    businessId: doc.businessId,
    sourceDocumentId: doc.id,
    postingRuleVersionId: POSTING_RULE_VERSION,
    accountCode,
    side,
    amount,
    ...(partnerId ? { partnerId } : {}),
  };
}

/** Kutia e deklaratës TVSH — vetëm hartat e konfirmuara; të tjerat TBD (VAT_Book_Mapping_v0). */
function boxFor(book: TaxBook, ratePct: string, farmerFlat: boolean): string {
  if (farmerFlat) return "[51]";
  if (book === "sales" && ratePct === "18") return "[12]";
  if (book === "purchase" && ratePct === "18") return "[43]";
  if (book === "purchase" && ratePct === "8") return "[45]";
  return "[?]"; // TBD — plotësohet nga VAT_Book_Mapping_v0
}

interface Totals {
  readonly net: Money;
  readonly vat: Money;
  readonly buckets: TaxBookBucket[];
}

function accumulate(
  lines: readonly SourceDocumentLine[],
  currency: string,
  book: TaxBook,
  farmerFlat: boolean,
): Totals {
  let net = Money.zero(currency);
  let vat = Money.zero(currency);
  const byRate = new Map<string, { base: Money; vat: Money }>();
  for (const l of lines) {
    const v = vatOf(l.net, l.vatRatePct);
    net = net.add(l.net);
    vat = vat.add(v);
    const prev = byRate.get(l.vatRatePct) ?? {
      base: Money.zero(currency),
      vat: Money.zero(currency),
    };
    byRate.set(l.vatRatePct, { base: prev.base.add(l.net), vat: prev.vat.add(v) });
  }
  const buckets: TaxBookBucket[] = [];
  for (const [ratePct, agg] of byRate) {
    buckets.push({
      ratePct,
      base: agg.base,
      vat: agg.vat,
      declarationBox: boxFor(book, ratePct, farmerFlat),
    });
  }
  return { net, vat, buckets };
}

function makeEntry(doc: SourceDocument, lines: JournalLine[]): JournalEntry {
  return {
    id: asJournalEntryId(`je:${doc.id}`),
    businessId: doc.businessId,
    sourceDocumentId: doc.id,
    date: doc.date,
    lines,
  };
}

function makeTaxBook(doc: SourceDocument, book: TaxBook, buckets: TaxBookBucket[]): TaxBookEntry {
  return {
    id: asTaxBookEntryId(`tbe:${doc.id}`),
    businessId: doc.businessId,
    book,
    sourceDocumentId: doc.id,
    date: doc.date,
    buckets,
    ...(doc.partnerId ? { partnerId: doc.partnerId } : {}),
  };
}

function makeSubledger(doc: SourceDocument, side: PostingSide, amount: Money): SubledgerEntry[] {
  if (!doc.partnerId) return [];
  return [
    {
      businessId: doc.businessId,
      sourceDocumentId: doc.id,
      partnerId: doc.partnerId,
      side,
      amount,
    },
  ];
}

function postSale(doc: SourceDocument): PostingResult {
  const t = accumulate(doc.lines, doc.currency, "sales", false);
  const gross = t.net.add(t.vat);
  const lines: JournalLine[] = [
    line(doc, ACCOUNTS.AR, "debit", gross, doc.partnerId),
    line(doc, ACCOUNTS.REVENUE, "credit", t.net),
  ];
  if (!t.vat.isZero()) lines.push(line(doc, ACCOUNTS.VAT_OUTPUT, "credit", t.vat));
  return {
    journalEntry: makeEntry(doc, lines),
    taxBookEntry: makeTaxBook(doc, "sales", t.buckets),
    subledger: makeSubledger(doc, "debit", gross),
  };
}

function postPurchaseLike(doc: SourceDocument, farmerFlat: boolean): PostingResult {
  const t = accumulate(doc.lines, doc.currency, "purchase", farmerFlat);
  const gross = t.net.add(t.vat);
  const apAccount = farmerFlat ? ACCOUNTS.AP_FARMER : ACCOUNTS.AP;
  const vatAccount = farmerFlat ? ACCOUNTS.VAT_FARMER_FLAT_PENDING : ACCOUNTS.VAT_INPUT;
  const lines: JournalLine[] = [line(doc, ACCOUNTS.EXPENSE, "debit", t.net)];
  if (!t.vat.isZero()) lines.push(line(doc, vatAccount, "debit", t.vat));
  lines.push(line(doc, apAccount, "credit", gross, doc.partnerId));
  return {
    journalEntry: makeEntry(doc, lines),
    taxBookEntry: makeTaxBook(doc, "purchase", t.buckets),
    subledger: makeSubledger(doc, "credit", gross),
  };
}

function postReturn(doc: SourceDocument): PostingResult {
  // Kthim shitjeje (credit note): pasqyra e kundërt e shitjes.
  const t = accumulate(doc.lines, doc.currency, "sales", false);
  const gross = t.net.add(t.vat);
  const lines: JournalLine[] = [line(doc, ACCOUNTS.REVENUE, "debit", t.net)];
  if (!t.vat.isZero()) lines.push(line(doc, ACCOUNTS.VAT_OUTPUT, "debit", t.vat));
  lines.push(line(doc, ACCOUNTS.AR, "credit", gross, doc.partnerId));
  return {
    journalEntry: makeEntry(doc, lines),
    taxBookEntry: makeTaxBook(doc, "sales", t.buckets),
    subledger: makeSubledger(doc, "credit", gross),
  };
}

/** Postimi i një dokumenti → journal_entry (+tax_book_entry +subledger), in-memory. */
export function postDocument(doc: SourceDocument): PostingResult {
  switch (doc.eventType) {
    case "sale":
      return postSale(doc);
    case "purchase":
      return postPurchaseLike(doc, false);
    case "farmer_settlement":
      return postPurchaseLike(doc, true);
    case "return":
      return postReturn(doc);
    default:
      throw new Error(
        `postDocument: eventType '${doc.eventType}' s'është implementuar në Increment 1`,
      );
  }
}
