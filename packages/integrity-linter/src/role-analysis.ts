/**
 * ROLE-ANALYSIS — AUTORITETI I VETËM për discovery + analizën e roleve.
 *
 * KONTRATA ME KATËR GJENDJE (correction pass fail-safe): çdo rajon
 * role-bearing klasifikohet në saktësisht NJË gjendje:
 *  - INACTIVE       — fenced/indented code: nuk skanohet si role evidence;
 *  - CLEAN          — sintaksë kanonike e kuptuar PLOTËSISHT dhe rol/vlerë e
 *                     allowlisted: e vetmja që kalon pa finding;
 *  - REVIEW         — entity/escape/markup/format-char/comment evidence/
 *                     parenthetical jo-i-allowlisted/identifikues numerik/
 *                     sintaksë e pasigurt → finding REVIEW i redaktuar;
 *  - INCOMPLETE_SCAN — markup i dëmtuar/strukturë e prishur → ScanError
 *                     (exit 2), kurrë silent.
 *
 * DISCOVERY FAIL-SAFE (para klasifikimit): kandidati role-bearing zbulohet
 * (1) nga label/header kanonik i drejtpërdrejtë, (2) nga një skeleton
 * KONSERVATOR detection-only (entity/escape/format-char të kufizuara — pa
 * dekodim universal) që reduktohet në label/header, (3) nga pjesëmarrja në
 * CILËNDO kolonë role-bearing, ose (4) nga comment/markup që merr pjesë në
 * formimin e një label-i. Sintaksë e pasigurt që MUND të maskojë label →
 * minimumi REVIEW. "Jo rajon roli" në heshtje lejohet vetëm kur analiza e
 * kufizuar e provon (asnjë rrugë kandidature nuk ndizet).
 *
 * KUFIJ TË DEKLARUAR: kjo është "restricted canonical syntax + fail-safe
 * discovery" — JO renderer i plotë HTML/Markdown dhe JO dekoder universal
 * sintaksash; gjithçka jashtë sintaksës kanonike të kuptuar merr REVIEW ose
 * INCOMPLETE_SCAN dhe human review mbetet backstop.
 *
 * doc-core konsumon VETËM rajonet e tipizuara të këtij moduli — asnjë vendim
 * paralel role/privacy jashtë tij. Output-i mban vetëm locator/kategori —
 * kurrë përmbajtje raw, identitet apo path.
 */

import { ScanError } from "./types.js";
import {
  containerOf, fencedCodeLineMask, isSeparatorRow, isTableRowCandidate, parseTableRow, tableContextAt,
} from "./markdown-table.js";
import { detectionSkeleton } from "./machine-token.js";

/* ────────────────────────── primitivat e markup-it ────────────────────────── */

/**
 * Neutralizon `<`/`>` brenda inline-code spans (delimiter me gjatësi ekzakte).
 * Offset-et janë UTF-16 code units (kontrata e locator-ëve; `split("")` e
 * ruan edhe pranë emoji-ve).
 */
export function projectInlineCodeSpans(line: string): string {
  const chars = line.split("");
  let at = 0;
  while (at < line.length) {
    if (line[at] !== "`") { at += 1; continue; }
    let run = 1;
    while (line[at + run] === "`") run += 1;
    let close = at + run;
    let found = -1;
    while (close < line.length) {
      if (line[close] !== "`") { close += 1; continue; }
      let closeRun = 1;
      while (line[close + closeRun] === "`") closeRun += 1;
      if (closeRun === run) { found = close; break; }
      close += closeRun;
    }
    if (found < 0) { at += run; continue; } // delimiter i pambyllur = tekst literal
    for (let i = at + run; i < found; i++) {
      if (chars[i] === "<" || chars[i] === ">") chars[i] = " ";
    }
    at = found + run;
  }
  return chars.join("");
}

export function blankPreservingNewlines(value: string): string {
  return value.replace(/[^\n]/g, " ");
}

/**
 * Blankim line-preserving i comments (projeksioni AKTIV — locator-ë të saktë).
 * Comment i pambyllur → fail-closed.
 */
export function stripHtmlCommentsPreservingNewlines(value: string): string {
  let out = "";
  let at = 0;
  while (at < value.length) {
    const open = value.indexOf("<!--", at);
    if (open < 0) return out + value.slice(at);
    out += value.slice(at, open);
    const close = value.indexOf("-->", open + 4);
    if (close < 0) {
      throw new ScanError("INCOMPLETE_SCAN:MARKDOWN_COMMENT_MALFORMED");
    }
    out += blankPreservingNewlines(value.slice(open, close + 3));
    at = close + 3;
  }
  return out;
}

/**
 * Blankim length-preserving i raw HTML tags me quotes në attributes
 * (projeksioni AKTIV). Tag i pambyllur → fail-closed.
 */
export function stripHtmlTagsPreservingLength(line: string): string {
  const chars = line.split("");
  let at = 0;
  while (at < line.length) {
    if (line[at] !== "<" || !/^<\/?[A-Za-z]/.test(line.slice(at))) {
      at += 1;
      continue;
    }
    let quote: "\"" | "'" | undefined;
    let end = -1;
    for (let i = at + 1; i < line.length; i++) {
      const ch = line[i];
      if (quote !== undefined) {
        if (ch === quote) quote = undefined;
        continue;
      }
      if (ch === "\"" || ch === "'") {
        quote = ch;
      } else if (ch === ">") {
        end = i;
        break;
      }
    }
    if (end < 0 || quote !== undefined) {
      throw new ScanError("INCOMPLETE_SCAN:MARKDOWN_HTML_MALFORMED");
    }
    for (let i = at; i <= end; i++) chars[i] = " ";
    at = end + 1;
  }
  return chars.join("");
}

