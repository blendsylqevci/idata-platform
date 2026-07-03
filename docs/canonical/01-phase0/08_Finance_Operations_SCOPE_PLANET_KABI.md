# Finance Operations — SCOPE (PLANET & KABI)

**Versioni:** 2.0 (i bashkuar) · **Data:** 29 Qershor 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.10
**Canonical:** ky doc bashkon `08` (v1.0) + **Codex `FINANCE_OPERATIONS_Scope_Separation_v1`** + **`FINANCE_OPERATIONS_Backup_Coverage_Audit`**. Codex docs = input/evidence; ky = burimi i vetëm canonical.
**Pse ekziston:** te PLANET, **financa operative ≠ kontabiliteti** — dy role të ndara. Po t'i ngjeshim në një ekran, ngatërrohen rolet dhe UI/UX-i del gabim.

---

## 1. Dy role të ndara
| Aspekti | **Financë Operative** (vajza/menaxhim/owner) | **Kontabiliteti** |
|---|---|---|
| Fokusi | cash, POS/kartë, barazime, shpenzime, tavolina/kamarierë, furnitorë (pagesa), raporte ditore | GL, TVSH, Libri Shitjes/Blerjes, deklarata, mbyllje periudhe, pasqyra |
| Koha | **në kohë reale / ditore** | periodike (mujore / mbyllje) |
| Niveli | **para-GL (operacional)** | **GL / ligjor** |
| Korrigjime | propozon/kryen ops me audit; **s'mbyll periudhë** | mbyll periudhë, postim final, reversal |

**Parimi:** Finance Operations **krijon evidence operative**; Accounting e **poston/mbyll ligjërisht**. **Handoff:** kamarieri mbyll tavolinën → Finance e sheh në barazim → Accounting e merr si dokument të postuar/raportuar. Shpenzimi: Finance "**Regjistro shpenzim**" → Accounting "**posto/klasifiko**". **Lidhen, por NUK janë i njëjti ekran.**

## 2. Rolet
| Rol | Sheh/bën | NUK bën |
|---|---|---|
| **Financë Operative / Vajza** | cash office, barazime, POS terminale, shpenzime, dorëzim cash, raporte ditore | s'hap/mbyll periudhë kontabël |
| **Finance Manager / Owner (Donati)** | aprovon diferenca, raport grupi, merr para me arsye, audit | s'ndryshon dokumente fiskale pa correction workflow |
| **Kamarier** | tavolinat/porositë e veta, pagesë/mbyllje sipas lejeve | s'bën zbritje/anulim/transfer pa aprovim |
| **Arkatar / Shank** | pagesa, cash drawer, shank/banak, dorëzim | s'poston GL |
| **Kontabilist** | GL, libra ATK, TVSH, AR/AP, mbyllje periudhe | s'bën barazim operativ pa evidence nga financa |
| **Tenant Admin** | konfigurime, role, lokacione, payment methods | s'ka qasje të pakufizuar pa audit/break-glass |

## 3. Ekranet
**PLANET:** 1) **Finance Daily Dashboard** (cash/card/bank, tavolina hapur/mbyllur, barazime pending, diferenca) · 2) **Cash Office/Zyra e Parave** (hyrje/dalje, kush mori para+arsye+aprovim) · 3) **Waiter Settlement / Barazimi i Kamarierit** (tavolinat→porositë→cash/card, zbritje/anulime, saldo/float, cash që duhet dorëzuar) · 4) **Shank/Banak Settlement** (banak i brendshëm/jashtëm, printer/POS/lokacion) · 5) **POS Terminal Reconciliation** (terminal bankar, batch/settlement banke, pagesa↔fatura/tavolina) · 6) **Expenses/Cash Out** (foto/faturë, kategori, aprovim, lidhje me kontabilitet) · 7) **Supplier Payments** (borxhe, pagesa pjesore/të plota) · 8) **Bank Deposit/Cash Transfer** · 9) **Audit & "What happened?"**.
**KABI:** 1) **Driver/Van Settlement** (ngarkim→shitje→kthime→cash→kredi→mbetje→diferenca) · 2) **Farmer Advances/Avanse** (zbritje nga kartela mujore) · 3) **Cash Collection from Field** · 4) **Mobile POS/Card reconciliation** (offline queue, fiscal status) · 5) **Daily Factory Cash Office**.

