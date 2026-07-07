import type { Pool } from "pg";

export interface PingResult {
  readonly ok: true;
  readonly echo: string;
  readonly ts: string;
}

/**
 * Round-trip real në DB: dërgon një parametër dhe merr kohën e serverit.
 * Provon që lidhja punon dhe që databaza ekzekuton query me parametra realë.
 */
export async function ping(pool: Pool): Promise<PingResult> {
  const res = await pool.query<{ echo: string; ts: string }>(
    "SELECT $1::text AS echo, now()::text AS ts",
    ["pong"],
  );
  const row = res.rows[0];
  if (!row || row.echo !== "pong") {
    throw new Error("DB ping round-trip dështoi");
  }
  return { ok: true, echo: row.echo, ts: row.ts };
}
