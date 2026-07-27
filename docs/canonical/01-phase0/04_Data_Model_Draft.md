# Faza 0 — Data Model Draft (iData+)

**Versioni:** 2.2.6 · **Data:** 26 Korrik 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.17 §13 + `03-kabi/KABI_Operational_Evidence_Update_2026-07-05.md` + `03-kabi/Accountant_Confirmation_TVSH_e_Sheshte_Fermere_KABI_2026-07-05.md`
**Statusi:** draft i fortë (jo DDL final) — pas tri raundeve review kundërshtues të Codex (v2.1 + v2.2 + v2.2.1). Emrat/vlerat reale mbeten private-local.
**Rishikime v2.0:** posting infra; `tax_book_entry`→header+buckets; shlyerja KABI; payroll Faza 1; lifecycle; posting hooks; matricë gjurmueshmërie.
**Rishikime v2.1 (Codex review):** `business_id` rregull eksplicit; ndarë `farmer_settlement_line` vs `finance_settlement_line`; `farmer` = subtype i `partner` (partner_id kudo në AP/AR); autofaturë fermeri lidhet me `invoice` (jo rrugë paralele); flat-rate 8% posting = pending sign-off; shtuar `tank`/`tank_movement`/`milk_pool_contribution`, `pasteurization_log`, versionim recete, product master i plotë, van-sales i zgjeruar, returns lifecycle; sqaruar `lab_analysis` vs `production_quality_test`, `tax_code` × `tax_treatment`.
**Rishikime v2.2 (Codex micro-hardening):** `business_id` direkt në të gjitha child-tables (rule==model, pa konflikt); kthim pas-faturimit → `credit_note` E DETYRUESHME vs refuzim para-faturimit pa të; hequr % rabati real (→ configurable/private-local); `farmer.advance_balance` = DERIVED/cached; `article.recipe_id` → `default_recipe_id`; +4 rreshta test (lab-gate, draft-vs-approved AP, credit_note-required, business_id isolation).
**Rishikime v2.2.1 (Codex):** zgjeruar entitetet shorthand të financës në Section C (`bank_account`, `cash_register`, `payment_method`, `payment_terminal`, `cash_count`, `finance_settlement_line`, `supplier_payment`, `finance_day`, `float_assignment`, `pos_terminal_batch`, `finance_handover`) me `id, business_id, …` — asgjë e nënkuptuar; rule==model absolut.

**Rishikime v2.2.4 (F1.1 correction pass, 24.07.2026):** `role.business_id` = **NOT NULL** (tenant-scoped; globalet deferred); `role_permission` me `business_id` direkt + composite FK; `is_system` = tenant-scoped/system-managed dhe **nuk caktohet nga endpoint-i F1.1** (provisioning i parë = seed/migration-only; production provisioning OPEN); `business_membership` me `version` (CAS) dhe fushat reale të migrimit 0002; `audit_log` me `command_receipt_id` + `correlation_id`. Sinkron me F1.1/migrimin 0002 — JO DDL final i platformës.

**Rishikime v2.2.5 (F1.1 concurrency correction pass, 26.07.2026):** pa ndryshim skeme. Sqarohet se ndryshimi i `business_membership.role_id` kalon nga një **authority-lock protocol** (lock eksplicit i business/memberships/users/role/permission/role_permission sipas rendit kanonik, rivlerësim përfundimtar nën lock, pastaj UPDATE i guarduar me `status='active'` + `version`), sepse nën READ COMMITTED një `UPDATE ... WHERE EXISTS(...)` i vetëm mund të vlerësojë faktet e autorizimit mbi një snapshot të vjetruar. Unique index-i `(business_id, user_id)` garanton se `user_id` i një membership-i nuk mund të ripikëzohet te një user që ka tashmë membership në atë business.

**Rishikime v2.2.6 (F1.1 correction pass i kufizuar, 26.07.2026):** pa ndryshim skeme. Terminologjia "linearization point atomik" zëvendësohet me "faza transaksionale authority-lock/validation/mutation"; DB contention (deadlock/lock timeout/serialization/statement timeout) → 503, JO 409 CAS; humbësi i garës same-Idempotency-Key rikuperohet me rilexim deterministik të receipt-it.

