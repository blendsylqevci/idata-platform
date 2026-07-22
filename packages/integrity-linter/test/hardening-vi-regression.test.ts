/**
 * P-VI — regression i fokusuar: §1 Unicode-aware negation, §2 etiketa
 * historike strukturore. (§3/§4 shtohen në commit-in pasues të këtij file-i.)
 * Karakteret ndërtohen runtime; burimi mbetet ASCII i pastër.
 */
import { describe, expect, it } from "vitest";
import { makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runDocCore, evaluateChainPresence, negatedChainClaim, type DocCoreSurfaces } from "../src/profiles/doc-core.js";
import { isHistoryLabeledRow, isHistoryLabeledRowInDoc, isHistoryPrefixLabeled } from "../src/authority-classes.js";
import { runHistoryPriv } from "../src/profiles/history-priv.js";
import { containsLocalAbsolutePath } from "../src/path-policy.js";
import { tableContextAt } from "../src/markdown-table.js";
import { toJsonReport, toTextReport } from "../src/reporter.js";

const CURLY = String.fromCharCode(0x2019);   // ’
const MODAP = String.fromCharCode(0x02bc);   // ʼ modifier apostrophe
const LSEP = String.fromCharCode(0x2028);    // U+2028 line separator
const FULL = "Step B → Step C → independent review → HUMAN GATE → private PREFLIGHT → Tier 2";

describe("P-VI §1 — mohim UNICODE-aware i zinxhirit", () => {
  const negForms = [
    "s'është ky rendi: ",
    `s${CURLY}është ky rendi: `,
    "s'janë këto hapat e rendit: ",
    `s${CURLY}janë këto hapat e rendit: `,
    "nuk është ky rendi: ",
    "jo ky rend: ",
    "mos e konsidero këtë rend: ",
    "S'ËSHTË KY RENDI: ",
    `s${MODAP}është ky rendi: `, // formë Unicode ekuivalente (modifier apostrophe)
  ];

  // P-VII Fix A supersedes P-VI: "ç'është …" është PYETJE, jo mohim — kaloi te aktivet.
  it("çdo formë mohimi → negatedChainClaim=true dhe JO chain aktiv", () => {
    for (const neg of negForms) {
      expect(negatedChainClaim(neg + FULL), JSON.stringify(neg)).toBe(true);
      expect(evaluateChainPresence(neg + FULL + "\n").noActiveChain, JSON.stringify(neg)).toBe(true);
    }
  });

  it("aktive të ruajtura: 'zinxhiri nuk ndryshon' / 'rendi mbetet i pandryshuar' / mohim në fjali tjetër / 'nuk' i palidhur", () => {
    for (const act of [
      "Zinxhiri nuk ndryshon: ",
      "Rendi mbetet i pandryshuar: ",
      "Nuk ka përjashtime të reja. Rendi aktual: ",
      "Skanimi nuk lexon evidence private. Rendi i detyrueshëm: ",
      "ç'është ky rend: ",              // P-VII: pyetje ≠ mohim
      `ç${CURLY}është ky rend: `,
    ]) {
      expect(negatedChainClaim(act + FULL), JSON.stringify(act)).toBe(false);
      const e = evaluateChainPresence(act + FULL + "\n");
      expect(e.noActiveChain, JSON.stringify(act)).toBe(false);
      expect(e.missing, JSON.stringify(act)).toEqual([]);
    }
  });

  it("U+2028 si delimitues fjalie: mohimi para separatorit NUK e mohon zinxhirin pas tij", () => {
    const line = `s'është ai rendi i vjetër${LSEP}Rendi aktual: ` + FULL;
    expect(negatedChainClaim(line)).toBe(false); // mohimi mbetet në "fjalinë" para U+2028
  });

  it("U+2028 brenda protected machine token → kontrata ekzistuese e obfuskimit (jo silent)", () => {
    const repo = makeTempRepo();
    repo.write("auth.md", "| SYN-01 | x | Faza 5 |\n");
    repo.write("led.md", `FISCAL-F${LSEP}5\n`);
    repo.commitAll("u2028 token");
    const surf: DocCoreSurfaces = {
      orderSurfaces: [], chainRequiredSurfaces: [], milestoneSurfaces: [],
      approvedRolePhrases: [], roleRequiredSurfaces: [], roleOptionalSurfaces: [],
      authorities: [{
        id: "syn-ph", kind: "phase", authorityFile: "auth.md",
        authorityPattern: /\| SYN-01 \|[^\n]*\| Faza (\d+) \|/g,
        consumers: [{ file: "led.md", requirement: "SCAN_IF_PRESENT", pattern: /FISCAL-F(\d+)/g }],
      }],
    };
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surf, undefined);
    expect(r.findings.filter((f) => f.ruleId === "DC-MACHINE-TOKEN-OBFUSCATED")).toHaveLength(1);
  });
});

