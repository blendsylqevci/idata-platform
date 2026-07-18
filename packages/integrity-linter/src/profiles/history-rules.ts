/**
 * HISTORY-PRIV — klasat canonical mbi ADDED-LINES (07 §Per-commit + §Acceptance;
 * Hardening III §6/§7). Kontroll POLICY-HISTORY class — JO semantika e plotë
 * domain e SRC-CLOSURE/SEF-ATK (ato mbeten NOT_IMPLEMENTED).
 *
 * §6C — PA BLANKET EXEMPTIONS:
 *  - etiketa historike vlen VETËM për old-value drift (te motori i drift-it) —
 *    JO për secrets/regulated IDs/evidence metrics/operational claims/maturity;
 *  - fjalët "synthetic/fixture" në dokument aktiv NUK janë suppression;
 *  - mohimi qeveris LOKALISHT claim-in (dritare para match-it) — "jo" i
 *    pavarur/fundor NUK e anulon kontrollin.
 * §7 — qelizat lexohen me parser-in GFM të përbashkët (escaped pipe/code-span/arity).
 */

import { mixedDepthTableRow, parseTableRow, tableContextAt, isSeparatorRow } from "../markdown-table.js";
import { normalizeApostrophesForDetection, NOT_LETTER_AFTER, NOT_LETTER_BEFORE } from "../machine-token.js";
import { containsLocalAbsolutePath } from "../path-policy.js";

export interface RuleContext {
  line: string;
  path: string;
  lineNo: number;
  /** Snapshot-i i plotë i file-it në ATË commit (tracked Git object) — lazily. */
  snapshot(): string | undefined;
}

export interface ContentRule {
  ruleId: string;
  category: string;
  severity: "ERROR" | "WARNING" | "REVIEW";
  test(ctx: RuleContext): boolean;
}

function frag(...parts: string[]): string {
  return parts.join("");
}

/**
 * Mohim LOKAL (§6C + P-V F5): tokeni mohues duhet të qëndrojë në dritaren
 * 24-karaktere MENJËHERË para claim-it — "…, jo 0" në fund të rreshtit nuk e
 * anulon claim-in. F5: VETËM një mohim i VETËM i lidhur drejtpërdrejt me
 * claim-in mund ta suppress-ojë; DY a më shumë mohues në fjalinë e claim-it
 * ("s'është që PoC NUK është …") = mohim AMBIG → NUK suppress-ohet (finding-u
 * primar mbetet). Pa NLP — numërim i thjeshtë mohuesish në sentence scope.
 */
export function locallyNegated(line: string, matchIndex: number): boolean {
  const nline = normalizeApostrophesForDetection(line);
  const win = nline.slice(Math.max(0, matchIndex - 24), matchIndex);
  const winRe = new RegExp(
    "(?:" + NOT_LETTER_BEFORE + "(?:nuk|mos|ndalohet)" + NOT_LETTER_AFTER + "|" + NOT_LETTER_BEFORE + "s')" +
    "\\s*\\S*\\s*$", "iu",
  );
  if (!winRe.test(win)) return false;
  // F5: kufiri i fjalisë së claim-it (delimituesit përfshijnë U+2028/U+2029 — §1 P-VI)
  let start = 0;
  for (const d of [".", ";", "!", "?", "\u00B7", "\u2028", "\u2029"]) {
    const p = nline.lastIndexOf(d, matchIndex);
    if (p + 1 > start) start = p + 1;
  }
  let end = nline.length;
  for (const d of [".", ";", "!", "?", "\u00B7", "\u2028", "\u2029"]) {
    const p = nline.indexOf(d, matchIndex);
    if (p !== -1 && p < end) end = p;
  }
  const sentence = nline.slice(start, end);
  const negRe = new RegExp(
    "(?:" + NOT_LETTER_BEFORE + "(?:nuk|mos|ndalohet)" + NOT_LETTER_AFTER + "|" + NOT_LETTER_BEFORE + "s')", "giu",
  );
  const negators = sentence.match(negRe) ?? [];
  return negators.length === 1;
}


/** Tag-set-i i Treatment sipas canonical 07_PRODATA §1A (REQ/MD/OB/REF/NO). */
export const TREATMENT_TOKENS = new Set(["REQ", "MD", "OB", "REF", "NO"]);

