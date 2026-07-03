# Accountant Review Pack v0

**Data:** 2026-06-29
**Status:** canonical review pack / partially answered 2026-06-30 / pending official closed-month files
**Scope:** PLANET parity + shared accounting core + KABI accounting gates
**Qëllimi:** t’i japim kontabilistit një listë të qartë vendimesh për t’i konfirmuar, që accounting core të mos mbetet me hamendje.

Ky dokument nuk është dokument marketingu dhe nuk është dokument teknik për klientin. Është **paketë konfirmimi** për kontabilistin, në mënyrë që iData+ të ndërtohet me logjikë kontabël të saktë, të testueshme dhe të pranueshme për ATK.

---

## 1. Çfarë kërkojmë nga kontabilisti

Kontabilisti duhet të konfirmojë:

1. a përputhen numrat e rindërtuar të TVSH-së për muajin test me deklaratën zyrtare;
2. a janë bucket-et e Librit të Shitjes/Blerjes të sakta;
3. a janë tax codes dhe llogaritë e TVSH-së të sakta për përdorim;
4. si trajtohen rastet e veçanta: fermerë, TVSH 8/18, TL/exempt, non-deductible VAT, reverse charge, note krediti/debiti;
5. cila metodë e COGS/vlerësimit të inventarit duhet përdorur;
6. cilat janë rregullat kontabël për KABI-specific flows.

Pa këto përgjigje, mund të ndërtojmë skeleton-in dhe test engine-in, por nuk duhet ta quajmë kontabilitetin “final 10/10”.

---

## 2. Evidence që kemi për PLANET

Nga backup-i i PRO DATA-s kemi:

- Libri i Shitjes;
- Libri i Blerjes;
- GL / Libri i Madh;
- chart of accounts;
- raw sales/purchases/payments/POS/stock;
- posting procedures;
- VAT monthly summary të rindërtuar nga librat;
- sample real nga kontabilisti për Librin e Blerjes.

Kjo evidence është shumë e fortë për **PLANET/shared-core**, por deklarata zyrtare e TVSH-së nga kontabilisti mbetet burimi final i krahasimit.

---

## 3. Testi kryesor — PLANET2, qershor 2026

Nga librat e rindërtuar të PRO DATA-s për PLANET2, qershor 2026:

| Metric | Value |
| --- | ---: |
| Libri i Shitjes — rows | 1,848 |
| Sales net / pa TVSH | XX,XXX.XX |
| Sales VAT / TVSH output | XX,XXX.XX |
| Sales gross / me TVSH | XX,XXX.XX |
| Libri i Blerjes — rows | 188 |
| Purchase net / pa TVSH | XX,XXX.XX |
| Purchase VAT / TVSH input | XX,XXX.XX |
| Purchase gross / me TVSH | XX,XXX.XX |
| VAT net payable / output - input | -XX,XXX.XX |

### Pyetja A — krahasimi me deklaratën zyrtare

Ju lutem konfirmoni:

1. A përputhen këto shifra me deklaratën zyrtare të TVSH-së për qershor 2026?
2. Nëse jo, cilat janë diferencat?
3. A mungon ndonjë rregullim manual, korrigjim, notë kredit/debit, import, reverse charge ose rresht që nuk del nga librat e rindërtuar?
4. A ka diferenca për shkak të rrumbullakimit?
5. A është pozicioni `-XX,XXX.XX` pozicion kreditimi i saktë për këtë muaj, apo deklarata zyrtare e trajton ndryshe?

**Vendimi që mbyllet:** nëse përputhet, tax-book engine vërtetohet end-to-end për një muaj real PLANET.

### 3.1 Përgjigje nga kontabilisti — 2026-06-30

Kontabilisti sqaroi se **TVSH e qershorit 2026 ende nuk është deklaruar**, sepse muaji ende nuk ka përfunduar dhe disa fatura/shpenzime të qershorit mund të vijnë pas mbylljes së muajit. ATK lejon deklarimin e TVSH-së së qershorit deri më **20.07.2026**.

