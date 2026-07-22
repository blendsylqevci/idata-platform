/**
 * Correction Pass — TYPED ROLE ANALYZER: matrica e regresionit WA-001/WA-002
 * + siblings (empty-tag/comment split në bold-label/heading/list/table;
 * lidhëzat funksionale në parentetika). IDENTITETE VETËM SINTETIKE
 * ("Sinteti Alfa/Beta/…") — kurrë emra realë.
 */

import { describe, expect, it } from "vitest";
import { GitCliReader } from "../src/git-repository.js";
import {
  parentheticalIdentityCandidate, roleTokensUnknown, runDocCore, type DocCoreSurfaces,
} from "../src/profiles/doc-core.js";
import {
  analyzeRoleDocument, classifyParenthetical, visibleRoleFragment,
  FUNCTIONAL_ANNOTATION_WORDS, type RoleRegion,
} from "../src/role-analysis.js";
import { makeTempRepo } from "./helpers.js";

const APPROVED = ["pronari", "pm", "group owner", "human gate", "aip"];

/**
 * Opener comment-i i PAMBYLLUR i ndërtuar në runtime — burimi i testeve nuk
 * mban literal raw të pambyllur (rregulli i defuzimit të fixture-ve: skanimet
 * per-commit të historisë mbi rreshtat e shtuar nuk duhet të thyhen nga vetë
 * burimi i testit). Testi furnizon runtime inputin EKZAKT me opener real.
 */
const unclosedCommentOpen = (rest: string): string => ["<!", "--"].join("") + rest;

function roleSurfaces(overrides: Partial<DocCoreSurfaces> = {}): DocCoreSurfaces {
  return {
    orderSurfaces: [],
    chainRequiredSurfaces: [],
    milestoneSurfaces: [],
    authorities: [],
    approvedRolePhrases: APPROVED,
    roleRequiredSurfaces: ["roles.md"],
    roleOptionalSurfaces: [],
    ...overrides,
  };
}

/** Ekzekuton DOC-CORE mbi një dokument të vetëm roli — caller-level, jo unit. */
function scanRoles(content: string) {
  const repo = makeTempRepo();
  repo.write("roles.md", content.endsWith("\n") ? content : content + "\n");
  repo.commitAll("role fixture");
  return runDocCore(new GitCliReader(repo.dir), "HEAD", roleSurfaces(), undefined);
}

function scanRolesWith(content: string, approved: string[]) {
  const repo = makeTempRepo();
  repo.write("roles.md", content.endsWith("\n") ? content : content + "\n");
  repo.commitAll("role fixture");
  return runDocCore(new GitCliReader(repo.dir), "HEAD", roleSurfaces({ approvedRolePhrases: approved }), undefined);
}

const byRule = (r: { findings: Array<{ ruleId: string }> }, ruleId: string) =>
  r.findings.filter((f) => f.ruleId === ruleId);

