import { describe, expect, it } from "vitest";
import { makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runScan } from "../src/engine.js";
import { main } from "../src/cli.js";
import type { ScanConfig } from "../src/types.js";

function baseRepo() {
  const repo = makeTempRepo();
  repo.write("README.md", "synthetic\n");
  const base = repo.commitAll("init");
  repo.write("a.md", "line one\n");
  const head = repo.commitAll("add a");
  return { repo, base, head };
}

describe("engine + cli exit codes", () => {
  it("test 1: advisory findings → exit 0", () => {
    const { repo, base, head } = baseRepo();
    // defaultSurfaces mbi repo sintetike → DC-AUTHORITY-MISSING (advisory WARNING), por exit 0
    const code = main(
      ["--profiles", "DOC-CORE,HISTORY-PRIV", "--base", base, "--head", head, "--cwd", repo.dir, "--format", "json"],
      {},
    );
    expect(code).toBe(0);
  });

  it("test 2: config error → exit 2", () => {
    const { repo } = baseRepo();
    expect(main(["--profiles", "BOGUS-PROFILE", "--cwd", repo.dir], {})).toBe(2);
    // range i munguar për HISTORY-PRIV → gjithashtu 2
    expect(main(["--profiles", "HISTORY-PRIV", "--cwd", repo.dir], {})).toBe(2);
  });

  it("test 3: --mode required refuzohet (HG-LINTER-REQUIRED i paaprovuar)", () => {
    const { repo, base, head } = baseRepo();
    const code = main(["--mode", "required", "--base", base, "--head", head, "--cwd", repo.dir], {});
    expect(code).toBe(2);
  });

  it("test 4 + 23: profil i paimplementuar → NOT_IMPLEMENTED, kurrë PASS (fixture sintetike PoC-promotion)", () => {
    const { repo } = baseRepo();
    // Fixture sintetike e klasës "PoC i promovuar gabimisht në production status":
    repo.write("docs/synthetic-status.md", "| PoC-9 simulator | IMPLEMENTED/TESTED |\n");
    repo.commitAll("synthetic promotion fixture");
    const reader = new GitCliReader(repo.dir);
    const config: ScanConfig = { mode: "advisory", profiles: ["SRC-CLOSURE"], docsRef: "HEAD" };
    const result = runScan(reader, config);
    const ev = result.evaluations.find((e) => e.profile === "SRC-CLOSURE");
    expect(ev?.status).toBe("NOT_IMPLEMENTED");
    // asnjë status "PASS" nuk ekziston askund në output
    expect(JSON.stringify(result)).not.toContain('"PASS"');
    expect(result.findings).toHaveLength(0); // s'pretendohet implementim → zero findings të fabrikuara
  });

  it("test 13: renditje/output deterministik në ekzekutime të përsëritura", () => {
    const { repo, base, head } = baseRepo();
    const reader = new GitCliReader(repo.dir);
    const config: ScanConfig = {
      mode: "advisory", profiles: ["DOC-CORE", "HISTORY-PRIV"], docsRef: "HEAD",
      baseSha: base, headSha: head,
    };
    const r1 = JSON.stringify(runScan(reader, config));
    const r2 = JSON.stringify(runScan(reader, config));
    expect(r1).toBe(r2);
  });
});
