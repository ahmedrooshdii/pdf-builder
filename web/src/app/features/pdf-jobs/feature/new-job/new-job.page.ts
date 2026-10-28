import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import {
  FieldTree,
  FormField,
  FormRoot,
  ReadonlyFieldState,
  TreeValidationResult,
  ValidationError,
  form,
  pattern,
  required,
  validate,
} from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ProblemDetails, toProblemDetails } from '../../../../core/http/problem-details';
import { TEMPLATE_KEY_PATTERN, isUsable } from '../../../../shared/data-access/template.models';
import { TemplatesApi } from '../../../../shared/data-access/templates.api';
import { FileSizePipe } from '../../../../shared/pipes/file-size.pipe';
import { Button } from '../../../../shared/ui/button';
import { CODE_BLOCK } from '../../../../shared/ui/code-block';
import { CodeEditor } from '../../../../shared/ui/code-editor/code-editor';
import { Spinner } from '../../../../shared/ui/feedback';
import { FieldErrors } from '../../../../shared/ui/field-errors';
import { Label } from '../../../../shared/ui/form-controls';
import { Select, SelectOption } from '../../../../shared/ui/select';
import { Icon } from '../../../../shared/ui/icon';
import { JobHistoryStore } from '../../data-access/job-history.store';
import { parsePayload } from '../../data-access/parse-payload';
import { pendingJob } from '../../data-access/pdf-job.models';
import { PdfJobsApi } from '../../data-access/pdf-jobs.api';

interface JobDraft {
  templateKey: string;
  /** Kept as text so the editor shows exactly what was typed, including mistakes. */
  data: string;
}

@Component({
  selector: 'app-new-job-page',
  imports: [
    FormRoot,
    FormField,
    FileSizePipe,
    Button,
    CODE_BLOCK,
    CodeEditor,
    FieldErrors,
    Icon,
    Label,
    Select,
    RouterLink,
    Spinner,
  ],
  templateUrl: './new-job.page.html',
  host: { class: 'grid gap-6' },
})
export class NewJobPage {
  private readonly api = inject(PdfJobsApi);
  private readonly templatesApi = inject(TemplatesApi);
  private readonly history = inject(JobHistoryStore);
  private readonly router = inject(Router);

  /** `?template=` from the URL, set by the "Generate a PDF" links on the templates pages. */
  readonly template = input<string>();

  /** Every template a job can use right now: published ones and built-ins. Drafts are left out. */
  protected readonly templates = rxResource({ stream: () => this.templatesApi.list() });
  protected readonly templateOptions = computed<readonly SelectOption[]>(() => {
    if (!this.templates.hasValue()) {
      // Until the list arrives, show the preselected key rather than an empty trigger.
      const key = this.draft().templateKey;
      return key ? [{ value: key, label: key }] : [];
    }
    return this.templates
      .value()
      .filter(isUsable)
      .map((t) => ({
        value: t.templateKey,
        label: t.templateKey,
        hint: t.source === 'BuiltIn' ? 'Built-in' : undefined,
      }));
  });

  private readonly draft = linkedSignal<JobDraft>(() => ({
    templateKey: this.template() ?? 'invoice',
    data: '{\n  \n}',
  }));

  /** Failures that belong to no single field: the API is down, a 500, an unexpected problem. */
  protected readonly formError = signal<string | null>(null);
  protected readonly sampleMessage = signal<string | null>(null);

  protected readonly payloadText = computed(() => this.draft().data);
  protected readonly lineCount = computed(() => this.payloadText().split('\n').length);
  /** UTF-8 size, the unit the API's 5 MB limit is measured in. */
  protected readonly payloadBytes = computed(() => new TextEncoder().encode(this.payloadText()).byteLength);

  protected readonly jobForm = form(
    this.draft,
    (path) => {
      required(path.templateKey, { message: 'Choose a template.' });
      pattern(path.templateKey, TEMPLATE_KEY_PATTERN, {
        message: "Letters, digits, '_' and '-' only, starting with a letter or digit (max 64).",
      });
      validate(path.data, ({ value }) => {
        const parsed = parsePayload(value());
        return parsed.ok ? null : { kind: 'payload', message: parsed.error };
      });
    },
    { submission: { action: (field) => this.enqueue(field) } },
  );

  /** `aria-invalid` drives the red border and ring, after the same "touched" rule the messages use. */
  protected showInvalid(field: ReadonlyFieldState<unknown>): true | null {
    return field.touched() && field.invalid() ? true : null;
  }

  protected async loadSample(): Promise<void> {
    const templateKey = this.draft().templateKey.trim();
    this.sampleMessage.set(null);

    try {
      const template = await firstValueFrom(this.templatesApi.get(templateKey));
      // The published sample is the one that matches what a job will render.
      const sample = (template.published ?? template.draft).sampleJson;
      if (sample.trim()) {
        this.draft.update((d) => ({ ...d, data: sample }));
      } else {
        this.sampleMessage.set(`'${templateKey}' has no sample data.`);
      }
    } catch {
      this.sampleMessage.set(`Could not load the sample for '${templateKey}'.`);
    }
  }

  protected formatPayload(): void {
    const parsed = parsePayload(this.draft().data);
    if (parsed.ok) {
      this.draft.update((d) => ({ ...d, data: JSON.stringify(parsed.value, null, 2) }));
    }
  }

  private async enqueue(field: FieldTree<JobDraft>): Promise<TreeValidationResult> {
    this.formError.set(null);

    const { templateKey, data } = this.draft();
    const payload = parsePayload(data);
    if (!payload.ok) {
      return { kind: 'payload', message: payload.error, fieldTree: field.data };
    }

    try {
      const accepted = await firstValueFrom(this.api.enqueue({ templateKey, data: payload.value }));
      this.history.record(pendingJob(accepted, templateKey, new Date()));
      await this.router.navigate(['/jobs', accepted.jobId]);
      return null;
    } catch (error) {
      const problem = toProblemDetails(error);
      const fieldErrors = toFieldErrors(problem, field);
      if (fieldErrors.length === 0) {
        this.formError.set(problem.detail ?? problem.title ?? 'The job could not be submitted.');
      }
      return fieldErrors;
    }
  }
}

/**
 * Pins a 400's messages to the inputs they are about. The endpoint names the payload `data` when
 * it rejects the shape and `JsonPayload` when the command validator does, so both map to one field.
 */
function toFieldErrors(problem: ProblemDetails, field: FieldTree<JobDraft>): ValidationError.WithFieldTree[] {
  const targets: Readonly<Record<string, FieldTree<string>>> = {
    templatekey: field.templateKey,
    data: field.data,
    jsonpayload: field.data,
  };

  return Object.entries(problem.errors ?? {}).flatMap(([key, messages]) => {
    const target = targets[key.toLowerCase()];
    return target ? messages.map((message) => ({ kind: 'server', message, fieldTree: target })) : [];
  });
}
