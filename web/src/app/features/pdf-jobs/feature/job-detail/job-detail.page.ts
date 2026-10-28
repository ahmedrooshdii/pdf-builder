import { Component, computed, inject, input, signal } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { DomSanitizer } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { interval, map, of, startWith, switchMap, tap } from 'rxjs';
import { toProblemDetails } from '../../../../core/http/problem-details';
import { DurationPipe } from '../../../../shared/pipes/duration.pipe';
import { Button } from '../../../../shared/ui/button';
import { CODE_BLOCK } from '../../../../shared/ui/code-block';
import { DIALOG } from '../../../../shared/ui/dialog';
import { PROGRESS, Skeleton, Spinner } from '../../../../shared/ui/feedback';
import { Icon } from '../../../../shared/ui/icon';
import { toObjectUrl } from '../../../../shared/util/object-url';
import { JobHistoryStore } from '../../data-access/job-history.store';
import { JobPoller } from '../../data-access/job-poller';
import { JOB_RETENTION_HOURS } from '../../data-access/pdf-job.models';
import { PdfJobsApi } from '../../data-access/pdf-jobs.api';
import { JobStatusBadge } from '../../ui/job-status-badge/job-status-badge';
import { JobTrace } from '../../ui/job-trace/job-trace';

/** Typical render time once the browser is warm; only shapes the progress curve. */
const EXPECTED_RENDER_MS = 4_000;

@Component({
  selector: 'app-job-detail-page',
  imports: [
    RouterLink,
    DurationPipe,
    Button,
    CODE_BLOCK,
    DIALOG,
    Icon,
    PROGRESS,
    Skeleton,
    Spinner,
    JobStatusBadge,
    JobTrace,
  ],
  templateUrl: './job-detail.page.html',
  host: { class: 'grid gap-6' },
})
export class JobDetailPage {
  private readonly api = inject(PdfJobsApi);
  private readonly poller = inject(JobPoller);
  private readonly history = inject(JobHistoryStore);
  private readonly sanitizer = inject(DomSanitizer);

  /** Bound from the `:jobId` route parameter. */
  readonly jobId = input.required<string>();

  protected readonly retentionHours = JOB_RETENTION_HOURS;
  protected readonly previewOpen = signal(false);

  /** Re-subscribes when the id changes, which also cancels polling of the previous job. */
  protected readonly job = rxResource({
    params: () => this.jobId(),
    stream: ({ params: jobId }) => this.poller.watch(jobId).pipe(tap((job) => this.history.record(job))),
  });

  protected readonly problem = computed(() => {
    const error = this.job.error();
    // A resource wraps anything that is not an `Error` (HttpErrorResponse included) and keeps it as the cause.
    return error ? toProblemDetails(error.cause ?? error) : null;
  });

  private readonly current = computed(() => (this.job.hasValue() ? this.job.value() : undefined));
  protected readonly isPending = computed(() => this.current()?.status === 'Pending');

  /** Ticks only while the job is pending; otherwise it holds still and costs nothing. */
  private readonly now = toSignal(
    toObservable(this.isPending).pipe(
      switchMap((pending) => (pending ? interval(200).pipe(startWith(0)) : of(0))),
      map(() => Date.now()),
    ),
    { initialValue: Date.now() },
  );

  protected readonly elapsedMs = computed(() => {
    const job = this.current();
    return job ? Math.max(0, this.now() - Date.parse(job.enqueuedAt)) : 0;
  });

  /**
   * The API reports no progress, only "pending", so this is an estimate: it eases towards 95% on a
   * curve shaped by the usual render time and never claims to be done before the job is.
   */
  protected readonly estimatedProgress = computed(() =>
    Math.round(95 * (1 - Math.exp(-this.elapsedMs() / EXPECTED_RENDER_MS))),
  );

  private readonly fileId = computed(() => {
    const job = this.current();
    return job?.status === 'Succeeded' ? job.fileId : null;
  });

  protected readonly downloadUrl = computed(() => {
    const fileId = this.fileId();
    return fileId ? this.api.fileUrl(fileId) : null;
  });

  protected readonly downloadName = computed(() => {
    const job = this.current();
    return job ? `${job.templateKey}-${job.jobId}.pdf` : null;
  });

  /**
   * The PDF as an object URL, fetched only while the preview is open. The API serves files as
   * `attachment`, so an iframe pointed at the download URL would download instead of display.
   * Closing the preview, or moving to another job, ends the subscription and revokes the URL.
   */
  protected readonly preview = rxResource({
    params: () => (this.previewOpen() ? (this.fileId() ?? undefined) : undefined),
    stream: ({ params: fileId }) => this.api.download(fileId).pipe(toObjectUrl()),
  });

  /** Only ever an object URL this page created from bytes it downloaded itself, so safe to frame. */
  protected readonly previewSrc = computed(() =>
    this.preview.hasValue() ? this.sanitizer.bypassSecurityTrustResourceUrl(this.preview.value()) : null,
  );

  protected forget(): void {
    this.history.remove(this.jobId());
  }
}
