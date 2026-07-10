# ATK Compliance Gate — regjistri i kërkesave KT/UA me gate

**Versioni:** 1.1 · **Data:** 2026-07-10 · **Pronari:** Blend · **Statusi:** canonical pas merge
**Burimet normative (PUBLIKE — URL/sha256/datat e plota: `ATK_Source_Register.md`):**
- **KT** — *Kërkesat Specifike Teknike dhe Funksionale për PEF/SF/SEF* (Dok. Nr. 01-06-1967, miratuar 29.05.2026, 88 f.) **SIÇ AMENDOHET** nga:
- **Ndryshim-Plotësimi i KT-së** (Nr. Prot. 01-06-2194) — **NË FUQI nga 22.06.2026** (data e nënshkrimit; protokolluar 24.06.2026): amendon **Nenin 25** (Instalimi i SEF) + **Nenin 26 par. 4** (Ruajtja e të Dhënave); citimet besnike → `ATK_Source_Register.md` §3.
- **UA** — *Udhëzimi Administrativ MF Nr. 01/2026* (52 nene, 40 f.)
- **Kushtet dhe procedurat për aplikimin, certifikimin dhe mirëmbajtjen e SEF** — nënshkruar + **NË FUQI 03.07.2026** (Neni 9).
- **LAPT** — *Ligji Nr. 08/L-257 "Për Administrimin e Procedurave Tatimore"* (GZ Nr. 5/2024, 12.01.2024) — ligji ku referojnë **sanksionet e UA N49**; harta nen-për-nen = **`ATK-011`** (mungon sot).

**Metoda:** auditim nen-për-nen i të dy dokumenteve (322 kërkesa të nxjerra → **278 unike**; 38 PEF-hardware-only = N/A në bllok; 240 të mapuara kundër repo-s me evidencë; verifikim aversarial me spot-checks). *Input pune: private local ATK compliance matrix, not committed.* **Nota aktuale e pajtueshmërisë (vlerësim i pavarur): 7/10 për fazën PoC.**
**Shënim (10.07.2026):** amendamenti 22.06 e ndryshon bazën normative (N25 + N26.4) — rreshtat përkatës këtu janë rifreskuar; regjistri i plotë 278-rreshtor private-local rifreskohet në auditin e radhës kundrejt `ATK_Source_Register.md`.

**Statuset:** `ZBATUAR` (kod+test) · `SPECIFIKUAR` (doc kanonik, pa kod) · `PJESËRISHT` · `MUNGON` · `N/A` (me arsyetim).
**Gate-t:** **[C]** = bllokues certifikimi · **[G]** = bllokues go-live · **[H]** = hardening (pas live).
**Shpërndarja:** ZBATUAR 33 · SPECIFIKUAR 17 · PJESËRISHT 97 · MUNGON 21 · N/A 72 (+38 PEF-only) — prioritete: **critical 8** · high 52 · medium 61 · low 34 · none 85.

---

## 1. TETË KRITIKËT (bllokues — të gjithë me fazë të caktuar)

