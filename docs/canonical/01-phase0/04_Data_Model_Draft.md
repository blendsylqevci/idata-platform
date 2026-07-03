# Faza 0 — Data Model Draft (iData+)

**Versioni:** 1.2 · **Data:** 28 Qershor 2026 · **Burimi:** `iData_Plus_SSOT.md` v2.5 §13
**Statusi:** draft (jo DDL final) — mjaftueshëm që monorepo të mos ndërtohet "në ajër".

## Konventa globale
- **Multi-tenant:** `business_id` (FK→business) në çdo tabelë tenant-scoped; jo në tabela globale/reference.
- **Çelësat:** `id` = UUID (gjenerohet edhe në pajisje për offline).
- **Para:** `numeric` për GL (precizion i lartë); **SEF money scale PER-FIELD** — `CouponItem.Price/Total` = €0.0001 (×10000); coupon `Total`/`Payment.Amount`/`TaxGroup.*`/`TotalTax`/`TotalNoTax`/`TotalDiscount` = cents (×100); decimal në TS; tabular-nums në UI. Kurrë float.
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
- **user** — id, email, name, mfa_enabled, status.
- **role** — id, business_id?, name (PO/GO/TA/KO/…), is_system.
- **permission** — id, feature_key, action(R/C/U/X/P/A).
- **role_permission** — role_id, permission_id.
- **membership** — user_id, business_id, role_id, scope (vetja/njësi/i tërë).
- **breakglass_grant** — id, user_id(iCode), approved_by(Donati), reason, expires_at, audit_ref.
- **audit_log** — id, business_id, user_id, entity, entity_id, action, before, after, event_at.

## B. Partnerë & Master Data
- **partner** — id, business_id, type(furnitor/blerës/agjent), name, nui, vat_no, credit_limit, terms.
- **farmer** — id, business_id, name, code, bank, advance_balance, quality_class.
- **collector (Punki)** — id, business_id, name, commission_type(%/per_litre), commission_value.
- **article** — id, business_id, sku, name, category_id, base_uom, is_catch_weight, tax_code, shelf_life_days, type(MTS/MTO), is_active.
- **uom / article_uom** — uom(copë/kg/litër/pako/gajbe), factor→base_uom, barcode/EAN.
- **price** — article_id, uom_id, price (me/pa TVSH), valid_from; + **price_history**.
- **category**, **tax_code** (T0/T8/T18/TL; **↔ SEF: T0→C, T8→D, T18→E, TL→A** [A=exempt]; konfirmo), **modifier** (medium/well-done/pa qepë…).
- **supplier_article_map** (smart matching) — supplier_id, supplier_name, article_id.

## C. Kontabiliteti
- **account** — id, business_id, code, name, class(1/2/4/6-7/8/9), parent_id.
- **journal_entry** — id, business_id, date, source_doc, status; **journal_line** — entry_id, account_id, debit, credit, partner_id?, cost_center?.
- **tax_book_entry** — id, business_id, book(shitje/blerje), period, declaration_box, base, vat, partner_id, invoice_id.
- **vat_declaration** — id, business_id, period, boxes(jsonb), status.
- **payment** — id, business_id, method(cash/card/bank), amount, partner_id, pos_terminal?, bank_account?; **payment_allocation** — payment_id, invoice_id, amount.
- **bank_account**, **cash_register** (saldo, rakordim).
- **expense** — id, business_id, amount, category, by_user, reason, register/location, date, approval_status, gl_posted(bool); **regjistrohet nga Financë Operative → postohet nga Accounting**.
- **cash_movement** — id, business_id, type(handover/deposit/transfer), from(register), to(office/bank), amount, by_user, at, reason. *(Finance Operations — shih `08_Finance_Operations_SCOPE`.)*
- **payment_method** (cash/kartë/bankë/POS/borxh/avans) · **payment_terminal** (POS-ID/bankë/lokacion) · **cash_count** (numërim fizik arke) · **settlement_line** (rresht barazimi: tavolinë/faturë/pagesë/shpenzim/diferencë) · **supplier_payment** (pjesore/totale).
- **finance_day** (dita financiare; hap/mbyll, status) · **float_assignment** (saldo/float dhënë kamarierit/arkës) · **pos_terminal_batch** (batch/settlement banke per terminal) · **finance_handover** (dorëzimi finance→accounting). *(Codex FO spec; shih `09_Finance_Operations_Workflow_Spec`.)*

