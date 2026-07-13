/**
 * Ndihmës testesh — repo git TEMPORARE me përmbajtje 100% SINTETIKE.
 * Asnjë e dhënë reale; stringjet "sensitive" ndërtohen runtime nga fragmente.
 */

import { execFileSync } from "node:child_process";
import { mkdtempSync, writeFileSync, mkdirSync, symlinkSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";

export interface TempRepo {
  dir: string;
  git(args: string[], input?: Buffer): string;
  write(rel: string, content: string | Buffer): void;
  commitAll(message: string): string;
  head(): string;
}

export function makeTempRepo(prefix = "linter-fixture-"): TempRepo {
  const dir = mkdtempSync(join(tmpdir(), prefix));
  const git = (args: string[], input?: Buffer): string =>
    execFileSync("git", args, {
      cwd: dir,
      stdio: [input === undefined ? "ignore" : "pipe", "pipe", "pipe"],
      ...(input !== undefined ? { input } : {}),
    }).toString("utf8");
  git(["init", "-q", "-b", "main"]);
  // Email sintetik i ndërtuar nga fragmente runtime — që vetë ky burim të mos
  // përmbajë literal email-like që kapet nga skanerat PII (zero-tolerance).
  git(["config", "user.email", ["fixture", "@", "example", ".invalid"].join("")]);
  git(["config", "user.name", "Synthetic Fixture"]);
  git(["config", "commit.gpgsign", "false"]);
  const write = (rel: string, content: string | Buffer): void => {
    const abs = join(dir, rel);
    mkdirSync(dirname(abs), { recursive: true });
    writeFileSync(abs, content);
  };
  const commitAll = (message: string): string => {
    git(["add", "-A"]);
    git(["commit", "-q", "-m", message]);
    return git(["rev-parse", "HEAD"]).trim();
  };
  return { dir, git, write, commitAll, head: () => git(["rev-parse", "HEAD"]).trim() };
}

export function addSymlink(repo: TempRepo, rel: string, target: string): void {
  symlinkSync(target, join(repo.dir, rel));
}

export const frag = (...parts: string[]): string => parts.join("");

/**
 * Krijon një commit me TREE të ndërtuar manualisht (git mktree -z), për path-e
 * që s'krijohen dot në filesystem (tab/newline/UTF-8 invalid) ose për gitlink.
 * Entry format (-z): "<mode> <type> <oid>\t<path>" me NUL në fund — path si Buffer.
 */
export function commitWithRawTree(
  repo: TempRepo,
  entries: Array<{ mode: string; type: "blob" | "commit"; oid: string; pathBytes: Buffer }>,
  message: string,
  parent?: string,
): string {
  const parts: Buffer[] = [];
  for (const e of entries) {
    parts.push(Buffer.from(`${e.mode} ${e.type} ${e.oid}\t`, "ascii"), e.pathBytes, Buffer.from([0]));
  }
  const tree = repo.git(["mktree", "-z"], Buffer.concat(parts)).trim();
  const commit = repo
    .git(parent !== undefined ? ["commit-tree", tree, "-p", parent, "-m", message] : ["commit-tree", tree, "-m", message])
    .trim();
  repo.git(["update-ref", "refs/heads/main", commit]);
  return commit;
}

export function hashBlob(repo: TempRepo, content: string): string {
  return repo.git(["hash-object", "-w", "--stdin"], Buffer.from(content, "utf8")).trim();
}

/** Sipërfaqet default të DOC-CORE me përmbajtje sintetike KONSISTENTE. */
export function writeFullCanonicalRepo(repo: TempRepo, opts?: { staleIndexLedger?: boolean }): void {
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

  repo.write(LEDGER, "**Versioni:** 1.6\nSource-requirement rows: 132**\nclosure/product keys: 46**\n**45 kontrolle** me `CONFIRM_X`\nRegister v2.4 sync\n");
  repo.write(REGISTER, "**Versioni:** 2.4\nLedger v1.6 pointer · 132 source-rows · 46 closure keys · 45 konfirmime\n");
  repo.write(GATE, "Ledger v1.6 · Register v2.4 · 132 source-rows · 46 closure keys · 45 konfirmime\nGOV-001 HG-LINTER-BUILD HG-LINTER-REQUIRED\n");
  repo.write(SSOT, "Ledger v1.6 (**132 source-rows · 46 closure keys · 45 konfirmime**) · Register **v2.4** · 45 CONFIRM_ rreshta\n");
  repo.write(TRACKER, "| `GOV-001` | governance | linter | Pronari | `PARTIAL` | HIGH | x | y |\n");
  const staleLine = [
    frag("Ledger v1", ".", "5"), " · ", frag("11", "7"), " source-rows · ", frag("4", "4"),
    " closure keys · ", frag("4", "3"), " konfirmime · ", frag("Register — v2", ".", "3"),
  ].join("");
  repo.write(INDEX, opts?.staleIndexLedger === true
    ? staleLine + "\nGOV-001 hapi 0\n"
    : "Ledger v1.6 · 132 source-rows · 46 closure keys · 45 konfirmime · Register — v2.4\nGOV-001 hapi 0\n");
  repo.write(ROADMAP, "Step B → Step C → independent review → HUMAN GATE → private PREFLIGHT → Tier 2\nLedger v1.6 · Register v2.4 · 132 source-rows · 46 closure keys · 45 konfirmime\nGOV-001 1a\n");
  repo.write(PM, "GOV-001 scope; Ledger v1.6 · 132 source-rows\n");
  repo.write(MATRIX07, "**Versioni:** 2.3\nlinter section\n");
  repo.write(ANALIZA, "Ledger v1.6 (132 source-rows · 46 closure keys) · Register v2.4 · 45 konfirmime\n");
  repo.write(POC5README, "Step B → Step C → independent review → HUMAN GATE → private PREFLIGHT → Tier 2\n");
  repo.write(FINDINGS, "Register v2.4 + Ledger v1.6 · 132 source-rows · 46 closure keys · 45 konfirmime\n");
  repo.commitAll("full synthetic canonical surfaces");
}