/**
 * Projeksioni i vetëm për semantikën AKTIVE të governance Markdown-it
 * (i zhvendosur nga doc-core pa ndryshim sjelljeje — authority/chain/claims).
 * Comments/fenced code blankohen; label-i i dukshëm i linkut ruhet; reference
 * definitions (edhe title next-line) janë metadata; newline count ruhet.
 */
export function projectActiveMarkdown(text: string): string {
  const withoutInlineCode = text.split("\n").map(projectInlineCodeSpans).join("\n");
  const withoutComments = stripHtmlCommentsPreservingNewlines(withoutInlineCode);
  const lines = withoutComments.split("\n");
  const fenced = fencedCodeLineMask(lines);
  let referenceTitlePending = false;
  const projected = lines.map((raw, idx) => {
    const titleMayContinuePreviousDefinition = referenceTitlePending;
    referenceTitlePending = false;
    if (fenced[idx] === true || /^(?: {4}|\t)/.test(raw)) return " ".repeat(raw.length);
    const withoutHtmlTags = stripHtmlTagsPreservingLength(raw);
    const rendered = withoutHtmlTags
      .replace(/!?\[([^\]\n]+)\]\((?:\\.|[^)\n])*\)/g, "$1")
      .replace(/!?\[([^\]\n]+)\]\[[^\]\n]*\]/g, "$1");
    if (/^\s{0,3}\[[^\]\n]+\]:\s+\S/.test(rendered)) {
      referenceTitlePending = true;
      return " ".repeat(rendered.length);
    }
    if (titleMayContinuePreviousDefinition) {
      if (/^ {1,3}(?:"(?:\\.|[^"\n])*"|'(?:\\.|[^'\n])*'|\((?:\\.|[^)\n])*\))\s*$/.test(rendered)) {
        return " ".repeat(rendered.length);
      }
    }
    return rendered;
  });
  return projected.join("\n");
}

/**
 * Tags që renderer-i i paraqet si NDARJE përmbajtjeje: kolapsohen në NJË
 * hapësirë. Çdo tag tjetër trajtohet inline (HTML: unknown elements = inline)
 * dhe hiqet zero-width. Set i KURUAR — jo renderer i plotë; devijimet marrin
 * REVIEW nga rrugët e discovery-t.
 */
const SEPARATOR_TAG_NAMES = new Set([
  "br", "hr", "p", "div", "td", "th", "tr", "table", "thead", "tbody", "tfoot",
  "caption", "col", "colgroup", "li", "ul", "ol", "dl", "dt", "dd",
  "blockquote", "pre", "h1", "h2", "h3", "h4", "h5", "h6",
  "section", "article", "aside", "header", "footer", "nav", "main", "form",
  "fieldset", "address", "figure", "figcaption", "details", "summary",
]);

interface CollapsedFragment {
  visible: string;
  markupBearing: boolean;
}

/**
 * Kolapson tags me semantikën e renderer-it mbi një rresht/fragment.
 * strict=true → tag i pambyllur = fail-closed; strict=false (fragmente
 * qelizash/bodies) → `<` i pastrukturuar mbetet literal — pa fail-open,
 * sepse strict-i i rreshtave të plotë zbatohet nga analyzeRoleDocument.
 */
function collapseHtmlTags(
  line: string,
  opts: { strict: boolean; separators: "space" | "none" },
): CollapsedFragment {
  let out = "";
  let at = 0;
  let markupBearing = false;
  while (at < line.length) {
    if (line[at] !== "<" || !/^<\/?[A-Za-z]/.test(line.slice(at))) {
      out += line[at];
      at += 1;
      continue;
    }
    let quote: "\"" | "'" | undefined;
    let end = -1;
    for (let i = at + 1; i < line.length; i++) {
      const ch = line[i];
      if (quote !== undefined) {
        if (ch === quote) quote = undefined;
        continue;
      }
      if (ch === "\"" || ch === "'") {
        quote = ch;
      } else if (ch === ">") {
        end = i;
        break;
      }
    }
    if (end < 0 || quote !== undefined) {
      if (opts.strict) throw new ScanError("INCOMPLETE_SCAN:MARKDOWN_HTML_MALFORMED");
      out += line[at];
      at += 1;
      continue;
    }
    markupBearing = true;
    const name = (/^<\/?\s*([A-Za-z][A-Za-z0-9-]*)/.exec(line.slice(at, end + 1))?.[1] ?? "").toLowerCase();
    if (opts.separators === "space" && SEPARATOR_TAG_NAMES.has(name)) out += " ";
    at = end + 1;
  }
  return { visible: out, markupBearing };
}

