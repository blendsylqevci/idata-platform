import { describe, expect, it } from "vitest";
import { makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runDocCore, type DocCoreSurfaces } from "../src/profiles/doc-core.js";

/** Sipërfaqe sintetike: authority = ledger.md; consumer = index.md. */
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
        consumers: [{ file: "index.md", pattern: /Ledger v(\d+\.\d+)/g }],
      },
      {
        id: "row-count",
        authorityFile: "ledger.md",
        authorityPattern: /TOTAL: (\d+)/,
        consumers: [{ file: "index.md", pattern: /(\d+) source-rows/g }],
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

describe("DOC-CORE — drift/order/milestone/claims/pr-body (sinteike)", () => {
  it("test 14 + 16: vlerë aktive stale kundër authority → finding drift", () => {
    const repo = makeTempRepo();
    writeBase(repo, "statusi: Ledger v1.5 me 117 source-rows\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    const drift = r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT");
    expect(drift.length).toBe(2); // versioni + count
    expect(drift[0]?.locator).toContain("index.md");
  });

  it("test 15: rreshti i etiketuar HISTORICAL/SUPERSEDED/Historia lejohet", () => {
    const repo = makeTempRepo();
    writeBase(repo, "aktuale: Ledger v1.6 me 132 source-rows\nHistoria: Ledger v1.5 me 117 source-rows (SUPERSEDED)\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    expect(r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT")).toHaveLength(0);
  });

  it("test 19: rend i gabuar i Human Gate (preflight para HUMAN GATE) → finding", () => {
    const repo = makeTempRepo();
    writeBase(repo, "Rendi: Step B pastaj private PREFLIGHT pastaj HUMAN GATE pastaj Tier 2.\nLedger v1.6 me 132 source-rows\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    expect(r.findings.some((f) => f.ruleId === "DC-GATE-ORDER")).toBe(true);
  });

  it("rendi i saktë s'jep finding", () => {
    const repo = makeTempRepo();
    writeBase(repo, "Step B → Step C → independent review → HUMAN GATE → private PREFLIGHT → Tier 2.\nLedger v1.6 me 132 source-rows\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    expect(r.findings.filter((f) => f.ruleId === "DC-GATE-ORDER")).toHaveLength(0);
  });

  it("test 17: milestone i aprovuar në tracker por që mungon në sipërfaqe → finding", () => {
    const repo = makeTempRepo();
    repo.write("ledger.md", "**Versioni:** 1.6\nTOTAL: 132\n");
    repo.write("index.md", "Ledger v1.6 me 132 source-rows\n");
    repo.write("roadmap.md", "asgjë këtu\n");
    repo.write("tracker.md", "| `GOV-002` | tooling i ri | y | Pronari | z |\n| `HG-SYNTH-GATE` | gate | y | PM | z |\n");
    repo.commitAll("docs");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    const missing = r.findings.filter((f) => f.ruleId === "DC-MILESTONE-MISSING").map((f) => f.locator);
    expect(missing.some((l) => l.includes("GOV-002"))).toBe(true);
    expect(missing.some((l) => l.includes("HG-SYNTH-GATE"))).toBe(true);
  });

  it("test 18: PR body stale kundër authority → finding pr-body", () => {
    const repo = makeTempRepo();
    writeBase(repo, "Ledger v1.6 me 132 source-rows\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "counts: Ledger v1.4 diçka\n" });
    expect(r.findings.some((f) => f.ruleId === "DC-PR-BODY-STALE")).toBe(true);
  });

  it("test 20: event/PR body që mungon → NOT_EVALUATED, kurrë PASS i fabrikuar", () => {
    const repo = makeTempRepo();
    writeBase(repo, "Ledger v1.6 me 132 source-rows\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), undefined);
    const notEval = r.evaluations.find((e) => e.status === "NOT_EVALUATED");
    expect(notEval?.reason).toContain("missing PR event");
    expect(JSON.stringify(r)).not.toContain('"PASS"');
  });

  it("vocabulary rolesh: owner-cell pa rol të aprovuar → REVIEW (jo vendim automatik)", () => {
    const repo = makeTempRepo();
    writeBase(repo, "Ledger v1.6 me 132 source-rows\n", "| `ACC-901` | x | y | Filan Persona | z |\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    const vocab = r.findings.filter((f) => f.ruleId === "DC-ROLE-VOCAB");
    expect(vocab).toHaveLength(1);
    expect(vocab[0]?.severity).toBe("REVIEW");
    // privacy: locator mban vetëm file:line — jo tekstin e qelizës
    expect(vocab[0]?.locator).toBe("tracker.md:1");
    expect(JSON.stringify(r)).not.toContain("Filan");
  });

  it("claims kontekstuale: 'closes on merge' pa mohim → finding; me kualifikim → jo", () => {
    const repo = makeTempRepo();
    writeBase(repo, "Kjo closes on merge nesër.\nNdalohet pretendimi closes on merge pa review (NUK lejohet).\nLedger v1.6 me 132 source-rows\n");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", syntheticSurfaces(), { body: "" });
    const claims = r.findings.filter((f) => f.ruleId === "DC-CLOSES-ON-MERGE");
    expect(claims).toHaveLength(1);
    expect(claims[0]?.locator).toBe("index.md:1");
  });
});
