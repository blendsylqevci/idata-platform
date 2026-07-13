/**
 * AUTHORITY CLASSES — kontrata bidirectional `authority → consumers` (§1, Hardening II).
 *
 * Metadata eksplicite, JO regex ad-hoc:
 *  - çdo rregull deklaron sipërfaqen autoritative, ekstraktimin e vlerës aktuale,
 *    konsumatorët dhe për secilin: REQUIRED_CURRENT ose SCAN_IF_PRESENT;
 *  - `kind` ∈ version | count | status | set — normalizimi për statuset;
 *  - `lineExcludes` = guard kontekstual i dokumentuar (p.sh. rreshtat CONTRADICTS
 *    kanë count-in e VET autoritativ dhe s'krahasohen me totalin);
 *  - authority missing / unreadable / AMBIGUOUS (dy vlera të ndryshme) → exit 2.
 *
 * Etiketa historike vlen VETËM same-row (row-scoped) — kurrë për gjithë dokumentin.
 */

export type ConsumerRequirement = "REQUIRED_CURRENT" | "SCAN_IF_PRESENT";
export type AuthorityKind = "version" | "count" | "status" | "set";

export interface ConsumerSpec {
  file: string;
  /** Grupi 1 = vlera e parë (normalizohet për kind=status). */
  pattern: RegExp;
  requirement: ConsumerRequirement;
  /**
   * Guard kontekstual i dokumentuar: match-i përjashtohet VETËM kur dritarja e
   * 24 karaktereve PARA tij përputhet (p.sh. "CONTRADICTS: 5 source-rows" ka
   * authority-n e vet) — jo krejt rreshti, që totali në të njëjtin rresht të mos humbasë.
   */
  excludeBefore?: RegExp;
}

export interface AuthorityClass {
  id: string;
  kind: AuthorityKind;
  authorityFile: string;
  /** Grupi 1 = vlera autoritative; të gjitha match-et duhet të japin të NJËJTËN vlerë (ndryshe AMBIGUOUS). */
  authorityPattern: RegExp;
  /** Normalizues i përbashkët për authority + consumers (statuset). */
  normalize?: (raw: string) => string;
  consumers: ConsumerSpec[];
}

export const LEDGER = "docs/canonical/05-sef-atk/ATK_SEF_Requirements_Ledger.md";
export const REGISTER = "docs/canonical/05-sef-atk/ATK_Source_Register.md";
export const GATE = "docs/canonical/05-sef-atk/ATK_Compliance_Gate.md";
export const SSOT = "docs/canonical/00-ssot/iData_Plus_SSOT.md";
export const TRACKER = "docs/canonical/00-ssot/Outstanding_Confirmations_Tracker_2026-06-29.md";
export const INDEX = "docs/INDEX.md";
export const ROADMAP = "docs/canonical/08-roadmap/Project_Delivery_Roadmap_Phases_1_10.md";
export const PM = "docs/PM_REVIEW_PROTOCOL.md";
export const MATRIX07 = "docs/canonical/01-phase0/07_PRODATA_Evidence_Closure_Parity_Matrix.md";
export const ANALIZA = "poc/poc5-sef/ANALIZA.md";
export const POC5README = "poc/poc5-sef/README.md";
export const FINDINGS = "docs/canonical/05-sef-atk/ATK_SEF_Offline_Faturimi_Findings.md";

/** Formatet reale të repo-s: backticks, bold, em/en dash, `·`, `:` — p.sh. "`Ledger.md` — v1.6:". */
const LEDGER_REF = /(?:Requirements_)?Ledger(?:\.md)?`?\*{0,2}\s*(?:[—–:·])?\s*\*{0,2}v(\d+\.\d+)/g;
const REGISTER_REF = /(?:Source_)?Register(?:\.md)?`?\*{0,2}\s*(?:[—–:·])?\s*\*{0,2}v(\d+\.\d+)/g;

