import { DOCUMENT, Injectable, effect, inject, signal } from '@angular/core';
import { BROWSER_STORAGE } from '../storage/browser-storage';

export type Theme = 'light' | 'dark';

/** Must match the key `public/theme-init.js` reads before the app boots. */
export const THEME_STORAGE_KEY = 'pdf-builder.theme';

/**
 * Light or dark, applied as the `dark` class on `<html>` (Tailwind's `dark:` variant keys off it).
 *
 * Starts from the saved choice, else the system's. `theme-init.js` applies the same rule before
 * Angular loads, so there is no flash of the wrong theme; this service owns it from then on.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly storage = inject(BROWSER_STORAGE);
  private readonly document = inject(DOCUMENT);

  readonly theme = signal<Theme>(this.initial());

  constructor() {
    effect(() => {
      const theme = this.theme();
      this.document.documentElement.classList.toggle('dark', theme === 'dark');
      try {
        this.storage?.setItem(THEME_STORAGE_KEY, theme);
      } catch {
        // Storage refused: the choice lasts for this session only.
      }
    });
  }

  toggle(): void {
    this.theme.update((theme) => (theme === 'dark' ? 'light' : 'dark'));
  }

  private initial(): Theme {
    let saved: string | null = null;
    try {
      saved = this.storage?.getItem(THEME_STORAGE_KEY) ?? null;
    } catch {
      // Fall through to the system preference.
    }
    if (saved === 'light' || saved === 'dark') return saved;
    return this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
}
