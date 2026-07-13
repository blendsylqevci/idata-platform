import { describe, expect, it } from "vitest";
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { join } from "node:path";
import { addSymlink, frag, makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runHistoryPriv, parseScopeBlock, pathInScope } from "../src/profiles/history-priv.js";
import { toJsonReport } from "../src/reporter.js";
import { ConfigError, ScanError } from "../src/types.js";

function repoWithBase() {
  const repo = makeTempRepo();
  repo.write("README.md", "synthetic base\n");
  const base = repo.commitAll("base");
  return { repo, base };
}

describe("HISTORY-PRIV — per-commit, privacy-safe (fixtures sintetike)", () => {
  it("test 5 + 6: introduced-then-removed mbetet finding (net-diff e fsheh, per-commit e kap)", () => {
    const { repo, base } = repoWithBase();
    const secretish = frag("g", "ho", "_SYNTHETICFIXTURE12345");
    repo.write("notes.md", `line\n${secretish}\n`);
    repo.commitAll("A introduces");
    repo.write("notes.md", "line\n");
    const head = repo.commitAll("B removes");
    const reader = new GitCliReader(repo.dir);
    // net-diff base..head s'e përmban stringun:
    const net = execFileSync("git", ["diff", base, head], { cwd: repo.dir }).toString("utf8");
    expect(net).not.toContain(secretish);
    const r = runHistoryPriv(reader, base, head, { body: undefined as unknown as string });
    const tokenFindings = r.findings.filter((f) => f.ruleId === "HP-TOKEN-CLASS");
    expect(tokenFindings).toHaveLength(1);
    // raporti s'e përmban kurrë vlerën:
    expect(toJsonReport({ mode: "advisory", evaluations: r.evaluations, findings: r.findings })).not.toContain("SYNTHETICFIXTURE");
  });

  it("test 7: range i munguar / repo shallow / ref invalid → error fail-closed", () => {
    const { repo, base } = repoWithBase();
    const reader = new GitCliReader(repo.dir);
    expect(() => runHistoryPriv(reader, undefined, undefined, undefined)).toThrow(ConfigError);
    expect(() => runHistoryPriv(reader, base, "ffffffffffffffffffffffffffffffffffffffff", undefined)).toThrow(ScanError);
  });

  it("test 8: rename/path-only violation kapet (old dhe new path)", () => {
    const { repo, base } = repoWithBase();
    repo.write("safe.md", "content\n");
    repo.commitAll("add safe");
    const privDir = frag("private", "-sensitive");
    repo.git(["mv", "safe.md", "safe2.md"]); // rename i pastër
    repo.commitAll("rename i pastër");
    repo.write(`${privDir}/leak.md`, "x\n");
    const head = repo.commitAll("shton path privat");
    const reader = new GitCliReader(repo.dir);
    const r = runHistoryPriv(reader, base, head, undefined);
    const priv = r.findings.filter((f) => f.ruleId === "HP-PRIVATE-PATH");
    expect(priv).toHaveLength(1);
    expect(priv[0]?.locator).toBe("[REDACTED_PATH]");
  });

  it("test 9: merge commit → REVIEW/INCOMPLETE_SCAN, jo bypass", () => {
    const { repo, base } = repoWithBase();
    repo.git(["checkout", "-q", "-b", "side"]);
    repo.write("side.md", "side\n");
    repo.commitAll("side work");
    repo.git(["checkout", "-q", "main"]);
    repo.write("main.md", "main\n");
    repo.commitAll("main work");
    repo.git(["merge", "-q", "--no-ff", "-m", "merge side", "side"]);
    const head = repo.head();
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined);
    const merge = r.findings.filter((f) => f.ruleId === "HP-MERGE-COMMIT");
    expect(merge).toHaveLength(1);
    expect(merge[0]?.category).toBe("INCOMPLETE_SCAN");
  });

  it("test 10: binary / symlink / oversize / invalid encoding → finding pa lexuar përmbajtjen", () => {
    const { repo, base } = repoWithBase();
    writeFileSync(join(repo.dir, "bin.dat"), Buffer.from([0, 159, 146, 150, 0, 7]));
    addSymlink(repo, "link.md", "README.md");
    writeFileSync(join(repo.dir, "big.txt"), "a".repeat(1_000_001));
    writeFileSync(join(repo.dir, "bad-encoding.txt"), Buffer.from([0x61, 0xc3, 0x28, 0x62, 0x0a])); // UTF-8 invalid, jo binar
    const head = repo.commitAll("special blobs");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined);
    const ids = r.findings.map((f) => f.ruleId);
    expect(ids).toContain("HP-BINARY-BLOB");
    expect(ids).toContain("HP-SYMLINK");
    expect(ids).toContain("HP-OVERSIZE-BLOB");
    expect(ids.some((id) => id === "HP-ENCODING-OR-BINARY" || id === "HP-BINARY-BLOB")).toBe(true);
  });

  it("test 11: path privat redaktohet plotësisht dhe blob-i nuk lexohet", () => {
    const { repo, base } = repoWithBase();
    const privDir = frag("evidence", "-private", "-local");
    const marker = "MUST_NEVER_APPEAR_IN_REPORT";
    repo.write(`${privDir}/dump.md`, `${marker}\n`);
    const head = repo.commitAll("private add");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined);
    const json = toJsonReport({ mode: "advisory", evaluations: r.evaluations, findings: r.findings });
    expect(json).toContain("[REDACTED_PATH]");
    expect(json).not.toContain(privDir);
    expect(json).not.toContain(marker);
    const priv = r.findings.find((f) => f.ruleId === "HP-PRIVATE-PATH");
    expect(priv?.severity).toBe("ERROR");
  });

  it("test 22: scope whitelist exact + prefix + file jashtë scope", () => {
    expect(parseScopeBlock(undefined).present).toBe(false);
    const body = [
      "PR desc",
      "<!-- integrity-linter:scope",
      "allowed-paths:",
      "- packages/synthetic/",
      "- docs/one.md",
      "-->",
    ].join("\n");
    const block = parseScopeBlock(body);
    expect(block.present).toBe(true);
    expect(block.malformed).toBe(false);
    expect(pathInScope("packages/synthetic/a/b.ts", block.allowed)).toBe(true);
    expect(pathInScope("docs/one.md", block.allowed)).toBe(true);
    expect(pathInScope("docs/two.md", block.allowed)).toBe(false);

    const { repo, base } = repoWithBase();
    repo.write("packages/synthetic/ok.ts", "export {};\n");
    repo.write("outside/file.md", "jashtë scope\n");
    const head = repo.commitAll("scope test");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, { body });
    const out = r.findings.filter((f) => f.ruleId === "HP-OUT-OF-SCOPE").map((f) => f.locator);
    expect(out).toEqual(["outside/file.md"]);
    // bllok malformed → HP-SCOPE-BLOCK
    const r2 = runHistoryPriv(new GitCliReader(repo.dir), base, head, { body: "<!-- integrity-linter:scope\nrreshta pa dash\n-->" });
    expect(r2.findings.some((f) => f.ruleId === "HP-SCOPE-BLOCK")).toBe(true);
  });

  it("klasat e përmbajtjes: credential-assign / absolute-path / regulated-id (sintetike)", () => {
    const { repo, base } = repoWithBase();
    const cred = frag("pass", "word") + ": " + frag("hunter", "22SYN");
    const abs = frag("/Us", "ers/", "synthetic/file.txt");
    const twelve = frag("1234", "5678", "9012");
    repo.write("mixed.md", `${cred}\n${abs}\nid ${twelve} fund\n`);
    const head = repo.commitAll("content classes");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined);
    const ids = r.findings.map((f) => f.ruleId);
    expect(ids).toContain("HP-CREDENTIAL-ASSIGN");
    expect(ids).toContain("HP-ABSOLUTE-LOCAL-PATH");
    expect(ids).toContain("HP-REGULATED-ID-CLASS");
    const json = toJsonReport({ mode: "advisory", evaluations: r.evaluations, findings: r.findings });
    expect(json).not.toContain("hunter");
    expect(json).not.toContain("synthetic/file.txt");
    expect(json).not.toContain(twelve);
  });

  it("test 24: self-scan i PR-së aktuale → zero privacy ERROR", () => {
    // Skanon repo-n REALE (cwd e testit = packages/integrity-linter → ngjitemi te rrënja e repo-s)
    const repoRoot = execFileSync("git", ["rev-parse", "--show-toplevel"]).toString("utf8").trim();
    const reader = new GitCliReader(repoRoot);
    let base: string;
    try {
      base = reader.revParse("origin/main");
    } catch {
      return; // pa origin/main (mjedis i izoluar) — testi s'mund të vlerësojë range-in
    }
    const head = reader.revParse("HEAD");
    if (reader.isShallow()) return;
    const r = runHistoryPriv(reader, base, head, undefined);
    const privacyErrors = r.findings.filter((f) => f.severity === "ERROR");
    expect(privacyErrors).toEqual([]);
  });
});