/** Statuset ATK-011 të normalizuara në token të krahasueshëm. */
function normalizeAtk011(raw: string): string {
  const s = raw.toUpperCase();
  if (s.includes("OPEN") && s.includes("PENDING")) return "OPEN/PENDING_INDEPENDENT_REVIEW";
  if (s.includes("CLOSES") || s.includes("MBYLLET")) return "CLOSES_ON_MERGE";
  if (s.includes("CLOSED") || s.includes("COMPLETED")) return "CLOSED";
  return s.trim();
}

function normalizeTier2(raw: string): string {
  const s = raw.toUpperCase();
  return s.includes("NO-GO") ? "NO-GO" : "GO";
}

function normalizeGov001(raw: string): string {
  const s = raw.toUpperCase();
  if (s.includes("ADVISORY_ACTIVE")) return "PARTIAL/ADVISORY_ACTIVE";
  if (s.includes("NOT_IMPLEMENTED")) return "APPROVED/NOT_IMPLEMENTED";
  if (s.includes("COMPLETE")) return "COMPLETE";
  return s.trim();
}

function normalizeHgRequired(raw: string): string {
  const s = raw.toLowerCase();
  if (/(paaprovuar|pending|gated|mbetet)/.test(s)) return "PENDING";
  if (/approved|aprovuar/.test(s)) return "APPROVED";
  return s.trim().toUpperCase();
}

