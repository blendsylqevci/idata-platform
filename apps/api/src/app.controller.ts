import { Controller, Get } from "@nestjs/common";
import { getPool, ping as dbPing } from "@idata/database";

/**
 * Controller minimal (pa DI): thërret `@idata/database` direkt.
 * - GET /health  → status statik.
 * - GET /ping    → round-trip real në DB.
 */
@Controller()
export class AppController {
  @Get("health")
  health(): { status: "ok" } {
    return { status: "ok" };
  }

  @Get("ping")
  async ping(): Promise<{ ok: boolean; db: { echo: string; ts: string } }> {
    const r = await dbPing(getPool());
    return { ok: true, db: { echo: r.echo, ts: r.ts } };
  }
}