/**
 * STATE MACHINE I VETËM (SIMETRIK) i aktivitetit fence/comment mbi RAW source
 * — correction pass cross-boundary. Gjendjet: NEUTRAL · IN_FENCE · IN_COMMENT.
 *  - Në IN_FENCE: `<!` + `--` / `--` + `>` janë LITERALE (s'hapin/mbyllin koment);
 *    rregullat e opener/closer-it pasqyrojnë kontratën ekzistuese të
 *    `fencedCodeLineMask` (backtick/tilde ≥3, indent total ≤3, info-string i
 *    backtick-fence pa backtick, closer i njëjti karakter/len ≥/bare/depth i
 *    njëjtë, dalja e kontejnerit e mbyll).
 *  - Në IN_COMMENT: fence markers janë LITERALE; mbyllja vetëm me `-->`;
 *    comment aktiv i pambyllur deri në EOF → fail-closed (exit 2).
 *  - Tranzicionet ndodhin VETËM nga gjendja aktive përkatëse — një comment/
 *    fence i mirëformuar s'mund të çaktivizojë tekst JASHTË tij.
 *  - Fence e PAMBYLLUR në EOF shënohet si AMBIGE — thirrësi e kthen përmbajtjen
 *    role-bearing brenda saj në REVIEW, kurrë në INACTIVE të heshtur.
 * Comment-et një-rreshtore hiqen zero-width nga kanali i dukshëm; pjesët
 * shumë-rreshtore blankohen line-preserving (kontrata ekzistuese e locator-ëve).
 * Kanali `spaced` (koment → NJË hapësirë) shërben vetëm për heuristikën e
 * formimit të label-it.
 */
interface RoleActivityLine {
  /** Brenda një fence (opener/content/closer) — INACTIVE kur fence mbyllet. */
  fence: boolean;
  /** Pjesë e një fence të PAMBYLLUR në EOF — ambige, kurrë INACTIVE i heshtur. */
  unclosedFence: boolean;
  /** Komentet reale të hequra (zero-width / blank për pjesët shumë-rreshtore). */
  visible: string;
  /** Si `visible`, por komenti një-rreshtor → një hapësirë (formation heuristic). */
  spaced: string;
  hadComment: boolean;
}

interface RoleActivityScan {
  lines: RoleActivityLine[];
  /** VETËM komentet e hapura në NEUTRAL — markers brenda fence janë literale. */
  comments: Array<{ body: string; line: number; prefix: string }>;
}

