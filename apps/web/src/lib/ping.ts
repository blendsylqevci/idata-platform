export interface PingResponse {
  ok: boolean;
  db: { echo: string; ts: string };
}

/**
 * Thërret API-n (`GET /ping`) — hallka web → api → db.
 * Base URL nga env `API_URL` (default localhost) ose e kaluar eksplicit (teste).
 */
export async function fetchPing(
  baseUrl: string = process.env.API_URL ?? "http://localhost:3001",
): Promise<PingResponse> {
  const res = await fetch(`${baseUrl}/ping`, { cache: "no-store" });
  if (!res.ok) {
    throw new Error(`API /ping ktheu ${res.status}`);
  }
  return (await res.json()) as PingResponse;
}
