# Feature Status Register (iData+)

**Versioni:** 1.6 · **Data:** 5 Shtator 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.40 (+ ADR-030 ACCEPTED / EFFECTIVE)
**Parim:** **MVP = bllokues për go-live.** **AI/premium/advanced NUK janë MVP.**
**[HISTORICAL / SUPERSEDED nga ADR-030, 02.09.2026]** *KABI del live i pari → "KABI MVP" + "Shared MVP" janë bllokuese për KABI live. PLANET vjen pas → "PLANET MVP" s'bllokon KABI live.*
**CURRENT — PLANET-FIRST (ADR-030 ACCEPTED / EFFECTIVE (03.09.2026: Fable → Codex → HG-PR31-MERGE → squash-merge PR #31 `42993f0`)):** PLANET është rollout-i i parë operacional — shared foundation + PLANET operational-ready deri më **2026-12-31** (`OPERATIONAL_READY_FOR_CONTROLLED_PILOT`), **JANUARY 2027 CONTROLLED PILOT** me sistemin ekzistues si source-of-record/fallback, source-of-record cutover = **TBD — HUMAN GO/NO-GO AFTER JANUARY PILOT EVIDENCE**; **KABI pas pranimit/stabilizimit të PLANET-it** (pa datë të sajuar). Klasifikimi i bucket-eve për rollout-in e parë: **Shared MVP** = `PLANET_PILOT_BLOCKER` (vetëm slice-i që kërkon PLANET; shared core product-neutral) · **Accounting MVP** = slice-i që kërkon PLANET = `PLANET_PILOT_BLOCKER`, pjesa tjetër = `KABI_LIVE_BLOCKER` · **KABI MVP** = `KABI_LIVE_BLOCKER` (**nuk bllokon pilotin e parë PLANET**) · **KABI Advanced** = `NON_BLOCKER_FIRST_ROLLOUT` · **PLANET MVP** = `PLANET_PILOT_BLOCKER` · **PLANET Later / Roadmap** = `NON_BLOCKER_FIRST_ROLLOUT`. Bucket-et dhe F1–F10 **nuk rinumërohen**; kjo është klasifikim rollout-i, jo ndryshim i statusit substantiv të asnjë rreshti.
**Buckets:** Shared MVP · Accounting MVP · KABI MVP · KABI Advanced · PLANET MVP · PLANET Later · Roadmap · + tags (Req-accountant / Req-ATK / Req-factory).

---

## 🟦 Shared MVP (bllokues për KABI live · **rollout i parë: `PLANET_PILOT_BLOCKER` për slice-in që kërkon PLANET**)
- Multi-tenant + `business_id` isolation
- Auth + 2FA + RBAC per-feature
- Roles: Platform Owner / Group Owner / Tenant-Admin + iCode Support break-glass
- Audit log append-only
- Hub lokal (Mini-PC+UPS, Docker+Postgres) + offline-first sync + idempotencë
- Rregullat e konfliktit (stok/çmim/kredi)
- Hosting EU + on-prem; secrets management; branch protection/2FA; PITR/restore drill
- ARBK auto-import (onboarding)

## 🧮 Accounting MVP (bllokues për KABI live · **rollout i parë: `PLANET_PILOT_BLOCKER` vetëm për slice-in që kërkon PLANET**) — `Req-accountant`
- Plan kontabël + motori i postimit (GL, debit=kredit)
- Libri i Blerjes/Shitjes 1:1 ATK + Deklaratë TVSH automatike
- Arka/Banka rakordim · AR/AP + aging
- COGS (mesatare e ponderuar + batch costing) — *finalizim me kontabilistin*
- Kartela analitike · pasqyrat financiare
- Normë e sheshtë fermeri [51] — `Req-accountant/ATK` (paraprak)
- Lista TVSH 8/18 per produkt — `Req-accountant`

## 🥛 KABI MVP (bllokues për KABI live = `KABI_LIVE_BLOCKER` · **nuk bllokon pilotin e parë PLANET**)
- Pranim qumështi + rrugët (fermer / punëtor / Punki)
- QC Pass/Fail (antibiotikë → bllokim) — siguri ushqimore minimum
- Punki settlement (komision %/per-litër)
- Shlyerje fermeri + formula USAID — `Phase0-evidence-validated` (Maj 2026 workbook; all active rows) + `Req-factory` final business sign-off
- Tank pool + mesatare e ponderuar
- Batch/LOT + FEFO + afat skadence
- Recetura/BOM bazë + auto-zbritje stoku
- Pasterizim (regjistrim bazë)
- Catch-weight (djathi)
- Stok automjeti (van) + check-in/out + POD + **offline**
- Kontroll kredie në terren
- Gajbe/ambalazh te klienti + rakordim
- Shitje/faturim terren + **SEF**

## 🥛➕ KABI Advanced (pas KABI live · `NON_BLOCKER_FIRST_ROLLOUT`)
- Urdhër prodhimi i plotë + **pasterizim CCP gating** i plotë
- Yield plan-vs-real (Yield Variance Report)
- Recall traceability UI i plotë
- Humbje normale % + Scrap automatik
- By-products / Grade B menaxhim i plotë
- MTO/MTS i plotë
- Raft-control GPS + foto (merchandising)
- KPI dashboards (terren/prodhim)

## 🍽️ PLANET MVP (bllokues për PLANET go-live; jo për KABI live · **rollout i parë: `PLANET_PILOT_BLOCKER`**)
- POS porosi/tavolina + plan real + hot tables
- Totali i fshehur + lock + rihapje me aprovim
- Split / transfer / merge tavoline
- Zbritje me PIN / anulim me aprovim
- Faturë + dërgim email
- Ndërrim 00:00 + barazim puntori + ekran financash (saldo per kamarier)
- **Finance Operations** (rol Financë Operative, i ndarë nga kontabiliteti): arka/cash control + cash handover/deposit
- **Finance Operations:** barazim kamarierë/shank (cash/card/bank + diferenca)
- **Finance Operations:** shpenzime operative + pagesa furnitorësh (ops) + raporte menaxheriale ditore
- KDS per-stacion (~10–12 ekrane) — `Req-factory` (floor-plan)
- Pizzeria 2-ekrane (blink/red 2min) · Kitchen Queue Monitor
- Shank/bar 3 statuse + 2 shanka
- Backup printer termik (30s)
- Multi-depo/shank + recetura auto-zbritje
- Printer/location routing
- Raporte (datë/produkt/kamarier) · Realtime alarme (>30min)

## 🍽️➕ PLANET Later (pas PLANET go-live · `NON_BLOCKER_FIRST_ROLLOUT`)
- Premium: Universal Search · table Timeline · "What happened?" per faturë
- Smart Notifications · Daily Snapshot · owner Dashboard
- Dark/Light toggle
- Purchasing intelligence i plotë (histori çmimi per produkt)

## 🛣️ Roadmap (e ardhme, jo v1)
- Events / banketet (PLANET)
- AI Invoice Scanner + Smart Product Matching
- AI inactive-client alert
- Konsignacion klasik (s'përdoret tani → shitje me kthim)
- ARBK për biznese EU

## ⏸️ Later (Faza 2) — të vendosura
- **Asete fikse / amortizim**

> **RISHIKIM (v2.17, 05.07):** **Payroll + kontribute** (ADR-004) u zhvendos në **Faza 1** — KABI/kontabilisti e kërkojnë të detyrueshëm në fazën e parë (rikthen vendimin v2.2). *Scope i saktë (payroll engine i plotë vs master-data punonjës + kontribute) për t'u sqaruar.*

---

## Tags (cross-cutting)
- **`Req-accountant`** (para Accounting Core/Go-live, jo bllokues për scaffold): plan kontabël/GL, librat ATK, deklarata TVSH, COGS, normë e sheshtë, lista TVSH 8/18, pasqyrat.
- **`Req-ATK`**: SEF (i tërë) · printera fiskalë hardware-vs-software (#8) · normë e sheshtë [51]. *(SEF test phase AKTIV.)*
- **`Req-factory`**: tabela USAID · floor-plan PLANET (numri i ekraneve KDS) · inventar pajisjesh · numrat e fabrikës.
