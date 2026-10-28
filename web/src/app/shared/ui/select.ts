import {
  Component,
  ElementRef,
  computed,
  input,
  model,
  output,
  signal,
  viewChild,
  viewChildren,
} from '@angular/core';
import { FormValueControl } from '@angular/forms/signals';
import { type ClassValue, cn } from './cn';
import { Icon } from './icon';

export interface SelectOption {
  readonly value: string;
  readonly label: string;
  /** A short tag on the right, for example "Built-in". */
  readonly hint?: string;
}

let nextId = 0;

/**
 * A select in shadcn's dropdown-menu style: a trigger like an Input, opening a menu of options.
 * Binds like a native input, to a signal form (`[formField]`) or two-way (`[(value)]`).
 *
 * The menu is a `popover="auto"`, so clicking outside or pressing Escape closes it with no extra
 * code. It follows the WAI-ARIA listbox pattern: arrows, Home/End, Enter or Space to choose, and
 * typing a letter jumps to the next option starting with it.
 */
@Component({
  selector: 'app-select',
  imports: [Icon],
  template: `
    <button
      #trigger
      type="button"
      data-slot="select-trigger"
      aria-haspopup="listbox"
      [id]="inputId()"
      [attr.aria-expanded]="open()"
      [attr.aria-controls]="listId"
      [attr.aria-invalid]="invalid() && touched() ? true : null"
      [attr.aria-describedby]="ariaDescribedBy() ?? null"
      [disabled]="disabled()"
      [class]="triggerClass()"
      (click)="toggle()"
      (keydown)="onTriggerKeydown($event)"
    >
      @if (selected(); as option) {
        <span class="truncate">{{ option.label }}</span>
        @if (option.hint) {
          <span class="text-xs text-muted-foreground">{{ option.hint }}</span>
        }
      } @else {
        <span class="truncate text-muted-foreground">{{ placeholder() }}</span>
      }
      <app-icon name="chevron-down" class="ml-auto size-4 text-muted-foreground transition-transform" [class.rotate-180]="open()" />
    </button>

    <div
      #menu
      popover="auto"
      role="listbox"
      tabindex="-1"
      data-slot="select-content"
      [id]="listId"
      [attr.aria-labelledby]="inputId()"
      [attr.data-open]="open() || null"
      class="fixed inset-auto m-0 max-h-72 min-w-32 overflow-x-hidden overflow-y-auto rounded-lg border-0 bg-popover p-1 text-popover-foreground shadow-md ring-1 ring-foreground/10 outline-none scrollbar-thin data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-open:slide-in-from-top-2"
      (toggle)="onMenuToggle($event)"
      (keydown)="onMenuKeydown($event)"
    >
      @for (option of options(); track option.value) {
        <!-- Keys are handled once, on the listbox (onMenuKeydown), as the WAI-ARIA listbox pattern does. -->
        <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events -->
        <div
          #item
          role="option"
          tabindex="-1"
          data-slot="select-item"
          [attr.aria-selected]="option.value === value()"
          class="relative flex cursor-default items-center gap-1.5 rounded-md py-1 pr-8 pl-1.5 text-sm outline-hidden select-none hover:bg-accent hover:text-accent-foreground focus:bg-accent focus:text-accent-foreground"
          (click)="choose(option.value)"
        >
          <span class="truncate">{{ option.label }}</span>
          @if (option.hint) {
            <span class="ml-auto text-xs text-muted-foreground">{{ option.hint }}</span>
          }
          @if (option.value === value()) {
            <app-icon name="check" class="absolute right-2 size-4" />
          }
        </div>
      } @empty {
        <div class="px-1.5 py-1 text-sm text-muted-foreground">{{ emptyText() }}</div>
      }
    </div>
  `,
  host: { class: 'block' },
})
export class Select implements FormValueControl<string> {
  readonly value = model('');
  readonly touch = output();
  readonly invalid = input(false);
  readonly touched = input(false);
  readonly disabled = input(false);

