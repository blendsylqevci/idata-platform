import type { PoolConfig } from "pg";

/**
 * Konfigurim lidhjeje nga variablat standarde `PG*` (pg i lexon edhe vetë; këtu
 * i bëjmë eksplicitë hostin/portin/userin/databazën). Fjalëkalimi lexohet nga
 * env-i standard i pg-së — trust auth lokal → pa fjalëkalim, asnjë sekret në kod.
 */
export function dbConfigFromEnv(): PoolConfig {
  return {
    host: process.env.PGHOST ?? "localhost",
    port: Number(process.env.PGPORT ?? "5432"),
    user: process.env.PGUSER ?? "idata",
    database: process.env.PGDATABASE ?? "idata",
    max: 5,
  };
}
