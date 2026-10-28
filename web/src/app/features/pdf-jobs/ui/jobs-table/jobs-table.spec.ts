import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PdfJob, PdfJobStatus } from '../../data-access/pdf-job.models';
import { JobsTable, sortJobs } from './jobs-table';

function job(n: number, status: PdfJobStatus = 'Succeeded', templateKey = 'invoice'): PdfJob {
  return {
    jobId: `job-${String(n).padStart(2, '0')}`,
    templateKey,
    status,
    enqueuedAt: new Date(Date.UTC(2026, 9, 28, 10, 0, n)).toISOString(),
    completedAt: null,
    fileId: null,
    fileUrl: null,
    sizeBytes: n * 1000,
    errorCode: null,
    errorMessage: null,
  };
}

function setup(jobs: PdfJob[]) {
  TestBed.configureTestingModule({ providers: [provideRouter([])] });
  const fixture = TestBed.createComponent(JobsTable);
  fixture.componentRef.setInput('jobs', jobs);
  fixture.componentRef.setInput('downloadUrlFor', (id: string) => `/files/${id}`);
  TestBed.tick();

  const el: HTMLElement = fixture.nativeElement;
  const rowIds = () =>
    Array.from(el.querySelectorAll('tbody tr')).map((tr) => tr.querySelector('td:nth-child(2) a')?.textContent?.trim());
  const button = (text: string) => {
    const found = Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim().startsWith(text));
    if (!found) throw new Error(`No button '${text}'.`);
    return found;
  };
  const click = (target: HTMLElement) => {
    target.click();
    TestBed.tick();
  };
  const filter = (text: string) => {
    const input = el.querySelector<HTMLInputElement>('input[type=search]');
    if (!input) throw new Error('No filter input.');
    input.value = text;
    input.dispatchEvent(new Event('input'));
    TestBed.tick();
  };

  return { fixture, el, rowIds, button, click, filter };
}

describe('JobsTable', () => {
  it('pages through rows ten at a time, newest first', () => {
    const jobs = Array.from({ length: 12 }, (_, i) => job(i + 1));
    const { el, button, click } = setup(jobs);

    expect(el.querySelectorAll('tbody tr')).toHaveLength(10);
    expect(el.textContent).toContain('Page 1 of 2');

    click(button('Next'));

    expect(el.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(el.textContent).toContain('Page 2 of 2');
  });

  it('filters by template, status or id, and returns to the first page', () => {
    const jobs = [
      ...Array.from({ length: 11 }, (_, i) => job(i + 1)),
      job(20, 'Failed', 'receipt'),
    ];
    const { el, rowIds, button, click, filter } = setup(jobs);
    click(button('Next'));

    filter('receipt');

    expect(rowIds()).toEqual(['receipt']);
    expect(el.textContent).toContain('Page 1 of 1');
  });

  it('says so when the filter matches nothing', () => {
    const { el, filter } = setup([job(1)]);

    filter('nothing-like-this');

    expect(el.querySelector('tbody')?.textContent).toContain('No jobs match');
  });

  it('selects every row on the page and emits them for removal', () => {
    const { fixture, el, button, click } = setup([job(1), job(2), job(3)]);
    const removed: (readonly string[])[] = [];
    fixture.componentInstance.removed.subscribe((ids) => removed.push(ids));

    const selectAll = el.querySelector<HTMLButtonElement>('thead [role=checkbox]');
    if (!selectAll) throw new Error('No select-all checkbox.');
    click(selectAll);

    expect(el.textContent).toContain('3 of 3 row(s) selected.');
    click(button('Remove 3'));

    expect(removed).toEqual([['job-03', 'job-02', 'job-01']]);
  });

  it('drops selected rows from the count once the caller removes them', () => {
    const { fixture, el, click } = setup([job(1), job(2)]);
    const first = el.querySelector<HTMLButtonElement>('tbody [role=checkbox]');
    if (!first) throw new Error('No row checkbox.');
    click(first);
    expect(el.textContent).toContain('1 of 2 row(s) selected.');

    fixture.componentRef.setInput('jobs', [job(1)]);
    TestBed.tick();

    expect(el.textContent).toContain('0 of 1 row(s) selected.');
  });
});

describe('sortJobs', () => {
  const jobs = [job(2, 'Succeeded', 'b'), job(1, 'Failed', 'a'), job(3, 'Pending', 'c')];

  it.each([
    ['templateKey', 'asc', ['a', 'b', 'c']],
    ['templateKey', 'desc', ['c', 'b', 'a']],
    ['enqueuedAt', 'desc', ['c', 'b', 'a']],
    ['status', 'asc', ['c', 'a', 'b']],
    ['sizeBytes', 'asc', ['a', 'b', 'c']],
  ] as const)('sorts by %s %s', (column, direction, expected) => {
    expect(sortJobs(jobs, { column, direction }).map((j) => j.templateKey)).toEqual(expected);
  });
});
