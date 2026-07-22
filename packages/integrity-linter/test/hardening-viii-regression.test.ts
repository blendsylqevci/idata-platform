/**
 * Hardening VIII — regression fixtures for the independent Codex review.
 * All repositories and values are synthetic; sensitive-looking tokens are
 * assembled at runtime so the fixture source itself remains safe to publish.
 */
import { describe, expect, it } from "vitest";
import { GitCliReader, parseUnifiedAddedLines } from "../src/git-repository.js";
import { detectionSkeleton, hasObfuscatedMatch } from "../src/machine-token.js";
import { parseTableRow } from "../src/markdown-table.js";
import { evaluatePath } from "../src/path-policy.js";
import {
  evaluateChainPresence,
  projectActiveMarkdown,
  runDocCore,
  type DocCoreSurfaces,
} from "../src/profiles/doc-core.js";
import { runHistoryPriv } from "../src/profiles/history-priv.js";
import type { GitReader } from "../src/types.js";
import { commitWithRawTree, frag, hashBlob, makeTempRepo } from "./helpers.js";

const FULL = "Step B → Step C → independent review → HUMAN GATE → private PREFLIGHT → Tier 2";
const WRONG = "Tier 2 → Step B → Step C → independent review → HUMAN GATE → private PREFLIGHT";

function emptyDocSurfaces(overrides: Partial<DocCoreSurfaces> = {}): DocCoreSurfaces {
  return {
    orderSurfaces: [],
    chainRequiredSurfaces: [],
    milestoneSurfaces: [],
    authorities: [],
    approvedRolePhrases: ["pronari", "pm"],
    roleRequiredSurfaces: [],
    roleOptionalSurfaces: [],
    ...overrides,
  };
}

function repoWithBase() {
  const repo = makeTempRepo();
  repo.write("README.md", "synthetic base\n");
  const base = repo.commitAll("base");
  return { repo, base };
}

