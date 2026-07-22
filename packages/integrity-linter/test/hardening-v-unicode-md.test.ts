/**
 * P-V F1/F2/F3/F4/F5/C3/C4 — Unicode machine tokens, blockquote GFM,
 * role markup/casing, mohim i dyfishtë, etiketa historike eksplicite.
 * Karakteret problematike ndërtohen RUNTIME me code points — burimi ASCII.
 */
import { describe, expect, it } from "vitest";
import { makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runHistoryPriv } from "../src/profiles/history-priv.js";
import {
  runDocCore, parentheticalIdentityCandidate, projectRoleMarkup, type DocCoreSurfaces,
} from "../src/profiles/doc-core.js";
import { detectionSkeleton, hasObfuscatedMatch } from "../src/machine-token.js";
import { extractAuthorityValue, isHistoryLabeledRow } from "../src/authority-classes.js";
import { containerOf, mixedDepthTableRow, parseTableRow, tableContextAt } from "../src/markdown-table.js";
import { toJsonReport } from "../src/reporter.js";

const ZWSP = String.fromCharCode(0x200b);
const LRM = String.fromCharCode(0x200e);
const WJ = String.fromCharCode(0x2060);
const CYR_O = String.fromCharCode(0x041e); // О cirilike
const CYR_C = String.fromCharCode(0x0421); // С cirilike
const GRK_O = String.fromCharCode(0x039f); // Ο greke

function repoWithBase() {
  const repo = makeTempRepo();
  repo.write("README.md", "synthetic base\n");
  const base = repo.commitAll("base");
  return { repo, base };
}

const phaseCls = () => ({
  id: "syn-ph", kind: "phase" as const, authorityFile: "auth.md",
  authorityPattern: /\| SYN-01 \|[^\n]*\| Faza (\d+) \|/g,
  consumers: [
    { file: "led.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /FISCAL-F(\d+)/g },
    { file: "led.md", requirement: "SCAN_IF_PRESENT" as const, pattern: /BLOCKS-F(\d+)/g },
  ],
});
const surf = (auth = [phaseCls()]): DocCoreSurfaces => ({
  orderSurfaces: [], chainRequiredSurfaces: [], milestoneSurfaces: [],
  approvedRolePhrases: ["pronari", "group owner", "human gate", "aip", "icode dev"],
  roleRequiredSurfaces: [], roleOptionalSurfaces: [], authorities: auth,
});

describe("F1/F2 — protected machine tokens: authority fail-closed, consumer finding i dedikuar", () => {
  it("skeleton: heq format-chars, harton konfuzablat; proza shqipe e paprekur", () => {
    expect(detectionSkeleton(`FISCAL-F${ZWSP}5`)).toBe("FISCAL-F5");
    expect(detectionSkeleton(`G${CYR_O}V-001`)).toBe("GOV-001");
    expect(detectionSkeleton("përgjegjësi ë ç ü")).toBe("përgjegjësi ë ç ü");
  });

  it("authority me homoglyph/zero-width → exit 2 me kod specifik OBFUSCATED", () => {
    const cls = phaseCls();
    expect(() => extractAuthorityValue(`| SYN-01 | x | Faza${ZWSP} 5 |\n`.replace("Faza" + ZWSP, "Fa" + ZWSP + "za"), cls))
      .toThrow("AUTHORITY_OBFUSCATED");
    const statusCls = {
      id: "syn-st", kind: "status" as const, authorityFile: "a.md",
      authorityPattern: /GJENDJA: (OK|JO)/g, consumers: [],
    };
    expect(() => extractAuthorityValue(`GJENDJA: ${CYR_O}K\n`, statusCls)).toThrow("AUTHORITY_OBFUSCATED");
    // pa asnjë kandidat as pas projektimit → UNREADABLE (kodi ekzistues)
    expect(() => extractAuthorityValue("asgjë këtu\n", statusCls)).toThrow("AUTHORITY_UNREADABLE");
  });

  it("DOC-CORE consumer: FISCAL-F<ZWSP>4 kundrejt Faza 5 → DC-MACHINE-TOKEN-OBFUSCATED (jo silent no-match)", () => {
    const repo = makeTempRepo();
    repo.write("auth.md", "| SYN-01 | x | Faza 5 |\n");
    repo.write("led.md", `aktive: FISCAL-F5\nfsheh: FISCAL-F${ZWSP}4\nBLOCKS-F${WJ}4 gjithashtu\n`);
    repo.commitAll("obfuscation consumers");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surf(), undefined);
    const obf = r.findings.filter((f) => f.ruleId === "DC-MACHINE-TOKEN-OBFUSCATED");
    expect(obf.map((f) => f.locator).sort()).toEqual(["led.md:2 (syn-ph)", "led.md:3 (syn-ph)"]);
    expect(r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT")).toHaveLength(0); // raw s'match-on → s'ka drift raw
    const json = toJsonReport({ mode: "advisory", evaluations: r.evaluations, findings: r.findings });
    expect(json).not.toContain("200b");
  });

  it("HISTORY-PRIV consumer: token i obfuskuar per-commit → HP-MACHINE-TOKEN-OBFUSCATED", () => {
    const { repo, base } = repoWithBase();
    repo.write("auth.md", "| SYN-01 | x | Faza 5 |\n");
    repo.commitAll("auth");
    repo.write("led.md", `FISCAL-F${ZWSP}4\n`);
    const head = repo.commitAll("obfuskuar");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined,
      { driftClasses: [phaseCls()], chainRequiredSurfaces: [] });
    const obf = r.findings.filter((f) => f.ruleId === "HP-MACHINE-TOKEN-OBFUSCATED");
    expect(obf).toHaveLength(1);
    expect(obf[0]?.locator).toBe("led.md:1 (syn-ph)");
  });

  it("mixed-script brenda token-it (GRK/CYR) → finding; FISCAL-F5 normal → zero", () => {
    const repo = makeTempRepo();
    repo.write("auth.md", "| SYN-01 | x | Faza 5 |\n");
    repo.write("led.md", `FISCAL-F5 normal\nBL${GRK_O}CKS-F5 i përzier\n`);
    repo.commitAll("mixed script");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surf(), undefined);
    const obf = r.findings.filter((f) => f.ruleId === "DC-MACHINE-TOKEN-OBFUSCATED");
    expect(obf).toHaveLength(1);
    expect(obf[0]?.locator).toBe("led.md:2 (syn-ph)");
  });

  it("inverse: prozë shqipe me Unicode + LRM jashtë token-it të mbrojtur → zero obfuscation findings", () => {
    const repo = makeTempRepo();
    repo.write("auth.md", "| SYN-01 | x | Faza 5 |\n");
    repo.write("led.md", `tekst ${LRM} i lirë me ë/ç dhe FISCAL-F5 korrekt\n`);
    repo.commitAll("proza");
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surf(), undefined);
    expect(r.findings.filter((f) => f.category === "obfuscation")).toHaveLength(0);
  });

  it("NFKC nuk përdoret si suppression: hasObfuscatedMatch NUK e 'pranon' vlerën — vetëm e zbulon", () => {
    // U+2160 (ROMAN NUMERAL ONE) me NFKC → 'I'; skeleton ynë NUK e harton → jo match, jo pranim
    const roman = String.fromCharCode(0x2160);
    expect(hasObfuscatedMatch(`FISCAL-F${roman}`, /FISCAL-F(\d+)/g)).toBe(false);
  });
});