| # | Ref (nen) | Kërkesa | Statusi sot | Faza | Gate |
|---|---|---|---|---|---|
| 1 | **UA N7.2.2** | **`vat_number` i tatimpaguesit në kupon** kur është i regjistruar për TVSH (fushë e kushtëzuar — pa vlera reale në repo; config per tenant "në TVSH: po/jo") | **MUNGON** — proto zyrtar s'e ka; asnjë fushë në modelin tonë; print layer s'ekziston | **F1–F2** (entiteti business + `Kupon_Print_Spec.md` header) | **[C]** |
| 2 | **KT N26.10** | **NUIKF**: alfanumerik, **≤16 karaktere**, unik, **i gjeneruar automatikisht + i validuar** | **PJESËRISHT** — `VerificationNo` ekziston si fushë proto; zero gjenerim/validim; burimi i vlerës = pyetje Tier 2 | **F5** (pas sqarimit Tier 2) | **[C]** |
| 3 | **KT N26.4 (i amenduar — në fuqi 22.06.2026)** | **Ruajtja e të Dhënave — teksti i RI** (amendamenti e ZËVENDËSOI N26.4 e vjetër; citimi besnik → `ATK_Source_Register.md` §3): **(i)** ruajtje **e mbrojtur me garanci integriteti** të të dhënave në rast defekti/ndërprerjeje të papritur; **(ii)** **dërgim automatik** sapo rikthehet lidhja; **(iii)** **eksport CSV/PDF** i të patransmetuarave **VETËM me kërkesë** të ATK-së/autoritetit shtetëror (rresht më vete §2c). **Enkriptimi at-rest NUK është më kërkesë eksplicite e KT-së** → **STANDARD I BRENDSHËM** sigurie/LMDhP (**AES-256 = kandidati**; vendim arkitekturor — mbyllet në **ADR**; pajisje: SQLCipher/keystore; server: disk/pgcrypto) | **PJESËRISHT** — (i) payload durable po; modeli i plotë i integritetit/pandryshueshmërisë = follow-up §4.2; (ii) retry/drain i provuar (PoC-5 Tier 1); (iii) eksporti **MUNGON** | **F1** (integriteti §4.2) + **F1/F5** (eksporti) + **ADR** (enkriptimi si standard i brendshëm) | **[C]** |
| 4 | **KT N26.5(a) f.48** | **Rendi ONLINE-first**: të dhënat **transmetohen në ATK PARA shtypjes** (merr `transaction_id` → shtyp); **offline = vetëm përjashtim** (Neni 44/48h) | **MUNGON si rend** — rrjedha e dokumentuar deri sot ishte offline-first | **SSOT §7 (ky PR)** → kod F1/PoC-4b/F5 | **[C]** |
| 5 | **KT N26.12(b) + N28.6** | Kuponët offline mbajnë **mbishkrimin "OFFLINE"** në kuponin e shtypur + kronologji | **PJESËRISHT** — kronologjia po (device_seq/outbox); mbishkrimi = s'ka print layer | **F5/F7** (`Kupon_Print_Spec.md` — bërë në këtë PR si spec) | **[C]** |
| 6 | **UA N39.4** | **QR element përbërës i ÇDO kuponi** — letër, PDF apo elektronik | **PJESËRISHT** — përmbajtja e QR e zbatuar+testuar; renderimi në kupon = s'ka print layer | **F1–F2** (modul kuponi me QR të detyrueshëm në çdo output + test "asnjë kupon pa QR") | **[C]** |
| 7 | **KT N25.5.1(a)** | Regjistrim **unik, write-once** i çdo kuponi (ID, datë/orë, artikuj, çmim, zbritje, operator) | **PJESËRISHT** — fushat + uniciteti po; **write-once JO** (`created_at` i ndryshueshëm; statusi i pambrojtur) | **F1 — modeli i saktë i pandryshueshmërisë:** **fiscal payload/identiteti/`created_at`/`deadline_at` = IMMUTABLE** (trigger); **DELETE i ndaluar**; **tranzicionet e statusit VETËM përmes funksionit/procedurës së kontrolluar**; çdo tentim/tranzicion → **append-only** në `fiscal_attempt_log`/`fiscal_status_event` (jo "pa UPDATE fare" — statusi DUHET të lëvizë) | **[C]** |
| 8 | **KT N26.15 + UA N35.8** | **Kodi Unik i SEF-it + NUI i zhvilluesit** të integruar në softuer, të identifikueshëm në çdo dërgim | **PJESËRISHT** — `ApplicationId`/`PosId`/`VerificationNo` në çdo PosCoupon (sintetikë në teste); mapimi eksplicit "Kodi Unik SEF ↔ fushë proto" i pakonfirmuar | **Tier 2** (konfirmim në onboarding real) + ANALIZA §11 | **[C]** |

## 2. Regjistri tematik i kërkesave të tjera me gate (përzgjedhja aktive)

*(Regjistri i plotë 278-rreshtor me evidencë per-kërkesë mbahet si dokument pune private-local; këtu = kërkesat me veprim aktiv dhe gate-i i tyre. Kërkesat PEF-hardware-only = N/A në bllok — iData+ është SEF softuerik.)*

