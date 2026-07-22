/**
 * HISTORY-PRIV — historia e git-it & politika e privatësisë (advisory, FAIL-CLOSED).
 *
 * Hardening III:
 *  §1 — drift per-commit mbi SNAPSHOT-in FINAL të secilit commit me validator të
 *       PËRBASHKËT: authority missing (pas aktivizimit ONCE_PRESENT) / zero match /
 *       vlera ambige → ScanError; TË GJITHË ConsumerSpec-at path-bound; defektet
 *       dedup-ohen kundrejt snapshot-it të PARENT-it (introduced-then-removed mbetet);
 *  §2 — content i pa-skanueshëm (binary/oversize, old OSE new blob) → ScanError /
 *       exit 2 — ASNJË agregat EVALUATED për coverage të paplotë;
 *  §3 — relocation privat→publik ndiqet me OID nëpër TËRË range-in (in-memory set;
 *       zero blob reads private; output vetëm [REDACTED_PATH]);
 *  §4 — chain-i invariant (prani + rend) vlerësohet per-commit mbi sipërfaqet
 *       chainRequired — commit-i ndërmjetës që e prish = finding historik.
 */

import type { Finding, GitReader, ProfileEvaluation, PullRequestEvent, RawEntry } from "../types.js";
import { ConfigError, ScanError } from "../types.js";
import { profileStatus } from "../profile-registry.js";
import { ALLOWED_FILE_MODES, NULL_MODE, evaluatePath } from "../path-policy.js";
import { buildContentRules, type RuleContext } from "./history-rules.js";
import {
  AuthorityError, DEFAULT_CHAIN_REQUIRED, defaultAuthorityClasses, extractAuthorityValue,
  isHistoryLabeledMatchInDoc, type AuthorityClass,
} from "../authority-classes.js";
import { chainOrderViolations, evaluateChainPresence, extractChainClaims, projectActiveMarkdown } from "./doc-core.js";
import { hasObfuscatedMatch } from "../machine-token.js";

const P = "HISTORY-PRIV" as const;
const REDACTED = "[REDACTED_PATH]";
const OVERSIZE_BYTES = 1_000_000;

export interface ScopeBlock {
  present: boolean;
  malformed: boolean;
  allowed: string[];
}

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
  /** Sipërfaqet chainRequired per-commit (§4). */
  chainRequiredSurfaces?: string[];
}

interface SnapshotReaderAt {
  has(path: string): boolean;
  read(path: string): string;
}

/**
 * P-IV §3 — klasifikim binar/encoding mbi BYTE-t RAW të blob-it (pas size gate):
 * NUL byte → BINARY_BLOB; UTF-8 invalid → INVALID_ENCODING — të dyja exit 2,
 * i PAVARUR nga .gitattributes/extension/diff classification. Pa content/OID
 * në gabim.
 */
function assertScannableBlob(buf: Buffer): void {
  if (buf.includes(0)) throw new ScanError("INCOMPLETE_SCAN:BINARY_BLOB");
  try {
    new TextDecoder("utf-8", { fatal: true }).decode(buf);
  } catch {
    throw new ScanError("INCOMPLETE_SCAN:INVALID_ENCODING");
  }
}

/**
 * Correction pass (Codex+Fable) — VALIDATOR EKSPLICIT i tranzicionit
 * status/mode, i thirrur PARA çdo blob/content access (pas path-policy):
 *  - statuset e njohura: VETËM A / D / M / T; status tjetër →
 *    `GIT_STATUS_UNSUPPORTED` (kod fiks, pa path);
 *  - A: oldMode = NULL_MODE dhe newMode ∈ ALLOWED_FILE_MODES;
 *  - D: oldMode ∈ ALLOWED_FILE_MODES dhe newMode = NULL_MODE;
 *  - M: oldMode ∈ ALLOWED_FILE_MODES dhe newMode ∈ ALLOWED_FILE_MODES
 *    (100644 ↔ 100755 i mbështetur — Git e raporton si M);
 *  - T (type-change, p.sh. symlink 120000 → 100644 ose regular → gitlink):
 *    GJITHMONË fail-closed `UNSUPPORTED_FILE_MODE` — defekti i mëparshëm
 *    kontrollonte vetëm newMode dhe e linte `120000→100644` të kalonte me
 *    exit 0; zero blobSizeByOid/blobByOid/addedContent për T;
 *  - tuple strukturorisht i pamundur (A me oldMode≠NULL, D me newMode≠NULL,
 *    M me NULL në njërën anë) → `INVALID_MODE_TRANSITION`.
 */
