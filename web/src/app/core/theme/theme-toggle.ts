import { Component, computed, inject } from '@angular/core';
import { Icon } from '../../shared/ui/icon';
import { Switch } from '../../shared/ui/switch';
import { ThemeService } from './theme.service';

/**
 * One switch for light and dark, with the current mode's icon riding on the thumb. `D` toggles it
 * from anywhere except while typing.
 */
@Component({
  selector: 'app-theme-toggle',
  imports: [Icon, Switch],
  template: `
    <button
      appSwitch
      size="lg"
      aria-label="Dark mode"
      [checked]="isDark()"
      (checkedChange)="theme.theme.set($event ? 'dark' : 'light')"
    >
      <app-icon [name]="isDark() ? 'moon' : 'sun'" class="size-3 text-foreground" />
    </button>
  `,
  host: { class: 'inline-flex', '(document:keydown)': 'onKeydown($event)' },
})
export class ThemeToggle {
  protected readonly theme = inject(ThemeService);
  protected readonly isDark = computed(() => this.theme.theme() === 'dark');

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key.toLowerCase() !== 'd' || event.ctrlKey || event.metaKey || event.altKey || event.repeat) return;
    if (isTyping(event.target)) return;
    this.theme.toggle();
  }
}

/** True inside inputs, selects, textareas and contenteditable regions such as the code editor. */
function isTyping(target: EventTarget | null): boolean {
  return (
    target instanceof HTMLElement &&
    (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))
  );
}
