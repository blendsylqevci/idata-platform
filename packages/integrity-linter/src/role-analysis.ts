/**
 * ROLE-ANALYSIS — reprezentimi I VETËM i tipizuar për analizën e roleve
 * (refactor i Correction Pass — WA-001/WA-002 + siblings; JO parser i ri
 * platforme: i njëjti quote-aware tag parser, i njëjti comment scanner dhe
 * i njëjti fence mask si projeksioni aktiv, të bashkuara në një strukturë).
 *
 * Dy kanale të NDARA qartë:
 *  1. `visible` — teksti semantik i RENDERUAR: markup zero-content hiqet PA
 *     krijuar word boundary artificial ("Own<span></span>er" → "Owner"),
 *     ndërsa markup që renderer-i e paraqet si ndarje (br/td/li/p/…) bëhet
 *     një hapësirë ("Own<br>er" NUK bëhet "Owner"). Vetëm ky kanal ushqen
 *     njohjen e role-label/role-header.
 *  2. `comments` — bodies RAW të HTML comments si evidencë privacy. Comments
 *     mbeten JO-AKTIVE për governance semantics: përmbajtja e tyre nuk mund
 *     të plotësojë role-label/authority/chain aktive; ato skanohen veçmas.
 *
 * Malformed markup (tag/comment i pambyllur) → fail-closed, të njëjtat kode
 * `INCOMPLETE_SCAN:MARKDOWN_*` si projeksioni aktiv — kurrë silent.
 */

import { ScanError } from "./types.js";
import { fencedCodeLineMask } from "./markdown-table.js";

/**
 * Neutralizon `<`/`>` brenda inline-code spans (delimiter me gjatësi ekzakte)
 * — i zhvendosur nga doc-core pa ndryshim sjelljeje. Offset-et janë UTF-16
 * code units (kontrata e locator-ëve; `split("")` e ruan edhe pranë emoji-ve).
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
 * Blankim line-preserving i comments (projeksioni AKTIV — locator-ët e saktë).
 * I zhvendosur nga doc-core pa ndryshim sjelljeje; comment i pambyllur →
 * fail-closed (do të fshihte deri në EOF nga semantika aktive).
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
 * Blankim length-preserving i raw HTML tags me quotes në attributes (projeksioni
 * AKTIV). I zhvendosur nga doc-core pa ndryshim sjelljeje; tag i pambyllur →
 * fail-closed.
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
 * Tags që renderer-i i paraqet si NDARJE përmbajtjeje (void/block/table/list):
 * kolapsohen në NJË hapësirë — "Own<br>er" nuk bashkohet. Çdo tag tjetër
 * (i njohur ose jo) trajtohet inline si në HTML (unknown elements = inline)
 * dhe hiqet zero-width — "Own<span></span>er" → "Owner".
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
 * Kolapson tags me semantikën e renderer-it mbi një RRESHT/fragment.
 * strict=true → tag i pambyllur = fail-closed (i njëjti kod si projeksioni);
 * strict=false (fragmente qelizash/bodies) → `<` i pastrukturuar mbetet tekst
 * literal — pa fail-open, sepse skanimi strict i rreshtave të plotë të po
 * atij dokumenti është kryer tashmë nga analyzeRoleDocument.
 * separators="space" → tag ndarës = një hapësirë; "none" → kolapsim TOTAL
 * zero-width (përdoret VETËM si sinjal markup-i për DC-ROLE-MARKUP, kurrë
 * si semantikë renderer-i).
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
 * Kolapsim i comments për kanalin e DUKSHËM: comment një-rreshtor prodhon
 * ZERO tekst në rendering ("Own<!-- x -->er" → "Owner"); comment
 * shumë-rreshtor blankohet line-preserving (kontrata ekzistuese e locator-ëve
 * — bashkimi i fjalëve NDËRMJET rreshtave mbetet jashtë kontratës dhe
 * dokumentohet si kufi). Unclosed → fail-closed.
 */
