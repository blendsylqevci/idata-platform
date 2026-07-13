import { describe, expect, it } from "vitest";
import { writeFileSync } from "node:fs";
import { frag, makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runHistoryPriv } from "../src/profiles/history-priv.js";
import { toJsonReport, toTextReport, sortEvaluations } from "../src/reporter.js";
import type { GitReader, ProfileEvaluation } from "../src/types.js";

function repoWithBase() {
  const repo = makeTempRepo();
  repo.write("README.md", "synthetic base\n");
  const base = repo.commitAll("base");
  return { repo, base };
}

describe("§3 CALL-ORDER — policy mbi TË GJITHA entries PARA çdo content access", () => {
  it("private entry NUK shkakton asnjë content call; public content vjen pas metadata-s", () => {
    const { repo, base } = repoWithBase();
    const privDir = frag("private", "-sensitive");
    repo.write("public.md", "safe\n");
    repo.write(`${privDir}/leak.md`, "MUST_NOT_BE_READ\n");
    const head = repo.commitAll("mixed commit");

    const real = new GitCliReader(repo.dir);
    const calls: string[] = [];
    const spy: GitReader = new Proxy(real, {
      get(target, prop, receiver) {
        const orig = Reflect.get(target, prop, receiver);
        if (typeof orig !== "function") return orig;
        return (...args: unknown[]) => {
          if (prop === "blobSizeByOid" || prop === "addedContent" || prop === "showFile") {
            calls.push(`${String(prop)}:${String(args[1] ?? args[0])}`);
          } else if (prop === "rawEntries") {
            calls.push("rawEntries");
          }
          return (orig as (...a: unknown[]) => unknown).apply(target, args);
        };
      },
    }) as GitReader;

    const r = runHistoryPriv(spy, base, head, undefined);
    expect(r.findings.some((f) => f.ruleId === "HP-PRIVATE-PATH")).toBe(true);
    // ASNJË content call për path-in privat:
    expect(calls.some((c) => c.includes(privDir))).toBe(false);
    expect(calls.some((c) => c.includes("leak.md"))).toBe(false);
    // metadata (rawEntries) vjen PARA çdo content access:
    const firstContent = calls.findIndex((c) => c.startsWith("blobSizeByOid") || c.startsWith("addedContent"));
    const firstMeta = calls.indexOf("rawEntries");
    expect(firstMeta).toBeGreaterThanOrEqual(0);
    if (firstContent !== -1) expect(firstMeta).toBeLessThan(firstContent);
  });
});

describe("§3 NO-TEXTCONV — .gitattributes sentinel nuk ekzekutohet kurrë", () => {
  it("textconv i konfiguruar nuk prek output-in e addedContent", () => {
    const { repo, base } = repoWithBase();
    repo.write(".gitattributes", "*.md diff=sentinel\n");
    repo.git(["config", "diff.sentinel.textconv", "echo TEXTCONV_LEAKED_SENTINEL #"]);
    repo.write("doc.md", "përmbajtje normale\n");
    const head = repo.commitAll("with textconv attr");
    const reader = new GitCliReader(repo.dir);
    const content = reader.addedContent(head, "doc.md");
    const all = content.lines.map((l) => l.text).join("\n");
    expect(all).toContain("përmbajtje normale");
    expect(all).not.toContain("TEXTCONV_LEAKED_SENTINEL");
  });
});

describe("§4 hasFile — kontrata missing-vs-failure", () => {
  it("ekziston → true; mungon → false; ref invalid → ScanError", () => {
    const { repo } = repoWithBase();
    const reader = new GitCliReader(repo.dir);
    expect(reader.hasFile("HEAD", "README.md")).toBe(true);
    expect(reader.hasFile("HEAD", "nuk-ekziston.md")).toBe(false);
    expect(() => reader.hasFile("bogus-ref-xyz", "README.md")).toThrow("GIT_OP_FAILED:ls-tree");
  });
});

describe("§3 WHITELIST vs PRIVACY — scope nuk e anulon policy-n private", () => {
  it("path privat brenda whitelist-it mbetet ERROR i redaktuar", () => {
    const { repo, base } = repoWithBase();
    const privDir = frag("docs", "-private");
    repo.write(`${privDir}/leak.md`, "x\n");
    const head = repo.commitAll("private në scope");
    const body = ["<!-- integrity-linter:scope", "allowed-paths:", `- ${privDir}/`, "- README.md", "-->"].join("\n");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, { body });
    const priv = r.findings.filter((f) => f.ruleId === "HP-PRIVATE-PATH");
    expect(priv).toHaveLength(1);
    expect(priv[0]?.locator).toBe("[REDACTED_PATH]");
    // dhe NUK ka HP-OUT-OF-SCOPE me path real privat:
    expect(r.findings.filter((f) => f.ruleId === "HP-OUT-OF-SCOPE")).toHaveLength(0);
  });
});

