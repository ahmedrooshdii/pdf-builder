import { Component, OnDestroy, input, output, signal } from '@angular/core';
import { Button } from './button';
import { Icon } from './icon';

/**
 * Copies `value` to the clipboard. Says "Copied" for two seconds; emits `failed` when the browser
 * refuses (no permission, an insecure origin, no Clipboard API), so the caller can offer a fallback.
 */
@Component({
  selector: 'app-copy-button',
  imports: [Button, Icon],
  template: `
    <button
      appButton
      type="button"
      [variant]="variant()"
      [size]="label() ? 'sm' : 'icon-xs'"
      [attr.aria-label]="label() ? null : copied() ? 'Copied' : ariaLabel()"
      (click)="copy()"
    >
      <app-icon [name]="copied() ? 'check' : 'copy'" [attr.data-icon]="label() ? 'inline-start' : null" />
      @if (label()) {
        {{ copied() ? 'Copied' : label() }}
      }
    </button>
    <span class="sr-only" aria-live="polite">{{ copied() ? 'Copied' : '' }}</span>
  `,
  host: { class: 'contents' },
})
export class CopyButton implements OnDestroy {
  readonly value = input.required<string>();
  /** Visible text. Without it the button is icon-only, named by `ariaLabel`. */
  readonly label = input<string>();
  readonly ariaLabel = input('Copy');
  readonly variant = input<'ghost' | 'outline' | 'default'>('ghost');

  readonly failed = output();

  protected readonly copied = signal(false);
  private timer: ReturnType<typeof setTimeout> | undefined;

  protected async copy(): Promise<void> {
    try {
      if (!navigator.clipboard?.writeText) throw new Error('No clipboard.');
      await navigator.clipboard.writeText(this.value());
      this.copied.set(true);
      clearTimeout(this.timer);
      this.timer = setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.failed.emit();
    }
  }

  ngOnDestroy(): void {
    clearTimeout(this.timer);
  }
}
