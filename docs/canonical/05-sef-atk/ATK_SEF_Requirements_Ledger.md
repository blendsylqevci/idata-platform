# ATK/SEF Requirements Ledger — kontrollet e corpus-it të regjistruar (v1.2)

**Versioni:** 1.2 · **Cutoff i auditit:** 2026-07-12 · **Përditësuar:** 2026-07-12 (v1.2: disposition LAPT 1–123 + U23 i ndarë + T2-09/T2-10 me rreshta realë + faqet KT/blk nga renderimi) · **Repo baseline:** `55126b1b48004e4fba4a5604c9717cd9456722d3` · **Pronari:** Group Owner (Human Gate)
**Statusi:** canonical — effective on merge. **Ky ledger është BURIMI EXHAUSTIV i statusit të pajtueshmërisë** (Gate-i mban verdiktin/gates; Register-i mban burimet).
**Baza:** *independent full-corpus audit, cutoff 2026-07-12* (zero-trust; Fable/Claude output i përjashtuar nga mbledhja e evidence-it — përdorur vetëm për reconciliation pas verdiktit).

**Rregulli i numërimit:** një rresht = **një kontroll i testueshëm/i mbyllshëm në mënyrë të pavarur**. Lista fushash të detyrueshme = një kontroll, përveç kur elementët kanë lifecycle/gate të ndryshëm. **Metodologjia e mapimit:** një klauzolë burimi MUND të mapojë disa closure keys (p.sh. U20 → T2-02+T2-03) OSE ndahet në rreshta atomikë kur detyrimet kanë jetë të pavarur (p.sh. U23/U27; T04/T22).

## Certifikata e mbulimit (RILLOGARITUR NGA ZERO — v1.2)

**(a) Source-requirement rows: 108** (102 aktive/potencialisht të aplikueshme + 6 lineage) · **(b) Unique closure/product keys: 40** (kolona `Closure key`; T2-01..T2-10 TË GJITHA me rreshta realë).

| Status (source-rows) | Count |
|---|---:|
| MISSING | 60 |
| PARTIAL | 18 |
| EXTERNAL_GATE | 17 |
| CONTRADICTS | 5 |
| PROVEN_LOCAL | 2 |
| N/A_LINEAGE | 6 |
| **TOTAL** | **108** |

**38 kontrolle** me `CONFIRM_ATK/ACCOUNTANT/LAWYER/AIP`. **Asnjë `PASS`.**
**CONTRADICTS: 5 source-rows = 4 defekte unike** (money `T2-01` · QR `T2-02` · NUIKF `T2-03` · **origjina e afatit 48h `T2-08` — U27**). **LIVE placeholder = MISSING (G06/`T2-04`), JO CONTRADICTS.**
**Konventa `—`:** vetëm me arsye eksplicite; web/GitHub/repo = `N/A — non-paginated` + locator. **Faqet e KT/amendamentit/CORE-08 = nga renderimi i PDF-ve** (skanuar → faqe të verifikuara vizualisht).

**108 vs 102/94/278:** numrat rillogariten sa herë korrigjohet semantika — nuk ruhen për raporte; 278 = extraction baseline historik.

## Ledger — kontrollet (108)

