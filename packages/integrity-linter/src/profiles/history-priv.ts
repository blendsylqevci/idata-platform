/**
 * HISTORY-PRIV — historia e git-it & politika e privatësisë (advisory, FAIL-CLOSED).
 *
 * Hardening II (§2/§3/§4/§5):
 *  - ÇDO commit në merge-base..head skanohet VEÇMAS (introduced-then-removed mbetet);
 *  - policy path/mode/scope zbatohet mbi TË GJITHA entries PARA çdo content access
 *    (provohet me call-order spy test); rename = D+A (pa -M);
 *  - private Add/Modify/Typechange → ERROR `[REDACTED_PATH]` pa blob read;
 *    private Delete = cleanup i lejuar; Delete privat + Add publik i njëjtit commit
 *    → HP-PRIVATE-RELOCATION; scope whitelist NUK e anulon privacy policy;
 *  - merge commit → INCOMPLETE_SCAN → exit 2; mode të lejuara vetëm 100644/100755;
 *  - binary/oversize (old OSE new blob) → REVIEW + evaluation NOT_EVALUATED me
 *    check="content" e reason kod fiks — run-i NUK quhet content-complete;
 *  - drift per-commit (version/count/status/set/phase-kind) kundër authority
 *    snapshot-it tracked të të NJËJTIT commit;
 *  - Human Gate order edhe multiline (lista/line-wrap) mbi blloqe added-lines.
 */

import type { Finding, GitReader, ProfileEvaluation, PullRequestEvent, RawEntry } from "../types.js";
import { ConfigError, ScanError } from "../types.js";
import { profileStatus } from "../profile-registry.js";
import { ALLOWED_FILE_MODES, NULL_MODE, evaluatePath } from "../path-policy.js";
import { buildContentRules, type RuleContext } from "./history-rules.js";
import { defaultAuthorityClasses, isHistoryLabeledRow, type AuthorityClass } from "../authority-classes.js";
import { extractChainText, chainOrderViolations } from "./doc-core.js";

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

export interface HistoryPrivOptions {
  /** Klasat e drift-it per-commit (default = klasat reale; testet injektojnë sintetike). */
  driftClasses?: AuthorityClass[];
}

function globalized(re: RegExp): RegExp {
  return new RegExp(re.source, re.flags.includes("g") ? re.flags : re.flags + "g");
}

