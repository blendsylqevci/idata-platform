import { describe, expect, it } from "vitest";
import { writeFileSync } from "node:fs";
import { GitCliReader } from "../src/git-repository.js";
import { makeTempRepo } from "./helpers.js";
import { ScanError } from "../src/types.js";

describe("GitCliReader — Git objects only, fail-closed", () => {
  it("lexon added-lines dhe changed entries per-commit (me rename)", () => {
    const repo = makeTempRepo();
    repo.write("a.md", "one\n");
    const c1 = repo.commitAll("c1");
    repo.write("a.md", "one\ntwo\n");
    const c2 = repo.commitAll("c2");
    repo.git(["mv", "a.md", "b.md"]);
    const c3 = repo.commitAll("c3 rename");

    const reader = new GitCliReader(repo.dir);
    expect(reader.revParse("HEAD")).toBe(c3);
    expect(reader.mergeBase(c1, c3)).toBe(c1);
    expect(reader.revList(c1, c3)).toEqual([c2, c3]);
    expect(reader.addedLines(c2, "a.md")).toEqual(["two"]);
    const entries = reader.changedEntries(c3);
    expect(entries).toHaveLength(1);
    expect(entries[0]?.status).toBe("R");
    expect(entries[0]?.oldPath).toBe("a.md");
    expect(entries[0]?.path).toBe("b.md");
  });

  it("test 7: ref/range invalid → ScanError (fail-closed)", () => {
    const repo = makeTempRepo();
    repo.write("a.md", "x\n");
    repo.commitAll("c1");
    const reader = new GitCliReader(repo.dir);
    expect(() => reader.revParse("nonexistent-ref")).toThrow(ScanError);
    expect(() => reader.mergeBase("deadbeef", "HEAD")).toThrow(ScanError);
  });

  it("blobSize + binaryPaths pa lexuar përmbajtjen", () => {
    const repo = makeTempRepo();
    repo.write("t.md", "text\n");
    repo.commitAll("c1");
    // binar sintetik (null bytes)
    writeFileSync(`${repo.dir}/blob.bin`, Buffer.from([0, 1, 2, 3, 0, 255]));
    const c2 = repo.commitAll("c2 bin");
    const reader = new GitCliReader(repo.dir);
    expect(reader.blobSize(c2, "blob.bin")).toBe(6);
    expect(reader.binaryPaths(c2).has("blob.bin")).toBe(true);
  });
});
