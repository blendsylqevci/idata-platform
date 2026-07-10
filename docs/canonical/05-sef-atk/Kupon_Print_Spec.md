# Kupon Print Spec — kuponi fiskal i shtypur / elektronik (SEF)

**Versioni:** 1.0 · **Data:** 2026-07-09 · **Pronari:** Blend · **Statusi:** canonical pas merge (spec — kodi vjen në F5/F7, shih `ATK_Compliance_Gate.md` §4)
**Burimet (PUBLIKE):** UA (MF) Nr. 01/2026 — Neni 5, 6, 7, 39, 45 · KT Maj 2026 — N25.18, N26.5, N26.12(b), N28.6, Shtojca F, Shtojca SEF, f.86–88.
**Parim:** ky spec mbulon **paraqitjen** (print/PDF/e-kupon); **përmbajtja dixhitale** (proto/firma/QR) = `poc/poc5-sef/ANALIZA.md`. Asnjë vlerë reale identifikuesi këtu — vetëm fusha/kërkesa/config.

---

## 1. Fushat e detyrueshme (UA N7.2 — transkriptim nga PDF publik, f. me Nenin 7)

Kuponi fiskal përmban, **të paktën**:

| # | Fusha (UA N7.2.x) | Shënime zbatimi |
|---|---|---|
| 2.1 | **NUI/Numri Fiskal i tatimpaguesit** | nga entiteti `business` |
| 2.2 | **Numri i TVSH-së** — *nëse tatimpaguesi është i regjistruar për TVSH* | **fushë e KUSHTËZUAR**: config per tenant **"në TVSH: po/jo"** (SSOT §7); kur PO → e detyrueshme; **asnjë vlerë reale në repo** |
| 2.3 | **Emri i tatimpaguesit** | |
| 2.4 | **Adresa e tatimpaguesit** (njësia ku lëshohet kuponi) | |
| 2.5 | **Emri i operatorit + numri identifikues i tij** | operator = user-i i POS-it |
| 2.6 | **Së paku një rresht për secilin artikull** (artikujt e njëjtë grupohen): | |
| 2.6.1 | · sasia | |
| 2.6.2 | · çmimi për njësi | |
| 2.6.3 | · emri i artikullit apo shërbimit | |
| 2.6.4 | · vlera e sasisë së furnizimit | |
| 2.6.5 | · **norma e TVSH-së me një shkronjë specifike** për çdo artikull | shkronjat A/C/D/E (tax groups) |
| 2.7 | **Zbritja/rritja per-artikull**: përqindja (ose vlera) dhe **shuma me shenjën MINUS** | shih §5 |
| 2.8 | **NËN-TOTALI** + zbritja mbi të **me minus** (ose teprica), nëse ka | shih §5 |
| 2.9 | **Shuma totale për pagesë** | |
| 2.10 | **Shumat sipas mënyrave të pagesës** (kesh, kredit/debit kartelë, llogari bankare, vauçer, çek, SMS, etj.) | |
| 2.11 | **Shuma e tatimit sipas normave aktive** | per grup tatimor |
| 2.12 | **Shuma totale pa tatim** | |
| 2.13 | **Data, ora dhe minutat e lëshimit** | |
| 2.14 | **Numri identifikues i SEF**: **`[Numri i Njësisë ARBK]-[NUI]-[PosId]`** (KT N25.18.4a: në krye të kuponit) | formatter = follow-up F5 |
| 2.15 | **Numri DITOR i kuponit** + numri **rendor kronologjik** per POS | gjenerator sekuence DB me reset ditor = follow-up F5 |
| 2.16 | **Kodi QR** | i renderuar e i skanueshëm — shih §3 |
| 2.17 | **NUIKF** — Numri Unik Identifikues i Kuponit Fiskal (për verifikim) | **i lexueshëm në print**; alfanumerik **≤16**, unik (KT N26.10) |
| 2.18 | **Logo – RKS** | + logo fiskale, shih §4 |
| 2.19 | **Tipi i kuponit** (i rregullt, anulim, kthim malli, etj.) | shih §6 |
| 2.20 | **Teksti i lirë**, etj. | + **"e-kupon"** për kuponët nga SEF (UA N7.2.20) |