### 2a. Kuponi i shtypur / elektronik (→ `Kupon_Print_Spec.md`)
| Ref | Kërkesa | Statusi | Faza | Gate |
|---|---|---|---|---|
| UA N7.2 (2.1–2.20) | Përmbajtja e plotë e detyrueshme e kuponit | SPECIFIKUAR (ky PR — spec kanonik) | F5/F7 kod | **[C]** |
| UA N7.2.7 + KT Shtojca F | Zbritja per-artikull **me shenjën minus**; ulja në total me NËNTOTAL/ULJE/rillogaritje TVSH | MUNGON (madje bllokohet nga guard-i i negativëve në kufirin e parasë) | F5 (spec ky PR; përfaqësimi në proto = pyetje Tier 2) | **[C]** |
| KT N25.18 par.2-3 | **1 origjinal**; çdo rishtypje **"KOPJE E KUPONIT"** + audit | MUNGON | F5/F7 | **[C]** |
| KT N25.18 par.1 | Printimi/dorëzimi fizik = rruga bazë; **dërgim elektronik VETËM në rastet e lejuara nga UA/ATK** (me **pëlqimin/kushtet** përkatëse — UA N39; shih `Kupon_Print_Spec.md` §3); për rastet e tjera **printimi mbetet gate**; scope e-kupon = **vendim OPEN** (Blend). **Print Preview me QR të skanueshëm** pa shtypje | PJESËRISHT | F5/F7 | **[C]** |
| KT Shtojca SEF | Tekst+grafikë; **logo fiskale 15x8–20x10 mm**; logo RKS (UA N7.2.18) | MUNGON | F7 (+ verifikim fizik në printer test, ADR-017) | **[C]** |
| UA N6.1 | Kuponi **në momentin e pagesës** — "pagesa s'mbyllet pa kupon" | PJESËRISHT (pipeline fiskal po; POS flow = F8) | **F8 hard gate në POS** | **[G]** |
| UA N6.2 | Kupon **edhe për parapagesat** (në arkëtim; lidhja parapagesë→faturë finale pa tatim të dyfishtë) | MUNGON | F5/F8 | **[G]** |
| UA N5.2 | Kupon **edhe për furnizimet falas** (total 0) | MUNGON | F5 (+ verifikim Tier 2 që serveri pranon total 0) | **[G]** |
| KT N25.11 / N25.9 / N25.10 | Anulim me **arsye** + referencë; kthim me referencë (NUIKF i ri); ndërrim artikulli = kthim + shitje e re | PJESËRISHT (ReferenceNo me test; fusha "arsye" mungon në modelin fiskal) | F5 | **[C]** |

### 2b. Gjeneratorë & tabela zyrtare
| Ref | Kërkesa | Statusi | Faza | Gate |
|---|---|---|---|---|
| UA N7.2.15 | **Numërim ditor + rendor kronologjik per POS** (sekuencë DB, reset ditor) | PJESËRISHT (CouponId nga thirrësi) | F5 | **[C]** |
| UA N7.2.14 + KT N25.18.4a | **ID e përbërë [Nr. Njësisë ARBK]-[NUI]-[PosId]** në krye të kuponit | PJESËRISHT | F5 | **[C]** |
| KT f.86 | **Tabela e 31 njësive matëse** — **katalog i KONTROLLUAR**: i seed-uar me 31 vlerat zyrtare, **i zgjerueshëm** sipas vetë KT-së ("mund të ketë edhe njësi tjera… SEF duhet të mundësojë konfigurimin") — **jo free-text arbitrar**; validim brenda **namespace-it të vet** + hartë UoM→kod SEF | MUNGON (Unit sot string i lirë) | F1/F5 (follow-up kodi) | **[C]** |
| KT f.87–88 | **Tabela e 53 kategorive** — **katalog i KONTROLLUAR** (seed 53 zyrtaret, i zgjerueshëm sipas KT; jo free-text) + kategori SEF per artikull + sqarim lidhjes me `CouponItem.Type`; kodet validohen **per-namespace** (mbivendosja ndër-tabelore, p.sh. PAL, LEJOHET) | MUNGON (vetëm 'TT' me semantikë të padokumentuar) | F1/F5 + Tier 2 | **[C]** |

### 2c. Siguria, integriteti, LMDhP
| Ref | Kërkesa | Statusi | Faza | Gate |
|---|---|---|---|---|
| KT N24 / UA N32.1 | Regjistër i nënshkruar per kupon me çelës privat/certifikatë + lidhje e sigurt | PJESËRISHT (firma per kupon po; certifikata reale = Tier 2) | Tier 2/F5 | **[C]** |
| KT N25.5.3 | Audit logs (hyrje/shitje/anulim/konfigurim) + kriptim + kontroll integriteti | PJESËRISHT | F1/F5 | **[C]** |
| KT N26.8/N26.9 | Pas certifikimit: kod burimor + të dhëna të mbrojtura nga ndërhyrjet; mekanizma kundër ndryshimeve të paautorizuara | PJESËRISHT | F5/F9 | **[C]** |
| UA N48 | **LMDhP**: inventar i të dhënave personale në rrjedhën SEF, retention, minimizim në logje | PJESËRISHT → SPECIFIKUAR (SSOT §9, ky PR) | F1–F9 (kontroll para certifikimit) | **[C]** |
| **Amendamenti 22.06.2026 — N26.4 i ri, pika (iii)** | **Eksport CSV/PDF i të dhënave të patransmetuara VETËM me kërkesë të ATK-së/autoritetit shtetëror** (funksion eksporti i kontrolluar + audit i çdo eksporti; zëvendëson N26.4d e vjetër "qasja vetëm me kërkesë") | MUNGON | F1/F5 | **[C]** |

