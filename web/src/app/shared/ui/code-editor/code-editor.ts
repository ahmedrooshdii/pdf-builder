import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FormValueControl } from '@angular/forms/signals';
import { Skeleton } from '../feedback';
import type { CodeEditorHandle, CodeLanguage } from './code-editor.setup';

/**
 * A code editor (CodeMirror 6) for JSON, HTML or CSS that binds like a native input, either to a
 * signal form (`[formField]="form.data"`) or two-way (`[(value)]="html"`).
 *
 * CodeMirror and the language pack are imported on first render, so they never weigh on the
 * initial bundle; until they arrive a skeleton holds the space.
 */
@Component({
  selector: 'app-code-editor',
  imports: [Skeleton],
  template: `
    <div #host data-slot="code-editor" class="h-full min-h-0" [class.hidden]="!ready()"></div>
    @if (!ready()) {
      <div class="grid content-start gap-2.5 px-4 py-3" aria-hidden="true">
        @for (width of skeletonWidths; track $index) {
          <div appSkeleton class="h-3.5" [style.width.%]="width"></div>
        }
      </div>
    }
  `,
  host: { class: 'block min-h-0' },
})
export class CodeEditor implements FormValueControl<string> {
  readonly value = model('');
  readonly touch = output();
  readonly invalid = input(false);
  readonly touched = input(false);
  readonly readonly = input(false);
  readonly language = input<CodeLanguage>('json');
  readonly ariaLabel = input('Code editor');
  readonly ariaDescribedBy = input<string>();
  readonly placeholder = input('');

  protected readonly ready = signal(false);
  protected readonly skeletonWidths = [8, 32, 46, 40, 28, 52, 36, 6];

  private readonly hostElement = viewChild.required<ElementRef<HTMLElement>>('host');
  private editor: CodeEditorHandle | undefined;
  private destroyed = false;

  private readonly contentAttributes = computed(() => {
    const attributes: Record<string, string> = {
      'aria-label': this.ariaLabel(),
      'aria-multiline': 'true',
      // Code is not prose: keep spellcheckers and writing assistants (Grammarly) out of it.
      spellcheck: 'false',
      autocorrect: 'off',
      autocapitalize: 'off',
      'data-gramm': 'false',
      'data-gramm_editor': 'false',
      'data-enable-grammarly': 'false',
    };
    const describedBy = this.ariaDescribedBy();
    if (describedBy) attributes['aria-describedby'] = describedBy;
    if (this.invalid() && this.touched()) attributes['aria-invalid'] = 'true';
    return attributes;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.editor?.destroy();
    });

    afterNextRender(async () => {
      const { createCodeEditor } = await import('./code-editor.setup');
      const editor = await createCodeEditor(this.hostElement().nativeElement, {
        doc: this.value(),
        language: this.language(),
        readOnly: this.readonly(),
        placeholder: this.placeholder(),
        contentAttributes: this.contentAttributes(),
        onChange: (value) => this.value.set(value),
        onBlur: () => this.touch.emit(),
      });
      if (this.destroyed) {
        editor.destroy();
        return;
      }
      this.editor = editor;
      this.ready.set(true);
    });

    // Changes from outside the editor flow in; the editor's own edits round-trip here too and
    // are ignored because the text already matches.
    effect(() => {
      const value = this.value();
      if (this.ready()) this.editor?.setValue(value);
    });

    effect(() => {
      const readOnly = this.readonly();
      if (this.ready()) this.editor?.setReadOnly(readOnly);
    });

    effect(() => {
      const attributes = this.contentAttributes();
      if (this.ready()) this.editor?.setContentAttributes(attributes);
    });
  }
}
