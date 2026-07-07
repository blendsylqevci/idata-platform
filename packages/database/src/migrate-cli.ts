import { runMigrations } from "./migrate.js";

const res = await runMigrations();
console.log(`[migrate] applied=${res.applied.length} ${JSON.stringify(res.applied)}`);
console.log(`[migrate] skipped=${res.skipped.length} (tashmë të aplikuara)`);
