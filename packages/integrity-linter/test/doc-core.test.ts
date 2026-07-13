import { describe, expect, it } from "vitest";
import { makeTempRepo, writeFullCanonicalRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runDocCore, defaultSurfaces, type DocCoreSurfaces } from "../src/profiles/doc-core.js";
import { ScanError } from "../src/types.js";

function syntheticSurfaces(): DocCoreSurfaces {
  return {
    orderSurfaces: ["index.md"],
    trackerFile: "tracker.md",
    milestoneSurfaces: ["index.md", "roadmap.md"],
    approvedRoleTokens: ["Pronari", "PM"],
    authorities: [
      {
        id: "ledger-version",
        authorityFile: "ledger.md",
        authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/,
        consumers: [{ file: "index.md", pattern: /Ledger(?:\.md)?`?\*{0,2}\s*(?:[—–:])?\s*\*{0,2}v(\d+\.\d+)/g }],
      },
      {
        id: "row-count",
        authorityFile: "ledger.md",
        authorityPattern: /TOTAL: (\d+)/,
        consumers: [{ file: "index.md", pattern: /(\d{2,3}) source-rows/g }],
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
