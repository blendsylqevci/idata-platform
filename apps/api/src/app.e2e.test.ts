import { describe, it, expect, beforeAll, afterAll } from "vitest";
import type { INestApplication } from "@nestjs/common";
import { createApp } from "./app.factory.js";
import { runMigrations, closePool } from "@idata/database";

// e2e api → db: kërkon Postgres lokal (docker compose up) + env PG*.
describe("api e2e (api → db)", () => {
  let app: INestApplication;
  let baseUrl: string;

  beforeAll(async () => {
    await runMigrations();
    app = await createApp();
    await app.listen(0); // port efemeral
    const address = app.getHttpServer().address();
    const port = typeof address === "object" && address !== null ? address.port : 0;
    baseUrl = `http://127.0.0.1:${port}`;
  });

  afterAll(async () => {
    await app?.close();
    await closePool();
  });

  it("GET /health → { status: 'ok' }", async () => {
    const res = await fetch(`${baseUrl}/health`);
    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({ status: "ok" });
  });

  it("GET /ping → round-trip real në DB", async () => {
    const res = await fetch(`${baseUrl}/ping`);
    expect(res.status).toBe(200);
    const body = (await res.json()) as { ok: boolean; db: { echo: string; ts: string } };
    expect(body.ok).toBe(true);
    expect(body.db.echo).toBe("pong");
    expect(typeof body.db.ts).toBe("string");
  });
});
