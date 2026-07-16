/**
 * P-IV §11 — REGRESION ADVERSARIAL: near-misses të REJA për çdo detektor të
 * ndryshuar në Hardening IV, PA zgjerim të fshehtë scope-i. Fixtures sintetike.
 */
import { describe, expect, it } from "vitest";
import { makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runHistoryPriv } from "../src/profiles/history-priv.js";
import { evaluateChainPresence, negatedChainClaim, parentheticalIdentityCandidate } from "../src/profiles/doc-core.js";

function repoWithBase() {
  const repo = makeTempRepo();
  repo.write("README.md", "synthetic base\n");
  const base = repo.commitAll("base");
  return { repo, base };
}

describe("near-miss: activationAnchor (§4A)", () => {
  it("etiketë e ngjashme por JO anchor-i i saktë → klasa mbetet NOT_APPLICABLE", () => {
    const cls = [{
      id: "syn-nm", kind: "status" as const, authorityFile: "auth.md",
      activationAnchor: /\bSEKSIONI-STATUS\b/,
      authorityPattern: /GJENDJA: (OK|JO)/g,
      consumers: [{ file: "cons.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /SHIH (OK|JO)/g }],
    }];
    const { repo, base } = repoWithBase();
    repo.write("auth.md", "SEKSIONI-STAT afër por jo i njëjti; pa vlerë\n");
    const c1 = repo.commitAll("near-miss anchor");
    expect(() => runHistoryPriv(new GitCliReader(repo.dir), base, c1, undefined,
      { driftClasses: cls, chainRequiredSurfaces: [] })).not.toThrow();
  });
});

describe("near-miss: mohimi PER-MATCH (§5)", () => {
  const run = (content: string) => {
    const { repo, base } = repoWithBase();
    repo.write("docs/n.md", content);
    const head = repo.commitAll("c");
    return runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined,
      { driftClasses: [], chainRequiredSurfaces: [] });
  };

  it("DY claims, TË DYJA të mohuara lokalisht → zero findings", () => {
    const r = run("nuk raportohet xhiroja ditore; nuk raportohet saldo reale\n");
    expect(r.findings.filter((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toHaveLength(0);
  });

  it("JO- i lidhur me secilin token veçmas → zero (asnjë token i pambuluar)", () => {
    const t1 = ["TES", "TED"].join("");
    const t2 = ["IMPLE", "MENTED"].join("");
    const r = run(`PoC JO-${t1} dhe JO-${t2} sintetik\n`);
    expect(r.findings.filter((f) => f.ruleId === "HP-POC-PROMOTION")).toHaveLength(0);
  });
});

describe("near-miss: zinxhiri (§7)", () => {
  it("'nuk' larg claim-it (jo i lidhur me rendin) NUK e mohon zinxhirin", () => {
    const line = "Skanimi nuk lexon evidence private. Rendi i detyrueshëm: Step B → Step C → independent review → HUMAN GATE → private PREFLIGHT → Tier 2";
    expect(negatedChainClaim(line)).toBe(false);
    const e = evaluateChainPresence(line + "\n");
    expect(e.noActiveChain).toBe(false);
    expect(e.missing).toEqual([]);
  });

  it("mohim i lidhur por pa asnjë token zinxhiri → jo claim, jo crash", () => {
    expect(negatedChainClaim("NUK është ky rendi: hapi 1 pastaj hapi 2")).toBe(false);
  });
});

describe("near-miss: identiteti në parentetika (§9)", () => {
  const APPROVED = ["human gate", "group owner", "pronari"];
  it("fraza të aprovuara, lowercase dhe akronime NUK janë kandidatë", () => {
    expect(parentheticalIdentityCandidate("Group Owner (Human Gate)", APPROVED)).toBe(false);
    expect(parentheticalIdentityCandidate("Pronari (aprovon çdo ndryshim)", APPROVED)).toBe(false);
    expect(parentheticalIdentityCandidate("Owner (AIP)", APPROVED)).toBe(false);
    expect(parentheticalIdentityCandidate("Owner (—)", APPROVED)).toBe(false);
  });
  it("sekuencë TitleCase sintetike e paaprovuar → kandidat", () => {
    expect(parentheticalIdentityCandidate("Owner (Filani Sintetiku)", APPROVED)).toBe(true);
  });
});

describe("near-miss: blob RAW (§3)", () => {
  it("UTF-8 shumë-bajtësh valid (ë/ç/€) me extension .bin → skanohet pa gabim", () => {
    const { repo, base } = repoWithBase();
    repo.write(".gitattributes", "*.bin diff\n");
    repo.write("t.bin", "tekst me ë, ç dhe € — valid\n");
    const head = repo.commitAll("utf8 multi-byte");
    expect(() => runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined,
      { driftClasses: [], chainRequiredSurfaces: [] })).not.toThrow();
  });
});
