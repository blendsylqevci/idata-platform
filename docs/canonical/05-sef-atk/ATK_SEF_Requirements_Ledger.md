# ATK/SEF Requirements Ledger — kontrollet e full-corpus (v1.1)

**Versioni:** 1.1 · **Cutoff i auditit:** 2026-07-12 · **Përditësuar:** 2026-07-12 (hardening: LAPT i plotësuar nga PDF-i primar + semantika e ndarë source-rows ↔ closure keys) · **Repo baseline:** `55126b1b48004e4fba4a5604c9717cd9456722d3` · **Pronari:** Group Owner (Human Gate)
**Statusi:** canonical — effective on merge. **Ky ledger është BURIMI EXHAUSTIV i statusit të pajtueshmërisë** (Gate-i mban verdiktin/gates; Register-i mban burimet).
**Baza:** *independent full-corpus audit, cutoff 2026-07-12* (zero-trust; Fable/Claude output i përjashtuar nga mbledhja e evidence-it — përdorur vetëm për reconciliation pas verdiktit).

**Rregulli i numërimit:** një rresht = **një kontroll i testueshëm/i mbyllshëm në mënyrë të pavarur**. Lista fushash të detyrueshme = një kontroll, përveç kur elementët kanë lifecycle/gate të ndryshëm.

## Certifikata e mbulimit (RILLOGARITUR v1.1 — dy boshte: source-rows ↔ closure keys)

**(a) Source-requirement rows: 102** (aktive/potencialisht të aplikueshme 96 + 6 lineage) · **(b) Unique closure/product keys: 37** (T2-01..T2-10 + gates ATK-012/013, ACC-012, PRIV-001, ATK-008 + area-keys — kolona `Closure key`).

| Status (source-rows) | Count |
|---|---:|
| MISSING | 57 |
| PARTIAL | 17 |
| EXTERNAL_GATE | 16 |
| CONTRADICTS | 4 |
| PROVEN_LOCAL | 2 |
| N/A_LINEAGE | 6 |
| **TOTAL** | **102** |

**36 kontrolle** kërkojnë `CONFIRM_ATK/ACCOUNTANT/LAWYER/AIP`. **Asnjë `PASS`.**
**Leximi i saktë i CONTRADICTS:** **4 source-rows = 3 defekte unike** (money `T2-01` · QR `T2-02` · NUIKF `T2-03`; U20 mbart T2-02+T2-03). **LIVE placeholder = MISSING/NOT_IMPLEMENTED (G06 → `T2-04`), JO CONTRADICTS.**
**Konventa `—`:** lejohet vetëm me arsye eksplicite (p.sh. "KT e skanuar pa text layer"); web/GitHub = `N/A — non-paginated` + locator.

**102 vs 94 vs 278:** 94 (v1.0) u zgjerua në 102 pas plotësimit të LAPT-it nga PDF-i primar (6.3.2/3/4/8, 6.13, 43.5, T22, G06) — numrat NUK ruhen për të përputhur raporte të vjetra; 278 mbetet extraction baseline historik me granularitet tjetër.

## Ledger — kontrollet (102)

