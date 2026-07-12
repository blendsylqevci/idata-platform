# iData+ — Single Source of Truth (SSOT)

**Versioni:** 2.25 · **Data:** 12 Korrik 2026 · **Statusi:** Draft pune për Fazën 0
**Pronar i dokumentit:** iCode L.L.C (Pronari / Human Gate)
**Rregull:** ky është **burimi i vetëm i vërtetë** për zhvillimin. Çdo vendim/feature i ri shkohet **vetëm këtu** (me datë te Decisions log). Dokumentet e aprovuara jashtë repo-s janë vetëm snapshot-e aprovimi të gjeneruara nga ky dokument. Faza aktuale: **dizajn + Faza 0**, pa kod prodhimi pa go-ahead.
**v2.0:** integron raundin v2 të pyetjeve me Codex (Qershor 2026) — vendime të reja, pyetje të zgjidhura, dhe 2 konflikte të shënuara (paga, printera fiskalë). Sinkronizuar me numërimin e Codex (v2).
**v2.1:** korrigjime nga rishikimi i Pronarit — frazë neutrale për bërthamën e ripërdorshme, ndarje rolesh + break-glass, RBAC pa "Fshirje" për financiare, fazim i qartë i SEF, Libri i Shitjes/Blerjes baseline nga PRO DATA, COGS i finalizueshëm me kontabilistin, [51] paraprak, ARBK te Onboarding, KDS ~10–12.
**v2.2:** VENDIM — **pagat/payroll = Faza 2** (jo v1), sipas rekomandimit të Codex, konfirmuar nga Pronari. **(RISHIKUAR v2.17 → Faza 1 për KABI/kontabilist.)**
**v2.3:** gati për Fazën 0 — paketa Faza 0 te `docs/canonical/01-phase0/` (+ `00-ssot/`); Node 24 LTS; +2 pyetje (floor-plan, inventar pajisjesh); decision gates shtesë; baseline sigurie; feature-status taxonomy.
**v2.4:** 🟢 **SEF "Testim para Aplikimit" PRANUAR nga ATK (28.06.2026)** — test phase aktiv; protokolli zyrtar github.com/fiskalizimi (mandatory); ADR shtesë; data model shtesë (business_group/service_day/cost_layer); paketa Faza 0 → v1.1.
**v2.5:** korrigjime teknike SEF (verifikuar nga pos-csharp + models.proto): **money scale PER-FIELD** (items €0.0001 / totals cents) **[SUPERSEDED v2.25: `CouponItem.Total` = ×100 cents — Gate v2 §3]**, **CouponType Sale=1/Cancel=2/Return=3**, **tax A/C/D/E** (TL→A); secrets private-local (jashtë git); paketa Faza 0 → v1.2.
**v2.6:** **template-et zyrtare ATK** (blerje+shitje) nga kontabilisti (28.06) → box-map i plotë për të dy librat; auto-deklaratë TVSH e mundshme; ruajtur te `docs/evidence/reference_atk/`.
**v2.7:** PRO DATA **Wave 2 (PLANET POS/KDS)** review nga Codex → entitete të konfirmuara (table_session, kitchen_ticket, kds_station, cash_shift, worker_settlement, manager_approval); dizajni ynë i barazimit/anulimit konfirmohet net-new & më i rreptë.
**v2.8:** PRO DATA **Wave 3 (audit/RBAC/config, sanitized)** krye → **nxjerrja nga backup-i E MBYLLUR** (gaps 0); evidence → `07_PRODATA_Evidence_Closure_Parity_Matrix.md`. Pa migrim passwords/PIN/MAC; audit append-only + break-glass + config typed.
**v2.9:** **Finance Operations e ndarë nga Accounting** → modul/rol i veçantë (Financë Operative); doc `08_Finance_Operations_SCOPE`; entitete expense/cash_movement.
**v2.10:** Finance Ops **backup audit** (Codex) — A/B gaps 0; `08` u bashkua **canonical v2.0**; **RESORT = DB reale POS-aktiv** (~429K pagesa) ≠ PLANET2; 3 micro-extract opsionale; entitete finance shtesë.
**v2.11:** Finance Ops **DDL/DML cross-check** (0 missing DDL, 3382 refs) + 4 micro-extract → evidence i mbyllur; **`09_Finance_Operations_Workflow_Spec`** (flukse per-ekran + paketa e pyetjeve live për PLANET).
**v2.12:** Codex workflow spec (FO-00..FO-13) harmonizuar; **`Prompt_FinanceOps_UI_Wireframe`** për Claude Code Design (dashboard SaaS, jo website); entitete shtesë (finance_day/float_assignment/pos_terminal_batch/finance_handover) + verbe RBAC (Eksport/Mbyllje/Rihapje).
**v2.13:** VENDIM — hapi tjetër i implementimit = **repo assembly + walking skeleton + accounting library-first** (jo module/UI/KABI/SEF-full ende); runbook gati: `Repo_Assembly_Walking_Skeleton_Runbook_2026-06-30.md`.
**v2.14:** **Përgjigjet e kontabilistit (30.06):** PLANET shitjet = **të gjitha 18%** (biznes lluksi); blerje **mixed-VAT per rresht** konfirmuar; librat **harmonizuar 1:1 me ATK**; qershori s'është deklaruar ende (afati 20.07) → parity me një muaj më të hershëm të deklaruar; KABI fermerë/produkte ende hapur.
**v2.15:** **VALIDIM PARITY MAJ 2026 (ACC-010, Q#4 RESOLVED)** — libra+deklaratë zyrtare PLANET u krahasuan me engine-in; input VAT dhe baza 18% përputhen; output ka vetëm diferencë të vogël rounding/reporting; carry-forward u konfirmua; **engine tax-book = bucket-based**; exact values dhe deklarata zyrtare ruhen jashtë git si evidence-private-local. Rounding-rule ende i hapur.
**v2.16:** **USAID Formula Validation Gate (04.07)** — formula-gate teknik i validuar + merge në `main` (PR #1); struktura në git, parametrat realë private-local (ACC-010 → closed). **Dedup Finance Ops:** SCOPE canonical = `01-phase0/08`, WORKFLOW canonical = `04-planet-finance/Finance_Operations_Workflow_Spec_2026-06-29.md` (mban FO-00..FO-13); të tjerët → superseded me pointer. **Data Model:** u shkrinë 3 entitete (`posting_run`, `vat_declaration_snapshot`, `partner_card_entry`) nga phase0-pack te `04_Data_Model_Draft`. **Audit consistency (05.07):** hequr emri real i pronarit (PII) → rol; metadata reale maskuar; tracker/registers freskuar (REPO-001/002/003 → closed); referenca private-local të shënuara; kontradikta të zgjidhura/loguara.
**v2.17:** **KABI operational evidence + accountant confirmation (05.07)** (shih `03-kabi/KABI_Operational_Evidence_Update_2026-07-05.md` + `03-kabi/Accountant_Confirmation_TVSH_e_Sheshte_Fermere_KABI_2026-07-05.md`). Përgjigjet e KABI-t mbyllën **KABI-002/003/004/005** + **ACC-005/006**. **Formula USAID = bazë ZYRTARE e pagesës** (jo vetëm raportim). **PAGA/KONTRIBUTE → FAZA 1** (KABI/kontabilist detyrueshëm) — RIKTHEN vendimin v2.2. ⚠️ *Sqaro scope-in me Pronari/Codex: payroll engine i plotë vs vetëm master-data punonjës + kontribute — ndikon kosto/scope.* **TVSH flat-rate 8% për fermerë** — bazë ligjore **UA 03/2015 neni 76**; autofaturë nga KABI + **NIF** + certifikatë **MBPZHR**; posting/libër-blerje pret sign-off kontabilist (ACC-006).

## Përmbajtja
1. Pamje e përgjithshme & komerciale · 2. Arkitektura & teknologjia · 3. Plani Faza 0→Lansim · 4. Kontabiliteti · 5. KABI · 6. PLANET · 7. SEF/Fiskalizimi · 8. Migrimi & Cutover · 9. Siguria & RBAC · 10. Dizajni · 11. Decisions log · 12. Pyetjet e hapura · 13. Glosar & model i të dhënave

---

## 1. Pamje e përgjithshme & komerciale
- **iData+** = platformë **multi-company / multi-tenant** për Kosovë; **një produkt i vetëm**, jo dy.
- Bizneset: **KABI** (fabrikë qumështi + fermerë + prodhim + stok automjeti + distribuim) dhe **PLANET** (restorant + POS + KDS + stok + kontabilitet).
- Gjuha e UI: **Shqip** (i18n për anglisht/serbisht më vonë, pa ndryshim kodi). **KABI hyn live i pari**, PLANET paralel pas tij. Events/banketet **jashtë v1**.
- **Bërthamë e ripërdorshme + emërtim neutral:** platforma ndërtohet me core të ripërdorshëm dhe emër neutral; **çdo ripërdorim jashtë projektit trajtohet sipas kushteve komerciale/IP**. Çdo entitet tenant-scoped lidhet me `business_id`.
- **Status për çdo feature:** **MVP / Later / Roadmap / Requires-accountant / Requires-ATK / Requires-factory** — regjistri: `docs/canonical/00-ssot/Feature_Status_Register.md`.
- **Komerciale:** jashtë repository-it teknik; ruhen vetëm në dokumente private të pronarit/iCode dhe në materialet ligjore konfidenciale.

## 2. Arkitektura & teknologjia
- **Fortified Hybrid Stack:** NestJS (truri i biznesit) + Next.js (web) + **React Native/Expo (aplikacion native, App Store/Play Store)** për mobile (KABI terren + PLANET kamarier) + **PostgreSQL** (burimi i vetëm) + **Supabase** (Auth/MFA, Storage, Realtime, RLS safety-net). **PWA përjashtohet** (vendim i mbyllur — duhet native për offline + printer BT + komunikim me hub lokal).
- **Rregulla:** frontend-i NUK shkruan direkt në tabela biznesi; logjika financiare në NestJS; **RLS = safety net**, jo vendi i logjikës; `business_id` në çdo tabelë tenant-scoped (jo në tabela globale/system/reference).
- **Hosting = të dyja:** rajon **EU** (managed/self-host) **+ servera on-prem te restoranti**. **Hub lokal i detyrueshëm** (server real on-prem).
- **Offline-first:** përvojë **e padallueshme online/offline**, e shënuar qartë kur është offline; pa humbje, pa dublim (UUID + outbox + idempotencë).
- **Shkallë:** 60–80 kamarierë **konkurrentë** çdo ditë (BYOD) → realtime + hub on-prem duhet ta mbajnë. **BYOD vetëm në Wi-Fi zyrtar** (IP whitelisting); ndërfaqe responsive.
- **Hub lokal & ruajtja:** Mini-PC/NUC + **UPS** (1–2h), **Docker + PostgreSQL** lokal te PLANET **dhe** te KABI (laborator + peshore s'duhet të bllokohen). Mobile: DB lokale **SQLite/Realm**, ruajtje offline **3–5 ditë** (afati ligjor i dërgimit = 48h).
- **Rregullat e konfliktit (offline→sync):** (a) i njëjti stok shitet dy herë → prano shitjen, lejo **stok negativ të përkohshëm** në nivel automjeti/depoje, rregullo në sync; (b) çmim i ndryshuar offline → çmimet **të ngrira** në pajisje (ndryshim vetëm nga cloud), faturat pranohen siç janë lëshuar (të fiskalizuara) + **Raport Variance Çmimi**; (c) klienti kalon limitin e kredisë → prano faturën (s'kthehet pas shtypjes), vendos klientin **"I Bllokuar"** në sekondën e parë të sync-ut.
- **Para:** `numeric` për GL (precizion i lartë); **SEF money scale PER-FIELD (korrigjuar 12.07.2026 — Gate v2 §3):** unit `CouponItem.Price` = ×10000; **item `CouponItem.Total` = ×100 (cents)**; coupon `Total`/payments/tax/totals = ×100; golden €1.50×3 → Price=15000, Total=450; decimal në TS; **tabular-nums** në UI. Kurrë float.
- **Hardware (klienti blen + zotëron; iCode specifikon + konfiguron):** **KABI** — PDA Android industriale me skaner (Sunmi/iData) + printer termik mobil (BT/Wi-Fi, rezistent); tableta **IP65** për laborator/pranim; peshore me integrim **RS232/LAN** (lexim automatik). **PLANET** — **~10–12 ekrane KDS** industriale (ose monitor + mini-PC; numri saktë finalizohet pas floor-plan), pajisje kamarieri (Sunmi M2/iData) + telefona personalë, printera termikë LAN/Wi-Fi (arka) + buzzer (kuzhinë backup), **Mini-PC/NUC hub + UPS**. Pajisjet ekzistuese ripërdoren nëse plotësojnë minimumet (8GB RAM, Win10/11 ose Linux, printera LAN/USB standardë); klienti jep inventarin, iCode inspekton.
- **Onboarding / Master Data / Integrations:** **ARBK auto-import** — butoni "Kërko subjektin" → faqja ARBK → një klik importon të dhënat e subjektit; e njëjta logjikë për **biznese EU** (regjistrat EU). *(Feature onboarding-u/master-data — jo siguri.)*
- **Decision gates (Faza 0):** Supabase hosted vs self-host EU; PowerSync vs custom-sync (pas PoC); sa GL-postim në Postgres functions vs NestJS; connection pooling; residenca/backup + **PITR/restore drill**; **DB e hub-it lokal: Postgres vs SQLite/embedded**; **strategjia e build-it RN/Expo (EAS vs bare)**; printera fiskalë vs SEF software (#8). *(RN vs PWA — MBYLLUR: native RN. Node = **24 LTS**, fiksuar me `.nvmrc`.)* → regjistri i plotë: `docs/canonical/01-phase0/05_Decision_Gates_ADR.md`.

## 3. Plani Faza 0 → Lansim (me kritere pranimi)
| Faza | Fokusi | Kriteri i pranimit (DoD) |
|---|---|---|
| 0 · Discovery & Themeli | mbyllja e pyetjeve, vendimet, **5 PoC**, modeli i të dhënave, mjediset, repo+Notion | pyetjet/PoC OK; stack & emër aprovuar |
| 1 · Foundation | monorepo, CI/CD, auth, multi-company, RBAC, audit, monitorim | user hyn; sheh subjektin sipas qasjes; të dhënat të ndara; teste bazë |
| 2 · Accounting Core | plan kontabël, motori i postimit, AR/AP, arkë/bankë, TVSH, saldo hapëse, pasqyra | debit=kredit; libra dalin; TVSH llogaritet; periudha mbyllet |
| 3 · Stok + Recetura + COGS | multi-depo, lëvizje, normativ/skrap, COGS, FEFO/LOT | lëvizjet postojnë stok+GL; COGS saktë; FEFO/LOT |
| 4 · KABI Operacional MVP | fermerë/Punki, pranim qumështi, prodhim/batch, stok | qumështi→batch me afat; shitja zbret stok; GL/TVSH/AR postohen |
| 5 · Offline + SEF → ★ KABI LIVE | mobile/outbox, fiscal outbox, **SEF: aplikim formal + certifikim**, stok automjeti/POD | ambulanti offline pa dublim; fiskalizim brenda rregullave; **KABI në prodhim** |
| 6 · KABI Advanced | raft-control, marrëveshje komerciale/konsignacion, ATP/rezervim, gajbe, KPI | vizita/POD; konsignacioni & risku; KPI |
| 7 · PLANET Foundation | menu/artikuj, tavolina/zona, POS bazë, arka, stok/recetura, furnitorë + hub PoC | POS hap/mbyll; recetura zbret stok; hub PoC |
| 8 · PLANET POS/KDS → ★ PLANET LIVE | POS live, KDS per-stacion, financa/saldo, multi-shank | porosia→KDS në LAN pa internet; Z-raporti rakordon; **PLANET në prodhim** |
| 9 · Hardening + Cutover + Certifikim + Pilot | security audit, opening balances, load test, backup drill, certifikim SEF, trajnim, pilot | cutover aprovuar; SEF e certifikuar; pilot pa bllokues |
| 10 · Lansim | go-live, rollback, monitorim, rakordim ditor, handover | prodhim i plotë; handover i nënshkruar |

- **Afati i synuar KABI live ≈ 4 muaj** (minimum real): ~2 muaj bërthamë + 1 muaj integrime/offline/SEF + **1 muaj test paralel** me PRO DATA. Përputhet me cutover-in **1 Jan 2027** (test paralel ~dhjetor → libra të pastër 1 janar).
- **"KABI stabil"** = Shitje + Stok + **Kontabilitet bazë** + **SEF/Offline** + **Ambulant (Van Sales)** 100% stabile; prodhimi i plotë/laboratori i avancuar mund të nisë me **deklarime manuale më të thjeshta** dhe të piqet në hapat vijues.
- **PLANET në fillim:** dizajn + arkitekturë e plotë **tani** (multi-tenant/RBAC); ndërtim paralel vetëm i **paketave të përbashkëta** (katalog artikujsh, klientë, motor sinkronizimi); logjika specifike (KDS, split-bill, ekrane pulzuese) **më vonë**.
- **5 PoC (porta e provës, Faza 0):** SEF sandbox · offline sell→fiskalizim · hub lokal LAN pa internet · kontabiliteti faturë→GL→ATK · tenant isolation.
- **SEF (fazim i qartë):** **Faza 0** = sandbox + PoC; **Faza 1–2** = fiscal flow minimal; **pas "Testo para Aplikimit" / Tier 2 të suksesshëm** = aplikim formal (vendim ATK brenda 15 ditëve pune; **testi teknik i Komisionit = hap formal PAS aplikimit**, Gate §2e); **para KABI live** = certifikimi i kryer. (Track paralel — s'bllokon fazat e tjera.)
- **Workflow:** Claude Code ndërton · Codex PM/verifikues (repo+teste) · Pronari (Human Gate) = porta njerëzore te SEF/GL/migrim/siguri. Teste + CI + "Definition of Done" për çdo fazë. **Faza 0 = 1–2 javë** dokumentim + PoC + setup para kodimit serioz; "Walking Skeleton" para logjikës së rëndë.

## 4. Kontabiliteti
- **Një bërthamë sipas ATK** për të dy bizneset; pastaj feature specifikë. Shqip.
- **Plan kontabël** (referencë PRO DATA: PLANET 142/145 llogari; KABI ndërton të vetin, i njëjti standard + dairy; **zgjerim në llogari kosto/Klasa 9** për lëndë të parë qumësht, ambalazh, gjysmë-produkte). Klasat: Aktive(1) · Detyrime/Kapital(2) · Të hyra(4) · Shpenzime(6/7) · Rezultat(8). Llogari kyçe: 10000 arka, 11000 blerës(AR), 12000 malli, 13000 TVSH-blerje, 21200 TVSH-doganë, 22000 TVSH-shitje, 40000 të hyra.
- **Motori i postimit** (skema e kontimit): dokument → GL (debit=kredit) + libra + nën-libra. AR/AP, arkë/bankë, alokim pagesash, aging. **90–95% postim automatik nga dokumenti**; manual vetëm kur s'ka faturë standarde; **kontabilisti lejon journal manual për korrigjime**.
- **TVSH:** kode **T0/T8/T18/TL** (T1=16% historik). **COGS:** default **mesatare e ponderuar**; KABI mban **batch costing/yield** për prodhim; **finalizohet me kontabilistin**.
- **Pasqyrat financiare** (Ligji 06/L-032): bilanci, PASH, cash flow, ekuiteti. **Pagat/payroll = Faza 1** (RISHIKUAR v2.17 — KABI/kontabilist detyrueshëm; dikur v2.2 = Faza 2). **Asetet fikse/amortizimi = Faza 2.**
- **Librat e ATK:** Libri i Blerjes (format zyrtar i verifikuar + **harta e kutive të Deklaratës**: Bazë18→[43], TVSH18→[K1], Bazë8→[45], **Fermerë normë e sheshtë→[51]**, Totali→[67]) + Libri i Shitjes (**template zyrtar ATK i marrë 28.06 + baseline PRO DATA**; box-map shitje: [9]/[10a-c]/[10]/[11] të liruara, [12]→18%, [14]→8%, [K1]/[K2], Totali [30]) + **Deklarata e TVSH-së automatike** nga librat. Kartela analitike; librat online ATK; formulari i TVSH-së (tregon nëse je në **kreditim**).
- **Fermerët:** skema e **normës së sheshtë** [51] (e vërejtur te Libri real i PLANET; **aplikimi për KABI = drejtim paraprak — konfirmohet me kontabilistin/ATK**); dokumenti = **akt-blerje me normë të sheshtë**.
- **Banka/arka rakordohen brenda sistemit.** Dokumentet financiare **kurrë s'fshihen** — vetëm anulim/korrigjim/notë krediti (aprovuar 100%).
- **Raporte:** për subjekt + konsoliduar + me filtrim (datë/depo/qendër kostoje/klient).

## 5. KABI (vertikali i qumështores)
- **Pranim qumështi:** dropdown i rrugës — fermer / punëtor KABI / **Punki**. **Moduli QC/Laborator** regjistron per pranim: litra/kg, **yndyrë%**, temperaturë, aciditet/pH, **SNF**, **antibiotikë (Pass/Fail → bllokim automatik)**. Matja **per dorëzim (per fermer)** = matja zyrtare për pagesë; cilësia e tankut = **mesatare e ponderuar**. Fletë-pranimi rri **"Në pritje analize"** derisa teknologu/QC plotëson parametrat → **aprovim/refuzim automatik** sipas limiteve (jo magazinieri).
- **Tank 10,000L i përzier** (Bulk Reception) → **gjurmueshmëri në nivel tank/ditë** (jo për fermer); recall = pool/batch ditor.
- **Punki (model shlyerjeje):** Punki mbledh nga fermerët dhe sjell **listë per fermer (litra secili)**. **KABI paguan Punkin** (totali **+ komision**); **Punki paguan fermerët**. KABI **mban kartelat per fermer** (Punki s'ka sistem). Detyrimi → te Punki; gjurma per fermer ruhet. Komisioni mund të jetë **% ose tarifë fikse per litër**; vlera reale konfirmohet jashtë git me KABI.
- **Teknologu** vendos **alokimin ditor** të qumështit via **Urdhër Prodhimi** (jogurt/djathë/kos/ajran/gjizë). **Pasterizimi = fazë CCP e detyrueshme** (p.sh. 72–75°C/15s): pa regjistrimin e parametrave, sistemi **bllokon** fazat vijuese. Mbështet **MTS** (produkte ditore) **dhe MTO** (porosi speciale — kërkon Sales Order para urdhrit).
- **Prodhimi:** batch/LOT (HACCP, seri unike per cikël); normativ/BOM me **humbje normale %** (p.sh. 1.5% brenda kostos) — **tejkalimi = Scrap Expense manual**; **yield plan-vs-real (Yield Variance Report)**; nën-produkte/refuz = **Grade B / kod i veçantë** (hirra/whey, ajka, qumësht për kafshë); afat skadence auto = data prodhimit + jetëgjatësia (në barcode); ~7 L → 1 kg djathë.
- **Produkti final:** etiketa/barcode/afat skadence; **catch-weight** (djathi: shitet copë, faturohet kg, peshë reale në etiketë); multi-UoM (L↔kg↔copë↔pako↔gajbe); **FEFO**. Emrat komercialë **ruhen**; kodet **standardizohen (SKU, p.sh. PROD-JOG-001)**. Data governance: barcode/EAN = produkt/marketing; njësi/paketim = teknolog+magazinier; çmim/TVSH = financa/shitje; afat = laborator.
- **Stok automjeti (depo mobile):** ngarkim me LOT; shitje/faturim në terren; cash/borxh/kthime; **LOT në rresht fature**; FEFO; **POD (nënshkrim/foto/GPS)**; check-in/out ditor (mëngjes ngarko → mbrëmje depo në zero) + rakordim (ngarkuar−shitur−kthyer−mbetur = cash + fatura kredie); **offline**. Shitje cash + afat; **app kontrollon limitin e kredisë** → bllokon mall të ri me kredi nëse ka borxh të vonuar (vetëm cash). **Raft-control: GPS check-in + foto rafti** (merchandising).
- **Gajbe/ambalazh (E5 — ZGJIDHUR):** **"stok ambalazhi te klienti"**; fletë-dërgesa regjistron produkte + gajbe **dalëse**; vizita tjetër regjistron gajbe **kthyese**; vlerë pengu + rakordim; faturim nëse s'kthehen.
- **Shlyerja e fermerit (fund muaji):** formula **USAID** e programuar në sistem — çmim bazë/litër ± centë sipas **yndyrës%** dhe **baktereve/qelizave somatike** → **klasa (Ekstra/I/II/III)**; tabela e saktë *pending nga fabrika*. Regjistrim ditor i litrave (fletë-pranime, pa detyrim GL) → **faturë e vetme përmbledhëse** në fund muaji. Avanset (parablerje, debitor) **zbriten automatikisht**. **Raporti i Grumbullimit** i printueshëm (sasi/cilësi + financa: çmim, total, avans i zbritur, pagesa, balancë) — për **AUVK** + transparencë me fermerin.
- **Konsignacioni (E4 — VENDIM):** për qumështore të freskëta **shmanget konsignacioni klasik** → **shitje direkte me të drejtë kthimi** (skadenca → notë krediti vizitën tjetër). *(Njohja kontabël — konfirmohet me kontabilistin.)* Marrëveshje komerciale; kredi/aging.

## 6. PLANET (vertikali i restorantit)
**POS / Kamarieri (aplikacion native RN):** porosi per tavolinë; **dërgo të gjitha / një-nga-një / zgjedh cilat**; shto artikuj; komente + **notes (të gjithë shtojnë)**; **totali i fshehur** derisa shtypet "Shfaq faturën/Paguaj" → pastaj **bllokohet** (rihapje = aprovim menaxheri/pronari, përndryshe vetëm paguaj+mbyll); **transfer tavoline = menaxheri, në çdo orë**; ndrysho ushqim/pije + **anulim me arsye = aprovim**; ndarje fature; cash/kartë; **zbritje me PIN**; **paguaj të gjitha ose një-nga-një**; **faturë e rregullt + dërgim email**; **plan tavolinash si pamja reale**; **tavolina hot** (telefon+desktop); **3 stacione rezervë te banaku** (nëse bie mobi); **60–80 konkurrentë**.
**Ndërrimi/barazimi:** mbyllje **automatike 00:00**; pas 00:00 ID e kamarierit hyn me **saldo të re**, porositë me datën e re; tavolinat e hapura **transferohen te kamarieri i ri** (ai ngarkohet, i larguari kreditohet); **pamja e barazimit të puntorit:** tavolinat = porositë që ka bërë, dhe brenda secilës tavolinë porositë e saj.
**Financat — Finance Operations (MODUL/ROL I NDARË nga kontabiliteti — Financë Operative; shih `08_Finance_Operations_SCOPE`):** ekran me cash në zyrë / saldo / **saldo per kamarier**; barazimi = **POS/banka + shpenzime → cash që duhet dorëzuar**; kur Group Owner merr cash → **dokumentohet kush e mori + arsyeja**; **identifikim banke/POS** (cili terminal); **shpenzime operative**; **pagesa furnitorësh (ops)**; **raporte menaxheriale ditore**. **Handoff:** kamarieri mbyll → finance barazim → accounting poston. Financa operative = para-GL; kontabiliteti = GL/ligjor (ekrane të ndara).
**KDS:** **~10–12 ekrane** (stacione: Grill, Pizza, Sushi, Sallata, Bar, Kuzhina + Expo + pizzeria 2-ekrane + backup; numri saktë **finalizohet pas floor-plan/vizitës**) — secili sheh vetëm vetveten; **route artikulli→ekran**; **pizzeria 2 ekrane** (pizzamen konfirmon → ekrani i kamarierit **blink**, **i kuq pas 2 min**); çdo porosi: kush, koha, tavolina, notes; status (e re/në punë/gati); timer; **Expo**; **backup në printer termik nëse ekrani s'konfirmon brenda ~30s**; **ekrani i shankut** me 3 statuse (Porositur→Në proces→Dorëzuar); **inspektori i kuzhinës** (porositë+tavolina+kamarieri+ora; njoftimi te kamarieri kur gati); **Kitchen Queue Monitor** (ngjyra sipas artikujve: jeshile 0–10/verdhë 11–20/portokalli 21–30/kuqe 31+); **rregull pizzerie:** >40 **tavolina** → e kuqe.
**Stok (multi-depo/multi-shank):** **depo vs restorant veçmas**; **depo ngarkon shankun**; **2 shanka** (banaku i jashtëm: porosia→fatura te banaku i jashtëm; Group Owner e aktivizon/çaktivizon); recetura **zbresin stokun automatikisht** + kosto reale; transfer depo→kuzhinë/bar; min-stock alert; korrigjime me arsye.
**Printera/lokacione:** multi-printer; **lokacion→printer** routing; lokacionet sipas planit real; **të lidhura me tavolinat**.
**Çmimet me TVSH** (PLANET: të gjitha shitjet **18%** — biznes lluksi, konfirmuar nga kontabilisti 30.06); kategori per produkt; modifiera (medium/well done/pa qepë/extra sos); njësi copë/kg/litër.
**Raporte:** **filter datash + per produkt** (sa copë + vlera); sipas kamarierit/tavolinës/artikullit; top; anulime; zbritje; stok kritik; borxhe furnitorësh.
**Realtime/alarme:** pasqyrë tavolinash të hapura; porosi **>30 min → e kuqe** për admin/menaxher.
**Premium:** Universal Search, Timeline tavoline, "What happened?" per faturë, Smart Notifications, Daily Snapshot, Dashboard pronari, **Dark/Light**. KDS zëvendëson printerat termikë (printera = backup); **asnjë porosi s'humbet**.
**Furnitorë/fatura hyrëse:** regjistrim, kartelë, fatura me **foto/PDF**, pagesa, borxh, histori; hyrje automatike në stok.
**Roadmap (jo v1):** AI Invoice Scanner; Smart Product Matching (furnitor↔artikull).

## 7. SEF / Fiskalizimi
- **iCode aplikon** si ofrues i softuerit SEF (Software House — jo secili biznes); merr Certifikatë + Numër Identifikues i SEF-it. Secili NUI/POS/PKI regjistrohet sipas ATK. KABI/PLANET = shfrytëzues të softuerit të certifikuar.
- **🟢 STATUS (09.07.2026):** **aplikim i RI për test phase u regjistrua** dhe është **referenca aktuale për Tier 2**; aplikimi i mëparshëm (28.06.2026) mbetet vetëm **historik/superseded**. **Identifikuesit private-local, jashtë git.**
- **Referenca teknike: github.com/fiskalizimi + Swagger v0.9** — **org-u ka KONFLIKTE të regjistruara (asnjë implementim i vetëm s'është kanonik në heshtje):** firma C# P1363 vs Go DER (OPEN — server vendos, T2-07); PHP proto i vjetër; Ruby pa proto (Register v2 §4–5). Kontratat e sakta (Gate v2 §3): **QR = `base64(CitizenCoupon proto)|base64(firma e CitizenCoupon)`**; tax **A/C/D/E**; **money: unit Price ×10000, item Total + totals/payments/tax ×100**; **CouponType Sale=1/Cancel=2/Return=3** +ReferenceNo; `transaction_id` = uint64 ekzakt. Test server `fiskalizimi-test.atk-ks.org`.
- **🔴 VERDIKTI AKTUAL (full-corpus re-audit, cutoff 12.07.2026):** PR #16 = **evidence historike mock**; **Tier 1 overall PARTIAL/REOPENED · Tier 1 compliance FAIL** (5 CONTRADICTS-rreshta = 4 defekte unike; LIVE placeholder = MISSING/`T2-04`) · **Tier 2 NOT IMPLEMENTED/NO-GO** · certifikimi **NO-GO**; regression run = 66 PASS/1 LIVE SKIP (vetëm mekanika lokale mock). **Sanitizer-i aktual ≠ standard i pranueshëm redaction; preflight wrapper-i evidence-safe ENDE S'EKZISTON** (ndërtohen në Step B/C). Burimet: `05-sef-atk/ATK_Source_Register.md` v2.3 (bounded registered corpus) · statusi: `05-sef-atk/ATK_SEF_Requirements_Ledger.md` v1.6 (**132 source-rows · 46 closure keys · 45 konfirmime · disposition substantiv 1–123**) · verdikti/gates: `05-sef-atk/ATK_Compliance_Gate.md` v2.0. **Sekuenca (rendi i detyrueshëm):** Step B (T2-01..10) → Step C (vectors/tests) → independent re-review → **HUMAN GATE (autorizimi për thirrjen e PARË reale)** → **private `/ca/verify` PREFLIGHT** → vetëm pas suksesit vazhdon pjesa tjetër e Tier 2. **Asnjë `/ca/*`, `/pos/*`, `/citizen/*` para Human Gate.**
- **Fazim:** Faza 0 = **test phase AKTIV + PoC** → Faza 1–2 = **fiscal flow minimal** → **pas "Testo para Aplikimit" / Tier 2 të suksesshëm** = aplikim formal (vendim ATK brenda **15 ditëve pune**; **testi teknik i Komisionit = hap formal PAS aplikimit** — Gate §2e) → **para KABI live** = certifikimi i kryer.
- **Rendi ONLINE-FIRST (KT N26.5a — bllokues certifikimi):** rruga normale = **transmeto në ATK → merr `transaction_id` → PASTAJ shtyp/dorëzo kuponin**. Offline = **VETËM përjashtim** (s'ka lidhje) sipas Nenit 44 — kuponi mban **mbishkrimin "OFFLINE"** (KT N26.12b).
- **Offline (Neni 44 — përjashtimi):** kupon offline + fiskalizim brenda **48h NGA MOMENTI I NDËRPRERJES së lidhjes** (JO nga krijimi per-kupon — implementimi aktual jo-konform, T2-08); **fiscal outbox i ndarë** nga sync (izolimi i provuar deri sot vetëm në mock lokal — pa scheduler production); retry/idempotencë; monitorim afati; raport ditor; pas ndërprerjes së plotë → **regjistrimi i bllokut brenda 5 ditësh** (UA N45.6).
- **Pajtueshmëria nen-për-nen:** `docs/canonical/05-sef-atk/ATK_Compliance_Gate.md` v2.0 (verdikti + T2-01..10 + Recertification Gate; *8-CRITICAL e vjetër = baseline historik PR #17*) · `docs/canonical/05-sef-atk/Kupon_Print_Spec.md` (kuponi i shtypur/elektronik: UA N7.2 i plotë + tabelat zyrtare 31/53) · **`docs/canonical/05-sef-atk/ATK_Source_Register.md`** (regjistri i burimeve normative: 7 dokumentet e portalit + sha256 + datat; **enumerimi i plotë i portalit para çdo auditi/PR-je normative** = rregull i detyrueshëm).
- **Amendamenti i KT-së (NË FUQI nga 22.06.2026; citimet besnike → `ATK_Source_Register.md` §3):** Neni 25 — instalimi i SEF *"së paku në njërën nga pajisjet apo ambientet e punës"*; Neni 26.4 i RI — **integritet i garantuar** i të dhënave në defekt/ndërprerje + **dërgim automatik** pas rikthimit të lidhjes + **eksport CSV/PDF i të patransmetuarave VETËM me kërkesë të ATK-së/autoritetit** (kërkesë e re — Gate §2c); **u hoq vetëm enkriptimi BLANKET at-rest i N26.4** — **kriptimi + integriteti i audit logs mbeten detyrim (KT N25.5.3, i paprekur)** (→ §9). **Kushtet e certifikimit**: nënshkruar + në fuqi **03.07.2026** (Neni 9; Neni 8 — ATK verifikon funksionimin pas instalimit; **procesi N5–N7 i mapuar 1:1** me conditional N/A për entitetin juridik → **Gate §2e**). **Rendi zyrtar — dy testime të ndara:** Tier 2 **"Testim para Aplikimit"** (hapi ynë përgatitor, i gated) → **aplikim formal + paketa** (N5) → **testi teknik i Komisionit** (N6) → **vendimi 15 ditë pune** (N7.1).
- **Config per tenant "në TVSH: po/jo"** — parakusht i fushës `vat_number` në kupon (UA N7.2.2; e detyrueshme vetëm kur tenant-i është i regjistruar për TVSH).
- POS ID + **PKI per pajisje/instalim = TARGET, NOT IMPLEMENTED** (T2-05: keypair unik per POS/makinë + certifikatë per instalim; çelësi s'largohet nga makina; onboarding `/ca/verify`→CSR→`/ca/signcsr` **s'ekziston ende në kod**); printer mobil + QR; Protobuf; dërgim `/pos/coupon`→`transaction_id` (uint64 ekzakt).
- **Statuset (flag per faturë):** lëshuar offline / në pritje fiskalizimi / fiskalizuar (FIC/NIVF+QR) / dështuar / **kërkon ndërhyrje** (alert te kontabilisti).
- **Go/No-Go:** KABI **nuk del live** pa kaluar 100% testi SEF/offline.
- **Terminologjia (UI):** fiskalizuar / në pritje fiskalizimi / lëshuar offline / dështuar / korrigjuar / anuluar / audit log. (Asnjëherë "hidden/undeclared/mos-deklarim".)

## 8. Migrimi & Cutover
- **Fillim i pastër:** PRO DATA punon derisa iData+ të finalizohet; **cutover 1 Janar 2027** me **vetëm saldo hapëse** (pa migrim **historiku transaksionesh**). **Master-data migrohet** (artikuj, klientë, furnitorë, çmime, receta, stok aktual, saldo) me rregulla strikte pastrimi; **përdoruesit e vjetër JO**.
- **Test paralel ~1 muaj kalendarik** para go-live për verifikim 1:1 me PRO DATA (kontabilisti sjell **Deklaratën e TVSH-së** mujore për krahasim). PRO DATA = **referencë** (ekstrakti ekziston; Libri i Shitjes/Blerjes + VAT summary tashmë të nxjerra për baseline). **Nxjerrja nga backup-i = E MBYLLUR (Wave 1–3, gaps 0); parity/closure → `07_PRODATA_Evidence_Closure_Parity_Matrix.md`** (çfarë = MD/OB/REQ/REF/NO). Backup-i = **golden reference baseline deri në lansim** (kontroll/parity në çdo fazë + UAT + cutover, jo vetëm Faza 0); **1076 tabela / 223 non-zero**.
- **KABI** njësoj: pa migrim nga programi i vjetër — vetëm saldo hapëse.
- Fjalëkalimet/PIN dhe `mac_id`/`mac_key` **NUK** migrohen.
- **Fabrika/klienti** jep + dokumenton saldot hapëse (stok/AR/AP/arka/banka/gajbe); persona përgjegjës.

## 9. Siguria & RBAC
- **2FA i detyrueshëm** (admin Group Owner, kontabilist).
- **Ndarja e roleve të larta:**
  - **Platform Owner** (iCode): menaxhon **tenantët + konfigurimet teknike**; **JO të dhëna biznesi** në punë normale.
  - **Group Owner / Admin**: sheh **raportet e konsoliduara të grupit** (KABI+PLANET); Company Switcher.
  - **Tenant-Admin** per biznes: qasje maksimale **brenda biznesit të vet**, s'sheh tjetrin.
  - **iCode Support:** qasje në të dhëna **vetëm me break-glass** (aprovim i Group Owner + arsye + kohë e kufizuar + **audit log**); **pa backdoor**.
  - **Financë Operative:** rol operacional — cash/barazime/POS/shpenzime/pagesa-ops/raporte ditore; **JO GL/TVSH/mbyllje periudhe** (= Kontabilisti). Shih `08_Finance_Operations_SCOPE`.
- **Rolet konfigurueshme** nga Group Owner; **matricë RBAC per-feature:** Lexim / Krijim / Modifikim / **Anulim-Korrigjim** / Shtypje / **Aprovim** (+ all-checks) — granular (p.sh. kamarier sheh vetëm tavolinat e veta vs tërë sallën). **"Fshirje" vetëm për objekte JO-financiare** (edhe aty zakonisht **deaktivizim**); dokumentet financiare **s'fshihen kurrë**.
- **Audit log** append-only për çdo veprim kritik; **matricë aprovimesh (default):** çmim/zbritje/anulim/kthim = **PIN menaxheri** (POS/terren); write-off/korrigjim stoku = **admin/kontabilist**; **hapje/mbyllje periudhe = vetëm kontabilisti kryesor**.
- **Baseline sigurie (Faza 0):** GitHub org me **2FA + branch protection** (PR + review i detyrueshëm); **asnjë sekret në repo** (vault / `.env`, `.env.example`); **rezidencë EU + DPA** (Ligji 06/L-082); rotacion sekretesh; least-privilege.
- **Mbrojtja at-rest e të dhënave fiskale (pas amendamentit të KT-së, në fuqi 22.06.2026):** kërkesa NORMATIVE e N26.4 të RI = **integritet i garantuar** i të dhënave në defekt/ndërprerje + **dërgim automatik** pas rikthimit + **eksport CSV/PDF i të patransmetuarave vetëm me kërkesë** të ATK-së/autoritetit — **u hoq vetëm enkriptimi BLANKET at-rest i N26.4**; **KT N25.5.3 vazhdon të kërkojë kriptim + kontroll integriteti për AUDIT LOGS** (i paprekur nga amendamenti). Enkriptimi i gjerë at-rest mbetet **STANDARD I BRENDSHËM i iData+** (siguri + LMDhP): **AES-256 = kandidati** — vendim arkitekturor për t'u mbyllur në **ADR**. Pajisje mobile = SQLCipher/OS keystore; server/hub = enkriptim disku ose pgcrypto për të dhënat fiskale; çelësat në keystore/vault — kurrë në kod/repo. Fazë: ADR + kod F1/F5; **eksporti CSV/PDF = kërkesë e re normative** (Gate §2c, F1/F5).
- **LMDhP — mbrojtja e të dhënave personale në rrjedhën SEF (UA N48):** (a) **inventar**: `OperatorId` (emër+ID operatori), `citizen_id`, kuponët me artikuj; (b) **retention** sipas LAPT/ligjit tatimor — periudha konkrete konfirmohet me kontabilistin/ATK para certifikimit; (c) **minimizim në logje/evidencë** — asnjë payload/PII në logje aplikative (redaction-i i PoC-5 bëhet standard); (d) baza ligjore = detyrimi i fiskalizimit; të drejtat e subjektit → procedurat e klientit. Kontroll i detyrueshëm para certifikimit (F5/F9).
- Asnjë fshirje reale e dokumenteve financiare — vetëm reversal/korrigjim/notë krediti.

## 10. Dizajni (iData+)
- Stil **Premium Modern SaaS** (WareHub-hybrid; "white + navy", jo ERP klasik i errët). **Font Inter Tight** (+ JetBrains Mono për ID/kode). **tabular-nums** për numra/ID.
- **Paleta:** Midnight Velvet `#01040C` (sidebar/hero) · White `#FFFFFF` · Moonlit Gray `#F5F5F5` · **Azure `#2463EB`→`#609FFF`** (primar+gradient) · text `#111827`/`#6B7280` · border `#E5E7EB` · success `#16A34A` · warning `#F59E0B` · error `#DC2626`.
- **Light default + Dark toggle.** Dy regjistra: **ajror** (dashboard/mobile) + **i dendur modern** (tabela kontabël). Ikona lineare (lucide); pa emoji; pa gradient purple/pink.
- **Izolim multi-tenant:** në KABI s'shfaqet PLANET dhe anasjelltas; vetëm admini i madh/kontabilisti kryesor ka **Company Switcher** + dashboard konsolidues.
- **Prototip i parë për 5 profile:** admin, kontabilist, KABI terren, PLANET kamarier, KDS.
- **Aplikacion (in-product), JO website marketing.** Mobile-first ku ka kuptim. Theming per subjekt: accent i vogël, baza e njëjtë (bërthamë e ripërdorshme). Logon e dizajnojmë ne.

## 11. Decisions log (të konfirmuara)
**27.06.2026 — bazë:**
- KABI live i pari; emër **iData+**; events jashtë v1; **bërthamë e ripërdorshme + emër neutral** (ripërdorimi sipas kushteve komerciale/IP).
- Stack Fortified Hybrid; hosting EU + on-prem; offline-first seamless; 60–80 kamarierë konkurrentë.
- Kontabiliteti një bërthamë ATK; **COGS default mesatare e ponderuar** (KABI batch costing/yield; finalizohet me kontabilistin); **pagat = Faza 1** (rishikuar v2.17; dikur v2.2 = Faza 2); **asetet fikse = Faza 2**; auto-deklaratë TVSH; **Libri i Shitjes/Blerjes baseline nga PRO DATA** (file zyrtar = validim); **KABI zgjeron Klasa 9 kosto**.
- Fermerët: normë e sheshtë [51] (**drejtim paraprak — konfirmim kontabilist/ATK**), akt-blerje. **Punki:** KABI paguan Punkin (+komision %/fiks), Punki paguan fermerët, KABI mban kartelat.
- KABI: tank i përzier (gjurmë tank/ditë); teknologu alokon (Urdhër Prodhimi); pasterizim = CCP gate; catch-weight i detyrueshëm.
- PLANET: të gjitha specat e seksionit 6 (POS/KDS/financa/multi-shank/raporte/premium).
- SEF: iCode aplikon; sandbox ASAP; offline 48h.
- Migrimi: fillim i pastër, **cutover 1 Jan 2027**, vetëm saldo hapëse + master-data; test paralel 1 muaj.
- Siguria: 2FA i detyrueshëm; **Platform Owner / Group Owner / Tenant-Admin / iCode Support (break-glass)**; **RBAC per-feature: Lexim/Krijim/Modifikim/Anulim-Korrigjim/Shtypje/Aprovim** ("Fshirje" vetëm jo-financiare).
- Dizajni: iData+ WareHub-hybrid, light default + dark toggle (mund të evoluojë).
- Komerciale: jashtë repository-it teknik; ruhen vetëm në dokumente private të pronarit/iCode dhe materiale ligjore konfidenciale.

**27.06.2026 — shtesa nga raundi Codex Q&A (v2.0):**
- **Afati KABI live ≈ 4 muaj**; PLANET = dizajn + paketa të përbashkëta tani, logjika specifike më vonë; "KABI stabil" lejon prodhim me deklarime manuale fillestare.
- **Mobile = native React Native** (App Store/Play Store); **PWA e përjashtuar**.
- **PLANET & PLANET2 = NJË subjekt ligjor** (një NUI), dy njësi biznesi/lokacione; raportim konsoliduar për pronarin.
- **Konsignacion = jo klasik → shitje direkte me të drejtë kthimi** (qumështore të freskëta).
- **Gajbe = stok ambalazhi te klienti + rakordim kthimi.**
- **Matrica e aprovimeve (default):** çmim/zbritje/anulim/kthim = PIN menaxheri; write-off/korrigjim stoku = admin/kontabilist; hapje/mbyllje periudhe = vetëm kontabilisti kryesor.
- **QC/Laborator:** parametrat (yndyrë/temp/pH/SNF/antibiotikë Pass-Fail); pranimi "Në pritje analize" → aprovim auto; yndyra matet per dorëzim, tank = mesatare e ponderuar.
- **Hardware:** klienti blen, iCode specifikon (PDA Sunmi/iData, tableta IP65, peshore RS232/LAN; ~10–12 ekrane KDS; hub Mini-PC+UPS; printera fiskalë sipas ATK — *me konfirmim, shih #8 te pyetjet*).

**27.06.2026 — korrigjime nga rishikimi i SSOT (Pronari, v2.1):**
- Frazë **neutrale** për bërthamën e ripërdorshme (jo "klienti s'e di").
- **Ndarje rolesh:** Platform Owner / Group Owner / Tenant-Admin / **iCode Support = break-glass** (aprovim+arsye+kohë+audit).
- **RBAC:** **Anulim-Korrigjim + Aprovim** në vend të "Fshirje" për dokumente financiare.
- **SEF fazim:** sandbox+PoC (F0) → fiscal flow minimal (F1–2) → aplikim pas testit → certifikim para KABI live.
- **Libri i Shitjes/Blerjes + VAT summary të PLANET** = nxjerrë nga backup PRO DATA (baseline); file zyrtar = validim.
- **COGS:** default mesatare e ponderuar; KABI batch costing/yield; **finalizohet me kontabilistin**.
- **Normë e sheshtë [51]** = drejtim paraprak (konfirmim kontabilist/ATK).
- **ARBK auto-import → Onboarding/Master Data/Integrations** (jo Siguri).
- **KDS = ~10–12 ekrane** (finalizohet pas floor-plan/vizitës).
- **Paga = Faza 1** (rishikuar v2.17 — KABI/kontabilist; dikur v2.2 = Faza 2).

**27.06.2026 — vendim v2.2:**
- **Pagat/payroll = Faza 1** (RISHIKUAR v2.17 — KABI/kontabilist detyrueshëm; dikur v2.2 = Faza 2).

**27.06.2026 — v2.3 (gati për Fazën 0):**
- Pranuar vlerësimi i Codex për Fazën 0 (~85–90% gati); **paketa Faza 0 prodhohet te `docs/canonical/01-phase0/`** (Execution Plan, 5 PoC specs, RBAC matrix, Accounting Compatibility Matrix, Data Model draft, Decision-Gates ADR, scaffold→rrënja e repo-s, SEF sandbox request, Feature Status Register).
- **Node = 24 LTS** (verifikuar Active LTS deri 30 Prill 2028; më mirë se 22 që është në maintenance), fiksuar me `.nvmrc`.
- Shtuar **2 pyetje të hapura:** floor-plan PLANET (#10), inventar pajisjesh (#11).
- Shtuar **decision gates:** DB e hub-it lokal, build RN/Expo, PITR/restore drill.
- Shtuar **baseline sigurie** (branch protection/2FA/sekretet/DPA) + **feature-status taxonomy**.

**28.06.2026 — v2.4 (SEF test phase active):**
- **SEF "Testim para Aplikimit" PRANUAR nga ATK (28.06.2026)** — faza e testimit teknik AKTIVE; identifikuesit (App ID/Fiscalization No) = **secrets** (jo README; te file 06 intern).
- **Referenca zyrtare e detyrueshme:** github.com/fiskalizimi (`models.proto`, ECDSA P-256, QR, **tax A/C/D/E**, **money per-field** (items €0.0001/totals cents) **[SUPERSEDED v2.25: Total=×100; QR=CitizenCoupon; org-u me konflikte]**, **CouponType Sale=1/Cancel=2/Return=3**); test server `fiskalizimi-test.atk-ks.org`. **PoC SEF s'nis pa analizë.**
- **ADR shtesë (Accepted):** SEF test active (019), Fiskalizimi GitHub mandatory (020), secret management (021), branch protection+2FA (022), backup/PITR drill (023).
- **Data model shtesë:** business_group, business_day/service_day, inventory_cost_layer/stock_valuation; audit redaction + SEF secrets.
- **Accounting:** baseline PRO DATA i vlefshëm për start; firma e kontabilistit para Accounting Core/Go-live (JO bllokues për foundation scaffold).
- **Feature Status Register** i ristrukturuar (KABI MVP/Advanced, PLANET MVP/Later, Roadmap, Req-*); premium/AI jashtë MVP.

**28.06.2026 — v2.5 (korrigjime teknike SEF — verifikuar nga pos-csharp + models.proto):**
- **Money scale PER-FIELD:** `CouponItem.Price/Total` = €0.0001 (×10000) **[SUPERSEDED v2.25: `CouponItem.Total` = ×100 cents — Gate v2 §3/T2-01]**; coupon `Total`/payments/tax-groups/totals = cents (×100). (Korrigjon "krejt cents".)
- **CouponType:** `Sale=1, Cancel=2, Return=3` (nga models.proto; README prose ishte gabim). RETURN/CANCEL kërkojnë ReferenceNo.
- **Tax codes:** `A`=exempt · `C`=0% · `D`=8% · `E`=18%; harta T0→C, T8→D, T18→E, **TL→A** (konfirmo). D=8% pa rregull blanket "qumështoret".
- **Secrets:** App ID/Fiscalization No → private-local (jashtë git); dokumenti publik pa ID.
- **ADR-017:** konfirmo printer hardware-vs-software gjatë test phase aktive, para blerjes së pajisjeve.

**28.06.2026 — v2.6 (template zyrtare ATK):**
- Kontabilisti dërgoi **template-et zyrtare** të Librit të Blerjes (29 kolona — IDENTIK me formatin tonë, incl. fermerë [51]) dhe të **Librit të Shitjes (24 kolona — i ri)**; ruajtur te `docs/evidence/reference_atk/`.
- **Box-map i plotë për të dy librat** → auto-deklaratë TVSH (input + output). Shitje: të liruara [9]/[10]/[11], 18%→[12], 8%→[14], totali TVSH [30].
- Mbetet vetëm **një muaj real i plotësuar** për validim end-to-end (pyetja #4).

**28.06.2026 — v2.7 (PRO DATA Wave 2: PLANET POS/KDS review nga Codex):**
- Extract Wave 2 i mbyllur (restaurant/POS/KDS): 9 raw tables / 804 rreshta, 80 tabela në inventar, 71 schema-only, 100 procedura të indeksuara. Mbetet vetëm **Wave 3** (audit/user logs, me sanitizim).
- **Validime të dizajnit tonë:** `tblDocPOSBarazimi*` = bosh → barazimi/saldo per kamarier = **modeli ynë net-new**; fshirjet ruheshin (`*_Deleted`+trigger) **pa PIN/arsye/aprovim** → modeli ynë (anulim+PIN+audit) **më i rreptë**; `tblRestTavolinat.LokacioniX/Y` = precedent floor-plan.
- **Entitete të konfirmuara (rebuild i pastër, JO 1:1):** table_session · order · order_item · kitchen_ticket(=kds_ticket) · kds_station(=station) · cash_shift · worker_settlement · manager_approval · audit_log → shtuar te §13 + `04_Data_Model_Draft`.
- Outputs Codex te `<private-local>/.../wave2_restaurant_.../` (jashtë workspace — referencë/evidence).

**28.06.2026 — v2.8 (PRO DATA Wave 3 + MBYLLJA e nxjerrjes):**
- **Wave 3 (audit/user/RBAC/config) krye me sanitizim** (34,942 rreshta; secrets redacted; users/hosts/devices hashed). **Priority A & B gaps = 0** → **nxjerrja nga backup-i E MBYLLUR.**
- Mësuam: audit real masiv (>1.7M rreshta PLANET2/RESORT); RBAC shumë i gjerë (groups/rights/report/special/warehouse-location/POS-mobile te `tblUSERS`) → matricë moderne per-feature (jo kopjim schema).
- **Nuk migrohen:** passwords, PIN, MAC/device IDs, secrets, historiku transaksioneve, audit logs (referencë); users rikrijohen/resetohen.
- **Evidence → specifikim:** `07_PRODATA_Evidence_Closure_Parity_Matrix.md` (per-domen: REQ/MD/OB/REF/NO + lidhja me Data Model & Phase 0). Tani ndalet nxjerrja; mbetet vetëm **një muaj real** nga kontabilisti (#4) + rakordimi i listave me manifest-et (Codex).
- **Pikë e re:** "RESORT" te audit-i → konfirmo (pyetja #12).
- **Backup = golden reference baseline deri në lansim** (1076 tabela / 223 non-zero); Codex prodhoi paketën e vet parity (CSV: evidence_index, phase_usage_matrix, parity_requirements) → rakordohet me `07_...` (canonical). **KABI dairy NUK inferohet nga PRO DATA** (vjen nga fabrika).

**28.06.2026 — v2.9 (Finance Operations e ndarë nga Accounting):**
- Te PLANET, **financa operative ≠ kontabiliteti** → modul + rol i veçantë **Financë Operative**; ekrane të ndara (**handoff:** kamarieri mbyll → finance barazim → accounting poston). Doc: `08_Finance_Operations_SCOPE_PLANET_KABI.md`.
- Entitete të reja: **expense**, **cash_movement** (+ cash_shift/worker_settlement/manager_approval ekzistuese).
- PRO DATA: pagesa/POS/banka ekzistojnë (`tblMenyraPagesa`/`tblBankat`/`tblDocPOSPagesat`/`tblDocPagesatMaster·Detalet`/`tblDocEkstrakti*`); POS-barazim = zero-row → net-new.
- Të vendosura (00:00, saldo per kamarier, Group Owner merr cash+arsye, identifikim POS/bankë, anulim+PIN) ↔ të hapura (roli i Financës Operative, cash-handover, tips, terminale, korrigjim-vs-propozim, format raporti) → pyetja #13.

**29.06.2026 — v2.10 (Finance Ops backup audit + bashkim canonical):**
- **Finance backup audit (Codex):** 104 objekte finance (43 raw / 57 schema-only / 4 supplementary); **A/B gaps = 0 — s'ka mungesë kritike.** Të mbuluara: pagesa/ekstrakte bankare/POS payments/banka/mënyra pagese.
- **Barazim (`tblDocPOSBarazimi*`) = zero-row → process-gap;** dizajn modern + valido live (mos infero nga backup).
- **`08` u bashkua → canonical v2.0** (absorbon Codex Scope_Separation_v1 + Coverage Audit).
- **RESORT = DB reale** (`PRODATA_PLANET_RESORT`, POS aktiv ~429K pagesa) ≠ PLANET2 → pyetja #12 e ndriçuar.
- **3 micro-extract opsionale** (jo bllokues): `tblDocBlerjetShpenzimetTjera`, `tblDocPagesatAvanset`, `tblUserMenyratPagesa` → Codex.
- Entitete finance shtesë: payment_method, payment_terminal, cash_count, settlement_line, supplier_payment.

**29.06.2026 — v2.11 (Finance Ops evidence i mbyllur + workflow spec):**
- **DDL/DML cross-check:** 0 tabela me DDL të munguar; **3382 referenca** (READ 3182/DELETE 256/INSERT 252/UPDATE 222); **4 micro-extract** (`tblUserMenyratPagesa` ×2, `tblDocBlerjetShpenzimetTjera`, `tblDocPagesatAvanset`) të nxjerra+sanitizuara → **finance evidence i mbyllur pa dyshim**. Të gjitha te `docs/evidence/reference_atk/`.
- **`09_Finance_Operations_Workflow_Spec`** (draft): flukse per-ekran + fusha/statuse + **paketa e pyetjeve live për PLANET** (e vetmja gjë e mbetur = konfirmim barazimi live, jo backup).
- Sinjale dizajni nga PRO DATA: pagesë→GL (ekstrakte bankare), recycle-bin/restore, `_DEL` audit → mbështesin reversal/audit + bank-reconciliation.

**29.06.2026 — v2.12 (Finance Ops UI prompt + harmonizim):**
- Codex prodhoi workflow spec të vetin (FO-00..FO-13; verbe close/reopen/export; entitete finance_day/float_assignment/pos_terminal_batch/finance_handover) → **harmonizuar me canonical** (04 + verbe RBAC E/Cl/Ro).
- **`Prompt_FinanceOps_UI_Wireframe.md`** (root) — prompt për Claude Code Design/Figma: screen breakdown FO-00..FO-13, design system i kyçur, desktop-first SaaS, mock data, **app jo website**.
- Hapi tjetër design: wireframe/prototip i FO (paralel me repo+skeleton).

**30.06.2026 — v2.13 (vendim implementimi):**
- **Hapi tjetër = repo assembly + walking skeleton + accounting library-first.** Jo module të mëdha, jo UI finale, jo KABI/SEF-full ende.
- Runbook operacional: `Repo_Assembly_Walking_Skeleton_Runbook_2026-06-30.md` (reuse scaffold, docs canonical/evidence/archive/private, monorepo minimal, packages/accounting me simulator+fixtures, invariantet nga commit-i i parë, acceptance criteria).
- Krijimi i repo-s = **veprim i Pronarit**; pastaj **skeleton → 5 PoC → module**. ADR-et e hapura (010-013, 016, 017) s'kyçen gjatë skeleton-it.

**30.06.2026 — v2.14 (përgjigjet e kontabilistit):**
- **PLANET: të gjitha shitjet me 18%** (biznes lluksi) → sales output = vetëm **E/18%** (pa 8%/exempt te shitjet e PLANET-it).
- **Blerje mixed-VAT per rresht** konfirmuar (çdo artikull norma e vet në të njëjtën faturë) → saktëson FX-005 + modelin tonë mixed-VAT.
- **Libri i Shitjes/Blerjes = harmonizuar 1:1 me ATK** → mapping-u ynë i saktë në format.
- **Validimi:** qershori s'është deklaruar ende (afati ATK **20.07.2026**) → kontabilisti dërgon një **muaj më të hershëm të deklaruar** (libra + deklaratë) = baseline parity (jo qershori).
- **KABI (fermerë/produkte/TVSH):** ende hapur — kontabilisti merr info te KABI kur të vijë në Kosovë.
- **Ende pa konfirmuar:** TL/exempt exact, llogaritë 22000/13000/21200, rounding, COGS, raportet mujore.

**01.07.2026 — v2.15 (validim parity maj 2026 — ACC-010):**
- **Dokumentet (Q#4 RESOLVED):** kontabilisti i PLANET dërgoi muajin e deklaruar — librat (shitje/blerje) + deklarata TVSH — emrat e file-ve private-local.
- **Rakordim (Claude ≡ Codex):** **INPUT VAT [67]=XX,XXX.XX** & **baza shitje 18% [12]=XX,XXX.XX = EKZAKT** (libra=deklaratë). **OUTPUT [30]: libri XX,XXX.XX vs deklaratë XX,XXX.XX → diff X.XXX** (jo ekzakt) → **tolerancë ±0.20** derisa kontabilisti konfirmon rregullin (shkak i mundshëm: çmime POS me TVSH të përfshirë → TVSH per receipt bruto×18/118 < bazë×18%).
- **VAT credit carry-forward** u konfirmua me muaj të deklaruar; exact values ruhen jashtë git si private evidence.
- **Engine tax-book = bucket-based** (18/8/import/investive/pa-TVSH/nota/reverse/fermerë[51]); shitje PLANET vetëm 18% (+[10] liruar-me-kredit XXX.XX +[16] nota XXX.XX; vlerat reale private-local); blerje mixed; **fermerë [51]=0** (PLANET).
- **Subjekti i deklaratës dhe librat PLANET2/RESORT** → lidhja juridike/operative duhet të konfirmohet me kontabilistin; identifikuesit ruhen jashtë git.
- **Evidence** → `evidence-private-local/02-accounting/PLANET_VAT_Reconciliation_Maj_2026_2026-07-01.md` (jashtë git); numrat → **fixture test për `packages/accounting`** (strict për bazat, tolerancë e vogël për output VAT).
- **KORRIGJIM:** leximi i parë i deklaratës ishte gabim; validimi i mëvonshëm konfirmoi carry-forward dhe la rounding-rule ende të hapur. Exact values ruhen jashtë git.

**07.07.2026 — v2.18 (Increment 1 — Walking Skeleton):**
- **Increment 1 skeleton merged (PR #4, `5a87f1a`):** monorepo + `shared-types` + accounting library-first + PM protocol; gitleaks rule filename-scoped; CI në corepack (pa action third-party). **Hapi B nisur** (mbyllja e Runbook §8: `packages/database` + migrime data-model fillestar + `apps/api` + `apps/web` + ping web→api→db).
- **Konventë modeli:** modeli logjik `user` → tabela fizike **`app_user`** (`user` = fjalë e rezervuar në Postgres). Auditimi group-level (audit_log pa `business_id`) = pikë e hapur për Fazën 1.

**07.07.2026 — v2.19 (Project Delivery Roadmap Fazat 1–10):**
- Project Delivery Roadmap Fazat 1–10 + Cutover/Rollback Runbook miratuar → `docs/canonical/08-roadmap/`; gates të reja **ENG-001..004 + SEC-DEP**; critical path me **★ KABI LIVE nëntor 2026**; **PLANET SEF i detyrueshëm për live**.

**07.07.2026 — v2.20 (Reliability Gate):**
- **Reliability Gate miratuar** (`06-architecture/Reliability_Gate.md`): çdo modul kritik kërkon **failure matrix + teste për 8 skenarët standardë të dështimit** para se të quhet gati; **waiver vetëm me vendim të shkruar të pronarit** me risk + afat. Zbatimi: PM_REVIEW_PROTOCOL kontrolli #7 · roadmap acceptance criteria (F1–F5, F7, F8) · Cutover Go/No-Go.

**08.07.2026 — v2.21 (PoC-1 + PoC-2 PASS):**
- **PoC-1 Tenant Isolation PASS** (PR #10) + **PoC-2 Accounting Posting PASS** (PR #12, +hardening 6-tabelësh reliability): izolimi 3-nivelësh (API deny-by-default / composite FK / RLS), motori i postimit me **debit=kredit të verifikuar në COMMIT**, **FX-005 mixed**, **[51] pending** (ACC-006), **dual-variant si input ADR-012**. **2/5 PoC të mbyllura.**
- **ADR-012, ACC-003 dhe ACC-006 MBETEN gates të hapura** — PoC-2 jep vetëm evidence/input teknik dhe kontabël, **nuk i mbyll**.
- **Gjetje për F1/F2:** rol aplikacioni jo-superuser (RLS); concurrency e `partner_card_entry.running_balance`; **TRUNCATE hardening** për tabelat append-only (edhe `audit_log`).

**09.07.2026 — v2.22 (ATK/SEF compliance hardening — auditimi nen-për-nen):**
- **Auditim i plotë KT (Maj 2026) + UA 01/2026** i kanonizuar: **278 kërkesa unike** (nota 7/10 për fazën PoC) **[baseline historik — shih v2.25]** → `05-sef-atk/ATK_Compliance_Gate.md` (regjistri me gate + **8 CRITICAL** + Recertification Gate + follow-ups kodi F1/F5) + `05-sef-atk/Kupon_Print_Spec.md` (UA N7.2 i plotë 2.1–2.20 + tabelat zyrtare 31 njësi/53 kategori + 11 pyetje të hapura Tier 2/ATK).
- **§7 i përditësuar:** rendi **ONLINE-first** (transmeto→`transaction_id`→shtyp; offline = vetëm përjashtim Neni 44 me mbishkrimin "OFFLINE"); regjistrimi i bllokut 5-ditësh (UA N45.6); **config per tenant "në TVSH: po/jo"** si parakusht i `vat_number` në kupon.
- **§9 i përditësuar:** **enkriptim at-rest** për të dhënat fiskale (KT N26.4c — KT: "standarde të avancuara"; **AES-256 = kandidati i brendshëm → ADR**) + seksioni **LMDhP** (UA N48: inventar/retention/minimizim në logje).
- **VENDIM I HAPUR (Pronari):** *shitje në distancë / e-kupon si kanal shitjeje* — PO me scope apo N/A me arsyetim? **Statusi: OPEN**; e-kuponi ndërkohë = **i kufizuar** te rastet që i lejon ATK (jo alternativë universale e printimit).
- Tracker: rreshta të rinj **ATK-005..ATK-010** (13 pyetjet Tier 2/ATK, PEF+SEF paralel, backup/restore drill i regjistrit fiskal, decommissioning POS + revokim certifikate, load test i kanalit fiskal, **kufiri i certifikimit** — bounded bundle vs full repo etj.).
- **Statusi i test-aplikimit:** aplikim i RI u regjistrua më **09.07.2026** → **referenca aktuale për Tier 2**; aplikimi i mëparshëm (28.06) = **historik/superseded**. Identifikuesit **private-local, jashtë git**.

**10.07.2026 — v2.23 (ATK normative refresh — amendamenti 22.06 + regjistri i burimeve):**
- **`ATK_Source_Register.md` i krijuar**: enumerimi FRESH i portalit `apps.atk-ks.org/sefaplikimi` (7 dokumente + URL të plota + sha256 + portal-listed dates / datat e nënshkrimit), **LAPT** (Ligji 08/L-257, GZ 5/2024 — titulli i portalit i pasaktë, i regjistruar), burimi teknik `github.com/fiskalizimi` veçmas; **rregulli i procesit**: enumerim + hash match para çdo auditi/PR-je normative; çdo zhvendosje date/hash-i = **gjetje e re** (leximi i ekzekutimit për "Kushtet": 03.07.2026).
- **Amendamenti i KT-së (Nr. Prot. 01-06-2194) NË FUQI nga 22.06.2026** (nënshkruar 22.06; protokolluar 24.06): **N25 instalimi** — *"së paku në njërën nga pajisjet apo ambientet e punës"* (kufiri i demonstrimit për certifikim = **ATK-010**, pa konkluzione tonat); **N26.4 i RI** — integritet + dërgim automatik + **eksport CSV/PDF vetëm me kërkesë** (kërkesë e re, Gate §2c); **u hoq vetëm enkriptimi BLANKET at-rest i N26.4** — **N25.5.3 (kriptimi+integriteti i audit logs) mbetet detyrim i KT-së**; enkriptimi i gjerë → **standard i brendshëm** (AES-256 kandidat → ADR) — §9 i riformuluar.
- **Kushtet dhe procedurat e certifikimit**: nënshkruar + **në fuqi 03.07.2026** (Neni 9); **procesi N5–N7 i mapuar 1:1 me gate** (N5.1–5.10 përfshirë conditional N/A 5.5.2/5.5.4 për entitetin juridik; N6.1–6.5; N7.1–7.9 me regjistrin publik) → **Gate §2e**; **dy testimet e NDARA**: Tier 2 "Testim para Aplikimit" (përgatitor, i gated) ≠ **testi teknik i Komisionit** (hap formal pas paketës, vendimi 15-ditë).
- **Gate v1.1:** CRITICAL #3 rishkruar mbi N26.4 e RI; rreshti i ri i eksportit; KT N25 i amenduar në §2d; **§2e procesi i certifikimit**; **§3a kufiri i certifikimit** (bounded surface: NJË monorepo, fiscal-core i izoluar; **POS/SEF + diagram integrimi = fokus prezantimi si kandidat i brendshëm pune**; **lista finale e source/artifacts = gated nga ATK-010 + Human Gate**); **§3b rregulli i artefaktit të komisionit** (pa DEMO/mock/PoC routes/placeholders/sample IDs; etiketa TEST s'hiqet; rregull i brendshëm, jo citim ligjor); **nota 7/10 + numëratorët = BASELINE 09.07/PR #17** — paketa e plotë **pending re-audit**.
- **Tracker:** ATK-005 = **13 pyetje protokollare + 1 pyetje regjistrimi** (ApplicationID nga ATK; **Numri i Njësisë nga Agjencioni i Regjistrimit të Tatimpaguesve** sipas KT; **OPEN = mapimi Numri i Njësisë↔`BranchId` + regjistrimi teknik te `/ca/verify`**); **ATK-011 i ri** (LAPT: burimi i regjistruar — **harta nen-për-nen mungon**; **P0 / PRE-TIER2** së bashku me **full-corpus re-audit-in** e Gate); **ATK-002** i rakorduar (Tier 1 PASS, Tier 2 pending) **[SUPERSEDED v2.25: REOPENED — Tier 2 NOT IMPLEMENTED/NO-GO]**. **Rendi aktual (roadmap §6):** PR #18 merge → **PR B PRO DATA Closure v2** → **ATK full-corpus/LAPT closure** → **PREFLIGHT privat** → **Tier 2**; PoC-4b paralel kur vjen hardware-i. **Blockers PRE-TIER2 (pa cikël) = PR B + full-corpus re-audit + ATK-011**; ATK-005 = lista e FIKSUAR para Tier 2 (server-dependent GJATË Tier 2); ATK-006/010 = certification gates të trackuara — **jo blockers automatikë** të Tier 2 (përveç ndryshimit scope/siguri).
- **Statuset reale të PoC-ve** (roadmap, statusi canonical/approved): PoC-1/2/3 = PASS **sipas kritereve të PoC-ve**; PoC-4 = **PARTIAL** (4a ✅, 4b hardware pending); PoC-5 = **Tier 1 PASS, Tier 2 LIVE pending** **[SUPERSEDED v2.25: Tier 1 PARTIAL/REOPENED · compliance FAIL · Tier 2 NOT IMPLEMENTED/NO-GO]**; **production-readiness arrihet përmes gates F1–F10** (jo automatikisht me F1+).
- **PM protocol v1.3:** + kontrolli i artefaktit të komisionit + verifikimi kundrejt `ATK_Source_Register.md` + scope aktual (Phase-0 closeout / PoC-4b / PoC-5 Tier 2 / ATK gates) me pointer dinamik te roadmap/SSOT.

**10.07.2026 — v2.24 (PRO DATA Source→Target Closure v2):**
- **`07_PRODATA_Evidence_Closure_Parity_Matrix.md` v2.0** — parimi **"NXJERRJA E MBYLLUR ≠ E IMPLEMENTUAR"**: dy boshte të NDARA — **Treatment tag-set** (REQ/MD/OB/REF/NO me kombinime; **`NO` = "source data nuk migrohet", JO "capability s'kërkohet"** — shembulli passwords `REQ+NO`) dhe **Target Maturity** (GAP→EVIDENCE→SPECIFIED→MODELED→IMPLEMENTED→TESTED→SIGNED-OFF, me prova të verifikueshme per rresht) + **PROVEN-PoC si qualifier i veçantë** (PoC PASS ≠ production readiness). **Zero përqindje pa metodologji**; claims që kërkojnë evidence të ndaluar = `EVIDENCE_REFERENCE_ONLY / NOT_REVERIFIED_IN_THIS_PR`.
- **Capability Coverage Map WMS/OMS** brenda 07 (sales_order · purchase_order · goods_receipt · reservation/allocation · bin/putaway · pick/pack/ship · wave picking · cycle count · assembly/production) me kufij eksplicitë: PLANET `order` ≠ `sales_order` universal; `delivery_note` ≠ `goods_receipt`; **prodhimi KABI NUK inferohet nga backup-i i PLANET-it**; GAP/LATER/DECISION_REQUIRED ku s'ka vendim — **jo pretendim WMS/OMS universal**.
- **ADR-003 riafirmuar, JO rihapur:** clean start + master-data + saldo hapëse (cutover 1 Jan 2027); pa histori transaksionale; pa kredenciale. **OPEN vetëm delivery gates:** mapping/cleansing/dedup/freeze/dry-run/reconciliation/sign-off (F9 + runbook).
- **Legacy PRO DATA custody ≠ ADR-023 (product DR):** burimi autoritativ = `.bak` (MDF/LDF jo); **kopje e dytë e enkriptuar + hash manifest + isolated restore drill = REQUIRED/OPEN/EVIDENCE PENDING** (evidence privatisht nga Pronari; PR-ja s'verifikoi asgjë) → 07 §6 + `Cutover_Rollback_Runbook.md` §3.1 (v0.3).
- **Evidence historike = opaque/read-only:** ruhet në **ignored private-local evidence store** (jashtë git, pa path/filename këtu) — verifikuar vetëm me metadata (`test -e`/`git ls-files`); **zero content-read në këtë PR**; extraction provenance/metrikat mbeten evidence historike e pandryshuar.
- **ACC-004 mbetet `answered`** (scope-i i burimit/parity) — **answered ≠ target SIGNED-OFF**; **ACC-010 = closed** (Maj 2026, vlerat private-local); **OPS-001/RESORT mbetet OPEN**; statuset stale të 07 v1.1 (burimi "(memory)", path private, "mungon muaji real", "të gjitha entitetet") = të korrigjuara në v2.
- **Roadmap §6 + PM protocol v1.4 + INDEX (merge-stable):** pas këtij merge next gate = **ATK full-corpus/LAPT closure → PREFLIGHT privat → Tier 2 LIVE**; **PoC-4b paralel**.

**12.07.2026 — v2.25 (Full-corpus re-audit i kanonizuar + Tier 2 NO-GO):**
- **Verdikti (identik kudo):** audit coverage COMPLETE vetëm për corpus-in publik të ngrirë (cutoff 12.07); **product compliance NOT COMPLETE**; regression 66 PASS/1 LIVE SKIP (mekanika lokale mock — disa assertions money/QR vetë të gabuara); **Tier 1 overall PARTIAL/REOPENED; compliance FAIL; Tier 2 NOT IMPLEMENTED/NO-GO; certifikimi NO-GO**; PR #16 = evidence historike mock.
- **Kontratat e korrigjuara (Gate v2 §3):** `CouponItem.Price` ×10000; **`CouponItem.Total` ×100**; totals/payments/tax ×100; golden €1.50×3 → 15000/450; **QR = CitizenCoupon** i firmosur; NUIKF formati DIHET (OPEN vetëm algoritmi/origjina/binding); **48h nga ndërprerja e lidhjes** (`created_at` immutable; `outage_started_at`/event model = PROPOSED/ADR input); **P1363 vs DER = konflikt i hapur** (C#↔Go; serveri vendos); PKI per POS/instalim = minimum Tier 2; LIVE aktual = placeholder.
- **Dokumente:** `ATK_Source_Register.md` **v2.4** (bounded registered corpus; affected-map L01–L41 + K07/U01–U27) · **`ATK_SEF_Requirements_Ledger.md` v1.6 I RI** (**132 source-rows**: 79/21/19/**5**/2/6 + K07 CONTEXT jashtë numërimit; **45 CONFIRM_***; **46 closure keys**; 5 CONTRADICTS-rreshta = 4 defekte unike; **disposition substantiv 1–123 me arsye per-nen**; L01–L41 përfshirë Nr.fiskal/NUIB (4(2) ARBK), çregjistrimin (5), faturat reale (13.5), amended returns (14), barter (43.1–2) → **ATK-011 = OPEN/PENDING_INDEPENDENT_REVIEW**) · `ATK_Compliance_Gate.md` **v2.0** (verdikti; **T2-01..T2-10**; rerating; 278/7-10 = baseline historik).
- **Tracker:** ATK-002 **REOPENED/P0** (umbrella — s'mbyllet pa ATK-012); ATK-005 rifokusuar (NUIKF algoritmi/origjina/binding; money/QR NUK janë pyetje — janë korrigjime kodi); **ATK-011 = OPEN/PENDING_INDEPENDENT_REVIEW** (mbyllja kërkon review të pavarur + Human Gate); **ATK-012 i RI** (T2-01..10, P0/PRE-TIER2, 1 POS testues; lifecycle i plotë → ATK-008); **ATK-013 i RI** (paketa ligjore/aplikueshmëria); **ACC-012 i RI** (fatura/libra/retention/e-invoice/cash+TVSH umbrella); **PRIV-001 i RI** (controller/processor, ROPA, DPIA/DPO, breach, retention-erasure, cloud transfer).
- **Sekuenca e re (roadmap §6 — rendi i detyrueshëm):** Step A (ky PR docs) → **Step B code hardening** → **Step C golden vectors/negative tests** → **independent re-review** → **HUMAN GATE për thirrjen e parë reale** → **private `/ca/verify` PREFLIGHT** → pjesa tjetër e Tier 2 vetëm pas suksesit; **asnjë endpoint fiskal para Human Gate**; certifikimi/go-live = NO-GO më vete; PoC-4b paralel.
- **Propagimi:** SSOT §2/§7 (money/QR/48h/PKI/izolimi i kualifikuar; "s'ndalet kurrë" hequr); PM v1.5 (kontrollet e reja të detyrueshme; CI green ≠ compliance); PoC-Specs/Data-Model/ADR-020 note/07_PRODATA rreshti SEF/Kupon v1.1/Reliability v1.3/Findings/Wireframe/ANALIZA+README (korrigjimi post-audit); Phase-0 plan = historik.

## 12. Pyetjet e hapura (pending)
| # | Pyetja | Kush | Afati |
|---|---|---|---|
| 1 | Lista TVSH per produkt **KABI** (PLANET shitjet = **18% — RESOLVED** 30.06) | Kontabilisti (pas vizitës KABI) | — |
| 2 | Formula USAID — **tabela e saktë** (yndyrë/baktere → klasa → €/litër) | Fabrika | të mërkurën |
| 3 | Numrat e fabrikës (fermerë/klientë/automjete/litra/ditë) | Fabrika | — |
| 4 | ✅ **RESOLVED 01.07** — Muaj i deklaruar për parity: **maj 2026** (LSH+LB+deklaratë) → input & baza përputhen; output ka rounding/reporting nuance; exact values ruhen private-local | Kontabilisti | ✅ |
| 4b | **Rregulli EKZAKT i rounding** (diferenca X.XXX te output [30]) — a nxirret TVSH per receipt nga çmimi me TVSH të përfshirë? | Kontabilisti | në vijim |
| 4c | **PLANET2 / RESORT** — konfirmo lidhjen juridike/operative me kontabilistin; identifikuesit ruhen jashtë git | Kontabilisti | në vijim |
| 5 | Rrjedha kontabël BLV→blerje→bartja te librat + raportimi bankar | Kontabilisti | të mërkurën |
| 6 | Formati i saktë i raporteve/eksporteve për ATK | Kontabilisti | — |
| 7 | Konsignacioni — **njohja kontabël** e modelit me të drejtë kthimi | Kontabilisti | të mërkurën |
| 8 | **Printera fiskalë vs SEF software-fiskalizim:** a duhen pajisje fiskale hardware (Tremol/Datecs etj.) apo mjafton printer termik + nënshkrim software? | Ti/ATK | — |
| 9 | **COGS final** (mesatare e ponderuar + batch costing KABI) | Kontabilisti | të mërkurën |
| 10 | **Floor-plan i saktë i PLANET-it** (për numrin e ekraneve KDS + lokacionet) | Klienti/Ti | — |
| 11 | **Inventari i pajisjeve ekzistuese** (për ripërdorim hardware) | Klienti | — |
| 12 | **"RESORT"** = DB reale e PRO DATA (`PRODATA_PLANET_RESORT`, POS aktiv ~429K pagesa) ≠ PLANET2 (back-office). Konfirmo: lokacion/njësi/subjekt + lidhja me `business_unit` | Ti | — |
| 13 | **Finance Operations workflow** (roli i Financës Operative, cash-handover, tips, POS terminale, korrigjim-vs-propozim, format raporti ditor) → `08_Finance_Operations_SCOPE` | Klienti/Ti | — |

*Të zgjidhura (s'janë më hapur):* **pagat → Faza 1** (rishikuar v2.17; dikur v2.2); yndyra per dorëzim vs tank → **per dorëzim + tank mesatare e ponderuar**; gajbe → **stok ambalazhi te klienti + rakordim**; konsignacioni → **vendim: shitje me të drejtë kthimi** (mbetet vetëm njohja kontabël, #7); Libri i Shitjes → **baseline i nxjerrë nga PRO DATA** (mbetet validimi zyrtar, #4).

## 13. Glosar & model i të dhënave (fillestar)
**Glosar:** NUI (nr. unik identifikues) · NUIKF (kodi fiskal i kuponit) · LOT/batch · FEFO (first-expired-first-out) · catch-weight (peshë variabile per copë) · normativ/BOM (recepturë prodhimi) · skrap (% humbje) · CCP (pikë kritike kontrolli — pasterizim) · SNF (solids-not-fat) · MTS/MTO (make-to-stock / make-to-order) · break-glass (qasje emergjente e mbikëqyrur me audit) · saldo (gjendja e arkës/kamarierit) · fiscal outbox (radhë e kuponave për fiskalizim) · RLS (row-level security) · business_id (çelësi tenant) · Punki (grumbullues qumështi me %/tarifë).

**Entitete kryesore (high-level, do detajohen):** business · **business_group** · business_unit · user · role · **permission(feature × Lexim/Krijim/Modifikim/Anulim-Korrigjim/Shtypje/Aprovim)** · partner(furnitor/blerës/agjent) · farmer · collector(Punki) · article(+UoM/çmimore/normativ/atribute LOT) · recipe · milk_intake(+QC params) · milk_pool_batch · production_order · production_batch · stock(depo/restorant/shank) · **stock_valuation/cost_layer** · vehicle_stock · packaging_stock(gajbe) · invoice(blerje/shitje) · payment · payment_method · payment_terminal · **expense** · supplier_payment · cash_count · settlement_line · **cash_movement** · journal/GL_entry · tax_book · fiscal_coupon · device(POS-ID/PKI) · table · zone · location · order · order_item · **table_session** · kds_ticket(kitchen_ticket) · **kds_station** · **business_day/service_day** · shift/cash_shift · **worker_settlement** · **manager_approval** · audit_log.

---
*Ndryshimet e ardhshme: shtohen te seksioni përkatës + një rresht te Decisions log me datë. Versioni rritet (2.14, 2.15…).*
