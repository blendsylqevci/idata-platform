/**
 * Engine — orkestrimi deterministik i profileve.
 *
 *  - `--mode required` REFUZOHET me ConfigError: HG-LINTER-REQUIRED nuk është
 *    aprovuar; ky version njeh vetëm `advisory`;
 *  - profilet NOT_IMPLEMENTED raportohen NOT_IMPLEMENTED — kurrë PASS;
 *  - fail-closed: ConfigError/ScanError propagohen te CLI (exit 2);
 *  - i testueshëm përmes GitReader të injektueshëm.
 */

import type { Finding, GitReader, ProfileEvaluation, ScanConfig, ScanResult } from "./types.js";
import { ConfigError } from "./types.js";
import { isProfileId, profileStatus } from "./profile-registry.js";
import { defaultSurfaces, runDocCore, type DocCoreSurfaces } from "./profiles/doc-core.js";
import { runHistoryPriv } from "./profiles/history-priv.js";
import { sortFindings } from "./reporter.js";

export interface EngineOptions {
  /** Sipërfaqe të injektueshme për teste; default = konfigurimi real i repo-s. */
  docCoreSurfaces?: DocCoreSurfaces;
}

export function validateMode(mode: string): asserts mode is "advisory" {
  if (mode !== "advisory") {
    throw new ConfigError("MODE_NOT_ALLOWED"); // vetëm advisory — HG-LINTER-REQUIRED i paaprovuar
  }
}

export function runScan(reader: GitReader, config: ScanConfig, options: EngineOptions = {}): ScanResult {
  validateMode(config.mode);
  if (config.profiles.length === 0) throw new ConfigError("NO_PROFILES");
  for (const p of config.profiles as string[]) {
    if (!isProfileId(p)) throw new ConfigError("UNKNOWN_PROFILE");
  }
  // P-V H2 — duplicate profiles refuzohen fail-closed (pa dedup të heshtur)
  if (new Set(config.profiles).size !== config.profiles.length) {
    throw new ConfigError("DUPLICATE_PROFILE");
  }

  const findings: Finding[] = [];
  const evaluations: ProfileEvaluation[] = [];

  // docsRef validohet NJË herë para çdo skanimi (invalid ref → exit 2, jo WARNING+0)
  if (config.profiles.some((p) => p === "DOC-CORE")) {
    reader.revParse(config.docsRef);
  }

  for (const profile of config.profiles) {
    const status = profileStatus(profile);
    if (status === "NOT_IMPLEMENTED") {
      evaluations.push({ profile, profileStatus: status, status: "NOT_IMPLEMENTED" });
      continue;
    }
    if (profile === "DOC-CORE") {
      const r = runDocCore(reader, config.docsRef, options.docCoreSurfaces ?? defaultSurfaces(), config.event);
      findings.push(...r.findings);
      evaluations.push(...r.evaluations);
    } else if (profile === "HISTORY-PRIV") {
      const r = runHistoryPriv(reader, config.baseSha, config.headSha, config.event);
      findings.push(...r.findings);
      evaluations.push(...r.evaluations);
    }
  }

  return { mode: config.mode, evaluations, findings: sortFindings(findings) };
}
