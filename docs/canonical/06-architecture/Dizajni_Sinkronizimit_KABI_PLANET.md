# Dizajni i Sinkronizimit (Sync Engine) — KABI & PLANET

**Versioni:** 1.1 · **Data:** 23.07.2026 · **iCode L.L.C**
**Stack boundary:** semantika e sync-ut ruhet; deployables/teknologjitë sinkronizohen me ADR-024. Target-i i aprovuar është ende **NOT YET IMPLEMENTED**.
**Dokument i brendshëm zhvillimi** · Plotëson seksionin 7 të Planit Teknik

---

## 1. Qëllimi & parimet

Objektivi: **pa humbje dhe pa dublim** të dhënash në rrjedhat kritike, komunikim pa shkëputje kamarier↔kuzhinë, dhe punë offline në mobile dhe PC.

Parimet bazë:
- **Offline-first / local-first** — pajisja punon mbi bazën lokale; rrjeti është "bonus", jo kusht.
- **Idempotencë** — i njëjti veprim mund të dërgohet shumë herë pa u aplikuar dy herë.
- **Eventual consistency** — pajisjet konvergojnë në të njëjtën gjendje pas sinkronizimit.
- **Server lokal (hub)** si burim i së vërtetës gjatë shërbimit në restorant.
- **Dukshmëri e statusit** — asgjë s'dështon "në heshtje".

---

## 2. Topologjia (ku rri çfarë)

```
                    ┌─────────────── CLOUD ───────────────────┐
                    │ ASP.NET API · PostgreSQL (source-of-record)│
                    │ WebSocket gateway · Backups · Telemetry  │
                    └────────▲──────────────────────▲──────────┘
                             │ sync delta            │ sync delta
        KABI mobile ─────────┘                       │ (internet)
       (agjentë në terren,                           │
        offline-first, sync                ┌─────────┴──────────┐
        direkt me cloud)                   │   HUB LOKAL (PLANET) │
                                           │ ASP.NET + PostgreSQL │ ◀─ realtime mbi LAN:
                                           │   e vërteta gjatë    │    kamarier↔KDS↔arkë
                                           │   shërbimit          │
                                           └──▲─────▲─────▲───────┘
                                              │     │     │  Wi-Fi / LAN
                                        kamarierë  KDS   arkë (PC)
```

