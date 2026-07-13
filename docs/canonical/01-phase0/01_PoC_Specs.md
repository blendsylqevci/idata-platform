# Faza 0 — Specifikimet e 5 PoC-ve (Porta e Provës)

**Versioni:** 1.2 · **Data:** 28 Qershor 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.5
**Rregull:** PoC = i izoluar, mock data, jo prodhim. Nëse një PoC **dështon → ndalemi dhe korrigjojmë arkitekturën** para Fazës 1. Çdo PoC kërkon **evidencë** (screenshot/log/test).

---

## PoC-1 — Tenant Isolation
**Qëllimi:** KABI s'i sheh të dhënat e PLANET dhe anasjelltas; izolim i fortë multi-company.
**Scope (brenda):** `business_id` në tabela tenant-scoped; RLS si safety-net; query nga NestJS i kufizuar me tenant; Company Switcher vetëm për Group Owner.
**Jashtë:** UI e plotë, module reale.
**Hapat:** krijo 2 business (KABI, PLANET), 2 user; user i KABI-t kërkon të dhënat e PLANET (API + direkt DB me rolin app); provo edhe RLS me kredencialin e gabuar.
**Kriteret e pranimit (PASS):**
- User i KABI-t **kurrë** s'merr rresht me `business_id` të PLANET (API + RLS).
- Pa `business_id` valid → mohim (deny by default).
- Group Owner sheh të dyja vetëm përmes Company Switcher të kontrolluar.
**FAIL:** çdo rrjedhje ndër-tenant.
**Evidenca:** logje query + test automatik (deny), screenshot.
**Pronari:** Claude Code → Codex verifikon. **SSOT:** §2, §9.

---

## PoC-2 — Accounting (faturë → TVSH → AR/AP → GL → libër ATK)
**Qëllimi:** të vërtetojmë motorin e postimit dhe librat ATK 1:1.
**Scope (brenda):** një faturë shitje + një blerje → postim GL (debit=kredit) → AR/AP → rresht në Libri i Shitjes/Blerjes me **hartën e kutive** të Deklaratës (Bazë18→[43], Bazë8→[45], fermer→[51], total→[67]); para në integer minor units / `numeric`.
**Jashtë:** plan kontabël i plotë, pasqyra.
**Hapat:** fut 1 faturë 18% + 1 faturë 8% + 1 akt-blerje fermeri; gjenero GL + të dy librat + përmbledhjen e TVSH; krahaso me një shembull PRO DATA.
**Kriteret (PASS):**
- Debit = Kredit në çdo postim.
- Shumat TVSH = 18%/8% × bazë (saktësi decimal, jo float).
- Rreshtat bien në kutitë e sakta të deklaratës; totali [67] = v18 + v8.
- Akti i fermerit shkon te [51].
**FAIL:** mospërputhje shumash ose kuti e gabuar.
**Evidenca:** export i librave + krahasimi numerik me PRO DATA.
**Pronari:** Claude Code + Kontabilisti (validim). **SSOT:** §4.

---

## PoC-3 — Offline → Sync (pa dublim)
**Qëllimi:** mobile offline → outbox → sync, pa humbje, pa dublim, me idempotencë.
**Scope (brenda):** DB lokale (SQLite/Realm); UUID në pajisje; outbox; sync idempotent; rregullat e konfliktit (stok negativ i përkohshëm, çmim i ngrirë + variance, kredi → bllokim në sync); audit me **timestamp real**.
**Jashtë:** UI e plotë e shitjes.
**Hapat:** krijo 5 fatura offline; ndërpre rrjetin në mes të sync-ut; ribëj sync; provo dy pajisje që shesin të njëjtin stok; provo klient mbi limit kredie offline.
**Kriteret (PASS):**
- Pas sync-ut të dyfishuar → **0 dublime** (idempotencë).
- Asnjë faturë e humbur; UUID unike.
- Stok i dyfishuar → pranohet + stok negativ i përkohshëm → rregullohet në sync + raport variance.
- Klient mbi limit → faturë e pranuar + klient "I Bllokuar" në sync.
**FAIL:** dublim, humbje, ose konflikt i pazgjidhur.
**Evidenca:** logje sync + raport variance + test idempotence.
**Pronari:** Claude Code → Codex. **SSOT:** §2 (rregullat e konfliktit).

---

