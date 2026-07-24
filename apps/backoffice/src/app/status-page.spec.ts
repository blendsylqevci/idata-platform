import { TestBed } from '@angular/core/testing';
import { StatusPage } from './status-page';
import { PLATFORM_API_BASE, PLATFORM_FETCH } from './platform-status.service';
import { syntheticOkResponse } from './testing/synthetic-status';

/** Faqja e statusit përmes klientit të GJENERUAR — loading/success/retry/error. */
describe('StatusPage', () => {
  function errorResponse(): Response {
    return new Response('{}', { status: 503, headers: { 'Content-Type': 'application/json' } });
  }

  async function configure(fetchStub: (request: Request) => Promise<Response>) {
    await TestBed.configureTestingModule({
      imports: [StatusPage],
      providers: [
        { provide: PLATFORM_API_BASE, useValue: 'http://api.invalid' },
        { provide: PLATFORM_FETCH, useValue: fetchStub },
      ],
    }).compileComponents();
    return TestBed.createComponent(StatusPage);
  }

  // Në zoneless, whenStable() nuk gjurmon Promise-t e papërpunuara të fetch-it;
  // një tik macrotask e derdh plotësisht zinxhirin fetch → parse → signal.
  const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

  it('success: tregon fushat e kontratës nga klienti i gjeneruar', async () => {
    const calls: string[] = [];
    const fixture = await configure((request) => {
      calls.push(request.url);
      return Promise.resolve(syntheticOkResponse());
    });
    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(calls).toEqual(['http://api.invalid/api/platform/status']);
    expect(el.querySelector('[data-testid="overall"]')?.textContent).toContain('OK');
    expect(el.querySelector('[data-testid="api-version"]')?.textContent).toContain('platform-v1');
    expect(el.querySelector('[data-testid="database"]')?.textContent).toContain('READY');
    expect(el.querySelector('[data-testid="capability-identity"]')?.textContent)
      .toContain('NOT_CONFIGURED_F1_PENDING');
  });

  it('error: API e paarritshme tregon gjendje gabimi të kontrolluar me retry (sq default)', async () => {
    const fixture = await configure(() => Promise.resolve(errorResponse()));
    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Gabim në komunikimin me API-n.');
    expect(el.querySelector('button')?.textContent).toContain('Riprovo');
  });

  it('loading: para resolve tregon loading, pas resolve kalon në success', async () => {
    let resolveFetch!: (response: Response) => void;
    const deferred = new Promise<Response>((resolve) => { resolveFetch = resolve; });
    const fixture = await configure(() => deferred);

    // Krijimi thërret refresh() → state = loading; para resolve, loading i dukshëm.
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.state-loading')?.textContent).toContain('Duke ngarkuar…');
    expect(el.querySelector('[data-testid="overall"]')).toBeNull();

    resolveFetch(syntheticOkResponse());
    await flush();
    fixture.detectChanges();

    expect(el.querySelector('.state-loading')).toBeNull();
    expect(el.querySelector('[data-testid="overall"]')?.textContent).toContain('OK');
  });

  it('retry: 503 → klik Riprovo → loading gjatë kërkesës së dytë → success; calls = 2', async () => {
    const calls: string[] = [];
    let resolveSecond!: (response: Response) => void;
    const secondDeferred = new Promise<Response>((resolve) => { resolveSecond = resolve; });
    let call = 0;
    const fixture = await configure((request) => {
      calls.push(request.url);
      return call++ === 0 ? Promise.resolve(errorResponse()) : secondDeferred;
    });

    // Kërkesa e parë → error + Riprovo.
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[role="alert"]')).not.toBeNull();
    const retry = el.querySelector('button');
    expect(retry?.textContent).toContain('Riprovo');
    expect(calls.length).toBe(1);

    // Klik REAL i Riprovo → kërkesa e dytë (deferred) → loading i dukshëm.
    retry!.click();
    await flush();
    fixture.detectChanges();
    expect(calls.length).toBe(2);
    expect(el.querySelector('.state-loading')).not.toBeNull();
    expect(el.querySelector('[role="alert"]')).toBeNull();

    // Response e dytë OK → error hiqet, success shfaqet.
    resolveSecond(syntheticOkResponse());
    await flush();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')).toBeNull();
    expect(el.querySelector('[data-testid="overall"]')?.textContent).toContain('OK');
    expect(calls).toEqual([
      'http://api.invalid/api/platform/status',
      'http://api.invalid/api/platform/status',
    ]);
  });
});
