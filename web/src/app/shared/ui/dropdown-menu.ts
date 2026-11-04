import { Component, Directive, ElementRef, computed, inject, input, signal, viewChild } from '@angular/core';
import { type ClassValue, cn } from './cn';

let nextMenuId = 0;

/**
 * shadcn/ui `base-nova` Dropdown Menu.
 *
 *   <app-dropdown-menu align="end" label="Session">
 *     <button appButton appDropdownMenuTrigger>…</button>
 *     <div appDropdownMenuLabel>Signed in</div>
 *     <button appDropdownMenuItem (click)="signOut()">Sign out</button>
 *   </app-dropdown-menu>
 *
 * The content is a `popover="auto"` element: it renders in the top layer (never clipped by the
 * header), and a click elsewhere closes it. Keyboard follows the WAI-ARIA menu button pattern:
 * Enter, Space or ArrowDown on the trigger opens it on the first item, ArrowUp on the last; arrows,
 * Home and End move between items; Escape closes and returns focus to the trigger; Tab closes.
 */
@Component({
  selector: 'app-dropdown-menu',
  template: `
    <ng-content select="[appDropdownMenuTrigger]" />
    <div
      #panel
      popover="auto"
      tabindex="-1"
      role="menu"
      data-slot="dropdown-menu-content"
      [id]="menuId"
      [attr.aria-label]="label()"
      [attr.data-open]="open() || null"
      [class]="computedClass()"
      (toggle)="onToggle($event)"
      (keydown)="onKeydown($event)"
    >
      <ng-content />
    </div>
  `,
  host: { class: 'contents' },
})
export class DropdownMenu {
  readonly align = input<'start' | 'end'>('start');
  readonly label = input<string>();
  readonly userClass = input<ClassValue>('', { alias: 'class' });

  readonly menuId = `app-dropdown-menu-${nextMenuId++}`;
  readonly open = signal(false);

  private readonly panel = viewChild.required<ElementRef<HTMLElement>>('panel');
  private trigger: HTMLElement | null = null;

  protected readonly computedClass = computed(() =>
    cn(
      'fixed inset-auto m-0 min-w-48 max-w-[calc(100vw-1rem)] overflow-hidden rounded-lg border-0 bg-popover p-1 text-sm text-popover-foreground shadow-md ring-1 ring-foreground/10 outline-none data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95',
      this.userClass(),
    ),
  );

  registerTrigger(element: HTMLElement): void {
    this.trigger = element;
  }

  toggle(): void {
    if (this.open()) this.close();
    else this.show('first');
  }

  show(focus: 'first' | 'last'): void {
    const panel = this.panel().nativeElement;
    if (!this.open() && typeof panel.showPopover === 'function') panel.showPopover();
    this.open.set(true);
    this.position(panel);
    const items = this.items();
    (focus === 'first' ? items[0] : items.at(-1))?.focus();
  }

  close(restoreFocus = true): void {
    const panel = this.panel().nativeElement;
    if (this.open() && typeof panel.hidePopover === 'function') panel.hidePopover();
    this.open.set(false);
    if (restoreFocus) this.trigger?.focus();
  }

  /** The browser closed it (a click outside): keep the state in step, leave focus where it went. */
  protected onToggle(event: Event): void {
    if ((event as ToggleEvent).newState === 'closed' && this.open()) this.open.set(false);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const items = this.items();
    const index = items.indexOf(document.activeElement as HTMLElement);
    const move = (to: number) => {
      event.preventDefault();
      items[(to + items.length) % items.length]?.focus();
    };

    switch (event.key) {
      case 'ArrowDown':
        return move(index + 1);
      case 'ArrowUp':
        return move(index < 0 ? items.length - 1 : index - 1);
      case 'Home':
        return move(0);
      case 'End':
        return move(items.length - 1);
      case 'Escape':
        event.preventDefault();
        return this.close();
      case 'Tab':
        return this.close(false);
    }
  }

  private items(): HTMLElement[] {
    return Array.from(
      this.panel().nativeElement.querySelectorAll<HTMLElement>('[role="menuitem"]:not([disabled]):not([aria-disabled="true"])'),
    );
  }

  private position(panel: HTMLElement): void {
    if (!this.trigger) return;
    const box = this.trigger.getBoundingClientRect();
    const width = panel.offsetWidth || 192;
    const left = this.align() === 'end' ? box.right - width : box.left;
    panel.style.top = `${box.bottom + 4}px`;
    panel.style.left = `${Math.max(8, Math.min(left, innerWidth - width - 8))}px`;
  }
}

/** The button that opens the menu. */
@Directive({
  selector: '[appDropdownMenuTrigger]',
  host: {
    'aria-haspopup': 'menu',
    '[attr.aria-expanded]': 'menu.open()',
    '[attr.aria-controls]': 'menu.menuId',
    '(click)': 'menu.toggle()',
    '(keydown)': 'onKeydown($event)',
  },
})
export class DropdownMenuTrigger {
  protected readonly menu = inject(DropdownMenu);

  constructor() {
    this.menu.registerTrigger(inject<ElementRef<HTMLElement>>(ElementRef).nativeElement);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      this.menu.show(event.key === 'ArrowDown' ? 'first' : 'last');
    }
  }
}

/** One choice. Choosing it closes the menu. Works on `<button>` and on `<a routerLink>`. */
@Directive({
  selector: '[appDropdownMenuItem]',
  host: {
    role: 'menuitem',
    tabindex: '-1',
    'data-slot': 'dropdown-menu-item',
    '[class]': 'computedClass()',
    '(click)': 'menu.close()',
  },
})
export class DropdownMenuItem {
  readonly variant = input<'default' | 'destructive'>('default');
  readonly userClass = input<ClassValue>('', { alias: 'class' });

  protected readonly menu = inject(DropdownMenu);

  protected readonly computedClass = computed(() =>
    cn(
      "relative flex w-full cursor-default items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm outline-hidden select-none hover:bg-accent hover:text-accent-foreground focus:bg-accent focus:text-accent-foreground disabled:pointer-events-none disabled:opacity-50 [&_app-icon:not([class*='size-'])]:size-4 [&_app-icon]:text-muted-foreground",
      this.variant() === 'destructive' && 'text-destructive hover:bg-destructive/10 focus:bg-destructive/10 [&_app-icon]:text-destructive',
      this.userClass(),
    ),
  );
}

@Directive({
  selector: '[appDropdownMenuLabel]',
  host: { 'data-slot': 'dropdown-menu-label', class: 'px-2 py-1.5 text-xs font-medium text-muted-foreground' },
})
export class DropdownMenuLabel {}

@Directive({
  selector: '[appDropdownMenuSeparator]',
  host: { role: 'separator', 'data-slot': 'dropdown-menu-separator', class: '-mx-1 my-1 block h-px bg-border' },
})
export class DropdownMenuSeparator {}

export const DROPDOWN_MENU = [
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
] as const;
