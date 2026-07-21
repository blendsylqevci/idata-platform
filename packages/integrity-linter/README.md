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
  `--no-ext-diff --no-textconv` (provohet me sentinel fixture);
  **`--literal-pathspecs` i centralizuar (P-V C1)** — çdo repo path nga metadata
  trajtohet LITERAL: zero glob/pathspec magic, sibling i bllokuar s'lexohet dot
  përmes emrash si `[x].md`/`*.md`.
- **Path-policy ancestor-aware (P-V C2):** klasifikuesit privatë (.env/.env.*,
  `*.secret.md`, stems owner-only, prapashtesa kredencialesh) kontrollohen mbi
  ÇDO segment të path-it, jo vetëm basename-in final; allowlist-i env
  (.env.example/.env.sef.example) vlen VETËM si file final; absolute/`..`/
  segment bosh/NUL → BLOCKED. Parity me .gitignore + guard + PROJECT_RULES.
- **Protected machine tokens (P-V F1/F2):** authority e obfuskuar me
  homoglyph/zero-width/format-char → exit 2 `AUTHORITY_OBFUSCATED`; consumer i
  obfuskuar → `DC/HP-MACHINE-TOKEN-OBFUSCATED` (kurrë silent no-match). RAW
  teksti mbetet burimi; projeksioni i detektimit vetëm ZBULON — zero NFKC
  global, zero heqje të heshtur zero-width, zero auto-zëvendësim homoglyph-i.
- **GFM container-aware (P-V F3):** tabelat brenda blockquote (edhe nested)
  japin të njëjtat findings strukturore; depth i përzier header/delimiter/data
  s'kalon silent; fence brenda quote ndiqet me thellësi.
- **Role identity (P-V F4/C4):** `<br>`/HTML comments/whitespace entities →
  separator vetëm në role-bearing values; markup i papritur → `DC-ROLE-MARKUP`
  REVIEW; kandidati i identitetit njihet në çdo casing. Ambiguiteti → REVIEW,
  kurrë auto-approval; zero matched content në output.
- **Etiketa historike (P-V C3 → P-VII Fix C, EKZAKTE):** prefiks me dy-pika
  (`HISTORICAL:`/`SUPERSEDED:`/`HISTORIK:`/`Historia:`), marker inline VETËM
  i MBYLLUR `(HISTORICAL)`/`(SUPERSEDED)`, ose qelizë statusi EKZAKTE VETËM
  në KOLONËN Status të një tabele reale GFM (parser + indeks kolone). Fjala
  e lirë në prozë/qelizë tjetër NUK exempt-on; `Step B (HISTORICAL)` blankon
  vetëm segmentin e vet. **Mohimi (P-V F5 → P-VII Fix B):** vetëm një mohim
  i vetëm i lidhur direkt (ose `JO-...` eksplicit) suppress-on; counter-i i
  ambiguitetit numëron edhe `jo`; mohim i dyfishtë → finding-u primar mbetet.
- **Mohimi i zinxhirit (P-VII Fix A, CLAIM-scoped):** vetëm gramatika e
  ngushtë e claim-mohimit (kopulë + emër rendi; "mos e konsidero ... rend";
  "jo <dëftor> <emër>") e shuan zinxhirin — "Nuk ndryshon rendi:", "Nuk ka
  PII, rendi aktual:" vlerësohen NORMALISHT; analizohet fjalia e TËRË e
  claim-it; `ç'është ...` është pyetje, jo mohim. **Chain-tokens (P-VII Fix
  F):** kufij Unicode-aware në të dyja anët — "Step Bë"/"Tier 2ë" NUK janë
  tokenë validë.