function scanRoleActivity(neutralLines: string[]): RoleActivityScan {
  const out: RoleActivityLine[] = [];
  const comments: Array<{ body: string; line: number; prefix: string }> = [];
  let fence: { depth: number; ch: string; len: number; openerIdx: number } | undefined;
  let comment: { startLine: number; prefix: string; body: string } | undefined;

  // Skanon një rresht (ose bishtin e tij pas mbylljes së një komenti) në
  // NEUTRAL: komentet një-rreshtore mblidhen; opener i pambyllur kalon në
  // IN_COMMENT dhe blankon pjesën e mbetur të rreshtit.
  const scanNeutralText = (line: string, lineNo: number, from: number): { vis: string; spaced: string; had: boolean } => {
    let vis = "";
    let spaced = "";
    let had = false;
    let at = from;
    for (;;) {
      const open = line.indexOf("<!--", at);
      if (open < 0) {
        vis += line.slice(at);
        spaced += line.slice(at);
        break;
      }
      had = true;
      vis += line.slice(at, open);
      spaced += line.slice(at, open);
      const close = line.indexOf("-->", open + 4);
      if (close < 0) {
        comment = { startLine: lineNo, prefix: line.slice(0, open), body: line.slice(open + 4) + "\n" };
        vis += " ".repeat(line.length - open);
        spaced += " ".repeat(line.length - open);
        break;
      }
      comments.push({ body: line.slice(open + 4, close), line: lineNo, prefix: line.slice(0, open) });
      spaced += " ";
      at = close + 3;
    }
    return { vis, spaced, had };
  };

  for (let i = 0; i < neutralLines.length; i++) {
    const line = neutralLines[i] ?? "";

    if (comment !== undefined) {
      // IN_COMMENT: fence markers literale; vetëm `-->` mbyll.
      const close = line.indexOf("-->");
      if (close < 0) {
        comment.body += line + "\n";
        out.push({ fence: false, unclosedFence: false, visible: blankPreservingNewlines(line), spaced: blankPreservingNewlines(line), hadComment: true });
        continue;
      }
      comment.body += line.slice(0, close);
      comments.push({ body: comment.body, line: comment.startLine, prefix: comment.prefix });
      comment = undefined;
      const tail = scanNeutralText(line, i + 1, close + 3);
      out.push({
        fence: false, unclosedFence: false,
        visible: " ".repeat(close + 3) + tail.vis,
        spaced: " ".repeat(close + 3) + tail.spaced,
        hadComment: true,
      });
      continue;
    }

    if (fence !== undefined) {
      const { depth, content } = containerOf(line);
      if (depth < fence.depth) {
        fence = undefined; // dalje kontejneri — rreshti riprocesohet si NEUTRAL
      } else {
        const m = /^( *)(`{3,}|~{3,})/.exec(content);
        const isCloser = m?.[1] !== undefined && m[2] !== undefined
          && (m[1].length + containerOf(line).indent) <= 3
          && (m[2][0] as string) === fence.ch && m[2].length >= fence.len
          && content.trim() === m[2] && depth === fence.depth;
        out.push({ fence: true, unclosedFence: false, visible: "", spaced: "", hadComment: false });
        if (isCloser) fence = undefined;
        continue;
      }
    }

    // NEUTRAL: fillimisht opener i fence-s (konstrukt i fillimrreshtit).
    const { depth, content, indent } = containerOf(line);
    const m = /^( *)(`{3,}|~{3,})/.exec(content);
    if (m?.[1] !== undefined && m[2] !== undefined && indent + m[1].length <= 3) {
      const ch = m[2][0] as string;
      const info = content.slice(m[1].length + m[2].length);
      if (!(ch === "`" && info.includes("`"))) {
        fence = { depth, ch, len: m[2].length, openerIdx: i };
        out.push({ fence: true, unclosedFence: false, visible: "", spaced: "", hadComment: false });
        continue;
      }
    }
    const p = scanNeutralText(line, i + 1, 0);
    out.push({ fence: false, unclosedFence: false, visible: p.vis, spaced: p.spaced, hadComment: p.had });
  }

  if (comment !== undefined) {
    // comment aktiv i pambyllur deri në EOF → fail-closed, kurrë silent.
    throw new ScanError("INCOMPLETE_SCAN:MARKDOWN_COMMENT_MALFORMED");
  }
  if (fence !== undefined) {
    for (let j = fence.openerIdx; j < out.length; j++) {
      const l = out[j];
      if (l !== undefined) l.unclosedFence = true;
    }
  }
  return { lines: out, comments };
}

/** Nxjerrja e comments me lokacion mbi tekstin RAW. */
export function htmlCommentsWithLocation(text: string): Array<{ body: string; line: number; prefix: string }> {
  const out: Array<{ body: string; line: number; prefix: string }> = [];
  for (const match of text.matchAll(/<!--([\s\S]*?)-->/g)) {
    const at = match.index ?? 0;
    const before = text.slice(0, at);
    const line = before.split("\n").length;
    const lastBreak = before.lastIndexOf("\n");
    out.push({
      body: match[1] ?? "",
      line,
      prefix: before.slice(lastBreak + 1),
    });
  }
  return out;
}

export function htmlCommentBodies(value: string): string[] {
  return [...value.matchAll(/<!--([\s\S]*?)-->/g)].map((m) => m[1] ?? "");
}

/**
 * Kolapsim i një FRAGMENTI (qelizë header-i, prefix/body comment-i) në tekst
 * të dukshëm — lenient ndaj strukturës së prerë nga copëzimi; strict-i i
 * rreshtave të plotë zbatohet nga analyzeRoleDocument mbi të njëjtin dokument.
 */
export function visibleRoleFragment(fragment: string): CollapsedFragment {
  const neutral = projectInlineCodeSpans(fragment);
  let stripped = "";
  let markupBearing = false;
  let at = 0;
  while (at < neutral.length) {
    const open = neutral.indexOf("<!--", at);
    if (open < 0) { stripped += neutral.slice(at); break; }
    const close = neutral.indexOf("-->", open + 4);
    if (close < 0) { stripped += neutral.slice(at); break; }
    stripped += neutral.slice(at, open);
    markupBearing = true;
    at = close + 3;
  }
  const collapsed = collapseHtmlTags(stripped, { strict: false, separators: "space" });
  return { visible: collapsed.visible, markupBearing: markupBearing || collapsed.markupBearing };
}

/* ─────────────────── vokabulari dhe projeksionet e rolit ─────────────────── */

/** Header-at e kolonave role-bearing — barazi e plotë e emrit të kolonës. */
export const ROLE_HEADER_RE = /^(Owner( \/ burimi)?|Pronari?|Kush|Përgjegjës\w*|Approver|Human Gate)$/i;
/** Etiketat metadata role-bearing — "Human Gate" njihet si label. */
export const ROLE_LABEL_RE = /^(Pronar(?:i)?(?: i dokumentit)?|Owner|Përgjegjës\w*|Approver|Human Gate)\s*:\s*(.+)$/i;

export function projectRoleKey(value: string): string {
  let unwrapped = value;
  for (;;) {
    const next = unwrapped.replace(
      /^\s{0,3}(?:>\s*|(?:[-+*]|\d+[.)])\s+|\[[ xX]\]\s+|#{1,6}\s+)/,
      "",
    );
    if (next === unwrapped) break;
    unwrapped = next;
  }
  return unwrapped
    .replace(/<\/?[A-Za-z][^>\n]*>/g, " ")
    .replace(/!?\[([^\]\n]+)\]\((?:\\.|[^)\n])*\)/g, "$1")
    .replace(/!?\[([^\]\n]+)\]\[[^\]\n]*\]/g, "$1")
    .replace(/<!--|-->/g, " ")
    .replace(/[`*_]/g, "")
    .replace(/\s+/g, " ")
    .trim();
}

/**
 * P-V F4 — projeksion identiteti VETËM për role-bearing values: <br>/HTML
 * comments/whitespace entities → separator; markup/entity i papritur i mbetur
 * → sinjal REVIEW. Asnjë content në output.
 */
export function projectRoleMarkup(value: string): { text: string; unexpectedMarkup: boolean } {
  const text = value
    .replace(/<br\s*\/?\s*>/gi, " ")
    .replace(/<!--[\s\S]*?-->/g, " ")
    .replace(/&(?:nbsp|ensp|emsp|thinsp|zwsp|#0*160|#0*8194|#0*8195|#0*8201|#0*8203|#x0*A0);/gi, " ");
  const unexpectedMarkup = /<[A-Za-z!/]|&[A-Za-z#][A-Za-z0-9]{1,10};/.test(text);
  return { text, unexpectedMarkup };
}

/**
 * Tokenizon një vlerë role-bearing → numri i tokenëve JO të aprovuar.
 * Konsistencë e frazës së mbyllur: NJË pikë fundore e tokenit normalizohet
 * ("iCode L.L.C." dhe "iCode L.L.C" trajtohen NJËSOJ — kurrë split).
 */
export function roleTokensUnknown(cellRaw: string, approvedPhrases: string[]): number {
  const cell = cellRaw
    .replace(/\([^)]*\)/g, " ")           // parentetikat marrin disposition më vete
    .replace(/[`*]/g, "")
    .trim();
  if (cell === "" || cell === "—" || cell === "-") return 0;
  const tokens = cell
    .split(/[/,+;·]|→|\bdhe\b/i)
    .map((t) => t.trim().toLowerCase()
      .replace(/\s+nëse duhet$/, "")
      .replace(/\s+sipas rreshtit$/, "")
      .replace(/\s+/g, " ")
      .replace(/\.$/, ""))
    .filter((t) => t !== "" && t !== "—" && t !== "-");
  const approved = new Set(approvedPhrases.map((p) => p.toLowerCase().replace(/\.$/, "")));
  let unknown = 0;
  for (const token of tokens) {
    if (!approved.has(token)) unknown += 1; // FRAZË E PLOTË — jo substring
  }
  return unknown;
}

