import { Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { ClientContext } from '../../../../core/client-context/client-context';
import { ProblemDetails, toProblemDetails } from '../../../../core/http/problem-details';
import { HasUnsavedChanges } from '../../../../core/routing/unsaved-changes.guard';
import { Button } from '../../../../shared/ui/button';
import { Skeleton } from '../../../../shared/ui/feedback';
import { Icon } from '../../../../shared/ui/icon';
import { ProblemAlert } from '../../../../shared/ui/problem-alert';
import { ApiKey, IssueKeyRequest } from '../data-access/api-key.models';
import { ApiKeysApi } from '../data-access/api-keys.api';
import { CreateKeyDialog } from '../ui/create-key-dialog';
import { KeyRevealDialog } from '../ui/key-reveal-dialog';
import { KeysTable } from '../ui/keys-table';
import { RevokeKeyDialog } from '../ui/revoke-key-dialog';

/**
 * The chosen client's API keys: list with last use, create with a reveal-once dialog, revoke with
 * confirmation (US3–US5). The shell guarantees a chosen client before this page renders.
 */
@Component({
  selector: 'app-api-keys-page',
  imports: [Button, CreateKeyDialog, Icon, KeyRevealDialog, KeysTable, ProblemAlert, RevokeKeyDialog, Skeleton],
  template: `
    <header class="flex flex-wrap items-end justify-between gap-4">
      <div class="grid gap-1">
        <h1 class="text-2xl font-semibold tracking-tight">API keys</h1>
        <p class="max-w-2xl text-sm text-muted-foreground">
          Keys a client's backend uses to call this service. A key is shown once, when it is created.
        </p>
      </div>
      <button appButton type="button" (click)="openCreate()">
        <app-icon name="plus" data-icon="inline-start" />
        New key
      </button>
    </header>

    @if (disabled()) {
      <div role="status" class="flex items-center gap-2 rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-warning">
        <app-icon name="lock" />
        <p><span class="font-mono">{{ clientId() }}</span> is disabled. Its keys are refused until it is enabled again.</p>
      </div>
    }

    @if (actionProblem(); as problem) {
      <app-problem-alert [problem]="problem" [retryable]="false" />
    }

    @if (keys.hasValue()) {
      <app-keys-table [keys]="keys.value()" [clientId]="clientId()" (revoke)="startRevoke($event)" (create)="openCreate()" />
    } @else if (keys.error(); as error) {
      <app-problem-alert [problem]="problem(error)" (retry)="keys.reload()" />
    } @else {
      <div class="grid gap-3 rounded-lg border p-4" aria-busy="true" aria-label="Loading keys">
        @for (row of [1, 2, 3]; track row) {
          <div class="flex items-center gap-4">
            <div appSkeleton class="h-4 w-32"></div>
            <div appSkeleton class="h-4 w-36"></div>
            <div appSkeleton class="h-5 w-24 rounded-full"></div>
            <div appSkeleton class="ml-auto h-4 w-24"></div>
          </div>
        }
      </div>
    }

    @if (creating()) {
      <app-create-key-dialog
        [(open)]="creating"
        [clientId]="clientId()"
        [scopes]="scopes.hasValue() ? scopes.value() : null"
        [scopesProblem]="scopes.error() ? problem(scopes.error()) : null"
        [busy]="issuing()"
        [problem]="issueProblem()"
        (submitted)="issue($event)"
        (retryScopes)="scopes.reload()"
      />
    }

    @if (revealing(); as revealed) {
      <app-key-reveal-dialog [key]="revealed.key" [clientId]="revealed.clientId" (confirmed)="closeReveal()" />
    }

    @if (revoking(); as key) {
      <app-revoke-key-dialog
        [open]="true"
        (openChange)="$event || revoking.set(null)"
        [key]="key"
        [clientId]="clientId()"
        [busy]="revokeBusy()"
        (confirmed)="revoke(key)"
      />
    }
  `,
  host: { class: 'grid gap-6', '(window:beforeunload)': 'onBeforeUnload($event)' },
})
export class ApiKeysPage implements HasUnsavedChanges {
  private readonly api = inject(ApiKeysApi);
  private readonly context = inject(ClientContext);

  protected readonly clientId = computed(() => this.context.current() ?? '');
  protected readonly disabled = computed(() => this.context.currentClient()?.status === 'Disabled');

  protected readonly keys = rxResource({
    params: () => this.context.current() ?? undefined,
    stream: ({ params: clientId }) => this.api.list(clientId),
  });

  protected readonly creating = signal(false);
  protected readonly scopes = rxResource({
    params: () => (this.creating() ? true : undefined),
    stream: () => this.api.scopes(),
  });
  protected readonly issuing = signal(false);
  protected readonly issueProblem = signal<ProblemDetails | null>(null);

  /** The issued key, only until the operator confirms they copied it. */
  protected readonly revealing = signal<{ readonly key: string; readonly clientId: string } | null>(null);

  protected readonly revoking = signal<ApiKey | null>(null);
  protected readonly revokeBusy = signal(false);
  protected readonly actionProblem = signal<ProblemDetails | null>(null);

  protected readonly problem = (error: unknown) => toProblemDetails(error);

  /** While a key is on screen, leaving the page would lose it: ask first. */
  hasUnsavedChanges(): boolean {
    return this.revealing() !== null;
  }

  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) event.preventDefault();
  }

  protected openCreate(): void {
    this.issueProblem.set(null);
    this.creating.set(true);
  }

  protected async issue(request: IssueKeyRequest): Promise<void> {
    const clientId = this.clientId();
    this.issuing.set(true);
    this.issueProblem.set(null);
    try {
      const issued = await firstValueFrom(this.api.issue(clientId, request));
      this.creating.set(false);
      // Only the key goes to the reveal dialog; the rest of the response is not kept.
      this.revealing.set({ key: issued.key, clientId });
    } catch (error) {
      this.issueProblem.set(toProblemDetails(error));
    } finally {
      this.issuing.set(false);
    }
  }

  protected closeReveal(): void {
    this.revealing.set(null);
    this.keys.reload();
  }

  protected startRevoke(key: ApiKey): void {
    this.actionProblem.set(null);
    this.revoking.set(key);
  }

  protected async revoke(key: ApiKey): Promise<void> {
    this.revokeBusy.set(true);
    try {
      await firstValueFrom(this.api.revoke(this.clientId(), key.keyId), { defaultValue: null });
    } catch (error) {
      this.actionProblem.set(toProblemDetails(error));
    } finally {
      this.revokeBusy.set(false);
      this.revoking.set(null);
      this.keys.reload();
    }
  }
}
