import { describe, expect, it } from "vitest";
import { writeFileSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { frag, makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runHistoryPriv } from "../src/profiles/history-priv.js";
import { toJsonReport, toTextReport, sortEvaluations } from "../src/reporter.js";
import type { GitReader, ProfileEvaluation } from "../src/types.js";
import { ScanError } from "../src/types.js";

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

describe("§2 boundary — content i pa-skanueshëm = exit 2 (fail-closed, Pass III)", () => {
  it("cap−1 dhe cap EKZAKT skanohen; cap+1 → ScanError OVERSIZE_BLOB pa path në error", () => {
    const { repo, base } = repoWithBase();
    repo.write("a.txt", "a".repeat(999_999) + "\n"); // = 1_000_000 me \n (cap ekzakt)
    const c1 = repo.commitAll("cap exact");
    const r1 = runHistoryPriv(new GitCliReader(repo.dir), base, c1, undefined);
    expect(r1.findings.filter((f) => f.ruleId.includes("OVERSIZE"))).toHaveLength(0);
    expect(r1.evaluations.some((e) => e.status === "EVALUATED" && e.check === undefined)).toBe(true);

    repo.write("b.txt", "b".repeat(1_000_001)); // cap+1
    const c2 = repo.commitAll("over cap");
    try {
      runHistoryPriv(new GitCliReader(repo.dir), c1, c2, undefined);
      expect.unreachable("duhet exit-2 path");
    } catch (err) {
      expect((err as { code?: string }).code).toBe("INCOMPLETE_SCAN:OVERSIZE_BLOB");
      expect((err as Error).message).not.toContain("b.txt");
    }
  });

  it("old blob oversize + new i vogël → ScanError (kontrollohen TË DY blob-et)", () => {
    const { repo } = repoWithBase();
    repo.write("big.txt", "x".repeat(1_000_100));
    const c1 = repo.commitAll("big v1");
    repo.write("big.txt", "i vogël tani\n");
    const c2 = repo.commitAll("small v2");
    expect(() => runHistoryPriv(new GitCliReader(repo.dir), c1, c2, undefined))
      .toThrow("INCOMPLETE_SCAN:OVERSIZE_BLOB");
  });

  it("binar (NUL fillim/fund) → ScanError BINARY_BLOB pa OID; UTF-8 invalid → ScanError", () => {
    const { repo, base } = repoWithBase();
    writeFileSync(`${repo.dir}/nul-start.bin`, Buffer.concat([Buffer.from([0]), Buffer.from("abc")]));
    const c1 = repo.commitAll("nul start");
    try {
      runHistoryPriv(new GitCliReader(repo.dir), base, c1, undefined);
      expect.unreachable("duhet exit-2 path");
    } catch (err) {
      expect((err as { code?: string }).code).toBe("INCOMPLETE_SCAN:BINARY_BLOB");
      expect((err as Error).message).not.toMatch(/[0-9a-f]{40}/);
    }
    writeFileSync(`${repo.dir}/nul-end.bin`, Buffer.concat([Buffer.from("abc"), Buffer.from([0])]));
    const c2 = repo.commitAll("nul end");
    expect(() => runHistoryPriv(new GitCliReader(repo.dir), c1, c2, undefined))
      .toThrow("INCOMPLETE_SCAN:BINARY_BLOB");

    const repo2 = makeTempRepo();
    repo2.write("README.md", "base\n");
    const b2 = repo2.commitAll("base");
    writeFileSync(`${repo2.dir}/bad.txt`, Buffer.from([0x61, 0xc3, 0x28, 0x62, 0x0a]));
    const h2 = repo2.commitAll("bad encoding");
    expect(() => runHistoryPriv(new GitCliReader(repo2.dir), b2, h2, undefined)).toThrow(ScanError);
  });
});

describe("§3 PRIVATE RELOCATION — OID nëpër TËRË range-in", () => {
  it("delete privat në A → add publik me të NJËJTIN OID në C → RELOCATION i redaktuar te C", () => {
    const { repo } = repoWithBase();
    const privDir = frag("private", "-sensitive");
    repo.write(`${privDir}/leak.md`, "synthetic-priv-content\n");
    const c1 = repo.commitAll("private add");
    execFileSync("git", ["rm", "-rq", privDir], { cwd: repo.dir });
    const c2 = repo.commitAll("private delete only");
    repo.write("public-copy.md", "synthetic-priv-content\n"); // i njëjti content → i njëjti OID
    const c3 = repo.commitAll("public re-add later");
    const r = runHistoryPriv(new GitCliReader(repo.dir), c1, c3, undefined);
    const reloc = r.findings.filter((f) => f.ruleId === "HP-PRIVATE-RELOCATION");
    expect(reloc).toHaveLength(1);
    expect(reloc[0]?.commit).toBe(c3.slice(0, 8));
    expect(reloc[0]?.locator).toBe("[REDACTED_PATH]");
    const json = toJsonReport({ mode: "advisory", evaluations: r.evaluations, findings: r.findings });
    expect(json).not.toContain(privDir);
    expect(json).not.toMatch(/[0-9a-f]{40}/);
  });

  it("same-commit relocation kapet; OID i palidhur → clean; delete-only → clean", () => {
    const { repo } = repoWithBase();
    const privDir = frag("docs", "-private");
    repo.write(`${privDir}/x.md`, "relocate-me\n");
    const c1 = repo.commitAll("private add");
    execFileSync("git", ["mv", `${privDir}/x.md`, "public-x.md"], { cwd: repo.dir });
    const c2 = repo.commitAll("same-commit relocation");
    const r2 = runHistoryPriv(new GitCliReader(repo.dir), c1, c2, undefined);
    expect(r2.findings.filter((f) => f.ruleId === "HP-PRIVATE-RELOCATION")).toHaveLength(1);

    const repo2 = makeTempRepo();
    repo2.write("README.md", "base\n");
    repo2.write(`${frag("private", "-sensitive")}/y.md`, "content-A\n");
    const d1 = repo2.commitAll("priv add");
    execFileSync("git", ["rm", "-rq", frag("private", "-sensitive")], { cwd: repo2.dir });
    repo2.write("unrelated.md", "content-B krejt tjetër\n");
    const d2 = repo2.commitAll("delete privat + add i palidhur");
    const rU = runHistoryPriv(new GitCliReader(repo2.dir), d1, d2, undefined);
    expect(rU.findings.filter((f) => f.ruleId === "HP-PRIVATE-RELOCATION")).toHaveLength(0);

    const repo3 = makeTempRepo();
    repo3.write("README.md", "base\n");
    repo3.write(`${frag("evidence", "-private", "-local")}/z.md`, "cleanup\n");
    const e1 = repo3.commitAll("priv");
    execFileSync("git", ["rm", "-rq", frag("evidence", "-private", "-local")], { cwd: repo3.dir });
    const e2 = repo3.commitAll("delete-only cleanup");
    const rC = runHistoryPriv(new GitCliReader(repo3.dir), e1, e2, undefined);
    expect(rC.findings).toHaveLength(0);
  });
});