Kuponi duhet të jetë **i lexueshëm** (KT N25.18.4a).

## 2. Momenti i lëshimit (UA N5–N6) — rregulla biznesi

- **UA N6.1 — HARD GATE (POS, F8) — semantika e saktë:** printimi + dhënia e kuponit është **e detyrueshme në momentin e pagesës**. Para mbylljes së pagesës:
  - **ONLINE (rruga normale):** ATK e ka **pranuar** kuponin + `transaction_id` i **persistuar** + kuponi i **dorëzuar/i printueshëm**;
  - **OFFLINE (vetëm përjashtim, Neni 44):** payload-i fiskal **durable në fiscal outbox** + kuponi me mbishkrimin **"OFFLINE"** + retry/deadline(48h)/alert **aktive**.
- **UA N6.2 — Parapagesat:** kupon fiskal **edhe për çdo pagesë para furnizimit**; modeli duhet të lidhë parapagesën me faturën finale **pa tatim të dyfishtë**.
- **UA N6.3 — Pagesa me afat:** kur furnizimi ndodh e pagesa vjen më vonë, kuponi lëshohet **në momentin e furnizimit**.
- **UA N5.2 — Furnizimet FALAS:** kupon i detyrueshëm edhe për furnizime falas (total 0) — verifikim Tier 2 që serveri e pranon.
- **UA N5.3:** për personat afaristë/juridikë përdoret **fatura tatimore** (B2B) — kalimi kupon→faturë tatimore = pyetje e hapur (§9).

## 3. QR + rendi i transmetimit

- **UA N39.4:** QR = **element përbërës i ÇDO kuponi** — print, PDF, apo elektronik. Test i detyrueshëm (F5+): *asnjë kupon s'del pa QR*.
- **Rendi ONLINE-first (KT N26.5a):** transmeto → ATK pranon → `transaction_id` **persistohet** → **PASTAJ shtyp/dorëzo**. Offline = **vetëm përjashtim** (Neni 44, 48h; kushtet e hard gate-it te §2) — shih §7.
- **Print Preview (KT N25.18 par.1):** para shtypjes ofrohet preview me **QR të skanueshëm pa shtypje**.
- **Dërgimi elektronik (e-kupon) — i KUFIZUAR, jo alternativë universale:** lejohet **vetëm** për furnizime elektronike/në distancë dhe rastet që i lejon ATK, me **pëlqimin/kushtet përkatëse** të konsumatorit; për të gjitha rastet e tjera **printimi/dorëzimi fizik mbetet gate**. **Scope-i i "shitjes në distancë / e-kupon" = vendim OPEN** (SSOT v2.22, pronari: Blend).

## 4. Elementet grafike (KT Shtojca SEF)

- **Logo fiskale**: min **15x8 mm**, max **20x10 mm** — verifikohet me **matje fizike** në printerin termik test (ADR-017).
- **Logo RKS** (UA N7.2.18) + mbështetje tekst+grafikë.
- Formati i paracaktuar i printimit nga ATK (UA N32.2.6) = **për t'u marrë gjatë test phase** (pyetje e hapur, §9).

## 5. Zbritjet (UA N7.2.7/2.8 + KT Shtojca F)

- **Per-artikull:** zbritja shfaqet si **rresht nën artikull me vlerë NEGATIVE** (shenja minus) — përfaqësimi në PosCoupon proto (rresht negativ vs çmim neto + `TotalDiscount`) = **pyetje Tier 2** (§9); guard-i aktual i negativëve në kufirin e parasë do **kualifikohet** sipas përgjigjes (follow-up F5).
- **Në total:** rreshtat **NËNTOTALI → ULJE (me minus) → TOTALI**, me **TVSH të rillogaritur** mbi bazën e ulur (KT Shtojca F).

## 6. Tipet e kuponit (UA N7.2.19 + KT N25.9–N25.11)

