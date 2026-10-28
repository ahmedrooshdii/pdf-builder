import { Component, input, linkedSignal, model, output } from '@angular/core';
import { FormField, FormRoot, form, pattern, required } from '@angular/forms/signals';
import { TEMPLATE_KEY_PATTERN, TEMPLATE_KEY_RULE } from '../../../shared/data-access/template.models';
import { Button } from '../../../shared/ui/button';
import { DIALOG } from '../../../shared/ui/dialog';
import { Spinner } from '../../../shared/ui/feedback';
import { FieldErrors } from '../../../shared/ui/field-errors';
import { Input, Label } from '../../../shared/ui/form-controls';

/**
 * Asks for a template key, for "New template" and "Duplicate". Validates the key's shape here; the
 * caller does the request and passes back a server error (say, the key is taken) to show.
 */
@Component({
  selector: 'app-template-key-dialog',
  imports: [FormRoot, FormField, Button, DIALOG, Spinner, FieldErrors, Input, Label],
  template: `
    <app-dialog [(open)]="open" labelledBy="template-key-title">
      <div appDialogHeader>
        <h2 appDialogTitle id="template-key-title">{{ title() }}</h2>
        <p appDialogDescription>{{ description() }}</p>
      </div>

      <form [formRoot]="keyForm" class="grid gap-4">
        <div class="grid gap-2">
          <label appLabel for="template-key">Template key</label>
          <input
            appInput
            id="template-key"
            autocomplete="off"
            spellcheck="false"
            placeholder="e.g. purchase-order"
            [formField]="keyForm.templateKey"
            [attr.aria-invalid]="keyForm.templateKey().touched() && keyForm.templateKey().invalid() ? true : null"
            aria-describedby="template-key-errors template-key-hint"
          />
          <app-field-errors id="template-key-errors" [field]="keyForm.templateKey()" />
          @if (serverError(); as message) {
            <p class="text-sm text-destructive" role="alert">{{ message }}</p>
          }
          <p id="template-key-hint" class="text-xs text-muted-foreground">
            Jobs name the template by this key. It cannot be changed later.
          </p>
        </div>

        <div class="-mx-4 -mb-4 flex justify-end gap-2 rounded-b-xl border-t bg-muted/50 p-4">
          <button appButton type="button" variant="outline" (click)="open.set(false)">Cancel</button>
          <button appButton type="submit" [disabled]="busy()">
            @if (busy()) {
              <app-spinner data-icon="inline-start" />
            }
            {{ submitLabel() }}
          </button>
        </div>
      </form>
    </app-dialog>
  `,
  host: { class: 'contents' },
})
export class TemplateKeyDialog {
  readonly open = model(false);
  readonly title = input.required<string>();
  readonly description = input.required<string>();
  readonly submitLabel = input('Create');
  readonly initialKey = input('');
  readonly busy = input(false);
  readonly serverError = input<string | null>(null);

  readonly submitted = output<string>();

  /** Seeded from the suggested key. Render this dialog under `@if`, so each opening starts fresh. */
  private readonly model = linkedSignal(() => ({ templateKey: this.initialKey() }));

  protected readonly keyForm = form(
    this.model,
    (path) => {
      required(path.templateKey, { message: 'Enter a template key.' });
      pattern(path.templateKey, TEMPLATE_KEY_PATTERN, { message: TEMPLATE_KEY_RULE });
    },
    {
      submission: {
        action: async () => {
          this.submitted.emit(this.model().templateKey.trim());
          return null;
        },
      },
    },
  );
}
