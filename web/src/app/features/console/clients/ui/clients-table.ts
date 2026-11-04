import { Component, input, output } from '@angular/core';
import { Client } from '../../../../shared/data-access/client.models';
import { Badge } from '../../../../shared/ui/badge';
import { Button } from '../../../../shared/ui/button';
import { Icon } from '../../../../shared/ui/icon';
import { ScrollArea } from '../../../../shared/ui/scroll-area';
import { TABLE } from '../../../../shared/ui/table';
import { Timestamp } from '../../../../shared/ui/timestamp';

/** Every client, with disable or enable per row. Choosing a name works on that client. */
@Component({
  selector: 'app-clients-table',
  imports: [Badge, Button, Icon, ScrollArea, TABLE, Timestamp],
  template: `
    <div class="overflow-hidden rounded-lg border">
      <div appScrollArea orientation="horizontal" aria-label="Clients table">
        <table appTable>
          <thead appTableHeader>
            <tr appTableRow class="hover:bg-transparent">
              <th appTableHead class="pl-4">Name</th>
              <th appTableHead>Id</th>
              <th appTableHead>State</th>
              <th appTableHead>Created</th>
              <th appTableHead class="w-0"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody appTableBody>
            @for (client of clients(); track client.id) {
              <tr appTableRow [attr.data-client-id]="client.id">
                <td appTableCell class="pl-4">
                  <button
                    type="button"
                    class="font-medium underline-offset-4 outline-none hover:underline focus-visible:underline"
                    (click)="chosen.emit(client)"
                  >
                    {{ client.name }}
                  </button>
                </td>
                <td appTableCell><code class="font-mono text-xs">{{ client.id }}</code></td>
                <td appTableCell>
                  @if (client.status === 'Active') {
                    <span appBadge variant="success" role="status"><app-icon name="circle-check" data-icon="inline-start" />Active</span>
                  } @else {
                    <span appBadge variant="outline" role="status" class="text-muted-foreground"><app-icon name="lock" data-icon="inline-start" />Disabled</span>
                  }
                </td>
                <td appTableCell class="text-muted-foreground"><app-timestamp [value]="client.createdAt" mode="date" /></td>
                <td appTableCell class="pr-3 text-right">
                  @if (client.status === 'Active') {
                    <button appButton type="button" variant="ghost" size="sm" (click)="disable.emit(client)">Disable client</button>
                  } @else {
                    <button appButton type="button" variant="ghost" size="sm" [disabled]="busyId() === client.id" (click)="enable.emit(client)">
                      Enable client
                    </button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </div>
  `,
  host: { class: 'contents' },
})
export class ClientsTable {
  readonly clients = input.required<readonly Client[]>();
  /** A client whose change is in flight, so its button cannot be pressed twice. */
  readonly busyId = input<string | null>(null);

  readonly chosen = output<Client>();
  readonly disable = output<Client>();
  readonly enable = output<Client>();
}
