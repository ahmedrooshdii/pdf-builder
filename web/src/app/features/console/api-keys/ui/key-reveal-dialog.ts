import { Component, ElementRef, input, linkedSignal, output, signal, viewChild } from '@angular/core';
import { Button } from '../../../../shared/ui/button';
import { CopyButton } from '../../../../shared/ui/copy-button';
import { DIALOG } from '../../../../shared/ui/dialog';
import { Icon } from '../../../../shared/ui/icon';

/**
 * Shows a new key exactly once (FR-022, FR-023). It cannot be dismissed by Escape, a backdrop click
 * or the browser closing the dialog; the only way out is "I have copied it", which drops the key
 * from this component before telling the page, so nothing in the console holds it afterwards.
 */
@Component({
  selector: 'app-key-reveal-dialog',
  imports: [Button, CopyButton, DIALOG, Icon],
  template: `
    <app-dialog [(open)]="open" [dismissible]="false" labelledBy="reveal-title" describedBy="reveal-warning" class="sm:max-w-lg">
      <div appDialogHeader class="pr-0">
        <h2 appDialogTitle id="reveal-title">Copy the key for <span class="font-mono">{{ clientId() }}</span></h2>
        <p appDialogDescription id="reveal-warning" class="flex items-center gap-2 font-medium text-warning">
          <app-icon name="triangle-alert" />
          Copy it now. It cannot be shown again.
        </p>
      </div>

      @if (held(); as key) {
        <div class="flex items-center gap-2 rounded-lg border bg-muted/50 p-2">
          <code #keyText data-testid="revealed-key" class="min-w-0 flex-1 font-mono text-xs break-all select-all">{{ key }}</code>
          <app-copy-button [value]="key" label="Copy" variant="outline" (failed)="copyFailed()" />
        </div>
      }
      @if (copyProblem()) {
        <p class="text-sm text-destructive" role="alert">Copy did not work. The key is selected: press Ctrl+C.</p>
      }

      <div appDialogFooter>
        <button appButton type="button" (click)="confirm()">I have copied it</button>
      </div>
    </app-dialog>
  `,
  host: { class: 'contents' },
})
export class KeyRevealDialog {
  /** The full key, from the one response that carries it. */
  readonly key = input.required<string>();
  readonly clientId = input.required<string>();

  readonly confirmed = output();

  protected readonly open = signal(true);
  /** The key while the dialog shows it; null once confirmed. */
  protected readonly held = linkedSignal<string | null>(() => this.key());
  protected readonly copyProblem = signal(false);

  private readonly keyText = viewChild<ElementRef<HTMLElement>>('keyText');

  /** Whether a key is still on screen. For tests and the page's leave guards. */
  holdsKey(): boolean {
    return this.held() !== null;
  }

  protected copyFailed(): void {
    this.copyProblem.set(true);
    const element = this.keyText()?.nativeElement;
    const selection = globalThis.getSelection?.();
    if (element && selection) selection.selectAllChildren(element);
  }

  protected confirm(): void {
    this.held.set(null);
    this.open.set(false);
    this.confirmed.emit();
  }
}