/** Fjalë funksionale të annotation-eve sintaktike — JO pjesë identiteti (e pandryshuar). */
export const FUNCTIONAL_ANNOTATION_WORDS = new Set([
  "aprovon", "miraton", "çdo", "ndryshim", "ndryshimet", "ndryshimin", "dokument",
  "dokumentit", "dokumenti", "rresht", "rreshtit", "nëse", "kur", "duhet", "sipas",
  "vetëm", "pas", "para", "për", "nga", "dhe", "ose", "me", "pa", "të", "e", "i",
  "u", "në", "që", "këtij", "kësaj", "atij", "asaj", "review", "approval",
]);

export type ParentheticalDisposition =
  | "APPROVED_ANNOTATION"
  | "IDENTITY_REVIEW"
  | "AMBIGUOUS_REVIEW";

export interface ParentheticalAnalysis {
  disposition: ParentheticalDisposition;
  residualTerms: number;
}

/**
 * Disposition EKSPLICIT për çdo parenthetical (fail-safe): jo-bosh kalon
 * CLEAN VETËM plotësisht i allowlisted. Lidhëzat funksionale nuk i ndajnë
 * termat residualë (≥2 → REVIEW edhe me "dhe"/"ose"); term i vetëm i
 * paaprovuar → REVIEW; përmbajtje pa fjalë por me numra/simbole (p.sh.
 * identifikues, telefon) → REVIEW; vetëm placeholder dash → e barasvlershme
 * me bosh. E paqarta KURRË silent approval.
 */
