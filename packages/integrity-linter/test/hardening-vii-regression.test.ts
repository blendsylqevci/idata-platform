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
import { isHistoryLabeledRow, isHistoryLabeledRowInDoc } from "../src/authority-classes.js";
import { containsLocalAbsolutePath } from "../src/path-policy.js";
import { tableContextAt } from "../src/markdown-table.js";

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

describe("P-VII Fix C — etiketa historike EKZAKTE (marker i mbyllur / prefix / qelize-status)", () => {
  it("5 rastet e prishura → JO historike (drift/aktiv kapet)", () => {
    expect(isHistoryLabeledRow("Ledger v1.4 (SUPERSEDED wording pa mbyllje")).toBe(false);
    expect(isHistoryLabeledRow("Ledger v1.4 (HISTORICALIZED)")).toBe(false);
    expect(isHistoryLabeledRow("proza thote SUPERSEDED diku")).toBe(false);
    const doc = ["| ID | Koment | Status |", "|---|---|---|", "| L01 | SUPERSEDED permendet | AKTIV |"].join("\n");
    expect(isHistoryLabeledRowInDoc(doc, 3)).toBe(false); // fjala ne qelize JO-status
    const noStatusCol = ["| ID | Vlera |", "|---|---|", "| L01 | SUPERSEDED |"].join("\n");
    expect(isHistoryLabeledRowInDoc(noStatusCol, 3)).toBe(false); // pa kolone statusi
  });

  it("3 sintaksat legjitime → exempt (prefix, marker i mbyllur, qelize-status)", () => {
    expect(isHistoryLabeledRow("SUPERSEDED: Ledger v1.4")).toBe(true);
    expect(isHistoryLabeledRow("Ledger v1.4 (HISTORICAL)")).toBe(true);
    const doc = ["| ID | Vlera | Statusi |", "|---|---|---|", "| L01 | Ledger v1.4 | HISTORICAL |"].join("\n");
    expect(isHistoryLabeledRowInDoc(doc, 3)).toBe(true);
  });

  it("'Step B (HISTORICAL) → Step C …': blankohet VETEM Step B — zinxhiri vleresohet me step-b missing", () => {
    const line = "Step B (HISTORICAL) → Step C → independent review → HUMAN GATE → private PREFLIGHT → Tier 2";
    const e = evaluateChainPresence(line + "\n");
    expect(e.noActiveChain).toBe(false);
    expect(e.missing).toEqual(["step-b"]);
  });
})

describe("P-VII Fix H — aside-t inline `[...]` s'jane tokene te zinxhirit te renditur", () => {
  it("status i ngulitur `[SUPERSEDED … Tier 2 …]` PARA zinxhirit korrekt → ZERO shkelje rendi (false-positive i mbyllur)", () => {
    const line = "ATK-002 **[SUPERSEDED v2.25: REOPENED — Tier 2 NOT IMPLEMENTED/NO-GO]**. Rendi aktual: " +
      "PR #18 merge → ATK closure → private PREFLIGHT → Tier 2";
    const e = evaluateChainPresence(line + "\n");
    expect(e.violations).toEqual([]); // "Tier 2" i bracket-it nuk numerohet ne rend
  });

  it("inverse: bracket-strip NUK fsheh nje shkelje REALE ne vete sekuencen me shigjeta", () => {
    const wrong = "shenim [aside] " + WRONG; // Step C → Step B ne zinxhirin real
    const e = evaluateChainPresence(wrong + "\n");
    expect(e.violations.length).toBeGreaterThan(0);
  });

  it("boundary: bracket i pambyllur nuk konsumon tere rreshtin (ende evaluon zinxhirin)", () => {
    const line = "[shenim pa mbyllje Tier 2 status → " + FULL;
    const e = evaluateChainPresence(line + "\n");
    expect(e.noActiveChain).toBe(false);
  });
})