- **KABI:** pajisjet mobile sinkronizohen **direkt me cloud** (agjentët janë në terren, s'ka rrjet lokal). Rast klasik offline-first.
- **PLANET:** pajisjet (kamarierë, KDS, arkë) lidhen me **hub-in lokal** mbi LAN; hub-i sinkronizohet me cloud kur ka internet. Kështu shërbimi vazhdon edhe pa internet.

---

## 3. Modeli i të dhënave për sync

Çdo entitet i sinkronizueshëm mban:

| Fusha | Roli |
|---|---|
| `id` (UUID) | Çelës i gjeneruar nga **klienti** (pajisja) — baza e idempotencës |
| `tenant_id` | Izolimi multi-tenant |
| `updated_at` | Koha e ndryshimit të fundit (server-assigned në sync) |
| `rev` / `version` | Numër versioni për zbulim konflikti |
| `device_id` | Origjina e ndryshimit |
| `deleted` | Soft-delete (nuk fshihet fizikisht derisa të sinkronizohet) |

Strukturat ndihmëse:
- **Outbox** (radhë lokale): operacionet e pakryera, me `status` (pending / sent / acked / failed).
- **Sync cursor**: `last_synced_at` ose checkpoint server-i, per pajisje, për delta-sync.

---

## 4. Rrjedha e shkrimit (write path) — offline-first

```
Veprimi i përdoruesit (p.sh. krijo porosi)
   │  (1) shkruhet LOKALISHT (optimistic) → UI përditësohet menjëherë
   ▼
Bazë lokale  +  OUTBOX { id=UUID, op, payload, ts, status=pending }
   │  (2) background sync (kur ka lidhje me hub/cloud)
   ▼
HUB / CLOUD  ──(3) validim + DEDUP (me UUID) → aplikon ndryshimin
   │  (4) ACK te pajisja
   ▼
OUTBOX: operacioni shënohet "acked" dhe hiqet
        │  pa ACK brenda afatit → (5) RETRY me exponential backoff
```

Pikat kyçe:
- Përdoruesi **s'pret kurrë** rrjetin (optimistic UI).
- Operacioni konsiderohet i kryer vetëm pas **ACK** nga serveri; ndryshe, riprovohet.
- Renditja e operacioneve në outbox ruhet (FIFO per-entitet) për konsistencë.

---

## 5. Idempotenca (pa dublim)

- Çdo operacion ka **UUID të gjeneruar nga pajisja**.
- Serveri mban një regjistër të id-ve të aplikuara; nëse merr të njëjtin id sërish (për shkak retry/dublim rrjeti) → **e injoron**, s'e riaplikon.
- Garancia reale: **at-least-once delivery + idempotent effect/dedup**. Retry mund ta dërgojë të njëjtin operacion disa herë; serveri e aplikon vetëm një herë. Nuk pretendohet exactly-once transport përtej pajisje/Edge/cloud.

---

## 6. Rrjedha e leximit (read / pull)

- **Delta-sync:** serveri kthen vetëm ndryshimet që nga `cursor`-i i fundit i pajisjes (jo gjithçka).
- **Snapshot fillestar** në instalim/login, pastaj vetëm delta.
- Kombinim **pull periodik + push realtime** (kur online) për freskim të menjëhershëm.

---

## 7. Realtime (kur ka lidhje)

- **WebSocket** me hub-in (PLANET) ose cloud (KABI) për push të menjëhershëm: porosia → ekrani i kuzhinës në çast.
- Kanal **per-tenant**; mesazhet kanë `event type` (porosi e re, status i ndryshuar, gati, etj.).
- **Fallback në polling** nëse WebSocket-i bie, që të mos humbet asgjë.

---

## 8. Conflict resolution (rregulla per-domain)

Konfliktet ndodhin kur dy pajisje ndryshojnë të njëjtën gjë offline. Rregulla sipas natyrës së të dhënës:

| Lloji i të dhënës | Strategjia |
|---|---|
| **Status** (porosi, tavolinë) | Last-write-wins me **timestamp server** + log i ndryshimeve |
| **Stoku / saldo** | **Jo mbishkrim** — ruhen lëvizjet (append-only movements); gjendja = shuma e lëvizjeve |
| **Të dhëna master** (menu, çmime, receptura) | Server/admin fiton; **s'editohen offline** nga pajisjet operative |
| **Krijim entiteti** (porosi e re) | Idempotencë me UUID — bashkohen, s'ka konflikt |

Konfliktet që s'zgjidhen automatikisht shkojnë te një **tabelë konfliktesh** për shqyrtim manual nga admini (rast i rrallë).

---

## 9. Serveri lokal (hub) — PLANET

- **Mini-PC në lokal** që mban **PostgreSQL lokal + shërbim realtime**.
- Pajisjet lidhen me hub-in mbi LAN; gjatë shërbimit, **hub-i është burimi i së vërtetës**.
- **Hub ↔ cloud:** sinkronizim dy-drejtimësh kur ka internet (delta + idempotencë).
- **Përparësia:** edhe pa internet fare, salla↔kuzhina↔arka punojnë normalisht mbi LAN.
- **Rikuperim & qëndrueshmëri:**
  - UPS (rrymë rezervë) + auto-restart i shërbimit.
  - Nëse bie hub-i: pajisjet kalojnë te cloud (nëse ka internet) ose në modalitet të degraduar (vetëm lokalisht në pajisje, me ri-sync kur kthehet hub-i).
  - Backup periodik i hub-it.

---

## 10. Ora dhe renditja (time & ordering)

- Përdoret **koha e serverit** si referencë; timestamp-et finale i cakton serveri në sync.
- Korrigjim i **clock skew** të pajisjes (ora e gabuar e telefonit nuk prish renditjen).
- Renditja e qëndrueshme me timestamp server-assigned (ose hybrid logical clock) për ngjarjet kritike.

---

## 11. Çfarë punon offline vs çfarë kërkon internet

| Punon offline (mbi LAN/lokalisht) | Kërkon internet (radhitet & kryhet kur kthehet) |
|---|---|
| Marrja e porosive (kamarier) | Fiskalizimi ATK (numër fiskal + QR) |
| KDS – ekranet e kuzhinës (mbi LAN) | Dërgimi i faturës me email |
| Printimi në printer lokal | Push notifications jashtë LAN-it |
| GPS + foto (KABI agjenti) | Dashboard & raporte në cloud |
| Gjenerim porosie / fature të brendshme | Pagesat me kartë (varet nga terminali) |

---

## 12. Fiskalizimi ATK në kontekst offline

- Kur s'ka internet, fatura gjenerohet si **dokument i brendshëm** dhe hyn në një **radhë fiskale**.
- Kur kthehet lidhja, fatura **fiskalizohet automatikisht** (merr numër fiskal + QR), sipas radhës.
- Mbahet **log i plotë** i transaksioneve fiskale për auditim.
- **Varësi/risk:** sjellja e saktë offline duhet konfirmuar me ofruesin fiskal/ATK (a lejohet fiskalizim i shtyrë dhe brenda çfarë afati). E adresojmë në fillim të projektit.

---

## 13. Siguria e të dhënave offline

- **Cache i enkriptuar** në pajisje; nuk ruhen të dhëna të ndjeshme të panevojshme.
- **Scope minimal:** vetëm të dhënat e nevojshme (menu e sotme, tavolina aktive, porositë e ditës) — jo gjithë historiku.
- **Skadim sesioni** edhe offline; token refresh kur online; **çkyçje/wipe nga distanca** (kritike për BYOD).

---

## 14. Treguesit e statusit (UX)

- Indikator **online / offline** gjithmonë i dukshëm.
- Numërues **"X veprime në pritje për sinkronizim"**.
- Konfirmim vizual kur sinkronizimi përfundon; rikujtues nëse diçka ngec.
- **Asnjë dështim në heshtje** — ose kalon, ose përdoruesi e sheh statusin.

---

## 15. Menaxhimi i gabimeve & rikuperimi

- **Retry me exponential backoff** për operacionet e padërguara.
- **Dead-letter queue** për operacione që dështojnë vazhdimisht → alarm te admini/monitorimi.
- **Rindërtim gjendjeje:** nëse pajisja humb të dhënat lokale, rindërtohet nga snapshot + delta nga cloud/hub.
- Backup i rregullt i hub-it dhe cloud-it (me restore të testuar).

---

## 16. Skenarët e testimit (QA — të detyrueshëm)

1. **Internet poshtë / LAN lart** — salla↔kuzhina duhet të punojnë normalisht mbi hub.
2. **Pajisje plotësisht offline** — porositë ruhen dhe sinkronizohen pas rikthimit.
3. **Rikthim lidhjeje** — sinkronizim automatik, pa dublim.
4. **Konflikt** — dy pajisje ndryshojnë të njëjtën tavolinë/stok → zgjidhet sipas rregullave.
5. **Dublim rrjeti / retry** — i njëjti operacion dy herë → aplikohet një herë (idempotencë).
6. **Dështim i hub-it** — fallback te cloud ose degradim, pastaj ri-sync.
7. **Clock skew** — ora e gabuar e pajisjes s'prish renditjen.
8. **Storage i mbushur** në pajisje — sjellje e kontrolluar, jo humbje.
9. **Ngarkesë** — 100+ pajisje njëkohësisht (latencë realtime < 1s në LAN).

---

## 17. Teknologjitë (referencë e brendshme)

- **Mobile/POS local:** SQLite e enkriptuar + sync adapter; PowerSync vs custom mbetet ADR-011. Te POS, .NET Local Host zotëron DB/outbox/keys dhe renderer-i Angular komunikon vetëm me typed IPC; te mobile, data layer-i i aplikacionit zotëron SQLite/keystore.
- **Semantika fiskale offline:** rregulla/vectors language-neutral; C# reference + TypeScript mobile duhet të kalojnë decimal/protobuf/hash equivalence në Step C. OpenAPI vetëm nuk mjafton për logjikën.
- **Realtime:** WebSocket/SSE; backplane/provider mbetet gate operacional.
- **Edge Hub:** ASP.NET Core + PostgreSQL lokal.
- **Cloud:** ASP.NET Core Platform API/Workers + PostgreSQL qendror.
- **Contracts:** OpenAPI → generated TypeScript clients.
- **Device integrations:** .NET Device Agent; hardware-i final varet nga PoC-4b/ADR-017.

*Shënim: ky dokument fokusohet te dizajni i sinkronizimit; zgjedhja e plotë e stack-ut është te Plani Teknik.*
