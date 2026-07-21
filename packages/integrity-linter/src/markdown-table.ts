/**
 * GFM-AWARE MARKDOWN TABLE PARSER (§7 Hardening III; §4 Hardening IV) — parser i
 * VETËM i qelizave logjike, i përdorur nga Treatment/Maturity/empty-cells/
 * role-fields/phase-version-status tables.
 *
 * Trajton: escaped pipe `\|`, pipe brenda inline code span, LEADING/TRAILING
 * PIPE OPSIONAL (GFM lejon tabela pa pipe të jashtëm), aritetin header/delimiter,
 * qelizën e fundit bosh, kolonën finale që mungon, placeholder `—`.
 *
 * NUK klasifikohen si tabelë: proza pa kufi qelizash, rreshtat brenda fenced
 * code blocks, rreshtat pa delimiter row valid, header/delimiter me aritet të
 * ndryshëm. Rresht i paqartë/malformuar NUK kalon silent — raportohet nga
 * thirrësi (HP-TABLE-MALFORMED / HP-TABLE-ARITY).
 */

export interface ParsedRow {
  /** Qelizat LOGJIKE (pa pipe-t e jashtëm; escaped/code-span pipes të ruajtura si tekst). */
  cells: string[];
  /** true kur rreshti s'mund të parsohet pa ambiguitet (p.sh. code span i pambyllur). */
  malformed: boolean;
  hadLeadingPipe: boolean;
  hadTrailingPipe: boolean;
}

/**
 * P-V F3 — konteksti i kontejnerit GFM: 0–3 hapësira + një a më shumë nivele
 * `>` (me hapësirë opsionale pas secilit marker). Kthen thellësinë e quote-s
 * dhe përmbajtjen pa prefiksin e kontejnerit. NUK bëhet asnjë replace global —
 * prefiksi hiqet vetëm në kufirin e analizës strukturore, rresht-për-rresht.
 */
export function containerOf(line: string): { depth: number; content: string; indent: number } {
  let i = 0;
  let depth = 0;
  let spaces = 0;
  while (i < line.length) {
    const ch = line[i];
    if (ch === " " && spaces < 3) { spaces += 1; i += 1; continue; }
    if (ch === ">") { depth += 1; i += 1; spaces = 0; if (line[i] === " ") i += 1; continue; }
    break;
  }
  // indent = hapesirat e konsumuara PAS markerit te fundit (P-VII Fix E:
  // fence-i lejon max 3 hapesira TOTALE indentimi brenda kontejnerit)
  return { depth, content: line.slice(i), indent: spaces };
}

/** Ndarja e një rreshti tabele në qeliza logjike sipas GFM (container-aware). */
export function parseTableRow(line: string): ParsedRow {
  const s = containerOf(line).content.trim();
  const hadLeadingPipe = s.startsWith("|");
  const cells: string[] = [];
  let cur = "";
  let inCode = false;
  let codeFence = 0; // gjatësia e backtick-run që hapi span-in
  let malformed = false;
  let i = hadLeadingPipe ? 1 : 0;
  while (i < s.length) {
    const ch = s[i] as string;
    if (ch === "\\" && i + 1 < s.length && s[i + 1] === "|") {
      cur += "|"; // escaped pipe = tekst
      i += 2;
      continue;
    }
    if (ch === "`") {
      let run = 1;
      while (i + run < s.length && s[i + run] === "`") run += 1;
      if (!inCode) {
        inCode = true;
        codeFence = run;
      } else if (run >= codeFence) {
        inCode = false;
      }
      cur += "`".repeat(run);
      i += run;
      continue;
    }
    if (ch === "|" && !inCode) {
      cells.push(cur);
      cur = "";
      i += 1;
      continue;
    }
    cur += ch;
    i += 1;
  }
  if (inCode) malformed = true; // code span i pambyllur → ambiguitet
  const hadTrailingPipe = !inCode && s.endsWith("|") && !s.endsWith("\\|");
  if (hadTrailingPipe) {
    // qeliza pas pipe-it të fundit është "" — hiqet si kufi, JO si qelizë
    if (cur !== "") {
      // tekst pas pipe-it të fundit pa mbyllje → qelizë reale pa trailing pipe
      cells.push(cur);
    }
  } else {
    cells.push(cur);
  }
  return { cells: cells.map((c) => c.trim()), malformed, hadLeadingPipe, hadTrailingPipe };
}

