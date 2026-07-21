/**
 * P-VII — regression: Fix A (negacion claim-scoped), Fix B (counter "jo"),
 * Fix F (chain-tokens Unicode-aware). (Fix C/D/E shtohen në commit-in pasues.)
 * Path-e/emra VETËM sintetikë; Unicode runtime nga code points.
 */
import { describe, expect, it } from "vitest";
import { makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { evaluateChainPresence, negatedChainClaim } from "../src/profiles/doc-core.js";
import { runHistoryPriv } from "../src/profiles/history-priv.js";

const FULL = "Step B → Step C → independent review → HUMAN GATE → private PREFLIGHT → Tier 2";
const WRONG = "Step C → Step B → independent review → HUMAN GATE → private PREFLIGHT → Tier 2";

function repoWithBase() {
  const repo = makeTempRepo();
  repo.write("README.md", "synthetic base\n");
  const base = repo.commitAll("base");
  return { repo, base };
}

describe("P-VII Fix A — chain negation CLAIM-scoped", () => {
  it("OVER-SUPPRESSION e mbyllur: 'Nuk ndryshon rendi:' dhe 'Nuk ka PII, rendi aktual:' → zinxhiri AKTIV, zero mohim", () => {
    for (const intro of ["Nuk ndryshon rendi: ", "Nuk ka PII, rendi aktual: ", "Nuk ndryshohet zinxhiri: "]) {
      expect(negatedChainClaim(intro + FULL), intro).toBe(false);
      const e = evaluateChainPresence(intro + FULL + "\n");
      expect(e.noActiveChain, intro).toBe(false);
      expect(e.missing, intro).toEqual([]);
      expect(e.violations, intro).toEqual([]);
    }
  });

  it("rend i plotë korrekt → aktiv me ZERO finding; rend i GABUAR nën intro jo-mohuese → FINDING", () => {
    const ok = evaluateChainPresence("Rendi i detyrueshëm: " + FULL + "\n");
    expect(ok.missing).toEqual([]);
    expect(ok.violations).toEqual([]);
    for (const intro of ["Nuk ndryshon rendi: ", "Jo vetëm ky rend po zbatohet: ", "ç'është ky rend: "]) {
      const e = evaluateChainPresence(intro + WRONG + "\n");
      expect(e.noActiveChain, intro).toBe(false);
      expect(e.violations.length, intro).toBeGreaterThan(0); // step-c para step-b
    }
  });

  it("mohim i VËRTETË i zinxhirit → suppressed; edhe kur vjen PAS token-it të parë", () => {
    expect(negatedChainClaim("s'është ky rendi: " + FULL)).toBe(true);
    expect(negatedChainClaim("Step B nuk është ky rendi: " + FULL)).toBe(true); // analizë e TËRË claim-it
    expect(evaluateChainPresence("Step B nuk është ky rendi: " + FULL + "\n").noActiveChain).toBe(true);
  });

  it("'Jo vetëm ky rend' NUK është mohim (dëftori i menjëhershëm kërkohet)", () => {
    expect(negatedChainClaim("Jo vetëm ky rend po ndiqet: " + FULL)).toBe(false);
    expect(negatedChainClaim("jo ky rend: " + FULL)).toBe(true); // forma e vërtetë mbetet
  });
});

describe("P-VII Fix B — F5 counter numëron 'jo'", () => {
  const run = (content: string) => {
    const { repo, base } = repoWithBase();
    repo.write("docs/n.md", content);
    const head = repo.commitAll("c");
    return runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined,
      { driftClasses: [], chainRequiredSurfaces: [] });
  };

  it("'jo s'raportohet xhiroja ditore reale 789' → FIRES (789 nuk fshihet më)", () => {
    const r = run("jo s'raportohet xhiroja ditore reale 789\n");
    expect(r.findings.filter((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toHaveLength(1);
  });

  it("rregresion: 'nuk s'raportohet …' → FIRES; mohim i vetëm legjitim → suppress; 'jo' brenda fjale ≠ negator", () => {
    expect(run("nuk s'raportohet xhiroja ditore këtu\n").findings.filter((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toHaveLength(1);
    expect(run("nuk raportohet xhiroja ditore këtu\n").findings.filter((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toHaveLength(0);
    // 'majo'/'bojkot' — 'jo' brenda fjale nuk numërohet: mohimi i vetëm mbetet suppress
    expect(run("majo: nuk raportohet xhiroja ditore\n").findings.filter((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toHaveLength(0);
  });
});

describe("P-VII Fix F — chain-tokens UNICODE-aware ('Step Bë' ≠ token)", () => {
  it("'Step Bë → … → Tier 2ë' NUK është zinxhir valid", () => {
    const fake = "Step Bë → Step Cë → independent reviewë → HUMAN GATEë → private PREFLIGHTë → Tier 2ë";
    const e = evaluateChainPresence(fake + "\n");
    expect(e.noActiveChain).toBe(true);
  });

  it("boundary: '**Step B**' dhe 'Step B,' njihen; inverse: zinxhiri normal i plotë", () => {
    const decorated = "**Step B** → **Step C** → independent review → HUMAN GATE, private PREFLIGHT → Tier 2.";
    const e = evaluateChainPresence(decorated + "\n");
    expect(e.noActiveChain).toBe(false);
    expect(e.missing).toEqual([]);
    const normal = evaluateChainPresence(FULL + "\n");
    expect(normal.missing).toEqual([]);
  });

  it("token i vetëm i prishur: 'Tier 2ë' mungon nga prania — kapet si missing", () => {
    const partial = "Step B → Step C → independent review → HUMAN GATE → private PREFLIGHT → Tier 2ë";
    const e = evaluateChainPresence(partial + "\n");
    expect(e.noActiveChain).toBe(false);
    expect(e.missing).toEqual(["tier-2"]);
  });
});
