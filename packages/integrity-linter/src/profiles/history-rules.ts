/**
 * HISTORY-PRIV — klasat canonical të kontrollit mbi ADDED-LINES (07_PRODATA
 * §Per-commit history scan). Kontroll POLICY-HISTORY class — JO semantika e
 * plotë domain e SRC-CLOSURE/SEF-ATK (ato profile mbeten NOT_IMPLEMENTED).
 *
 * Pattern-et sensitive ndërtohen RUNTIME nga fragmente sintetike.
 * Asnjë matched text nuk del kurrë nga këto rregulla — vetëm rule-id + locator.
 */

export interface ContentRule {
  ruleId: string;
  category: string;
  severity: "ERROR" | "WARNING" | "REVIEW";
  test(line: string): boolean;
}

function frag(...parts: string[]): string {
  return parts.join("");
}

/** Mohim/kualifikim që e kthen pretendimin në jo-pretendim. */
function negated(line: string): boolean {
  return /(\bnuk\b|\bs'|\bjo\b|\bpa\b|NUK|ndalohet|forbidden|never|WRONG|gabim)/i.test(line);
}

/** Etiketë historike në të njëjtin rresht. */
function historyLabeled(line: string): boolean {
  return /(HISTORICAL|SUPERSEDED|Historia\b|HISTORIK|superseded|ish |i atëhershëm|e atëhershme)/i.test(line);
}

const TREATMENT_TOKENS = new Set(["REQ", "MD", "OB", "REF", "NO"]);

export function buildContentRules(): ContentRule[] {
  const dash5 = frag("--", "---");
  const beginKey = new RegExp(frag(dash5, "BEGIN ", "(?:RSA |EC |OPENSSH )?", "PRIVATE", " KEY"));
  const ghToken = frag("g", "ho", "_[A-Za-z0-9_]{8,}");
  const ghPat = frag("github", "_", "pat", "_[A-Za-z0-9_]{8,}");
  const skToken = frag("\\bsk", "-[A-Za-z0-9_-]{20,}");
  const awsKey = frag("AK", "IA", "[0-9A-Z]{16}");
  const tokenClass = new RegExp(`${ghToken}|${ghPat}|${skToken}|${awsKey}`);
  const credWords = [frag("api", "[_-]?", "key"), frag("pass", "word"), frag("tok", "en"), frag("sec", "ret")].join("|");
  const credAssign = new RegExp(frag("(?:^|[^A-Za-z])(?:", credWords, ")\\s*[:=]\\s*['\"]?[A-Za-z0-9_/+.-]{6,}"), "i");
  const absPaths = new RegExp([frag("/Us", "ers/"), frag("C:\\\\", "Us", "ers"), frag("/ho", "me/[a-z]")].join("|"));

  return [
    // ── 2) identifiers / secrets ─────────────────────────────────────────
    { ruleId: "HP-PRIVATE-KEY-BLOCK", category: "secrets", severity: "ERROR", test: (l) => beginKey.test(l) },
    { ruleId: "HP-TOKEN-CLASS", category: "secrets", severity: "WARNING", test: (l) => tokenClass.test(l) },
    { ruleId: "HP-CREDENTIAL-ASSIGN", category: "secrets", severity: "WARNING", test: (l) => credAssign.test(l) },
    { ruleId: "HP-ABSOLUTE-LOCAL-PATH", category: "privacy", severity: "WARNING", test: (l) => absPaths.test(l) },
    { ruleId: "HP-REGULATED-ID-CLASS", category: "privacy", severity: "WARNING", test: (l) => /(^|\D)\d{12}(\D|$)/.test(l) },
    {
      ruleId: "HP-FINANCIAL-VALUE-CLASS", category: "privacy", severity: "REVIEW",
      test: (l) => /\b\d{2,3}[.,]\d{3}[.,]\d{2}\b/.test(l) && !/(sintetik|synthetic|€.*ligj|gjob[aë]|sanksion)/i.test(l),
    },

    // ── 3) evidence metrics (metrika nxjerrjeje/backup-i të evidence-it real) ─
    {
      ruleId: "HP-EVIDENCE-METRIC", category: "evidence", severity: "REVIEW",
      test: (l) =>
        /\b\d{1,3}(?:[,.]\d{3})+\s*(?:rreshta|rows|records|tabela|tables)\b/i.test(l) &&
        /(backup|extraction|nxjerr|eksport|dump|restore|MDF|LDF)/i.test(l) &&
        !historyLabeled(l),
    },

    // ── 4) operational-state claims (narrativa e operimit real të klientit) ──
    {
      ruleId: "HP-OPERATIONAL-CLAIM", category: "privacy", severity: "REVIEW",
      test: (l) =>
        /\b(xhiro(ja|n)?\s+(ditore|mujore|reale)|qarkullim(i)?\s+(ditor|mujor|real)|shitjet\s+(ditore|reale)\s+t[eë])\b/i.test(l) &&
        !negated(l) && !historyLabeled(l),
    },

    // ── 5) Treatment vocabulary i pavlefshëm (REQ/MD/OB/REF/NO + kombinime) ──
    {
      ruleId: "HP-TREATMENT-VOCAB", category: "vocabulary", severity: "WARNING",
      test: (l) => {
        if (!l.includes("|")) return false;
        for (const cellRaw of l.split("|")) {
          const cell = cellRaw.trim().replace(/[`*]/g, "");
          if (!/^[A-Z]{2,6}(\+[A-Z]{2,6})+$/.test(cell) && !/^[A-Z]{2,6}$/.test(cell)) continue;
          const tokens = cell.split("+");
          const known = tokens.filter((t) => TREATMENT_TOKENS.has(t));
          if (known.length > 0 && known.length !== tokens.length) return true; // p.sh. REQ+FOO
        }
        return false;
      },
    },

    // ── 6) PoC/simulator i promovuar në IMPLEMENTED/TESTED (policy-history class) ─
    (() => {
      // fjalët e maturity-t ndërtohen nga fragmente që ky burim të mos vetë-flagohet
      const impl = frag("IMPLE", "MENTED");
      const tstd = frag("TES", "TED");
      const prod = frag("PRODUCTION", "[- ]READY");
      const promo = new RegExp(`\\b(${impl}|${tstd}|${prod})\\b`);
      const noJo = new RegExp(`(JO(?:-|\\s)?(production|${tstd})|feasibility|vetëm mock|mock lokal|mock evidence)`, "i");
      return {
        ruleId: "HP-POC-PROMOTION", category: "maturity", severity: "WARNING",
        test: (l) =>
          /\b(PoC(?:-\d+)?|simulator|mock)\b/i.test(l) &&
          promo.test(l) && !negated(l) && !historyLabeled(l) && !noJo.test(l),
      } as ContentRule;
    })(),

    // ── 7) mixed maturity në të njëjtin rresht/qelizë ─────────────────────────
    (() => {
      const early = new RegExp(`\\b(${frag("MOD","ELED")}|${frag("SPECI","FIED")}|${frag("EVID","ENCE")})\\b`);
      const late = new RegExp(`\\b(${frag("TES","TED")}|${frag("IMPLE","MENTED")})\\b`);
      return {
        ruleId: "HP-MIXED-MATURITY", category: "maturity", severity: "REVIEW",
        test: (l) => early.test(l) && late.test(l) &&
          !negated(l) && !historyLabeled(l) && !/(→|SUPERSEDE|maturity stage|stage-i)/i.test(l),
      } as ContentRule;
    })(),

    // ── 8) qeliza mandatory bosh në tabela ────────────────────────────────────
    {
      ruleId: "HP-EMPTY-MANDATORY-CELL", category: "structure", severity: "REVIEW",
      test: (l) => {
        if (!l.trimStart().startsWith("|")) return false;
        const cells = l.split("|");
        if (cells.length < 5) return false;
        if (/^\|[\s\-:|]+\|?\s*$/.test(l.trim())) return false; // separator row
        return cells.slice(1, -1).some((c) => c.trim() === "");
      },
    },

    // ── 10) Human Gate order i gabuar në rresht zinxhiri të shtuar ────────────
    {
      ruleId: "HP-GATE-ORDER", category: "sequence", severity: "WARNING",
      test: (l) => {
        if (!/(→|\bpastaj\b)/.test(l)) return false;
        const stripped = l.replace(/\([^)]*\)/g, " ");
        const hg = stripped.search(/HUMAN GATE/i);
        const pf = stripped.search(/PREFLIGHT/i);
        return hg >= 0 && pf >= 0 && pf < hg && !negated(stripped) && !historyLabeled(l);
      },
    },
  ];
}

/**
 * 9) phase/version/status drift PER-COMMIT: added line që referon një version
 * Ledger/Register të ndryshëm nga versioni i AUTHORITY-t NË SNAPSHOT-in e po
 * atij commit-i (tracked Git object — kurrë filesystem).
 */
export interface VersionAuthority {
  label: "Ledger" | "Register";
  authorityFile: string;
  authorityPattern: RegExp;
  consumerPattern: RegExp;
}

export function versionAuthorities(): VersionAuthority[] {
  return [
    {
      label: "Ledger",
      authorityFile: "docs/canonical/05-sef-atk/ATK_SEF_Requirements_Ledger.md",
      authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/,
      consumerPattern: /Ledger(?:\.md)?`?\*{0,2}\s*(?:—|:)?\s*v(\d+\.\d+)/,
    },
    {
      label: "Register",
      authorityFile: "docs/canonical/05-sef-atk/ATK_Source_Register.md",
      authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/,
      consumerPattern: /Register(?:\.md)?`?\*{0,2}\s*(?:—|:)?\s*v(\d+\.\d+)/,
    },
  ];
}

export { historyLabeled as ruleHistoryLabeled };
