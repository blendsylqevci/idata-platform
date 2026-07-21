import { describe, expect, it } from "vitest";
import { makeTempRepo, writeFullCanonicalRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runDocCore, defaultSurfaces, evaluateChainPresence, negatedChainClaim, type DocCoreSurfaces } from "../src/profiles/doc-core.js";
import { ScanError } from "../src/types.js";

function syntheticSurfaces(rowCountRequirement: "REQUIRED_CURRENT" | "SCAN_IF_PRESENT" = "REQUIRED_CURRENT"): DocCoreSurfaces {
  return {
    orderSurfaces: ["index.md"],
    chainRequiredSurfaces: [],
    trackerFile: "tracker.md",
    milestoneSurfaces: ["index.md", "roadmap.md"],
    approvedRolePhrases: ["pronari", "pm"],
    roleRequiredSurfaces: ["index.md", "tracker.md"],
    roleOptionalSurfaces: [],
    authorities: [
      {
        id: "ledger-version", kind: "version",
        authorityFile: "ledger.md",
        authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/g,
        consumers: [{ file: "index.md", requirement: "REQUIRED_CURRENT", pattern: /Ledger(?:\.md)?`?\*{0,2}\s*(?:[—–:·])?\s*\*{0,2}v(\d+\.\d+)/g }],
      },
      {
        id: "row-count", kind: "count",
        authorityFile: "ledger.md",
        authorityPattern: /TOTAL: (\d+)/g,
        consumers: [{ file: "index.md", requirement: rowCountRequirement, pattern: /(\d+) source-rows/g, excludeBefore: /CONTRADICTS[^0-9]{0,12}$/ }],
      },
    ],
  };
}

const TRACKER_HDR = "| ID | W | K | Owner / burimi | S |\n|---|---|---|---|---|\n";
function writeBase(repo: ReturnType<typeof makeTempRepo>, indexBody: string, trackerRows = "| `GOV-001` | x | y | Pronari | z |\n") {
  const tracker = TRACKER_HDR + trackerRows;
  repo.write("ledger.md", "**Versioni:** 1.6\nTOTAL: 132\n");
  repo.write("index.md", indexBody);
  repo.write("tracker.md", tracker);
  repo.write("roadmap.md", "GOV-001 prerequisite\n");
  repo.commitAll("docs");
}

describe("DOC-CORE — fail-closed + drift/order/milestone/claims/pr-body", () => {
  it("§2: surface që mungon (authority) → INCOMPLETE_SCAN, jo WARNING+heshtje", () => {
    const repo = makeTempRepo();
    repo.write("index.md", "Ledger v1.6\n");
    repo.commitAll("pa authority");
    expect(() => runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" }))
      .toThrow("INCOMPLETE_SCAN:SURFACE_MISSING");
  });

  it("§2: consumer/order/tracker surface që mungon → INCOMPLETE_SCAN", () => {
    const repo = makeTempRepo();
    repo.write("ledger.md", "**Versioni:** 1.6\nTOTAL: 132\n");
    repo.commitAll("vetëm authority");
    expect(() => runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" }))
      .toThrow(ScanError);
  });

  it("§2: authority ekziston por vlera e palexueshme → INCOMPLETE_SCAN:AUTHORITY_UNREADABLE", () => {
    const repo = makeTempRepo();
    repo.write("ledger.md", "pa version fare\n");
    repo.write("index.md", "x\n");
    repo.write("tracker.md", TRACKER_HDR + "| `GOV-001` | x | y | Pronari | z |\n");
    repo.write("roadmap.md", "GOV-001\n");
    repo.commitAll("authority pa vlerë");
    expect(() => runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" }))
      .toThrow("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");
  });

  it("§7 F-1 bidirectional: formatet reale markdown — backtick/em-dash/**/':'", () => {
    const repo = makeTempRepo();
    const V = (maj: string, min: string) => ["v", maj, ".", min].join("");
    writeBase(repo, [
      "aktuale: `ATK_SEF_Requirements_Ledger.md` — v1.6 me 132 source-rows",   // ✓ em dash + backtick
      `stale1: Ledger.md\u0060 ${V("1", "5")}`,                                 // ✗ drift
      `stale2: **Ledger ${V("1", "4")}** diçka`,                                 // ✗ drift
      `stale3: Ledger: ${V("1", "3")}`,                                          // ✗ drift
      `historike: Ledger ${V("1", "2")} (SUPERSEDED)`,                           // lejohet
    ].join("\n") + "\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    const drift = r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT");
    expect(drift.map((f) => f.locator).sort()).toEqual([
      "index.md:2 (ledger-version)",
      "index.md:3 (ledger-version)",
      "index.md:4 (ledger-version)",
    ]);
  });

  it("§7: '5 source-rows' i CONTRADICTS nuk ngatërrohet me totalin (\\d{2,3})", () => {
    const repo = makeTempRepo();
    writeBase(repo, "CONTRADICTS: 5 source-rows = 4 defekte\ntotali: 132 source-rows · Ledger v1.6\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    expect(r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT")).toHaveLength(0);
  });

  it("rendi i gabuar (preflight para HUMAN GATE në zinxhir 'pastaj') → finding; rendi i saktë → jo", () => {
    const repo = makeTempRepo();
    writeBase(repo, "Rendi: Step B pastaj private PREFLIGHT pastaj HUMAN GATE pastaj Tier 2.\nLedger v1.6 me 132 source-rows\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    expect(r.findings.some((f) => f.ruleId === "DC-GATE-ORDER")).toBe(true);

    const repo2 = makeTempRepo();
    writeBase(repo2, "Step B → Step C → independent review → HUMAN GATE → private PREFLIGHT → Tier 2.\nLedger v1.6 me 132 source-rows\n");
    const r2 = runDocCore(new GitCliReader(repo2.dir), "HEAD", syntheticSurfaces(), { body: "" });
    expect(r2.findings.filter((f) => f.ruleId === "DC-GATE-ORDER")).toHaveLength(0);
  });

  it("milestone i tracker-it që mungon në sipërfaqe → finding", () => {
    const repo = makeTempRepo();
    repo.write("ledger.md", "**Versioni:** 1.6\nTOTAL: 132\n");
    repo.write("index.md", "Ledger v1.6 me 132 source-rows\n");
    repo.write("roadmap.md", "asgjë\n");
    repo.write("tracker.md", "| `GOV-002` | t | y | Pronari | z |\n| `HG-SYNTH-GATE` | g | y | PM | z |\n");
    repo.commitAll("docs");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    const missing = r.findings.filter((f) => f.ruleId === "DC-MILESTONE-MISSING").map((f) => f.locator);
    expect(missing.some((l) => l.includes("GOV-002"))).toBe(true);
    expect(missing.some((l) => l.includes("HG-SYNTH-GATE"))).toBe(true);
  });

  it("PR body stale → finding pr-body; §2 event që mungon → NOT_EVALUATED me check të dallueshëm", () => {
    const repo = makeTempRepo();
    writeBase(repo, "Ledger v1.6 me 132 source-rows\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: ["counts: Ledger v1", ".", "4 diçka\n"].join("") });
    expect(r.findings.some((f) => f.ruleId === "DC-PR-BODY-STALE")).toBe(true);

    const r2 = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), undefined);
    const notEval = r2.evaluations.filter((e) => e.status === "NOT_EVALUATED");
    expect(notEval).toHaveLength(1);
    expect(notEval[0]?.check).toBe("pr-body");
    const aggregate = r2.evaluations.filter((e) => e.check === undefined);
    expect(aggregate).toHaveLength(1);
    expect(aggregate[0]?.status).toBe("EVALUATED"); // pa status kontradiktor pa subcheck
    expect(JSON.stringify(r2)).not.toContain('"PASS"');
  });

  it("vocabulary rolesh → REVIEW pa publikuar tekstin e qelizës", () => {
    const repo = makeTempRepo();
    writeBase(repo, "Ledger v1.6 me 132 source-rows\n", "| `ACC-901` | x | y | Filan Persona | z |\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    const vocab = r.findings.filter((f) => f.ruleId === "DC-ROLE-VOCAB");
    expect(vocab).toHaveLength(1);
    expect(vocab[0]?.severity).toBe("REVIEW");
    expect(JSON.stringify(r)).not.toContain("Filan");
  });

  it("§1 inverse: required consumer PA referencë → DC-CURRENT-VALUE-MISSING; optional pa referencë → clean", () => {
    const repo = makeTempRepo();
    repo.write("ledger.md", "**Versioni:** 1.6\nTOTAL: 132\n");
    repo.write("index.md", "asnjë referencë ledger këtu\n");
    repo.write("tracker.md", TRACKER_HDR + "| `GOV-001` | x | y | Pronari | z |\n");
    repo.write("roadmap.md", "GOV-001\n");
    repo.commitAll("docs");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    const missing = r.findings.filter((f) => f.ruleId === "DC-CURRENT-VALUE-MISSING").map((f) => f.locator);
    expect(missing.some((l) => l.includes("ledger-version"))).toBe(true);
    expect(missing.some((l) => l.includes("row-count"))).toBe(true);
    // optional (SCAN_IF_PRESENT) → clean
    const rOpt = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces("SCAN_IF_PRESENT"), { body: "" });
    expect(rOpt.findings.filter((f) => f.locator.includes("row-count"))).toHaveLength(0);
  });

  it("§1: current + stale bashkë → vetëm stale finding; etiketa historike në rresht TJETËR nuk e mbron stale-in", () => {
    const repo = makeTempRepo();
    const V = (a: string, b: string) => ["v", a, ".", b].join("");
    writeBase(repo, [
      "aktuale: Ledger v1.6 me 132 source-rows",
      "Historia e vjetër është SUPERSEDED këtu poshtë:",     // etiketë në rresht TJETËR
      `stale aktive: Ledger ${V("1", "5")}`,                  // NUK mbrohet nga rreshti i mësipërm
    ].join("\n") + "\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    const drift = r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT");
    expect(drift).toHaveLength(1);
    expect(drift[0]?.locator).toContain("index.md:3");
    expect(r.findings.filter((f) => f.ruleId === "DC-CURRENT-VALUE-MISSING")).toHaveLength(0);
  });

  it("§1: rresht i etiketuar historik me vlerën CURRENT nuk e plotëson REQUIRED_CURRENT", () => {
    const repo = makeTempRepo();
    writeBase(repo, "Historia: Ledger v1.6 (SUPERSEDED wording) me 132 source-rows\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    expect(r.findings.some((f) => f.ruleId === "DC-CURRENT-VALUE-MISSING" && f.locator.includes("ledger-version"))).toBe(true);
    expect(r.findings.some((f) => f.locator.includes("[vetëm stale/historike]"))).toBe(true);
  });

  it("§1: authority AMBIGUOUS (dy vlera të ndryshme) → exit-2 path (ScanError)", () => {
    const repo = makeTempRepo();
    repo.write("ledger.md", "**Versioni:** 1.6\n**Versioni:** 1.5\nTOTAL: 132\n");
    repo.write("index.md", "Ledger v1.6 · 132 source-rows\n");
    repo.write("tracker.md", TRACKER_HDR + "| `GOV-001` | x | y | Pronari | z |\n");
    repo.write("roadmap.md", "GOV-001\n");
    repo.commitAll("docs");
    expect(() => runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" }))
      .toThrow("INCOMPLETE_SCAN:AUTHORITY_AMBIGUOUS");
  });

  it("§4: hasFile fail-injection (reader hedh) → propagon exit-2, kurrë skip+EVALUATED", () => {
    const repo = makeTempRepo();
    writeBase(repo, "Ledger v1.6 me 132 source-rows\n");
    const real = new GitCliReader(repo.dir);
    const failing = new Proxy(real, {
      get(target, prop, receiver) {
        if (prop === "hasFile") return () => { throw new ScanError("GIT_OP_FAILED:ls-tree"); };
        return Reflect.get(target, prop, receiver);
      },
    });
    expect(() => runDocCore(failing as GitCliReader, "HEAD", syntheticSurfaces(), { body: "" }))
      .toThrow("GIT_OP_FAILED:ls-tree");
  });

  it("§7 role tokenized: rol i aprovuar si substring NUK mjafton (word-boundary)", () => {
    const repo = makeTempRepo();
    writeBase(repo, "Ledger v1.6 me 132 source-rows\n",
      "| `ACC-902` | x | y | Pronari / PM | z |\n| `ACC-903` | x | y | Xpronarix | z |\n| `ACC-904` | x | y | Emri Sintetik + Pronari | z |\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    const vocab = r.findings.filter((f) => f.ruleId === "DC-ROLE-VOCAB").map((f) => f.locator);
    expect(vocab).toHaveLength(2); // ACC-903 (substring bypass) + ACC-904 (token i panjohur)
    expect(vocab.some((l) => l.endsWith(":4"))).toBe(true);
    expect(vocab.some((l) => l.endsWith(":5"))).toBe(true);
    expect(JSON.stringify(r)).not.toContain("Sintetik"); // kandidati s'publikohet
  });

  it("defaultSurfaces + repo kanonik sintetik i PLOTË: zero findings; stale INDEX → drift", () => {
    const clean = makeTempRepo();
    writeFullCanonicalRepo(clean);
    const rClean = runDocCore(new GitCliReader(clean.dir), "HEAD", defaultSurfaces(), { body: "" });
    expect(rClean.findings).toEqual([]);

    const stale = makeTempRepo();
    writeFullCanonicalRepo(stale, { staleIndexLedger: true });
    const rStale = runDocCore(new GitCliReader(stale.dir), "HEAD", defaultSurfaces(), { body: "" });
    const ids = rStale.findings.map((f) => `${f.ruleId}@${f.locator}`);
    expect(rStale.findings.some((f) => f.ruleId === "DC-OLD-VALUE-DRIFT" && f.locator.includes("docs/INDEX.md"))).toBe(true);
    // kap edhe formatin '— v2.3' të Register-it edhe counts 117/44/43
    expect(ids.filter((i) => i.includes("docs/INDEX.md")).length).toBeGreaterThanOrEqual(4);
  });
});

describe("P-IV \u00a76 \u2014 phase authority (rreshti ATK-002) + consumers row-bound + PR-body multi-consumer", () => {
  const phaseCls = () => ({
    id: "syn-phase", kind: "phase" as const, authorityFile: "roadmap.md",
    authorityPattern: /\| ATK-002 \|[^\n]*\| Faza (\d+) \|/g,
    consumers: [
      { file: "m07.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /\| Fiskalizim \/ SEF \|[^\n]*\| F(\d+) \|/g },
      { file: "ledger.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /FISCAL-F(\d+)/g },
      { file: "ledger.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /BLOCKS-F(\d+)/g },
    ],
  });
  const surf = (): DocCoreSurfaces => ({
    orderSurfaces: [], chainRequiredSurfaces: [], milestoneSurfaces: [],
    approvedRolePhrases: [], roleRequiredSurfaces: [], roleOptionalSurfaces: [],
    authorities: [phaseCls()],
  });
  function writePhase(repo: ReturnType<typeof makeTempRepo>, fazaRows: string[], m07F: string, fiscal: string, blocks: string) {
    repo.write("roadmap.md", ["| ID | S | Faza | A |", "|---|---|---|---|", ...fazaRows, "proza e lir\u00eb F4 nuk num\u00ebrohet"].join("\n") + "\n");
    repo.write("m07.md", "| Domeni | T | Faza |\n|---|---|---|\n| Fiskalizim / SEF | t | F" + m07F + " |\nproza F4 e lir\u00eb\n");
    repo.write("ledger.md", "FISCAL-F" + fiscal + " \u00b7 BLOCKS-F" + blocks + "\nfaza F4 n\u00eb proz\u00eb jo-path-bound\n");
    repo.commitAll("phase docs");
  }
  const row5 = "| ATK-002 | x | Faza 5 | y |";

  it("authority F5 + t\u00eb tre consumers F5 \u2192 zero; F-numra n\u00eb proz\u00eb nuk num\u00ebrohen", () => {
    const repo = makeTempRepo();
    writePhase(repo, [row5], "5", "5", "5");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surf(), undefined);
    expect(r.findings).toEqual([]);
  });

  it("authority F5\u2192F4: t\u00eb tre consumers stale \u2192 3 findings row-bound", () => {
    const repo = makeTempRepo();
    writePhase(repo, ["| ATK-002 | x | Faza 4 | y |"], "5", "5", "5");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surf(), undefined);
    const drift = r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT");
    expect(drift).toHaveLength(3);
    expect(drift.some((f) => f.locator.startsWith("m07.md:3"))).toBe(true);
    expect(drift.filter((f) => f.locator.startsWith("ledger.md:1"))).toHaveLength(2);
  });

  it("drift i VE\u00c7UAR per-consumer: 07 F4; FISCAL-F4; BLOCKS-F4 \u2192 nga nj\u00eb finding secili", () => {
    for (const [m07F, fiscal, blocks, want] of [["4", "5", "5", "m07.md:3"], ["5", "4", "5", "ledger.md:1"], ["5", "5", "4", "ledger.md:1"]] as const) {
      const repo = makeTempRepo();
      writePhase(repo, [row5], m07F, fiscal, blocks);
      const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surf(), undefined);
      const drift = r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT");
      expect(drift).toHaveLength(1);
      expect(drift[0]?.locator.startsWith(want)).toBe(true);
    }
  });

  it("PR body: drift te consumer #2 DHE #3 kapet (jo vet\u00ebm consumers[0])", () => {
    const repo = makeTempRepo();
    writePhase(repo, [row5], "5", "5", "5");
    const r2 = runDocCore(new GitCliReader(repo.dir), "HEAD", surf(), { body: "raport: FISCAL-F4 mbetet" });
    expect(r2.findings.filter((f) => f.ruleId === "DC-PR-BODY-STALE")).toHaveLength(1);
    const r3 = runDocCore(new GitCliReader(repo.dir), "HEAD", surf(), { body: "shih BLOCKS-F4" });
    expect(r3.findings.filter((f) => f.ruleId === "DC-PR-BODY-STALE")).toHaveLength(1);
    const rOk = runDocCore(new GitCliReader(repo.dir), "HEAD", surf(), { body: "FISCAL-F5 dhe BLOCKS-F5 \u00b7 faza F4 proz\u00eb e lir\u00eb" });
    expect(rOk.findings.filter((f) => f.ruleId === "DC-PR-BODY-STALE")).toHaveLength(0);
  });

  it("authority row missing \u2192 exit 2 UNREADABLE; rreshta ATK-002 t\u00eb dyfisht\u00eb me vlera t\u00eb ndryshme \u2192 AMBIGUOUS", () => {
    const repo = makeTempRepo();
    repo.write("roadmap.md", "| ID | S | Faza | A |\n|---|---|---|---|\n| ATK-003 | x | Faza 5 | y |\n");
    repo.write("m07.md", "x\n");
    repo.write("ledger.md", "x\n");
    repo.commitAll("pa rresht ATK-002");
    expect(() => runDocCore(new GitCliReader(repo.dir), "HEAD", surf(), undefined))
      .toThrow("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");
    const repo2 = makeTempRepo();
    writePhase(repo2, [row5, "| ATK-002 | z | Faza 4 | w |"], "5", "5", "5");
    expect(() => runDocCore(new GitCliReader(repo2.dir), "HEAD", surf(), undefined))
      .toThrow("INCOMPLETE_SCAN:AUTHORITY_AMBIGUOUS");
  });
});

describe("P-IV \u00a77 \u2014 chain: negacion claim-scoped, historik segment-scoped, dritare pa prerje", () => {
  const FULL = "Step B \u2192 Step C \u2192 independent review \u2192 HUMAN GATE \u2192 private PREFLIGHT \u2192 Tier 2";

  it("complete active \u2192 zero mungesa/shkelje", () => {
    const e = evaluateChainPresence(FULL + "\n");
    expect(e.noActiveChain).toBe(false);
    expect(e.missing).toEqual([]);
    expect(e.violations).toEqual([]);
  });

  it("negated complete: zinxhiri i MOHUAR nuk \u00ebsht\u00eb chain aktiv", () => {
    const e = evaluateChainPresence("NUK \u00ebsht\u00eb ky rendi: " + FULL + "\n");
    expect(e.noActiveChain).toBe(true);
    expect(negatedChainClaim("NUK \u00ebsht\u00eb ky rendi: " + FULL)).toBe(true);
    expect(negatedChainClaim("Rendi i detyruesh\u00ebm: " + FULL)).toBe(false);
    expect(negatedChainClaim("Zinxhiri mbetet i pandryshuar: " + FULL)).toBe(false); // jo-mohim \u2192 aktiv
  });

  it("missing-link dhe wrong-order kapen", () => {
    const noReview = "Step B \u2192 Step C \u2192 HUMAN GATE \u2192 private PREFLIGHT \u2192 Tier 2";
    expect(evaluateChainPresence(noReview + "\n").missing).toContain("independent-review");
    const wrong = "Step B \u2192 Step C \u2192 independent review \u2192 private PREFLIGHT \u2192 HUMAN GATE \u2192 Tier 2";
    expect(evaluateChainPresence(wrong + "\n").violations.length).toBeGreaterThan(0);
  });

  it("Step A (HISTORICAL) parenthetical NUK i p\u00ebrjashton hallkat aktive pas tij", () => {
    const gate98 = "**Step A (docs \u2014 HISTORICAL, COMPLETED me PR #20)** \u2192 **Step B** hardening \u2192 **Step C** vectors \u2192 **independent re-review** \u2192 **HUMAN GATE (autorizimi)** \u2192 **private PREFLIGHT** (wrapper) \u2192 **vet\u00ebm pas suksesit vazhdon pjesa tjet\u00ebr e Tier 2**.";
    const e = evaluateChainPresence(gate98 + "\n");
    expect(e.noActiveChain).toBe(false);
    expect(e.missing).toEqual([]);
    expect(e.violations).toEqual([]);
  });

  it("segment historik me etikete STRUKTURORE blankohet vetem ai; fjala e lire 'ish' NUK blankon (P-VI \u00a72 supersedes fjalen e lire)", () => {
    // Kontrata e vjeter blankonte segmentin per fjalen e lire 'ish' — hequr me
    // P-VI \u00a72 (fjala e lire fshihte pretendime aktive). Strukturorja mbetet.
    const structural = "Step B \u2192 Step C \u2192 independent review \u2192 HUMAN GATE \u2192 private PREFLIGHT (SUPERSEDED) \u2192 Tier 2"; // P-VII: marker i MBYLLUR
    const e = evaluateChainPresence(structural + "\n");
    expect(e.missing).toEqual(["preflight"]); // segmenti me SUPERSEDED strukturor hiqet
    const freeWord = "Step B \u2192 Step C \u2192 independent review \u2192 HUMAN GATE \u2192 private PREFLIGHT ish faza \u2192 Tier 2";
    const e2 = evaluateChainPresence(freeWord + "\n");
    expect(e2.missing).toEqual([]); // 'ish' i lire s'e ben segmentin historik
  });

  it("rresht i gjat\u00eb: Step B ~100 karaktere para markerit t\u00eb par\u00eb NUK pritet", () => {
    const long = "**Step B** = detyra T2-01..10 hardening i plot\u00eb i kodit fiskal " +
      "\u00b7 **Step C** = golden vectors + negative tests t\u00eb plota \u00b7 pastaj re-review \u2192 " +
      "**HUMAN GATE (para thirrjes s\u00eb PAR\u00cb reale)** \u2192 private PREFLIGHT \u2192 pjesa tjet\u00ebr e Tier 2";
    const e = evaluateChainPresence(long + "\n");
    expect(e.noActiveChain).toBe(false);
    expect(e.missing).toEqual([]);
  });

  it("chain historik (prefiks rreshti) + chain aktiv \u2192 aktivi fiton; vet\u00ebm historiku \u2192 asnj\u00eb chain aktiv", () => {
    const both = "Historia: Step C \u2192 Step B rend i vjet\u00ebr\n\n" + FULL + "\n";
    const e = evaluateChainPresence(both);
    expect(e.noActiveChain).toBe(false);
    expect(e.violations).toEqual([]);
    const histOnly = evaluateChainPresence("Historia: " + FULL + "\n");
    expect(histOnly.noActiveChain).toBe(true);
  });

  it("variantet reale t\u00eb re-review normalizohen n\u00eb t\u00eb nj\u00ebjt\u00ebn hallk\u00eb", () => {
    for (const v of ["independent review", "independent re-review", "review i pavarur", "re-review", "rishikim i pavarur"]) {
      const line = "Step B \u2192 Step C \u2192 " + v + " \u2192 HUMAN GATE \u2192 private PREFLIGHT \u2192 Tier 2";
      expect(evaluateChainPresence(line + "\n").missing).toEqual([]);
    }
  });

  it("near-miss: nj\u00eb hallk\u00eb e vetme n\u00eb proz\u00eb nuk \u00ebsht\u00eb pretendim zinxhiri", () => {
    const e = evaluateChainPresence("K\u00ebtu p\u00ebrmendet vet\u00ebm Step B pa rend.\n");
    expect(e.noActiveChain).toBe(true);
  });
});

describe("P-IV \u00a79 \u2014 role/privacy coverage: sipërfaqe machine-readable + parentetikat", () => {
  const surf = (req: string[], opt: string[] = []): DocCoreSurfaces => ({
    orderSurfaces: [], chainRequiredSurfaces: [], milestoneSurfaces: [],
    approvedRolePhrases: ["pronari", "group owner", "human gate", "icode dev"],
    roleRequiredSurfaces: req, roleOptionalSurfaces: opt, authorities: [],
  });

  it("tabel\u00eb PA leading pipe me kolon\u00eb Owner: rol i aprovuar \u2192 zero; i panjohur \u2192 REVIEW pa content", () => {
    const repo = makeTempRepo();
    repo.write("reg.md", ["ID | Burimi | Owner / burimi | S", "--- | --- | --- | ---", "S1 | ligji | pronari | ok", "S2 | ua | RolIPanjohurSintetik | x"].join("\n") + "\n");
    repo.commitAll("register sintetik");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surf(["reg.md"]), undefined);
    const vocab = r.findings.filter((f) => f.ruleId === "DC-ROLE-VOCAB");
    expect(vocab).toHaveLength(1);
    expect(vocab[0]?.locator).toBe("reg.md:4");
    expect(JSON.stringify(r.findings)).not.toContain("RolIPanjohurSintetik");
  });

  it("\u201cHuman Gate:\u201d njihet si etiket\u00eb metadata; vlera e aprovuar \u2192 zero, e panjohura \u2192 REVIEW", () => {
    const repo = makeTempRepo();
    repo.write("idx.md", "**Human Gate:** Pronari\n**Human Gate:** EmriSintetik99\n");
    repo.commitAll("labels");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surf(["idx.md"]), undefined);
    const vocab = r.findings.filter((f) => f.ruleId === "DC-ROLE-VOCAB");
    expect(vocab).toHaveLength(1);
    expect(vocab[0]?.locator).toBe("idx.md:2");
  });

  it("identitet sintetik n\u00eb parenthetical t\u00eb nj\u00eb vlere role \u2192 DC-ROLE-IDENTITY REVIEW; fraz\u00eb e aprovuar n\u00eb parenteza \u2192 zero", () => {
    const repo = makeTempRepo();
    repo.write("idx.md", ["**Pronari:** Group Owner (Human Gate)", "**Pronari:** Group Owner (Filani Sintetiku)"].join("\n") + "\n");
    repo.commitAll("parentetika");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surf(["idx.md"]), undefined);
    const ident = r.findings.filter((f) => f.ruleId === "DC-ROLE-IDENTITY");
    expect(ident).toHaveLength(1);
    expect(ident[0]?.locator).toBe("idx.md:2");
    expect(JSON.stringify(r.findings)).not.toContain("Filani");
  });

  it("required surface missing \u2192 exit 2; optional missing \u2192 lejohet", () => {
    const repo = makeTempRepo();
    repo.write("idx.md", "x\n");
    repo.commitAll("baza");
    expect(() => runDocCore(new GitCliReader(repo.dir), "HEAD", surf(["idx.md", "mungon.md"]), undefined))
      .toThrow("INCOMPLETE_SCAN:SURFACE_MISSING");
    expect(() => runDocCore(new GitCliReader(repo.dir), "HEAD", surf(["idx.md"], ["opsionale.md"]), undefined)).not.toThrow();
  });
})
