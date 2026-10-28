import { Directive, ElementRef, computed, inject, input } from '@angular/core';
import { type ClassValue, cn } from './cn';

/*
 * shadcn/ui `base-nova` Tabs. The owner keeps which tab is active (usually a signal) and passes it
 * to each trigger, so the panels can be anything — including ones kept alive while hidden.
 *
 *   <div appTabsList aria-label="Source">
 *     <button appTabsTrigger [active]="tab() === 'html'" (click)="tab.set('html')">HTML</button>
 *   </div>
 */

@Directive({
  selector: '[appTabsList]',
  host: {
    role: 'tablist',
    'data-slot': 'tabs-list',
    'data-variant': 'default',
    '[class]': 'computedClass()',
    '(keydown)': 'moveFocus($event)',
  },
})
export class TabsList {
  readonly userClass = input<ClassValue>('', { alias: 'class' });
  private readonly element: HTMLElement = inject(ElementRef).nativeElement;

  protected readonly computedClass = computed(() =>
    cn(
      'group/tabs-list inline-flex h-8 w-fit items-center justify-center rounded-lg bg-muted p-[3px] text-muted-foreground',
      this.userClass(),
    ),
  );

  /** Arrow keys move between tabs and activate them, as the WAI-ARIA tabs pattern expects. */
  protected moveFocus(event: KeyboardEvent): void {
    const step = { ArrowRight: 1, ArrowLeft: -1 }[event.key];
    if (!step) return;

    const tabs = Array.from(this.element.querySelectorAll<HTMLElement>('[role=tab]:not([disabled])'));
    const current = tabs.indexOf(document.activeElement as HTMLElement);
    const next = tabs[(current + step + tabs.length) % tabs.length];
    next?.focus();
    next?.click();
    event.preventDefault();
  }
}

@Directive({
  selector: 'button[appTabsTrigger]',
  host: {
    type: 'button',
    role: 'tab',
    'data-slot': 'tabs-trigger',
    '[attr.aria-selected]': 'active()',
    '[attr.tabindex]': 'active() ? 0 : -1',
    '[attr.data-active]': 'active() || null',
    '[class]': 'computedClass()',
  },
})
export class TabsTrigger {
  readonly active = input(false);
  readonly userClass = input<ClassValue>('', { alias: 'class' });

  protected readonly computedClass = computed(() =>
    cn(
      "relative inline-flex h-[calc(100%-1px)] flex-1 cursor-pointer items-center justify-center gap-1.5 rounded-md border border-transparent px-2 py-0.5 text-sm font-medium whitespace-nowrap text-foreground/60 transition-all hover:text-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-1 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-50 dark:text-muted-foreground dark:hover:text-foreground data-active:bg-background data-active:text-foreground data-active:shadow-sm dark:data-active:border-input dark:data-active:bg-input/30 dark:data-active:text-foreground [&_app-icon:not([class*='size-'])]:size-3.5",
      this.userClass(),
    ),
  );
}

export const TABS = [TabsList, TabsTrigger] as const;
