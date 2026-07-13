/**
 * GitCliReader — implementimi i GitReader mbi CLI-në e git-it.
 *
 * Kufij (07_PRODATA §Linter + hardening PR #21):
 *  - vetëm Git objects të tracked (`show/diff-tree/rev-list/merge-base/cat-file`);
 *  - `execFileSync` me argument arrays — kurrë shell;
 *  - metadata merren me `diff-tree --raw -z --no-renames` (rename = D+A;
 *    ASNJË similarity detection që lexon blob para policy gate);
 *  - blob-et maten me OBJECT-ID (`cat-file -s <oid>`), jo me path;
 *  - path-et parsohen nga Buffer NUL-delimited me UTF-8 STRIKT — path me
 *    tab/newline/kontroll/non-UTF8 → INCOMPLETE_SCAN pa e publikuar path-in;
 *  - gabimet mbajnë VETËM kode fikse (asnjë raw git message/argv/ref/path);
 *  - fail-closed: çdo dështim git → ScanError (exit 2).
 */

import { execFileSync } from "node:child_process";
import { ScanError, type AddedContent, type GitReader, type RawEntry } from "./types.js";

const MAX_GIT_OUTPUT = 64 * 1024 * 1024;

export class GitCliReader implements GitReader {
  constructor(private readonly cwd: string) {}

  /** Ekzekutim i sigurt; dështimi kthen VETËM kod fiks (pa message/argv/path). */
  private git(opLabel: string, args: string[]): Buffer {
    try {
      return execFileSync("git", args, {
        cwd: this.cwd,
        maxBuffer: MAX_GIT_OUTPUT,
        stdio: ["ignore", "pipe", "pipe"],
      });
    } catch {
      throw new ScanError(`GIT_OP_FAILED:${opLabel}`);
    }
  }

  private gitText(opLabel: string, args: string[]): string {
    return decodeStrictUtf8(this.git(opLabel, args), `GIT_OUTPUT_ENCODING:${opLabel}`);
  }

  revParse(ref: string): string {
    const out = this.gitText("rev-parse", ["rev-parse", "--verify", "--end-of-options", `${ref}^{commit}`]).trim();
    if (!/^[0-9a-f]{40}$/.test(out)) throw new ScanError("GIT_REF_INVALID");
    return out;
  }

  isShallow(): boolean {
    return this.gitText("shallow-check", ["rev-parse", "--is-shallow-repository"]).trim() === "true";
  }

  mergeBase(a: string, b: string): string {
    const out = this.gitText("merge-base", ["merge-base", a, b]).trim();
    if (!/^[0-9a-f]{40}$/.test(out)) throw new ScanError("GIT_MERGE_BASE_INVALID");
    return out;
  }

  revList(baseExclusive: string, head: string): string[] {
    const out = this.gitText("rev-list", ["rev-list", "--reverse", `${baseExclusive}..${head}`]).trim();
    return out === "" ? [] : out.split("\n");
  }

  parents(sha: string): string[] {
    const out = this.gitText("rev-list-parents", ["rev-list", "--parents", "-n", "1", sha]).trim().split(/\s+/);
    return out.slice(1);
  }

  rawEntries(sha: string): RawEntry[] {
    // ":oldmode newmode oldoid newoid status\0path\0" — PA rename detection.
    const raw = this.git("diff-tree-raw", [
      "diff-tree", "-r", "--raw", "-z", "--no-renames", "--no-commit-id", "--abbrev=40", "--root", sha,
    ]);
    const entries: RawEntry[] = [];
    let i = 0;
    while (i < raw.length) {
      if (raw[i] !== 0x3a /* ':' */) throw new ScanError("DIFF_TREE_PARSE_FAILED");
      const metaEnd = raw.indexOf(0x00, i);
      if (metaEnd === -1) throw new ScanError("DIFF_TREE_PARSE_FAILED");
      const metaBuf = raw.subarray(i, metaEnd);
      // metadata duhet të jetë ASCII e pastër
      for (const b of metaBuf) {
        if (b < 0x20 || b > 0x7e) throw new ScanError("DIFF_TREE_META_ENCODING");
      }
      const fields = metaBuf.toString("ascii").slice(1).split(" ").filter((f) => f !== "");
      const [oldMode, newMode, oldOid, newOid, statusFull] = fields;
      if (
        oldMode === undefined || newMode === undefined || oldOid === undefined ||
        newOid === undefined || statusFull === undefined || statusFull.length !== 1
      ) {
        throw new ScanError("DIFF_TREE_PARSE_FAILED");
      }
      const pathEnd = raw.indexOf(0x00, metaEnd + 1);
      if (pathEnd === -1) throw new ScanError("DIFF_TREE_PARSE_FAILED");
      const pathBuf = raw.subarray(metaEnd + 1, pathEnd);
      // path: UTF-8 STRIKT dhe pa kontroll-karaktere (tab/newline/etj.)
      const path = decodeStrictUtf8(pathBuf, "PATH_ENCODING_INVALID");
      for (const ch of path) {
        const c = ch.codePointAt(0) ?? 0;
        if (c < 0x20 || c === 0x7f) throw new ScanError("PATH_CONTROL_CHARS");
      }
      entries.push({ oldMode, newMode, oldOid, newOid, status: statusFull, path });
      i = pathEnd + 1;
    }
    return entries;
  }

