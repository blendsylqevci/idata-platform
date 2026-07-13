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
    throw new ConfigError(
      `mode "${mode}" refuzohet: vetëm "advisory" lejohet — HG-LINTER-REQUIRED nuk është aprovuar`,
    );
  }
}

export function runScan(reader: GitReader, config: ScanConfig, options: EngineOptions = {}): ScanResult {
  validateMode(config.mode);
  if (config.profiles.length === 0) throw new ConfigError("no profiles requested");
  for (const p of config.profiles as string[]) {
    if (!isProfileId(p)) throw new ConfigError(`unknown profile: ${p}`);
  }

  const findings: Finding[] = [];
  const evaluations: ProfileEvaluation[] = [];

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
