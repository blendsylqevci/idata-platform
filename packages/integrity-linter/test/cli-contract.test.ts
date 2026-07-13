import { beforeAll, describe, expect, it } from "vitest";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, writeFileSync, symlinkSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { makeTempRepo, writeFullCanonicalRepo } from "./helpers.js";
import { readPullRequestEvent, enforceRangeConsistency } from "../src/cli.js";
import { ConfigError } from "../src/types.js";

const PKG_DIR = process.cwd(); // vitest ekzekutohet nga packages/integrity-linter
const CLI = join(PKG_DIR, "dist", "cli.js");

beforeAll(() => {
  execFileSync("pnpm", ["run", "build"], { cwd: PKG_DIR, stdio: "ignore" });
}, 120_000);

function runCli(args: string[], cwd: string, env: Record<string, string> = {}) {
  const r = spawnSync(process.execPath, [CLI, ...args, "--cwd", cwd], {
    encoding: "utf8",
    env: { ...process.env, GITHUB_EVENT_PATH: "", ...env },
  });
  return { status: r.status, stdout: r.stdout, stderr: r.stderr };
}

describe("§8 KONTRATA JSON e CLI-së (procesi real i ndërtuar)", () => {
  it("--format json: stdout = VETËM JSON valid; counts brenda; trailer në stderr", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo, { staleIndexLedger: true }); // findings > 0
    const { status, stdout, stderr } = runCli(["--profiles", "DOC-CORE", "--format", "json"], repo.dir);
    expect(status).toBe(0);
    const doc = JSON.parse(stdout) as { totals: { WARNING: number }; findings: unknown[] };
    expect(doc.totals.WARNING).toBeGreaterThan(0);
    expect(stdout.trim().startsWith("{")).toBe(true);
    expect(stdout).not.toContain("advisory: exit");
    expect(stderr).toContain("advisory: exit 0");
  });

  it("--format json me ZERO findings: JSON valid, totals 0", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo);
    const { status, stdout } = runCli(["--profiles", "DOC-CORE", "--format", "json"], repo.dir);
    expect(status).toBe(0);
    const doc = JSON.parse(stdout) as { totals: { ERROR: number; WARNING: number; REVIEW: number } };
    expect(doc.totals).toEqual({ ERROR: 0, WARNING: 0, REVIEW: 0 });
  });

  it("NOT_IMPLEMENTED në JSON: evaluation e qartë, kurrë PASS", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo);
    const { status, stdout } = runCli(["--profiles", "SEF-ATK,SRC-CLOSURE", "--format", "json"], repo.dir);
    expect(status).toBe(0);
    const doc = JSON.parse(stdout) as { evaluations: Array<{ profile: string; evaluation: string }> };
    expect(doc.evaluations.every((e) => e.evaluation === "NOT_IMPLEMENTED")).toBe(true);
    expect(stdout).not.toContain('"PASS"');
  });

  it("error exit 2: stdout PA JSON të korruptuar; stderr me kod fiks; INCOMPLETE_SCAN i dallueshëm", () => {
    const repo = makeTempRepo();
    repo.write("README.md", "jo kanonik\n");
    repo.commitAll("c");
    const r1 = runCli(["--profiles", "DOC-CORE", "--format", "json"], repo.dir);
    expect(r1.status).toBe(2);
    expect(r1.stdout).toBe(""); // zero JSON i pjesshëm
    expect(r1.stderr).toContain("INCOMPLETE_SCAN:SURFACE_MISSING");

    const r2 = runCli(["--profiles", "DOC-CORE", "--docs-ref", "missing-ref", "--format", "json"], repo.dir);
    expect(r2.status).toBe(2);
    expect(r2.stdout).toBe("");
  });

  it("--format both: JSON në stdout, raporti human në stderr", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo);
    const { status, stdout, stderr } = runCli(["--profiles", "DOC-CORE", "--format", "both"], repo.dir);
    expect(status).toBe(0);
    expect(() => JSON.parse(stdout)).not.toThrow();
    expect(stderr).toContain("integrity-linter mode=advisory");
  });
});