  blobSizeByOid(oid: string): number {
    if (!/^[0-9a-f]{40}$/.test(oid)) throw new ScanError("BLOB_OID_INVALID");
    const out = this.gitText("cat-file-size", ["cat-file", "-s", oid]).trim();
    const n = Number.parseInt(out, 10);
    if (!Number.isFinite(n) || n < 0) throw new ScanError("BLOB_SIZE_UNREADABLE");
    return n;
  }

  addedContent(sha: string, path: string): AddedContent {
    const buf = this.git("show-diff", [
      "show", sha, "--format=", "--unified=0", "--no-renames", "--", path,
    ]);
    const text = decodeStrictUtf8(buf, "DIFF_CONTENT_ENCODING");
    return parseUnifiedAddedLines(text);
  }

  showFile(ref: string, path: string): string {
    return decodeStrictUtf8(this.git("show-file", ["show", `${ref}:${path}`]), "FILE_CONTENT_ENCODING");
  }

  hasFile(ref: string, path: string): boolean {
    // KONTRATË: ref-i duhet të jetë validuar më parë me revParse();
    // atëherë dështimi i vetëm i mundshëm këtu = path-i mungon në atë ref.
    try {
      this.git("cat-file-exists", ["cat-file", "-e", `${ref}:${path}`]);
      return true;
    } catch {
      return false;
    }
  }
}

/** Dekodim strikt UTF-8; input invalid → ScanError me kod fiks (pa content). */
export function decodeStrictUtf8(buf: Buffer, errorCode: string): string {
  try {
    return new TextDecoder("utf-8", { fatal: true }).decode(buf);
  } catch {
    throw new ScanError(errorCode);
  }
}

/**
 * Parser i diff-it unified=0 me NUMRA REALË rreshtash të target file-it.
 *
 *  - "+++ b/x" njihet si header VETËM në header-state (para çdo hunk-u);
 *  - brenda hunk-ut, çdo rresht që fillon me '+' është ADDED CONTENT —
 *    edhe kur content-i real fillon me '+', '++' ose '+++';
 *  - "Binary files ..." / "GIT binary patch" → binary=true, zero rreshta;
 *  - strukturë e papritur → ScanError (INCOMPLETE_SCAN).
 */
export function parseUnifiedAddedLines(diffText: string): AddedContent {
  const lines = diffText.split("\n");
  const added: Array<{ line: number; text: string }> = [];
  let inHunk = false;
  let targetLine = 0;
  let expectedInHunk = 0;
  for (const raw of lines) {
    if (!inHunk) {
      if (raw.startsWith("Binary files ") || raw === "GIT binary patch") {
        return { binary: true, lines: [] };
      }
      if (raw.startsWith("@@")) {
        const m = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/.exec(raw);
        if (!m || m[1] === undefined) throw new ScanError("HUNK_HEADER_PARSE_FAILED");
        targetLine = Number.parseInt(m[1], 10);
        expectedInHunk = m[2] !== undefined ? Number.parseInt(m[2], 10) : 1;
        inHunk = expectedInHunk > 0;
      }
      // header lines (diff --git / index / --- / +++ / mode) injorohen këtu
      continue;
    }
    // brenda hunk-ut (unified=0: vetëm +/- rreshta)
    if (raw.startsWith("+")) {
      added.push({ line: targetLine, text: raw.slice(1) });
      targetLine += 1;
      expectedInHunk -= 1;
      if (expectedInHunk <= 0) inHunk = false;
    } else if (raw.startsWith("-") || raw.startsWith("\\")) {
      // removed line ose "\ No newline at end of file" — nuk ndikon target counter
    } else if (raw.startsWith("@@")) {
      const m = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/.exec(raw);
      if (!m || m[1] === undefined) throw new ScanError("HUNK_HEADER_PARSE_FAILED");
      targetLine = Number.parseInt(m[1], 10);
      expectedInHunk = m[2] !== undefined ? Number.parseInt(m[2], 10) : 1;
      inHunk = expectedInHunk > 0;
    } else if (raw === "") {
      inHunk = false;
    } else if (raw.startsWith("diff ")) {
      inHunk = false;
    } else {
      throw new ScanError("DIFF_STRUCTURE_UNEXPECTED");
    }
  }
  return { binary: false, lines: added };
}
