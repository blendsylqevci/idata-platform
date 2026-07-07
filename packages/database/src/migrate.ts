import { readdir, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import pg from "pg";
import { dbConfigFromEnv } from "./config.js";

const { Client } = pg;

const MIGRATIONS_DIR = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
  "migrations",
);

/** Çelës i dedikuar advisory-lock për serializim migrimesh (teste paralele/CI). */
const ADVISORY_LOCK_KEY = 727274;

export interface MigrateResult {
  /** Migrimet e aplikuara tani (bosh nëse s'ka të reja). */
  readonly applied: string[];
  /** Migrimet e kapërcyera se ishin aplikuar më parë. */
  readonly skipped: string[];
}

/**
 * Runner minimal i versionuar: aplikon çdo `NNNN_*.sql` të pa-aplikuar (sipas emrit),
 * secilin në transaksionin e vet, dhe e regjistron te `schema_migrations`.
 * Idempotent: ri-ekzekutimi kur s'ka migrime të reja → `applied = []`.
 */
export async function runMigrations(): Promise<MigrateResult> {
  const client = new Client(dbConfigFromEnv());
  await client.connect();
  const applied: string[] = [];
  const skipped: string[] = [];
  try {
    await client.query("SELECT pg_advisory_lock($1)", [ADVISORY_LOCK_KEY]);
    try {
      await client.query(
        `CREATE TABLE IF NOT EXISTS schema_migrations (
           version    text PRIMARY KEY,
           applied_at timestamptz NOT NULL DEFAULT now()
         )`,
      );
      const files = (await readdir(MIGRATIONS_DIR))
        .filter((f) => f.endsWith(".sql"))
        .sort();
      const doneRes = await client.query<{ version: string }>(
        "SELECT version FROM schema_migrations",
      );
      const done = new Set(doneRes.rows.map((r) => r.version));

      for (const file of files) {
        if (done.has(file)) {
          skipped.push(file);
          continue;
        }
        const sql = await readFile(path.join(MIGRATIONS_DIR, file), "utf8");
        await client.query("BEGIN");
        try {
          await client.query(sql);
          await client.query("INSERT INTO schema_migrations(version) VALUES ($1)", [file]);
          await client.query("COMMIT");
          applied.push(file);
        } catch (err) {
          await client.query("ROLLBACK");
          throw err;
        }
      }
    } finally {
      await client.query("SELECT pg_advisory_unlock($1)", [ADVISORY_LOCK_KEY]);
    }
  } finally {
    await client.end();
  }
  return { applied, skipped };
}