export function classifyParenthetical(inner: string, approvedPhrases: string[]): ParentheticalAnalysis {
  const approved = new Set(approvedPhrases.map((p) => p.toLowerCase()));
  const cleaned = inner.replace(/[`*]/g, "").trim();
  if (cleaned === "" || approved.has(cleaned.toLowerCase())) {
    return { disposition: "APPROVED_ANNOTATION", residualTerms: 0 };
  }
  const words = [...cleaned.matchAll(/\p{L}[\p{L}'’-]*/gu)].map((w) => w[0]);
  if (words.length === 0) {
    // pa asnjë fjalë: vetëm placeholder dash/whitespace ≈ bosh; çdo gjë
    // tjetër (numra, simbole, formate telefoni) = identifikues i mundshëm.
    const nonPlaceholder = cleaned.replace(/[—–\-·\s]/g, "");
    if (nonPlaceholder === "") return { disposition: "APPROVED_ANNOTATION", residualTerms: 0 };
    return { disposition: "AMBIGUOUS_REVIEW", residualTerms: 1 };
  }
  const residual: string[] = [];
  let run: string[] = [];
  const closeRun = (): void => {
    if (run.length === 0) return;
    if (!approved.has(run.join(" ").toLowerCase())) residual.push(...run);
    run = [];
  };
  for (const w of words) {
    const lw = w.toLowerCase();
    if (FUNCTIONAL_ANNOTATION_WORDS.has(lw) || approved.has(lw)) {
      closeRun();
      continue;
    }
    run.push(w);
  }
  closeRun();
  if (residual.length === 0) {
    // fjalët mbulohen — por numra shtesë jashtë një fraze të aprovuar EKZAKTE
    // nuk kalojnë në heshtje (fail-safe: identifikues i mundshëm).
    if (/\p{N}/u.test(cleaned)) return { disposition: "AMBIGUOUS_REVIEW", residualTerms: 1 };
    return { disposition: "APPROVED_ANNOTATION", residualTerms: 0 };
  }
  if (residual.length >= 2) return { disposition: "IDENTITY_REVIEW", residualTerms: residual.length };
  const single = residual[0] ?? "";
  if (single.length >= 4 && /^\p{Lu}/u.test(single)) {
    return { disposition: "IDENTITY_REVIEW", residualTerms: 1 };
  }
  return { disposition: "AMBIGUOUS_REVIEW", residualTerms: 1 };
}

export function classifyParentheticals(value: string, approvedPhrases: string[]): ParentheticalAnalysis[] {
  return [...value.matchAll(/\(([^)]*)\)/g)].map((m) => classifyParenthetical(m[1] ?? "", approvedPhrases));
}

/** Wrapper i kontratës ekzistuese: kandidat = çdo disposition ≠ APPROVED. */
export function parentheticalIdentityCandidate(cellRaw: string, approvedPhrases: string[]): boolean {
  return classifyParentheticals(cellRaw, approvedPhrases)
    .some((p) => p.disposition !== "APPROVED_ANNOTATION");
}

/* ─────────────────────── discovery skeleton (fail-safe) ─────────────────────── */

const NAMED_ENTITY_FOR_DISCOVERY: Record<string, string> = {
  amp: "&", lt: "<", gt: ">", quot: "\"", apos: "'", colon: ":", nbsp: " ",
  sol: "/", equals: "=", period: ".", comma: ",",
};

function safeCodePointForDiscovery(cp: number): string {
  if (!Number.isFinite(cp) || cp <= 0 || cp > 0x10ffff) return "�";
  if (cp < 0x20 || (cp >= 0x7f && cp <= 0x9f)) return "�";
  try {
    return String.fromCodePoint(cp);
  } catch {
    return "�";
  }
}

/**
 * Skeleton KONSERVATOR vetëm-për-discovery: format-chars/konfuzabla (skeleton
 * i protected tokens), entity numerike dec/hex, një set i VOGËL entity-sh të
 * emërtuara dhe CommonMark backslash-escape. NUK është dekoder universal —
 * përdoret vetëm për të zbuluar që një label/header MUND të jetë maskuar;
 * rezultati i zbulimit është gjithmonë REVIEW, kurrë semantikë e besuar.
 */
export function discoverySkeleton(value: string): string {
  let s = detectionSkeleton(value);
  s = s.replace(/&#(\d{1,7});/g, (_m, d: string) => safeCodePointForDiscovery(Number.parseInt(d, 10)));
  s = s.replace(/&#[xX]([0-9A-Fa-f]{1,6});/g, (_m, h: string) => safeCodePointForDiscovery(Number.parseInt(h, 16)));
  s = s.replace(/&([A-Za-z][A-Za-z0-9]{1,30});/g,
    (m, name: string) => NAMED_ENTITY_FOR_DISCOVERY[name.toLowerCase()] ?? m);
  s = s.replace(/\\([!-/:-@[-`{-~])/g, "$1");
  return s;
}

/* ───────────────────────── modeli i rajoneve me gjendje ───────────────────────── */

export type RoleRegionState = "INACTIVE" | "CLEAN" | "REVIEW";
export type RoleReasonCode = "ROLE_MARKUP" | "ROLE_VOCAB" | "ROLE_IDENTITY";

export interface RoleRegion {
  /** Deterministik, pa tekst sensitiv: `kind:line:col`. */
  regionId: string;
  kind: "label" | "label-candidate" | "table-header" | "table-cell" | "comment";
  /** 1-based; locator-i i finding-ut. */
  line: number;
  state: RoleRegionState;
  /** Një hyrje për çdo finding REVIEW që duhet emetuar (multiplicitet i ruajtur). */
  reasons: RoleReasonCode[];
  /** true kur comment-et morën pjesë në formimin e label-it — evidencë e të NJËJTIT rajon. */
  commentEvidence: boolean;
}

export interface RoleLineView {
  visible: string;
  /** Kolapsim total zero-width — VETËM sinjal markup-i, jo semantikë. */
  flattened: string;
  markupBearing: boolean;
  /** true = fenced/indented code (INACTIVE brenda kontratës). */
  inactive: boolean;
}

export interface RoleDocumentAnalysis {
  lines: RoleLineView[];
  comments: Array<{ body: string; line: number; prefix: string }>;
  regions: RoleRegion[];
}

/** Arsyet e një vlere role-bearing — parity me kontratën e mëparshme + fail-safe. */
function valueReasons(value: string, approvedPhrases: string[]): RoleReasonCode[] {
  const reasons: RoleReasonCode[] = [];
  const candidates = [value, ...htmlCommentBodies(value)];
  for (const candidate of candidates) {
    const proj = projectRoleMarkup(candidate);
    if (proj.unexpectedMarkup) reasons.push("ROLE_MARKUP");
    if (roleTokensUnknown(proj.text, approvedPhrases) > 0) reasons.push("ROLE_VOCAB");
    if (parentheticalIdentityCandidate(proj.text, approvedPhrases)) reasons.push("ROLE_IDENTITY");
  }
  return reasons;
}

/**
 * ANALIZA E TIPIZUAR e një dokumenti role-bearing — pipeline i vetëm:
 * RAW → activity mask → discovery konservator → rajone me gjendje.
 * Po aq strict sa projeksioni aktiv (të njëjtat fail-closed mbi të njëjtat
 * inpute). Çdo rajon merr saktësisht NJË gjendje; dedup me region-id.
 */
