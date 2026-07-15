/**
 * GFM-AWARE MARKDOWN TABLE PARSER (§7, Hardening III) — parser i VETËM i
 * qelizave logjike, i përdorur nga Treatment/Maturity/empty-cells/role-fields.
 *
 * Trajton: escaped pipe `\|`, pipe brenda inline code span, leading/trailing
 * pipe opsional, aritetin rresht/header, qelizën e fundit bosh me/pa trailing
 * pipe, kolonën finale që mungon, placeholder `—`.
 * Rresht i paqartë/malformuar NUK kalon silent — raportohet nga thirrësi.
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

export function isSeparatorRow(line: string): boolean {
  return /^\s*\|?[\s\-:|]+\|?\s*$/.test(line) && line.includes("-");
}

export interface TableContext {
  headerCells: string[];
  headerArity: number;
  /** Indeksi i kolonës me emër që përputhet, ose undefined. */
  columnIndex(nameRe: RegExp): number | undefined;
}

/**
 * Gjen header-in e tabelës që mbulon rreshtin `lineNo` (1-based) në snapshot.
 * Kthen undefined kur rreshti s'është brenda një tabele me header të vërtetë.
 */
export function tableContextAt(snapshot: string, lineNo: number): TableContext | undefined {
  const lines = snapshot.split("\n");
  const idx = lineNo - 1;
  const target = lines[idx];
  if (target === undefined || !target.trimStart().startsWith("|")) return undefined;
  for (let i = idx - 1; i >= 0; i--) {
    const l = lines[i] ?? "";
    if (!l.trimStart().startsWith("|")) return undefined;
    const next = lines[i + 1] ?? "";
    if (isSeparatorRow(next) && !isSeparatorRow(l)) {
      const parsed = parseTableRow(l);
      if (parsed.malformed) return undefined;
      const headerCells = parsed.cells;
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