Prandaj, qershori mbetet muaj i mirë për analizë teknike, por **nuk mund të përdoret ende si krahasim zyrtar me deklaratën e TVSH-së**.

Kontabilisti ofroi mundësinë të dërgojë një muaj më të hershëm të mbyllur:

- Librin e Blerjes;
- Librin e Shitjes;
- Deklaratën zyrtare të TVSH-së;
- si janë deklaruar realisht për atë muaj.

**Vendim operativ:** për validation/sign-off të parë duhet të përdorim **një muaj të mbyllur dhe të deklaruar**, jo qershorin 2026 derisa të dorëzohet deklarata zyrtare.

**Status:** `answered / pending official closed-month files`.

Kontabilisti gjithashtu konfirmoi se **Librat e Shitjes dhe Blerjes janë komplet të harmonizuara me ATK-në**. Kjo e forcon mapping-un tonë, por validimi final kërkon ende një muaj të deklaruar me file-t zyrtarë.

---

## 4. Tax codes dhe llogaritë e TVSH-së

### 4.1 Tax codes nga PRO DATA

| Code | Meaning | Rate | Status |
| --- | --- | ---: | --- |
| `T0` | Pa tatim / 0% | 0% | për konfirmim bucket |
| `T8` | TVSH e reduktuar | 8% | për konfirmim produktesh |
| `T18` | TVSH standarde | 18% | për konfirmim |
| `TL` | TVSH e liruar | 0% | duhet konfirmuar saktësisht |

### 4.2 Llogaritë e TVSH-së nga PRO DATA

| Purpose | Account |
| --- | --- |
| TVSH në shitje / output | `22000` |
| TVSH në blerje / input | `13000` |
| TVSH në doganë / transit | `21200` |

### Pyetja B — konfirmim

1. A janë këto llogari të sakta për modelin e ri?
2. A duhet të përdorim të njëjtat llogari për PLANET?
3. Për KABI, a përdoret e njëjta strukturë kontosh apo duhen llogari të veçanta për prodhim/qumësht/fermerë?
4. Si duhet të trajtohet `TL` në libra dhe në SEF/fiskalizim: exempt, out-of-scope, zero-rated apo diçka tjetër?

**Vendimi që mbyllet:** finalizimi i `tax_code` dhe `VAT account mapping`.

---

## 5. Libri i Shitjes — konfirmim i mapping-ut

Në iData+ do të ndërtojmë Librin e Shitjes me këto kategori:

- shitjet totale;
- furnizimet e përjashtuara pa të drejtë kreditimi;
- shitjet me ngarkesë të kundërt;
- furnizimet e trajtuara si eksporte me të drejtë kreditimi;
- shitjet e shërbimeve jashtë vendit;
- eksportet;
- shitjet e tatueshme 18%;
- TVSH e llogaritur për shitjet 18%;
- shitjet e tatueshme 8%;
- TVSH e llogaritur për shitjet 8%;
- notat debitore/kreditore 18%;
- notat debitore/kreditore 8%;
- reverse-charge related buckets;
- total pa TVSH, TVSH totale, total me TVSH.

### Pyetja C — Libri i Shitjes

1. A janë këto kategori të sakta për raportim ATK?
2. A ka kolona/kategori që duhet t’i shtojmë?
3. Si trajtohet saktë një notë krediti për kthim malli?
4. Në cilën periudhë raportohet korrigjimi: periudha e faturës origjinale apo periudha e notës?
5. A duhet që POS/kuponët fiskalë të shfaqen të grumbulluar apo individualisht në Librin e Shitjes?

**Vendimi që mbyllet:** finalizimi i sales tax-book export.

### 5.1 Përgjigje nga kontabilisti — shitjet PLANET

Kontabilisti konfirmoi se **PLANET, si restaurant, i shet të gjitha artikujt me TVSH 18%**, sepse trajtohet si restaurant/biznes luksi.

