import { Component, computed, input } from '@angular/core';
import { Badge, BadgeVariants } from '../../../../shared/ui/badge';
import { Icon, IconName } from '../../../../shared/ui/icon';
import { PdfJobStatus } from '../../data-access/pdf-job.models';

const APPEARANCE: Readonly<Record<PdfJobStatus, { variant: BadgeVariants['variant']; icon: IconName }>> = {
  Pending: { variant: 'secondary', icon: 'loader' },
  Succeeded: { variant: 'success', icon: 'circle-check' },
  Failed: { variant: 'destructive', icon: 'circle-x' },
};

@Component({
  selector: 'app-job-status-badge',
  imports: [Badge, Icon],
  template: `
    <span appBadge role="status" [variant]="appearance().variant">
      <app-icon data-icon="inline-start" [name]="appearance().icon" [class.animate-spin]="status() === 'Pending'" />
      {{ status() }}
    </span>
  `,
  host: { class: 'contents' },
})
export class JobStatusBadge {
  readonly status = input.required<PdfJobStatus>();

  protected readonly appearance = computed(() => APPEARANCE[this.status()]);
}
