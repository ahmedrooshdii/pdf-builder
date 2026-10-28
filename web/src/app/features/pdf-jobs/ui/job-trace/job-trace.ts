import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { DurationPipe } from '../../../../shared/pipes/duration.pipe';
import { FileSizePipe } from '../../../../shared/pipes/file-size.pipe';
import { CODE_BLOCK } from '../../../../shared/ui/code-block';
import { Icon } from '../../../../shared/ui/icon';
import { TRACE } from '../../../../shared/ui/trace';
import { PdfErrorCodes, PdfJob } from '../../data-access/pdf-job.models';

/** What a person can do about each failure, keyed by the API's machine-readable code. */
const ERROR_HINTS: Readonly<Record<string, string>> = {
  [PdfErrorCodes.ValidationFailed]: 'The request was rejected. Check the template key and the payload.',
  [PdfErrorCodes.TemplateNotFound]: 'No published template has this key on the server.',
  [PdfErrorCodes.TemplateRenderFailed]:
    'The template could not be bound to this payload. A field the template uses is probably missing or of the wrong type.',
  [PdfErrorCodes.RenderFailed]: 'The browser failed to print the page. This is usually transient: submit again.',
};

/** A job's life so far, step by step, in shadcn's Trace style. */
@Component({
  selector: 'app-job-trace',
  imports: [DatePipe, DurationPipe, FileSizePipe, CODE_BLOCK, Icon, TRACE],
  templateUrl: './job-trace.html',
  host: { class: 'block' },
})
export class JobTrace {
  readonly job = input.required<PdfJob>();
  /** Milliseconds since the job was queued, for the running step while it is pending. */
  readonly elapsedMs = input(0);

  protected readonly errorHint = computed(() => {
    const code = this.job().errorCode;
    return code ? ERROR_HINTS[code] : undefined;
  });

  protected readonly rawResponse = computed(() => JSON.stringify(this.job(), null, 2));
}
