-- PoC-2 — Accounting Posting: tabelat minimale sipas EMRAVE të Data Model v2.2.1 §C.
-- Invariante nga dita zero: business_id NOT NULL DIREKT kudo (rule==model);
-- composite FK cross-tenant (modeli ENG-003); journal_entry/journal_line APPEND-ONLY
-- (korrigjimi = reversal); debit=kredit i verifikuar edhe në DB me CONSTRAINT TRIGGER
-- DEFERRABLE (në commit — PostgreSQL s'lejon aggregate në CHECK); shumat NUMERIC, kurrë float.
-- Shënime kolonash (detaje nën-model, raportuar në PR): date→entry_date (identifikator i
-- rezervuar praktikisht); account_id→account_code (tabela `account`/COA final = ACC-003 open);
-- partner_id pa FK (partner master = Faza 1); invoice_id pa FK (tabela invoice = më vonë).

CREATE TABLE fiscal_period (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id uuid NOT NULL REFERENCES business (id),
  period      text NOT NULL, -- YYYY-MM
  status      text NOT NULL DEFAULT 'open'
              CHECK (status IN ('open','closing','closed','locked')),
  created_at  timestamptz NOT NULL DEFAULT now(),
  UNIQUE (business_id, period),
  UNIQUE (business_id, id)
);

CREATE TABLE posting_run (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id      uuid NOT NULL REFERENCES business (id),
  fiscal_period_id uuid NOT NULL,
  type             text NOT NULL DEFAULT 'adhoc'
                   CHECK (type IN ('monthly','settlement','adhoc')),
  status           text NOT NULL DEFAULT 'draft'
                   CHECK (status IN ('draft','approved','locked','posted')),
  approved_by      uuid REFERENCES app_user (id),
  locked_at        timestamptz,
  created_at       timestamptz NOT NULL DEFAULT now(),
  UNIQUE (business_id, id),
  FOREIGN KEY (business_id, fiscal_period_id) REFERENCES fiscal_period (business_id, id)
);

CREATE TABLE posting_event (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id     uuid NOT NULL REFERENCES business (id),
  source_entity   text NOT NULL,
  source_id       text NOT NULL,
  event_type      text NOT NULL,
  occurred_at     timestamptz NOT NULL DEFAULT now(),
  posting_run_id  uuid,
  status          text NOT NULL DEFAULT 'pending'
                  CHECK (status IN ('pending','posted','failed','reversed')),
  idempotency_key text NOT NULL,
  UNIQUE (business_id, idempotency_key), -- "një burim → një postim" (Data Model §C :53)
  UNIQUE (business_id, id),
  FOREIGN KEY (business_id, posting_run_id) REFERENCES posting_run (business_id, id)
);

CREATE TABLE journal_entry (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id      uuid NOT NULL REFERENCES business (id),
  posting_event_id uuid NOT NULL,
  fiscal_period_id uuid NOT NULL,
  entry_date       date NOT NULL,
  source_doc       text NOT NULL,
  status           text NOT NULL DEFAULT 'posted',
  created_at       timestamptz NOT NULL DEFAULT now(),
  UNIQUE (business_id, id),
  UNIQUE (business_id, posting_event_id), -- një event → një entry (mbrojtje dublimi)
  FOREIGN KEY (business_id, posting_event_id) REFERENCES posting_event (business_id, id),
  FOREIGN KEY (business_id, fiscal_period_id) REFERENCES fiscal_period (business_id, id)
);

CREATE TABLE journal_line (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id      uuid NOT NULL REFERENCES business (id),
  journal_entry_id uuid NOT NULL,
  account_code     text NOT NULL, -- COA final = ACC-003 (open); kodet = config provisional
  debit            numeric(18,2) NOT NULL DEFAULT 0 CHECK (debit >= 0),
  credit           numeric(18,2) NOT NULL DEFAULT 0 CHECK (credit >= 0),
  partner_id       uuid, -- partner master = Faza 1 (pa FK në PoC)
  created_at       timestamptz NOT NULL DEFAULT now(),
  CHECK ((debit = 0) <> (credit = 0)), -- saktësisht njëra anë per rresht
  FOREIGN KEY (business_id, journal_entry_id) REFERENCES journal_entry (business_id, id)
);

CREATE TABLE tax_book_entry (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id      uuid NOT NULL REFERENCES business (id),
  book             text NOT NULL CHECK (book IN ('shitje','blerje')),
  fiscal_period_id uuid NOT NULL,
  partner_id       uuid, -- pa FK (partner master = Faza 1)
  invoice_id       uuid, -- pa FK (tabela invoice = më vonë); burimi ndiqet te posting_event
  doc_type         text NOT NULL,
  entry_date       date NOT NULL,
  total_base       numeric(18,2) NOT NULL,
  total_vat        numeric(18,2) NOT NULL,
  status           text NOT NULL DEFAULT 'posted',
  posting_event_id uuid NOT NULL,
  created_at       timestamptz NOT NULL DEFAULT now(),
  UNIQUE (business_id, id),
  UNIQUE (business_id, posting_event_id),
  FOREIGN KEY (business_id, fiscal_period_id) REFERENCES fiscal_period (business_id, id),
  FOREIGN KEY (business_id, posting_event_id) REFERENCES posting_event (business_id, id)
);

CREATE TABLE tax_book_bucket (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id       uuid NOT NULL REFERENCES business (id),
  tax_book_entry_id uuid NOT NULL,
  declaration_box   text NOT NULL, -- [12]/[43]/[45]/[51]/…; totalet [30]/[67] llogariten në export
  rate              text NOT NULL CHECK (rate IN ('0','8','18','flat-8')),
  base              numeric(18,2) NOT NULL,
  vat               numeric(18,2) NOT NULL,
  created_at        timestamptz NOT NULL DEFAULT now(),
  FOREIGN KEY (business_id, tax_book_entry_id) REFERENCES tax_book_entry (business_id, id)
);

CREATE TABLE partner_card_entry (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id     uuid NOT NULL REFERENCES business (id),
  partner_id      uuid NOT NULL, -- pa FK (partner master = Faza 1)
  doc_ref         text NOT NULL,
  entry_date      date NOT NULL,
  debit           numeric(18,2) NOT NULL DEFAULT 0,
  credit          numeric(18,2) NOT NULL DEFAULT 0,
  running_balance numeric(18,2) NOT NULL,
  type            text NOT NULL
                  CHECK (type IN ('intake','settlement','advance','penalty','payment','return','invoice')),
  created_at      timestamptz NOT NULL DEFAULT now()
);

-- APPEND-ONLY për GL (si audit_log): korrigjimi = reversal, kurrë UPDATE/DELETE.
CREATE FUNCTION accounting_append_only() RETURNS trigger
  LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION '% është append-only: % nuk lejohet (korrigjimi = reversal)', TG_TABLE_NAME, TG_OP;
END;
$$;

CREATE TRIGGER journal_entry_no_mutate
  BEFORE UPDATE OR DELETE ON journal_entry
  FOR EACH ROW EXECUTE FUNCTION accounting_append_only();

CREATE TRIGGER journal_line_no_mutate
  BEFORE UPDATE OR DELETE ON journal_line
  FOR EACH ROW EXECUTE FUNCTION accounting_append_only();

-- DEBIT=KREDIT në nivel DB: CONSTRAINT TRIGGER DEFERRABLE — verifikohet në COMMIT
-- të transaksionit (jo CHECK i thjeshtë kolone; PostgreSQL s'bën aggregate në CHECK).
CREATE FUNCTION journal_entry_balanced() RETURNS trigger
  LANGUAGE plpgsql AS $$
DECLARE
  v_debit  numeric;
  v_credit numeric;
BEGIN
  SELECT COALESCE(SUM(debit), 0), COALESCE(SUM(credit), 0)
    INTO v_debit, v_credit
    FROM journal_line
   WHERE business_id = NEW.business_id AND journal_entry_id = NEW.journal_entry_id;
  IF v_debit <> v_credit THEN
    RAISE EXCEPTION 'journal_entry % i pabalancuar në commit: debit % <> kredit %',
      NEW.journal_entry_id, v_debit, v_credit;
  END IF;
  RETURN NULL;
END;
$$;

CREATE CONSTRAINT TRIGGER journal_line_balance_check
  AFTER INSERT ON journal_line
  DEFERRABLE INITIALLY DEFERRED
  FOR EACH ROW EXECUTE FUNCTION journal_entry_balanced();

-- ============================================================================
-- ADR-012 INPUT (dual-variant, PA vendim): varianti "GL posting në funksion DB".
-- I njëjti postim (shitje 18%) ekziston edhe si transaksion app-level në
-- packages/database/src/posting.ts — rezultatet krahasohen me test.
-- Kodet e llogarive këtu janë të DUBLUARA me qëllim (observim për ADR-012:
-- config-i SQL-side duhet versionuar/deploy-uar veçmas nga app config).
-- ============================================================================
CREATE FUNCTION poc2_post_sale18(
  p_business_id      uuid,
  p_fiscal_period_id uuid,
  p_source_id        text,
  p_idempotency_key  text,
  p_partner_id       uuid,
  p_net              numeric,
  p_entry_date       date
) RETURNS uuid LANGUAGE plpgsql AS $$
DECLARE
  v_status   text;
  v_event_id uuid;
  v_entry_id uuid;
  v_tbe_id   uuid;
  v_vat      numeric;
  v_gross    numeric;
BEGIN
  SELECT status INTO v_status
    FROM fiscal_period
   WHERE business_id = p_business_id AND id = p_fiscal_period_id;
  IF v_status IS NULL THEN
    RAISE EXCEPTION 'fiscal_period s''u gjet për këtë business';
  END IF;
  IF v_status <> 'open' THEN
    RAISE EXCEPTION 'fiscal_period është "%": postimi refuzohet (period lock)', v_status;
  END IF;

  -- Idempotencë: nëse event-i ekziston, kthe entry-n ekzistues (0 dublime).
  SELECT je.id INTO v_entry_id
    FROM posting_event pe
    JOIN journal_entry je
      ON je.business_id = pe.business_id AND je.posting_event_id = pe.id
   WHERE pe.business_id = p_business_id AND pe.idempotency_key = p_idempotency_key;
  IF v_entry_id IS NOT NULL THEN
    RETURN v_entry_id;
  END IF;

  v_vat   := round(p_net * 18 / 100, 2);
  v_gross := p_net + v_vat;

  INSERT INTO posting_event (business_id, source_entity, source_id, event_type, status, idempotency_key)
  VALUES (p_business_id, 'invoice', p_source_id, 'sale', 'pending', p_idempotency_key)
  RETURNING id INTO v_event_id;

  INSERT INTO journal_entry (business_id, posting_event_id, fiscal_period_id, entry_date, source_doc, status)
  VALUES (p_business_id, v_event_id, p_fiscal_period_id, p_entry_date, p_source_id, 'posted')
  RETURNING id INTO v_entry_id;

  INSERT INTO journal_line (business_id, journal_entry_id, account_code, debit, credit, partner_id) VALUES
    (p_business_id, v_entry_id, '1210', v_gross, 0, p_partner_id),
    (p_business_id, v_entry_id, '6010', 0, p_net,  NULL),
    (p_business_id, v_entry_id, '4710', 0, v_vat,  NULL);

  INSERT INTO tax_book_entry (business_id, book, fiscal_period_id, partner_id, doc_type,
                              entry_date, total_base, total_vat, status, posting_event_id)
  VALUES (p_business_id, 'shitje', p_fiscal_period_id, p_partner_id, 'SHV',
          p_entry_date, p_net, v_vat, 'posted', v_event_id)
  RETURNING id INTO v_tbe_id;

  INSERT INTO tax_book_bucket (business_id, tax_book_entry_id, declaration_box, rate, base, vat)
  VALUES (p_business_id, v_tbe_id, '[12]', '18', p_net, v_vat);

  INSERT INTO partner_card_entry (business_id, partner_id, doc_ref, entry_date, debit, credit, running_balance, type)
  VALUES (p_business_id, p_partner_id, p_source_id, p_entry_date, v_gross, 0,
          COALESCE((SELECT pce.running_balance FROM partner_card_entry pce
                     WHERE pce.business_id = p_business_id AND pce.partner_id = p_partner_id
                     ORDER BY pce.created_at DESC, pce.id DESC LIMIT 1), 0) + v_gross,
          'invoice');

  UPDATE posting_event SET status = 'posted'
   WHERE business_id = p_business_id AND id = v_event_id;

  RETURN v_entry_id;
END;
$$;