### 2d. Procesi operacional & rikuperimi
| Ref | Kërkesa | Statusi | Faza | Gate |
|---|---|---|---|---|
| KT N25.15 f.46 | Pas rikthimit: transmetim automatik + **regjistrim i ngjarjes teknike** (fillim/fund/shkak/kohëzgjatje/kuponë të prekur) | PJESËRISHT (retry+alerts po; technical_event log mungon) | F1–F5 (follow-up) | **[C]** |
| UA N45.6 | **Regjistrimi i kuponëve të bllokut** pas ndërprerjes së plotë: brenda **5 ditësh** pas 48h, dërgim automatik | PJESËRISHT (parimi i dokumentuar; moduli mungon) | **F5 para KABI live** | **[G]** |
| KT N25.2/N25.13/N25.17 | Raportim real-time i strukturuar përmes HTTPS | PJESËRISHT (pipeline me mock; lidhja me shitjen reale F5+) | Tier 2 + F5 | **[C]** |
| UA N35.10 | **Tatimpaguesi njofton ATK-në** për zgjidhjen/zhvilluesin/mirëmbajtësin PARA kuponit të parë | MUNGON | F5/F9 (Cutover Runbook parakusht) | **[G]** |
| UA N34.3 / KT N31.4 + Kushtet N8.1 | **Regjistrim + certifikatë digjitale PER INSTALIM** (çdo instancë SEF/POS); **ATK ka të drejtë të verifikojë funksionimin pas instalimit** (Kushtet, Neni 8.1) — sistemi duhet të jetë **gati për verifikim** në çdo kohë | SPECIFIKUAR | F5/F7 (roadmap) | **[G]** |
| **KT N25 "Instalimi i SEF" (i amenduar — në fuqi 22.06.2026)** | Teksti i RI (citimi besnik → `ATK_Source_Register.md` §3): SEF duhet të mundësojë instalimin dhe përdorimin **"së paku në njërën nga pajisjet apo ambientet e punës"** (PC, laptop, tablet, telefon i mençur, pajisje të tjera të përshtatshme). **KUJDES — pa konkluzion të nxjerrë:** NUK rrjedh nga ky tekst se një formë e vetme demonstrimi mjafton për certifikim; kufiri i prezantimit/pajisjeve para Komisionit mbetet **i gated nga `ATK-010`** | PJESËRISHT (web + mobile by design; verifikim në pajisje reale = F7/PoC-4b) | F5/F7 | **[C]** |
| UA N36.1 | Tërheqja e certifikatës (probleme serioze/shkelje) — procedurë reagimi | MUNGON | F9 (runbook) | **[H]** |
| KT N25.8 + N31.8 | **Mirëmbajtja e SEF**: monitorim i specifikave, zbatim brenda afateve, ri-testim/ri-certifikim | PJESËRISHT (snapshot+checksum po; procesi me SLA mungon) | shih §3 (Recertification Gate) | **[C]** |

## 3. RECERTIFICATION GATE (KT N25.8 / N31.8)

**Rregull i detyrueshëm nga ky PR e tutje:** çdo release/PR që **prek modulin fiskal** (proto/firma/outbox/kupon/print/transport/tabelat zyrtare/statuset) **klasifikohet eksplicit**:

- **`RECERT: JO`** — ndryshim që s'e prek sjelljen e certifikuar (refactor, teste, docs) → shënohet në PR body.
- **`RECERT: PO`** — ndryshim i sjelljes fiskale → **PARA deploy**: vlerësim ri-certifikimi/njoftim ATK (a kërkohet ri-test në test-server, a duhet njoftim formal, a preket certifikata e instancave), i dokumentuar në PR + tracker.

Procesi i mirëmbajtjes (pronari: iCode/Tech; frekuenca: **javore** gjatë test phase, mujore pas live): monitoro `github.com/fiskalizimi` + portalin SEF → në ndryshim: krahaso checksum-in e `models.proto` të vendor-uar → përditëso → Tier 1 + Tier 2 ri-test → klasifiko RECERT → zbato **brenda afateve që publikon ATK** (SLA e brendshme).

## 3a. Kufiri i certifikimit (bounded surface)

