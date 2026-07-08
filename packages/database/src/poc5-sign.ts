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

/** Firmos bytes e proto-s: base64(proto) → sign(SHA256(UTF8(b64))) → base64 sig. */
export function signProtoBytes(protoBytes: Uint8Array, privateKey: KeyObject): {
  detailsB64: string;
  signatureB64: string;
} {
  const detailsB64 = Buffer.from(protoBytes).toString("base64");
  const signer = createSign("SHA256");
  signer.update(Buffer.from(detailsB64, "utf8"));
  signer.end();
  const signatureB64 = signer.sign(privateKey).toString("base64");
  return { detailsB64, signatureB64 };
}

/** Verifikim lokal i firmës (i njëjti kanal si serveri). */
export function verifyDetails(detailsB64: string, signatureB64: string, publicKey: KeyObject): boolean {
  const verifier = createVerify("SHA256");
  verifier.update(Buffer.from(detailsB64, "utf8"));
  verifier.end();
  return verifier.verify(publicKey, Buffer.from(signatureB64, "base64"));
}

/** Formati i QR-së zyrtare. */
export function qrString(detailsB64: string, signatureB64: string): string {
  return `${detailsB64}|${signatureB64}`;
}

/** SHA-256 hex i një file/byte-array (drift-check i proto-s së vendor-uar). */
export function sha256Hex(data: Uint8Array | string): string {
  return createHash("sha256").update(data).digest("hex");
}
