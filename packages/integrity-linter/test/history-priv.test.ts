import { describe, expect, it } from "vitest";
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { join } from "node:path";
import { addSymlink, commitWithRawTree, frag, hashBlob, makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runHistoryPriv, parseScopeBlock, pathInScope } from "../src/profiles/history-priv.js";
import { defaultAuthorityClasses } from "../src/authority-classes.js";
import { toJsonReport } from "../src/reporter.js";
import { ConfigError, ScanError } from "../src/types.js";

function repoWithBase() {
  const repo = makeTempRepo();
  repo.write("README.md", "synthetic base\n");
  const base = repo.commitAll("base");
  return { repo, base };
}

describe("HISTORY-PRIV — fail-closed, per-commit, privacy-safe", () => {
  it("introduced-then-removed mbetet finding (net-diff e fsheh, per-commit e kap)", () => {
    const { repo, base } = repoWithBase();
    const secretish = frag("g", "ho", "_SYNTHETICFIXTURE12345");
    repo.write("notes.md", `line\n${secretish}\n`);
    repo.commitAll("A introduces");
    repo.write("notes.md", "line\n");
    const head = repo.commitAll("B removes");
    const reader = new GitCliReader(repo.dir);
    const net = execFileSync("git", ["diff", base, head], { cwd: repo.dir }).toString("utf8");
    expect(net).not.toContain(secretish);
    const r = runHistoryPriv(reader, base, head, undefined);
    expect(r.findings.filter((f) => f.ruleId === "HP-TOKEN-CLASS")).toHaveLength(1);
    expect(toJsonReport({ mode: "advisory", evaluations: r.evaluations, findings: r.findings })).not.toContain("SYNTHETICFIXTURE");
  });

  it("range i munguar → ConfigError; ref invalid → ScanError (exit-2 path)", () => {
    const { repo, base } = repoWithBase();
    const reader = new GitCliReader(repo.dir);
    expect(() => runHistoryPriv(reader, undefined, undefined, undefined)).toThrow(ConfigError);
    expect(() => runHistoryPriv(reader, base, "ffffffffffffffffffffffffffffffffffffffff", undefined)).toThrow(ScanError);
  });

  it("§4A: MERGE COMMIT në range → INCOMPLETE_SCAN (kurrë REVIEW+continue) — token i futur në conflict resolution NUK humbet në heshtje", () => {
    const { repo, base } = repoWithBase();
    repo.write("c.md", "start\n");
    repo.commitAll("add c");
    repo.git(["checkout", "-q", "-b", "side"]);
    repo.write("c.md", "side version\n");
    repo.commitAll("side edit");
    repo.git(["checkout", "-q", "main"]);
    repo.write("c.md", "main version\n");
    repo.commitAll("main edit");
    try {
      repo.git(["merge", "-q", "side"]);
    } catch {
      // konflikt i pritur — zgjidhja fut token sintetik VETËM në merge commit
      const tok = frag("g", "ho", "_MERGEONLYTOKEN99999");
      repo.write("c.md", `resolved ${tok}\n`);
      repo.git(["add", "c.md"]);
      repo.git(["commit", "-q", "-m", "merge side (resolution)"]);
    }
    const head = repo.head();
    const reader = new GitCliReader(repo.dir);
    try {
      runHistoryPriv(reader, base, head, undefined);
      expect.unreachable("duhet të hidhte INCOMPLETE_SCAN");
    } catch (err) {
      expect(err).toBeInstanceOf(ScanError);
      expect((err as ScanError).code).toBe("INCOMPLETE_SCAN:MERGE_COMMIT_IN_RANGE");
      expect((err as Error).message).not.toContain("MERGEONLYTOKEN");
    }
  });

  it("§4B: symlink → INCOMPLETE_SCAN pa content read", () => {
    const { repo, base } = repoWithBase();
    addSymlink(repo, "link.md", "README.md");
    const head = repo.commitAll("adds symlink");
    expect(() => runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined))
      .toThrow("INCOMPLETE_SCAN:UNSUPPORTED_FILE_MODE");
  });

  it("§4B/§4D: gitlink (160000) → INCOMPLETE_SCAN pa content read", () => {
    const { repo, base } = repoWithBase();
    const fakeCommitOid = "a".repeat(40);
    const head = commitWithRawTree(repo, [
      { mode: "160000", type: "commit", oid: fakeCommitOid, pathBytes: Buffer.from("vendored-module", "utf8") },
    ], "adds gitlink", base);
    expect(() => runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined))
      .toThrow("INCOMPLETE_SCAN:UNSUPPORTED_FILE_MODE");
  });

  it("§3: private Add → ERROR + [REDACTED_PATH], zero blob read; Delete privat = cleanup pa finding", () => {
    const { repo, base } = repoWithBase();
    const privDir = frag("evidence", "-private", "-local");
    const marker = "MUST_NEVER_APPEAR_IN_REPORT";
    repo.write(`${privDir}/dump.md`, `${marker}\n`);
    const c1 = repo.commitAll("private add");
    const r1 = runHistoryPriv(new GitCliReader(repo.dir), base, c1, undefined);
    const priv = r1.findings.filter((f) => f.ruleId === "HP-PRIVATE-PATH");
    expect(priv).toHaveLength(1);
    expect(priv[0]?.severity).toBe("ERROR");
    expect(priv[0]?.locator).toBe("[REDACTED_PATH]");
    const json1 = toJsonReport({ mode: "advisory", evaluations: r1.evaluations, findings: r1.findings });
    expect(json1).not.toContain(privDir);
    expect(json1).not.toContain(marker);

    // Delete i pastër i path-it privat (cleanup) — pa finding privat të ri
    execFileSync("git", ["rm", "-rq", privDir], { cwd: repo.dir });
    const c2 = repo.commitAll("private cleanup delete");
    const r2 = runHistoryPriv(new GitCliReader(repo.dir), c1, c2, undefined);
    expect(r2.findings.filter((f) => f.ruleId === "HP-PRIVATE-PATH")).toHaveLength(0);
    expect(r2.findings.filter((f) => f.ruleId === "HP-PRIVATE-RELOCATION")).toHaveLength(0);
  });

  it("§3: private Delete + public Add në të njëjtin commit → HP-PRIVATE-RELOCATION (rename nga private → public, D+A pa -M)", () => {
    const { repo, base } = repoWithBase();
    const privDir = frag("private", "-sensitive");
    repo.write(`${privDir}/leak.md`, "synthetic-priv-content\n");
    const c1 = repo.commitAll("private add");
    execFileSync("git", ["mv", `${privDir}/leak.md`, "public-leak.md"], { cwd: repo.dir });
    const c2 = repo.commitAll("relocate to public");
    const r = runHistoryPriv(new GitCliReader(repo.dir), c1, c2, undefined);
    const reloc = r.findings.filter((f) => f.ruleId === "HP-PRIVATE-RELOCATION");
    expect(reloc).toHaveLength(1);
    expect(reloc[0]?.locator).toBe("[REDACTED_PATH]");
    const json = toJsonReport({ mode: "advisory", evaluations: r.evaluations, findings: r.findings });
    expect(json).not.toContain(privDir);
  });

  it("§4D: rename i pastër publik (D+A) + rename me modifikim skanohen si Add i ri", () => {
    const { repo, base } = repoWithBase();
    repo.write("plain.md", "safe line\n");
    const c1 = repo.commitAll("add plain");
    execFileSync("git", ["mv", "plain.md", "renamed.md"], { cwd: repo.dir });
    const c2 = repo.commitAll("pure rename");
    const r2 = runHistoryPriv(new GitCliReader(repo.dir), c1, c2, undefined);
    expect(r2.findings).toHaveLength(0); // içeriku i vjetër ri-added por i pastër
    const tok = frag("g", "ho", "_RENMODTOKEN123456");
    repo.write("renamed2.md", `safe line\n${tok}\n`);
    execFileSync("git", ["rm", "-q", "renamed.md"], { cwd: repo.dir });
    const c3 = repo.commitAll("rename + modification");
    const r3 = runHistoryPriv(new GitCliReader(repo.dir), c2, c3, undefined);
    expect(r3.findings.some((f) => f.ruleId === "HP-TOKEN-CLASS")).toBe(true);
  });

  it("§1 battery: authority per-commit — missing/unreadable/ambige pas aktivizimit → exit 2; para aktivizimit lejohet nga metadata", () => {
    const { repo, base } = repoWithBase();
    const mk = () => [{
      id: "syn-auth", kind: "count" as const, authorityFile: "auth.md",
      authorityPattern: /VLERA: (\d+)/g,
      consumers: [{ file: "cons.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /REF (\d+)/g }],
    }];
    const run = (b: string, h: string) =>
      runHistoryPriv(new GitCliReader(repo.dir), b, h, undefined, { driftClasses: mk(), chainRequiredSurfaces: [] });

    repo.write("cons.md", "REF 100\n");
    const c1 = repo.commitAll("consumer pa authority");
    expect(() => run(base, c1)).not.toThrow(); // para aktivizimit — lejohet nga metadata

    repo.write("auth.md", "VLERA: 100\n");
    const c2 = repo.commitAll("authority aktivizohet");

    repo.write("auth.md", "pa asnjë vlerë këtu\n");
    const c3 = repo.commitAll("authority bosh");
    expect(() => run(c2, c3)).toThrow("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE"); // aktiv në bazën c2

    repo.write("auth.md", "VLERA: 100\n");
    const c4 = repo.commitAll("rikthehet valid");

    repo.write("auth.md", "VLERA: 100\nVLERA: 200\n");
    const c5 = repo.commitAll("dy vlera të ndryshme");
    expect(() => run(c4, c5)).toThrow("INCOMPLETE_SCAN:AUTHORITY_AMBIGUOUS");

  });

  it("§1 battery: dy match me të njëjtën vlerë lejohen; fshirja PAS aktivizimit → AUTHORITY_MISSING", () => {
    const { repo, base } = repoWithBase();
    const mk = () => [{
      id: "syn-auth2", kind: "count" as const, authorityFile: "auth.md",
      authorityPattern: /VLERA: (\d+)/g,
      consumers: [{ file: "cons.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /REF (\d+)/g }],
    }];
    const run = (b: string, h: string) =>
      runHistoryPriv(new GitCliReader(repo.dir), b, h, undefined, { driftClasses: mk(), chainRequiredSurfaces: [] });
    repo.write("auth.md", "VLERA: 100\n");
    repo.write("cons.md", "REF 100\n");
    const c1 = repo.commitAll("aktiv");
    repo.write("auth.md", "VLERA: 100\nVLERA: 100\n");
    const c2 = repo.commitAll("dyfish e njëjta");
    expect(() => run(c1, c2)).not.toThrow();
    execFileSync("git", ["rm", "-q", "auth.md"], { cwd: repo.dir });
    const c3 = repo.commitAll("fshihet pas aktivizimit");
    expect(() => run(c2, c3)).toThrow("INCOMPLETE_SCAN:AUTHORITY_MISSING");
  });

  it("§1 battery: consumers PATH-BOUND — stale #2/#3, dy spec në një file, jo-consumer s'skanohet, zero vlerë raw në output", () => {
    const { repo, base } = repoWithBase();
    const classes = [{
      id: "syn-multi", kind: "version" as const, authorityFile: "auth.md",
      authorityPattern: /V: (\d+\.\d+)/g,
      consumers: [
        { file: "c1.md", requirement: "REQUIRED_CURRENT" as const, pattern: /REF v(\d+\.\d+)/g },
        { file: "c2.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /REF v(\d+\.\d+)/g },
        { file: "c2.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /ALT (\d+\.\d+)/g },
        { file: "c3.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /REF v(\d+\.\d+)/g },
      ],
    }];
    repo.write("auth.md", "V: 2.0\n");
    repo.write("c1.md", "REF v2.0\n");
    repo.commitAll("bazë e pastër");
    repo.write("c2.md", "REF v1.9 stale\nALT 1.8 stale\n");
    repo.write("c3.md", "REF v1.7 stale\n");
    repo.write("jo-consumer.md", "REF v0.1 tekst i ngjashëm\n");
    const head = repo.commitAll("stale consumers");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, { driftClasses: classes, chainRequiredSurfaces: [] });
    const drift = r.findings.filter((f) => f.ruleId === "HP-DRIFT-VERSION").map((f) => f.locator);
    expect(drift.some((l) => l.startsWith("c2.md:1"))).toBe(true);
    expect(drift.some((l) => l.startsWith("c2.md:2"))).toBe(true);
    expect(drift.some((l) => l.startsWith("c3.md:1"))).toBe(true);
    expect(drift.some((l) => l.includes("jo-consumer"))).toBe(false);
    const json = toJsonReport({ mode: "advisory", evaluations: r.evaluations, findings: r.findings });
    expect(json).not.toContain("1.9");
    expect(json).not.toContain("2.0");
  });

  it("§1 battery: REQUIRED hiqet në A dhe rikthehet në B → finding historik te A; rreshti historik s'e plotëson REQUIRED", () => {
    const { repo, base } = repoWithBase();
    const classes = [{
      id: "syn-req", kind: "status" as const, authorityFile: "auth.md",
      authorityPattern: /ST: (OK|JO)/g,
      consumers: [{ file: "c1.md", requirement: "REQUIRED_CURRENT" as const, pattern: /GJENDJA (OK|JO)/g }],
    }];
    repo.write("auth.md", "ST: OK\n");
    repo.write("c1.md", "GJENDJA OK\n");
    repo.commitAll("bazë");
    repo.write("c1.md", "pa referencë fare tani\n");
    const cA = repo.commitAll("A heq referencën");
    repo.write("c1.md", "GJENDJA OK\n");
    const cB = repo.commitAll("B e rikthen");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, cB, undefined, { driftClasses: classes, chainRequiredSurfaces: [] });
    const missing = r.findings.filter((f) => f.ruleId === "HP-CURRENT-VALUE-MISSING");
    expect(missing).toHaveLength(1);
    expect(missing[0]?.commit).toBe(cA.slice(0, 8));
    repo.write("c1.md", "Historia: GJENDJA OK (SUPERSEDED)\n");
    const cC = repo.commitAll("C vetëm historik");
    const r2 = runHistoryPriv(new GitCliReader(repo.dir), base, cC, undefined, { driftClasses: classes, chainRequiredSurfaces: [] });
    expect(r2.findings.filter((f) => f.ruleId === "HP-CURRENT-VALUE-MISSING" && f.commit === cC.slice(0, 8))).toHaveLength(1);
  });

  it("§1 battery: GitReader failure → propagohet exit 2, kurrë skip+EVALUATED", () => {
    const { repo, base } = repoWithBase();
    repo.write("x.md", "diçka\n");
    const head = repo.commitAll("c");
    const real = new GitCliReader(repo.dir);
    const failing = new Proxy(real, {
      get(t, prop, rec) {
        if (prop === "hasFile") return () => { throw new ScanError("GIT_OP_FAILED:ls-tree"); };
        return Reflect.get(t, prop, rec);
      },
    }) as GitCliReader;
    expect(() => runHistoryPriv(failing, base, head, undefined)).toThrow("GIT_OP_FAILED:ls-tree");
  });

  it("§4: commit-i ndërmjetës që prish chain-in kapet; rikthimi s'e fshin finding-un historik", () => {
    const { repo, base } = repoWithBase();
    const CH = "Step B → Step C → independent review → HUMAN GATE → private PREFLIGHT → Tier 2";
    repo.write("chain.md", CH + "\n");
    repo.commitAll("chain i plotë");
    repo.write("chain.md", "Step B → Step C → HUMAN GATE → private PREFLIGHT → Tier 2\n");
    const cBreak = repo.commitAll("prish chain-in");
    repo.write("chain.md", CH + "\n");
    const cFix = repo.commitAll("e rikthen");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, cFix, undefined, { driftClasses: [], chainRequiredSurfaces: ["chain.md"] });
    const broken = r.findings.filter((f) => f.ruleId === "HP-CHAIN-INCOMPLETE");
    expect(broken).toHaveLength(1);
    expect(broken[0]?.commit).toBe(cBreak.slice(0, 8));
    expect(broken[0]?.locator).toContain("independent-review");
  });

  it("§4D: binary me TAB në filename → INCOMPLETE_SCAN nga path policy (para blob-it)", () => {
    const { repo, base } = repoWithBase();
    const oid = hashBlob(repo, "x\n");
    const head = commitWithRawTree(repo, [
      { mode: "100644", type: "blob", oid, pathBytes: Buffer.from("bin\tname.dat", "utf8") },
    ], "tab filename", base);
    expect(() => runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined)).toThrow("PATH_CONTROL_CHARS");
  });

  it("scope whitelist: exact + prefix + out-of-scope + bllok malformed", () => {
    expect(parseScopeBlock(undefined).present).toBe(false);
    const body = ["PR", "<!-- integrity-linter:scope", "allowed-paths:", "- packages/synthetic/", "- docs/one.md", "-->"].join("\n");
    const block = parseScopeBlock(body);
    expect(block.malformed).toBe(false);
    expect(pathInScope("packages/synthetic/a/b.ts", block.allowed)).toBe(true);
    expect(pathInScope("docs/one.md", block.allowed)).toBe(true);
    expect(pathInScope("docs/two.md", block.allowed)).toBe(false);

    const { repo, base } = repoWithBase();
    repo.write("packages/synthetic/ok.ts", "export {};\n");
    repo.write("outside/file.md", "jashtë scope\n");
    const head = repo.commitAll("scope test");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, { body });
    expect(r.findings.filter((f) => f.ruleId === "HP-OUT-OF-SCOPE").map((f) => f.locator)).toEqual(["outside/file.md"]);
    const r2 = runHistoryPriv(new GitCliReader(repo.dir), base, head, { body: "<!-- integrity-linter:scope\npa dash\n-->" });
    expect(r2.findings.some((f) => f.ruleId === "HP-SCOPE-BLOCK")).toBe(true);
  });

  it("§6 klasat canonical (pozitive + negative + column-aware)", () => {
    const { repo, base } = repoWithBase();
    const IMPL = frag("IMPLE", "MENTED");
    const TSTD = frag("TES", "TED");
    const MODL = frag("MOD", "ELED");
    const XHIRO = frag("xhiro", "ja ditore");
    const EXTR = frag("extrac", "tion");
    const PF = frag("PRE", "FLIGHT");
    const HG = frag("HUMAN", " ", "GATE");
    // tabelë REALE me header Treatment + Maturity (column-aware §2D/§2E/§2G)
    repo.write("matrix.md", [
      "| ID | Treatment | Maturity | Prova |",
      "|---|---|---|---|",
      "| a | REQ+" + "FOO" + " | " + MODL + " | x |",              // Treatment: token i panjohur
      "| b | LATER | " + MODL + " | x |",                          // Treatment: unknown-only standalone
      "| c | DECISION_REQUIRED | " + MODL + " | x |",              // Treatment: unknown-only standalone
      "| d | REQ+REF | " + IMPL + " " + TSTD + " | x |",           // Maturity: dy stage në një qelizë
      "| e | REQ | GAP " + MODL + " | x |",                         // Maturity: GAP + MODELED mixed
      "| f | REQ | " + MODL + " | x |",                             // valid single-stage → clean
      "| g | REQ+MD | PROVEN-PoC " + MODL + " | x |",               // qualifier + stage → clean
      "| h | REQ | " + MODL + " |  |",                              // qelizë mandatory bosh (kolona e fundit)
    ].join("\n") + "\n");
    // jashtë tabele/kolone: zero FP
    repo.write("prose.md", [
      "LATER do të vendoset më vonë; DECISION_REQUIRED është term i lirë këtu.",
      "Tranzicioni narrativ " + MODL + " → " + TSTD + " jashtë maturity cell.",
      "backup " + EXTR + ": " + "2" + "23 records nga dump-i",       // §2B pa separator
      "backup " + EXTR + ": " + "10" + "76 rows",                    // §2B pa separator
      XHIRO + " e lokalit u rrit",                                   // §2C
      "PoC " + IMPL + " pa production evidence",                     // §2F — 'pa' NUK e shpëton
      "rendi: private " + PF + " → " + HG,                           // §2I single-line
      "",
      "raste §6C:",
      "| a | REQ+REF | b | c |",
      "PoC feasibility-only, JO-" + TSTD + ".",                        // strukturuar → clean
      "Historia: " + XHIRO + " (HISTORICAL)",                          // §6C: historiku NUK përjashton → FINDING
      "nuk raportohet " + XHIRO + " këtu",                             // mohim LOKAL → clean
      "shit" + "jet reale janë 123, jo 0",                             // trailing "jo" s'bypasson → FINDING
      "PoC " + IMPL + "; jo për fiskalizim",                           // "jo" i mëvonshëm s'bypasson → FINDING
      "rendi i saktë: " + HG + " → private " + PF,
      "vetëm 223 records pa kontekst këtu (near-miss)",
    ].join("\n") + "\n");
    // §2I multiline: lista e shpërndarë në rreshta të njëpasnjëshëm
    repo.write("order.md", [
      "1. private " + PF + " së pari",
      "2. " + HG + " më pas",
      "3. Tier 2 në fund",
    ].join("\n") + "\n");
    const head = repo.commitAll("canonical classes");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined);
    const ids = r.findings.map((f) => f.ruleId);
    expect(ids.filter((i) => i === "HP-TREATMENT-VOCAB")).toHaveLength(3); // FOO/LATER/DECISION_REQUIRED
    expect(ids.filter((i) => i === "HP-MIXED-MATURITY")).toHaveLength(2);  // d + e
    expect(ids.filter((i) => i === "HP-EMPTY-MANDATORY-CELL")).toHaveLength(1);
    expect(ids.filter((i) => i === "HP-EVIDENCE-METRIC")).toHaveLength(2); // 223 + 1076
    expect(ids.filter((i) => i === "HP-OPERATIONAL-CLAIM")).toHaveLength(3); // aktivi + historiku(§6C) + "shitjet reale…, jo 0"
    expect(ids.filter((i) => i === "HP-POC-PROMOTION")).toHaveLength(2);   // 'pa' s'e shpëton + '; jo …' s'e shpëton
    expect(ids.filter((i) => i === "HP-GATE-ORDER").length).toBeGreaterThanOrEqual(2); // single + multiline
  });

  it("§2A: regulated IDs kontekstualë — 9-shifror me etiketë → finding; 7-shifror pa kontekst → clean", () => {
    const { repo, base } = repoWithBase();
    const nine = frag("1234", "56789");
    const seven = frag("765", "4321");
    const label = frag("N", "UI");
    const vat = frag("TV", "SH");
    repo.write("ids.md", [
      `${label}: ${nine} (sintetik-format, jo vlerë reale)`,
      `${vat} nr ${frag("8765", "4321")}`,
      `thjesht numri ${seven} pa etiketë`,
    ].join("\n") + "\n");
    const head = repo.commitAll("ids");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined);
    const ctx = r.findings.filter((f) => f.ruleId === "HP-REGULATED-ID-CONTEXT");
    expect(ctx).toHaveLength(2);
    const json = toJsonReport({ mode: "advisory", evaluations: r.evaluations, findings: r.findings });
    expect(json).not.toContain(nine); // vlera KURRË në output
  });

  it("§6.9: version drift PER-COMMIT — konsumatorë PATH-BOUND kundër snapshot-it të commit-it", () => {
    const { repo, base } = repoWithBase();
    const baseCls = {
      id: "syn-ledger", kind: "version" as const, authorityFile: "docs/ledger.md",
      authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/g,
      consumers: [{ file: "docs/note.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /Ledger(?:\.md)?`?\s*(?:—)?\s*v(\d+\.\d+)/g }],
    };
    repo.write("docs/ledger.md", "**Versioni:** 1.6\n");
    repo.commitAll("authority v1.6");
    repo.write("docs/note.md", ["shih Ledger.md` — v1", ".", "5 për detajet\n"].join(""));
    const head = repo.commitAll("stale consumer");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, { driftClasses: [baseCls], chainRequiredSurfaces: [] });
    const drift = r.findings.filter((f) => f.ruleId === "HP-DRIFT-VERSION");
    expect(drift).toHaveLength(1);
    expect(drift[0]?.locator).toContain("docs/note.md:1");
    repo.write("docs/note2.md", "Ledger v1.6 aktual\nHistoria: Ledger v1.5 (SUPERSEDED)\n");
    const head2 = repo.commitAll("ok consumer");
    const cls2 = { ...baseCls, consumers: [...baseCls.consumers, { file: "docs/note2.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /Ledger(?:\.md)?`?\s*(?:—)?\s*v(\d+\.\d+)/g }] };
    const r2 = runHistoryPriv(new GitCliReader(repo.dir), head, head2, undefined, { driftClasses: [cls2], chainRequiredSurfaces: [] });
    expect(r2.findings.filter((f) => f.ruleId === "HP-DRIFT-VERSION")).toHaveLength(0);
  });

  it("§2H: phase/status/count/set drift per-commit me klasa të injektuara (engine generik)", () => {
    const { repo, base } = repoWithBase();
    repo.write("auth.md", "PHASE: F5\nSTATUS: NO-GO\nCOUNT: 132\n");
    repo.commitAll("authorities");
    repo.write("cons.md", [
      "faza aktive: PHASE F4 këtu",             // phase drift
      "verdikti: STATUS GO tani",                // status drift
      "totali: COUNT 117 rreshta",               // count drift
      "ok: PHASE F5 · STATUS NO-GO · COUNT 132", // current → clean
    ].join("\n") + "\n");
    const head = repo.commitAll("consumers");
    const classes = [
      { id: "syn-phase", kind: "status" as const, authorityFile: "auth.md", authorityPattern: /PHASE: (F\d+)/g,
        consumers: [{ file: "cons.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /PHASE (F\d+)/g }] },
      { id: "syn-status", kind: "status" as const, authorityFile: "auth.md", authorityPattern: /STATUS: (NO-GO|GO)/g,
        consumers: [{ file: "cons.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /STATUS (NO-GO|(?<!NO-)GO)/g }] },
      { id: "syn-count", kind: "count" as const, authorityFile: "auth.md", authorityPattern: /COUNT: (\d+)/g,
        consumers: [{ file: "cons.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /COUNT (\d+)/g }] },
    ];
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, { driftClasses: classes });
    const ids = r.findings.map((f) => `${f.ruleId}|${f.locator}`);
    expect(ids.some((x) => x.startsWith("HP-DRIFT-STATUS") && x.includes("syn-phase"))).toBe(true);
    expect(ids.some((x) => x.startsWith("HP-DRIFT-STATUS") && x.includes("syn-status"))).toBe(true);
    expect(ids.some((x) => x.startsWith("HP-DRIFT-COUNT") && x.includes("syn-count"))).toBe(true);
    expect(r.findings.filter((f) => f.locator.includes("cons.md:4"))).toHaveLength(0); // current clean
  });

  it("locator i content-finding = numër rreshti REAL i file-it", () => {
    const { repo, base } = repoWithBase();
    repo.write("deep.md", Array.from({ length: 30 }, (_, i) => `l${i + 1}`).join("\n") + "\n");
    repo.commitAll("deep base");
    const lines = Array.from({ length: 30 }, (_, i) => `l${i + 1}`);
    const tok = frag("g", "ho", "_DEEPLINETOKEN12345");
    lines.splice(24, 0, tok); // bëhet rreshti 25 real
    repo.write("deep.md", lines.join("\n") + "\n");
    const head = repo.commitAll("insert token deep");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined);
    const f = r.findings.find((x) => x.ruleId === "HP-TOKEN-CLASS");
    expect(f?.locator).toBe("deep.md:25");
  });

  // §7A: INTEGRATION/SELF-SCAN test mbi HISTORINË REALE të repo-s — jo unit test.
  // Madhësia e historisë së PR-së rritet me çdo pass; default-i 5s tejkalohet në
  // makina më të ngadalta. Timeout-i NUK fsheh hang — dështimi/hang mbetet dështim.
  it("self-scan i PR-së aktuale → zero privacy ERROR (SKIP eksplicit kur s'ka origin/main)", { timeout: 60_000 }, (ctx) => {
    const repoRoot = execFileSync("git", ["rev-parse", "--show-toplevel"]).toString("utf8").trim();
    const reader = new GitCliReader(repoRoot);
    let base: string;
    try {
      base = reader.revParse("origin/main");
    } catch {
      ctx.skip(); // SKIPPED eksplicit — workflow-i real e ekzekuton gjithsesi scan-in
      return;
    }
    if (reader.isShallow()) {
      ctx.skip();
      return;
    }
    const head = reader.revParse("HEAD");
    const r = runHistoryPriv(reader, base, head, undefined);
    expect(r.findings.filter((f) => f.severity === "ERROR")).toEqual([]);
  });
});

describe("P-IV §2 — ONCE_PRESENT: PRANIA e authority-t aktivizon, jo leximi i parë valid", () => {
  const mk = () => [{
    id: "syn-present", kind: "count" as const, authorityFile: "auth.md",
    authorityPattern: /VLERA: (\d+)/g,
    consumers: [{ file: "cons.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /REF (\d+)/g }],
  }];
  const opts = () => ({ driftClasses: mk(), chainRequiredSurfaces: [] });
  const run = (dir: string, b: string, h: string) =>
    runHistoryPriv(new GitCliReader(dir), b, h, undefined, opts());

  it("F1: file shfaqet BOSH → exit 2 UNREADABLE (jo tolerim deri në leximin e parë valid)", () => {
    const { repo, base } = repoWithBase();
    repo.write("auth.md", "");
    const c1 = repo.commitAll("shfaqet bosh");
    expect(() => run(repo.dir, base, c1)).toThrow("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");
  });

  it("F2: file shfaqet me ZERO regex match → exit 2 UNREADABLE", () => {
    const { repo, base } = repoWithBase();
    repo.write("auth.md", "tekst pa asnjë vlerë të vlefshme\n");
    const c1 = repo.commitAll("shfaqet pa match");
    expect(() => run(repo.dir, base, c1)).toThrow("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");
  });

  it("F3: file shfaqet me dy vlera AMBIGE → exit 2 AMBIGUOUS", () => {
    const { repo, base } = repoWithBase();
    repo.write("auth.md", "VLERA: 100\nVLERA: 200\n");
    const c1 = repo.commitAll("shfaqet ambige");
    expect(() => run(repo.dir, base, c1)).toThrow("INCOMPLETE_SCAN:AUTHORITY_AMBIGUOUS");
  });

  it("F4: bosh/ambige në A → valid në B: dështimi historik NUK zhduket (range përmes A = exit 2)", () => {
    const { repo, base } = repoWithBase();
    repo.write("auth.md", "");
    repo.commitAll("A e prishur");
    repo.write("auth.md", "VLERA: 100\n");
    const cB = repo.commitAll("B e rregullon");
    expect(() => run(repo.dir, base, cB)).toThrow("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");
  });

  it("F5: authority ekziston në BAZË → prishet në commit-in e PARË → exit 2", () => {
    const { repo } = repoWithBase();
    repo.write("auth.md", "VLERA: 100\n");
    const baseWithAuth = repo.commitAll("baza me authority");
    repo.write("auth.md", "u zëvendësua pa vlerë\n");
    const c1 = repo.commitAll("prishet menjëherë");
    expect(() => run(repo.dir, baseWithAuth, c1)).toThrow("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");
  });

  it("F6: mungesa e FILE-it para aktivizimit lejohet nga kontrata; pas aktivizimit heqja → MISSING", () => {
    const { repo, base } = repoWithBase();
    repo.write("cons.md", "REF 100\n");
    const c1 = repo.commitAll("vetëm consumer");
    expect(() => run(repo.dir, base, c1)).not.toThrow();
    repo.write("auth.md", "VLERA: 100\n");
    const c2 = repo.commitAll("aktivizohet valide");
    execFileSync("git", ["rm", "-q", "auth.md"], { cwd: repo.dir });
    const c3 = repo.commitAll("hiqet pas aktivizimit");
    expect(() => run(repo.dir, c2, c3)).toThrow("INCOMPLETE_SCAN:AUTHORITY_MISSING");
  });

  it("activationAnchor (kontratë e deklaruar): para lindjes së seksionit lejohet; lind i prishur → exit 2; sticky pas heqjes", () => {
    const cls = () => [{
      id: "syn-anchor", kind: "status" as const, authorityFile: "auth.md",
      activationAnchor: /SEKSIONI-STATUS/,
      authorityPattern: /GJENDJA: (OK|JO)/g,
      consumers: [{ file: "cons.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /SHIH (OK|JO)/g }],
    }];
    const runA = (dir: string, b: string, h: string) =>
      runHistoryPriv(new GitCliReader(dir), b, h, undefined, { driftClasses: cls(), chainRequiredSurfaces: [] });

    // file ekziston PA anchor dhe PA vlerë → e lejuar (seksioni s'ka lindur)
    const { repo, base } = repoWithBase();
    repo.write("auth.md", "material tjetër i vjetër\n");
    const c1 = repo.commitAll("file pa seksion");
    expect(() => runA(repo.dir, base, c1)).not.toThrow();

    // seksioni LIND i prishur (anchor pa vlerë) → exit 2 që në lindje
    repo.write("auth.md", "material i vjetër\nSEKSIONI-STATUS\n");
    const c2 = repo.commitAll("anchor lind pa vlerë");
    expect(() => runA(repo.dir, c1, c2)).toThrow("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");

    // rikthim valid → aktivizimi sticky: heqja e anchor-it S'e çaktivizon
    const { repo: r2 } = repoWithBase();
    r2.write("auth.md", "SEKSIONI-STATUS\nGJENDJA: OK\n");
    const d1 = r2.commitAll("aktiv valid");
    r2.write("auth.md", "pa anchor dhe pa vlerë tani\n");
    const d2 = r2.commitAll("fshihet seksioni");
    expect(() => runA(r2.dir, d1, d2)).toThrow("INCOMPLETE_SCAN:AUTHORITY_ANCHOR_MISSING");
  });
});

describe("P-IV §4 — tabela PA outer pipes: rregullat strukturore mbi të njëjtin parser", () => {
  it("Maturity/Treatment/empty-cell kapen NJËSOJ si me pipe të jashtëm", () => {
    const { repo, base } = repoWithBase();
    repo.write("docs/t.md", [
      "Domeni | Prova | Maturity | Treatment",
      "--- | --- | --- | ---",
      "x |  | MODELED TESTED | LATER",
    ].join("\n") + "\n");
    const head = repo.commitAll("pipe-less table");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined,
      { driftClasses: [], chainRequiredSurfaces: [] });
    const ids = r.findings.map((f) => f.ruleId);
    expect(ids).toContain("HP-MIXED-MATURITY");
    expect(ids).toContain("HP-TREATMENT-VOCAB");
    expect(ids).toContain("HP-EMPTY-MANDATORY-CELL");
  });

  it("inverse: e njëjta strukturë me vlera valide → zero findings strukturore", () => {
    const { repo, base } = repoWithBase();
    repo.write("docs/t2.md", [
      "Domeni | Prova | Maturity | Treatment",
      "--- | --- | --- | ---",
      "y | prova-x | MODELED | REQ+REF",
    ].join("\n") + "\n");
    const head = repo.commitAll("pipe-less valid");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined,
      { driftClasses: [], chainRequiredSurfaces: [] });
    const bad = r.findings.filter((f) =>
      ["HP-MIXED-MATURITY", "HP-TREATMENT-VOCAB", "HP-EMPTY-MANDATORY-CELL", "HP-TABLE-ARITY", "HP-TABLE-MALFORMED"].includes(f.ruleId));
    expect(bad).toEqual([]);
  });

  it("prozë me pipe (pa delimiter row) → asnjë finding strukturor tabele", () => {
    const { repo, base } = repoWithBase();
    repo.write("docs/p.md", "ky tekst | ka pipe | por s'është tabelë\nedhe ky | vazhdon | i lirë\n");
    const head = repo.commitAll("proza me pipe");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined,
      { driftClasses: [], chainRequiredSurfaces: [] });
    const bad = r.findings.filter((f) => f.ruleId.startsWith("HP-TABLE") ||
      ["HP-MIXED-MATURITY", "HP-EMPTY-MANDATORY-CELL"].includes(f.ruleId));
    expect(bad).toEqual([]);
  });
});

describe("P-IV \u00a74A \u2014 activationAnchor: kufijt\u00eb e kontrat\u00ebs", () => {
  const mkCls = (id: string) => [{
    id, kind: "status" as const, authorityFile: "auth.md",
    activationAnchor: /SEKSIONI-STATUS/,
    authorityPattern: /GJENDJA: (OK|JO)/g,
    consumers: [{ file: "cons.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /SHIH (OK|JO)/g }],
  }];
  const runA = (dir: string, b: string, h: string, id = "syn-a") =>
    runHistoryPriv(new GitCliReader(dir), b, h, undefined, { driftClasses: mkCls(id), chainRequiredSurfaces: [] });

  it("anchor i hequr me vler\u00eb ende PREZENTE \u2192 exit 2 ANCHOR_MISSING (sticky)", () => {
    const { repo } = repoWithBase();
    repo.write("auth.md", "SEKSIONI-STATUS\nGJENDJA: OK\n");
    const c1 = repo.commitAll("aktiv valid");
    repo.write("auth.md", "GJENDJA: OK\n");
    const c2 = repo.commitAll("heq vet\u00ebm anchor-in");
    expect(() => runA(repo.dir, c1, c2)).toThrow("INCOMPLETE_SCAN:AUTHORITY_ANCHOR_MISSING");
  });

  it("anchor i duplikuar \u2192 exit 2 ANCHOR_DUPLICATE (edhe n\u00eb lindje)", () => {
    const { repo, base } = repoWithBase();
    repo.write("auth.md", "SEKSIONI-STATUS\nGJENDJA: OK\nSEKSIONI-STATUS\n");
    const c1 = repo.commitAll("duplikat n\u00eb lindje");
    expect(() => runA(repo.dir, base, c1)).toThrow("INCOMPLETE_SCAN:AUTHORITY_ANCHOR_DUPLICATE");
    const { repo: r2 } = repoWithBase();
    r2.write("auth.md", "SEKSIONI-STATUS\nGJENDJA: OK\n");
    const d1 = r2.commitAll("aktiv");
    r2.write("auth.md", "SEKSIONI-STATUS\nGJENDJA: OK\nSEKSIONI-STATUS\n");
    const d2 = r2.commitAll("duplikohet pas aktivizimit");
    expect(() => runA(r2.dir, d1, d2)).toThrow("INCOMPLETE_SCAN:AUTHORITY_ANCHOR_DUPLICATE");
  });

  it("anchor+vlera valide \u2192 AKTIVE: drifti i consumer-it kapet", () => {
    const { repo, base } = repoWithBase();
    repo.write("auth.md", "SEKSIONI-STATUS\nGJENDJA: OK\n");
    repo.commitAll("aktiv valid");
    repo.write("cons.md", "SHIH JO\n");
    const c2 = repo.commitAll("consumer stale");
    const r = runA(repo.dir, base, c2);
    expect(r.findings.filter((f) => f.ruleId === "HP-DRIFT-STATUS")).toHaveLength(1);
  });

  it("lindje e prishur n\u00eb A \u2192 riparim n\u00eb B: range p\u00ebrmes A mbetet exit 2", () => {
    const { repo, base } = repoWithBase();
    repo.write("auth.md", "SEKSIONI-STATUS\npa vler\u00eb\n");
    repo.commitAll("A lind i prishur");
    repo.write("auth.md", "SEKSIONI-STATUS\nGJENDJA: OK\n");
    const cB = repo.commitAll("B riparon");
    expect(() => runA(repo.dir, base, cB)).toThrow("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");
  });

  it("VET\u00cbM dy klasat e lejuara p\u00ebrdorin activationAnchor n\u00eb config default", () => {
    const anchored = defaultAuthorityClasses().filter((c) => c.activationAnchor !== undefined).map((c) => c.id).sort();
    expect(anchored).toEqual(["hg-linter-required-status", "profile-registry-set"]);
  });
});

describe("P-IV \u00a75 \u2014 mohim lokal PER-MATCH: \u00e7do match gjykohet ve\u00e7mas", () => {
  const run = (content: string) => {
    const { repo, base } = repoWithBase();
    repo.write("docs/n.md", content);
    const head = repo.commitAll("content");
    return runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, { driftClasses: [], chainRequiredSurfaces: [] });
  };

  it("F1: mohimi i claim-it t\u00eb par\u00eb NUK e p\u00ebrjashton t\u00eb dytin n\u00eb t\u00eb nj\u00ebjtin rresht", () => {
    const r = run("nuk raportohet xhiroja ditore; shitjet reale jan\u00eb 123\n");
    expect(r.findings.filter((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toHaveLength(1);
  });

  it("F2: PoC JO-TESTED historically; PoC IMPLEMENTED now \u2192 IMPLEMENTED kapet", () => {
    const r = run("PoC JO-" + ["TES", "TED"].join("") + " historically; PoC " + ["IMPLE", "MENTED"].join("") + " now\n");
    expect(r.findings.filter((f) => f.ruleId === "HP-POC-PROMOTION")).toHaveLength(1);
  });

  it("F3: vet\u00ebm claim i mohuar \u2192 zero finding", () => {
    const r = run("nuk raportohet xhiroja ditore k\u00ebtu\n");
    expect(r.findings.filter((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toHaveLength(0);
    const r2 = run("PoC JO-" + ["TES", "TED"].join("") + " dhe asgj\u00eb tjet\u00ebr\n");
    expect(r2.findings.filter((f) => f.ruleId === "HP-POC-PROMOTION")).toHaveLength(0);
  });

  it("F4: dy claims operacionale, vet\u00ebm i pari i mohuar \u2192 i dyti kapet (rend i kund\u00ebrt gjithashtu)", () => {
    const r = run("saldo reale \u00ebsht\u00eb 7; nuk raportohet xhiroja ditore\n");
    expect(r.findings.filter((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toHaveLength(1);
  });

  it("F5: \u2018jo\u2019 brenda nj\u00eb fjale NUK \u00ebsht\u00eb operator mohimi", () => {
    const r = run("PoC VA" + ["JO-TES", "TED"].join("") + " sintetik\n"); // JO- brenda fjale
    expect(r.findings.filter((f) => f.ruleId === "HP-POC-PROMOTION")).toHaveLength(1);
  });

  it("F6: mohim n\u00eb fjalin\u00eb e par\u00eb, claim real n\u00eb t\u00eb dyt\u00ebn \u2192 kapet; output pa matched content", () => {
    const r = run("nuk ka raportim k\u00ebtu. gjendja reale e ark\u00ebs \u00ebsht\u00eb 9\n");
    const hits = r.findings.filter((f) => f.ruleId === "HP-OPERATIONAL-CLAIM");
    expect(hits).toHaveLength(1);
    const json = toJsonReport({ mode: "advisory", evaluations: r.evaluations, findings: r.findings });
    expect(json).not.toContain("ark\u00ebs");
  });

  it("near-miss i ri: kualifikuesi mock lidhet me match-in e vet, jo me gjith\u00eb rreshtin", () => {
    const r = run("regression = vet\u00ebm mock " + ["TES", "TED"].join("") + "; por PoC " + ["IMPLE", "MENTED"].join("") + " në prod\n");
    expect(r.findings.filter((f) => f.ruleId === "HP-POC-PROMOTION")).toHaveLength(1);
  });
});

describe("P-IV \u00a73 near-miss: kontroll-karakter jo-NUL n\u00eb tekst valid UTF-8 \u2192 skanohet normalisht", () => {
  it("byte 0x01 nuk trajtohet si binar dhe nuk ndal skanimin", () => {
    const { repo, base } = repoWithBase();
    writeFileSync(join(repo.dir, "ctl.md"), Buffer.concat([Buffer.from("tekst i past\u00ebr "), Buffer.from([1]), Buffer.from(" fund\n")]));
    const head = repo.commitAll("kontroll-karakter");
    expect(() => runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined)).not.toThrow();
  });
});
