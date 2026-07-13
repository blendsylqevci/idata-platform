#!/usr/bin/env node
/**
 * CLI — advisory scanner (GOV-001 baseline, i FORCUAR).
 *
 * Exit codes:
 *   0 — scan i PLOTË përfundoi; advisory findings lejohen;
 *   2 — engine/config/input/git/parser/incomplete-scan error (fail-closed).
 *
 * KONTRATA E OUTPUT-it:
 *   --format text  → raport human në stdout;
 *   --format json  → stdout përmban VETËM një dokument JSON valid
 *                    (diagnostics/human summary → stderr);
 *   --format both  → JSON në stdout, raporti human në stderr.
 *   Në error (exit 2): stdout NUK merr JSON të korruptuar; stderr merr
 *   vetëm kodin fiks të sanitizuar (kurrë raw git message/argv/ref/path/body).
 *
 * GITHUB_EVENT_PATH (i vetmi lexim jashtë Git objects) trajtohet si INPUT I
 * JASHTËM: regular file (jo symlink), size caps, parse vetëm base.sha/head.sha/
 * body, SHA 40-hex, përputhje e detyrueshme me --base/--head kur jepen të dyja.
 */

import { lstatSync, readFileSync } from "node:fs";
import { GitCliReader } from "./git-repository.js";
import { runScan } from "./engine.js";
import { toJsonReport, toTextReport, countBySeverity } from "./reporter.js";
import { ConfigError, ScanError, type ProfileId, type PullRequestEvent, type ScanConfig } from "./types.js";

const MAX_EVENT_BYTES = 1_000_000;
const MAX_BODY_BYTES = 65_536;
const SHA40 = /^[0-9a-f]{40}$/;

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
      if (v === undefined) throw new ConfigError("ARG_VALUE_MISSING");
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
        if (f !== "text" && f !== "json" && f !== "both") throw new ConfigError("FORMAT_INVALID");
        args.format = f;
        break;
      }
      case "--cwd": args.cwd = next(); break;
      default: throw new ConfigError("ARG_UNKNOWN");
    }
  }
  return args;
}

/** Lexim i forcuar i PR event-it — input i jashtëm, i pabesuar. */
export function readPullRequestEvent(eventPath: string | undefined): PullRequestEvent | undefined {
  if (eventPath === undefined || eventPath === "") return undefined;
  let st;
  try {
    st = lstatSync(eventPath);
  } catch {
    throw new ConfigError("EVENT_UNREADABLE");
  }
  if (!st.isFile() || st.isSymbolicLink()) throw new ConfigError("EVENT_NOT_REGULAR_FILE");
  if (st.size > MAX_EVENT_BYTES) throw new ConfigError("EVENT_OVERSIZE");
  let parsed: unknown;
  try {
    parsed = JSON.parse(readFileSync(eventPath, "utf8"));
  } catch {
    throw new ConfigError("EVENT_MALFORMED");
  }
  const pr = (parsed as { pull_request?: { base?: { sha?: unknown }; head?: { sha?: unknown }; body?: unknown } }).pull_request;
  if (pr === undefined) return undefined;
  const ev: PullRequestEvent = {};
  if (pr.base?.sha !== undefined) {
    if (typeof pr.base.sha !== "string" || !SHA40.test(pr.base.sha)) throw new ConfigError("EVENT_SHA_INVALID");
    ev.baseSha = pr.base.sha;
  }
  if (pr.head?.sha !== undefined) {
    if (typeof pr.head.sha !== "string" || !SHA40.test(pr.head.sha)) throw new ConfigError("EVENT_SHA_INVALID");
    ev.headSha = pr.head.sha;
  }
  if (typeof pr.body === "string") {
    if (Buffer.byteLength(pr.body, "utf8") > MAX_BODY_BYTES) throw new ConfigError("EVENT_BODY_OVERSIZE");
    ev.body = pr.body;
  }
  return ev;
}

/** Kur CLI merr --base/--head DHE event-i mban SHA, duhet të përputhen. */
export function enforceRangeConsistency(args: { base?: string; head?: string }, ev: PullRequestEvent | undefined): void {
  if (ev === undefined) return;
  if (args.base !== undefined && ev.baseSha !== undefined && args.base !== ev.baseSha) {
    throw new ConfigError("EVENT_RANGE_MISMATCH");
  }
  if (args.head !== undefined && ev.headSha !== undefined && args.head !== ev.headSha) {
    throw new ConfigError("EVENT_RANGE_MISMATCH");
  }
}

export function main(argv: string[], env: NodeJS.ProcessEnv): number {
  let format: CliArgs["format"] = "text";
  try {
    const args = parseArgs(argv);
    format = args.format;
    const event = readPullRequestEvent(env["GITHUB_EVENT_PATH"]);
    enforceRangeConsistency(args, event);
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
    const counts = countBySeverity(result.findings);
    const trailer = `advisory: exit 0 (findings ERROR=${counts.ERROR} WARNING=${counts.WARNING} REVIEW=${counts.REVIEW})\n`;
    if (format === "text") {
      process.stdout.write(toTextReport(result));
      process.stdout.write(trailer);
    } else if (format === "json") {
      process.stdout.write(toJsonReport(result)); // stdout = VETËM JSON
      process.stderr.write(trailer);
    } else {
      process.stdout.write(toJsonReport(result)); // stdout = VETËM JSON
      process.stderr.write(toTextReport(result));
      process.stderr.write(trailer);
    }
    return 0;
  } catch (err) {
    // Vetëm kode fikse të sanitizuara; stdout nuk ndotet me JSON të pjesshëm.
    if (err instanceof ConfigError || err instanceof ScanError) {
      process.stderr.write(`${err.name}: ${err.code}\n`);
      return 2;
    }
    process.stderr.write("EngineError: UNEXPECTED\n");
    return 2;
  }
}

const invokedDirectly = process.argv[1] !== undefined && import.meta.url.endsWith(process.argv[1].split("/").pop() ?? "");
if (invokedDirectly) {
  process.exitCode = main(process.argv.slice(2), process.env);
}
