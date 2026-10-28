import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { JOB_POLL_POLICY } from '../../data-access/job-poller';
import { PdfJob } from '../../data-access/pdf-job.models';
import { JobDetailPage } from './job-detail.page';

const FILE_ID = 'a7e0495b0c1d4e2f8a9b0c1d2e3f4a5b';

const pending: PdfJob = {
  jobId: 'j1',
  templateKey: 'invoice',
  status: 'Pending',
  enqueuedAt: '2026-10-28T10:00:00Z',
  completedAt: null,
  fileId: null,
  fileUrl: null,
  sizeBytes: null,
  errorCode: null,
  errorMessage: null,
};

function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: JOB_POLL_POLICY, useValue: { stepMs: 0, maxDelayMs: 0, maxPolls: 10 } },
    ],
  });

  const fixture = TestBed.createComponent(JobDetailPage);
  fixture.componentRef.setInput('jobId', 'j1');
  // Not whenStable(): that waits for the outstanding poll, which only the test can answer.
  TestBed.tick();

  const http = TestBed.inject(HttpTestingController);
  /** Answers the outstanding poll, then lets the next one (if any) get scheduled and rendered. */
  const answer = async (body: object, init?: { status: number; statusText: string }) => {
    http.expectOne('/api/pdf/jobs/j1').flush(body, init);
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  };

  return { el: fixture.nativeElement as HTMLElement, http, answer };
}

describe('JobDetailPage', () => {
  it('keeps polling while pending and offers the PDF once it succeeds', async () => {
    const { el, http, answer } = setup();

    await answer(pending);
    expect(el.textContent).toContain('Pending');

    await answer({ ...pending, status: 'Succeeded', fileId: FILE_ID, sizeBytes: 64_210 });

    http.verify();
    const download = el.querySelector<HTMLAnchorElement>('a[download]');
    expect(download?.getAttribute('href')).toBe(`/files/${FILE_ID}`);
    expect(el.textContent).toContain('62.7 KB');
    // Nothing is fetched for the preview until it is asked for.
    expect(el.querySelector('iframe')).toBeNull();
  });

  it('fetches the PDF and shows it when Preview is clicked', async () => {
    // jsdom has neither object URLs nor modal dialogs; the page only needs them to exist.
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    URL.revokeObjectURL = vi.fn();
    HTMLDialogElement.prototype.showModal ??= function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    const { el, http, answer } = setup();
    await answer({ ...pending, status: 'Succeeded', fileId: FILE_ID, sizeBytes: 64_210 });

    const preview = Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Preview'));
    preview?.click();
    TestBed.tick();

    const file = http.expectOne(`/files/${FILE_ID}`);
    expect(file.request.responseType).toBe('blob');
    file.flush(new Blob(['%PDF'], { type: 'application/pdf' }));
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();

    expect(el.querySelector('iframe')?.getAttribute('src')).toBe('blob:preview');
  });

  it("shows the failure and what to do about it", async () => {
    const { el, answer } = setup();

    await answer({ ...pending, status: 'Failed', errorCode: 'TEMPLATE_NOT_FOUND', errorMessage: 'No template' });

    expect(el.textContent).toContain('No published template has this key');
    expect(el.textContent).toContain('Failed: TEMPLATE_NOT_FOUND');
    expect(el.querySelector('a[download]')).toBeNull();
  });

  it('explains that an unknown job has probably expired', async () => {
    const { el, answer } = setup();

    await answer({}, { status: 404, statusText: 'Not Found' });

    expect(el.textContent).toContain('Job not found');
  });
});
