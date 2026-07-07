# PoC-2 — Accounting Posting (dokument → GL → libra ATK) — raport evidence

**Burimi i specit:** `docs/canonical/01-phase0/01_PoC_Specs.md` (PoC-2) + `04_Data_Model_Draft.md` §C (emrat e tabelave) + `02-accounting/` (Posting Rules, VAT_Book_Mapping, FX-005) + roadmap PoC Track + `Reliability_Gate.md`.
**Verdikti:** **PASS** — 14/14 teste të reja; debit=kredit i zbatuar edhe në DB (trigger DEFERRABLE); box-map i saktë; draft s'poston; idempotencë + period-lock + failure matrix #5/#6/#7 të provuara.

> **KUFIRI I PoC-2 (guardrail):** PoC-2 = **motori kontabël + strukturat e librave; JO certifikim fiskal, JO format zyrtar Excel.**
> PoC-2 **nuk certifikon SEF** dhe **nuk pretendon përputhje 100% me formatet zyrtare Excel të ATK-së**. Prodhon strukturën e brendshme të librave (**header→buckets**) dhe mapping-un e box-eve me **data sintetike**.
> - Përputhja finale me template-t zyrtare Excel të ATK-së mbyllet në **"ATK VAT Excel Export Compliance Pack"** — deliverable i **Fazës 2**, i validuar me kontabilistin (lidhet me ACC-011).
> - Gjithçka SEF (ApplicationId, ECDSA, protobuf, kupona, money-scaling ×10000/cents, test-server ATK) = **PoC-5 dhe VETËM PoC-5**.
> - Parity kundër vlerave reale PRO DATA (ACC-010) **s'bëhet këtu** — vetëm private-local dhe vetëm me kërkesë të veçantë të Blendit; parity i plotë = Faza 2.

## Si riprodhohet

```bash
docker compose -f packages/database/docker-compose.yml up -d
export PGHOST=localhost PGPORT=5433 PGUSER=idata PGDATABASE=idata
pnpm --filter @idata/database build && pnpm --filter @idata/database migrate   # 0001+0002+0003
pnpm --filter @idata/database test                                            # 23/23 (14 PoC-2)
```

## Çfarë u ndërtua (migrimi `0003_accounting_posting.sql`)

Tabelat me **emrat EKZAKTË të Data Model v2.2.1 §C**: `journal_entry`, `journal_line`, `tax_book_entry` (HEADER), `tax_book_bucket`, `partner_card_entry`, `fiscal_period`, `posting_run`, `posting_event` (me `idempotency_key` UNIQUE per business — "një burim → një postim").