## Konventa globale
- **Multi-tenant (RREGULL):** ÇDO tabelë tenant-scoped ka `business_id` **direkt** (FK→business) — e detyrueshme për tabelat operative/financiare/offline/audit/GL-posting (`journal_line`, `tax_book_bucket`, `payment_allocation`, `stock_balance`, `recipe_line`, `invoice_line`, `order_item`, `kitchen_ticket`, `fiscal_coupon`, `fiscal_outbox`, `payslip`, çdo `*_line`). Composite-FK parent-based lejohet **vetëm si vendim eksplicit i dokumentuar**, jo i nënkuptuar. Jo në tabela globale/reference. **v2.2: të gjitha child/leaf-et e listuara e mbajnë `business_id` DIREKT (denormalizuar për RLS/offline/partitioning) → rregulli == modeli, pa përjashtime të fshehura.** **F1.1 (ENG-003/PoC-1): composite FK `(business_id, role_id) → role(business_id, id)` te `business_membership`/`role_permission` — DB refuzon lidhje cross-tenant; production RLS NUK pretendohet.**
- **Çelësat:** `id` = UUID (gjenerohet edhe në pajisje për offline).
- **Para:** `numeric` për GL (precizion i lartë); **SEF money scale PER-FIELD (korrigjuar 12.07.2026 — Gate v2 §3):** `CouponItem.Price` = ×10000 (€0.0001); **`CouponItem.Total` = ×100 (cents)**; coupon `Total`/`Payment.Amount`/`TaxGroup.*`/`TotalTax`/`TotalNoTax`/`TotalDiscount` = cents (×100); golden: €1.50×3 → Price=15000, Total=450; decimal në TS; tabular-nums në UI. Kurrë float.
- **Audit redaction:** PII + sekretet SEF + çelësat **maskohen** në `audit_log`/eksporte.
- **SEF secrets:** private key, `fiscalization_no`, `application_id`, certifikata → **secrets store** (jo tabela të zakonshme/log); akses vetëm me break-glass.
- **Kohë:** `created_at`, `updated_at`, + `event_at` (koha reale e veprimit në terren) për offline.
- **Fshirje:** soft-deactivate (`is_active`) për master-data; dokumente financiare **kurrë s'fshihen** (vetëm reversal/anulim).
- **Audit:** çdo veprim kritik → `audit_log` (append-only).
- **LOT/FEFO:** atributet e gjurmës bartën në lëvizje stoku + rresht fature.

---

## A. Platform & Tenancy
- **business_group** — id, name, owner_user_id; lidh disa `business` për raportim të konsoliduar (Group Owner / Company Switcher).
- **business** — id, name, nui, type(KABI/PLANET/…), legal_unit, **group_id**(FK→business_group), region, settings.
- **business_unit** — id, business_id, name (p.sh. PLANET, PLANET2 = i njëjti business, dy units), location.
- **user** *(modeli logjik `user` → **tabela fizike `app_user`**; `user` është fjalë e rezervuar në Postgres)* — id, email, name, mfa_enabled, status.
- **role** — id, **business_id NOT NULL**, name (PO/GO/TA/KO/…), is_system. *(F1.1: rolet janë VETËM tenant-scoped; rolet globale/system-wide SHTYHEN. `is_system` = rol tenant-scoped i menaxhuar nga sistemi (JO rol global) dhe **NUK caktohet përmes endpoint-it F1.1** — provisioning-u i parë vjen vetëm nga seed/migration i kontrolluar; production provisioning = OPEN.)*
- **permission** — id, feature_key, action(R/C/U/X/P/A).
- **role_permission** — **business_id**, role_id, permission_id. *(F1.1: `business_id` DIREKT + composite FK `(business_id, role_id) → role(business_id, id)`.)*
- **business_membership** *(modeli logjik 'membership'; F1.1 tabela fizike `business_membership`)* — id, business_id, user_id, role_id, **version** (CAS optimistic concurrency), status, created_at. *(F1.1: `version` = concurrency token DB-backed; ndryshimi i rolit kalon nga faza transaksionale authority-lock/validation/mutation dhe aktori NUK ndryshon dot rolin e vet. Fusha `scope` (vetja/njësi/i tërë) SHTYHET për një increment të mëvonshëm — nuk është në migrimin 0002.)*
- **breakglass_grant** — id, user_id(iCode), approved_by(Group Owner), reason, expires_at, audit_ref.
- **audit_log** — id, business_id, user_id, entity, entity_id, action, before, after, **command_receipt_id** (FK→command_receipt, DB-enforced tenant-safe), **correlation_id** (server-generated), event_at. *(F1.1: append-only nën DML të zakonshëm me triggers BEFORE UPDATE/DELETE/TRUNCATE; JO tamper-proof ndaj owner/superuser — provisioning i rolit runtime production = OPEN.)*
- **command_receipt** *(F1.1, net-new)* — id, business_id, operation_key, idempotency_key, request_fingerprint, result_status, result_payload(typed/redacted), created_at, completed_at; UNIQUE (business_id, operation_key, idempotency_key). Kurrë payload i papërpunuar/PII/sekrete.

