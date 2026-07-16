/**
 * §7 — GFM-AWARE TABLE PARSER: qeliza logjike, jo split("|") naiv.
 * Fixtures sintetike; pa asnjë vlerë reale.
 */
import { describe, expect, it } from "vitest";
import { isSeparatorRow, isTableRowCandidate, parseTableRow, tableContextAt } from "../src/markdown-table.js";

describe("parseTableRow — qeliza logjike sipas GFM", () => {
  it("rresht bazik me leading+trailing pipe", () => {
    const r = parseTableRow("| a | b | c |");
    expect(r.cells).toEqual(["a", "b", "c"]);
    expect(r.malformed).toBe(false);
    expect(r.hadLeadingPipe).toBe(true);
    expect(r.hadTrailingPipe).toBe(true);
  });

  it("escaped pipe \\| është tekst, JO kufi qelize", () => {
    const r = parseTableRow("| a \\| b | c |");
    expect(r.cells).toEqual(["a | b", "c"]);
    expect(r.malformed).toBe(false);
  });

  it("pipe brenda code span nuk e ndan qelizën", () => {
    const r = parseTableRow("| `x | y` | c |");
    expect(r.cells).toEqual(["`x | y`", "c"]);
    expect(r.malformed).toBe(false);
  });

  it("code span me backtick-run të dyfishtë (`` a|`b ``)", () => {
    const r = parseTableRow("| `` a|`b `` | c |");
    expect(r.cells).toEqual(["`` a|`b ``", "c"]);
    expect(r.malformed).toBe(false);
  });

  it("code span i pambyllur → malformed=true (kurrë silent)", () => {
    const r = parseTableRow("| `x | y |");
    expect(r.malformed).toBe(true);
  });

  it("qeliza e fundit bosh me trailing pipe: '| a |  |' → dy qeliza, e dyta bosh", () => {
    const r = parseTableRow("| a |  |");
    expect(r.cells).toEqual(["a", ""]);
  });

  it("pa trailing pipe: kolona finale mbetet qelizë reale", () => {
    const r = parseTableRow("| a | b | fund");
    expect(r.cells).toEqual(["a", "b", "fund"]);
    expect(r.hadTrailingPipe).toBe(false);
  });

  it("pa leading pipe: qeliza e parë s'humbet", () => {
    const r = parseTableRow("a | b | c");
    expect(r.cells).toEqual(["a", "b", "c"]);
    expect(r.hadLeadingPipe).toBe(false);
  });

  it("placeholder — dhe qeliza me vetëm hapësira trim-ohen", () => {
    const r = parseTableRow("| — |   | x |");
    expect(r.cells).toEqual(["—", "", "x"]);
  });
});

describe("isSeparatorRow", () => {
  it("njeh separatorë me/pa alignment", () => {
    expect(isSeparatorRow("|---|---|")).toBe(true);
    expect(isSeparatorRow("| :-- | --: | :-: |")).toBe(true);
    expect(isSeparatorRow("|  ---  |")).toBe(true);
  });
  it("refuzon rreshta me tekst ose pa vizë", () => {
    expect(isSeparatorRow("| a | b |")).toBe(false);
    expect(isSeparatorRow("| | |")).toBe(false);
    expect(isSeparatorRow("tekst i lirë")).toBe(false);
  });
});

