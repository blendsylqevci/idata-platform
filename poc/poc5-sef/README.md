# PoC-5 — SEF: raport evidence (Tier 1) + Reliability Matrix

**Statusi (post-audit, cutoff 2026-07-12):** **audit regression / local mock evidence VETËM** — 6 test files, 66 PASS, 1 LIVE SKIPPED. **Tier 1 overall = PARTIAL / REOPENED** (JO overall mechanics/compliance PASS) · **Tier 1 compliance = FAIL** (4 CONTRADICTS: money `Total` ×100, QR = CitizenCoupon, NUIKF invalid, LIVE = placeholder) · **Tier 2 = NOT IMPLEMENTED / NO-GO** (jo thjesht "në pritje aprovimi" — mungojnë harness/CA/preflight/transport hardening) · **PoC-5 PASS = parakusht i ardhshëm, EKSPLICITISHT NOT MET sot**. PR #16 = **evidence historike e mock mechanics**. Burimi i verdiktit: `docs/canonical/05-sef-atk/ATK_Compliance_Gate.md` v2.0 + `ATK_SEF_Requirements_Ledger.md`.

> ⚠️ **Kualifikim i matricës më poshtë:** çdo rresht = **local mock evidence only** — provon nën-mekanikat lokale të outbox-it, JO kontratat zyrtare të wire-it (disa assertions money/QR janë vetë të gabuara — T2-01/T2-02), JO sjelljen e serverit real.
**Burimi:** `ANALIZA.md` (Faza A e aprovuar) · SSOT §7 · Reliability_Gate v1.1 · checklist-a SEF e PM_REVIEW_PROTOCOL.
**Sekretet:** zero identifikues në git/teste/evidencë; live raw → vetëm `evidence-private-local/`/`.sef-local/`.

## Si riprodhohet (Tier 1)

```bash
docker compose -f packages/database/docker-compose.yml up -d
export PGHOST=localhost PGPORT=5433 PGUSER=idata PGDATABASE=idata
pnpm --filter @idata/database build && pnpm --filter @idata/database migrate   # 0001..0006
pnpm --filter @idata/database test   # Tier 1; Tier 2 = SKIPPED me log të qartë
```

## Reliability matrix (moduli = KRITIK: fiscal outbox; Reliability_Gate v1.1, N/A sipas §5.1)

