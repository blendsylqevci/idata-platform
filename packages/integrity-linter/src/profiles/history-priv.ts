/**
 * HISTORY-PRIV — historia e git-it & politika e privatësisë (advisory, FAIL-CLOSED).
 *
 * Rregullat e forta (07_PRODATA §Per-commit history scan + hardening PR #21):
 *  - ÇDO commit në merge-base..head skanohet VEÇMAS (jo net-diff);
 *  - metadata (diff-tree --raw -z --no-renames) merren dhe policy e path/mode/scope
 *    zbatohet mbi TË GJITHA entries PARA çdo qasjeje në content;
 *  - rename mbulohet si Delete+Add (pa -M/-C — zero similarity blob reads);
 *  - path privat: Add/Modify/Typechange → ERROR pa blob read; Delete = cleanup i
 *    lejuar pa blob read; Delete privat + Add/Modify publik në të njëjtin commit →
 *    HP-PRIVATE-RELOCATION (REVIEW);
 *  - merge commit në feature range → INCOMPLETE_SCAN → exit 2 (jo bypass);
 *  - mode të lejuara: vetëm 100644/100755 (symlink/gitlink/unknown → exit 2);
 *  - introduced-then-removed mbetet finding (kapet te commit-i që e futi);
 *  - locator për path privat = VETËM `[REDACTED_PATH]`; zero content kudo.
 */

import type { Finding, GitReader, ProfileEvaluation, PullRequestEvent, RawEntry } from "../types.js";
import { ConfigError, ScanError } from "../types.js";
import { profileStatus } from "../profile-registry.js";
import { ALLOWED_FILE_MODES, NULL_MODE, evaluatePath } from "../path-policy.js";
import { buildContentRules, versionAuthorities } from "./history-rules.js";

const P = "HISTORY-PRIV" as const;
const REDACTED = "[REDACTED_PATH]";
const OVERSIZE_BYTES = 1_000_000;

export interface ScopeBlock {
  present: boolean;
  malformed: boolean;
  allowed: string[];
}

/** Parse i bllokut machine-readable `<!-- integrity-linter:scope ... -->`. */
export function parseScopeBlock(body: string | undefined): ScopeBlock {
  if (body === undefined) return { present: false, malformed: false, allowed: [] };
  const start = body.indexOf("<!-- integrity-linter:scope");
  if (start === -1) return { present: false, malformed: false, allowed: [] };
  const end = body.indexOf("-->", start);
  if (end === -1) return { present: true, malformed: true, allowed: [] };
  const block = body.slice(start, end);
  const allowed: string[] = [];
  for (const raw of block.split("\n").slice(1)) {
    const line = raw.trim();
    if (line === "" || line.startsWith("#") || line.startsWith("allowed-paths")) continue;
    if (!line.startsWith("- ")) return { present: true, malformed: true, allowed: [] };
    const p = line.slice(2).trim();
    if (p === "" || p.includes("..") || p.startsWith("/")) return { present: true, malformed: true, allowed: [] };
    allowed.push(p);
  }
  if (allowed.length === 0) return { present: true, malformed: true, allowed: [] };
  return { present: true, malformed: false, allowed };
}

export function pathInScope(path: string, allowed: string[]): boolean {
  return allowed.some((a) => (a.endsWith("/") ? path.startsWith(a) : path === a));
}

interface ApprovedEntry {
  entry: RawEntry;
  short: string;
  sha: string;
}

