-- PoC-4a — ana CLOUD (zinxhiri normal i migrimeve; hub-i ka skemë të veçantë
-- jashtë zinxhirit — shih poc4-hub-schema.sql). VETËM tabela poc4_cloud_*;
-- emrat canonical (order/table/kds_ticket) = F7/F8.

CREATE TABLE poc4_cloud_event (
  id          bigserial PRIMARY KEY, -- rendi i aplikimit në cloud
  business_id uuid NOT NULL REFERENCES business (id),
  hub_id      text NOT NULL,
  hub_seq     bigint NOT NULL,
  event_uuid  uuid NOT NULL,
  payload     jsonb NOT NULL,
  applied_at  timestamptz NOT NULL DEFAULT now(),
  replay      boolean NOT NULL DEFAULT false,
  UNIQUE (business_id, event_uuid),            -- idempotencë: një event → një aplikim
  UNIQUE (business_id, hub_id, hub_seq)        -- anti-dublim i hub_seq në DB
);

CREATE TABLE poc4_cloud_order (
  id           uuid PRIMARY KEY, -- UUID i order_item-it nga hub-i (një rresht per artikull)
  business_id  uuid NOT NULL REFERENCES business (id),
  order_id     uuid NOT NULL,
  device_id    text NOT NULL,
  article_code text NOT NULL,
  qty          numeric(18,2) NOT NULL,
  station_code text NOT NULL,
  created_at   timestamptz NOT NULL DEFAULT now()
);