## PoC-4 — Local Hub (LAN pa internet)
**Qëllimi:** PLANET (kamarier + KDS) punon 100% në LAN kur bie interneti.
**Scope (brenda):** Mini-PC/NUC hub me Docker + Postgres lokal; porosia nga "kamarieri" → hub → ekran KDS; kur kthehet interneti → sync në cloud.
**Jashtë:** POS i plotë, të gjitha stacionet.
**Hapat:** fik internetin; bëj 10 porosi nga 2 pajisje; ruto në 2 ekrane KDS; ndiz internetin; verifiko sync + asnjë porosi e humbur; provo fikje rryme (UPS) pa korruptim.
**Kriteret (PASS):**
- Cikli porosi→KDS punon pa internet (<2s latencë në LAN).
- Pas rikthimit → të gjitha porositë në cloud, pa dublim.
- Fikje rryme → DB e paprishur (UPS + WAL).
**FAIL:** ndalesë pune offline, humbje porosish, korruptim DB.
**Evidenca:** video/screenshot offline + logje sync + test recovery.
**Pronari:** Claude Code → Codex. **SSOT:** §2, §6.

---

## PoC-5 — SEF (fiskalizim) — ⚠️ ACCEPTANCE NOT MET (post-audit 12.07.2026)

> **Statusi aktual:** PR #16 = evidence historike mock; **Tier 1 overall PARTIAL/REOPENED · compliance FAIL · Tier 2 NOT IMPLEMENTED/NO-GO**; kriteret PASS më poshtë **NUK janë të përmbushura sot** dhe mbeten parakusht i ardhshëm (Gate v2.0 + Ledger). Money/QR më poshtë = **të korrigjuara**.
**Para-kusht (MANDATORY):** analizo `github.com/fiskalizimi` (pos-golang/csharp/php/ruby) + `models.proto` + Swagger (`https://fiskalizimi-test.atk-ks.org/swagger`). **PoC nuk nis pa këtë.** Detajet/identifikuesit: `06_SEF_Test_Phase_Active.md`.
**Qëllimi:** onboard një POS test (PKI) + dërgo PosCoupon te serveri TEST + statuset + alarmi 48h.
**Scope (brenda):** ECDSA P-256 per POS; onboarding `/ca/verify`→CSR→`/ca/signcsr`; Protobuf (CitizenCoupon + PosCoupon, **detajet përputhen**); QR = **base64(`CitizenCoupon` proto)|base64(firma e CitizenCoupon)** *(korrigjuar 12.07)*; `POST /pos/coupon` → `transaction_id` (uint64 ekzakt); fiscal outbox i ndarë nga sync; statuset (lëshuar offline / në pritje / fiskalizuar / dështuar / kërkon ndërhyrje); alarm 48h **nga ndërprerja e lidhjes** *(korrigjuar 12.07)*; **tax A/C/D/E** (A=exempt/C=0/D=8/E=18); **money PER-FIELD i KORRIGJUAR: unit `Price` = ×10000; item `Total` + totals/payments/tax = ×100 (cents)** — golden: €1.50×3 → Price=15000, Total=450; **CouponType Sale=1/Cancel=2/Return=3**.
**Jashtë:** integrim i plotë POS.
**Pre-kushti:** ✅ test phase AKTIV (App ID/Fiscalization No te `06_SEF_Test_Phase_Active.md`).
**Hapat:** (1) onboard POS test (verify→CSR→signcsr → çelës+certifikatë); (2) ndërto+nënshkruaj PosCoupon → `/pos/coupon` (test) → merr `transaction_id`; (3) `RETURN`/`CANCEL` me `ReferenceNo`; (4) offline → fiskalizim brenda 48h + alarm para afatit; (5) certifikatë e skaduar → "kërkon ndërhyrje" + alert.
**Kriteret (PASS):**
- PosCoupon merr `transaction_id` nga test serveri; CitizenCoupon↔PosCoupon **përputhen** (jo "FAILED VERIFICATION").
- `RETURN`/`CANCEL` me `ReferenceNo` pranohen; pa ReferenceNo → refuzohen (siç pritet).
- Kuponi offline fiskalizohet brenda 48h; alarm para afatit.
- Dështimi → status + alert; **fiscal outbox i izoluar nga sync-u i përgjithshëm** (izolimi i provuar deri sot vetëm në mock lokal).
**FAIL:** "FAILED VERIFICATION", kupon pa `transaction_id` pa alarm, ose outbox i bllokuar.
**Evidenca:** përgjigjet e test serverit (`transaction_id`) + QR + screenshot statusesh + log alarmi.
**Pronari:** Claude Code + Pronari (ATK). **SSOT:** §7. **Ref:** `06_SEF_Test_Phase_Active.md`.

---

## Përmbledhje (porta Go/No-Go për Fazën 1)
| PoC | Pa të cilin s'kalojmë në Fazën 1 | Varësi e jashtme |
|---|---|---|
| 1 Tenant isolation | PO | — |
| 2 Accounting | PO | shembull PRO DATA (e kemi) |
| 3 Offline sync | PO | — |
| 4 Local hub | PO | 1 Mini-PC + UPS (test) |
| 5 SEF | PO | 🟢 test phase AKTIV (github.com/fiskalizimi + test server) |
