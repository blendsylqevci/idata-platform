import { describe, expect, it } from "vitest";
import { sanitizeLocator, sortFindings, toJsonReport, toTextReport } from "../src/reporter.js";
import type { Finding, ScanResult } from "../src/types.js";

const f = (over: Partial<Finding>): Finding => ({
  profile: "DOC-CORE",
  profileStatus: "IMPLEMENTED_ADVISORY",
  severity: "WARNING",
  ruleId: "DC-TEST",
  category: "test",
  locator: "docs/x.md:1",
  ...over,
});

describe("reporter sanitization + determinism", () => {
  it("test 21: escape për pipe/backtick/CRLF dhe kontroll-karaktere në locator", () => {
    const dirty = "docs/a|b`c.md:9\r\n";
    const clean = sanitizeLocator(dirty);
    expect(clean).not.toContain("|");
    expect(clean).not.toContain("`");
    expect(clean).not.toContain("\r");
    expect(clean).not.toContain("\n");
    expect(clean).toContain("\\u007c");
    expect(clean).toContain("\\u0060");
  });

  it("test 12: reporter nuk nxjerr matched text — vetëm fushat e lejuara", () => {
    const syntheticSecretish = ["sk", "-", "SYNTHETICAAAABBBBCCCCDDDD"].join("");
    const result: ScanResult = {
      mode: "advisory",
      evaluations: [{ profile: "HISTORY-PRIV", profileStatus: "IMPLEMENTED_ADVISORY", status: "EVALUATED" }],
      findings: [f({ profile: "HISTORY-PRIV", ruleId: "HP-TOKEN-CLASS", locator: "docs/x.md:+3", commit: "abcd1234" })],
    };
    const json = toJsonReport(result);
    const text = toTextReport(result);
    expect(json).not.toContain(syntheticSecretish);
    expect(text).not.toContain(syntheticSecretish);
    const parsed = JSON.parse(json) as { findings: Array<Record<string, unknown>> };
    expect(Object.keys(parsed.findings[0] ?? {}).sort()).toEqual(
      ["category", "commit", "locator", "profile", "profileStatus", "ruleId", "severity"],
    );
  });

  it("test 13: sortFindings deterministik pavarësisht renditjes hyrëse", () => {
    const a = f({ ruleId: "DC-B", locator: "b.md:2" });
    const b = f({ ruleId: "DC-A", locator: "a.md:1" });
    expect(sortFindings([a, b])).toEqual(sortFindings([b, a]));
  });
});
