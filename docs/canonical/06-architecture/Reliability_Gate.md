# Reliability Gate — iData+

**Versioni:** 1.0 · **Data:** 2026-07-07 · **Pronari:** Blend (aprovon çdo ndryshim të këtij dokumenti)
**Statusi:** canonical pas merge · **Zbatimi:** çdo modul kritik, nga sot deri në lansim (dhe pas tij)
**Lidhje:** `../00-ssot/iData_Plus_SSOT.md` §2 (offline-first) · §7 (SEF offline 48h) · `../01-phase0/05_Decision_Gates_ADR.md` (ADR-023 backup/PITR/restore — Accepted) · `../../PM_REVIEW_PROTOCOL.md` (kontrolli #7) · `../08-roadmap/` (acceptance criteria per fazë + Go/No-Go)

---

## 1. Parimi

**Dështimet ndodhin.** Interneti bie, ATK ka mirëmbajtje, printeri mbetet pa letër, pajisja fiket në terren, serveri ristartohet, disku mbushet. Ky gate **nuk** premton "sistemi s'bie kurrë" — premton sjellje të përcaktuar:

> **Sistemi nuk humb të dhëna, nuk dublon dokumente, nuk krijon gjendje financiare gjysmake, nuk bllokon gjithë platformën pa plan, dhe rikuperohet me procedurë të testuar.**

Një modul kritik pa sjellje të përcaktuar, të testuar dhe të dokumentuar për skenarët e dështimit **nuk quhet gati** — pavarësisht sa mirë punon në "ditën e lumtur".

## 2. Përkufizimi i modulit kritik (i prerë — jo i diskutueshëm per PR)

**KRITIK** = çdo modul që prek:

- para / pagesa;
- dokumente financiare ose fiskale;
- GL / postim;
- stok;
- porosi live (POS/KDS);
- sync / offline;
- fiscal outbox;
- settlement (kamarier / fermer / financë operative);
- funksionimin e hub-it lokal.

**JO-KRITIK** = raporte read-only, UI kozmetike, docs, tooling dev.

**Në dyshim → trajtohet si kritik.** Klasifikimi rrjedh nga ky përkufizim, jo nga negociata në PR.

## 3. Matrica standarde e dështimeve (8 skenarët minimalë)

Çdo modul kritik duhet të ketë sjellje të përcaktuar + të testuar për **të tetë** skenarët. Skenarë shtesë specifikë per modul shtohen sipas natyrës së tij (p.sh. antibiotik-gate për laboratorin, afati 48h për fiscal outbox) — 8-shja është dyshemeja, jo tavani.

| # | Skenari | Shembuj tipikë |
|---|---|---|
| 1 | Internet down | WAN bie në restorant/fabrikë/terren; vetëm LAN i hub-it punon |
| 2 | SEF/ATK down | serveri i ATK-së s'përgjigjet; timeout; 5xx; mirëmbajtje |
| 3 | Printer / peripheral down | printer termik pa letër/offline; peshorja s'lexon; KDS s'konfirmon |
| 4 | Device crash / battery / offline | pajisja e kamarierit/shoferit fiket, ristartohet, mbetet offline gjatë |
| 5 | Sync interrupted / retry / idempotency | sync ndërpritet në mes; replay; i njëjti dokument dërgohet dy herë |
| 6 | Server / hub restart | hub-i lokal ose API ristartohet me radhë jo-bosh |
| 7 | DB unavailable | Postgres i paarritshëm përkohësisht (lokal ose cloud) |
| 8 | Disk / queue full | disku i hub-it mbushet; outbox / fiscal outbox arrin kapacitetin |

## 4. Template i failure matrix (kolonat e detyrueshme)

Çdo modul kritik dorëzon matricën e vet me këto kolona — **asnjë kolonë s'lihet bosh**:

| Kolona | Çfarë kërkohet |
|---|---|
| Failure scenario | cili nga 8 skenarët (+ specifikët e modulit) |
| Expected behavior | çfarë bën sistemi (bllokon? radhit? vazhdon offline? degradon me plan?) |
| Data safety guarantee | çfarë garantohet: pa humbje, pa dublim, pa gjendje gjysmake (atomicitet / idempotencë) |
| User-visible behavior | çfarë sheh përdoruesi (mesazh i qartë, gjendje offline e shënuar, statusi i radhës) |
| Recovery behavior | si rikuperohet (auto-retry, drain i radhës, procedurë manuale e skriptuar) |
| Automated/manual test | testi që e provon (i automatizuar në CI ku është e mundur; manual i skriptuar ku s'është) |
| Evidence required | çfarë evidence pranohet (output testesh, log CI, procedurë e ekzekutuar me rezultat) |

## 5. Rregulli i gate-it

> **Modul kritik pa failure matrix + teste = JO GATI → REJECT.**

- Zbatohet nga PM/Quality-Gate (Codex) sipas `docs/PM_REVIEW_PROTOCOL.md` — kontrolli #7.
- Vlen për **çdo PR** që prek modul kritik: matrica përditësohet **bashkë me kodin**, jo "më vonë".
- Fazat kritike të roadmap-it (F1–F5, F7, F8) kanë "**Reliability Gate PASS për modulet e fazës**" në acceptance criteria; Go/No-Go i cutover-it e kërkon si kusht Go.

## 6. Waiver (rruga E VETME e anashkalimit)

- VETËM me **vendim të shkruar të Blendit**, me:
  - **riskun** e shënuar eksplicit;
  - **afatin** e mbylljes së gap-it;
  - **logim** në SSOT (Decisions log) dhe/ose Outstanding Confirmations Tracker.
- Pa waiver të shkruar, gate-i **s'anashkalohet kurrë** — as për afat, as për demo, as për "e mbyllim javën tjetër".

## 7. Numrat (targets — JO SLA prodhimi)

Asnjë numër këtu s'është SLA prodhimi pa test të riprodhueshëm që e mbështet. Statusi i secilit shënohet eksplicit:

| Metrikë | Vlerë aktuale | Statusi |
|---|---|---|
| p95 latency (POS/KDS në LAN, hub lokal) | TBD | initial target vendoset në PoC-4 (local hub); JO SLA |
| RPO / RTO | TBD | **gated nga ADR-023** (backup/PITR/restore drill — Accepted; drill i detyrueshëm para Fazës 1) |
| Kapaciteti outbox / fiscal outbox (ditë offline) | 3–5 ditë ruajtje mobile (SSOT §2) | initial target për PoC-3/PoC-5; **afati ligjor SEF 48h = kufiri i fortë** (SSOT §7) |
| Koha e drain-it të fiscal outbox pas rikthimit | TBD | initial target në PoC-5; monitorimi i afatit 48h i detyrueshëm |

Kur një numër provohet me test të riprodhueshëm, promovohet në SSOT/ADR si vlerë e mbështetur — deri atëherë mbetet target/TBD.

---

## Historia

- v1.0 (2026-07-07) — krijuar me aprovimin e Blendit; formalizon rregullin e qëndrueshmërisë: çdo modul kritik kërkon failure matrix (8 skenarë standardë) + teste + evidencë para se të quhet gati; waiver vetëm me vendim të shkruar të pronarit.
