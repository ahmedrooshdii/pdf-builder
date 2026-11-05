import { Component, input, output } from '@angular/core';
import { ProblemDetails } from '../../core/http/problem-details';
import { Button } from './button';
import { Icon } from './icon';

/**
 * The error state of a view: the problem's title as the heading, its detail below, and its `code`
 * in muted monospace for searching the logs, plus "Try again" when the caller can retry.
 * A problem with status 0 already carries the title "The API could not be reached.".
 */
@Component({
  selector: 'app-problem-alert',
  imports: [Button, Icon],
  template: `
    <app-icon name="triangle-alert" class="mt-0.5" />
    <div class="grid flex-1 gap-1">
      <p class="font-medium">{{ problem().title ?? 'Something went wrong.' }}</p>
      @if (problem().detail; as detail) {
        <p class="text-destructive/90">{{ detail }}</p>
      }
      @if (problem().code; as code) {
        <p class="font-mono text-xs text-muted-foreground">{{ code }}</p>
      }
    </div>
    @if (retryable()) {
      <button appButton type="button" variant="outline" size="sm" (click)="retry.emit()">
        <app-icon name="refresh" data-icon="inline-start" />
        Try again
      </button>
    }
  `,
  host: {
    role: 'alert',
    class:
      'flex flex-wrap items-start gap-3 rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive',
  },
})
export class ProblemAlert {
  readonly problem = input.required<ProblemDetails>();
  readonly retryable = input(true);
  readonly retry = output();
}
