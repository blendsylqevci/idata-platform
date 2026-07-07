import pg from "pg";
import type { Pool } from "pg";
import { dbConfigFromEnv } from "./config.js";

const { Pool: PgPool } = pg;

let pool: Pool | undefined;

/** Singleton pool (lazy). Përdor konfigurimin nga env-i PG*. */
export function getPool(): Pool {
  if (!pool) {
    pool = new PgPool(dbConfigFromEnv());
  }
  return pool;
}

/** Mbyll pool-in (për teardown në teste / shutdown i pastër). */
export async function closePool(): Promise<void> {
  if (pool) {
    await pool.end();
    pool = undefined;
  }
}
