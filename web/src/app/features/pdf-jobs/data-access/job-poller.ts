import { Injectable, InjectionToken, inject } from '@angular/core';
import { Observable, defer, repeat, takeWhile, timer } from 'rxjs';
import { PdfJob, isTerminal } from './pdf-job.models';
import { PdfJobsApi } from './pdf-jobs.api';

export interface JobPollPolicy {
  /** Delay before the second poll; each later one waits this much longer, up to `maxDelayMs`. */
  readonly stepMs: number;
  readonly maxDelayMs: number;
  /** After this many polls the stream completes even if the job is still pending. */
  readonly maxPolls: number;
}

/**
 * The first render takes seconds while Chromium launches and later ones take a few hundred
 * milliseconds, so start quickly and back off linearly rather than hammering a slow job.
 */
export const JOB_POLL_POLICY = new InjectionToken<JobPollPolicy>('JOB_POLL_POLICY', {
  factory: () => ({ stepMs: 500, maxDelayMs: 3_000, maxPolls: 100 }),
});

@Injectable({ providedIn: 'root' })
export class JobPoller {
  private readonly api = inject(PdfJobsApi);
  private readonly policy = inject(JOB_POLL_POLICY);

  /**
   * Emits the job on every poll and completes with the first terminal state. Errors (a 404 for an
   * expired or unknown id, a network failure) end the stream; retrying is the caller's decision.
   */
  watch(jobId: string): Observable<PdfJob> {
    const { stepMs, maxDelayMs, maxPolls } = this.policy;

    return defer(() => this.api.get(jobId)).pipe(
      repeat({ count: maxPolls, delay: (polls) => timer(Math.min(stepMs * polls, maxDelayMs)) }),
      takeWhile((job) => !isTerminal(job.status), true),
    );
  }
}
