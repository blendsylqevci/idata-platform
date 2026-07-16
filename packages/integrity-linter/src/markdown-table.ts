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

/** Ndarja e një rreshti tabele në qeliza logjike sipas GFM. */
export function parseTableRow(line: string): ParsedRow {
  const s = line.trim();
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

/** A ndodhet rreshti idx (0-based) brenda një fenced code block (``` / ~~~)? */
function inFencedBlockAt(lines: string[], idx: number): boolean {
  let open: { ch: string; len: number } | undefined;
  for (let i = 0; i < idx; i++) {
    const m = /^\s{0,3}(`{3,}|~{3,})/.exec(lines[i] ?? "");
    if (!m || m[1] === undefined) continue;
    const ch = m[1][0] as string;
    const len = m[1].length;
    if (open === undefined) {
      open = { ch, len };
    } else if (ch === open.ch && len >= open.len && (lines[i] ?? "").trim() === m[1]) {
      open = undefined; // fence mbyllës: vetëm run-i, pa info string
    }
  }
  return open !== undefined;
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
  for (let i = idx - 1; i >= 0; i--) {
    const l = lines[i] ?? "";
    // tabela është bllok rreshtash të NJËPASNJËSHËM kandidatë — prishja e ndan
    if (!isTableRowCandidate(l) && !isSeparatorRow(l)) return undefined;
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