describe("P-VII Fix D — path classifier: kufi absolut + skema + UNC/file/vscode + case", () => {
  const R = (a: string, b: string) => a + b;

  it("FALSE-POSITIVE te mbyllura: word/tmp relative; C:/ brenda https URL", () => {
    expect(containsLocalAbsolutePath("word" + R("/tm", "p/relative"))).toBe(false);
    expect(containsLocalAbsolutePath("shih https://example.com/C:/temp/file")).toBe(false);
    expect(containsLocalAbsolutePath("docs" + R("/tm", "p/x"))).toBe(false);
  });

  it("MISS te mbyllura: UNC (edhe ne pikesim), file:///, vscode://file, /users case", () => {
    expect(containsLocalAbsolutePath("shih \\\\srv1\\share\\file.txt")).toBe(true);
    expect(containsLocalAbsolutePath("(\\\\srv1\\share\\f)")).toBe(true);
    expect(containsLocalAbsolutePath("hape file://" + R("/tm", "p/a"))).toBe(true);
    expect(containsLocalAbsolutePath("vscode://file" + R("/Us", "ers/synthetic-user/x"))).toBe(true);
    expect(containsLocalAbsolutePath("log te " + R("/us", "ers/synthetic-user/f"))).toBe(true); // lowercase
    expect(containsLocalAbsolutePath(R("/Volu", "mes/Disk/f"))).toBe(true); // ne fillim te rreshtit
  });

  it("URL-only → zero; URL + path i DYTE lokal ne te njejtin rresht → path-i kapet", () => {
    expect(containsLocalAbsolutePath("shih https://example.com/faqja dhe README")).toBe(false);
    expect(containsLocalAbsolutePath("https://example.com/x dhe lokal " + R("/tm", "p/f.log"))).toBe(true);
  });

  it("inverse: endpoints fiskale dhe path relative → zero", () => {
    expect(containsLocalAbsolutePath("POST /ca/verify pastaj /ca/signcsr")).toBe(false);
    expect(containsLocalAbsolutePath("endpoint /pos/coupon dhe /citizen/receipts")).toBe(false);
    expect(containsLocalAbsolutePath("packages/integrity-linter/src/cli.ts")).toBe(false);
  });
})

describe("P-VII Fix E — CommonMark fence: indentim 0–3 + info-string pa backtick", () => {
  const TBL = ["Domeni | Prova | Maturity | Treatment", "--- | --- | --- | ---", "x |  | MODELED TESTED | LATER"];

  it("fence me 4–6 hapesira NUK eshte fence — tabela pas saj skanohet", () => {
    for (const ind of ["    ", "     ", "      "]) {
      const st = [ind + "```", ...TBL].join("\n");
      expect(tableContextAt(st, 3)?.headerArity, JSON.stringify(ind)).toBe(4);
    }
  });

  it("backtick ne info-string te backtick-fence → JO fence valid; tilde-fence me backtick ne info → fence", () => {
    const notFence = ["``` info`me`backtick", ...TBL].join("\n");
    expect(tableContextAt(notFence, 3)?.headerArity).toBe(4); // s'u hap fence — tabela e dukshme
    const tilde = ["~~~ info`me`backtick", ...TBL, "~~~"].join("\n");
    expect(tableContextAt(tilde, 3)).toBeUndefined(); // tilde e lejon — tabela brenda fence
  });

  it("inverse: fence legjitim 0–3 hapesira hapet/mbyllet normalisht; tabela brenda fence real e fshehur", () => {
    const legit = ["   ```md", ...TBL, "   ```", ...TBL].join("\n");
    expect(tableContextAt(legit, 3)).toBeUndefined();      // brenda
    expect(tableContextAt(legit, 7)?.headerArity).toBe(4); // pas closer-it
  });

  it("ENGINE: tabela pas fence-it 4-hapesirash prodhon findings Treatment/Maturity", () => {
    const { repo, base } = repoWithBase();
    repo.write("docs/f.md", ["    ```", ...TBL].join("\n") + "\n");
    const head = repo.commitAll("indented pseudo-fence");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, { driftClasses: [], chainRequiredSurfaces: [] });
    const ids = r.findings.map((f) => f.ruleId);
    expect(ids).toContain("HP-TREATMENT-VOCAB");
    expect(ids).toContain("HP-MIXED-MATURITY");
  });
})
