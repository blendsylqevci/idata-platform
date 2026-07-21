/**
 * P-V F1/F2 — BOUNDARY I CENTRALIZUAR për PROTECTED MACHINE TOKENS
 * (vlera authority/consumer: versione, counts, statuse, çelësa FISCAL-F*).
 *
 * PARIME (të detyrueshme nga direktiva):
 *  - RAW TEXT mbetet burimi i së vërtetës — projeksioni "skeleton" përdoret
 *    VETËM për të ZBULUAR obfuscation, KURRË për ta pranuar tokenin si valid;
 *  - asnjë NFKC global, asnjë heqje e heshtur e zero-width, asnjë zëvendësim
 *    automatik i homoglyph-eve, asnjë normalizim i gjithë dokumentit;
 *  - proza normale shqipe/Unicode (ë, ç, …) NUK preket — kontrolli vlen vetëm
 *    aty ku pattern-i i një machine-token-i do të përputhej pas projektimit
 *    por NUK përputhet mbi tekstin raw (dallim = obfuscation kandidat).
 */

/**
 * Unicode format/invisible characters (Cf + kontrolle drejtimi/bashkimi) +
 * U+2028/U+2029 (line/paragraph separator \u2014 P-VI \u00A71: brenda nj\u00EB rreshti jan\u00EB
 * kandidat\u00EB obfuskimi p\u00EBr protected tokens; n\u00EB analiz\u00EBn e fjalive trajtohen
 * si delimitues \u2014 kurr\u00EB silent bypass).
 */
export const FORMAT_CHAR_RE = /[\p{Default_Ignorable_Code_Point}\u2028\u2029]/u;

/**
 * P-VI \u00A71 \u2014 KUFIJ TOKEN\u00CBSH UNICODE-AWARE t\u00EB centralizuar: `\b` i ASCII-t
 * d\u00EBshton pran\u00EB shkronjave shqipe (\u00EB, \u00E7) dhe apostrofit \u2014 p\u00EBrdor lookaround
 * me \p{L}/\p{N} + flag `u`. Fragmentet jan\u00EB STRING-je p\u00EBr t'u kompozuar n\u00EB
 * RegExp me flag-un e duhur nga thirr\u00EBsi.
 */
export const NOT_LETTER_BEFORE = "(?<![\\p{L}\\p{N}])";
export const NOT_LETTER_AFTER = "(?![\\p{L}\\p{N}])";

/** Delimitues fjalie \u2014 p\u00EBrfshin U+2028/U+2029 (kontrat\u00EB e dokumentuar \u00A71). */
export const SENTENCE_DELIM_RE = /[.!?;\u00B7\u2028\u2029]/;

/**
 * Normalizim apostrof\u00EBsh VET\u00CBM P\u00CBR DETECTION (\u2019 \u2018 \u02BC ` \u2192 ') \u2014 raw source-i
 * nuk ndryshohet kurr\u00EB; gjat\u00EBsia e string-ut ruhet (z\u00EBvend\u00EBsim 1:1).
 */
export function normalizeApostrophesForDetection(text: string): string {
  return text.replace(/[\u2018\u2019\u02BC\u0060]/g, "'");
}

/**
 * Hartë e KURUAR konfuzablesh (cirilike/greke → latine) — vetëm shkronja që
 * janë vizualisht identike me ASCII latine. Qëllimisht E VOGËL: false-negative
 * mbi konfuzabla ekzotikë pranohet; false-positive mbi prozë reale JO.
 */
const CONFUSABLES = new Map<string, string>([
  // cirilike
  ["А", "A"], ["В", "B"], ["Е", "E"], ["К", "K"], ["М", "M"],
  ["Н", "H"], ["О", "O"], ["Р", "P"], ["С", "C"], ["Т", "T"],
  ["Х", "X"], ["У", "Y"], ["І", "I"], ["Ѕ", "S"], ["Ј", "J"],
  ["а", "a"], ["е", "e"], ["о", "o"], ["р", "p"], ["с", "c"],
  ["у", "y"], ["х", "x"], ["і", "i"], ["ѕ", "s"], ["ј", "j"],
  // greke
  ["Α", "A"], ["Β", "B"], ["Ε", "E"], ["Ζ", "Z"], ["Η", "H"],
  ["Ι", "I"], ["Κ", "K"], ["Μ", "M"], ["Ν", "N"], ["Ο", "O"],
  ["Ρ", "P"], ["Τ", "T"], ["Υ", "Y"], ["Χ", "X"],
  ["ο", "o"], ["ν", "v"], ["ι", "i"], ["κ", "k"], ["α", "a"],
]);

/**
 * Projeksion DETEKTIMI: heq format-chars dhe harton konfuzablat e kuruar në
 * latinishte. NUK është normalizim pranues — vetëm krahasues.
 */
export function detectionSkeleton(text: string): string {
  let out = "";
  for (const ch of text) {
    if (FORMAT_CHAR_RE.test(ch)) continue;
    out += CONFUSABLES.get(ch) ?? ch;
  }
  return out;
}

function globalized(re: RegExp): RegExp {
  return new RegExp(re.source, re.flags.includes("g") ? re.flags : re.flags + "g");
}

export function countMatches(text: string, pattern: RegExp): number {
  const g = globalized(pattern);
  let n = 0;
  while (g.exec(text) !== null) n += 1;
  return n;
}

/**
 * true kur rreshti ka MË SHUMË përputhje të pattern-it mbi skeleton sesa mbi
 * raw — d.m.th. një token i mbrojtur ekziston vetëm i OBFUSKUAR (zero-width /
 * homoglyph / format-char brenda tokenit). Proza normale: raw == skeleton.
 */
export function hasObfuscatedMatch(line: string, pattern: RegExp): boolean {
  const raw = countMatches(line, pattern);
  const sk = countMatches(detectionSkeleton(line), pattern);
  return sk > raw;
}
