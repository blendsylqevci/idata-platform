# PoC-4a — Local Hub SEMANTIKA (Docker) — raport evidence

> ## ⚠️ STATUSI: **PoC-4a = PARTIAL**
> **PoC-4 PASS FINAL shpallet VETËM pas 4b në hardware** (Mini-PC real + power-pull + UPS + LAN fizik).
> 4a provon **semantikën** (routing, fallback, crash-recovery të procesit, sync idempotent) në Docker në këtë makinë — **jo** rrymën reale, **jo** latencën LAN, **jo** pajisjet fizike.

**Burimi i specit:** `docs/canonical/01-phase0/01_PoC_Specs.md` (PoC-4) + SSOT §2 (hub) + §6 (KDS/printer fallback ~30s) + roadmap PoC Track (ADR-013) + `Reliability_Gate.md` v1.1 (§5.1).
**Verdikti 4a:** **PASS (PARTIAL)** — 9/9 teste të reja (suite 71/71): cikli porosi→KDS me cloud OFF, routing i saktë + default-flag, fallback printeri, **kill -9 + restart me WAL recovery zero-humbje**, sync hub→cloud saktësisht-një-herë, radhë me cap.

## Vendimet e para-marra — respektuar

- **Ndarja 4a/4b ZYRTARE**: kjo është 4a; 4b vjen me hardware gjatë javës (checklist më poshtë).
- **HubStore prapa interface** (plan-B i ADR-013, deadline 10.08): GJITHË qasja e hub-it përmes `HubStore`; implementimi PoC = `PgHubStore` (Postgres në container të veçantë). Vëzhgimet Pg-vs-SQLite → §ADR-013 më poshtë — **pa vendim**.

## Arkitektura e 4a

```text
"pajisja e kamarierit" (simulator i hollë)
        │ createOrder (in-process API)
        ▼
  HUB = Postgres i DYTË në Docker (container poc4-hub, port 5434,
        compose i VEÇANTË docker-compose.poc4.yml — 5433 "cloud" S'PREKET)
        ├─ routing artikull→stacion → poc4_kds_ticket (demo-grill / demo-bar)
        ├─ i pa-mapuar → stacion default + flagged (sjellje e definuar)
        ├─ poc4_fallback_queue ("printeri" i simuluar; afati ~30s → config 2s në test)
        └─ poc4_outbox_cloud (hub_seq bigserial + event_uuid — semantika e PoC-3)
        │ ToggleCloudTransport (interneti ON/OFF; failpoints si PoC-3)
        ▼
  CLOUD (5433): poc4_cloud_event (UNIQUE event_uuid = idempotencë;
        UNIQUE hub_id+hub_seq = anti-dublim) + poc4_cloud_order
```

- Skema e hub-it = `packages/database/poc4-hub-schema.sql`, e aplikuar nga **harness-i i PoC** — **qëllimisht JASHTË zinxhirit të migrimeve** (hub-i mund të dalë SQLite me ADR-013; zinxhiri 000X = i cloud-it).
- Migrimi **0005** (cloud, zinxhiri normal): vetëm `poc4_cloud_*`; emrat canonical (order/table/kds_ticket) = **F7/F8**.
- Testet menaxhojnë lifecycle-in **VETËM** të containerit `poc4-hub` (up/kill/restart përmes compose-it poc4) — **kurrë** containerin kryesor 5433 (përjashtim i kontrolluar; CI e ngre vetë hub-in pa prekur `ci.yml`).

## Si riprodhohet

```bash
docker compose -f packages/database/docker-compose.yml up -d          # cloud 5433
export PGHOST=localhost PGPORT=5433 PGUSER=idata PGDATABASE=idata
pnpm --filter @idata/database build && pnpm --filter @idata/database migrate  # 0001..0005
pnpm --filter @idata/database test    # 46/46 — poc4-hub (5434) ngrihet vetë nga harness-i
```

## Reliability matrix (moduli = KRITIK: hub lokal; kolona "Provohet në" = 4a ose 4b)

