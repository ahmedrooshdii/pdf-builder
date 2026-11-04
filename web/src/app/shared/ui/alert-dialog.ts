import { Component, Directive, input, model } from '@angular/core';
import { DIALOG } from './dialog';

/**
 * shadcn/ui Alert Dialog: a confirmation that interrupts, for actions that cannot be undone.
 *
 *   <app-alert-dialog [(open)]="confirming" labelledBy="x-title" describedBy="x-body">
 *     <div appDialogHeader>
 *       <h2 appDialogTitle id="x-title">Revoke …?</h2>
 *       <p appDialogDescription id="x-body">This cannot be undone.</p>
 *     </div>
 *     <div appDialogFooter>
 *       <button appButton appAlertDialogCancel variant="outline" (click)="confirming.set(false)">Cancel</button>
 *       <button appButton variant="destructive" (click)="revoke()">Revoke key</button>
 *     </div>
 *   </app-alert-dialog>
 *
 * Unlike a plain dialog it has no corner close button and ignores backdrop clicks: the choice is
 * made with its buttons. Escape still means Cancel. Focus starts on the Cancel button, so Enter
 * right after opening never confirms by accident.
 */
@Component({
  selector: 'app-alert-dialog',
  imports: [DIALOG],
  template: `
    <app-dialog
      role="alertdialog"
      [(open)]="open"
      [labelledBy]="labelledBy()"
      [describedBy]="describedBy()"
      [showClose]="false"
      [backdropCloses]="false"
    >
      <ng-content />
    </app-dialog>
  `,
  host: { class: 'contents' },
})
export class AlertDialog {
  readonly open = model(false);
  readonly labelledBy = input.required<string>();
  readonly describedBy = input<string>();
}

/** Marks the button that cancels, which takes focus when the dialog opens. */
@Directive({ selector: 'button[appAlertDialogCancel]', host: { autofocus: '', 'data-slot': 'alert-dialog-cancel' } })
export class AlertDialogCancel {}

export const ALERT_DIALOG = [AlertDialog, AlertDialogCancel, ...DIALOG] as const;
