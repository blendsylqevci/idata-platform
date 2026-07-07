import type { SourceDocument, PostingResult } from "./domain.js";
import { postDocument } from "./postingRules.js";

/**
 * Simulator postimi in-memory (pa DB). Zbaton invariantin e **idempotency-t**:
 * i njëjti `idempotency_key` → i njëjti rezultat, pa krijuar postim të dyfishtë.
 */
export class PostingSimulator {
  private readonly seen = new Map<string, PostingResult>();
  private readonly journal: PostingResult[] = [];

  post(doc: SourceDocument): PostingResult {
    const key = doc.idempotencyKey as string;
    const existing = this.seen.get(key);
    if (existing) return existing;
    const result = postDocument(doc);
    this.seen.set(key, result);
    this.journal.push(result);
    return result;
  }

  get entries(): readonly PostingResult[] {
    return this.journal;
  }

  get size(): number {
    return this.journal.length;
  }
}
