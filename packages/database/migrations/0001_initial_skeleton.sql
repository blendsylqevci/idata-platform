-- Increment 1 — Walking Skeleton: Platform & Tenancy fillestar.
-- Rregull (Data Model v2.2.1): business_id DIREKT (jo i trashëguar) në çdo tabelë
-- tenant-scoped. permission = katalog global (PA business_id). audit_log = append-only.
-- Modeli logjik "user" -> tabela fizike "app_user" (user = fjalë e rezervuar në Postgres).

CREATE TABLE business_group (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name       text NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE business (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  group_id   uuid NOT NULL REFERENCES business_group (id),
  name       text NOT NULL,
  nui        text,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE app_user (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  email       text NOT NULL UNIQUE,
  name        text NOT NULL,
  mfa_enabled boolean NOT NULL DEFAULT false,
  status      text NOT NULL DEFAULT 'active',
  created_at  timestamptz NOT NULL DEFAULT now()
);

-- Katalog global reference: PA business_id (sipas kushtit të skeleton-it).
CREATE TABLE permission (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  feature_key text NOT NULL,
  action      text NOT NULL,
  created_at  timestamptz NOT NULL DEFAULT now(),
  UNIQUE (feature_key, action)
);

-- Tenant-scoped: business_id DIREKT + NOT NULL.
CREATE TABLE role (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id uuid NOT NULL REFERENCES business (id),
  name        text NOT NULL,
  is_system   boolean NOT NULL DEFAULT false,
  created_at  timestamptz NOT NULL DEFAULT now(),
  UNIQUE (business_id, name)
);

-- Child/leaf: business_id DIREKT (denormalizuar; rule==model).
CREATE TABLE role_permission (
  id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id   uuid NOT NULL REFERENCES business (id),
  role_id       uuid NOT NULL REFERENCES role (id),
  permission_id uuid NOT NULL REFERENCES permission (id),
  created_at    timestamptz NOT NULL DEFAULT now(),
  UNIQUE (business_id, role_id, permission_id)
);

-- Lidh app_user + business + role. business_id DIREKT + NOT NULL.
CREATE TABLE business_membership (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id uuid NOT NULL REFERENCES business (id),
  user_id     uuid NOT NULL REFERENCES app_user (id),
  role_id     uuid NOT NULL REFERENCES role (id),
  created_at  timestamptz NOT NULL DEFAULT now(),
  UNIQUE (business_id, user_id, role_id)
);

-- Append-only. business_id NOT NULL për skeleton (auditimi group-level = pikë e hapur Faza 1).
CREATE TABLE audit_log (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  business_id uuid NOT NULL REFERENCES business (id),
  user_id     uuid REFERENCES app_user (id),
  entity      text NOT NULL,
  entity_id   uuid,
  action      text NOT NULL,
  before      jsonb,
  after       jsonb,
  event_at    timestamptz NOT NULL DEFAULT now()
);

-- Zbato append-only: çdo UPDATE/DELETE mbi audit_log ngre gabim (provohet me test).
CREATE FUNCTION audit_log_append_only() RETURNS trigger
  LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'audit_log është append-only: % nuk lejohet', TG_OP;
END;
$$;

CREATE TRIGGER audit_log_no_mutate
  BEFORE UPDATE OR DELETE ON audit_log
  FOR EACH ROW EXECUTE FUNCTION audit_log_append_only();
