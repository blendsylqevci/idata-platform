/**
 * P-VII §9 — KNOWN_LIMITATION: teste që DËSHMOJNË sjelljen AKTUALE të kufijve
 * residualë të dokumentuar (README §"Kufij residualë"). KËTO NUK janë fixe të
 * heshtura dhe NUK autorizojnë shpërfilljen e gjetjeve të ardhshme — pranimi
 * formal i çdo residuali kërkon Human Gate të VEÇANTË (follow-up OPEN).
 * Nëse një kufi mbyllet më vonë, testi përkatës DUHET të përditësohet me
 * kontratën e re — dështimi i tij sinjalizon ndryshim sjelljeje, jo regresion.
 */
import { describe, expect, it } from "vitest";
import { hasObfuscatedMatch } from "../src/machine-token.js";
import { containsLocalAbsolutePath } from "../src/path-policy.js";

const ZWSP = String.fromCharCode(0x200b);
const RLO = String.fromCharCode(0x202e);
const COMBINING_ACUTE = String.fromCharCode(0x0301);
const CHEROKEE_KV = String.fromCharCode(0x13b6); // Ꮶ — konfuzabël JASHTË hartës së kuruar

describe("KNOWN_LIMITATION — kufij residualë të dokumentuar (advisory)", () => {
  it("KNOWN_LIMITATION: konfuzabël JASHTË setit të kuruar (jo UTS #39) nuk detektohet si obfuscation", () => {
    // 'K' e zëvendësuar me Cherokee Ꮶ: raw s'match-on; skeleton-i NUK e harton
    // → asnjë obfuscation finding. Kufi i pranuar i hartës së kuruar.
    expect(hasObfuscatedMatch(`GJENDJA: O${CHEROKEE_KV}`, /GJENDJA: (OK|JO)/g)).toBe(false);
  });

  it("KNOWN_LIMITATION: bidi/format-char NGJITUR token-it (jashtë tij) kalon — detektimi është count-based", () => {
    // RLO pas token-it valid: raw=1, skeleton=1 → sk>raw s'vlen → zero finding.
    expect(hasObfuscatedMatch(`FISCAL-F5${RLO}`, /FISCAL-F(\d+)/g)).toBe(false);
  });

  it("KNOWN_LIMITATION: combining marks (jo NFC) brenda token-it nuk detektohen si obfuscation", () => {
    // 'O' + combining acute: raw s'match-on; skeleton-i s'bën NFC → as skeleton-i.
    expect(hasObfuscatedMatch(`GJENDJA: O${COMBINING_ACUTE}K`, /GJENDJA: (OK|JO)/g)).toBe(false);
  });

  it("KNOWN_LIMITATION: format-char (ZWSP) i injektuar BRENDA token-it të path-it e shpëton nga classifier-i", () => {
    expect(containsLocalAbsolutePath(`/tm${ZWSP}p/x/f.log`)).toBe(false);
  });

  it("KNOWN_LIMITATION: path lokal i NGULITUR brenda një URL-je http(s) (query/redirect) nuk kapet", () => {
    // Token-i ka skemë https → kandidati anashkalohet; s'ka path të dytë të lirë.
    expect(containsLocalAbsolutePath("https://example.com/redirect?to=/tmp/f.log")).toBe(false);
  });
});
