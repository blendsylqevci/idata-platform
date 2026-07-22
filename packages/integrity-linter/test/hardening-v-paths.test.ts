/**
 * P-V C1/C2 — git paths LITERALE + path-policy mbi çdo ancestor segment.
 * Fixtures sintetike; asnjë path/vlerë reale private.
 */
import { describe, expect, it } from "vitest";
import { frag, makeTempRepo } from "./helpers.js";
import { GitCliReader } from "../src/git-repository.js";
import { runHistoryPriv } from "../src/profiles/history-priv.js";
import { evaluatePath } from "../src/path-policy.js";
import { toJsonReport } from "../src/reporter.js";
import type { GitReader } from "../src/types.js";

function repoWithBase() {
  const repo = makeTempRepo();
  repo.write("README.md", "synthetic base\n");
  const base = repo.commitAll("base");
  return { repo, base };
}

describe("C1 — Git path si LITERAL, kurrë pathspec/glob", () => {
  it("'[x].md' dhe 'x.md' janë objekte të ndara: addedContent kthen VETËM rreshtat e path-it literal", () => {
    const { repo, base } = repoWithBase();
    repo.write("[x].md", "BRACKET_CONTENT\n");
    repo.write("x.md", "PLAIN_CONTENT\n");
    const head = repo.commitAll("dy path-e");
    const r = new GitCliReader(repo.dir);
    const bracket = r.addedContent(head, "[x].md").lines.map((l) => l.text).join("\n");
    expect(bracket).toContain("BRACKET_CONTENT");
    expect(bracket).not.toContain("PLAIN_CONTENT");
    const plain = r.addedContent(head, "x.md").lines.map((l) => l.text).join("\n");
    expect(plain).toContain("PLAIN_CONTENT");
    expect(plain).not.toContain("BRACKET_CONTENT");
    void base;
  });

  it("'*.md' dhe '?.md' si emra LITERALE: pa glob në addedContent/hasFile/showFile", () => {
    const { repo } = repoWithBase();
    repo.write("*.md", "STAR_LITERAL\n");
    repo.write("?.md", "QMARK_LITERAL\n");
    repo.write("a.md", "A_CONTENT\n");
    const head = repo.commitAll("emra me wildcard literal");
    const r = new GitCliReader(repo.dir);
    const star = r.addedContent(head, "*.md").lines.map((l) => l.text).join("\n");
    expect(star).toContain("STAR_LITERAL");
    expect(star).not.toContain("A_CONTENT");
    expect(star).not.toContain("QMARK_LITERAL");
    expect(r.hasFile(head, "*.md")).toBe(true);
    expect(r.hasFile(head, "?.md")).toBe(true);
    expect(r.hasFile(head, "!.md")).toBe(false); // s'ka file të tillë — pa glob match
    expect(r.showFile(head, "*.md")).toContain("STAR_LITERAL");
  });

  it("emër që fillon me pathspec-magic syntax trajtohet literal në reader (dhe mbetet i refuzuar në scan)", () => {
    const { repo } = repoWithBase();
    repo.write(":(top)x.md", "MAGIC_LITERAL\n");
    const head = repo.commitAll("magic literal name");
    const r = new GitCliReader(repo.dir);
    expect(r.hasFile(head, ":(top)x.md")).toBe(true);   // literal — jo magic
    expect(r.hasFile(head, ":(top)y.md")).toBe(false);
    // kufiri ekzistues i skanimit: prefiksi ':' refuzohet fail-closed
    expect(() => r.rawEntries(head)).toThrow("PATH_SUSPICIOUS_PREFIX");
  });

  it("RASTI KRITIK: path i lejuar që me GLOB do të përputhej me sibling të BLLOKUAR nuk e lexon atë", () => {
    const { repo, base } = repoWithBase();
    const privDir = frag("docs", "-private");
    const globbyDir = frag("docs", "-privat", "[e]"); // glob [e] → do përputhej me '…private'
    const marker = "LEAK_MARKER_MUST_NOT_APPEAR";
    repo.write(`${privDir}/leak.md`, `${marker}\n`);
    repo.write(`${globbyDir}/x.md`, "PUBLIC_OK\n");
    const head = repo.commitAll("sibling i bllokuar + literal i lejuar");
    const r = new GitCliReader(repo.dir);

    // unit: diff-i i path-it literal NUK përmban blob-in e sibling-ut të bllokuar
    const pub = r.addedContent(head, `${globbyDir}/x.md`).lines.map((l) => l.text).join("\n");
    expect(pub).toContain("PUBLIC_OK");
    expect(pub).not.toContain(marker);

    // engine + spy: asnjë content call për path-in e bllokuar
    const calls: string[] = [];
    const spy: GitReader = new Proxy(r, {
      get(target, prop, receiver) {
        const orig = Reflect.get(target, prop, receiver);
        if (typeof orig !== "function") return orig;
        return (...args: unknown[]) => {
          if (prop === "addedContent" || prop === "showFile" || prop === "blobByOid" || prop === "blobSizeByOid") {
            calls.push(`${String(prop)}:${String(args[1] ?? args[0])}`);
          }
          return (orig as (...a: unknown[]) => unknown).apply(target, args);
        };
      },
    }) as GitReader;
    const res = runHistoryPriv(spy, base, head, undefined, { driftClasses: [], chainRequiredSurfaces: [] });
    expect(res.findings.some((f) => f.ruleId === "HP-PRIVATE-PATH")).toBe(true);
    expect(calls.some((c) => c.includes(`${privDir}/`))).toBe(false);
    const json = toJsonReport({ mode: "advisory", evaluations: res.evaluations, findings: res.findings });
    expect(json).not.toContain(marker);
    expect(json).not.toContain(privDir);
  });

  it("inverse: path normal vazhdon të funksionojë identik", () => {
    const { repo } = repoWithBase();
    repo.write("docs/normal.md", "rresht normal\n");
    const head = repo.commitAll("normal");
    const r = new GitCliReader(repo.dir);
    expect(r.addedContent(head, "docs/normal.md").lines[0]?.text).toBe("rresht normal");
    expect(r.hasFile(head, "docs/normal.md")).toBe(true);
  });
});

