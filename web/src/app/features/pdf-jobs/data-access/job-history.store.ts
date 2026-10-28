import { Injectable, effect, inject, signal } from '@angular/core';
import { BROWSER_STORAGE } from '../../../core/storage/browser-storage';
import { PdfJob } from './pdf-job.models';

const STORAGE_KEY = 'pdf-builder.job-history.v1';
const MAX_ENTRIES = 50;

/** Entries saved before the API renamed `templateId` to `templateKey` still carry the old name. */
function upgrade(entry: PdfJob & { templateId?: string }): PdfJob {
  if (entry.templateKey !== undefined || entry.templateId === undefined) return entry;
  const { templateId, ...rest } = entry;
  return { ...rest, templateKey: templateId };
}

/**
 * Jobs this browser has submitted, newest first.
 *
 * The API has no "list jobs" endpoint and forgets jobs after a day, so this is the only record a
 * user has of what they sent. It is kept per browser and is a convenience, not a source of truth:
 * the server's answer always wins when a job is polled.
 */
@Injectable({ providedIn: 'root' })
export class JobHistoryStore {
  private readonly storage = inject(BROWSER_STORAGE);
  private readonly entries = signal<readonly PdfJob[]>(this.load());

  readonly jobs = this.entries.asReadonly();

  constructor() {
    effect(() => this.save(this.entries()));
  }

  /** Adds the job, or replaces the entry with the same id while keeping its position. */
  record(job: PdfJob): void {
    this.entries.update((jobs) => {
      const index = jobs.findIndex((j) => j.jobId === job.jobId);
      if (index === -1) {
        return [job, ...jobs].slice(0, MAX_ENTRIES);
      }
      return jobs.map((j, i) => (i === index ? job : j));
    });
  }

  remove(jobId: string): void {
    this.entries.update((jobs) => jobs.filter((j) => j.jobId !== jobId));
  }

  clear(): void {
    this.entries.set([]);
  }

  private load(): readonly PdfJob[] {
    try {
      const parsed: unknown = JSON.parse(this.storage?.getItem(STORAGE_KEY) ?? '[]');
      return Array.isArray(parsed) ? parsed.slice(0, MAX_ENTRIES).map(upgrade) : [];
    } catch {
      return [];
    }
  }

  private save(jobs: readonly PdfJob[]): void {
    try {
      this.storage?.setItem(STORAGE_KEY, JSON.stringify(jobs));
    } catch {
      // Quota exceeded or storage revoked mid-session: history just stops persisting.
    }
  }
}
