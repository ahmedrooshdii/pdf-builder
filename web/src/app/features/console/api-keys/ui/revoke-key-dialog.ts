import { Component, input, model, output } from '@angular/core';
import { ALERT_DIALOG } from '../../../../shared/ui/alert-dialog';
import { Button } from '../../../../shared/ui/button';
import { Spinner } from '../../../../shared/ui/feedback';
import { ApiKey } from '../data-access/api-key.models';

/** Confirms a revocation, naming the key and the client, because it cannot be undone (FR-024). */
@Component({
  selector: 'app-revoke-key-dialog',
  imports: [ALERT_DIALOG, Button, Spinner],
  template: `
    <app-alert-dialog [(open)]="open" labelledBy="revoke-title" describedBy="revoke-body">
      <div appDialogHeader class="pr-0">
        <h2 appDialogTitle id="revoke-title" class="leading-snug">
          Revoke <span class="font-medium">{{ key().name }}</span> (<code class="font-mono">{{ key().display }}</code>) for
          <span class="font-mono">{{ clientId() }}</span>?
        </h2>
        <p appDialogDescription id="revoke-body">Calls with this key stop working within a minute. This cannot be undone.</p>
      </div>
      <div appDialogFooter>
        <button appButton appAlertDialogCancel type="button" variant="outline" (click)="open.set(false)">Cancel</button>
        <button appButton type="button" variant="destructive" [disabled]="busy()" (click)="confirmed.emit()">
          @if (busy()) {
            <app-spinner data-icon="inline-start" />
          }
          Revoke key
        </button>
      </div>
    </app-alert-dialog>
  `,
  host: { class: 'contents' },
})
export class RevokeKeyDialog {
  readonly open = model(false);
  readonly key = input.required<ApiKey>();
  readonly clientId = input.required<string>();
  readonly busy = input(false);

  readonly confirmed = output();
}
