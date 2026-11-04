import { Component, Directive, ElementRef, afterRenderEffect, computed, input, model, viewChild } from '@angular/core';
import { Button } from './button';
import { type ClassValue, cn } from './cn';
import { Icon } from './icon';

/**
 * shadcn/ui `base-nova` Dialog, on the native `<dialog>` element, which already provides the focus
 * trap, Escape to close, the top layer and inert background content.
 *
 * `dismissible = false` is for a dialog the user must answer, such as a secret shown once: no close
 * button, no backdrop click, Escape does nothing, and if the browser closes it anyway (Chrome lets a
 * second Escape through a prevented `cancel`), it is opened again at once. Only setting `open` to
 * false closes it.
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
      [attr.role]="role() === 'alertdialog' ? 'alertdialog' : null"
      [attr.aria-labelledby]="labelledBy()"
      [attr.aria-describedby]="describedBy()"
      (cancel)="onCancel($event)"
      (close)="onClose()"
      (click)="closeOnBackdrop($event)"
    >
      <ng-content />
      @if (showCloseButton()) {
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
      }
    </dialog>
  `,
  host: { class: 'contents' },
})
export class Dialog {
  readonly open = model(false);
  /** Id of the element that names the dialog, for screen readers. */
  readonly labelledBy = input<string>();
  /** Id of the element that describes the dialog, for screen readers. */
  readonly describedBy = input<string>();
  /** False: only the dialog's own buttons can close it (see the class comment). */
  readonly dismissible = input(true);
  /** The corner close button. Never shown when the dialog is not dismissible. */
  readonly showClose = input(true);
  /** Whether a click on the backdrop closes the dialog. Never when it is not dismissible. */
  readonly backdropCloses = input(true);
  readonly role = input<'dialog' | 'alertdialog'>('dialog');
  readonly userClass = input<ClassValue>('', { alias: 'class' });

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly showCloseButton = computed(() => this.dismissible() && this.showClose());

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

  protected onCancel(event: Event): void {
    if (!this.dismissible()) event.preventDefault();
  }

  /** The element closed. For a dialog that must be answered, a close nobody asked for is undone. */
  protected onClose(): void {
    if (!this.dismissible() && this.open()) {
      this.dialog().nativeElement.showModal();
      return;
    }
    this.open.set(false);
  }

  /** A click whose target is the dialog itself landed on the backdrop, outside the content box. */
  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement && this.dismissible() && this.backdropCloses()) {
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

/** The button row at the foot of a dialog, on the muted strip shadcn uses. */
@Directive({
  selector: '[appDialogFooter]',
  host: {
    'data-slot': 'dialog-footer',
    class: '-mx-4 -mb-4 flex flex-col-reverse gap-2 rounded-b-xl border-t bg-muted/50 p-4 sm:flex-row sm:justify-end',
  },
})
export class DialogFooter {}

export const DIALOG = [Dialog, DialogHeader, DialogTitle, DialogDescription, DialogFooter] as const;