| Failure scenario | Expected behavior | Data safety guarantee | User-visible behavior | Recovery behavior | Automated/manual test | **Provohet në** | Evidence |
|---|---|---|---|---|---|---|---|
| **#1 Internet down** | Cikli i PLOTË porosi→KDS punon me cloud OFF | Porositë + tickets + outbox në hub-DB lokale (WAL) | Kamarieri punon normalisht; radha e sync-ut rritet | Sync sapo rikthehet interneti | **Automatik:** 10 porosi nga 2 pajisje → stacionet e sakta (5 grill + 5 bar); cloud = 0; sync me OFF → gabim i pastër, pending i paprekur | **4a ✓** (LAN fizik = 4b) | test #1 PASS |
| **#3 Printer/stacion s'konfirmon** | Ticket i pa-konfirmuar brenda afatit (~30s → config 2s në test) → **fallback queue** + flamur | Ticket-i s'humbet kurrë — shkon në radhën e "printerit" | Backup printer merr ticket-in; flamur për shqyrtim | Stacioni rikthehet → tickets e reja normale | **Automatik:** backdate 3s → sweep(2s) → vetëm i pa-konfirmuari në queue; i konfirmuari JO | **4a ✓** *(kufiri: printer FIZIK + routing real = 4b/F7)* | test #3 PASS |
| **#5 Sync hub→cloud i ndërprerë** | Cut pas k nga n → resume ekzakt | Saktësisht-një-herë (event_uuid + ack pas konfirmimit) | Gabim i qartë; radha ruhet | Resume në sync-un e ardhshëm | **Automatik:** cut pas 1/3 → pending 2 → resume → +3 në cloud, 0 dublime | **4a ✓** | test #5 PASS |
| **#6 Hub restart me radhë jo-bosh** | `docker kill -9` **me radhë JO-bosh (lidhjet e mbyllura para kill — crash mbi WAL me 13 porosi të pashkruara në cloud)** → WAL recovery | **Zero humbje**: orders + tickets + outbox IDENTIKE para/pas; sync resumon | Hub rikthehet; pajisjet rilidhen | Restart → gjendja e plotë nga WAL | **Automatik:** guard para kill (orders>0 ∧ tickets>0 ∧ pending>0); counts para = counts pas kill -9 + restart; sync i mëpasshëm i plotë | **4a ✓** — **SHËNIM I NDERSHËM: kill -9 ≈ crash procesi me lidhje të mbyllura; kill GJATË shkrimit aktiv + fikja REALE e rrymës me UPS/disk barriers = 4b power-pull — s'e zëvendëson softueri** | test #6 PASS |
| **#7 Hub DB i paarritshëm nga pajisja** | Gabim i pastër lidhjeje; asnjë shkrim | S'ka lidhje → s'ka gjysmë-porosi | "S'u lidh me hub-in" | Retry me hub të kthyer → OK | **Automatik:** matcher specifik (ECONNREFUSED/timeout incl. AggregateError codes); retry OK | **4a ✓** | test #7 PASS |
| **#8 Radha e hub-it plot** | Cap i konfigurueshëm; porosi e re → **refuzim i qartë** | **Zero fshirje të heshtura** (radha = dokumente); porosia e refuzuar s'lë gjurmë (tx rollback) | "Radha plot — pritet sync me cloud" | Sync zbraz radhën → krijimi rifillon | **Automatik:** cap 2 → e 3-ta refuzohet; pas sync → krijimi OK | **4a ✓** | test #8 PASS |
| #2 SEF/ATK down | **N/A** (§5.1) | — | — | — | — | — | *Justifikim:* fiskalizimi = fiscal outbox i izoluar (SSOT §7) → **PoC-5**; hub-i s'flet me ATK. *Pranimi: review PM #7.* |
| #4 Device crash/battery | **N/A** (§5.1) | — | — | — | — | — | *Justifikim:* modeli i pajisjes (outbox+seq+file persistencë) u provua në **PoC-3**; këtu pajisja = simulator i hollë, fokusi = hub/LAN. *Pranimi: review PM #7.* |
| **LATENCA (<2s LAN)** | — | — | — | — | Proxy in-process i matur: **~1.5 ms** porosi→ticket i dukshëm | **VETËM 4b** — proxy raportohet **pa e quajtur përmbushje** të kriterit LAN | log në test |

## Kufiri "KURRË 5433" — evidencë e provuar

Compose-i poc4 ka **`name: poc4`** (project-name i fiksuar) → harness-i s'mund ta prekë project-in e compose-it kryesor as me `COMPOSE_REMOVE_ORPHANS`. Prova reale (suite e plotë `--force` **2× radhazi mbi të njëjtat volume**):

```text
GREP harness/teste: asnjë 'compose down' / '--remove-orphans' askund —
  composeCmd përdor VETËM 'up -d' dhe 'kill -s SIGKILL hub' mbi compose-in poc4.

docker ps PARA:  ID=59a1cae3648b NAME=idata-postgres-dev STATUS=Up (healthy)
  SUITE RUN 1/2: Tests 47 passed (47)  · Tasks 8 successful
  SUITE RUN 2/2: Tests 47 passed (47)  · Tasks 8 successful
docker ps PAS:   ID=59a1cae3648b NAME=idata-postgres-dev STATUS=Up (healthy)
→ i NJËJTI container ID = 5433 i PAPREKUR; harness-i menaxhoi vetëm project poc4.
```