describe("F3 — tabela GFM brenda blockquote", () => {
  const T = (p: string) => [
    `${p}Domeni | Prova | Maturity | Treatment`,
    `${p}--- | --- | --- | ---`,
    `${p}x |  | MODELED TESTED | LATER`,
  ].join("\n") + "\n";

  it("containerOf/parseTableRow: prefikset e quote hiqen; '>' brenda qelize ruhet", () => {
    expect(containerOf("> | a | b |")).toEqual({ depth: 1, content: "| a | b |", indent: 0 });
    expect(containerOf(">> x")).toEqual({ depth: 2, content: "x", indent: 0 });
    expect(parseTableRow("> | a | b |").cells).toEqual(["a", "b"]);
    expect(parseTableRow("| a > b | c |").cells).toEqual(["a > b", "c"]);
  });

  it("single-level dhe nested blockquote: konteksti i tabelës zgjidhet me depth konsistent", () => {
    const s1 = T("> ");
    const ctx1 = tableContextAt(s1, 3);
    expect(ctx1?.headerArity).toBe(4);
    expect(ctx1?.columnIndex(/Treatment/i)).toBe(3);
    const s2 = T("> > ");
    expect(tableContextAt(s2, 3)?.headerArity).toBe(4);
  });

  it("depth i përzier header/delimiter/data → JO kontekst + mixedDepthTableRow=true (jo silent)", () => {
    const s = ["> Domeni | T", "--- | ---", "> x | y"].join("\n");
    expect(tableContextAt(s, 3)).toBeUndefined();
    expect(mixedDepthTableRow(s, 3)).toBe(true);
    expect(mixedDepthTableRow(T("> "), 3)).toBe(false);
  });

  it("prozë me '>' dhe fence brenda quote: s'ka kontekst tabele", () => {
    expect(tableContextAt("> thjesht prozë e cituar\n", 1)).toBeUndefined();
    const s = ["> ```", "> A | B", "> --- | ---", "> x | y", "> ```", "", "A | B", "--- | ---", "z | w"].join("\n");
    expect(tableContextAt(s, 4)).toBeUndefined(); // brenda fence të quote-s
    expect(tableContextAt(s, 9)?.headerArity).toBe(2); // jashtë — normale
    const unclosed = ["> ```", "> A | B", "> --- | ---", "> x | y"].join("\n");
    expect(tableContextAt(unclosed, 4)).toBeUndefined();
  });

  it("ENGINE: tabela e cituar me Treatment invalid + mixed maturity + empty cell → të TRI klasat si tabela normale", () => {
    const { repo, base } = repoWithBase();
    repo.write("docs/q.md", T("> "));
    const head = repo.commitAll("quoted table");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined,
      { driftClasses: [], chainRequiredSurfaces: [] });
    const ids = r.findings.map((f) => f.ruleId);
    expect(ids).toContain("HP-TREATMENT-VOCAB");
    expect(ids).toContain("HP-MIXED-MATURITY");
    expect(ids).toContain("HP-EMPTY-MANDATORY-CELL");
  });
});

