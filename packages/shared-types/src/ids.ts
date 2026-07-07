/**
 * Branded ID types — parandalojnë ngatërrimin e një `BusinessId` me një `PartnerId`
 * në kohën e kompilimit, edhe pse të dyja janë `string` (UUID) në runtime.
 */
declare const __brand: unique symbol;
type Brand<T, B extends string> = T & { readonly [__brand]: B };

export type BusinessId = Brand<string, "BusinessId">;
export type BusinessUnitId = Brand<string, "BusinessUnitId">;
export type PartnerId = Brand<string, "PartnerId">;
export type AccountId = Brand<string, "AccountId">;
export type JournalEntryId = Brand<string, "JournalEntryId">;
export type TaxBookEntryId = Brand<string, "TaxBookEntryId">;
export type SourceDocumentId = Brand<string, "SourceDocumentId">;
export type PostingRuleVersionId = Brand<string, "PostingRuleVersionId">;
export type IdempotencyKey = Brand<string, "IdempotencyKey">;

export const asBusinessId = (v: string): BusinessId => v as BusinessId;
export const asBusinessUnitId = (v: string): BusinessUnitId => v as BusinessUnitId;
export const asPartnerId = (v: string): PartnerId => v as PartnerId;
export const asAccountId = (v: string): AccountId => v as AccountId;
export const asJournalEntryId = (v: string): JournalEntryId => v as JournalEntryId;
export const asTaxBookEntryId = (v: string): TaxBookEntryId => v as TaxBookEntryId;
export const asSourceDocumentId = (v: string): SourceDocumentId => v as SourceDocumentId;
export const asPostingRuleVersionId = (v: string): PostingRuleVersionId =>
  v as PostingRuleVersionId;
export const asIdempotencyKey = (v: string): IdempotencyKey => v as IdempotencyKey;