## 4. Data model (finance operations)
`service_day` (dita operative; PLANET mbyllet 00:00) · `cash_register` (arkë/shank/banak/zyra/automjet) · `cash_shift` · `cash_count` (numërim fizik) · `worker_settlement` (kamarier/shofer/arkatar) · `settlement_line` (tavolinë/faturë/pagesë/shpenzim/diferencë) · `payment` (cash/card/bank/credit/advance) · `payment_method` · `payment_terminal` (POS-ID/bankë/lokacion) · `cash_movement` (handover/deposit/transfer) · `expense` (shpenzim arke + kategori/foto/aprovim) · `supplier_payment` (pjesore/totale) · `manager_approval` (PIN/aprovim) · `audit_log` (append-only). *(Sinkronizuar me `04_Data_Model_Draft`.)*

## 5. Integrimi me kontabilitetin
```
tavolinë/faturë/pagesë/shpenzim/barazim → finance event + audit + attachment → accounting posting queue → GL / AR / AP / TVSH / cash-bank
```
Kontabilisti **nuk ribën ditën manualisht** — kontrollon raportin, sheh diferencat, poston/mbyll periudhën, nxjerr raportet zyrtare.

## 6. Evidence nga backup-i (Backup Coverage Audit, 29.06)
**104 objekte finance të mapuara: 43 covered_raw_data · 57 schema-only/zero-row · 4 supplementary.** A/B gaps = **0** (s'ka mungesë kritike).
- **Të mbuluara mirë (raw):** `tblDocPagesatMaster/Detalet` (PLANET2 ~218K), `tblDocEkstrakti*` (bankë/reconciliation), `tblMenyraPagesa`, `tblBankat`, `tblDocPOSPagesat` (**RESORT ~429,654**), `tblDocPOSShitjet*`, `tblDocBlerjet*`.
- **⚠ Barazim = process-gap:** `tblDocPOSBarazimiMaster/Detalet` = **zero-row** → **mos e infero barazimin nga backup-i; dizajno modern + valido live me vajzën/Donatin.**
- **RESORT:** `PRODATA_PLANET_RESORT` është DB reale (POS aktiv, ~429K pagesa) ≠ `PRODATA_PLANET2` (back-office/pagesa ~218K). → ndriçon SSOT pyetjen #12.
- **3 micro-extract opsionale (jo bllokues, "zero doubt"):** `tblDocBlerjetShpenzimetTjera` (4 rreshta → `expense` parity), `tblDocPagesatAvanset` (1 → semantika e avanseve), `tblUserMenyratPagesa` (2102/4423 → leje user-payment-method; ose modelo RBAC fresh). **Bën Codex.**
- Procedura referencë: bank reconciliation (`prc*Ekstrakti*`/`prc*Bankat*`), payments (`prc*Pagesa*`) — REF, jo kopjim.
- **DDL/DML cross-check (29.06): 0 tabela me DDL të munguar; 3382 referenca (READ 3182/DELETE 256/INSERT 252/UPDATE 222); 4 micro-extract të nxjerra+sanitizuara → finance evidence i mbyllur pa dyshim.** Sinjale: pagesë→GL (ekstrakte bankare), **recycle-bin/restore**, `_DEL` audit. Flukse + pyetjet live → `09_Finance_Operations_Workflow_Spec`.

## 7. Vendime: të mbyllura vs të hapura
**TASHMË E VENDOSUR (mos i rihap):** barazim auto **00:00**; ekran me **saldo per kamarier**; **Donati merr cash → dokumentohet kush+arsye**; **identifikim banke/POS**; **anulim/zbritje me PIN+audit**.
**ENDE E HAPUR (→ SSOT pyetja #13, konfirmo me PLANET):** roli i saktë i vajzës; a hap arkën në mëngjes / jep saldo fillestare; kush e pranon cash-in (vajza vs menaxher); **tips/bakshish**; sa POS terminale e ku; finance **korrigjon vetë apo propozon**; barazim per-ndërrim brenda ditës apo vetëm 00:00; depozitim cash në bankë; format i raportit ditor i Donatit; banak i jashtëm aktivizim/çaktivizim.

## 8. Gatishmëria
- PLANET finance operations: **65–70%**; evidence pagesa/POS/banka: **~75%**; barazim modern: **45–55%** (net-new); KABI finance ops: **55–60%**.

## 9. Lidhjet
SSOT §6 (PLANET) + §9 (RBAC: Financë Operative) · `02_RBAC_Matrix` · `03_Accounting_Compatibility_Matrix` (ana GL) · `04_Data_Model_Draft` · `07_PRODATA_Evidence_Closure_Parity_Matrix` (evidence). Vendndodhja repo (kur të krijohet): `canonical/finance-operations/`.