function collapseCommentsForVisible(
  text: string,
  opts: { strict: boolean },
): { visible: string; markupLines: Set<number> } {
  let out = "";
  let at = 0;
  const markupLines = new Set<number>();
  while (at < text.length) {
    const open = text.indexOf("<!--", at);
    if (open < 0) {
      out += text.slice(at);
      break;
    }
    out += text.slice(at, open);
    const close = text.indexOf("-->", open + 4);
    if (close < 0) {
      if (opts.strict) throw new ScanError("INCOMPLETE_SCAN:MARKDOWN_COMMENT_MALFORMED");
      out += text.slice(open);
      break;
    }
    const segment = text.slice(open, close + 3);
    const startLine = text.slice(0, open).split("\n").length;
    const segmentLines = segment.split("\n").length;
    for (let l = startLine; l < startLine + segmentLines; l++) markupLines.add(l);
    out += segment.includes("\n") ? blankPreservingNewlines(segment) : "";
    at = close + 3;
  }
  return { visible: out, markupLines };
}

export interface RoleLineView {
  /** Teksti i dukshëm i renderuar i rreshtit (markup i kolapsuar me semantikë renderer-i). */
  visible: string;
  /** Kolapsim TOTAL zero-width — VETËM sinjal për markup review, jo semantikë. */
  flattened: string;
  /** true kur rreshti mbante HTML tag/comment (burim për DC-ROLE-MARKUP). */
  markupBearing: boolean;
}

export interface RoleDocumentView {
  lines: RoleLineView[];
  /** Comments me lokacion — evidencë privacy RAW, kurrë semantikë aktive. */
  comments: Array<{ body: string; line: number; prefix: string }>;
}

/** Nxjerrja e comments me lokacion mbi tekstin RAW (e zhvendosur nga doc-core). */
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

/**
 * Analiza e tipizuar e një dokumenti role-bearing. I njëjti rend faze si
 * projeksioni aktiv (inline-code → comments → fence mask → tags), po aq
 * strict (të njëjtat fail-closed mbi të njëjtat inpute) — vetëm kolapsimi
 * i markup-it zero-content ndryshon nga blank-im në HEQJE pa boundary.
 * Rreshtat e fenced/indented code mbeten JOAKTIVË (visible bosh).
 */
export function analyzeRoleDocument(text: string): RoleDocumentView {
  const comments = htmlCommentsWithLocation(text);
  const inlineNeutral = text.split("\n").map(projectInlineCodeSpans).join("\n");
  const commentsCollapsed = collapseCommentsForVisible(inlineNeutral, { strict: true });
  const lines = commentsCollapsed.visible.split("\n");
  const fenced = fencedCodeLineMask(lines);
  const views: RoleLineView[] = lines.map((raw, idx) => {
    const markupFromComment = commentsCollapsed.markupLines.has(idx + 1);
    if (fenced[idx] === true || /^(?: {4}|\t)/.test(raw)) {
      return { visible: "", flattened: "", markupBearing: false };
    }
    const rendered = collapseHtmlTags(raw, { strict: true, separators: "space" });
    const flattened = collapseHtmlTags(raw, { strict: false, separators: "none" });
    return {
      visible: rendered.visible,
      flattened: flattened.visible,
      markupBearing: rendered.markupBearing || markupFromComment,
    };
  });
  return { lines: views, comments };
}

/**
 * Kolapsim i një FRAGMENTI (qelizë header-i, prefix/body comment-i) në tekst
 * të dukshëm. Lenient ndaj strukturës së prerë nga copëzimi i qelizave —
 * strict-i i rreshtave të plotë është zbatuar tashmë nga analyzeRoleDocument
 * mbi të njëjtin dokument, ndaj këtu s'krijohet fail-open i ri.
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

/** Fjalë funksionale të annotation-eve sintaktike — JO pjesë identiteti (e pandryshuar). */
export const FUNCTIONAL_ANNOTATION_WORDS = new Set([
  "aprovon", "miraton", "çdo", "ndryshim", "ndryshimet", "ndryshimin", "dokument",
  "dokumentit", "dokumenti", "rresht", "rreshtit", "nëse", "kur", "duhet", "sipas",
  "vetëm", "pas", "para", "për", "nga", "dhe", "ose", "me", "pa", "të", "e", "i",
  "u", "në", "që", "këtij", "kësaj", "atij", "asaj", "review", "approval",
]);

