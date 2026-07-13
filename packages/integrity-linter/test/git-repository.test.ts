import { describe, expect, it } from "vitest";
import { writeFileSync } from "node:fs";
import { GitCliReader, parseUnifiedAddedLines } from "../src/git-repository.js";
import { commitWithRawTree, hashBlob, makeTempRepo } from "./helpers.js";
import { ScanError } from "../src/types.js";

describe("GitCliReader — Git objects only, fail-closed, error codes fikse", () => {
  it("rawEntries pa rename detection: rename = Delete + Add (asnjë -M)", () => {
    const repo = makeTempRepo();
    repo.write("a.md", "one\n");
    const c1 = repo.commitAll("c1");
    repo.git(["mv", "a.md", "b.md"]);
    const c3 = repo.commitAll("rename");
    const reader = new GitCliReader(repo.dir);
    const entries = reader.rawEntries(c3);
    const statuses = entries.map((e) => `${e.status}:${e.path}`).sort();
    expect(statuses).toEqual(["A:b.md", "D:a.md"]);
    expect(reader.revList(c1, c3)).toEqual([c3]);
  });

  it("test 7 + §9: ref invalid → ScanError me kod fiks, pa raw git message/path", () => {
    const sentinel = "SENTINEL-DIR-NAME-XYZ";
    const repo = makeTempRepo(`linter-${sentinel}-`);
    repo.write("a.md", "x\n");
    repo.commitAll("c1");
    const reader = new GitCliReader(repo.dir);
    try {
      reader.revParse("definitely-missing-ref");
      expect.unreachable("duhet të hidhte");
    } catch (err) {
      expect(err).toBeInstanceOf(ScanError);
      const msg = (err as Error).message;
      expect(msg).toBe("GIT_OP_FAILED:rev-parse");
      expect(msg).not.toContain(sentinel);
      expect(msg).not.toContain("definitely-missing-ref");
    }
    expect(() => reader.mergeBase("deadbeef", "HEAD")).toThrow(ScanError);
  });

  it("blobSizeByOid me OBJECT-ID (jo path) + addedContent detekton binarin pa content", () => {
    const repo = makeTempRepo();
    repo.write("t.md", "text\n");
    repo.commitAll("c1");
    writeFileSync(`${repo.dir}/blob.bin`, Buffer.from([0, 1, 2, 3, 0, 255]));
    const c2 = repo.commitAll("c2 bin");
    const reader = new GitCliReader(repo.dir);
    const entry = reader.rawEntries(c2).find((e) => e.path === "blob.bin");
    expect(entry).toBeDefined();
    expect(reader.blobSizeByOid(entry!.newOid)).toBe(6);
    const content = reader.addedContent(c2, "blob.bin");
    expect(content.binary).toBe(true);
    expect(content.lines).toHaveLength(0);
  });

  it("§4D: path me TAB dhe path me NEWLINE → INCOMPLETE_SCAN pa publikim path-i", () => {
    const repo = makeTempRepo();
    repo.write("seed.md", "x\n");
    const base = repo.commitAll("seed");
    const oid = hashBlob(repo, "synthetic\n");
    const cTab = commitWithRawTree(repo, [
      { mode: "100644", type: "blob", oid, pathBytes: Buffer.from("a\tb.md", "utf8") },
    ], "tab path", base);
    const reader = new GitCliReader(repo.dir);
    try {
      reader.rawEntries(cTab);
      expect.unreachable("duhet të hidhte");
    } catch (err) {
      expect((err as Error).message).toBe("PATH_CONTROL_CHARS");
      expect((err as Error).message).not.toContain("a\tb");
    }
    const cNl = commitWithRawTree(repo, [
      { mode: "100644", type: "blob", oid, pathBytes: Buffer.from("a\nb.md", "utf8") },
    ], "newline path", base);
    expect(() => reader.rawEntries(cNl)).toThrow("PATH_CONTROL_CHARS");
  });

  it("§4D: path me UTF-8 INVALID → INCOMPLETE_SCAN pa publikim", () => {
    const repo = makeTempRepo();
    repo.write("seed.md", "x\n");
    const base = repo.commitAll("seed");
    const oid = hashBlob(repo, "synthetic\n");
    const badPath = Buffer.from([0x61, 0xff, 0xfe, 0x62, 0x2e, 0x6d, 0x64]); // a<invalid>b.md
    const c = commitWithRawTree(repo, [{ mode: "100644", type: "blob", oid, pathBytes: badPath }], "bad utf8 path", base);
    const reader = new GitCliReader(repo.dir);
    expect(() => reader.rawEntries(c)).toThrow("PATH_ENCODING_INVALID");
  });

  it("§4C: parser i hunk-eve — added content që fillon me '+', '++', '+++' + numra REALË rreshtash", () => {
    const repo = makeTempRepo();
    repo.write("f.md", "l1\nl2\nl3\nl4\nl5\n");
    repo.commitAll("base");
    repo.write("f.md", "l1\nl2\n+plus\n++dyplus\n+++treplus\nl3\nl4\nl5\n");
    const c2 = repo.commitAll("adds plus-lines");
    const reader = new GitCliReader(repo.dir);
    const content = reader.addedContent(c2, "f.md");
    expect(content.binary).toBe(false);
    expect(content.lines).toEqual([
      { line: 3, text: "+plus" },
      { line: 4, text: "++dyplus" },
      { line: 5, text: "+++treplus" },
    ]);
    // '+++ b/f.md' i header-it NUK u ngatërrua me content (content-i i parë = rreshti 3)
  });

  it("§4C: locator = numër real i target file-it, jo ordinal i added-lines", () => {
    const repo = makeTempRepo();
    repo.write("g.md", Array.from({ length: 20 }, (_, i) => `line${i + 1}`).join("\n") + "\n");
    repo.commitAll("base");
    // shto NJË rresht pas rreshtit 17 → added line = rreshti 18 real
    const lines = Array.from({ length: 20 }, (_, i) => `line${i + 1}`);
    lines.splice(17, 0, "INSERTED");
    repo.write("g.md", lines.join("\n") + "\n");
    const c2 = repo.commitAll("insert deep");
    const reader = new GitCliReader(repo.dir);
    const content = reader.addedContent(c2, "g.md");
    expect(content.lines).toEqual([{ line: 18, text: "INSERTED" }]);
  });

  it("parseUnifiedAddedLines: strukturë e papritur → ScanError (parser failure)", () => {
    expect(() => parseUnifiedAddedLines("@@ garbage @@\n+x\n")).toThrow(ScanError);
    expect(parseUnifiedAddedLines("").lines).toHaveLength(0);
  });
});