**Implikim për iData+:**

- për PLANET/POS, default sales VAT = `T18` / 18%;
- kjo është konfigurim për business/tenant `PLANET`, jo rregull global për të gjitha bizneset;
- KABI nuk trashëgon këtë rregull — produktet KABI mbeten për konfirmim veçmas.

**Status:** `approved for PLANET sales VAT default`.

---

## 6. Libri i Blerjes — konfirmim i mapping-ut

Në iData+ do të ndërtojmë Librin e Blerjes me këto kategori:

- blerjet/importet pa TVSH;
- blerjet/importet investive pa TVSH;
- blerjet/importet me TVSH jo të zbritshme;
- blerjet/importet investive me TVSH jo të zbritshme;
- importet e tatueshme 18% / 8%;
- TVSH e zbritshme për importet 18% / 8%;
- importet investive 18% / 8%;
- blerjet vendore 18% / 8%;
- TVSH e zbritshme për blerjet vendore 18% / 8%;
- blerjet investive vendore 18% / 8%;
- blerjet nga fermerët vendore 8%;
- TVSH e zbritshme për blerjet nga fermerët vendore 8%;
- notat debitore/kreditore 18% / 8%;
- reverse-charge credit buckets;
- total pa TVSH, TVSH totale, total me TVSH.

### Pyetja D — Libri i Blerjes

1. A janë këto kategori të sakta për raportim ATK?
2. A është e saktë që një faturë mund të ketë 18% dhe 8% në të njëjtin rresht raportues?
3. Si dallohen blerjet investive nga blerjet normale?
4. Si trajtohet TVSH jo e zbritshme?
5. Si trajtohet `TVSHKapitalizuese` / TVSH e kapitalizuar?
6. Si trajtohen importet dhe DUD?
7. Si trajtohet reverse charge?
8. A duhet ndonjë kolonë shtesë për raportim praktik të kontabilistit?

**Vendimi që mbyllet:** finalizimi i purchase tax-book export.

### 6.1 Përgjigje nga kontabilisti — mixed VAT në blerje

Kontabilisti konfirmoi se në blerje një faturë mund të ketë artikuj me norma të ndryshme TVSH-je. Praktikisht:

- artikulli i parë mund të jetë me 18%;
- artikulli tjetër mund të jetë me 8%;
- norma e TVSH-së selektohet sipas artikullit;
- në fund të faturës/librave dalin të gjitha normat që i përkasin artikujve të caktuar.

Kjo e konfirmon dizajnin tonë:

```text
1 faturë -> 1 tax_book_entry -> shumë tax_book_entry_bucket rows
```

**Status:** `approved / mixed-rate purchase invoices required`.

---

## 7. Mixed VAT invoices

PRO DATA dhe sample-i real tregojnë se një faturë blerjeje mund të ketë edhe 18% edhe 8%.

Në iData+ vendimi teknik është:

```text
1 faturë -> 1 tax_book_entry -> shumë tax_book_entry_bucket rows
```

### Pyetja E — mixed VAT

1. A është kjo mënyra e saktë për të raportuar fatura me disa norma TVSH?
2. A duhet raporti të tregojë një rresht për faturë apo një rresht për normë TVSH?
3. Nëse fatura ka shumë norma, a duhet totalet të dalin në të njëjtin rresht me kolona të ndara, si PRO DATA?

**Vendimi që mbyllet:** modeli i mixed-rate invoices.

---

## 8. Rounding / rrumbullakimi

Rounding duhet të jetë i definuar që në fillim, sepse ATK/SEF, librat dhe GL duhet të përputhen.

### Pyetja F — rounding

1. A llogaritet TVSH për çdo rresht artikulli apo në total fature?
2. A rrumbullakohet në dy decimale në nivel rreshti, fature apo raporti?
3. Nëse ka diferencë rounding, në cilën konto shkon?
4. A ka rregull të ndryshëm për SEF/fiskalizim krahas GL/accounting?

