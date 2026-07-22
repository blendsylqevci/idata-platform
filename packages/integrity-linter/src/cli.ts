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

import { closeSync, constants as fsConstants, fstatSync, openSync, readSync, realpathSync } from "node:fs";
import { resolve as resolvePath } from "node:path";
import { pathToFileURL } from "node:url";
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
  /** Lexim eksplicit i event file lokalisht — env-i NUK lexohet implicit (§8). */
  eventFile?: string;
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
      case "--event-file": args.eventFile = next(); break;
      default: throw new ConfigError("ARG_UNKNOWN");
    }
  }
  return args;
}

/**
 * Lexim i forcuar i PR event-it — input i jashtëm, i pabesuar (§8):
 *  - O_NOFOLLOW + fstat mbi FD (TOCTOU-safe; symlink → error);
 *  - regular file only; size cap fiks; lexim nga i njëjti FD;
 *  - `pull_request` null/mungues → EVENT_MALFORMED (kur leximi është kërkuar).
 */
export function readPullRequestEvent(eventPath: string | undefined): PullRequestEvent | undefined {
  if (eventPath === undefined || eventPath === "") return undefined;
  let fd: number;
  try {
    fd = openSync(eventPath, fsConstants.O_RDONLY | fsConstants.O_NOFOLLOW);
  } catch {
    throw new ConfigError("EVENT_NOT_REGULAR_FILE"); // symlink ose i palexueshëm
  }
  let raw: string;
  try {
    const st = fstatSync(fd);
    if (!st.isFile()) throw new ConfigError("EVENT_NOT_REGULAR_FILE");
    if (st.size > MAX_EVENT_BYTES) throw new ConfigError("EVENT_OVERSIZE");
    raw = readExactly(fd, Number(st.size)).toString("utf8");
  } finally {
    closeSync(fd);
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw new ConfigError("EVENT_MALFORMED");
  }
  // §8 SKEMA STRIKTE (P-IV; kontrata e GitHub Actions pull_request):
  // root = object jo-array; pull_request = object jo-array;
  // base/head = MANDATORY plain objects me sha MANDATORY SHA-40 —
  // node/sha mungues/null ose tip invalid = exit 2;
  // body = string (me cap) OSE null/mungues = BODY I NJOHUR BOSH ("") —
  // event prezent ≠ "pa event": PR-body vlerësohet EVALUATED me body bosh.
  const isPlainObject = (v: unknown): v is Record<string, unknown> =>
    typeof v === "object" && v !== null && !Array.isArray(v);
  if (!isPlainObject(parsed)) throw new ConfigError("EVENT_MALFORMED");
  const pr = parsed["pull_request"];
  if (!isPlainObject(pr)) throw new ConfigError("EVENT_MALFORMED");
  const ev: PullRequestEvent = {};
  for (const side of ["base", "head"] as const) {
    const node = pr[side];
    if (!isPlainObject(node)) throw new ConfigError("EVENT_MALFORMED"); // mungues/null/tip
    const sha = node["sha"];
    if (sha === undefined || sha === null) throw new ConfigError("EVENT_MALFORMED");
    if (typeof sha !== "string" || !SHA40.test(sha)) throw new ConfigError("EVENT_SHA_INVALID");
    if (side === "base") ev.baseSha = sha;
    else ev.headSha = sha;
  }
  const body = pr["body"];
  if (body === undefined || body === null) {
    ev.body = ""; // §8: i njohur BOSH — jo i barabartë me "pa event"
  } else if (typeof body === "string") {
    if (Buffer.byteLength(body, "utf8") > MAX_BODY_BYTES) throw new ConfigError("EVENT_BODY_OVERSIZE");
    ev.body = body;
  } else {
    throw new ConfigError("EVENT_MALFORMED"); // object/array/number/boolean
  }
  return ev;
}

/**
 * P-V H4 — lexim i PLOTË nga i njëjti FD: readSync në loop deri në madhësinë
 * e pritur; bytesRead=0 para plotësimit (file shrink / partial read) →
 * ConfigError EVENT_SHORT_READ (fail-closed, pa raw content në error).
 * `readFn` i injektueshëm vetëm për testueshmëri — trust boundary i pandryshuar.
 */
export function readExactly(
  fd: number,
  size: number,
  readFn: (fd: number, buf: Buffer, offset: number, length: number, position: number) => number = readSync,
): Buffer {
  const buf = Buffer.alloc(size);
  let off = 0;
  while (off < size) {
    const n = readFn(fd, buf, off, size - off, off);
    if (n === 0) throw new ConfigError("EVENT_SHORT_READ");
    off += n;
  }
  return buf;
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
    // §8 TRUST BOUNDARY: GITHUB_EVENT_PATH lexohet AUTOMATIKISHT vetëm brenda
    // GitHub Actions me event pull_request; çdo event tjetër INJOROHET (kontratë
    // e dokumentuar). Lokalisht përdoret vetëm flag-u eksplicit --event-file.
    const inActionsPr = env["GITHUB_ACTIONS"] === "true" && env["GITHUB_EVENT_NAME"] === "pull_request";
    const eventPath = args.eventFile ?? (inActionsPr ? env["GITHUB_EVENT_PATH"] : undefined);
    const event = readPullRequestEvent(eventPath);
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

// P-V H1 — krahasim REAL URL/path (cross-platform), jo vetëm basename:
// import-i i këtij moduli nga një file tjetër me basename "cli.js" NUK e
// ekzekuton main; ekzekutimi direkt (edhe me hapësira/symlink në path)
// funksionon — realpath si Node-i për entry-n kryesor.
const invokedDirectly = ((): boolean => {
  const argv1 = process.argv[1];
  if (argv1 === undefined) return false;
  try {
    return import.meta.url === pathToFileURL(realpathSync(resolvePath(argv1))).href;
  } catch {
    return false; // path i pazgjidhshëm ⇒ sigurisht jo ky modul
  }
})();
if (invokedDirectly) {
  process.exitCode = main(process.argv.slice(2), process.env);
}