## B. Partnerë & Master Data
- **partner** — id, business_id, type(furnitor/blerës/agjent/**fermer**), name, nui, vat_no, credit_limit, terms. **Palë financiare bazë** — `open_item`, `partner_card_entry`, `payment`, `tax_book_entry` përdorin **`partner_id`** (jo `partner_id/farmer_id`).
- **farmer** (extension i `partner`) — id, business_id, **partner_id**(FK→partner), code, bank, **advance_balance (DERIVED/cached)**. *(Të dhënat financiare rrjedhin nga `partner`; ky mban vetëm specifikat e fermerit. `advance_balance` NUK është burim i së vërtetës — llogaritet nga `farmer_advance`/`open_item`/`partner_card_entry` dhe ruhet vetëm si cache, rifreskuar nga posting; për të shmangur drift.)*
- **collector (Punki)** — id, business_id, **partner_id**(FK→partner, role=agjent), commission_type(%/per_litre), commission_value.
- **article** — id, business_id, sku/code, **original_name**(nga KABI, pa "pastrim" auto), name, category_id, base_uom, **is_sold_by_weight**(catch-weight), **tax_code**, **shelf_life_days**, **print_label**(bool), **default_recipe_id?**(→`recipe`; recetat aktive vijnë nga `recipe.output_article_id`+effective dates — kjo vetëm default për UI, me constraint që s'lejon dy receta aktive njëkohësisht), status(**active/passive**), type(MTS/MTO).
- **uom / article_uom** — uom(copë/kg/litër/pako/gajbe/karton), factor→base_uom, barcode/EAN.
- **article_packaging** — id, business_id, article_id, packaging_type(gajbe/karton/pako), units_per_pack, is_returnable. *(Numri i njësive per gajbe/karton ndryshon per produkt — konfigurueshëm, jo fiks.)*
- **price** — id, business_id, article_id, uom_id, price (me/pa TVSH), valid_from; + **price_history**.
- **category**, **modifier** (medium/well-done/pa qepë…).
- **tax_code** — code(T0/T8/T18/TL) × **tax_treatment**(output_sale / purchase_input / **farmer_flat_rate_8** / exempt_no_vat); **↔ SEF: T0→C, T8→D, T18→E, TL→A**. *(Flat-rate fermeri NUK është thjesht tax_code produkti — është trajtim i veçantë blerjeje; shih `farmer_purchase_document`.)*
- **supplier_article_map** (smart matching) — id, business_id, supplier_id, supplier_name, article_id.

## C. Kontabiliteti
- **account** — id, business_id, code, name, class(1/2/4/6-7/8/9), parent_id.
- **journal_entry** — id, business_id, date, source_doc, status; **journal_line** — id, business_id, entry_id, account_id, debit, credit, partner_id?, cost_center?.
- **tax_book_entry** (HEADER — një per faturë/dokument) — id, business_id, book(shitje/blerje), period(→fiscal_period), partner_id, invoice_id, doc_type, date, total_base, total_vat, status. *(Ndarë nga buckets sepse një faturë mund të ketë 18%+8% njëkohësisht — konfirmuar nga kontabilisti.)*
- **tax_book_bucket** (rreshta per header) — id, business_id, tax_book_entry_id, declaration_box([12]/[43]/[45]/[51]/[67]…), rate(0/8/18/flat-8), base, vat.
- **posting_rule** — id, business_id, event_type(sale/purchase/farmer_settlement/return/payment/payroll/expense…), effective_from/to, debit_account_id, credit_account_id, tax_treatment, conditions(jsonb). **Parametra sipas periudhës, jo hardcode.**
- **posting_event** — id, business_id, source_entity, source_id, event_type, occurred_at, posting_run_id?, status(pending/posted/failed/reversed), **idempotency_key** (një burim → një postim).
- **posting_run** — id, business_id, period(→fiscal_period), type(monthly/settlement/adhoc), status(draft/approved/locked/posted), approved_by, locked_at. Grupon `posting_event`→`journal_entry`. **Draft NUK poston GL/AP; vetëm approved/locked.**
- **open_item** — id, business_id, **partner_id**, doc_ref, amount, balance, due_date, status(open/partial/closed). AP/AR: detyrime fermeri/furnitori, kërkesa klienti; matching me `payment`. *(Fermeri = `partner` subtype → një rrjedhë AP, jo `farmer_id` paralel.)*
- **fiscal_period** — id, business_id, period(YYYY-MM), status(open/closing/closed/locked). **Asnjë postim në periudhë të mbyllur pa rihapje të aprovuar.**
- **partner_card_entry** (kartelë analitike) — id, business_id, **partner_id**, doc_ref, date, debit, credit, running_balance, type(intake/settlement/advance/penalty/payment/return/invoice).
- **vat_declaration** — id, business_id, period, boxes(jsonb), status (deklarata e punës/aktive).
- **vat_declaration_snapshot** — id, business_id, period, boxes(jsonb), generated_at, source_run_id, locked(bool). **Snapshot i pandryshueshëm** për auditim + carry-forward.
- **payment** — id, business_id, method(cash/card/bank), amount, partner_id, pos_terminal?, bank_account?; **payment_allocation** — id, business_id, payment_id, invoice_id, amount.
- **bank_account** — id, business_id, name, iban?, currency, opening_balance, current_balance. *(saldo, rakordim)*
- **cash_register** — id, business_id, location_id?, name, opening_balance, current_balance, status(open/closed). *(saldo, rakordim)*
- **expense** — id, business_id, amount, category, by_user, reason, register/location, date, approval_status, gl_posted(bool); **regjistrohet nga Financë Operative → postohet nga Accounting**.
- **cash_movement** — id, business_id, type(handover/deposit/transfer), from(register), to(office/bank), amount, by_user, at, reason. *(Finance Operations — shih `08_Finance_Operations_SCOPE`.)*
- **payment_method** — id, business_id, code(cash/kartë/bankë/POS/borxh/avans), is_active.
- **payment_terminal** — id, business_id, pos_id, bank, location_id.
- **cash_count** — id, business_id, cash_register_id, counted_amount, expected_amount, variance, counted_by, at. *(numërim fizik arke)*
- **finance_settlement_line** — id, business_id, finance_day_id?, ref_type(tavolinë/faturë/pagesë/shpenzim/diferencë), ref_id, amount. *(barazim finance; i ndarë nga `farmer_settlement_line`)*
- **supplier_payment** — id, business_id, partner_id(furnitor), amount, type(pjesore/totale), method, date, invoice_id?.
- **finance_day** — id, business_id, date, opened_by, closed_by?, status(open/closed). *(dita financiare; hap/mbyll)*
- **float_assignment** — id, business_id, finance_day_id, assigned_to(waiter/register), amount, returned_amount?, status. *(saldo/float dhënë kamarierit/arkës)*
- **pos_terminal_batch** — id, business_id, payment_terminal_id, batch_date, total_amount, settled(bool). *(batch/settlement banke per terminal)*
- **finance_handover** — id, business_id, finance_day_id, from_user, to_role(accounting), amount, at, status. *(dorëzimi finance→accounting; Codex FO spec; shih `09_Finance_Operations_Workflow_Spec`.)*

## D. Inventari
- **warehouse** — id, business_id, type(depo/restorant/shank/vehicle), name, is_external(shank i jashtëm).
- **stock_balance** — id, business_id, warehouse_id, article_id, lot_id?, qty, avg_cost.
- **stock_movement** — id, business_id, type(in/out/transfer/adjust/scrap), article_id, uom, qty, lot_id?, from_wh?, to_wh?, reason, event_at.
- **recipe (BOM)** — id, business_id, output_article_id, normal_loss_pct, **version**, **effective_from/to**, **approved_by**, **status**(draft/active/superseded), **locked**(bool). *(Normativi ndryshon në kohë → çdo prodhim lidhet me versionin e recetës që ishte aktive; nuk mbishkruhet.)* **recipe_line** — id, business_id, recipe_id, input_article_id, qty, uom.
- **packaging_stock (gajbe)** — id, business_id, partner_id/vehicle_id, qty_out, qty_returned, deposit_value.
- **inventory_cost_layer / stock_valuation** — id, business_id, article_id, warehouse_id, qty, unit_cost, method(weighted-avg/batch), valid_from → baza për **COGS** (mesatare e ponderuar + batch costing KABI).

## E. KABI — Prodhimi & Qumështi
- **milk_intake** — id, business_id, route(direct/internal/punki), farmer_id, collector_id?, litres, sample_id(→milk_sample), pool_batch_id?, operator_id, gps?, receipt_ref, status(draft/offline_recorded/synced/waiting_lab/accepted/rejected/transferred_to_tank), event_at. *(QC lëviz te `lab_analysis`; gjurmueshmëri per-fermer para përzierjes në tank — rajon/fermer/LOT/datë/sasi/analizë.)*
- **tank** — id, business_id, code, capacity_litres, type(raw/pasteurized/buffer), status(empty/filling/full/cleaning). *(Aseti fizik i tankut; pool-i mblidhet këtu.)*
- **tank_movement** — id, business_id, tank_id, type(in/out/transfer/cleaning), litres, source(milk_intake/tank/production), ref_id, event_at. **Balancë litri per tank; gjurmë hyrje/dalje.**
- **milk_pool_batch** — id, business_id, date, total_litres, weighted_quality, tank_id.
- **milk_pool_contribution** — id, business_id, pool_batch_id, **milk_intake_id**, farmer_id, litres, fat_pct, quality_class. **Ura e gjurmueshmërisë fermer→pranim→tank→pool→prodhim→LOT (recall).**
- **production_order** — id, business_id, pool_batch_id, output_article_id, planned_qty, technologist_id. *(Pasterizimi te `pasteurization_log`.)*
- **pasteurization_log** — id, business_id, production_order_id/tank_id, temp_c, hold_time, start_at, end_at, result(ok/fail), operator_id, ccp_flag. **CCP siguri ushqimore — fail bllokon vazhdimin.**
- **production_batch** — id, business_id, order_id, lot_id, produced_qty, yield_actual_vs_plan, expiry_date.
- **lot** — id, business_id, article_id, code, production_date, expiry_date.
- **production_quality_test** (test per batch prodhimi) — id, business_id, production_batch_id, params(jsonb), result. *(Cilësia e qumështit të fermerit = `lab_analysis` mbi `milk_sample`; ky është vetëm për batch-in e prodhuar — dy gjëra të ndryshme.)*

### E2. KABI — Mostra, laborator & shlyerja e fermerit *(evidence 05.07)*
- **milk_sample** — id, business_id, intake_id(1:1), barcode, taken_by, taken_at, gps?, status(waiting_lab/tested/accepted/rejected), chain_of_custody(jsonb). **Mostër e detyrueshme për çdo dorëzim.**
- **lab_analysis** — id, business_id, sample_id, nbc?, nqs?, fat_pct, snf, protein, density, acidity_ph, temperature, water_added_pct, antibiotics(neg/pos), analyzed_by, analyzed_at, class_proposed, class_final, override_reason. **Antibiotikë pozitiv → refuzim automatik. Lab-gate: pa analizë s'ka aprovim/shlyerje.**
- **quality_class** (reference, tenant-config) — code(E/I/II/III), nbc_max, nqs_max, multiplier_ref. *(Pragjet/shumëzuesit realë = private-local/tenant config, jo në git.)*
- **farmer_contract** — id, business_id, farmer_id, valid_from/to (**vjetore**), price_scheme(USAID formula ref), **nif**, **mafrd_certificate_no**, flat_rate_applicable(bool), status.
- **settlement_run** — id, business_id, period(→fiscal_period), status(draft/approved/locked/posted_to_accounting), approved_by, locked_at. **farmer_settlement_line** — id, business_id, run_id, **partner_id**(fermer), total_litres(←Σ `milk_intake`), quality_class, fat_pct, gross, deduction, net, params_ref(config i periudhës). **Decimal; draft s'poston GL/AP.** *(Emër i veçantë nga `finance_settlement_line`.)*
- **farmer_advance** — id, business_id, partner_id(fermer), amount, date, status, offset_farmer_settlement_line_id?. *(Avanse jepen dhe zbriten në shlyerje; inpute/hua JO — jashtë scope aktual.)*
- **farmer_adjustment** — id, business_id, partner_id(fermer), type(**penalty**/**bonus**), amount|pct, reason, farmer_settlement_line_id?. *(Penalty ekziston; bonus si opsion — konfirmuar KABI.)*
- **farmer_purchase_document (autofaturë)** — id, business_id, **invoice_id**(FK→`invoice` type=farmer_purchase), partner_id(fermer), settlement_run_id, base, **flat_vat_amount**, total, nif, mafrd_certificate_no, **flat_rate_eligibility_status**(eligible/pending/ineligible), **deductibility_status**(pending_accountant_signoff/confirmed/rejected). **Autofaturë e gjeneruar nga KABI** që materializohet si `invoice`(farmer_purchase) — JO rrugë paralele; → `tax_book_entry`(blerje) + `open_item`(AP fermer). **Flat-rate 8% (UA 03/2015 neni 76) NUK ngurtësohet si e zbritshme para sign-off (ACC-006).**

