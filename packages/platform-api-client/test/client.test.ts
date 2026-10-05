import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { components, paths } from "../src/generated/platform-v1.js";
import { createPlatformApiClient, type PlatformStatusResponse } from "../src/index.js";

/** Gabim i kontrolluar i AccessControl — tip i GJENERUAR nga kontrata, pa DTO manual. */
type AccessControlProblem = components["schemas"]["AccessControlProblemDetails"];

/** 400-ja e dokumentuar e UpdateMembershipRole — direkt nga kontrata e gjeneruar. */
type UpdateMembershipRole400 =
  paths["/api/platform/memberships/{membershipId}/role"]["put"]["responses"]["400"]["content"]["application/problem+json"];

/** Trajtim type-safe pa `any`: çdo 400 e dokumentuar mbart `errorCode` string. */
const errorCodeOf400 = (problem: UpdateMembershipRole400): string => problem.errorCode;

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

  it("gabimi i AccessControl ekspozon errorCode të tipizuar nga kontrata e gjeneruar", async () => {
    const problem: AccessControlProblem = {
      type: "https://docs.idata.plus/problems/access-denied",
      title: "Qasja refuzohet.",
      status: 403,
      errorCode: "ACCESS_DENIED",
    };
    const client = createPlatformApiClient({
      baseUrl: "http://api.invalid",
      fetch: () =>
        Promise.resolve(
          new Response(JSON.stringify(problem), {
            status: 403,
            headers: { "Content-Type": "application/problem+json" },
          }),
        ),
    });

    const { data, error, response } = await client.GET("/api/platform/access-context");

    expect(data).toBeUndefined();
    expect(response.status).toBe(403);
    expect(error?.errorCode).toBe("ACCESS_DENIED");
  });

  it("400 i UpdateMembershipRole trajtohet type-safe si gabim i kontrolluar me errorCode", async () => {
    const problem: UpdateMembershipRole400 = {
      type: "https://docs.idata.plus/problems/invalid-request-body",
      title: "Trupi i kerkeses i pavlefshem.",
      status: 400,
      errorCode: "INVALID_REQUEST_BODY",
    };
    const client = createPlatformApiClient({
      baseUrl: "http://api.invalid",
      fetch: () =>
        Promise.resolve(
          new Response(JSON.stringify(problem), {
            status: 400,
            headers: { "Content-Type": "application/problem+json" },
          }),
        ),
    });

    const { data, error, response } = await client.PUT("/api/platform/memberships/{membershipId}/role", {
      params: {
        path: { membershipId: "eaeaeaea-bead-4ead-8ead-eaeaeaeaeaea" },
        header: { "Idempotency-Key": "key-sintetik" },
      },
      body: { roleId: "daaaaaaa-fade-4ade-8ade-daaaaaaaaaaa", expectedVersion: 0 },
    });

    expect(data).toBeUndefined();
    expect(response.status).toBe(400);
    expect(error ? errorCodeOf400(error) : undefined).toBe("INVALID_REQUEST_BODY");
  });

  it("kontrata e gjeneruar deklaron errorCode string dhe kërkesën me fushat e detyrueshme të pandryshuara", () => {
    const contract = JSON.parse(
      readFileSync(new URL("../../../contracts/openapi/platform-v1.json", import.meta.url), "utf8"),
    );
    const schemas = contract.components.schemas;

    expect(schemas.AccessControlProblemDetails.properties.errorCode).toEqual({ type: "string" });
    expect(schemas.AccessControlProblemDetails.required).toContain("errorCode");
    expect(schemas.UpdateMembershipRoleRequest.required).toEqual(["roleId", "expectedVersion"]);
    expect(schemas.UpdateMembershipRoleRequest.properties.roleId).toEqual({ type: "string", format: "uuid" });
    expect(schemas.UpdateMembershipRoleRequest.properties.expectedVersion).toEqual({
      pattern: "^-?(?:0|[1-9]\\d*)$",
      type: ["integer", "string"],
      format: "int64",
    });

    const generated = readFileSync(new URL("../src/generated/platform-v1.ts", import.meta.url), "utf8");
    expect(generated).toMatch(/AccessControlProblemDetails: \{[^}]*\berrorCode: string;/);
    expect(generated).toMatch(/UpdateMembershipRoleRequest: \{[^}]*\broleId: string;[^}]*\bexpectedVersion: number \| string;/);
  });
});
