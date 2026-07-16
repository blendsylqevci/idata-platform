/**
 * GOV-001 Documentation & Compliance Integrity Linter — tipet bazë.
 *
 * Kufij të fortë (07_PRODATA §Linter):
 *  - findings NUK mbajnë kurrë matched text / raw content / path privat;
 *  - statuset e profileve janë machine-readable dhe të mbyllura;
 *  - engine-i është deterministik dhe FAIL-CLOSED: skanim i paplotë = exit 2;
 *  - gabimet mbajnë vetëm KODE FIKSE — kurrë raw git message/argv/ref/path.
 */

export type ProfileId =
  | "DOC-CORE"
  | "HISTORY-PRIV"
  | "SEF-ATK"
  | "SRC-CLOSURE"
  | "ARCH-ADR"
  | "DB-MIG"
  | "SEC-TENANT"
  | "CI-SUPPLY"
  | "FIN-ACCOUNTING"
  | "INV-WMS-OMS"
  | "KABI-OPS"
  | "OFFLINE-EDGE"
  | "PLANET-POS"
  | "UX-CLIENT"
  | "OBS-CAPACITY"
  | "CUTOVER-OPS";

/** Statuset e vetme machine-readable të lejuara për një profil. */
export type ProfileStatus = "NOT_IMPLEMENTED" | "IMPLEMENTED_ADVISORY" | "REQUIRED";

/** Modaliteti; `required` refuzohet derisa HG-LINTER-REQUIRED të aprovohet. */
export type ScanMode = "advisory";

export type Severity = "ERROR" | "WARNING" | "REVIEW";

/** Vlerësimi i një (nën)kontrolli — kurrë PASS i fabrikuar. */
export type EvaluationStatus = "EVALUATED" | "NOT_EVALUATED" | "NOT_IMPLEMENTED";

export interface Finding {
  profile: ProfileId;
  profileStatus: ProfileStatus;
  severity: Severity;
  ruleId: string;
  category: string;
  /** `tracked-file:line` · `tracked-file` · `[REDACTED_PATH]` — kurrë content/path privat. */
  locator: string;
  /** SHA i shkurtër (vetëm rregullat per-commit). */
  commit?: string;
}

export interface ProfileEvaluation {
  profile: ProfileId;
  profileStatus: ProfileStatus;
  status: EvaluationStatus;
  /** Nën-kontrolli të cilit i referohet vlerësimi (p.sh. "pr-body", "scope"); pa `check` = agregati i profilit. */
  check?: string;
  /** Kod/arsye e sigurt — kurrë content. */
  reason?: string;
}

export interface ScanResult {
  mode: ScanMode;
  evaluations: ProfileEvaluation[];
  findings: Finding[];
}

/** Gabim konfigurimi/inputi → exit 2. Mban vetëm kod fiks të sanitizuar. */
export class ConfigError extends Error {
  override readonly name = "ConfigError";
  constructor(public readonly code: string) {
    super(code);
  }
}

/** Gabim git/range/parser/skanim i paplotë → exit 2. Mban vetëm kod fiks. */
export class ScanError extends Error {
  override readonly name = "ScanError";
  constructor(public readonly code: string) {
    super(code);
  }
}

/** Një hyrje raw e diff-tree PA rename detection (rename = D + A). */
export interface RawEntry {
  oldMode: string;
  newMode: string;
  oldOid: string;
  newOid: string;
  /** A/M/D/T/U/X — një karakter (pa R/C sepse --no-renames). */
  status: string;
  path: string;
}

export interface AddedContent {
  /** true = blob binar; asnjë rresht nuk lexohet/publikohet. */
  binary: boolean;
  /** Rreshtat e SHTUAR me numër REAL rreshti të target file-it. */
  lines: Array<{ line: number; text: string }>;
}

/**
 * Lexues Git i injektueshëm — i vetmi kanal drejt përmbajtjes.
 * Vetëm Git objects të tracked; asnjë filesystem walk; asnjë network.
 */
export interface GitReader {
  revParse(ref: string): string;
  isShallow(): boolean;
  mergeBase(a: string, b: string): string;
  revList(baseExclusive: string, head: string): string[];
  parents(sha: string): string[];
  /** Metadata të plota PA rename detection dhe PA lexim blob-i. */
  rawEntries(sha: string): RawEntry[];
  /** Madhësia e blob-it me OBJECT-ID (pa path). */
  blobSizeByOid(oid: string): number;
  /**
   * Blob-i RAW me OBJECT-ID (P-IV §3): klasifikimi binar/encoding bëhet mbi
   * byte-t e vërtetë të objektit — i PAVARUR nga .gitattributes/extension/diff.
   * Thirret VETËM pas path/mode/scope/size gate.
   */
  blobByOid(oid: string): Buffer;
  /** Added-lines me numra realë; thirret VETËM për paths të aprovuara nga policy. */
  addedContent(sha: string, path: string): AddedContent;
  showFile(ref: string, path: string): string;
  /** Kërkon ref të VALIDUAR më parë; false = path-i mungon në atë ref. */
  hasFile(ref: string, path: string): boolean;
}

export interface PullRequestEvent {
  baseSha?: string;
  headSha?: string;
  body?: string;
}

export interface ScanConfig {
  mode: ScanMode;
  profiles: ProfileId[];
  docsRef: string;
  baseSha?: string;
  headSha?: string;
  event?: PullRequestEvent;
}
