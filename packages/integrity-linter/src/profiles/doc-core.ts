/**
 * DOC-CORE — integriteti i dokumentacionit canonical & governance (advisory, FAIL-CLOSED).
 *
 * Hardening III:
 *  - §1: validator i PËRBASHKËT pure `extractAuthorityValue` (i njëjti me HISTORY-PRIV);
 *  - §4: zinxhiri invariant kontrollohet për PRANI + REND në sipërfaqet chainRequired —
 *    të 6 hallkat duhen në një block AKTIV; HISTORICAL chain s'e plotëson;
 *    variantet e review normalizohen në të njëjtën hallkë;
 *  - §7: qelizat me GFM parser; role-bearing fields (kolona Owner/Pronari/Kush/… +
 *    etiketa **Pronari:**) në TË GJITHA sipërfaqet aktive; FRAZA TË PLOTA rolesh —
 *    një fjalë e lejuar brenda tokenit NUK e aprovon tokenin.
 *
 * Linter-i NUK jep opinion ligjor dhe NUK vendos semantikën e asnjë neni.
 */

import type { Finding, GitReader, ProfileEvaluation, PullRequestEvent } from "../types.js";
import { ScanError } from "../types.js";
import { profileStatus } from "../profile-registry.js";
import {
  AuthorityError, defaultAuthorityClasses, extractAuthorityValue, isHistoryLabeledRow, isHistoryPrefixLabeled,
  GATE, INDEX, LEDGER, PM, POC5README, ROADMAP, SSOT, TRACKER,
  ROLE_OPTIONAL_SURFACES, ROLE_REQUIRED_SURFACES,
  type AuthorityClass,
} from "../authority-classes.js";
import { isSeparatorRow, isTableRowCandidate, parseTableRow, tableContextAt } from "../markdown-table.js";
import {
  hasObfuscatedMatch, normalizeApostrophesForDetection,
  NOT_LETTER_AFTER, NOT_LETTER_BEFORE, SENTENCE_DELIM_RE,
} from "../machine-token.js";

const P = "DOC-CORE" as const;

export interface DocCoreSurfaces {
  orderSurfaces: string[];
  /** Sipërfaqet e DETYRUARA të mbajnë një block AKTIV me TË GJITHA 6 hallkat (§4). */
  chainRequiredSurfaces: string[];
  trackerFile?: string;
  milestoneSurfaces: string[];
  authorities: AuthorityClass[];
  approvedRolePhrases: string[];
  /** §9 P-IV: canonical active surfaces për role/privacy — mungesa = exit 2. */
  roleRequiredSurfaces: string[];
  /** §9 P-IV: consumers canonical aktivë që skanohen VETËM kur ekzistojnë. */
  roleOptionalSurfaces: string[];
}

/**
 * FRAZA TË PLOTA rolesh të aprovuara (normalizuara lowercase; §7) — të nxjerra
 * nga vokabulari canonical i normalizuar (PR #20). KURRË emra personalë realë.
 * Tokeni duhet të BARAZOHET me një frazë pas normalizimit — jo substring.
 */
export const APPROVED_ROLE_PHRASES = [
  "pronari", "codex", "claude code", "pm", "group owner", "human gate", "finance owner",
  "kontabilisti", "icode", "icode dev", "icode support", "icode l.l.c",
  "atk", "kabi", "kabi admin", "kabi sales", "planet", "planet finance",
  "planet operations", "planet management", "teknologu", "fabrika", "laboratori",
  "jurist", "aip", "tech", "hardware vendor", "klienti", "owner", "burimi",
  "logistics", "tier 2", "ti", "group",
];

export function defaultSurfaces(): DocCoreSurfaces {
  return {
    orderSurfaces: [INDEX, ROADMAP, GATE, SSOT, PM, POC5README, LEDGER],
    chainRequiredSurfaces: [INDEX, ROADMAP, GATE, SSOT, PM],
    trackerFile: TRACKER,
    milestoneSurfaces: [INDEX, ROADMAP, SSOT, PM],
    authorities: defaultAuthorityClasses(),
    approvedRolePhrases: APPROVED_ROLE_PHRASES,
    roleRequiredSurfaces: ROLE_REQUIRED_SURFACES,
    roleOptionalSurfaces: ROLE_OPTIONAL_SURFACES,
  };
}

export { isHistoryLabeledRow as isHistoryLabeled };

