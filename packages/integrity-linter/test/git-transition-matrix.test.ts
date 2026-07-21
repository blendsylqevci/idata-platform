/**
 * Correction pass (Codex+Fable) — MATRICA E MBYLLUR e tranzicioneve Git
 * status/mode (A/D/M/T + status i panjohur), me positive-control dhe inverse
 * në të njëjtën bateri. Rasti kritik i riprodhuar: `T` (symlink 120000 →
 * regular 100644) kalonte me EVALUATED/exit 0 sepse kontrollohej vetëm
 * newMode — tani GJITHMONË fail-closed PARA çdo blob/content access.
 * Fixtures sintetike; asnjë path/identitet real.
 */
import { describe, expect, it } from "vitest";
import { execFileSync } from "node:child_process";
import { addSymlink, commitWithRawTree, hashBlob, makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runHistoryPriv, validateEntryTransition } from "../src/profiles/history-priv.js";
import { ScanError, type GitReader, type RawEntry } from "../src/types.js";

const OPTS = { driftClasses: [], chainRequiredSurfaces: [] };

function repoWithBase() {
  const repo = makeTempRepo();
  repo.write("README.md", "synthetic base\n");
  const base = repo.commitAll("base");
  return { repo, base };
}

/** Spy që regjistron ÇDO content-access; provë mekanike zero-read për T. */
function contentSpy(real: GitCliReader): { reader: GitReader; calls: string[] } {
  const calls: string[] = [];
  const reader = new Proxy(real, {
    get(target, prop, receiver) {
      const orig = Reflect.get(target, prop, receiver);
      if (typeof orig !== "function") return orig;
      return (...args: unknown[]) => {
        if (prop === "blobSizeByOid" || prop === "blobByOid" || prop === "addedContent") {
          calls.push(String(prop));
        }
        return (orig as (...a: unknown[]) => unknown).apply(target, args);
      };
    },
  }) as GitReader;
  return { reader, calls };
}

function expectTypechangeFailClosed(
  repoDir: string, base: string, head: string, pathHint: string,
  expectedCode = "INCOMPLETE_SCAN:UNSUPPORTED_FILE_MODE",
): void {
  const { reader, calls } = contentSpy(new GitCliReader(repoDir));
  try {
    runHistoryPriv(reader, base, head, undefined, OPTS);
    expect.unreachable("duhet exit-2 për type-change");
  } catch (err) {
    expect(err).toBeInstanceOf(ScanError);
    expect((err as ScanError).code).toBe(expectedCode);
    expect((err as Error).message).not.toContain(pathHint); // vetëm kod fiks, pa path
  }
  expect(calls).toEqual([]); // ZERO blobSizeByOid/blobByOid/addedContent
}

