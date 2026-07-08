-- PoC-5 — FISCAL OUTBOX i IZOLUAR (SSOT §7): radhë E VEÇANTË nga sync-u i
-- përgjithshëm (poc3_*) — s'ndalet kurrë prej tij dhe s'e ndal atë.
-- Statuset = terminologjia EKZAKTE e SSOT §7. Vetëm tabela poc5_.
-- ASNJË identifikues SEF këtu — payload-et në teste janë sintetike;
-- vlerat reale (kur të vijnë në Tier 2) jetojnë vetëm në DB lokale + .sef-local/.

CREATE TABLE poc5_fiscal_outbox (
  id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id    uuid NOT NULL REFERENCES business (id),
  coupon_uuid    uuid NOT NULL,
  coupon_type    text NOT NULL CHECK (coupon_type IN ('Sale','Cancel','Return')),
  payload_ref    text NOT NULL, -- referencë/payload i nënshkruar (details+signature b64; sintetik në teste)
  status         text NOT NULL DEFAULT 'leshuar_offline'
                 CHECK (status IN ('leshuar_offline','ne_pritje','fiskalizuar','deshtuar','kerkon_nderhyrje')),
  attempts       integer NOT NULL DEFAULT 0,
  next_retry_at  timestamptz NOT NULL DEFAULT now(),
  created_at     timestamptz NOT NULL DEFAULT now(),
  deadline_at    timestamptz NOT NULL, -- created + 48h (afati ligjor i Nenit 44)
  transaction_id bigint,
  fiscalized_at  timestamptz,
  UNIQUE (business_id, coupon_uuid) -- idempotencë: një kupon → një rresht radhe
);

CREATE TABLE poc5_fiscal_alert (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id uuid NOT NULL REFERENCES business (id),
  outbox_id   uuid REFERENCES poc5_fiscal_outbox (id),
  alert_type  text NOT NULL
              CHECK (alert_type IN ('deadline_warning','deadline_missed','sign_failure','cert_failure','server_rejection')),
  message     text NOT NULL,
  created_at  timestamptz NOT NULL DEFAULT now(),
  UNIQUE (business_id, outbox_id, alert_type) -- pa spam alarmesh per rresht/tip
);
