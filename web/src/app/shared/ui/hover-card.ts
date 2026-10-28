import { Component, ElementRef, OnDestroy, input, signal, viewChild } from '@angular/core';

/**
 * shadcn/ui Hover Card: rich detail that appears after hovering (or focusing) a trigger.
 *
 *   <app-hover-card>
 *     <a appHoverCardTrigger …>…</a>
 *     <div appHoverCardContent>…</div>
 *   </app-hover-card>
 *
 * The content is a `popover` element, so it renders in the top layer and is never clipped by a
 * scrolling table. It is positioned once when it opens, from the trigger's box. Like shadcn's, it
 * is supplementary: everything in it must also be reachable another way (here, the job page).
 */
@Component({
  selector: 'app-hover-card',
  template: `
    <span
      class="contents"
      (mouseenter)="scheduleOpen()"
      (mouseleave)="scheduleClose()"
      (focusin)="scheduleOpen()"
      (focusout)="scheduleClose()"
    >
      <ng-content select="[appHoverCardTrigger]" />
    </span>
    <div
      #panel
      popover="manual"
      data-slot="hover-card-content"
      [attr.data-open]="open() || null"
      [style.top.px]="top()"
      [style.left.px]="left()"
      class="fixed inset-auto m-0 w-64 origin-top rounded-lg border-0 bg-popover p-2.5 text-sm text-popover-foreground shadow-md ring-1 ring-foreground/10 outline-hidden data-open:animate-in data-open:fade-in-0 data-open:zoom-in-95 data-open:slide-in-from-top-2"
      [style.width]="width()"
      (mouseenter)="scheduleOpen()"
      (mouseleave)="scheduleClose()"
    >
      <ng-content select="[appHoverCardContent]" />
    </div>
  `,
  host: { class: 'contents' },
})
export class HoverCard implements OnDestroy {
  readonly openDelay = input(500);
  readonly closeDelay = input(150);
  readonly width = input<string>();

  protected readonly open = signal(false);
  protected readonly top = signal(0);
  protected readonly left = signal(0);

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('panel');
  private timer: ReturnType<typeof setTimeout> | undefined;
  private trigger: HTMLElement | null = null;

  protected scheduleOpen(): void {
    clearTimeout(this.timer);
    if (this.open()) return;
    this.timer = setTimeout(() => this.show(), this.openDelay());
  }

  protected scheduleClose(): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.hide(), this.closeDelay());
  }

  private show(): void {
    const panel = this.host().nativeElement;
    this.trigger ??= panel.parentElement?.querySelector<HTMLElement>('[appHoverCardTrigger]') ?? null;
    if (!this.trigger || typeof panel.showPopover !== 'function') return;

    const box = this.trigger.getBoundingClientRect();
    const width = panel.offsetWidth || 256;
    this.top.set(box.bottom + 4);
    this.left.set(Math.max(8, Math.min(box.left + box.width / 2 - width / 2, innerWidth - width - 8)));
    panel.showPopover();
    this.open.set(true);
  }

  private hide(): void {
    const panel = this.host().nativeElement;
    if (this.open() && typeof panel.hidePopover === 'function') panel.hidePopover();
    this.open.set(false);
  }

  ngOnDestroy(): void {
    clearTimeout(this.timer);
  }
}
