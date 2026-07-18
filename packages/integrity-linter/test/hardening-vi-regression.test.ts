/**
 * P-VI — regression i fokusuar: §1 Unicode-aware negation, §2 etiketa
 * historike strukturore. (§3/§4 shtohen në commit-in pasues të këtij file-i.)
 * Karakteret ndërtohen runtime; burimi mbetet ASCII i pastër.
 */
import { describe, expect, it } from "vitest";
import { makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runDocCore, evaluateChainPresence, negatedChainClaim, type DocCoreSurfaces } from "../src/profiles/doc-core.js";
import { isHistoryLabeledRow, isHistoryPrefixLabeled } from "../src/authority-classes.js";
import { runHistoryPriv } from "../src/profiles/history-priv.js";

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
    "ç'është ky rend: ",
    `ç${CURLY}është ky rend: `,
    "nuk është ky rendi: ",
    "jo ky rend: ",
    "mos e konsidero këtë rend: ",
    "S'ËSHTË KY RENDI: ",
    `s${MODAP}është ky rendi: `, // formë Unicode ekuivalente (modifier apostrophe)
  ];

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
    expect(isHistoryLabeledRow("Ledger v1.4 **[SUPERSEDED v2.25: REOPENED]**")).toBe(true);
    expect(isHistoryLabeledRow("| L01 | Ledger v1.4 | SUPERSEDED | x |")).toBe(true);
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
