/**
 * DOC-CORE — integriteti i dokumentacionit canonical & governance (advisory).
 *
 * Kontrolle mekanike (07_PRODATA §Linter):
 *  - old-value / version / count / status drift me model `authority → consumers`;
 *  - `HISTORICAL/SUPERSEDED/Historia` vlen VETËM në rreshtin/seksionin ku gjendet vlera;
 *  - rendi invariant Step B → Step C → independent review → HUMAN GATE →
 *    private /ca/verify PREFLIGHT → pjesa tjetër e Tier 2;
 *  - milestone/gate në tracker që mungon në sipërfaqet e rendit;
 *  - vocabulary rolesh si REVIEW (kurrë vendim automatik);
 *  - pretendime `complete/full/exhaustive/closes-on-merge` si kontroll kontekstual
 *    per-rresht me kualifikues — JO grep global i verbër;
 *  - PR body kundrejt authority-ve; mungesa e event-it → NOT_EVALUATED (kurrë PASS).
 *
 * Linter-i NUK jep opinion ligjor dhe NUK vendos semantikën e asnjë neni.
 */

import type { Finding, GitReader, ProfileEvaluation, PullRequestEvent } from "../types.js";
import { ScanError } from "../types.js";
import { profileStatus } from "../profile-registry.js";

const P = "DOC-CORE" as const;

export interface AuthorityRule {
  id: string;
  authorityFile: string;
  /** Grupi 1 = vlera autoritative. */
  authorityPattern: RegExp;
  consumers: Array<{ file: string; pattern: RegExp }>;
}

export interface DocCoreSurfaces {
  /** Sipërfaqet ku kontrollohet rendi invariant + claims + milestones. */
  orderSurfaces: string[];
  trackerFile?: string;
  milestoneSurfaces: string[];
  authorities: AuthorityRule[];
  approvedRoleTokens: string[];
}

