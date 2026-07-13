#!/usr/bin/env node
/**
 * CLI — advisory scanner (GOV-001 baseline).
 *
 * Exit codes:
 *   0 — scan përfundoi; advisory findings lejohen;
 *   2 — engine/config/input/git/incomplete-scan error (fail-closed).
 *
 * I/O boundary: Git objects + (opsionale) GITHUB_EVENT_PATH — i vetmi lexim
 * jashtë Git objects, vetëm për metadata të PR-së (base/head SHA + body).
 * Zero network. Zero auto-fix. Zero raw content në output.
 */

import { readFileSync } from "node:fs";
import { GitCliReader } from "./git-repository.js";
import { runScan } from "./engine.js";
import { toJsonReport, toTextReport, countBySeverity } from "./reporter.js";
import { ConfigError, ScanError, type ProfileId, type PullRequestEvent, type ScanConfig } from "./types.js";

interface CliArgs {
  profiles: string[];
  mode: string;
  base?: string;
  head?: string;
  docsRef: string;
  format: "text" | "json" | "both";
  cwd: string;
}

export function parseArgs(argv: string[]): CliArgs {
  const args: CliArgs = {
    profiles: ["DOC-CORE", "HISTORY-PRIV"],
    mode: "advisory",
    docsRef: "HEAD",
    format: "text",
    cwd: process.cwd(),
  };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const next = (): string => {
      const v = argv[++i];
      if (v === undefined) throw new ConfigError(`missing value for ${a}`);
      return v;
    };
    switch (a) {
      case "--profiles": args.profiles = next().split(",").map((s) => s.trim().toUpperCase()); break;
      case "--mode": args.mode = next(); break;
      case "--base": args.base = next(); break;
      case "--head": args.head = next(); break;
      case "--docs-ref": args.docsRef = next(); break;
      case "--format": {
        const f = next();
        if (f !== "text" && f !== "json" && f !== "both") throw new ConfigError(`invalid --format ${f}`);
        args.format = f;
        break;
      }
      case "--cwd": args.cwd = next(); break;
      default: throw new ConfigError(`unknown argument: ${a}`);
    }
  }
  return args;
}

/** I vetmi lexim jashtë Git objects: GITHUB_EVENT_PATH (metadata PR). */
export function readPullRequestEvent(eventPath: string | undefined): PullRequestEvent | undefined {
  if (eventPath === undefined || eventPath === "") return undefined;
  let parsed: unknown;
  try {
    parsed = JSON.parse(readFileSync(eventPath, "utf8"));
  } catch {
    throw new ConfigError("GITHUB_EVENT_PATH exists but is unreadable/malformed");
  }
  const pr = (parsed as { pull_request?: { base?: { sha?: string }; head?: { sha?: string }; body?: string | null } }).pull_request;
  if (pr === undefined) return undefined;
  const ev: PullRequestEvent = {};
  if (typeof pr.base?.sha === "string") ev.baseSha = pr.base.sha;
  if (typeof pr.head?.sha === "string") ev.headSha = pr.head.sha;
  if (typeof pr.body === "string") ev.body = pr.body;
  return ev;
}

export function main(argv: string[], env: NodeJS.ProcessEnv): number {
  try {
    const args = parseArgs(argv);
    const event = readPullRequestEvent(env["GITHUB_EVENT_PATH"]);
    const config: ScanConfig = {
      mode: args.mode as ScanConfig["mode"], // validohet në engine (required → refuzim)
      profiles: args.profiles as ProfileId[], // validohet në engine (unknown → refuzim)
      docsRef: args.docsRef,
      ...(args.base !== undefined ? { baseSha: args.base } : {}),
      ...(args.head !== undefined ? { headSha: args.head } : {}),
      ...(event !== undefined ? { event } : {}),
    };
    const reader = new GitCliReader(args.cwd);
    const result = runScan(reader, config);
    if (args.format === "text" || args.format === "both") process.stdout.write(toTextReport(result));
    if (args.format === "json" || args.format === "both") process.stdout.write(toJsonReport(result));
    const counts = countBySeverity(result.findings);
    process.stdout.write(`advisory: exit 0 (findings ERROR=${counts.ERROR} WARNING=${counts.WARNING} REVIEW=${counts.REVIEW})\n`);
    return 0;
  } catch (err) {
    if (err instanceof ConfigError || err instanceof ScanError) {
      process.stderr.write(`${err.name}: ${err.message}\n`);
      return 2;
    }
    process.stderr.write(`EngineError: ${err instanceof Error ? err.message.split("\n")[0] : "unknown"}\n`);
    return 2;
  }
}

const invokedDirectly = process.argv[1] !== undefined && import.meta.url.endsWith(process.argv[1].split("/").pop() ?? "");
if (invokedDirectly) {
  process.exitCode = main(process.argv.slice(2), process.env);
}
