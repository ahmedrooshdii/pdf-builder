import { DatePipe } from '@angular/common';
import { Component, computed, input, model, output, signal } from '@angular/core';
import { FormField, FormRoot, form, maxLength, required, validate } from '@angular/forms/signals';
import { ProblemDetails } from '../../../../core/http/problem-details';
import { Button } from '../../../../shared/ui/button';
import { Checkbox } from '../../../../shared/ui/checkbox';
import { DIALOG } from '../../../../shared/ui/dialog';
import { Skeleton, Spinner } from '../../../../shared/ui/feedback';
import { FieldErrors } from '../../../../shared/ui/field-errors';
import { Input, Label } from '../../../../shared/ui/form-controls';
import { ProblemAlert } from '../../../../shared/ui/problem-alert';
import { formatUtcOffset } from '../../../../shared/util/clock';
import { IssueKeyRequest, KEY_NAME_MAX_LENGTH, Scope } from '../data-access/api-key.models';

interface KeyDraft {
  name: string;
  scopes: string[];
  /** `datetime-local` value, in the browser's time zone; empty for no expiry. */
  expiresAt: string;
}

/** The value of a `datetime-local` input as an instant, or null when empty or unreadable. */
function parseLocal(value: string): Date | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

/**
 * The form for a new key (contracts/console-ui.md "Create dialog"). Validates as the API does; the
 * page sends the request and passes back field errors from a 400 and whether it is busy.
 */
@Component({
  selector: 'app-create-key-dialog',
  imports: [DatePipe, FormRoot, FormField, Button, Checkbox, DIALOG, FieldErrors, Input, Label, ProblemAlert, Skeleton, Spinner],
  template: `
    <app-dialog [(open)]="open" labelledBy="create-key-title" class="sm:max-w-lg">
      <div appDialogHeader>
        <h2 appDialogTitle id="create-key-title">New key for <strong class="font-mono">{{ clientId() }}</strong></h2>
        <p appDialogDescription>The key is shown once, when it is created. Its name, scopes and expiry cannot be changed later.</p>
      </div>

      <form [formRoot]="keyForm" class="grid gap-5">
        <div class="grid gap-2">
          <label appLabel for="key-name">Name</label>
          <input
            appInput
            id="key-name"
            autocomplete="off"
            placeholder="e.g. production"
            [formField]="keyForm.name"
            [attr.aria-invalid]="keyForm.name().touched() && keyForm.name().invalid() ? true : null"
            aria-describedby="key-name-errors key-name-server"
          />
          <app-field-errors id="key-name-errors" [field]="keyForm.name()" />
          <p id="key-name-server" class="text-sm text-destructive empty:hidden" role="alert">{{ serverError('name') }}</p>
        </div>

        <fieldset class="grid gap-2" aria-describedby="key-scopes-errors key-scopes-server">
          <legend class="mb-2 text-sm leading-none font-medium">Scopes</legend>
          @if (scopesProblem(); as problem) {
            <app-problem-alert [problem]="problem" (retry)="retryScopes.emit()" />
          } @else if (scopes(); as list) {
            @for (scope of list; track scope.name) {
              <div class="flex items-start gap-3">
                <button
                  appCheckbox
                  class="mt-0.5"
                  [id]="'scope-' + $index"
                  [attr.aria-label]="scope.name"
                  [checked]="isChosen(scope.name)"
                  (checkedChange)="toggle(scope.name, $event)"
                  [attr.aria-describedby]="'scope-' + $index + '-description'"
                ></button>
                <label class="grid gap-0.5" [for]="'scope-' + $index">
                  <span class="font-mono text-sm">{{ scope.name }}</span>
                  <span class="text-sm text-muted-foreground" [id]="'scope-' + $index + '-description'">{{ scope.description }}</span>
                </label>
              </div>
            }
          } @else {
            <div class="grid gap-2" aria-busy="true" aria-label="Loading scopes">
              @for (row of [1, 2, 3]; track row) {
                <div appSkeleton class="h-9 w-full"></div>
              }
            </div>
          }
          <app-field-errors id="key-scopes-errors" [field]="keyForm.scopes()" />
          <p id="key-scopes-server" class="text-sm text-destructive empty:hidden" role="alert">{{ serverError('scopes') }}</p>
        </fieldset>

        <div class="grid gap-2">
          <label appLabel for="key-expires">Expires <span class="font-normal text-muted-foreground">(optional)</span></label>
          <input
            appInput
            id="key-expires"
            type="datetime-local"
            [formField]="keyForm.expiresAt"
            [attr.aria-invalid]="keyForm.expiresAt().touched() && keyForm.expiresAt().invalid() ? true : null"
            aria-describedby="key-expires-hint key-expires-errors key-expires-server"
          />
          <app-field-errors id="key-expires-errors" [field]="keyForm.expiresAt()" />
          <p id="key-expires-server" class="text-sm text-destructive empty:hidden" role="alert">{{ serverError('expiresAt') }}</p>
          <p id="key-expires-hint" class="text-xs text-muted-foreground">
            @if (expiry(); as at) {
              Stops working on {{ at | date: 'd MMM y, HH:mm' }} ({{ offset(at) }}).
            } @else {
              Leave empty for a key that works until it is revoked.
            }
          </p>
        </div>

        @if (generalProblem(); as problem) {
          <app-problem-alert [problem]="problem" [retryable]="false" />
        }

        <div appDialogFooter>
          <button appButton type="button" variant="outline" (click)="open.set(false)">Cancel</button>
          <button appButton type="submit" [disabled]="busy()">
            @if (busy()) {
              <app-spinner data-icon="inline-start" />
            }
            Create key
          </button>
        </div>
      </form>
    </app-dialog>
  `,
  host: { class: 'contents' },
})
export class CreateKeyDialog {
  readonly open = model(false);
  readonly clientId = input.required<string>();
  /** The scopes the service offers; null while loading. */
  readonly scopes = input<readonly Scope[] | null>(null);
  readonly scopesProblem = input<ProblemDetails | null>(null);
  readonly busy = input(false);
  /** A refusal from the API: field errors go on their fields, anything else below the form. */
  readonly problem = input<ProblemDetails | null>(null);

