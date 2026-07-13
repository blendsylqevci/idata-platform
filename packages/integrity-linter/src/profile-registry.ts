/**
 * Regjistri canonical i 16 profileve (07_PRODATA §Linter — Regjistri i profileve).
 *
 * Në këtë baseline advisory:
 *  - DOC-CORE dhe HISTORY-PRIV = IMPLEMENTED_ADVISORY;
 *  - 14 profilet e tjera = NOT_IMPLEMENTED — dhe NUK raportohen kurrë PASS;
 *  - REQUIRED nuk mund të caktohet nga ky version (HG-LINTER-REQUIRED i paaprovuar).
 */

import type { ProfileId, ProfileStatus } from "./types.js";

export const PROFILE_REGISTRY: Readonly<Record<ProfileId, ProfileStatus>> = Object.freeze({
  "DOC-CORE": "IMPLEMENTED_ADVISORY",
  "HISTORY-PRIV": "IMPLEMENTED_ADVISORY",
  "SEF-ATK": "NOT_IMPLEMENTED",
  "SRC-CLOSURE": "NOT_IMPLEMENTED",
  "ARCH-ADR": "NOT_IMPLEMENTED",
  "DB-MIG": "NOT_IMPLEMENTED",
  "SEC-TENANT": "NOT_IMPLEMENTED",
  "CI-SUPPLY": "NOT_IMPLEMENTED",
  "FIN-ACCOUNTING": "NOT_IMPLEMENTED",
  "INV-WMS-OMS": "NOT_IMPLEMENTED",
  "KABI-OPS": "NOT_IMPLEMENTED",
  "OFFLINE-EDGE": "NOT_IMPLEMENTED",
  "PLANET-POS": "NOT_IMPLEMENTED",
  "UX-CLIENT": "NOT_IMPLEMENTED",
  "OBS-CAPACITY": "NOT_IMPLEMENTED",
  "CUTOVER-OPS": "NOT_IMPLEMENTED",
});

export const ALL_PROFILE_IDS = Object.freeze(Object.keys(PROFILE_REGISTRY) as ProfileId[]);

export function isProfileId(value: string): value is ProfileId {
  return (ALL_PROFILE_IDS as readonly string[]).includes(value);
}

export function profileStatus(id: ProfileId): ProfileStatus {
  return PROFILE_REGISTRY[id];
}
