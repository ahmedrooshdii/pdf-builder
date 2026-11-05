import { Component, input, model, output } from '@angular/core';
import { Client } from '../../../../shared/data-access/client.models';
import { ALERT_DIALOG } from '../../../../shared/ui/alert-dialog';
import { Button } from '../../../../shared/ui/button';
import { Spinner } from '../../../../shared/ui/feedback';

/** Confirms disabling a client, naming it and saying what happens to its keys (FR-026). */
@Component({
  selector: 'app-disable-client-dialog',
  imports: [ALERT_DIALOG, Button, Spinner],
  template: `
    <app-alert-dialog [(open)]="open" labelledBy="disable-title" describedBy="disable-body">
      <div appDialogHeader class="pr-0">
        <h2 appDialogTitle id="disable-title" class="leading-snug">
          Disable {{ client().name }} (<code class="font-mono">{{ client().id }}</code>)?
        </h2>
        <p appDialogDescription id="disable-body">
          Its keys stop working within a minute, until you enable it again. Nothing is deleted.
        </p>
      </div>
      <div appDialogFooter>
        <button appButton appAlertDialogCancel type="button" variant="outline" (click)="open.set(false)">Cancel</button>
        <button appButton type="button" variant="destructive" [disabled]="busy()" (click)="confirmed.emit()">
          @if (busy()) {
            <app-spinner data-icon="inline-start" />
          }
          Disable client
        </button>
      </div>
    </app-alert-dialog>
  `,
  host: { class: 'contents' },
})
export class DisableClientDialog {
  readonly open = model(false);
  readonly client = input.required<Client>();
  readonly busy = input(false);

  readonly confirmed = output();
}