| Tipi | Rregulla |
|---|---|
| **I rregullt (Sale)** | rrjedha normale |
| **Anulim (Cancel)** | **arsyeja e detyrueshme** (fushë lokale + shfaqet në kupon; matrica e aprovimeve/PIN) + **referenca** te kuponi origjinal (`ReferenceNo`) |
| **Kthim malli (Return)** | **referenca** te origjinali + kuponi i kthimit merr **NUIKF të RI**; shenja e shumave = pyetje Tier 2 (§9) |
| **Ndërrim artikulli** | = kupon kthimi + kupon shitjeje e re (KT N25.10) |

## 7. Mbishkrimet e detyrueshme

- **"OFFLINE"** — mbi çdo kupon të lëshuar offline (KT N26.12b + N28.6), i dukshëm në print/PDF/e-kupon.
- **"KOPJE E KUPONIT"** — mbi **çdo rishtypje**; vetëm **1 origjinal** per kupon; çdo rishtypje regjistrohet në **audit** (kush/kur/pse) (KT N25.18 par.2-3).
- **"Test"** — mbi çdo kupon të prodhuar me `SEF_ENV=test` (mbrojtje kundër ngatërrimit me kuponë realë gjatë test phase).

## 8. Shtojcë — tabelat zyrtare (KT f.86–88)

> **Manual transcription from public ATK PDF** (faqet 86–88 të KT-së, të renderuara e të transkriptuara me dorë; verifikim: numërim mekanik 31/53 + spot-checks — shih PR body). Vendor-imi në **kod** me checksum + validim = follow-up F1/F5 (`ATK_Compliance_Gate.md` §4.1). Të dy tabelat janë **jo-shteruese** sipas vetë KT-së: *"mund të ketë edhe njësi/kategori tjera që nuk janë në tabelë; SEF duhet të mundësojë konfigurimin"*.

### 8a. Njësitë matëse (31 — KT f.86)

| Nr. | Kodi | Emërtimi | Sqarimi |
|---|---|---|---|
| 1 | EA | Një (njësi) | Pjesë individuale |
| 2 | CP | Copë | Pjesë identike |
| 3 | LTR | Litër | Matje e lëndës në gjendje të lëngët |
| 4 | SEC | Sekondë | Njësi matëse e kohës |
| 5 | MIN | Minutë | Njësi matëse e kohës |
| 6 | HUR | Orë | Njësi matëse e kohës |
| 7 | DAY | Ditë | Njësi matëse e kohës |
| 8 | MON | Muaj | Njësi matëse e kohës |
| 9 | ANN | Vite | Njësi matëse e kohës |
| 10 | KGB | Kilogram bruto | Njësi matëse e peshës me paketim |
| 11 | KGN | Kilogram neto | Njësi matëse e peshës vetëm përmbajtja |
| 12 | TN | Ton | Njësi matëse e peshës |
| 13 | GRM | Gram | Njësi matëse e peshës |
| 14 | M4 | Vlerë monetare | Njësi valutore |
| 15 | UDH | Udhëtim | Udhëtim nga një vend në vendin tjetër |
| 16 | KMT | Km | Njësi matëse e largësisë |
| 17 | LM | Metër linear | Njësi matëse e gjatësisë |
| 18 | MTS | M2 | Njësi matëse e sipërfaqes |
| 19 | MTV | M3 | Njësi matëse e vëllimit |
| 20 | MSK | M/S2 | Njësi matëse e largësisë *(TODO: verify with ATK source — përshkrimi "largësi" për M/S² duket anomali e burimit)* |
| 21 | KËH | Këh | Njësi matëse e Fuqisë *(TODO: verify with ATK source — ka gjasë kWh)* |
| 22 | NFJ | Numri i fjalëve | Njësi matëse e tekstit |
| 23 | NFQ | Numri i faqeve | Njësi matëse e largësisë *(TODO: verify with ATK source — përshkrimi duket anomali e burimit)* |
| 24 | PRQ | Përqindje për njëqind | % |
| 25 | DP | Ditë pune | Njësi matëse e kohës |
| 26 | OP | Orë pune | Njësi matëse e kohës |
| 27 | PRD | Përdorim | Njësi matëse për numrin e përdorimeve |
| 28 | PAL | Palë | Njësi matëse |
| 29 | XBX | Kuti | Njësi matëse |
| 30 | PK | Pako | Njësi matëse |
| 31 | SET | Set | Njësi matëse |

