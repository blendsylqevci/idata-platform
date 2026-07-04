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

Prioritetet:

- `P0` — bllokon accounting/SEF/KABI live/repo start.
- `P1` — duhet para freeze të modulit.
- `P2` — e forcon produktin, por nuk bllokon skeleton-in.

---

## 3. Tracker kryesor

| ID | Workstream | Konfirmimi që kërkohet | Owner / burimi | Status | Prioritet | Evidence / doc | Çfarë zhbllokon |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `ACC-001` | Accounting | Dërgimi i `Accountant Review Pack v0` te kontabilisti. | Blend → kontabilisti | `answered` | P0 | `docs/Accountant_Review_Pack_v0_2026-06-29.md` | Përgjigje fillestare pranuar; disa gates mbeten pending. |
| `ACC-002` | Accounting / VAT | Krahasimi i qershorit 2026: sales VAT `XX,XXX.XX`, purchase VAT `XX,XXX.XX`, net `-XX,XXX.XX` kundër deklaratës zyrtare. | Kontabilisti | `answered` | P0 | `docs/VAT_Book_Mapping_v0_2026-06-29.md` | Qershori nuk është deklaruar ende; përdor muaj të mbyllur më herët për sign-off të parë. |
| `ACC-003` | Accounting / VAT | Konfirmim i tax codes `T0/T8/T18/TL`, llogaritë `22000/13000/21200`, bucket-et dhe rounding. | Kontabilisti / ATK nëse duhet | `answered` | P0 | `docs/Accountant_Review_Pack_v0_2026-06-29.md` | Mixed VAT në blerje u konfirmua; tax codes/accounts/rounding final ende për konfirmim. |
| `ACC-004` | PLANET parity | Cilat raporte PRO DATA i përdor kontabilisti çdo muaj dhe cilat duhet të jenë 1:1 në MVP. | Kontabilisti | `answered` | P1 | `docs/PRODATA_Evidence_Traceability_Matrix_2026-06-29.md` | Librat Shitje/Blerje janë harmonizuar me ATK; raportet e tjera parity mbeten për validim gjatë muajit të deklaruar. |
| `ACC-005` | KABI VAT | TVSH për secilin produkt KABI: 8%, 18%, 0%, exempt/out-of-scope nëse ka. | Kontabilisti / ATK / KABI admin | `sent` | P0 | `docs/KABI_Logjika_Operacionale_Kontabiliteti_Draft.md` | KABI mbetet pending; kontabilisti do të marrë info kur të jetë në Kosovë/KABI. |
| `ACC-006` | KABI farmers | Trajtimi i fermerëve: pa faturë, flat-rate/box 51, avanse, offset, pagesë mujore, dokumentim AUVK. | Kontabilisti / ATK / KABI admin | `sent` | P0 | `docs/Accountant_Review_Pack_v0_2026-06-29.md` | Pending; kontabilisti do të shkojë te KABI dhe të marrë informata. |
| `ACC-007` | COGS / inventory | Metoda zyrtare: mesatare e ponderuar, FIFO, batch costing/yield, packaging/loss/by-products. | Kontabilisti + KABI/PLANET management | `sent` | P0 | `docs/Accounting_10_10_Hardening_Checklist_2026-06-29.md` | Zhbllokon cost layers, COGS dhe inventory valuation. |
| `ACC-008` | Corrections | Rregullat për kthime, skadencë, write-off, note krediti/debiti, periudha e raportimit. | Kontabilisti | `sent` | P1 | `docs/Accountant_Review_Pack_v0_2026-06-29.md` | Zhbllokon reversal/correction policy. |
| `ACC-009` | PLANET sales VAT | PLANET restaurant sales default VAT rate. | Kontabilisti | `approved` | P0 | `docs/Accountant_Review_Pack_v0_2026-06-29.md` | PLANET sales default = 18% / `T18`; implement as PLANET config, not global hardcode. |
| `ACC-010` | Official closed-month validation | Kontabilisti të dërgojë një muaj më të hershëm të deklaruar: Libri i Shitjes, Libri i Blerjes, deklarata TVSH. | Kontabilisti | `open` | P0 | future accountant files | Zëvendëson qershorin për sign-off të parë derisa qershori të deklarohet. |
| `ACC-011` | ATK book format | Konfirmim që Libri i Shitjes dhe Libri i Blerjes janë të harmonizuar me ATK. | Kontabilisti | `approved` | P0 | `docs/Accountant_Review_Pack_v0_2026-06-29.md` + future official closed-month files | Mapping-u i librave qëndron; verifikohet me muaj të deklaruar. |
| `FIN-001` | PLANET finance | Takim live me vajzën e financave / personin që bën barazimet. | Blend → PLANET finance | `open` | P0 | `docs/Finance_Operations_Workflow_Spec_2026-06-29.md` | Validon ekranet e finance operations. |
| `FIN-002` | PLANET finance | Një ditë reale: saldo/float, cash, kartelë, POS terminale, shpenzime, withdrawals, handover te kontabilisti. | PLANET finance | `open` | P0 | `docs/Finance_Operations_PLANET_KABI_2026-06-29.md` | Zhbllokon barazim ditor, cash office dhe payment reconciliation. |
| `FIN-003` | PLANET POS/KDS | Floor plan real, zona/tavolina, banak i brendshëm/jashtëm, printera, KDS 10–12 ekrane, lokacion → printer/KDS. | PLANET operations / Donati | `open` | P1 | Wave 2 + user requirements | Zhbllokon POS/KDS UX dhe konfigurim lokacioni. |
| `OPS-001` | PLANET structure | Sqarim i termit `RESORT`: a është vetëm emër/lokacion/business unit brenda PLANET-it, apo subjekt/njësi tjetër që duhet modeluar veç. | Donati / PLANET management / kontabilisti nëse duhet | `open` | P0 | Master docs + SSOT decisions | Zhbllokon `business_unit/location` model pa gabuar tenant/legal structure. |
| `KABI-001` | KABI farmers | Formula USAID për çmimin e qumështit sipas yndyrës/cilësisë. Teknikisht e validuar kundër workbook-ut privat Maj 2026; mbetet konfirmim biznesor. | KABI admin / teknologu / kontabilisti | `technical-validated / waiting-business-signoff` | P0 | `docs/canonical/03-kabi/USAID_Formula_Validation_Gate_2026-07-04.md` | Zhbllokon dizajnin e modulit Pranim + Shlyerje Fermeri; production final pas konfirmimit. |
| `KABI-002` | KABI intake/QC | Fushat e sakta të pranimit: litra/kg, yndyrë, aciditet/pH, temperaturë, SNF, antibiotikë, ujë, klasë, refuzim. | Fabrika / teknologu / laboratori | `open` | P0 | `docs/KABI_Logjika_Operacionale_Kontabiliteti_Draft.md` | Zhbllokon milk reception + QC workflow. |
| `KABI-003` | KABI products | Lista zyrtare e produkteve me njësi, barkod, TVSH, afat skadence, kategori, packaging, catch-weight. | KABI admin + kontabilisti | `open` | P0 | user-provided product list | Zhbllokon product master pa “normalizim” të gabuar. |
| `KABI-004` | KABI production | BOM/receta, yield, batch/LOT, skadenca, pasterizimi, humbje teknologjike, by-products/refuz. | Teknologu / fabrika / kontabilisti | `open` | P1 | `docs/KABI_Logjika_Operacionale_Kontabiliteti_Draft.md` | Zhbllokon production module pas MVP. |
| `KABI-005` | KABI van-sales | Rruga reale e shoferit: ngarkim, shitje cash/kredi, kthime/skadencë, gajbe, POD, barazim mbrëmje, offline. | KABI sales/logistics | `open` | P0 | user factory findings | Zhbllokon mobile van-sales MVP. |
| `ATK-001` | SEF | SEF test phase active të mbahet si track i veçantë; identifikuesit mbeten vetëm private. | iCode / ATK | `approved` | P0 | private SEF docs, jo publik | Zhbllokon PoC SEF në test environment. |
| `ATK-002` | SEF / technical | PoC me `github.com/fiskalizimi`: PKI/CSR, coupon proto, signature, QR, offline retry, tax mapping. | iCode dev | `open` | P0 | official Fiskalizimi GitHub + SEF docs | Zhbllokon aplikim/certifikim teknik. |
| `ATK-003` | SEF / certification | Aplikimi final për certifikim dhe mirëmbajtje të SEF pas testimit të suksesshëm. | iCode / ATK | `open` | P0 | `docs/ATK_SEF_Offline_Faturimi_Findings.md` | Bllokon KABI/PLANET live me fiskalizim real. |
| `ATK-004` | SEF / hardware | Konfirmim: a kërkohet pajisje fiskale hardware apo mjafton SEF software + printer termik/standard sipas rregullave të ATK-së. | iCode / ATK / hardware vendor | `open` | P0 | `docs/ATK_SEF_Offline_Faturimi_Findings.md` + official SEF docs | Zhbllokon blerjen e printerëve/pajisjeve pa risk. |
| `HW-001` | Hardware inventory | Inventari i pajisjeve ekzistuese te PLANET/KABI: PC/NUC, printerë, ekrane, tabletë/PDA, peshore, rrjet, UPS, POS terminale. | Klienti / PLANET / KABI | `open` | P1 | future hardware inventory sheet/photos | Zhbllokon ripërdorim pajisjesh dhe listën finale të blerjes. |
| `REPO-001` | Repo | Krijimi i repo-s private me emër neutral. | Blend | `open` | P0 | Faza 0 pack / scaffold | Zhbllokon development workflow. |
| `REPO-002` | Project management | Krijimi i Notion/board për tasks, gates, confirmations dhe status. | Blend | `open` | P1 | Faza 0 docs | Zhbllokon ndjekjen e punës pa humbje. |
| `REPO-003` | Documentation control | Bashkimi i raw docs nga Claude/Codex në strukturë canonical/evidence/archive/private. | Blend + Codex | `open` | P0 | all raw docs | Zhbllokon source-of-truth profesional. |

---

## 4. Veprimet më të rëndësishme në 72 orët e ardhshme

1. **Kërko nga kontabilisti një muaj të mbyllur më herët**
   Dokumentet: Libri i Shitjes, Libri i Blerjes, Deklarata zyrtare e TVSH-së.
   Objektivi: sign-off i parë me muaj të deklaruar, sepse qershori 2026 ende nuk është deklaruar.

2. **Cakto takim me financat e PLANET-it**
   Dokumenti: `docs/Finance_Operations_Workflow_Spec_2026-06-29.md`
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
