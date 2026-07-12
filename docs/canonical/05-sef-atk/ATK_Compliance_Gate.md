# ATK Compliance Gate — verdikti + gates (v2.0, post full-corpus re-audit)

**Versioni:** 2.0 · **Cutoff i auditit:** 2026-07-12 · **Repo baseline:** `55126b1b48004e4fba4a5604c9717cd9456722d3` · **Pronari:** Group Owner (Human Gate) · **Statusi:** canonical — effective on merge
**Burimet:** `ATK_Source_Register.md` v2.3 (bounded registered corpus) · **burimi i statusit = `ATK_SEF_Requirements_Ledger.md` v1.4 (117 source-rows · 44 closure keys · disposition substantiv 1–123)** — ky Gate mban verdiktin, kontratat e korrigjuara dhe gates; NUK dyfishon statuset per-kontroll.
**Gate-t:** **[T2]** = bllokues PARA Tier 2 (thirrjes së parë reale të test-serverit) · **[C]** = bllokues certifikimi · **[G]** = bllokues go-live · **[H]** = hardening.

---

## 1. VERDIKTI EKZEKUTIV (identik kudo — pa zbutje)

- **Audit coverage: COMPLETE** — VETËM për source set-in e enumeruar/ngrirë shprehimisht dhe metodën e dokumentuar të kërkimit publik, **cutoff 2026-07-12** (CORE-01..08 = content-frozen; extended legal = reviewed as-of cutoff; GitHub/Swagger/regjistri publik = dynamic snapshots). **JO closed-world claim** se çdo akt i mundshëm publik/i papublikuar është gjetur.
- **Product compliance: NOT COMPLETE.**
- **Audit regression run:** 6 test files, **66 PASS, 1 LIVE SKIPPED** (cutoff 12.07.2026) — kjo provon **vetëm nën-mekanikat lokale mock/outbox**; **disa assertions money/QR janë VETË të gabuara** (shih §3).
- **Tier 1 overall: PARTIAL / REOPENED.** **Tier 1 compliance: FAIL.**
- **Tier 2: NOT IMPLEMENTED / NO-GO.**
- **Certification / go-live: NO-GO.**
- **PoC-5 (PR #16) = evidence historike e mock mechanics — JO compliance PASS.**

**Shpërndarja aktuale (ledger v1.4 — 117 source-rows):** MISSING 64 · PARTIAL 21 · EXTERNAL_GATE 19 · **CONTRADICTS 5 (= 4 defekte unike: money/QR/NUIKF/origjina-48h)** · PROVEN_LOCAL 2 · N/A_LINEAGE 6; **43 konfirmime**; **44 unique closure keys**; **T2-01..T2-10 të gjithë me rreshta realë** (U27→T2-08; G06→T2-04; G07→T2-09; G08→T2-10).

## 2. `CONTRADICTS`: **5 source-rows = 4 DEFEKTE UNIKE** (+ LIVE placeholder = MISSING/`T2-04`, JO CONTRADICTS)

| # | Ledger | Kontrata zyrtare | Evidence aktual në repo | Verdikt |
|---|---|---|---|---|
| 1 | **T05** | KT 25.4 + builders C#/Go: **unit Price ×10000; item Total ×100 (cents)** | `poc5-money.ts` / `poc5-proto.ts` / `poc5-sef.test.ts` enkodojnë/assert-ojnë **item Total ×10000** | **100× gabim** → `T2-01` |
| 2 | **G04** (+U20) | QR = **base64(`CitizenCoupon` protobuf) \| base64(firma e TIJ)** | testi firmos **`PosCoupon`** për QR; CitizenCoupon vetëm round-trip | **objekt i gabuar** → `T2-02` |
| 3 | **T18** (+U20) | **NUIKF**: alfanumerik, **pa separatorë**, **max 16**, unik, automatik | fixture me **viza / 17 karaktere**; zero validator/generator/uniqueness runtime | **invalid** → `T2-03` |
| 4 | **U27** | UA 44: **afati 48h NIS nga momenti i ndërprerjes** (mbështetje: forma EDI e blloqeve kërkon "Data e fillimit të ndërprerjes" — CORE-08 f.8) | trigger-i `0006` llogarit `created_at + 48h` per kupon | **origjinë e gabuar** → `T2-08` |
| — | **G06 (MISSING — jo CONTRADICTS)** | Tier 2 = CA onboarding + rrugë reale test-serveri | testi LIVE = **placeholder i skip-uar që gjithmonë hedh** | **NOT IMPLEMENTED** → `T2-04` |

## 3. KONTRATAT TEKNIKE TË KORRIGJUARA (e vërteta canonical)

1. `CouponItem.Price` = **euro ×10000**.
2. `CouponItem.Total` = **euro ×100 (cents)** — **JO ×10000**.
3. Coupon total, payments, tax groups, `TotalTax`, `TotalNoTax`, `TotalDiscount` = **×100**.
4. **Golden example:** €1.50 × 3 → `Price=15000`, `Total=450`.
5. API `/pos/coupon` dërgon **`PosCoupon`**; **QR = base64(`CitizenCoupon` protobuf) | base64(signature e CitizenCoupon)**; fushat e përbashkëta PosCoupon↔CitizenCoupon **duhet të rakordojnë**.
6. **NUIKF:** alfanumerik, pa separatorë, max 16, unik, automatik — **formati DIHET**; **OPEN mbeten VETËM**: binding-u me proto `VerificationNo`, algoritmi dhe origjina server-side (`ATK-005`) — **termat NUK trajtohen si ekuivalentë të provuar**.
7. **Fakt normativ:** afati **48h NIS nga momenti i ndërprerjes së lidhjes** (UA 44). Implementimi aktual `coupon.created_at + 48h` për çdo kupon = **jo-konform**; `created_at` mbetet **immutable** për audit. Emri `outage_started_at`, modeli i eventit dhe detektimi = **PROPOSED / ADR input** — JO vendim arkitekturor i finalizuar nga ky PR.
8. **Firma:** C# zyrtar = **P1363**; Go zyrtar = **ASN.1 DER** (CONF-02). **P1363 lokal = kandidat, JO kontratë finale** — encoding mbetet **konflikt i hapur** deri në server evidence/ATK.
9. **PKI minimum Tier 2:** keypair **unik per POS/makinë** + certifikatë **per instalim SEF**; **çelësi privat nuk largohet** nga makina ku u gjenerua.
10. **Tier 2 nuk pret vetëm Human Gate:** ekziston një `LiveAtkTransport` minimal, por **mungojnë** executable LIVE harness, CA onboarding, evidence-safe preflight wrapper dhe production-grade timeout/cap/retry/redaction.

## 4. BLLOKUESIT `T2-01..T2-10` — PRE-Tier2 [T2] (ID stabile; identike me `ATK-012` dhe PR body; ASNJËRI i implementuar)

| ID | Blocker |
|---|---|
| `T2-01` | `CouponItem.Total` **cents**, jo ×10000 |
| `T2-02` | QR **bind/sign `CitizenCoupon`**, jo `PosCoupon` |
| `T2-03` | **NUIKF** valid/generator/validator/uniqueness; binding-u me `VerificationNo` i trackuar |
| `T2-04` | Zëvendësohet LIVE placeholder me **executable test-host-only harness** |
| `T2-05` | **CA onboarding për një POS testues** + secure local key/cert custody |
| `T2-06` | Transport: **timeout, response cap, exact uint64, retry classification, allowlisted redaction** |
| `T2-07` | **P1363/DER explicit/configurable** + independent vectors; server-confirmed behavior pa evidence sensitive |
| `T2-08` | Korrigjo/hiq claim-in jo-konform të **48h**; modeli final i outage-it = Step B/ADR input |
| `T2-09` | Hiq **hardcoded synthetic business context** nga live path |
| `T2-10` | **Zero identifier/payload/key/cert/signature/raw response** në evidence të commit-uar |

**Statusi:** të DHJETË = **OPEN** (`ATK-012`, P0/PRE-TIER2). **Certifikimi/go-live kanë blockers SHTESË veçmas** (§5) — mbyllja e T2-01..10 hap vetëm Tier 2, jo certifikimin.

## 5. Blockers certifikimi/go-live PËRTEJ Tier 2 [C]/[G] (ledger-i mban detajet)

Online-accept/persist-**para**-print pipeline (U03/T15) · renderer i plotë kuponi/QR + etiketat OFFLINE/KOPJE + fushat e detyrueshme (U04/T13/U09) · pandryshueshmëria fiskale, tranzicionet e kontrolluara, attempt/status logs, tenant RLS/composite FK në tabelat fiskale (T06) · invariantet monetare/tatimore runtime + **CouponId zyrtar unik business-wide** për idempotencë (G02/T03) · scheduler/leasing/`FOR UPDATE SKIP LOCKED`/recovery (T15) · blloqet manuale EDI + vazhdimësia e sekuencës (B01–B03) · paketa e certifikimit/manuali/deklarata + recert workflow (C02/C05/C06) · TVSH faturim/libra/eksport (V01–V08) · governance e privatësisë (P01–P10) · PoC-4b hardware = paralel.

## 6. Rerating i pretendimeve file-specific (i DETYRUESHËM — zëvendëson vlerësimet e vjetra)

| Pretendimi i vjetër | Rerating (sot) |
|---|---|
| "kronologjia po (device_seq/outbox)" | **FALSE** — vetëm rendi në outbox mock (T20 PARTIAL); pa sinjal vizual, pa mbishkrim OFFLINE në print |
| "pipeline fiskal po (PoC)" | **OVERCLAIM** — mock-only; pa online-accept-before-print, pa server real (T02/T15 PARTIAL) |
| numërimi ditor + rendor kronologjik | **NOT IMPLEMENTED** (MISSING — ledger U04-fusha/T-rows përkatëse) |
| privacy/redaction "e zgjidhur" | **ULUR** — sanitizer-i aktual ≠ standard i pranueshëm redaction; allowlisted redaction = `T2-06`/`T2-10` |
| retry "automatik" | **manual/mock** — JO production scheduler/automatic transmission (T15) |
| "Tier 1 mock i mbulon rrugët" | **FALSE** — mock s'provon money/QR/NUIKF/server/encoding (shih §1 "regression run") |
| "firma per kupon po" | **e pakonfirmuar në encoding** — P1363 vs DER = CONF-02/`T2-07` |
| QR "i zbatuar+testuar" | **FAIL sot** — firmoset PosCoupon (G04/`T2-02`) |
| NUIKF "PJESËRISHT" | **FAIL sot** — fixture invalid; zero generator/validator (T18/`T2-03`) |
| artefaktet private të certifikimit "mungojnë" | **NOT VERIFIED in tracked repo** — zonat private të injoruara NUK u lexuan; s'pretendohet mungesa e tyre |

## 7. RECERTIFICATION GATE (KT 25.7–8 + Kushtet N7.4 — i pandryshuar në parim)

Çdo release/PR që prek modulin fiskal klasifikohet **`RECERT: PO/JO`**; ndryshimet funksionale/teknike që ndikojnë gjenerimin/ruajtjen/transmetimin e të dhënave fiskale kërkojnë **njoftim PARAPRAK të ATK-së** (C05). Mirëmbajtja: monitoro burimet sipas `ATK_Source_Register.md` §6 (drift rules + refresh cadence); çdo drift → rihap rreshtat e prekur të ledger-it.

## 8. Kufiri i certifikimit (bounded surface — i pandryshuar)

Monorepo mbetet NJË; fiscal-core i izoluar; **fokusi i prezantimit para Komisionit = kandidat i brendshëm pune (POS/SEF + diagrami i integrimit), JO vendim**; **lista finale e source/artifacts = GATED nga `ATK-010`** + Human Gate. **Rregulli i artefaktit të komisionit (i brendshëm, i hekurt):** pa DEMO VERSION/mock/PoC routes/placeholders/sample identifiers; release-candidate i versionuar e funksional; etiketa zyrtare TEST **nuk hiqet**; fixtures sintetike vetëm në test suite.

## 9. `ATK-011` — **OPEN / PENDING_INDEPENDENT_REVIEW** (builder-i s'e mbyll vetë)

Crosswalk-u LAPT (Arts. 6.1–6.13, 43.3–43.5; sanksionet **100/101(1)/101(2)/101(9)/102.3/106**; Art.6.11–12 ↔ Vendimi 19/2026 me caveat-in **akti i nënshkruar s'u gjet publikisht**; UA 49(2) scope; KO159/24 dispozitivi) = i PËRPUNUAR në `ATK_SEF_Requirements_Ledger.md` v1.3 (L01–L26 + K01–K04 + disposition substantiv 1–123 me arsye per-nen). **Statusi: OPEN / PENDING_INDEPENDENT_REVIEW** — mbyllja kërkon coverage nen/paragraf të konfirmuar + reconciliation të plotë + zero substantive gap + **review të pavarur nga Codex + Human Gate të Pronarit**; edhe atëherë mbyllet **vetëm mapping-u dokumentar** (JO compliance/implementim/certifikim). Step B/C mbeten blockers të sekuencës.

## 10. BASELINE HISTORIK (etiketë — JO status aktual)

Auditimi 09.07.2026 (PR #17): **278 kërkesa të nxjerra** me granularitet extraction + **nota 7/10** + shpërndarja e atëhershme (ZBATUAR 33 / SPECIFIKUAR 17 / PJESËRISHT 97 / MUNGON 21 / N/A 72) = **HISTORICAL BASELINE me granularitet tjetër** — **NUK është certifikatë aktuale**. Statusi aktual = §1 + ledger-i 94-kontrollesh (pa krahasim mekanik 278↔94 — njësi të ndryshme matëse; shih shpjegimin në ledger).

## 11. Sekuenca e radhës (pa cikël)

**Step A (ky PR docs)** → **Step B** PRE-Tier2 code hardening (T2-01..T2-10, `ATK-012`) → **Step C** official golden vectors + fake-HTTP/negative tests → **independent re-review** → **HUMAN GATE (autorizimi për thirrjen e PARË reale)** → **private `/ca/verify` PREFLIGHT** (wrapper evidence-safe i ndërtuar në Step B/C) → **vetëm pas suksesit të preflight-it vazhdon pjesa tjetër e Tier 2**. **Asnjë `/ca/verify`, `/ca/signcsr`, `/pos/*` ose `/citizen/*` para Human Gate.** Certifikimi/go-live = NO-GO më vete (§5). PoC-4b paralel.

## Lidhjet

`ATK_Source_Register.md` v2.3 · `ATK_SEF_Requirements_Ledger.md` v1.4 · `Kupon_Print_Spec.md` v1.1 · `Reliability_Gate.md` §5.2 · `PM_REVIEW_PROTOCOL.md` v1.5 · `poc/poc5-sef/ANALIZA.md` (korrigjimi post-audit) · Tracker (`ATK-002` REOPENED, `ATK-005`, `ATK-010`, `ATK-011` OPEN/PENDING_INDEPENDENT_REVIEW, `ATK-012`, `ATK-013`, `ACC-012`, `PRIV-001`).

---
*Historia: v1.0 (2026-07-09, PR #17) — regjistri 278-kërkesësh KT+UA, 8 CRITICAL, nota 7/10. · v1.1 (2026-07-10, PR #18) — amendamenti 22.06, §2e Kushtet, kufiri i certifikimit. · v2.0 (2026-07-12, ky PR) — **full-corpus re-audit i kanonizuar**: verdikti ekzekutiv (Tier 1 PARTIAL/REOPENED, compliance FAIL, Tier 2 NOT IMPLEMENTED/NO-GO, certifikimi NO-GO), 4 CONTRADICTS, kontratat e korrigjuara (money ×100, CitizenCoupon QR, NUIKF, 48h nga outage, P1363/DER OPEN, PKI per-POS), T2-01..T2-10, rerating i overclaims, statusi → Ledger (94 në v1.0 — numërator historik); 278/7-10 = baseline historik. · hardening 12.07 (po ky PR): §2 = **5 rreshta / 4 defekte unike** (U27 = origjina 48h); counts → **Ledger v1.2 (108/38/40)**; ATK-011 = disposition 1–123; **rendi: … → Human Gate → private `/ca/verify` preflight → Tier 2**. Dorëzuar për review të pavarur.*
