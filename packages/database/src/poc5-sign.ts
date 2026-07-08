import { createHash, createSign, createVerify, generateKeyPairSync } from "node:crypto";
import type { KeyObject } from "node:crypto";

/**
 * PoC-5 — nënshkrimi sipas protokollit zyrtar (ANALIZA §6):
 * ECDSA P-256 mbi SHA-256(UTF8(base64Proto)); firma → base64.
 * QR = `${base64Proto}|${base64Sig}` (pipe).
 * Çelësat në teste = EFEMERALË (gjenerohen në test — kurrë nga skedarë sekretë).
 */

export interface KeyPair {
  privateKey: KeyObject;
  publicKey: KeyObject;
}

/** Çelës efemeral P-256 (prime256v1) — për Tier 1 / verifikim lokal. */
export function generateEphemeralP256(): KeyPair {
  const { privateKey, publicKey } = generateKeyPairSync("ec", { namedCurve: "prime256v1" });
  return { privateKey, publicKey };
}

/** Kodimi i firmës ECDSA — EKSPLICIT gjithmonë (gjetje kritike PM):
 *  Node default = DER (ASN.1, ~70-72 byte); .NET (referenca pos-csharp) prodhon
 *  raw r||s = IEEE P1363 (EKZAKT 64 byte për P-256). Default praktik për PoC =
 *  'ieee-p1363'; DER mbahet për debug/krahasim. Konfirmimi FINAL kundër
 *  serverit real = Tier 2 (nëse kuponi i parë → FAILED VERIFICATION,
 *  dyshimi #1 = ky format — provo alternativën para çdo gjëje tjetër). */
export type SigEncoding = "ieee-p1363" | "der";
export const DEFAULT_SIG_ENCODING: SigEncoding = "ieee-p1363";

/** Firmos bytes e proto-s: base64(proto) → sign(SHA256(UTF8(b64))) → base64 sig. */
export function signProtoBytes(
  protoBytes: Uint8Array,
  privateKey: KeyObject,
  encoding: SigEncoding = DEFAULT_SIG_ENCODING,
): {
  detailsB64: string;
  signatureB64: string;
} {
  const detailsB64 = Buffer.from(protoBytes).toString("base64");
  const signer = createSign("SHA256");
  signer.update(Buffer.from(detailsB64, "utf8"));
  signer.end();
  const signatureB64 = signer.sign({ key: privateKey, dsaEncoding: encoding }).toString("base64");
  return { detailsB64, signatureB64 };
}

/** Verifikim lokal i firmës (i njëjti kanal si serveri). Kodimi eksplicit;
 *  input i keqformuar → false (jo throw). */
export function verifyDetails(
  detailsB64: string,
  signatureB64: string,
  publicKey: KeyObject,
  encoding: SigEncoding = DEFAULT_SIG_ENCODING,
): boolean {
  try {
    const verifier = createVerify("SHA256");
    verifier.update(Buffer.from(detailsB64, "utf8"));
    verifier.end();
    return verifier.verify({ key: publicKey, dsaEncoding: encoding }, Buffer.from(signatureB64, "base64"));
  } catch {
    return false;
  }
}

/** Formati i QR-së zyrtare. */
export function qrString(detailsB64: string, signatureB64: string): string {
  return `${detailsB64}|${signatureB64}`;
}

/** SHA-256 hex i një file/byte-array (drift-check i proto-s së vendor-uar). */
export function sha256Hex(data: Uint8Array | string): string {
  return createHash("sha256").update(data).digest("hex");
}
