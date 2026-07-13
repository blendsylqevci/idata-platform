/**
 * GitCliReader — implementimi i vetëm i GitReader mbi CLI-në e git-it.
 *
 * Kufij privacy/filesystem (07_PRODATA §Linter + PROJECT_RULES):
 *  - lexon VETËM Git objects të tracked (`git show`, `git diff-tree`, `git rev-list`,
 *    `git merge-base`, `git cat-file`);
 *  - ASNJË recursive filesystem walk / find / glob / readdir mbi repo;
 *  - ASNJË network call;
 *  - `execFileSync` me argument arrays — kurrë shell interpolation;
 *  - fail-closed: çdo dështim git → ScanError.
 */

import { execFileSync } from "node:child_process";
import { ScanError, type ChangedEntry, type GitReader } from "./types.js";

const MAX_GIT_OUTPUT = 32 * 1024 * 1024;

export class GitCliReader implements GitReader {
  constructor(private readonly cwd: string) {}

  private git(args: string[]): Buffer {
    try {
      return execFileSync("git", args, {
        cwd: this.cwd,
        maxBuffer: MAX_GIT_OUTPUT,
        stdio: ["ignore", "pipe", "pipe"],
      });
    } catch (err) {
      const msg = err instanceof Error ? err.message.split("\n")[0] : "unknown git error";
      throw new ScanError(`git ${args[0] ?? ""} failed: ${msg}`);
    }
  }

  private gitText(args: string[]): string {
    return this.git(args).toString("utf8");
  }

  revParse(ref: string): string {
    const out = this.gitText(["rev-parse", "--verify", `${ref}^{commit}`]).trim();
    if (!/^[0-9a-f]{40}$/.test(out)) throw new ScanError(`invalid ref resolution for ${ref}`);
    return out;
  }

  isShallow(): boolean {
    return this.gitText(["rev-parse", "--is-shallow-repository"]).trim() === "true";
  }

  mergeBase(a: string, b: string): string {
    const out = this.gitText(["merge-base", a, b]).trim();
    if (!/^[0-9a-f]{40}$/.test(out)) throw new ScanError("merge-base did not resolve");
    return out;
  }

  revList(baseExclusive: string, head: string): string[] {
    const out = this.gitText(["rev-list", "--reverse", `${baseExclusive}..${head}`]).trim();
    return out === "" ? [] : out.split("\n");
  }

  parents(sha: string): string[] {
    const out = this.gitText(["rev-list", "--parents", "-n", "1", sha]).trim().split(/\s+/);
    return out.slice(1);
  }

  changedEntries(sha: string): ChangedEntry[] {
    // --raw -z: ":oldmode newmode oldsha newsha status\0path\0[newpath\0]"
    const raw = this.gitText([
      "diff-tree", "-r", "-M", "--no-commit-id", "--root", "--raw", "-z", sha,
    ]);
    const entries: ChangedEntry[] = [];
    const parts = raw.split("\0").filter((p) => p !== "");
    let i = 0;
    while (i < parts.length) {
      const meta = parts[i];
      if (meta === undefined || !meta.startsWith(":")) break;
      const fields = meta.slice(1).split(/\s+/);
      const newMode = fields[1] ?? "";
      const newBlob = fields[3] ?? "";
      const statusFull = fields[4] ?? "";
      const status = statusFull.charAt(0);
      const p1 = parts[i + 1];
      if (p1 === undefined) throw new ScanError("malformed diff-tree output");
      if (status === "R" || status === "C") {
        const p2 = parts[i + 2];
        if (p2 === undefined) throw new ScanError("malformed diff-tree rename output");
        entries.push({ status, newMode, newBlob, path: p2, oldPath: p1 });
        i += 3;
      } else {
        entries.push({ status, newMode, newBlob, path: p1 });
        i += 2;
      }
    }
    return entries;
  }

  addedLines(sha: string, path: string): string[] {
    const buf = this.git(["show", sha, "--format=", "--unified=0", "--", path]);
    const text = decodeStrictUtf8(buf, path);
    const added: string[] = [];
    for (const line of text.split("\n")) {
      if (line.startsWith("+") && !line.startsWith("+++")) added.push(line.slice(1));
    }
    return added;
  }

  binaryPaths(sha: string): Set<string> {
    const out = this.gitText(["diff-tree", "-r", "--no-commit-id", "--root", "--numstat", sha]);
    const bins = new Set<string>();
    for (const line of out.split("\n")) {
      const parts = line.split("\t");
      if (parts.length >= 3 && parts[0] === "-" && parts[1] === "-") {
        const p = parts[2];
        if (p !== undefined && p !== "") bins.add(p);
      }
    }
    return bins;
  }

  blobSize(sha: string, path: string): number {
    const out = this.gitText(["cat-file", "-s", `${sha}:${path}`]).trim();
    const n = Number.parseInt(out, 10);
    if (!Number.isFinite(n)) throw new ScanError("cat-file size unreadable");
    return n;
  }

  showFile(ref: string, path: string): string {
    return decodeStrictUtf8(this.git(["show", `${ref}:${path}`]), path);
  }

  hasFile(ref: string, path: string): boolean {
    try {
      this.git(["cat-file", "-e", `${ref}:${path}`]);
      return true;
    } catch {
      return false;
    }
  }
}

/** Dekodim strikt UTF-8; input invalid → ScanError (pa e publikuar përmbajtjen). */
export function decodeStrictUtf8(buf: Buffer, safePathLabel: string): string {
  try {
    return new TextDecoder("utf-8", { fatal: true }).decode(buf);
  } catch {
    throw new ScanError(`invalid UTF-8 encoding in tracked object (${safePathLabel})`);
  }
}