export function runHistoryPriv(
  reader: GitReader,
  baseSha: string | undefined,
  headSha: string | undefined,
  event: PullRequestEvent | undefined,
): { findings: Finding[]; evaluations: ProfileEvaluation[] } {
  const status = profileStatus(P);
  const findings: Finding[] = [];
  const evaluations: ProfileEvaluation[] = [];

  const base = baseSha ?? event?.baseSha;
  const head = headSha ?? event?.headSha;
  if (base === undefined || head === undefined) {
    throw new ConfigError("RANGE_MISSING"); // pa hamendësim origin/main
  }
  if (reader.isShallow()) {
    throw new ScanError("INCOMPLETE_SCAN:SHALLOW_REPOSITORY");
  }

  const baseResolved = reader.revParse(base);
  const headResolved = reader.revParse(head);
  const mergeBase = reader.mergeBase(baseResolved, headResolved);
  const commits = reader.revList(mergeBase, headResolved);

  const scope = parseScopeBlock(event?.body);
  if (event?.body !== undefined && (!scope.present || scope.malformed)) {
    findings.push({
      profile: P, profileStatus: status, severity: "WARNING",
      ruleId: "HP-SCOPE-BLOCK", category: "scope",
      locator: scope.present ? "pr-body scope block malformed" : "pr-body scope block missing",
    });
  } else if (event?.body === undefined) {
    evaluations.push({
      profile: P, profileStatus: status, status: "NOT_EVALUATED",
      check: "scope", reason: "missing PR event/body",
    });
  }

  if (commits.length === 0) {
    evaluations.push({ profile: P, profileStatus: status, status: "EVALUATED", reason: "empty range" });
    return { findings, evaluations };
  }

  const contentRules = buildContentRules();
  const vAuthorities = versionAuthorities();

  for (const sha of commits) {
    const short = sha.slice(0, 8);

    // ── MERGE COMMIT: rrugë e ngushtë e sigurt → skanim i paplotë → exit 2 ──
    if (reader.parents(sha).length > 1) {
      throw new ScanError("INCOMPLETE_SCAN:MERGE_COMMIT_IN_RANGE");
    }

    // ── FAZA 1: metadata + POLICY mbi TË GJITHA entries PARA content access ──
    const entries = reader.rawEntries(sha);
    const approved: ApprovedEntry[] = [];
    let privateDeletion = false;
    let publicAddition = false;

    for (const entry of entries) {
      const verdict = evaluatePath(entry.path);
      if (verdict === "BLOCKED") {
        if (entry.status === "D") {
          privateDeletion = true; // cleanup i lejuar — zero blob read, zero finding
          continue;
        }
        findings.push({
          profile: P, profileStatus: status, severity: "ERROR",
          ruleId: "HP-PRIVATE-PATH", category: "privacy",
          locator: REDACTED, commit: short,
        });
        continue; // ASNJË qasje në blob
      }

      // MODE policy: vetëm 100644/100755 (plus 000000 për anën që mungon)
      const modeToCheck = entry.status === "D" ? entry.oldMode : entry.newMode;
      if (!ALLOWED_FILE_MODES.has(modeToCheck) && modeToCheck !== NULL_MODE) {
        throw new ScanError("INCOMPLETE_SCAN:UNSUPPORTED_FILE_MODE");
      }
      if (entry.status === "D") continue; // fshirje publike — pa content scan

      publicAddition = true;
      // SCOPE whitelist (kur ka bllok të vlefshëm)
      if (event?.body !== undefined && scope.present && !scope.malformed && !pathInScope(entry.path, scope.allowed)) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "HP-OUT-OF-SCOPE", category: "scope",
          locator: entry.path, commit: short,
        });
      }
      approved.push({ entry, short, sha });
    }

    if (privateDeletion && publicAddition) {
      findings.push({
        profile: P, profileStatus: status, severity: "REVIEW",
        ruleId: "HP-PRIVATE-RELOCATION", category: "privacy",
        locator: REDACTED, commit: short,
      });
    }

    // ── FAZA 2: content vetëm për blob-et e APROVUARA (me object-id për size) ──
    // authority snapshot per-commit për version drift (9)
    const snapshotVersions = new Map<string, string>();
    for (const va of vAuthorities) {
      if (reader.hasFile(sha, va.authorityFile)) {
        const m = va.authorityPattern.exec(reader.showFile(sha, va.authorityFile));
        if (m?.[1] !== undefined) snapshotVersions.set(va.label, m[1]);
      }
    }

    for (const { entry } of approved) {
      const size = reader.blobSizeByOid(entry.newOid);
      if (size > OVERSIZE_BYTES) {
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "HP-OVERSIZE-BLOB", category: "filesystem-boundary",
          locator: entry.path, commit: short,
        });
        continue;
      }
      const content = reader.addedContent(sha, entry.path);
      if (content.binary) {
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "HP-BINARY-BLOB", category: "filesystem-boundary",
          locator: entry.path, commit: short,
        });
        continue;
      }
      for (const { line, text } of content.lines) {
        for (const rule of contentRules) {
          if (rule.test(text)) {
            findings.push({
              profile: P, profileStatus: status, severity: rule.severity,
              ruleId: rule.ruleId, category: rule.category,
              locator: `${entry.path}:${line}`, commit: short,
            });
          }
        }
        // 9) version drift per-commit kundër snapshot-it tracked të po atij commit-i
        for (const va of vAuthorities) {
          const current = snapshotVersions.get(va.label);
          if (current === undefined || entry.path === va.authorityFile) continue;
          const m = va.consumerPattern.exec(text);
          if (m?.[1] !== undefined && m[1] !== current && !/(HISTORICAL|SUPERSEDED|Historia|HISTORIK|superseded|ish |atëhersh)/i.test(text)) {
            findings.push({
              profile: P, profileStatus: status, severity: "WARNING",
              ruleId: "HP-VERSION-DRIFT", category: "drift",
              locator: `${entry.path}:${line}`, commit: short,
            });
          }
        }
      }
    }
  }

  evaluations.push({ profile: P, profileStatus: status, status: "EVALUATED" });
  return { findings, evaluations };
}