Invariantet nga dita zero:
- `business_id` **NOT NULL DIREKT** në çdo tabelë (rule==model, edhe `journal_line`/`tax_book_bucket`).
- **Composite FK** (modeli ENG-003): `journal_line (business_id, journal_entry_id) → journal_entry (business_id, id)`; njësoj `tax_book_bucket → tax_book_entry`; edhe `posting_event→posting_run`, `journal_entry→posting_event/fiscal_period`, `tax_book_entry→fiscal_period/posting_event`.
- `journal_entry`/`journal_line` **APPEND-ONLY** (trigger si `audit_log`; korrigjimi = reversal) — testuar.
- **DEBIT=KREDIT**: i zbatuar në service layer + **CONSTRAINT TRIGGER DEFERRABLE** në DB (verifikim në COMMIT; jo CHECK kolone — PostgreSQL s'bën aggregate në CHECK) — testuar që entry i pabalancuar rrëzohet në commit.
- Shumat: **NUMERIC**; gjithë aritmetika monetare në SQL `numeric` (asnjë float/JS number).

## Tre dokumentet e postuara (të tria të detyrueshme)

| Dokument | GL | Libri | Bucket |
|---|---|---|---|
| (i) Faturë shitje 18% (net 100.00) | Dr AR 118.00 · Cr Të ardhura 100.00 · Cr TVSH output 18.00 | shitje | `[12]` 18% 100.00/18.00 |
| (ii) Blerje MIXED 18%+8% (FX-005: 100.00 + 50.00) | Dr shpenzim 100+50 · Dr TVSH input 18.00+4.00 · Cr AP **172.00** (një gross) | blerje | `[43]` 100.00/18.00 **+** `[45]` 50.00/4.00 (2 buckets në një header) |
| (iii) Akt-blerje fermeri (net 500.00, flat 8%) | Dr shpenzim 500.00 · Dr TVSH flat **PENDING** 40.00 · Cr AP fermer 540.00 | blerje | `[51]` flat-8 500.00/40.00 · `tax_book_entry.status = pending_accountant_signoff` (ACC-006) |

Akti i fermerit kalon përmes `posting_run`: **DRAFT → refuzohet me "draft NUK poston GL/AP"** (test: zero event/GL/AP/kartelë) → **approved → poston** (Data Model §C :54).

## Eksportet e librave (vlera SINTETIKE — file-t në këtë folder)

```text
Libri i Shitjes  : [12] bazë 18% = 200.00 (2 shitje×100) · [30] TVSH totale = 36.00
Libri i Blerjes  : [43] = 100.00 · [45] = 50.00 · [51] = 500.00 · [67] = 22.00
```
- **[67] = v18 + v8 = 18.00 + 4.00 = 22.00 EKZAKT** (barazi e provuar me `numeric` në SQL, jo float).
- **[67] NUK përfshin TVSH-në flat të fermerit ([51]=40.00)**: deductibility = *pending accountant sign-off* (ACC-006) — konsistent me llogarinë PENDING në GL. Pas sign-off, trajtimi finalizohet.
- **[51] merr VETËM akte fermeri** (test: `DISTINCT doc_type` në [51] = `AKT_FERMER`).
- File-t: `libri_shitjes_sintetik.{json,csv}` · `libri_blerjes_sintetik.{json,csv}`. *(CSV me delimiter `;` — praktikë EU; shmang edhe false-positive të skanerëve të shumave ku presja e CSV ngjitet me numrat.)*

## Dual-variant (ADR-012 INPUT — PA vendim)

I njëjti postim (shitje 18%) u ekzekutua në **dy mënyra** me **rezultat identik** (test i barazisë së plotë të rreshtave GL + buckets):
1. **Transaksion app-level** (`postSale18App`, pg client, BEGIN/COMMIT eksplicit)
2. **Funksion DB** (`poc2_post_sale18`, plpgsql)

**Vëzhgime (input për ADR-012, jo vendim):**
- *Atomiciteti:* funksioni DB = transaksion i vetëm implicit (më e vështirë ta "harrosh"); app-level kërkon disiplinë BEGIN/COMMIT/ROLLBACK (e zbatuar me try/catch + failpoint testi e provon).
- *Testueshmëria:* app-level testohet/instrumentohet lehtë nga TS (failpoints, mocks); plpgsql kërkon teste SQL të veçanta dhe debugging më i rëndë.
- *Config-u:* varianti DB i **dublon kodet e llogarive në SQL** — konfigurimi duhet versionuar/deploy-uar veçmas nga app-i (rrezik divergjence; u desh dublim i qëllimshëm edhe në këtë PoC).
- *Gabimet:* plpgsql jep SQLSTATE/RAISE të qarta; app-level jep stack TS më të pasur për diagnozë.
- *Race idempotencë:* të dy variantet mbrohen përfundimisht nga `UNIQUE (business_id, idempotency_key)` në DB (check-then-insert i të dyve ka teorikisht race — unique-u është garanci finale).

## Reliability Gate — failure matrix (moduli = KRITIK: GL/postim)

| Failure scenario | Expected behavior | Data safety guarantee | User-visible behavior | Recovery behavior | Automated/manual test | Evidence required |
|---|---|---|---|---|---|---|
| **#5 Sync interrupted / rollback në mes** | Transaksioni rrëzohet i plotë | **Ose gjithçka, ose asgjë** — zero gjendje gjysmake (pa event/GL/kartelë të pjesshme) | Gabim i qartë; dokumenti mbetet i papostuar | Ri-tentim me të njëjtin `idempotency_key` | **Automatik, 2 failpoints**: (a) herët — pas `journal_entry`; (b) vonë — pas TË GJITHA shkrimeve, PARA COMMIT → rollback i plotë i verifikuar në **TË 6 tabelat** (posting_event, journal_entry, journal_line, tax_book_entry, tax_book_bucket, partner_card_entry) | 2 teste `reliability #5` PASS |
| **#6 Server/hub restart / replay** | Ri-ekzekutimi i të njëjtit event = no-op | **0 dublime** (`UNIQUE (business_id, idempotency_key)` + një entry per event) | Replay kthen entry-n ekzistues | Radha ri-dërgon lirshëm; postimi idempotent | **Automatik**: replay i 3 dokumenteve → `replay=true`, count i pandryshuar | test `reliability #6` PASS |
| **#7 DB unavailable** | Gabim i pastër i lidhjes; asnjë shkrim i pjesshëm | S'ka lidhje → s'ka shkrim fare | Gabim i qartë "s'u lidh" | Ri-tentim kur DB rikthehet (idempotent) | **Automatik**: pool me port të gabuar BRENDA test scope (container-i i përbashkët s'preket) → **matcher SPECIFIK** gabimi lidhjeje (ECONNREFUSED/timeout/unreachable, incl. AggregateError codes — jo çfarëdo error-i) + zero rows në posting_event **dhe** journal_entry të verifikuara përmes pool-it të shëndoshë | test `reliability #7` PASS |
| #1 Internet down | **N/A për këtë modul** | — | — | — | — | *Justifikim:* motori i postimit punon kundër DB-së lokale/hub; WAN-i është shqetësim i shtresës sync/outbox (PoC-3), jo i motorit |
| #2 SEF/ATK down | **N/A** | — | — | — | — | *Justifikim:* fiskalizimi = fiscal outbox i izoluar (PoC-5); postimi kontabël s'varet nga ATK |
| #3 Printer/peripheral down | **N/A** | — | — | — | — | *Justifikim:* shtresë edge/POS (PoC-4/F7-F8); motori s'flet me pajisje |
| #4 Device crash/battery | **N/A** | — | — | — | — | *Justifikim:* pajisjet mobile prekin outbox/sync (PoC-3); motori merr evente të radhitura |
| #8 Disk/queue full | **N/A këtu** | — | — | — | — | *Justifikim:* kapaciteti i outbox/fiscal outbox matet te PoC-3/PoC-5 (targets te `Reliability_Gate.md` §7); disku i DB-së = ADR-023 backup/PITR |

## PASS/FAIL kundër kritereve

| Kriter | Rezultat | Evidencë |
|---|---|---|
| Debit = Kredit në ÇDO posting (+ trigger deferrable) | **PASS** | query 0 entries të pabalancuara; commit i pabalancuar rrëzohet |
| TVSH = normë × bazë decimal; FX-005 → 2 buckets | **PASS** | [43] 100/18 + [45] 50/4 në një header; AP gross 172.00 |
| Kutitë [43]/[45]/[51]/[67] + [12]/[30]; [67]=v18+v8 ekzakt | **PASS** | barazi `numeric` në SQL; [67]=22.00 |
| Akt fermeri → [51]; draft → 0 GL/AP; approved → poston | **PASS** | test draft→reject/zero; approve→GL+[51]+PENDING |
| Idempotencë (replay = 0 dublime) + period lock | **PASS** | replay ×3 no-op; periudha e mbyllur refuzon me zero gjurmë |
| Dual-variant rezultat identik | **PASS** | barazia e lines+buckets app vs plpgsql |
| Failure matrix #5/#6/#7 me teste; N/A të argumentuara | **PASS** | tabela më lart; 3 teste automatike |
| Migrimi 0003 fresh + re-run no-op; suite --force jeshile | **PASS** | applied=3 → applied=0; 5 workspaces jeshile |

## Kufizime & shënime të njohura

- **Konflikt emërtimi (raportuar, JO i zgjidhur këtu):** Posting Rules përdor `partner_ledger_entry`; VAT_Book_Mapping përdor `tax_book_entry_bucket` — **Data Model v2.2.1 fiton** (`partner_card_entry`, `tax_book_bucket`); dokumentet harmonizohen me docs-PR pas PoC-2.
- **Kodet e llogarive = config provisional** (1210/4010/4015/5010/6010/4710/4720/4725 — të njëjtat placeholder me `packages/accounting/src/accounts.ts`); COA final = ACC-003 (open). Dublimi DB-fn ↔ TS = vëzhgim ADR-012.
- **Rounding**: `round()` i Postgres `numeric` (half-up) në nivel rreshti; rregulli FINAL = Q#4b/ACC-003 (open) — vlerat e PoC s'kanë raste kufitare; tolerenca dokumentohet kur të vijë përgjigja.
- **Kolona (nën-model, s'prekin emrat e tabelave):** `date`→`entry_date`; `account_id`→`account_code` (s'ka tabelë `account` ende); `partner_id`/`invoice_id` pa FK (partner master/invoice = Faza 1).
- AR/AP subledger i plotë (open_item/payment_allocation/aging), period-close workflow, reversal/credit-note flow, UI = **jashtë** PoC-2.
