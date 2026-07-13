/**
 * Reporter — output text + JSON, të dyja të SANITIZUARA dhe deterministike.
 *
 * Rregulla të forta:
 *  - asnjë matched line/body/identifier në asnjë output;
 *  - locator-ët escape-ohen (pipe/backtick/CR/LF/kontroll) që të mos thyejnë
 *    Markdown-in e GitHub summary dhe të mos injektojnë përmbajtje;
 *  - renditje plotësisht deterministike.
 */

import type { Finding, ProfileEvaluation, ScanResult } from "./types.js";

export function sanitizeLocator(raw: string): string {
  let out = "";
  for (const ch of raw) {
    const code = ch.codePointAt(0) ?? 0;
    if (ch === "|") out += "\\u007c";
    else if (ch === "`") out += "\\u0060";
    else if (ch === "\r") out += "\\r";
    else if (ch === "\n") out += "\\n";
    else if (code < 0x20 || code === 0x7f) out += `\\u${code.toString(16).padStart(4, "0")}`;
    else out += ch;
  }
  return out;
}

function findingKey(f: Finding): string {
  return [f.profile, f.severity, f.ruleId, f.commit ?? "", f.locator, f.category].join("");
}

export function sortFindings(findings: Finding[]): Finding[] {
  return [...findings].sort((a, b) => (findingKey(a) < findingKey(b) ? -1 : findingKey(a) > findingKey(b) ? 1 : 0));
}

export function toTextReport(result: ScanResult): string {
  const lines: string[] = [];
  lines.push(`integrity-linter mode=${result.mode}`);
  for (const ev of sortEvaluations(result.evaluations)) {
    const checkPart = ev.check !== undefined ? ` check=${sanitizeLocator(ev.check)}` : "";
    lines.push(`profile ${ev.profile}${checkPart} status=${ev.profileStatus} evaluation=${ev.status}${ev.reason ? ` reason=${sanitizeLocator(ev.reason)}` : ""}`);
  }
  for (const f of sortFindings(result.findings)) {
    const commitPart = f.commit ? ` ${f.commit} ·` : "";
    lines.push(`${f.profile} · ${f.severity} · ${f.ruleId} ·${commitPart} ${sanitizeLocator(f.locator)} · ${f.category}`);
  }
  const counts = countBySeverity(result.findings);
  lines.push(`totals: ERROR=${counts.ERROR} WARNING=${counts.WARNING} REVIEW=${counts.REVIEW}`);
  return lines.join("\n") + "\n";
}

export function toJsonReport(result: ScanResult): string {
  const evaluations = sortEvaluations(result.evaluations)
    .map((ev: ProfileEvaluation) => ({
      profile: ev.profile,
      profileStatus: ev.profileStatus,
      evaluation: ev.status,
      ...(ev.check !== undefined ? { check: sanitizeLocator(ev.check) } : {}),
      ...(ev.reason !== undefined ? { reason: sanitizeLocator(ev.reason) } : {}),
    }));
  const findings = sortFindings(result.findings).map((f) => ({
    profile: f.profile,
    profileStatus: f.profileStatus,
    severity: f.severity,
    ruleId: f.ruleId,
    category: f.category,
    locator: sanitizeLocator(f.locator),
    ...(f.commit !== undefined ? { commit: f.commit } : {}),
  }));
  return JSON.stringify({ mode: result.mode, evaluations, findings, totals: countBySeverity(result.findings) }, null, 2) + "\n";
}

/** Renditje deterministike: profile → check → evaluation/status. */
export function sortEvaluations(evs: ProfileEvaluation[]): ProfileEvaluation[] {
  return [...evs].sort((a, b) => {
    const ka = [a.profile, a.check ?? "", a.status].join("\u0000");
    const kb = [b.profile, b.check ?? "", b.status].join("\u0000");
    return ka < kb ? -1 : ka > kb ? 1 : 0;
  });
}

export function countBySeverity(findings: Finding[]): { ERROR: number; WARNING: number; REVIEW: number } {
  const counts = { ERROR: 0, WARNING: 0, REVIEW: 0 };
  for (const f of findings) counts[f.severity] += 1;
  return counts;
}