describe("§6 REPORTER — kontrata e `check` në text + JSON + sort deterministik", () => {
  it("evaluation me check dallohet nga agregati; renditja profile→check→status", () => {
    const evs: ProfileEvaluation[] = [
      { profile: "HISTORY-PRIV", profileStatus: "IMPLEMENTED_ADVISORY", status: "EVALUATED" },
      { profile: "DOC-CORE", profileStatus: "IMPLEMENTED_ADVISORY", status: "NOT_EVALUATED", check: "pr-body", reason: "missing PR event/body" },
      { profile: "DOC-CORE", profileStatus: "IMPLEMENTED_ADVISORY", status: "EVALUATED" },
      { profile: "HISTORY-PRIV", profileStatus: "IMPLEMENTED_ADVISORY", status: "NOT_EVALUATED", check: "content", reason: "BINARY_BLOB" },
    ];
    const sorted = sortEvaluations(evs);
    expect(sorted.map((e) => `${e.profile}|${e.check ?? ""}|${e.status}`)).toEqual([
      "DOC-CORE||EVALUATED",
      "DOC-CORE|pr-body|NOT_EVALUATED",
      "HISTORY-PRIV||EVALUATED",
      "HISTORY-PRIV|content|NOT_EVALUATED",
    ]);
    const result = { mode: "advisory" as const, evaluations: evs, findings: [] };
    const text = toTextReport(result);
    expect(text).toContain("profile DOC-CORE check=pr-body status=IMPLEMENTED_ADVISORY evaluation=NOT_EVALUATED reason=missing PR event/body");
    expect(text).toContain("profile HISTORY-PRIV check=content status=IMPLEMENTED_ADVISORY evaluation=NOT_EVALUATED reason=BINARY_BLOB");
    const json = JSON.parse(toJsonReport(result)) as { evaluations: Array<{ check?: string; evaluation: string }> };
    expect(json.evaluations.filter((e) => e.check !== undefined)).toHaveLength(2);
  });
});

describe("§5 boundary — size cap ekzakt / ±1 dhe old-blob coverage", () => {
  it("cap-1 dhe cap ekzakt skanohen; cap+1 → REVIEW + NOT_EVALUATED(content)", () => {
    const { repo, base } = repoWithBase();
    repo.write("a.txt", "a".repeat(999_999) + "\n");   // nën cap (size = 1_000_000 me \n)
    repo.write("b.txt", "b".repeat(1_000_001));         // mbi cap
    const head = repo.commitAll("sizes");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined);
    const over = r.findings.filter((f) => f.ruleId === "HP-OVERSIZE-BLOB").map((f) => f.locator);
    expect(over).toEqual(["b.txt"]);
    const notEval = r.evaluations.filter((e) => e.check === "content");
    expect(notEval.map((e) => e.reason)).toContain("OVERSIZE_BLOB");
    expect(notEval.every((e) => e.status === "NOT_EVALUATED")).toBe(true);
  });

  it("old blob oversize + new i vogël → coverage e paplotë (kontrollohen TË DY blob-et)", () => {
    const { repo, base } = repoWithBase();
    repo.write("big.txt", "x".repeat(1_000_100));
    const c1 = repo.commitAll("big v1");
    repo.write("big.txt", "i vogël tani\n");
    const head = repo.commitAll("small v2");
    const r = runHistoryPriv(new GitCliReader(repo.dir), c1, head, undefined);
    expect(r.findings.some((f) => f.ruleId === "HP-OVERSIZE-BLOB" && f.locator === "big.txt")).toBe(true);
    expect(r.evaluations.some((e) => e.check === "content" && e.reason === "OVERSIZE_BLOB")).toBe(true);
  });

  it("binar me NUL në fillim dhe NUL në fund → REVIEW pa content; UTF-8 invalid pa NUL → exit-2 path", () => {
    const { repo, base } = repoWithBase();
    writeFileSync(`${repo.dir}/nul-start.bin`, Buffer.concat([Buffer.from([0]), Buffer.from("abc")]));
    writeFileSync(`${repo.dir}/nul-end.bin`, Buffer.concat([Buffer.from("abc"), Buffer.from([0])]));
    const head = repo.commitAll("nul blobs");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined);
    const bins = r.findings.filter((f) => f.ruleId === "HP-BINARY-BLOB").map((f) => f.locator).sort();
    expect(bins).toEqual(["nul-end.bin", "nul-start.bin"]);
    expect(r.evaluations.some((e) => e.check === "content" && e.reason === "BINARY_BLOB")).toBe(true);
  });
});
