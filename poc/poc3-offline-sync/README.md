# PoC-3 — Offline Sync (outbox → sync pa humbje, pa dublim) — raport evidence

**Burimi i specit:** `docs/canonical/01-phase0/01_PoC_Specs.md` (PoC-3) + SSOT §2 (offline-first + TRE rregullat e konfliktit) + roadmap PoC Track (ADR-011) + `Reliability_Gate.md` v1.1 (§5.1) + PM_REVIEW_PROTOCOL (checklist "Offline/Sync" — **aktive** për këtë PoC).
**Verdikti:** **PASS** — 13/13 teste të reja (suite 62/62): zero humbje, zero dublime, rend i garantuar nga `device_seq`, tri rregullat e konfliktit të provuara (R1 përfshirë sync KONKURRENT paralel), reliability matrix #1/#4/#5/#6/#7/#8 me teste.

> **KUFIJTË e PoC-3 (vendime të para-marra — nuk rihapen këtu):**
> - **ADAPTER ABSTRACTION, PA VENDOR:** `LocalStore` / `SyncTransport` / `ServerApply` = interface; implementimet janë PoC. **ADR-011 mbetet open (31.07)** — ky PoC është EVIDENCA e vendimit, jo vendimi. Vëzhgimet → §ADR-011 më poshtë.
> - **PA APP RN/EXPO (ADR-016):** "pajisja" = simulator në Node me store të PERSISTUAR në file (JSON), VETËM në temp dir (`<os-tmpdir>/poc3-store-…`) — kurrë në repo; DB lokale reale (SQLite/Realm) vjen me ADR-016.
> - **FISCAL OUTBOX = PoC-5, i VEÇANTË nga ky sync** (SSOT §7): radha e fiskalizimit SEF është e izoluar nga sync-u i përgjithshëm — parim që ky PoC **s'e prek fare**.
> - Emrat canonical (invoice/article/stock/partner) dizajnohen në F2/F3 — këtu VETËM tabela `poc3_`.

## Si riprodhohet

```bash
docker compose -f packages/database/docker-compose.yml up -d
export PGHOST=localhost PGPORT=5433 PGUSER=idata PGDATABASE=idata
pnpm --filter @idata/database build && pnpm --filter @idata/database migrate   # 0001..0004
pnpm --filter @idata/database test                                            # 37/37 (13 PoC-3)
```

## Çfarë u ndërtua

**Migrimi `0004_poc3_offline_sync.sql`** — tabela vetëm `poc3_`, `business_id NOT NULL` direkt kudo:
- `poc3_sync_event`: `device_seq bigint` monotonic per pajisje (**rendi NUK varet nga ora** — `occurred_at` informativ); `id bigserial` = rendi real i aplikimit; **UNIQUE (business_id, event_uuid)** = idempotencë; **UNIQUE (business_id, device_id, device_seq)** = **anti-dublim i `device_seq` në DB** — rendi aktual zbatohet nga **sync engine** (sortim sipas `device_seq`); **validimi server-side i rendit/gap-eve mbetet kërkesë për ADR-011**; `replay boolean`.
- `poc3_stock` (qty NUMERIC — **negativi lejohet**, R1) · `poc3_invoice` (UUID PK nga pajisja; status accepted/rejected_blocked) · `poc3_partner` (credit_limit/balance/blocked) · `poc3_price_variance`.

**Adapter-ët (ADR-011 input):** `LocalStore` (impl: `FileLocalStore` — JSON per pajisje në temp dir) · `SyncTransport` (impl: `InProcessTransport` me failpoints; `FlakyTransport` për retry) · `ServerApply` (impl: `ServerApplyPg`).

**Simulatori i pajisjes:** fatura OFFLINE me `crypto.randomUUID` + `device_seq` inkremental **i persistuar bashkë me outbox-in**; çmime **të ngrira** lokalisht; cap i konfigurueshëm i outbox-it; punon 100% pa server.

**Sync engine:** rendit sipas `device_seq`; **çdo event = NJË transaksion DB** (sync_event + invoice + stock + partner/block + variance BASHKË — dështim në mes = rollback i PLOTË i atij eventi); ACK/heqje nga outbox vetëm pas konfirmimit (resume-safe); retry me backoff eksponencial (delay i injektueshëm — pa sleep në teste); replay → skip + `replay=true`.

## TRE rregullat e konfliktit (SSOT §2) — të provuara

