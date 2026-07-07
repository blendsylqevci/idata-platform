import type {
  Money,
  BusinessId,
  PartnerId,
  SourceDocumentId,
  JournalEntryId,
  TaxBookEntryId,
  PostingRuleVersionId,
  IdempotencyKey,
  PostingEventType,
  PostingSide,
  TaxBook,
  TaxTreatment,
} from "@idata/shared-types";

/** Një rresht i dokumentit burimor (input i simulatorit). */
export interface SourceDocumentLine {
  readonly description?: string;
  readonly net: Money; // bazë pa TVSH
  readonly vatRatePct: string; // "0" | "8" | "18"
  readonly taxTreatment: TaxTreatment;
}

/** Dokumenti burimor (faturë/shlyerje/kthim) — fixture in-memory, jo DB. */
export interface SourceDocument {
  readonly id: SourceDocumentId;
  readonly businessId: BusinessId;
  readonly eventType: PostingEventType;
  readonly date: string; // ISO
  readonly currency: string;
  readonly partnerId?: PartnerId;
  readonly idempotencyKey: IdempotencyKey;
  readonly lines: readonly SourceDocumentLine[];
}

/** Rreshti i regjistrimit në GL. Çdo rresht bart invariantet e detyrueshme. */
export interface JournalLine {
  readonly businessId: BusinessId;
  readonly sourceDocumentId: SourceDocumentId;
  readonly postingRuleVersionId: PostingRuleVersionId;
  readonly accountCode: string;
  readonly side: PostingSide;
  readonly amount: Money;
  readonly partnerId?: PartnerId;
}

export interface JournalEntry {
  readonly id: JournalEntryId;
  readonly businessId: BusinessId;
  readonly sourceDocumentId: SourceDocumentId;
  readonly date: string;
  readonly lines: readonly JournalLine[];
}

export interface TaxBookBucket {
  readonly ratePct: string;
  readonly base: Money;
  readonly vat: Money;
  readonly declarationBox: string;
}

export interface TaxBookEntry {
  readonly id: TaxBookEntryId;
  readonly businessId: BusinessId;
  readonly book: TaxBook;
  readonly sourceDocumentId: SourceDocumentId;
  readonly partnerId?: PartnerId;
  readonly date: string;
  readonly buckets: readonly TaxBookBucket[];
}

export interface SubledgerEntry {
  readonly businessId: BusinessId;
  readonly sourceDocumentId: SourceDocumentId;
  readonly partnerId: PartnerId;
  readonly side: PostingSide;
  readonly amount: Money;
}

export interface PostingResult {
  readonly journalEntry: JournalEntry;
  readonly taxBookEntry?: TaxBookEntry;
  readonly subledger: readonly SubledgerEntry[];
}
