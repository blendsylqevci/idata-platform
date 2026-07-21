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

import { countMatches, detectionSkeleton } from "./machine-token.js";
import { isTableRowCandidate, parseTableRow, tableContextAt } from "./markdown-table.js";

export type ConsumerRequirement = "REQUIRED_CURRENT" | "SCAN_IF_PRESENT";
export type AuthorityKind = "version" | "count" | "status" | "set" | "phase";

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
  /**
   * Activation boundary per-commit (§1.2; ngurtësuar në Hardening IV §2):
   *  - "ONCE_PRESENT" (default): klasa aktivizohet sapo authority FILE EKZISTON
   *    në snapshot — përfshirë base snapshot-in (aktive nga commit-i i parë i
   *    skanuar). PARA aktivizimit kontrata lejon VETËM mungesën e file-it;
   *    PAS aktivizimit bosh / zero match / vlerë ambige / unreadable / file i
   *    hequr = ScanError me kod fiks (exit 2) — kurrë ONCE_VALID, aktivizimi
   *    s'fsheh shfaqjen e parë të prishur dhe s'printon vlerën problematike.
   */
  activation?: "ONCE_PRESENT";
  /**
   * §4A (P-IV) — COMPATIBILITY MECHANISM me pranim të kushtëzuar: vetëm për
   * authority surface që NUK ekzistonte semantikisht para futjes së seksionit,
   * edhe pse file-i prind ekzistonte. KUFIJ TË DETYRUESHËM:
   *  - default-i mbetet FILE PRESENCE; anchor = opt-in i RRALLË;
   *  - në PR #21 lejohet VETËM për `profile-registry-set` dhe
   *    `hg-linter-required-status` (zgjerim te klasa të tjera kërkon
   *    review/Human Gate);
   *  - anchor-i duhet të jetë structural label UNIK dhe path-bound — KURRË
   *    vetë statusi/versioni/vlera e authority-t;
   *  - para anchor-it klasa është NOT_APPLICABLE; në commit-in e lindjes
   *    bosh/zero-match/ambige → exit 2 PO NË ATË COMMIT;
   *  - pas aktivizimit sticky: anchor i hequr → ANCHOR_MISSING; anchor i
   *    duplikuar → ANCHOR_DUPLICATE; authority e prishur/hequr/ambige ose
   *    file unreadable → exit 2;
   *  - heqja e ardhshme e anchor-it kërkon PR + review më vete (pa premtim
   *    heqjeje automatike pas merge-it).
   */
  activationAnchor?: RegExp;
  consumers: ConsumerSpec[];
}

/**
 * VALIDATOR I PËRBASHKËT PURE (§1) — i njëjti për DOC-CORE dhe HISTORY-PRIV:
 * nxjerr TË GJITHA match-et, normalizon, zero vlera → AUTHORITY_UNREADABLE,
 * >1 vlerë të NDRYSHME → AUTHORITY_AMBIGUOUS; match-e të përsëritura me të
 * njëjtën vlerë të normalizuar lejohen (i njëjti authority).
 * Vlera kthehet vetëm për krahasim të brendshëm — KURRË në output.
 */
export function extractAuthorityValue(text: string, cls: AuthorityClass): string {
  const norm = cls.normalize ?? ((v: string) => v);
  const re = new RegExp(
    cls.authorityPattern.source,
    cls.authorityPattern.flags.includes("g") ? cls.authorityPattern.flags : cls.authorityPattern.flags + "g",
  );
  const values = new Set<string>();
  let rawCount = 0;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    rawCount += 1;
    if (m[1] !== undefined) values.add(norm(m[1]));
  }
  // P-V F1/F2 — authority FAIL-CLOSED me kod specifik: token i obfuskuar
  // (zero-width / homoglyph / format-char) zbulohet me projeksionin e
  // detektimit; RAW mbetet burimi — projeksioni s'e pranon kurrë vlerën.
  const skCount = countMatches(detectionSkeleton(text), cls.authorityPattern);
  if (skCount > rawCount) throw new AuthorityError("INCOMPLETE_SCAN:AUTHORITY_OBFUSCATED");
  if (values.size === 0) throw new AuthorityError("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");
  if (values.size > 1) throw new AuthorityError("INCOMPLETE_SCAN:AUTHORITY_AMBIGUOUS");
  return [...values][0] as string;
}

