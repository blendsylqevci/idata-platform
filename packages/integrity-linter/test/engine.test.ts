import { describe, expect, it } from "vitest";
import { makeTempRepo, writeFullCanonicalRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runScan } from "../src/engine.js";
import { main } from "../src/cli.js";
import type { ScanConfig } from "../src/types.js";

describe("engine + cli exit codes (fail-closed)", () => {
  it("advisory findings reale → exit 0 (repo kanonik sintetik i plotë me stale value)", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo, { staleIndexLedger: true });
    repo.write("x.md", "extra\n");
    const head = repo.commitAll("delta");
    const code = main(
      ["--profiles", "DOC-CORE,HISTORY-PRIV", "--base", `${head}~1`, "--head", head, "--cwd", repo.dir, "--format", "json"],
      {},
    );
    expect(code).toBe(0);
  });

  it("§2: --docs-ref invalid → exit 2 (jo WARNING + exit 0)", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo);
    const code = main(["--profiles", "DOC-CORE", "--docs-ref", "definitely-missing-ref", "--cwd", repo.dir], {});
    expect(code).toBe(2);
  });

  it("§2: default surfaces që mungojnë (repo jo-kanonik) → exit 2", () => {
    const repo = makeTempRepo();
    repo.write("README.md", "vetëm ky\n");
    repo.commitAll("jo kanonik");
    const code = main(["--profiles", "DOC-CORE", "--cwd", repo.dir], {});
    expect(code).toBe(2);
  });

  it("config error (profil bogus / range i munguar) → exit 2", () => {
    const repo = makeTempRepo();
    repo.write("a.md", "x\n");
    repo.commitAll("c");
    expect(main(["--profiles", "BOGUS-PROFILE", "--cwd", repo.dir], {})).toBe(2);
    expect(main(["--profiles", "HISTORY-PRIV", "--cwd", repo.dir], {})).toBe(2);
  });

  it("--mode required refuzohet (HG-LINTER-REQUIRED i paaprovuar) → exit 2", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo);
    expect(main(["--mode", "required", "--base", "HEAD", "--head", "HEAD", "--cwd", repo.dir], {})).toBe(2);
  });

  it("profil i paimplementuar → NOT_IMPLEMENTED, kurrë PASS, zero findings të fabrikuara (fixture sintetike PoC-promotion NUK numërohet si detektor)", () => {
    const repo = makeTempRepo();
    repo.write("docs/synthetic-status.md", ["| PoC-9 simulator | ", ["IMPLE","MENTED"].join(""), "/", ["TES","TED"].join(""), " |\n"].join(""));
    repo.commitAll("synthetic promotion fixture");
    const reader = new GitCliReader(repo.dir);
    const config: ScanConfig = { mode: "advisory", profiles: ["SRC-CLOSURE"], docsRef: "HEAD" };
    const result = runScan(reader, config);
    expect(result.evaluations.find((e) => e.profile === "SRC-CLOSURE")?.status).toBe("NOT_IMPLEMENTED");
    expect(JSON.stringify(result)).not.toContain('"PASS"');
    expect(result.findings).toHaveLength(0);
  });

  it("renditje/output deterministik në ekzekutime të përsëritura", () => {
    const repo = makeTempRepo();
    writeFullCanonicalRepo(repo, { staleIndexLedger: true });
    const head = repo.head();
    const reader = new GitCliReader(repo.dir);
    const config: ScanConfig = {
      mode: "advisory", profiles: ["DOC-CORE", "HISTORY-PRIV"], docsRef: "HEAD",
      baseSha: `${head}~0`, headSha: head,
    };
    const r1 = JSON.stringify(runScan(reader, config));
    const r2 = JSON.stringify(runScan(reader, config));
    expect(r1).toBe(r2);
  });
});