describe("WA-001 — label recognition mbi tekstin e DUKSHËM (split markup)", () => {
  it("empty-tag split label → vlera skanohet + markup REVIEW; zero content në output", () => {
    const r = scanRoles("Own<span></span>er: Sinteti Alfa");
    expect(byRule(r, "DC-ROLE-VOCAB").length).toBeGreaterThanOrEqual(1);
    expect(byRule(r, "DC-ROLE-MARKUP").length).toBeGreaterThanOrEqual(1);
    expect(JSON.stringify(r)).not.toContain("Sinteti");
  });

  it("HTML-comment split label → vlera skanohet", () => {
    const r = scanRoles("Own<!-- meta -->er: Sinteti Alfa");
    expect(byRule(r, "DC-ROLE-VOCAB").length).toBeGreaterThanOrEqual(1);
    expect(JSON.stringify(r)).not.toContain("Sinteti");
  });

  it("unsplit positive control → ende ndizet; zero markup finding", () => {
    const r = scanRoles("Owner: Sinteti Alfa");
    expect(byRule(r, "DC-ROLE-VOCAB")).toHaveLength(1);
    expect(byRule(r, "DC-ROLE-MARKUP")).toHaveLength(0);
  });

  it("split në heading → vlera skanohet", () => {
    const r = scanRoles("# Own<span></span>er: Sinteti Alfa");
    expect(byRule(r, "DC-ROLE-VOCAB").length).toBeGreaterThanOrEqual(1);
  });

  it("split në list-item dhe task-list → vlera skanohet", () => {
    const r = scanRoles([
      "- Own<span></span>er: Sinteti Alfa",
      "- [ ] **Own<span></span>er:** Sinteti Beta",
    ].join("\n"));
    expect(byRule(r, "DC-ROLE-VOCAB").length).toBeGreaterThanOrEqual(2);
    expect(JSON.stringify(r)).not.toContain("Sinteti");
  });

  it("split në bold-label → vlera skanohet", () => {
    const r = scanRoles("**Own<span></span>er:** Sinteti Alfa");
    expect(byRule(r, "DC-ROLE-VOCAB").length).toBeGreaterThanOrEqual(1);
  });

  it("split table-HEADER → kolona njihet, qeliza skanohet + header markup REVIEW", () => {
    const r = scanRoles([
      "| Own<span></span>er | Status |",
      "|---|---|",
      "| Sinteti Alfa | active |",
    ].join("\n"));
    expect(byRule(r, "DC-ROLE-VOCAB")).toHaveLength(1);
    expect(byRule(r, "DC-ROLE-MARKUP").length).toBeGreaterThanOrEqual(1);
    expect(JSON.stringify(r)).not.toContain("Sinteti");
  });

  it("comment-split table-HEADER → kolona njihet, qeliza skanohet", () => {
    const r = scanRoles([
      "| Own<!-- x -->er | Status |",
      "|---|---|",
      "| Sinteti Alfa | active |",
    ].join("\n"));
    expect(byRule(r, "DC-ROLE-VOCAB")).toHaveLength(1);
  });

  it("split markup brenda VLERËS së një qelize role → markup + vocab REVIEW", () => {
    const r = scanRoles([
      "| Owner | Status |",
      "|---|---|",
      "| Sin<span></span>teti | active |",
    ].join("\n"));
    expect(byRule(r, "DC-ROLE-MARKUP").length).toBeGreaterThanOrEqual(1);
    expect(byRule(r, "DC-ROLE-VOCAB")).toHaveLength(1);
  });

  it("tag NDARËS me semantikë renderer-i nuk bashkon fjalë — jo label aktiv, por markup REVIEW i pavarur", () => {
    const r = scanRoles("Own<br>er: Sinteti Alfa");
    // renderer-i e ndan ("Own / er:") — vlera nuk bëhet role-value aktive,
    // por rreshti markup-bearing që formon label nën kolapsim total → REVIEW.
    expect(byRule(r, "DC-ROLE-VOCAB")).toHaveLength(0);
    expect(byRule(r, "DC-ROLE-MARKUP")).toHaveLength(1);
    const td = scanRoles("<td>Own</td><td>er: Sinteti Alfa</td>");
    expect(byRule(td, "DC-ROLE-VOCAB")).toHaveLength(0);
    expect(byRule(td, "DC-ROLE-MARKUP")).toHaveLength(1);
  });

  it("tag me tekst të dukshëm brenda nuk krijon label që renderer-i s'e tregon", () => {
    // "Own<span>x</span>er:" renderohet "Ownxer:" — jo label; zero forcim.
    const r = scanRoles("Own<span>x</span>er: tekst i lirë");
    expect(byRule(r, "DC-ROLE-VOCAB")).toHaveLength(0);
    expect(byRule(r, "DC-ROLE-MARKUP")).toHaveLength(0);
  });

  it("malformed tag/comment → fail-closed, kurrë silent", () => {
    expect(() => scanRoles("Own<span er: Sinteti"))
      .toThrow("INCOMPLETE_SCAN:MARKDOWN_HTML_MALFORMED");
    expect(() => scanRoles("Own" + unclosedCommentOpen(" er: Sinteti")))
      .toThrow("INCOMPLETE_SCAN:MARKDOWN_COMMENT_MALFORMED");
  });

  it("approved-role inverse: split label me vlerë të aprovuar → zero vocab/identity (markup mbetet REVIEW)", () => {
    const r = scanRoles("Own<span></span>er: Pronari");
    expect(byRule(r, "DC-ROLE-VOCAB")).toHaveLength(0);
    expect(byRule(r, "DC-ROLE-IDENTITY")).toHaveLength(0);
    expect(byRule(r, "DC-ROLE-MARKUP").length).toBeGreaterThanOrEqual(1);
    const clean = scanRoles("Owner: Pronari");
    expect(clean.findings).toHaveLength(0);
  });

  it("split label në PREFIX të një comment-i → body i comment-it mbetet privacy evidence", () => {
    const r = scanRoles("**Own<b></b>er:** Pronari <!-- Sinteti Alfa -->");
    expect(byRule(r, "DC-ROLE-VOCAB").length).toBeGreaterThanOrEqual(1);
    expect(JSON.stringify(r)).not.toContain("Sinteti");
  });

  it("split label BRENDA body-t të comment-it → ende privacy-review (jo governance aktive)", () => {
    const r = scanRoles("<!-- Own<b></b>er: Sinteti Alfa -->");
    expect(byRule(r, "DC-ROLE-VOCAB").length).toBeGreaterThanOrEqual(1);
    expect(JSON.stringify(r)).not.toContain("Sinteti");
  });

  it("fenced code mbetet joaktiv edhe me label të split-uar brenda", () => {
    const r = scanRoles(["```", "Own<span></span>er: Sinteti Alfa", "```"].join("\n"));
    expect(r.findings).toHaveLength(0);
  });
});

