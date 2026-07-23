import { Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { TranslationService } from './i18n';

/** Shell minimal e accessible i Backoffice-it — zero ekrane biznesi. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly i18n = inject(TranslationService);
}