export function analyzeRoleDocument(text: string, approvedPhrases: string[] = []): RoleDocumentAnalysis {
  const rawLines = text.split("\n");
  const neutralLines = rawLines.map(projectInlineCodeSpans);
  // STATE MACHINE I VETËM fence/comment mbi RAW (simetrik, fail-safe):
  // markers në gjendjen tjetër janë LITERALE; comment i pambyllur → exit 2;
  // fence e pambyllur në EOF → ambige (REVIEW mbi role-bearing, jo INACTIVE).
  const activity = scanRoleActivity(neutralLines);
  const comments = activity.comments;
  const views: RoleLineView[] = rawLines.map((_raw, idx) => {
    const act = activity.lines[idx] ?? { fence: false, unclosedFence: false, visible: "", spaced: "", hadComment: false };
    // Indent-i i indented-code gjykohet mbi rreshtin RAW (semantika CommonMark)
    // — blankimi i comment-eve nuk duhet të fabrikojë "code" nga hapësirat.
    if (act.fence || /^(?: {4}|\t)/.test(rawLines[idx] ?? "")) {
      return { visible: "", flattened: "", markupBearing: false, inactive: true };
    }
    const rendered = collapseHtmlTags(act.visible, { strict: true, separators: "space" });
    const flattened = collapseHtmlTags(act.visible, { strict: false, separators: "none" });
    return {
      visible: rendered.visible,
      flattened: flattened.visible,
      markupBearing: rendered.markupBearing || act.hadComment,
      inactive: false,
    };
  });
  // Snapshot për strukturën e tabelave — i qeverisur nga i NJËJTI state
  // machine (rreshtat joaktivë bosh); zero vendim paralel fence/comment.
  const tableSnapshot = views.map((v) => (v.inactive ? "" : v.visible)).join("\n");
  const regions: RoleRegion[] = [];
  const seenRegionIds = new Set<string>();
  const pushRegion = (region: RoleRegion): void => {
    if (seenRegionIds.has(region.regionId)) return; // dedup deterministik me id
    seenRegionIds.add(region.regionId);
    regions.push(region);
  };
  const stateFor = (reasons: RoleReasonCode[]): RoleRegionState =>
    reasons.length > 0 ? "REVIEW" : "CLEAN";

  for (let i = 0; i < rawLines.length; i++) {
    const raw = rawLines[i] ?? "";
    const view = views[i] ?? { visible: "", flattened: "", markupBearing: false, inactive: true };
    const act = activity.lines[i] ?? { fence: false, unclosedFence: false, visible: "", spaced: "", hadComment: false };

    if (view.inactive) {
      const labelLike = ROLE_LABEL_RE.test(projectRoleKey(raw));
      const roleHeaderRowLike = isTableRowCandidate(raw)
        && parseTableRow(raw).cells.some((c) => ROLE_HEADER_RE.test(projectRoleKey(c)));
      if (act.unclosedFence && (labelLike || roleHeaderRowLike)) {
        // RREGULLI FAIL-SAFE: fence e PAMBYLLUR në EOF është ambige —
        // përmbajtja role-bearing brenda saj NUK bëhet INACTIVE e heshtur;
        // minimumi REVIEW për shqyrtim njerëzor.
        pushRegion({
          regionId: `label-candidate:${i + 1}:0`, kind: "label-candidate", line: i + 1,
          state: "REVIEW", reasons: ["ROLE_MARKUP"], commentEvidence: false,
        });
        continue;
      }
      // INACTIVE: vetëm brenda gjendjeje joaktive të QARTË (fence e mbyllur /
      // indented code); rajoni emetohet me gjendje eksplicite kur ka formë
      // label-i — kurrë finding.
      if (labelLike) {
        pushRegion({
          regionId: `label:${i + 1}:0`, kind: "label", line: i + 1,
          state: "INACTIVE", reasons: [], commentEvidence: false,
        });
      }
      continue;
    }

    // (a) tabelat: TË GJITHA kolonat role-bearing (jo vetëm e para) — struktura
    // nga snapshot-i i qeverisur prej state machine-it.
    if (isTableRowCandidate(view.visible) && !isSeparatorRow(view.visible)) {
      const tctx = tableContextAt(tableSnapshot, i + 1);
      const rawCtx = tableContextAt(text, i + 1);
      if (tctx !== undefined) {
        const roleCols: Array<{ col: number; uncertain: boolean }> = [];
        for (let c = 0; c < tctx.headerCells.length; c++) {
          const rawHeader = rawCtx?.headerCells[c] ?? tctx.headerCells[c] ?? "";
          const fragment = visibleRoleFragment(rawHeader);
          if (ROLE_HEADER_RE.test(projectRoleKey(fragment.visible))
            || ROLE_HEADER_RE.test(projectRoleKey(tctx.headerCells[c] ?? ""))) {
            roleCols.push({ col: c, uncertain: false });
            continue;
          }
          // discovery fail-safe: header i maskuar me entity/escape/format-char
          const skeletonKey = projectRoleKey(discoverySkeleton(fragment.visible));
          if (skeletonKey !== projectRoleKey(fragment.visible) && ROLE_HEADER_RE.test(skeletonKey)) {
            roleCols.push({ col: c, uncertain: true });
          }
        }
        if (roleCols.length > 0) {
          const firstDataRow = isSeparatorRow(views[i - 1]?.visible ?? "");
          const row = parseTableRow(raw);
          for (const { col, uncertain } of roleCols) {
            if (firstDataRow) {
              const rawHeader = rawCtx?.headerCells[col] ?? tctx.headerCells[col] ?? "";
              const headerReasons: RoleReasonCode[] =
                uncertain || projectRoleMarkup(rawHeader).unexpectedMarkup ? ["ROLE_MARKUP"] : [];
              pushRegion({
                regionId: `table-header:${i + 1}:${col}`, kind: "table-header", line: i + 1,
                state: stateFor(headerReasons), reasons: headerReasons, commentEvidence: false,
              });
            }
            const cell = row.cells[col];
            if (!row.malformed && cell !== undefined) {
              const reasons = valueReasons(cell, approvedPhrases);
              pushRegion({
                regionId: `table-cell:${i + 1}:${col}`, kind: "table-cell", line: i + 1,
                state: stateFor(reasons), reasons, commentEvidence: false,
              });
            }
          }
          continue;
        }
      }
    }

    // (b) label metadata mbi tekstin e DUKSHËM.
    const visibleKey = projectRoleKey(view.visible);
    const lm = ROLE_LABEL_RE.exec(visibleKey);
    if (lm?.[2] !== undefined) {
      const reasons: RoleReasonCode[] = [];
      if (projectRoleMarkup(raw).unexpectedMarkup) reasons.push("ROLE_MARKUP");
      reasons.push(...valueReasons(lm[2], approvedPhrases));
      // comment-et që MORËN PJESË në formimin e label-it (label i dukshëm
      // ekziston, por me komentet → hapësirë + tags → hapësirë s'njihet) →
      // bodies janë evidencë raw e TË NJËJTIT rajon, kurrë governance aktive.
      let commentEvidence = false;
      const spacedKey = projectRoleKey(stripHtmlTagsPreservingLength(act.spaced));
      if (!ROLE_LABEL_RE.test(spacedKey)) {
        for (const cm of comments) {
          if (cm.line !== i + 1) continue;
          commentEvidence = true;
          reasons.push(...valueReasons(cm.body, approvedPhrases));
        }
        if (commentEvidence && reasons.length === 0) reasons.push("ROLE_MARKUP");
      }
      pushRegion({
        regionId: `label:${i + 1}:0`, kind: "label", line: i + 1,
        state: stateFor(reasons), reasons, commentEvidence,
      });
      continue;
    }
    // discovery fail-safe: label i maskuar me entity/escape/format-char →
    // minimumi REVIEW (pa dekodim semantik të vlerës).
    const skeletonKey = projectRoleKey(discoverySkeleton(view.visible));
    if (skeletonKey !== visibleKey && ROLE_LABEL_RE.test(skeletonKey)) {
      pushRegion({
        regionId: `label-candidate:${i + 1}:0`, kind: "label-candidate", line: i + 1,
        state: "REVIEW", reasons: ["ROLE_MARKUP"], commentEvidence: false,
      });
      continue;
    }
    if (view.markupBearing && ROLE_LABEL_RE.test(projectRoleKey(view.flattened))) {
      // label vetëm nën kolapsim total (p.sh. "Own<br>er:") — markup-bearing
      // → REVIEW i pavarur nga suksesi i label-regex-it.
      pushRegion({
        regionId: `label-candidate:${i + 1}:0`, kind: "label-candidate", line: i + 1,
        state: "REVIEW", reasons: ["ROLE_MARKUP"], commentEvidence: false,
      });
    }
  }

  // (c) comments si evidencë privacy — kurrë governance aktive. Comment
  // brenda code = INACTIVE (nuk raportohet si role evidence).
  for (let ci = 0; ci < comments.length; ci++) {
    const comment = comments[ci];
    if (comment === undefined) continue;
    const startView = views[comment.line - 1];
    if (startView?.inactive === true) {
      pushRegion({
        regionId: `comment:${comment.line}:${ci}`, kind: "comment", line: comment.line,
        state: "INACTIVE", reasons: [], commentEvidence: true,
      });
      continue;
    }
    const attached = ROLE_LABEL_RE.exec(projectRoleKey(visibleRoleFragment(comment.prefix).visible));
    if (attached?.[2] !== undefined) {
      const reasons = valueReasons(comment.body, approvedPhrases);
      pushRegion({
        regionId: `comment:${comment.line}:${ci}`, kind: "comment", line: comment.line,
        state: stateFor(reasons), reasons, commentEvidence: true,
      });
      continue;
    }
    const hiddenLabel = ROLE_LABEL_RE.exec(projectRoleKey(visibleRoleFragment(comment.body).visible));
    if (hiddenLabel?.[2] !== undefined) {
      const reasons = valueReasons(hiddenLabel[2], approvedPhrases);
      pushRegion({
        regionId: `comment:${comment.line}:${ci}`, kind: "comment", line: comment.line,
        state: stateFor(reasons), reasons, commentEvidence: true,
      });
    }
  }

  return { lines: views, comments, regions };
}
