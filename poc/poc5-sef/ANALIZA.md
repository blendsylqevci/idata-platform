# PoC-5 — SEF: ANALIZA e protokollit zyrtar (FAZA A — SANITIZED)

**Statusi:** FAZA A — analizë PA ASNJË kod SEF (dokument HISTORIK i Fazës A; ruhet si histori).

> ## ⚠️ KORRIGJIM POST-AUDIT (full-corpus re-audit, cutoff 2026-07-12) — VERDIKTI AKTUAL
> **Tier 1 overall = PARTIAL / REOPENED · Tier 1 compliance = FAIL · Tier 2 = NOT IMPLEMENTED / NO-GO · Certifikimi = NO-GO.** Auditimi i pavarur gjeti **4 CONTRADICTS** kundër burimeve zyrtare (money `CouponItem.Total` ×100 jo ×10000; QR duhet të firmosë **CitizenCoupon** jo PosCoupon; NUIKF fixture invalid; LIVE = placeholder) — disa pretendime të këtij dokumenti më poshtë janë **SUPERSEDED** dhe shënohen inline. Burimi aktual i së vërtetës: `docs/canonical/05-sef-atk/ATK_Compliance_Gate.md` v2.0 (§3 kontratat) + `ATK_SEF_Requirements_Ledger.md` (94 kontrolle) + `ATK_Source_Register.md` v2.0. **Ky dokument NUK thotë më se `pos-csharp` është i vetmi kanonik dhe NUK pretendon "asnjë kontradiktë"** — org-u zyrtar ka konflikte të regjistruara (CONF-01..04).
**Rregulli i sekreteve:** ky dokument përmban **ZERO identifikues** (App ID / Fiscalization No / POS ID / NUI / çelësa / QR reale). Vlerat hyjnë vetëm përmes env-it që e mbush Blendi me dorë (`.env.sef.local`, gitignored); agjenti s'i lexon kurrë.

---

## 0. Snapshot i burimit (rregulli D)