/**
 * Hallkat e zinxhirit invariant; review normalizohet nga TË GJITHA variantet
 * reale (§4). P-VII Fix F: kufij UNICODE-aware në të DYJA anët — `\b` i
 * ASCII-t pranonte "Step Bë"/"Tier 2ë" si token valid (ë = jo-fjalë për \b).
 */
function chainToken(src: string, flags = "u"): RegExp {
  return new RegExp(NOT_LETTER_BEFORE + src + NOT_LETTER_AFTER, flags);
}
export const CHAIN_TOKENS: Array<{ key: string; res: RegExp[] }> = [
  { key: "step-b", res: [chainToken("Step B")] },
  { key: "step-c", res: [chainToken("Step C")] },
  {
    key: "independent-review",
    res: [chainToken("independent re-?review", "iu"), chainToken("re-review", "iu"),
      chainToken("independent review", "iu"),
      chainToken("review(?:-t|t)? (?:i|të) pavarur", "iu"),
      chainToken("rishikim(?:i|in)? (?:i|të) pavarur", "iu")],
  },
  { key: "human-gate", res: [chainToken("HUMAN GATE", "iu")] },
  { key: "preflight", res: [chainToken("PREFLIGHT", "iu")] },
  { key: "tier-2", res: [chainToken("Tier 2")] },
];

function firstIndex(paragraph: string, res: RegExp[]): number {
  let best = -1;
  for (const re of res) {
    const m = re.exec(paragraph);
    if (m && (best === -1 || m.index < best)) best = m.index;
  }
  return best;
}

const ALL_CHAIN_RES = CHAIN_TOKENS.flatMap((t) => t.res);

/**
 * §7 P-IV + P-VI §1 — mohim CLAIM-scoped i zinxhirit, UNICODE-AWARE:
 * "NUK është ky rendi:", "s'është ky rendi:", "s’janë …", "ç'është ky rend",
 * "jo ky rend", "mos e konsidero këtë rend" ≠ chain aktiv. `\b` i ASCII-t
 * dështonte pranë apostrofit/ë-së — përdoren lookaround-e \p{L}\p{N} me flag
 * `u` + normalizim apostrofësh VETËM për detection. "zinxhiri nuk ndryshon" /
 * "rendi mbetet i pandryshuar" mbeten claim AKTIV (mohuesi duhet të NDJEKET
 * nga folja/emri i rendit); mohimi në fjali tjetër nuk e mohon zinxhirin
 * (delimituesit përfshijnë U+2028/U+2029 — kontratë §1).
 */
/**
 * P-VII Fix A — mohim i lidhur me CLAIM-in e VETË zinxhirit, jo me çdo fjalë
 * chain-ish brenda 40 karakteresh (over-suppression e P-VI fshihte zinxhirë
 * REALË aktivë si "Nuk ndryshon rendi: …" / "Nuk ka PII, rendi aktual: …").
 * Grama e ngushtë e claim-mohimit (pas normalizimit të apostrofëve):
 *  - (nuk|mos|s') + KOPULA (është/janë) + … + emër rendi (rend/zinxhir/sekuenc/hap);
 *  - "mos (e) konsidero … <emër rendi>";
 *  - "jo <dëftor> <emër rendi>" ("jo ky rend" PO; "Jo vetëm ky rend" JO).
 * "ç'është …" është PYETJE, jo mohim (supersedes P-VI — dokumentuar në test).
 * Analizohet FJALIA që përmban token-ët e zinxhirit — jo vetëm teksti para
 * token-it të parë ("Step B nuk është ky rendi: Step B → …" njihet i mohuar).
 */
const CHAIN_NOUN = "(?:rend|zinxhir|sekuenc|hap)\\p{L}*";
const CHAIN_CLAIM_NEG_RE = new RegExp(
  "(?:" +
    NOT_LETTER_BEFORE + "(?:nuk\\s+|mos\\s+|s')(?:është|janë)" +
      "[^\\n]{0,24}?" + NOT_LETTER_BEFORE + CHAIN_NOUN + NOT_LETTER_AFTER +
  "|" + NOT_LETTER_BEFORE + "mos(?:\\s+e)?\\s+konsidero\\p{L}*" +
      "[^\\n]{0,24}?" + NOT_LETTER_BEFORE + CHAIN_NOUN + NOT_LETTER_AFTER +
  "|" + NOT_LETTER_BEFORE + "jo\\s+(?:ky|kjo|këto)\\s+" + CHAIN_NOUN + NOT_LETTER_AFTER +
  ")",
  "iu",
);
export function negatedChainClaim(line: string): boolean {
  const norm = normalizeApostrophesForDetection(line);
  for (const sentence of norm.split(SENTENCE_DELIM_RE)) {
    if (firstIndex(sentence, ALL_CHAIN_RES) < 0) continue;
    return CHAIN_CLAIM_NEG_RE.test(sentence); // fjalia e claim-it — e TËRA
  }
  return false;
}