| Rregull | Sjellja e provuar | Test |
|---|---|---|
| **R1 STOK** | Dy pajisje shesin të njëjtin stok (7.00 + 8.00 nga 10.00) **PARALELISHT** (`Promise.all`) → të dyja pranohen; stok **-5.00** deterministik; zero dublime; variance report = stoku negativ; asnjë konflikt i pazgjidhur | ✓ (concurrent i detyrueshëm) |
| **R2 ÇMIM** | Çmimi ndryshon në server (5.00→6.00) gjatë offline → fatura pranohet **me çmimin e ngrirë 5.00** + rresht në `poc3_price_variance` (device=5.00, server=6.00, diff=-1.00) | ✓ |
| **R3 KREDI** | **Semantikë TRANSAKSIONALE, pa timer:** eventi tejkalues (120.00 > limit 100.00) → fatura **pranohet** DHE `blocked=true` **në të njëjtin tx** (partner `FOR UPDATE`); eventi PASUES kreditor (`device_seq` më i lartë) → **rejected_blocked**; cash pas bllokimit → lejohet (SSOT: "vetëm cash"); stoku i paprekur nga i refuzuari | ✓ (sekuencë e verifikuar, zero sleep) |

## Reliability matrix (moduli = KRITIK: sync/offline; N/A sipas §5.1 v1.1)

| Failure scenario | Expected behavior | Data safety guarantee | User-visible behavior | Recovery behavior | Automated/manual test | Evidence required |
|---|---|---|---|---|---|---|
| **#1 Internet down** | Pajisja punon 100% offline; faturat → outbox | Zero humbje: outbox i persistuar në file me `device_seq` | Fatura lëshohet normalisht; radha rritet | Sync sapo kthehet lidhja | **Automatik:** 5 fatura krejt offline → outbox 5, zero kontakt me server | test #1 PASS |
| **#4 Device crash/battery** | Rikrijim NGA FILE — asgjë nga memoria | Outbox + `device_seq` të paprekur pas crash | Pajisja rinis me radhën e plotë | Sync i plotë pas rikrijimit (5/5) | **Automatik:** vritet simulatori para sync, `loadFrom(file)` → sync | test #4 PASS |
| **#5 Sync interrupted** | Ndërprerje pas k nga n → resume ekzakt | **Secili event SAKTËSISHT një herë** (tx-boundary per event + ACK pas konfirmimit) | Gabim i qartë; radha ruhet | Resume nga gjysma; edhe rasti "apliku por pa ACK" → replay skip | **Automatik ×3:** cut para dërgimit (2/5→resume 3) · cut PAS aplikimit pa ACK (→replay, 0 dup) · failpoint BRENDA tx (→rollback i plotë i eventit, batch resumon) | teste #5/#5b/tx PASS |
| **#6 Server restart / replay** | Ri-dërgim i plotë i batch-it = no-op | **0 dublime** (UNIQUE event_uuid); stoku i pandryshuar nga replay | Replay i heshtur (skip + shënim) | Pajisja mund të ri-dërgojë lirshëm | **Automatik:** batch i plotë ×2 → të gjitha `replay`, counts + stok të pandryshuar, `replay=true` të shënuara | test #6 PASS |
| **#7 Server DB unavailable** | Gabim i pastër lidhjeje; outbox i paprekur | S'ka lidhje → s'ka shkrim; radha ruhet | "S'u lidh" — qartë | Retry me DB të rikthyer **ia del** | **Automatik:** pool port i gabuar BRENDA test scope → matcher specifik (ECONNREFUSED/timeout incl. AggregateError codes); outbox 1→sync i suksesshëm pas | test #7 PASS |
| **#8 Outbox full** | Cap i konfigurueshëm N; krijimi i ri **refuzohet me gabim të qartë** | **KURRË fshirje e heshtur** e eventeve të vjetra (janë dokumente) | "Outbox plot (cap N) — sync-o para se të vazhdosh" | Sync zbraz radhën → krijimi rifillon | **Automatik:** cap 3 → i 4-ti hidhet; outbox 3; i pari ende aty | test #8 PASS |
| #2 SEF/ATK down | **N/A** (§5.1) | — | — | — | — | *Justifikim:* fiskalizimi = **fiscal outbox i izoluar** (SSOT §7) → mbulohet nga **PoC-5**; ky sync s'flet me ATK. *Pranimi: review PM #7.* |
| #3 Printer/peripheral down | **N/A** (§5.1) | — | — | — | — | *Justifikim:* pajisjet periferike = shtresa edge/POS/hub → **PoC-4 / F7-F8**; motori i sync-ut s'flet me printera. *Pranimi: review PM #7.* |

## Logje sync-u + variance reports (evidencë reale, kode sintetike — pa path absolutë)