describe("Hardening VIII — active Markdown and chain claims", () => {
  it("a negated chain sentence cannot be reactivated by an earlier token", () => {
    const e = evaluateChainPresence(`Step B. NUK është ky rendi: ${FULL}\n`);
    expect(e.noActiveChain).toBe(true);
  });

  it("a negated stub cannot suppress a later active malformed claim", () => {
    const e = evaluateChainPresence(`jo ky rend: ${FULL}. Rendi aktiv: ${WRONG}\n`);
    expect(e.noActiveChain).toBe(false);
    expect(e.violations.length).toBeGreaterThan(0);
  });

  it("a correct chain does not mask a second active contradictory chain", () => {
    const e = evaluateChainPresence(`${FULL}\n\n${WRONG}\n`);
    expect(e.missing).toEqual([]);
    expect(e.violations.length).toBeGreaterThan(0);
  });

  it("contrast clauses in one sentence are evaluated independently", () => {
    const recovered = evaluateChainPresence(`NUK është ky rend: ${WRONG}, por rendi aktiv është: ${FULL}\n`);
    expect(recovered.missing).toEqual([]);
    expect(recovered.violations).toEqual([]);
    const conflicting = evaluateChainPresence(`${FULL}, por ekziston edhe pretendimi aktiv ${WRONG}\n`);
    expect(conflicting.violations.length).toBeGreaterThan(0);
  });

  it("visible Markdown link labels participate in order checks", () => {
    const linked = "[Tier 2](#premature) → Step B → Step C → independent review → HUMAN GATE → private PREFLIGHT";
    expect(evaluateChainPresence(linked + "\n").violations.length).toBeGreaterThan(0);
  });

  it("HTML comments and fenced code cannot satisfy an active chain", () => {
    expect(evaluateChainPresence(`<!-- ${FULL} -->\n`).noActiveChain).toBe(true);
    expect(evaluateChainPresence(["```md", FULL, "```"].join("\n") + "\n").noActiveChain).toBe(true);
    const visible = evaluateChainPresence(`${FULL}\n<!-- ${WRONG} -->\n`);
    expect(visible.missing).toEqual([]);
    expect(visible.violations).toEqual([]);
  });

  it("an unclosed HTML comment fails closed instead of hiding privacy content", () => {
    expect(() => projectActiveMarkdown("<!-- Owner: Synthetic Hidden\n"))
      .toThrow("INCOMPLETE_SCAN:MARKDOWN_COMMENT_MALFORMED");
  });

  it("comment fence markers cannot hide later active claims", () => {
    const e = evaluateChainPresence(`<!-- \`\`\` -->\n${WRONG}\n`);
    expect(e.noActiveChain).toBe(false);
    expect(e.violations.length).toBeGreaterThan(0);
  });

  it("non-rendered Markdown and HTML metadata cannot satisfy a chain", () => {
    const hidden = [
      `[flow]: /target "${FULL}"`,
      `<span title="${FULL}"></span>`,
      `    ${FULL}`,
    ];
    for (const candidate of hidden) {
      expect(evaluateChainPresence(candidate + "\n").noActiveChain).toBe(true);
    }
    expect(evaluateChainPresence(`\`${FULL}\`\n`).missing).toEqual([]); // inline-code renderohet
  });

  it("a next-line CommonMark reference title stays metadata, not an active chain", () => {
    const hidden = `[flow]: /target\n  "${FULL}"\n`;
    expect(evaluateChainPresence(hidden).noActiveChain).toBe(true);
    // I njëjti rresht pa reference-definition para tij mbetet tekst aktiv.
    expect(evaluateChainPresence(`  "${FULL}"\n`).missing).toEqual([]);
    // Pending nuk rrjedh përtej rreshtit menjëherë vijues.
    const separated = `[flow]: /target\n    indented code\n  "${FULL}"\n`;
    expect(evaluateChainPresence(separated).missing).toEqual([]);
  });

  it("astral Unicode before inline-code preserves offsets and hides HTML markers", () => {
    expect(projectActiveMarkdown("😀😀 `x<y>`")).toBe("😀😀 `x y `");
    const hidden = projectActiveMarkdown(`😀 \`<!-- ${FULL} -->\``);
    expect(hidden).not.toContain("<!--");
    expect(hidden).not.toContain("-->");
  });

  it("a fully linked visible chain remains complete", () => {
    const linked = [
      "[Step B](#b)", "[Step C](#c)", "[independent review](#r)",
      "[HUMAN GATE](#h)", "[private PREFLIGHT](#p)", "[Tier 2](#t)",
    ].join(" → ");
    expect(evaluateChainPresence(linked + "\n")).toEqual({
      missing: [], violations: [], noActiveChain: false,
    });
  });

  it("hidden authority text is not accepted as active governance", () => {
    const repo = makeTempRepo();
    repo.write("authority.md", "<!-- VERSION: 2.0 -->\n");
    repo.write("consumer.md", "Ref v2.0\n");
    repo.commitAll("hidden authority");
    const surfaces = emptyDocSurfaces({
      authorities: [{
        id: "synthetic-version",
        kind: "version",
        authorityFile: "authority.md",
        authorityPattern: /VERSION: (\d+\.\d+)/g,
        consumers: [{ file: "consumer.md", requirement: "REQUIRED_CURRENT", pattern: /Ref v(\d+\.\d+)/g }],
      }],
    });
    expect(() => runDocCore(new GitCliReader(repo.dir), "HEAD", surfaces, undefined))
      .toThrow("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");
  });

  it("a greater-than sign inside a quoted HTML attribute cannot expose hidden authority", () => {
    const repo = makeTempRepo();
    repo.write("authority.md", '<span title="x > VERSION: 2.0">visible</span>\n');
    repo.write("consumer.md", "Ref v2.0\n");
    repo.commitAll("quoted html attribute");
    const surfaces = emptyDocSurfaces({
      authorities: [{
        id: "synthetic-version", kind: "version", authorityFile: "authority.md",
        authorityPattern: /VERSION: (\d+\.\d+)/g,
        consumers: [{ file: "consumer.md", requirement: "REQUIRED_CURRENT", pattern: /Ref v(\d+\.\d+)/g }],
      }],
    });
    expect(() => runDocCore(new GitCliReader(repo.dir), "HEAD", surfaces, undefined))
      .toThrow("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");
  });

  it("hidden consumer and PR-body values neither satisfy current nor create stale drift", () => {
    const repo = makeTempRepo();
    repo.write("authority.md", "VERSION: 2.0\n");
    repo.write("consumer.md", [
      "<!-- Ref v2.0 -->",
      "```md",
      "Ref v1.8",
      "```",
      "[ref]: /target \"Ref v1.7\"",
    ].join("\n") + "\n");
    repo.commitAll("hidden values");
    const surfaces = emptyDocSurfaces({
      authorities: [{
        id: "synthetic-version", kind: "version", authorityFile: "authority.md",
        authorityPattern: /VERSION: (\d+\.\d+)/g,
        consumers: [{ file: "consumer.md", requirement: "REQUIRED_CURRENT", pattern: /Ref v(\d+\.\d+)/g }],
      }],
    });
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surfaces, {
      body: "<!-- Ref v1.6 -->\n```md\nRef v1.5\n```\n",
    });
    expect(r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT")).toHaveLength(0);
    expect(r.findings.filter((f) => f.ruleId === "DC-CURRENT-VALUE-MISSING")).toHaveLength(1);
    expect(r.findings.filter((f) => f.ruleId === "DC-PR-BODY-STALE")).toHaveLength(0);
  });
});

