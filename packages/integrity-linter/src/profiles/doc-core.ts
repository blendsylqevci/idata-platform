/**
 * DOC-CORE — integriteti i dokumentacionit canonical & governance (advisory, FAIL-CLOSED).
 *
 * Hardening III:
 *  - §1: validator i PËRBASHKËT pure `extractAuthorityValue` (i njëjti me HISTORY-PRIV);
 *  - §4: zinxhiri invariant kontrollohet për PRANI + REND në sipërfaqet chainRequired —
 *    të 6 hallkat duhen në një block AKTIV; HISTORICAL chain s'e plotëson;
 *    variantet e review normalizohen në të njëjtën hallkë;
 *  - §7: qelizat me GFM parser; role-bearing fields (kolona Owner/Pronari/Kush/… +
 *    etiketa **Pronari:**) në TË GJITHA sipërfaqet aktive; FRAZA TË PLOTA rolesh —
 *    një fjalë e lejuar brenda tokenit NUK e aprovon tokenin.
 *
 * Linter-i NUK jep opinion ligjor dhe NUK vendos semantikën e asnjë neni.
 */

import type { Finding, GitReader, ProfileEvaluation, PullRequestEvent } from "../types.js";
import { ScanError } from "../types.js";
import { profileStatus } from "../profile-registry.js";
import {
  AuthorityError, defaultAuthorityClasses, extractAuthorityValue, isHistoryLabeledRow,
  GATE, INDEX, LEDGER, PM, POC5README, ROADMAP, SSOT, TRACKER,
  type AuthorityClass,
} from "../authority-classes.js";
import { isSeparatorRow, parseTableRow, tableContextAt } from "../markdown-table.js";

const P = "DOC-CORE" as const;

export interface DocCoreSurfaces {
  orderSurfaces: string[];
  /** Sipërfaqet e DETYRUARA të mbajnë një block AKTIV me TË GJITHA 6 hallkat (§4). */
  chainRequiredSurfaces: string[];
  trackerFile?: string;
  milestoneSurfaces: string[];
  authorities: AuthorityClass[];
  approvedRolePhrases: string[];
}

/**
 * FRAZA TË PLOTA rolesh të aprovuara (normalizuara lowercase; §7) — të nxjerra
 * nga vokabulari canonical i normalizuar (PR #20). KURRË emra personalë realë.
 * Tokeni duhet të BARAZOHET me një frazë pas normalizimit — jo substring.
 */
export const APPROVED_ROLE_PHRASES = [
  "pronari", "codex", "claude code", "pm", "group owner", "human gate", "finance owner",
  "kontabilisti", "icode", "icode dev", "icode support", "icode l.l.c",
  "atk", "kabi", "kabi admin", "kabi sales", "planet", "planet finance",
  "planet operations", "planet management", "teknologu", "fabrika", "laboratori",
  "jurist", "aip", "tech", "hardware vendor", "klienti", "owner", "burimi",
  "logistics", "tier 2", "ti", "group",
];

export function defaultSurfaces(): DocCoreSurfaces {
  return {
    orderSurfaces: [INDEX, ROADMAP, GATE, SSOT, PM, POC5README, LEDGER],
    chainRequiredSurfaces: [INDEX, ROADMAP, GATE, SSOT, PM],
    trackerFile: TRACKER,
    milestoneSurfaces: [INDEX, ROADMAP, SSOT, PM],
    authorities: defaultAuthorityClasses(),
    approvedRolePhrases: APPROVED_ROLE_PHRASES,
  };
}

export { isHistoryLabeledRow as isHistoryLabeled };