describe("tableContextAt — header i tabelës që mbulon rreshtin", () => {
  const SNAP = [
    "# Titull",
    "",
    "| Artikulli | Trajtimi | Referenca |",
    "|---|---|---|",
    "| a1 | REQ | r1 |",
    "| a2 | REF | r2 |",
    "",
    "tekst jashtë tabele",
  ].join("\n");

  it("rreshti i të dhënave merr header-in e saktë + columnIndex", () => {
    const ctx = tableContextAt(SNAP, 5);
    expect(ctx).toBeDefined();
    expect(ctx?.headerCells).toEqual(["Artikulli", "Trajtimi", "Referenca"]);
    expect(ctx?.headerArity).toBe(3);
    expect(ctx?.columnIndex(/trajtimi/i)).toBe(1);
    expect(ctx?.columnIndex(/inekzistente/i)).toBeUndefined();
  });

  it("rresht jashtë tabele → undefined", () => {
    expect(tableContextAt(SNAP, 8)).toBeUndefined();
    expect(tableContextAt(SNAP, 1)).toBeUndefined();
  });

  it("rresht që s'ka separator midis vetes dhe 'header-it' → undefined (s'është tabelë e vërtetë)", () => {
    const s = ["| a | b |", "| c | d |"].join("\n");
    expect(tableContextAt(s, 2)).toBeUndefined();
  });

  it("header me escaped pipe numërohet si NJË kolonë logjike", () => {
    const s = ["| Emri \\| Alias | Statusi |", "|---|---|", "| x | y |"].join("\n");
    const ctx = tableContextAt(s, 3);
    expect(ctx?.headerArity).toBe(2);
    expect(ctx?.columnIndex(/statusi/i)).toBe(1);
  });

  it("header malformed (code span i pambyllur) → undefined, kurrë kontekst i gabuar", () => {
    const s = ["| `x | Statusi |", "|---|---|", "| a | b |"].join("\n");
    expect(tableContextAt(s, 3)).toBeUndefined();
  });

  it("lineNo jashtë kufijve → undefined", () => {
    expect(tableContextAt(SNAP, 99)).toBeUndefined();
  });
});

describe("P-IV §4 — outer pipes OPSIONALE + fence exclusion + delimiter/arity", () => {
  const PIPELESS = [
    "Domeni | Prova | Maturity | Treatment",
    "--- | --- | --- | ---",
    "x |  | MODELED TESTED | LATER",
  ].join("\n");

  it("tabelë PA outer pipes: header, aritet dhe kolonat zgjidhen saktë", () => {
    const ctx = tableContextAt(PIPELESS, 3);
    expect(ctx).toBeDefined();
    expect(ctx?.headerArity).toBe(4);
    expect(ctx?.columnIndex(/Maturity/i)).toBe(2);
    expect(ctx?.columnIndex(/Treatment/i)).toBe(3);
    const row = parseTableRow("x |  | MODELED TESTED | LATER");
    expect(row.cells).toEqual(["x", "", "MODELED TESTED", "LATER"]);
  });

  it("prozë me pipe PA delimiter row → JO tabelë", () => {
    const s = ["ky është tekst | me pipe", "dhe vazhdon | këtu"].join("\n");
    expect(tableContextAt(s, 2)).toBeUndefined();
  });

  it("tabelë brenda fenced code block → JO tabelë; pas mbylljes së fence NJIHET (inverse)", () => {
    const s = ["```", "A | B", "--- | ---", "x | y", "```", "", "A | B", "--- | ---", "x | y"].join("\n");
    expect(tableContextAt(s, 4)).toBeUndefined();
    expect(tableContextAt(s, 9)?.headerArity).toBe(2);
  });

  it("fence me ~~~ dhe fence i pambyllur → rreshtat brenda s'janë tabelë", () => {
    const s = ["~~~", "A | B", "--- | ---", "x | y"].join("\n");
    expect(tableContextAt(s, 4)).toBeUndefined();
  });

  it("header/delimiter me aritet të NDRYSHËM → JO tabelë (asnjë kontekst i gabuar)", () => {
    const s = ["A | B | C", "--- | ---", "x | y | z"].join("\n");
    expect(tableContextAt(s, 3)).toBeUndefined();
  });

  it("'---' i vetëm pa pipe NUK është delimiter; me pipe është", () => {
    expect(isSeparatorRow("---")).toBe(false);
    expect(isSeparatorRow("--- | ---")).toBe(true);
    expect(isSeparatorRow(":-- | --:")).toBe(true);
    expect(isSeparatorRow("|---|")).toBe(true);
  });

  it("isTableRowCandidate: kufi real qelize kërkohet; escaped pipe s'kualifikon", () => {
    expect(isTableRowCandidate("a | b")).toBe(true);
    expect(isTableRowCandidate("| vetëm një |")).toBe(true);
    expect(isTableRowCandidate("prozë e thjeshtë")).toBe(false);
    expect(isTableRowCandidate("a \\| b")).toBe(false);
  });

  it("rreshta me trailing pipe të përzier me pa-pipe: konteksti mbetet konsistent", () => {
    const s = ["A | B |", "--- | --- |", "x | y", "z | w |"].join("\n");
    expect(tableContextAt(s, 3)?.headerArity).toBe(2);
    expect(tableContextAt(s, 4)?.headerArity).toBe(2);
  });
});