describe("§1 WORKFLOW FAIL-CLOSED (smoke i komandës reale)", () => {
  it("CLI me ref invalid del 2 dhe wrapper-i bash 'set -euo pipefail' me redirect e ruan exit 2", () => {
    const repo = makeTempRepo();
    repo.write("a.md", "x\n");
    repo.commitAll("c");
    const script = [
      "set -euo pipefail",
      `node "${CLI}" --profiles DOC-CORE --docs-ref missing-ref --cwd "${repo.dir}" --format text > "${repo.dir}/report.txt"`,
      `cat "${repo.dir}/report.txt"`,
    ].join("\n");
    const r = spawnSync("bash", ["-c", script], { encoding: "utf8", env: { ...process.env, GITHUB_EVENT_PATH: "" } });
    expect(r.status).toBe(2); // exit-code i CLI-së NUK gëlltitet
  });

  it("kundër-prova e defektit të vjetër: '| tee' nën bash pa pipefail e kthen 2 → 0 (prandaj u hoq)", () => {
    const repo = makeTempRepo();
    repo.write("a.md", "x\n");
    repo.commitAll("c");
    const script = `node "${CLI}" --profiles DOC-CORE --docs-ref missing-ref --cwd "${repo.dir}" --format text | tee "${repo.dir}/r.txt"`;
    const r = spawnSync("bash", ["-e", "-c", script], { encoding: "utf8", env: { ...process.env, GITHUB_EVENT_PATH: "" } });
    expect(r.status).toBe(0); // dokumentim i rrugës fail-open që mbyllëm
  });
});

