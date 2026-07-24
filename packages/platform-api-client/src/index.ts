/**
 * Klient i TIPIZUAR i Platform API — i ndërtuar VETËM mbi tipet e gjeneruara
 * nga `contracts/openapi/platform-v1.json` (openapi-typescript) dhe klientin
 * callable `openapi-fetch`. Asnjë DTO manual dhe asnjë kopjim i kontratës:
 * path-et, parametrat dhe përgjigjet vijnë nga tipi i gjeneruar `paths`.
 */
import createClient, { type Client } from "openapi-fetch";
import type { paths } from "./generated/platform-v1.js";

export type { paths } from "./generated/platform-v1.js";

export type PlatformApiClient = Client<paths>;

/** Përgjigja e tipizuar e statusit — direkt nga kontrata e gjeneruar. */
export type PlatformStatusResponse =
  paths["/api/platform/status"]["get"]["responses"]["200"]["content"]["application/json"];

export interface PlatformApiClientOptions {
  /** Default "/" — Angular shell përdor dev proxy / URL relative. */
  baseUrl?: string;
  /** Fetch i personalizuar (teste/dev) — pa varësi vendor-i. */
  fetch?: (request: Request) => Promise<Response>;
}

export function createPlatformApiClient(
  options: PlatformApiClientOptions = {},
): PlatformApiClient {
  return createClient<paths>({
    baseUrl: options.baseUrl ?? "/",
    fetch: options.fetch,
  });
}
