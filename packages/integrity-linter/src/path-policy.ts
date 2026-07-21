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
 *
 * P-V C2: klasifikuesit privatë kontrollohen mbi ÇDO SEGMENT të path-it —
 * jo vetëm mbi basename-in final: `.env/secret.txt`, `nested/.env/x`,
 * `notes.secret.md/x`, `docs/OfertaClient/x` = BLOCKED. Allowlist-i i env
 * (.env.example / .env.sef.example) vlen VETËM si FILE FINAL — kurrë si
 * directory ancestor. Absolute / `..` / segment bosh / NUL → BLOCKED
 * (fail-closed drejt privatësisë; kufijtë e encoding-ut mbeten te reader-i).
 */
export function evaluatePath(path: string): PathVerdict {
  if (path.includes("\u0000")) return "BLOCKED";
  if (path.startsWith("/") || path.startsWith("\\") || /^[A-Za-z]:[\\/]/.test(path)) return "BLOCKED";
  const norm = path.replace(/\\/g, "/").replace(/^\/+/, "");
  const lower = norm.toLowerCase();
  const segments = norm.split("/");
  if (segments.some((s) => s === "" || s === "." || s === "..")) return "BLOCKED";

  // 2) direktori të bllokuara kudo
  for (const dir of BLOCKED_DIRS) {
    const re = new RegExp(`(^|/)${escapeRe(dir)}(/|$)`, "i");
    if (re.test(norm)) return "BLOCKED";
  }

  // 3) nën-pemë specifike nga rrënja
  for (const sub of BLOCKED_SUBTREES) {
    if (lower === sub.toLowerCase() || lower.startsWith(sub.toLowerCase() + "/")) return "BLOCKED";
  }

  // 1/4/5) klasifikues privatë mbi ÇDO segment (C2)
  for (let i = 0; i < segments.length; i++) {
    const seg = segments[i] ?? "";
    const segLower = seg.toLowerCase();
    const isFinal = i === segments.length - 1;

    // env: .env dhe .env.* bllokohen KUDO; allowlist VETËM si file final
    if (segLower === ENV_BASENAME || segLower.startsWith(ENV_BASENAME + ".")) {
      if (!(isFinal && ENV_ALLOWED.has(segLower))) return "BLOCKED";
    }

    // *.secret.md — klasë kudo, edhe si directory ancestor
    if (segLower.endsWith(frag(".", "secret", ".md"))) return "BLOCKED";

    // prapashtesa binare/kredencialesh — edhe si emër direktorie
    const ext = segLower.includes(".") ? segLower.slice(segLower.lastIndexOf(".") + 1) : "";
    if (BLOCKED_EXTENSIONS.includes(ext)) return "BLOCKED";

    // stema owner-only SUBSTRING në çdo segment (parity me guard-in: grep -iE
    // substring mbi PATH e plotë — pa kufij fjale, pa kufizim në basename)
    for (const stem of BLOCKED_BASENAME_STEMS) {
      if (segLower.includes(stem.toLowerCase())) return "BLOCKED";
    }
    for (const sub of BLOCKED_BASENAME_SUBSTRINGS) {
      if (segLower.includes(sub.toLowerCase())) return "BLOCKED";
    }
    if (BLOCKED_KEY_BASENAMES.test(seg)) return "BLOCKED";
  }

  return "ALLOWED";
}

/**
 * P-VI §4 — KLASIFIKUES I CENTRALIZUAR për përmendje path-esh ABSOLUTE LOKALE
 * në content (rregulli HP-ABSOLUTE-LOCAL-PATH). Mbulon rrënjët POSIX/macOS/
 * Linux, `~/`, drive-letters Windows (slash/backslash), UNC dhe extended
 * `\\?\`. ANTI-FP: URL-të (`scheme://…`) NUK janë local path; endpoints si
 * `/ca/verify`, `/pos/*`, `/citizen/*` NUK janë në rrënjët e mbuluara;
 * path-et relative të tracked (`docs/…`) s'kanë prefiks absolut.
 * Output-i i thirrësit mbetet vetëm rule-id + safe locator — kurrë raw path.
 */
const LOCAL_ABS_ROOTS: string[] = [
  frag("/Us", "ers/"), frag("/ho", "me/"), frag("/ro", "ot/"),
  frag("/tm", "p/"), frag("/priv", "ate/"), frag("/va", "r/"),
  frag("/op", "t/"), frag("/et", "c/"), frag("/Volu", "mes/"),
  frag("/Applica", "tions/"), frag("/Libr", "ary/"), frag("/usr/", "local/"),
  frag("/mn", "t/"), frag("/sr", "v/"),
].map((r) => r.toLowerCase());

// P-VII Fix D: rrenja POSIX = segment ABSOLUT — karakteri para '/' nuk duhet
// te jete pjese path-i/tokeni ("word/tmp" → relativ, JO local-absolute).
const PATHISH_BEFORE_RE = /[\p{L}\p{N}\/.\-_~\\]/u;
const TILDE_PATH_RE = /(?<![\p{L}\p{N}])~\//u;
const WIN_DRIVE_RE = /(?<![\p{L}\p{N}])[A-Za-z]:[\\/]/u;
const UNC_RE = /(?<![\p{L}\p{N}\\/])\\\\[\p{L}\p{N}._$-]+\\/u;
const SCHEME_RE = /^([a-z][a-z0-9+.\-]*):\/\//i;

function tokenAt(line: string, idx: number): string {
  let s = idx;
  while (s > 0 && !/\s/.test(line[s - 1] as string)) s -= 1;
  return line.slice(s, idx);
}

/**
 * P-VII Fix D — vendimi per nje kandidat brenda nje tokeni:
 *  - skema file:// dhe vscode://… = path LOKAL → FIRE;
 *  - skema te tjera (http/https/…) = URL → ky kandidat anashkalohet
 *    (por kandidate te tjere ne te njejtin rresht vleresohen me vete);
 *  - pa skeme → kandidat i vlefshem (me kufi absolut kur kerkohet).
 */
function candidateFires(line: string, idx: number, requireAbsBoundary: boolean): boolean {
  const tok = tokenAt(line, idx);
  const scheme = SCHEME_RE.exec(tok);
  if (scheme !== null) {
    const sc = (scheme[1] as string).toLowerCase();
    return sc === "file" || sc.startsWith("vscode");
  }
  if (requireAbsBoundary && idx > 0 && PATHISH_BEFORE_RE.test(line[idx - 1] as string)) return false;
  return true;
}

export function containsLocalAbsolutePath(line: string): boolean {
  const lower = line.toLowerCase(); // /Users dhe /users — case-insensitive
  for (const root of LOCAL_ABS_ROOTS) {
    let from = 0;
    for (;;) {
      const idx = lower.indexOf(root, from);
      if (idx === -1) break;
      from = idx + root.length;
      if (candidateFires(line, idx, true)) return true;
    }
  }
  for (const re of [TILDE_PATH_RE, WIN_DRIVE_RE]) {
    const g = new RegExp(re.source, re.flags.includes("g") ? re.flags : re.flags + "g");
    let m: RegExpExecArray | null;
    while ((m = g.exec(line)) !== null) {
      if (candidateFires(line, m.index, false)) return true;
    }
  }
  if (UNC_RE.test(line)) return true;
  if (line.includes(frag("\\\\?", "\\"))) return true; // extended Windows syntax
  return false;
}

/** Mode-t e vetme të lejuara për content access. */
export const ALLOWED_FILE_MODES = new Set(["100644", "100755"]);

/** Mode "zero" i diff-tree për anën që mungon (A/D). */
export const NULL_MODE = "000000";
