import { describe, expect, it } from "vitest";
import { createPlatformApiClient, type PlatformStatusResponse } from "../src/index.js";

/** Fixture sintetike — e njëjta formë si kontrata e gjeneruar. */
const syntheticStatus: PlatformStatusResponse = {
  status: "OK",
  apiVersion: "platform-v1",
  database: "READY",
  capabilities: {
    identity: "NOT_CONFIGURED_F1_PENDING",
    audit: "NOT_CONFIGURED_F1_PENDING",
    outbox: "NOT_CONFIGURED_F1_PENDING",
    observability: "NOT_CONFIGURED_F1_PENDING",
  },
};

describe("platform-api-client — klient callable i tipizuar nga kontrata e gjeneruar", () => {
  it("GET /api/platform/status thërret path-in e kontratës dhe kthen përgjigje të tipizuar", async () => {
    const calls: string[] = [];
    const client = createPlatformApiClient({
      baseUrl: "http://api.invalid",
      fetch: (request) => {
        calls.push(request.url);
        return Promise.resolve(
          new Response(JSON.stringify(syntheticStatus), {
            headers: { "Content-Type": "application/json" },
          }),
        );
      },
    });

    const { data, error } = await client.GET("/api/platform/status");

    expect(error).toBeUndefined();
    expect(data?.status).toBe("OK");
    expect(data?.apiVersion).toBe("platform-v1");
    expect(data?.database).toBe("READY");
    expect(data?.capabilities.identity).toBe("NOT_CONFIGURED_F1_PENDING");
    expect(calls).toEqual(["http://api.invalid/api/platform/status"]);
  });

  it("gabimi HTTP kthehet si error i strukturuar — pa exception të papritur", async () => {
    const client = createPlatformApiClient({
      baseUrl: "http://api.invalid",
      fetch: () => Promise.resolve(new Response("{}", { status: 503, headers: { "Content-Type": "application/json" } })),
    });

    const { data, error, response } = await client.GET("/api/platform/status");

    expect(data).toBeUndefined();
    expect(error).toBeDefined();
    expect(response.status).toBe(503);
  });
});