*(Shënim kalimtar: futja e `name: poc4` kërkoi një pastrim një-herësh lokal të containerit të vjetër poc4-hub të krijuar nën project-name-in e mëparshëm — CI s'preket, gjithmonë i pastër.)*

## Vëzhgime — INPUT për ADR-013 (Postgres vs SQLite në hub; PA vendim)

1. **Çfarë do NDRYSHONTE me SQLite:** vetëm `PgHubStore` (një klasë) — interface-i `HubStore` (createOrder/confirmTicket/sweepToFallback/pendingOutbox/markSynced) mbetet identik; routing/fallback/sync s'preken. Kjo është pikërisht ajo që plan-B i ADR-013 kërkonte të provohej.
2. **Çfarë na dha Postgres-i falas në 4a:** WAL crash-recovery (kill -9 → zero humbje, e provuar), transaksione të plota per porosi (order+items+tickets+outbox bashkë), `bigserial` për hub_seq. Me SQLite: WAL mode ekziston por semantika e durimit të rrymës varet nga `synchronous`/fsync — duhet provuar në 4b-hardware nëse ADR-013 anon nga SQLite.
3. **Kosto e Postgres në hub:** container + RAM në Mini-PC; SQLite = zero-ops. Ky trade-off matet realisht vetëm në 4b (hardware profile).
4. **Backup i hub-it**: me Postgres → pg_dump/PITR lokale; me SQLite → file copy me WAL checkpoint. Vendimi ndikon procedurën e F7/F8 — të shënohet në ADR-013.
5. **hub_seq pas rindërtimit të hub-it (store-swap):** nëse hub-i rindërtohet nga zero me të njëjtin `hub_id`, `hub_seq` (bigserial) **rifillon** → përplaset me `UNIQUE (hub_id, hub_seq)` në cloud për seq të vjetra tashmë të përdorura. Zgjidhja finale kërkon **hub-epoch/generation** (p.sh. `hub_id + epoch` ose seq i dhënë nga cloud në regjistrim) — **skenar që ADR-013 duhet ta mbulojë**. *(E goditur REALISHT gjatë zhvillimit të 4a: hub-volume u rikrijua, cloud jo → sync u bllokua me zë nga UNIQUE — sjellje e sigurt, por bllokuese; konfirmon nevojën e epoch-ut.)*

## Plani i 4b (checklist për ditën kur vjen hardware)

- [ ] Mini-PC/NUC me Docker + i njëjti compose poc4 (ose Postgres nativ) — të njëjtat teste kundër tij nga një laptop në LAN.
- [ ] **Power-pull test**: hiq rrymën fizikisht NË MES të krijimit të porosive (pa UPS) → verifiko WAL recovery + zero korruptim; përsërit ×5.
- [ ] **Power-pull GJATË shkrimit aktiv** (kill-i i 4a ishte me lidhje të mbyllura — crash-i gjatë INSERT-eve aktive provohet VETËM këtu me rrymë reale).
- [ ] **UPS runtime i matur**: sa minuta mban hub-in; dokumento.
- [ ] **apcupsd/NUT graceful shutdown**: UPS në baterinë e fundit → shutdown i pastër i containerit/OS.
- [ ] **LAN latency <2s (kriteri i specit)**: 2 pajisje reale (telefon/tablet) në switch → porosi→ticket i dukshëm në ekran KDS; matje me kohë reale.
- [ ] 10 porosi nga 2 pajisje reale me WAN të fikur fizikisht (skenari i specit 1:1).
- [ ] Rikthim WAN → sync → 0 dublime (rikonfirmim në hardware).
- [ ] Vëzhgimet SQLite-vs-Pg të 4b → mbyllja e ADR-013 (deadline 10.08).

## Kufizime të njohura

- **PARTIAL**: gjithçka këtu = semantikë në Docker; rryma reale/UPS/LAN/pajisjet fizike = **4b**.
- Routing artikull→stacion = **config në kod** të PoC (tabela reale e routing + floor-plan = F7/F8, FIN-003).
- "Pajisja" = simulator i hollë in-process (API rrjeti reale e pajisjes → F7; modeli offline i pajisjes u provua në PoC-3).
- Statuset e plota KDS (blink/red 2min, Expo, Queue Monitor) = F8 — këtu vetëm `e_re→…→konfirmuar` minimal.
- Hub-i mban `business_id NOT NULL` direkt por **pa FK** (s'ka tabelën business lokalisht; lidhja verifikohet në cloud).

**Shënime për F7/F8 (dizajni real, jo ky PoC):**
- **Cap-i i radhës = COUNT-then-INSERT — JO concurrency-safe** (dy `createOrder` paralele mund ta kalojnë të dyja kontrollin nën cap): mjafton për PoC me pajisje serike; dizajni F7/F8 kërkon lock/constraint real.
- **`poc4_station` s'ka `business_id`** (config PoC me 2 stacione demo): tabela reale e stacioneve/routing në F7/F8 = **tenant-scoped** me `business_id` si kudo.