- **Monorepo mbetet NJË** — **JO repo e dytë** për certifikim.
- **Fiscal-core i izoluar**: moduli fiskal me kufij të qartë (paketë/module boundary), i identifikueshëm si njësi më vete brenda monorepo-s.
- **Artefakti për Komisionin e Certifikimit** = **POS/SEF + diagrami/përshkrimi i integrimit** — varësitë reale **nuk fshihen**, paraqiten në diagram/përshkrim.
- **Vendimi final** për formën e dorëzimit (bounded source bundle vs alternativa) merret **VETËM pas përgjigjes së `ATK-010` + aprovimit të Blendit** — deri atëherë asnjë dorëzim.

## 3b. Rregulli i artefaktit të komisionit (i brendshëm, i hekurt)

> **Artefakti i komisionit nuk përmban DEMO VERSION, mock, PoC routes, placeholders ose sample identifiers; prezantohet release-candidate i versionuar e funksional; etiketa zyrtare TEST e ambientit test NUK hiqet; test fixtures sintetike mbeten në test suite por jo në paketën e komisionit.**

*Statusi i rregullit:* **rregull i brendshëm i iData+** (mësim terreni) — **JO citim ligjor**. Zbatimi verifikohet në review: `PM_REVIEW_PROTOCOL.md` checklist SEF (v1.3).

## 4. Follow-ups kodi (Faza 1/5 — JO në këtë PR)

*Ky PR është docs-only. Kërkesat e mëposhtme kërkojnë KOD dhe regjistrohen si follow-up me fazë:*

1. **Katalogët e kontrolluar** të tabelave zyrtare (31 njësi + 53 kategori): seed me vlerat zyrtare (checksum si `models.proto`), **të zgjerueshëm sipas KT** (jo free-text arbitrar); validim **per-namespace** (PAL ndër-tabelor lejohet) + hartë artikull→kod SEF. (F1/F5)
2. **Pandryshueshmëria e fiscal outbox** (modeli i saktë): payload/identiteti/`created_at`/`deadline_at` **immutable** (trigger) + **DELETE i ndaluar** + **tranzicionet e statusit vetëm përmes funksionit/procedurës së kontrolluar** + çdo tentim/tranzicion **append-only** në `fiscal_attempt_log`/`fiscal_status_event`. (F1)
3. **Gjeneratorët**: numërim ditor + rendor kronologjik per POS (sekuencë DB me reset ditor + test dublimi/rendi); **gjenerim+validim NUIKF** (alfanumerik ≤16, unik — pas sqarimit Tier 2); **formatter i ID-së** `[Njësia]-[NUI]-[PosId]`. (F5)
4. **`fiscal_attempt_log`** (per tentim dërgimi: kush/kur/rezultat/gabim i pastruar) + **`fiscal_status_event`** (per tranzicion statusi) — të dyja **append-only**. (F1–F5)
5. **`technical_event`** log i ndërprerjeve (fillim/fund/shkak/kohëzgjatje/kuponë të prekur — KT N25.15). (F1–F5)
6. **Drain scheduler automatik** (daemon/cron; sot thirrje manuale) me monitorim. (F5)
7. **Hard gate POS (semantika e saktë):** **ONLINE** = ATK accepted + `transaction_id` i **persistuar** + kuponi i **dorëzueshëm/printueshëm** PARA mbylljes së pagesës; **OFFLINE (vetëm përjashtim)** = payload fiskal **durable në outbox** + kupon me mbishkrimin "OFFLINE" + retry/deadline/alert **aktive** PARA mbylljes — test end-to-end. (F8; parimi edhe në F5 KABI terren)
8. **Moduli "regjistrimi i bllokut"** pas 48h: hyrje manuale e kuponëve të bllokut → outbox (burim=bllok) → dërgim automatik + **afati 5-ditësh me alarm** (UA N45.6). (F5)

## 5. Lidhjet

`ATK_Source_Register.md` (burimet + hash + citimet e amendamentit) · `Kupon_Print_Spec.md` (i njëjti folder) · SSOT §7/§9 · `Reliability_Gate.md` §5.2 · `PM_REVIEW_PROTOCOL.md` (checklist SEF v1.3) · `poc/poc5-sef/ANALIZA.md` §11 (pyetjet Tier 2) · Tracker (`ATK-005`, `ATK-010`, `ATK-011`).

---
*Historia: v1.0 (2026-07-09) — krijimi (PR #17, me aprovimin e Blendit). · v1.1 (2026-07-10) — **ATK normative refresh**: burimet → `ATK_Source_Register.md`; amendamenti 22.06 në fuqi (CRITICAL #3 rishkruar mbi N26.4 e RI; rreshti i ri i eksportit CSV/PDF §2c; KT N25 instalimi i amenduar §2d); §3a kufiri i certifikimit (bounded surface); §3b rregulli i artefaktit të komisionit (me aprovimin e Blendit — PR review = aprovimi).*
