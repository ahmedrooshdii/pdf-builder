import { Component, input, output } from '@angular/core';
import { Badge } from '../../../../shared/ui/badge';
import { Button } from '../../../../shared/ui/button';
import { CopyButton } from '../../../../shared/ui/copy-button';
import { DROPDOWN_MENU } from '../../../../shared/ui/dropdown-menu';
import { Icon } from '../../../../shared/ui/icon';
import { ScrollArea } from '../../../../shared/ui/scroll-area';
import { TABLE } from '../../../../shared/ui/table';
import { Timestamp } from '../../../../shared/ui/timestamp';
import { ApiKey } from '../data-access/api-key.models';

/**
 * A client's keys (contracts/console-ui.md "Table"). Shows only the public part of each key. Exact
 * UTC times open on hover and on keyboard focus. "Revoke key" is offered for active keys only.
 */
@Component({
  selector: 'app-keys-table',
  imports: [Badge, Button, CopyButton, DROPDOWN_MENU, Icon, ScrollArea, TABLE, Timestamp],
  template: `
    @if (keys().length > 0) {
      <div class="overflow-hidden rounded-lg border">
        <div appScrollArea orientation="horizontal" aria-label="API keys table">
          <table appTable>
            <thead appTableHeader>
              <tr appTableRow class="hover:bg-transparent">
                <th appTableHead class="pl-4">Name</th>
                <th appTableHead>Key</th>
                <th appTableHead>Scopes</th>
                <th appTableHead>Last used</th>
                <th appTableHead>Created</th>
                <th appTableHead>Expires</th>
                <th appTableHead>State</th>
                <th appTableHead class="w-0"><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody appTableBody>
              @for (key of keys(); track key.keyId) {
                <tr appTableRow [attr.data-key-id]="key.keyId">
                  <td appTableCell class="pl-4 font-medium">{{ key.name }}</td>
                  <td appTableCell>
                    <span class="inline-flex items-center gap-1">
                      <code class="font-mono text-xs">{{ key.display }}</code>
                      <app-copy-button [value]="key.keyId" [ariaLabel]="'Copy the key id ' + key.keyId" />
                    </span>
                  </td>
                  <td appTableCell>
                    <span class="flex flex-wrap gap-1">
                      @for (scope of key.scopes; track scope) {
                        <span appBadge variant="outline" class="font-mono">{{ scope }}</span>
                      }
                    </span>
                  </td>
                  <td appTableCell class="text-muted-foreground"><app-timestamp [value]="key.lastUsedAt" /></td>
                  <td appTableCell class="text-muted-foreground"><app-timestamp [value]="key.createdAt" mode="date" /></td>
                  <td appTableCell class="text-muted-foreground"><app-timestamp [value]="key.expiresAt" mode="date" /></td>
                  <td appTableCell>
                    @switch (key.status) {
                      @case ('Active') {
                        <span appBadge variant="success" role="status"><app-icon name="circle-check" data-icon="inline-start" />Active</span>
                      }
                      @case ('Expired') {
                        <span appBadge variant="secondary" role="status"><app-icon name="clock" data-icon="inline-start" />Expired</span>
                      }
                      @default {
                        <span appBadge variant="outline" role="status" class="text-muted-foreground"><app-icon name="lock" data-icon="inline-start" />Revoked</span>
                      }
                    }
                  </td>
                  <td appTableCell class="pr-3">
                    @if (key.status === 'Active') {
                      <app-dropdown-menu align="end" [label]="'Actions for ' + key.name">
                        <button appButton appDropdownMenuTrigger type="button" variant="ghost" size="icon-sm" [attr.aria-label]="'Actions for ' + key.name">
                          <app-icon name="ellipsis" />
                        </button>
                        <button appDropdownMenuItem variant="destructive" type="button" (click)="revoke.emit(key)">
                          <app-icon name="lock" />
                          Revoke key
                        </button>
                      </app-dropdown-menu>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </div>
    } @else {
      <div class="flex flex-col items-center gap-3 rounded-lg border border-dashed px-6 py-16 text-center">
        <div class="flex size-10 items-center justify-center rounded-lg bg-muted">
          <app-icon name="key-round" class="size-5 text-muted-foreground" />
        </div>
        <p class="text-sm text-muted-foreground">No keys yet for <span class="font-mono">{{ clientId() }}</span>.</p>
        <button appButton type="button" variant="outline" (click)="create.emit()">
          <app-icon name="plus" data-icon="inline-start" />
          Create a key
        </button>
      </div>
    }
  `,
  host: { class: 'contents' },
})
export class KeysTable {
  readonly keys = input.required<readonly ApiKey[]>();
  readonly clientId = input.required<string>();

  readonly revoke = output<ApiKey>();
  readonly create = output();
}
