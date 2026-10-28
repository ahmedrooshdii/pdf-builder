import { Pipe, PipeTransform } from '@angular/core';

/**
 * Time between two instants, or a length in milliseconds: `312 ms`, `4.4 s`, `2 min 5 s`.
 * Empty when either end is missing, so a job that has not finished shows nothing.
 */
@Pipe({ name: 'duration' })
export class DurationPipe implements PipeTransform {
  transform(from: string | number | null | undefined, to?: string | null): string {
    if (from == null) return '';

    let ms: number;
    if (typeof from === 'number') {
      ms = from;
    } else {
      if (!to) return '';
      ms = Date.parse(to) - Date.parse(from);
    }

    if (!Number.isFinite(ms) || ms < 0) return '';
    if (ms < 1000) return `${Math.round(ms)} ms`;
    if (ms < 60_000) return `${(ms / 1000).toFixed(1)} s`;
    const minutes = Math.floor(ms / 60_000);
    return `${minutes} min ${Math.round((ms % 60_000) / 1000)} s`;
  }
}
