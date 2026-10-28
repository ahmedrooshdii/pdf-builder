import { TestBed } from '@angular/core/testing';
import { Observable, firstValueFrom, of, toArray } from 'rxjs';
import { JOB_POLL_POLICY, JobPoller } from './job-poller';
import { PdfJob, PdfJobStatus } from './pdf-job.models';
import { PdfJobsApi } from './pdf-jobs.api';

function job(status: PdfJobStatus): PdfJob {
  return {
    jobId: 'j1',
    templateKey: 'invoice',
    status,
    enqueuedAt: '2026-10-28T10:00:00Z',
    completedAt: null,
    fileId: null,
    fileUrl: null,
    sizeBytes: null,
    errorCode: null,
    errorMessage: null,
  };
}

/** Answers each poll with the next status in line, repeating the last one forever. */
function setup(statuses: PdfJobStatus[], maxPolls = 100) {
  let calls = 0;
  const api: Pick<PdfJobsApi, 'get'> = {
    get: (): Observable<PdfJob> => of(job(statuses[Math.min(calls++, statuses.length - 1)])),
  };

  TestBed.configureTestingModule({
    providers: [
      { provide: PdfJobsApi, useValue: api },
      { provide: JOB_POLL_POLICY, useValue: { stepMs: 0, maxDelayMs: 0, maxPolls } },
    ],
  });

  return { poller: TestBed.inject(JobPoller), calls: () => calls };
}

describe('JobPoller', () => {
  it('polls until the job reaches a terminal state, and emits that state last', async () => {
    const { poller, calls } = setup(['Pending', 'Pending', 'Succeeded']);

    const seen = await firstValueFrom(poller.watch('j1').pipe(toArray()));

    expect(seen.map((j) => j.status)).toEqual(['Pending', 'Pending', 'Succeeded']);
    expect(calls()).toBe(3);
  });

  it('stops after one poll when the job is already finished', async () => {
    const { poller, calls } = setup(['Failed']);

    const seen = await firstValueFrom(poller.watch('j1').pipe(toArray()));

    expect(seen.map((j) => j.status)).toEqual(['Failed']);
    expect(calls()).toBe(1);
  });

  it('gives up after the configured number of polls', async () => {
    const { poller, calls } = setup(['Pending'], 4);

    const seen = await firstValueFrom(poller.watch('j1').pipe(toArray()));

    expect(seen.every((j) => j.status === 'Pending')).toBe(true);
    expect(calls()).toBe(4);
  });
});
