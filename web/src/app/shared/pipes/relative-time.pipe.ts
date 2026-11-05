import { formatDate } from '@angular/common';
import { Pipe, PipeTransform, inject } from '@angular/core';
import { NOW } from '../util/clock';

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/**
 * How long ago: "never" for no time, "less than a minute ago", "3 minutes ago", "5 hours ago", and
 * the date ("2 Nov 2026") from a day on. Pure, so it reads the clock when its input changes, which
 * is when the list holding it reloads.
 */
@Pipe({ name: 'relativeTime' })
export class RelativeTimePipe implements PipeTransform {
  private readonly now = inject(NOW);

  transform(value: string | null | undefined): string {
    if (!value) return 'never';
    const at = Date.parse(value);
    if (Number.isNaN(at)) return '';

    const ago = Math.max(0, this.now() - at);
    if (ago < MINUTE) return 'less than a minute ago';
    if (ago < HOUR) return plural(Math.floor(ago / MINUTE), 'minute');
    if (ago < DAY) return plural(Math.floor(ago / HOUR), 'hour');
    return formatDate(at, 'd MMM y', 'en-US', 'UTC');
  }
}

function plural(count: number, unit: string): string {
  return `${count} ${unit}${count === 1 ? '' : 's'} ago`;
}
