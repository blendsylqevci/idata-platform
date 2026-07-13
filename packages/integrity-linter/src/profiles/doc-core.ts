/**
 * DOC-CORE — integriteti i dokumentacionit canonical & governance (advisory, FAIL-CLOSED).
 *
 * Kontrata bidirectional authority → consumers (§1, Hardening II):
 *  - REQUIRED_CURRENT: vlera aktuale DUHET të jetë e pranishme (mungesa e saj =
 *    DC-CURRENT-VALUE-MISSING); çdo vlerë stale aktive = DC-OLD-VALUE-DRIFT;
 *  - SCAN_IF_PRESENT: mungesa e referencës lejohet; nëse është, duhet current;
 *  - etiketa historike vlen VETËM same-row; rresht i etiketuar me vlerën current
 *    NUK e plotëson kërkesën REQUIRED_CURRENT (current duhet AKTIV);
 *  - authority missing/unreadable/AMBIGUOUS → ScanError → exit 2 (kurrë WARNING);
 *  - PR body: SCAN_IF_PRESENT kundrejt authority-ve; event/body që mungon →
 *    evaluation NOT_EVALUATED me check="pr-body" — kurrë PASS i fabrikuar.
 *
 * Linter-i NUK jep opinion ligjor dhe NUK vendos semantikën e asnjë neni.
 */

import type { Finding, GitReader, ProfileEvaluation, PullRequestEvent } from "../types.js";
import { ScanError } from "../types.js";
import { profileStatus } from "../profile-registry.js";
import {
  defaultAuthorityClasses, isHistoryLabeledRow,
  GATE, INDEX, LEDGER, PM, POC5README, ROADMAP, SSOT, TRACKER,
  type AuthorityClass,
} from "../authority-classes.js";

const P = "DOC-CORE" as const;

export interface DocCoreSurfaces {
  orderSurfaces: string[];
  trackerFile?: string;
  milestoneSurfaces: string[];
  authorities: AuthorityClass[];
  approvedRoleWords: string[];
}

/** Fjalët e aprovuara të roleve (word-boundary; KURRË emra personalë realë). */
export const APPROVED_ROLE_WORDS = [
  "pronari", "codex", "claude", "pm", "group", "owner", "human", "gate", "finance",
  "kontabilisti", "icode", "atk", "kabi", "planet", "teknologu", "fabrika", "laboratori",
  "jurist", "aip", "tech", "dev", "admin", "management", "operations", "sales",
  "logistics", "hardware", "vendor", "klienti", "burimi", "tier",
];

export function defaultSurfaces(): DocCoreSurfaces {
  return {
    orderSurfaces: [INDEX, ROADMAP, GATE, SSOT, PM, POC5README, LEDGER],
    trackerFile: TRACKER,
    milestoneSurfaces: [INDEX, ROADMAP, SSOT, PM],
    authorities: defaultAuthorityClasses(),
    approvedRoleWords: APPROVED_ROLE_WORDS,
  };
}

export { isHistoryLabeledRow as isHistoryLabeled };

const ORDER_TOKENS: Array<{ key: string; res: RegExp[] }> = [
  { key: "step-b", res: [/Step B\b/] },
  { key: "step-c", res: [/Step C\b/] },
  { key: "review", res: [/independent re-?review/i, /review(?:-t|t)? (?:i|të) pavarur/i, /independent review/i] },
  { key: "human-gate", res: [/HUMAN GATE/i] },
  { key: "preflight", res: [/PREFLIGHT/i] },
  { key: "tier-2", res: [/Tier 2\b/] },
];

function firstIndex(paragraph: string, res: RegExp[]): number {
  let best = -1;
  for (const re of res) {
    const m = re.exec(paragraph);
    if (m && (best === -1 || m.index < best)) best = m.index;
  }
  return best;
}

/**
 * Nxjerr "chain text" nga një grup rreshtash — vetëm segmentet që janë
 * pretendime reale rendi (→ / "pastaj" / artikuj liste / bullets), me
 * parenteza të hequra, tabela/historikë të përjashtuar dhe subjekte 48-kar.
 * E përdorin edhe DOC-CORE (paragrafët) edhe HISTORY-PRIV (blloqet e added-lines).
 */