  readonly options = input.required<readonly SelectOption[]>();
  readonly placeholder = input('Select…');
  readonly emptyText = input('Nothing to choose from.');
  readonly inputId = input(`app-select-${nextId++}`);
  readonly ariaDescribedBy = input<string>();
  readonly userClass = input<ClassValue>('', { alias: 'class' });

  protected readonly listId = `app-select-list-${nextId++}`;
  protected readonly open = signal(false);
  protected readonly selected = computed(() => this.options().find((o) => o.value === this.value()));

  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly menu = viewChild.required<ElementRef<HTMLElement>>('menu');
  private readonly items = viewChildren<ElementRef<HTMLElement>>('item');

  protected readonly triggerClass = computed(() =>
    cn(
      'flex h-8 w-full min-w-0 cursor-pointer items-center gap-2 rounded-lg border border-input bg-transparent py-1 pr-2 pl-2.5 text-left text-sm whitespace-nowrap transition-colors outline-none select-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50 aria-expanded:border-ring aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 dark:bg-input/30 dark:hover:bg-input/50 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40',
      this.userClass(),
    ),
  );

  protected toggle(): void {
    if (this.open()) this.close();
    else this.show();
  }

  protected choose(value: string): void {
    this.value.set(value);
    this.close();
  }

  protected onTriggerKeydown(event: KeyboardEvent): void {
    if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(event.key)) {
      event.preventDefault();
      this.show();
    }
  }

  protected onMenuKeydown(event: KeyboardEvent): void {
    const items = this.items().map((i) => i.nativeElement);
    const current = items.indexOf(document.activeElement as HTMLElement);
    const focus = (index: number) => items[(index + items.length) % items.length]?.focus();

    switch (event.key) {
      case 'ArrowDown':
        focus(current + 1);
        break;
      case 'ArrowUp':
        focus(current - 1);
        break;
      case 'Home':
        focus(0);
        break;
      case 'End':
        focus(items.length - 1);
        break;
      case 'Enter':
      case ' ':
        if (current >= 0) this.choose(this.options()[current].value);
        break;
      case 'Tab':
        this.close();
        return;
      default: {
        if (event.key.length !== 1) return;
        // Typeahead: the next option after the current one whose label starts with the key.
        const key = event.key.toLowerCase();
        const options = this.options();
        for (let step = 1; step <= options.length; step++) {
          const index = (current + step) % options.length;
          if (options[index].label.toLowerCase().startsWith(key)) {
            focus(index);
            break;
          }
        }
      }
    }
    event.preventDefault();
  }

  /** Fires for every way the menu closes, including the browser's own light dismiss. */
  protected onMenuToggle(event: Event): void {
    const isOpen = (event as ToggleEvent).newState === 'open';
    this.open.set(isOpen);
    if (!isOpen) this.touch.emit();
  }

  private show(): void {
    const menu = this.menu().nativeElement;
    if (this.disabled() || typeof menu.showPopover !== 'function') return;

    const box = this.trigger().nativeElement.getBoundingClientRect();
    menu.style.minWidth = `${box.width}px`;
    menu.style.visibility = 'hidden';
    menu.showPopover();

    // Below the trigger, or above it when the menu would run off the bottom of the viewport.
    const viewport = document.documentElement;
    const below = box.bottom + 4;
    const top = below + menu.offsetHeight > viewport.clientHeight - 8 ? box.top - menu.offsetHeight - 4 : below;
    menu.style.top = `${Math.max(8, top)}px`;
    menu.style.left = `${Math.min(box.left, viewport.clientWidth - menu.offsetWidth - 8)}px`;
    menu.style.visibility = '';

    const options = this.options();
    const index = Math.max(0, options.findIndex((o) => o.value === this.value()));
    this.items()[index]?.nativeElement.focus();
  }

  private close(): void {
    const menu = this.menu().nativeElement;
    if (menu.matches(':popover-open')) menu.hidePopover();
    this.trigger().nativeElement.focus();
  }
}
