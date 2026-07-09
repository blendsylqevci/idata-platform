# Outstanding Confirmations Tracker

**Data:** 2026-06-29
**Status:** canonical active tracker / external confirmations
**Scope:** kontabilist · PLANET finance · KABI factory · ATK/SEF · repo/documentation
**Qëllimi:** një vend i vetëm ku ndiqen të gjitha përgjigjet e jashtme që ndikojnë vendime, scope, faza, risk, kontabilitet, SEF, finance operations dhe go-live.

Ky dokument ekziston që të mos krijohet “rremujë” me pyetje të shpërndara në chat, PDF, Excel, backup-e dhe dokumente të veçanta. Nëse një përgjigje e jashtme ndryshon mënyrën si ndërtohet sistemi, ajo duhet të hyjë këtu, pastaj të reflektohet në dokumentin përkatës canonical.

---

## 1. Rregulli kryesor

Asnjë vendim me ndikim financiar, ligjor, fiskal, operacional ose go-live nuk duhet të ngrijë në kod pa njërën nga këto:

1. evidence nga PRO DATA / backup / export real;
2. konfirmim nga kontabilisti;
3. konfirmim nga ATK/SEF;
4. konfirmim nga fabrika KABI;
5. konfirmim live nga financat/operacionet e PLANET-it;
6. vendim i shkruar i pronarit/adminit, me risk të shënuar.

Nëse përgjigjja mungon, feature nuk fshihet; shënohet me status dhe pronar.

---

## 2. Status legend

| Status | Kuptimi |
| --- | --- |
| `open` | Duhet marrë përgjigje / nuk është dërguar ende. |
| `sent` | Pyetja/dokumenti është dërguar, presim përgjigje. |
| `answered` | Përgjigjja ka ardhur, por duhet analizuar/reflektuar. |
| `approved` | Përgjigjja është pranuar dhe mund të përdoret. |
| `blocked` | Pa këtë përgjigje nuk mund të ecet në atë zonë. |
| `deferred` | Nuk hyn në MVP / shtyhet për fazë tjetër. |
| `closed` | Është reflektuar në docs/SSOT/ADR dhe nuk ka punë të hapur. |
| `technical-validated` | Teknikisht e validuar kundër evidence; pret sign-off biznesor. |

Prioritetet:

- `P0` — bllokon accounting/SEF/KABI live/repo start.
- `P1` — duhet para freeze të modulit.
- `P2` — e forcon produktin, por nuk bllokon skeleton-in.

---

## 3. Tracker kryesor

