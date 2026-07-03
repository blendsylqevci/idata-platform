# 04 Data Model Draft — Update from PRO DATA Closure

## Add / confirm shared accounting entities

- `business_group`
- `business`
- `branch`
- `cost_center`
- `fiscal_year`
- `accounting_period`
- `account`
- `journal_entry`
- `journal_line`
- `document_type`
- `posting_rule`
- `posting_run`
- `tax_code`
- `tax_book_entry`
- `vat_declaration_snapshot`
- `partner`
- `partner_card_entry`
- `payment`
- `payment_allocation`
- `cash_register`
- `bank_account`
- `bank_reconciliation`

## Add / confirm inventory and costing entities

- `item`
- `item_category`
- `item_unit`
- `item_price_history`
- `warehouse`
- `warehouse_location`
- `stock_movement`
- `stock_movement_line`
- `stock_adjustment`
- `inventory_count`
- `recipe`
- `recipe_line`
- `cost_layer`
- `stock_valuation_snapshot`
- `opening_balance`

## Add / confirm PLANET POS/KDS entities

- `service_day`
- `restaurant_zone`
- `restaurant_floor_plan`
- `restaurant_table`
- `table_session`
- `order`
- `order_item`
- `order_send_batch`
- `kds_station`
- `kitchen_ticket`
- `kitchen_ticket_status_event`
- `cash_shift`
- `worker_settlement`
- `payment_terminal`
- `manager_approval`
- `void_reason`
- `printer_route`

## Add / confirm RBAC/audit/config entities

- `role`
- `permission`
- `role_permission`
- `user_role`
- `user_location_access`
- `user_warehouse_access`
- `approval_policy`
- `approval_request`
- `audit_event`
- `break_glass_session`
- `config_setting`
- `config_change_event`

## Add / confirm fiscal/SEF entities

- `fiscal_device`
- `fiscal_pos`
- `fiscal_outbox`
- `fiscal_status_event`
- `fiscal_payload_archive`
- `offline_fiscal_ticket`

## Important design rules

- Keep write path through backend/domain layer.
- Keep audit append-only.
- Keep financial correction through reversal/correction/credit-note, not hard delete.
- Preserve offline timestamp and sync timestamp separately.
- Keep service day/business day separate from calendar timestamp for PLANET.
- Do not model PLANET restaurant flow as legacy temp tables; model it as table sessions, orders, tickets, status events and fiscal/payment handoff.