describe("Hardening VIII — rendered role and table boundaries", () => {
  it("emphasized role headers and labels remain privacy-review surfaces", () => {
    const repo = makeTempRepo();
    repo.write("roles.md", [
      "| **Owner** | Status |",
      "|---|---|",
      "| Synthetic Alpha | active |",
      "<!-- ***Owner:*** Synthetic Beta -->",
    ].join("\n") + "\n");
    repo.commitAll("role markup");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", emptyDocSurfaces({
      roleRequiredSurfaces: ["roles.md"],
    }), undefined);
    expect(r.findings.filter((f) => f.ruleId === "DC-ROLE-VOCAB")).toHaveLength(2);
    expect(JSON.stringify(r)).not.toContain("Synthetic Alpha");
    expect(JSON.stringify(r)).not.toContain("Synthetic Beta");
  });

  it("comments attached to a role are privacy-reviewed, but benign comments are not roles", () => {
    const repo = makeTempRepo();
    repo.write("roles.md", [
      "**Owner:** Pronari <!-- Synthetic Gamma -->",
      "<!-- rationale teknike pa deklaratë role -->",
    ].join("\n") + "\n");
    repo.commitAll("role comment");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", emptyDocSurfaces({
      roleRequiredSurfaces: ["roles.md"],
    }), undefined);
    const roleFindings = r.findings.filter((f) => f.ruleId === "DC-ROLE-VOCAB");
    expect(roleFindings).toHaveLength(1);
    expect(roleFindings[0]?.locator).toBe("roles.md:1");
    expect(JSON.stringify(r)).not.toContain("Synthetic Gamma");
  });

  it("multiline role comments remain privacy-reviewed without content disclosure", () => {
    const repo = makeTempRepo();
    repo.write("roles.md", "<!--\nOwner: Synthetic Delta\n-->\n<!-- benign rationale only -->\n");
    repo.commitAll("multiline role comment");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", emptyDocSurfaces({
      roleRequiredSurfaces: ["roles.md"],
    }), undefined);
    const roleFindings = r.findings.filter((f) => f.ruleId === "DC-ROLE-VOCAB");
    expect(roleFindings).toHaveLength(1);
    expect(roleFindings[0]?.locator).toBe("roles.md:1");
    expect(JSON.stringify(r)).not.toContain("Synthetic Delta");
  });

  it("raw HTML cannot hide role keys or table headers", () => {
    const repo = makeTempRepo();
    repo.write("roles.md", [
      "<span>Owner:</span> Synthetic Epsilon",
      "| <span>Owner</span> | Status |",
      "|---|---|",
      "| Synthetic Zeta | active |",
    ].join("\n") + "\n");
    repo.commitAll("html role markup");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", emptyDocSurfaces({
      roleRequiredSurfaces: ["roles.md"],
    }), undefined);
    expect(r.findings.filter((f) => f.ruleId === "DC-ROLE-VOCAB")).toHaveLength(2);
    expect(r.findings.filter((f) => f.ruleId === "DC-ROLE-MARKUP")).toHaveLength(2);
    expect(JSON.stringify(r)).not.toContain("Synthetic Epsilon");
    expect(JSON.stringify(r)).not.toContain("Synthetic Zeta");
  });

  it("role labels inside blockquotes and lists remain privacy-review surfaces", () => {
    const repo = makeTempRepo();
    repo.write("roles.md", [
      "> **Owner:** Synthetic Eta",
      "- **Owner:** Synthetic Theta",
      "> - **Owner:** Synthetic Iota",
      "- [ ] **Owner:** Synthetic Kappa",
      `${"> ".repeat(9)}**Owner:** Synthetic Lambda`,
    ].join("\n") + "\n");
    repo.commitAll("contained role labels");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", emptyDocSurfaces({
      roleRequiredSurfaces: ["roles.md"],
    }), undefined);
    expect(r.findings.filter((f) => f.ruleId === "DC-ROLE-VOCAB")).toHaveLength(5);
    expect(JSON.stringify(r)).not.toContain("Synthetic Eta");
    expect(JSON.stringify(r)).not.toContain("Synthetic Theta");
    expect(JSON.stringify(r)).not.toContain("Synthetic Iota");
    expect(JSON.stringify(r)).not.toContain("Synthetic Kappa");
    expect(JSON.stringify(r)).not.toContain("Synthetic Lambda");
  });

  it("pipe escaping follows odd/even backslash parity", () => {
    expect(parseTableRow(String.raw`| A | one\|two | C |`).cells).toEqual(["A", "one|two", "C"]);
    expect(parseTableRow(String.raw`| A | one\\| | C |`).cells).toEqual(["A", "one\\", "", "C"]);
    expect(parseTableRow(String.raw`| A | one\\\|two | C |`).cells).toEqual(["A", "one\\|two", "C"]);
  });

  it("inline code closes only with an equal-length backtick run", () => {
    const p = parseTableRow("| A | ``x```|hidden`` | C |");
    expect(p.malformed).toBe(false);
    expect(p.cells).toEqual(["A", "``x```|hidden``", "C"]);
  });
});