export function runHistoryPriv(
  reader: GitReader,
  baseSha: string | undefined,
  headSha: string | undefined,
  event: PullRequestEvent | undefined,
  options: HistoryPrivOptions = {},
): { findings: Finding[]; evaluations: ProfileEvaluation[] } {
  const status = profileStatus(P);
  const findings: Finding[] = [];
  const evaluations: ProfileEvaluation[] = [];
  const contentNotEvaluated = new Set<string>(); // kode fikse: BINARY_BLOB / OVERSIZE_BLOB

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
  const driftClasses = options.driftClasses ?? defaultAuthorityClasses();

  for (const sha of commits) {
    const short = sha.slice(0, 8);

    // ── MERGE COMMIT: INCOMPLETE_SCAN → exit 2 (kurrë REVIEW+continue) ──
    if (reader.parents(sha).length > 1) {
      throw new ScanError("INCOMPLETE_SCAN:MERGE_COMMIT_IN_RANGE");
    }

    // ── FAZA 1: metadata + POLICY mbi TË GJITHA entries PARA content access ──
    const entries = reader.rawEntries(sha);
    const approved: RawEntry[] = [];
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
        continue; // ASNJË qasje në blob — as scope check (whitelist s'e anulon policy-n)
      }
      const modeToCheck = entry.status === "D" ? entry.oldMode : entry.newMode;
      if (!ALLOWED_FILE_MODES.has(modeToCheck) && modeToCheck !== NULL_MODE) {
        throw new ScanError("INCOMPLETE_SCAN:UNSUPPORTED_FILE_MODE");
      }
      if (entry.status === "D") continue;

      publicAddition = true;
      if (event?.body !== undefined && scope.present && !scope.malformed && !pathInScope(entry.path, scope.allowed)) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "HP-OUT-OF-SCOPE", category: "scope",
          locator: entry.path, commit: short,
        });
      }
      approved.push(entry);
    }

    if (privateDeletion && publicAddition) {
      findings.push({
        profile: P, profileStatus: status, severity: "REVIEW",
        ruleId: "HP-PRIVATE-RELOCATION", category: "privacy",
        locator: REDACTED, commit: short,
      });
    }

    // authority snapshot per-commit (§2H) — tracked Git objects të të njëjtit commit
    const snapshotValues = new Map<string, { current: string; cls: AuthorityClass }>();
    for (const cls of driftClasses) {
      if (!reader.hasFile(sha, cls.authorityFile)) continue;
      const norm = cls.normalize ?? ((v: string) => v);
      const m = globalized(cls.authorityPattern).exec(reader.showFile(sha, cls.authorityFile));
      if (m?.[1] !== undefined) snapshotValues.set(cls.id, { current: norm(m[1]), cls });
    }

    // ── FAZA 2: content vetëm për blob-et e APROVUARA ──
    const snapshotCache = new Map<string, string | undefined>();
    const getSnapshot = (path: string): string | undefined => {
      if (!snapshotCache.has(path)) {
        snapshotCache.set(path, reader.hasFile(sha, path) ? reader.showFile(sha, path) : undefined);
      }
      return snapshotCache.get(path);
    };

    for (const entry of approved) {
      // §5: kontrollo TË DY blob-et relevante — edhe old blob-i oversize/binar
      // e bën content coverage të paplotë (diff-i varet prej tij).
      if (entry.status === "M" && entry.oldOid !== undefined && !/^0+$/.test(entry.oldOid)) {
        const oldSize = reader.blobSizeByOid(entry.oldOid);
        if (oldSize > OVERSIZE_BYTES) {
          findings.push({
            profile: P, profileStatus: status, severity: "REVIEW",
            ruleId: "HP-OVERSIZE-BLOB", category: "filesystem-boundary",
            locator: entry.path, commit: short,
          });
          contentNotEvaluated.add("OVERSIZE_BLOB");
          continue;
        }
      }
      const size = reader.blobSizeByOid(entry.newOid);
      if (size > OVERSIZE_BYTES) {
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "HP-OVERSIZE-BLOB", category: "filesystem-boundary",
          locator: entry.path, commit: short,
        });
        contentNotEvaluated.add("OVERSIZE_BLOB");
        continue;
      }
      const content = reader.addedContent(sha, entry.path);
      if (content.binary) {
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "HP-BINARY-BLOB", category: "filesystem-boundary",
          locator: entry.path, commit: short,
        });
        contentNotEvaluated.add("BINARY_BLOB");
        continue;
      }

      // rregullat e klasave (kontekst + snapshot column-aware)
      for (const { line, text } of content.lines.map((l) => ({ line: l.line, text: l.text }))) {
        const ctx: RuleContext = { line: text, path: entry.path, lineNo: line, snapshot: () => getSnapshot(entry.path) };
        for (const rule of contentRules) {
          if (rule.test(ctx)) {
            findings.push({
              profile: P, profileStatus: status, severity: rule.severity,
              ruleId: rule.ruleId, category: rule.category,
              locator: `${entry.path}:${line}`, commit: short,
            });
          }
        }
        // §2H drift per-commit kundër snapshot-it të të njëjtit commit
        for (const [id, { current, cls }] of snapshotValues) {
          if (entry.path === cls.authorityFile) continue;
          const consumer = cls.consumers[0];
          if (consumer === undefined) continue;
          if (isHistoryLabeledRow(text)) continue;
          const norm = cls.normalize ?? ((v: string) => v);
          const re = globalized(consumer.pattern);
          let cm: RegExpExecArray | null;
          while ((cm = re.exec(text)) !== null) {
            if (cm[1] === undefined) continue;
            if (consumer.excludeBefore !== undefined &&
                consumer.excludeBefore.test(text.slice(Math.max(0, cm.index - 24), cm.index))) {
              continue;
            }
            if (norm(cm[1]) !== current) {
              findings.push({
                profile: P, profileStatus: status, severity: "WARNING",
                ruleId: `HP-DRIFT-${cls.kind.toUpperCase()}`, category: "drift",
                locator: `${entry.path}:${line} (${id})`, commit: short,
              });
            }
          }
        }
      }

      // §2I: Human Gate order edhe MULTILINE — blloqe rreshtash added KONSEKUTIVË
      const blocks: Array<Array<{ line: number; text: string }>> = [];
      let cur: Array<{ line: number; text: string }> = [];
      for (const l of content.lines) {
        const last = cur[cur.length - 1];
        if (last !== undefined && l.line !== last.line + 1) {
          if (cur.length > 0) blocks.push(cur);
          cur = [];
        }
        cur.push(l);
      }
      if (cur.length > 0) blocks.push(cur);
      for (const block of blocks) {
        const chainText = extractChainText(block.map((b) => b.text));
        for (const v of chainOrderViolations(chainText)) {
          findings.push({
            profile: P, profileStatus: status, severity: "WARNING",
            ruleId: "HP-GATE-ORDER", category: "sequence",
            locator: `${entry.path}:${block[0]?.line ?? 0} (${v.later} para ${v.earlier})`,
            commit: short,
          });
        }
      }
    }
  }

  // §5: coverage e paplotë e content-it raportohet EKSPLICIT — kurrë "scan complete"
  for (const reason of [...contentNotEvaluated].sort()) {
    evaluations.push({
      profile: P, profileStatus: status, status: "NOT_EVALUATED",
      check: "content", reason,
    });
  }
  evaluations.push({ profile: P, profileStatus: status, status: "EVALUATED" });
  return { findings, evaluations };
}
