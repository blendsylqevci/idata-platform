# iData+ — Cutover & Rollback Runbook

**Status:** Draft PM/Architecture v0.4 për review · **Data:** 2026-07-07 · **Përditësuar:** 2026-09-03 (ADR-030 = ACCEPTED / EFFECTIVE me squash-merge të PR #31 `42993f0`; §1.6 kufiri i pilotit PLANET-first sipas ADR-030 ACCEPTED / EFFECTIVE; klasifikimi i rollout-it te §6) · *(v0.3, 2026-07-10: §3.1 legacy custody + ndarja custody↔ADR-023 në Go/No-Go/rollback/evidence)*

**Qëllimi:** të përcaktojë si kalohet nga PRO DATA / proceset ekzistuese në iData+ pa humbur kontroll financiar, fiskal, stok apo operativ.

---

## 1. Parimet e cutover-it

1. **[HISTORICAL / SUPERSEDED nga ADR-030 — vetëm data]** *Cutover i pastër më 2027-01-01 është objektivi planifikues.* **CURRENT (PLANET-FIRST, ADR-030 ACCEPTED / EFFECTIVE (03.09.2026: Fable → Codex → HG-PR31-MERGE → squash-merge PR #31 `42993f0`), 02.09.2026):** PLANET operational-ready deri më **2026-12-31** → **JANUARY 2027 CONTROLLED PILOT** (parallel-run me sistemin ekzistues si source-of-record/fallback) → source-of-record cutover = **TBD — HUMAN GO/NO-GO AFTER JANUARY PILOT EVIDENCE**; KABI pas pranimit/stabilizimit të PLANET-it. Cutover-i mbetet **i pastër** (parimet 2–6 të pandryshuara).
2. **Nuk migrohet historiku transaksional.** Migrohen master-data dhe saldo hapëse.
3. **PRO DATA mbetet golden reference baseline** deri në go-live final dhe gjatë parallel run.
4. **Nëse nuk rakordon, nuk kalon.** Nuk bëjmë “hajt se e rregullojmë pas live”.
5. **Rollback është i planifikuar, jo panik.** Çdo fazë ka kriter kur kthehemi në fallback.
6. **Asnjë sekret ose data private në docs publike.** Evidence me vlera reale rri private-local.

### 1.6 Kufiri i pilotit të janarit 2027 (PLANET-first, ADR-030) — pesë gjendje TË NDARA

| Gjendja | Kuptimi | Çfarë NUK do të thotë |
|---|---|---|
| **implementation complete** | increment-i/faza ka evidencë të ekzekutuar + review + Human Gate + merge | jo operational-ready; jo pilot |
| **operational-ready** (`OPERATIONAL_READY_FOR_CONTROLLED_PILOT`, cak 2026-12-31) | shared foundation + PLANET MVP i autorizuar + readiness teknike/operative (hardware në vend, trajnim, DR drill, UAT critical PASS, rollback owner) | **jo** `PRODUCTION_READY`, **jo** `SOURCE_OF_RECORD`, **jo** GA, **jo** certifikim fiskal, **jo** autorizim automatik për data reale |
| **controlled pilot** (janar 2027) | parallel-run me përdorues realë, workflows/punë reale, audit, reconciliation ditore, fallback/rollback aktiv; **sistemi ekzistues = source-of-record/fallback** | jo cutover; dokumentet ligjore/fiskale reale vijnë nga sistemi ekzistues, përveç kur gates + Pronari autorizojnë ndryshe |
| **source-of-record cutover** (data = TBD, Human Go/No-Go pas evidencës së pilotit) | iData+ bëhet source-of-record për scope-in e aprovuar; saldo hapëse të nënshkruara; §6 Go PASS | jo GA për module jashtë scope-it |
| **general availability** | operim i plotë sipas F10 + hypercare i mbyllur + handover | — |

**Data reale ose dokumente fiskale reale në pilot lejohen VETËM kur** gates përkatëse janë **PASS** — privacy (`PRIV-001`), encryption/key custody (`ADR-025`), hosting/secrets (`ADR-026`), access (F1 Identity/RBAC), backup/restore (product DR `ADR-023` + legacy custody §3.1) dhe compliance (SEF/ATK për çdo dokument fiskal real) — **dhe Pronari i autorizon shprehimisht**. Klasifikimi i rollout-it të parë (Tracker/Roadmap §2): `PLANET_PILOT_BLOCKER` · `KABI_LIVE_BLOCKER` · `NON_BLOCKER_FIRST_ROLLOUT`.

**Dështimi i një gate-i kritik gjatë pilotit:** mban sistemin ekzistues source-of-record · mban fallback-un aktiv · **shtyn cutover-in** · **nuk ndryshon automatikisht datën në një datë tjetër të sajuar** — data e re vendoset vetëm nga Pronari me Human Go/No-Go (`OVERDUE / DATE REBASELINE REQUIRED / OWNER`).


---

## 2. Roli dhe përgjegjësitë

| Roli | Përgjegjësia |
|---|---|
| Group Owner | Go/No-Go final, aprovim rollback, prioritetet biznesore |
| Kontabilisti | saldo hapëse, VAT books, GL, period close, sign-off financiar |
| KABI Operations | stok, fermerë, qumësht, batch, van-sales, hardware në fabrikë/terren |
| PLANET Finance | cash/POS settlement, handover, Z-report, operacionet ditore |
| PLANET Operations | floor-plan, KDS, tavolina, shank, printer routing |
| iCode / Platform Owner | deploy, DB, backup/restore, SEF software, incident response |
| ATK / SEF | certifikim, test/fiscal behavior, hardware-vs-software clarity |

---

## 3. Çka migrohet dhe çka nuk migrohet

### Migrohet

- Subjektet/business records.
- Klientët, furnitorët, fermerët/partnerët.
- Artikujt/produktet, barcode, TVSH, njësi, paketim.
- Çmimet aktive dhe konfigurimet e miratuara.
- Recetat/BOM aktive, kur konfirmohen.
- Stoku aktual dhe LOT/expiry kur disponohet.
- Saldo hapëse: AR, AP, arka, banka, stok, gajbe/ambalazh.
- Role/përdorues të rinj, jo kredencialet e vjetra.

### Nuk migrohet

- Historiku transaksional.
- Password/PIN/MAC/device secrets.
- Audit logs të vjetra.
- Raw PRO DATA dumps.
- Kontrata/oferta/çmime marrëveshjeje në GitHub/Notion/Slack.

### 3.1 Legacy PRO DATA backup custody (kontrolle PARA cutover-it — ≠ ADR-023)

Burimi historik (backup-i PRO DATA) duhet të mbetet i rikthyeshëm deri në cutover + periudhën e referencës:

- **Burimi autoritativ = `.bak`** — MDF/LDF të container-it **nuk janë** burimi autoritativ.
- **Kopje e dytë e enkriptuar** e `.bak` — **REQUIRED / OPEN / EVIDENCE PENDING**.
- **Hash manifest** i kopjeve — **REQUIRED / OPEN / EVIDENCE PENDING**.
- **Isolated restore drill** (mjedis i izoluar, pa ekspozim të dhënash) — **REQUIRED / OPEN / EVIDENCE PENDING**.
- Evidence për këto kontrolle jepet **privatisht nga Pronari** (asnjë filename/path/hash/inventar privat në git); **ky dokument nuk pretendon verifikim** pa atë evidence.
- **KUFIRI:** ky seksion mbulon **vetëm custody-n e backup-it legacy**; DR i produktit të ri (PITR/backup/restore drill i Postgres-it iData+) = **ADR-023**, track më vete.

---

## 4. Timeline planifikues

| Koha | Veprimi | Kriteri |
|---|---|---|
| T-12 javë | Roadmap + cutover runbook approved | Fazat/gates/datat të mbyllura |
| T-10 javë | ADR/gates P0 mbyllen ose kanë Plan-B | asnjë vendim fiskal i shpikur |
| T-8 javë | UAT scripts + training plan | role + flows të dokumentuara |
| T-6 javë | Hardware/hub/POS/SEF readiness | pajisjet testohen në vend |
| T-4 javë | Parallel run start | PRO DATA + iData+ krahasohen |
| T-3 javë | Opening balances dry-run | import + rakordim pa bllokues |
| T-2 javë | Code/config freeze për core flows | vetëm bugfix P0/P1 |
| T-7 ditë | Go/No-Go 1 | kritikat PASS |
| T-1 ditë | Final backup/export/saldot | nënshkrim kontabilist/owner |
| T day | Go-live / cutover | aktivizo source-of-record |
| T+1 deri T+7 | Daily reconciliation + hypercare | çdo ditë mbyllet pa diferencë kritike |

---

## 5. Parallel Run — ~~Dhjetor~~ **CURRENT: janar 2027 (controlled pilot i PLANET-it, ADR-030); dhjetori = readiness/rehearsal**

Parallel run nuk është demo; është prova financiare/operative. Gjatë tij **sistemi ekzistues mbetet source-of-record/fallback** (§1.6).

### Çdo ditë krahasohen

- Shitjet totale dhe sipas TVSH.
- Libri i Shitjes dhe Libri i Blerjes.
- Cash/card/bank/POS terminale.
- Stoku: hyrje, dalje, kthime, transferime.
- ~~KABI: pranim qumështi, shlyerje, batch/LOT, van close.~~ **N/A për pilotin PLANET të janarit 2027 / future KABI phase** (KABI = pas pranimit/stabilizimit të PLANET-it).
- PLANET: tavolina, porosi, KDS, Z-report, settlement — **pjesë e pilotit real** (reconciliation ditore, audit, fallback/rollback aktiv).
- SEF/fiscal outbox status *(për scope-in PLANET të pilotit)*.

### PASS

- Diferencat shpjegohen dhe mbyllen brenda ditës.
- Nuk ka humbje dokumenti.
- Nuk ka dublim.
- Kontabilisti mund të gjurmojë dokument → GL → libër ATK.

### FAIL

- Diferencë e pashpjeguar në cash/TVSH/stok.
- Dokument pa GL ose GL pa dokument.
- Fiskalizim i dështuar pa alarm.
- Offline sync humb ose dublon dokument.

---

## 6. Go/No-Go checklist

### Go lejohet vetëm nëse

- SEF/certifikimi i kërkuar është i kryer për scope-in live.
- **(A) Legacy PRO DATA custody PASS:** `.bak` autoritativ + kopje e dytë e enkriptuar + hash manifest + **isolated legacy restore drill** (evidence private e Pronarit).
- **(B) iData+ product DR PASS (ADR-023):** PITR + backup + **restore drill i produktit të ri** — rollout i parë: **`PLANET_PILOT_BLOCKER`** (para pilotit me të dhëna reale).
- *(A dhe B janë kontrolle TË NDARA — një PASS nuk e maskon tjetrin.)*
- Opening balances janë nënshkruar.
- Accounting books dhe VAT mapping janë validuar.
- Hardware është testuar në vend.
- User/role/2FA/RBAC janë gati.
- UAT critical flows PASS.
- Reliability scenarios kritike të testuara (matricat e moduleve live = PASS, sipas `Reliability_Gate.md` te `../06-architecture/`).
- Parallel run minimalisht 1 muaj ose periudha e miratuar nuk ka bllokues (CURRENT: piloti i kontrolluar i janarit 2027 — evidenca e tij + Human Go/No-Go i veçantë i Pronarit para çdo source-of-record cutover).
- Rollback owner dhe procedura janë të qarta.

### No-Go nëse

- Nuk ka SEF compliance për dokumentet live.
- Nuk rakordon GL/TVSH/stock/cash.
- **(A)** Legacy custody dështon: s'ka kopje të dytë të enkriptuar / hash manifest / legacy restore drill të provuar.
- **(B)** Product DR (ADR-023) dështon: s'ka PITR/backup/restore drill të produktit të rikthyeshëm.
- Nuk ka person përgjegjës për day-1 support.
- Një gate P0 mbetet “open” pa Plan-B të aprovuar.

---

## 7. Rollback scenarios

### Scenario A — Dështon para cutover

- **Veprim:** shtyhet go-live. PRO DATA/sistemi ekzistues mbetet source-of-record; fallback-u mbetet aktiv.
- **Kriter:** UAT/SEF/opening balances dështojnë para T day, OSE dështon njëri nga dy kontrollet e ndara të backup-it — **(A) legacy custody** apo **(B) product DR (ADR-023)**.
- **Evidence:** incident log + `OVERDUE / DATE REBASELINE REQUIRED / OWNER` (data e re vetëm nga Pronari — asnjë datë e sajuar) + owner approval.

### Scenario B — Dështon në ditën e parë

**Veprim:**

1. Ndalo krijimin e dokumenteve të reja në iData+ për affected module.
2. Eksporto listën e dokumenteve të krijuara deri në momentin e ndalimit.
3. Kontabilisti/finance vendos si trajtohen dokumentet: mbeten, anulohen/korrigjohen, ose rikrijohen në fallback.
4. PRO DATA/procesi ekzistues aktivizohet si fallback i përkohshëm.
5. Incident team bën root-cause dhe hotfix vetëm pas verifikimit.

**Kriter rollback:** humbje dokumenti, fiskalizim i dështuar pa recovery, cash/stok që nuk rakordon, sistem i paaksesueshëm në operim kritik.

### Scenario C — Dështon pas disa ditësh

**Veprim:** rollback selektiv ose hybrid fallback.

- Dokumentet e krijuara në iData+ nuk fshihen.
- Përdoren reversal/korrigjim/notë krediti sipas rregullave.
- Mbyllet data e fundit e besueshme dhe bëhet reconciliation.

**Kriter:** problemi është i kufizuar dhe nuk prek të gjitha modulet.

---

## 8. Komunikimi gjatë incidentit / rollback

Incidenti nuk menaxhohet vetëm teknikisht. Duhet komunikim i qartë, me role dhe afate, që të mos krijohet konfuzion në operim.

| Kur | Kush njofton | Kë njofton | Çka komunikohet |
|---|---|---|---|
| Brenda 15 minutave nga incidenti P0/P1 | iCode / Platform Owner | Group Owner + PM | çka është prekur, workaround fillestar, a ndalet krijimi i dokumenteve |
| Brenda 30 minutave | PM | Kontabilisti + finance lead për biznesin e prekur | ndikimi financiar/fiskal, cilat dokumente ruhen, cilat presin verifikim |
| Brenda 30 minutave | Ops Lead për biznesin e prekur | stafi operativ | a përdoret iData+, fallback, apo proces manual/PRO DATA |
| Çdo 60 minuta deri në zgjidhje | PM + Tech Lead | Group Owner + leads | status, vendim i ardhshëm, rreziku për ditën e punës |
| Pas mbylljes | PM | të gjithë owner-at | incident summary, root cause, dokumentet e prekura, vendimi reversal/credit-note/fallback |

**Rregull:** asnjë përdorues nuk udhëzohet të fshijë dokument financiar. Korrigjimet bëhen me reversal, anulim/korrigjim, notë krediti ose procedurë të miratuar nga kontabilisti.

---

## 9. Daily reconciliation checklist pas go-live

Për 7 ditët e para minimum:

- Shitjet ditore = POS/SEF/GL.
- Cash/card/bank = finance settlement.
- Stoku = lëvizje + shitje + kthime.
- ~~KABI van close = ngarkim - shitje - kthime - mbetje.~~ **N/A / future KABI phase** (jo në pilotin PLANET të janarit 2027).
- ~~Farmer intake = pranim + lab + tank.~~ **N/A / future KABI phase.**
- PLANET Z-report = tavolina + pagesa + anulime.
- Error logs dhe fiscal outbox = 0 bllokues P0.
- Çdo diferencë ka owner dhe status.

---

## 10. Evidence që ruhet

- Go/No-Go checklist i nënshkruar.
- **(A)** Legacy custody evidence (kopja e enkriptuar + hash manifest + isolated legacy restore drill) — **private, e Pronarit**; këtu vetëm konfirmimi PASS/FAIL.
- **(B)** Product DR (ADR-023): backup/restore drill logs të produktit.
- Opening balances import reports.
- Parallel run comparison reports.
- SEF certification/test evidence.
- UAT scripts/results.
- Training attendance.
- Day 1–7 reconciliation reports.
- Incident/rollback logs nëse ndodh.

---

## 11. Vendimet që nuk mbyllen nga ky runbook

Ky runbook nuk vendos vetë:

- Printer hardware vs SEF software.
- Full payroll engine vs master-data + kontribute.
- RESORT/business_unit semantics.
- COGS final method.
- Tax rounding final.
- PLANET floor/KDS routing.

Këto duhet të mbyllen në tracker/ADR/SSOT para fazës ku bllokojnë.

---

## 12. Hapi i radhës për këtë dokument

1. Review nga Fable/Claude.
2. Review kundërshtues nga Codex.
3. Aprovim nga Pronari.
4. PR docs në repo.
5. Përditësim Notion + Slack.