describe("Hardening VIII — Git/history fail-closed boundaries", () => {
  it("Git color configuration cannot hide added lines", () => {
    const { repo } = repoWithBase();
    repo.git(["config", "color.ui", "always"]);
    repo.write("visible.md", "added marker\n");
    const head = repo.commitAll("colored diff");
    const content = new GitCliReader(repo.dir).addedContent(head, "visible.md");
    expect(content.lines).toEqual([{ line: 1, text: "added marker" }]);
  });

  it("malformed, colored or truncated unified diffs fail closed", () => {
    expect(() => parseUnifiedAddedLines("\u001b[31m@@ -0,0 +1 @@\n+x\n")).toThrow("DIFF_STRUCTURE_UNEXPECTED");
    expect(() => parseUnifiedAddedLines("@@ -0,0 +1,2 @@\n+only-one\n")).toThrow("DIFF_HUNK_TRUNCATED");
    expect(() => parseUnifiedAddedLines("@@ -0,0 +1 @@\n+visible\n+++ hidden\n"))
      .toThrow("DIFF_STRUCTURE_UNEXPECTED");
    expect(() => parseUnifiedAddedLines("@@ -0,0 +1 @@\n+visible\nGIT binary patch\n"))
      .toThrow("DIFF_STRUCTURE_UNEXPECTED");
  });

  it("valid multi-hunk and deletion-only diffs retain exact coverage", () => {
    expect(parseUnifiedAddedLines("@@ -1 +1 @@\n-old\n+new\n@@ -4,0 +5 @@\n+later\n")).toEqual({
      binary: false,
      hadHunk: true,
      lines: [{ line: 1, text: "new" }, { line: 5, text: "later" }],
    });
    expect(parseUnifiedAddedLines("@@ -1 +0,0 @@\n-old\n")).toEqual({
      binary: false, hadHunk: true, lines: [],
    });
    expect(() => parseUnifiedAddedLines("@@ -1,2 +1 @@\n-old\n+new\n"))
      .toThrow("DIFF_HUNK_TRUNCATED");
  });

  it("a non-empty add without a parsed hunk fails closed", () => {
    const { repo, base } = repoWithBase();
    repo.write("public.md", "non-empty\n");
    const head = repo.commitAll("public add");
    const real = new GitCliReader(repo.dir);
    const spy = new Proxy(real, {
      get(target, prop, receiver) {
        if (prop === "addedContent") {
          return () => ({ binary: false, hadHunk: false, lines: [] });
        }
        const value = Reflect.get(target, prop, receiver);
        return typeof value === "function" ? value.bind(target) : value;
      },
    }) as GitReader;
    expect(() => runHistoryPriv(spy, base, head, undefined, {
      driftClasses: [], chainRequiredSurfaces: [],
    })).toThrow("INCOMPLETE_SCAN:DIFF_HUNK_MISSING");
  });

  it("an empty add is valid without a content hunk", () => {
    const { repo, base } = repoWithBase();
    repo.write("empty.md", "");
    const head = repo.commitAll("empty add");
    expect(() => runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
      driftClasses: [], chainRequiredSurfaces: [],
    })).not.toThrow();
  });

  it("an exact public copy is detected even when its private deletion is later", () => {
    const repo = makeTempRepo();
    const privatePath = [["00", "_raw"].join(""), "synthetic.txt"].join("/");
    repo.write(privatePath, "synthetic opaque material\n");
    repo.write("README.md", "base\n");
    const base = repo.commitAll("base with blocked fixture");
    repo.write("docs/public.md", "synthetic opaque material\n");
    repo.commitAll("copy before cleanup");
    repo.git(["rm", "-q", privatePath]);
    const head = repo.commitAll("private cleanup later");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
      driftClasses: [], chainRequiredSurfaces: [],
    });
    expect(r.findings.filter((f) => f.ruleId === "HP-PRIVATE-RELOCATION")).toHaveLength(1);
    expect(r.findings.filter((f) => f.ruleId === "HP-PRIVATE-RELOCATION-UNVERIFIED")).toHaveLength(0);
  });

  it("an exact public copy is detected even without deleting the private source", () => {
    const repo = makeTempRepo();
    const privatePath = [["00", "_raw"].join(""), "synthetic.txt"].join("/");
    repo.write(privatePath, "synthetic opaque material\n");
    repo.write("README.md", "base\n");
    const base = repo.commitAll("private baseline");
    repo.write("docs/public.md", "synthetic opaque material\n");
    const head = repo.commitAll("public copy");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
      driftClasses: [], chainRequiredSurfaces: [],
    });
    expect(r.findings.filter((f) => f.ruleId === "HP-PRIVATE-RELOCATION")).toHaveLength(1);
  });

  it("modified relocation is reviewed even when the private source is retained", () => {
    const repo = makeTempRepo();
    const privatePath = [["00", "_raw"].join(""), "synthetic.txt"].join("/");
    repo.write(privatePath, "synthetic opaque material\n");
    repo.write("README.md", "base\n");
    const base = repo.commitAll("base with blocked fixture");
    repo.write("docs/public.md", "synthetic opaque material changed\n");
    const head = repo.commitAll("modified public copy with source retained");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
      driftClasses: [], chainRequiredSurfaces: [],
    });
    expect(r.findings.some((f) => f.ruleId === "HP-PRIVATE-RELOCATION-UNVERIFIED")).toBe(true);
  });

  it("unrelated adds are reviewed when a known private source exists", () => {
    const repo = makeTempRepo();
    const privatePath = [["00", "_raw"].join(""), "synthetic.txt"].join("/");
    repo.write(privatePath, "private synthetic\n");
    repo.write("README.md", "base\n");
    const base = repo.commitAll("base");
    repo.write("docs/unrelated.md", "unrelated public text\n");
    repo.git(["rm", "-q", privatePath]);
    const head = repo.commitAll("delete private and add unrelated");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
      driftClasses: [], chainRequiredSurfaces: [],
    });
    expect(r.findings.filter((f) => f.ruleId === "HP-PRIVATE-RELOCATION-UNVERIFIED")).toHaveLength(1);

    const clean = makeTempRepo();
    clean.write("README.md", "base\n");
    const cleanBase = clean.commitAll("base");
    clean.write("docs/unrelated.md", "unrelated public text\n");
    const cleanHead = clean.commitAll("public only");
    const cleanResult = runHistoryPriv(new GitCliReader(clean.dir), cleanBase, cleanHead, undefined, {
      driftClasses: [], chainRequiredSurfaces: [],
    });
    expect(cleanResult.findings.filter((f) => f.ruleId.includes("PRIVATE-RELOCATION"))).toHaveLength(0);
  });

  it("a public mode-only change is not treated as modified relocation", () => {
    const repo = makeTempRepo();
    const privatePath = [["00", "_raw"].join(""), "synthetic.txt"].join("/");
    repo.write(privatePath, "private synthetic\n");
    repo.write("public.sh", "echo synthetic\n");
    const base = repo.commitAll("base");
    repo.git(["rm", "-q", privatePath]);
    repo.git(["update-index", "--chmod=+x", "public.sh"]);
    repo.git(["commit", "-qm", "cleanup and mode only"]);
    const head = repo.git(["rev-parse", "HEAD"]).trim();
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
      driftClasses: [], chainRequiredSurfaces: [],
    });
    expect(r.findings.filter((f) => f.ruleId === "HP-PRIVATE-RELOCATION-UNVERIFIED")).toHaveLength(0);
  });

  it("a historical marker exempts only its own segment", () => {
    const repo = makeTempRepo();
    repo.write("authority.md", "VERSION: 2.0\n");
    repo.write("consumer.md", "Ref v2.0 current\n");
    const base = repo.commitAll("clean baseline");
    repo.write("consumer.md", "Ref v2.0 current; Ref v1.9 (HISTORICAL); Ref v1.8 active\n");
    const head = repo.commitAll("mixed historical segments");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
      chainRequiredSurfaces: [],
      driftClasses: [{
        id: "synthetic-version", kind: "version", authorityFile: "authority.md",
        authorityPattern: /VERSION: (\d+\.\d+)/g,
        consumers: [{ file: "consumer.md", requirement: "REQUIRED_CURRENT", pattern: /Ref v(\d+\.\d+)/g }],
      }],
    });
    const drift = r.findings.filter((f) => f.ruleId === "HP-DRIFT-VERSION");
    expect(drift).toHaveLength(1);
  });

  it("the same per-match historical rule applies to DOC-CORE and PR body", () => {
    const repo = makeTempRepo();
    repo.write("authority.md", "VERSION: 2.0\n");
    repo.write("consumer.md", "Ref v2.0 current; Ref v1.9 (HISTORICAL); Ref v1.8 active\n");
    repo.commitAll("mixed current and historical");
    const surfaces = emptyDocSurfaces({
      authorities: [{
        id: "synthetic-version", kind: "version", authorityFile: "authority.md",
        authorityPattern: /VERSION: (\d+\.\d+)/g,
        consumers: [{ file: "consumer.md", requirement: "REQUIRED_CURRENT", pattern: /Ref v(\d+\.\d+)/g }],
      }],
    });
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surfaces, {
      body: "Ref v2.0 current; Ref v1.9 (HISTORICAL); Ref v1.8 active\n",
    });
    expect(r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT")).toHaveLength(1);
    expect(r.findings.filter((f) => f.ruleId === "DC-PR-BODY-STALE")).toHaveLength(1);
  });

  it("historical exclusion follows the exact match, not another match on the row", () => {
    const repo = makeTempRepo();
    repo.write("authority.md", "VERSION: 2.0\n");
    repo.write("consumer.md", "Ref v2.0 current\n");
    const base = repo.commitAll("baseline");
    repo.write("consumer.md", "Ref v2.0 (HISTORICAL); Ref v1.8 active\n");
    const head = repo.commitAll("current historical, stale active");
    const cls = [{
      id: "synthetic-version", kind: "version" as const, authorityFile: "authority.md",
      authorityPattern: /VERSION: (\d+\.\d+)/g,
      consumers: [{ file: "consumer.md", requirement: "REQUIRED_CURRENT" as const, pattern: /Ref v(\d+\.\d+)/g }],
    }];
    const doc = runDocCore(new GitCliReader(repo.dir), head, emptyDocSurfaces({ authorities: cls }), {
      body: "Ref v2.0 (HISTORICAL); Ref v1.8 active\n",
    });
    expect(doc.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT")).toHaveLength(1);
    expect(doc.findings.filter((f) => f.ruleId === "DC-CURRENT-VALUE-MISSING")).toHaveLength(1);
    expect(doc.findings.filter((f) => f.ruleId === "DC-PR-BODY-STALE")).toHaveLength(1);
    const history = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
      driftClasses: cls, chainRequiredSurfaces: [],
    });
    expect(history.findings.filter((f) => f.ruleId === "HP-DRIFT-VERSION")).toHaveLength(1);
    expect(history.findings.filter((f) => f.ruleId === "HP-CURRENT-VALUE-MISSING")).toHaveLength(1);
  });

  it("historical per-match behavior is stable in both value orders", () => {
    const run = (line: string) => {
      const repo = makeTempRepo();
      repo.write("authority.md", "VERSION: 2.0\n");
      repo.write("consumer.md", line + "\n");
      repo.commitAll("value order");
      return runDocCore(new GitCliReader(repo.dir), "HEAD", emptyDocSurfaces({
        authorities: [{
          id: "synthetic-version", kind: "version", authorityFile: "authority.md",
          authorityPattern: /VERSION: (\d+\.\d+)/g,
          consumers: [{ file: "consumer.md", requirement: "REQUIRED_CURRENT", pattern: /Ref v(\d+\.\d+)/g }],
        }],
      }), undefined);
    };
    const historicalStale = run("Ref v1.8 (HISTORICAL); Ref v2.0 active");
    expect(historicalStale.findings.filter((f) => f.ruleId === "DC-CURRENT-VALUE-MISSING")).toHaveLength(0);
    expect(historicalStale.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT")).toHaveLength(0);
    const staleFirst = run("Ref v1.8 active; Ref v2.0 (HISTORICAL)");
    expect(staleFirst.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT")).toHaveLength(1);
    expect(staleFirst.findings.filter((f) => f.ruleId === "DC-CURRENT-VALUE-MISSING")).toHaveLength(1);
  });

  it("only an exact Status cell grants a row-wide historical exemption", () => {
    const run = (consumer: string) => {
      const repo = makeTempRepo();
      repo.write("authority.md", "VERSION: 2.0\n");
      repo.write("consumer.md", consumer);
      repo.commitAll("table case");
      return runDocCore(new GitCliReader(repo.dir), "HEAD", emptyDocSurfaces({
        authorities: [{
          id: "synthetic-version", kind: "version", authorityFile: "authority.md",
          authorityPattern: /VERSION: (\d+\.\d+)/g,
          consumers: [{ file: "consumer.md", requirement: "SCAN_IF_PRESENT", pattern: /Ref v(\d+\.\d+)/g }],
        }],
      }), undefined);
    };
    const status = run("| Ref | Status |\n|---|---|\n| Ref v1.8 | HISTORICAL |\n");
    expect(status.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT")).toHaveLength(0);
    const other = run("| Ref | Note | Status |\n|---|---|---|\n| Ref v1.8 | HISTORICAL | ACTIVE |\n");
    expect(other.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT")).toHaveLength(1);
  });
});

describe("Hardening VIII — Unicode and secret signatures", () => {
  it("confusable and combining-mark variants of blocked path stems fail closed", () => {
    const cyrillicO = "\u041e";
    expect(evaluatePath(`docs/${cyrillicO}fertaClient/terms.md`)).toBe("BLOCKED");
    expect(evaluatePath("docs/Ofer\u0301taClient/terms.md")).toBe("BLOCKED");
    expect(evaluatePath("docs/OfertaClient/terms.md")).toBe("BLOCKED");
    expect(evaluatePath("docs/Publike/udhëzues.md")).toBe("ALLOWED");
  });

  it("all default-ignorable format characters are visible to the detection skeleton", () => {
    const tagSpace = String.fromCodePoint(0xe0020);
    const raw = `Ledger v1.${tagSpace}5`;
    expect(detectionSkeleton(raw)).toBe("Ledger v1.5");
    expect(hasObfuscatedMatch(raw, /Ledger v(\d+\.\d+)/g)).toBe(true);
    expect(evaluatePath(`00${tagSpace}_raw/synthetic.txt`)).toBe("BLOCKED");
  });

  it("Git metadata with a default-ignorable path fails closed before content", () => {
    const { repo, base } = repoWithBase();
    const blob = hashBlob(repo, "synthetic\n");
    const tagSpace = String.fromCodePoint(0xe0020);
    const head = commitWithRawTree(repo, [{
      mode: "100644", type: "blob", oid: blob,
      pathBytes: Buffer.from(`synthetic${tagSpace}.md`, "utf8"),
    }], "obfuscated path", base);
    expect(() => new GitCliReader(repo.dir).rawEntries(head)).toThrow("PATH_FORMAT_CHARS");
  });

  it("classic GitHub tokens and encrypted private-key headers are detected", () => {
    const { repo, base } = repoWithBase();
    const classic = frag("g", "hp", "_", "A".repeat(32));
    const keyHeader = frag("-----", "BEGIN ", "ENCRYPTED ", "PRIVATE", " KEY", "-----");
    repo.write("synthetic.md", `${classic}\n${keyHeader}\n`);
    const head = repo.commitAll("synthetic signatures");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
      driftClasses: [], chainRequiredSurfaces: [],
    });
    expect(r.findings.some((f) => f.ruleId === "HP-TOKEN-CLASS")).toBe(true);
    expect(r.findings.some((f) => f.ruleId === "HP-PRIVATE-KEY-BLOCK")).toBe(true);
    expect(JSON.stringify(r)).not.toContain(classic);
    expect(JSON.stringify(r)).not.toContain(keyHeader);
  });

  it("DSA private-key headers are detected without disclosure", () => {
    const { repo, base } = repoWithBase();
    const header = frag("-----", "BEGIN ", "DSA ", "PRIVATE", " KEY", "-----");
    repo.write("synthetic.md", header + "\n");
    const head = repo.commitAll("synthetic dsa header");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
      driftClasses: [], chainRequiredSurfaces: [],
    });
    expect(r.findings.filter((f) => f.ruleId === "HP-PRIVATE-KEY-BLOCK")).toHaveLength(1);
    expect(JSON.stringify(r)).not.toContain(header);
  });

  it("all documented classic GitHub token prefixes are covered", () => {
    for (const prefix of ["p", "o", "u", "s", "r"]) {
      const { repo, base } = repoWithBase();
      const candidate = frag("g", "h", prefix, "_", "A".repeat(32));
      repo.write("synthetic.md", candidate + "\n");
      const head = repo.commitAll(`synthetic prefix ${prefix}`);
      const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
        driftClasses: [], chainRequiredSurfaces: [],
      });
      expect(r.findings.filter((f) => f.ruleId === "HP-TOKEN-CLASS")).toHaveLength(1);
    }
    const { repo, base } = repoWithBase();
    repo.write("near-miss.md", frag("g", "hx", "_", "A".repeat(32)) + "\n");
    const head = repo.commitAll("near miss");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
      driftClasses: [], chainRequiredSurfaces: [],
    });
    expect(r.findings.filter((f) => f.ruleId === "HP-TOKEN-CLASS")).toHaveLength(0);
  });
});