describe("F4/C4 — role markup + casing", () => {
  const APPROVED = ["pronari", "group owner", "human gate", "aip"];

  it("projectRoleMarkup: br/comment/entities → separator; markup i papritur sinjalizohet", () => {
    expect(projectRoleMarkup("A<br>B").text).toBe("A B");
    expect(projectRoleMarkup("A<br />B").text).toBe("A B");
    expect(projectRoleMarkup("A<!-- x -->B").text).toBe("A B");
    expect(projectRoleMarkup("A&nbsp;B").text).toBe("A B");
    expect(projectRoleMarkup("A<span>B").unexpectedMarkup).toBe(true);
    expect(projectRoleMarkup("A&copy;B").unexpectedMarkup).toBe(true);
    expect(projectRoleMarkup("A B").unexpectedMarkup).toBe(false);
  });

  it("identiteti i ndarë me <br>/comment kapet; UPPERCASE/lowercase kapen (C4)", () => {
    expect(parentheticalIdentityCandidate(projectRoleMarkup("Pronari (Filani<br>Sintetiku)").text, APPROVED)).toBe(true);
    expect(parentheticalIdentityCandidate(projectRoleMarkup("Pronari (Filani<!-- x -->Sintetiku)").text, APPROVED)).toBe(true);
    expect(parentheticalIdentityCandidate("Pronari (FILANI SINTETIKU)", APPROVED)).toBe(true);
    expect(parentheticalIdentityCandidate("Pronari (filani sintetiku)", APPROVED)).toBe(true);
    expect(parentheticalIdentityCandidate("Pronari (Filani Sintetiku)", APPROVED)).toBe(true);
  });

  it("inverse: role të aprovuara, Human Gate, AIP, —, annotation funksionale → zero kandidat", () => {
    expect(parentheticalIdentityCandidate("Group Owner (Human Gate)", APPROVED)).toBe(false);
    expect(parentheticalIdentityCandidate("Owner (AIP)", APPROVED)).toBe(false);
    expect(parentheticalIdentityCandidate("Owner (—)", APPROVED)).toBe(false);
    expect(parentheticalIdentityCandidate("Pronari (aprovon çdo ndryshim të këtij dokumenti)", APPROVED)).toBe(false);
  });

  it("ENGINE: metadata label + table cell — markup i papritur → DC-ROLE-MARKUP; output pa kandidatin", () => {
    const repo = makeTempRepo();
    repo.write("idx.md", [
      "**Pronari:** Group Owner (Filani<br>Sintetiku)",
      "| ID | Owner / burimi |",
      "|---|---|",
      "| S1 | Pronari<span>x |",
    ].join("\n") + "\n");
    repo.commitAll("role markup");
    const surfaces: DocCoreSurfaces = {
      orderSurfaces: [], chainRequiredSurfaces: [], milestoneSurfaces: [],
      approvedRolePhrases: APPROVED, roleRequiredSurfaces: ["idx.md"], roleOptionalSurfaces: [], authorities: [],
    };
    const r = runDocCore(new GitCliReader(repo.dir), "HEAD", surfaces, undefined);
    expect(r.findings.some((f) => f.ruleId === "DC-ROLE-IDENTITY" && f.locator === "idx.md:1")).toBe(true);
    expect(r.findings.some((f) => f.ruleId === "DC-ROLE-MARKUP" && f.locator === "idx.md:4")).toBe(true);
    const json = toJsonReport({ mode: "advisory", evaluations: r.evaluations, findings: r.findings });
    expect(json).not.toContain("Filani");
    expect(json).not.toContain("Sintetiku");
  });
});