## D. Inventari
- **warehouse** — id, business_id, type(depo/restorant/shank/vehicle), name, is_external(shank i jashtëm).
- **stock_balance** — warehouse_id, article_id, lot_id?, qty, avg_cost.
- **stock_movement** — id, business_id, type(in/out/transfer/adjust/scrap), article_id, uom, qty, lot_id?, from_wh?, to_wh?, reason, event_at.
- **recipe (BOM)** — id, business_id, output_article_id, normal_loss_pct; **recipe_line** — recipe_id, input_article_id, qty, uom.
- **packaging_stock (gajbe)** — partner_id/vehicle_id, qty_out, qty_returned, deposit_value.
- **inventory_cost_layer / stock_valuation** — article_id, warehouse_id, qty, unit_cost, method(weighted-avg/batch), valid_from → baza për **COGS** (mesatare e ponderuar + batch costing KABI).

## E. KABI — Prodhimi & Qumështi
- **milk_intake** — id, business_id, route(direct/internal/punki), farmer_id?, collector_id?, litres, **qc**: fat_pct, temp, ph, snf, antibiotics(pass/fail), status(në pritje/aprovuar/refuzuar), event_at.
- **milk_pool_batch** — id, business_id, date, total_litres, weighted_quality, tank_id.
- **production_order** — id, business_id, pool_batch_id, output_article_id, planned_qty, technologist_id, **pasteurization**(temp/time/ok).
- **production_batch** — id, order_id, lot_id, produced_qty, yield_actual_vs_plan, expiry_date.
- **lot** — id, article_id, code, production_date, expiry_date.
- **quality_test** — id, milk_intake_id/production_batch_id, params(jsonb), result.

## F. Shitje & Blerje / Van
- **invoice** — id, business_id, type(purchase/sale/farmer_act), partner_id, date, totals, tax_totals, status, **fiscal_status**, uuid.
- **invoice_line** — invoice_id, article_id, uom, qty, real_weight?(catch-weight), price, tax_code, lot_id?.
- **delivery_note** / **credit_note** (kthim) — lidhur me invoice.
- **van_load** — id, vehicle_wh_id, date, lines(article/qty/lot); **van_reconciliation** — loaded−sold−returned=remaining, cash, credit_invoices.

## G. PLANET — POS / KDS
- **zone**, **location** (lidhur me printer), **table** — id, business_id, zone_id, location_id, **pos_x/pos_y** (floor-plan; precedent PRO DATA `tblRestTavolinat.LokacioniX/Y`), state(free/hot/occupied).
- **table_session** — id, table_id, waiter_id, opened_at, closed_at; grupon porositë e një "vizite" në tavolinë (mes hapjes dhe paguaj+mbyll).
- **order** — id, business_id, table_id, table_session_id, waiter_id, status, total_hidden(bool), locked(bool), opened_at.
- **order_item** — order_id, article_id, qty, modifiers, notes, station_id, status(re/proces/gati/dorëzuar), sent_at.
- **kds_station** (≡ station: Grill/Pizza/Sushi/Sallata/Bar/Kuzhina/Expo), **kitchen_ticket** (≡ kds_ticket) — order_item_id, station_id, status, timers. *(Precedent PRO DATA: `tblTMPRestShitjet` = porosi të hapura; `tblTMPRestShitjetKuzhina` = queue KDS.)*
- **business_day / service_day** — id, business_id, date, opened_at, closed_at (**00:00 auto**), status; grupon shift-et + porositë e një dite shërbimi (handover 00:00).
- **shift / cash_shift** — id, business_id, **service_day_id**, user_id/register, opened_at, closed_at, saldo.
- **worker_settlement** — id, service_day_id, waiter_id, total_sales, cash_due, expenses, status; **barazimi per kamarier** (PRO DATA `tblDocPOSBarazimi*` ekzistonte por **bosh** → modeli ynë net-new).
- **manager_approval** — id, action(zbritje/anulim/void/rihapje), ref_entity, manager_id, reason, at; çdo aprovim me **PIN** ruhet (PRO DATA ruante fshirjet `*_Deleted`+trigger **por pa PIN/arsye/aprovim** → modeli ynë më i rreptë).
- **printer** — id, business_id, location_id, type(termik/buzzer/fiskal?), role(arka/kuzhinë/backup).

## H. SEF / Fiskalizimi
- **device** — id, business_id, pos_id, pki_ref, certificate_id, status.
- **fiscal_coupon** — id, invoice_id, fic, nivf, qr, status(offline/pritje/fiskalizuar/dështuar/ndërhyrje), signed_at, sent_at.
- **fiscal_outbox** — id, coupon_id, attempts, next_retry_at, deadline_48h (i ndarë nga sync_outbox).
- **certificate** — id, device_id, valid_from, valid_to, alert_before.

## I. Sync & Konflikte
- **sync_outbox** — id, business_id, entity, payload, uuid, idempotency_key, status, event_at.
- **conflict_log** — id, type(stock/price/credit), entities, resolution, report_ref.

---
*Hapi tjetër (Faza 1): ky draft → skema reale + migrime + `generate_typescript_types`. Çdo entitet i ri → SSOT §13 + ky draft.*
