import { Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { toProblemDetails } from '../../../../core/http/problem-details';
import { Button } from '../../../../shared/ui/button';
import { Icon } from '../../../../shared/ui/icon';
import { saveBlob } from '../../../../shared/util/save-blob';
import { JobHistoryStore } from '../../data-access/job-history.store';
import { JOB_RETENTION_HOURS, PdfJob } from '../../data-access/pdf-job.models';
import { PdfJobsApi } from '../../data-access/pdf-jobs.api';
import { JobsTable } from '../../ui/jobs-table/jobs-table';

@Component({
  selector: 'app-jobs-page',
  imports: [RouterLink, Button, Icon, JobsTable],
  template: `
    <header class="flex flex-wrap items-end justify-between gap-4">
      <div class="grid gap-1">
        <h1 class="text-2xl font-semibold tracking-tight">Jobs</h1>
        <p class="text-sm text-muted-foreground">
          Every job submitted or opened in this browser. The server forgets a job's status after
          {{ retentionHours }} hours.
        </p>
      </div>
      <a appButton routerLink="/jobs/new">
        <app-icon name="plus" data-icon="inline-start" />
        New PDF
      </a>
    </header>

    @if (history.jobs().length > 0) {
      <app-jobs-table [jobs]="history.jobs()" (download)="download($event)" (removed)="remove($event)" />
    } @else {
      <div class="flex flex-col items-center gap-3 rounded-lg border border-dashed px-6 py-16 text-center">
        <div class="flex size-10 items-center justify-center rounded-lg bg-muted">
          <app-icon name="inbox" class="size-5 text-muted-foreground" />
        </div>
        <div class="grid gap-1">
          <h2 class="font-medium">No jobs yet</h2>
          <p class="text-sm text-muted-foreground">Generate a PDF and it will show up here.</p>
        </div>
        <a appButton variant="outline" routerLink="/jobs/new">
          <app-icon name="plus" data-icon="inline-start" />
          New PDF
        </a>
      </div>
    }
  `,
  host: { class: 'grid gap-6' },
})
export class JobsPage {
  protected readonly history = inject(JobHistoryStore);
  private readonly api = inject(PdfJobsApi);

  protected readonly retentionHours = JOB_RETENTION_HOURS;
  private readonly destroyRef = inject(DestroyRef);

  /** Fetched with the console's own credentials; no link is made. */
  protected download(job: PdfJob): void {
    this.api
      .downloadFile(job.jobId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => saveBlob(blob, `${job.templateKey}-${job.jobId}.pdf`),
        // Gone since the job was last read: say so in the table. Any other failure is left to the job page.
        error: (error: unknown) => {
          if (toProblemDetails(error).status === 410) this.history.record({ ...job, fileExpired: true });
        },
      });
  }

  protected remove(jobIds: readonly string[]): void {
    for (const id of jobIds) this.history.remove(id);
  }
}