/** §7 P-IV — blanko VETËM segmentet (midis →/pastaj) me etiketë historike. */
function blankHistoricalSegments(line: string): string {
  const label = /HISTORICAL|SUPERSEDED|HISTORIK[EU]?\b/; // P-VI §2: vetëm strukturore — jo fjalë të lira
  const bounds: number[] = [0];
  for (const mk of line.matchAll(/→|\bpastaj\b/g)) {
    if (mk.index !== undefined) bounds.push(mk.index);
  }
  bounds.push(line.length);
  let out = "";
  for (let b = 0; b + 1 < bounds.length; b++) {
    const seg = line.slice(bounds[b], bounds[b + 1]);
    out += label.test(seg) ? " ".repeat(seg.length) : seg;
  }
  return out;
}

/**
 * §7 P-IV — CLAIMS të rendit: çdo rresht me marker (→/"pastaj") është
 * pretendim rendi MË VETE (dy bullets të pavarura në një paragraf NUK
 * krahasohen mes tyre); vetëm run-et e njëpasnjëshme të artikujve të listës
 * numerike/bullet pa marker bashkohen si NJË claim i renditur. Parenteza të
 * hequra; tabela të përjashtuara; etiketa historike row-prefiks ose
 * segment-scoped ("Step A (HISTORICAL)" s'i përjashton hallkat pas tij);
 * zinxhir i MOHUAR s'është claim; dritare e VAZHDUESHME
 * [markeri i parë − 160, i fundit + 160] që të mos pritet "Step B".
 */
