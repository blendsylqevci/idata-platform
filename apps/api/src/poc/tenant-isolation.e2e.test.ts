import { describe, it, expect, beforeAll, afterAll } from "vitest";
import { createApp } from "../app.factory.js";
import { runMigrations, closePool, getPool, seedPocTenants, DEMO_IDS } from "@idata/database";

// PoC-1: izolim tenant në nivel API (deny by default; membership real; kontekst eksplicit).
describe("PoC-1 tenant isolation — API", () => {
  let app: Awaited<ReturnType<typeof createApp>>;
  let baseUrl: string;

  beforeAll(async () => {
    await runMigrations();
    await seedPocTenants(getPool());
    app = await createApp();
    await app.listen(0);
    const address = app.getHttpServer().address();
    const port = typeof address === "object" && address !== null ? address.port : 0;
    baseUrl = `http://127.0.0.1:${port}`;
  });

  afterAll(async () => {
    await app?.close();
    await closePool();
  });

  async function getRoles(userId?: string, businessId?: string): Promise<Response> {
    const headers: Record<string, string> = {};
    if (userId) headers["x-user-id"] = userId;
    if (businessId) headers["x-business-id"] = businessId;
    return fetch(`${baseUrl}/poc/roles`, { headers });
  }

  interface Body {
    businessId: string;
    roles: { id: string; name: string; business_id: string }[];
  }

  it("KABI user + business=KABI → LEJOHET; vetëm role të KABI-t (KURRË PLANET)", async () => {
    const res = await getRoles(DEMO_IDS.userKabi, DEMO_IDS.bizKabi);
    expect(res.status).toBe(200);
    const body = (await res.json()) as Body;
    expect(body.roles.length).toBeGreaterThan(0);
    expect(body.roles.every((r) => r.business_id === DEMO_IDS.bizKabi)).toBe(true);
    expect(body.roles.some((r) => r.business_id === DEMO_IDS.bizPlanet)).toBe(false);
  });

  it("KABI user + business=PLANET → DENY (403), asnjë e dhënë", async () => {
    const res = await getRoles(DEMO_IDS.userKabi, DEMO_IDS.bizPlanet);
    expect(res.status).toBe(403);
  });

  it("KABI user PA X-Business-Id → DENY (403)", async () => {
    const res = await getRoles(DEMO_IDS.userKabi, undefined);
    expect(res.status).toBe(403);
  });

  it("Group-owner + business=KABI → LEJOHET (vetëm role të KABI-t)", async () => {
    const res = await getRoles(DEMO_IDS.userOwner, DEMO_IDS.bizKabi);
    expect(res.status).toBe(200);
    const body = (await res.json()) as Body;
    expect(body.roles.every((r) => r.business_id === DEMO_IDS.bizKabi)).toBe(true);
  });

  it("Group-owner + business=PLANET → LEJOHET (vetëm role të PLANET-it)", async () => {
    const res = await getRoles(DEMO_IDS.userOwner, DEMO_IDS.bizPlanet);
    expect(res.status).toBe(200);
    const body = (await res.json()) as Body;
    expect(body.roles.every((r) => r.business_id === DEMO_IDS.bizPlanet)).toBe(true);
  });

  it("Group-owner PA X-Business-Id → DENY (kurrë 'all tenants' implicit)", async () => {
    const res = await getRoles(DEMO_IDS.userOwner, undefined);
    expect(res.status).toBe(403);
  });
});