export function validateEntryTransition(entry: RawEntry): void {
  const oldOk = ALLOWED_FILE_MODES.has(entry.oldMode);
  const newOk = ALLOWED_FILE_MODES.has(entry.newMode);
  switch (entry.status) {
    case "T":
      throw new ScanError("INCOMPLETE_SCAN:UNSUPPORTED_FILE_MODE");
    case "A":
      if (!newOk) throw new ScanError("INCOMPLETE_SCAN:UNSUPPORTED_FILE_MODE");
      if (entry.oldMode !== NULL_MODE) throw new ScanError("INCOMPLETE_SCAN:INVALID_MODE_TRANSITION");
      return;
    case "D":
      if (!oldOk) throw new ScanError("INCOMPLETE_SCAN:UNSUPPORTED_FILE_MODE");
      if (entry.newMode !== NULL_MODE) throw new ScanError("INCOMPLETE_SCAN:INVALID_MODE_TRANSITION");
      return;
    case "M":
      if (entry.oldMode === NULL_MODE || entry.newMode === NULL_MODE) {
        throw new ScanError("INCOMPLETE_SCAN:INVALID_MODE_TRANSITION");
      }
      if (!oldOk || !newOk) throw new ScanError("INCOMPLETE_SCAN:UNSUPPORTED_FILE_MODE");
      return;
    default:
      throw new ScanError("INCOMPLETE_SCAN:GIT_STATUS_UNSUPPORTED");
  }
}

/**
 * Vlerëson defektet e drift-it të një klase mbi NJË snapshot (semantikë IDENTIKE
 * me DOC-CORE — validator/kontratë e përbashkët §1). Kthen firma të brendshme
 * defektesh (vlerat KURRË s'dalin në output) + locator-ë të sigurt.
 */
function evaluateClassOnSnapshot(
  cls: AuthorityClass,
  snap: SnapshotReaderAt,
): Map<string, string> {
  const defects = new Map<string, string>(); // firma → locator i sigurt
  const authorityText = projectActiveMarkdown(snap.read(cls.authorityFile));
  const current = extractAuthorityValue(authorityText, cls); // zero/ambige → AuthorityError
  const norm = cls.normalize ?? ((v: string) => v);
  for (const consumer of cls.consumers) {
    if (!snap.has(consumer.file)) {
      if (consumer.requirement === "REQUIRED_CURRENT") {
        defects.set(`${cls.id}|${consumer.file}|missing-file`, `${consumer.file} (${cls.id})`);
      }
      continue; // SCAN_IF_PRESENT: mungesa e file-it lejohet nga metadata
    }
    const text = projectActiveMarkdown(snap.read(consumer.file));
    const lines = text.split("\n");
    let currentSeenActive = false;
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i] ?? "";
      // P-V F1/F2: token i mbrojtur i pranishëm VETËM i obfuskuar (zero-width/
      // homoglyph) NUK kalon si "no match" — finding i dedikuar, pa content.
      if (hasObfuscatedMatch(line, consumer.pattern)) {
        defects.set(`${cls.id}|${consumer.file}|obfuscated:${i + 1}`, `${consumer.file}:${i + 1} (${cls.id})`);
      }
      const re = new RegExp(consumer.pattern.source, consumer.pattern.flags.includes("g") ? consumer.pattern.flags : consumer.pattern.flags + "g");
      let cm: RegExpExecArray | null;
      while ((cm = re.exec(line)) !== null) {
        if (cm[1] === undefined) continue;
        if (consumer.excludeBefore !== undefined &&
            consumer.excludeBefore.test(line.slice(Math.max(0, cm.index - 24), cm.index))) {
          continue;
        }
        const seen = norm(cm[1]);
        const labeled = isHistoryLabeledMatchInDoc(text, i + 1, cm.index, cm.index + cm[0].length);
        if (seen === current) {
          if (!labeled) currentSeenActive = true; // historical row s'plotëson REQUIRED aktiv
        } else if (!labeled) {
          // firma mban vlerën vetëm PËR KRAHASIM të brendshëm; locator pa vlerë
          defects.set(`${cls.id}|${consumer.file}|stale|${seen}`, `${consumer.file}:${i + 1} (${cls.id})`);
        }
      }
    }
    if (consumer.requirement === "REQUIRED_CURRENT" && !currentSeenActive) {
      defects.set(`${cls.id}|${consumer.file}|missing`, `${consumer.file} (${cls.id})`);
    }
  }
  return defects;
}

