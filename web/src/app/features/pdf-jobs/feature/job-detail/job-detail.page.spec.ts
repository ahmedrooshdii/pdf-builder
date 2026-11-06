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
  fileUrlExpiresAt: null,
  fileExpired: false,
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
  const succeeded = { ...pending, status: 'Succeeded', fileId: FILE_ID, sizeBytes: 64_210 };
  const buttonNamed = (el: HTMLElement, text: string) =>
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes(text));

  it('keeps polling while pending and offers the PDF once it succeeds, without a link to it', async () => {
    const { el, http, answer } = setup();

    await answer(pending);
    expect(el.textContent).toContain('Pending');

    await answer(succeeded);

    http.verify();
    expect(buttonNamed(el, 'Download')).toBeDefined();
    // The console never links to a file: it fetches it with its own credentials.
    expect(el.querySelector('a[href*="/files/"]')).toBeNull();
    expect(el.querySelector('a[download]')).toBeNull();
    expect(el.textContent).toContain('62.7 KB');
    // Nothing is fetched for the preview until it is asked for.
    expect(el.querySelector('iframe')).toBeNull();
  });

  it('downloads through the API and saves it as {template}-{job}.pdf', async () => {
    URL.createObjectURL = vi.fn(() => 'blob:saved');
    URL.revokeObjectURL = vi.fn();
    const saved: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      saved.push(`${this.getAttribute('href')} as ${this.download}`);
    });
    const { el, http, answer } = setup();
    await answer(succeeded);

    buttonNamed(el, 'Download')?.click();
    TestBed.tick();

    const file = http.expectOne('/api/pdf/jobs/j1/file');
    expect(file.request.responseType).toBe('blob');
    file.flush(new Blob(['%PDF'], { type: 'application/pdf' }));
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();

    expect(saved).toEqual(['blob:saved as invoice-j1.pdf']);
    vi.restoreAllMocks();
  });

  it('fetches the PDF through the API and shows it when Preview is clicked', async () => {
    // jsdom has neither object URLs nor modal dialogs; the page only needs them to exist.
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    URL.revokeObjectURL = vi.fn();
    HTMLDialogElement.prototype.showModal ??= function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    const { el, http, answer } = setup();
    await answer(succeeded);

    buttonNamed(el, 'Preview')?.click();
    TestBed.tick();

    const file = http.expectOne('/api/pdf/jobs/j1/file');
    expect(file.request.responseType).toBe('blob');
    file.flush(new Blob(['%PDF'], { type: 'application/pdf' }));
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();

    expect(el.querySelector('iframe')?.getAttribute('src')).toBe('blob:preview');
  });

  it('says the file expired, and offers neither Download nor Preview, when the job reports it', async () => {
    const { el, answer } = setup();

    await answer({ ...succeeded, fileExpired: true });

    expect(el.textContent).toContain('File expired');
    expect(buttonNamed(el, 'Download')).toBeUndefined();
    expect(buttonNamed(el, 'Preview')).toBeUndefined();
  });

  it('turns into the expired state when the download is answered 410', async () => {
    const { el, http, answer } = setup();
    await answer(succeeded);

    buttonNamed(el, 'Download')?.click();
    TestBed.tick();
    http
      .expectOne('/api/pdf/jobs/j1/file')
      .flush(new Blob(['{}'], { type: 'application/json' }), { status: 410, statusText: 'Gone' });
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();

    expect(el.textContent).toContain('File expired');
    expect(buttonNamed(el, 'Download')).toBeUndefined();
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
