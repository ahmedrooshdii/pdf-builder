import { Component, input, model, output, signal } from '@angular/core';
import { FormField, FormRoot, form, maxLength, pattern, required } from '@angular/forms/signals';
import { ProblemDetails } from '../../../../core/http/problem-details';
import { CLIENT_ID_PATTERN } from '../../../../shared/data-access/client.models';
import { Button } from '../../../../shared/ui/button';
import { DIALOG } from '../../../../shared/ui/dialog';
import { Spinner } from '../../../../shared/ui/feedback';
import { FieldErrors } from '../../../../shared/ui/field-errors';
import { Input, Label } from '../../../../shared/ui/form-controls';
import { ProblemAlert } from '../../../../shared/ui/problem-alert';

export const CLIENT_NAME_MAX_LENGTH = 100;

/**
 * Registers a client (FR-025). The id is checked as it is typed, by the same rule as the API; a
 * taken id comes back from the API as `409 CLIENT_ID_TAKEN` and is shown on the field.
 */
@Component({
  selector: 'app-add-client-dialog',
  imports: [FormRoot, FormField, Button, DIALOG, FieldErrors, Input, Label, ProblemAlert, Spinner],
  template: `
    <app-dialog [(open)]="open" labelledBy="add-client-title">
      <div appDialogHeader>
        <h2 appDialogTitle id="add-client-title">Add client</h2>
        <p appDialogDescription>A product that calls this service. Its keys belong to it.</p>
      </div>

      <form [formRoot]="clientForm" class="grid gap-4">
        <div class="grid gap-2">
          <label appLabel for="client-id">Id</label>
          <input
            appInput
            id="client-id"
            autocomplete="off"
            spellcheck="false"
            class="font-mono"
            placeholder="e.g. ecommerce"
            [formField]="clientForm.id"
            [attr.aria-invalid]="idInvalid() ? true : null"
            aria-describedby="client-id-hint client-id-errors client-id-server"
            (input)="clientForm.id().markAsTouched()"
          />
          <app-field-errors id="client-id-errors" [field]="clientForm.id()" />
          <p id="client-id-server" class="text-sm text-destructive empty:hidden" role="alert">{{ idServerError() }}</p>
          <p id="client-id-hint" class="text-xs text-muted-foreground">
            Lowercase letters, digits and hyphens, starting with a letter. Cannot be changed later.
          </p>
        </div>

        <div class="grid gap-2">
          <label appLabel for="client-name">Name</label>
          <input
            appInput
            id="client-name"
            autocomplete="off"
            placeholder="e.g. E-commerce"
            [formField]="clientForm.name"
            [attr.aria-invalid]="clientForm.name().touched() && clientForm.name().invalid() ? true : null"
            aria-describedby="client-name-errors client-name-server"
          />
          <app-field-errors id="client-name-errors" [field]="clientForm.name()" />
          <p id="client-name-server" class="text-sm text-destructive empty:hidden" role="alert">{{ serverError('name') }}</p>
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
            Add client
          </button>
        </div>
      </form>
    </app-dialog>
  `,
  host: { class: 'contents' },
})
export class AddClientDialog {
  readonly open = model(false);
  readonly busy = input(false);
  readonly problem = input<ProblemDetails | null>(null);

  readonly submitted = output<{ id: string; name: string }>();

  private readonly model = signal({ id: '', name: '' });

  protected readonly clientForm = form(
    this.model,
    (path) => {
      required(path.id, { message: 'Enter an id.' });
      pattern(path.id, CLIENT_ID_PATTERN, {
        message: 'Use lowercase letters, digits and hyphens, starting with a letter, up to 40 characters.',
      });
      required(path.name, { message: 'Enter a name.' });
      maxLength(path.name, CLIENT_NAME_MAX_LENGTH, { message: `Use at most ${CLIENT_NAME_MAX_LENGTH} characters.` });
    },
    {
      submission: {
        action: async () => {
          const value = this.model();
          this.submitted.emit({ id: value.id.trim(), name: value.name.trim() });
          return null;
        },
      },
    },
  );

  protected idInvalid(): boolean {
    return (this.clientForm.id().touched() && this.clientForm.id().invalid()) || this.idServerError() !== '';
  }

  protected idServerError(): string {
    if (this.problem()?.code === 'CLIENT_ID_TAKEN') return 'A client with this id already exists.';
    return this.serverError('id');
  }

  protected generalProblem(): ProblemDetails | null {
    const problem = this.problem();
    if (!problem || problem.code === 'CLIENT_ID_TAKEN' || Object.keys(problem.errors ?? {}).length > 0) return null;
    return problem;
  }

  protected serverError(field: string): string {
    const errors = this.problem()?.errors ?? {};
    const key = Object.keys(errors).find((k) => k.toLowerCase() === field);
    return key ? (errors[key]?.[0] ?? '') : '';
  }
}