export function defaultAuthorityClasses(): AuthorityClass[] {
  const ledgerCore: Array<[string, ConsumerRequirement]> = [
    [GATE, "REQUIRED_CURRENT"], [REGISTER, "REQUIRED_CURRENT"], [SSOT, "REQUIRED_CURRENT"],
    [INDEX, "REQUIRED_CURRENT"], [TRACKER, "SCAN_IF_PRESENT"], [ROADMAP, "SCAN_IF_PRESENT"],
    [ANALIZA, "SCAN_IF_PRESENT"], [FINDINGS, "SCAN_IF_PRESENT"],
  ];
  return [
    {
      id: "ledger-version", kind: "version", authorityFile: LEDGER,
      authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/g,
      consumers: ledgerCore.map(([file, requirement]) => ({ file, requirement, pattern: LEDGER_REF })),
    },
    {
      id: "register-version", kind: "version", authorityFile: REGISTER,
      authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/g,
      consumers: ([[GATE, "REQUIRED_CURRENT"], [SSOT, "REQUIRED_CURRENT"], [INDEX, "REQUIRED_CURRENT"],
        [LEDGER, "SCAN_IF_PRESENT"], [ROADMAP, "SCAN_IF_PRESENT"], [ANALIZA, "SCAN_IF_PRESENT"],
        [FINDINGS, "SCAN_IF_PRESENT"]] as Array<[string, ConsumerRequirement]>)
        .map(([file, requirement]) => ({ file, requirement, pattern: REGISTER_REF })),
    },
    {
      id: "matrix07-version", kind: "version", authorityFile: MATRIX07,
      authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/g,
      consumers: [{
        file: TRACKER, requirement: "REQUIRED_CURRENT",
        pattern: /07_PRODATA_Evidence_Closure_Parity_Matrix\.md`?\*{0,2}\s*(?:[—–:·])?\s*\*{0,2}v(\d+\.\d+)/g,
      }],
    },
    {
      // Totali i source-rows. Guard i dokumentuar: rreshtat e setit CONTRADICTS
      // kanë count-in e vet autoritativ (klasa "contradicts-rows") — s'janë totali.
      id: "ledger-source-rows", kind: "count", authorityFile: LEDGER,
      authorityPattern: /Source-requirement rows: (\d+)\*\*/g,
      consumers: ([[GATE, "REQUIRED_CURRENT"], [SSOT, "REQUIRED_CURRENT"], [INDEX, "REQUIRED_CURRENT"],
        [REGISTER, "SCAN_IF_PRESENT"], [ROADMAP, "SCAN_IF_PRESENT"], [ANALIZA, "SCAN_IF_PRESENT"],
        [FINDINGS, "SCAN_IF_PRESENT"], [PM, "SCAN_IF_PRESENT"]] as Array<[string, ConsumerRequirement]>)
        .map(([file, requirement]) => ({
          file, requirement, pattern: /(\d+) source-rows/g, excludeBefore: /CONTRADICTS[^0-9]{0,12}$/,
        })),
    },
    {
      id: "contradicts-rows", kind: "count", authorityFile: LEDGER,
      authorityPattern: /CONTRADICTS: (\d+) source-rows/g,
      consumers: [GATE, SSOT, POC5README, ANALIZA, TRACKER].map((file) => ({
        file, requirement: "SCAN_IF_PRESENT" as const, pattern: /(\d+) CONTRADICTS-rreshta/g,
      })),
    },
    {
      id: "ledger-closure-keys", kind: "count", authorityFile: LEDGER,
      authorityPattern: /closure\/product keys: (\d+)\*\*/g,
      consumers: ([[GATE, "REQUIRED_CURRENT"], [SSOT, "REQUIRED_CURRENT"], [INDEX, "REQUIRED_CURRENT"],
        [REGISTER, "SCAN_IF_PRESENT"], [ANALIZA, "SCAN_IF_PRESENT"], [FINDINGS, "SCAN_IF_PRESENT"],
        [ROADMAP, "SCAN_IF_PRESENT"]] as Array<[string, ConsumerRequirement]>)
        .map(([file, requirement]) => ({ file, requirement, pattern: /(\d+) closure keys/g })),
    },
    {
      id: "ledger-confirmations", kind: "count", authorityFile: LEDGER,
      authorityPattern: /\*\*(\d+) kontrolle\*\* me `CONFIRM/g,
      consumers: [
        ...([[GATE, "REQUIRED_CURRENT"], [SSOT, "REQUIRED_CURRENT"], [REGISTER, "SCAN_IF_PRESENT"],
          [ANALIZA, "SCAN_IF_PRESENT"], [FINDINGS, "SCAN_IF_PRESENT"], [INDEX, "SCAN_IF_PRESENT"],
          [ROADMAP, "SCAN_IF_PRESENT"]] as Array<[string, ConsumerRequirement]>)
          .map(([file, requirement]) => ({ file, requirement, pattern: /(\d+) konfirmime/g })),
        { file: SSOT, requirement: "SCAN_IF_PRESENT" as const, pattern: /(\d+) CONFIRM_/g },
      ],
    },
    {
      id: "atk-011-status", kind: "status", authorityFile: TRACKER,
      authorityPattern: /ATK-011[^\n]*?statusi: (OPEN \/ PENDING_INDEPENDENT_REVIEW|CLOSED|COMPLETED)/g,
      normalize: normalizeAtk011,
      consumers: ([[GATE, "REQUIRED_CURRENT"], [SSOT, "REQUIRED_CURRENT"], [INDEX, "REQUIRED_CURRENT"],
        [ROADMAP, "SCAN_IF_PRESENT"]] as Array<[string, ConsumerRequirement]>)
        .map(([file, requirement]) => ({
          file, requirement,
          pattern: /ATK-011`?\*{0,2}[^A-Za-z0-9\n]{0,8}\*{0,2}(OPEN ?\/ ?PENDING_INDEPENDENT_REVIEW|closes[ -]on[ -]merge|CLOSED|COMPLETED)/gi,
        })),
    },
    {
      id: "tier2-status", kind: "status", authorityFile: GATE,
      authorityPattern: /Tier 2\*{0,2} ?=? ?\*{0,2}(NOT IMPLEMENTED ?\/ ?NO-GO)/g,
      normalize: normalizeTier2,
      consumers: ([[SSOT, "REQUIRED_CURRENT"], [INDEX, "REQUIRED_CURRENT"], [ROADMAP, "SCAN_IF_PRESENT"],
        [POC5README, "SCAN_IF_PRESENT"], [PM, "SCAN_IF_PRESENT"], [TRACKER, "SCAN_IF_PRESENT"]] as Array<[string, ConsumerRequirement]>)
        .map(([file, requirement]) => ({
          file, requirement,
          pattern: /Tier 2\*{0,2} ?[=:]? ?\*{0,2}(NOT IMPLEMENTED ?\/ ?NO-GO|NO-GO|(?<!NO-)GO\b)/g,
          excludeBefore: /(pjesa tjetër e|para|gjatë|blockers?|checklist[^|]{0,20})$/i,
        })),
    },
    (() => {
      // Alternativat e statusit ndërtohen nga fragmente që VETË ky burim të mos
      // vetë-flagohet nga konsumatori i klasës (drift mbi added-lines të kodit).
      const stPartial = ["PARTIAL ?\\/ ?ADVISORY", "_ACTIVE"].join("");
      const stNotImpl = ["APPROVED ?\\/ ?NOT", "_IMPLEMENTED"].join("");
      const stComplete = ["COMPLE", "TE\\b"].join("");
      const alt = `(${stPartial}|${stNotImpl}|${stComplete})`;
      return {
        id: "gov-001-status", kind: "status" as const, authorityFile: TRACKER,
        authorityPattern: new RegExp("`GOV-001`[^\\n]*?`" + alt + "`", "g"),
        normalize: normalizeGov001,
        consumers: [SSOT, INDEX, ROADMAP, PM, MATRIX07].map((file) => ({
          file, requirement: "SCAN_IF_PRESENT" as const,
          pattern: new RegExp("GOV-001[^\\n]{0,120}?" + alt, "g"),
        })),
      };
    })(),
    {
      // set/profile registry: "14 profilet e tjera = NOT_IMPLEMENTED" (authority = 07 §Linter)
      id: "profile-registry-set", kind: "set", authorityFile: MATRIX07,
      authorityPattern: /(\d+) profilet e tjera = `?NOT_IMPLEMENTED`?/g,
      consumers: [TRACKER, SSOT, INDEX, ROADMAP, PM].map((file) => ({
        file, requirement: "SCAN_IF_PRESENT" as const,
        pattern: /(\d+) profil(?:e|et)?(?: e tjera)? ?=? ?(?:mbeten )?`?NOT_IMPLEMENTED/g,
      })),
    },
    {
      id: "hg-linter-required-status", kind: "status", authorityFile: MATRIX07,
      authorityPattern: /HG-LINTER-REQUIRED[`*,]{0,3} ?(?:mbetet |është )?(i paaprovuar|pending|gated|APPROVED)/gi,
      normalize: normalizeHgRequired,
      consumers: [TRACKER, SSOT, INDEX, ROADMAP, PM, GATE].map((file) => ({
        file, requirement: "SCAN_IF_PRESENT" as const,
        pattern: /HG-LINTER-REQUIRED[`*,)]{0,3} ?(?:mbetet |është )?(i paaprovuar|pending|gated|mbetet gated|APPROVED)/gi,
      })),
    },
  ];
}

/** Etiketë historike ROW-SCOPED — vlen vetëm në rreshtin ku gjendet vlera. */
export function isHistoryLabeledRow(line: string): boolean {
  // Etiketa duhet të jetë EKSPLICITE dhe row-scoped: HISTORICAL/SUPERSEDED/HISTORIK
  // (uppercase si etiketa), "Historia:", "superseded", "ish ", "i/e atëhershëm/e".
  // Mbiemri i thjeshtë "historike" (p.sh. "evidence historike mock") NUK është etiketë.
  return /(HISTORICAL|SUPERSEDED|Historia\b|\bHISTORIK(E|U)?\b|superseded|\bish\b|atëhersh|numërator(i)? historik|vs \d)/.test(line);
}
