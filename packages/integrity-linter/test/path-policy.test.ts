import { describe, expect, it } from "vitest";
import { evaluatePath, ALLOWED_FILE_MODES } from "../src/path-policy.js";
import { frag } from "./helpers.js";

/**
 * §5 POLICY PARITY — moduli i vetëm i policy-t kundër kufijve tracked të repo-s.
 * Emrat "sensitive" ndërtohen nga fragmente (zero literale që trip-ojnë skanerat).
 */
describe("path-policy — parity me .gitignore + guard + PROJECT_RULES", () => {
  it("LEJON shembujt publikë tracked", () => {
    for (const p of [
      frag(".", "env", ".example"),
      frag(".", "env", ".sef", ".example"),
      "docs/INDEX.md",
      "packages/integrity-linter/src/cli.ts",
      "docs/archive/README.md",
      "poc/poc5-sef/ANALIZA.md",
      ".github/workflows/ci.yml",
    ]) {
      expect(evaluatePath(p), p).toBe("ALLOWED");
    }
  });

  it("BLLOKON klasat private/owner-only (pa publikuar path real)", () => {
    const cases: string[] = [
      frag(".", "env"),
      frag(".", "env", ".local"),
      frag(".", "env", ".sef", ".local"),
      frag("00", "_raw") + "/x/y.md",
      frag("docs", "-private") + "/a.md",
      frag("private", "-sensitive") + "/b.md",
      frag("evidence", "-private", "-local") + "/c.md",
      frag(".", "sef", "-local") + "/id.txt",
      frag("03", "_contracts") + "/d.pdf",
      frag("docs/", "evidence") + "/e.md",
      frag("docs/archive/", "prodata") + "/f.md",
      frag("docs/archive/", "client-docs") + "/g.md",
      frag("docs/archive/", "client-docs-text") + "/h.md",
      frag("docs/archive/", "contracts") + "/i.md",
      frag("docs/archive/", "old-master") + "/j.md",
      frag("docs/archive/", "prompts") + "/k.md",
      "outputs/report.md",
      frag("CANONICAL_SPLIT_COPY_", "MANIFEST") + "_2026.csv",
      frag("PLANET_VAT_", "Reconciliation_") + "2026.md",
      "docs/" + frag("Ofer", "ta") + "_synthetic.pdf",
      frag("Kontra", "t") + "a_synthetic.docx",
      frag("Shtoj", "ca") + "_1.pdf",
      frag("Dokumenti", "_", "Master") + "_v9.pdf",
      frag("Dosja", "_", "Certifikimit") + "_x.zip",
      "x/" + frag("Certifikimit", "_", "SEF") + "_pack.md",
      "docs/Spec" + frag("_v2_", "Draft") + ".md",
      "backup/db.mdf",
      "backup/db.ldf",
      "old/data.bak",
      "keys/server.pem",
      "keys/host.key",
      "keys/bundle.p12",
      "keys/cred.pfx",
      "keys/store.keystore",
      "keys/store.jks",
      ".ssh/id_rsa",
    ];
    for (const p of cases) {
      expect(evaluatePath(p), p).toBe("BLOCKED");
    }
  });

  it("mode policy: vetëm 100644/100755", () => {
    expect(ALLOWED_FILE_MODES.has("100644")).toBe(true);
    expect(ALLOWED_FILE_MODES.has("100755")).toBe(true);
    expect(ALLOWED_FILE_MODES.has("120000")).toBe(false);
    expect(ALLOWED_FILE_MODES.has("160000")).toBe(false);
  });
});