describe("Matrica A/D/M/T — tranzicionet e mbështetura skanohen (positive control + inverse)", () => {
  it("A: regular file i lejuar → skanohet (me finding real si provë leximi)", () => {
    const { repo, base } = repoWithBase();
    repo.write("docs/n.md", "xhiroja ditore reale 12\n"); // trigger sintetik
    const head = repo.commitAll("add regular");
    const r = runHistoryPriv(new GitCliReader(repo.dir), base, head, undefined, OPTS);
    expect(r.findings.some((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toBe(true);
  });

  it("D: regular file → delete pa content read", () => {
    const { repo, base } = repoWithBase();
    repo.write("docs/d.md", "tekst\n");
    const c1 = repo.commitAll("add");
    execFileSync("git", ["rm", "-q", "docs/d.md"], { cwd: repo.dir });
    const c2 = repo.commitAll("delete");
    const { reader, calls } = contentSpy(new GitCliReader(repo.dir));
    const r = runHistoryPriv(reader, c1, c2, undefined, OPTS);
    expect(r.findings).toEqual([]);
    expect(calls).toEqual([]); // delete = zero content access
    void base;
  });

  it("M: regular 100644 → 100644 → skanohet", () => {
    const { repo, base } = repoWithBase();
    repo.write("docs/m.md", "para\n");
    const c1 = repo.commitAll("v1");
    repo.write("docs/m.md", "para\nxhiroja ditore reale 34\n");
    const c2 = repo.commitAll("v2");
    const r = runHistoryPriv(new GitCliReader(repo.dir), c1, c2, undefined, OPTS);
    expect(r.findings.some((f) => f.ruleId === "HP-OPERATIONAL-CLAIM")).toBe(true);
    void base;
  });

  it("M: 100644 ↔ 100755 (chmod) → lejohet dhe skanohet", () => {
    const { repo, base } = repoWithBase();
    repo.write("tools/run.sh", "#!/bin/sh\necho ok\n");
    const c1 = repo.commitAll("add script");
    execFileSync("chmod", ["755", `${repo.dir}/tools/run.sh`], {});
    const c2 = repo.commitAll("make executable");
    expect(() => runHistoryPriv(new GitCliReader(repo.dir), c1, c2, undefined, OPTS)).not.toThrow();
    // dhe kthimi mbrapsht 755 → 644 (chmod në DISK — commitAll ristage-on nga disku)
    execFileSync("chmod", ["644", `${repo.dir}/tools/run.sh`], {});
    const c3 = repo.commitAll("remove executable bit");
    expect(() => runHistoryPriv(new GitCliReader(repo.dir), c2, c3, undefined, OPTS)).not.toThrow();
    void base;
  });

  it("A: symlink i ri → exit 2 para content read (sjellje ekzistuese e ruajtur)", () => {
    const { repo, base } = repoWithBase();
    addSymlink(repo, "lidhje.md", "README.md");
    const head = repo.commitAll("add symlink");
    expectTypechangeFailClosed(repo.dir, base, head, "lidhje");
  });
});

describe("Matrica T — type-change GJITHMONË fail-closed, zero content access", () => {
  it("T: symlink 120000 → regular 100644 (repo REAL Git) → exit 2 — rasti i riprodhuar", () => {
    const { repo, base } = repoWithBase();
    addSymlink(repo, "target.md", "README.md");
    const c1 = repo.commitAll("symlink in");
    execFileSync("rm", [`${repo.dir}/target.md`], {});
    repo.write("target.md", "CONTENT_THAT_MUST_NOT_BE_SCANNED xhiroja ditore reale 99\n");
    const c2 = repo.commitAll("typechange to regular");
    // verifikim që Git e raporton vërtet si T
    const raw = execFileSync("git", ["diff-tree", "-r", "--raw", "--no-renames", c1, c2], { cwd: repo.dir }).toString();
    expect(raw).toContain(["120", "000 100", "644"].join("")); // modet git — fragmente për gitleaks-gate
    expect(raw).toMatch(/\sT\s/);
    expectTypechangeFailClosed(repo.dir, c1, c2, "target");
    void base;
  });

  it("T: regular 100644 → symlink 120000 → exit 2", () => {
    const { repo, base } = repoWithBase();
    repo.write("kthim.md", "tekst i thjeshtë\n");
    const c1 = repo.commitAll("regular in");
    execFileSync("rm", [`${repo.dir}/kthim.md`], {});
    addSymlink(repo, "kthim.md", "README.md");
    const c2 = repo.commitAll("typechange to symlink");
    expectTypechangeFailClosed(repo.dir, c1, c2, "kthim");
    void base;
  });

  it("T: gitlink 160000 → regular 100644 → exit 2 (shtresa e bazës: treeEntries refuzon gitlink në pemë — LS_TREE_PARSE_FAILED; validatori mbulon diff-entry-n në unit-test)", () => {
    const { repo, base } = repoWithBase();
    const fakeCommit = "a".repeat(40);
    const c1 = commitWithRawTree(repo, [
      { mode: "160000", type: "commit", oid: fakeCommit, pathBytes: Buffer.from("moduli", "utf8") },
    ], "gitlink in", base);
    const blobOid = hashBlob(repo, "u kthye në file\n");
    const c2 = commitWithRawTree(repo, [
      { mode: "100644", type: "blob", oid: blobOid, pathBytes: Buffer.from("moduli", "utf8") },
    ], "typechange to blob", c1);
    expectTypechangeFailClosed(repo.dir, c1, c2, "moduli", "LS_TREE_PARSE_FAILED");
  });

  it("T: regular 100644 → gitlink 160000 → exit 2 — baza e pastër, VALIDATORI godet diff-entry-n T me kodin ekzakt", () => {
    const { repo, base } = repoWithBase();
    const blobOid = hashBlob(repo, "file fillestar\n");
    const c1 = commitWithRawTree(repo, [
      { mode: "100644", type: "blob", oid: blobOid, pathBytes: Buffer.from("nenmoduli", "utf8") },
    ], "blob in", base);
    const fakeCommit = "b".repeat(40);
    const c2 = commitWithRawTree(repo, [
      { mode: "160000", type: "commit", oid: fakeCommit, pathBytes: Buffer.from("nenmoduli", "utf8") },
    ], "typechange to gitlink", c1);
    expectTypechangeFailClosed(repo.dir, c1, c2, "nenmoduli"); // UNSUPPORTED_FILE_MODE nga validatori
  });
});

describe("Matrica — status i panjohur dhe tuple të pamundur (unit + engine)", () => {
  const e = (status: string, oldMode: string, newMode: string): RawEntry => ({
    status, oldMode, newMode,
    oldOid: "1".repeat(40), newOid: "2".repeat(40), path: "sintetik.md",
  });

  it("unit: status i panjohur → GIT_STATUS_UNSUPPORTED; tuple A/D/M i pavlefshëm → INVALID_MODE_TRANSITION", () => {
    expect(() => validateEntryTransition(e("X", "100644", "100644"))).toThrow("INCOMPLETE_SCAN:GIT_STATUS_UNSUPPORTED");
    expect(() => validateEntryTransition(e("A", "100644", "100644"))).toThrow("INCOMPLETE_SCAN:INVALID_MODE_TRANSITION");
    expect(() => validateEntryTransition(e("D", "100644", "100644"))).toThrow("INCOMPLETE_SCAN:INVALID_MODE_TRANSITION");
    expect(() => validateEntryTransition(e("M", "000000", "100644"))).toThrow("INCOMPLETE_SCAN:INVALID_MODE_TRANSITION");
    expect(() => validateEntryTransition(e("M", "100644", "000000"))).toThrow("INCOMPLETE_SCAN:INVALID_MODE_TRANSITION");
    // positive controls në të njëjtën bateri
    expect(() => validateEntryTransition(e("A", "000000", "100644"))).not.toThrow();
    expect(() => validateEntryTransition(e("D", "100755", "000000"))).not.toThrow();
    expect(() => validateEntryTransition(e("M", "100644", "100755"))).not.toThrow();
    expect(() => validateEntryTransition(e("T", "120000", "100644"))).toThrow("INCOMPLETE_SCAN:UNSUPPORTED_FILE_MODE");
    expect(() => validateEntryTransition(e("T", "100644", "160000"))).toThrow("INCOMPLETE_SCAN:UNSUPPORTED_FILE_MODE");
  });

  it("engine: rawEntries me status të panjohur → exit 2, zero content access", () => {
    const { repo, base } = repoWithBase();
    repo.write("x.md", "diçka\n");
    const head = repo.commitAll("c");
    const real = new GitCliReader(repo.dir);
    const calls: string[] = [];
    const fake = new Proxy(real, {
      get(target, prop, receiver) {
        if (prop === "rawEntries") {
          return () => [e("Q", "100644", "100644")];
        }
        const orig = Reflect.get(target, prop, receiver);
        if (typeof orig !== "function") return orig;
        return (...args: unknown[]) => {
          if (prop === "blobSizeByOid" || prop === "blobByOid" || prop === "addedContent") calls.push(String(prop));
          return (orig as (...a: unknown[]) => unknown).apply(target, args);
        };
      },
    }) as GitReader;
    expect(() => runHistoryPriv(fake, base, head, undefined, OPTS)).toThrow("INCOMPLETE_SCAN:GIT_STATUS_UNSUPPORTED");
    expect(calls).toEqual([]);
  });

  it("privacy e ruajtur: path privat me T → finding [REDACTED_PATH] pa blob read (jo exit 2)", () => {
    const { repo, base } = repoWithBase();
    const privDir = ["private", "-sensitive"].join("");
    execFileSync("mkdir", ["-p", `${repo.dir}/${privDir}`], {});
    addSymlink(repo, `${privDir}/lidhje.md`, "README.md");
    const c1 = repo.commitAll("private symlink");
    execFileSync("rm", [`${repo.dir}/${privDir}/lidhje.md`], {});
    repo.write(`${privDir}/lidhje.md`, "SEKRET_QE_S_LEXOHET\n");
    const c2 = repo.commitAll("private typechange");
    const { reader, calls } = contentSpy(new GitCliReader(repo.dir));
    // BLOCKED-path T: policy vjen PARA validatorit → finding i redaktuar, jo throw
    const r = runHistoryPriv(reader, c1, c2, undefined, OPTS);
    const priv = r.findings.filter((f) => f.ruleId === "HP-PRIVATE-PATH");
    expect(priv.length).toBeGreaterThan(0);
    expect(priv.every((f) => f.locator === "[REDACTED_PATH]")).toBe(true);
    expect(calls).toEqual([]);
    expect(JSON.stringify(r.findings)).not.toContain("SEKRET");
    void base;
  });
});