| ID | Source | Ref | Faqe | Control | Status | Actor/aplikueshmëria | Kategori | Afat/sanksion | Repo evidence | Confirmation | Closure key | Closure owner/gate |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| L01 | LAPT (CORE-01) | 6.1–2 | 9–10 | Libra/regjistra me shkrim ose elektronikë; software sipas kritereve ATK | PARTIAL | tatimpaguesi (KABI/PLANET) | Books/ERP | — · sanksioni: 101(2) 150–2,000€ sipas qarkullimit (f.58–59) | `packages/accounting/src/posting.ts`; `docs/canonical/02-accounting/` | CLEAR | BOOKS-F2 | Product |
| L02 | LAPT | 6.3.1 | 10 | Ruajtje e dokumentacionit së paku 6 vjet | MISSING | tatimpaguesi | Retention | 6 vjet · 101(2) | none | CONFIRM_LAWYER | RETENTION-F1 | Legal/retention |
| L03 | LAPT | 6.3.7 | 10 | Regjistra të shërbimeve elektronike 10 vjet, nëse aplikohet | MISSING | tatimpaguesi (nëse aplikohet) | Retention | 10 vjet · 101(2) | none | CONFIRM_ATK+CONFIRM_LAWYER | RETENTION-F1 | ATK/jurist |
| L09 | LAPT (CORE-01) | 6.3.2 | 10 | Regjistrat e pasurive të zhvlerësueshme >6 vjet — ruhen sa jetëgjatësia e zhvlerësimit **+1 vit** | MISSING | tatimpaguesi | Retention | jetëgjatësia+1 · 101(2) | none | CONFIRM_LAWYER | RETENTION-F1 | Legal/retention |
| L10 | LAPT (CORE-01) | 6.3.3 | 10 | Regjistrat e pasurive për fitime kapitale — deri në shitje/ekspozim **+6 vjet** | MISSING | tatimpaguesi | Retention | shitja+6 · 101(2) | none | CONFIRM_LAWYER | RETENTION-F1 | Legal/retention |
| L11 | LAPT (CORE-01) | 6.3.4 | 10 | Regjistrat e kontratave afatgjata të ndërtimit — **projekti +6 vjet** | MISSING | tatimpaguesi (nëse aplikohet) | Retention | projekti+6 · 101(2) | none | CONFIRM_LAWYER | RETENTION-F1 | Legal/retention |
| L12 | LAPT (CORE-01) | 6.3.8 | 10 | Regjistrat e transaksioneve të arit të investueshëm — **≥10 vjet** (aplikueshmëria për KABI/PLANET = e dyshimtë) | MISSING | tatimpaguesi (nëse aplikohet) | Retention | 10 vjet · 101(2) | none | CONFIRM_LAWYER | RETENTION-F1 | Legal/retention |
| L04 | LAPT | 6.5–6 | 10 | Inventari vjetor deri më 10 janar + dokumentet e origjinës | MISSING | tatimpaguesi | Inventory | 10 janar · 101(2) | none | CLEAR | RETENTION-F1 | ERP |
| L05 | LAPT | 6.7–8 | 10 | Furnizimet regjistrohen dhe jepet kupon teknikisht i rregullt | PARTIAL | tatimpaguesi/POS | Fiscal | — · 101(2) (përfshi numrin fiskal/NUI në kupon/fatura) | `packages/database/migrations/0003_accounting_posting.sql` | CLEAR | FISCAL-F5 | POS/SEF |
| L06 | LAPT | 6.9 | 10–11 | B2B mbi 300 EUR vetëm bankë/elektronik | MISSING | tatimpaguesi | Payments | 300 € · — | none | CLEAR | PAY-GATE-F2/F8 | Payment gate |
| L07 | LAPT | 6.10 | 11 | **>100 € pjesërisht me para + pjesa me kompensim → pjesa CASH detyrimisht bankë/elektronik** (zbatim i 6.9) | MISSING | tatimpaguesi | Payments | 100 € · — | none | CLEAR | PAY-GATE-F2/F8 | ATK/jurist |
| L08 | LAPT | 43.3–4 | 33 | Librat blerje/shitje elektronikë mujorë ose tremujorë sipas statusit | PARTIAL | tatimpaguesi/kontabilisti | Books | mujor (TVSH) / tremujor · **101(1): 150€/pasqyrë deri 3,000€** (f.58–59) | `docs/canonical/02-accounting/VAT_Book_Mapping_v0_2026-06-29.md` | CONFIRM_ACCOUNTANT | BOOKS-EDI-F2 | Kontabilist |
| L13 | LAPT (CORE-01) | 6.13 | 11 | **Çdo pagë e të punësuarit paguhet përmes institucioni financiar të licencuar** | MISSING | payroll (F1) | Payments/Payroll | — · — | none | CLEAR | PAYROLL-F1 | Payroll F1 |
| L14 | LAPT (CORE-01) | 43.5 | 33 | **Njoftimi i ATK-së për kontratën e punës NJË DITË PARA fillimit të punës** (forma e ATK-së) | MISSING | operacionet/HR | Operations | 1 ditë para · **102.3: 500€/punëtor i padeklaruar** (f.60) | none | CLEAR | OPS-HR-F1 | Operations/HR |
| D01 | Vendimi19-notice (EXT-03d/EXT-04) | LAPT 6.11–6.12 + Vendimi19-notice (EXT-03d/EXT-04) | 11 (LAPT) + njoftimi | B2C: transaksioni me person fizik jo-afarist mbi vlerën e vendimit të ministrit (19/2026: 2,000 € nga 01.06.2026) → bankë/elektronik; **6.12: edhe pjesa CASH e pagesës së pjesshme**. **Ndarje statusi:** product/control = MISSING (s'ka gate pagese në produkt); **source evidence = vetëm njoftimi zyrtar — AKTI ORIGJINAL I NËNSHKRUAR MUNGON publikisht** (EXT-04). MISSING ≠ thjesht "mungon kod" | MISSING | tatimpaguesi | Payments | 2,000 € (njoftimi) · — | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK-013 | ATK/jurist |
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
| U23 | UA 01/2026 | 44.1–4 | 37 | Kupon offline i plotë dhe transmetim brenda 48h — **afati NIS nga momenti i ndërprerjes së lidhjes, JO per-kupon `created_at`** | PARTIAL | SEF/offline | Offline/Deadline | 48h nga outage start | `packages/database/migrations/0006_poc5_fiscal.sql` (deadline aktual jo-konform: `created_at + 48h`) | CLEAR | T2-08 | Deadline model (T2-08) |
| U24 | UA 01/2026 | 44.5–6 | 37 | Pas 48h: njoftim/prova/day-10 procedure | MISSING | operacione/runbook | Offline/Deadline | day-10 | none | CONFIRM_ATK | OFFLINE-F5 | ATK/runbook |
| U25 | UA 01/2026 | 50.1 | 39 | Vendimi i ministrit për grupet/periudhat PEF→SEF | EXTERNAL_GATE | kohortat | Applicability | — | none | CONFIRM_ATK | ATK-013 | **Vendimi nuk u gjet publikisht** (EXT-11/LIN-06) |
| U26 | UA 01/2026 | 50.2, 52 | 39 | Regjimi 60-ditor për subjektet pa PEF/SF dhe blloqet ndërkohë | EXTERNAL_GATE | kohortat/blloqet | Applicability | 60 ditë | none | CONFIRM_ATK+CONFIRM_LAWYER | ATK-013 | ATK/jurist |
| T01 | KT+amend (CORE-03/06) | 25 + amend N2 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Instalim/përdorim në ambient të përshtatshëm; riparim 48h | MISSING | product/support | Operations | riparim 48h | none | CLEAR | OPS-F7 | Product/support |
| T02 | KT+amend | 25.1–2 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Çdo shitje/kupon/raportim real-time | PARTIAL | SEF | Fiscal | — | `packages/database/src/poc5-transport.ts` (**vetëm mock**) | CLEAR | SEF-F5 | Mock only → production |
| T03 | KT+amend | hyrje f.44 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Verifikim saktësie, siguri minimale, UoM/kategori | MISSING | validation | Validation | — | none | CLEAR | VALID-F5 | Validation |
| T04 | KT+amend | 25.3 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Struktura zyrtare e proto-s (schema drift-lock kundër C#/Go @ pins) | **PROVEN_LOCAL** | SEF | Proto | — | `packages/database/proto/sef-models.proto` + drift-test në `packages/database/src/poc5-sef.test.ts` | CLEAR | PROTO-LOCK | Proto drift-lock — ≠ pranim serveri |
| T05 | KT+amend | 25.4 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | **Unit price 4dp (×10000); item total 2dp (×100/cents)** | **CONTRADICTS** | SEF/money | Money | — | `packages/database/src/poc5-money.ts`; `packages/database/src/poc5-proto.ts` (enkodojnë item total ×10000 — **100× gabim**) | CLEAR | T2-01 | Money scale (T2-01) |
| T06 | KT+amend | 25.5–5.2 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | ID unike, write-once/read-many, no-delete, histori | MISSING | DB integrity | Integrity | — | none | CLEAR | INTEGRITY-F1 | DB integrity |
| T07 | KT+amend | 25.5.3 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Audit login/sale/cancel/config me retention, encryption, integrity | MISSING | audit | Audit | — | none | CLEAR | AUDIT-F1 | Audit |
| T08 | KT+amend | 25.7–8 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Test/certifikim dhe recertifikim pas ndryshimeve | EXTERNAL_GATE | ATK proces | Certification | — | none | CONFIRM_ATK | CERT-F9 | ATK |
| T09 | KT+amend | 25.9–11 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Return/exchange/cancel me arsye dhe referencë | PARTIAL | SEF/POS | Fiscal lifecycle | — | `packages/database/src/poc5-sef.test.ts` (ReferenceNo) | CLEAR | FISCAL-F5 | Fiscal lifecycle |
| T10 | KT+amend | 25.12 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Tax groups A/C/D/E dhe pa TVSH negative | PARTIAL | money/tax | Money/Tax | — | `packages/database/src/poc5-proto.ts` (kodet A/C/D/E në mapim) | CONFIRM_ACCOUNTANT | TAX-F2 | Accountant |
| T11 | KT+amend | 25.15; 26.4 i ri (auto-send) | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Offline secure, auto-send dhe technical event | PARTIAL | SEF/offline | Offline | — | `packages/database/src/poc5-outbox.ts` (pa technical_event) | CLEAR | OFFLINE-F5 | Offline |
| T12 | KT+amend | 25.16 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Futje e kuponëve të bllokut manual | MISSING | POS/blloqe | Blocks | — | none | CLEAR | BLOCKS-F5 | Manual blocks |
| T13 | KT+amend | 25.18; 26.13 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Print/preview/copy/fushat/58–100mm/readability | MISSING | print layer | Print | — | none | CLEAR | PRINT-F7 | Print |
| T14 | KT+amend | 26.4 i ri | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Integritet në defekt, auto-send, eksport CSV/PDF me kërkesë | MISSING | SEF/DB | Integrity/Export | — | none | CLEAR | INTEGRITY-F1 | F1/F5 |
| T15 | KT+amend | 26.5 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Transmit para print; retry; 24/7; time sync | PARTIAL | SEF | Fiscal/Transport | — | `packages/database/src/poc5-outbox.ts` (**vetëm retry mock/manual**) | CLEAR | SEF-F5 | Retry only → production |
| T16 | KT+amend | 26.6–7 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Gjurmë dërguara/dështuara/gabime/modifikime dhe eksport | MISSING | audit | Audit | — | none | CLEAR | AUDIT-F1 | Audit |
| T17 | KT+amend | 26.8–9 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Mbrojtja e kodit/të dhënave dhe anti-tamper | MISSING | siguria | Security | — | none | CLEAR | SEC-F5 | Security |
| T18 | KT+amend | 26.10 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | **NUIKF alfanumerik pa ndarës max 16, unik, automatik** | **CONTRADICTS** | SEF | NUIKF | — | `packages/database/src/poc5-sef.test.ts` (fixture me viza/17 karaktere = **invalid**; zero validator/generator/uniqueness runtime) | CLEAR | T2-03 | Fixture/validator (T2-03) |
| T19 | KT+amend | 26.11 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Integrim accounting/sales/suppliers | MISSING | product | Integration | — | none | CLEAR | ERP-F2 | Product |
| T20 | KT+amend | 26.12 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Rend offline, sinjal vizual dhe OFFLINE në kupon | PARTIAL | SEF/POS | Offline/Print | — | `packages/database/src/poc5-outbox.ts` (**vetëm rendi**; pa sinjal vizual/print) | CLEAR | OFFLINE-F5 | Only ordering → full |
| T21 | KT+amend | 26.14–15 | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | Test-only para certifikimit; prod pas; kod SEF+NUI | EXTERNAL_GATE | iCode/ATK | Certification | — | none | CONFIRM_ATK | CERT-F9 | ATK |
| T22 | KT+amend (CORE-03/06) | 25.3 (pjesa e çelësit/protokollit) | — (KT/amendamenti = PDF i skanuar pa text layer; paginimi → pas i targetuar) | **Çelësi privat production + protokolli i plotë nënshkrim→dërgim** (i ndarë nga schema drift-lock i T04) | MISSING | SEF/PKI | Signature/PKI | — · — | none | CLEAR | T2-05+T2-07 | T2-05/T2-07 |
| G01 | GitHub C#/Go @ pins (Register §4) | proto | N/A — non-paginated (web/GitHub locator) | Proto vendored me checksum | **PROVEN_LOCAL** | SEF | Proto | — | `packages/database/proto/sef-models.proto` (byte-sha256 = C#/Go @ pins) | CLEAR | PROTO-LOCK | C#/Go proto — ≠ pranim serveri |
| G02 | GitHub/Swagger | CouponId | N/A — non-paginated (web/GitHub locator) | CouponId unik në tërë biznesin dhe degët | MISSING | DB | Integrity | — | none | CLEAR | INTEGRITY-F1 | DB uniqueness |
| G03 | GitHub/Swagger | sign/send | N/A — non-paginated (web/GitHub locator) | ECDSA P-256, SHA256(base64proto), `/pos/coupon` | PARTIAL | SEF | Signature/Transport | — | `packages/database/src/poc5-sign.ts` (**pa provë serveri**; P1363 vs DER = CONF-02) | CONFIRM_ATK | T2-07 | Server authority (T2-07) |
| G04 | GitHub/Swagger | QR | N/A — non-paginated (web/GitHub locator) | **QR nga `CitizenCoupon`** (base64(CitizenCoupon proto) + firma e tij) | **CONTRADICTS** | SEF | QR | — | `packages/database/src/poc5-sef.test.ts` (**firmos `PosCoupon` për QR**; CitizenCoupon vetëm round-trip) | CLEAR | T2-02 | QR object (T2-02) |
| G05 | GitHub/Swagger | CA | N/A — non-paginated (web/GitHub locator) | `/ca/verify`, CSR, `/ca/signcsr` | EXTERNAL_GATE | PKI/onboarding | PKI | — | none | CONFIRM_ATK | T2-05+ATK-008 | PKI/onboarding (T2-05) |
| G06 | GitHub/Swagger (Register §4) | LIVE harness | N/A — non-paginated (web/GitHub locator) | **Executable LIVE test-host-only harness** — trupi aktual i testit LIVE = placeholder që gjithmonë hedh | MISSING | SEF | Tier2 harness | — · — | `packages/database/src/poc5-sef.test.ts` (placeholder i skip-uar) | CLEAR | T2-04 | T2-04 |
| C01 | Certification (CORE-04/07) | pre-test | 4 | Pre-test i suksesshëm para aplikimit ("Testim para Aplikimit") | EXTERNAL_GATE | iCode → ATK | Certification | — | none | CONFIRM_ATK | CERT-F9 | ATK |
| C02 | Certification (CORE-05/07) | 5.5 | 4–5 | Formular/ARBK/solution/integration/stack/manual/declaration | MISSING | iCode | Certification | — | none | CLEAR | CERT-F9 | Certification pack |
| C03 | Certification (CORE-07) | 6 | 6 | Commission technical test + 7-day noncritical cure | EXTERNAL_GATE | ATK Komisioni | Certification | 7 ditë pune cure | none | CONFIRM_ATK | CERT-F9 | ATK |
| C04 | Certification (CORE-07) | 7.1 | 6 | Vendim brenda 15 ditëve pune | EXTERNAL_GATE | ATK Komisioni | Certification | 15 ditë pune | none | CONFIRM_ATK | CERT-F9 | ATK |
| C05 | Certification (CORE-07) | 7.4, 7.6 | 6 | Prior notice për ndryshime; reapply pas 30 ditësh | MISSING | recert governance | Certification | 30 ditë | none | CLEAR | CERT-F9 | Recert governance (Gate §3) |
| C06 | Certification (CORE-07) | 8 | 7 | Suspension/withdrawal/cancellation governance | MISSING | runbook | Certification | — | none | CLEAR | CERT-F9 | Runbook |
| B01 | Blloqet 02/2026 (CORE-08) | 5–8 | — (CORE-08 i skanuar pa text layer; paginimi → pas i targetuar) | Bllok i prefiskalizuar, restore 48h, futje brenda 5 ditësh | MISSING | POS/operacione | Blocks | 48h + 5 ditë | none | CLEAR | BLOCKS-F5 | Manual blocks |
| B02 | Blloqet 02/2026 | EDI code | — (CORE-08 i skanuar pa text layer; paginimi → pas i targetuar) | Kod unik EDI; maksimum 50 seriale | MISSING | EDI/blloqe | Blocks | max 50 seriale | none | CLEAR | BLOCKS-F5 | EDI |
| B03 | Blloqet 02/2026 | continuity | — (CORE-08 i skanuar pa text layer; paginimi → pas i targetuar) | Vazhdimësi SEF→bllok→SEF, dy kopje, fusha manuale | MISSING | POS/sekuenca | Blocks | — | none | CLEAR | BLOCKS-F5 | Sequence |
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

## Interpretimi (i detyrueshëm)

- **`PROVEN_LOCAL` ≠ authority acceptance** — vetëm artefakt/test lokal i verifikuar pavarësisht.
- **`CONTRADICTS` = blocker i detyrueshëm PRE-Tier2** — **4 source-rows = 3 defekte unike**: T05→`T2-01` (money); U20→`T2-02`+`T2-03`; G04→`T2-02` (QR); T18→`T2-03` (NUIKF). **LIVE placeholder = G06/`T2-04` (MISSING, jo CONTRADICTS).**
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
*Historia: v1.0 (2026-07-12) — 94 kontrolle nga independent full-corpus audit. · v1.1 (2026-07-12, hardening po në këtë PR) — LAPT i plotësuar nga PDF-i primar (L09–L14; L07/D01 semantika e saktë; sanksionet 101(1)/101(2)/102.3 me faqe); T04 i ndarë (T22); G06 LIVE-harness; kolona `Closure key` + dallimi 4-rows/3-defekte; faqe reale per PDF-row ose arsye eksplicite; G03→CONFIRM_ATK; G01/T10 pointer fix → TOTAL 102, konfirmime 36. Me aprovimin e Pronarit (PR review = aprovimi).*
