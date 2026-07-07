import { Controller, Get, Headers, ForbiddenException } from "@nestjs/common";
import { hasMembership, rolesForBusiness, type RoleRow } from "./tenant.js";

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * PoC-1 tenant context PA auth real (auth = Faza 1). Konteksti vjen nga header-a
 * sintetikë X-User-Id + X-Business-Id. **Deny by default**: pa kontekst valid +
 * membership REAL → 403, kurrë të dhëna. Group-owner sheh çdo business VETËM me
 * kontekst eksplicit per-kërkesë (kurrë "all tenants" implicit).
 */
@Controller("poc")
export class PocController {
  @Get("roles")
  async roles(
    @Headers("x-user-id") userId?: string,
    @Headers("x-business-id") businessId?: string,
  ): Promise<{ businessId: string; roles: RoleRow[] }> {
    // Deny by default: mungon ose i pavlefshëm konteksti.
    if (!userId || !businessId || !UUID_RE.test(userId) || !UUID_RE.test(businessId)) {
      throw new ForbiddenException("Kontekst tenant i pavlefshëm (X-User-Id / X-Business-Id).");
    }
    // Verifiko membership REAL në business-in e kërkuar.
    if (!(await hasMembership(userId, businessId))) {
      throw new ForbiddenException("S'ka membership në këtë business.");
    }
    // Lexon vetëm brenda kontekstit (filtruar me business_id).
    const roles = await rolesForBusiness(businessId);
    return { businessId, roles };
  }
}