export function extractChainText(lines: string[]): string {
  const chainParts: string[] = [];
  for (const rawLine of lines) {
    if (rawLine.trimStart().startsWith("|")) continue; // tabelat s'janë zinxhirë
    if (isHistoryLabeledRow(rawLine)) continue;
    const line = rawLine.replace(/\([^)]*\)/g, " ");
    const isNumbered = /^\s*(?:\d+[a-z]?\.|[-*])\s/.test(line);
    const markers: number[] = [];
    for (const mk of line.matchAll(/→|\bpastaj\b/g)) markers.push(mk.index ?? 0);
    if (isNumbered && markers.length === 0) {
      const subject = line.slice(0, 48);
      if (!/(\bnuk\b|\bs'|\bjo\b|vetëm pas)/i.test(subject)) chainParts.push(subject);
    } else if (markers.length > 0) {
      const keep = new Array<boolean>(line.length).fill(false);
      for (const mi of markers) {
        for (let k = Math.max(0, mi - 48); k < Math.min(line.length, mi + 48); k++) keep[k] = true;
      }
      let windowed = "";
      for (let k = 0; k < line.length; k++) windowed += keep[k] === true ? line[k] : " ";
      chainParts.push(windowed);
    }
  }
  return chainParts.join("\n");
}

export function chainOrderViolations(chainText: string): Array<{ later: string; earlier: string }> {
  const present = ORDER_TOKENS
    .map((t) => ({ key: t.key, idx: firstIndex(chainText, t.res) }))
    .filter((t) => t.idx >= 0);
  const out: Array<{ later: string; earlier: string }> = [];
  for (let i = 1; i < present.length; i++) {
    const prev = present[i - 1];
    const cur = present[i];
    if (prev !== undefined && cur !== undefined && cur.idx < prev.idx) {
      out.push({ later: cur.key, earlier: prev.key });
    }
  }
  return out;
}

/** Tokenizim i owner-cell: ndarje në separatorë; çdo token kërkon ≥1 fjalë roli me kufij fjale. */
export function ownerCellUnknownTokens(cell: string, approvedWords: string[]): number {
  const tokens = cell.split(/[/,+;·]|→|\bdhe\b/i).map((t) => t.trim()).filter((t) => t !== "" && t !== "—" && t !== "-");
  let unknown = 0;
  for (const token of tokens) {
    const ok = approvedWords.some((w) => new RegExp(`(^|[^\\p{L}])${w}([^\\p{L}]|$)`, "iu").test(token));
    if (!ok) unknown += 1;
  }
  return unknown;
}

function globalized(re: RegExp): RegExp {
  return new RegExp(re.source, re.flags.includes("g") ? re.flags : re.flags + "g");
}

export function runDocCore(
  reader: GitReader,
  docsRef: string,
  surfaces: DocCoreSurfaces,
  event: PullRequestEvent | undefined,
): { findings: Finding[]; evaluations: ProfileEvaluation[] } {
  const status = profileStatus(P);
  const findings: Finding[] = [];
  const evaluations: ProfileEvaluation[] = [];
  const fileCache = new Map<string, string>();
  const readDoc = (path: string): string => {
    const cached = fileCache.get(path);
    if (cached !== undefined) return cached;
    if (!reader.hasFile(docsRef, path)) throw new ScanError("INCOMPLETE_SCAN:SURFACE_MISSING");
    const text = reader.showFile(docsRef, path);
    fileCache.set(path, text);
    return text;
  };
  const readDocIfPresent = (path: string): string | undefined => {
    const cached = fileCache.get(path);
    if (cached !== undefined) return cached;
    if (!reader.hasFile(docsRef, path)) return undefined;
    const text = reader.showFile(docsRef, path);
    fileCache.set(path, text);
    return text;
  };

  // ── 1) kontrata bidirectional authority → consumers ─────────────────────
  for (const cls of surfaces.authorities) {
    const authorityText = readDoc(cls.authorityFile); // missing → exit 2
    const norm = cls.normalize ?? ((v: string) => v);
    const values = new Set<string>();
    const are = globalized(cls.authorityPattern);
    let am: RegExpExecArray | null;
    while ((am = are.exec(authorityText)) !== null) {
      if (am[1] !== undefined) values.add(norm(am[1]));
    }
    if (values.size === 0) throw new ScanError("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");
    if (values.size > 1) throw new ScanError("INCOMPLETE_SCAN:AUTHORITY_AMBIGUOUS");
    const current = [...values][0] as string;

    for (const consumer of cls.consumers) {
      const text = consumer.requirement === "REQUIRED_CURRENT"
        ? readDoc(consumer.file)
        : readDocIfPresent(consumer.file);
      if (text === undefined) continue; // SCAN_IF_PRESENT: file-i mungon → lejohet
      const lines = text.split("\n");
      let currentSeenActive = false;
      let anyMatch = false;
      for (let i = 0; i < lines.length; i++) {
        const line = lines[i] ?? "";
        const cre = globalized(consumer.pattern);
        let cm: RegExpExecArray | null;
        while ((cm = cre.exec(line)) !== null) {
          if (cm[1] === undefined) continue;
          if (consumer.excludeBefore !== undefined &&
              consumer.excludeBefore.test(line.slice(Math.max(0, cm.index - 24), cm.index))) {
            continue; // guard kontekstual per-match (jo per-rresht)
          }
          anyMatch = true;
          const seen = norm(cm[1]);
          const labeled = isHistoryLabeledRow(line);
          if (seen === current) {
            if (!labeled) currentSeenActive = true; // vetëm current AKTIV plotëson kërkesën
          } else if (!labeled) {
            findings.push({
              profile: P, profileStatus: status, severity: "WARNING",
              ruleId: "DC-OLD-VALUE-DRIFT", category: "drift",
              locator: `${consumer.file}:${i + 1} (${cls.id})`,
            });
          }
        }
      }
      if (consumer.requirement === "REQUIRED_CURRENT" && !currentSeenActive) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "DC-CURRENT-VALUE-MISSING", category: "drift",
          locator: `${consumer.file} (${cls.id})${anyMatch ? " [vetëm stale/historike]" : ""}`,
        });
      }
    }
  }

  // ── 2) rendi invariant i zinxhirit SEF (per paragraf) ──────────────────
  for (const file of surfaces.orderSurfaces) {
    const text = readDoc(file);
    const paragraphs = text.split(/\n\s*\n/);
    let lineNo = 1;
    for (const para of paragraphs) {
      const paraLines = para.split("\n");
      const chainText = extractChainText(paraLines);
      for (const v of chainOrderViolations(chainText)) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "DC-GATE-ORDER", category: "sequence",
          locator: `${file}:${lineNo} (${v.later} para ${v.earlier})`,
        });
      }
      lineNo += paraLines.length + 1;
    }
  }

  // ── 3) milestone/gate në tracker që mungon nga sipërfaqet e rendit ─────
  if (surfaces.trackerFile !== undefined) {
    const tracker = readDoc(surfaces.trackerFile);
    const ids = new Set<string>();
    for (const mm of tracker.matchAll(/\b(GOV-\d{3}|HG-[A-Z][A-Z0-9-]{2,})\b/g)) {
      const id = mm[1];
      if (id !== undefined) ids.add(id);
    }
    for (const id of [...ids].sort()) {
      const presentSomewhere = surfaces.milestoneSurfaces.some((f) => readDoc(f).includes(id));
      if (!presentSomewhere) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "DC-MILESTONE-MISSING", category: "governance",
          locator: `${surfaces.trackerFile} (${id})`,
        });
      }
    }

    // ── 4) vocabulary rolesh — TOKENIZED word-boundary (jo substring bypass) ─
    const lines = tracker.split("\n");
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i] ?? "";
      if (!line.startsWith("| `")) continue;
      const cells = line.split("|").map((c) => c.trim());
      const ownerCell = cells[4];
      if (ownerCell === undefined || ownerCell === "" || ownerCell === "—") continue;
      if (ownerCellUnknownTokens(ownerCell, surfaces.approvedRoleWords) > 0) {
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "DC-ROLE-VOCAB", category: "privacy-review",
          locator: `${surfaces.trackerFile}:${i + 1}`,
        });
      }
    }
  }

  // ── 5) pretendime absolute — kontekstuale per-rresht ────────────────────
  for (const file of [...surfaces.orderSurfaces, ...(surfaces.trackerFile ? [surfaces.trackerFile] : [])]) {
    const text = readDoc(file);
    const lines = text.split("\n");
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i] ?? "";
      if (isHistoryLabeledRow(line)) continue;
      if (/closes[ -]on[ -]merge|mbyllet (?:me|në) merge/i.test(line) && !/(pa review|ndal|NUK|s'|\bjo\b|hiq)/i.test(line)) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "DC-CLOSES-ON-MERGE", category: "claims", locator: `${file}:${i + 1}`,
        });
      }
      if (/\b(exhaustiv\w*|exhaustive)\b/i.test(line) && !/(bounded|brenda|kufizuar|nuk|s'|\bjo\b)/i.test(line)) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "DC-ABSOLUTE-CLAIM", category: "claims", locator: `${file}:${i + 1}`,
        });
      }
    }
  }

  // ── 6) PR body kundrejt authority-ve (SCAN_IF_PRESENT — jo çdo authority
  //       kërkohet në body; body pa përmbajtje → NOT_EVALUATED, jo PASS) ────
  if (event?.body === undefined) {
    evaluations.push({
      profile: P, profileStatus: status, status: "NOT_EVALUATED",
      check: "pr-body", reason: "missing PR event/body",
    });
  } else {
    const bodyLines = event.body.split("\n");
    for (const cls of surfaces.authorities) {
      const authorityText = readDocIfPresent(cls.authorityFile);
      if (authorityText === undefined) continue;
      const norm = cls.normalize ?? ((v: string) => v);
      const am2 = globalized(cls.authorityPattern).exec(authorityText);
      const current = am2?.[1] !== undefined ? norm(am2[1]) : undefined;
      if (current === undefined) continue;
      const consumer0 = cls.consumers[0];
      if (consumer0 === undefined) continue;
      for (let i = 0; i < bodyLines.length; i++) {
        const line = bodyLines[i] ?? "";
        if (isHistoryLabeledRow(line)) continue;
        const re2 = globalized(consumer0.pattern);
        let cm: RegExpExecArray | null;
        while ((cm = re2.exec(line)) !== null) {
          if (cm[1] === undefined) continue;
          if (consumer0.excludeBefore !== undefined &&
              consumer0.excludeBefore.test(line.slice(Math.max(0, cm.index - 24), cm.index))) {
            continue;
          }
          if (norm(cm[1]) !== current) {
            findings.push({
              profile: P, profileStatus: status, severity: "WARNING",
              ruleId: "DC-PR-BODY-STALE", category: "pr-body",
              locator: `pr-body:${i + 1} (${cls.id})`,
            });
          }
        }
      }
    }
  }

  evaluations.push({ profile: P, profileStatus: status, status: "EVALUATED" });
  return { findings, evaluations };
}
