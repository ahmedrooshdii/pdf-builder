import { Component, Directive, computed, input } from '@angular/core';
import { type ClassValue, cn } from './cn';
import { Icon } from './icon';

/** shadcn/ui Skeleton: a pulsing placeholder in the shape of the content that is loading. */
@Directive({ selector: '[appSkeleton]', host: { 'data-slot': 'skeleton', '[class]': 'computedClass()' } })
export class Skeleton {
  readonly userClass = input<ClassValue>('', { alias: 'class' });
  protected readonly computedClass = computed(() => cn('animate-pulse rounded-md bg-muted', this.userClass()));
}

/** shadcn/ui Spinner. */
@Component({
  selector: 'app-spinner',
  imports: [Icon],
  template: `<app-icon name="loader" [class]="iconClass()" />`,
  host: { role: 'status', 'aria-label': 'Loading', 'data-slot': 'spinner', class: 'inline-flex' },
})
export class Spinner {
  readonly userClass = input<ClassValue>('', { alias: 'class' });
  protected readonly iconClass = computed(() => cn('size-4 animate-spin', this.userClass()));
}

/**
 * shadcn/ui Progress: label and value on one line, a 4px bar below. Pass `value` as 0–100, or
 * leave it out for an indeterminate bar.
 */
@Component({
  selector: 'app-progress',
  template: `
    <ng-content select="[appProgressLabel]" />
    <span data-slot="progress-value" class="ml-auto text-sm text-muted-foreground tabular-nums">
      <ng-content select="[appProgressValue]" />
    </span>
    <div
      data-slot="progress-track"
      role="progressbar"
      aria-valuemin="0"
      aria-valuemax="100"
      [attr.aria-valuenow]="value() ?? null"
      [attr.aria-label]="ariaLabel()"
      class="relative flex h-1 w-full items-center overflow-x-hidden rounded-full bg-muted"
    >
      @if (value() === undefined) {
        <div class="absolute h-full w-1/3 animate-[progress-indeterminate_1.4s_ease-in-out_infinite] rounded-full bg-primary"></div>
      } @else {
        <div data-slot="progress-indicator" class="h-full bg-primary transition-all duration-500" [style.width.%]="value()"></div>
      }
    </div>
  `,
  styles: `
    @keyframes progress-indeterminate {
      from { left: -33%; }
      to { left: 100%; }
    }
  `,
  host: { 'data-slot': 'progress', '[class]': 'computedClass()' },
})
export class Progress {
  readonly value = input<number>();
  readonly ariaLabel = input<string>('Progress');
  readonly userClass = input<ClassValue>('', { alias: 'class' });
  protected readonly computedClass = computed(() => cn('flex flex-wrap gap-3', this.userClass()));
}

@Directive({ selector: '[appProgressLabel]', host: { 'data-slot': 'progress-label', class: 'text-sm font-medium' } })
export class ProgressLabel {}

@Directive({ selector: '[appProgressValue]' })
export class ProgressValue {}

export const PROGRESS = [Progress, ProgressLabel, ProgressValue] as const;