describe("P-VI §2 — etiketa historike VETËM strukturore", () => {
  it("dy rastet CURRENT të konfirmuara → JO historike", () => {
    expect(isHistoryLabeledRow("CURRENT: Ledger v1.5 nuk është superseded")).toBe(false);
    expect(isHistoryLabeledRow("CURRENT: Ledger v1.5 nuk është ish")).toBe(false);
  });

  it("prefikset strukturore → historike; markerat paren/bracket → historike; qeliza ekzakte → historike", () => {
    expect(isHistoryPrefixLabeled("HISTORICAL: Ledger v1.4")).toBe(true);
    expect(isHistoryLabeledRow("HISTORICAL: Ledger v1.4")).toBe(true);
    expect(isHistoryLabeledRow("SUPERSEDED: Ledger v1.4")).toBe(true);
    expect(isHistoryLabeledRow("HISTORIK: Ledger v1.4")).toBe(true);
    expect(isHistoryLabeledRow("**Historia:** v2.2 — ...")).toBe(true);
    expect(isHistoryLabeledRow("Ledger v1.4 (SUPERSEDED)")).toBe(true);
    // P-VII Fix C supersedes P-VI: vetem marker i MBYLLUR ekzakt — bracket-i me
    // suffix "[SUPERSEDED v2.25: …]" NUK eshte me etikete (behet rresht aktiv).
    expect(isHistoryLabeledRow("Ledger v1.4 **[SUPERSEDED v2.25: REOPENED]**")).toBe(false);
    // P-VII: qeliza vleresohet VETEM doc-aware ne kolonen Status (shih testin InDoc)
    expect(isHistoryLabeledRow("| L01 | Ledger v1.4 | SUPERSEDED | x |")).toBe(false);
    const doc = ["| ID | Vlera | Status |", "|---|---|---|", "| L01 | Ledger v1.4 | SUPERSEDED |", "| L02 | SUPERSEDED fjale | AKTIV |"].join("\n");
    expect(isHistoryLabeledRowInDoc(doc, 3)).toBe(true);  // qeliza ekzakte NE kolonen Status
    expect(isHistoryLabeledRowInDoc(doc, 4)).toBe(false); // fjala ne kolone tjeter s'exempt-on
  });

  it("fjalët e lira NUK përjashtojnë: prose superseded/ish/atëhershëm, 'superseded wording'", () => {
    expect(isHistoryLabeledRow("kjo fjali e përmend superseded wording pa etiketë")).toBe(false);
    expect(isHistoryLabeledRow("versioni i atëhershëm ishte ndryshe")).toBe(false);
    expect(isHistoryLabeledRow("ish drejtuesi e përshkroi")).toBe(false);
    expect(isHistoryLabeledRow("| L01 | teksti superseded në prozë | ok | x |")).toBe(false); // qeliza JO-ekzakte
  });

  it("etiketa në një rresht NUK përjashton rreshtin fqinj (drift integration)", () => {
    const repo = makeTempRepo();
    repo.write("led.md", "**Versioni:** 1.6\n");
    repo.write("idx.md", [
      "HISTORICAL: Ledger v1.4",
      "CURRENT: Ledger v1.5 nuk është superseded",
      "Ledger v1.6 aktual",
    ].join("\n") + "\n");
    repo.commitAll("docs");
    const surf: DocCoreSurfaces = {
      orderSurfaces: [], chainRequiredSurfaces: [], milestoneSurfaces: [],
      approvedRolePhrases: [], roleRequiredSurfaces: [], roleOptionalSurfaces: [],
      authorities: [{
        id: "syn-ver", kind: "version", authorityFile: "led.md",
        authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/g,
        consumers: [{ file: "idx.md", requirement: "REQUIRED_CURRENT", pattern: /Ledger v(\d+\.\d+)/g }],
      }],
    };
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surf, undefined);
    const drift = r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT");
    expect(drift).toHaveLength(1); // VETËM rreshti 2 (CURRENT i pa-etiketuar); rreshti 1 historik
    expect(drift[0]?.locator).toBe("idx.md:2 (syn-ver)");
  });

  it("Step A (HISTORICAL) → Step B ...: vetëm segmenti A historik — zinxhiri aktiv i plotë", () => {
    const line = "Step A (HISTORICAL) → " + FULL;
    const e = evaluateChainPresence(line + "\n");
    expect(e.noActiveChain).toBe(false);
    expect(e.missing).toEqual([]);
  });

  it("engine HISTORY-PRIV përdor të njëjtin klasifikim qendror (pa regex kontradiktor)", () => {
    const repo = makeTempRepo();
    repo.write("README.md", "base\n");
    const base = repo.commitAll("base");
    repo.write("led.md", "**Versioni:** 1.6\n");
    repo.commitAll("auth");
    repo.write("idx.md", "CURRENT: Ledger v1.5 nuk është superseded\n");
    const head = repo.commitAll("stale me fjale te lire");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, {
      driftClasses: [{
        id: "syn-ver", kind: "version", authorityFile: "led.md",
        authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/g,
        consumers: [{ file: "idx.md", requirement: "SCAN_IF_PRESENT", pattern: /Ledger v(\d+\.\d+)/g }],
      }],
      chainRequiredSurfaces: [],
    });
    expect(r.findings.filter((f) => f.ruleId === "HP-DRIFT-VERSION")).toHaveLength(1);
  });
});