**Vendimi që mbyllet:** formula zyrtare e llogaritjes së TVSH-së dhe rounding.

---

## 9. PLANET — parity me PRO DATA

Për PLANET, objektivi është që kontabilisti të mos humbë asnjë funksion kryesor që e ka përdorur në PRO DATA.

### Pyetja G — parity

1. Cilat raporte nga PRO DATA i përdorni çdo muaj?
2. A mjaftojnë këto si MVP:
   - Libri i Shitjes;
   - Libri i Blerjes;
   - VAT summary/deklaratë draft;
   - GL account card;
   - kartela klienti;
   - kartela furnitori;
   - AR/AP aging;
   - cash/bank reports;
   - stock/COGS reports?
3. A përdorni raport tjetër që duhet ta kemi 1:1?
4. A ka ndonjë kolonë në Libri i Shitjes/Blerjes që duhet të dalë në format identik me PRO DATA?

**Vendimi që mbyllet:** minimum report pack për PLANET go-live.

---

## 10. KABI — product VAT

KABI ka produkte qumështore me norma të mundshme 8%/18%, por nuk duhet ta nxjerrim këtë nga PLANET.

### Pyetja H — TVSH për produktet KABI

Ju lutem jepni listën finale:

| Produkt / kategori | TVSH | Shënim |
| --- | --- | --- |
| Qumësht | 8% / 18% / tjetër | për konfirmim |
| Kos | 8% / 18% / tjetër | për konfirmim |
| Jogurt | 8% / 18% / tjetër | për konfirmim |
| Ajran | 8% / 18% / tjetër | për konfirmim |
| Djathë | 8% / 18% / tjetër | për konfirmim |
| Gjizë | 8% / 18% / tjetër | për konfirmim |
| Speca me ajkë | 8% / 18% / tjetër | për konfirmim |
| Ambalazh/gajbe nëse faturohet | 8% / 18% / tjetër | për konfirmim |

**Vendimi që mbyllet:** product VAT mapping për KABI.

---

## 11. KABI — fermerët dhe qumështi

Kjo është pika më kritike për KABI.

### Pyetja I — farmer VAT / flat-rate / pa faturë

1. Si trajtohet qumështi i blerë nga fermerët?
2. A përdoret skemë e normës së sheshtë për fermerë?
3. A duhet të përdoret bucket-i `[51] / Blerjet nga fermerët`?
4. Si trajtohen fermerët që nuk lëshojnë faturë?
5. Çfarë dokumenti duhet të krijojë sistemi për pranimin ditor të qumështit?
6. A bëhet fatura/përmbledhja mujore për fermerin?
7. Si trajtohen avanset e fermerëve?
8. Si trajtohen inputet/hua që u jepen fermerëve dhe zbriten më vonë?
9. A ka TVSH të zbritshme për blerjet nga fermerët? Nëse po, si llogaritet?
10. Çfarë duhet të shfaqet në kartelën e fermerit / AVUK?

**Vendimi që mbyllet:** farmer settlement + VAT treatment.

---

## 12. KABI — COGS dhe vlerësimi i inventarit

KABI ka prodhim, batch, yield, by-products dhe humbje.

### Pyetja J — COGS / inventory valuation

1. Cila metodë përdoret për vlerësimin e inventarit në pasqyra financiare: FIFO, mesatare e ponderuar, batch costing apo tjetër?
2. Për KABI, a duhet kostoja të lidhet me batch-in e prodhimit?
3. Si trajtohen humbjet normale të prodhimit?
4. Si trajtohen humbjet jashtë normales / prishjet?
5. Si trajtohet hirra / nën-produktet?
6. Si trajtohen produktet e skaduara?
7. Si trajtohet kthimi i produkteve nga klienti?
8. A kërkohet note krediti për çdo kthim?
9. Kur njihet COGS: në shitje, në dorëzim, në fiskalizim apo në mbyllje dite?

