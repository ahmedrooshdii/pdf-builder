import { TestBed } from '@angular/core/testing';
import { BROWSER_STORAGE } from '../../../core/storage/browser-storage';
import { JobHistoryStore } from './job-history.store';
import { PdfJob, PdfJobStatus } from './pdf-job.models';

const KEY = 'pdf-builder.job-history.v1';

function job(jobId: string, status: PdfJobStatus = 'Pending'): PdfJob {
  return {
    jobId,
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

function setup(storage: Storage | null = new MemoryStorage()) {
  TestBed.configureTestingModule({ providers: [{ provide: BROWSER_STORAGE, useValue: storage }] });
  return { store: TestBed.inject(JobHistoryStore), storage };
}

describe('JobHistoryStore', () => {
  it('puts new jobs first', () => {
    const { store } = setup();

    store.record(job('a'));
    store.record(job('b'));

    expect(store.jobs().map((j) => j.jobId)).toEqual(['b', 'a']);
  });

  it('updates a known job in place instead of moving it', () => {
    const { store } = setup();
    store.record(job('a'));
    store.record(job('b'));

    store.record(job('a', 'Succeeded'));

    expect(store.jobs().map((j) => [j.jobId, j.status])).toEqual([
      ['b', 'Pending'],
      ['a', 'Succeeded'],
    ]);
  });

  it('persists changes and reads them back in a new session', () => {
    const storage = new MemoryStorage();
    const first = setup(storage).store;
    first.record(job('a'));
    TestBed.tick();

    TestBed.resetTestingModule();
    const second = setup(storage).store;

    expect(second.jobs().map((j) => j.jobId)).toEqual(['a']);
  });

  it('reads entries saved under the old templateId name', () => {
    const storage = new MemoryStorage();
    const { templateKey, ...legacy } = job('old');
    storage.setItem(KEY, JSON.stringify([{ ...legacy, templateId: templateKey }]));

    expect(setup(storage).store.jobs()[0].templateKey).toBe('invoice');
  });

  it('starts empty when the stored value is corrupt', () => {
    const storage = new MemoryStorage();
    storage.setItem(KEY, '{not json');

    expect(setup(storage).store.jobs()).toEqual([]);
  });

  it('works without storage at all', () => {
    const { store } = setup(null);

    store.record(job('a'));
    TestBed.tick();

    expect(store.jobs()).toHaveLength(1);
  });
});

class MemoryStorage implements Storage {
  private readonly items = new Map<string, string>();
  get length() {
    return this.items.size;
  }
  clear() {
    this.items.clear();
  }
  getItem(key: string) {
    return this.items.get(key) ?? null;
  }
  key(index: number) {
    return [...this.items.keys()][index] ?? null;
  }
  removeItem(key: string) {
    this.items.delete(key);
  }
  setItem(key: string, value: string) {
    this.items.set(key, value);
  }
}
