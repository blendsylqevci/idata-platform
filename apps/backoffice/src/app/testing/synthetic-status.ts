import type { PlatformStatusResponse } from '@idata/platform-api-client';

/** Fixture SINTETIKE me formën e kontratës së gjeneruar — zero të dhëna reale. */
export const syntheticStatus: PlatformStatusResponse = {
  status: 'OK',
  apiVersion: 'platform-v1',
  database: 'READY',
  capabilities: {
    identity: 'NOT_CONFIGURED_F1_PENDING',
    audit: 'NOT_CONFIGURED_F1_PENDING',
    outbox: 'NOT_CONFIGURED_F1_PENDING',
    observability: 'NOT_CONFIGURED_F1_PENDING',
  },
};

export function syntheticOkResponse(): Response {
  return new Response(JSON.stringify(syntheticStatus), {
    headers: { 'Content-Type': 'application/json' },
  });
}