describe("C2 — path policy mbi ÇDO ancestor segment", () => {
  const secretMd = frag(".", "secret", ".md");
  const env = frag(".", "env");
  const oferta = frag("Ofer", "ta");
  const kontrata = frag("Kontra", "ta");
  const shtojca = frag("Shtoj", "ca");

  it("katër rastet e konfirmuara → BLOCKED", () => {
    expect(evaluatePath(`${env}/secret.txt`)).toBe("BLOCKED");
    expect(evaluatePath(`nested/${env}/value.txt`)).toBe("BLOCKED");
    expect(evaluatePath(`notes${secretMd}/value.txt`)).toBe("BLOCKED");
    expect(evaluatePath(`docs/${oferta}Client/terms.txt`)).toBe("BLOCKED");
  });

  it("variante nesting/case → BLOCKED; file normal afër → ALLOWED", () => {
    expect(evaluatePath(`a/b/${env.toUpperCase()}/x.txt`)).toBe("BLOCKED");
    expect(evaluatePath(`x/NOTES${secretMd.toUpperCase()}/y.md`)).toBe("BLOCKED");
    expect(evaluatePath(`${kontrata}Archive/file.txt`)).toBe("BLOCKED");
    expect(evaluatePath(`${shtojca}Material/file.txt`)).toBe("BLOCKED");
    expect(evaluatePath(`arkiva/dokumente/${oferta.toLowerCase()}-2024/f.txt`)).toBe("BLOCKED");
    expect(evaluatePath("docs/notes-te-lira/file.txt")).toBe("ALLOWED");
    expect(evaluatePath("docs/" + oferta.toLowerCase() + "-teknike-publike.txt")).toBe("BLOCKED"); // stem substring — parity guard
  });

  it("allowlist env: VETËM si file final; si ancestor → BLOCKED", () => {
    expect(evaluatePath(`${env}.example`)).toBe("ALLOWED");
    expect(evaluatePath(`${env}.sef.example`)).toBe("ALLOWED");
    expect(evaluatePath(`config/${env}.example`)).toBe("ALLOWED");
    expect(evaluatePath(`${env}.example/child.txt`)).toBe("BLOCKED");
    expect(evaluatePath(`${env}.sef.example/x`)).toBe("BLOCKED");
  });

  it("prapashtesë e bllokuar si emër direktorie → BLOCKED; key basename si segment → BLOCKED", () => {
    expect(evaluatePath("backup.bak/readme.txt")).toBe("BLOCKED");
    expect(evaluatePath("certs/server.pem/notes.txt")).toBe("BLOCKED");
    expect(evaluatePath("keys/id_rsa/x.txt")).toBe("BLOCKED");
    expect(evaluatePath("release-2.0/notes.txt")).toBe("ALLOWED"); // near-miss: '0' s'është ext e bllokuar
  });

  it("absolute / '..' / segment bosh / NUL → BLOCKED fail-closed", () => {
    expect(evaluatePath("/etc/x.md")).toBe("BLOCKED");
    expect(evaluatePath("a/../b.md")).toBe("BLOCKED");
    expect(evaluatePath("a//b.md")).toBe("BLOCKED");
    expect(evaluatePath("a/" + String.fromCharCode(0) + "b.md")).toBe("BLOCKED"); // NUL runtime
    expect(evaluatePath("C:\\x\\y.md")).toBe("BLOCKED");
  });

  it("engine: path me ancestor privat → ERROR i redaktuar + ZERO blob/content read", () => {
    const { repo, base } = repoWithBase();
    const marker = "ANCESTOR_LEAK_MUST_NOT_APPEAR";
    repo.write(`nested/${env}/value.txt`, `${marker}\n`);
    const head = repo.commitAll("ancestor privat");
    const real = new GitCliReader(repo.dir);
    const calls: string[] = [];
    const spy: GitReader = new Proxy(real, {
      get(target, prop, receiver) {
        const orig = Reflect.get(target, prop, receiver);
        if (typeof orig !== "function") return orig;
        return (...args: unknown[]) => {
          if (prop === "addedContent" || prop === "showFile" || prop === "blobByOid" || prop === "blobSizeByOid") {
            calls.push(String(prop));
          }
          return (orig as (...a: unknown[]) => unknown).apply(target, args);
        };
      },
    }) as GitReader;
    const res = runHistoryPriv(spy, base, head, undefined, { driftClasses: [], chainRequiredSurfaces: [] });
    const priv = res.findings.filter((f) => f.ruleId === "HP-PRIVATE-PATH");
    expect(priv).toHaveLength(1);
    expect(priv[0]?.locator).toBe("[REDACTED_PATH]");
    expect(calls).toEqual([]); // zero content access për BLOCKED
    const json = toJsonReport({ mode: "advisory", evaluations: res.evaluations, findings: res.findings });
    expect(json).not.toContain(marker);
    expect(json).not.toContain(env);
  });
});
