import { describe, it, expect, beforeAll, afterAll } from "vitest";
import { createApp } from "@idata/api";
import { runMigrations, closePool } from "@idata/database";
import { fetchPing } from "../src/lib/ping";

// e2e web → api → db: nis API-n Nest në port efemeral dhe thërret `fetchPing` (kodi i web-it).
describe("web e2e (web → api → db)", () => {
  let app: Awaited<ReturnType<typeof createApp>>;
  let apiUrl: string;

  beforeAll(async () => {
    await runMigrations();
    app = await createApp();
    await app.listen(0);
    const address = app.getHttpServer().address();
    const port = typeof address === "object" && address !== null ? address.port : 0;
    apiUrl = `http://127.0.0.1:${port}`;
  });

  afterAll(async () => {
    await app?.close();
    await closePool();
  });

  it("fetchPing() → web merr round-trip nga db përmes API-t", async () => {
    const r = await fetchPing(apiUrl);
    expect(r.ok).toBe(true);
    expect(r.db.echo).toBe("pong");
    expect(typeof r.db.ts).toBe("string");
  });
});
