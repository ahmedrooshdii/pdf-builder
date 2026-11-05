import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Button } from '../../../shared/ui/button';
import { Skeleton } from '../../../shared/ui/feedback';
import { Icon } from '../../../shared/ui/icon';
import { ProblemAlert } from '../../../shared/ui/problem-alert';
import { ClientContext } from '../../client-context/client-context';
import { toProblemDetails } from '../../http/problem-details';

/**
 * Shown in place of a page that works within a client while none is chosen, so no page ever shows
 * one client's data with another named, or with none (FR-016).
 */
@Component({
  selector: 'app-choose-client',
  imports: [RouterLink, Button, Icon, ProblemAlert, Skeleton],
  template: `
    @if (context.clients.error(); as error) {
      <app-problem-alert [problem]="problem(error)" (retry)="context.reload()" />
    } @else if (!context.clients.hasValue()) {
      <div class="grid gap-3" aria-busy="true" aria-label="Loading clients">
        <div appSkeleton class="h-5 w-64"></div>
        <div appSkeleton class="h-8 w-80"></div>
      </div>
    } @else if (context.clients.value().length === 0) {
      <div class="flex flex-col items-center gap-3 rounded-lg border border-dashed px-6 py-16 text-center">
        <div class="flex size-10 items-center justify-center rounded-lg bg-muted">
          <app-icon name="users" class="size-5 text-muted-foreground" />
        </div>
        <p class="max-w-md text-sm text-muted-foreground">
          Add a client to get started. Each product that calls this service is a client.
        </p>
        <a appButton routerLink="/clients" [queryParams]="{ add: 1 }">
          <app-icon name="plus" data-icon="inline-start" />
          Add client
        </a>
      </div>
    } @else {
      <div class="grid gap-4 rounded-lg border px-6 py-8">
        @if (context.unknownClientId(); as unknown) {
          <p class="text-sm" role="alert">
            No client <code class="font-mono">{{ unknown }}</code> exists. Choose one.
          </p>
        } @else {
          <p class="text-sm">Choose the client to work on.</p>
        }
        <ul class="flex flex-wrap gap-2" aria-label="Clients">
          @for (client of context.clients.value(); track client.id) {
            <li>
              <button appButton type="button" variant="outline" (click)="context.select(client.id)">
                @if (client.status === 'Disabled') {
                  <app-icon name="lock" data-icon="inline-start" />
                }
                {{ client.name }}
                <span class="font-mono text-xs text-muted-foreground">{{ client.id }}</span>
              </button>
            </li>
          }
        </ul>
      </div>
    }
  `,
  host: { class: 'grid gap-6' },
})
export class ChooseClient {
  protected readonly context = inject(ClientContext);
  protected readonly problem = (error: unknown) => toProblemDetails(error);
}
