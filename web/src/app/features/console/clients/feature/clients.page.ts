import { Component, OnInit, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ClientContext } from '../../../../core/client-context/client-context';
import { ProblemDetails, toProblemDetails } from '../../../../core/http/problem-details';
import { Client } from '../../../../shared/data-access/client.models';
import { ClientsApi } from '../../../../shared/data-access/clients.api';
import { Button } from '../../../../shared/ui/button';
import { Skeleton } from '../../../../shared/ui/feedback';
import { Icon } from '../../../../shared/ui/icon';
import { ProblemAlert } from '../../../../shared/ui/problem-alert';
import { AddClientDialog } from '../ui/add-client-dialog';
import { ClientsTable } from '../ui/clients-table';
import { DisableClientDialog } from '../ui/disable-client-dialog';

/**
 * Every client: add, disable (confirmed) and enable (US6). Works without a chosen client, so a new
 * deployment can be set up from here. The list is the client context's, so the switcher and this
 * page never disagree.
 */
@Component({
  selector: 'app-clients-page',
  imports: [AddClientDialog, Button, ClientsTable, DisableClientDialog, Icon, ProblemAlert, Skeleton],
  template: `
    <header class="flex flex-wrap items-end justify-between gap-4">
      <div class="grid gap-1">
        <h1 class="text-2xl font-semibold tracking-tight">Clients</h1>
        <p class="max-w-2xl text-sm text-muted-foreground">
          Each product that calls this service is a client. Keys belong to a client.
        </p>
      </div>
      <button appButton type="button" (click)="openAdd()">
        <app-icon name="plus" data-icon="inline-start" />
        Add client
      </button>
    </header>

    @if (actionProblem(); as problem) {
      <app-problem-alert [problem]="problem" [retryable]="false" />
    }

    @if (context.clients.hasValue()) {
      @if (context.clients.value().length > 0) {
        <app-clients-table
          [clients]="context.clients.value()"
          [busyId]="busyId()"
          (chosen)="choose($event)"
          (disable)="disabling.set($event)"
          (enable)="enable($event)"
        />
      } @else {
        <div class="flex flex-col items-center gap-3 rounded-lg border border-dashed px-6 py-16 text-center">
          <div class="flex size-10 items-center justify-center rounded-lg bg-muted">
            <app-icon name="users" class="size-5 text-muted-foreground" />
          </div>
          <p class="max-w-md text-sm text-muted-foreground">
            Add a client to get started. Each product that calls this service is a client.
          </p>
          <button appButton type="button" variant="outline" (click)="openAdd()">
            <app-icon name="plus" data-icon="inline-start" />
            Add client
          </button>
        </div>
      }
    } @else if (context.clients.error(); as error) {
      <app-problem-alert [problem]="problem(error)" (retry)="context.reload()" />
    } @else {
      <div class="grid gap-3 rounded-lg border p-4" aria-busy="true" aria-label="Loading clients">
        @for (row of [1, 2, 3]; track row) {
          <div class="flex items-center gap-4">
            <div appSkeleton class="h-4 w-32"></div>
            <div appSkeleton class="h-4 w-24"></div>
            <div appSkeleton class="h-5 w-20 rounded-full"></div>
          </div>
        }
      </div>
    }

    @if (adding()) {
      <app-add-client-dialog [(open)]="adding" [busy]="addBusy()" [problem]="addProblem()" (submitted)="register($event)" />
    }

    @if (disabling(); as client) {
      <app-disable-client-dialog
        [open]="true"
        (openChange)="$event || disabling.set(null)"
        [client]="client"
        [busy]="busyId() === client.id"
        (confirmed)="disable(client)"
      />
    }
  `,
  host: { class: 'grid gap-6' },
})
export class ClientsPage implements OnInit {
  /** `?add=1` opens the add dialog: the "Add client" links elsewhere in the console use it. */
  readonly add = input<string>();

  protected readonly context = inject(ClientContext);
  private readonly api = inject(ClientsApi);

  protected readonly adding = signal(false);
  protected readonly addBusy = signal(false);
  protected readonly addProblem = signal<ProblemDetails | null>(null);
  protected readonly disabling = signal<Client | null>(null);
  protected readonly busyId = signal<string | null>(null);
  protected readonly actionProblem = signal<ProblemDetails | null>(null);

  protected readonly problem = (error: unknown) => toProblemDetails(error);

  ngOnInit(): void {
    if (this.add()) this.openAdd();
  }

  protected openAdd(): void {
    this.addProblem.set(null);
    this.adding.set(true);
  }

  protected async register(client: { id: string; name: string }): Promise<void> {
    this.addBusy.set(true);
    this.addProblem.set(null);
    try {
      const created = await firstValueFrom(this.api.create(client));
      this.adding.set(false);
      this.context.reload();
      // The new client is the one the operator will work on next.
      await this.context.select(created.id, '/clients');
    } catch (error) {
      this.addProblem.set(toProblemDetails(error));
    } finally {
      this.addBusy.set(false);
    }
  }

  protected choose(client: Client): void {
    void this.context.select(client.id, '/keys');
  }

  protected async disable(client: Client): Promise<void> {
    await this.change(client, true);
    this.disabling.set(null);
  }

  protected enable(client: Client): Promise<void> {
    return this.change(client, false);
  }

  private async change(client: Client, disabled: boolean): Promise<void> {
    this.busyId.set(client.id);
    this.actionProblem.set(null);
    try {
      await firstValueFrom(this.api.update(client.id, { disabled }));
    } catch (error) {
      this.actionProblem.set(toProblemDetails(error));
    } finally {
      this.busyId.set(null);
      this.context.reload();
    }
  }
}
