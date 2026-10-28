import { DatePipe } from '@angular/common';
import { Component, computed, input, linkedSignal, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DurationPipe } from '../../../../shared/pipes/duration.pipe';
import { FileSizePipe } from '../../../../shared/pipes/file-size.pipe';
import { Button } from '../../../../shared/ui/button';
import { Checkbox } from '../../../../shared/ui/checkbox';
import { Input } from '../../../../shared/ui/form-controls';
import { HoverCard } from '../../../../shared/ui/hover-card';
import { Icon } from '../../../../shared/ui/icon';
import { ScrollArea } from '../../../../shared/ui/scroll-area';
import { TABLE } from '../../../../shared/ui/table';
import { PdfJob } from '../../data-access/pdf-job.models';
import { JobStatusBadge } from '../job-status-badge/job-status-badge';

export type SortColumn = 'templateKey' | 'status' | 'enqueuedAt' | 'sizeBytes';
export interface Sort {
  readonly column: SortColumn;
  readonly direction: 'asc' | 'desc';
}

const PAGE_SIZE = 10;

/**
 * shadcn's data-table pattern for jobs: filter, sortable columns, row selection with a bulk
 * action, and pagination. The table state is view state, so it lives here; the rows and what
 * happens to removed ones belong to the caller.
 */
@Component({
  selector: 'app-jobs-table',
  imports: [
    DatePipe,
    RouterLink,
    DurationPipe,
    FileSizePipe,
    Button,
    Checkbox,
    Input,
    HoverCard,
    Icon,
    ScrollArea,
    TABLE,
    JobStatusBadge,
  ],
  templateUrl: './jobs-table.html',
  host: { class: 'flex flex-col gap-4' },
})
export class JobsTable {
  readonly jobs = input.required<readonly PdfJob[]>();
  /** Turns a file id into a download URL; the table cannot know the API's address itself. */
  readonly downloadUrlFor = input.required<(fileId: string) => string>();

  readonly removed = output<readonly string[]>();

  protected readonly filter = signal('');
  protected readonly sort = signal<Sort>({ column: 'enqueuedAt', direction: 'desc' });
  /** Back to the first page whenever the filter changes what there is to page through. */
  private readonly requestedPage = linkedSignal({ source: this.filter, computation: () => 0 });
  private readonly checked = signal<ReadonlySet<string>>(new Set());

  protected readonly filtered = computed(() => {
    const term = this.filter().trim().toLowerCase();
    const rows = term
      ? this.jobs().filter((job) =>
          [job.templateKey, job.jobId, job.status, job.errorCode ?? ''].some((v) => v.toLowerCase().includes(term)),
        )
      : this.jobs();
    return sortJobs(rows, this.sort());
  });

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.filtered().length / PAGE_SIZE)));
  /** Clamped, so removing the last rows of the last page shows the page before it. */
  protected readonly page = computed(() => Math.min(this.requestedPage(), this.pageCount() - 1));
  protected readonly rows = computed(() => this.filtered().slice(this.page() * PAGE_SIZE, (this.page() + 1) * PAGE_SIZE));

  /** Checked ids that still exist, so rows removed elsewhere never count towards the bulk action. */
  protected readonly selected = computed<ReadonlySet<string>>(() => {
    const ids = new Set(this.jobs().map((job) => job.jobId));
    return new Set([...this.checked()].filter((id) => ids.has(id)));
  });

  protected readonly allOnPageSelected = computed(
    () => this.rows().length > 0 && this.rows().every((job) => this.selected().has(job.jobId)),
  );
  protected readonly someOnPageSelected = computed(() => this.rows().some((job) => this.selected().has(job.jobId)));

  protected goToPage(page: number): void {
    this.requestedPage.set(Math.max(0, Math.min(page, this.pageCount() - 1)));
  }

  protected toggleSort(column: SortColumn): void {
    this.sort.update((sort) =>
      sort.column === column
        ? { column, direction: sort.direction === 'asc' ? 'desc' : 'asc' }
        : { column, direction: column === 'enqueuedAt' || column === 'sizeBytes' ? 'desc' : 'asc' },
    );
  }

  protected sortIcon(column: SortColumn): 'arrow-up' | 'arrow-down' | 'arrow-up-down' {
    const sort = this.sort();
    if (sort.column !== column) return 'arrow-up-down';
    return sort.direction === 'asc' ? 'arrow-up' : 'arrow-down';
  }

  protected ariaSort(column: SortColumn): 'ascending' | 'descending' | null {
    const sort = this.sort();
    return sort.column === column ? (sort.direction === 'asc' ? 'ascending' : 'descending') : null;
  }

  protected setSelected(jobId: string, checked: boolean): void {
    this.setChecked([jobId], checked);
  }

  protected setPageSelected(checked: boolean): void {
    this.setChecked(
      this.rows().map((job) => job.jobId),
      checked,
    );
  }

  protected removeSelected(): void {
    this.removed.emit([...this.selected()]);
    this.checked.set(new Set());
  }

  private setChecked(ids: readonly string[], checked: boolean): void {
    this.checked.update((current) => {
      const next = new Set(current);
      for (const id of ids) {
        if (checked) next.add(id);
        else next.delete(id);
      }
      return next;
    });
  }
}

const STATUS_ORDER = { Pending: 0, Failed: 1, Succeeded: 2 } as const;

export function sortJobs(jobs: readonly PdfJob[], sort: Sort): readonly PdfJob[] {
  const sign = sort.direction === 'asc' ? 1 : -1;
  const key = (job: PdfJob): string | number => {
    switch (sort.column) {
      case 'templateKey':
        return job.templateKey.toLowerCase();
      case 'status':
        return STATUS_ORDER[job.status];
      case 'enqueuedAt':
        return Date.parse(job.enqueuedAt);
      case 'sizeBytes':
        return job.sizeBytes ?? -1;
    }
  };
  return [...jobs].sort((a, b) => {
    const [x, y] = [key(a), key(b)];
    return x < y ? -sign : x > y ? sign : 0;
  });
}
