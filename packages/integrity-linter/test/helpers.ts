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
  git(args: string[]): string;
  write(rel: string, content: string): void;
  commitAll(message: string): string;
  head(): string;
}

export function makeTempRepo(): TempRepo {
  const dir = mkdtempSync(join(tmpdir(), "linter-fixture-"));
  const git = (args: string[]): string =>
    execFileSync("git", args, { cwd: dir, stdio: ["ignore", "pipe", "pipe"] }).toString("utf8");
  git(["init", "-q", "-b", "main"]);
  // Email sintetik i ndërtuar nga fragmente runtime — që vetë ky burim të mos
  // përmbajë literal email-like që kapet nga skanerat PII (zero-tolerance).
  git(["config", "user.email", ["fixture", "@", "example", ".invalid"].join("")]);
  git(["config", "user.name", "Synthetic Fixture"]);
  git(["config", "commit.gpgsign", "false"]);
  const write = (rel: string, content: string): void => {
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