  readonly submitted = output<IssueKeyRequest>();
  readonly retryScopes = output();

  private readonly now = signal(Date.now());
  private readonly model = signal<KeyDraft>({ name: '', scopes: [], expiresAt: '' });

  protected readonly keyForm = form(
    this.model,
    (path) => {
      required(path.name, { message: 'Enter a name.' });
      maxLength(path.name, KEY_NAME_MAX_LENGTH, { message: `Use at most ${KEY_NAME_MAX_LENGTH} characters.` });
      validate(path.scopes, ({ value }) =>
        value().length === 0 ? { kind: 'scopes', message: 'Choose at least one scope.' } : null,
      );
      validate(path.expiresAt, ({ value }) => {
        if (!value()) return null;
        const at = parseLocal(value());
        if (!at) return { kind: 'expiresAt', message: 'Enter a date and time.' };
        return at.getTime() <= Date.now() ? { kind: 'expiresAt', message: 'Choose a time in the future.' } : null;
      });
    },
    {
      submission: {
        action: async () => {
          const draft = this.model();
          this.submitted.emit({
            name: draft.name.trim(),
            scopes: draft.scopes,
            expiresAt: parseLocal(draft.expiresAt)?.toISOString() ?? null,
          });
          return null;
        },
      },
    },
  );

  protected readonly expiry = computed(() => {
    const at = parseLocal(this.model().expiresAt);
    return at && at.getTime() > this.now() ? at : null;
  });

  /** A refusal that is not about one field (a 404, a 503): shown below the form. */
  protected readonly generalProblem = computed(() => {
    const problem = this.problem();
    return problem && Object.keys(problem.errors ?? {}).length === 0 ? problem : null;
  });

  protected readonly offset = formatUtcOffset;

  protected isChosen(scope: string): boolean {
    return this.model().scopes.includes(scope);
  }

  protected toggle(scope: string, chosen: boolean): void {
    this.model.update((draft) => ({
      ...draft,
      scopes: chosen ? [...draft.scopes.filter((s) => s !== scope), scope] : draft.scopes.filter((s) => s !== scope),
    }));
    this.keyForm.scopes().markAsTouched();
  }

  /** The first message the API gave for a field, matching its name without regard to case. */
  protected serverError(field: string): string {
    const errors = this.problem()?.errors ?? {};
    const key = Object.keys(errors).find((k) => k.toLowerCase() === field.toLowerCase());
    return key ? (errors[key]?.[0] ?? '') : '';
  }
}
