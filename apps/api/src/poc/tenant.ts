import { getPool } from "@idata/database";

export interface RoleRow {
  id: string;
  name: string;
  business_id: string;
}

/** A ka user-i membership REAL në business-in e kërkuar (lookup në business_membership). */
export async function hasMembership(userId: string, businessId: string): Promise<boolean> {
  const r = await getPool().query(
    "SELECT 1 FROM business_membership WHERE user_id = $1 AND business_id = $2 LIMIT 1",
    [userId, businessId],
  );
  return (r.rowCount ?? 0) > 0;
}

/** Lexon role-t VETËM të një business-i (tabelë tenant-scoped, filtruar me business_id). */
export async function rolesForBusiness(businessId: string): Promise<RoleRow[]> {
  const r = await getPool().query<RoleRow>(
    "SELECT id, name, business_id FROM role WHERE business_id = $1 ORDER BY name",
    [businessId],
  );
  return r.rows;
}
