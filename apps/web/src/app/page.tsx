import { fetchPing } from "@/lib/ping";

// Faqja thërret API-n në kohë kërkese (jo në build) → build s'ka nevojë për API.
export const dynamic = "force-dynamic";

export default async function Home() {
  let status: string;
  try {
    const ping = await fetchPing();
    status = ping.ok
      ? `web → api → db OK · echo=${ping.db.echo} · ts=${ping.db.ts}`
      : "API u përgjigj por jo ok";
  } catch (err) {
    status = `API nuk u arrit: ${(err as Error).message}`;
  }

  return (
    <main style={{ fontFamily: "system-ui, sans-serif", padding: 24 }}>
      <h1>iData+ — Walking Skeleton</h1>
      <p data-testid="ping-result">{status}</p>
    </main>
  );
}
