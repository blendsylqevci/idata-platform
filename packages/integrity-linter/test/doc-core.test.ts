import { describe, expect, it } from "vitest";
import { makeTempRepo, writeFullCanonicalRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runDocCore, defaultSurfaces, type DocCoreSurfaces } from "../src/profiles/doc-core.js";
import { ScanError } from "../src/types.js";

function syntheticSurfaces(rowCountRequirement: "REQUIRED_CURRENT" | "SCAN_IF_PRESENT" = "REQUIRED_CURRENT"): DocCoreSurfaces {
  return {
    orderSurfaces: ["index.md"],
    trackerFile: "tracker.md",
    milestoneSurfaces: ["index.md", "roadmap.md"],
    approvedRoleWords: ["pronari", "pm"],
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

function writeBase(repo: ReturnType<typeof makeTempRepo>, indexBody: string, tracker = "| `GOV-001` | x | y | Pronari | z |\n") {
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
    repo.write("tracker.md", "| `GOV-001` | x | y | Pronari | z |\n");
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
    repo.write("tracker.md", "| `GOV-001` | x | y | Pronari | z |\n");
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
    repo.write("tracker.md", "| `GOV-001` | x | y | Pronari | z |\n");
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
    expect(vocab.some((l) => l.endsWith(":2"))).toBe(true);
    expect(vocab.some((l) => l.endsWith(":3"))).toBe(true);
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
