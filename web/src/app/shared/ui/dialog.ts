import { Component, Directive, ElementRef, afterRenderEffect, computed, input, model, viewChild } from '@angular/core';
import { Button } from './button';
import { type ClassValue, cn } from './cn';
import { Icon } from './icon';

/**
 * shadcn/ui `base-nova` Dialog, on the native `<dialog>` element, which already provides the focus
 * trap, Escape to close, the top layer and inert background content.
 */
@Component({
  selector: 'app-dialog',
  imports: [Button, Icon],
  template: `
    <!-- The backdrop click is a mouse shortcut; its keyboard equivalent is Escape, which <dialog> handles itself. -->
    <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -->
    <dialog
      #dialog
      data-slot="dialog-content"
      [class]="computedClass()"
      [attr.aria-labelledby]="labelledBy()"
      (close)="open.set(false)"
      (click)="closeOnBackdrop($event)"
    >
      <ng-content />
      <button
        appButton
        type="button"
        variant="ghost"
        size="icon-sm"
        class="absolute top-2 right-2"
        aria-label="Close"
        (click)="open.set(false)"
      >
        <app-icon name="x" />
      </button>
    </dialog>
  `,
  host: { class: 'contents' },
})
export class Dialog {
  readonly open = model(false);
  /** Id of the element that names the dialog, for screen readers. */
  readonly labelledBy = input<string>();
  readonly userClass = input<ClassValue>('', { alias: 'class' });

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly computedClass = computed(() =>
    cn(
      'm-auto w-full max-w-[calc(100%-2rem)] gap-4 rounded-xl border-0 bg-popover p-4 text-sm text-popover-foreground shadow-lg ring-1 ring-foreground/10 outline-none open:grid sm:max-w-sm open:animate-in open:fade-in-0 open:zoom-in-95 backdrop:bg-black/10 backdrop:backdrop-blur-xs',
      this.userClass(),
    ),
  );

  constructor() {
    afterRenderEffect(() => {
      const element = this.dialog().nativeElement;
      if (this.open() && !element.open) {
        element.showModal();
      } else if (!this.open() && element.open) {
        element.close();
      }
    });
  }

  /** A click whose target is the dialog itself landed on the backdrop, outside the content box. */
  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.open.set(false);
    }
  }
}

@Directive({ selector: '[appDialogHeader]', host: { 'data-slot': 'dialog-header', class: 'flex flex-col gap-2 pr-8' } })
export class DialogHeader {}

@Directive({
  selector: '[appDialogTitle]',
  host: { 'data-slot': 'dialog-title', class: 'text-base leading-none font-medium' },
})
export class DialogTitle {}

@Directive({
  selector: '[appDialogDescription]',
  host: { 'data-slot': 'dialog-description', class: 'text-sm text-muted-foreground' },
})
export class DialogDescription {}

export const DIALOG = [Dialog, DialogHeader, DialogTitle, DialogDescription] as const;
