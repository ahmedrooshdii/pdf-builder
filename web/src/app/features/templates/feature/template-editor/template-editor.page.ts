import { DOCUMENT, DatePipe, NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, computed, inject, input, linkedSignal, signal, viewChild } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { debounceTime, firstValueFrom } from 'rxjs';
import { ProblemDetails, toProblemDetails } from '../../../../core/http/problem-details';
import { HasUnsavedChanges } from '../../../../core/routing/unsaved-changes.guard';
import {
  TemplateContent,
  TemplateDetail,
  TemplateErrorCodes,
  isUsable,
  sameContent,
} from '../../../../shared/data-access/template.models';
import { TemplatesApi } from '../../../../shared/data-access/templates.api';
import { Button } from '../../../../shared/ui/button';
import { CODE_BLOCK } from '../../../../shared/ui/code-block';
import { CodeEditor } from '../../../../shared/ui/code-editor/code-editor';
import { DIALOG } from '../../../../shared/ui/dialog';
import { Skeleton, Spinner } from '../../../../shared/ui/feedback';
import { Icon, IconName } from '../../../../shared/ui/icon';
import { TABS } from '../../../../shared/ui/tabs';
import { TemplateKeyDialog } from '../../ui/template-key-dialog';
import { TemplateStatusBadge } from '../../ui/template-status-badge';

type SourceTab = 'html' | 'css' | 'sample';

const TABS_INFO: readonly { id: SourceTab; label: string; icon: IconName; file: string; hint: string }[] = [
  {
    id: 'html',
    label: 'HTML',
    icon: 'code',
    file: 'template.html',
    hint: 'Scriban syntax: {{ customer.name }}, {{ for item in items }} … {{ end }}, {{ if paid }} … {{ end }}',
  },
  {
    id: 'css',
    label: 'CSS',
    icon: 'palette',
    file: 'styles.css',
    hint: 'Injected into <head> when printed. Use @page { size: A4; margin: 16mm; } for paper and margins.',
  },
  {
    id: 'sample',
    label: 'Sample data',
    icon: 'file-json',
    file: 'sample.json',
    hint: 'Drives the preview, the check that runs before publishing, and "Load sample" on New PDF.',
  },
];

@Component({
  selector: 'app-template-editor-page',
  imports: [
    DatePipe,
    NgTemplateOutlet,
    RouterLink,
    Button,
    CODE_BLOCK,
    CodeEditor,
    DIALOG,
    Icon,
    Skeleton,
    Spinner,
    TABS,
    TemplateKeyDialog,
    TemplateStatusBadge,
  ],
  templateUrl: './template-editor.page.html',
  host: {
    class: 'grid gap-4',
    '(document:keydown)': 'onKeydown($event)',
    '(document:fullscreenchange)': 'onFullscreenChange()',
    '(window:beforeunload)': 'onBeforeUnload($event)',
  },
})
export class TemplateEditorPage implements HasUnsavedChanges {
  private readonly api = inject(TemplatesApi);
  private readonly router = inject(Router);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly document = inject(DOCUMENT);

  /** Editor and preview side by side over the whole viewport. */
  protected readonly fullscreen = signal(false);
  private readonly workspace = viewChild<ElementRef<HTMLElement>>('workspace');

  /** Bound from the `:templateKey` route parameter. */
  readonly templateKey = input.required<string>();

  protected readonly tabs = TABS_INFO;
  protected readonly tab = signal<SourceTab>('html');
  protected readonly activeTab = computed(() => TABS_INFO.find((t) => t.id === this.tab()) ?? TABS_INFO[0]);

  protected readonly template = rxResource({
    params: () => this.templateKey(),
    stream: ({ params: key }) => this.api.get(key),
  });

  protected readonly server = computed(() => (this.template.hasValue() ? this.template.value() : undefined));
  protected readonly isBuiltIn = computed(() => this.server()?.source === 'BuiltIn');
  protected readonly loadProblem = computed(() => {
    const error = this.template.error();
    return error ? toProblemDetails(error.cause ?? error) : null;
  });

  // The editable copy of the draft. Each resets to the server's text whenever a new version arrives
  // (load, save, publish, reload), and otherwise holds whatever the user typed.
  protected readonly html = linkedSignal(() => this.server()?.draft.html ?? '');
  protected readonly css = linkedSignal(() => this.server()?.draft.css ?? '');
  protected readonly sampleJson = linkedSignal(() => this.server()?.draft.sampleJson ?? '');

  private readonly content = computed<TemplateContent | undefined>(() => {
    const server = this.server();
    return server
      ? { html: this.html(), css: this.css(), sampleJson: this.sampleJson(), options: server.draft.options }
      : undefined;
  });

  protected readonly dirty = computed(() => {
    const server = this.server();
    const content = this.content();
    return !!server && !!content && !sameContent(content, server.draft);
  });

  protected readonly canPublish = computed(
    () => !this.isBuiltIn() && (this.dirty() || this.server()?.status !== 'Published'),
  );
  protected readonly usable = computed(() => {
    const server = this.server();
    return !!server && isUsable(server);
  });

  // ---- Live preview ---------------------------------------------------------------------------

  /** Waits for a pause in typing, so the server renders once per thought rather than per key. */
  private readonly settledContent = toSignal(toObservable(this.content).pipe(debounceTime(350)));

