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
import { ScanError, type AddedContent, type GitReader, type RawEntry, type TreeEntry } from "./types.js";

const MAX_GIT_OUTPUT = 64 * 1024 * 1024;

export class GitCliReader implements GitReader {
  constructor(private readonly cwd: string) {}

  /**
   * Ekzekutim i sigurt; dështimi kthen VETËM kod fiks (pa message/argv/path).
   * P-V C1: `--literal-pathspecs` GLOBAL dhe I CENTRALIZUAR — çdo path që
   * vjen nga metadata trajtohet LITERAL: `[x].md` s'përputhet me `x.md`,
   * `*.md` s'bën glob, asnjë pathspec magic. Mbulon show/ls-tree/diff-tree
   * dhe çdo command të ardhshëm pa patch ad-hoc per-command.
   */
  private git(opLabel: string, args: string[]): Buffer {
    try {
      return execFileSync("git", [
        "--no-pager", "--literal-pathspecs",
        "-c", "color.ui=false", "-c", "color.diff=false",
        "-c", "core.pager=cat", "-c", "pager.show=false",
        ...args,
      ], {
        cwd: this.cwd,
        maxBuffer: MAX_GIT_OUTPUT,
        stdio: ["ignore", "pipe", "pipe"],
        env: {
          ...process.env,
          LC_ALL: "C", LANG: "C", GIT_PAGER: "cat", PAGER: "cat",
          TERM: "dumb", NO_COLOR: "1",
        },
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

  treeEntries(ref: string): TreeEntry[] {
    const raw = this.git("ls-tree-all", ["ls-tree", "-r", "-z", "--full-tree", ref]);
    const entries: TreeEntry[] = [];
    let at = 0;
    while (at < raw.length) {
      const end = raw.indexOf(0x00, at);
      if (end === -1) throw new ScanError("LS_TREE_PARSE_FAILED");
      const record = raw.subarray(at, end);
      const tab = record.indexOf(0x09);
      if (tab <= 0) throw new ScanError("LS_TREE_PARSE_FAILED");
      const meta = record.subarray(0, tab);
      for (const b of meta) {
        if (b < 0x20 || b > 0x7e) throw new ScanError("LS_TREE_META_ENCODING");
      }
      const [mode, type, oid] = meta.toString("ascii").split(" ");
      if (
        mode === undefined || type !== "blob" || oid === undefined ||
        !/^\d{6}$/.test(mode) || !/^[0-9a-f]{40}$/.test(oid)
      ) {
        throw new ScanError("LS_TREE_PARSE_FAILED");
      }
      const path = decodeRepoPath(record.subarray(tab + 1));
      entries.push({ mode, oid, path });
      at = end + 1;
    }
    return entries;
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
      const path = decodeRepoPath(pathBuf);
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

  blobByOid(oid: string): Buffer {
    if (!/^[0-9a-f]{40}$/.test(oid)) throw new ScanError("BLOB_OID_INVALID");
    return this.git("cat-file-blob", ["cat-file", "blob", oid]);
  }

  addedContent(sha: string, path: string): AddedContent {
    // --no-ext-diff / --no-textconv: asnjë driver i jashtëm ose textconv nuk
    // ekzekutohet kurrë (edhe kur .gitattributes e kërkon) — provohet me fixture.
    // --text (P-IV §3): klasifikimi tekst/binar NUK i besohet diff-it —
    // .gitattributes `binary`/`-diff` s'mund ta fshehë tekstin nga skanimi;
    // blob-et realisht binare refuzohen PËRPARA kësaj me blobByOid (NUL/UTF-8).
    const buf = this.git("show-diff", [
      "show", sha, "--format=", "--unified=0", "--no-renames", "--no-ext-diff", "--no-textconv", "--text", "--", path,
    ]);
    const text = decodeStrictUtf8(buf, "DIFF_CONTENT_ENCODING");
    return parseUnifiedAddedLines(text);
  }

  showFile(ref: string, path: string): string {
    return decodeStrictUtf8(this.git("show-file", ["show", `${ref}:${path}`]), "FILE_CONTENT_ENCODING");
  }

  hasFile(ref: string, path: string): boolean {
    // KONTRATË (§4): dallon "path realisht mungon" (ls-tree OK me output bosh →
    // false) nga dështimi operacional i Git-it (ls-tree exit ≠ 0 → ScanError →
    // exit 2). Asnjë catch-all → false.
    const out = this.git("ls-tree", ["ls-tree", "-z", "--name-only", ref, "--", path]);
    return out.length > 0;
  }
}

function decodeRepoPath(pathBuf: Buffer): string {
  const path = decodeStrictUtf8(pathBuf, "PATH_ENCODING_INVALID");
  if (/\p{Default_Ignorable_Code_Point}/u.test(path)) {
    throw new ScanError("PATH_FORMAT_CHARS");
  }
  for (const ch of path) {
    const c = ch.codePointAt(0) ?? 0;
    if (c < 0x20 || c === 0x7f) throw new ScanError("PATH_CONTROL_CHARS");
    if (
      (c >= 0x200b && c <= 0x200f) || (c >= 0x202a && c <= 0x202e) ||
      (c >= 0x2066 && c <= 0x2069) || c === 0xfeff
    ) {
      throw new ScanError("PATH_FORMAT_CHARS");
    }
  }
  if (path.startsWith("-") || path.startsWith(":")) {
    throw new ScanError("PATH_SUSPICIOUS_PREFIX");
  }
  return path;
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
  if (diffText.includes("\u001b")) throw new ScanError("DIFF_STRUCTURE_UNEXPECTED");
  const lines = diffText.split("\n");
  const added: Array<{ line: number; text: string }> = [];
  let inHunk = false;
  let hadHunk = false;
  let targetLine = 0;
  let sourceRemaining = 0;
  let targetRemaining = 0;
  let headerOpen = true;
  const openHunk = (raw: string): void => {
    if (inHunk || sourceRemaining !== 0 || targetRemaining !== 0) {
      throw new ScanError("DIFF_HUNK_TRUNCATED");
    }
    const m = /^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@(?: .*)?$/.exec(raw);
    if (!m || m[1] === undefined || m[3] === undefined) {
      throw new ScanError("HUNK_HEADER_PARSE_FAILED");
    }
    sourceRemaining = m[2] === undefined ? 1 : Number.parseInt(m[2], 10);
    targetLine = Number.parseInt(m[3], 10);
    targetRemaining = m[4] === undefined ? 1 : Number.parseInt(m[4], 10);
    if (![sourceRemaining, targetLine, targetRemaining].every(Number.isSafeInteger)) {
      throw new ScanError("HUNK_HEADER_PARSE_FAILED");
    }
    hadHunk = true;
    headerOpen = false;
    inHunk = sourceRemaining > 0 || targetRemaining > 0;
  };
  for (const raw of lines) {
    if (!inHunk) {
      if ((raw.startsWith("Binary files ") || raw === "GIT binary patch") && headerOpen) {
        return { binary: true, hadHunk, lines: [] };
      }
      if (raw.startsWith("@@")) {
        openHunk(raw);
        continue;
      }
      if (raw === "" || raw === "\\ No newline at end of file") continue;
      if (raw.startsWith("diff --git ")) {
        headerOpen = true;
        continue;
      }
      if (
        headerOpen && (
          raw.startsWith("index ") || raw.startsWith("--- ") || raw.startsWith("+++ ") ||
          /^(?:new file|deleted file|old|new) mode \d{6}$/.test(raw)
        )
      ) continue;
      throw new ScanError("DIFF_STRUCTURE_UNEXPECTED");
    }
    if (raw.startsWith("+")) {
      if (targetRemaining <= 0) throw new ScanError("DIFF_HUNK_COUNT_MISMATCH");
      added.push({ line: targetLine, text: raw.slice(1) });
      targetLine += 1;
      targetRemaining -= 1;
    } else if (raw.startsWith("-")) {
      if (sourceRemaining <= 0) throw new ScanError("DIFF_HUNK_COUNT_MISMATCH");
      sourceRemaining -= 1;
    } else if (raw.startsWith(" ")) {
      if (sourceRemaining <= 0 || targetRemaining <= 0) {
        throw new ScanError("DIFF_HUNK_COUNT_MISMATCH");
      }
      sourceRemaining -= 1;
      targetRemaining -= 1;
      targetLine += 1;
    } else if (raw === "\\ No newline at end of file") {
      // marker metadata — nuk konsumon as source, as target
    } else if (raw === "") {
      // split-i prodhon një element bosh pas newline-it final; counter-at e
      // pambyllur kontrollohen pas loop-it si DIFF_HUNK_TRUNCATED.
      continue;
    } else {
      throw new ScanError("DIFF_STRUCTURE_UNEXPECTED");
    }
    if (sourceRemaining === 0 && targetRemaining === 0) {
      inHunk = false;
    }
  }
  if (inHunk || sourceRemaining !== 0 || targetRemaining !== 0) {
    throw new ScanError("DIFF_HUNK_TRUNCATED");
  }
  return { binary: false, hadHunk, lines: added };
}
