/**
 * P-VI §5 — KONTRATA e workflow-it governance-advisory.yml (pa dependency të
 * re: verifikim tekstual mbi YAML-in e tracked). Advisory / JO-required.
 */
import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";

const WF_PATH = join(process.cwd(), "..", "..", ".github", "workflows", "governance-advisory.yml");
const wf = readFileSync(WF_PATH, "utf8");

describe("workflow-contract — governance-advisory.yml", () => {
  it("pull_request me types: opened/reopened/synchronize/EDITED aktive", () => {
    const onBlock = wf.slice(wf.indexOf("on:"), wf.indexOf("permissions:"));
    expect(onBlock).toContain("pull_request:");
    for (const t of ["opened", "reopened", "synchronize", "edited"]) {
      expect(onBlock, t).toMatch(new RegExp(`-\\s+${t}\\b`));
    }
  });

  it("kurrë pull_request_target; permissions contents: read; zero secrets", () => {
    // koment-i dokumentues e përmend me qëllim; TRIGGER-i real nuk duhet të ekzistojë
    expect(wf).not.toMatch(/^\s*pull_request_target\s*:/m);
    expect(wf).toMatch(/permissions:\s*\n\s*contents:\s*read/);
    expect(wf).not.toContain("secrets.");
  });

  it("checkout me head SHA real + full history + persist-credentials: false", () => {
    expect(wf).toContain("ref: ${{ github.event.pull_request.head.sha }}");
    expect(wf).toContain("fetch-depth: 0");
    expect(wf).toContain("persist-credentials: false");
  });

  it("scan përdor head/base/docs-ref SHA të PR-së dhe NUK maskon exit code", () => {
    expect(wf).toContain('--base "${{ github.event.pull_request.base.sha }}"');
    expect(wf).toContain('--head "${{ github.event.pull_request.head.sha }}"');
    expect(wf).toContain('--docs-ref "${{ github.event.pull_request.head.sha }}"');
    expect(wf).toContain("set -euo pipefail");
    expect(wf).not.toMatch(/\|\|\s*true/);
    expect(wf).not.toContain("continue-on-error");
    // scan-i shkruan në file me redirect — asnjë pipe që gëlltit exit code
    const scanStep = wf.slice(wf.indexOf("Advisory scan"), wf.indexOf("Summary"));
    expect(scanStep).not.toMatch(/node[^\n]*\|/);
  });

  it("read-only/advisory: zero artifact upload; mbetet jo-required (dokumentuar në koka)", () => {
    expect(wf).not.toContain("actions/upload-artifact");
    expect(wf).toContain("ADVISORY, JO-required");
  });
});
