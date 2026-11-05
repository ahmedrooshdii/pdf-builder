import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, signal } from '@angular/core';
import { FormField, FormRoot, form, required } from '@angular/forms/signals';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { safeReturnUrl } from '../../../../core/auth/safe-return-url';
import { SessionService } from '../../../../core/auth/session.service';
import { Logo } from '../../../../core/layout/logo';
import { APP_NAME } from '../../../../core/layout/page-title.strategy';
import { Button } from '../../../../shared/ui/button';
import { Spinner } from '../../../../shared/ui/feedback';
import { FieldErrors } from '../../../../shared/ui/field-errors';
import { Input, Label } from '../../../../shared/ui/form-controls';
import { Icon } from '../../../../shared/ui/icon';

/** What a failed sign-in says. A wrong key never says which part was wrong. */
function messageFor(error: unknown): string {
  const status = error instanceof HttpErrorResponse ? error.status : -1;
  switch (status) {
    case 401:
      return 'That key was not accepted.';
    case 429:
      return 'Too many attempts. Try again in a minute.';
    case 503:
      return 'The service cannot reach its storage. Try again in a few seconds.';
    case 0:
      return 'The API could not be reached.';
    default:
      return 'Signing in did not work. Try again.';
  }
}

/**
 * Paste the admin key once, get a session (contracts/console-ui.md "Sign in"). The field is emptied
 * after every attempt and the key is handed straight to the request, never kept.
 */
@Component({
  selector: 'app-sign-in-page',
  imports: [FormRoot, FormField, Button, Spinner, FieldErrors, Icon, Input, Label, Logo],
  template: `
    <div class="grid w-full max-w-sm gap-6 rounded-xl bg-card p-6 text-card-foreground shadow-sm ring-1 ring-foreground/10">
      <div class="flex items-center gap-3">
        <app-logo class="size-8" />
        <h1 class="text-lg font-semibold tracking-tight">{{ appName }} console</h1>
      </div>

      <form [formRoot]="signInForm" class="grid gap-4">
        <div class="grid gap-2">
          <label appLabel for="admin-key">Admin key</label>
          <div class="flex gap-2">
            <input
              appInput
              id="admin-key"
              autocomplete="off"
              spellcheck="false"
              class="font-mono"
              [type]="revealed() ? 'text' : 'password'"
              [formField]="signInForm.adminKey"
              [attr.aria-invalid]="error() ? true : null"
              aria-describedby="admin-key-hint admin-key-errors sign-in-error"
            />
            <button
              appButton
              type="button"
              variant="outline"
              size="icon"
              [attr.aria-pressed]="revealed()"
              [attr.aria-label]="revealed() ? 'Hide the key' : 'Show the key'"
              (click)="revealed.set(!revealed())"
            >
              <app-icon [name]="revealed() ? 'eye-off' : 'eye'" />
            </button>
          </div>
          <app-field-errors id="admin-key-errors" [field]="signInForm.adminKey()" />
          <p id="admin-key-hint" class="text-xs text-muted-foreground">
            The key from this service's configuration (Blocks__Admin__KeyHash is its hash).
          </p>
        </div>

        <p id="sign-in-error" class="text-sm text-destructive empty:hidden" role="alert">{{ error() }}</p>

        <div class="flex justify-end">
          <button appButton type="submit" [disabled]="busy()">
            @if (busy()) {
              <app-spinner data-icon="inline-start" />
            }
            Sign in
          </button>
        </div>
      </form>
    </div>
  `,
  host: { class: 'flex min-h-dvh items-center justify-center bg-muted/40 px-4' },
})
export class SignInPage {
  /** Bound from `?returnUrl=`; checked by `safeReturnUrl` before use. */
  readonly returnUrl = input<string>();

  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  protected readonly appName = APP_NAME;
  protected readonly revealed = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  private readonly model = signal({ adminKey: '' });

  protected readonly signInForm = form(
    this.model,
    (path) => required(path.adminKey, { message: 'Paste the admin key.' }),
    { submission: { action: () => this.submit() } },
  );

  constructor() {
    // Already signed in (an old tab, a bookmarked sign-in page): go straight on.
    void this.session.ensureKnown().then(() => {
      if (this.session.state() === 'signed-in') void this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
    });
  }

  private async submit(): Promise<null> {
    const adminKey = this.model().adminKey;
    // Emptied at once, whatever the answer: the key must not sit in the page.
    this.model.set({ adminKey: '' });
    this.signInForm.adminKey().reset();
    this.busy.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.session.signIn(adminKey), { defaultValue: undefined });
      await this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
    } catch (error) {
      this.error.set(messageFor(error));
    } finally {
      this.busy.set(false);
    }
    return null;
  }
}