## F. Shitje & Blerje / Van
- **invoice** — id, business_id, type(purchase/sale/**farmer_purchase**), partner_id, date, totals, tax_totals, status, **fiscal_status**, uuid. *(Autofaturë fermeri = `invoice` type=farmer_purchase; pa rrugë të dytë.)*
- **invoice_line** — id, business_id, invoice_id, article_id, uom, qty, real_weight?(catch-weight), price, tax_code, lot_id?.
- **delivery_note** — id, business_id, invoice_id, date, delivered_to, driver_id?, status. *(fletëdërgesë, lidhur me invoice)*
- **return_document** (kthim/refuzim) — id, business_id, **type**(post_invoice_return / pre_invoice_rejection / pre_close_correction), original_invoice_id?, partner_id, date, **reason**(e detyrueshme), approved_by, credit_note_id?, status; **return_line** — id, business_id, return_document_id, original_invoice_line_id?, article_id, qty, lot_id?, **disposition**(return_to_stock/scrap/expired/rejected), stock_movement_id?. **Rregull kontabël (kontabilist KABI):** `post_invoice_return` → **`credit_note` E DETYRUESHME** + `reason` + `original_invoice_line_id`; `pre_invoice_rejection`/`pre_close_correction` → return/correction **PA** `credit_note`.
- **credit_note** — id, business_id, return_document_id, invoice_id, original_invoice_line_id, amount, reason. **E detyrueshme për çdo kthim PAS faturimit** (jo për refuzim/korrigjim para faturimit).
- **vehicle** — id, business_id, plate, warehouse_id(depo lëvizëse), active. · **driver_route** — id, business_id, vehicle_id, driver_user_id, date, planned_stops.
- **van_load** — id, business_id, vehicle_id, driver_route_id, date, status; **van_load_line** — id, business_id, van_load_id, article_id, qty, lot_id.
- **van_return_line** — id, business_id, van_load_id, article_id, qty, lot_id, reason. · **proof_of_delivery** — id, business_id, invoice_id/stop_id, signed_by, signature/photo, gps, at.
- **driver_day_close** — id, business_id, driver_route_id, loaded, sold, returned, remaining, cash_collected, credit_invoices, **van_variance**(diferencë), status. **ngarkim−shitje−kthim=mbetje; diferencë→variance + aprovim.**
- **external_distributor_terms** — id, business_id, partner_id(distributor), **rabat_pct(configurable)**, payment_terms. *(Distributorë të jashtëm me rabat — konfirmuar KABI; përqindjet reale = tenant config/private-local, jo në git.)*

## G. PLANET — POS / KDS
- **zone**, **location** (lidhur me printer), **table** — id, business_id, zone_id, location_id, **pos_x/pos_y** (floor-plan; precedent PRO DATA `tblRestTavolinat.LokacioniX/Y`), state(free/hot/occupied).
- **table_session** — id, business_id, table_id, waiter_id, opened_at, closed_at; grupon porositë e një "vizite" në tavolinë (mes hapjes dhe paguaj+mbyll).
- **order** — id, business_id, table_id, table_session_id, waiter_id, status, total_hidden(bool), locked(bool), opened_at.
- **order_item** — id, business_id, order_id, article_id, qty, modifiers, notes, station_id, status(re/proces/gati/dorëzuar), sent_at.
- **kds_station** (≡ station: Grill/Pizza/Sushi/Sallata/Bar/Kuzhina/Expo), **kitchen_ticket** (≡ kds_ticket) — id, business_id, order_item_id, station_id, status, timers. *(Precedent PRO DATA: `tblTMPRestShitjet` = porosi të hapura; `tblTMPRestShitjetKuzhina` = queue KDS.)*
- **business_day / service_day** — id, business_id, date, opened_at, closed_at (**00:00 auto**), status; grupon shift-et + porositë e një dite shërbimi (handover 00:00).
- **shift / cash_shift** — id, business_id, **service_day_id**, user_id/register, opened_at, closed_at, saldo.
- **worker_settlement** — id, business_id, service_day_id, waiter_id, total_sales, cash_due, expenses, status; **barazimi per kamarier** (PRO DATA `tblDocPOSBarazimi*` ekzistonte por **bosh** → modeli ynë net-new).
- **manager_approval** — id, business_id, action(zbritje/anulim/void/rihapje), ref_entity, manager_id, reason, at; çdo aprovim me **PIN** ruhet (PRO DATA ruante fshirjet `*_Deleted`+trigger **por pa PIN/arsye/aprovim** → modeli ynë më i rreptë).
- **printer** — id, business_id, location_id, type(termik/buzzer/fiskal?), role(arka/kuzhinë/backup).

## H. SEF / Fiskalizimi
- **device** — id, business_id, pos_id, pki_ref, certificate_id, status.
- **fiscal_coupon** — id, business_id, invoice_id, fic, nivf, qr, status(offline/pritje/fiskalizuar/dështuar/ndërhyrje), signed_at, sent_at.
- **fiscal_outbox** — id, business_id, coupon_id, attempts, next_retry_at, deadline sipas **origjinës së ndërprerjes** (i ndarë nga sync_outbox). **TARGET (jo i implementuar):** afati 48h lidhet me **outage-event** (fillimi i ndërprerjes — UA 44), JO me krijimin per-kupon; modeli i eventit = PROPOSED/ADR input (T2-08).
- **certificate** — id, business_id, device_id, valid_from, valid_to, alert_before. **TARGET (jo i implementuar):** certifikatë + keypair **per instalim/POS** (çelësi privat s'largohet nga makina — Gate v2 §3.9/T2-05).

## I. Sync & Konflikte
- **sync_outbox** — id, business_id, entity, payload, uuid, idempotency_key, status, event_at.
- **conflict_log** — id, business_id, type(stock/price/credit), entities, resolution, report_ref.

## J. Payroll & Punonjës (Faza 1) *(RISHIKUAR v2.17 — detyrueshëm sipas KABI/kontabilist)*
- **employee** — id, business_id, business_unit_id?, name(ref private), role, hire_date, contract_type, status.
- **employee_contribution** — id, business_id, employee_id, type(pension 5%+5% / tatim në të ardhura), rate, base, period.
- **payroll_run** — id, business_id, period(→fiscal_period), status(draft/approved/locked/posted), approved_by; **payslip** — id, business_id, run_id, employee_id, gross, contributions, tax, net.
- ⚠️ **Scope për t'u sqaruar:** payroll engine i plotë vs vetëm master-data punonjës + kontribute (ndikon scope/kosto Faza 1).

## K. Status-lifecycle (per-entitet, jo global)
| Entitet | Lifecycle |
|---|---|
| milk_intake / milk_sample | `draft → offline_recorded → synced → waiting_lab → accepted/rejected → transferred_to_tank` |
| settlement_run | `draft → approved → locked → posted_to_accounting` |
| posting_run | `draft → approved → locked → posted` |
| fiscal_period | `open → closing → closed → locked` |
| invoice / fiscal_coupon | `draft → issued → (offline → queued → fiscalized/failed)` |
| van_load | `loaded → in_field → reconciled` |
| open_item | `open → partial → closed` |

Kalimet njëdrejtimëshe; kthim mbrapa = veprim i ri + arsye + audit (jo mbishkrim).

## L. Posting hooks (event operativ → `posting_rule` → `journal_entry`)
| Event | Burimi | Postimi (vetëm approved/locked) |
|---|---|---|
| Shlyerje fermeri e aprovuar | `settlement_run` | Dr kosto/lëndë-parë qumësht · Cr detyrim fermeri (`open_item` AP); TVSH flat 8% = `deductibility_status` **pending sign-off** (ACC-006, jo ngurtësim); autofaturë → `invoice`(farmer_purchase) → `tax_book_entry`(blerje) |
| Shitje / faturë | `invoice`(sale) | Dr klient/arkë · Cr të ardhura + TVSH output; `tax_book_entry`(shitje) |
| Blerje / furnitor | `invoice`(purchase) | Dr stok/shpenzim + TVSH input · Cr furnitor (`open_item`) |
| Kthim klienti | `credit_note` | reverse pjesor + `partner_card_entry` |
| Pagesë | `payment` | mbyll `open_item` (matching) |
| Payroll | `payroll_run` | Dr shpenzim pagash · Cr detyrime punonjës/kontribute/tatim |

Rregull: **draft NUK poston**; vetëm approved/locked → `journal_entry` (idempotent me `posting_event.idempotency_key`).

## M. Matrica e gjurmueshmërisë (kërkesë → entitet → posting → test)
| Kërkesë (burim) | Entitete | Posting | Test/fixture |
|---|---|---|---|
| Mostër per fermer + lab-gate (KABI-002) | milk_sample, lab_analysis | — (para shlyerjes) | reject pa analizë; antibiotik→reject |
| Formula USAID = bazë pagese (KABI-001) | settlement_run, farmer_settlement_line, quality_class | settlement → AP fermer (partner) | 1:1 me Excel (fixtures private) |
| Recall LOT→fermerë (siguri ushqimore) | milk_intake, milk_pool_contribution, tank/tank_movement, production_batch, lot, pasteurization_log | — | LOT→kontribues fermerë; CCP fail bllokon |
| Flat-rate 8% fermerë (ACC-006) | farmer_purchase_document→invoice(farmer_purchase), tax_book_entry+bucket | Dr stok · Cr fermer; TVSH8% **deductibility pending sign-off** | autofaturë + box libri blerje; jo ngurtësim para ACC-006 |
| Mixed-VAT 18%+8% (kontabilist) | tax_book_entry + tax_book_bucket | tax_book per rate | faturë me 2 rate |
| TVSH produkte 18/8 (ACC-005) | article.tax_code | shitje output VAT | qumësht/ajkë=18, tjera=8 |
| Van-sales + FEFO (KABI-005) | van_load/van_load_line, van_return_line, driver_day_close, proof_of_delivery, invoice(sale), lot | shitje në terren (offline) | ngarkim−shitje−kthim=mbetje; variance→aprovim |
| Kthime + dispozicion (ACC-008) | return_document/return_line, credit_note, partner_card_entry | reverse pjesor | kthim→faturë+rresht origjinal+LOT; disposition scrap/return_to_stock |
| Payroll Faza 1 (v2.17) | employee, payroll_run | pagat → GL | payslip gross/contrib/net |
| Lab-gate → shlyerje (KABI-002) | lab_analysis, settlement_run | settlement s'niset pa `lab_analysis.class_final` të aprovuar | fixture: intake pa lab → settlement **bllokuar** |
| Draft vs approved (posting invariant) | settlement_run/posting_run, open_item, journal_entry | **draft = 0 GL/AP**; approved/locked → `open_item` AP + `journal_entry` | fixture: draft s'krijon AP; approved krijon AP |
| Kthim pas-faturimit → notë krediti (kontabilist) | return_document(post_invoice_return), credit_note | reverse me `credit_note` **të detyrueshme** | fixture: post-invoice return pa credit_note → **invalid** |
| business_id isolation (multi-tenant) | journal_line, tax_book_bucket, order_item, van_load_line, payslip | çdo child-row ka business_id; RLS bllokon cross-tenant | fixture: tenant A s'lexon rreshtat e tenant B |

**Parim 100/100:** çdo kërkesë ka burim → entitet → posting → test. Asnjë entitet pa arsye; asnjë kërkesë pa mbulim.

## N. MVP vs Later
- **KABI MVP (Faza 1):** A, B, C(bërthama posting), D, E+E2, F(van), J(payroll bazë), H(SEF core).
- **Later:** integrim direkt Lacto Scan, OCR import, traceability-recall i plotë, KPI cilësi/humbje, asete fikse/amortizim (Faza 2), events/banketet.

---
*Hapi tjetër (Faza 1): ky draft → skema reale + migrime + `generate_typescript_types`. Çdo entitet i ri → SSOT §13 + ky draft. **Vlerat/pragjet/parametrat realë (formula, thresholds, çmime) = private-local/tenant config, kurrë në git.***
