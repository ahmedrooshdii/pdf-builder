import { Component, input } from '@angular/core';
import { ReadonlyFieldState } from '@angular/forms/signals';

/**
 * The error messages of one signal-form field, shown once the user has left the field (or tried
 * to submit), so typing into an empty input does not shout at them on the first keystroke.
 */
@Component({
  selector: 'app-field-errors',
  template: `
    @if (field().touched()) {
      @for (error of field().errors(); track $index) {
        <p>{{ error.message ?? error.kind }}</p>
      }
    }
  `,
  host: { class: 'grid gap-1 text-sm text-destructive empty:hidden', 'aria-live': 'polite' },
})
export class FieldErrors {
  readonly field = input.required<ReadonlyFieldState<unknown>>();
}
