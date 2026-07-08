-- PoC-4a — skema e HUB-it lokal. QËLLIMISHT JASHTË zinxhirit të migrimeve 000X
-- (zinxhiri = i cloud-it; hub-i mund të dalë SQLite me ADR-013 — kjo skemë
-- aplikohet nga harness-i i PoC dhe është e zëvendësueshme).
-- IF NOT EXISTS kudo → aplikim idempotent. business_id NOT NULL direkt
-- (pa FK — hub-i s'ka tabelën business; lidhja bëhet në cloud).
-- Emrat = vetëm poc4_ (order/table/kds_ticket e vërteta = F7/F8).

CREATE TABLE IF NOT EXISTS poc4_station (
  code text PRIMARY KEY,
  name text NOT NULL
);
INSERT INTO poc4_station (code, name) VALUES
  ('demo-grill', 'Grill Demo'),
  ('demo-bar', 'Bar Demo')
ON CONFLICT (code) DO NOTHING;

CREATE TABLE IF NOT EXISTS poc4_order (
  id          uuid PRIMARY KEY, -- UUID nga pajisja
  business_id uuid NOT NULL,
  device_id   text NOT NULL,
  created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS poc4_order_item (
  id           uuid PRIMARY KEY,
  business_id  uuid NOT NULL,
  order_id     uuid NOT NULL REFERENCES poc4_order (id),
  article_code text NOT NULL,
  qty          numeric(18,2) NOT NULL
);

CREATE TABLE IF NOT EXISTS poc4_kds_ticket (
  id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id   uuid NOT NULL,
  order_item_id uuid NOT NULL REFERENCES poc4_order_item (id),
  station_code  text NOT NULL REFERENCES poc4_station (code),
  status        text NOT NULL DEFAULT 'e_re'
                CHECK (status IN ('e_re','ne_pune','gati','konfirmuar')),
  flagged       boolean NOT NULL DEFAULT false, -- artikull i pa-mapuar → stacion default + flamur
  created_at    timestamptz NOT NULL DEFAULT now(),
  confirmed_at  timestamptz
);

-- "Printeri" i simuluar si radhë fallback (SSOT §6: ekrani s'konfirmon
-- brenda ~afatit → backup në printer termik; afati = config i testueshëm).
CREATE TABLE IF NOT EXISTS poc4_fallback_queue (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id uuid NOT NULL,
  ticket_id   uuid NOT NULL REFERENCES poc4_kds_ticket (id),
  reason      text NOT NULL,
  created_at  timestamptz NOT NULL DEFAULT now(),
  UNIQUE (ticket_id)
);

-- Outbox hub→cloud: semantika e provuar e PoC-3 (seq monotonic + event_uuid).
-- hub_seq bigserial = monotonic; synced=false → pending; ack → synced=true
-- (rreshtat MBETEN si audit — zero fshirje të heshtura).
CREATE TABLE IF NOT EXISTS poc4_outbox_cloud (
  hub_seq     bigserial PRIMARY KEY,
  business_id uuid NOT NULL,
  event_uuid  uuid NOT NULL UNIQUE,
  payload     jsonb NOT NULL,
  created_at  timestamptz NOT NULL DEFAULT now(),
  synced      boolean NOT NULL DEFAULT false,
  synced_at   timestamptz
);
