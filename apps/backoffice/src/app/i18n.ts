import { Injectable, signal } from '@angular/core';

/**
 * i18n MINIMAL foundation i shell-it (ADR-024: Shqip default; Anglisht +
 * Serbisht të detyrueshme si gjuhë produkti). VETËM resource keys neutrale
 * të shell-it — pa përkthime ligjore/fiskale dhe PA pretendim se U11/full
 * i18n është mbyllur (fallback/fonts/print fixtures janë deliverable F1+).
 */
export const LOCALES = ['sq', 'en', 'sr'] as const;
export type Locale = (typeof LOCALES)[number];
export const DEFAULT_LOCALE: Locale = 'sq';

export type MessageKey =
  | 'app.title'
  | 'app.subtitle'
  | 'nav.status'
  | 'status.heading'
  | 'status.loading'
  | 'status.error'
  | 'status.retry'
  | 'status.overall'
  | 'status.apiVersion'
  | 'status.database'
  | 'status.capabilities'
  | 'capability.identity'
  | 'capability.audit'
  | 'capability.outbox'
  | 'capability.observability';

export const MESSAGES: Record<Locale, Record<MessageKey, string>> = {
  sq: {
    'app.title': 'iData+',
    'app.subtitle': 'Backoffice — themeli i platformës',
    'nav.status': 'Statusi',
    'status.heading': 'Statusi i platformës',
    'status.loading': 'Duke ngarkuar…',
    'status.error': 'Gabim në komunikimin me API-n.',
    'status.retry': 'Riprovo',
    'status.overall': 'Gjendja',
    'status.apiVersion': 'Versioni i API-t',
    'status.database': 'Database',
    'status.capabilities': 'Capabilities të Bootstrap-it',
    'capability.identity': 'Identity',
    'capability.audit': 'Audit',
    'capability.outbox': 'Outbox',
    'capability.observability': 'Observability',
  },
  en: {
    'app.title': 'iData+',
    'app.subtitle': 'Backoffice — platform foundation',
    'nav.status': 'Status',
    'status.heading': 'Platform status',
    'status.loading': 'Loading…',
    'status.error': 'Failed to reach the API.',
    'status.retry': 'Retry',
    'status.overall': 'State',
    'status.apiVersion': 'API version',
    'status.database': 'Database',
    'status.capabilities': 'Bootstrap capabilities',
    'capability.identity': 'Identity',
    'capability.audit': 'Audit',
    'capability.outbox': 'Outbox',
    'capability.observability': 'Observability',
  },
  sr: {
    'app.title': 'iData+',
    'app.subtitle': 'Backoffice — osnova platforme',
    'nav.status': 'Status',
    'status.heading': 'Status platforme',
    'status.loading': 'Učitavanje…',
    'status.error': 'Greška u komunikaciji sa API-jem.',
    'status.retry': 'Pokušaj ponovo',
    'status.overall': 'Stanje',
    'status.apiVersion': 'Verzija API-ja',
    'status.database': 'Baza podataka',
    'status.capabilities': 'Bootstrap mogućnosti',
    'capability.identity': 'Identity',
    'capability.audit': 'Audit',
    'capability.outbox': 'Outbox',
    'capability.observability': 'Observability',
  },
};

@Injectable({ providedIn: 'root' })
export class TranslationService {
  readonly locale = signal<Locale>(DEFAULT_LOCALE);

  t(key: MessageKey): string {
    return MESSAGES[this.locale()][key];
  }
}
