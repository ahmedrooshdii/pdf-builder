import { InjectionToken } from '@angular/core';

/** The current time in milliseconds. Injected so tests can fix it. */
export const NOW = new InjectionToken<() => number>('NOW', { factory: () => () => Date.now() });

/** `2026-11-04 19:04 UTC`: the exact time, for tooltips next to relative ones. */
export function formatUtc(value: string | Date): string {
  const date = typeof value === 'string' ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) return '';
  return `${date.toISOString().slice(0, 16).replace('T', ' ')} UTC`;
}

/** `UTC+02:00` for the browser's offset at `date`. */
export function formatUtcOffset(date: Date): string {
  const minutes = -date.getTimezoneOffset();
  const sign = minutes < 0 ? '-' : '+';
  const abs = Math.abs(minutes);
  return `UTC${sign}${String(Math.floor(abs / 60)).padStart(2, '0')}:${String(abs % 60).padStart(2, '0')}`;
}
