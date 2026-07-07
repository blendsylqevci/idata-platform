-- PoC-1 / ENG-003 — izolim tenant në nivel DB me composite FK cross-tenant.
-- Rregull: (business_id, role_id) e një child duhet t'i referohet një role të TË NJËJTIT
-- business. INSERT me role të biznesit tjetër dështon në DB (jo vetëm në aplikacion).
-- FK-të single-column nga 0001 mbeten (të padëmshme; composite-t janë zbatuesi i tenant-it).

-- Target për composite FK: role duhet të ketë UNIQUE (business_id, id).
ALTER TABLE role
  ADD CONSTRAINT role_business_id_id_key UNIQUE (business_id, id);

-- role_permission → role i TË NJËJTIT business.
ALTER TABLE role_permission
  ADD CONSTRAINT role_permission_business_role_fk
  FOREIGN KEY (business_id, role_id) REFERENCES role (business_id, id);

-- business_membership → role i TË NJËJTIT business.
ALTER TABLE business_membership
  ADD CONSTRAINT business_membership_business_role_fk
  FOREIGN KEY (business_id, role_id) REFERENCES role (business_id, id);
