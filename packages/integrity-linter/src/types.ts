/**
 * GOV-001 Documentation & Compliance Integrity Linter — tipet bazë.
 *
 * Kufij të fortë (shih 07_PRODATA §Linter):
 *  - findings NUK mbajnë kurrë matched text / raw content / path privat;
 *  - statuset e profileve janë machine-readable dhe të mbyllura;
 *  - engine-i është deterministik dhe fail-closed.
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

/** Modaliteti i ekzekutimit; `required` refuzohet derisa HG-LINTER-REQUIRED të aprovohet. */
export type ScanMode = "advisory";

export type Severity = "ERROR" | "WARNING" | "REVIEW";

/** Vlerësimi i një profili/rregulli që NUK u ekzekutua — kurrë PASS i fabrikuar. */
export type EvaluationStatus = "EVALUATED" | "NOT_EVALUATED" | "NOT_IMPLEMENTED" | "INCOMPLETE_SCAN";

export interface Finding {
  profile: ProfileId;
  profileStatus: ProfileStatus;
  severity: Severity;
  ruleId: string;
  category: string;
  /**
   * Locator i sigurt: `tracked-file:line`, `tracked-file`, ose `[REDACTED_PATH]`.
   * KURRË matched line/body/identifier; KURRË path privat real.
   */
  locator: string;
  /** SHA i shkurtër i commit-it (vetëm për rregullat per-commit). */
  commit?: string;
}

export interface ProfileEvaluation {
  profile: ProfileId;
  profileStatus: ProfileStatus;
  status: EvaluationStatus;
  /** Arsye e sigurt (pa content), p.sh. "missing GITHUB_EVENT_PATH". */
  reason?: string;
}

export interface ScanResult {
  mode: ScanMode;
  evaluations: ProfileEvaluation[];
  findings: Finding[];
}

/** Gabim konfigurimi/inputi — engine-i del me exit code 2 (fail-closed). */
export class ConfigError extends Error {
  override readonly name = "ConfigError";
}

/** Gabim git/range/scan i paplotë — engine-i del me exit code 2 (fail-closed). */
export class ScanError extends Error {
  override readonly name = "ScanError";
}

/** Një hyrje e ndryshuar në një commit (nga git diff-tree --raw). */
export interface ChangedEntry {
  /** A/M/D/R/C/T (statusi i parë; R100 -> R). */
  status: string;
  /** Mode i ri (p.sh. "120000" = symlink). */
  newMode: string;
  /** SHA e blob-it të ri ("0000..." kur fshihet). */
  newBlob: string;
  path: string;
  /** Path-i i vjetër për rename/copy. */
  oldPath?: string;
}

/**
 * Lexues Git i injektueshëm — i vetmi kanal i lejuar drejt përmbajtjes.
 * Vetëm Git objects të tracked; ASNJË filesystem walk, ASNJË network.
 */
export interface GitReader {
  revParse(ref: string): string;
  isShallow(): boolean;
  mergeBase(a: string, b: string): string;
  /** Commits në (baseExclusive, head], të renditur nga më i vjetri. */
  revList(baseExclusive: string, head: string): string[];
  parents(sha: string): string[];
  changedEntries(sha: string): ChangedEntry[];
  /** Rreshtat e SHTUAR të një file-i në një commit (unified=0). Refuzon UTF-8 invalid. */
  addedLines(sha: string, path: string): string[];
  /** Paths të prekur si BINARË në commit (nga numstat "-"), pa lexuar përmbajtje. */
  binaryPaths(sha: string): Set<string>;
  /** Madhësia e blob-it në byte për `<sha>:<path>`. */
  blobSize(sha: string, path: string): number;
  /** Përmbajtja e një file-i të tracked në një ref (git show ref:path). */
  showFile(ref: string, path: string): string;
  /** A ekziston file-i i tracked në ref. */
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
  /** Ref-i ku lexohen dokumentet canonical (default: HEAD). */
  docsRef: string;
  baseSha?: string;
  headSha?: string;
  event?: PullRequestEvent;
}
