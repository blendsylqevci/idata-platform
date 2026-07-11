# ATK/SEF Requirements Ledger — 94 kontrollet e full-corpus (v1.0)

**Versioni:** 1.0 · **Cutoff i auditit:** 2026-07-12 · **Repo baseline:** `55126b1b48004e4fba4a5604c9717cd9456722d3` · **Pronari:** Group Owner (Human Gate)
**Statusi:** canonical — effective on merge. **Ky ledger është BURIMI EXHAUSTIV i statusit të pajtueshmërisë** (Gate-i mban verdiktin/gates; Register-i mban burimet).
**Baza:** *independent full-corpus audit, cutoff 2026-07-12* (zero-trust; Fable/Claude output i përjashtuar nga mbledhja e evidence-it — përdorur vetëm për reconciliation pas verdiktit).

**Rregulli i numërimit:** një rresht = **një kontroll i testueshëm/i mbyllshëm në mënyrë të pavarur**. Lista fushash të detyrueshme = një kontroll, përveç kur elementët kanë lifecycle/gate të ndryshëm.

## Certifikata e mbulimit

| Status | Count | Kuptimi |
|---|---:|---|
| `MISSING` | **49** | pa implementim/spec/kontroll operacional adekuat |
| `PARTIAL` | **17** | ka evidencë të pjesshme; kontrolli i plotë s'është i mbyllur |
| `EXTERNAL_GATE` | **16** | kërkon ATK/kontabilist/jurist/AIP ose evidence real serveri — **s'mbyllet nga AI** |
| `CONTRADICTS` | **4** | implementimi/testi aktual **kundërshton** burimin zyrtar — **blocker i detyrueshëm PRE-Tier2** |
| `PROVEN_LOCAL` | **2** | vetëm artefakt/test lokal i verifikuar — **≠ pranim i autoritetit** |
| `N/A_LINEAGE` | **6** | material ligjor i shfuqizuar/i pavlefshëm — ruhet **anti-regression** (të mos ripërdoret si ligj aktiv) |
| **TOTAL** | **94** | 88 aktive/potencialisht të aplikueshme + 6 lineage/N/A |

**32 kontrolle** kërkojnë konfirmim eksplicit `CONFIRM_ATK` / `CONFIRM_ACCOUNTANT` / `CONFIRM_LAWYER` / `CONFIRM_AIP`. **Asnjë status `PASS` nuk ekziston** — pajtueshmëria dallon evidencën lokale nga pranimi i autoritetit.

**94 vs 278 (PA krahasim mekanik):** 278 ishte **extraction granular historical baseline** (auditimi nen-për-nen i KT+UA, 09.07, me granularitet tjetër — çdo kërkesë e nxjerrë); **94 = kontrolle të mbyllshme në mënyrë të pavarur mbi FULL corpus** (LAPT+UA+KT+amendament+Kushtet+blloqe+GitHub/Swagger+TVSH+privatësi+lineage). Asnjëri s'është "gabim" — janë dy njësi matëse të ndryshme; crosswalk-u = ky ledger + baseline-i historik i Gate.

**Statuset e lejuara:** VETËM 6 të mësipërmet. **Konventa e qelizave:** `—` = fusha s'është në evidence-in e audituar (nuk sajohet — riverifikohet kur të duhet); "Repo evidence" = vetëm path relative tracked ose `none`.

## Ledger — 94 kontrollet

