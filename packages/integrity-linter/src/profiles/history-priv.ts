/**
 * HISTORY-PRIV — historia e git-it & politika e privatësisë (advisory).
 *
 * Skanon ÇDO commit veçmas në base..head (jo vetëm net-diff):
 *  - path kontrollohet PARA çdo qasjeje në blob;
 *  - path i dyshuar privat raportohet VETËM si `[REDACTED_PATH]` — pa path real,
 *    pa hash të kthyeshëm, pa matched text;
 *  - added-lines inspektohen vetëm TRANZIENT në memorie; raw content nuk ruhet,
 *    nuk printohet, nuk futet në artifact/exception/baseline;
 *  - introduced-then-removed mbetet finding (kapet te commit-i që e futi);
 *  - merge commit / binary / symlink / oversize / invalid encoding raportohen
 *    pa lexuar a publikuar përmbajtjen;
 *  - scope whitelist lexohet nga blloku machine-readable i PR body-t.
 *
 * Pattern-et e klasave sensitive ndërtohen RUNTIME nga fragmente sintetike,
 * që vetë ky burim të mos përmbajë literale që kapen nga guard/gitleaks.
 */

import type { Finding, GitReader, ProfileEvaluation, PullRequestEvent } from "../types.js";
import { profileStatus } from "../profile-registry.js";
import { ConfigError, ScanError } from "../types.js";

const P = "HISTORY-PRIV" as const;
const REDACTED = "[REDACTED_PATH]";
const OVERSIZE_BYTES = 1_000_000;

function joinFrag(...parts: string[]): string {
  return parts.join("");
}

/** Rrënjë/direktori të bllokuara (klasa private-local; ndërtuar nga fragmente). */
function blockedRootPatterns(): RegExp[] {
  const roots = [
    joinFrag("00", "_raw"),
    joinFrag("docs", "-private"),
    joinFrag("private", "-sensitive"),
    joinFrag("evidence", "-private", "-local"),
    joinFrag(".", "sef", "-local"),
    joinFrag("03", "_contracts"),
    joinFrag("docs/", "evidence"),
    joinFrag("docs/archive/", "prodata"),
  ];
  return roots.map((r) => new RegExp(`(^|/)${r.replace(/[.\\/]/g, (c) => `\\${c}`)}(/|$)`, "i"));
}

/** Emra file / prapashtesa të bllokuara (klasa, jo emra realë). */
function blockedFilePatterns(): RegExp[] {
  const envName = joinFrag(".", "env");
  const ownerDocStems = [
    joinFrag("Ofer", "ta"), joinFrag("Kontra", "ta"), joinFrag("Shtoj", "ca"),
    joinFrag("Dokumenti", "_", "Master"), joinFrag("Dosja", "_", "Certifikimit"),
  ];
  return [
    new RegExp(`(^|/)\\${envName}(\\.|$)`, "i"),
    new RegExp(`\\.(mdf|ldf|bak|pem|p12|pfx|keystore|jks)$`, "i"),
    new RegExp(`(^|/)id_(rsa|ed25519|ecdsa)(\\.|$)`, "i"),
    new RegExp(`(^|/)(${ownerDocStems.join("|")})[_-]`, "i"),
  ];
}

/** Klasa përmbajtjesh të rregulluara/sekrete në added-lines (fragmente runtime). */
function contentClassPatterns(): Array<{ ruleId: string; re: RegExp }> {
  const dash5 = joinFrag("--", "---");
  const beginKey = joinFrag(dash5, "BEGIN ", "(?:RSA |EC |OPENSSH )?", "PRIVATE", " KEY");
  const ghToken = joinFrag("g", "ho", "_[A-Za-z0-9_]{8,}");
  const ghPat = joinFrag("github", "_", "pat", "_[A-Za-z0-9_]{8,}");
  const skToken = joinFrag("\\bsk", "-[A-Za-z0-9_-]{20,}");
  const awsKey = joinFrag("AK", "IA", "[0-9A-Z]{16}");
  const credWords = [joinFrag("api", "[_-]?", "key"), joinFrag("pass", "word"), joinFrag("tok", "en"), joinFrag("sec", "ret")].join("|");
  const credAssign = joinFrag("(?:^|[^A-Za-z])(?:", credWords, ")\\s*[:=]\\s*['\"]?[A-Za-z0-9_/+.-]{6,}");
  const absPaths = [joinFrag("/Us", "ers/"), joinFrag("C:\\\\", "Us", "ers"), joinFrag("/ho", "me/[a-z]")].join("|");
  return [
    { ruleId: "HP-PRIVATE-KEY-BLOCK", re: new RegExp(beginKey) },
    { ruleId: "HP-TOKEN-CLASS", re: new RegExp(`${ghToken}|${ghPat}|${skToken}|${awsKey}`) },
    { ruleId: "HP-CREDENTIAL-ASSIGN", re: new RegExp(credAssign, "i") },
    { ruleId: "HP-ABSOLUTE-LOCAL-PATH", re: new RegExp(absPaths) },
    { ruleId: "HP-REGULATED-ID-CLASS", re: /(^|\D)\d{12}(\D|$)/ },
    { ruleId: "HP-FINANCIAL-VALUE-CLASS", re: /\b\d{2,3}[.,]\d{3}[.,]\d{2}\b/ },
  ];
}