| Burimi | Referenca | Versioni/SHA | Data e kontrollit |
|---|---|---|---|
| Org zyrtar | `github.com/fiskalizimi` | 4 repo publike | 2026-07-08 |
| `pos-csharp` **[SUPERSEDED 12.07.2026: asnjë implementim i vetëm s'është kanonik — C#/Go konfliktojnë në firmë (CONF-02); shih Register v2 §4]** | `readme.md`, `models.proto`, `fiskalizimi/*.cs` | commit `a4060ef08838` · 2026-06-11 | 2026-07-08 |
| `pos-golang` | `readme.md`, kod | commit `e28cc25d32f3` · 2026-06-11 | 2026-07-08 |
| `pos-php` | (referencë dytësore) | commit `4e7dddbbed6c` · 2026-06-08 | 2026-07-08 |
| `pos-ruby` | (referencë dytësore) | commit `d146f7f16e71` · 2024-09-27 | 2026-07-08 |
| Swagger test-server | `https://fiskalizimi-test.atk-ks.org/swagger/index.html` (spec: `/swagger/doc.json`) | "Fiscalization API" v0.9 | 2026-07-08 |

**Verdikt përputhjeje (i Fazës A) — [SUPERSEDED 12.07.2026]:** leximi i atëhershëm "asnjë kontradiktë" ishte **i gabuar për `CouponItem.Total`** (shih korrigjimin te §3) dhe **nuk e kapte konfliktin C#↔Go të firmës** (P1363 vs DER — CONF-02). Konfliktet aktuale = `ATK_Source_Register.md` §5 (CONF-01..10).

## 1. Endpoints (nga Swagger v0.9 + README)

Test-server: `https://fiskalizimi-test.atk-ks.org` (prodhimi `https://fiskalizimi.atk-ks.org` — **NUK përdoret askund në PoC**).

| Endpoint | Request (skema Swagger) | Response 200 |
|---|---|---|
| `POST /ca/verify/{nui}` | `{application_id:int, branch_id:int, fiscalization_no:string, pos_id:int}` | `{business_name, verification_code:string}` *(korrigjuar 12.07: string, JO int)* |
| `POST /ca/signcsr` | `{application_id, branch_id, business_id, business_name, csr(PEM), pos_id, **verification_code**}` *(korrigjuar 12.07: fusha = `verification_code`, JO `verification_no`)* | `{signed_certificate}` |
| `POST /pos/coupon` | `{details: base64(PosCoupon proto), signature: base64(ECDSA sig)}` | `{message, transaction_id:**uint64 ekzakt**}` *(korrigjuar 12.07: JO int gjenerik — trajtohet exact/pa humbje)* |
| `POST /citizen/coupon` | `{citizen_id, qr_code}` (aplikacioni i qytetarit — jo fokusi ynë) | kuponi i deshifruar |

Onboarding-u i plotë: gjenero çelës **ECDSA P-256** → `POST /ca/verify/{nui}` → merr `verification_code` → ndërto **CSR (PEM)** me business_name + verification_code → `POST /ca/signcsr` → merr `signed_certificate`. (Ekziston edhe onboarder-tool zyrtar si binar; ne ndjekim rrugën programatike.)

## 2. Strukturat proto (nga `models.proto` @ `a4060ef`)

**Enums:** `CouponType { UnknownType=0, Sale=1, Cancel=2, Return=3 }` · `PaymentType { UnknownPayment=0, Cash=1, CreditCard=2, Voucher=3, Cheque=4, CryptoCurrency=5, Other=6 }`

**`PosCoupon`** (fushat + numrat e proto-s — DUHET të përputhen bit-për-bit):
`BusinessId u64=1 · CouponId u64=2 · BranchId u64=3 · Location str=4 · OperatorId str=5 · PosId u64=6 · ApplicationId u64=7 · VerificationNo str=8 · Type CouponType=9 · Time i64=10 (Unix SECONDS) · Items repeated CouponItem=11 · Payments repeated Payment=12 · Total i64=13 · TaxGroups repeated TaxGroup=14 · TotalTax i64=15 · TotalNoTax i64=16 · ReferenceNo u64=17 · TransactionNo u64=18 · TotalDiscount i64=19`

**`CouponItem`:** `Name str=1 · Price i64=2 · Unit str=3 · Quantity float=4 · Total i64=5 · TaxRate str=6 · Type str=7` *(shembujt zyrtarë: `Type="TT"` — semantika s'dokumentohet; ndiqet shembulli)*

**`Payment`:** `Type PaymentType=1 · Amount i64=2` — **`TaxGroup`:** `TaxRate str=1 · TotalForTax i64=2 · TotalTax i64=3`

**`CitizenCoupon`** (për QR): `BusinessId=1 · CouponId=2 · BranchId=3 · PosId=4 · VerificationNo str=5 · Type=6 · Time i64=7 · Total i64=8 · TaxGroups=9 · TotalTax=10 · TotalNoTax=11` — **detajet duhet të përputhen me PosCoupon-in përkatës**, përndryshe serveri kthen *FAILED VERIFICATION*.

## 3. TABELA MONEY-SCALE PER-FIELD (kritike — burim: tabelat e README + shembulli numerik zyrtar)

| Fusha | Shkalla | Kuptimi | Provë nga shembulli zyrtar |
|---|---|---|---|
| `CouponItem.Price` | **×10000** (€0.0001) | çmimi njësi | "uje rugove" Price=15000 = €1.50 |
| `CouponItem.Total` | ~~×10000~~ **[SUPERSEDED — CURRENT DEFECT në kod]** → **×100 (cents)** sipas KT 25.4 + builders C#/Go @ pins | totali i rreshtit | **Golden i saktë:** €1.50 × 3 → `Price=15000`, `Total=450` (T2-01) |
| `Payment.Amount` | **×100** (cents) | pagesa | Cash 500 + Card 1000 + Voucher 320 = 1820 = €18.20 |
| `TaxGroup.TotalForTax` | **×100** (cents) | baza per normë | E: 889 = €8.89 (nga €10.50 gross me 18%) |
| `TaxGroup.TotalTax` | **×100** (cents) | TVSH per normë | E: 161 = €1.61 |
| `PosCoupon.Total` | **×100** (cents) | totali me TVSH | 1820 = €18.20 = Σ items €4.50+6.00+3.20+4.50 ✓ |
| `PosCoupon.TotalTax` | **×100** (cents) | TVSH totale | 185 = €1.85 (0+24+161) ✓ |
| `PosCoupon.TotalNoTax` | **×100** (cents) | pa TVSH | 1635 = 1820−185 ✓ |
| `PosCoupon.TotalDiscount` | **×100** (cents) | zbritja | 0 në shembull |
| `CitizenCoupon.Total/TotalTax/TotalNoTax` | **×100** (cents) | si te PosCoupon | duhen identikë me PosCoupon |

**Rregulli i kufirit (PoC-2 ↔ PoC-5):** kontabiliteti mbetet **NUMERIC** (PoC-2, i paprekur); konvertimi ×10000/×100 ndodh **VETËM në kufirin SEF** (`poc5-money.ts`), me teste shteruese në të dy drejtimet + teste që përzierja e shkallëve **dështon**.

**Rrumbullakosja e TaxGroup-eve (vëzhgim nga shembulli zyrtar — lidhet me Q#4b):** vlerat e grupit **E** (TotalForTax=889, TotalTax=161) dalin nga rrumbullakosja **PER-RRESHT** dhe pastaj mbledhje — jo per-grup: sendviq €6.00 gross → bazë 5.08 + TVSH 0.92; machiato €4.50 → 3.81 + 0.69; Σ = **8.89 + 1.61** ✓. Llogaritja per-GRUP (10.50/1.18 → 8.90 + 1.60/1.61) **s'e jep** dot çiftin 889/161. Pra shembulli zyrtar tregon **rounding per-rresht** → kjo ushqen rregullin e hapur Q#4b/ACC-003.

## 4. CouponType + rregulli ReferenceNo

- `Sale=1` — `ReferenceNo=0`.
- `Cancel=2` / `Return=3` — `ReferenceNo` = **CouponId i shitjes origjinale**, i DETYRUESHËM.
- Zyrtare (README): *"If the ReferenceNo is missing when the coupon type is RETURN or CANCEL the coupon will be rejected!"* → testohet të dyja anët (me → pranohet; pa → refuzim i pritur).

## 5. Tax groups (harta provisional)

| SEF | % | Harta jonë (provisional — ACC-003 open) |
|---|---|---|
| `A` | 0% (exempt) | `TL → A` **(provisional — konfirmim kontabilist/ATK)** |
| `C` | 0% | `T0 → C` |
| `D` | 8% | `T8 → D` |
| `E` | 18% | `T18 → E` |

## 6. Nënshkrimi + QR (nga pos-csharp `Signer.cs`/README)

1. Serializo proto → bytes. 2. `base64Proto = Base64(bytes)`. 3. **Firma = ECDSA P-256 mbi SHA-256(UTF8(base64Proto))** → `base64Sig`. 4. **QR = `base64(CitizenCoupon proto)|base64(firma e CitizenCoupon)`** — **[korrigjuar 12.07: QR ndërtohet/firmoset mbi `CitizenCoupon`, JO PosCoupon** — implementimi aktual firmos PosCoupon = CONTRADICTS G04/T2-02**]**. Për `/pos/coupon`: `details=base64Proto(PosCoupon)`, `signature=base64Sig(PosCoupon)`; fushat e përbashkëta PosCoupon↔CitizenCoupon duhet të rakordojnë.

**KODIMI I FIRMËS — EKSPLICIT (amendament pas review-t PM):** referenca `pos-csharp` = **.NET**, ku `ECDsa.SignData` prodhon **raw r||s = IEEE P1363** (EKZAKT **64 byte** për P-256); **Node default = DER** (ASN.1, ~70-72 byte) → pa `dsaEncoding` eksplicit firmat NUK përputhen. **Kodimi praktik për PoC = `ieee-p1363`** (default-i ynë eksplicit; DER mbahet për debug). **[Korrigjim 12.07: C# zyrtar = P1363, por Go zyrtar = ASN.1 DER → shembujt zyrtarë KONFLIKTOJNË (CONF-02); P1363 lokal = KANDIDAT, JO kontratë finale — encoding bëhet explicit/configurable (T2-07) dhe konfirmohet nga serveri.]**
> **Playbook i debug-ut në Tier 2** (nëse kuponi i parë real → *FAILED VERIFICATION*):
> **#1 = formati i firmës (P1363 vs DER)** — provo alternativën PARA çdo gjëje tjetër.
> **#2 = ÇFARË nënshkruhet:** `SHA256(UTF8(base64Proto))` (rruga jonë, nga pos-csharp) vs **raw proto bytes** — provo edhe këtë axis nëse swap-i P1363↔DER s'e rregullon.

## 7. ZGJEDHJA E DEP-it PROTOBUF (saktësisht NJË)

**Vendimi: `protobufjs` (i pinuar ekzakt në instalim — versioni i fundit stabil 7.x; regjistrohet në PR body).**

| Kriter | `protobufjs` ✅ | `@bufbuild/protobuf` |
|---|---|---|
| Nr. i paketave | **1 dep runtime**; ngarkon `models.proto` NË RUNTIME (file zyrtar i vendor-uar verbatim → drift i kontrollueshëm me checksum kundër upstream) | Runtime + **codegen toolchain** (`@bufbuild/buf`, `protoc-gen-es`) → 2-3 paketa + hap build |
| Përshtatja me kufirin "SAKTËSISHT NJË dependency" | ✅ | ❌ (idiomatikisht kërkon codegen) |
| int64/uint64 | Long (i brendshëm i paketës); vlerat tona hyjnë si string/Long — pa `number` për para | bigint native (avantazh, por s'e justifikon toolchain-in shtesë për PoC) |
| Rreziku i encoding-ut | Proto3 s'garanton bytes kanonikë NDËRMJET librarive — **irelevante këtu**: NE i prodhojmë bytes dhe firmosim PIKËRISHT ata bytes që dërgojmë/fusim në QR | njësoj |

Verifikimi anti-*FAILED VERIFICATION*: (a) round-trip encode/decode lokal kundër fushave/numrave të §2; (b) krahasim me vlerat e shembullit zyrtar; (c) prova finale = pranimi nga test-serveri (Tier 2).

## 8. Mospërputhje TË BRENDSHME të burimit (të shënuara; zgjidhen nga proto+tabela+shembull — JO kontradikta me SSOT)

1. **`pos-golang/readme.md` (rr. ~225-228):** proza te përshkrimi i PosCoupon thotë *"Price … value is in cents"* për items — **bie ndesh me tabelën e VET** (items ×10000), me `pos-csharp`, me shembullin numerik dhe me SSOT. Precedent i njohur (SSOT v2.5: "README prose ishte gabim" për CouponType). **Fiton: tabela + shembulli + proto.**
2. **`pos-csharp/readme.md` (rr. 187/275):** *"When the coupon type is SALE the ReferenceNo must be…"* — typo e qartë; konteksti (titulli RETURN, rr. 204/330) tregon **RETURN**.

## 9. Plani i Fazës B (PËR REVIEW — s'ka kod ende)

Migrimi **0006** vetëm `poc5_`: `poc5_fiscal_outbox` (business_id NOT NULL; `coupon_uuid` UNIQUE per biz; `coupon_type`; `payload_ref`; **status** CHECK sipas SSOT §7: `leshuar_offline / ne_pritje / fiskalizuar / deshtuar / kerkon_nderhyrje`; `attempts`, `next_retry_at`, `deadline_at = created+48h` **[SUPERSEDED 12.07: afati normativ NIS nga NDËRPRERJA e lidhjes (UA 44), jo per-kupon nga krijimi — implementimi aktual = jo-konform (T2-08); `created_at` mbetet immutable; modeli i outage-event = PROPOSED/ADR input]**, `transaction_id`, `fiscalized_at`) + `poc5_fiscal_alert`. Module: `poc5-money` (konvertimet per-field + teste kufiri) · `poc5-proto` (encode PosCoupon/CitizenCoupon të përputhur) · `poc5-sign` (P-256 `node:crypto`, sign+verify lokal, QR) · `poc5-outbox` (**radhë e IZOLUAR** nga sync — SSOT §7; retry/backoff; idempotencë; alarm 48h me fake clock, warning 40h; `kerkon_nderhyrje` në cert/sign failure) · `poc5-transport` (**MockAtkServer** për CI — simulon edhe FAILED VERIFICATION + refuzimin pa ReferenceNo; **LiveAtkTransport** me opt-in të dyfishtë `RUN_SEF_LIVE=1 ∧ SEF_ENV=test ∧ URL=test-server` + redaction rregulli E). Teste TIER 1 CI-safe (çelësa efemeralë; mock; **testi i izolimit dy-drejtimësh me radhën e PoC-3**) · TIER 2 LIVE vetëm lokalisht pas AskUserQuestion + "done" nga Blendi (rregulli C). Reliability matrix: #2 = bërthama; #3/#4 N/A §5.1 (PoC-4b/F7 · PoC-3) me tekst EKZAKT për postim nga Codex.

## 10. Paqartësi për review (do konfirmohen në Tier 2, s'bllokojnë Fazën B)

- `CouponItem.Type="TT"` — semantikë e padokumentuar (ndiqet shembulli zyrtar).
- `CouponItem.Quantity` është `float` në proto (jo para — sasi; paraja mbetet i64 e shkallëzuar; shënohet si vëzhgim, qty catch-weight → kujdes në F5).
- Sjellja REALE e serverit ndaj dërgimit të dyfishtë të të njëjtit kupon (`CouponId` i njëjtë ×2) — e padokumentuar; **dokumentohet empirikisht në Tier 2** (modeli #5b).
- `VerificationNo` (string 16-shifror në shembuj) — burimi/gjenerimi i vlerës reale sqarohet në onboarding Tier 2.

## 11. Pyetje për test-serverin (Tier 2) — nga auditimi ATK nen-për-nen (v2.22)

**HAPI I RI I PLAYBOOK-ut — PREFLIGHT:** para ÇDO dërgimi kuponi real, ekzekuto **VETËM `POST /ca/verify/{nui}`** me env-in real — konfirmon lidhjen, identifikuesit dhe statusin e regjistrimit **pa prodhuar asnjë kupon**. Vetëm pas PREFLIGHT OK vazhdohet me CSR/signcsr dhe `/pos/coupon`.

**PRIVATËSIA E PREFLIGHT-it (e detyrueshme):** ekzekutohet përmes një **wrapper-i lokal** që e lexon `.env.sef.local` **pa echo** (asnjë `cat`/`printenv`/interpolim në komandë). **Asnjë NUI/ID/URL e plotë** nuk shfaqet në output komande, shell history, PR, CI apo evidencë — output-i i lejuar është vetëm **`SET`/`MISSING` per variabël + vlera të maskuara** (p.sh. 2 karakteret e para + "…"). Përgjigja raw e serverit → **vetëm** `evidence-private-local/`/`.sef-local/`. **Tier 2 mbetet i NDALUAR** deri në aprovimin e veçantë të Blendit — PREFLIGHT-i vetë bën pjesë në Tier 2 dhe s'ekzekutohet para atij aprovimi.

Pyetjet (burimi: `docs/canonical/05-sef-atk/Kupon_Print_Spec.md` §9 + Tracker `ATK-005`):
1. **NUIKF** — burimi/formati (kush e gjeneron; lidhja me `VerificationNo`).
2. **Return** — shenja e shumave (pozitive me Type vs negative).
3. **Zbritja per-artikull** në PosCoupon (rresht negativ vs çmim neto + `TotalDiscount`).
4. **QR i kuponit të pafiskalizuar** (offline, para `transaction_id`) — përmbajtja.
5. **Semantika e `Time`** në fiskalizim të vonuar (koha e lëshimit vs e dërgimit).
6. **53 kategoritë ↔ `CouponItem.Type`** (a është 'TT' kod nga tabela).
7. **Formati i paracaktuar i printimit** (UA N32.2.6).
8. **X/Z raporti ditor** për SEF softuerik.
9. **Refund payout** — rrjedha fiskale e pagesës negative.
10. **Kupon→faturë tatimore B2B** (UA N5.3).
11. **Kodi PM i dublikuar** në tabelën zyrtare të kategorive (#8 vs #36).
12. **Furnizim falas / total 0** — a e pranon serveri (UA N5.2).
13. **Dublimi i `CouponId`** — sjellja reale (zëvendëson supozimin idempotent të mock-ut).

---
*FAZA A përfundon këtu. Asnjë kod SEF s'shkruhet pa: review të këtij dokumenti nga Codex/PM + aprovimin e Blendit (HARD STOP — rregulli A). §11 u shtua pas Fazës B si input i Tier 2 (docs-only).*
