/**
 * HISTORY-PRIV — klasat canonical mbi ADDED-LINES (07_PRODATA §Per-commit history
 * scan + §Acceptance; Hardening II §2). Kontroll POLICY-HISTORY class — JO
 * semantika e plotë domain e SRC-CLOSURE/SEF-ATK (ato mbeten NOT_IMPLEMENTED).
 *
 *  - Detektorët kontekstualë (jo grep i verbër): identifikues me ETIKETË,
 *    metrika me KONTEKST evidence, tabela me KOLONA (Treatment/Maturity) të
 *    lexuara nga snapshot-i tracked i të njëjtit commit;
 *  - "pa" NUK është mohim universal (§2C) — vetëm mohime të sakta;
 *  - asnjë matched value nuk publikohet — vetëm rule-id + safe locator;
 *  - pattern-et sensitive ndërtohen RUNTIME nga fragmente sintetike.
 */

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

/** Mohim i SAKTË (§2C: "pa" NUK pranohet si mohim universal). */
function negated(line: string): boolean {
  return /(\bnuk\b|\bs'|\bjo\b|NUK|ndalohet|forbidden|never|WRONG|gabim)/i.test(line);
}

function historyLabeled(line: string): boolean {
  return /(HISTORICAL|SUPERSEDED|Historia\b|HISTORIK|superseded|\bish\b|i atëhershëm|e atëhershme)/i.test(line);
}

function syntheticLabeled(line: string): boolean {
  return /(sintetik|synthetic|fixture)/i.test(line);
}

/** Tag-set-i i Treatment sipas canonical 07_PRODATA §1A (REQ/MD/OB/REF/NO). */
export const TREATMENT_TOKENS = new Set(["REQ", "MD", "OB", "REF", "NO"]);

/** Stage-t e Maturity sipas canonical 07_PRODATA §1B (+ qualifier PROVEN-PoC §1C). */
export const MATURITY_STAGES = ["GAP", "EVIDENCE", "SPECIFIED", "MODELED", "IMPLEMENTED", "TESTED", "SIGNED-OFF"] as const;

/**
 * Gjen kolonën e një header-i tabele që mbulon rreshtin `lineNo` në snapshot:
 * kërkon lart header-in më të afërt ('| ... |' i ndjekur nga separator '---').
 */
export function tableColumnIndex(snapshot: string, lineNo: number, headerNameRe: RegExp): number | undefined {
  const lines = snapshot.split("\n");
  const idx = lineNo - 1;
  const target = lines[idx];
  if (target === undefined || !target.trimStart().startsWith("|")) return undefined;
  for (let i = idx - 1; i >= 0; i--) {
    const l = lines[i] ?? "";
    if (!l.trimStart().startsWith("|")) return undefined; // dolëm nga tabela
    const next = lines[i + 1] ?? "";
    if (/^\s*\|[\s\-:|]+\|?\s*$/.test(next)) {
      const cells = l.split("|").map((c) => c.trim());
      for (let c = 0; c < cells.length; c++) {
        if (headerNameRe.test(cells[c] ?? "")) return c;
      }
      return undefined;
    }
  }
  return undefined;
}

export function cellAt(line: string, colIndex: number): string | undefined {
  const cells = line.split("|");
  const cell = cells[colIndex];
  return cell?.trim();
}

/** A ka tabela që e mbulon rreshtin një header të vërtetë me ≥4 kolona? */
export function coveredByRealTable(snapshot: string, lineNo: number): boolean {
  const lines = snapshot.split("\n");
  const idx = lineNo - 1;
  const target = lines[idx];
  if (target === undefined || !target.trimStart().startsWith("|")) return false;
  for (let i = idx - 1; i >= 0; i--) {
    const l = lines[i] ?? "";
    if (!l.trimStart().startsWith("|")) return false;
    const next = lines[i + 1] ?? "";
    if (/^\s*\|[\s\-:|]+\|?\s*$/.test(next)) {
      return l.split("|").length >= 6; // header + ≥4 kolona reale
    }
  }
  return false;
}

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
  const credAssign = new RegExp(frag("(?:^|[^A-Za-z])(?:", credWords, ")\\s*[:=]\\s*['\"]?[A-Za-z0-9_/+.-]{6,}"), "i");
  const absPaths = new RegExp([frag("/Us", "ers/"), frag("C:\\\\", "Us", "ers"), frag("/ho", "me/[a-z]")].join("|"));
  // §2A: identifikues të RREGULLUAR me kontekst etikete (jo vetëm \d{12}) —
  // etiketat ndërtohen nga fragmente që ky burim të mos vetë-flagohet.
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
  const noJo = new RegExp(`(JO(?:-|\\s)?(production|${testWord})|vetëm mock|mock lokal|mock evidence)`, "i");
  const maturityAlt = MATURITY_STAGES.map((s) => s.replace("-", "[- ]")).join("|");

  return [
    // ── 2) identifiers / secrets (historical-allowed NUK aplikohet) ─────────
    { ruleId: "HP-PRIVATE-KEY-BLOCK", category: "secrets", severity: "ERROR", test: (c) => beginKey.test(c.line) },
    { ruleId: "HP-TOKEN-CLASS", category: "secrets", severity: "WARNING", test: (c) => tokenClass.test(c.line) },
    { ruleId: "HP-CREDENTIAL-ASSIGN", category: "secrets", severity: "WARNING", test: (c) => credAssign.test(c.line) },
    { ruleId: "HP-ABSOLUTE-LOCAL-PATH", category: "privacy", severity: "WARNING", test: (c) => absPaths.test(c.line) },
    { ruleId: "HP-REGULATED-ID-CLASS", category: "privacy", severity: "WARNING", test: (c) => /(^|\D)\d{12}(\D|$)/.test(c.line) },
    {
      ruleId: "HP-REGULATED-ID-CONTEXT", category: "privacy", severity: "WARNING",
      test: (c) => regulatedIdContext.test(c.line),
    },
    {
      ruleId: "HP-FINANCIAL-VALUE-CLASS", category: "privacy", severity: "REVIEW",
      test: (c) => /\b\d{2,3}[.,]\d{3}[.,]\d{2}\b/.test(c.line) && !/(sintetik|synthetic|€.*ligj|gjob[aë]|sanksion)/i.test(c.line),
    },

    // ── 3) evidence metrics — edhe PA thousands separator (§2B) ─────────────
    {
      ruleId: "HP-EVIDENCE-METRIC", category: "evidence", severity: "REVIEW",
      test: (c) =>
        /\b(?:\d{1,3}(?:[,.]\d{3})+|\d{3,})\s*(?:rreshta|rows|records|tabela|tables)\b/i.test(c.line) &&
        /(backup|extraction|nxjerr|eksport|dump|restore|MDF|LDF|evidence)/i.test(c.line) &&
        !historyLabeled(c.line) && !syntheticLabeled(c.line),
    },

    // ── 4) operational-state claims — klasat reale të politikës (§2C) ───────
    {
      ruleId: "HP-OPERATIONAL-CLAIM", category: "privacy", severity: "REVIEW",
      test: (c) =>
        /(xhiro(ja|n)?\s+(ditore|mujore|reale)|qarkullim(i)?\s+(ditor|mujor|real)\b|shitjet\s+(ditore|reale)|barazim(i|et)?\s+(ditor|real)\w*|saldo\s+reale?\b|gjendja\s+(reale|aktuale)\s+e\s+(arkës|stokut|barazimit)|operon\s+(aktualisht|çdo ditë|realisht)|klient[ëe]?\s+aktiv|fatura(t)?\s+reale\s+t[ëe]\s+(muajit|ditës)|arka\s+(bosh|aktive|e mbyllur)|legacy\s+\w+\s+(count|numër))/i.test(c.line) &&
        !negated(c.line) && !historyLabeled(c.line) && !syntheticLabeled(c.line),
    },

    // ── 5) Treatment vocabulary — TABLE/COLUMN-AWARE (§2D) ──────────────────
    {
      ruleId: "HP-TREATMENT-VOCAB", category: "vocabulary", severity: "WARNING",
      test: (c) => {
        if (!c.line.includes("|")) return false;
        if (/^\s*\|[\s\-:|]+\|?\s*$/.test(c.line)) return false; // separator row
        const snap = c.snapshot();
        if (snap !== undefined) {
          const col = tableColumnIndex(snap, c.lineNo, /Treatment/i);
          if (col !== undefined) {
            const cell = cellAt(c.line, col)?.replace(/[`*]/g, "").trim();
            if (cell === undefined || cell === "" || cell === "—" || cell === "-") return false;
            // kolona Treatment: ÇDO token duhet të jetë në tag-set-in canonical —
            // edhe qeliza unknown-only (FOO/LATER/DECISION_REQUIRED) flagohet.
            const tokens = cell.split("+").map((t) => t.trim());
            return tokens.some((t) => !TREATMENT_TOKENS.has(t));
          }
        }
        // fallback jashtë kolonës Treatment: vetëm kombinim i përzier known+unknown
        // (fjalët LATER/DECISION_REQUIRED jashtë kolonës NUK prodhojnë FP)
        for (const cellRaw of c.line.split("|")) {
          const cell = cellRaw.trim().replace(/[`*]/g, "");
          if (!/^[A-Z_]{2,20}(\+[A-Z_]{2,20})+$/.test(cell)) continue;
          const tokens = cell.split("+");
          const known = tokens.filter((t) => TREATMENT_TOKENS.has(t));
          if (known.length > 0 && known.length !== tokens.length) return true;
        }
        return false;
      },
    },

    // ── 6) PoC/simulator → promovim production (§2F) ────────────────────────
    {
      ruleId: "HP-POC-PROMOTION", category: "maturity", severity: "WARNING",
      test: (c) =>
        /\b(PoC(?:-\d+)?|simulator|mock|feasibility[- ]only)\b/i.test(c.line) &&
        promo.test(c.line.replace(provenPoc, " ")) &&
        !negated(c.line) && !historyLabeled(c.line) && !noJo.test(c.line),
    },

    // ── 7) mixed maturity — MATURITY-COLUMN-AWARE (§2E) ─────────────────────
    {
      ruleId: "HP-MIXED-MATURITY", category: "maturity", severity: "REVIEW",
      test: (c) => {
        if (!c.line.includes("|")) return false;
        const snap = c.snapshot();
        if (snap === undefined) return false;
        const col = tableColumnIndex(snap, c.lineNo, /Maturity/i);
        if (col === undefined) return false; // narrativa jashtë maturity cell → clean
        const cell = cellAt(c.line, col)?.replace(/[`*]/g, "");
        if (cell === undefined || /→/.test(cell) || historyLabeled(c.line)) return false;
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

    // ── 8) qeliza mandatory bosh — vetëm tabela reale me header ≥4 kolona (§2G) ─
    {
      ruleId: "HP-EMPTY-MANDATORY-CELL", category: "structure", severity: "REVIEW",
      test: (c) => {
        if (!c.line.trimStart().startsWith("|")) return false;
        if (/^\s*\|[\s\-:|]+\|?\s*$/.test(c.line)) return false; // separator
        const snap = c.snapshot();
        if (snap === undefined || !coveredByRealTable(snap, c.lineNo)) return false;
        const cells = c.line.split("|");
        if (cells.length < 6) return false;
        return cells.slice(1, -1).some((cell) => cell.trim() === "");
      },
    },
  ];
}

export { historyLabeled as ruleHistoryLabeled, negated as ruleNegated };
