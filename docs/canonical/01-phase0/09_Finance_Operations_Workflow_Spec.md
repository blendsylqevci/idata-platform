# Finance Operations — Workflow Spec (draft) + PLANET Live-Questions Pack

**Versioni:** 1.0 (draft) · **Data:** 29 Qershor 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.11 · scope: `08_Finance_Operations_SCOPE`
**Statusi:** flukset janë specifikuar aty ku janë **të vendosura**; pjesët e procesit që presin PLANET janë shënuar **[LIVE-Q]** dhe mblidhen te §8 (paketa e pyetjeve). Ky doc bëhet final pas takimit me ekipin e financave të PLANET-it.
**Parim:** Finance Operations = **para-GL** (evidence operative); handoff te Accounting përmes posting-queue. Asnjë fshirje — anulim/korrigjim me PIN + audit.

---

## 1. Cash Office / Zyra e Parave
**Fluksi:** hap ditën operative → regjistro float hyrës/dalës per arkë → kur merret cash (p.sh. Donati) regjistro **kush + shuma + arsye + aprovim** → pozicioni i cash-it në fund të ditës.
**Fusha:** register, type(in/out), amount, by_user, reason, approval, at. **Statuset:** open → closed.
**Vendosur:** Donati merr cash → **dokumentohet kush+arsye**. **[LIVE-Q]** kush e hap ditën; float fillestar per arkë.

## 2. Waiter / Cashier Settlement (Barazimi)
**Fluksi:** mbyllje **auto 00:00** → per kamarier/arkatar: **tavolinat → porositë → pagesat (cash/kartë/bankë) → zbritje/anulime → saldo/float → cash që duhet dorëzuar → shpenzime ndërrimi → diferenca** → krijon `worker_settlement` + handover.
**Fusha:** waiter, service_day, tables[], orders[], cash, card, bank, discounts, voids, float, cash_due, expenses, diff. **Statuset:** open → reconciled → handed-over.
**Vendosur:** auto 00:00; **saldo per kamarier**; tavolinat e hapura → kamarieri i ri. **[LIVE-Q]** barazim per-ndërrim brenda ditës apo vetëm 00:00; kush e pranon cash-in (vajza vs menaxher); finance **korrigjon vetë apo propozon**; tips/bakshish.

## 3. POS Terminal Reconciliation
**Fluksi:** pagesat me kartë grupohen sipas **terminalit/bankës** → përputhen me **batch/settlement** e bankës → flag për të pa-barazuara.
**Fusha:** payment_terminal, bank, batch_id, amount, matched(bool). **[LIVE-Q]** sa terminale dhe cilit shank/lokacion/bankë i takojnë.

## 4. Expenses / Cash Out
**Fluksi:** "**Regjistro shpenzim**" (shumë/kategori/kush/arsye/foto/aprovim) → pending → approved → **postohet në GL nga Accounting**.
**Fusha:** amount, category, by_user, reason, attachment, approval_status, gl_posted. **Statuset:** draft → approved → posted. **[LIVE-Q]** cilat shpenzime nga arka; kush i aprovon.

## 5. Supplier Payments (ops view)
**Fluksi:** listë fatura **pa-paguara/pjesërisht** → **paguaj një ose disa bashkë** (cash/bankë) → regjistro kush pagoi + foto/PDF → përditëson AP.
**Fusha:** supplier, invoice, paid, balance, method, by_user, attachment. **[LIVE-Q]** burimi i pagesës (arkë/bankë/borxh).

## 6. Bank Deposit / Cash Handover
**Fluksi:** cash nga zyra → bankë (depozitim) ose transfer mes arkash; gjurmë e plotë (kush/sa/kur).
**Fusha:** `cash_movement` (type, from, to, amount, by_user, at, reason). **[LIVE-Q]** kush bën depozitimin; ritmi.

## 7. Daily Handover → Accounting
**Fluksi:** mbyllja e ditës financiare → përmbledhje (settlement + shpenzime + cash/kartë/bankë) → **posting queue** → kontabilisti shqyrton diferencat, poston, mbyll periudhën. **Kufiri:** finance s'mbyll periudhë; accounting s'bën barazim operativ.

**KABI variant:** Driver/Van Settlement (ngarkim→shitje→kthime→cash→kredi→mbetje→diferenca) · Farmer Advances (zbritje nga kartela mujore) · Cash Collection from Field · Mobile POS/Card recon (offline+fiscal) · Daily Factory Cash Office. **[LIVE-Q]** validohet me KABI.

## 8. 🔴 PLANET Live-Questions Pack (për vajzën e financave / menaxherin / Donatin)
*(Paketë e konsoliduar — SSOT #13 + Codex + 08 §7; merre në takim. Pa këto, spec-i s'mbyllet.)*
1. Kush e **hap** dhe kush e **mbyll** ditën operative?
2. A ka **saldo/float** për kamarierët? Sa dhe si regjistrohet?
3. Kush e **pranon cash-in** nga kamarierët dhe kur (vajza apo menaxheri)?
4. Barazimi: vetëm **00:00** apo edhe **per-ndërrim** brenda ditës?
5. Sa **POS terminale bankare** ka dhe cilit shank/lokacion/bankë i takojnë?
6. **Tips/bakshish** — regjistrohen? Ku shkojnë?
7. Cilat **shpenzime** paguhen nga arka dhe **kush i aprovon**?
8. Kush mund të **korrigjojë barazimin** pas mbylljes — vajza vetë apo vetëm propozon?
9. Çfarë **raporti ditor** e do Donati (cash/kartë/shpenzime/diferenca/tavolina/kamarierë)?
10. Si bëhet **depozitimi i cash-it në bankë** (kush/ritmi)?
11. Pagesat e furnitorëve: nga **arka, banka apo borxh**?
12. A ka **banak të jashtëm** që aktivizohet/çaktivizohet sipas sezonit/eventit?
13. (KABI) si barazohet **shoferi/ambulanti**; avanset e fermerëve.

## 9. Baza e evidencës
DDL/DML cross-check: **0 tabela me DDL të munguar**; 3382 referenca (READ 3182 / DELETE 256 / INSERT 252 / UPDATE 222); **4 micro-extract të nxjerra+sanitizuara** → finance evidence i mbyllur. **Sinjale dizajni nga PRO DATA:** pagesë→GL përmes ekstrakteve bankare (`prcREGDocPagesaEktraktet`), pattern **recycle-bin/restore** (`prcRecycledDocumentsRestore`), `_DEL` audit. **Barazim (`tblDocPOSBarazimi*`) = zero-row → konfirmim live** (jo nga backup). Detajet: `reference_atk/FINANCE_OPERATIONS_*`.

## 10. Lidhjet
`08_Finance_Operations_SCOPE` (scope/role/data-model) · `07_PRODATA_Evidence_Closure_Parity_Matrix` · `02_RBAC_Matrix` (FO) · `03_Accounting_Compatibility_Matrix` (ana GL) · `04_Data_Model_Draft`. Final pas §8.
