import { createApp } from "./app.factory.js";

const app = await createApp();
const port = Number(process.env.API_PORT ?? "3001");
await app.listen(port);
console.log(`[api] dëgjon në http://localhost:${port}`);