/**
 * Rresht kandidat tabele (§4 P-IV): të paktën NJË kufi qelize real (pipe i
 * pa-escaped jashtë code span) — me ose pa pipe të jashtëm. Proza pa asnjë
 * pipe s'kualifikohet; rreshti malformed s'është kandidat i sigurt.
 */
export function isTableRowCandidate(line: string): boolean {
  const p = parseTableRow(line);
  if (p.malformed) return false;
  return p.cells.length >= 2 || p.hadLeadingPipe || p.hadTrailingPipe;
}

/**
 * Delimiter row valid GFM: çdo qelizë `:?-+:?`, me/pa pipe të jashtëm; "---"
 * i vetëm PA pipe s'është delimiter (thematic break/setext heading).
 */
export function isSeparatorRow(line: string): boolean {
  const p = parseTableRow(line);
  if (p.malformed || p.cells.length === 0) return false;
  if (!p.cells.every((c) => /^:?-+:?$/.test(c))) return false;
  return p.cells.length >= 2 || p.hadLeadingPipe || p.hadTrailingPipe;
}

/**
 * A ndodhet rreshti idx (0-based) brenda një fenced code block (``` / ~~~)?
 * P-V F3 — container-aware: fence-t brenda blockquote (`> ```) ndiqen me
 * thellësinë e tyre; dalja nga kontejneri i mbyll fence-t e hapura më thellë;
 * fence i hapur në depth d mbulon rreshtat pasues me depth >= d.
 */
