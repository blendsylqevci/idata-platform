import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { DEFAULT_LOCALE, LOCALES, MESSAGES, TranslationService } from './i18n';
import { PLATFORM_API_BASE, PLATFORM_FETCH } from './platform-status.service';
import { routes } from './app.routes';
import { syntheticOkResponse } from './testing/synthetic-status';

describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes),
        { provide: PLATFORM_API_BASE, useValue: 'http://api.invalid' },
        { provide: PLATFORM_FETCH, useValue: () => Promise.resolve(syntheticOkResponse()) },
      ],
    }).compileComponents();
  });

  it('renderon shell-in me emrin iData+', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('iData+');
    expect(compiled.querySelector('main')).not.toBeNull();
    expect(compiled.querySelector('header')).not.toBeNull();
  });

  it('locale default = sq dhe çelësat ekzistojnë për sq/en/sr me paritet të plotë', () => {
    const i18n = TestBed.inject(TranslationService);
    expect(i18n.locale()).toBe('sq');
    expect(DEFAULT_LOCALE).toBe('sq');

    const referenceKeys = Object.keys(MESSAGES.sq).sort();
    for (const locale of LOCALES) {
      expect(Object.keys(MESSAGES[locale]).sort()).toEqual(referenceKeys);
      for (const key of referenceKeys) {
        expect(MESSAGES[locale][key as keyof typeof MESSAGES.sq]).not.toBe('');
      }
    }
  });
});
