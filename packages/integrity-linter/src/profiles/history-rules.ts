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

import { parseTableRow, tableContextAt, isSeparatorRow } from "../markdown-table.js";

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
 * Mohim LOKAL (§6C): tokeni mohues duhet të qëndrojë në dritaren 24-karaktere
 * MENJËHERË para claim-it — "…, jo 0" në fund të rreshtit nuk e anulon claim-in.
 */
export function locallyNegated(line: string, matchIndex: number): boolean {
  const win = line.slice(Math.max(0, matchIndex - 24), matchIndex);
  return /(\bnuk\b|\bs'|\bmos\b|ndalohet|NUK)\s*\S*\s*$/i.test(win);
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
  const absPaths = new RegExp([frag("/Us", "ers/"), frag("C:\\\\", "Us", "ers"), frag("/ho", "me/[a-z]")].join("|"));
  const idLabels = [
    frag("N", "UI", "\\w{0,2}"), frag("N", "IF"), frag("TV", "SH"), frag("V", "AT"),
    frag("Application", "[ _-]?ID"), frag("Fiscal", "\\w*", "[ _-]?(?:No|Nr|ID)"),
    frag("transaction", "[ _-]?id"), frag("NUI", "KF"),
  ].join("|");
  const regulatedIdContext = new RegExp(frag("\\b(?:", idLabels, ")\\b", "[^0-9\\n]{0,12}", "\\d{7,}"), "i");
  const implWord = frag("IMPLE", "MENTED");
  const testWord = frag("TES", "TED");
  const prodReady = frag("PRODUCTION", "[- ]READY");
  const promo = new RegExp(`\\b(${implWord}|${testWord}|${prodReady})\\b`);
  const provenPoc = new RegExp(frag("PROVEN", "[- ]PoC"), "i");
  // strukturë mohimi SPECIFIKE (jo fjala e lirë "jo"): JO-TESTED / JO-production
  const structuredJo = new RegExp(`JO(?:-|\\s)(production|${testWord})|vetëm mock|mock lokal|mock evidence`, "i");
  const maturityAlt = MATURITY_STAGES.map((st) => st.replace("-", "[- ]")).join("|");
  const operationalClaim = new RegExp(
    "(xhiro(ja|n)?\\s+(ditore|mujore|reale)|qarkullim(i)?\\s+(ditor|mujor|real)\\b|shitjet\\s+(ditore|reale)|barazim(i|et)?\\s+(ditor|real)\\w*|saldo\\s+reale?\\b|gjendja\\s+(reale|aktuale)\\s+e\\s+(arkës|stokut|barazimit)|operon\\s+(aktualisht|çdo ditë|realisht)|klient[ëe]?\\s+aktiv|fatura(t)?\\s+reale\\s+t[ëe]\\s+(muajit|ditës)|arka\\s+(bosh|aktive|e mbyllur)|legacy\\s+\\w+\\s+(count|numër))",
    "i",
  );

  return [
    // ── 2) identifiers / secrets — ASNJË exemption (as historike, as sintetike) ─
    { ruleId: "HP-PRIVATE-KEY-BLOCK", category: "secrets", severity: "ERROR", test: (c) => beginKey.test(c.line) },
    { ruleId: "HP-TOKEN-CLASS", category: "secrets", severity: "WARNING", test: (c) => tokenClass.test(c.line) },
    { ruleId: "HP-CREDENTIAL-ASSIGN", category: "secrets", severity: "WARNING", test: (c) => credAssign.test(c.line) },
    { ruleId: "HP-ABSOLUTE-LOCAL-PATH", category: "privacy", severity: "WARNING", test: (c) => absPaths.test(c.line) },
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

    // ── 4) operational-state claims — mohim LOKAL; pa blanket exemptions (§6C) ──
    {
      ruleId: "HP-OPERATIONAL-CLAIM", category: "privacy", severity: "REVIEW",
      test: (c) => {
        const m = operationalClaim.exec(c.line);
        if (m === null) return false;
        return !locallyNegated(c.line, m.index);
      },
    },

    // ── 5) Treatment — kolonë e parsuar me GFM parser (§7) ──────────────────
    {
      ruleId: "HP-TREATMENT-VOCAB", category: "vocabulary", severity: "WARNING",
      test: (c) => {
        if (!c.line.includes("|") || isSeparatorRow(c.line)) return false;
        const snap = c.snapshot();
        if (snap !== undefined && c.line.trimStart().startsWith("|")) {
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

    // ── 6) PoC promotion — mohim LOKAL para stage-tokenit (§6C) ─────────────
    {
      ruleId: "HP-POC-PROMOTION", category: "maturity", severity: "WARNING",
      test: (c) => {
        if (!/\b(PoC(?:-\d+)?|simulator|mock|feasibility[- ]only)\b/i.test(c.line)) return false;
        const cleaned = c.line.replace(provenPoc, " ");
        const m = promo.exec(cleaned);
        if (m === null) return false;
        if (structuredJo.test(cleaned)) return false; // JO-TESTED / JO-production etj.
        return !locallyNegated(cleaned, m.index);
      },
    },

    // ── 7) mixed maturity — kolonë Maturity me GFM parser (§7) ──────────────
    {
      ruleId: "HP-MIXED-MATURITY", category: "maturity", severity: "REVIEW",
      test: (c) => {
        if (!c.line.trimStart().startsWith("|") || isSeparatorRow(c.line)) return false;
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
        if (!c.line.trimStart().startsWith("|") || isSeparatorRow(c.line)) return false;
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
        if (!c.line.trimStart().startsWith("|") || isSeparatorRow(c.line)) return false;
        return parseTableRow(c.line).malformed;
      },
    },
    {
      ruleId: "HP-TABLE-ARITY", category: "structure", severity: "REVIEW",
      test: (c) => {
        if (!c.line.trimStart().startsWith("|") || isSeparatorRow(c.line)) return false;
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