| Failure scenario | Expected behavior | Data safety guarantee | User-visible behavior | Recovery behavior | Automated/manual test | **Provohet në** | Evidence |
|---|---|---|---|---|---|---|---|
| **#2 SEF/ATK down — BËRTHAMA e këtij moduli** *(local mock evidence)* | Kuponi mbetet në radhë (`leshuar_offline`→`ne_pritje`), retry me backoff; izolimi i radhës = **i provuar vetëm në mock lokal** (pa scheduler/leasing production — T2-06/§5 Gate) | Rreshti + payload i firmosur të persistuar; idempotencë per `coupon_uuid` | Statusi i qartë në radhë; alarmi 48h/40h | Retry automatik; fiskalizim sapo kthehet ATK | **Automatik:** dështim rrjeti i shënjestruar ×2 → `ne_pritje` attempts 1→2 → sukses; backoff i respektuar | **Tier 1 (mock)** + vëzhgime Tier 2 | teste outbox PASS |
| **#1 Network down mid-send** | Dërgimi dështon → retry pa dublim | UNIQUE `coupon_uuid` lokal; serveri e sheh njësoj (idempotencë e synuar) | Radha në pritje | Retry i ardhshëm ia del | **Automatik:** i mbuluar nga i njëjti kanal dështimi (targeted network-fail para përgjigjes) | **Tier 1** | teste outbox PASS |
| **#5 Crash PAS send, PARA ACK** | Rreshti mbetet `ne_pritje` → ri-dërgim në resume | **Zero dublim lokal** (1 rresht); dublimi shihet nga serveri | Radha në pritje | Resume → fiskalizuar | **Automatik:** transport që pranon→pret para ACK → resume → `fiskalizuar`; `duplicateSends=1` te mock. ⚠️ **Mock = server idempotent (i njëjti tx) — SUPOZIM i flamuruar; sjellja REALE dokumentohet empirikisht në Tier 2** | **Tier 1 (supozim)** → **Tier 2 (empirik)** | test #5 PASS |
| **#6 Restart me outbox jo-bosh** | Radha = NË DB; instancë e re e drain-on | Persistencë e plotë (pg) | — | Resume i plotë pas restart | **Automatik:** "procesi A" enqueue → "procesi B" (instancë e re) drain → fiskalizuar | **Tier 1** | test #6 PASS |
| **#7 DB e outbox-it e paarritshme** | Gabim i pastër lidhjeje; zero shkrime të pjesshme | S'ka lidhje → s'ka rresht | Gabim i qartë | Retry kur DB kthehet | **Automatik:** pool port i gabuar → matcher specifik (codes incl. AggregateError); zero gjurmë me pool të shëndoshë | **Tier 1** | test #7 PASS |
| **#8 Outbox cap** | Kupon i ri → refuzim i qartë; **kurrë fshirje e heshtur** | Radha e paprekur | "Outbox plot (cap N)" | Drain → krijimi rifillon | **Automatik:** cap 2 → i 3-ti refuzohet; recovery pas drain. *Cap-i numëron VETËM të ridërgueshmet (leshuar_offline+ne_pritje) — deshtuar/kerkon_nderhyrje ndiqen nga ALARMET + ndërhyrja, jo nga cap-i (vendim i dokumentuar)* | **Tier 1** | test #8 PASS |
| Signing/cert failure | `kerkon_nderhyrje` + alert `sign_failure` — kurrë humbje e heshtur | Rreshti regjistrohet edhe pa firmë | Alert për ndërhyrje | Ndërhyrje njerëzore → ri-futje | **Automatik:** çelës i pavlefshëm → status + alert | **Tier 1** | test PASS |
| Afati 48h (Neni 44) — **⚠️ ORIGJINA JO-KONFORME (T2-08)** | trigger-i aktual llogarit `created_at + 48h` **per kupon** — **normativja NIS nga NDËRPRERJA e lidhjes (UA 44)** → mekanika e alarmit u provua lokalisht, **claim-i i konformitetit HIQET**; modeli outage-event = Step B/ADR input; warning ≈40h; missed edhe për `deshtuar` | Alarmet unike per rresht/tip | Alarme të qarta | Fiskalizim para afatit; pas = eskalim | **Automatik:** fake clock 39h→0 · 40h→warning · 49h→missed + testet e trigger-it | **Tier 1** | test 48h PASS |
| #3 Printer/peripheral | **N/A vetëm për SCOPE-in e izoluar të outbox-it** (§5.1) | — | — | — | — | — | *Riklasifikim 12.07:* N/A vlen VETËM pse ky PoC = outbox i izoluar; **për SEF end-to-end, fiscal print/recovery i pajisjes = GATE i hapur (U09/T13, F7/F8) — JO i hequr në heshtje.* |
| #4 Device crash | **N/A vetëm për SCOPE-in e izoluar të outbox-it** (§5.1) | — | — | — | — | — | *Riklasifikim 12.07:* modeli i pajisjes u provua në PoC-3 vetëm për sync-un e përgjithshëm; **për SEF end-to-end, custody e çelësit PER-POS + rikuperimi i pajisjes fiskale = GATES të hapura (T2-05/U13) — JO waived.* |

**Izolimi (SSOT §7):** i provuar dy-drejtimësh **vetëm në mock lokal** (skenari i izoluar outbox↔outbox); **≠ izolim end-to-end i SEF** (POS→print→server). Test i dedikuar Tier 1.

## Firma — kodimi (gjetja kritike e review-t)

**P1363 (raw r||s, EKZAKT 64 byte për P-256) = default-i ynë EKSPLICIT** (`dsaEncoding: 'ieee-p1363'`); Node default = DER (~70-72 byte) → pa eksplicitim firmat s'përputhen me .NET-referencën. Test: P1363 = 64B ✓ verifikohet; DER ≠ 64B ✓ dallohet; kryqëzimi i formateve → false. **Konfirmimi final = Tier 2**; **playbook i debug-ut** për *FAILED VERIFICATION* të parë real: **#1** formati i firmës (P1363↔DER) — provo alternativën para çdo gjëje; **#2** *ÇFARË nënshkruhet* — `SHA256(UTF8(base64Proto))` (rruga jonë) vs **raw proto bytes** — provo këtë axis nëse #1 s'e rregullon.

