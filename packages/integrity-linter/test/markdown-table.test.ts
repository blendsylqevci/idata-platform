/**
 * §7 — GFM-AWARE TABLE PARSER: qeliza logjike, jo split("|") naiv.
 * Fixtures sintetike; pa asnjë vlerë reale.
 */
import { describe, expect, it } from "vitest";
import { isSeparatorRow, parseTableRow, tableContextAt } from "../src/markdown-table.js";

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