/**
 * Disposition EKSPLICIT për çdo parenthetical të një vlere role-bearing —
 * asnjë heqje pa klasifikim; e paqartë → REVIEW, kurrë silent approval.
 */
export type ParentheticalDisposition =
  | "APPROVED_ANNOTATION"
  | "IDENTITY_REVIEW"
  | "AMBIGUOUS_REVIEW";

export interface ParentheticalAnalysis {
  disposition: ParentheticalDisposition;
  /** Terma jo-funksionalë e jo-pjesë të një fraze të aprovuar. */
  residualTerms: number;
}

/**
 * Klasifikon UN parenthetical (WA-002 + siblings):
 *  - fraza e plotë e aprovuar ose vetëm fjalë funksionale/të aprovuara →
 *    APPROVED_ANNOTATION;
 *  - lidhëzat funksionale ("dhe", "ose", …) NUK i ndajnë termat residualë në
 *    run-e të pavarura — ≥2 terma residualë → IDENTITY_REVIEW edhe kur ndahen
 *    me lidhëz (presja/`&` vazhdojnë të ndezin njësoj);
 *  - 1 term residual identity-like (nis me shkronjë të madhe, ≥4 shkronja) →
 *    IDENTITY_REVIEW; 1 term residual tjetër → AMBIGUOUS_REVIEW (default
 *    REVIEW — invarianti: çdo term ose pjesë e frazës së aprovuar, ose REVIEW).
 * Casing-u i termave mbulohet në çdo formë (TitleCase/UPPERCASE/lowercase/
 * Unicode me apostrof e vizë) — vetëm rregulli i termit të VETËM shikon casing.
 */
export function classifyParenthetical(inner: string, approvedPhrases: string[]): ParentheticalAnalysis {
  const approved = new Set(approvedPhrases.map((p) => p.toLowerCase()));
  const cleaned = inner.replace(/[`*]/g, "").trim();
  if (cleaned === "" || approved.has(cleaned.toLowerCase())) {
    return { disposition: "APPROVED_ANNOTATION", residualTerms: 0 };
  }
  const words = [...cleaned.matchAll(/\p{L}[\p{L}'’-]*/gu)].map((w) => w[0]);
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
  if (residual.length === 0) return { disposition: "APPROVED_ANNOTATION", residualTerms: 0 };
  if (residual.length >= 2) return { disposition: "IDENTITY_REVIEW", residualTerms: residual.length };
  const single = residual[0] ?? "";
  if (single.length >= 4 && /^\p{Lu}/u.test(single)) {
    return { disposition: "IDENTITY_REVIEW", residualTerms: 1 };
  }
  return { disposition: "AMBIGUOUS_REVIEW", residualTerms: 1 };
}

/** Klasifikon TË GJITHA parentetikat e një vlere — një disposition secila. */
export function classifyParentheticals(value: string, approvedPhrases: string[]): ParentheticalAnalysis[] {
  return [...value.matchAll(/\(([^)]*)\)/g)].map((m) => classifyParenthetical(m[1] ?? "", approvedPhrases));
}

/**
 * Wrapper i kontratës ekzistuese: kandidat = ÇDO parenthetical me disposition
 * të ndryshëm nga APPROVED_ANNOTATION (asnjë auto-approval i të paqartës).
 */
export function parentheticalIdentityCandidate(cellRaw: string, approvedPhrases: string[]): boolean {
  return classifyParentheticals(cellRaw, approvedPhrases)
    .some((p) => p.disposition !== "APPROVED_ANNOTATION");
}