export interface ScopeBlock {
  present: boolean;
  malformed: boolean;
  allowed: string[];
}

/** Parse i bllokut machine-readable `<!-- integrity-linter:scope ... -->` nga PR body. */
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
    throw new ConfigError("HISTORY-PRIV requires explicit --base/--head or a PR event (no origin/main guessing)");
  }
  if (reader.isShallow()) {
    throw new ScanError("shallow repository — incomplete history; refusing to scan (fetch-depth: 0 required)");
  }

  const baseResolved = reader.revParse(base);
  const headResolved = reader.revParse(head);
  const mergeBase = reader.mergeBase(baseResolved, headResolved);
  const commits = reader.revList(mergeBase, headResolved);
  if (commits.length === 0) {
    evaluations.push({ profile: P, profileStatus: status, status: "EVALUATED", reason: "empty range" });
    return { findings, evaluations };
  }

  const roots = blockedRootPatterns();
  const files = blockedFilePatterns();
  const contentClasses = contentClassPatterns();
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
      reason: "scope checks: missing PR event/body",
    });
  }

  const isPrivatePath = (p: string): boolean =>
    roots.some((re) => re.test(p)) || files.some((re) => re.test(p));

  for (const sha of commits) {
    const short = sha.slice(0, 8);
    const binaries = reader.parents(sha).length > 1 ? new Set<string>() : reader.binaryPaths(sha);
    if (reader.parents(sha).length > 1) {
      findings.push({
        profile: P, profileStatus: status, severity: "REVIEW",
        ruleId: "HP-MERGE-COMMIT", category: "INCOMPLETE_SCAN",
        locator: "merge commit in feature range (scanned parents individually only if in range)",
        commit: short,
      });
      continue;
    }
    for (const entry of reader.changedEntries(sha)) {
      // 1) PATH kontrollohet PARA blob-it — rename kap edhe old edhe new path.
      const candidatePaths = entry.oldPath !== undefined ? [entry.path, entry.oldPath] : [entry.path];
      const privateHit = candidatePaths.some((p) => isPrivatePath(p));
      if (privateHit) {
        findings.push({
          profile: P, profileStatus: status, severity: "ERROR",
          ruleId: "HP-PRIVATE-PATH", category: "privacy",
          locator: REDACTED,
          commit: short,
        });
        continue; // asnjë qasje në blob
      }
      if (entry.status === "D") continue; // fshirjet e pastrimit lejohen; futja kapet te commit-i i vet
      if (event?.body !== undefined && scope.present && !scope.malformed && !pathInScope(entry.path, scope.allowed)) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "HP-OUT-OF-SCOPE", category: "scope",
          locator: entry.path,
          commit: short,
        });
      }
      // 2) symlink — pa lexim përmbajtjeje
      if (entry.newMode === "120000") {
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "HP-SYMLINK", category: "filesystem-boundary",
          locator: entry.path, commit: short,
        });
        continue;
      }
      // 3) oversize — pa lexim përmbajtjeje
      let size = 0;
      try {
        size = reader.blobSize(sha, entry.path);
      } catch {
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "HP-BLOB-UNREADABLE", category: "INCOMPLETE_SCAN",
          locator: entry.path, commit: short,
        });
        continue;
      }
      if (size > OVERSIZE_BYTES) {
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "HP-OVERSIZE-BLOB", category: "filesystem-boundary",
          locator: entry.path, commit: short,
        });
        continue;
      }
      // 4) binar — raportohet pa lexuar përmbajtjen
      if (binaries.has(entry.path)) {
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "HP-BINARY-BLOB", category: "filesystem-boundary",
          locator: entry.path, commit: short,
        });
        continue;
      }
      // 5) added-lines — tranzient, në memorie; encoding invalid → finding pa content
      let added: string[];
      try {
        added = reader.addedLines(sha, entry.path);
      } catch {
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "HP-ENCODING-OR-BINARY", category: "filesystem-boundary",
          locator: entry.path, commit: short,
        });
        continue;
      }
      for (let i = 0; i < added.length; i++) {
        const line = added[i] ?? "";
        for (const cls of contentClasses) {
          if (cls.re.test(line)) {
            findings.push({
              profile: P, profileStatus: status, severity: "WARNING",
              ruleId: cls.ruleId, category: "privacy",
              locator: `${entry.path}:+${i + 1}`,
              commit: short,
            });
          }
        }
      }
    }
  }

  evaluations.push({ profile: P, profileStatus: status, status: "EVALUATED" });
  return { findings, evaluations };
}