### 8b. Kategoritë e mallrave dhe shërbimeve (53 — KT f.87–88)

| Nr. | Përshkrimi i kategorisë | Emri i kategorisë | Kodi |
|---|---|---|---|
| 1 | Të tjera | Të tjera | TT |
| 2 | Ushqime të përgatitura në restorante dhe kuzhina | Ushqime restoranti | UR |
| 3 | Artikuj ushqimorë nga dyqanet me pakicë | Artikuj ushqimorë | AU |
| 4 | Veshmbathje, rroba dhe këpucë nga dyqanet me pakicë | Veshmbathje | VM |
| 5 | Produkte kozmetike dhe materiale për zbukurim | Kozmetikë | KO |
| 6 | Autopjesë nga autoserviset dhe dyqanet e autopjesëve | Autopjesë | AP |
| 7 | Barna dhe ilaçe mjekësore | Barna mjekësore | BM |
| 8 | Pajisje dhe aparatura të ndryshme mjekësore | Pajisje mjekësore | PM |
| 9 | Pemë dhe perime | Pemë dhe perime | PP |
| 10 | Pajisje elektronike, audio dhe video | Pajisje elektronike | PE |
| 11 | Pajisje shtëpiake si frigoriferë, friza, fshesa elektrike, shporete, pajisje kuzhine etj. | Pajisje shtëpiake | PS |
| 12 | Pajisje dhe makineri bujqësore | Pajisje bujqësore | PB |
| 13 | Barna bujqësore | Barna bujqësore | BB |
| 14 | Karburante, përfshirë të gjitha llojet e naftës dhe benzinës | Karburante | KB |
| 15 | Energji elektrike | Energji elektrike | EE |
| 16 | Pajisje speciale elektrike | Pajisje speciale elektrike | PSE |
| 17 | Shërbime mjekësore | Shërbime mjekësore | SM |
| 18 | Shërbime juridike | Shërbime juridike | SJ |
| 19 | Shërbime të tjera | Shërbime të tjera | ST |
| 20 | Shërbime të kontabilitetit | Shërbime të kontabilitetit | SK |
| 21 | Shërbime mekanike | Shërbime mekanike | SMK |
| 22 | Shërbime transporti | Shërbime transporti | STR |
| 23 | Pajisje dhe makineri industriale | Pajisje industriale | PI |
| 24 | Mjete transporti si autobusë, kamionë dhe vetura | Pajisje transporti | PT |
| 25 | Shërbime edukative dhe arsimore | Shërbime edukative dhe arsimore | SEA |
| 26 | Klube sportive | Klube sportive | KS |
| 27 | Tregtia me pakicë e materialeve ndërtimore | Materiale ndërtimore | MN |
| 28 | Shërbime ndërtimore | Shërbime ndërtimore | SHN |
| 29 | Shtyp dhe dizajn grafik | Shtyp dhe dizajn grafik | SDG |
| 30 | Hoteleri dhe turizëm | Hoteleri dhe turizëm | HT |
| 31 | Shërbime bankare | Shërbime bankare | SHB |
| 32 | Shërbime të teknologjisë informative | Shërbime të TI-së | STI |
| 33 | Shërbime të telekomunikimit | Shërbime të telekomunikimit | SHT |
| 34 | Tregtia me pakicë e luleve, bimëve, farërave, plehrave artificiale, kafshëve shtëpiake dhe ushqimit për to | Prodhimi bimor dhe shtazor | PBS |
| 35 | Tregtia me pakicë e mishit dhe produkteve të mishit në dyqane të specializuara | Mish dhe produkte të mishit | MPM |
| 36 | Miell dhe produkte të miellit si bukë, byrek etj. | Produkte të miellit | PM *(TODO: verify with ATK source — **kodi PM i DUBLIKUAR** me #8 Pajisje mjekësore NË VETË PDF-in zyrtar; pyetje e hapur §9)* |
| 37 | Prodhimi i produkteve të drurit | Produkte të drurit | PPD |
| 38 | Prodhimi i produkteve të hekurit | Produkte të hekurit | PPH |
| 39 | Prodhimi i produkteve të plastikës | Produkte të plastikës | PPP |
| 40 | Prodhimi i produkteve të aluminit | Produkte të aluminit | PPA |
| 41 | Pajisje hidraulike dhe të ngrohjes | Pajisje hidraulike dhe të ngrohjes | PHN |
| 42 | Shërbime hidraulike | Shërbime hidraulike | SHH |
| 43 | Agjenci udhëtimi, shërbime udhëtimi dhe akomodimi | Shërbime udhëtimi dhe akomodimi | SUA |
| 44 | Sallone floktarie dhe trajtime të tjera të bukurisë | Shërbime të zbukurimit | SHZ |
| 45 | Pije joalkoolike | Pije joalkoolike | PJA |
| 46 | Pije alkoolike | Pije alkoolike | PAL |
| 47 | Duhan | Duhan | DUH |
| 48 | Agjenci të publicitetit dhe marketingut | Shërbime marketingu | SHM |
| 49 | Veprimtari prodhuese | Prodhimtari | PRO |
| 50 | Shpërndarja e filmave, videove dhe programeve televizive | Shpërndarje filmash, videosh dhe programesh televizive | SFV |
| 51 | Blegtori dhe shpezëtari | Blegtori dhe shpezëtari | BSH |
| 52 | Riparimi i pajisjeve të komunikimit | Riparimi i pajisjeve të komunikimit | RPK |
| 53 | Pajisje të teknologjisë informative si PC dhe laptopë | Pajisje të teknologjisë informative | PTI |

*Shënim ndër-tabelor:* kodi **PAL** ekziston edhe te njësitë (#28 Palë) edhe te kategoritë (#46 Pije alkoolike) — validimi në kod duhet të jetë **per-tabelë**, jo global.

## 9. Pyetje të hapura për Tier 2 / ATK (regjistrohen — NUK zgjidhen këtu)

1. **NUIKF**: burimi/formati i saktë (kush e gjeneron — SEF ynë sipas algoritmi apo jepet nga ATK; lidhja me `VerificationNo` në proto).
2. **Return**: shenja e shumave (pozitive me `Type=Return` vs negative)?
3. **Zbritja per-artikull në PosCoupon**: rresht negativ vs çmim neto + `TotalDiscount` agregat?
4. **QR i kuponit të pafiskalizuar** (offline, para `transaction_id`): çfarë përmban saktësisht?
5. **Semantika e `Time`** në fiskalizim të vonuar: koha e LËSHIMIT vs koha e DËRGIMIT?
6. **Lidhja e 53 kategorive me `CouponItem.Type`**: a është 'TT' kod kategorie nga kjo tabelë?
7. **Formati i paracaktuar i printimit** nga ATK (UA N32.2.6): merret gjatë test phase.
8. **X/Z raporti ditor**: kërkesat për SEF softuerik (formati, detyrimi).
9. **Refund payout** (kthimi i parave te klienti): rrjedha fiskale e pagesës negative.
10. **Kalimi kupon→faturë tatimore B2B** (UA N5.3): procedura kur klienti kërkon faturë pas kuponit.
11. **Kodi PM i dublikuar** në tabelën zyrtare të kategorive (#8 vs #36): si e trajton serveri; cili kod përdoret për "Produkte të miellit"?

## 10. Lidhjet

`ATK_Compliance_Gate.md` (gate-t + follow-ups) · SSOT §7 (rendi ONLINE-first) · `poc/poc5-sef/ANALIZA.md` (proto/firma/QR + §11 Tier 2) · Tracker `ATK-005`+ (pyetjet formale).