```text
== 1) Dy pajisje shesin të njëjtin stok (10.00) PARALELISHT: A=7.00, B=8.00 ==
   dev-demo-a sync: applied=1 replayed=0 (event seq=1)
   dev-demo-b sync: applied=1 replayed=0 (event seq=1)
== 2) Replay i plotë (të dy eventet ri-dërgohen) ==
   dev-demo-a event -> replay
   dev-demo-b event -> replay
== 3) R2: çmimi ndryshon në server 5.00 -> 6.00 gjatë offline ==
   fatura u pranua me çmimin e NGRIRË 5.00; variance u regjistrua
== 4) R3: kredi 10×12.00=120.00 > limit 100.00; pastaj kredi tjetër; pastaj cash ==
   fatura seq1: accepted
   fatura seq2: rejected_blocked
   fatura seq3: accepted

== RAPORTI I VARIANCE-s SË STOKUT (R1) ==
   art-demo-01: qty=-7.00  (10.00 - 7.00 - 8.00 - 2.00 = -7.00)

== RAPORTI I VARIANCE-s SË ÇMIMIT (R2) ==
   art-demo-01: device=5.00 server=6.00 diff=-1.00

== PARTNERI I BLLOKUAR (R3) ==
   prt-demo-credit: limit=100.00 balance=120.00 blocked=true

== SYNC EVENTS (rendi i aplikimit sipas device_seq; replay flags) ==
   dev-demo-a seq=1 replay=true
   dev-demo-b seq=1 replay=true
   dev-demo-p seq=1 replay=false
   dev-demo-k seq=1..3 replay=false
```

## Vëzhgime — INPUT për ADR-011 (PowerSync vs custom; PA vendim)

1. **Çfarë doli e LEHTË me custom (outbox + idempotencë në DB):** garancitë kryesore (0 dublime, rend, resume) erdhën nga **dy UNIQUE constraints + tx-boundary per event** — pak kod, i testueshëm plotësisht, pa magji vendor-i.
2. **Çfarë do KUSHTOJË me custom në shkallë reale:** delta-sync dy-drejtimësh (server→pajisje), kompaktimi i outbox-it, schema evolution e payload-eve, backpressure për 60-80 pajisje — këto janë pikërisht ato që një vendor (PowerSync) i jep gati.
3. **Adapter-ët funksionuan:** `LocalStore`/`SyncTransport`/`ServerApply` ndajnë pastër pajisjen nga serveri — një vendor mund të futet si implementim i `SyncTransport`/`LocalStore` **pa prekur** rregullat e konfliktit server-side (ato mbeten tonat në çdo skenar).
4. **Rendi:** `device_seq` monotonic + `UNIQUE (device, seq)` në server e bëri rendin të pavarur nga ora e pajisjes — kusht që duhet t'i imponohet edhe një vendor-i.
5. **R3 transaksionale** kërkoi `FOR UPDATE` mbi partner — logjika e konflikteve me semantikë financiare **duhet të jetojë në server** (jo në shtresën sync), pavarësisht vendimit të ADR-011.
6. **Poison-seq nga backup i vjetër i pajisjes:** nëse pajisja rikthehet nga backup i vjetër dhe **ripërdor një `device_seq`** me UUID të ri → `UNIQUE (device_id, device_seq)` violation → **outbox-i bllokohet me zë** (asgjë s'aplikohet në heshtje — sjellje e sigurt, por bllokuese). **Procedura e recovery-t (re-numërim / kthim seq nga serveri / re-regjistrim pajisjeje) duhet të vendoset në ADR-011.**

## PASS kundër kritereve

| Kriter | Rezultat |
|---|---|
| 5 fatura offline → të 5-ta në server, UUID unike, zero humbje | **PASS** (#1 + #4: outbox 5 → server 5) |
| Replay i dyfishtë → 0 dublime; ndërprerje → resume ekzakt | **PASS** (#6, #5, #5b, tx-boundary) |
| Rendi sipas `device_seq` me `occurred_at` qëllimisht të çrregullt | **PASS** (apply order = seq, jo ora) |
| CONCURRENT 2 pajisje paralel → deterministik, 0 dublime | **PASS** (stok -5.00; 2 accepted) |
| R1 negativ+variance · R2 çmim i ngrirë+variance · R3 blocked transaksional + pasuesi refuzohet | **PASS ×3** |
| Device crash → rikrijim nga file → outbox i paprekur | **PASS** (#4) |
| Outbox cap → sjellje e definuar | **PASS** (#8) |
| Migrimi 0004 fresh + re-run no-op; suite --force jeshile | **PASS** (`applied=4`→`applied=0`; **62/62** në 5 workspaces) |

## Kufizime të njohura

- DB lokale reale (SQLite/Realm) + RN/Expo → **ADR-016**; ky simulator provon vetëm semantikën outbox/seq/persistencë.
- **`FileLocalStore.save` përdor `writeFileSync` — JO shkrim atomik** (crash në mes të shkrimit mund të korruptojë JSON-in e store-it); zgjidhja reale (tmp+rename+fsync ose SQLite transaksionale) mbetet **ADR-016**.
- Vendor sync (PowerSync) vs custom → **ADR-011 (open, 31.07)**; ky PoC = evidenca.
- Fiscal outbox (SEF, 48h) = **PoC-5**, radhë e izoluar — e paprekur këtu (SSOT §7).
- Server→pajisje sync (çmime të reja, katalog) = jashtë PoC (vetëm outbox pajisje→server); katalogu i çmimeve të serverit në PoC = config i adapterit (tabela canonical e çmimeve = F2/F3).
- `occurred_at` ruhet si ora e pajisjes (audit "timestamp real" i specit); rendi transaksional = `device_seq`.