describe("WA-002 — parenthetical disposition (lidhëzat s'ndajnë termat residualë)", () => {
  it("2 dhe 3 terma me 'dhe' → kandidat", () => {
    expect(parentheticalIdentityCandidate("Pronari (Sinteti dhe Alfa)", APPROVED)).toBe(true);
    expect(parentheticalIdentityCandidate("Pronari (Sinteti dhe Alfa dhe Beta)", APPROVED)).toBe(true);
  });

  it("'ose' dhe kombinimet → kandidat", () => {
    expect(parentheticalIdentityCandidate("Pronari (Sinteti ose Alfa)", APPROVED)).toBe(true);
    expect(parentheticalIdentityCandidate("Pronari (Sinteti dhe Alfa ose Beta)", APPROVED)).toBe(true);
  });

  it("presja dhe '&' VAZHDOJNË të ndezin (positive regression controls)", () => {
    expect(parentheticalIdentityCandidate("Pronari (Sinteti, Alfa)", APPROVED)).toBe(true);
    expect(parentheticalIdentityCandidate("Pronari (Sinteti & Alfa)", APPROVED)).toBe(true);
  });

  it("një term / dy terma ngjitur", () => {
    expect(parentheticalIdentityCandidate("Pronari (Sinteti)", APPROVED)).toBe(true);
    expect(parentheticalIdentityCandidate("Pronari (Sinteti Alfa)", APPROVED)).toBe(true);
    // term i vetëm residual mes fjalësh funksionale — s'kalon më silent
    expect(parentheticalIdentityCandidate("Pronari (review nga Sinteti)", APPROVED)).toBe(true);
  });

  it("casing: TitleCase / UPPERCASE / lowercase / Unicode; apostrof dhe vizë", () => {
    expect(parentheticalIdentityCandidate("Pronari (Sinteti dhe Alfa)", APPROVED)).toBe(true);
    expect(parentheticalIdentityCandidate("Pronari (SINTETI dhe ALFA)", APPROVED)).toBe(true);
    expect(parentheticalIdentityCandidate("Pronari (sinteti dhe alfa)", APPROVED)).toBe(true);
    expect(parentheticalIdentityCandidate("Pronari (Ëlfa dhe Çeta)", APPROVED)).toBe(true);
    expect(parentheticalIdentityCandidate("Pronari (S'inteti dhe Alfa-Beta)", APPROVED)).toBe(true);
  });

  it("annotation i aprovuar dhe functional prose → clean (inverse të VËRTETA)", () => {
    expect(parentheticalIdentityCandidate("Pronari (aprovon çdo ndryshim të këtij dokumenti)", APPROVED)).toBe(false);
    expect(parentheticalIdentityCandidate("Owner (nëse duhet review)", APPROVED)).toBe(false);
    expect(parentheticalIdentityCandidate("Owner (review approval)", APPROVED)).toBe(false);
    expect(parentheticalIdentityCandidate("Group Owner (Human Gate)", APPROVED)).toBe(false);
    expect(parentheticalIdentityCandidate("Owner (AIP)", APPROVED)).toBe(false);
    expect(parentheticalIdentityCandidate("Owner (—)", APPROVED)).toBe(false);
    // fraza të aprovuara të lidhura me lidhëz funksionale mbeten clean
    expect(parentheticalIdentityCandidate("Owner (Group Owner dhe Human Gate)", APPROVED)).toBe(false);
  });

  it("disposition-et janë eksplicite; e paqarta → REVIEW, kurrë silent approval", () => {
    expect(classifyParenthetical("Sinteti dhe Alfa", APPROVED))
      .toEqual({ disposition: "IDENTITY_REVIEW", residualTerms: 2 });
    expect(classifyParenthetical("Sinteti", APPROVED))
      .toEqual({ disposition: "IDENTITY_REVIEW", residualTerms: 1 });
    expect(classifyParenthetical("drafti", APPROVED))
      .toEqual({ disposition: "AMBIGUOUS_REVIEW", residualTerms: 1 });
    expect(classifyParenthetical("aprovon review", APPROVED))
      .toEqual({ disposition: "APPROVED_ANNOTATION", residualTerms: 0 });
    expect(classifyParenthetical("Human Gate", APPROVED))
      .toEqual({ disposition: "APPROVED_ANNOTATION", residualTerms: 0 });
  });

  it("invariant: çdo term ose pjesë e frazës së aprovuar/funksionale, ose → REVIEW", () => {
    const cases = [
      "Sinteti dhe Alfa", "Sinteti ose Alfa", "review nga Sinteti", "drafti",
      "aprovon çdo ndryshim", "Human Gate", "Group Owner dhe Human Gate", "nëse duhet review",
    ];
    for (const inner of cases) {
      const words = [...inner.matchAll(/\p{L}[\p{L}'’-]*/gu)].map((w) => w[0].toLowerCase());
      const allCovered = words.every((w) =>
        FUNCTIONAL_ANNOTATION_WORDS.has(w)
        || APPROVED.some((p) => p.split(" ").includes(w)));
      const { disposition } = classifyParenthetical(inner, APPROVED);
      if (disposition === "APPROVED_ANNOTATION") {
        expect(allCovered).toBe(true);
      } else {
        expect(allCovered).toBe(false);
      }
    }
  });

  it("caller-level (runDocCore, JO vetëm helper unit): label, tabelë dhe kombinim me split", () => {
    const label = scanRoles("Owner: Pronari (Sinteti dhe Alfa)");
    expect(byRule(label, "DC-ROLE-IDENTITY")).toHaveLength(1);
    expect(JSON.stringify(label)).not.toContain("Sinteti");

    const ose = scanRoles("Owner: Pronari (Sinteti ose Alfa)");
    expect(byRule(ose, "DC-ROLE-IDENTITY")).toHaveLength(1);

    const cell = scanRoles([
      "| Owner | Status |",
      "|---|---|",
      "| Pronari (Sinteti dhe Alfa) | active |",
    ].join("\n"));
    expect(byRule(cell, "DC-ROLE-IDENTITY")).toHaveLength(1);

    const comma = scanRoles("Owner: Pronari (Sinteti, Alfa)");
    expect(byRule(comma, "DC-ROLE-IDENTITY")).toHaveLength(1);

    // WA-001 + WA-002 bashkë: label i split-uar + lidhëz funksionale
    const combined = scanRoles("Own<span></span>er: Pronari (Sinteti dhe Alfa)");
    expect(byRule(combined, "DC-ROLE-IDENTITY")).toHaveLength(1);

    const approvedInverse = scanRoles("Owner: Group Owner (Human Gate)");
    expect(byRule(approvedInverse, "DC-ROLE-IDENTITY")).toHaveLength(0);
    expect(byRule(approvedInverse, "DC-ROLE-VOCAB")).toHaveLength(0);

    // terma jashtë kllapave → rruga vocab (kontroll ndarjeje identity/vocab)
    const outside = scanRoles("Owner: Sinteti Alfa");
    expect(byRule(outside, "DC-ROLE-VOCAB")).toHaveLength(1);
    expect(byRule(outside, "DC-ROLE-IDENTITY")).toHaveLength(0);
    expect(roleTokensUnknown("Sinteti Alfa", APPROVED)).toBeGreaterThan(0);
  });
});

describe("reprezentimi i tipizuar — vetitë strukturore", () => {
  it("visible heq zero-content pa boundary; separator-ët bëhen hapësirë; flattened është vetëm sinjal", () => {
    const doc = analyzeRoleDocument("Own<span></span>er: X\nOwn<br>er: Y\n");
    expect(doc.lines[0]?.visible).toBe("Owner: X");
    expect(doc.lines[0]?.markupBearing).toBe(true);
    expect(doc.lines[1]?.visible).toBe("Own er: Y");
    expect(doc.lines[1]?.flattened).toBe("Owner: Y");
  });

  it("comment një-rreshtor → zero tekst i dukshëm; body ruhet si evidencë raw", () => {
    const doc = analyzeRoleDocument("Own<!-- Sinteti -->er: X\n");
    expect(doc.lines[0]?.visible).toBe("Owner: X");
    expect(doc.comments).toHaveLength(1);
    expect(doc.comments[0]?.body).toContain("Sinteti");
  });

  it("fragmentet janë leniente (strict-i zbatohet nga analiza e dokumentit të plotë)", () => {
    expect(visibleRoleFragment("Own<b></b>er").visible).toBe("Owner");
    expect(visibleRoleFragment("tekst me < i lirë").visible).toBe("tekst me < i lirë");
  });
});

/* ───────── KONTRATA ME KATËR GJENDJE + DISCOVERY FAIL-SAFE (correction pass) ───────── */

function regionsOf(content: string): RoleRegion[] {
  return analyzeRoleDocument(content.endsWith("\n") ? content : content + "\n", APPROVED).regions;
}

describe("fail-safe: bypass-et e konfirmuara → REVIEW/exit 2 me STATE + reason + locator", () => {
  it("comment BRENDA label-it → REVIEW dhe body evidencë e TË NJËJTIT rajon", () => {
    const rs = regionsOf("Own<!-- Sinteti Alfa -->er: Pronari");
    expect(rs).toHaveLength(1);
    expect(rs[0]).toMatchObject({
      kind: "label", line: 1, state: "REVIEW", commentEvidence: true, regionId: "label:1:0",
    });
    expect(rs[0]?.reasons).toContain("ROLE_VOCAB"); // nga body — jo rajon i dytë
    const r = scanRoles("Own<!-- Sinteti Alfa -->er: Pronari");
    const vocab = byRule(r, "DC-ROLE-VOCAB");
    expect(vocab).toHaveLength(1);
    expect(vocab[0]?.locator).toBe("roles.md:1");
    expect(JSON.stringify(r)).not.toContain("Sinteti");
  });

  it("entity decimal në label (discovery emeton rajon me STATE, jo thjesht finding)", () => {
    const rs = regionsOf("Own&#101;r: Sinteti Alfa");
    expect(rs).toHaveLength(1);
    expect(rs[0]).toMatchObject({
      kind: "label-candidate", line: 1, state: "REVIEW", regionId: "label-candidate:1:0",
    });
    expect(rs[0]?.reasons).toEqual(["ROLE_MARKUP"]);
    const r = scanRoles("Own&#101;r: Sinteti Alfa");
    const markup = byRule(r, "DC-ROLE-MARKUP");
    expect(markup).toHaveLength(1);
    expect(markup[0]?.locator).toBe("roles.md:1");
    expect(JSON.stringify(r)).not.toContain("Sinteti");
  });

  it("entity hex + named colon + backslash-escape në label → REVIEW", () => {
    for (const line of [
      "Own&#x65;r: Sinteti Alfa",
      "Owner&colon; Sinteti Alfa",
      "Owner\\: Sinteti Alfa",
    ]) {
      const rs = regionsOf(line);
      expect(rs.some((rg) => rg.state === "REVIEW" && rg.reasons.includes("ROLE_MARKUP"))).toBe(true);
      const r = scanRoles(line);
      expect(byRule(r, "DC-ROLE-MARKUP").length).toBeGreaterThanOrEqual(1);
      expect(JSON.stringify(r)).not.toContain("Sinteti");
    }
  });

  it("zero-width/format-char në label → REVIEW", () => {
    const ZWSP = String.fromCharCode(0x200b);
    const line = "Own" + ZWSP + "er: Sinteti Alfa";
    const rs = regionsOf(line);
    expect(rs.some((rg) => rg.state === "REVIEW" && rg.reasons.includes("ROLE_MARKUP"))).toBe(true);
    const r = scanRoles(line);
    expect(byRule(r, "DC-ROLE-MARKUP").length).toBeGreaterThanOrEqual(1);
  });

  it("parenthetical vetëm numra / format telefoni → REVIEW (identifikues i mundshëm)", () => {
    const digits = ["123", "456", "789"].join("");
    const phone = "+" + ["383", "44", "123", "456"].join("");
    expect(classifyParenthetical(digits, APPROVED))
      .toEqual({ disposition: "AMBIGUOUS_REVIEW", residualTerms: 1 });
    expect(classifyParenthetical(phone, APPROVED))
      .toEqual({ disposition: "AMBIGUOUS_REVIEW", residualTerms: 1 });
    const r = scanRoles(`Owner: Pronari (${digits})`);
    const ident = byRule(r, "DC-ROLE-IDENTITY");
    expect(ident).toHaveLength(1);
    expect(ident[0]?.locator).toBe("roles.md:1");
    const rp = scanRoles(`Owner: Pronari (${phone})`);
    expect(byRule(rp, "DC-ROLE-IDENTITY")).toHaveLength(1);
    // placeholder-i bosh mbetet i barasvlershëm me bosh (inverse)
    expect(classifyParenthetical("—", APPROVED))
      .toEqual({ disposition: "APPROVED_ANNOTATION", residualTerms: 0 });
  });

  it("TË GJITHA kolonat role-bearing skanohen — region-id i veçantë secila", () => {
    const content = [
      "| Owner | Approver | Human Gate |",
      "|---|---|---|",
      "| Sinteti Alfa | Sinteti Beta | Sinteti Gama |",
    ].join("\n");
    const rs = regionsOf(content);
    const cells = rs.filter((rg) => rg.kind === "table-cell");
    expect(cells.map((rg) => rg.regionId).sort())
      .toEqual(["table-cell:3:0", "table-cell:3:1", "table-cell:3:2"]);
    expect(cells.every((rg) => rg.state === "REVIEW")).toBe(true);
    const r = scanRoles(content);
    expect(byRule(r, "DC-ROLE-VOCAB")).toHaveLength(3);
    expect(new Set(rs.map((rg) => rg.regionId)).size).toBe(rs.length); // dedup deterministik
  });
});

describe("fail-safe: katër gjendjet", () => {
  it("INACTIVE: label dhe comment brenda fenced code → zero finding, gjendje eksplicite", () => {
    const content = ["```", "Owner: Sinteti Alfa", "<!-- Owner: Sinteti Beta -->", "```"].join("\n");
    const rs = regionsOf(content);
    expect(rs.length).toBeGreaterThanOrEqual(1);
    expect(rs.every((rg) => rg.state === "INACTIVE")).toBe(true);
    const r = scanRoles(content);
    expect(r.findings).toHaveLength(0);
  });

  it("CLEAN: vetëm sintaksa kanonike + vlera e allowlisted; gjendja eksplicite", () => {
    const rs = regionsOf("Owner: Pronari");
    expect(rs).toHaveLength(1);
    expect(rs[0]).toMatchObject({ kind: "label", state: "CLEAN", reasons: [] });
    expect(scanRoles("Owner: Pronari").findings).toHaveLength(0);
  });

  it("INCOMPLETE_SCAN: markup/comment i dëmtuar → exit 2, kurrë silent", () => {
    expect(() => scanRoles("Own<span er: Sinteti"))
      .toThrow("INCOMPLETE_SCAN:MARKDOWN_HTML_MALFORMED");
    expect(() => scanRoles("Own" + unclosedCommentOpen(" er: Sinteti")))
      .toThrow("INCOMPLETE_SCAN:MARKDOWN_COMMENT_MALFORMED");
  });

  it("inverse: prozë jo-role-bearing me entity/escape → PA rajon REVIEW (jo REVIEW masiv)", () => {
    const content = "Teksti ka &amp; dhe \\: por asnjë etiketë roli këtu.";
    expect(regionsOf(content).filter((rg) => rg.state === "REVIEW")).toHaveLength(0);
    expect(scanRoles(content).findings).toHaveLength(0);
  });

  it("invarianti: çdo rajon ka saktësisht një gjendje nga kontrata dhe id unik", () => {
    const content = [
      "Owner: Pronari",
      "Own&#101;r: Sinteti Alfa",
      "```",
      "Owner: Sinteti Beta",
      "```",
      "| Kush | Afati |",
      "|---|---|",
      "| Sinteti Gama | — |",
    ].join("\n");
    const rs = regionsOf(content);
    const states = new Set(rs.map((rg) => rg.state));
    for (const st of states) expect(["INACTIVE", "CLEAN", "REVIEW"]).toContain(st);
    expect(new Set(rs.map((rg) => rg.regionId)).size).toBe(rs.length);
    expect(rs.some((rg) => rg.state === "CLEAN")).toBe(true);
    expect(rs.some((rg) => rg.state === "REVIEW")).toBe(true);
    expect(rs.some((rg) => rg.state === "INACTIVE")).toBe(true);
  });

  it("precision: fraza e aprovuar e mbyllur me pikë trajtohet konsistent (jo split)", () => {
    const withLlc = [...APPROVED, "icode l.l.c"];
    const r1 = scanRolesWith("Owner: iCode L.L.C.", withLlc);
    const r2 = scanRolesWith("Owner: iCode L.L.C", withLlc);
    expect(byRule(r1, "DC-ROLE-VOCAB")).toHaveLength(0);
    expect(byRule(r2, "DC-ROLE-VOCAB")).toHaveLength(0);
  });
});
