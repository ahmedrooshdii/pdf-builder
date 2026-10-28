import { InjectionToken } from '@angular/core';

/**
 * `localStorage`, or `null` where it is unavailable (private windows, blocked site data).
 *
 * Injected rather than referenced as a global so callers degrade instead of throwing, and so tests
 * can hand in an in-memory fake.
 */
export const BROWSER_STORAGE = new InjectionToken<Storage | null>('BROWSER_STORAGE', {
  factory: () => {
    try {
      return globalThis.localStorage ?? null;
    } catch {
      return null;
    }
  },
});
