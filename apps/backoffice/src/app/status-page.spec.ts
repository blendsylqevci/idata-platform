import { TestBed } from '@angular/core/testing';
import { StatusPage } from './status-page';
import { PLATFORM_API_BASE, PLATFORM_FETCH } from './platform-status.service';
import { syntheticOkResponse } from './testing/synthetic-status';

/** Faqja e statusit përmes klientit të GJENERUAR — success dhe error state. */
describe('StatusPage', () => {
  async function render(fetchStub: (request: Request) => Promise<Response>) {
    await TestBed.configureTestingModule({
      imports: [StatusPage],
      providers: [
        { provide: PLATFORM_API_BASE, useValue: 'http://api.invalid' },
        { provide: PLATFORM_FETCH, useValue: fetchStub },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(StatusPage);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('success: tregon fushat e kontratës nga klienti i gjeneruar', async () => {
    const calls: string[] = [];
    const fixture = await render((request) => {
      calls.push(request.url);
      return Promise.resolve(syntheticOkResponse());
    });

    const el = fixture.nativeElement as HTMLElement;
    expect(calls).toEqual(['http://api.invalid/api/platform/status']);
    expect(el.querySelector('[data-testid="overall"]')?.textContent).toContain('OK');
    expect(el.querySelector('[data-testid="api-version"]')?.textContent).toContain('platform-v1');
    expect(el.querySelector('[data-testid="database"]')?.textContent).toContain('READY');
    expect(el.querySelector('[data-testid="capability-identity"]')?.textContent)
      .toContain('NOT_CONFIGURED_F1_PENDING');
  });

  it('error: API e paarritshme tregon gjendje gabimi të kontrolluar me retry (sq default)', async () => {
    const fixture = await render(() =>
      Promise.resolve(new Response('{}', { status: 503, headers: { 'Content-Type': 'application/json' } })));

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Gabim në komunikimin me API-n.');
    expect(el.querySelector('button')?.textContent).toContain('Riprovo');
  });
});