**Vendimi që mbyllet:** COGS method dhe stock accounting.

---

## 13. KABI -> PLANET inter-company

KABI mund ta furnizojë PLANET-in me produkte.

### Pyetja K — inter-company

1. A duhet KABI t’i lëshojë faturë PLANET-it për çdo furnizim?
2. A trajtohet si shitje normale B2B e KABI dhe blerje normale e PLANET?
3. A duhet të ketë çmime të veçanta inter-company?
4. Si trajtohet TVSH për KABI -> PLANET?
5. A nevojiten raporte konsoliduese me eliminime inter-company?

**Vendimi që mbyllet:** accounting treatment për furnizimin KABI -> PLANET.

---

## 14. Kthimet, write-off dhe skadenca

### Pyetja L — correction/write-off

1. Kur klienti kthen mall, a krijohet gjithmonë notë krediti?
2. Nëse malli kthehet në stok, si trajtohet TVSH dhe COGS?
3. Nëse malli është i skaduar dhe asgjësohet, si trajtohet TVSH?
4. A kërkohet dokument i veçantë për write-off?
5. Kush duhet ta aprovojë write-off?
6. Në cilën periudhë raportohet kthimi/skadenca?

**Vendimi që mbyllet:** reversal/correction/write-off policy.

---

## 15. Checklist për sign-off

Kontabilisti mund ta kthejë me status:

| Area | Status | Koment |
| --- | --- | --- |
| Qershor 2026 VAT reconstruction | Approved / Differences / Need more info |  |
| Tax codes T0/T8/T18/TL | Approved / Changes |  |
| VAT accounts 22000/13000/21200 | Approved / Changes |  |
| Libri i Shitjes mapping | Approved / Changes |  |
| Libri i Blerjes mapping | Approved / Changes |  |
| Mixed VAT invoice handling | Approved / Changes |  |
| Rounding rule | Approved / Changes |  |
| PLANET report pack | Approved / Changes |  |
| KABI product VAT | Approved / Changes |  |
| Farmer flat-rate / box 51 | Approved / Changes |  |
| Farmer without invoice treatment | Approved / Changes |  |
| KABI COGS/inventory valuation | Approved / Changes |  |
| KABI -> PLANET inter-company | Approved / Changes |  |
| Returns/write-off/skadenca | Approved / Changes |  |

---

## 16. Çfarë zhbllokohet pas përgjigjeve

| Nëse konfirmohet | Zhbllokohet |
| --- | --- |
| VAT declaration comparison | Tax-book engine end-to-end validation for PLANET |
| Tax codes/accounts | Accounting config v1 |
| Libri Shitjes/Blerjes mapping | VAT reports implementation |
| Mixed VAT | Purchase-book implementation without risk |
| Product VAT KABI | KABI sales/fiscal config |
| Farmer treatment | KABI milk purchase/farmer settlement |
| COGS method | Inventory costing engine |
| Inter-company | KABI -> PLANET flow |
| Returns/write-off | Correction/reversal policy |

---

## 17. Rregulli final

Nëse një përgjigje mungon:

- nuk e shpikim;
- e shënojmë si decision gate;
- ndërtojmë config që e lejon të vendoset më vonë;
- nuk e bëjmë hardcode në kod.

Ky dokument duhet të ruhet si evidence kur kontabilisti përgjigjet. Përgjigjet pastaj reflektohen në:

- `VAT_Book_Mapping_v0_2026-06-29.md`;
- `Accounting_Posting_Rules_v0_2026-06-29.md`;
- `Accounting_10_10_Hardening_Checklist_2026-06-29.md`;
- SSOT / decision gates.

> **Shënim konfidencialiteti:** shifrat reale të klientit janë hequr me qëllim (rregullore privacy). Vlerat reale ruhen te `evidence-private-local/` (jashtë git). Këtu mbetet vetëm struktura/logjika e rakordimit dhe hartat e kutive të deklaratës.