/** Hallkat e zinxhirit invariant; review normalizohet nga TË GJITHA variantet reale (§4). */
export const CHAIN_TOKENS: Array<{ key: string; res: RegExp[] }> = [
  { key: "step-b", res: [/Step B\b/] },
  { key: "step-c", res: [/Step C\b/] },
  {
    key: "independent-review",
    res: [/independent re-?review/i, /\bre-review\b/i, /independent review/i,
      /review(?:-t|t)? (?:i|të) pavarur/i, /rishikim(?:i|in)? (?:i|të) pavarur/i],
  },
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
 * parenteza të hequra, tabela/historikë të përjashtuar; dritare ±80 rreth
 * markerëve; subjekte 48-kar. për artikuj liste pa marker.
 */
export function extractChainText(lines: string[]): string {
  const chainParts: string[] = [];
  for (const rawLine of lines) {
    if (rawLine.trimStart().startsWith("|")) continue; // tabelat s'janë zinxhirë
    if (isHistoryLabeledRow(rawLine)) continue; // chain historik ≠ chain aktiv
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
        for (let k = Math.max(0, mi - 80); k < Math.min(line.length, mi + 80); k++) keep[k] = true;
      }
      let windowed = "";
      for (let k = 0; k < line.length; k++) windowed += keep[k] === true ? line[k] : " ";
      chainParts.push(windowed);
    }
  }
  return chainParts.join("\n");
}