/** Firma të defekteve të chain-it mbi një snapshot (§4). */
function evaluateChainOnSnapshot(surfaces: string[], snap: SnapshotReaderAt): Map<string, string> {
  const defects = new Map<string, string>();
  for (const file of surfaces) {
    if (!snap.has(file)) continue; // sipërfaqja mund të mos ekzistojë ende në histori
    const evaln = evaluateChainPresence(snap.read(file));
    if (evaln.noActiveChain) {
      defects.set(`chain|${file}|noactive`, `${file} (asnjë chain aktiv)`);
    } else {
      for (const missing of evaln.missing) {
        defects.set(`chain|${file}|missing:${missing}`, `${file} (mungon: ${missing})`);
      }
      for (const v of evaln.violations) {
        defects.set(`chain|${file}|order:${v.later}<${v.earlier}`, `${file} (${v.later} para ${v.earlier})`);
      }
    }
  }
  return defects;
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

  const base = baseSha ?? event?.baseSha;
  const head = headSha ?? event?.headSha;
  if (base === undefined || head === undefined) {
    throw new ConfigError("RANGE_MISSING");
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
  const chainSurfaces = options.chainRequiredSurfaces ?? DEFAULT_CHAIN_REQUIRED;

  // Metadata e TËRË range-it materializohet para çdo content access. Kjo
  // mbyll relocation-in copy-before-delete dhe ruan policy-before-blob.
  const commitEntries = commits.map((sha) => {
    if (reader.parents(sha).length > 1) {
      throw new ScanError("INCOMPLETE_SCAN:MERGE_COMMIT_IN_RANGE");
    }
    return { sha, entries: reader.rawEntries(sha) };
  });
  // OID-t private merren vetëm nga metadata (kurrë blob content): snapshot-i
  // bazë + të gjitha entry-t private në range. Kështu kapet edhe copy pa
  // delete dhe copy-before-delete, pa cenuar policy-before-blob.
  const knownPrivateOids = new Set<string>();
  for (const entry of reader.treeEntries(mergeBase)) {
    if (evaluatePath(entry.path) === "BLOCKED") knownPrivateOids.add(entry.oid);
  }
  for (const { entries } of commitEntries) {
    for (const entry of entries) {
      if (evaluatePath(entry.path) !== "BLOCKED") continue;
      if (entry.status === "D" && !/^0+$/.test(entry.oldOid)) {
        knownPrivateOids.add(entry.oldOid);
      } else if (!/^0+$/.test(entry.newOid)) {
        knownPrivateOids.add(entry.newOid);
      }
    }
  }
  const hasKnownPrivateSource = knownPrivateOids.size > 0;

  const snapAt = (sha: string): SnapshotReaderAt => {
    const cache = new Map<string, string>();
    const hasCache = new Map<string, boolean>();
    return {
      has(path: string): boolean {
        if (!hasCache.has(path)) hasCache.set(path, reader.hasFile(sha, path));
        return hasCache.get(path) === true;
      },
      read(path: string): string {
        const c = cache.get(path);
        if (c !== undefined) return c;
        const t = reader.showFile(sha, path);
        cache.set(path, t);
        return t;
      },
    };
  };

  // §1: activation ONCE_PRESENT per klasë + gjendja parent e defekteve për dedup.
  // Activation ONCE_PRESENT (Hardening IV §2): klasa aktivizohet sapo authority
  // FILE EKZISTON në snapshot — JO kur vlera lexohet për herë të parë (ONCE_VALID
  // do të tolerontе shfaqje bosh/ambige). Para aktivizimit lejohet nga metadata
  // VETËM mungesa e file-it; PAS aktivizimit bosh / zero match / ambige /
  // unreadable / i hequr = ScanError (exit 2). Vlera problematike s'printohet.
  const activated = new Map<string, boolean>();
  // §4A: numri i përputhjeve të anchor-it strukturor në authority file
  // (undefined = klasa s'ka anchor; aktivizimi default = prania e FILE-it).
  const anchorCount = (cls: AuthorityClass, snap: SnapshotReaderAt): number | undefined => {
    if (cls.activationAnchor === undefined) return undefined;
    const flags = cls.activationAnchor.flags.includes("g")
      ? cls.activationAnchor.flags : cls.activationAnchor.flags + "g";
    const re = new RegExp(cls.activationAnchor.source, flags);
    let n = 0;
    while (re.exec(projectActiveMarkdown(snap.read(cls.authorityFile))) !== null) n += 1;
    return n;
  };
  const baseSnap = snapAt(mergeBase);
  let parentDriftDefects = new Map<string, string>();
  for (const cls of driftClasses) {
    if (!baseSnap.has(cls.authorityFile)) continue;
    const n = anchorCount(cls, baseSnap);
    if (n === 0) continue; // anchor s'ka lindur në bazë → NOT_APPLICABLE (§4A.7)
    activated.set(cls.id, true); // prania në bazë aktivizon nga commit-i i parë
    try {
      const d = evaluateClassOnSnapshot(cls, baseSnap);
      for (const [k, v] of d) parentDriftDefects.set(k, v);
    } catch (err) {
      if (!(err instanceof AuthorityError)) throw err;
      // baza (jashtë range-ut të skanuar) e palexueshme: s'ka dedup baseline;
      // commit-i i PARË që e trashëgon gjendjen dështon me exit 2 më poshtë.
    }
  }
  let parentChainDefects = evaluateChainOnSnapshot(chainSurfaces, baseSnap);

  for (const { sha, entries } of commitEntries) {
    const short = sha.slice(0, 8);

    // PASS 2: policy path/mode/scope mbi TË GJITHA entries PARA content access
    const approved: RawEntry[] = [];
    for (const entry of entries) {
      const verdict = evaluatePath(entry.path);
      if (verdict === "BLOCKED") {
        if (entry.status === "D") continue; // cleanup i lejuar — zero blob read
        findings.push({
          profile: P, profileStatus: status, severity: "ERROR",
          ruleId: "HP-PRIVATE-PATH", category: "privacy",
          locator: REDACTED, commit: short,
        });
        continue;
      }
      // Validator i plotë status/mode — T dhe çdo tuple i pamundur dështojnë
      // KËTU, para çdo blob/content access (correction pass Codex+Fable).
      validateEntryTransition(entry);
      if (entry.status === "D") continue;

      // §3: relocation exact në TËRË range-in, edhe copy-before-delete.
      if (knownPrivateOids.has(entry.newOid)) {
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "HP-PRIVATE-RELOCATION", category: "privacy",
          locator: REDACTED, commit: short,
        });
      } else if (
        hasKnownPrivateSource &&
        (entry.status === "A" || (entry.status === "M" && entry.oldOid !== entry.newOid))
      ) {
        // Pa lexuar private blob-in, similarity nuk provohet dot. Prandaj çdo
        // public content-change ndërsa ekziston një private source dërgohet për
        // review — edhe kur private source mbahet dhe s'ka delete në range.
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "HP-PRIVATE-RELOCATION-UNVERIFIED", category: "privacy",
          locator: REDACTED, commit: short,
        });
      }
      if (event?.body !== undefined && scope.present && !scope.malformed && !pathInScope(entry.path, scope.allowed)) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "HP-OUT-OF-SCOPE", category: "scope",
          locator: entry.path, commit: short,
        });
      }
      approved.push(entry);
    }

    const snap = snapAt(sha);

    // ── CONTENT — §2: e pa-skanueshme = SKANIM I PAPLOTË = exit 2 ──
    for (const entry of approved) {
      const hasOld = entry.status === "M" && !/^0+$/.test(entry.oldOid);
      const oldSize = hasOld ? reader.blobSizeByOid(entry.oldOid) : 0;
      const newSize = reader.blobSizeByOid(entry.newOid);
      if (oldSize > OVERSIZE_BYTES) {
        throw new ScanError("INCOMPLETE_SCAN:OVERSIZE_BLOB");
      }
      if (newSize > OVERSIZE_BYTES) {
        throw new ScanError("INCOMPLETE_SCAN:OVERSIZE_BLOB");
      }
      // P-IV §3: klasifikimi bëhet mbi blob-in RAW sipas OID (brenda cap),
      // JO mbi diff output — .gitattributes s'mund ta shndërrojë binarin në
      // "tekst" e as tekstin valid në "binar" të pa-skanuar.
      if (hasOld) assertScannableBlob(reader.blobByOid(entry.oldOid));
      assertScannableBlob(reader.blobByOid(entry.newOid));
      const content = reader.addedContent(sha, entry.path);
      if (content.binary) {
        // me --text s'duhet të ndodhë kurrë; nëse ndodh = strukturë e papritur
        throw new ScanError("INCOMPLETE_SCAN:BINARY_BLOB");
      }
      const contentChanged =
        (entry.status === "A" && newSize > 0) ||
        (entry.status === "M" && entry.oldOid !== entry.newOid);
      if (contentChanged && !content.hadHunk) {
        throw new ScanError("INCOMPLETE_SCAN:DIFF_HUNK_MISSING");
      }

      for (const { line, text } of content.lines) {
        const ctx: RuleContext = { line: text, path: entry.path, lineNo: line, snapshot: () => (snap.has(entry.path) ? snap.read(entry.path) : undefined) };
        for (const rule of contentRules) {
          if (rule.test(ctx)) {
            findings.push({
              profile: P, profileStatus: status, severity: rule.severity,
              ruleId: rule.ruleId, category: rule.category,
              locator: `${entry.path}:${line}`, commit: short,
            });
          }
        }
      }

      // rend zinxhiri mbi blloqe added-lines KONSEKUTIVË (plotësues i §4 snapshot)
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
        // §7 P-IV: rendi PER-CLAIM — bullets të pavarura nuk krahasohen mes tyre
        for (const claim of extractChainClaims(block.map((b) => b.text))) {
          for (const v of chainOrderViolations(claim)) {
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

    // ── §1 DRIFT per-commit mbi SNAPSHOT-in FINAL (dedup kundrejt parent-it) ──
    const currentDriftDefects = new Map<string, string>();
    for (const cls of driftClasses) {
      const exists = snap.has(cls.authorityFile);
      if (!exists) {
        if (activated.get(cls.id) === true) {
          // pas aktivizimit ONCE_PRESENT mungesa = dështim (activation s'fsheh commits)
          throw new ScanError("INCOMPLETE_SCAN:AUTHORITY_MISSING");
        }
        continue; // mungesa e file-it lejohet vetëm para aktivizimit (§2 P-IV)
      }
      const n = anchorCount(cls, snap);
      if (n !== undefined && n > 1) {
        // §4A.9: anchor i duplikuar = surface ambig — exit 2 (edhe në lindje)
        throw new ScanError("INCOMPLETE_SCAN:AUTHORITY_ANCHOR_DUPLICATE");
      }
      if (activated.get(cls.id) !== true) {
        if (n === 0) {
          continue; // seksioni/anchor-i s'ka lindur ende → NOT_APPLICABLE (§4A.7)
        }
        activated.set(cls.id, true); // ONCE_PRESENT: PRANIA aktivizon — edhe kur
        // shfaqja e parë është bosh/ambige; leximi më poshtë dështon me exit 2.
      } else if (n === 0) {
        // §4A.9 sticky: heqja e anchor-it pas aktivizimit = exit 2, edhe me vlerë prezente
        throw new ScanError("INCOMPLETE_SCAN:AUTHORITY_ANCHOR_MISSING");
      }
      let clsDefects: Map<string, string>;
      try {
        clsDefects = evaluateClassOnSnapshot(cls, snap);
      } catch (err) {
        if (err instanceof AuthorityError) throw new ScanError(err.code); // exit 2
        throw err;
      }
      for (const [k, v] of clsDefects) currentDriftDefects.set(k, v);
    }
    for (const [sig, locator] of currentDriftDefects) {
      if (parentDriftDefects.has(sig)) continue; // ekzistonte para këtij commit-i
      const cls = driftClasses.find((c) => sig.startsWith(`${c.id}|`));
      const kind = cls?.kind.toUpperCase() ?? "VALUE";
      const isMissing = sig.endsWith("|missing") || sig.endsWith("|missing-file");
      const isObfuscated = sig.includes("|obfuscated:");
      findings.push({
        profile: P, profileStatus: status, severity: "WARNING",
        ruleId: isObfuscated ? "HP-MACHINE-TOKEN-OBFUSCATED"
          : isMissing ? "HP-CURRENT-VALUE-MISSING" : `HP-DRIFT-${kind}`,
        category: isObfuscated ? "obfuscation" : "drift", locator, commit: short,
      });
    }
    parentDriftDefects = currentDriftDefects;

    // ── §4 CHAIN per-commit mbi snapshot (prani + rend; dedup kundrejt parent-it) ─
    const currentChainDefects = evaluateChainOnSnapshot(chainSurfaces, snap);
    for (const [sig, locator] of currentChainDefects) {
      if (parentChainDefects.has(sig)) continue;
      findings.push({
        profile: P, profileStatus: status, severity: "WARNING",
        ruleId: sig.includes("|order:") ? "HP-GATE-ORDER" : "HP-CHAIN-INCOMPLETE",
        category: "sequence", locator, commit: short,
      });
    }
    parentChainDefects = currentChainDefects;
  }

  evaluations.push({ profile: P, profileStatus: status, status: "EVALUATED" });
  return { findings, evaluations };
}