/** Konfigurimi real i repo-s (tests injektojnë sipërfaqe sintetike). */
export function defaultSurfaces(): DocCoreSurfaces {
  const LEDGER = "docs/canonical/05-sef-atk/ATK_SEF_Requirements_Ledger.md";
  const REGISTER = "docs/canonical/05-sef-atk/ATK_Source_Register.md";
  const GATE = "docs/canonical/05-sef-atk/ATK_Compliance_Gate.md";
  const SSOT = "docs/canonical/00-ssot/iData_Plus_SSOT.md";
  const TRACKER = "docs/canonical/00-ssot/Outstanding_Confirmations_Tracker_2026-06-29.md";
  const INDEX = "docs/INDEX.md";
  const ROADMAP = "docs/canonical/08-roadmap/Project_Delivery_Roadmap_Phases_1_10.md";
  const PM = "docs/PM_REVIEW_PROTOCOL.md";
  const MATRIX07 = "docs/canonical/01-phase0/07_PRODATA_Evidence_Closure_Parity_Matrix.md";
  const ANALIZA = "poc/poc5-sef/ANALIZA.md";
  const POC5README = "poc/poc5-sef/README.md";
  const FINDINGS = "docs/canonical/05-sef-atk/ATK_SEF_Offline_Faturimi_Findings.md";

  const ledgerConsumers = [GATE, REGISTER, SSOT, INDEX, TRACKER, ROADMAP, ANALIZA, FINDINGS];
  return {
    orderSurfaces: [INDEX, ROADMAP, GATE, SSOT, PM, POC5README, LEDGER],
    trackerFile: TRACKER,
    milestoneSurfaces: [INDEX, ROADMAP, SSOT, PM],
    approvedRoleTokens: [
      "Pronari", "Human Gate", "Group Owner", "PM", "Finance Owner", "Codex", "Claude Code",
      "kontabilisti", "iCode", "ATK", "KABI", "PLANET", "fabrika", "teknologu", "laboratori",
      "payroll", "finance", "operacionet", "Tech", "management", "vendor", "jurist", "lab",
    ],
    authorities: [
      {
        id: "ledger-version",
        authorityFile: LEDGER,
        authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/,
        consumers: ledgerConsumers.map((file) => ({
          file,
          pattern: /(?:Requirements_)?Ledger(?:\.md)?`?\*{0,2}\s*(?:[—–:])?\s*\*{0,2}v(\d+\.\d+)/g,
        })),
      },
      {
        id: "ledger-source-rows",
        authorityFile: LEDGER,
        authorityPattern: /Source-requirement rows: (\d+)\*\*/,
        consumers: ledgerConsumers.map((file) => ({ file, pattern: /(\d{2,3}) source-rows/g })),
      },
      {
        id: "ledger-closure-keys",
        authorityFile: LEDGER,
        authorityPattern: /closure\/product keys: (\d+)\*\*/,
        consumers: ledgerConsumers.map((file) => ({ file, pattern: /(\d+) closure keys/g })),
      },
      {
        id: "ledger-confirmations",
        authorityFile: LEDGER,
        authorityPattern: /\*\*(\d+) kontrolle\*\* me `CONFIRM/,
        consumers: [
          ...ledgerConsumers.map((file) => ({ file, pattern: /(\d+) konfirmime/g })),
          ...ledgerConsumers.map((file) => ({ file, pattern: /(\d+) CONFIRM_/g })),
        ],
      },
      {
        id: "register-version",
        authorityFile: REGISTER,
        authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/,
        consumers: [GATE, SSOT, INDEX, ROADMAP, ANALIZA, FINDINGS, LEDGER].map((file) => ({
          file,
          pattern: /(?:Source_)?Register(?:\.md)?`?\*{0,2}\s*(?:[—–:])?\s*\*{0,2}v(\d+\.\d+)/g,
        })),
      },
      {
        id: "matrix07-version",
        authorityFile: MATRIX07,
        authorityPattern: /\*\*Versioni:\*\* (\d+\.\d+)/,
        consumers: [TRACKER].map((file) => ({
          file,
          pattern: /07_PRODATA_Evidence_Closure_Parity_Matrix\.md`? v(\d+\.\d+)/g,
        })),
      },
    ],
  };
}

/** Etiketë historike E VLEFSHME vetëm në të njëjtin rresht (row/section-scoped). */
export function isHistoryLabeled(line: string): boolean {
  return /(HISTORICAL|SUPERSEDED|Historia\b|HISTORIK|superseded|ish |i atëhershëm|e atëhershme|numërator(i)? historik|vs \d)/i.test(line);
}

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

export function runDocCore(
  reader: GitReader,
  docsRef: string,
  surfaces: DocCoreSurfaces,
  event: PullRequestEvent | undefined,
): { findings: Finding[]; evaluations: ProfileEvaluation[] } {
  const status = profileStatus(P);
  const findings: Finding[] = [];
  const evaluations: ProfileEvaluation[] = [];
  // FAIL-CLOSED: çdo surface e konfiguruar është REQUIRED — mungesa e file-it
  // në docsRef (ref i validuar më parë nga engine-i) = skanim i paplotë → exit 2.
  const fileCache = new Map<string, string>();
  const readDoc = (path: string): string => {
    const cached = fileCache.get(path);
    if (cached !== undefined) return cached;
    if (!reader.hasFile(docsRef, path)) {
      throw new ScanError("INCOMPLETE_SCAN:SURFACE_MISSING");
    }
    const text = reader.showFile(docsRef, path);
    fileCache.set(path, text);
    return text;
  };

  // ---- 1) authority → consumers drift (version/count/status; old-value drift) ----
  for (const rule of surfaces.authorities) {
    const authorityText = readDoc(rule.authorityFile);
    const m = rule.authorityPattern.exec(authorityText);
    if (!m || m[1] === undefined) {
      // authority ekziston por vlera s'lexohet dot → skanim i paplotë, jo heshtje
      throw new ScanError("INCOMPLETE_SCAN:AUTHORITY_UNREADABLE");
    }
    const currentValue = m[1];
    for (const consumer of rule.consumers) {
      const text = readDoc(consumer.file);
      const lines = text.split("\n");
      for (let i = 0; i < lines.length; i++) {
        const line = lines[i] ?? "";
        const re = new RegExp(consumer.pattern.source, consumer.pattern.flags);
        let cm: RegExpExecArray | null;
        while ((cm = re.exec(line)) !== null) {
          const seen = cm[1];
          if (seen !== undefined && seen !== currentValue && !isHistoryLabeled(line)) {
            findings.push({
              profile: P, profileStatus: status, severity: "WARNING",
              ruleId: "DC-OLD-VALUE-DRIFT", category: "drift",
              locator: `${consumer.file}:${i + 1} (${rule.id})`,
            });
          }
        }
      }
    }
  }

  // ---- 2) rendi invariant i zinxhirit SEF (per paragraf; historikët përjashtohen) ----
  for (const file of surfaces.orderSurfaces) {
    const text = readDoc(file);
    const paragraphs = text.split(/\n\s*\n/);
    let lineNo = 1;
    for (const para of paragraphs) {
      const paraLines = para.split("\n").length;
      if (!isHistoryLabeled(para.split("\n")[0] ?? "")) {
        // Vlerësohen VETËM segmentet "zinxhir" — pretendime reale rendi:
        //  - rreshta me → ose "pastaj": token-ët merren vetëm PRANË markerit (±48 kar.);
        //  - artikuj liste të numëruar (jo rreshta tabele, jo rreshta historikë);
        //  - përmbajtja në parenteza hiqet (referenca anësore ≠ pretendim rendi).
        const chainParts: string[] = [];
        for (const rawLine of para.split("\n")) {
          if (rawLine.trimStart().startsWith("|")) continue; // tabelat s'janë zinxhirë
          if (isHistoryLabeled(rawLine)) continue;
          const line = rawLine.replace(/\([^)]*\)/g, " ");
          const isNumbered = /^\s*\d+[a-z]?\.\s/.test(line);
          const markers: number[] = [];
          for (const mk of line.matchAll(/→|\bpastaj\b/g)) markers.push(mk.index ?? 0);
          if (isNumbered && markers.length === 0) {
            // Artikull liste i numëruar: vetëm SUBJEKTI i artikullit (48 kar. e para)
            // është pretendim rendi — jo referencat e thella brenda tekstit të tij.
            // Subjekt i MOHUAR ("nuk/s'/jo/vetëm pas") nuk është pretendim rendi.
            const subject = line.slice(0, 48);
            if (!/(\bnuk\b|\bs'|\bjo\b|vetëm pas)/i.test(subject)) chainParts.push(subject);
          } else if (markers.length > 0) {
            // mbaj vetëm dritaret ±48 karaktere rreth markerëve
            const keep = new Array<boolean>(line.length).fill(false);
            for (const mi of markers) {
              for (let k = Math.max(0, mi - 48); k < Math.min(line.length, mi + 48); k++) keep[k] = true;
            }
            let windowed = "";
            for (let k = 0; k < line.length; k++) windowed += keep[k] === true ? line[k] : " ";
            chainParts.push(windowed);
          }
        }
        const chainText = chainParts.join("\n");
        const present = ORDER_TOKENS
          .map((t) => ({ key: t.key, idx: firstIndex(chainText, t.res) }))
          .filter((t) => t.idx >= 0);
        if (present.length >= 2) {
          for (let i = 1; i < present.length; i++) {
            const prev = present[i - 1];
            const cur = present[i];
            if (prev !== undefined && cur !== undefined && cur.idx < prev.idx) {
              findings.push({
                profile: P, profileStatus: status, severity: "WARNING",
                ruleId: "DC-GATE-ORDER", category: "sequence",
                locator: `${file}:${lineNo} (${cur.key} para ${prev.key})`,
              });
            }
          }
        }
      }
      lineNo += paraLines + 1;
    }
  }

  // ---- 3) milestone/gate në tracker që mungon nga sipërfaqet e rendit ----
  if (surfaces.trackerFile !== undefined) {
    const tracker = readDoc(surfaces.trackerFile);
    {
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

      // ---- 4) vocabulary rolesh (owner-cells të tracker-it) — REVIEW, jo vendim ----
      const lines = tracker.split("\n");
      for (let i = 0; i < lines.length; i++) {
        const line = lines[i] ?? "";
        if (!line.startsWith("| `")) continue;
        const cells = line.split("|").map((c) => c.trim());
        const ownerCell = cells[4];
        if (ownerCell === undefined || ownerCell === "" || ownerCell === "—") continue;
        const ok = surfaces.approvedRoleTokens.some((tok) => ownerCell.toLowerCase().includes(tok.toLowerCase()));
        if (!ok) {
          findings.push({
            profile: P, profileStatus: status, severity: "REVIEW",
            ruleId: "DC-ROLE-VOCAB", category: "privacy-review",
            locator: `${surfaces.trackerFile}:${i + 1}`,
          });
        }
      }
    }
  }

  // ---- 5) pretendime absolute — kontekstuale per-rresht, me kualifikues ----
  for (const file of [...surfaces.orderSurfaces, ...(surfaces.trackerFile ? [surfaces.trackerFile] : [])]) {
    const text = readDoc(file);
    const lines = text.split("\n");
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i] ?? "";
      if (isHistoryLabeled(line)) continue;
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

  // ---- 6) PR body kundrejt authority-ve (nga GITHUB_EVENT_PATH) ----
  if (event?.body === undefined) {
    evaluations.push({
      profile: P, profileStatus: status, status: "NOT_EVALUATED",
      check: "pr-body", reason: "missing PR event/body",
    });
  } else {
    const bodyLines = event.body.split("\n");
    for (const rule of surfaces.authorities) {
      const authorityText = readDoc(rule.authorityFile);
      const m = rule.authorityPattern.exec(authorityText);
      const currentValue = m?.[1];
      if (currentValue === undefined) continue;
      const consumerPattern = rule.consumers[0]?.pattern;
      if (consumerPattern === undefined) continue;
      for (let i = 0; i < bodyLines.length; i++) {
        const line = bodyLines[i] ?? "";
        if (isHistoryLabeled(line)) continue;
        const re = new RegExp(consumerPattern.source, consumerPattern.flags);
        let cm: RegExpExecArray | null;
        while ((cm = re.exec(line)) !== null) {
          if (cm[1] !== undefined && cm[1] !== currentValue) {
            findings.push({
              profile: P, profileStatus: status, severity: "WARNING",
              ruleId: "DC-PR-BODY-STALE", category: "pr-body",
              locator: `pr-body:${i + 1} (${rule.id})`,
            });
          }
        }
      }
    }
  }

  evaluations.push({ profile: P, profileStatus: status, status: "EVALUATED" });
  return { findings, evaluations };
}