/** Stage-t e Maturity sipas canonical 07_PRODATA §1B (+ qualifier PROVEN-PoC §1C). */
export const MATURITY_STAGES = ["GAP", "EVIDENCE", "SPECIFIED", "MODELED", "IMPLEMENTED", "TESTED", "SIGNED-OFF"] as const;

export function buildContentRules(): ContentRule[] {
  const dash5 = frag("--", "---");
  const beginKey = new RegExp(frag(dash5, "BEGIN ", "(?:RSA |EC |OPENSSH )?", "PRIVATE", " KEY"));
  const ghToken = frag("g", "ho", "_[A-Za-z0-9_]{8,}");
  const ghPat = frag("github", "_", "pat", "_[A-Za-z0-9_]{8,}");
  const skToken = frag("\\bsk", "-[A-Za-z0-9_-]{20,}");
  const awsKey = frag("AK", "IA", "[0-9A-Z]{16}");
  const tokenClass = new RegExp(`${ghToken}|${ghPat}|${skToken}|${awsKey}`);
  const credWords = [
    frag("api", "[_-]?", "key"), frag("pass", "word"), frag("tok", "en"), frag("sec", "ret"),
    frag("d", "b", "[_-]?", "url"), frag("data", "base", "[_-]?", "url"),
  ].join("|");
  // §6B PARITY me guard-in: ÇDO vlerë non-space e caktuar (edhe 1 karakter,
  // edhe punctuation, edhe me thonjëza) — jo gjatësi minimale.
  const credAssign = new RegExp(frag("(?:^|[^A-Za-z])(?:", credWords, ")\\s*[:=]\\s*\\S+"), "i");
  // P-VI §4: klasifikuesi i centralizuar (rrënjët POSIX + ~/ + Windows + UNC)
  const idLabels = [
    frag("N", "UI", "\\w{0,2}"), frag("N", "IF"), frag("TV", "SH"), frag("V", "AT"),
    frag("Application", "[ _-]?ID"), frag("Fiscal", "\\w*", "[ _-]?(?:No|Nr|ID)"),
    frag("transaction", "[ _-]?id"), frag("NUI", "KF"),
  ].join("|");
  const regulatedIdContext = new RegExp(frag("\\b(?:", idLabels, ")\\b", "[^0-9\\n]{0,12}", "\\d{7,}"), "i");
  const implWord = frag("IMPLE", "MENTED");
  const testWord = frag("TES", "TED");
  const prodReady = frag("PRODUCTION", "[- ]READY");
  const promo = new RegExp(`\\b(${implWord}|${testWord}|${prodReady})\\b`, "g");
  const provenPoc = new RegExp(frag("PROVEN", "[- ]PoC"), "i");
  // §5 P-IV: mohimi strukturor JO- lidhet me MATCH-in konkret (dritarja
  // menjëherë para tij), JO me gjithë rreshtin — "PoC JO-TESTED; PoC
  // IMPLEMENTED now" e kap IMPLEMENTED. "jo" brenda fjale s'është operator.
  const joBeforeMatch = /(^|[^A-Za-zë])JO[-\s]$/;
  const mockQualifierBefore = /(vetëm mock|mock lokal|mock evidence)[^A-Za-z0-9]{0,10}$/i;
  const maturityAlt = MATURITY_STAGES.map((st) => st.replace("-", "[- ]")).join("|");
  const operationalClaim = new RegExp(
    "(xhiro(ja|n)?\\s+(ditore|mujore|reale)|qarkullim(i)?\\s+(ditor|mujor|real)\\b|shitjet\\s+(ditore|reale)|barazim(i|et)?\\s+(ditor|real)\\w*|saldo\\s+reale?\\b|gjendja\\s+(reale|aktuale)\\s+e\\s+(arkës|stokut|barazimit)|operon\\s+(aktualisht|çdo ditë|realisht)|klient[ëe]?\\s+aktiv|fatura(t)?\\s+reale\\s+t[ëe]\\s+(muajit|ditës)|arka\\s+(bosh|aktive|e mbyllur)|legacy\\s+\\w+\\s+(count|numër))",
    "ig",
  );

  return [
    // ── 2) identifiers / secrets — ASNJË exemption (as historike, as sintetike) ─
    { ruleId: "HP-PRIVATE-KEY-BLOCK", category: "secrets", severity: "ERROR", test: (c) => beginKey.test(c.line) },
    { ruleId: "HP-TOKEN-CLASS", category: "secrets", severity: "WARNING", test: (c) => tokenClass.test(c.line) },
    { ruleId: "HP-CREDENTIAL-ASSIGN", category: "secrets", severity: "WARNING", test: (c) => credAssign.test(c.line) },
    { ruleId: "HP-ABSOLUTE-LOCAL-PATH", category: "privacy", severity: "WARNING", test: (c) => containsLocalAbsolutePath(c.line) },
    { ruleId: "HP-REGULATED-ID-CLASS", category: "privacy", severity: "WARNING", test: (c) => /(^|\D)\d{12}(\D|$)/.test(c.line) },
    { ruleId: "HP-REGULATED-ID-CONTEXT", category: "privacy", severity: "WARNING", test: (c) => regulatedIdContext.test(c.line) },
    {
      ruleId: "HP-FINANCIAL-VALUE-CLASS", category: "privacy", severity: "REVIEW",
      // kualifikuesit tematikë ligjorë mbeten (gjoba/sanksion/€-ligj); "sintetik/
      // synthetic/fixture" NUK janë më bypass (§6C)
      test: (c) => /\b\d{2,3}[.,]\d{3}[.,]\d{2}\b/.test(c.line) && !/(€.*ligj|gjob[aë]|sanksion)/i.test(c.line),
    },

    // ── 3) evidence metrics — pa separator; PA exemption historik/sintetik (§6C) ─
    {
      ruleId: "HP-EVIDENCE-METRIC", category: "evidence", severity: "REVIEW",
      test: (c) =>
        /\b(?:\d{1,3}(?:[,.]\d{3})+|\d{3,})\s*(?:rreshta|rows|records|tabela|tables)\b/i.test(c.line) &&
        /(backup|extraction|nxjerr|eksport|dump|restore|MDF|LDF|evidence)/i.test(c.line),
    },

    // ── 4) operational-state claims — mohim LOKAL PER-MATCH (§5 P-IV): çdo
    //      match gjykohet veçmas; mohimi i claim-it të parë NUK e përjashton
    //      claim-in pasues në të njëjtin rresht ──────────────────────────────
    {
      ruleId: "HP-OPERATIONAL-CLAIM", category: "privacy", severity: "REVIEW",
      test: (c) => {
        operationalClaim.lastIndex = 0;
        let m: RegExpExecArray | null;
        while ((m = operationalClaim.exec(c.line)) !== null) {
          if (!locallyNegated(c.line, m.index)) return true;
        }
        return false;
      },
    },

    // ── 5) Treatment — kolonë e parsuar me GFM parser (§7) ──────────────────
    {
      ruleId: "HP-TREATMENT-VOCAB", category: "vocabulary", severity: "WARNING",
      test: (c) => {
        if (!c.line.includes("|") || isSeparatorRow(c.line)) return false;
        const snap = c.snapshot();
        if (snap !== undefined) {
          const tctx = tableContextAt(snap, c.lineNo);
          const col = tctx?.columnIndex(/^Treatment$/i);
          if (tctx !== undefined && col !== undefined) {
            const row = parseTableRow(c.line);
            if (row.malformed) return false; // e kap HP-TABLE-MALFORMED
            const cell = row.cells[col]?.replace(/[`*]/g, "").trim();
            if (cell === undefined || cell === "" || cell === "—" || cell === "-") return false;
            const tokens = cell.split("+").map((t) => t.trim());
            return tokens.some((t) => !TREATMENT_TOKENS.has(t));
          }
        }
        // fallback jashtë kolonës Treatment: vetëm kombinim i përzier known+unknown
        const row = parseTableRow(c.line);
        for (const cellRaw of row.cells) {
          const cell = cellRaw.replace(/[`*]/g, "").trim();
          if (!/^[A-Z_]{2,20}(\+[A-Z_]{2,20})+$/.test(cell)) continue;
          const tokens = cell.split("+");
          const known = tokens.filter((t) => TREATMENT_TOKENS.has(t));
          if (known.length > 0 && known.length !== tokens.length) return true;
        }
        return false;
      },
    },

    // ── 6) PoC promotion — mohim PER-MATCH (§5 P-IV): "JO-TESTED" e mohon
    //      VETËM token-in e vet; "PoC JO-TESTED; PoC IMPLEMENTED" e kap
    //      IMPLEMENTED. Kualifikuesit mock janë gjithashtu match-lokalë. ─────
    {
      ruleId: "HP-POC-PROMOTION", category: "maturity", severity: "WARNING",
      test: (c) => {
        if (!/\b(PoC(?:-\d+)?|simulator|mock|feasibility[- ]only)\b/i.test(c.line)) return false;
        const cleaned = c.line.replace(provenPoc, " ");
        promo.lastIndex = 0;
        let m: RegExpExecArray | null;
        while ((m = promo.exec(cleaned)) !== null) {
          const before = cleaned.slice(Math.max(0, m.index - 28), m.index);
          if (joBeforeMatch.test(before.slice(-4))) continue;      // JO-TESTED etj.
          if (mockQualifierBefore.test(before)) continue;          // "vetëm mock ..."
          if (locallyNegated(cleaned, m.index)) continue;
          return true;
        }
        return false;
      },
    },

    // ── 7) mixed maturity — kolonë Maturity me GFM parser (§7) ──────────────
    {
      ruleId: "HP-MIXED-MATURITY", category: "maturity", severity: "REVIEW",
      test: (c) => {
        if (!c.line.includes("|") || isSeparatorRow(c.line)) return false; // outer pipes opsionale (§4 P-IV)
        const snap = c.snapshot();
        if (snap === undefined) return false;
        const tctx = tableContextAt(snap, c.lineNo);
        const col = tctx?.columnIndex(/Maturity/i);
        if (tctx === undefined || col === undefined) return false;
        const row = parseTableRow(c.line);
        if (row.malformed) return false;
        const cell = row.cells[col]?.replace(/[`*]/g, "");
        if (cell === undefined || /→/.test(cell)) return false;
        const cleaned = cell.replace(provenPoc, " "); // PROVEN-PoC = qualifier (§1C)
        const stages = new Set<string>();
        const re = new RegExp(`\\b(${maturityAlt})\\b`, "g");
        let m: RegExpExecArray | null;
        while ((m = re.exec(cleaned)) !== null) {
          if (m[1] !== undefined) stages.add(m[1].replace(" ", "-").toUpperCase());
        }
        return stages.size >= 2;
      },
    },

    // ── 8) qeliza mandatory bosh + ariteti/malformim (§7) ───────────────────
    {
      ruleId: "HP-EMPTY-MANDATORY-CELL", category: "structure", severity: "REVIEW",
      test: (c) => {
        if (!c.line.includes("|") || isSeparatorRow(c.line)) return false; // outer pipes opsionale (§4 P-IV)
        const snap = c.snapshot();
        if (snap === undefined) return false;
        const tctx = tableContextAt(snap, c.lineNo);
        if (tctx === undefined || tctx.headerArity < 4) return false;
        const row = parseTableRow(c.line);
        if (row.malformed || row.cells.length !== tctx.headerArity) return false; // ariteti → HP-TABLE-ARITY
        return row.cells.some((cell) => cell === "");
      },
    },
    {
      ruleId: "HP-TABLE-MALFORMED", category: "structure", severity: "WARNING",
      test: (c) => {
        if (!c.line.includes("|") || isSeparatorRow(c.line)) return false;
        // P-V F3: bllok table-like me thellësi kontejneri të përzier — i paqartë,
        // s'kalon silent
        const snapEarly = c.snapshot();
        if (snapEarly !== undefined && mixedDepthTableRow(snapEarly, c.lineNo)) return true;
        if (!parseTableRow(c.line).malformed) return false;
        if (c.line.trimStart().startsWith("|")) return true; // synim eksplicit tabele
        // pa leading pipe: vetëm brenda konteksti tabele real — jo prozë me ` dhe |
        const snap = c.snapshot();
        return snap !== undefined && tableContextAt(snap, c.lineNo) !== undefined;
      },
    },
    {
      ruleId: "HP-TABLE-ARITY", category: "structure", severity: "REVIEW",
      test: (c) => {
        if (!c.line.includes("|") || isSeparatorRow(c.line)) return false; // outer pipes opsionale (§4 P-IV)
        const snap = c.snapshot();
        if (snap === undefined) return false;
        const tctx = tableContextAt(snap, c.lineNo);
        if (tctx === undefined || tctx.headerArity < 4) return false;
        const row = parseTableRow(c.line);
        if (row.malformed) return false;
        return row.cells.length !== tctx.headerArity; // përfshin kolonën finale që mungon
      },
    },
  ];
}