## Tier 2 LIVE — checklist (post-audit: NIS me blockers e Step B)

> **Trupi aktual i testit LIVE = PLACEHOLDER që gjithmonë hedh** (T2-04). Checklist-a NIS me mbylljen e **T2-01..T2-10** (Gate v2 §4 / tracker `ATK-012`) — para tyre asnjë hap live s'ekziston realisht:

- [ ] **Step B:** T2-01 money ×100 · T2-02 QR CitizenCoupon · T2-03 NUIKF gen/valid/unik · T2-04 harness real test-host-only · T2-05 CA onboarding 1 POS + key custody · T2-06 transport hardening · T2-07 P1363/DER explicit + vectors · T2-08 48h nga outage · T2-09 heq business context sintetik · T2-10 zero sensitive në evidence.
- [ ] **Step C:** golden vectors zyrtarë + fake-HTTP/negative tests; **pastaj** independent review + private preflight (wrapper-i evidence-safe ndërtohet në Step B/C — **sot NUK ekziston**) + Human Gate i veçantë.
- [ ] Blendi: `.env.sef.local` (nga `.env.sef.example`) + `.sef-local/` — **jashtë chat-it**; konfirmim "done".
- [ ] Env opt-in: `RUN_SEF_LIVE=1` + `SEF_ENV=test` (URL = vetëm test-serveri; CI gjithmonë skip).
- [ ] Onboarding PKI: `/ca/verify/{nui}` → CSR (P-256, çelësi në `.sef-local/`) → `/ca/signcsr`.
- [ ] PosCoupon → `transaction_id` real (**pa FAILED VERIFICATION**; nëse po → playbook-u i debug-ut më lart: #1 P1363↔DER, #2 çfarë-nënshkruhet).
- [ ] RETURN/CANCEL me ReferenceNo → pranohen; **pa** → refuzim i pritur (dokumento mesazhin).
- [ ] Dublim i dërgimit (i njëjti kupon ×2) → **dokumento sjelljen REALE** (zëvendëson supozimin e mock-ut te #5).
- [ ] Crash-pas-send-para-ACK real (modeli #5b) → dokumento resend-in.
- [ ] Evidence raw → `evidence-private-local/`; në git vetëm **të maskuara** (tx_id → 8 karaktere + "…").

## Kufizime të njohura

- Tier 1 = mock; **prova përfundimtare e protokollit = Tier 2** (server real). **Redaction-i aktual (sanitizer) ≠ standard i pranueshëm** — allowlisted redaction = T2-06/T2-10.
- Sjellja e serverit ndaj dublimit = e padokumentuar → supozimi i mock-ut i flamuruar; mbyllet empirikisht.
- `payload_ref` mban payload-in e firmosur në DB-në lokale të PoC (sintetik në teste); në live mbetet në DB lokale — kurrë në git/log.
- Integrimi i plotë POS + certifikimi i prodhimit = jashtë PoC (kjo është provë teknike e rrugës).

**Inpute për F1 (dizajni real — jo ky PoC):**
- **`created_at` immutability**: trigger-i rillogarit `deadline_at` nga `created_at`, por vetë `created_at` mbetet i ndryshueshëm — në F1 outbox-i fiskal merr modelin canonical: **payload/identitet/`created_at`/`deadline_at` immutable; DELETE i ndaluar; status transitions vetëm përmes funksionit të kontrolluar; attempt/status-event logs append-only** — që afati të mos zhvendoset dot as përmes `created_at`.
- **Guard dy-drejtimësh i shkallëve**: sot kufiri validon hyrjet (dp-max per shkallë); në F1 shtohet mbrojtje edhe në nivel tipi/lexim (brand types për ×10000 vs cents, konvertime të kryqëzuara të bllokuara në compile-time), që një int i shkallës së gabuar të mos futet dot askund në PosCoupon.
