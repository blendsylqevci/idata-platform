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
→ fail-closed), zinxhiri SEF me **prani + rend** të 6 hallkave (mohimi i zinxhirit
claim-scoped; etiketa historike segment-scoped; rendi vlerësohet **PER-CLAIM** —
bullets të pavarura s'krahasohen mes tyre), klasa reale `phase` (authority = rreshti
`ATK-002` i Roadmap; consumers = rreshti `Fiskalizim / SEF` i 07 + FISCAL-/BLOCKS-F*
të Ledger-it; Roadmap s'është njëkohësisht consumer), milestones, role/privacy
coverage mbi **listë machine-readable** sipërfaqesh canonical aktive (sipërfaqe e
detyrueshme që mungon → exit 2; "Human Gate" etiketë e njohur; kandidat identiteti
personal në parentetika → privacy REVIEW pa content), PR-body drift kundrejt **të
gjithë** consumers.

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
  authority missing/unreadable/ambiguous (validator i përbashkët, per-commit);
  **binary/oversize blob** (`BINARY_BLOB`/`OVERSIZE_BLOB`/`INVALID_ENCODING` —
  klasifikimi mbi **byte-t RAW të blob-it sipas OID**, i PAVARUR nga
  `.gitattributes`/extension/diff; tekst valid UTF-8 SKANOHET edhe kur attributes
  e quajnë binar); surface e detyrueshme që mungon; shallow repo; event i
  malformuar/oversize/SHA invalid/range mismatch. Kode gabimi FIKSE — kurrë raw
  git message/argv/ref/path.
- **Aktivizimi ONCE_PRESENT (P-IV §2):** klasa aktivizohet nga **PRANIA** e
  authority file — jo nga leximi i parë valid; pas aktivizimit bosh/zero-match/
  ambige/i hequr → exit 2; dështimi historik s'zhduket nga riparimi pasues.
- **`activationAnchor` (P-IV §4A, compatibility mechanism i KUFIZUAR):** vetëm për
  authority surface që s'ekzistonte semantikisht para futjes së seksionit (asertimet
  e §Linter lindën me 07 v2.3 brenda vetë PR #21, mbi histori të pandryshueshme).
  Lejohet VETËM për `profile-registry-set` + `hg-linter-required-status`; anchor =
  structural label unik i seksionit — kurrë vetë vlera; para anchor-it klasa është
  NOT_APPLICABLE; lindje e prishur → exit 2 po në atë commit; pas aktivizimit sticky:
  anchor i hequr/duplikuar ose authority e prishur → exit 2. Zgjerim te klasa të
  tjera ose heqje e ardhshme: VETËM me PR + review/Human Gate më vete.
- **Relocation private→publike:** OID-t e blob-eve private të fshira ndiqen nëpër
  GJITHË range-un — rikthimi në commit tjetër kapet (REVIEW me `[REDACTED_PATH]`).
- **Mohimi lokal PER-MATCH (P-IV §5):** çdo match gjykohet veçmas — mohimi i një
  claim-i s'e përjashton claim-in pasues në të njëjtin rresht; "jo" brenda fjale
  s'është operator mohimi.
- **Event trust boundary:** `GITHUB_EVENT_PATH` lexohet automatikisht VETËM kur
  `GITHUB_ACTIONS=true` dhe `GITHUB_EVENT_NAME=pull_request`; lokalisht vetëm
  `--event-file` eksplicit; O_NOFOLLOW + fstat; caps fikse. **Skema (P-IV §8):**
  `pull_request.base.sha`/`head.sha` MANDATORY SHA-40 (mungesa/null/tip invalid →
  exit 2); mismatch me `--base`/`--head` → `EVENT_RANGE_MISMATCH`. **State-et:**
  pa event në run lokal → PR-body `NOT_EVALUATED`; event me body `""`/null/mungues →
  body i NJOHUR BOSH → PR-body `EVALUATED` + mungesa e scope block raportohet
  sipas kontratës (nuk barazohet me "pa event").
- **Trust boundary i ardhshëm për required-CI (i regjistruar):** kur ndonjë profil
  të promovohet në required (pas HG-LINTER-REQUIRED — ende pending), policy/config/
  kodi që gjykon duhet të vijë nga burim **trusted/protected/pinned**; PR head-i
  është VETËM input — PR head-i nuk e përcakton vetë policy-n required që e gjykon.
  PR #21 mbetet advisory/non-required.

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
