import { Injectable, InjectionToken, inject } from '@angular/core';
import {
  createPlatformApiClient,
  type PlatformStatusResponse,
} from '@idata/platform-api-client';

/**
 * Baza e API-t — default URL relative (dev proxy / same-origin në prod).
 * Testet japin një bazë absolute bashkë me fetch stub.
 */
export const PLATFORM_API_BASE = new InjectionToken<string>('PLATFORM_API_BASE', {
  providedIn: 'root',
  factory: () => '/',
});

/** Fetch i injektueshëm — testet japin stub; default = fetch i shfletuesit. */
export const PLATFORM_FETCH = new InjectionToken<(request: Request) => Promise<Response>>(
  'PLATFORM_FETCH',
  {
    providedIn: 'root',
    factory: () => (request) => fetch(request),
  },
);

/**
 * Shtresë e hollë mbi klientin e GJENERUAR nga kontrata OpenAPI — asnjë DTO
 * manual dhe asnjë fetch i shkruar me dorë që kopjon kontratën.
 */
@Injectable({ providedIn: 'root' })
export class PlatformStatusService {
  private readonly client = createPlatformApiClient({
    baseUrl: inject(PLATFORM_API_BASE),
    fetch: inject(PLATFORM_FETCH),
  });

  async load(): Promise<PlatformStatusResponse> {
    const { data, error } = await this.client.GET('/api/platform/status');
    if (error !== undefined || data === undefined) {
      throw new Error('platform-status-unavailable');
    }
    return data;
  }
}
