# @idata/integrity-linter — GOV-001 (advisory baseline)

Documentation & Compliance Integrity Linter — **advisory**, JO-required.
Autoriteti canonical i mandatit: `docs/canonical/01-phase0/07_PRODATA_Evidence_Closure_Parity_Matrix.md` §Linter.

## Statusi (i saktë, pa pretendime të pakufizuara)

**HISTORY-PRIV baseline canonical rule catalogue = IMPLEMENTED_ADVISORY:** çdo klasë e
listuar shprehimisht në 07_PRODATA §Per-commit history scan dhe §Acceptance ka
implementim aktiv per-commit + fixtures pozitive, negative, boundary dhe inverse.
**Ky status NUK nënkupton** privacy/compliance complete, findings-free, required-CI
ose implementim të profileve të tjera.

**DOC-CORE baseline = IMPLEMENTED_ADVISORY:** kontratë bidirectional
`authority → consumers` (REQUIRED_CURRENT / SCAN_IF_PRESENT; missing/stale/ambiguous
→ fail-closed), zinxhiri SEF me **prani + rend** të 6 hallkave, klasa reale `phase`
(kolona "Faza" e 07 → çelësat FISCAL-/BLOCKS- të Ledger-it), milestones,
role-vocabulary me fraza të plota (REVIEW), claims kontekstuale, PR-body drift
(NOT_EVALUATED vetëm kur mungon event-i).

**14 profilet e tjera = NOT_IMPLEMENTED** (kurrë PASS). `--mode required` refuzohet —
`HG-LINTER-REQUIRED` i paaprovuar.

## Kufij të fortë

- Vetëm **Git objects të tracked** (`show/diff-tree/rev-list/merge-base/cat-file/ls-tree`);
  zero filesystem walk; zero network; `execFileSync` me argument arrays;
  `--no-ext-diff --no-textconv` (provohet me sentinel fixture).
- **Policy para blob-it:** path/mode/scope zbatohet mbi TË GJITHA entries e commit-it
  para çdo content access (call-order spy test). Path privat → `[REDACTED_PATH]`,
  zero content/hash; delete-only cleanup lejohet; relocation privat→publik → REVIEW.
- **Fail-closed (exit 2):** merge commit në range; mode ≠ 100644/100755; path me
  tab/newline/UTF-8 invalid/bidi/format chars/prefiks të dyshimtë; parser failure;
  authority missing/unreadable/ambiguous (validator i përbashkët, per-commit,
  aktivizim ONCE_PRESENT); **binary/oversize blob** (`INCOMPLETE_SCAN:BINARY_BLOB`/
  `OVERSIZE_BLOB` — përshkallëzuar nga REVIEW+NOT_EVALUATED e Pass II, shih 07 v2.5);
  surface e detyrueshme që mungon; shallow repo; event i malformuar/oversize/
  SHA invalid/range mismatch. Kode gabimi FIKSE — kurrë raw git message/argv/ref/path.
- **Relocation private→publike:** OID-t e blob-eve private të fshira ndiqen nëpër
  GJITHË range-un — rikthimi në commit tjetër kapet (REVIEW me `[REDACTED_PATH]`).
- **Event trust boundary:** `GITHUB_EVENT_PATH` lexohet automatikisht VETËM kur
  `GITHUB_ACTIONS=true` dhe `GITHUB_EVENT_NAME=pull_request`; lokalisht vetëm
  `--event-file` eksplicit; O_NOFOLLOW + fstat; caps fikse; SHA 40-hex.

## Përdorimi

```bash
node dist/cli.js --profiles DOC-CORE,HISTORY-PRIV --base <sha> --head <sha> --format text
node dist/cli.js --profiles DOC-CORE --format json     # stdout = VETËM JSON valid
node dist/cli.js ... --event-file event.json           # event eksplicit lokalisht
```

Exit 0 = scan i plotë (advisory findings lejohen; WARNING/REVIEW **janë findings, jo PASS**).
Exit 2 = config/git/parser/incomplete-scan error.

## Workflow

`.github/workflows/governance-advisory.yml` — advisory, **JO-required**; `bash` +
`set -euo pipefail`, pa pipe exit-sensitive; findings advisory → jeshil; engine error →
i kuq. Required checks mbeten vetëm `build-test` + `gitleaks`.
