import { Component, computed, input, model } from '@angular/core';
import { type ClassValue, cn } from './cn';

/**
 * shadcn/ui `base-nova` Switch, as a native button with `role="switch"`. Content projected into
 * it is drawn inside the thumb, for an icon that moves with it (size `lg` leaves room for one).
 */
@Component({
  // Hosted on a native <button> so focus, Space/Enter and disabled behave correctly with no extra code.
  // eslint-disable-next-line @angular-eslint/component-selector
  selector: 'button[appSwitch]',
  template: `
    <span
      data-slot="switch-thumb"
      [attr.data-checked]="checked() || null"
      [attr.data-unchecked]="!checked() || null"
      class="pointer-events-none flex items-center justify-center rounded-full bg-background ring-0 transition-transform duration-200 group-data-[size=default]/switch:size-4 group-data-[size=sm]/switch:size-3 group-data-[size=lg]/switch:size-5 group-data-[size=default]/switch:data-checked:translate-x-[calc(100%-2px)] group-data-[size=sm]/switch:data-checked:translate-x-[calc(100%-2px)] group-data-[size=lg]/switch:data-checked:translate-x-[calc(100%+2px)] data-unchecked:translate-x-0 dark:data-checked:bg-primary-foreground dark:data-unchecked:bg-foreground"
    >
      <ng-content />
    </span>
  `,
  host: {
    type: 'button',
    role: 'switch',
    'data-slot': 'switch',
    '[attr.aria-checked]': 'checked()',
    '[attr.data-size]': 'size()',
    '[attr.data-checked]': 'checked() || null',
    '[attr.data-unchecked]': '!checked() || null',
    '[class]': 'computedClass()',
    '(click)': 'checked.set(!checked())',
  },
})
export class Switch {
  readonly checked = model(false);
  readonly size = input<'sm' | 'default' | 'lg'>('default');
  readonly userClass = input<ClassValue>('', { alias: 'class' });

  protected readonly computedClass = computed(() =>
    cn(
      'peer group/switch relative inline-flex shrink-0 cursor-pointer items-center rounded-full border border-transparent transition-all outline-none after:absolute after:-inset-x-3 after:-inset-y-2 focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 data-[size=default]:h-[18.4px] data-[size=default]:w-[32px] data-[size=sm]:h-[14px] data-[size=sm]:w-[24px] data-[size=lg]:h-6 data-[size=lg]:w-11 data-[size=lg]:px-px data-checked:bg-primary data-unchecked:bg-input dark:data-unchecked:bg-input/80 disabled:cursor-not-allowed disabled:opacity-50',
      this.userClass(),
    ),
  );
}
