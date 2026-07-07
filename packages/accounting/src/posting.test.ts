import { describe, it, expect } from "vitest";
import { Money } from "@idata/shared-types";
import type { PostingSide } from "@idata/shared-types";
import type { JournalEntry } from "./domain.js";
import { PostingSimulator } from "./simulator.js";
import { postDocument, POSTING_RULE_VERSION } from "./postingRules.js";
import { ACCOUNTS } from "./accounts.js";
import { ALL_FIXTURES, FX_SALE_18, FX_PURCHASE_MIXED, FX_FARMER_FLAT } from "./fixtures.js";

function total(entry: JournalEntry, side: PostingSide): Money {
  return entry.lines
    .filter((l) => l.side === side)
    .reduce((a, l) => a.add(l.amount), Money.zero("EUR"));
}

describe("Posting simulator — invariantet (runbook §5)", () => {
  it("debit == credit për ÇDO fixture", () => {
    for (const doc of ALL_FIXTURES) {
      const r = postDocument(doc);
      expect(total(r.journalEntry, "debit").equals(total(r.journalEntry, "credit"))).toBe(true);
    }
  });

  it("çdo rresht ka business_id + source_document_id + posting_rule_version_id", () => {
    for (const doc of ALL_FIXTURES) {
      const r = postDocument(doc);
      expect(r.journalEntry.lines.length).toBeGreaterThan(0);
      for (const l of r.journalEntry.lines) {
        expect(l.businessId).toBeTruthy();
        expect(l.sourceDocumentId).toBe(doc.id);
        expect(l.postingRuleVersionId).toBe(POSTING_RULE_VERSION);
      }
    }
  });

  it("shitje/blerje → tax_book_entry me buckets që barazojnë gross-in", () => {
    for (const doc of ALL_FIXTURES) {
      const r = postDocument(doc);
      expect(r.taxBookEntry).toBeDefined();
      const tbe = r.taxBookEntry;
      if (!tbe) throw new Error("pritej tax_book_entry");
      const bucketBase = tbe.buckets.reduce((a, b) => a.add(b.base), Money.zero("EUR"));
      const bucketVat = tbe.buckets.reduce((a, b) => a.add(b.vat), Money.zero("EUR"));
      const gross = total(r.journalEntry, "debit");
      expect(bucketBase.add(bucketVat).equals(gross)).toBe(true);
    }
  });

  it("no-float: shitje 100.00 @18% → gross 118.00 ekzakt", () => {
    const r = postDocument(FX_SALE_18);
    expect(total(r.journalEntry, "debit").toString()).toBe("118.00");
    expect(total(r.journalEntry, "credit").toString()).toBe("118.00");
  });

  it("blerje mixed 18%+8% → dy buckets", () => {
    const r = postDocument(FX_PURCHASE_MIXED);
    expect(r.taxBookEntry?.buckets.length).toBe(2);
  });

  it("shlyerje fermeri flat 8% → TVSH në llogari PENDING (deductibility ACC-006)", () => {
    const r = postDocument(FX_FARMER_FLAT);
    const vatLine = r.journalEntry.lines.find(
      (l) => l.accountCode === ACCOUNTS.VAT_FARMER_FLAT_PENDING,
    );
    expect(vatLine).toBeDefined();
    expect(vatLine?.amount.toString()).toBe("40.00"); // 500 * 8%
  });

  it("idempotency: i njëjti idempotency_key s'krijon dyfish", () => {
    const sim = new PostingSimulator();
    const a = sim.post(FX_SALE_18);
    const b = sim.post(FX_SALE_18);
    expect(sim.size).toBe(1);
    expect(a).toBe(b);
  });

  it("eventType i pambështetur → hedh gabim eksplicit", () => {
    expect(() =>
      postDocument({ ...FX_SALE_18, eventType: "payroll" }),
    ).toThrow(/Increment 1/);
  });
});
