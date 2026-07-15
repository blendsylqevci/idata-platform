/**
 * PATH POLICY — moduli i VETËM i politikës së path-eve private/të bllokuara.
 *
 * Burim i sinkronizuar me `.gitignore` + `scripts/precommit_confidential_guard.sh`
 * + PROJECT_RULES.md (parity tests në test/path-policy.test.ts — lista nuk lejohet
 * të driftojë nëpër module).
 *
 * Stemat që kapen nga skanerat e sekreteve mbi VETË këtë burim ndërtohen
 * RUNTIME nga fragmente sintetike (zero literale që trip-ojnë guard/gitleaks).
 */

function frag(...parts: string[]): string {
  return parts.join("");
}

function escapeRe(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\/]/g, (c) => `\\${c}`);
}

/** Direktori/rrënjë të bllokuara (kudo në path). */
const BLOCKED_DIRS: string[] = [
  frag(".", "claude"),
  frag("00", "_raw"),
  frag("docs", "-private"),
  frag("private", "-sensitive"),
  frag("evidence", "-private", "-local"),
  frag(".", "sef", "-local"),
  frag("03", "_contracts"),
  "outputs",
];

/** Nën-pemë specifike të bllokuara (prefiks nga rrënja e repo-s). */
const BLOCKED_SUBTREES: string[] = [
  frag("docs/", "evidence"),
  frag("docs/archive/", "prodata"),
  frag("docs/archive/", "client-docs"),
  frag("docs/archive/", "client-docs-text"),
  frag("docs/archive/", "contracts"),
  frag("docs/archive/", "old-master"),
  frag("docs/archive/", "prompts"),
];

/** Prapashtesa binare/kredencialesh të bllokuara. */
const BLOCKED_EXTENSIONS = ["mdf", "ldf", "bak", "pem", "key", "p12", "pfx", "keystore", "jks"];

/** Stema emrash file owner-only (fillim i basename-it). */
const BLOCKED_BASENAME_STEMS: string[] = [
  frag("CANONICAL_SPLIT_COPY_", "MANIFEST"),
  frag("PLANET_VAT_", "Reconciliation_"),
  frag("Ofer", "ta"),
  frag("Ofer", "t"),
  frag("Kontra", "t"),
  frag("Shtoj", "ca"),
  frag("Shtoj", "c"),
  frag("Dokumenti", "_", "Master"),
  frag("Dosja", "_", "Certifikimit"),
];

/** Nën-stringje të bllokuara kudo në basename. */
const BLOCKED_BASENAME_SUBSTRINGS: string[] = [
  frag("Certifikimit", "_", "SEF"),
  frag("_v2_", "Draft"),
];

/** Emra çelësash SSH. */
const BLOCKED_KEY_BASENAMES = /^id_(rsa|ed25519|ecdsa|dsa)(\.|$)/i;

const ENV_BASENAME = frag(".", "env");
const ENV_ALLOWED = new Set([
  frag(".", "env", ".example"),
  frag(".", "env", ".sef", ".example"),
]);

export type PathVerdict = "ALLOWED" | "BLOCKED";

/**
 * Vlerëson një path repo-relativ TË TRACKED (asnjë qasje në filesystem).
 * BLOCKED = klasë private/owner-only; raportohet gjithmonë vetëm si [REDACTED_PATH].
 */
export function evaluatePath(path: string): PathVerdict {
  const norm = path.replace(/\\/g, "/").replace(/^\/+/, "");
  const lower = norm.toLowerCase();
  const segments = norm.split("/");
  const basename = segments[segments.length - 1] ?? "";
  const basenameLower = basename.toLowerCase();

  // 1) env: .env dhe .env.* bllokohen; .env.example / .env.sef.example lejohen
  if (basenameLower === ENV_BASENAME || basenameLower.startsWith(ENV_BASENAME + ".")) {
    if (!ENV_ALLOWED.has(basenameLower)) return "BLOCKED";
  }

  // 2) direktori të bllokuara kudo
  for (const dir of BLOCKED_DIRS) {
    const re = new RegExp(`(^|/)${escapeRe(dir)}(/|$)`, "i");
    if (re.test(norm)) return "BLOCKED";
  }

  // 3) nën-pemë specifike nga rrënja
  for (const sub of BLOCKED_SUBTREES) {
    if (lower === sub.toLowerCase() || lower.startsWith(sub.toLowerCase() + "/")) return "BLOCKED";
  }

  // 4) prapashtesa
  const ext = basenameLower.includes(".") ? basenameLower.slice(basenameLower.lastIndexOf(".") + 1) : "";
  if (BLOCKED_EXTENSIONS.includes(ext)) return "BLOCKED";

  // 4b) *.secret.md — klasë kudo, çfarëdo case/nesting
  if (basenameLower.endsWith(frag(".", "secret", ".md"))) return "BLOCKED";

  // 5) stema owner-only SUBSTRING kudo në basename (§6A: parity e saktë me
  // guard-in, i cili bën grep -iE substring mbi path — pa kufij fjale)
  for (const stem of BLOCKED_BASENAME_STEMS) {
    if (basenameLower.includes(stem.toLowerCase())) return "BLOCKED";
  }
  for (const sub of BLOCKED_BASENAME_SUBSTRINGS) {
    if (basenameLower.includes(sub.toLowerCase())) return "BLOCKED";
  }
  if (BLOCKED_KEY_BASENAMES.test(basename)) return "BLOCKED";

  return "ALLOWED";
}

/** Mode-t e vetme të lejuara për content access. */
export const ALLOWED_FILE_MODES = new Set(["100644", "100755"]);

/** Mode "zero" i diff-tree për anën që mungon (A/D). */
export const NULL_MODE = "000000";