  protected readonly preview = rxResource({
    params: () => this.settledContent(),
    stream: ({ params: content }) => this.api.preview(content),
  });

  /**
   * The last preview that rendered. Kept on screen while the next one loads or when an edit breaks
   * the template, so the pane never flashes blank mid-typing.
   *
   * Trusted because it is only ever shown in an iframe with an empty `sandbox`: no scripts, no
   * same-origin access, no forms. The markup cannot reach this app whatever it contains.
   */
  protected readonly previewDoc = linkedSignal<string | undefined, SafeHtml | null>({
    source: () => (this.preview.hasValue() ? this.preview.value() : undefined),
    computation: (html, previous) =>
      html !== undefined ? this.sanitizer.bypassSecurityTrustHtml(html) : (previous?.value ?? null),
  });

  protected readonly previewProblem = computed(() => {
    const error = this.preview.error();
    return error ? describe(toProblemDetails(error.cause ?? error)) : null;
  });

  // ---- Actions --------------------------------------------------------------------------------

  protected readonly busy = signal<'save' | 'publish' | 'delete' | 'duplicate' | null>(null);
  protected readonly actionProblem = signal<ProblemDetails | null>(null);
  protected readonly isConflict = computed(() => this.actionProblem()?.code === TemplateErrorCodes.RevisionConflict);
  protected readonly duplicating = signal(false);
  protected readonly duplicateError = signal<string | null>(null);
  protected readonly confirmingDelete = signal(false);

  hasUnsavedChanges(): boolean {
    return this.dirty();
  }

  protected async save(): Promise<TemplateDetail | undefined> {
    const server = this.server();
    const content = this.content();
    if (!server || !content || this.isBuiltIn() || this.busy()) return undefined;
    if (!this.dirty()) return server;

    return this.run('save', async () => {
      const saved = await firstValueFrom(this.api.saveDraft(server.templateKey, server.revision, content));
      this.template.set(saved);
      return saved;
    });
  }

  protected async publish(): Promise<void> {
    const saved = this.dirty() ? await this.save() : this.server();
    if (!saved || this.isBuiltIn()) return;

    await this.run('publish', async () => {
      this.template.set(await firstValueFrom(this.api.publish(saved.templateKey, saved.revision)));
      // Publishing finishes a piece of work, so go back to the list, where the new status shows.
      await this.router.navigate(['/templates']);
    });
  }

  protected async duplicate(templateKey: string): Promise<void> {
    const server = this.server();
    if (!server) return;

    this.duplicateError.set(null);
    this.busy.set('duplicate');
    try {
      const copy = await firstValueFrom(this.api.duplicate(server.templateKey, templateKey));
      this.duplicating.set(false);
      await this.router.navigate(['/templates', copy.templateKey]);
    } catch (error) {
      this.duplicateError.set(describe(toProblemDetails(error)));
    } finally {
      this.busy.set(null);
    }
  }

  protected async remove(): Promise<void> {
    const server = this.server();
    if (!server) return;

    await this.run('delete', async () => {
      await firstValueFrom(this.api.delete(server.templateKey));
      this.confirmingDelete.set(false);
      // The edits are gone with the template, so leaving must not ask about them.
      this.html.set(server.draft.html);
      this.css.set(server.draft.css);
      this.sampleJson.set(server.draft.sampleJson);
      await this.router.navigate(['/templates']);
    });
  }

  /** After a conflict: drop local edits and load what is on the server now. */
  protected reloadFromServer(): void {
    this.actionProblem.set(null);
    this.template.reload();
  }

  /**
   * Covers the viewport with the workspace, and asks the browser for real full screen too so its
   * own toolbars go away. If the browser refuses (an iframe, a policy), the covering layout alone
   * still gives the room.
   */
  protected enterFullscreen(): void {
    this.fullscreen.set(true);
    this.workspace()?.nativeElement.requestFullscreen?.().catch(() => undefined);
  }

  protected exitFullscreen(): void {
    this.fullscreen.set(false);
    if (this.document.fullscreenElement) void this.document.exitFullscreen().catch(() => undefined);
  }

  /** The browser leaves full screen on its own when Esc is pressed; follow it. */
  protected onFullscreenChange(): void {
    if (!this.document.fullscreenElement) this.fullscreen.set(false);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
      event.preventDefault();
      void this.save();
    }
    // Only reached when the browser did not take real full screen, which handles Esc itself.
    // An open dialog takes Esc for itself.
    const dialogOpen = this.duplicating() || this.confirmingDelete();
    if (event.key === 'Escape' && this.fullscreen() && !dialogOpen) this.exitFullscreen();
  }

  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.dirty()) event.preventDefault();
  }

  private async run<T>(action: 'save' | 'publish' | 'delete', work: () => Promise<T>): Promise<T | undefined> {
    this.busy.set(action);
    this.actionProblem.set(null);
    try {
      return await work();
    } catch (error) {
      this.actionProblem.set(toProblemDetails(error));
      return undefined;
    } finally {
      this.busy.set(null);
    }
  }
}

/** One readable line from a problem: a field error if there is one, else its detail or title. */
function describe(problem: ProblemDetails): string {
  const fieldError = Object.values(problem.errors ?? {})[0]?.[0];
  return fieldError ?? problem.detail ?? problem.title ?? 'Something went wrong.';
}