function inFencedBlockAt(lines: string[], idx: number): boolean {
  // P-VI §3 — STATE MACHINE CommonMark për fenced blocks (kontrata e linter-it,
  // JO parser i përgjithshëm Markdown):
  //  - opener: run >=3 backticks/tildes (info string i lejuar);
  //  - BRENDA një fence aktive NUK hapet fence nested — çdo run më i shkurtër,
  //    me karakter tjetër, me info string ose me thellësi tjetër është CONTENT;
  //  - closer: i NJËJTI karakter, gjatësi >= opener-it, PA info string (vetëm
  //    whitespace rreth run-it), në të njëjtën thellësi kontejneri;
  //  - dalja nga kontejneri (depth < depth-i i fence-s) e mbyll fence-n;
  //  - fence e pambyllur përjashton përmbajtjen deri në fund;
  //  - pas closer-it valid parsing-u normal rifillon.
  let open: { depth: number; ch: string; len: number } | undefined;
  for (let i = 0; i < idx; i++) {
    const { depth, content, indent } = containerOf(lines[i] ?? "");
    if (open !== undefined && depth < open.depth) open = undefined; // dalje kontejneri
    const m = /^( *)(`{3,}|~{3,})/.exec(content);
    if (!m || m[1] === undefined || m[2] === undefined) continue;
    // P-VII Fix E (CommonMark): fence lejon MAX 3 hapesira indentim total —
    // 4+ hapesira = indented code block, JO fence.
    if (indent + m[1].length > 3) continue;
    const ch = m[2][0] as string;
    const len = m[2].length;
    if (open === undefined) {
      // P-VII Fix E (CommonMark): info-string i backtick-fence-it NUK lejon
      // backtick — nje run i tille eshte tekst/inline, jo opener.
      const info = content.slice(m[1].length + len);
      if (ch === "`" && info.includes("`")) continue;
      open = { depth, ch, len }; // opener (me info string te lejuar)
    } else if (depth === open.depth && ch === open.ch && len >= open.len && content.trim() === m[2]) {
      open = undefined; // closer valid — parsing-u normal rifillon
    }
    // përndryshe: CONTENT brenda fence-s — kurrë nested opener
  }
  if (open === undefined) return false;
  const tDepth = containerOf(lines[idx] ?? "").depth;
  return tDepth >= open.depth; // target-i brenda kontejnerit të fence-s së hapur
}

/**
 * P-V F3 — bllok "table-like" me THELLËSI të përzier kontejneri (header/
 * delimiter/data në depth të ndryshme): i paqartë — s'kalon silent (thirrësi
 * e raporton si strukturor). true kur rreshti është kandidat dhe brenda
 * run-it të tij të njëpasnjëshëm ka separator me depth tjetër.
 */
export function mixedDepthTableRow(snapshot: string, lineNo: number): boolean {
  const lines = snapshot.split("\n");
  const idx = lineNo - 1;
  const target = lines[idx];
  if (target === undefined || !isTableRowCandidate(target)) return false;
  const tDepth = containerOf(target).depth;
  for (const dir of [-1, 1]) {
    for (let i = idx + dir; i >= 0 && i < lines.length; i += dir) {
      const l = lines[i] ?? "";
      if (!isTableRowCandidate(l) && !isSeparatorRow(l)) break;
      if (isSeparatorRow(l) && containerOf(l).depth !== tDepth) return true;
    }
  }
  return false;
}

export interface TableContext {
  headerCells: string[];
  headerArity: number;
  /** Indeksi i kolonës me emër që përputhet, ose undefined. */
  columnIndex(nameRe: RegExp): number | undefined;
}

/**
 * Gjen header-in e tabelës që mbulon rreshtin `lineNo` (1-based) në snapshot.
 * Kthen undefined kur rreshti s'është brenda një tabele GFM valide: kërkohet
 * header + delimiter row me ARITET IDENTIK, rreshta të njëpasnjëshëm kandidatë,
 * jashtë fenced code blocks; outer pipes OPSIONALE (§4 P-IV).
 */
export function tableContextAt(snapshot: string, lineNo: number): TableContext | undefined {
  const lines = snapshot.split("\n");
  const idx = lineNo - 1;
  const target = lines[idx];
  if (target === undefined) return undefined;
  // target duhet të jetë kandidat OSE i malformuar (i malformuari merr kontekst
  // që HP-TABLE-MALFORMED të mund të vendosë me informacion tabele)
  if (!isTableRowCandidate(target) && !parseTableRow(target).malformed) return undefined;
  if (inFencedBlockAt(lines, idx)) return undefined;
  // P-V F3: header/delimiter/data duhet të kenë THELLËSI konsistente kontejneri
  const tDepth = containerOf(target).depth;
  for (let i = idx - 1; i >= 0; i--) {
    const l = lines[i] ?? "";
    // tabela është bllok rreshtash të NJËPASNJËSHËM kandidatë — prishja e ndan
    if (!isTableRowCandidate(l) && !isSeparatorRow(l)) return undefined;
    if (containerOf(l).depth !== tDepth) return undefined; // mixed depth ≠ tabelë e qartë
    const next = lines[i + 1] ?? "";
    if (isSeparatorRow(next) && !isSeparatorRow(l)) {
      const parsed = parseTableRow(l);
      if (parsed.malformed) return undefined;
      const headerCells = parsed.cells;
      // GFM: header + delimiter DUHET të kenë aritet identik — përndryshe
      // s'është tabelë (asnjë kontekst i gabuar për rregullat strukturore)
      if (parseTableRow(next).cells.length !== headerCells.length) return undefined;
      return {
        headerCells,
        headerArity: headerCells.length,
        columnIndex(nameRe: RegExp): number | undefined {
          for (let c = 0; c < headerCells.length; c++) {
            if (nameRe.test(headerCells[c] ?? "")) return c;
          }
          return undefined;
        },
      };
    }
  }
  return undefined;
}