describe("§9 EVENT hardening — input i jashtëm i pabesuar", () => {
  const sha = (c: string) => c.repeat(40);

  function writeEvent(obj: unknown): string {
    const dir = mkdtempSync(join(tmpdir(), "linter-event-"));
    const p = join(dir, "event.json");
    writeFileSync(p, JSON.stringify(obj));
    return p;
  }

  it("parse i shëndoshë: vetëm base.sha/head.sha/body; SHA 40-hex", () => {
    const p = writeEvent({ pull_request: { base: { sha: sha("a") }, head: { sha: sha("b") }, body: "hi" } });
    const ev = readPullRequestEvent(p);
    expect(ev).toEqual({ baseSha: sha("a"), headSha: sha("b"), body: "hi" });
  });

  it("SHA jo-40-hex → EVENT_SHA_INVALID; JSON i prishur → EVENT_MALFORMED", () => {
    const bad = writeEvent({ pull_request: { base: { sha: "short" } } });
    expect(() => readPullRequestEvent(bad)).toThrow("EVENT_SHA_INVALID");
    const dir = mkdtempSync(join(tmpdir(), "linter-event-"));
    const p = join(dir, "event.json");
    writeFileSync(p, "{jo json");
    expect(() => readPullRequestEvent(p)).toThrow("EVENT_MALFORMED");
  });

  it("symlink event → EVENT_NOT_REGULAR_FILE; oversize body → EVENT_BODY_OVERSIZE", () => {
    const dir = mkdtempSync(join(tmpdir(), "linter-event-"));
    const real = join(dir, "real.json");
    writeFileSync(real, JSON.stringify({ pull_request: { body: "x" } }));
    const link = join(dir, "link.json");
    symlinkSync(real, link);
    expect(() => readPullRequestEvent(link)).toThrow("EVENT_NOT_REGULAR_FILE");

    const big = writeEvent({ pull_request: { body: "a".repeat(70_000) } });
    expect(() => readPullRequestEvent(big)).toThrow("EVENT_BODY_OVERSIZE");
  });

  it("mospërputhje --base/--head me event → EVENT_RANGE_MISMATCH", () => {
    const ev = { baseSha: sha("a"), headSha: sha("b") };
    expect(() => enforceRangeConsistency({ base: sha("c") }, ev)).toThrow(ConfigError);
    expect(() => enforceRangeConsistency({ base: sha("a"), head: sha("b") }, ev)).not.toThrow();
    expect(() => enforceRangeConsistency({}, ev)).not.toThrow();
  });

  it("§8 trust boundary: env NUK lexohet pa GITHUB_ACTIONS+pull_request; --event-file eksplicit funksionon; wrong event name injorohet", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo);
    const p = writeEvent({ pull_request: { body: "x" } });
    // env i vendosur POR jo në Actions → NUK lexohet (scan OK pa event)
    const r1 = runCli(["--profiles", "DOC-CORE", "--format", "json"], repo.dir, { GITHUB_EVENT_PATH: p });
    expect(r1.status).toBe(0);
    const doc1 = JSON.parse(r1.stdout) as { evaluations: Array<{ check?: string; evaluation: string }> };
    expect(doc1.evaluations.some((e) => e.check === "pr-body" && e.evaluation === "NOT_EVALUATED")).toBe(true);
    // Actions + event i GABUAR (push) → injorohet
    const r2 = runCli(["--profiles", "DOC-CORE", "--format", "json"], repo.dir,
      { GITHUB_EVENT_PATH: p, GITHUB_ACTIONS: "true", GITHUB_EVENT_NAME: "push" });
    expect(r2.status).toBe(0);
    // Actions + pull_request → lexohet
    const r3 = runCli(["--profiles", "DOC-CORE", "--format", "json"], repo.dir,
      { GITHUB_EVENT_PATH: p, GITHUB_ACTIONS: "true", GITHUB_EVENT_NAME: "pull_request" });
    expect(r3.status).toBe(0);
    const doc3 = JSON.parse(r3.stdout) as { evaluations: Array<{ check?: string }> };
    expect(doc3.evaluations.some((e) => e.check === "pr-body")).toBe(false); // body u lexua
    // --event-file eksplicit lokalisht ✓
    const r4 = runCli(["--profiles", "DOC-CORE", "--format", "json", "--event-file", p], repo.dir, {});
    expect(r4.status).toBe(0);
  });

  it("§8 boundary: body limit ekzakt OK; limit+1 → EVENT_BODY_OVERSIZE; pull_request null → EVENT_MALFORMED", () => {
    const exact = writeEvent({ pull_request: { body: "a".repeat(65_536) } });
    expect(readPullRequestEvent(exact)?.body?.length).toBe(65_536);
    const plus1 = writeEvent({ pull_request: { body: "a".repeat(65_537) } });
    expect(() => readPullRequestEvent(plus1)).toThrow("EVENT_BODY_OVERSIZE");
    const nullPr = writeEvent({ pull_request: null });
    expect(() => readPullRequestEvent(nullPr)).toThrow("EVENT_MALFORMED");
    const noPr = writeEvent({ something_else: 1 });
    expect(() => readPullRequestEvent(noPr)).toThrow("EVENT_MALFORMED");
  });

  it("event i pranishëm live (proces real): scope block i vlefshëm → zero HP-SCOPE findings", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo);
    repo.write("docs/extra.md", "shtesë e pastër\n");
    const head = repo.commitAll("delta");
    const base = execFileSync("git", ["rev-parse", `${head}~1`], { cwd: repo.dir }).toString().trim();
    const body = ["<!-- integrity-linter:scope", "allowed-paths:", "- docs/", "-->"].join("\n");
    const p = writeEvent({ pull_request: { base: { sha: base }, head: { sha: head }, body } });
    const { status, stdout } = runCli(
      ["--profiles", "HISTORY-PRIV", "--base", base, "--head", head, "--format", "json", "--event-file", p],
      repo.dir,
      {},
    );
    expect(status).toBe(0);
    const doc = JSON.parse(stdout) as { findings: Array<{ ruleId: string }> };
    expect(doc.findings.filter((f) => f.ruleId.startsWith("HP-SCOPE") || f.ruleId === "HP-OUT-OF-SCOPE")).toEqual([]);
  });
});
