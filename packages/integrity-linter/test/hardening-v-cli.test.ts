/**
 * P-V H1–H4 — CLI direct-execution, duplicate profiles, sanitizer i
 * jo-koliziv, event read loop. Fixtures sintetike.
 */
import { beforeAll, describe, expect, it } from "vitest";
import { execFileSync, spawnSync } from "node:child_process";
import { cpSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { makeTempRepo, writeFullCanonicalRepo } from "./helpers.js";
import { readExactly } from "../src/cli.js";
import { sanitizeLocator } from "../src/reporter.js";
import { runScan } from "../src/engine.js";
import { GitCliReader } from "../src/git-repository.js";
import { ConfigError } from "../src/types.js";

const PKG_DIR = process.cwd();
const CLI = join(PKG_DIR, "dist", "cli.js");

beforeAll(() => {
  execFileSync("pnpm", ["run", "build"], { cwd: PKG_DIR, stdio: "ignore" });
}, 120_000);

function runNode(script: string[], env: Record<string, string> = {}) {
  const r = spawnSync(process.execPath, script, {
    encoding: "utf8",
    env: { ...process.env, GITHUB_EVENT_PATH: "", GITHUB_ACTIONS: "", GITHUB_EVENT_NAME: "", ...env },
  });
  return { status: r.status, stdout: r.stdout, stderr: r.stderr };
}

describe("H1 — direct execution guard: URL/path real, jo basename", () => {
  it("wrapper /tmp/**/cli.js që importon dist/cli.js NUK e ekzekuton main-in e dist-it", () => {
    const dir = mkdtempSync(join(tmpdir(), "linter-h1-"));
    const wrapper = join(dir, "cli.js"); // basename IDENTIK me dist/cli.js
    writeFileSync(wrapper, `import ${JSON.stringify("file://" + CLI)}; console.log("WRAPPER_DONE");\n`);
    const { status, stdout, stderr } = runNode([wrapper]);
    expect(status).toBe(0); // me guard-in e vjetër main() do të kthente exit 2 (pa repo)
    expect(stdout).toContain("WRAPPER_DONE");
    expect(stdout).not.toContain("integrity-linter mode=");
    expect(stderr).not.toContain("ConfigError");
  });

  it("ekzekutimi direkt funksionon — edhe nga path me HAPËSIRA", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo);
    const spaced = mkdtempSync(join(tmpdir(), "linter dist me hapesira-"));
    cpSync(join(PKG_DIR, "dist"), join(spaced, "dist"), { recursive: true });
    const { status, stdout } = runNode([join(spaced, "dist", "cli.js"), "--profiles", "DOC-CORE", "--format", "text", "--cwd", repo.dir]);
    expect(status).toBe(0);
    expect(stdout).toContain("integrity-linter mode=advisory");
  });
});

describe("H2 — duplicate profiles refuzohen fail-closed", () => {
  it("DOC-CORE,DOC-CORE → exit 2, DUPLICATE_PROFILE, stdout bosh në JSON", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo);
    const { status, stdout, stderr } = runNode([CLI, "--profiles", "DOC-CORE,DOC-CORE", "--format", "json", "--cwd", repo.dir]);
    expect(status).toBe(2);
    expect(stderr).toContain("DUPLICATE_PROFILE");
    expect(stdout).toBe(""); // zero JSON i pjesshëm
  });

  it("unit: runScan hedh DUPLICATE_PROFILE; profile unike → sjellja ekzistuese", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo);
    const reader = new GitCliReader(repo.dir);
    expect(() => runScan(reader, { mode: "advisory", profiles: ["DOC-CORE", "DOC-CORE"] as never, docsRef: "HEAD" }))
      .toThrow("DUPLICATE_PROFILE");
    expect(() => runScan(reader, { mode: "advisory", profiles: ["DOC-CORE"] as never, docsRef: "HEAD" })).not.toThrow();
  });
});

describe("H3 — sanitizer deterministik dhe JO-koliziv", () => {
  it("raw 'a|b' ≠ raw literal backslash-u007c-b", () => {
    const rawPipe = "a|b";
    const rawLiteral = "a\\u007cb"; // backslash real + 'u007cb'
    expect(sanitizeLocator(rawPipe)).not.toBe(sanitizeLocator(rawLiteral));
    expect(sanitizeLocator(rawPipe)).toBe("a\\u007cb");
    expect(sanitizeLocator(rawLiteral)).toBe("a\\\\u007cb");
  });

  it("backslash/backtick/CR/LF/kontroll: zero raw control chars, encoding i paekuivok", () => {
    const s = sanitizeLocator("x\\y`z\r\n" + String.fromCharCode(1) + String.fromCharCode(0x7f));
    expect(s).toBe("x\\\\y\\u0060z\\r\\n\\u0001\\u007f");
    for (const ch of s) {
      const c = ch.codePointAt(0) ?? 0;
      expect(c >= 0x20 && c !== 0x7f).toBe(true);
    }
  });

  it("corpus pairwise: inputs të ndryshëm → outputs të ndryshëm; determinizëm", () => {
    const corpus = [
      "a|b", "a\\u007cb", "a\\|b", "a\\\\|b", "a`b", "a\\u0060b",
      "a\rb", "a\\rb", "a\nb", "a\\nb", "a\\b", "a\\\\b",
      "a" + String.fromCharCode(1) + "b", "a\\u0001b", "plain",
    ];
    const outputs = corpus.map((c) => sanitizeLocator(c));
    for (let i = 0; i < corpus.length; i++) {
      for (let j = i + 1; j < corpus.length; j++) {
        expect(outputs[i]).not.toBe(outputs[j]);
      }
      expect(sanitizeLocator(corpus[i] as string)).toBe(outputs[i]); // determinizëm
    }
  });
});

describe("H4 — event read loop: bytesRead respektohet", () => {
  it("lexim i plotë në copa të vogla → OK; bytesRead=0 para plotësimit → EVENT_SHORT_READ", () => {
    const data = Buffer.from("abcdefghij");
    const chunked = (_fd: number, buf: Buffer, offset: number, length: number, position: number): number => {
      const n = Math.min(3, length, data.length - position);
      data.copy(buf, offset, position, position + n);
      return n;
    };
    expect(readExactly(0, 10, chunked).toString("utf8")).toBe("abcdefghij");

    const shrinking = (_fd: number, buf: Buffer, offset: number, length: number, position: number): number => {
      if (position >= 4) return 0; // file u tkurr — s'ka më bajte
      const n = Math.min(2, length, 4 - position);
      data.copy(buf, offset, position, position + n);
      return n;
    };
    expect(() => readExactly(0, 10, shrinking)).toThrow(ConfigError);
    expect(() => readExactly(0, 10, shrinking)).toThrow("EVENT_SHORT_READ");
  });

  it("rrjedha reale e event-it mbetet funksionale (madhësi ekzakte, i njëjti FD)", () => {
    const dir = mkdtempSync(join(tmpdir(), "linter-h4-"));
    const p = join(dir, "event.json");
    const sha = (c: string) => c.repeat(40);
    writeFileSync(p, JSON.stringify({ pull_request: { base: { sha: sha("a") }, head: { sha: sha("b") }, body: "ok" } }));
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo);
    const { status } = runNode([CLI, "--profiles", "DOC-CORE", "--format", "json", "--event-file", p, "--cwd", repo.dir]);
    expect(status).toBe(0);
  });
});