| ID | Source | Ref | Faqe | Control | Status | Actor/aplikueshmëria | Kategori | Afat/sanksion | Repo evidence | Confirmation | Closure key | Closure owner/gate |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| L01 | LAPT (CORE-01) | 6.1–2 | 9–10 | Libra/regjistra me shkrim ose elektronikë; software sipas kritereve ATK | PARTIAL | tatimpaguesi (KABI/PLANET) | Books/ERP | — · sanksioni: 101(2) 150–2,000€ sipas qarkullimit (f.58–59) | `packages/accounting/src/posting.ts`; `docs/canonical/02-accounting/` | CLEAR | BOOKS-F2 | Product |
| L02 | LAPT | 6.3.1 | 10 | Ruajtje e dokumentacionit së paku 6 vjet | MISSING | tatimpaguesi | Retention | 6 vjet · 101(2) | none | CONFIRM_LAWYER | RETENTION-F1 | Legal/retention |
| L03 | LAPT | 6.3.7 | 10 | Regjistra të shërbimeve elektronike 10 vjet, nëse aplikohet | MISSING | tatimpaguesi (nëse aplikohet) | Retention | 10 vjet · 101(2) | none | CONFIRM_ATK+CONFIRM_LAWYER | RETENTION-F1 | ATK/jurist |
| L09 | LAPT (CORE-01) | 6.3.2 | 10 | Regjistrat e pasurive të zhvlerësueshme >6 vjet — ruhen sa jetëgjatësia e zhvlerësimit **+1 vit** | MISSING | tatimpaguesi | Retention | jetëgjatësia+1 · 101(2) | none | CONFIRM_LAWYER | RETENTION-F1 | Legal/retention |
| L10 | LAPT (CORE-01) | 6.3.3 | 10 | Regjistrat e pasurive për fitime kapitale — deri në shitje/ekspozim **+6 vjet** | MISSING | tatimpaguesi | Retention | shitja+6 · 101(2) | none | CONFIRM_LAWYER | RETENTION-F1 | Legal/retention |
| L11 | LAPT (CORE-01) | 6.3.4 | 10 | Regjistrat e kontratave afatgjata të ndërtimit — **projekti +6 vjet** | MISSING | tatimpaguesi (nëse aplikohet) | Retention | projekti+6 · 101(2) | none | CONFIRM_LAWYER | RETENTION-F1 | Legal/retention |
| L12 | LAPT (CORE-01) | 6.3.8 | 10 | Regjistrat e transaksioneve të arit të investueshëm — **≥10 vjet** (aplikueshmëria për KABI/PLANET = e dyshimtë) | MISSING | tatimpaguesi (nëse aplikohet) | Retention | 10 vjet · 101(2) | none | CONFIRM_LAWYER | RETENTION-F1 | Legal/retention |
| L04 | LAPT | 6.5–6 | 10 | Inventari vjetor deri më 10 janar + dokumentet e origjinës | MISSING | tatimpaguesi | Inventory | 10 janar · 101(2); **109: 30% e vlerës së tregut për mallra pa dokumentacion origjine** (f.62) | none | CLEAR | RETENTION-F1 | ERP |
| L05 | LAPT | 6.7–8 | 10 | Furnizimet regjistrohen dhe jepet kupon teknikisht i rregullt | PARTIAL | tatimpaguesi/POS | Fiscal | — · **101(6): mos-lëshimi i kuponit → gjoba sipas 101(2)**; dështim pajisje/rryme → kupona të shkruar me dorë (f.59); eskalimi 101(7): mbyllje 7/15 ditë | `packages/database/migrations/0003_accounting_posting.sql` | CLEAR | FISCAL-F5 | POS/SEF |
| L06 | LAPT | 6.9 | 10–11 | B2B mbi 300 EUR vetëm bankë/elektronik | MISSING | tatimpaguesi | Payments | 300 € · **101(9): pranimi i pagesës kundër 6.9–6.12 → gjoba sipas 101(2)** (f.59) | none | CLEAR | PAY-GATE-F2/F8 | Payment gate |
| L07 | LAPT | 6.10 | 11 | **>100 € pjesërisht me para + pjesa me kompensim → pjesa CASH detyrimisht bankë/elektronik** (zbatim i 6.9) | MISSING | tatimpaguesi | Payments | 100 € · **101(9)** (f.59) | none | CLEAR | PAY-GATE-F2/F8 | ATK/jurist |
| L08 | LAPT | 43.3–4 | 33 | Librat blerje/shitje elektronikë mujorë ose tremujorë sipas statusit | PARTIAL | tatimpaguesi/kontabilisti | Books | mujor (TVSH) / tremujor · **101(1): 150€/pasqyrë deri 3,000€** (f.58–59) | `docs/canonical/02-accounting/VAT_Book_Mapping_v0_2026-06-29.md` | CONFIRM_ACCOUNTANT | BOOKS-EDI-F2 | Kontabilist |
| L13 | LAPT (CORE-01) | 6.13 | 11 | **Çdo pagë e të punësuarit paguhet përmes institucioni financiar të licencuar** | MISSING | payroll (F1) | Payments/Payroll | — · — | none | CLEAR | PAYROLL-F1 | Payroll F1 |
| L14 | LAPT (CORE-01) | 43.5 | 33 | **Njoftimi i ATK-së për kontratën e punës NJË DITË PARA fillimit të punës** (forma e ATK-së) | MISSING | operacionet/HR | Operations | 1 ditë para · **102.3: 500€/punëtor i padeklaruar** (f.60) | none | CLEAR | OPS-HR-F1 | Operations/HR |
| L15 | LAPT (CORE-01) | 6.4 | 10 | Librat/regjistrat për biznese mbi pragun e TVSH-së mbahen sipas **parimeve të kontabilitetit dhe raportimit financiar** në fuqi | PARTIAL | tatimpaguesi (KABI/PLANET) | Books/ERP | — · 101(2) | `packages/accounting/` (libraria synon këtë; jo e mbyllur) | CONFIRM_ACCOUNTANT | BOOKS-F2 | Kontabilist/Product |
| L16 | LAPT (CORE-01) | 7 (+107 sanksionet) | 11 (+61) | **Aplikueshmëria e regjimit OE** (furnizim/instalim/mirëmbajtje PEF/SF: garanci bankare, ri-licencim 4-vjeçar, riçertifikime) **ndaj iCode si zhvillues/mirëmbajtës SEF** — SEF licencohet sipas UA 35 (U14/U15); a shtrihet edhe regjimi OE? **MOS infero** | EXTERNAL_GATE | iCode | Applicability | — · **107: 150€ per shkelje (instalim 15 ditë pune; servisim 48h; përsëritëse)** (f.61) | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK-013 | ATK/jurist |
| L17 | LAPT (CORE-01) | 8 (+101(5)) | 11–12 | **Qasja e plotë e ATK-së** në libra/regjistra/kompjuterë/PEF-SF/pajisje ruajtjeje — përfshirë te **palët e treta** që mbajnë dokumentacionin (cloud/hosting) brenda e jashtë Kosovës; tatimpaguesi e siguron atë qasje | MISSING | operacionet + legal (cloud custody) | Operations/Access | — · **101(5): 150€/ditë** mos-sigurimi i qasjes (f.59) | none | CLEAR | OPS-ACCESS-F9 | Operations/Legal (lidhet me P10/V08) |
| D01 | Vendimi19-notice (EXT-03d/EXT-04) | LAPT 6.11–6.12 + Vendimi19-notice (EXT-03d/EXT-04) | 11 (LAPT) + njoftimi | B2C: transaksioni me person fizik jo-afarist mbi vlerën e vendimit të ministrit (19/2026: 2,000 € nga 01.06.2026) → bankë/elektronik; **6.12: edhe pjesa CASH e pagesës së pjesshme**. **Ndarje statusi:** product/control = MISSING (s'ka gate pagese në produkt); **source evidence = vetëm njoftimi zyrtar — AKTI ORIGJINAL I NËNSHKRUAR MUNGON publikisht** (EXT-04). MISSING ≠ thjesht "mungon kod" | MISSING | tatimpaguesi | Payments | 2,000 € (njoftimi) · **101(9)** (f.59) | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK-013 | ATK/jurist |
| U01 | UA 01/2026 (CORE-02) | 5.1–2 | 6 | Kupon për çdo furnizim te konsumatori final, përfshirë falas | MISSING | POS/SEF | Fiscal | — | none | CONFIRM_ATK+CONFIRM_ACCOUNTANT | FISCAL-F5 | ATK/kontabilist |
| U02 | UA 01/2026 | 5.3 | 6 | Faturë tatimore për biznes/person juridik, me përjashtimet | MISSING | ERP/faturimi | Fiscal/Books | — | none | CONFIRM_ATK+CONFIRM_ACCOUNTANT | BOOKS-F2 | ATK/kontabilist |
| U03 | UA 01/2026 | 6 | 7 | Koha e kuponit: pagesë, parapagim, furnizim me afat | MISSING | POS/accounting | Fiscal | — | none | CLEAR | POS-F8 | POS/accounting |
| U04 | UA 01/2026 | 7 | 7 | Fushat e plota të kuponit | MISSING | print layer | Print | — | none | CLEAR | PRINT-F7 | Print layer (spec: `Kupon_Print_Spec.md`) |
| U05 | UA 01/2026 | 31.1–3 | 26 | Subjektet retail/shërbime/cash që duhet të përdorin SEF | EXTERNAL_GATE | klasifikimi i subjekteve | Applicability | — | none | CONFIRM_ATK | ATK-013 | ATK |
| U06 | UA 01/2026 | 31.4 | 26 | Njoftime ATK për lokacion/transfer/çregjistrim/ndërprerje | MISSING | operacionet | Operations | — | none | CLEAR | OPS-F9 | Operations |
| U07 | UA 01/2026 | 31.5–7 | 26 | Përjashtime B2B-only, mixed activity dhe vendime ministri | EXTERNAL_GATE | klasifikimi KABI/PLANET | Applicability | — | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK-013 | ATK/jurist |
| U08 | UA 01/2026 | 32.1–2.5, 2.8 | 27 | Nënshkrim, certifikatë, HTTPS, QR dhe mesazhe ATK | PARTIAL | SEF | Transport/PKI | — | `packages/database/src/poc5-sign.ts`; `packages/database/src/poc5-transport.ts` | CLEAR | SEF-F5 | SEF |
| U09 | UA 01/2026 | 32.2.6 | 27 | Lidhje me printer | MISSING | POS/hardware | Print | — | none | CLEAR | PRINT-F7 | PoC-4b/F7 |
| U10 | UA 01/2026 | 32.2.7 | 27 | Upgrade sipas nevojës | MISSING | release governance | Governance | — | none | CLEAR | GOV-F9 | Release governance |
| U11 | UA 01/2026 | 32.2.9 | 27 | Shumëgjuhësi sipas gjuhëve zyrtare | MISSING | UX | UX | — | none | CLEAR | UX-F7 | UX |
| U12 | UA 01/2026 | 32.3 | 27 | Offline secure dhe dërgim automatik pas rikthimit | PARTIAL | SEF/offline | Offline | — | `packages/database/src/poc5-outbox.ts`; `packages/database/migrations/0006_poc5_fiscal.sql` | CLEAR | OFFLINE-F5 | SEF/offline |
| U13 | UA 01/2026 | 34 | 28 | Certifikatë/çelës per instalim dhe identifikuesit e POS | EXTERNAL_GATE | PKI per instalim | PKI | — | none | CONFIRM_ATK | T2-05+ATK-008 | ATK/PKI (T2-05) |
| U14 | UA 01/2026 | 35.1–7 | 29 | Zhvilluesi/mirëmbajtësi certifikohet para ofertës/shitjes/instalimit | EXTERNAL_GATE | iCode si zhvillues | Certification | — | none | CONFIRM_ATK | CERT-F9 | ATK |
| U15 | UA 01/2026 | 35.8–12 | 29 | Kodi SEF + NUI zhvilluesi; njoftim dhe ndalim SEF të pacertifikuar | EXTERNAL_GATE | iCode/tatimpaguesi | Certification | — | none | CONFIRM_ATK | CERT-F9 | ATK |
| U16 | UA 01/2026 | 35.13 | 29 | Përgjegjësi e përbashkët tatimpagues–zhvillues | MISSING | governance | Governance | — | none | CLEAR | GOV-F9 | Governance |
| U17 | UA 01/2026 | 36 | 30 | Tërheqje/anulim certifikate dhe inspektim instalimesh | MISSING | runbook | Certification | — | none | CLEAR | CERT-F9 | Runbook |
| U18 | UA 01/2026 | 37 | 31 | Rrjet i enkriptuar/nënshkruar/direkt HTTPS/kompatibil | PARTIAL | transport | Transport | — | `packages/database/src/poc5-transport.ts` | CLEAR | T2-06 | Transport (T2-06) |
| U19 | UA 01/2026 | 39.1–2 | 33 | Letër/e-kupon/cashless me pëlqim sipas rastit | MISSING | POS/kanalet | Print/Channel | — | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK-013 | ATK/jurist |
| U20 | UA 01/2026 | 39.3–5 | 33 | QR online/offline dhe numër unik automatik | **CONTRADICTS** | SEF | QR/NUIKF | — | `packages/database/src/poc5-sef.test.ts` (firmos PosCoupon; fixture NUIKF invalid) | CLEAR | T2-02+T2-03 | QR/NUIKF (T2-02, T2-03) |
| U21 | UA 01/2026 | 40 | 33 | Njoftim i dukshëm për detyrimin e kuponit | MISSING | POS UX | UX | — | none | CLEAR | POS-F8 | POS UX |
| U22 | UA 01/2026 | 43 | 37 | Kupon korrigjues me referencë te origjinali | PARTIAL | SEF | Fiscal lifecycle | — | `packages/database/src/poc5-sef.test.ts` (ReferenceNo) | CLEAR | FISCAL-F5 | Return/cancel |
| U23 | UA 01/2026 | 44.1–4 | 37 | Kupon offline i plotë dhe transmetim brenda afatit (përmbajtja/rrjedha e transmetimit) | PARTIAL | SEF/offline | Offline/Deadline | 48h nga outage start | `packages/database/migrations/0006_poc5_fiscal.sql` (deadline aktual jo-konform: `created_at + 48h`) | CLEAR | OFFLINE-F5 | Deadline model (T2-08) |
| U27 | UA 01/2026 | 44.1–4 (origjina e afatit) | 37 | **Origjina e afatit 48h = MOMENTI I NDËRPRERJES së lidhjes**; implementimi aktual e llogarit `created_at + 48h` per kupon → **kundërshton burimin** (mbështetje shtesë: forma EDI e blloqeve kërkon "Data e fillimit të ndërprerjes", CORE-08 f.8) | **CONTRADICTS** | SEF/offline | Offline/Deadline | 48h nga outage start · — | `packages/database/migrations/0006_poc5_fiscal.sql` (trigger `created_at+48h`) | CLEAR | T2-08 | Deadline model (Step B) |
| U24 | UA 01/2026 | 44.5–6 | 37 | Pas 48h: njoftim/prova/day-10 procedure | MISSING | operacione/runbook | Offline/Deadline | day-10 | none | CONFIRM_ATK | OFFLINE-F5 | ATK/runbook |
| U25 | UA 01/2026 | 50.1 | 39 | Vendimi i ministrit për grupet/periudhat PEF→SEF | EXTERNAL_GATE | kohortat | Applicability | — | none | CONFIRM_ATK | ATK-013 | **Vendimi nuk u gjet publikisht** (EXT-11/LIN-06) |
| U26 | UA 01/2026 | 50.2, 52 | 39 | Regjimi 60-ditor për subjektet pa PEF/SF dhe blloqet ndërkohë | EXTERNAL_GATE | kohortat/blloqet | Applicability | 60 ditë | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK-013 | ATK/jurist |
| T01 | KT+amend (CORE-03/06) | 25 + amend N2 | 44 (+CORE-06 f.2) | Instalim/përdorim në ambient të përshtatshëm; riparim 48h | MISSING | product/support | Operations | riparim 48h | none | CLEAR | OPS-F7 | Product/support |
| T02 | KT+amend | 25.1–2 | 44 | Çdo shitje/kupon/raportim real-time | PARTIAL | SEF | Fiscal | — | `packages/database/src/poc5-transport.ts` (**vetëm mock**) | CLEAR | SEF-F5 | Mock only → production |
| T03 | KT+amend | hyrje f.44 | 44 | Verifikim saktësie, siguri minimale, UoM/kategori | MISSING | validation | Validation | — | none | CLEAR | VALID-F5 | Validation |
| T04 | KT+amend | 25.3 | 44 | Struktura zyrtare e proto-s (schema drift-lock kundër C#/Go @ pins) | **PROVEN_LOCAL** | SEF | Proto | — | `packages/database/proto/sef-models.proto` + drift-test në `packages/database/src/poc5-sef.test.ts` | CLEAR | PROTO-LOCK | Proto drift-lock — ≠ pranim serveri |
| T05 | KT+amend | 25.4 | 44 | **Unit price 4dp (×10000); item total 2dp (×100/cents)** | **CONTRADICTS** | SEF/money | Money | — | `packages/database/src/poc5-money.ts`; `packages/database/src/poc5-proto.ts` (enkodojnë item total ×10000 — **100× gabim**) | CLEAR | T2-01 | Money scale (T2-01) |
| T06 | KT+amend | 25.5–5.2 | 44–45 | ID unike, write-once/read-many, no-delete, histori | MISSING | DB integrity | Integrity | — | none | CLEAR | INTEGRITY-F1 | DB integrity |
| T07 | KT+amend | 25.5.3 | 45 | Audit login/sale/cancel/config me retention, encryption, integrity | MISSING | audit | Audit | — | none | CLEAR | AUDIT-F1 | Audit |
| T08 | KT+amend | 25.7–8 | 45 | Test/certifikim dhe recertifikim pas ndryshimeve | EXTERNAL_GATE | ATK proces | Certification | — | none | CONFIRM_ATK | CERT-F9 | ATK |
| T09 | KT+amend | 25.9–11 | 45 | Return/exchange/cancel me arsye dhe referencë | PARTIAL | SEF/POS | Fiscal lifecycle | — | `packages/database/src/poc5-sef.test.ts` (ReferenceNo) | CLEAR | FISCAL-F5 | Fiscal lifecycle |
| T10 | KT+amend | 25.12 | 45 | Tax groups A/C/D/E dhe pa TVSH negative | PARTIAL | money/tax | Money/Tax | — | `packages/database/src/poc5-proto.ts` (kodet A/C/D/E në mapim) | CONFIRM_ACCOUNTANT | TAX-F2 | Accountant |
| T11 | KT+amend | 25.15; 26.4 i ri (auto-send) | 46 (+CORE-06 f.2–3 auto-send) | Offline secure, auto-send dhe technical event | PARTIAL | SEF/offline | Offline | — | `packages/database/src/poc5-outbox.ts` (pa technical_event) | CLEAR | OFFLINE-F5 | Offline |
| T12 | KT+amend | 25.16 | 46 | Futje e kuponëve të bllokut manual | MISSING | POS/blloqe | Blocks | — | none | CLEAR | BLOCKS-F5 | Manual blocks |
| T13 | KT+amend | 25.18; 26.13 | 46–47; 49 (26.13) | Print/preview/copy/fushat/58–100mm/readability | MISSING | print layer | Print | — | none | CLEAR | PRINT-F7 | Print |
| T14 | KT+amend | 26.4 i ri | 48 (bazë) · CORE-06 f.2–3 (teksti i ri) | Integritet në defekt, auto-send, eksport CSV/PDF me kërkesë | MISSING | SEF/DB | Integrity/Export | — | none | CLEAR | INTEGRITY-F1 | F1/F5 |
| T15 | KT+amend | 26.5 | 48 | Transmit para print; retry; 24/7; time sync | PARTIAL | SEF | Fiscal/Transport | — | `packages/database/src/poc5-outbox.ts` (**vetëm retry mock/manual**) | CLEAR | SEF-F5 | Retry only → production |
| T16 | KT+amend | 26.6–7 | 48–49 | Gjurmë dërguara/dështuara/gabime/modifikime dhe eksport | MISSING | audit | Audit | — | none | CLEAR | AUDIT-F1 | Audit |
| T17 | KT+amend | 26.8–9 | 49 | Mbrojtja e kodit/të dhënave dhe anti-tamper | MISSING | siguria | Security | — | none | CLEAR | SEC-F5 | Security |
| T18 | KT+amend | 26.10 | 49 | **NUIKF alfanumerik pa ndarës max 16, unik, automatik** | **CONTRADICTS** | SEF | NUIKF | — | `packages/database/src/poc5-sef.test.ts` (fixture me viza/17 karaktere = **invalid**; zero validator/generator/uniqueness runtime) | CLEAR | T2-03 | Fixture/validator (T2-03) |
| T19 | KT+amend | 26.11 | 49 | Integrim accounting/sales/suppliers | MISSING | product | Integration | — | none | CLEAR | ERP-F2 | Product |
| T20 | KT+amend | 26.12 | 49 | Rend offline, sinjal vizual dhe OFFLINE në kupon | PARTIAL | SEF/POS | Offline/Print | — | `packages/database/src/poc5-outbox.ts` (**vetëm rendi**; pa sinjal vizual/print) | CLEAR | OFFLINE-F5 | Only ordering → full |
| T21 | KT+amend | 26.14–15 | 49 | Test-only para certifikimit; prod pas; kod SEF+NUI | EXTERNAL_GATE | iCode/ATK | Certification | — | none | CONFIRM_ATK | CERT-F9 | ATK |
| T22 | KT (CORE-03) | 25.3 (pjesa e çelësit/protokollit) | 44 | **Çelësi privat production + protokolli i plotë nënshkrim→dërgim** (i ndarë nga schema drift-lock i T04) | MISSING | SEF/PKI | Signature/PKI | — · — | none | CLEAR | T2-05+T2-07 | T2-05/T2-07 |
| G01 | GitHub C#/Go @ pins (Register §4) | proto | N/A — non-paginated (web/GitHub locator) | Proto vendored me checksum | **PROVEN_LOCAL** | SEF | Proto | — | `packages/database/proto/sef-models.proto` (byte-sha256 = C#/Go @ pins) | CLEAR | PROTO-LOCK | C#/Go proto — ≠ pranim serveri |
| G02 | GitHub/Swagger | CouponId | N/A — non-paginated (web/GitHub locator) | CouponId unik në tërë biznesin dhe degët | MISSING | DB | Integrity | — | none | CLEAR | INTEGRITY-F1 | DB uniqueness |
| G03 | GitHub/Swagger | sign/send | N/A — non-paginated (web/GitHub locator) | ECDSA P-256, SHA256(base64proto), `/pos/coupon` | PARTIAL | SEF | Signature/Transport | — | `packages/database/src/poc5-sign.ts` (**pa provë serveri**; P1363 vs DER = CONF-02) | CONFIRM_ATK | T2-07 | Server authority (T2-07) |
| G04 | GitHub/Swagger | QR | N/A — non-paginated (web/GitHub locator) | **QR nga `CitizenCoupon`** (base64(CitizenCoupon proto) + firma e tij) | **CONTRADICTS** | SEF | QR | — | `packages/database/src/poc5-sef.test.ts` (**firmos `PosCoupon` për QR**; CitizenCoupon vetëm round-trip) | CLEAR | T2-02 | QR object (T2-02) |
| G05 | GitHub/Swagger | CA | N/A — non-paginated (web/GitHub locator) | `/ca/verify`, CSR, `/ca/signcsr` | EXTERNAL_GATE | PKI/onboarding | PKI | — | none | CONFIRM_ATK | T2-05+ATK-008 | PKI/onboarding (T2-05) |
| G06 | GitHub/Swagger (Register §4) | LIVE harness | N/A — non-paginated (web/GitHub locator) | **Executable LIVE test-host-only harness** — trupi aktual i testit LIVE = placeholder që gjithmonë hedh | MISSING | SEF | Tier2 harness | — · — | `packages/database/src/poc5-sef.test.ts` (placeholder i skip-uar) | CLEAR | T2-04 | T2-04 |
| G07 | Repo evidence (Gate v2 §4) | live path | N/A — non-paginated (repo) | **Heqja e hardcoded synthetic business context nga live path** — rruga live sot mbart ID-në sintetike të PoC-së; s'është tenant-aware | MISSING | SEF | Tier2 hardening | — · — | `packages/database/src/poc5-outbox.ts` / `poc5-transport.ts` (konteksti sintetik) | CLEAR | T2-09 | iCode dev (Step B) |
| G08 | Repo evidence (Gate v2 §4) | evidence policy | N/A — non-paginated (repo) | **Zero identifier/payload/key/cert/signature/raw response në evidence të commit-uar** + allowlisted redaction i PROVUAR me teste | MISSING | SEF/evidence | Tier2 hardening | — · — | none (politika ekziston; prova e testuar mungon) | CLEAR | T2-10 | iCode dev (Step B) |
| C01 | Certification (CORE-04/07) | pre-test | 4 | Pre-test i suksesshëm para aplikimit ("Testim para Aplikimit") | EXTERNAL_GATE | iCode → ATK | Certification | — | none | CONFIRM_ATK | CERT-F9 | ATK |
| C02 | Certification (CORE-05/07) | 5.5 | 4–5 | Formular/ARBK/solution/integration/stack/manual/declaration | MISSING | iCode | Certification | — | none | CLEAR | CERT-F9 | Certification pack |
| C03 | Certification (CORE-07) | 6 | 6 | Commission technical test + 7-day noncritical cure | EXTERNAL_GATE | ATK Komisioni | Certification | 7 ditë pune cure | none | CONFIRM_ATK | CERT-F9 | ATK |
| C04 | Certification (CORE-07) | 7.1 | 6 | Vendim brenda 15 ditëve pune | EXTERNAL_GATE | ATK Komisioni | Certification | 15 ditë pune | none | CONFIRM_ATK | CERT-F9 | ATK |
| C05 | Certification (CORE-07) | 7.4, 7.6 | 6 | Prior notice për ndryshime; reapply pas 30 ditësh | MISSING | recert governance | Certification | 30 ditë | none | CLEAR | CERT-F9 | Recert governance (Gate §3) |
| C06 | Certification (CORE-07) | 8 | 7 | Suspension/withdrawal/cancellation governance | MISSING | runbook | Certification | — | none | CLEAR | CERT-F9 | Runbook |
| B01 | Blloqet 02/2026 (CORE-08) | 5–8 | 6–7 | Bllok i prefiskalizuar, restore 48h, futje brenda 5 ditësh | MISSING | POS/operacione | Blocks | 48h + 5 ditë | none | CLEAR | BLOCKS-F5 | Manual blocks |
| B02 | Blloqet 02/2026 | EDI code | 8 | Kod unik EDI; maksimum 50 seriale | MISSING | EDI/blloqe | Blocks | max 50 seriale | none | CLEAR | BLOCKS-F5 | EDI |
| B03 | Blloqet 02/2026 | continuity | 7–8 | Vazhdimësi SEF→bllok→SEF, dy kopje, fusha manuale — forma EDI kërkon **"Data e fillimit të ndërprerjes"** + fillim/mbarim të numrit rendor (f.8) | MISSING | POS/sekuenca | Blocks | — | none | CLEAR | BLOCKS-F5 | Sequence |
| V01 | TVSH 05/L-037 (EXT-06/07) | 44 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Invoice/prepayment/deadline/self-billing | MISSING | faturimi | VAT/Books | — | none | CONFIRM_ACCOUNTANT | ACC-012 | Accountant |
| V02 | TVSH 05/L-037 | 45–46 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Fushat B2B dhe ndarja multi-rate | MISSING | faturimi | VAT/Books | — | none | CONFIRM_ACCOUNTANT | ACC-012 | Accountant |
| V03 | TVSH 05/L-037 | 47 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Debit/credit note me referencë/arsye/shuma | MISSING | accounting | VAT/Books | — | none | CLEAR | ACC-012 | Accounting |
| V04 | TVSH 05/L-037 | 48 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Bad-debt invoice | MISSING | accounting | VAT/Books | — | none | CONFIRM_ACCOUNTANT | ACC-012 | Accountant |
| V05 | TVSH 05/L-037 | 49 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Simplified invoice/fiscal receipt/pragjet | MISSING | faturimi/POS | VAT/Fiscal | — | none | CONFIRM_ATK+CONFIRM_ACCOUNTANT | ACC-012 | ATK/accountant |
| V06 | TVSH 05/L-037 | 50 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | E-invoice consent dhe autenticitet/integritet | MISSING | faturimi | VAT/E-invoice | — | none | CONFIRM_LAWYER+CONFIRM_ACCOUNTANT | ACC-012 | Jurist/accountant |
| V07 | TVSH 05/L-037 | 56–57 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Retention 6y/20y, electronic storage, serial order | MISSING | retention | Retention | 6/20 vjet | none | CLEAR | ACC-012 | Retention |
| V08 | TVSH 05/L-037 | 56.3 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Njoftim për vendin e ruajtjes dhe qasje ATK | MISSING | operacionet | Operations | — | none | CLEAR | ACC-012 | Operations |
| P01 | Privatësia 06/L-082 (EXT-08) | 4–5 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Parimet dhe baza ligjore e përpunimit | MISSING | platforma | Privacy | — | none | CONFIRM_LAWYER+CONFIRM_AIP | PRIV-001 | Jurist/AIP |
| P02 | Privatësia 06/L-082 | 12–21 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Notice dhe data-subject rights workflow | MISSING | platforma | Privacy | — | none | CLEAR | PRIV-001 | Privacy |
| P03 | Privatësia 06/L-082 | 23–24 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Accountability dhe privacy by design/default | PARTIAL | arkitektura | Privacy | — | `docs/canonical/06-architecture/Reliability_Gate.md` | CLEAR | PRIV-001 | Architecture |
| P04 | Privatësia 06/L-082 | 27 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Controller/processor/subprocessor agreement | MISSING | legal | Privacy | — | none | CLEAR | PRIV-001 | Legal (PRIV-001) |
| P05 | Privatësia 06/L-082 | 29 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | ROPA (regjistri i aktiviteteve të përpunimit) | MISSING | privacy | Privacy | — | none | CLEAR | PRIV-001 | Privacy (PRIV-001) |
| P06 | Privatësia 06/L-082 | 31 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Risk security, CIA, restore dhe testing | PARTIAL | reliability | Privacy/Security | — | `docs/canonical/06-architecture/Reliability_Gate.md` | CLEAR | PRIV-001 | Reliability |
| P07 | Privatësia 06/L-082 | 33–34 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Breach notification 72h dhe high-risk notice | MISSING | incident response | Privacy | 72h | none | CLEAR | PRIV-001 | Incident response |
| P08 | Privatësia 06/L-082 | 35 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | DPIA për risk të lartë | EXTERNAL_GATE | AIP/jurist | Privacy | — | none | CONFIRM_AIP+CONFIRM_LAWYER | PRIV-001 | AIP/jurist |
| P09 | Privatësia 06/L-082 | 37, 40 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | DPO applicability dhe aktet/personat e brendshëm | EXTERNAL_GATE | AIP/jurist | Privacy | — | none | CONFIRM_AIP+CONFIRM_LAWYER | PRIV-001 | AIP/jurist |
| P10 | Privatësia 06/L-082 | 46–49 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Cross-border/cloud transfers | EXTERNAL_GATE | AIP/jurist | Privacy | — | none | CONFIRM_AIP+CONFIRM_LAWYER | PRIV-001 | AIP/jurist |
| K01 | KO159/24 (EXT-01) | LAPT 9(5–7) | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Të pavlefshme; no blanket periodic bank/notary feed | N/A_LINEAGE | — | Legal lineage | — | none | CLEAR | LINEAGE | Legal correction |
| K02 | KO159/24 | LAPT 14, 16, 26 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | U shpallën kushtetuese; JO të anuluara | N/A_LINEAGE | — | Legal lineage | — | none | CLEAR | LINEAGE | Legal correction |
| K03 | KO159/24 | LAPT 89(12) | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Claim **dismissed** — JO i pavlefshëm | N/A_LINEAGE | — | Legal lineage | — | none | CLEAR | LINEAGE | Legal correction |
| K04 | KO159/24 | LAPT 79(4), (6) | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | Të pavlefshme | N/A_LINEAGE | — | Legal lineage | — | none | CLEAR | LINEAGE | Legal correction |
| K05 | UA 01/2025 (EXT-02a/b/c) | PA09/2025 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | UA 01/2025 e shfuqizuar (Supreme PA09/2025 + korrigjimi); historical only | N/A_LINEAGE | — | Legal lineage | — | none | CLEAR | LINEAGE | Lineage |
| K06 | UA 02/2018 (EXT-02d) | UA 01/2026 Art.51 | N/A — GZ ActDetail (HTML/PDF locator; ligji jo i ngrirë me hash këtu) | UA 02/2018 e shfuqizuar nga UA 01/2026 | N/A_LINEAGE | — | Legal lineage | — | none | CLEAR | LINEAGE | Lineage |

## Disposition LAPT nen-për-nen (1–123, PDF-ja primare — kusht i ATK-011)

*Çdo nen i PDF-së primare (GZ 5/2024, 69 f.) ka disposition: `IN_SCOPE` → kontroll(e) atomik(e) në ledger; `SANCTION_CONTEXT` → i lidhur te kontrolli përkatës; `LINEAGE/N-A` → KO159/24; `CONTEXT`/`N/A` → arsyetim eksplicit. Ky pass mbulon PDF-në primare të LAPT — **pa pretendim "exhaustiv" për akte të tjera**.*

| Neni (faqe) | Titulli | Disposition | Arsyeja / Ledger IDs |
|---|---|---|---|
| 1 (f.1) | Qëllimi | N/A | qëllimi/fushëveprimi — pa detyrim produkti |
| 2 (f.1) | Fushëveprimi | N/A | qëllimi/fushëveprimi — pa detyrim produkti |
| 3 (f.1) | Përkufizimet | CONTEXT | përkufizimet (person, aktivitet ekonomik…) — bazë interpretimi, pa kontroll më vete |
| 4 (f.8) | Numri Fiskal dhe pajisja me Certifikatë Fiskale | N/A | e drejta e informimit të tatimpaguesit — procedurë ATK |
| 5 (f.8) | Çregjistrimi i Tatimpaguesit | N/A | mbrojtja e të dhënave tatimore nga ATK — detyrim i ATK-së (privatësia jonë = P-rows) |
| 6 (f.9) | Krijimi dhe mbajtja e regjistrave | IN_SCOPE | L01–L15, D01 (6.11–12), L13 (6.13) |
| 7 (f.11) | Operatori Ekonomik i autorizuar për furnizim, instalim dhe m | IN_SCOPE | L16 (aplikueshmëria OE) + sanksionet 107 |
| 8 (f.11) | Qasja në libra, regjistra, kompjuterë dhe pajisje të tjera p | IN_SCOPE | L17 (qasja; sanksioni 101(5)) |
| 9 (f.13) | Mbledhja e informatave ose provave | LINEAGE/N-A | PJESËRISHT INVALID (K01: 9(5–7)); pjesa e mbetur = kërkesa informatash (kontekst i L17/101(5)) |
| 10 (f.14) | Shkëmbimi i informacionit për qëllime ndërkombëtare | N/A | kërkesa informatash/vizita — procedura ATK ndaj tatimpaguesit (operacionale, jo sistem) |
| 11 (f.14) | Negociatat për Marrëveshje Ndërkombëtare | N/A | kërkesa informatash/vizita — procedura ATK ndaj tatimpaguesit (operacionale, jo sistem) |
| 12 (f.14) | Obligimet e përfaqësuesve juridik dhe menaxherëve të pasuris | N/A | kërkesa informatash/vizita — procedura ATK ndaj tatimpaguesit (operacionale, jo sistem) |
| 13 (f.15) | Deklaratat tatimore | N/A | kërkesa informatash/vizita — procedura ATK ndaj tatimpaguesit (operacionale, jo sistem) |
| 14 (f.16) | Vetë-vlerësimi | LINEAGE/N-A | UPHELD (K02) — procedurë ATK, N/A produkt |
| 15 (f.16) | Vlerësimi i ATK për tatimin | N/A | deklarimi/vlerësimi tatimor — procedura ATK (deklaratat mbulohen te BOOKS/EDI: L08/V-rows) |
| 16 (f.19) | Kufizimet kohore për vlerësimet | LINEAGE/N-A | UPHELD (K02) — procedurë ATK, N/A produkt |
| 17 (f.19) | Vlerësimet në rast rreziku | N/A | deklarimi/vlerësimi tatimor — procedura ATK (deklaratat mbulohen te BOOKS/EDI: L08/V-rows) |
| 18 (f.19) | Njoftim vlerësimi | N/A | deklarimi/vlerësimi tatimor — procedura ATK (deklaratat mbulohen te BOOKS/EDI: L08/V-rows) |
| 19 (f.20) | Anulimi i dokumenteve tatimore | N/A | deklarimi/vlerësimi tatimor — procedura ATK (deklaratat mbulohen te BOOKS/EDI: L08/V-rows) |
| 20 (f.20) | Detyrimi në rastet e veprës penale në fushën e tatimeve | N/A | deklarimi/vlerësimi tatimor — procedura ATK (deklaratat mbulohen te BOOKS/EDI: L08/V-rows) |
| 21 (f.20) | Detyrimi i personave përgjegjës | N/A | deklarimi/vlerësimi tatimor — procedura ATK (deklaratat mbulohen te BOOKS/EDI: L08/V-rows) |
| 22 (f.21) | Vlerësimi i detyrimit të personit të tretë | N/A | deklarimi/vlerësimi tatimor — procedura ATK (deklaratat mbulohen te BOOKS/EDI: L08/V-rows) |
| 23 (f.22) | Pagesat | N/A | deklarimi/vlerësimi tatimor — procedura ATK (deklaratat mbulohen te BOOKS/EDI: L08/V-rows) |
| 24 (f.22) | Interesi | N/A | deklarimi/vlerësimi tatimor — procedura ATK (deklaratat mbulohen te BOOKS/EDI: L08/V-rows) |
| 25 (f.23) | Rendi i pagesave | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 26 (f.23) | Kreditimet dhe rimbursimet | LINEAGE/N-A | UPHELD (K02) — procedurë ATK, N/A produkt |
| 27 (f.24) | Pagesat përmes bankave apo institucioneve financiare të lice | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 28 (f.24) | Tatimi i detyrueshëm dhe i pagueshëm | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 29 (f.24) | Barra tatimore | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 30 (f.27) | Masa për marrjen e pasurisë | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 31 (f.28) | Zbatimi me përmbarim i masës së marrjes | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 32 (f.28) | Shitja në ankand publik | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 33 (f.28) | Kufizimet mbi mbledhjen e detyrueshme | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 34 (f.29) | Mbledhja e tatimit nga ortakët dhe anëtarët e shoqatave të p | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 35 (f.30) | Urdhrat për raste rrezikimi | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 36 (f.30) | Embargo në importe dhe eksporte | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 37 (f.30) | Vendimi për publikim | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 38 (f.30) | Transferimi i aseteve | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 39 (f.31) | Borxhet tatimore të pambledhshme | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 40 (f.32) | Të ardhurat e kundërligjshme | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 41 (f.32) | Ri-karakterizimi i marrëveshjeve | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 42 (f.32) | Transaksionet në mes të personave të lidhur | N/A | vlerësime/ankesa/mbledhje/masa — procedura administrative ATK, pa kërkesë sistemi POS/SEF |
| 43 (f.33) | Shkëmbimi i transaksioneve dhe raportimi i informatave nga p | IN_SCOPE | L08 (43.3–4; 101(1)) + L14 (43.5; 102.3) |
| 44 (f.33) | Nën-deklarimi i të ardhurave dhe arkëtimet e devijuara | N/A | nën-deklarimi/metodat indirekte — procedurë vlerësimi e ATK-së |
| 45 (f.33) | Legjislacioni Tatimor | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 46 (f.34) | Administrata Tatimore e Kosovës | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 47 (f.34) | Organizimi i Administratës Tatimore të Kosovës | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 48 (f.34) | Përgjegjësitë dhe detyrat e Drejtorit të Përgjithshëm të Adm | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 49 (f.35) | Delegimi i kompetencave | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 50 (f.35) | Raportimi i Drejtorit të Përgjithshëm | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 51 (f.36) | Kriteret për përzgjedhjen dhe emërimin e Drejtorit të Përgji | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 52 (f.37) | Përfundimi i mandatit të Drejtorit të Përgjithshëm | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 53 (f.38) | Zëvendës Drejtorët e Përgjithshëm të Administratës Tatimore  | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 54 (f.38) | Kriteret për përzgjedhjen dhe emërimin e Zëvendës Drejtorit  | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 55 (f.38) | Përfundimi i mandatit të Zëvendës Drejtorit të Përgjithshëm | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 56 (f.39) | Hetimet Tatimore dhe Inteligjenca | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 57 (f.41) | Ankesat | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 58 (f.41) | Avokati i Tatimpaguesve | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 59 (f.42) | Standardet Profesionale | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 60 (f.43) | Zyra për Gjoba dhe Ndëshkime Administrative | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 61 (f.43) | Bordi i Ankesave | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 62 (f.44) | Juridiksioni i Bordit | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 63 (f.44) | Vendimi shpjegues publik | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 64 (f.44) | Vendimi shpjegues individual | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 65 (f.45) | Përfaqësuesit e Tatimpaguesit | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 66 (f.45) | Konfidencialiteti i informatave tatimore | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 67 (f.47) | Regjimi juridik i marrëdhënies së punës së punonjësit të ATK | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 68 (f.47) | Klasifikimi i vendeve të punës | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 69 (f.47) | Plani i Rekrutimit | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 70 (f.47) | Konkurrimi | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 71 (f.48) | Komisioni për zhvillimin e procedurave të punësimit | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 72 (f.48) | Kushtet për pranimin në marrëdhënie pune të zyrtarit tatimor | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 73 (f.49) | Kohëzgjatja e akteve të marrëdhënies së punës | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 74 (f.49) | Periudha provuese | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 75 (f.49) | Avancimi në karrierë i zyrtarit tatimor | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 76 (f.50) | Akti i brendshëm për pranimin dhe karrierën në ATK | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 77 (f.50) | Vlerësimi i rezultateve të punës | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 78 (f.50) | Emërimet e përkohshme | N/A | organizimi i ATK-së / legjislacioni / bordi / procedurat — institucionale |
| 79 (f.50) | Emërimi dhe mandati i pozitave drejtuese | LINEAGE/N-A | 79(4),(6) INVALID (K04); pjesa tjetër procedurë mbledhjeje, N/A produkt |
| 80 (f.51) | Transferimi | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 81 (f.52) | Riorganizimi | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 82 (f.52) | Orari i punës dhe pushimet | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 83 (f.52) | Pezullimi i marrëdhënies së punës | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 84 (f.53) | Përfundimi i marrëdhënies së punës | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 85 (f.53) | Parandalimi i konfliktit të Interesit | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 86 (f.54) | Kufizimi i shfrytëzimit të informatave | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 87 (f.54) | Përgjegjësia dhe shkeljet disiplinore | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 88 (f.54) | Masat disiplinore për shkeljen e detyrave të punës | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 89 (f.55) | Shqyrtimi i përgjegjësisë disiplinore dhe mbrojtja e të drej | LINEAGE/N-A | 89(12) DISMISSED — JO invalid (K03); procedurë, N/A produkt |
| 90 (f.56) | Shuarja e masave disiplinore | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 91 (f.56) | Akti i brendshëm për disiplinën dhe ankesat e punonjësve të  | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 92 (f.56) | Zbatimi i legjislacionit penal | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 93 (f.56) | Mosraportimi i veprave penale në fushën e tatimeve | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 94 (f.57) | Bashkëpunimi me institucionet për zbatimin e ligjit | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 95 (f.57) | Pjesëmarrja e ATK në procedurat gjyqësore | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 96 (f.57) | Përgjegjësitë e zyrtarëve tatimor | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 97 (f.57) | Sanksionet për mospërmbushje | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 98 (f.57) | Gjoba lidhur me Çertifikatën Fiskale dhe Çertifikatën e Regj | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 99 (f.58) | Gjobat lidhur me mosdeklarim | N/A | ankesat/ekzekutimi/parashkrimi — procedura ligjore, pa kontroll produkti |
| 100 (f.58) | Gjobat lidhur me nën deklarimin e tatimit dhe mbi deklarimin | SANCTION_CONTEXT | kontekst sanksioni (nën/mbi-deklarimi 15%/25%) — lidhet me saktësinë e deklarimeve (BOOKS) |
| 101 (f.58) | Sanksionet për mos dorëzim, mos krijim ose mos paraqitje të  | SANCTION_CONTEXT | sanksionet bazë të regjistrimeve/kuponit/qasjes/pagesave — të lidhura në L04–L08/L17/D01 |
| 102 (f.60) | Gjoba për mos ofrimin e informatave dhe provave | SANCTION_CONTEXT | 102.3 → L14; 102.1–2 kontekst i qasjes/informatave |
| 103 (f.60) | Gjobat për mos-mbajte të tatimit/kontributit në burim | SANCTION_CONTEXT | kontekst (mbajtja në burim — payroll F1) |
| 104 (f.60) | Gjobë për mosdorëzim të pasurisë që i nënshtrohet masës së m | SANCTION_CONTEXT | kontekst (konfiskimi) |
| 105 (f.60) | Gjoba për gabime nga përfaqësuesit e tatimpaguesve, këshillt | SANCTION_CONTEXT | kontekst (përfaqësuesit) |
| 106 (f.60) | Gjobat lidhur me mosinstalimin e PEF/SF | SANCTION_CONTEXT | kontekst sanksionesh të përgjithshme — referuar në crosswalk |
| 107 (f.61) | Gjobat për Operatorët Ekonomik të autorizuar për furnizim, i | SANCTION_CONTEXT | → L16 (OE) |
| 108 (f.61) | Gjobat lidhur me TVSh | N/A | procedurë administrative |
| 109 (f.62) | Gjoba për mallrat pa dokumentacion | SANCTION_CONTEXT | → sanksion i L04 (mallra pa dokumentacion) |
| 110 (f.62) | Zbritja e gjobave | SANCTION_CONTEXT | kontekst (zbritja e gjobave — stimulon vetëdeklarimin) |
| 111 (f.63) | Ankesat në Administratën Tatimore të Kosovës | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |
| 112 (f.64) | Ankesat në Bord | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |
| 113 (f.64) | Përmbajtja e ankesës në Bord | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |
| 114 (f.65) | Barra e provës | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |
| 115 (f.65) | Procedura e Bordit | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |
| 116 (f.66) | Marrëveshja tatimore | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |
| 117 (f.66) | Obligimi për të paguar detyrimin tatimor gjatë procedurës së | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |
| 118 (f.67) | Rregullat ndërkombëtare | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |
| 119 (f.67) | Dispozitat Kalimtare | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |
| 120 (f.67) | Plotësimi dhe ndryshimi i Ligji Nr. 08/L-196 për Pagat në Se | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |
| 121 (f.68) | Aktet Nënligjore | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |
| 122 (f.68) | Shfuqizimi | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |
| 123 (f.68) | Hyrja në fuqi | N/A | dispozita kalimtare/përfundimtare (shfuqizimet → LIN-01) |

## Interpretimi (i detyrueshëm)

- **`PROVEN_LOCAL` ≠ authority acceptance** — vetëm artefakt/test lokal i verifikuar pavarësisht.
- **`CONTRADICTS` = blocker i detyrueshëm PRE-Tier2** — **5 source-rows = 4 defekte unike**: T05→`T2-01` (money); U20→`T2-02`+`T2-03`; G04→`T2-02` (QR); T18→`T2-03` (NUIKF); **U27→`T2-08` (origjina 48h)**. **LIVE placeholder = G06/`T2-04` (MISSING, jo CONTRADICTS).**
- **`EXTERNAL_GATE` s'mbyllet nga AI** — kërkon autoritetin e emërtuar ose evidencë reale të test-serverit ATK.
- **`N/A_LINEAGE`** ruhet anti-regression: materiali i shfuqizuar/i pavlefshëm të mos ripërdoret si ligj aktiv.
- Fushat `—` = jo në evidence-in e audituar; plotësohen VETËM me verifikim të ri (kurrë me hamendje).

## Crosswalk-u LAPT → ATK-011 (mapping deliverable — mbyllet me merge; JO compliance)

- **Arts. 6.1–6.13** → L01–L07 + **L09–L13** (libra/software 6.1–2 · retention 6.3.1–4/7/8 · inventari 6.5–6 · kuponi 6.7–8 · cash 6.9–6.12 · **pagat 6.13**) — faqe reale nga PDF-i primar (f.9–11).
- **Arts. 6.11–6.12** → konteksti i kufizimeve cash → **D01 / Vendimi 19/2026** — me caveat-in: **akti i nënshkruar NUK u gjet publikisht** (EXT-04; vetëm njoftimi zyrtar).
- **Arts. 43.3–43.5** → L08 (librat elektronikë; **sanksioni 101(1) 150€/pasqyrë deri 3,000€**) + **L14 (43.5: njoftimi i punëtorit 1 ditë para; sanksioni 102.3 = 500€/punëtor)** — f.33/58–60.
- **Sanksionet:** Arts. **100, 101 (përfshirë 101(9)) dhe 106** = konteksti i sanksioneve për shkeljet e regjistrimit/kuponit/librave — konteksti i rrezikut ligjor, jo kontrolle produkti më vete.
- **UA Art.49(2)** — kufiri i shtrirjes së sanksioneve të UA-së: referon te ligji (LAPT), nuk krijon sanksione të reja më vete.
- **KO159/24 dispozitivi** (K01–K04): Art.9(5–7) **invalid**; Arts.14/16/26 **upheld**; Art.89(12) **dismissed — JO invalid**; Art.79(4) **dhe** (6) **invalid**.
- **Verifikimi mekanik i crosswalk-ut (kusht i "closes on merge"):** skripti lokal provon që çdo L-row ka **Ref + Faqe reale + Afat/sanksion (ku ekziston)** — plotësuar në v1.1; **shënim:** një pas i targetuar riverifikimi për nene shtesë të LAPT jashtë listës aktuale = i flamuruar si follow-up (jo pretendim exhaustiviteti).
- **Kufiri:** ky crosswalk mbyll **ATK-011 si mapping deliverable**; **NUK mbyll** implementimin (L01–L08 mbeten PARTIAL/MISSING) as pajtueshmërinë/aplikueshmërinë ligjore (gates e konfirmimit mbeten).

## Lidhjet

`ATK_Source_Register.md` v2.0 (burimet/CORE/EXT/LIN/CONF) · `ATK_Compliance_Gate.md` v2.0 (verdikti + T2-01..T2-10 + gates) · Tracker (`ATK-002`, `ATK-012`, `ATK-013`, `ACC-012`, `PRIV-001`) · `poc/poc5-sef/ANALIZA.md` (korrigjimi post-audit).

---
*Historia: v1.0 (2026-07-12) — 94 kontrolle (numëratori HISTORIK i v1.0) nga independent full-corpus audit. · v1.1 (2026-07-12, hardening po në këtë PR) — LAPT i plotësuar nga PDF-i primar (L09–L14; L07/D01 semantika e saktë; sanksionet 101(1)/101(2)/102.3 me faqe); T04 i ndarë (T22); G06 LIVE-harness; kolona `Closure key` + dallimi 4-rows/3-defekte; faqe reale per PDF-row ose arsye eksplicite; G03→CONFIRM_ATK; G01/T10 pointer fix → TOTAL 102, konfirmime 36. · v1.2 (2026-07-12, hardening final) — **Disposition LAPT nen-për-nen 1–123** (kusht i ATK-011); +L15 (6.4)/L16 (Art.7+107 OE)/L17 (Art.8+101(5) qasja); **U23 i ndarë → U27 CONTRADICTS (origjina 48h → T2-08)**; **G07/G08 = rreshta realë për T2-09/T2-10**; sanksionet 101(6)/101(9)/109/107 me faqe; **faqet e KT (44–49) dhe CORE-08 (6–8) nga renderimi**; T22 → CORE-03 (jo CORE-06) → **TOTAL 108 (60/18/17/5/2/6), 38 konfirmime, 40 closure keys; 5 CONTRADICTS-rreshta = 4 defekte unike**. Me aprovimin e Pronarit (PR review = aprovimi).*
