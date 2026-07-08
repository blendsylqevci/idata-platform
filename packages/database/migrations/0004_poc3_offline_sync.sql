-- PoC-3 — Offline Sync: tabela VETËM poc3_ (emrat canonical invoice/article/stock/
-- partner dizajnohen në F2/F3 — QËLLIMISHT të papërdorura këtu).
-- business_id NOT NULL DIREKT kudo. Idempotenca dhe rendi zbatohen NË DB:
--   UNIQUE (business_id, event_uuid)              -> "një event → një aplikim"
--   UNIQUE (business_id, device_id, device_seq)   -> rendi/anti-dublim per pajisje
-- device_seq = monotonic per pajisje (rendi NUK varet nga ora e pajisjes;
-- occurred_at është informativ). id bigserial = rendi real i aplikimit në server.

CREATE TABLE poc3_sync_event (
  id          bigserial PRIMARY KEY, -- rendi i aplikimit në server (deterministik)
  business_id uuid NOT NULL REFERENCES business (id),
  device_id   text NOT NULL,
  event_uuid  uuid NOT NULL,
  device_seq  bigint NOT NULL,
  occurred_at timestamptz NOT NULL, -- ora e pajisjes (informative, JO burim rendi)
  applied_at  timestamptz NOT NULL DEFAULT now(),
  payload     jsonb NOT NULL,
  replay      boolean NOT NULL DEFAULT false,
  UNIQUE (business_id, event_uuid),
  UNIQUE (business_id, device_id, device_seq)
);

CREATE TABLE poc3_stock (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id  uuid NOT NULL REFERENCES business (id),
  article_code text NOT NULL,
  qty          numeric(18,2) NOT NULL DEFAULT 0, -- lejohet NEGATIVE (rregulli R1, SSOT §2)
  UNIQUE (business_id, article_code)
);

CREATE TABLE poc3_invoice (
  id           uuid PRIMARY KEY, -- UUID i gjeneruar NË PAJISJE (pa default — vjen nga eventi)
  business_id  uuid NOT NULL REFERENCES business (id),
  device_id    text NOT NULL,
  article_code text NOT NULL,
  qty          numeric(18,2) NOT NULL,
  price        numeric(18,2) NOT NULL, -- çmimi i NGRIRË i pajisjes (R2)
  partner_code text NOT NULL,
  status       text NOT NULL
               CHECK (status IN ('accepted','rejected_blocked')),
  created_at   timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE poc3_partner (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id  uuid NOT NULL REFERENCES business (id),
  code         text NOT NULL,
  credit_limit numeric(18,2) NOT NULL,
  balance      numeric(18,2) NOT NULL DEFAULT 0,
  blocked      boolean NOT NULL DEFAULT false, -- R3: vendoset në TË NJËJTIN tx me faturën tejkaluese
  UNIQUE (business_id, code)
);

CREATE TABLE poc3_price_variance (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id  uuid NOT NULL REFERENCES business (id),
  invoice_id   uuid NOT NULL, -- fatura me çmim të ngrirë që devijoi nga serveri (R2)
  article_code text NOT NULL,
  device_price numeric(18,2) NOT NULL,
  server_price numeric(18,2) NOT NULL,
  diff         numeric(18,2) NOT NULL,
  created_at   timestamptz NOT NULL DEFAULT now()
);