describe("F5 — mohim i dyfishtë/ambig nuk suppress-on", () => {
  const run = (content: string) => {
    const { repo, base } = repoWithBase();
    repo.write("docs/n.md", content);
    const head = repo.commitAll("c");
    return runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined,
      { driftClasses: [], chainRequiredSurfaces: [] });
  };
  const IMPL = ["IMPLE", "MENTED"].join("");
  const TSTD = ["TES", "TED"].join("");

  it("positive control: 'PoC është IMPLEMENTED' → finding", () => {
    expect(run(`PoC është ${IMPL}\n`).findings.filter((f) => f.ruleId === "HP-POC-PROMOTION")).toHaveLength(1);
  });

  it("double negation: \"s'është që PoC NUK është IMPLEMENTED\" → finding (jo suppression)", () => {
    expect(run(`s'është që PoC NUK është ${IMPL}\n`).findings.filter((f) => f.ruleId === "HP-POC-PROMOTION")).toHaveLength(1);
  });

  it("single explicit negation dhe JO-TESTED mbeten suppression të lejuara", () => {
    expect(run(`nuk raportohet xhiroja ditore këtu\n`).findings.filter((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toHaveLength(0);
    expect(run(`PoC JO-${TSTD} vetëm\n`).findings.filter((f) => f.ruleId === "HP-POC-PROMOTION")).toHaveLength(0);
  });

  it("'nuk … por … IMPLEMENTED' → claim-i pozitiv kapet per-match", () => {
    expect(run(`PoC nuk mbyllet këtu por mbetet ${IMPL} në prod\n`).findings.filter((f) => f.ruleId === "HP-POC-PROMOTION")).toHaveLength(1);
  });

  it("double negation te operational claims — e njëjta kontratë", () => {
    expect(run(`s'thuhet që nuk raportohet xhiroja ditore\n`).findings.filter((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toHaveLength(1);
  });

  it("dy claims në një rresht: vetëm i mohuari (i vetëm) suppress-ohet", () => {
    const r = run(`saldo reale është 7. nuk raportohet xhiroja ditore\n`);
    expect(r.findings.filter((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toHaveLength(1);
  });
});

describe("C3 — history label eksplicit; 'vs <n>' s'është më exemption", () => {
  const verCls = () => ({
    id: "syn-ver", kind: "version" as const, authorityFile: "led.md",
    authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/g,
    consumers: [{ file: "idx.md", requirement: "REQUIRED_CURRENT" as const, pattern: /Ledger v(\d+\.\d+)/g }],
  });
  const scan = (idxBody: string) => {
    const repo = makeTempRepo();
    repo.write("led.md", "**Versioni:** 1.6\n");
    repo.write("idx.md", idxBody);
    repo.commitAll("docs");
    return runDocCore(new GitCliReader(repo.dir), "HEAD", surf([verCls()]), undefined);
  };

  it("'CURRENT: Ledger v1.5 vs 2 consumers' → drift finding (s'fshihet më)", () => {
    const r = scan("Ledger v1.6 aktual\nCURRENT: Ledger v1.5 vs 2 consumers\n");
    const drift = r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT");
    expect(drift).toHaveLength(1);
    expect(drift[0]?.locator).toBe("idx.md:2 (syn-ver)");
  });

  it("'comparison v1 vs 2' nuk fiton exemption automatik", () => {
    expect(isHistoryLabeledRow("comparison Ledger v1.5 vs 2 rows")).toBe(false);
    expect(isHistoryLabeledRow("Historia: Ledger v1.5")).toBe(true);
    expect(isHistoryLabeledRow("Ledger v1.5 (SUPERSEDED)")).toBe(true);
  });

  it("label-i në një rresht nuk e përjashton rreshtin fqinj", () => {
    const r = scan("Historia: Ledger v1.4 (SUPERSEDED)\nLedger v1.5 pa etiketë\nLedger v1.6 aktual\n");
    const drift = r.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT");
    expect(drift).toHaveLength(1);
    expect(drift[0]?.locator).toBe("idx.md:2 (syn-ver)");
  });

  it("current vetëm historik → DC-CURRENT-VALUE-MISSING; aktiv + old historik → pa false-positive", () => {
    const rMissing = scan("Historia: Ledger v1.6 (SUPERSEDED)\n");
    expect(rMissing.findings.some((f) => f.ruleId === "DC-CURRENT-VALUE-MISSING")).toBe(true);
    const rOk = scan("Ledger v1.6 aktual\nHistoria: Ledger v1.5 (SUPERSEDED)\n");
    expect(rOk.findings.filter((f) => f.ruleId === "DC-OLD-VALUE-DRIFT")).toHaveLength(0);
    expect(rOk.findings.filter((f) => f.ruleId === "DC-CURRENT-VALUE-MISSING")).toHaveLength(0);
  });
});