| ID | Source | Ref | Faqe | Control | Status | Actor/aplikueshmëria | Kategori | Afat/sanksion | Repo evidence | Confirmation | Closure owner/gate |
|---|---|---|---|---|---|---|---|---|---|---|---|
| L01 | LAPT (CORE-01) | 6.1–2 | — | Libra/regjistra me shkrim ose elektronikë; software sipas kritereve ATK | PARTIAL | tatimpaguesi (KABI/PLANET) | Books/ERP | — | `packages/accounting/src/posting.ts`; `docs/canonical/02-accounting/` | CLEAR | Product |
| L02 | LAPT | 6.3.1 | — | Ruajtje e dokumentacionit së paku 6 vjet | MISSING | tatimpaguesi | Retention | 6 vjet | none | CONFIRM_LAWYER | Legal/retention |
| L03 | LAPT | 6.3.7 | — | Regjistra të shërbimeve elektronike 10 vjet, nëse aplikohet | MISSING | tatimpaguesi (nëse aplikohet) | Retention | 10 vjet | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK/jurist |
| L04 | LAPT | 6.5–6 | — | Inventari vjetor deri më 10 janar + dokumentet e origjinës | MISSING | tatimpaguesi | Inventory | 10 janar | none | CLEAR | ERP |
| L05 | LAPT | 6.7–8 | — | Furnizimet regjistrohen dhe jepet kupon teknikisht i rregullt | PARTIAL | tatimpaguesi/POS | Fiscal | — | `packages/database/migrations/0003_accounting_posting.sql` | CLEAR | POS/SEF |
| L06 | LAPT | 6.9 | — | B2B mbi 300 EUR vetëm bankë/elektronik | MISSING | tatimpaguesi | Payments | 300 EUR | none | CLEAR | Payment gate |
| L07 | LAPT | 6.10 | — | Pagesat e pjesshme për transaksione mbi 100 EUR | MISSING | tatimpaguesi | Payments | 100 EUR | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK/jurist |
| L08 | LAPT | 43.3–4 | — | Librat blerje/shitje elektronikë mujorë ose tremujorë sipas statusit | PARTIAL | tatimpaguesi/kontabilisti | Books | mujor/tremujor | `docs/canonical/02-accounting/VAT_Book_Mapping_v0_2026-06-29.md` | CONFIRM_ACCOUNTANT | Kontabilist |
| D01 | Vendimi19-notice (EXT-03d/EXT-04) | 27.04/01.06.2026 | — | B2C mbi 2,000 EUR jo-cash. **Ndarje statusi:** product/control = MISSING (s'ka gate pagese në produkt); **source evidence = vetëm njoftimi zyrtar — AKTI ORIGJINAL I NËNSHKRUAR MUNGON publikisht** (EXT-04). MISSING ≠ thjesht "mungon kod" | MISSING | tatimpaguesi | Payments | 2,000 EUR | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK/jurist |
| U01 | UA 01/2026 (CORE-02) | 5.1–2 | — | Kupon për çdo furnizim te konsumatori final, përfshirë falas | MISSING | POS/SEF | Fiscal | — | none | CONFIRM_ATK+CONFIRM_ACCOUNTANT | ATK/kontabilist |
| U02 | UA 01/2026 | 5.3 | — | Faturë tatimore për biznes/person juridik, me përjashtimet | MISSING | ERP/faturimi | Fiscal/Books | — | none | CONFIRM_ATK+CONFIRM_ACCOUNTANT | ATK/kontabilist |
| U03 | UA 01/2026 | 6 | — | Koha e kuponit: pagesë, parapagim, furnizim me afat | MISSING | POS/accounting | Fiscal | — | none | CLEAR | POS/accounting |
| U04 | UA 01/2026 | 7 | — | Fushat e plota të kuponit | MISSING | print layer | Print | — | none | CLEAR | Print layer (spec: `Kupon_Print_Spec.md`) |
| U05 | UA 01/2026 | 31.1–3 | — | Subjektet retail/shërbime/cash që duhet të përdorin SEF | EXTERNAL_GATE | klasifikimi i subjekteve | Applicability | — | none | CONFIRM_ATK | ATK |
| U06 | UA 01/2026 | 31.4 | — | Njoftime ATK për lokacion/transfer/çregjistrim/ndërprerje | MISSING | operacionet | Operations | — | none | CLEAR | Operations |
| U07 | UA 01/2026 | 31.5–7 | — | Përjashtime B2B-only, mixed activity dhe vendime ministri | EXTERNAL_GATE | klasifikimi KABI/PLANET | Applicability | — | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK/jurist |
| U08 | UA 01/2026 | 32.1–2.5, 2.8 | — | Nënshkrim, certifikatë, HTTPS, QR dhe mesazhe ATK | PARTIAL | SEF | Transport/PKI | — | `packages/database/src/poc5-sign.ts`; `packages/database/src/poc5-transport.ts` | CLEAR | SEF |
| U09 | UA 01/2026 | 32.2.6 | — | Lidhje me printer | MISSING | POS/hardware | Print | — | none | CLEAR | PoC-4b/F7 |
| U10 | UA 01/2026 | 32.2.7 | — | Upgrade sipas nevojës | MISSING | release governance | Governance | — | none | CLEAR | Release governance |
| U11 | UA 01/2026 | 32.2.9 | — | Shumëgjuhësi sipas gjuhëve zyrtare | MISSING | UX | UX | — | none | CLEAR | UX |
| U12 | UA 01/2026 | 32.3 | — | Offline secure dhe dërgim automatik pas rikthimit | PARTIAL | SEF/offline | Offline | — | `packages/database/src/poc5-outbox.ts`; `packages/database/migrations/0006_poc5_fiscal.sql` | CLEAR | SEF/offline |
| U13 | UA 01/2026 | 34 | — | Certifikatë/çelës per instalim dhe identifikuesit e POS | EXTERNAL_GATE | PKI per instalim | PKI | — | none | CONFIRM_ATK | ATK/PKI (T2-05) |
| U14 | UA 01/2026 | 35.1–7 | — | Zhvilluesi/mirëmbajtësi certifikohet para ofertës/shitjes/instalimit | EXTERNAL_GATE | iCode si zhvillues | Certification | — | none | CONFIRM_ATK | ATK |
| U15 | UA 01/2026 | 35.8–12 | — | Kodi SEF + NUI zhvilluesi; njoftim dhe ndalim SEF të pacertifikuar | EXTERNAL_GATE | iCode/tatimpaguesi | Certification | — | none | CONFIRM_ATK | ATK |
| U16 | UA 01/2026 | 35.13 | — | Përgjegjësi e përbashkët tatimpagues–zhvillues | MISSING | governance | Governance | — | none | CLEAR | Governance |
| U17 | UA 01/2026 | 36 | — | Tërheqje/anulim certifikate dhe inspektim instalimesh | MISSING | runbook | Certification | — | none | CLEAR | Runbook |
| U18 | UA 01/2026 | 37 | — | Rrjet i enkriptuar/nënshkruar/direkt HTTPS/kompatibil | PARTIAL | transport | Transport | — | `packages/database/src/poc5-transport.ts` | CLEAR | Transport (T2-06) |
| U19 | UA 01/2026 | 39.1–2 | — | Letër/e-kupon/cashless me pëlqim sipas rastit | MISSING | POS/kanalet | Print/Channel | — | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK/jurist |
| U20 | UA 01/2026 | 39.3–5 | — | QR online/offline dhe numër unik automatik | **CONTRADICTS** | SEF | QR/NUIKF | — | `packages/database/src/poc5-sef.test.ts` (firmos PosCoupon; fixture NUIKF invalid) | CLEAR | QR/NUIKF (T2-02, T2-03) |
| U21 | UA 01/2026 | 40 | — | Njoftim i dukshëm për detyrimin e kuponit | MISSING | POS UX | UX | — | none | CLEAR | POS UX |
| U22 | UA 01/2026 | 43 | — | Kupon korrigjues me referencë te origjinali | PARTIAL | SEF | Fiscal lifecycle | — | `packages/database/src/poc5-sef.test.ts` (ReferenceNo) | CLEAR | Return/cancel |
| U23 | UA 01/2026 | 44.1–4 | — | Kupon offline i plotë dhe transmetim brenda 48h — **afati NIS nga momenti i ndërprerjes së lidhjes, JO per-kupon `created_at`** | PARTIAL | SEF/offline | Offline/Deadline | 48h nga outage start | `packages/database/migrations/0006_poc5_fiscal.sql` (deadline aktual jo-konform: `created_at + 48h`) | CLEAR | Deadline model (T2-08) |
| U24 | UA 01/2026 | 44.5–6 | — | Pas 48h: njoftim/prova/day-10 procedure | MISSING | operacione/runbook | Offline/Deadline | day-10 | none | CONFIRM_ATK | ATK/runbook |
| U25 | UA 01/2026 | 50.1 | — | Vendimi i ministrit për grupet/periudhat PEF→SEF | EXTERNAL_GATE | kohortat | Applicability | — | none | CONFIRM_ATK | **Vendimi nuk u gjet publikisht** (EXT-11/LIN-06) |
| U26 | UA 01/2026 | 50.2, 52 | — | Regjimi 60-ditor për subjektet pa PEF/SF dhe blloqet ndërkohë | EXTERNAL_GATE | kohortat/blloqet | Applicability | 60 ditë | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK/jurist |
| T01 | KT+amend (CORE-03/06) | 25 + amend N2 | — | Instalim/përdorim në ambient të përshtatshëm; riparim 48h | MISSING | product/support | Operations | riparim 48h | none | CLEAR | Product/support |
| T02 | KT+amend | 25.1–2 | — | Çdo shitje/kupon/raportim real-time | PARTIAL | SEF | Fiscal | — | `packages/database/src/poc5-transport.ts` (**vetëm mock**) | CLEAR | Mock only → production |
| T03 | KT+amend | hyrje f.44 | 44 | Verifikim saktësie, siguri minimale, UoM/kategori | MISSING | validation | Validation | — | none | CLEAR | Validation |
| T04 | KT+amend | 25.3 | — | Strukturë zyrtare, çelës privat dhe protokoll | **PROVEN_LOCAL** | SEF | Proto | — | `packages/database/proto/sef-models.proto` + drift-test në `packages/database/src/poc5-sef.test.ts` | CLEAR | Proto only — ≠ pranim serveri |
| T05 | KT+amend | 25.4 | — | **Unit price 4dp (×10000); item total 2dp (×100/cents)** | **CONTRADICTS** | SEF/money | Money | — | `packages/database/src/poc5-money.ts`; `packages/database/src/poc5-proto.ts` (enkodojnë item total ×10000 — **100× gabim**) | CLEAR | Money scale (T2-01) |
| T06 | KT+amend | 25.5–5.2 | — | ID unike, write-once/read-many, no-delete, histori | MISSING | DB integrity | Integrity | — | none | CLEAR | DB integrity |
| T07 | KT+amend | 25.5.3 | — | Audit login/sale/cancel/config me retention, encryption, integrity | MISSING | audit | Audit | — | none | CLEAR | Audit |
| T08 | KT+amend | 25.7–8 | — | Test/certifikim dhe recertifikim pas ndryshimeve | EXTERNAL_GATE | ATK proces | Certification | — | none | CONFIRM_ATK | ATK |
| T09 | KT+amend | 25.9–11 | — | Return/exchange/cancel me arsye dhe referencë | PARTIAL | SEF/POS | Fiscal lifecycle | — | `packages/database/src/poc5-sef.test.ts` (ReferenceNo) | CLEAR | Fiscal lifecycle |
| T10 | KT+amend | 25.12 | — | Tax groups A/C/D/E dhe pa TVSH negative | PARTIAL | money/tax | Money/Tax | — | `packages/database/src/poc5-money.ts` (kodet A/C/D/E) | CONFIRM_ACCOUNTANT | Accountant |
| T11 | KT+amend | 25.15 | — | Offline secure, auto-send dhe technical event | PARTIAL | SEF/offline | Offline | — | `packages/database/src/poc5-outbox.ts` (pa technical_event) | CLEAR | Offline |
| T12 | KT+amend | 25.16 | — | Futje e kuponëve të bllokut manual | MISSING | POS/blloqe | Blocks | — | none | CLEAR | Manual blocks |
| T13 | KT+amend | 25.18; 26.13 | — | Print/preview/copy/fushat/58–100mm/readability | MISSING | print layer | Print | — | none | CLEAR | Print |
| T14 | KT+amend | 26.4 i ri | — | Integritet në defekt, auto-send, eksport CSV/PDF me kërkesë | MISSING | SEF/DB | Integrity/Export | — | none | CLEAR | F1/F5 |
| T15 | KT+amend | 26.5 | — | Transmit para print; retry; 24/7; time sync | PARTIAL | SEF | Fiscal/Transport | — | `packages/database/src/poc5-outbox.ts` (**vetëm retry mock/manual**) | CLEAR | Retry only → production |
| T16 | KT+amend | 26.6–7 | — | Gjurmë dërguara/dështuara/gabime/modifikime dhe eksport | MISSING | audit | Audit | — | none | CLEAR | Audit |
| T17 | KT+amend | 26.8–9 | — | Mbrojtja e kodit/të dhënave dhe anti-tamper | MISSING | siguria | Security | — | none | CLEAR | Security |
| T18 | KT+amend | 26.10 | — | **NUIKF alfanumerik pa ndarës max 16, unik, automatik** | **CONTRADICTS** | SEF | NUIKF | — | `packages/database/src/poc5-sef.test.ts` (fixture me viza/17 karaktere = **invalid**; zero validator/generator/uniqueness runtime) | CLEAR | Fixture/validator (T2-03) |
| T19 | KT+amend | 26.11 | — | Integrim accounting/sales/suppliers | MISSING | product | Integration | — | none | CLEAR | Product |
| T20 | KT+amend | 26.12 | — | Rend offline, sinjal vizual dhe OFFLINE në kupon | PARTIAL | SEF/POS | Offline/Print | — | `packages/database/src/poc5-outbox.ts` (**vetëm rendi**; pa sinjal vizual/print) | CLEAR | Only ordering → full |
| T21 | KT+amend | 26.14–15 | — | Test-only para certifikimit; prod pas; kod SEF+NUI | EXTERNAL_GATE | iCode/ATK | Certification | — | none | CONFIRM_ATK | ATK |
| G01 | GitHub/Swagger (EXT-05/§4) | proto | — | Proto vendored me checksum | **PROVEN_LOCAL** | SEF | Proto | — | `packages/database/proto/sef-models.proto` (byte-sha256 = C#/Go @ pins) | CLEAR | C#/Go proto — ≠ pranim serveri |
| G02 | GitHub/Swagger | CouponId | — | CouponId unik në tërë biznesin dhe degët | MISSING | DB | Integrity | — | none | CLEAR | DB uniqueness |
| G03 | GitHub/Swagger | sign/send | — | ECDSA P-256, SHA256(base64proto), `/pos/coupon` | PARTIAL | SEF | Signature/Transport | — | `packages/database/src/poc5-sign.ts` (**pa provë serveri**; P1363 vs DER = CONF-02) | CLEAR | No server proof (T2-07) |
| G04 | GitHub/Swagger | QR | — | **QR nga `CitizenCoupon`** (base64(CitizenCoupon proto) + firma e tij) | **CONTRADICTS** | SEF | QR | — | `packages/database/src/poc5-sef.test.ts` (**firmos `PosCoupon` për QR**; CitizenCoupon vetëm round-trip) | CLEAR | QR object (T2-02) |
| G05 | GitHub/Swagger | CA | — | `/ca/verify`, CSR, `/ca/signcsr` | EXTERNAL_GATE | PKI/onboarding | PKI | — | none | CONFIRM_ATK | PKI/onboarding (T2-05) |
| C01 | Certification (CORE-04/07) | pre-test | — | Pre-test i suksesshëm para aplikimit ("Testim para Aplikimit") | EXTERNAL_GATE | iCode → ATK | Certification | — | none | CONFIRM_ATK | ATK |
| C02 | Certification (CORE-05/07) | 5.5 | — | Formular/ARBK/solution/integration/stack/manual/declaration | MISSING | iCode | Certification | — | none | CLEAR | Certification pack |
| C03 | Certification (CORE-07) | 6 | — | Commission technical test + 7-day noncritical cure | EXTERNAL_GATE | ATK Komisioni | Certification | 7 ditë pune cure | none | CONFIRM_ATK | ATK |
| C04 | Certification (CORE-07) | 7.1 | — | Vendim brenda 15 ditëve pune | EXTERNAL_GATE | ATK Komisioni | Certification | 15 ditë pune | none | CONFIRM_ATK | ATK |
| C05 | Certification (CORE-07) | 7.4, 7.6 | — | Prior notice për ndryshime; reapply pas 30 ditësh | MISSING | recert governance | Certification | 30 ditë | none | CLEAR | Recert governance (Gate §3) |
| C06 | Certification (CORE-07) | 8 | — | Suspension/withdrawal/cancellation governance | MISSING | runbook | Certification | — | none | CLEAR | Runbook |
| B01 | Blloqet 02/2026 (CORE-08) | 5–8 | — | Bllok i prefiskalizuar, restore 48h, futje brenda 5 ditësh | MISSING | POS/operacione | Blocks | 48h + 5 ditë | none | CLEAR | Manual blocks |
| B02 | Blloqet 02/2026 | EDI code | — | Kod unik EDI; maksimum 50 seriale | MISSING | EDI/blloqe | Blocks | max 50 seriale | none | CLEAR | EDI |
| B03 | Blloqet 02/2026 | continuity | — | Vazhdimësi SEF→bllok→SEF, dy kopje, fusha manuale | MISSING | POS/sekuenca | Blocks | — | none | CLEAR | Sequence |
| V01 | TVSH 05/L-037 (EXT-06/07) | 44 | — | Invoice/prepayment/deadline/self-billing | MISSING | faturimi | VAT/Books | — | none | CONFIRM_ACCOUNTANT | Accountant |
| V02 | TVSH 05/L-037 | 45–46 | — | Fushat B2B dhe ndarja multi-rate | MISSING | faturimi | VAT/Books | — | none | CONFIRM_ACCOUNTANT | Accountant |
| V03 | TVSH 05/L-037 | 47 | — | Debit/credit note me referencë/arsye/shuma | MISSING | accounting | VAT/Books | — | none | CLEAR | Accounting |
| V04 | TVSH 05/L-037 | 48 | — | Bad-debt invoice | MISSING | accounting | VAT/Books | — | none | CONFIRM_ACCOUNTANT | Accountant |
| V05 | TVSH 05/L-037 | 49 | — | Simplified invoice/fiscal receipt/pragjet | MISSING | faturimi/POS | VAT/Fiscal | — | none | CONFIRM_ATK+CONFIRM_ACCOUNTANT | ATK/accountant |
| V06 | TVSH 05/L-037 | 50 | — | E-invoice consent dhe autenticitet/integritet | MISSING | faturimi | VAT/E-invoice | — | none | CONFIRM_LAWYER+CONFIRM_ACCOUNTANT | Jurist/accountant |
| V07 | TVSH 05/L-037 | 56–57 | — | Retention 6y/20y, electronic storage, serial order | MISSING | retention | Retention | 6/20 vjet | none | CLEAR | Retention |
| V08 | TVSH 05/L-037 | 56.3 | — | Njoftim për vendin e ruajtjes dhe qasje ATK | MISSING | operacionet | Operations | — | none | CLEAR | Operations |
| P01 | Privatësia 06/L-082 (EXT-08) | 4–5 | — | Parimet dhe baza ligjore e përpunimit | MISSING | platforma | Privacy | — | none | CONFIRM_LAWYER+CONFIRM_AIP | Jurist/AIP |
| P02 | Privatësia 06/L-082 | 12–21 | — | Notice dhe data-subject rights workflow | MISSING | platforma | Privacy | — | none | CLEAR | Privacy |
| P03 | Privatësia 06/L-082 | 23–24 | — | Accountability dhe privacy by design/default | PARTIAL | arkitektura | Privacy | — | `docs/canonical/06-architecture/Reliability_Gate.md` | CLEAR | Architecture |
| P04 | Privatësia 06/L-082 | 27 | — | Controller/processor/subprocessor agreement | MISSING | legal | Privacy | — | none | CLEAR | Legal (PRIV-001) |
| P05 | Privatësia 06/L-082 | 29 | — | ROPA (regjistri i aktiviteteve të përpunimit) | MISSING | privacy | Privacy | — | none | CLEAR | Privacy (PRIV-001) |
| P06 | Privatësia 06/L-082 | 31 | — | Risk security, CIA, restore dhe testing | PARTIAL | reliability | Privacy/Security | — | `docs/canonical/06-architecture/Reliability_Gate.md` | CLEAR | Reliability |
| P07 | Privatësia 06/L-082 | 33–34 | — | Breach notification 72h dhe high-risk notice | MISSING | incident response | Privacy | 72h | none | CLEAR | Incident response |
| P08 | Privatësia 06/L-082 | 35 | — | DPIA për risk të lartë | EXTERNAL_GATE | AIP/jurist | Privacy | — | none | CONFIRM_AIP+CONFIRM_LAWYER | AIP/jurist |
| P09 | Privatësia 06/L-082 | 37, 40 | — | DPO applicability dhe aktet/personat e brendshëm | EXTERNAL_GATE | AIP/jurist | Privacy | — | none | CONFIRM_AIP+CONFIRM_LAWYER | AIP/jurist |
| P10 | Privatësia 06/L-082 | 46–49 | — | Cross-border/cloud transfers | EXTERNAL_GATE | AIP/jurist | Privacy | — | none | CONFIRM_AIP+CONFIRM_LAWYER | AIP/jurist |
| K01 | KO159/24 (EXT-01) | LAPT 9(5–7) | — | Të pavlefshme; no blanket periodic bank/notary feed | N/A_LINEAGE | — | Legal lineage | — | none | CLEAR | Legal correction |
| K02 | KO159/24 | LAPT 14, 16, 26 | — | U shpallën kushtetuese; JO të anuluara | N/A_LINEAGE | — | Legal lineage | — | none | CLEAR | Legal correction |
| K03 | KO159/24 | LAPT 89(12) | — | Claim **dismissed** — JO i pavlefshëm | N/A_LINEAGE | — | Legal lineage | — | none | CLEAR | Legal correction |
| K04 | KO159/24 | LAPT 79(4), (6) | — | Të pavlefshme | N/A_LINEAGE | — | Legal lineage | — | none | CLEAR | Legal correction |
| K05 | UA 01/2025 (EXT-02a/b/c) | PA09/2025 | — | UA 01/2025 e shfuqizuar (Supreme PA09/2025 + korrigjimi); historical only | N/A_LINEAGE | — | Legal lineage | — | none | CLEAR | Lineage |
| K06 | UA 02/2018 (EXT-02d) | UA 01/2026 Art.51 | — | UA 02/2018 e shfuqizuar nga UA 01/2026 | N/A_LINEAGE | — | Legal lineage | — | none | CLEAR | Lineage |

## Interpretimi (i detyrueshëm)

- **`PROVEN_LOCAL` ≠ authority acceptance** — vetëm artefakt/test lokal i verifikuar pavarësisht.
- **`CONTRADICTS` = blocker i detyrueshëm PRE-Tier2** (4: T05 money, U20+G04 QR, T18 NUIKF — plus Tier2 placeholder si blocker teknik në Gate).
- **`EXTERNAL_GATE` s'mbyllet nga AI** — kërkon autoritetin e emërtuar ose evidencë reale të test-serverit ATK.
- **`N/A_LINEAGE`** ruhet anti-regression: materiali i shfuqizuar/i pavlefshëm të mos ripërdoret si ligj aktiv.
- Fushat `—` = jo në evidence-in e audituar; plotësohen VETËM me verifikim të ri (kurrë me hamendje).

## Crosswalk-u LAPT → ATK-011 (mapping deliverable — mbyllet me merge; JO compliance)

- **Arts. 6.1–6.10** → L01–L07 (libra/retention/inventar/kuponi/kufizimet cash 300/100 EUR).
- **Arts. 6.11–6.12** → konteksti i kufizimeve cash → **D01 / Vendimi 19/2026** — me caveat-in: **akti i nënshkruar NUK u gjet publikisht** (EXT-04; vetëm njoftimi zyrtar).
- **Arts. 43.3–43.4** → L08 (librat elektronikë mujorë/tremujorë sipas statusit).
- **Sanksionet:** Arts. **100, 101 (përfshirë 101(9)) dhe 106** = konteksti i sanksioneve për shkeljet e regjistrimit/kuponit/librave — konteksti i rrezikut ligjor, jo kontrolle produkti më vete.
- **UA Art.49(2)** — kufiri i shtrirjes së sanksioneve të UA-së: referon te ligji (LAPT), nuk krijon sanksione të reja më vete.
- **KO159/24 dispozitivi** (K01–K04): Art.9(5–7) **invalid**; Arts.14/16/26 **upheld**; Art.89(12) **dismissed — JO invalid**; Art.79(4) **dhe** (6) **invalid**.
- **Kufiri:** ky crosswalk mbyll **ATK-011 si mapping deliverable**; **NUK mbyll** implementimin (L01–L08 mbeten PARTIAL/MISSING) as pajtueshmërinë/aplikueshmërinë ligjore (gates e konfirmimit mbeten).

## Lidhjet

`ATK_Source_Register.md` v2.0 (burimet/CORE/EXT/LIN/CONF) · `ATK_Compliance_Gate.md` v2.0 (verdikti + T2-01..T2-10 + gates) · Tracker (`ATK-002`, `ATK-012`, `ATK-013`, `ACC-012`, `PRIV-001`) · `poc/poc5-sef/ANALIZA.md` (korrigjimi post-audit).

---
*Historia: v1.0 (2026-07-12, ky PR) — krijuar nga independent full-corpus audit (cutoff 2026-07-12); 94 kontrolle me reconciliation mekanik 49+17+16+4+2+6=94; 32 me gate konfirmimi. Me aprovimin e Pronarit (PR review = aprovimi).*