- **Path classifier (P-VI §4 → P-VII Fix D):** rrënjët POSIX vetëm si segment
  ABSOLUT ("word/tmp" ≠ path lokal), case-insensitive (`/users`); `C:/`
  brenda URL-je http(s) s'është FP; `file:///...` dhe `vscode://file/...`
  janë LOKALE dhe kapen; UNC edhe i rrethuar me pikësim; URL + path i dytë
  lokal në të njëjtin rresht → path-i kapet. **Fence (P-VII Fix E,
  CommonMark):** opener/closer me MAX 3 hapësira indentim total; info-string
  i backtick-fence nuk lejon backtick (tilde po).
- **CLI/engine determinist (P-V H1–H4):** direct-exec guard me URL/realpath
  real; duplicate profiles → `DUPLICATE_PROFILE` exit 2; sanitizer i paekuivok
  (backslash i pari — zero kolizione në corpus); event read loop me `bytesRead`
  → `EVENT_SHORT_READ` fail-closed.
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

Linter-i mbulon klasat e regjistruara në 07_PRODATA §Linter për 2/16 profile —
statusi mbetet i KUFIZUAR në atë katalog: kontroll strukture/konsistence, JO
provë privacy/compliance. **Semantika ligjore mbetet gjithmonë human review +
Human Gate** — linter-i nuk jep opinion ligjor dhe nuk vendos semantikën e
asnjë neni.

**Kufij residualë të njohur (advisory — P-VII, sjellja AKTUALE e dëshmuar me
teste KNOWN_LIMITATION në `test/known-limitations.test.ts`):**

- Konfuzabla Unicode PËRTEJ setit të kuruar (jo tabela e plotë UTS #39) nuk
  detektohen si obfuscation.
- Bidi/format-char NGJITUR një protected token-i (jashtë vetë tokenit) kalon
  — detektimi është count-based (skeleton vs raw).
- Combining marks (jo-NFC) brenda token-it nuk detektohen (nuk bëhet NFC).
- Format-char (p.sh. ZWSP) i injektuar BRENDA një token-i path e shpëton nga
  classifier-i i path-eve lokale.
- Path lokal i NGULITUR brenda një URL-je http(s) (query/redirect) nuk kapet.

KY SEKSION DOKUMENTON kufij të njohur — NUK autorizon shpërfilljen e gjetjeve
të ardhshme dhe NUK i deklaron ato "jo-defekte". Pranimi formal i cilitdo
residual si "jashtë scope" kërkon **Human Gate të VEÇANTË** (follow-up OPEN).

**Kufij të tjerë të dokumentuar (P-VI):**

- Markdown-i mbulohet vetëm në kontratën që kërkon linter-i (tabela GFM me
  container/fence semantics të CommonMark) — **JO parser i përgjithshëm
  Markdown** (p.sh. setext headings, HTML blocks, reference links nuk
  interpretohen).
- Mohimi/etiketat trajtohen sintaksorisht (forma të deklaruara + kufij
  Unicode) — jo kuptim semantik i fjalive; formulime krejt të reja mohimi
  jashtë formave të mbuluara mund të kërkojnë zgjerim kontrate.
- Konfuzablat e Unicode mbulohen nga një hartë e KURUAR (cirilike/greke të
  zakonshme) — jo tabela e plotë UTS #39.
- Klasifikuesi i path-eve absolute mbulon rrënjët e listuara + Windows/UNC —
  jo çdo layout ekzotik OS-i.
- `\b` i ASCII-t mbetet VETËM në pattern-e ku fqinji Unicode s'formon fjalë
  reale (IMPLEMENTED/TESTED, GOV-\d, pastaj, dhe) — auditi u RIFRESKUA në
  P-VII: chain-tokens (Step B/C, Tier 2, re-review, HUMAN GATE, PREFLIGHT)
  u konvertuan në lookaround \p{L}\p{N} sepse pretendimi i P-VI "të sigurta"
  ishte i PASAKTË për ta ("Step Bë" pranohej).

## Workflow

`.github/workflows/governance-advisory.yml` — advisory, **JO-required**; `bash` +
`set -euo pipefail`, pa pipe exit-sensitive; findings advisory → jeshil; engine error →
i kuq. Required checks mbeten vetëm `build-test` + `gitleaks`.
