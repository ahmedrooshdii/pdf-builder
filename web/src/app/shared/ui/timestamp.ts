import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { RelativeTimePipe } from '../pipes/relative-time.pipe';
import { formatUtc } from '../util/clock';
import { HoverCard } from './hover-card';

/**
 * A time as people read it ("3 minutes ago", or "12 Oct 2026"), with the exact UTC time one hover or
 * one Tab away: the trigger is focusable and the hover card opens on focus too, and screen readers
 * get the exact time as part of the text. `null` reads as `empty` ("never" by default).
 */
@Component({
  selector: 'app-timestamp',
  imports: [DatePipe, HoverCard, RelativeTimePipe],
  template: `
    @if (value(); as at) {
      <app-hover-card [openDelay]="300" width="auto">
        <time
          appHoverCardTrigger
          tabindex="0"
          class="rounded-sm underline decoration-dotted decoration-muted-foreground/50 underline-offset-4 outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
          [attr.datetime]="at"
        >
          {{ mode() === 'relative' ? (at | relativeTime) : (at | date: 'd MMM y') }}
          <span class="sr-only">({{ exact() }})</span>
        </time>
        <span appHoverCardContent class="font-mono text-xs whitespace-nowrap">{{ exact() }}</span>
      </app-hover-card>
    } @else {
      {{ empty() }}
    }
  `,
  host: { class: 'contents' },
})
export class Timestamp {
  readonly value = input<string | null | undefined>();
  readonly mode = input<'relative' | 'date'>('relative');
  readonly empty = input('never');

  protected readonly exact = computed(() => {
    const value = this.value();
    return value ? formatUtc(value) : '';
  });
}