/** Gabim autoriteti me kod fiks — ri-hidhet si ScanError nga thirrësit. */
export class AuthorityError extends Error {
  override readonly name = "AuthorityError";
  constructor(public readonly code: string) {
    super(code);
  }
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
      // Asertimi i statusit LINDI me v2.3 (§Linter status block) — jo me file-in v2.2;
      // anchor-i e deklaron lindjen e seksionit (P-IV §2, kontratë reale).
      id: "profile-registry-set", kind: "set", authorityFile: MATRIX07,
      activationAnchor: /Status \(effective on merge të PR-së tooling GOV-001\)/,
      authorityPattern: /(\d+) profilet e tjera = `?NOT_IMPLEMENTED`?/g,
      consumers: [TRACKER, SSOT, INDEX, ROADMAP, PM].map((file) => ({
        file, requirement: "SCAN_IF_PRESENT" as const,
        pattern: /(\d+) profil(?:e|et)?(?: e tjera)? ?=? ?(?:mbeten )?`?NOT_IMPLEMENTED/g,
      })),
    },
    {
      // Statusi eksplicit i HG-LINTER-REQUIRED lindi me §Linter status block (v2.3) —
      // v2.2 e përmend gate-in pa asertim statusi; anchor si më sipër.
      id: "hg-linter-required-status", kind: "status", authorityFile: MATRIX07,
      activationAnchor: /Status \(effective on merge të PR-së tooling GOV-001\)/,
      authorityPattern: /HG-LINTER-REQUIRED[`*,]{0,3} ?(?:mbetet |është )?(i paaprovuar|pending|gated|APPROVED)/gi,
      normalize: normalizeHgRequired,
      consumers: [TRACKER, SSOT, INDEX, ROADMAP, PM, GATE].map((file) => ({
        file, requirement: "SCAN_IF_PRESENT" as const,
        pattern: /HG-LINTER-REQUIRED[`*,)]{0,3} ?(?:mbetet |është )?(i paaprovuar|pending|gated|mbetet gated|APPROVED)/gi,
      })),
    },
    {
      // §6 P-IV PHASE: NJË authority i vetëm = rreshti path-bound `ATK-002` në
      // Roadmap, kolona "Faza" (Roadmap NUK është njëkohësisht consumer i kësaj
      // klase). Konsumatorë row/path-bound: rreshti `Fiskalizim / SEF` në 07
      // (qeliza F<n>) + çelësat FISCAL-F*/BLOCKS-F* në Ledger. F-numra në prozë
      // të zakonshme NUK numërohen (pattern-et janë të lidhur me rresht/çelës).
      id: "sef-fiscal-phase", kind: "phase", authorityFile: ROADMAP,
      authorityPattern: /\| ATK-002 \|[^\n]*\| Faza (\d+) \|/g,
      consumers: [
        { file: MATRIX07, requirement: "SCAN_IF_PRESENT" as const, pattern: /\| Fiskalizim \/ SEF \|[^\n]*\| F(\d+) \|/g },
        { file: LEDGER, requirement: "SCAN_IF_PRESENT" as const, pattern: /FISCAL-F(\d+)/g },
        { file: LEDGER, requirement: "SCAN_IF_PRESENT" as const, pattern: /BLOCKS-F(\d+)/g },
      ],
    },
  ];
}

/** Sipërfaqet default që DUHET të mbajnë chain-in e plotë aktiv (§4). */
export const DEFAULT_CHAIN_REQUIRED = [INDEX, ROADMAP, GATE, SSOT, PM];

/**
 * §9 P-IV — lista MACHINE-READABLE e canonical active surfaces për
 * role/privacy coverage. Sipërfaqe e detyrueshme që mungon/s'lexohet → exit 2.
 */
export const ROLE_REQUIRED_SURFACES = [
  SSOT, ROADMAP, TRACKER, PM, INDEX, GATE, REGISTER, LEDGER, MATRIX07,
];
/** README/ANALIZA skanohen kur ekzistojnë si consumers canonical aktivë. */
export const ROLE_OPTIONAL_SURFACES = [POC5README, ANALIZA];

/** Etiketë historike ROW-SCOPED — vlen vetëm në rreshtin ku gjendet vlera. */
/**
 * P-VI §2 — etiketa historike VETËM STRUKTURORE, të centralizuara (asnjë
 * regex kontradiktor nëpër module):
 *  - PREFIX rreshti: `HISTORICAL:` / `SUPERSEDED:` / `HISTORIK:` / `Historia:`
 *    (me dekorime markdown-i të lejuara para tyre);
 *  - MARKER strukturor: `(HISTORICAL…)` / `(SUPERSEDED…)` ose `[HISTORICAL…]`
 *    / `[SUPERSEDED…]` (kllapa MENJËHERË para fjalës UPPERCASE);
 *  - QELIZË statusi tabele me vlerë EKZAKTE (parser-i GFM, jo substring).
 * FJALËT E LIRA nuk përjashtojnë: "nuk është superseded", "nuk është ish",
 * "versioni i atëhershëm", `ish`/`superseded` në prozë → JO historike.
 * Etiketa vlen VETËM për rreshtin e vet — kurrë për fqinjët.
 */
export function isHistoryPrefixLabeled(line: string): boolean {
  return /^\s*[*_>\-|\s]*(?:HISTORICAL|SUPERSEDED|HISTORIK|Historia)\s*:/.test(line);
}

/**
 * P-VII Fix C — marker inline i MBYLLUR saktë, pa suffix: VETËM "(HISTORICAL)"
 * ose "(SUPERSEDED)" ekzakt. "(HISTORICALIZED)", "(SUPERSEDED wording",
 * "[SUPERSEDED v2.25 …]" dhe fjala e lirë në prozë NUK exempt-ojnë.
 */
const HISTORY_MARKER_RE = /\((?:HISTORICAL|SUPERSEDED)\)/;
const HISTORY_CELL_RE = /^(?:HISTORICAL|SUPERSEDED)$/;
const STATUS_HEADER_RE = /^(?:Status|Statusi|Gjendja|State)$/i;

/** Klasifikimi VETËM-rresht: prefix me dy-pika OSE marker i mbyllur ekzakt. */
export function isHistoryLabeledRow(line: string): boolean {
  return isHistoryPrefixLabeled(line) || HISTORY_MARKER_RE.test(line);
}

/**
 * P-VII Fix C — klasifikimi DOC-aware: si më lart, PLUS vlerë qelize statusi
 * ekzakt "HISTORICAL"/"SUPERSEDED" VETËM në tabelë reale GFM te KOLONA e
 * statusit (parser + indeks kolone) — kurrë substring rreshti, kurrë qelizë
 * tjetër.
 */
export function isHistoryLabeledRowInDoc(docText: string, lineNo: number): boolean {
  const line = docText.split("\n")[lineNo - 1] ?? "";
  if (isHistoryLabeledRow(line)) return true;
  if (!isTableRowCandidate(line)) return false;
  const tctx = tableContextAt(docText, lineNo);
  if (tctx === undefined) return false;
  const col = tctx.columnIndex(STATUS_HEADER_RE);
  if (col === undefined) return false;
  const cell = parseTableRow(line).cells[col]?.replace(/[`*]/g, "").trim() ?? "";
  return HISTORY_CELL_RE.test(cell);
}