describe("P-VI \u00a73 \u2014 CommonMark fenced-block state machine", () => {
  const TBL = [
    "Domeni | Prova | Maturity | Treatment",
    "--- | --- | --- | ---",
    "x | prova | MODELED TESTED | LATER",
  ];

  it("FIXTURE BAZ\u00cb: opener 4-backtick + inner 3-backtick (CONTENT) + closer 4 \u2192 tabela pasuese skanohet", () => {
    const s = ["````md", "```", "brenda fence", "````", ...TBL].join("\n");
    const ctx = tableContextAt(s, 7);
    expect(ctx?.headerArity).toBe(4);
    expect(ctx?.columnIndex(/Treatment/i)).toBe(3);
  });

  it("ENGINE: tabela pas closer-it valid prodhon findings Treatment/Maturity ekzistuese", () => {
    const repo = makeTempRepo();
    repo.write("README.md", "base\n");
    const base = repo.commitAll("base");
    repo.write("docs/f.md", ["````md", "```", "````", ...TBL].join("\n") + "\n");
    const head = repo.commitAll("fence + tabele");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, { driftClasses: [], chainRequiredSurfaces: [] });
    const ids = r.findings.map((f) => f.ruleId);
    expect(ids).toContain("HP-TREATMENT-VOCAB");
    expect(ids).toContain("HP-MIXED-MATURITY");
  });

  it("opener 4 / inner 3 / closer 5 \u2192 mbyllet; backtick opener + tilde inner = CONTENT; tilde opener/closer OK", () => {
    const s1 = ["````", "```", "`````", ...TBL].join("\n");
    expect(tableContextAt(s1, 6)?.headerArity).toBe(4);
    const s2 = ["````", "~~~", "````", ...TBL].join("\n");
    expect(tableContextAt(s2, 6)?.headerArity).toBe(4);
    const s3 = ["~~~info", "```", "~~~", ...TBL].join("\n");
    expect(tableContextAt(s3, 6)?.headerArity).toBe(4);
  });

  it("closer me info string NUK mbyll; fence e pambyllur p\u00ebrjashton deri n\u00eb fund", () => {
    const withInfo = ["````", "```` jo-closer-me-info", ...TBL].join("\n");
    expect(tableContextAt(withInfo, 5)).toBeUndefined();
    const unclosed = ["````md", "```", ...TBL].join("\n");
    expect(tableContextAt(unclosed, 5)).toBeUndefined();
  });

  it("fence n\u00eb blockquote + nested: inner 3 brenda 4 mbetet content edhe me depth", () => {
    const s = ["> ````", "> ```", "> ````", "> " + TBL[0], "> " + TBL[1], "> " + TBL[2]].join("\n");
    expect(tableContextAt(s, 6)?.headerArity).toBe(4);
    const inside = ["> ````", "> ```", "> " + TBL[0], "> " + TBL[1], "> " + TBL[2]].join("\n");
    expect(tableContextAt(inside, 5)).toBeUndefined();
  });

  it("tabela BRENDA fence = e padukshme (inverse)", () => {
    const s = ["````", ...TBL, "````"].join("\n");
    expect(tableContextAt(s, 4)).toBeUndefined();
  });
});

