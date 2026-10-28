import { Component, computed, input } from '@angular/core';
import { TemplateSource, TemplateStatus } from '../../../shared/data-access/template.models';
import { Badge, BadgeVariants } from '../../../shared/ui/badge';
import { Icon, IconName } from '../../../shared/ui/icon';

const STATUS: Readonly<Record<TemplateStatus, { label: string; variant: BadgeVariants['variant']; icon: IconName }>> = {
  Draft: { label: 'Draft', variant: 'secondary', icon: 'circle-dot' },
  Published: { label: 'Published', variant: 'success', icon: 'circle-check' },
  Modified: { label: 'Unpublished changes', variant: 'warning', icon: 'circle-dot' },
};

/** Whether jobs can use the template, at a glance; built-ins also say they are read-only. */
@Component({
  selector: 'app-template-status-badge',
  imports: [Badge, Icon],
  template: `
    <span appBadge [variant]="appearance().variant">
      <app-icon data-icon="inline-start" [name]="appearance().icon" />
      {{ appearance().label }}
    </span>
    @if (source() === 'BuiltIn') {
      <span appBadge variant="outline">
        <app-icon data-icon="inline-start" name="lock" />
        Built-in
      </span>
    }
  `,
  host: { class: 'inline-flex flex-wrap items-center gap-1.5' },
})
export class TemplateStatusBadge {
  readonly status = input.required<TemplateStatus>();
  readonly source = input<TemplateSource>('Authored');

  protected readonly appearance = computed(() => STATUS[this.status()]);
}
