# PRO DATA — Source→Target Closure v2 (iData+)

**Versioni:** 2.13 · **Data:** 22 Korrik 2026 · **Pronari:** Group Owner (Human Gate) · **Statusi:** canonical/effective upon merge of this change (supersedes v1.1 — VETËM për interpretimin target/migrim/maturity, shih §9)
**Burimi i këtij versioni:** canonical docs aktuale (SSOT, tracker, roadmap, `05_Decision_Gates_ADR.md`/ADR-003, `04_Data_Model_Draft.md`, `02_RBAC_Matrix.md`, `03_Accounting_Compatibility_Matrix.md`) + **kodi/migrimet/testet tracked** + **metadata e evidence paths**. **Zero content-read nga depoja e injoruar e evidence-it private-local (ignored private-local evidence store), backup-e, MDF/LDF, DB e restauruar apo evidence tjetër private-local në këtë version** — evidence historike trajtohet si **opaque reference** (§7).
**Historia:** v2.2 — Documentation & Compliance Integrity Linter u zgjerua në 16 profile platform-wide; fazat u regjistruan; HG-LINTER-BUILD u aprovua; implementimi nuk ka nisur; required-CI mbetet gated. · v2.3 (13.07.2026, effective on merge të PR-së tooling GOV-001) — **engine + `DOC-CORE` + `HISTORY-PRIV` = IMPLEMENTED_ADVISORY** (`packages/integrity-linter` + workflow advisory jo-required); **14 profilet e tjera = NOT_IMPLEMENTED**; output i redaktuar për path sensitivë (`[REDACTED_PATH]`, zero matched content); acceptance sipas fazimit të profilit; required-CI gate i paprekur. · v2.4 (13.07.2026, hardening II i PR #21) — **formulimi i saktë i statusit**: *"HISTORY-PRIV baseline canonical rule catalogue = IMPLEMENTED_ADVISORY: çdo klasë e listuar shprehimisht në §Per-commit history scan dhe §Acceptance ka implementim aktiv per-commit + fixtures pozitive/negative/boundary/inverse. Ky status NUK nënkupton privacy/compliance complete, findings-free, required-CI ose implementim të profileve të tjera."* DOC-CORE me kontratë bidirectional authority→consumers (REQUIRED_CURRENT/SCAN_IF_PRESENT; missing/ambiguous → skanim i paplotë); binary/oversize → coverage NOT_EVALUATED eksplicite; WARNING/REVIEW = findings, JO PASS; merge i PR #21 e bën Step B vetëm **ELIGIBLE** — nuk e nis. · v2.5 (16.07.2026, hardening III i PR #21) — **përshkallëzim i vetëdijshëm i politikës së content-coverage**: binary/oversize blob në range → **fail-closed `INCOMPLETE_SCAN` / exit 2** (*supersedes* v2.4 "coverage NOT_EVALUATED": një run që s'e lexon dot çdo blob NUK raporton më exit 0); authority per-commit me validator të përbashkët fail-closed (missing/unreadable/ambiguous → exit 2, aktivizim ONCE_PRESENT); konsumatorët path-bound; relocation private→publike ndiqet me OID nëpër gjithë range-un; zinxhiri SEF kontrollohet për **prani + rend** të të 6 hallkave; klasa reale `phase` nga kolona "Faza" e këtij dokumenti; parity e plotë path/credential me guard-in; parser GFM për tabela; event schema strikte; workflow i lidhur me head SHA imutabël. Statusi advisory/jo-required dhe gate-t (HG-LINTER-REQUIRED i paaprovuar) të paprekura. · v2.6 (16.07.2026, hardening IV i PR #21) — mbyllje e klasave fail-open të review-t R4: aktivizimi ONCE_PRESENT bëhet **nga PRANIA** e authority-t (jo nga leximi i parë valid); **`activationAnchor`** si compatibility mechanism i KUFIZUAR (vetëm `profile-registry-set` + `hg-linter-required-status`, structural label i seksionit v2.3 — zgjerim te klasa të tjera VETËM me review/Human Gate; heqja e ardhshme kërkon PR + review më vete); klasifikimi binar/encoding mbi **byte-t RAW të blob-it sipas OID** — i pavarur nga `.gitattributes` (`INVALID_ENCODING` i ri); parser GFM me **outer pipes opsionale** + përjashtim fenced blocks; mohimi lokal **PER-MATCH**; authority e fazës = rreshti `ATK-002` i Roadmap (ky dokument bëhet consumer i saj); zinxhiri me mohim claim-scoped, etiketa historike segment-scoped dhe rend PER-CLAIM (bullets të pavarura s'krahasohen mes tyre); event schema: `base/head.sha` MANDATORY, body null/mungues me event prezent = **bosh i njohur → EVALUATED**; role/privacy coverage mbi listë machine-readable sipërfaqesh + kandidati i identitetit në parentetika → privacy REVIEW. Statusi advisory/jo-required dhe HG-LINTER-REQUIRED i paaprovuar — të paprekura. · v2.7 (18.07.2026, hardening V i PR #21 — bashkimi i gjetjeve të pavarura Fable + Codex) — **git paths LITERALE** (`--literal-pathspecs` i centralizuar: asnjë glob/pathspec magic nga metadata; sibling i bllokuar s'lexohet); **path-policy ancestor-aware**: klasifikuesit privatë (.env/.env.*, *.secret.md, stems owner-only, prapashtesa) mbi ÇDO segment — allowlist-i env vetëm si file final; **boundary i protected machine tokens**: authority e obfuskuar (homoglyph/zero-width/format-char) → exit 2 `AUTHORITY_OBFUSCATED`, consumer i obfuskuar → finding i dedikuar `MACHINE-TOKEN-OBFUSCATED` (kurrë silent no-match; raw = burimi, zero NFKC global); **GFM blockquote-aware** (tabela të cituara japin të njëjtat findings; mixed depth s'kalon silent); **role identity** përmes `<br>`/HTML comment/entities + çdo casing, markup i papritur → `DC-ROLE-MARKUP`; **etiketa historike vetëm strukturore eksplicite** (`vs <n>` hequr — s'fsheh më drift aktiv); **mohimi i dyfishtë/ambig** nuk suppress-on; **CLI determinist** (direct-exec guard real URL, duplicate profiles → exit 2, sanitizer i paekuivok, event read loop me `EVENT_SHORT_READ`). Statusi advisory/jo-required, GOV-001 `PARTIAL / ADVISORY_ACTIVE` dhe HG-LINTER-REQUIRED pending — të paprekura; semantika ligjore mbetet human review. · v2.8 (18.07.2026, hardening VI i PR #21 — katër bypass-et e konfirmuara në mënyrë të pavarur Codex + Claude) — **mohimi i zinxhirit UNICODE-aware** (`s'është/s’janë/ç'është` me apostrof straight/curly/modifier — `\b` i ASCII-t dështonte pranë ë-së; lookaround `\p{L}\p{N}` të centralizuar; U+2028/29 delimitues + kandidat obfuskimi; "zinxhiri nuk ndryshon" mbetet aktiv); **etiketa historike VETËM strukturore** (prefix `HISTORICAL:/SUPERSEDED:/HISTORIK:/Historia:`, marker `(X)`/`[X…`, qelizë tabele ekzakte — "nuk është superseded/ish" dhe fjalët e lira NUK përjashtojnë; klasifikim i centralizuar pa regex kontradiktorë); **CommonMark fence state machine** (run më i shkurtër brenda fence aktive = CONTENT — jo nested opener; closer = i njëjti karakter, len ≥ opener, pa info string; tabela pas closer-it skanohet); **absolute local path coverage** (klasifikues i centralizuar: rrënjët POSIX + `~/` + Windows drive/UNC/`\\?\` — jo vetëm `/Users/`; anti-FP për URL/endpoints fiskale/path relative); **workflow rescan në `edited`** (PR body-edit ri-skanon; kontrata e workflow-it e provuar me test). Statusi advisory/jo-required dhe gates të paprekura. · v2.9 (21.07.2026, hardening VII i PR #21 — klasat aksidentale mbi input natyror Markdown/shqip) — **mohimi i zinxhirit CLAIM-scoped** ("Nuk ndryshon rendi:"/"Nuk ka PII, rendi aktual:" NUK fshihen më — gramatikë e ngushtë kopulë+emër rendi; analizohet fjalia e tërë; `ç'është` = pyetje, jo mohim); **counter-i i mohimit numëron edhe `jo`** ("jo s'raportohet … 789" kapet); **etiketa historike EKZAKTE** (marker vetëm i mbyllur `(HISTORICAL)`/`(SUPERSEDED)`; qeliza vetëm në KOLONËN Status me parser GFM; "Step B (HISTORICAL)" blankon vetëm segmentin); **path classifier v2** (rrënjë vetëm si segment absolut — "word/tmp" jo; case-insensitive; `C:/` brenda http(s) URL jo-FP; UNC në pikësim + `file:///` + `vscode://file/` kapen); **CommonMark fence** (indentim 0–3; info-string i backtick-fence pa backtick); **chain-tokens Unicode-aware** ("Step Bë"/"Tier 2ë" s'pranohen — korrigjon pretendimin e audit-it §5 të P-VI); **kufij residualë të dokumentuar HONESTISHT** me teste KNOWN_LIMITATION (konfuzabla jashtë setit të kuruar, bidi ngjitur token-it, combining marks, format-char në path-token, path brenda URL-je) — pranimi formal i çdo residuali kërkon Human Gate të VEÇANTË (follow-up OPEN). Statusi advisory/jo-required dhe gates të paprekura.

**Historia v2.10 (21.07.2026, hardening VIII i PR #21 — gjetjet e review-t të pavarur Codex):** **Markdown aktiv**: HTML comments/tags (parser quote-aware), fenced/indented code, link destinations dhe reference definitions (edhe title next-line) nuk plotësojnë authority/chain; comment/tag HTML i pambyllur → fail-closed; label-et e dukshme dhe inline code mbeten semantikisht aktive; offset-et UTF-16 ruhen edhe pranë karaktereve astrale; të gjitha chain-claims aktive vlerësohen dhe mohimi është sentence/claim-scoped. **Drift-i historik është per-match** në DOC-CORE, HISTORY-PRIV dhe PR body; vetëm prefiksi/qeliza Status ekzakte mbeten row-wide. **Git fail-closed/determinist**: color/pager/locale të imponuara, unified-diff me counter source+target, hunk i munguar/cunguar/ANSI → exit 2. Metadata e gjithë range-it lexohet para blob-eve; relocation exact-copy kapet edhe pa private-delete ose kur kopja vjen para tij; kur ekziston private source, çdo public content-change jo-identik kalon në privacy REVIEW pa lexim të blob-it privat. Parser-i GFM zbaton odd/even backslash parity dhe code-span delimiter me gjatësi ekzakte; role comments dhe role-labels brenda listës/blockquote/task-list me nesting arbitrar kontrollohen për privacy pa u bërë governance aktiv. Path-et me `Default_Ignorable_Code_Point`, combining marks ose homoglyph që maskon klasë private dështojnë fail-closed; seti përdoret edhe nga protected-token skeleton; u shtuan të gjitha prefikset klasike të dokumentuara dhe encrypted/DSA private-key headers. Statusi mbetet advisory/jo-required; `GOV-001` mbetet `PARTIAL / ADVISORY_ACTIVE`; `HG-LINTER-REQUIRED`, `ATK-011`, Step B dhe Tier 2 mbeten të hapura.

**Historia v2.11 (22.07.2026, correction pass i PR #21 — typed role analyzer / WA-001 / WA-002):** analiza e roleve u bashkua në një reprezentim të vetëm të tipizuar (`role-analysis`), mbi state machines ekzistuese — jo parser i ri platforme. Njohja e role-label/role-header bëhet mbi tekstin e DUKSHËM të renderuar: markup zero-content (p.sh. `Own<span></span>er`, comment i ngjitur brenda fjalës) nuk krijon më word boundary artificial dhe nuk e nxjerr vlerën nga privacy review; markup ndarës (`br`/`td`/`li`/…) nuk bashkon fjalë që renderer-i s'i bashkon; markup review NUK varet nga suksesi i label-regex-it; malformed HTML/comment mbetet fail-closed. HTML comments mbeten JO-AKTIVE për governance semantics (s'plotësojnë role-label/authority/chain) dhe skanohen veçmas si privacy evidence raw. Parentetikat e vlerave role-bearing marrin disposition EKSPLICIT (`APPROVED_ANNOTATION` / `IDENTITY_REVIEW` / `AMBIGUOUS_REVIEW`): lidhëzat funksionale ("dhe"/"ose") nuk i ndajnë më termat residualë — ≥2 terma residualë → REVIEW edhe kur ndahen me lidhëz; term i vetëm i paaprovuar → REVIEW (e paqarta kurrë silent approval — mbyllet edhe kalimi i heshtur i akronimeve <4 shkronja të paaprovuara); presja dhe `&` vazhdojnë të ndezin si më parë; allowlist-et e aprovuara/funksionale të pandryshuara. Statusi advisory/jo-required; `GOV-001` mbetet `PARTIAL / ADVISORY_ACTIVE`; `HG-LINTER-REQUIRED`, `ATK-011`, Step B dhe Tier 2 mbeten të hapura.

**Historia v2.12 (22.07.2026, correction pass fail-safe i PR #21 — kontrata me katër gjendje + discovery):** analiza e roleve zëvendëson "provo të njohësh çdo variant" me **restricted canonical syntax + fail-safe discovery**: çdo rajon role-bearing klasifikohet në saktësisht një gjendje — `INACTIVE` (fenced/indented code, s'skanohet si role evidence), `CLEAN` (vetëm sintaksë kanonike e kuptuar plotësisht DHE rol/vlerë e allowlisted), `REVIEW` (entity/escape/markup/format-char/comment evidence/parenthetical jo-i-allowlisted përfshirë vetëm-numra e formate telefoni/sintaksë e pasigurt — finding i redaktuar), ose `INCOMPLETE_SCAN` (markup i dëmtuar → exit 2). Discovery është vetë pjesë e kontratës: kandidati zbulohet edhe përmes skeleton-it konservator detection-only (entity dec/hex, set i vogël named, backslash-escape, format-chars/konfuzabla) — pa dekodim universal; sintaksa e pasigurt që mund të maskojë label/header merr minimumi REVIEW; "jo rajon roli" në heshtje vlen vetëm kur analiza e kufizuar e provon. TË GJITHA kolonat role-bearing skanohen (jo vetëm e para), me region-id deterministik pa tekst sensitiv; comment-et që marrin pjesë në formimin e label-it janë evidencë e të njëjtit rajon; comment-et brenda code = INACTIVE; fraza e aprovuar e mbyllur me pikë trajtohet konsistent. `role-analysis` është autoriteti i vetëm — `doc-core` konsumon vetëm rajonet e tipizuara. Asnjë pretendim "renderer i plotë" apo "dekoder i plotë": gjithçka jashtë sintaksës kanonike të kuptuar → REVIEW ose exit 2, dhe **human review mbetet backstop**. Allowlist-et e pandryshuara. Statusi advisory/jo-required; `GOV-001` mbetet `PARTIAL / ADVISORY_ACTIVE`; `HG-LINTER-REQUIRED`, `ATK-011`, Step B dhe Tier 2 mbeten të hapura.

**Historia v2.13 (22.07.2026, correction pass cross-boundary fence/comment i PR #21):** u mbyll vrima e vetisë fail-safe ku një konstrukt role-bearing që ndez bëhej i heshtur (INACTIVE/exit 0) i mbështjellë në strukturë fence/comment cross-boundary. Skanimi i aktivitetit të roleve bëhet nga **NJË state machine i vetëm SIMETRIK** mbi raw source me gjendjet **NEUTRAL · IN_FENCE · IN_COMMENT**: në IN_FENCE marker-ët e komentit janë LITERALE, në IN_COMMENT marker-ët e fence-s janë LITERALE; tranzicionet ndodhin vetëm nga gjendja aktive përkatëse (rregullat e fence-s pasqyrojnë kontratën ekzistuese: backtick/tilde ≥3, indent ≤3, info-string i backtick-fence pa backtick, closer i njëjti karakter/gjatësi ≥/bare/depth i njëjtë). Përmbajtja role-bearing bëhet INACTIVE **vetëm** brenda një gjendjeje joaktive të QARTË; **fence e pambyllur në EOF është ambige → përmbajtja role-bearing brenda saj merr minimumi REVIEW, kurrë heshtje**; comment aktiv i pambyllur → exit 2; comment tërësisht brenda fence = literal (jo evidencë komenti); fence tërësisht brenda comment-i = literale. Autoriteti mbetet i vetëm te analiza e tipizuar e roleve; **projeksioni aktiv i authority/chain dhe semantika e HISTORY-PRIV nuk u prekën**. Statusi advisory/jo-required; `GOV-001` mbetet `PARTIAL / ADVISORY_ACTIVE`; `HG-LINTER-REQUIRED`, `ATK-011`, Step B dhe Tier 2 mbeten të hapura.

---

## 0. RREGULLI KRYESOR — NXJERRJA E MBYLLUR ≠ E IMPLEMENTUAR

> **"Extraction complete" do të thotë VETËM se evidence është siguruar.** NUK do të thotë se target capability është e implementuar, e testuar, production-ready apo e nënshkruar (signed-off). Implementimi ndodh **në fazën përkatëse sipas kolonës "Faza" dhe roadmap-it**, dhe çdo capability kalon nëpër **pipeline-in Source→Target** (§1B). **ZERO përqindje pa metodologji** — përdoren vetëm statuse të numërueshme me prova të verifikueshme.

Nxjerrja nga backup-i mbetet **E MBYLLUR** (asnjë nxjerrje e re; ky version nuk hapi asnjë burim evidence). Backup-i mbetet **golden reference baseline deri në lansim** — për parity/UAT/cutover — por statusi i çdo capability në iData+ matet VETËM me boshtet e §1.

## 1. Metodologjia — dy boshte të NDARA + një qualifier

### 1A. TREATMENT — tag-set (JO vlerë ekskluzive)

| Tag | Kuptimi |
|---|---|
| `REQ` | capability **kërkohet** në iData+ |
| `MD` | **master-data** migrohet |
| `OB` | **saldo hapëse** migrohet në cutover |
| `REF` | source/evidence përdoret **vetëm si referencë** |
| `NO` | **source data nuk migrohet** |

Kombinimet lejohen (`REQ+REF`, `REQ+MD`, `REQ+NO`, `REF+NO`, …).

> **RREGULL KRITIK:** `NO` do të thotë *"source data nuk migrohet"* — **JO** *"target capability nuk kërkohet"*. Shembull kanonik: **passwords/PIN/MAC = `NO` për migrim** (ADR-003), por **auth/user capability = `REQ`** në target.

### 1B. TARGET MATURITY — stage-i MË I LARTË I PROVUAR

`GAP → EVIDENCE → SPECIFIED → MODELED → IMPLEMENTED → TESTED → SIGNED-OFF`

| Stage | Përkufizimi (prova e kërkuar) |
|---|---|
| `GAP` | asnjë provë target |
| `EVIDENCE` | burim canonical i sanitizuar OSE **opaque evidence reference** që ekziston por **nuk është rishikuar në këtë PR** |
| `SPECIFIED` | file+seksion canonical i verifikueshëm |
| `MODELED` | entitet/fushë/invariant **konkret** në `04_Data_Model_Draft.md` |
| `IMPLEMENTED` | **kod/migrim JO-PoC** në repo (commit/PR real; implementim aktual ekzistues) |
| `TESTED` | test i ekzekutueshëm që teston **implementimin aktual** (jo vetëm simulatorin) + rezultat CI real |
| `SIGNED-OFF` | rol + datë + evidence e qëndrueshme + aprovim i verifikueshëm |

Çdo rresht ka: **maturity + proof/referencë + fazë + qualifier PoC (nëse vlen) + gap/next gate**.

> Kur një claim s'provohet dot pa hapur evidence të ndaluar → shënohet **`EVIDENCE_REFERENCE_ONLY / NOT_REVERIFIED_IN_THIS_PR`** dhe **NUK promovohet** në IMPLEMENTED/TESTED/SIGNED-OFF.

### 1C. `PROVEN-PoC` — qualifier i VEÇANTË (jo maturity stage)

- **PROVEN-PoC ≠ IMPLEMENTED production. PROVEN-PoC ≠ TESTED production. PoC PASS ≠ production readiness.**
- PoC provon **feasibility/invariant**; capability finale vazhdon pipeline-in në fazën e vet.
- **Migrimet `0002`–`0006` janë PoC sipas header-ave realë të tyre** (0002 = PoC-1/ENG-003 · 0003 = PoC-2 · 0004 = PoC-3 · 0005 = PoC-4a · 0006 = PoC-5); **vetëm `0001` është skeleton JO-PoC (Increment 1)**. Asnjë provë nga 0002/0003, funksionet `poc2_*` apo testet PoC **nuk e promovon** capability-n në IMPLEMENTED/TESTED production.
- Testet e librarisë `packages/accounting` provojnë **feasibility të librarisë/simulatorit** (rregullat në izolim) — **JO** target capability të integruar production.

## 2. Çfarë u nxor nga backup-i — EVIDENCE HISTORIKE (opaque)

Përmbledhja Wave 1–3 (struktura/DDL; accounting/costing/fiscal/setup; restaurant POS/KDS; audit/user/RBAC/config i sanitizuar) dhe metrikat e nxjerrjes qëndrojnë **siç janë regjistruar historikisht** (v1.1 i këtij dokumenti + SSOT Decisions Log historik + auditi i plotësisë private-local). **Historical evidence remains opaque, not reread and not reproduced here.** Statusi në këtë version: `EVIDENCE_REFERENCE_ONLY / NOT_REVERIFIED_IN_THIS_PR` (§7, §9).

## 3. Parity Matrix v2 — Source→Target me dy boshte

*(Proof-et = file/migrim/test **tracked** në repo ose file+seksion canonical — të verifikueshme pa hapur evidence të ndaluar. "0001…0006" = `packages/database/migrations/…`; **`0001` = skeleton JO-PoC; `0002`–`0006` = migrime PoC (PoC-1…PoC-5 sipas header-ave)** — provat prej tyre mbeten në qualifier-in PROVEN-PoC, §1C.)*

| Domeni | Source (PRO DATA) | Target (iData+) | Treatment | Target Maturity | PROVEN-PoC | Proof | Faza | Gap / next gate |
|---|---|---|---|---|---|---|---|---|
| GL / Kontabilitet | skema e kontimit, ditari, `*DEL` | `journal_entry/line` + motori i postimit | `REQ+REF+NO` (historia s'migrohet) | **MODELED** (target model) | **PO** (rregullat në librari `packages/accounting` + rrjedha DB PoC-2 — **jo production TESTED**) | `04_Data_Model_Draft.md` §C/§L; 0003 (**PoC-2**: journal/APPEND-ONLY/DEFERRABLE debit=kredit = feasibility); `packages/accounting/src/posting.test.ts` (feasibility librarie) | F2 | posting engine production + integrimi (ADR-012 Open; `poc2_*` = PoC → DROP/supersede) |
| Libra ATK + Deklaratë TVSH | Libri Blerjes/Shitjes (PLANET2) + template zyrtare | `tax_book_entry` + buckets, `vat_declaration` | `REQ+REF+NO` | **MODELED** (struktura header→buckets) | **PO** (buckets/mixed-VAT rrjedha DB PoC-2; validimi baseline Maj 2026 = `ACC-010` closed, vlerat private-local) | `04_Data_Model_Draft.md` §C; 0003 (**PoC-2**); `03_Accounting_Compatibility_Matrix.md` (box-maps) | F2 | `vat_declaration`/eksporti production = **pending F2**; rounding → `ACC-003` |
| Costing / COGS | `tblCalculationItemsCost*` | `inventory_cost_layer / stock_valuation` | `REQ+REF+NO` | **MODELED** | — | `04_Data_Model_Draft.md` §D | F3 | implementim + rikrijim kundër baseline (UAT) |
| Historik çmimesh | `tblArtikujtCmimetLog` | `price` + `price_history` | `REQ+REF+NO` (log-u historik s'migrohet; çmimet AKTIVE = MD te rreshti i artikujve) | **MODELED** | — | `04_Data_Model_Draft.md` §B | F2/F3 | implementim |
| Artikuj / njësi / tipe | `tblArtikujt*`, `tblNjesiteMatese`, `tblTipetMalli` | `article`, `uom`, `category` | `MD+REQ` | **MODELED** | — | `04_Data_Model_Draft.md` §B; katalogët zyrtarë SEF 31/53 → `ATK_Compliance_Gate.md` §2b (follow-up F1/F5) | **F3** (article/product master) · **F9** (migrimi MD) | migrimi real i MD = delivery gate ADR-003 (mapping/cleansing/dedup) |
| Banka / vite fiskale | `tblBankat`, `tblFiscalYears` | `bank_account`, `fiscal_period` | `MD` | **MODELED** | **PO** (`fiscal_period`+period-lock si feasibility në 0003 = **PoC-2**; invarianti production i period-lock **JO i implementuar ende**) | `04_Data_Model_Draft.md` §C; 0003 (**PoC-2**) | F2 + F9 | period-lock production (F2) + migrimi MD në cutover |
| Saldo hapëse | `tblDocBeginningBalance*` | saldo hapëse në cutover **1 Jan 2027** | `OB` | **SPECIFIED** | — | ADR-003; `Cutover_Rollback_Runbook.md` §3–4 | F9 (cutover) | delivery gates: mapping/cleansing/dedup/freeze/dry-run/reconciliation/sign-off |
| Fiskalizim / SEF | `tblFiscalisedDocuments` | `fiscal_coupon`, fiscal outbox | `REQ+REF+NO` | **SPECIFIED** | **PO** (PR #16 = **evidence historike mock**; 0006 = **PoC-5**, tabela `poc5_*`) — **post-audit 12.07: Tier 1 overall PARTIAL/REOPENED · compliance FAIL (5 CONTRADICTS-rreshta = 4 defekte unike) · Tier 2 NOT IMPLEMENTED/NO-GO** | SSOT §7; `ATK_Compliance_Gate.md` v2.0 + Ledger | F5 | Step B (T2-01..10, `ATK-012`) → Step C → Tier 2; print layer |
| POS — porosi/tavolina | `tblTMPRestShitjet`, `tblDocPorositeShitjet*`, `tblRestTavolinat`(X/Y) | `table`(pos_x/y), `table_session`, `order`, `order_item` | `REQ+REF+NO` | **MODELED** | — | `04_Data_Model_Draft.md` §G | **F7 (bazë) / F8 (POS i plotë)** | implementim POS |
| KDS — kuzhinë | `tblTMPRestShitjetKuzhina`, `tblRestDailyPorosiaCount` | `kitchen_ticket`, `kds_station` | `REQ+REF+NO` | **MODELED** | — | `04_Data_Model_Draft.md` §G | F8 | implementim KDS |
| Barazim arke/kamarieri | `tblDocPOSBarazimi*` | `cash_shift`, `worker_settlement` | `REQ+REF+NO` (capability e kërkuar; source vetëm referencë; historia s'migrohet — modeli ynë **net-new**) | **MODELED** | — | `04_Data_Model_Draft.md` §G | F8 | implementim + reconciliation ditore (runbook §9) |
| Fshirje/korrigjime → aprovim | `*_Deleted`+trigger, `*DEL` | `manager_approval` + audit (anulim+PIN+arsye) | `REQ+REF+NO` (**më i rreptë se source**) | **MODELED** | — | `04_Data_Model_Draft.md` §G (`manager_approval`); invarianti bazë i audit-it → rreshti "Audit — invarianti DB" | F1/F8 | approval workflow JO TESTED ende — implementim + teste |
| RBAC — skeleton i skemës | `tblUSERS` (groups/rights/report/special/warehouse/POS) | `role`, `role_permission`, `permission`, `business_membership` | `REQ+REF+NO` (privilegjet legacy = vetëm referencë; s'migrohen 1:1) | **IMPLEMENTED** (**ngushtë: vetëm skema skeleton**) | — | 0001 (**skeleton JO-PoC**); `02_RBAC_Matrix.md` (spec e matricës) | F1/F2 | matrica e plotë per-feature + **API enforcement production = F1** |
| RBAC — izolimi cross-tenant | — | composite FK cross-tenant + deny-by-default + RLS | `REQ` | **MODELED** (invarianti composite-FK = konventë e `04_Data_Model_Draft.md`/ENG-003) | **PO** (PoC-1 PASS, PR #10 — feasibility në 0002 **PoC-1/ENG-003** + `tenant-isolation.test.ts`, demo API + RLS **test-scoped**) | `04_Data_Model_Draft.md` (konventa globale business_id/composite FK); roadmap ENG-003 (closed) | F1 | enforcement production (API + RLS policy reale) = F1 |
| Përdorues / auth | `tblUSERS` (passwords/PIN/MAC) | `app_user` + `business_membership` | `REQ+NO` (**capability REQ; source data NO** — shembulli kanonik i §1A) | **IMPLEMENTED** (**ngushtë: skema skeleton 0001**) | — | 0001 (**skeleton JO-PoC**): `app_user` = **identitet GLOBAL pa `business_id`** (email/name/mfa/status); lidhja tenant = **`business_membership.business_id`**; `role`/`role_permission` = tenant-scoped | F1 | **auth stack production = F1** (ADR-010 vetëm ku provider/hosting është relevant); users rikrijohen/resetohen — kurrë migrim kredencialesh |
| Audit — invarianti DB | `tblAuditLogs`, `tblUsersLog`, `tblActivities` | `audit_log` append-only | `REF+NO` (historiku NUK migrohet) | **TESTED** (vlen VETËM për invariantin append-only: `audit_log` refuzon UPDATE/DELETE) | — | 0001 trigger `audit_log_no_mutate` (**skeleton JO-PoC**) + `schema.test.ts` "audit_log është append-only" (CI real) | F1 | — |
| Audit — producers/retention | — | event producers + retention/arkivim + lidhja me approval workflow | `REQ` | **MODELED** | — | `04_Data_Model_Draft.md` §G; SSOT §9 (LMDhP retention/minimizim) | F1/F8 | JO TESTED ende — implementim producers + retention policy + teste |
| Config | tabela config legacy | config i **versionuar/typed** | `REQ` (riprojektim) | **GAP** | — | — (kërkesa e regjistruar; s'ka entitet konkret ende) | F1/F2 | modelim config i versionuar |
| Procedura legacy (stored procedures) | stored procedures | logjika → NestJS/Postgres functions | `REF+NO` | **EVIDENCE** (`EVIDENCE_REFERENCE_ONLY / NOT_REVERIFIED_IN_THIS_PR`) | — | referencë opaque historike (§7) | F2–F8 (sipas domenit) | rilexim i targetuar per-fazë (jo nxjerrje e re) |

## 4. Capability Coverage Map — WMS/OMS (kufiri i qartë)

> **Ky është Capability Coverage Map — JO pretendim se iData+ është WMS/OMS universal i implementuar.** Guardrails: PLANET `order` (dine-in POS) **≠** `sales_order` universal; `delivery_note` (fletëdërgesë dalëse e lidhur me `invoice`) **≠** `goods_receipt`; **logjika e prodhimit KABI NUK inferohet nga backup-i i PLANET-it** (vjen nga fabrika — SSOT §5); `OPS-001`/RESORT mbetet **OPEN**; `business_unit` nuk ngurtësohet këtu. Statuset e lejuara pa vendim: `GAP` / `LATER` / `DECISION_REQUIRED`.

| Capability | Kufiri (çfarë ËSHTË / çfarë S'ËSHTË) | Ku specifikohet | Treatment | Target Maturity | PROVEN-PoC | Proof | Faza | Gap / next gate |
|---|---|---|---|---|---|---|---|---|
| `sales_order` — PLANET dine-in | Porosia POS në tavolinë (`order`/`order_item`) = e modeluar. **≠ `sales_order` universal** | `04_Data_Model_Draft.md` §G | `REQ+REF+NO` | **MODELED** | — | §G `order/order_item/table_session` | **F7 (bazë) / F8 (POS/KDS i plotë)** | implementim POS |
| `sales_order` — KABI MTO | **MTO kërkon Sales Order PARA `production_order`** (SSOT §5: *"MTO — porosi speciale — kërkon Sales Order para urdhrit"*); entiteti s'është i modeluar ende | SSOT §5 (KABI, MTS/MTO) | `REQ` | **GAP** | — | SSOT §5 | **F6** | **decision gate** — modelimi i `sales_order` MTO para F6 |
| `sales_order` — OMS universal | Order-to-invoice wholesale/OMS përtej PLANET dine-in + KABI MTO | — | `REF+NO` (vetëm referencë source; pa kërkesë të vendosur) | **GAP** | — | — | **LATER** | **Human Gate** — vendim kërkese (s'ndalohet përgjithmonë) |
| `purchase` — procesimi invoice-based | Blerja sot = **`invoice` type=purchase** | `04_Data_Model_Draft.md` §F | `REQ+REF+NO` | **MODELED** | — | §F `invoice(purchase)` | F2/F3 | implementim |
| `purchase_order` — workflow PO | Porosi PARA faturës, approval, matching — **s'është i modeluar** | — | `REF+NO` (pa kërkesë të vendosur) | **GAP** | — | — | **LATER** | **Human Gate** — vendim kërkese për PO workflow |
| `goods_receipt` — pranimi si movement | Pranimi ekziston si **`stock_movement(type=in)`** | `04_Data_Model_Draft.md` §D | `REQ+REF+NO` | **MODELED** | — | §D `stock_movement` | F2/F3 | implementim |
| `goods_receipt` — dokumenti GR | **Dokument GR më vete s'ka** — `delivery_note` ekzistuese është dalëse (shitje), **JO** GR | `04_Data_Model_Draft.md` §F (`delivery_note` — dalëse) | `REQ` | **GAP** | — | — | F2/F3 | **DECISION_REQUIRED** — dokument GR më vete apo movement+invoice (vendim modelimi F2/F3) |
| `reservation / allocation` | S'ekziston asnjë entitet rezervimi/alokimi stoku | — | `NO` (source s'migrohet; kërkesa target e pavendosur) | **GAP** | — | — | **LATER** | **Human Gate / requirement decision** (s'ndalohet përgjithmonë) |
| `bin / putaway` — warehouse-level | `warehouse` ekziston (depo/restorant/shank/vehicle) | `04_Data_Model_Draft.md` §D (`warehouse`) | `REQ` | **MODELED** | — | §D | F3 | implementim |
| `bin / putaway` — bin-level | **Pa nivel bin/lokacion brenda depos** | — | `NO` (pa kërkesë të vendosur) | **GAP** | — | — | **LATER** | **Human Gate** për bin-level |
| `pick / pack / ship` — KABI van fulfillment | **Van distribution pipeline KABI** (`van_load`, `driver_route`, `proof_of_delivery`, `driver_day_close`) — **van-flow, JO pick/pack/ship universal magazine** | `04_Data_Model_Draft.md` §F | `REQ+REF+NO` | **MODELED** | — | §F (ngarkim−shitje−kthim=mbetje + variance) | **F5** (van sales/loading/POD/day-close) | implementim van-flow |
| `pick / pack / ship` — universal | Pick/pack/ship universal magazine | — | `NO` (pa kërkesë të vendosur) | **GAP** | — | — | **LATER** | **Human Gate** për pick/pack universal |
| `wave picking` | S'ekziston | — | `NO` (source s'migrohet; kërkesa target e pavendosur) | **GAP** | — | — | **LATER** | **Human Gate / requirement decision** |
| `cycle count` — adjustment hook | `stock_movement(type=adjust)` + `stock_balance` ekzistojnë | `04_Data_Model_Draft.md` §D | `REQ+REF+NO` | **MODELED** | — | §D | F3 | implementim |
| `cycle count` — procesi/dokumenti i numërimit | **Proces/dokument numërimi fizik s'është modeluar** (kërkohet së paku për saldot hapëse në cutover) | runbook §4 (T-1 saldot) | `REQ` | **GAP** | — | — | F3 + F9 | modelim i dokumentit të numërimit para cutover |
| `assembly / production` | **Prodhimi KABI**: recipe/BOM me version+approved_by+locked + cost layers; regjistri i pasterizimit; CCP gating; MTO/MTS advanced. **Burimi = fabrika/on-site (SSOT §5) — JO backup-i i PLANET-it.** PLANET kitchen prep **≠** assembly | `04_Data_Model_Draft.md` §D (recipe/BOM), §E (KABI); SSOT §5 | `REQ` | **MODELED** | — | §D/§E; SSOT §5 (CCP/pasterizim; MTS/MTO) | **F3** (recipe/BOM/cost layers) · **F4** (regjistri i pasterizimit — minimumi) · **F6** (CCP gating i plotë + MTO/MTS advanced) | implementim per fazë + batch costing (ADR-005, me kontabilistin); MTO → `sales_order` KABI (F6) |

## 5. ADR-003 — RIAFIRMUAR (i mbyllur, NUK rihapet)

**ADR-003 = Accepted (27.06.2026):** **fillim i pastër**; **master-data migrohet**; **saldo hapëse migrohet** (cutover 1 Jan 2027); **pa histori transaksionale**; **users/passwords/PIN/MAC/secrets NUK migrohen** (rikrijohen/resetohen); **historia transaksionale = REF/NO**.

**OPEN mbeten VETËM delivery gates** (jo vendimi): **mapping source→target · cleansing · dedup · freeze window · dry-run · reconciliation · sign-off** — jetojnë në F9 + `Cutover_Rollback_Runbook.md`. **Nuk ka dhe nuk krijohet vendim OPEN "saldo hapëse vs selected history"** — ai vendim është i mbyllur. Çdo ndryshim i ADR-003 kërkon **Proposed ADR të ri + analizë ndikimi + Human Gate** (JO në këtë PR).

## 6. Legacy PRO DATA custody ≠ iData+ product DR (ADR-023)

**Dy kontrolle TË NDARA — nuk bashkohen:**

**A. Legacy PRO DATA backup custody** (burimi historik deri në cutover):
- **Burimi autoritativ = `.bak`**; MDF/LDF të container-it **NUK janë** burimi autoritativ.
- Kontrolle të kërkuara: **kopje e dytë e enkriptuar** · **hash manifest** · **isolated restore drill**.
- **Statusi: REQUIRED / OPEN / EVIDENCE PENDING** — evidence jepet **privatisht nga Pronari**; **ky PR nuk pretendon se e verifikoi asgjë** (asnjë filename/path/hash/inventar privat këtu; asnjë status "verified" pa evidence private të Pronarit).
- Regjistrimi operacional → `Cutover_Rollback_Runbook.md` (seksioni përkatës).

**B. iData+ PostgreSQL DR** = **ADR-023**: PITR + backup + restore drill i **produktit të ri** — track më vete, i pandryshuar këtu.

## 7. Evidence historike — OPAQUE / READ-ONLY

Content-read i **NDALUAR** (folderi i evidence-it historik të extraction-it = **gitignored, private-local**; verifikuar në këtë PR **vetëm me metadata**: `test -e` = ekzistojnë; `git ls-files` = jo në repo):

> **Dy artefakte historike të extraction-it (auditi i plotësisë dhe matrica v1) ruhen private-local; opaque references; not reverified and not reproduced in this PR.** E njëjta vlen për pjesën tjetër të folderit dhe paketën parity të Codex-it (private-local, opaque, pa path/emra këtu).

Rregull: **nuk hapen, nuk editohen, nuk citohen vlera/rreshta prej tyre, nuk rishkruhet evidence historike** — vetëm ekzistencë me metadata. **Historical evidence remains opaque, not reread and not reproduced here.** Statuset e tyre: `EVIDENCE_REFERENCE_ONLY / NOT_REVERIFIED_IN_THIS_PR`.

## 8. Phase Usage — si përdoret baseline-i (i pandryshuar në parim)

| Faza | Përdorimi i PRO DATA |
|---|---|
| Faza 0 | data model + parity scoping + opening-balance scoping (**kryer**) |
| Accounting Core (F2) | rakordim **1:1** GL / libra / Deklaratë TVSH kundër baseline-it |
| Stok + COGS (F2/F3) | rikrijim COGS + cost layers kundër evidence historike |
| KABI (F5) | parity vetëm ku ka; **logjika dairy NUK inferohet nga PRO DATA** |
| PLANET POS/KDS (F7/F8) | parity operacional (porosi/floor-plan/queue) |
| SEF (F5) | parity i fiscal docs |
| UAT | test kundër baseline-it (numrat tanë duhet të përputhen) |
| Cutover (1 Jan 2027, F9) | saldo hapëse + kontroll final + delivery gates ADR-003 |

## 9. Scope i supersede-it (v2 mbi v1.1)

**v2 supersedes VETËM:** interpretimin **target** (çfarë do të thotë "mbulohet"), interpretimin e **migrimit** (tag-set §1A, jo vlera ekskluzive) dhe interpretimin **maturity/readiness** (§1B/§1C) — sipas **ADR-003** dhe canonical docs aktuale. **Nëse interpretimi historik bie ndesh me ADR-003 → ADR-003 fiton** (pa hapur evidence për ta verifikuar).

**v2 NUK supersede:** provenance-in e nxjerrjes, metrikat e nxjerrjes si **evidence historike**, as ekzistencën e artefakteve historike (§2, §7).

**Korrigjime stale nga v1.1 (before → after):**
- Burimi "(memory)" → **hequr**; burimet e v2 = canonical + tracked + metadata (header).
- Path private/non-portable i paketës Codex → **referencë opaque pa path** (§7).
- "Mbetet **një muaj real** për validim" → **stale**: `ACC-010` = **closed** (validuar me Maj 2026, muaj i deklaruar; vlerat ekzakte private-local; rounding-rule i hapur → `ACC-003`).
- "**Të gjitha** entitetet e konfirmuara janë shtuar" → zëvendësuar me **lista të verifikueshme** per-rresht (§3, kolona Proof).
- Çdo lexim "extraction complete ⇒ implementation" → **i pavlefshëm** (§0).
- "RESORT — pikë për konfirmim" → jeton si **`OPS-001` (OPEN)** në tracker (s'dyfishohet këtu).

## 10. Lidhjet

ADR-003 + delivery gates → `05_Decision_Gates_ADR.md` · saldo/cutover → `Cutover_Rollback_Runbook.md` · box-maps → `03_Accounting_Compatibility_Matrix.md` · RBAC → `02_RBAC_Matrix.md` · entitetet → `04_Data_Model_Draft.md` · statuset e PoC-ve → roadmap §4 · `ACC-004` (answered — parity scope) / `ACC-010` (closed) / `OPS-001` (OPEN) → tracker.

## Kontroll i planifikuar — Linter i Integritetit të Dokumentacionit dhe Pajtueshmërisë

**Emri funksional:** `Documentation & Compliance Integrity Linter`

**Status (effective on merge të PR-së tooling GOV-001):** **engine i përbashkët + `DOC-CORE` + `HISTORY-PRIV` = `IMPLEMENTED_ADVISORY`** (`packages/integrity-linter`; workflow `governance-advisory.yml` — advisory, JO-required) · **14 profilet e tjera = `NOT_IMPLEMENTED`** (asnjë profil `NOT_IMPLEMENTED` nuk raportohet PASS) · **NON-BLOCKING derisa profili përkatës të promovohet veçmas në required CI** (HG-LINTER-BUILD u shfrytëzua për këtë build; **HG-LINTER-REQUIRED mbetet i paaprovuar**) · **Output-i raporton vetëm `profile · severity · rule-id · locator i sigurt`; path i dyshuar privat → `[REDACTED_PATH]`; zero matched content**

**Prioriteti:** **HIGH-ROI**

**Porta e implementimit:** Human Gate për **ndërtimin e linter-it është APPROVED** (**HG-LINTER-BUILD**, 2026-07-12). Implementimi kryhet në **PR #21 — PR-ja tooling AKTUALE e GOV-001** (effective vetëm pas merge-it të saj), sipas rollout-it më poshtë. *(HISTORICAL v2.2–v2.5: "PR tooling/code të veçantë pas mbylljes së PR #20" — ai PR është pikërisht PR #21.)* Ky aprovim autorizon kodin/testet e linter-it, por **NUK** autorizon automatikisht promovimin në `required CI`, leximin e evidence-it private, thirrje fiskale apo ndryshim runtime të produktit. Çdo profil kalon fillimisht në `advisory`; promovimi i tij në `required` kërkon evidence kundër false-positive + review të pavarur + Human Gate të dytë (**HG-LINTER-REQUIRED**, i veçantë për secilin profil).

### Regjistri i profileve dhe fazat e implementimit

| ID | Profili | Çfarë mbron | Implementohet | Kandidat për `required` |
|---|---|---|---|---|
| `DOC-CORE` | Canonical Documentation & Governance | SSOT/roadmap/tracker/PM/INDEX; vocabulary; version/count/status drift; stale/current/superseded; PR body | **Menjëherë pas PR #20, para PR-së së parë të Step B** | Pas 2 PR-ve advisory pa false-positive kritik; pastaj aktiv në **F1–F10** |
| `HISTORY-PRIV` | Git History & Privacy Policy | per-commit added-lines; introduced-then-removed; private paths/filenames; identifiers/secrets; scope whitelist | Bashkë me `DOC-CORE`, **para Step B** | Para çdo PR-je që prek SEF/GL/security/migrime; zgjerim në të gjitha PR-të nga **F1** |
| `SEF-ATK` | ATK/SEF Normative Integrity | coverage nen/paragraf; sanksione; Source Register/Ledger/Gate; `T2-01…10`; Human Gate order; source drift | **PRE-Tier2, pas PR #20 dhe para mbylljes së Step B** | Para Human Gate të Tier 2 vetëm pas promotion-gate; gjithsesi required para **F5 freeze**, dhe për PLANET fiscal path në **F8/F9** |
| `SRC-CLOSURE` | PRO DATA Source→Target & Migration Closure | Treatment/Maturity; proof; parity; 7 delivery gates; cleansing/dedup/freeze/reconciliation/sign-off | Framework në **F1**; rregulla domain shtohen në **F2–F8** | Required për dry-run/cutover artifacts në **F9** dhe sign-off në **F10** |
| `ARCH-ADR` | Architecture, ADR & Dependency Integrity | ADR status/owner/deadline; dependency graph; module boundaries; vendim-vs-propozim; no duplicate authority | **F1** | Required për architecture freeze të çdo moduli nga **F2**; kontroll final në **F9** |
| `DB-MIG` | Database, Schema & Migration Integrity | tenant keys; constraints; append-only/immutability; migration IDs/order/idempotency; fresh apply/no-op; rollback/restore pointers | **F1** | Required për migrime production-grade në **F2**; freeze/cutover required në **F9/F10** |
| `SEC-TENANT` | Tenant Isolation, Identity, RBAC, Audit & Privacy | `business_id`; composite FK; deny-by-default; role verbs; 2FA/break-glass; audit; privacy gates; secret handling | **F1** | Required për çdo modul tenant-scoped nga **F1**; full security/privacy gate në **F9/F10** |
| `CI-SUPPLY` | CI, Dependencies & Supply-Chain Governance | required checks; lockfile/dependency scope; Dependabot policy; overrides review; provenance; secret scans | **F1** | Required për të gjitha PR-të pas stabilizimit advisory në **F1**; audit i plotë në **F9** |
| `FIN-ACCOUNTING` | Accounting, Tax & Financial Integrity | debit=kredit; draft-vs-approved; idempotency; period lock; AR/AP; VAT books; rounding; reversals; accountant gates | **F2** | Required për Accounting Core në **F2** dhe çdo integrim financiar në **F3–F10** |
| `INV-WMS-OMS` | Product, Inventory, WMS/OMS & Costing | article lifecycle; stock movement; LOT/FEFO; warehouse/vehicle; reservation/allocation; receipt/pick/ship boundaries; COGS/BOM | **F3** | Required për inventory/COGS në **F3**; zgjerohet për KABI **F4–F6** dhe PLANET **F7/F8**; reconciliation në **F9/F10** |
| `KABI-OPS` | KABI Dairy, QC, Traceability & Settlement | milk intake; sample/lab gate; antibiotic reject; tank/pool/batch; USAID settlement; AP/TVSH gates; recall/yield | **F4** | Required për KABI MVP në **F4/F5**; advanced controls në **F6**; UAT/cutover në **F9** |
| `OFFLINE-EDGE` | Offline Sync, Local Hub, Hardware & Reliability | outbox separation; idempotency; sequence/gap; retry; poison isolation; hub epoch; printer/UPS/LAN; recovery matrix | Bazamenti në **F1** nga PoC-3/4; profil production në **F5** | Required për KABI offline në **F5**, PLANET hub/POS në **F7/F8**, stress/DR në **F9/F10** |
| `PLANET-POS` | PLANET POS, KDS, FinanceOps & Peripherals | table/order lifecycle; station routing; printer fallback; KDS timers; business day; waiter/worker settlement; cash/card/bank reconciliation | **F7** | Required për PLANET Foundation në **F7**, live në **F8**, UAT/cutover në **F9/F10** |
| `UX-CLIENT` | Web, Mobile, UX/UI, Accessibility & Localization | role-based flows; offline/error states; keyboard/touch; tabular money; accessibility; sq/en terminology; device/layout coverage | Baseline në **F1** | Required për user-facing MVP-të në **F4/F5/F7/F8**; UAT në **F9**; regression në **F10** |
| `OBS-CAPACITY` | Observability, Performance, Capacity & SLO Evidence | health/log/error taxonomy; metrics/traces; queue age; load targets; latency/throughput; alert/runbook links; no untested SLA | Baseline në **F1** | Required për live-path capacity në **F5/F8**, load/stress në **F9**, monitoring/hypercare në **F10** |
| `CUTOVER-OPS` | Backup, DR, Cutover, Certification & Operations | backup/PITR/restore evidence; opening balances; UAT; training; Go/No-Go; rollback; handover; recertification/source drift | Backup baseline në **F1**; profile skeleton gjatë fazave | Required për **F9** hardening/pilot dhe **F10** launch/hypercare |

**Rregulli i fazimit:** framework-u nuk rindërtohet për çdo profil. `DOC-CORE` + engine-i i përbashkët ndërtohen një herë; çdo profil shton rules/fixtures/config para fazës së vet. Një profil i paimplementuar raportohet `NOT_IMPLEMENTED` — nuk konsiderohet implicitisht PASS. Faza nuk mbyllet nga linter-i vetëm: testet e produktit, evidence-i dhe Human Gate mbeten autoritetet e pranimit.

### Rregullat fillestare të detajuara — `SRC-CLOSURE`

Linter-i kontrollon këtë matricë dhe dokumentet e sinkronizuara për:

- vocabulary-n **Treatment** — vetëm `REQ/MD/OB/REF/NO` dhe kombinimet e lejuara;
- ndalimin e `IMPLEMENTED/TESTED` kur prova është vetëm PoC/simulator;
- qeliza të detyrueshme bosh, stage të përzier dhe overclaim;
- phase references kundër roadmap-it;
- ekzistencën e **7 delivery gates**: mapping · cleansing · dedup · freeze · dry-run · reconciliation · sign-off;
- version/status/count drift ndërmjet SSOT, roadmap, tracker, PM protocol dhe INDEX;
- ndalimin e leximit të evidence-it private.

### Rregullat fillestare të detajuara — `SEF-ATK`

Linter-i kontrollon mekanikisht dokumentet canonical ATK/SEF për:

- **coverage nen-për-nen/paragraf-për-paragraf**: çdo ID normative e corpus-it të regjistruar paraqitet saktësisht një herë me burim, faqe/locator, disposition dhe arsyetim jo-bosh;
- **referencat e sanksioneve**: çdo `SANCTION_CONTEXT` lidhet me kontrollin përkatës dhe çdo sanksion i deklaruar ka nen/faqe/burim;
- praninë reale dhe unike të **`T2-01`…`T2-10`** në Ledger, Gate dhe Tracker — pa orphan, boshllëk ose pretendim vetëm në header;
- version/count/status drift ndërmjet Source Register, Requirements Ledger, Compliance Gate, SSOT, roadmap, tracker, PoC-5 README/ANALIZA, INDEX dhe PR body;
- rendin e pandryshueshëm: **Step B → Step C → independent review → HUMAN GATE → private `/ca/verify` PREFLIGHT → pjesa tjetër e Tier 2**;
- klasifikime/status të ndaluara ose kontradiktore, përfshirë `LIVE placeholder = MISSING/T2-04`, jo `CONTRADICTS`;
- përdorimin e kontrolluar të `current`, `historical`, `superseded`, `complete/full/exhaustive` dhe pretendimeve `closes on merge`;
- sinkronizimin e metadata-ve të burimit (`version`, `last_verified_at`, affected IDs, cadence dhe source hash/status).

### Kufiri i detyrueshëm i automatizimit

Linter-i **NUK jep opinion ligjor dhe NUK vendos vetë semantikën** e një neni. Ai provon praninë, strukturën, referencat dhe konsistencën — jo nëse një nen është interpretuar drejt. Çdo ndryshim `IN_SCOPE ↔ N/A ↔ SANCTION_CONTEXT`, çdo source drift dhe çdo mbyllje gate kërkon **review njerëzor kundër burimit primar + Human Gate**. Linter-i:

- nuk lexon private-local evidence, `.env`, identifikues, payload, certifikata apo raw response;
- nuk bën thirrje `/ca/*`, `/pos/*` ose `/citizen/*`;
- nuk modifikon dokumente automatikisht;
- raporton vetëm `file · rresht · rule-id · kategori`, pa përmbajtje sensitive.

### Per-commit history scan

- Kontrollon **çdo commit në `base..HEAD` veçmas**, jo vetëm net-diff.
- Skanon added-lines për private paths/filenames, evidence metrics, operational-state claims, vocabulary të ndaluar, PoC→production promotion, mixed maturity, qeliza bosh, phase/version/gate drift dhe rend të gabuar të Human Gate.
- **Introduced-then-removed = FAIL**: një shkelje e shtuar në commit A dhe e hequr në commit B mbetet shkelje historike.
- Fixtures janë vetëm sintetike dhe riprodhojnë klasat e defekteve pa materiale private.

### Acceptance dhe promovimi në CI

PR-ja tooling e GOV-001 (**PR #21 — aktuale, effective on merge**) mban fixtures/tests për gabimet e gjetura në review-t e PR #19 dhe PR #20, përfshirë:

- PoC/simulator i promovuar gabimisht në `IMPLEMENTED/TESTED`;
- `Treatment` i pavlefshëm, qelizë bosh, delivery gate i munguar, phase/version/count drift;
- ID normative e munguar/dubluar, sanksion pa referencë, `T2-01…10` jo të plota;
- PR body ose dokument aktiv me status/version stale;
- preflight i vendosur para Human Gate;
- shkelje `introduced-then-removed` në historinë e branch-it.

Fillimisht ekzekutohet si **advisory check**. Bëhet **required CI** vetëm pas një periudhe prove me false-positive të matur, review të pavarur dhe **Human Gate të dytë (HG-LINTER-REQUIRED)**. Zero ndikim runtime; risku kryesor është false-positive nga parser shumë rigid.

### Acceptance shtesë — klasa rregullash nga review-t e PR #19/#20

**a) Old-value drift.** Kur ndryshon një count/version/status/set: vlera e re duhet të ekzistojë; vlera e vjetër duhet të mungojë nga të gjitha surfaces aktive; rreshtat e etiketuar qartë `HISTORICAL`/`SUPERSEDED` lejohen. Kërkimet janë të kualifikuara sipas dokumentit dhe kontekstit — jo grep i verbër mbi stringje si "v2.0"/"v2.1".

**b) Repeated N/A reason.** Arsye identike `N/A` të përsëritura në nene me tituj/përmbajtje të ndryshme prodhojnë **WARNING për review njerëzor**. Ky kontroll NUK vendos vetë që interpretimi është i gabuar dhe NUK ndryshon disposition automatikisht.

**c) Title/structure lock.** Titujt e neneve krahasohen mekanikisht me një fixture të versionuar, të nxjerrë nga burimi publik i ngrirë dhe të lidhur me source hash. Source hash/version drift → ndalim dhe review njerëzor. Linter-i nuk bën interpretim ligjor nga titulli.

**d) Role/privacy vocabulary.** Dokumentet canonical aktive përdorin vetëm role të aprovuara (Pronari, Human Gate, Group Owner, PM, Finance Owner, etj.). Emra personalë realë NUK vendosen në config, fixtures apo deny-list të komituar; testet përdorin vetëm identitete sintetike. Kandidati për emër personal prodhon **privacy-review flag** pa e publikuar përmbajtjen e dyshuar në log.

**e) Fixtures shtesë nga PR #19/#20** — përveç klasave të listuara më lart: stale old-value në surface aktive; status/count/version drift i pastër; rend i gabuar i Human Gate (përtej rastit preflight-para-gate); pretendim `closes on merge` pa review; titull neni i prerë.

Acceptance matet vetëm me fixtures/tests reale — pa vlerësime ilustruese në canonical. **Acceptance-i zbatohet sipas FAZIMIT të profilit:** klasat e mësipërme mbulohen nga fixtures sintetike të baseline-it advisory aty ku profili përkatës është implementuar; **fixtures/rregullat specifike `SEF-ATK` dorëzohen me profilin `SEF-ATK` (gjatë Step B, para mbylljes së tij)** — baseline-i nuk pretendon implementimin e tyre. Linter-i kontrollon strukturë dhe konsistencë; semantika ligjore mbetet review njerëzor + Human Gate.

*(**HISTORICAL — vlen për v2.2:** "Ky seksion vetëm regjistron kontrollin e planifikuar canonical. Asnjë script, config, workflow ose test nuk implementohet nga ky ndryshim dokumentimi." — **superseded nga v2.3:** engine + `DOC-CORE` + `HISTORY-PRIV` = IMPLEMENTED_ADVISORY në PR-në tooling GOV-001; 14 profilet e tjera mbeten NOT_IMPLEMENTED.)*
