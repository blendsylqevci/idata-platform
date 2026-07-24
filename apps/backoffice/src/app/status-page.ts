import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import type { PlatformStatusResponse } from '@idata/platform-api-client';
import { TranslationService } from './i18n';
import { PlatformStatusService } from './platform-status.service';

type ViewState =
  | { kind: 'loading' }
  | { kind: 'ok'; data: PlatformStatusResponse }
  | { kind: 'error' };

/** Faqja e statusit — loading/success/error mbi klientin e gjeneruar. */
@Component({
  selector: 'app-status-page',
  templateUrl: './status-page.html',
  styleUrl: './status-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusPage {
  protected readonly i18n = inject(TranslationService);
  private readonly statusService = inject(PlatformStatusService);

  protected readonly state = signal<ViewState>({ kind: 'loading' });

  constructor() {
    void this.refresh();
  }

  protected async refresh(): Promise<void> {
    this.state.set({ kind: 'loading' });
    try {
      this.state.set({ kind: 'ok', data: await this.statusService.load() });
    } catch {
      this.state.set({ kind: 'error' });
    }
  }
}