export function chainOrderViolations(chainText: string): Array<{ later: string; earlier: string }> {
  const present = CHAIN_TOKENS
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

export interface ChainEvaluation {
  /** Hallkat që mungojnë nga blloku MË I MIRË aktiv i sipërfaqes. */
  missing: string[];
  /** Shkeljet e rendit në bllokun më të mirë. */
  violations: Array<{ later: string; earlier: string }>;
  /** true kur s'u gjet ASNJË block kandidat aktiv (≥2 hallka). */
  noActiveChain: boolean;
}

/**
 * §4: vlerëson PRANINË + RENDIN e zinxhirit të plotë në një dokument:
 * blloku më i mirë AKTIV duhet t'i ketë TË GJITHA hallkat në rendin e saktë.
 * Përdoret nga DOC-CORE (HEAD) dhe HISTORY-PRIV (snapshot per-commit).
 */
export function evaluateChainPresence(text: string): ChainEvaluation {
  const paragraphs = text.split(/\n\s*\n/);
  let best: { count: number; missing: string[]; violations: Array<{ later: string; earlier: string }> } | undefined;
  for (const para of paragraphs) {
    const chainText = extractChainText(para.split("\n"));
    const present = CHAIN_TOKENS.filter((t) => firstIndex(chainText, t.res) >= 0);
    if (present.length < 2) continue; // jo pretendim zinxhiri (partial prose near-miss)
    const missing = CHAIN_TOKENS.filter((t) => firstIndex(chainText, t.res) < 0).map((t) => t.key);
    const violations = chainOrderViolations(chainText);
    const score = present.length - violations.length * 0.5;
    if (best === undefined || score > best.count) {
      best = { count: score, missing, violations };
    }
  }
  if (best === undefined) return { missing: CHAIN_TOKENS.map((t) => t.key), violations: [], noActiveChain: true };
  return { missing: best.missing, violations: best.violations, noActiveChain: false };
}

/** Header-at e kolonave role-bearing (§7) — barazi e plotë e emrit të kolonës. */
const ROLE_HEADER_RE = /^(Owner( \/ burimi)?|Pronari?|Kush|Përgjegjës\w*|Approver|Human Gate)$/i;
/** Etiketat metadata role-bearing. */
const ROLE_LABEL_RE = /^\*{0,2}(Pronar(?:i)?(?: i dokumentit)?|Owner|Përgjegjës\w*|Approver)\s*:\*{0,2}\s*(.+)$/i;

/** Tokenizon një vlerë role-bearing dhe kthen numrin e tokenëve JO të aprovuar. */
export function roleTokensUnknown(cellRaw: string, approvedPhrases: string[]): number {
  const cell = cellRaw
    .replace(/\([^)]*\)/g, " ")           // kualifikuesit në parenteza hiqen
    .replace(/[`*]/g, "")
    .trim();
  if (cell === "" || cell === "—" || cell === "-") return 0;
  const tokens = cell
    .split(/[/,+;·]|→|\bdhe\b/i)
    .map((t) => t.trim().toLowerCase()
      .replace(/\s+nëse duhet$/, "")
      .replace(/\s+sipas rreshtit$/, "")
      .replace(/\s+/g, " "))
    .filter((t) => t !== "" && t !== "—" && t !== "-");
  const approved = new Set(approvedPhrases.map((p) => p.toLowerCase()));
  let unknown = 0;
  for (const token of tokens) {
    if (!approved.has(token)) unknown += 1; // FRAZË E PLOTË — jo substring/fjalë e vetme
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

  // ── 1) kontrata bidirectional authority → consumers (validator i përbashkët) ─
  for (const cls of surfaces.authorities) {
    const authorityText = readDoc(cls.authorityFile); // missing → exit 2
    let current: string;
    try {
      current = extractAuthorityValue(authorityText, cls);
    } catch (err) {
      if (err instanceof AuthorityError) throw new ScanError(err.code);
      throw err;
    }
    const norm = cls.normalize ?? ((v: string) => v);

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
            continue; // guard kontekstual per-match
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

  // ── 2) rendi invariant (të gjitha orderSurfaces) + PRANIA (chainRequired, §4) ─
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
  for (const file of surfaces.chainRequiredSurfaces) {
    const evaln = evaluateChainPresence(readDoc(file));
    if (evaln.noActiveChain) {
      findings.push({
        profile: P, profileStatus: status, severity: "WARNING",
        ruleId: "DC-CHAIN-INCOMPLETE", category: "sequence",
        locator: `${file} (asnjë chain aktiv)`,
      });
    } else {
      for (const missing of evaln.missing) {
        findings.push({
          profile: P, profileStatus: status, severity: "WARNING",
          ruleId: "DC-CHAIN-INCOMPLETE", category: "sequence",
          locator: `${file} (mungon: ${missing})`,
        });
      }
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
  }

  // ── 4) role/privacy vocabulary — kontekste të STRUKTURUARA kudo (§7) ────
  const roleSurfaces = [...surfaces.orderSurfaces, ...(surfaces.trackerFile ? [surfaces.trackerFile] : [])];
  for (const file of roleSurfaces) {
    const text = readDoc(file);
    const lines = text.split("\n");
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i] ?? "";
      // (a) rreshta tabele nën kolonë role-bearing
      if (line.trimStart().startsWith("|") && !isSeparatorRow(line)) {
        const tctx = tableContextAt(text, i + 1);
        const col = tctx?.columnIndex(ROLE_HEADER_RE);
        if (tctx !== undefined && col !== undefined) {
          const row = parseTableRow(line);
          const cell = row.cells[col];
          if (!row.malformed && cell !== undefined &&
              roleTokensUnknown(cell, surfaces.approvedRolePhrases) > 0) {
            findings.push({
              profile: P, profileStatus: status, severity: "REVIEW",
              ruleId: "DC-ROLE-VOCAB", category: "privacy-review",
              locator: `${file}:${i + 1}`,
            });
          }
          continue;
        }
      }
      // (b) etiketa metadata role-bearing
      const lm = ROLE_LABEL_RE.exec(line.trim());
      if (lm?.[2] !== undefined && roleTokensUnknown(lm[2], surfaces.approvedRolePhrases) > 0) {
        findings.push({
          profile: P, profileStatus: status, severity: "REVIEW",
          ruleId: "DC-ROLE-VOCAB", category: "privacy-review",
          locator: `${file}:${i + 1}`,
        });
      }
    }
  }

  // ── 5) pretendime absolute — kontekstuale per-rresht ────────────────────
  for (const file of roleSurfaces) {
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

  // ── 6) PR body kundrejt authority-ve (SCAN_IF_PRESENT; pa event → NOT_EVALUATED) ─
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
      let current: string;
      try {
        current = extractAuthorityValue(authorityText, cls);
      } catch (err) {
        if (err instanceof AuthorityError) throw new ScanError(err.code);
        throw err;
      }
      const norm = cls.normalize ?? ((v: string) => v);
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