| ID | Workstream | Konfirmimi që kërkohet | Owner / burimi | Status | Prioritet | Evidence / doc | Çfarë zhbllokon |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `ACC-001` | Accounting | Dërgimi i `Accountant Review Pack v0` te kontabilisti. | Blend → kontabilisti | `answered` | P0 | `docs/canonical/02-accounting/Accountant_Review_Pack_v0_2026-06-29.md` | Përgjigje fillestare pranuar; disa gates mbeten pending. |
| `ACC-002` | Accounting / VAT | Krahasimi i qershorit 2026: sales VAT `XX,XXX.XX`, purchase VAT `XX,XXX.XX`, net `-XX,XXX.XX` kundër deklaratës zyrtare. | Kontabilisti | `answered` | P0 | `docs/canonical/02-accounting/VAT_Book_Mapping_v0_2026-06-29.md` | Qershori nuk është deklaruar ende; përdor muaj të mbyllur më herët për sign-off të parë. |
| `ACC-003` | Accounting / VAT | Konfirmim i tax codes `T0/T8/T18/TL`, llogaritë `22000/13000/21200`, bucket-et dhe rounding. | Kontabilisti / ATK nëse duhet | `answered` | P0 | `docs/canonical/02-accounting/Accountant_Review_Pack_v0_2026-06-29.md` | Mixed VAT në blerje u konfirmua; tax codes/accounts/rounding final ende për konfirmim. |
| `ACC-004` | PLANET parity | Cilat raporte PRO DATA i përdor kontabilisti çdo muaj dhe cilat duhet të jenë 1:1 në MVP. | Kontabilisti | `answered` | P1 | `docs/canonical/01-phase0/07_PRODATA_Evidence_Closure_Parity_Matrix.md` | Librat Shitje/Blerje janë harmonizuar me ATK; raportet e tjera parity mbeten për validim gjatë muajit të deklaruar. |
| `ACC-005` | KABI VAT | TVSH për secilin produkt KABI: 8%, 18%, 0%, exempt/out-of-scope nëse ka. | Kontabilisti / ATK / KABI admin | `answered` | P0 | `docs/canonical/03-kabi/KABI_Operational_Evidence_Update_2026-07-05.md` | KABI konfirmoi (05.07): qumësht + ajkë 1kg = **18%**; të gjitha të tjerat = **8%**. |
| `ACC-006` | KABI farmers | Trajtimi i fermerëve: pa faturë, flat-rate/box 51, avanse, offset, pagesë mujore, dokumentim AUVK. | Kontabilisti / ATK / KABI admin | `answered / pending accountant sign-off` | P0 | `docs/canonical/03-kabi/Accountant_Confirmation_TVSH_e_Sheshte_Fermere_KABI_2026-07-05.md` | Flat-rate **8%** (UA 03/2015 neni 76) + **NIF** + certifikatë MBPZHR; autofaturë nga KABI; fermerët s'lëshojnë fatura; pagesë mujore + avanse. Posting/libër-blerje pret sign-off (checklist 21 pyetje). |
| `ACC-007` | COGS / inventory | Metoda zyrtare: mesatare e ponderuar, FIFO, batch costing/yield, packaging/loss/by-products. | Kontabilisti + KABI/PLANET management | `sent` | P0 | `docs/canonical/02-accounting/Accounting_10_10_Hardening_Checklist_2026-06-29.md` | Zhbllokon cost layers, COGS dhe inventory valuation. |
| `ACC-008` | Corrections | Rregullat për kthime, skadencë, write-off, note krediti/debiti, periudha e raportimit. | Kontabilisti | `sent` | P1 | `docs/canonical/02-accounting/Accountant_Review_Pack_v0_2026-06-29.md` | Zhbllokon reversal/correction policy. |
| `ACC-009` | PLANET sales VAT | PLANET restaurant sales default VAT rate. | Kontabilisti | `approved` | P0 | `docs/canonical/02-accounting/Accountant_Review_Pack_v0_2026-06-29.md` | PLANET sales default = 18% / `T18`; implement as PLANET config, not global hardcode. |
| `ACC-010` | Official closed-month validation | Kontabilisti të dërgojë një muaj më të hershëm të deklaruar: Libri i Shitjes, Libri i Blerjes, deklarata TVSH. | Kontabilisti | `closed` | P0 | Validuar me Maj 2026 (muaj i deklaruar) — shih SSOT v2.15; exact values private-local. Rounding-rule i hapur ndiqet te `ACC-003`. | Sign-off i parë me muaj të deklaruar (Maj 2026). |
| `ACC-011` | ATK book format | Konfirmim që Libri i Shitjes dhe Libri i Blerjes janë të harmonizuar me ATK. | Kontabilisti | `approved` | P0 | `docs/canonical/02-accounting/Accountant_Review_Pack_v0_2026-06-29.md` + future official closed-month files | Mapping-u i librave qëndron; verifikohet me muaj të deklaruar. |
| `FIN-001` | PLANET finance | Takim live me personin e financave / personin që bën barazimet. | Blend → PLANET finance | `open` | P0 | `docs/canonical/04-planet-finance/Finance_Operations_Workflow_Spec_2026-06-29.md` | Validon ekranet e finance operations. |
| `FIN-002` | PLANET finance | Një ditë reale: saldo/float, cash, kartelë, POS terminale, shpenzime, withdrawals, handover te kontabilisti. | PLANET finance | `open` | P0 | `docs/canonical/04-planet-finance/Finance_Operations_PLANET_KABI_2026-06-29.md` | Zhbllokon barazim ditor, cash office dhe payment reconciliation. |
| `FIN-003` | PLANET POS/KDS | Floor plan real, zona/tavolina, banak i brendshëm/jashtëm, printera, KDS 10–12 ekrane, lokacion → printer/KDS. | PLANET operations / Group Owner | `open` | P1 | Wave 2 + user requirements | Zhbllokon POS/KDS UX dhe konfigurim lokacioni. |
| `OPS-001` | PLANET structure | Sqarim i termit `RESORT`: a është vetëm emër/lokacion/business unit brenda PLANET-it, apo subjekt/njësi tjetër që duhet modeluar veç. | Group Owner / PLANET management / kontabilisti nëse duhet | `open` | P0 | Master docs + SSOT decisions | Zhbllokon `business_unit/location` model pa gabuar tenant/legal structure. |
| `KABI-001` | KABI farmers | Formula USAID për çmimin e qumështit sipas yndyrës/cilësisë. | KABI admin / teknologu / kontabilisti | `answered — bazë zyrtare pagese; pending posting/rounding` | P0 | `docs/canonical/03-kabi/USAID_Formula_Validation_Gate_2026-07-04.md` + `KABI_Operational_Evidence_Update_2026-07-05.md` | KABI konfirmoi formulën si **bazë zyrtare pagese** (jo vetëm raportim); implementim 1:1. Mbetet rrumbullakosja (cent) + posting kontabël (ACC-006). |
| `KABI-002` | KABI intake/QC | Fushat e sakta të pranimit: litra/kg, yndyrë, aciditet/pH, temperaturë, SNF, antibiotikë, ujë, klasë, refuzim. | Fabrika / teknologu / laboratori | `answered` | P0 | `docs/canonical/03-kabi/KABI_Operational_Evidence_Update_2026-07-05.md` | Testet QC + pajisjet (Lacto Scan, pH-meter, densimetër) + pragjet u konfirmuan; lab-i aprovon. Njësitë proteinë/SNF për konfirmim me teknologun. |
| `KABI-003` | KABI products | Lista zyrtare e produkteve me njësi, barkod, TVSH, afat skadence, kategori, packaging, catch-weight. | KABI admin + kontabilisti | `answered (strukturë; lista reale private-local)` | P0 | `docs/canonical/03-kabi/KABI_Operational_Evidence_Update_2026-07-05.md` | Fushat e produktit + VAT 18/8 + barkode nga lista e ministrisë + catch-weight + normativ; lista reale mbetet private-local. |
| `KABI-004` | KABI production | BOM/receta, yield, batch/LOT, skadenca, pasterizimi, humbje teknologjike, by-products/refuz. | Teknologu / fabrika / kontabilisti | `answered (strukturë; recetat reale private-local)` | P1 | `docs/canonical/03-kabi/KABI_Operational_Evidence_Update_2026-07-05.md` | Recetura/normativ + linja prodhimi/kapacitet + paketim (gajbe/karton i konfigurueshëm) u konfirmuan; recetat reale private-local. |
| `KABI-005` | KABI van-sales | Rruga reale e shoferit: ngarkim, shitje cash/kredi, kthime/skadencë, gajbe, POD, barazim mbrëmje, offline. | KABI sales/logistics | `answered` | P0 | `docs/canonical/03-kabi/KABI_Operational_Evidence_Update_2026-07-05.md` | 7 shpërndarës (3 të jashtëm me rabat të konfigurueshëm/private-local); ngarkim sipas porosisë + model ambulant; faturim në vend; barazim mbrëmje; **FEFO për t'u aplikuar**; refuzim LOT/pjesor → korrigjim fature. |
| `ATK-001` | SEF | SEF test phase active të mbahet si track i veçantë; identifikuesit mbeten vetëm private. | iCode / ATK | `approved` | P0 | private SEF docs, jo publik | Zhbllokon PoC SEF në test environment. |
| `ATK-002` | SEF / technical | PoC me `github.com/fiskalizimi`: PKI/CSR, coupon proto, signature, QR, offline retry, tax mapping. | iCode dev | `open` | P0 | official Fiskalizimi GitHub + SEF docs | Zhbllokon aplikim/certifikim teknik. |
| `ATK-003` | SEF / certification | Aplikimi final për certifikim dhe mirëmbajtje të SEF pas testimit të suksesshëm. | iCode / ATK | `open` | P0 | `docs/canonical/05-sef-atk/ATK_SEF_Offline_Faturimi_Findings.md` | Bllokon KABI/PLANET live me fiskalizim real. |
| `ATK-004` | SEF / hardware | Konfirmim: a kërkohet pajisje fiskale hardware apo mjafton SEF software + printer termik/standard sipas rregullave të ATK-së. | iCode / ATK / hardware vendor | `open` | P0 | `docs/canonical/05-sef-atk/ATK_SEF_Offline_Faturimi_Findings.md` + official SEF docs | Zhbllokon blerjen e printerëve/pajisjeve pa risk. |
| `HW-001` | Hardware inventory | Inventari i pajisjeve ekzistuese te PLANET/KABI: PC/NUC, printerë, ekrane, tabletë/PDA, peshore, rrjet, UPS, POS terminale. | Klienti / PLANET / KABI | `open` | P1 | future hardware inventory sheet/photos | Zhbllokon ripërdorim pajisjesh dhe listën finale të blerjes. |
| `REPO-001` | Repo | Krijimi i repo-s private me emër neutral. | Blend | `closed` | P0 | Repo privat `idata-platform` e krijuar; baseline commit 2026-07-04. | Development workflow i zhbllokuar. |
| `REPO-002` | Project management | Krijimi i Notion/board për tasks, gates, confirmations dhe status. | Blend | `closed` | P1 | Notion HQ + Update Log + task-e aktivë (2026-07-05). | Ndjekja e punës aktive në Notion. |
| `REPO-003` | Documentation control | Bashkimi i raw docs nga Claude/Codex në strukturë canonical/evidence/archive/private. | Blend + Codex | `closed` | P0 | `docs/CANONICAL_SPLIT_REPORT_2026-07-02.md` | Source-of-truth profesional i vendosur. |
| `ATK-005` | SEF / Tier 2 | **Pyetjet e hapura teknike drejt test-serverit/ATK-së — 13 GJITHSEJ** (11 nga `Kupon_Print_Spec.md` §9 + 2 shtesë nga ANALIZA §11): NUIKF burimi/formati; shenja e Return; zbritja per-artikull në proto; QR i të pafiskalizuarit; semantika e `Time`; 53 kategoritë ↔ `CouponItem.Type`; formati i printimit (UA N32.2.6); X/Z raporti; refund payout; kupon→faturë B2B; **kodi PM i dublikuar** në tabelën zyrtare (#8 vs #36); **+ furnizimi falas/total 0 (UA N5.2)**; **+ sjellja reale e dublimit të `CouponId`**. | iCode dev → ATK/Tier 2 | `open` | P0 | `docs/canonical/05-sef-atk/Kupon_Print_Spec.md` §9 + `poc/poc5-sef/ANALIZA.md` §11 | Zhbllokon gjeneratorët (NUIKF/numërim), print layer final, mapping-un e kategorive. |
| `ATK-006` | SEF / legal | **Pyetje FORMALE me shkrim drejt ATK-së: a lejohet PEF/SF + SEF aktive NJËKOHËSISHT në të njëjtën njësi/POS?** Konteksti: kërkesa e klientit PLANET për redundancë; UA hesht — as ndalon as lejon. Kërkohet edhe si shmangET raportimi i dyfishtë. **Statusi zyrtar: OPEN — pa përgjigje me shkrim s'vendoset asgjë.** | Blend/iCode → ATK | `open` | P0 | UA 01/2026 (hesht) + kërkesa PLANET | Zhbllokon strategjinë e redundancës në PLANET pa risk raportimi të dyfishtë. |
| `ATK-007` | SEF / operims | **Backup/restore drill i regjistrit fiskal** (outbox + kuponët + alertet): procedurë + provë reale që rikthimi s'humb/dublon kuponë (lidhet me ADR-023 + hub-epoch të ADR-013). | Tech + iCode | `open` | P1 | `Reliability_Gate.md` + ADR-023 | Zhbllokon F5/F9 hardening; kërkesë e integritetit KT N25.5. |
| `ATK-008` | SEF / lifecycle | **Decommissioning i POS-it + revokim i certifikatës**: procedura kur një pajisje del nga përdorimi/vidhet (çregjistrim në ATK, revokim çelësi, arkivim i regjistrit lokal). | iCode → ATK | `open` | P1 | UA N34/N36 | Zhbllokon operimin e sigurt shumë-pajisjesh (F5/F7). |
| `ATK-009` | SEF / performance | **Load test i kanalit fiskal**: throughput i outbox→ATK në pikun e PLANET (60–80 kamarierë) + sjellja e test/prod serverit nën ngarkesë; targets → `Reliability_Gate.md` §7. | Tech | `open` | P1 | Reliability_Gate §7 (targets TBD) | Zhbllokon dimensionimin F8 + SLA-të e brendshme. |
| `ATK-010` | SEF / certification boundary | **Kufiri i certifikimit — pyetje FORMALE drejt ATK-së (vetëm OPEN; ADR final VETËM pas përgjigjes + aprovimit të Blendit):** (a) çfarë dorëzohet për certifikim — **bounded source bundle vs full repo**; (b) **scope**: certifikohet vertikali fiskal apo zgjidhja universale; (c) kuptimi i saktë i **"demo"**-s në proces; (d) semantika e fushës **"Lloji"**; (e) politika **notification/retest/recertification** për ndryshime pas certifikimit. Asnjë repo e dytë s'krijohet për këtë. | iCode/Blend → ATK | `open` | P0 | UA N34/N35 + KT N25.8/N31.8 + `ATK_Compliance_Gate.md` §3 | Zhbllokon strategjinë e dorëzimit për certifikim (F5) pa ekspozim të panevojshëm të repo-s. |

---

## 4. Veprimet më të rëndësishme në 72 orët e ardhshme

1. **✅ KRYER — Muaj i mbyllur i validuar**
   Maj 2026 (muaj i deklaruar) u dorëzua dhe u validua kundër engine-it (shih SSOT v2.15 / `ACC-010`). Rounding-rule mbetet i hapur te `ACC-003`.

2. **Cakto takim me financat e PLANET-it**
   Dokumenti: `docs/canonical/04-planet-finance/Finance_Operations_Workflow_Spec_2026-06-29.md`
   Objektivi: një ditë reale barazimi nga fillimi deri në handover te kontabilisti.

3. **Kërko nga KABI dokumentet/fotot/file-t e mbetura**
   - formula USAID;
   - produktet me TVSH/njësi/barkod/afat;
   - fushat laboratorike;
   - flow i shoferit/van-sales;
   - recetat/BOM/yield nëse janë gati.

4. **Përgatit SEF PoC checklist**
   Pa publikuar identifikues sekretë. Protokolli teknik vjen nga GitHub zyrtar i Fiskalizimit dhe dokumentet private SEF.

5. **Pyet ATK/vendor-in për printera/pajisje fiskale**
   Objektivi: mos të blihet hardware gabim para se të dihet qartë kërkesa hardware-vs-software.

6. **Kërko inventarin e pajisjeve ekzistuese**
   Objektivi: të dimë çka ripërdoret dhe çka duhet blerë realisht.

7. **Sqaroni `RESORT` në modelin legal/operacional**
   Objektivi: të mos gabojmë modelin `business_unit/location`.

8. **Krijo repo-n private dhe vendos docs në strukturë të kontrolluar**
   `canonical / evidence / archive / docs-private`.

---

## 5. Çfarë nuk duhet të ngrihet në kod pa konfirmim

| Zonë | Pse jo |
| --- | --- |
| TVSH për produktet KABI | Mund të ndryshojë fiskalizimin, librat dhe faturat. |
| Trajtimi i fermerëve / box 51 / flat-rate | Është çështje kontabël/tatimore, jo vetëm ERP. |
| COGS dhe valuation | Ndikon P&L, stok, prodhim, fitim/humbje. |
| PLANET finance settlement | Nëse barazimi del gabim, sistemi nuk i shërben personit që kontrollon financat. |
| Floor/KDS/banak/printer routing | Ndikon punën live të restorantit; backup-i nuk e mjafton. |
| Printera/pajisje fiskale | Prek blerje hardware; duhet konfirmim ATK/vendor para blerjes. |
| Inventari i pajisjeve ekzistuese | Prek kosto, scope integrimi dhe testim në terren. |
| `RESORT` si strukturë biznesi/lokacioni | Prek multi-company/business-unit model dhe raportim. |
| SEF certification behavior | PRO DATA nuk e zëvendëson ATK test/certification. |
| KABI product master cleanup | Lista aktuale është source-of-truth derisa KABI ta konfirmojë ndryshe. |

---

## 6. Si përditësohet ky tracker

Kur vjen përgjigje:

1. ndrysho statusin e rreshtit (`answered`, `approved`, `closed`, etj.);
2. vendos ku është evidence: email, PDF, Excel, foto, meeting notes, screenshot, ATK response;
3. përditëso dokumentin canonical që ndikohet;
4. nëse bëhet vendim final, reflektoje në Master/SSOT/ADR;
5. nëse përgjigjja hap pyetje të re, shto rresht të ri këtu.

---

## 7. Verdikti

Ky tracker është tani kontrolli i jashtëm i projektit.

Puna teknike/evidence për PRO DATA dhe accounting është shumë e fortë. Gjërat që mbeten nuk janë “mungesë ideje”; janë konfirmime reale nga njerëzit që mbajnë përgjegjësi: kontabilisti, financat e PLANET-it, fabrika e KABI-t dhe ATK.

Kjo është pozita e shëndetshme para development-it: dimë çfarë kemi, dimë çfarë presim, dimë kush e zhbllokon dhe dimë çfarë nuk guxojmë të shpikim.