describe("P-VI \u00a74 \u2014 absolute local path classifier + anti-FP", () => {
  const R = (a: string, b: string) => a + b;
  const roots = [
    R("/Us", "ers/synthetic-user/x"), R("/ho", "me/user/x"), R("/ro", "ot/x"),
    R("/tm", "p/scratch/f.txt"), R("/priv", "ate/tmp/f"), R("/va", "r/folders/zz/f"),
    R("/op", "t/tool"), R("/et", "c/hosts"), R("/Volu", "mes/Disk/f"),
    R("/Applica", "tions/App.app"), R("/Libr", "ary/Pref"), R("/usr/", "local/bin/x"),
    R("/mn", "t/data"), R("/sr", "v/www"),
  ];

  it("t\u00eb gjitha rr\u00ebnj\u00ebt pozitive + ~/ + Windows + UNC \u2192 true (edhe n\u00eb code span/proz\u00eb)", () => {
    for (const p of roots) expect(containsLocalAbsolutePath("shih " + p + " k\u00ebtu"), p).toBe(true);
    expect(containsLocalAbsolutePath("cfg te ~/config/f.toml")).toBe(true);
    expect(containsLocalAbsolutePath("C:\\Users\\x\\f.txt")).toBe(true);
    expect(containsLocalAbsolutePath("D:/data/f.txt")).toBe(true);
    expect(containsLocalAbsolutePath("\\\\server\\share\\f.txt")).toBe(true);
    expect(containsLocalAbsolutePath("ekst " + R("\\\\?", "\\") + "C:\\x")).toBe(true);
    expect(containsLocalAbsolutePath("n\u00eb code span: `" + R("/tm", "p/x.sh") + "`")).toBe(true);
  });

  it("ANTI-FP: URL, endpoints fiskale, path relative \u2192 false", () => {
    expect(containsLocalAbsolutePath("shih https://example.com" + R("/Us", "ers/profil"))).toBe(false);
    expect(containsLocalAbsolutePath("thirrja POST /ca/verify dhe /ca/signcsr")).toBe(false);
    expect(containsLocalAbsolutePath("endpoint /pos/coupon dhe /citizen/receipts")).toBe(false);
    expect(containsLocalAbsolutePath("file docs/canonical/x.md dhe packages/y.ts")).toBe(false);
    expect(containsLocalAbsolutePath("raport 10:30/dita e madhe")).toBe(false);
  });

  it("ENGINE + PRIVACY: kandidati kurr\u00eb n\u00eb text/JSON output \u2014 vet\u00ebm locator", () => {
    const repo = makeTempRepo();
    repo.write("README.md", "base\n");
    const base = repo.commitAll("base");
    const marker = R("/priv", "ate/tmp/SECRETDIR-SYN/f.txt");
    repo.write("docs/n.md", "log: " + marker + "\n");
    const head = repo.commitAll("abs path mention");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, { driftClasses: [], chainRequiredSurfaces: [] });
    const hits = r.findings.filter((f) => f.ruleId === "HP-ABSOLUTE-LOCAL-PATH");
    expect(hits).toHaveLength(1);
    expect(hits[0]?.locator).toBe("docs/n.md:1");
    const result = { mode: "advisory" as const, evaluations: r.evaluations, findings: r.findings };
    expect(toJsonReport(result)).not.toContain("SECRETDIR-SYN");
    expect(toTextReport(result)).not.toContain("SECRETDIR-SYN");
  });
});