export function extractChainClaims(lines: string[]): string[] {
  const claims: string[] = [];
  let subjectRun: string[] = [];
  const flush = (): void => {
    if (subjectRun.length > 0) {
      claims.push(subjectRun.join("\n"));
      subjectRun = [];
    }
  };
  for (const rawLine of lines) {
    if (isTableRowCandidate(rawLine)) { flush(); continue; } // tabelat s'janë zinxhirë
    if (isHistoryPrefixLabeled(rawLine)) { flush(); continue; } // historik ROW-scoped (P-VI: prefiks strukturor qendror)
    const line = blankHistoricalSegments(rawLine.replace(/\([^)]*\)/g, " "));
    if (negatedChainClaim(line)) { flush(); continue; } // i mohuar ≠ claim aktiv
    const isNumbered = /^\s*(?:\d+[a-z]?\.|[-*])\s/.test(line);
    const markers: number[] = [];
    for (const mk of line.matchAll(/→|\bpastaj\b/g)) markers.push(mk.index ?? 0);
    if (isNumbered && markers.length === 0) {
      const subject = line.slice(0, 48);
      if (!/(\bnuk\b|\bs'|\bjo\b|vetëm pas)/i.test(subject)) subjectRun.push(subject);
    } else if (markers.length > 0) {
      flush();
      const first = markers[0] ?? 0;
      const last = markers[markers.length - 1] ?? 0;
      const from = Math.max(0, first - 160);
      const to = Math.min(line.length, last + 160);
      claims.push(" ".repeat(from) + line.slice(from, to));
    } else {
      flush(); // prozë pa marker e ndan run-in e listës
    }
  }
  flush();
  return claims;
}

/** Bashkimi i claims — për PRANI të hallkave (jo për rend ndër-claims). */
export function extractChainText(lines: string[]): string {
  return extractChainClaims(lines).join("\n");
}

/**
 * §7 P-IV — indeksi i parë JO-I-MOHUAR i një tokeni: "JO Tier 2 direkt" /
 * "nuk … Tier 2" NUK numërohet as si prani, as për rend.
 */
const NEG_TOKEN_BEFORE_RE = new RegExp(
  NOT_LETTER_BEFORE + "(?:JO|nuk|jo|mos|asnjë|s')" + "[^\\p{L}\\p{N}]{0,6}$", "iu",
);

function firstActiveIndex(text: string, res: RegExp[]): number {
  let best = -1;
  for (const re of res) {
    const g = new RegExp(re.source, re.flags.includes("g") ? re.flags : re.flags + "g");
    let m: RegExpExecArray | null;
    while ((m = g.exec(text)) !== null) {
      const before = normalizeApostrophesForDetection(text.slice(Math.max(0, m.index - 12), m.index));
      // P-VI §1: kufij Unicode-aware — `\basnjë\b` i ASCII-t ishte i vdekur (ë)
      if (NEG_TOKEN_BEFORE_RE.test(before)) continue;
      if (best === -1 || m.index < best) best = m.index;
      break; // i pari jo-i-mohuar për këtë variant
    }
  }
  return best;
}

export function chainOrderViolations(chainText: string): Array<{ later: string; earlier: string }> {
  const present = CHAIN_TOKENS
    .map((t) => ({ key: t.key, idx: firstActiveIndex(chainText, t.res) }))
    .filter((t) => t.idx >= 0);
  const out: Array<{ later: string; earlier: string }> = [];
  for (let i = 1; i < present.length; i++) {
    const prev = present[i - 1];
    const cur = present[i];
    if (prev !== undefined && cur !== undefined && cur.idx < prev.idx) {
      out.push({ later: cur.key, earlier: prev.key });
    }
  }
  return out;
}

export interface ChainEvaluation {
  /** Hallkat që mungojnë nga blloku MË I MIRË aktiv i sipërfaqes. */
  missing: string[];
  /** Shkeljet e rendit në bllokun më të mirë. */
  violations: Array<{ later: string; earlier: string }>;
  /** true kur s'u gjet ASNJË block kandidat aktiv (≥2 hallka). */
  noActiveChain: boolean;
}

/**
 * §4: vlerëson PRANINË + RENDIN e zinxhirit të plotë në një dokument:
 * blloku më i mirë AKTIV duhet t'i ketë TË GJITHA hallkat në rendin e saktë.
 * Përdoret nga DOC-CORE (HEAD) dhe HISTORY-PRIV (snapshot per-commit).
 */
export function evaluateChainPresence(text: string): ChainEvaluation {
  const paragraphs = text.split(/\n\s*\n/);
  let best: { count: number; missing: string[]; violations: Array<{ later: string; earlier: string }> } | undefined;
  for (const para of paragraphs) {
    const claims = extractChainClaims(para.split("\n"));
    const chainText = claims.join("\n");
    const present = CHAIN_TOKENS.filter((t) => firstActiveIndex(chainText, t.res) >= 0);
    if (present.length < 2) continue; // jo pretendim zinxhiri (partial prose near-miss)
    const missing = CHAIN_TOKENS.filter((t) => firstActiveIndex(chainText, t.res) < 0).map((t) => t.key);
    // §7 P-IV: rendi vlerësohet PER-CLAIM — jo ndër-claims të pavarura
    const violations = claims.flatMap((c) => chainOrderViolations(c));
    const score = present.length - violations.length * 0.5;
    if (best === undefined || score > best.count) {
      best = { count: score, missing, violations };
    }
  }
  if (best === undefined) return { missing: CHAIN_TOKENS.map((t) => t.key), violations: [], noActiveChain: true };
  return { missing: best.missing, violations: best.violations, noActiveChain: false };
}

/** Header-at e kolonave role-bearing (§7) — barazi e plotë e emrit të kolonës. */
const ROLE_HEADER_RE = /^(Owner( \/ burimi)?|Pronari?|Kush|Përgjegjës\w*|Approver|Human Gate)$/i;
/** Etiketat metadata role-bearing — "Human Gate" njihet si label (§9 P-IV). */
const ROLE_LABEL_RE = /^\*{0,2}(Pronar(?:i)?(?: i dokumentit)?|Owner|Përgjegjës\w*|Approver|Human Gate)\s*:\*{0,2}\s*(.+)$/i;

/**
 * P-V F4 — projeksion identiteti VETËM për role-bearing values (pa HTML
 * normalization global): <br> variantet, HTML comments dhe whitespace
 * entities → separator; markup/entity i papritur i mbetur → sinjal
 * DC-ROLE-MARKUP (REVIEW) te thirrësi. Asnjë content në output.
 */
export function projectRoleMarkup(value: string): { text: string; unexpectedMarkup: boolean } {
  const text = value
    .replace(/<br\s*\/?\s*>/gi, " ")
    .replace(/<!--[\s\S]*?-->/g, " ")
    .replace(/&(?:nbsp|ensp|emsp|thinsp|zwsp|#0*160|#0*8194|#0*8195|#0*8201|#0*8203|#x0*A0);/gi, " ");
  const unexpectedMarkup = /<[A-Za-z!/]|&[A-Za-z#][A-Za-z0-9]{1,10};/.test(text);
  return { text, unexpectedMarkup };
}

/** Fjalë funksionale të annotation-eve sintaktike — JO pjesë identiteti. */
const FUNCTIONAL_ANNOTATION_WORDS = new Set([
  "aprovon", "miraton", "çdo", "ndryshim", "ndryshimet", "ndryshimin", "dokument",
  "dokumentit", "dokumenti", "rresht", "rreshtit", "nëse", "kur", "duhet", "sipas",
  "vetëm", "pas", "para", "për", "nga", "dhe", "ose", "me", "pa", "të", "e", "i",
  "u", "në", "që", "këtij", "kësaj", "atij", "asaj", "review", "approval",
]);

/**
 * §9 P-IV + P-V C4 — parentetikat NUK hidhen blanket: kandidat identiteti
 * brenda `(…)` të një vlere role → privacy REVIEW (pa publikuar content-in).
 * C4: kandidati njihet në ÇDO casing (TitleCase/UPPERCASE/lowercase/Unicode):
 * run ≥2 fjalësh jo-funksionale e jo-të-aprovuara = kandidat; fjalë e vetme
 * me shkronjë të madhe nistore ≥4 shkronja jo e aprovuar = kandidat.
 * Ambiguiteti identitet-vs-annotation zgjidhet me REVIEW — kurrë auto-approval.
 */
export function parentheticalIdentityCandidate(cellRaw: string, approvedPhrases: string[]): boolean {
  const approved = new Set(approvedPhrases.map((p) => p.toLowerCase()));
  for (const m of cellRaw.matchAll(/\(([^)]*)\)/g)) {
    const inner = (m[1] ?? "").replace(/[`*]/g, "").trim();
    if (inner === "" || approved.has(inner.toLowerCase())) continue;
    const words = [...inner.matchAll(/\p{L}[\p{L}'’-]*/gu)].map((w) => w[0]);
    let run: string[] = [];
    let hit = false;
    const runHits = (): boolean => run.length >= 2 && !approved.has(run.join(" ").toLowerCase());
    for (const w of words) {
      const lw = w.toLowerCase();
      if (FUNCTIONAL_ANNOTATION_WORDS.has(lw) || approved.has(lw)) {
        if (runHits()) hit = true;
        run = [];
        continue;
      }
      run.push(w);
    }
    if (runHits()) hit = true;
    if (hit) return true;
    if (words.length === 1) {
      const w = words[0] as string;
      if (w.length >= 4 && /^\p{Lu}/u.test(w) && !approved.has(w.toLowerCase())) return true;
    }
  }
  return false;
}

/** Tokenizon një vlerë role-bearing dhe kthen numrin e tokenëve JO të aprovuar. */
export function roleTokensUnknown(cellRaw: string, approvedPhrases: string[]): number {
  const cell = cellRaw
    .replace(/\([^)]*\)/g, " ")           // parentetikat trajtohen veçmas (§9: identity scan)
    .replace(/[`*]/g, "")
    .trim();
  if (cell === "" || cell === "—" || cell === "-") return 0;
  const tokens = cell
    .split(/[/,+;·]|→|\bdhe\b/i)
    .map((t) => t.trim().toLowerCase()
      .replace(/\s+nëse duhet$/, "")
      .replace(/\s+sipas rreshtit$/, "")
      .replace(/\s+/g, " "))
    .filter((t) => t !== "" && t !== "—" && t !== "-");
  const approved = new Set(approvedPhrases.map((p) => p.toLowerCase()));
  let unknown = 0;
  for (const token of tokens) {
    if (!approved.has(token)) unknown += 1; // FRAZË E PLOTË — jo substring/fjalë e vetme
  }
  return unknown;
}

function globalized(re: RegExp): RegExp {
  return new RegExp(re.source, re.flags.includes("g") ? re.flags : re.flags + "g");
}

export function runDocCore(
  reader: GitReader,
  docsRef: string,
  surfaces: DocCoreSurfaces,
  event: PullRequestEvent | undefined,
): { findings: Finding[]; evaluations: ProfileEvaluation[] } {
  const status = profileStatus(P);
  const findings: Finding[] = [];
  const evaluations: ProfileEvaluation[] = [];
  const fileCache = new Map<string, string>();
  const readDoc = (path: string): string => {
    const cached = fileCache.get(path);
    if (cached !== undefined) return cached;
    if (!reader.hasFile(docsRef, path)) throw new ScanError("INCOMPLETE_SCAN:SURFACE_MISSING");
    const text = reader.showFile(docsRef, path);
    fileCache.set(path, text);
    return text;
  };
  const readDocIfPresent = (path: string): string | undefined => {
    const cached = fileCache.get(path);
    if (cached !== undefined) return cached;
    if (!reader.hasFile(docsRef, path)) return undefined;
    const text = reader.showFile(docsRef, path);
    fileCache.set(path, text);
    return text;
  };

  // §4A: gjendja e anchor-it strukturor në docsRef — NOT_APPLICABLE para
  // lindjes së seksionit; duplikimi = exit 2 (surface ambig).
  const anchorState = (cls: AuthorityClass, text: string): "none" | "absent" | "present" => {
    if (cls.activationAnchor === undefined) return "none";
    const flags = cls.activationAnchor.flags.includes("g")
      ? cls.activationAnchor.flags : cls.activationAnchor.flags + "g";
    const re = new RegExp(cls.activationAnchor.source, flags);
    let n = 0;
    while (re.exec(text) !== null) n += 1;
    if (n > 1) throw new ScanError("INCOMPLETE_SCAN:AUTHORITY_ANCHOR_DUPLICATE");
    return n === 1 ? "present" : "absent";
  };

  // ── 1) kontrata bidirectional authority → consumers (validator i përbashkët) ─
  for (const cls of surfaces.authorities) {
    const authorityText = readDoc(cls.authorityFile); // missing → exit 2
    if (anchorState(cls, authorityText) === "absent") continue; // §4A.7 NOT_APPLICABLE
    let current: string;
    try {
      current = extractAuthorityValue(authorityText, cls);
    } catch (err) {
      if (err instanceof AuthorityError) throw new ScanError(err.code);
      throw err;
    }
    const norm = cls.normalize ?? ((v: string) => v);

    for (const consumer of cls.consumers) {
      const text = consumer.requirement === "REQUIRED_CURRENT"
        ? readDoc(consumer.file)
        : readDocIfPresent(consumer.file);
      if (text === undefined) continue; // SCAN_IF_PRESENT: file-i mungon → lejohet
      const lines = text.split("\n");
      let currentSeenActive = false;
      let anyMatch = false;
      for (let i = 0; i < lines.length; i++) {
        const line = lines[i] ?? "";
        // P-V F1/F2: token i mbrojtur i pranishëm VETËM i obfuskuar nuk kalon
        // si "no match" — finding i dedikuar (raw mbetet burimi i vlerësimit).
        if (hasObfuscatedMatch(line, consumer.pattern)) {
          findings.push({
            profile: P, profileStatus: status, severity: "WARNING",
            ruleId: "DC-MACHINE-TOKEN-OBFUSCATED", category: "obfuscation",
            locator: `${consumer.file}:${i + 1} (${cls.id})`,
          });
        }
        const cre = globalized(consumer.pattern);
        let cm: RegExpExecArray | null;
        while ((cm = cre.exec(line)) !== null) {
          if (cm[1] === undefined) continue;
          if (consumer.excludeBefore !== undefined &&
              consumer.excludeBefore.test(line.slice(Math.max(0, cm.index - 24), cm.index))) {
            continue; // guard kontekstual per-match
          }
          anyMatch = true;
          const seen = norm(cm[1]);
          const labeled = isHistoryLabeledRow(line);
          if (seen === current) {
            if (!labeled) currentSeenActive = true; // vetëm current AKTIV plotëson kërkesën
          } else if (!labeled) {
            findings.push({
              profile: P, profileStatus: status, severity: "WARNING",
              ruleId: "DC-OLD-VALUE-DRIFT", category: "drift",
              locator: `${consumer.file}:${i + 1} (${cls.id})`,
            });
          }
        }
      }
      if (consumer.requirement === "REQUIRED_CURRENT" && !currentSeenActive) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "DC-CURRENT-VALUE-MISSING", category: "drift",
          locator: `${consumer.file} (${cls.id})${anyMatch ? " [vetëm stale/historike]" : ""}`,
        });
      }
    }
  }

  // ── 2) rendi invariant (të gjitha orderSurfaces) + PRANIA (chainRequired, §4) ─
  for (const file of surfaces.orderSurfaces) {
    const text = readDoc(file);
    const paragraphs = text.split(/\n\s*\n/);
    let lineNo = 1;
    for (const para of paragraphs) {
      const paraLines = para.split("\n");
      // §7 P-IV: rendi PER-CLAIM — bullets të pavarura nuk krahasohen mes tyre
      for (const claim of extractChainClaims(paraLines)) {
        for (const v of chainOrderViolations(claim)) {
          findings.push({
            profile: P, profileStatus: status, severity: "WARNING",
            ruleId: "DC-GATE-ORDER", category: "sequence",
            locator: `${file}:${lineNo} (${v.later} para ${v.earlier})`,
          });
        }
      }
      lineNo += paraLines.length + 1;
    }
  }
  for (const file of surfaces.chainRequiredSurfaces) {
    const evaln = evaluateChainPresence(readDoc(file));
    if (evaln.noActiveChain) {
      findings.push({
        profile: P, profileStatus: status, severity: "WARNING",
        ruleId: "DC-CHAIN-INCOMPLETE", category: "sequence",
        locator: `${file} (asnjë chain aktiv)`,
      });
    } else {
      for (const missing of evaln.missing) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "DC-CHAIN-INCOMPLETE", category: "sequence",
          locator: `${file} (mungon: ${missing})`,
        });
      }
    }
  }

  // ── 3) milestone/gate në tracker që mungon nga sipërfaqet e rendit ─────
  if (surfaces.trackerFile !== undefined) {
    const tracker = readDoc(surfaces.trackerFile);
    const ids = new Set<string>();
    for (const mm of tracker.matchAll(/\b(GOV-\d{3}|HG-[A-Z][A-Z0-9-]{2,})\b/g)) {
      const id = mm[1];
      if (id !== undefined) ids.add(id);
    }
    for (const id of [...ids].sort()) {
      const presentSomewhere = surfaces.milestoneSurfaces.some((f) => readDoc(f).includes(id));
      if (!presentSomewhere) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "DC-MILESTONE-MISSING", category: "governance",
          locator: `${surfaces.trackerFile} (${id})`,
        });
      }
    }
  }

  // ── 4) role/privacy vocabulary — canonical active surfaces (§9 P-IV):
  //      lista machine-readable; required missing → exit 2; optional if-present ─
  const roleTexts: Array<[string, string]> = [];
  for (const file of surfaces.roleRequiredSurfaces) roleTexts.push([file, readDoc(file)]);
  for (const file of surfaces.roleOptionalSurfaces) {
    const t = readDocIfPresent(file);
    if (t !== undefined) roleTexts.push([file, t]);
  }
  const roleValueFindings = (file: string, lineNo: number, value: string): void => {
    // P-V F4: projeksioni i markup-it zbatohet VETËM këtu (role-bearing value)
    const proj = projectRoleMarkup(value);
    if (proj.unexpectedMarkup) {
      findings.push({
        profile: P, profileStatus: status, severity: "REVIEW",
        ruleId: "DC-ROLE-MARKUP", category: "privacy-review",
        locator: `${file}:${lineNo}`,
      });
    }
    if (roleTokensUnknown(proj.text, surfaces.approvedRolePhrases) > 0) {
      findings.push({
        profile: P, profileStatus: status, severity: "REVIEW",
        ruleId: "DC-ROLE-VOCAB", category: "privacy-review",
        locator: `${file}:${lineNo}`,
      });
    }
    if (parentheticalIdentityCandidate(proj.text, surfaces.approvedRolePhrases)) {
      findings.push({
        profile: P, profileStatus: status, severity: "REVIEW",
        ruleId: "DC-ROLE-IDENTITY", category: "privacy-review",
        locator: `${file}:${lineNo}`,
      });
    }
  };
  for (const [file, text] of roleTexts) {
    const lines = text.split("\n");
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i] ?? "";
      // (a) rreshta tabele nën kolonë role-bearing (outer pipes opsionale, §4 P-IV)
      if (isTableRowCandidate(line) && !isSeparatorRow(line)) {
        const tctx = tableContextAt(text, i + 1);
        const col = tctx?.columnIndex(ROLE_HEADER_RE);
        if (tctx !== undefined && col !== undefined) {
          const row = parseTableRow(line);
          const cell = row.cells[col];
          if (!row.malformed && cell !== undefined) roleValueFindings(file, i + 1, cell);
          continue;
        }
      }
      // (b) etiketa metadata role-bearing (përfshirë "Human Gate:")
      const lm = ROLE_LABEL_RE.exec(line.trim());
      if (lm?.[2] !== undefined) roleValueFindings(file, i + 1, lm[2]);
    }
  }

  // ── 5) pretendime absolute — kontekstuale per-rresht ────────────────────
  const claimSurfaces = [...surfaces.orderSurfaces, ...(surfaces.trackerFile ? [surfaces.trackerFile] : [])];
  for (const file of claimSurfaces) {
    const text = readDoc(file);
    const lines = text.split("\n");
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i] ?? "";
      if (isHistoryLabeledRow(line)) continue;
      if (/closes[ -]on[ -]merge|mbyllet (?:me|në) merge/i.test(line) && !/(pa review|ndal|NUK|s'|\bjo\b|hiq)/i.test(line)) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "DC-CLOSES-ON-MERGE", category: "claims", locator: `${file}:${i + 1}`,
        });
      }
      if (/\b(exhaustiv\w*|exhaustive)\b/i.test(line) && !/(bounded|brenda|kufizuar|nuk|s'|\bjo\b)/i.test(line)) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "DC-ABSOLUTE-CLAIM", category: "claims", locator: `${file}:${i + 1}`,
        });
      }
    }
  }

  // ── 6) PR body kundrejt authority-ve (SCAN_IF_PRESENT; pa event → NOT_EVALUATED) ─
  if (event?.body === undefined) {
    evaluations.push({
      profile: P, profileStatus: status, status: "NOT_EVALUATED",
      check: "pr-body", reason: "missing PR event/body",
    });
  } else {
    const bodyLines = event.body.split("\n");
    for (const cls of surfaces.authorities) {
      const authorityText = readDocIfPresent(cls.authorityFile);
      if (authorityText === undefined) continue;
      if (anchorState(cls, authorityText) === "absent") continue; // §4A.7
      let current: string;
      try {
        current = extractAuthorityValue(authorityText, cls);
      } catch (err) {
        if (err instanceof AuthorityError) throw new ScanError(err.code);
        throw err;
      }
      const norm = cls.normalize ?? ((v: string) => v);
      // §6 P-IV: PR body kontrollohet kundrejt TË GJITHË consumers (pattern-e
      // të dallueshëm), jo vetëm consumers[0] — dedup për pattern identikë.
      const seenPatterns = new Set<string>();
      for (const consumer of cls.consumers) {
        const pkey = `${consumer.pattern.source}\\u0000${consumer.pattern.flags}`;
        if (seenPatterns.has(pkey)) continue;
        seenPatterns.add(pkey);
        for (let i = 0; i < bodyLines.length; i++) {
          const line = bodyLines[i] ?? "";
          if (isHistoryLabeledRow(line)) continue;
          if (hasObfuscatedMatch(line, consumer.pattern)) {
            findings.push({
              profile: P, profileStatus: status, severity: "WARNING",
              ruleId: "DC-MACHINE-TOKEN-OBFUSCATED", category: "obfuscation",
              locator: `pr-body:${i + 1} (${cls.id})`,
            });
          }
          const re2 = globalized(consumer.pattern);
          let cm: RegExpExecArray | null;
          while ((cm = re2.exec(line)) !== null) {
            if (cm[1] === undefined) continue;
            if (consumer.excludeBefore !== undefined &&
                consumer.excludeBefore.test(line.slice(Math.max(0, cm.index - 24), cm.index))) {
              continue;
            }
            if (norm(cm[1]) !== current) {
              findings.push({
                profile: P, profileStatus: status, severity: "WARNING",
                ruleId: "DC-PR-BODY-STALE", category: "pr-body",
                locator: `pr-body:${i + 1} (${cls.id})`,
              });
            }
          }
        }
      }
    }
  }

  evaluations.push({ profile: P, profileStatus: status, status: "EVALUATED" });
  return { findings, evaluations };
}
