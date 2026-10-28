import { Component, computed, input, model } from '@angular/core';
import { type ClassValue, cn } from './cn';
import { Icon } from './icon';

/**
 * shadcn/ui `base-nova` Checkbox, as a native button with `role="checkbox"`. `indeterminate`
 * shows a dash, for a "select all" box when only some rows are selected.
 */
@Component({
  // Hosted on a native <button> so focus, Space/Enter and disabled behave correctly with no extra code.
  // eslint-disable-next-line @angular-eslint/component-selector
  selector: 'button[appCheckbox]',
  imports: [Icon],
  template: `
    @if (checked() || indeterminate()) {
      <span data-slot="checkbox-indicator" class="grid place-content-center text-current [&_app-icon]:size-3.5">
        @if (indeterminate() && !checked()) {
          <span class="h-0.5 w-2 rounded-full bg-current"></span>
        } @else {
          <app-icon name="check" />
        }
      </span>
    }
  `,
  host: {
    type: 'button',
    role: 'checkbox',
    'data-slot': 'checkbox',
    '[attr.aria-checked]': 'indeterminate() && !checked() ? "mixed" : checked()',
    '[attr.data-checked]': 'checked() || indeterminate() || null',
    '[class]': 'computedClass()',
    '(click)': 'checked.set(!checked())',
  },
})
export class Checkbox {
  readonly checked = model(false);
  readonly indeterminate = input(false);
  readonly userClass = input<ClassValue>('', { alias: 'class' });

  protected readonly computedClass = computed(() =>
    cn(
      'peer relative flex size-4 shrink-0 cursor-pointer items-center justify-center rounded-[4px] border border-input transition-colors outline-none after:absolute after:-inset-x-3 after:-inset-y-2 focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30 data-checked:border-primary data-checked:bg-primary data-checked:text-primary-foreground dark:data-checked:bg-primary',
      this.userClass(),
    ),
  );
}
