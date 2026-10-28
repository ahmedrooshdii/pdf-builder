import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toProblemDetails } from '../../../../core/http/problem-details';
import { isUsable } from '../../../../shared/data-access/template.models';
import { TemplatesApi } from '../../../../shared/data-access/templates.api';
import { Button } from '../../../../shared/ui/button';
import { Skeleton } from '../../../../shared/ui/feedback';
import { Icon } from '../../../../shared/ui/icon';
import { ScrollArea } from '../../../../shared/ui/scroll-area';
import { TABLE } from '../../../../shared/ui/table';
import { TemplateKeyDialog } from '../../ui/template-key-dialog';
import { TemplateStatusBadge } from '../../ui/template-status-badge';

@Component({
  selector: 'app-templates-page',
  imports: [
    DatePipe,
    RouterLink,
    Button,
    Icon,
    ScrollArea,
    Skeleton,
    TABLE,
    TemplateKeyDialog,
    TemplateStatusBadge,
  ],
  templateUrl: './templates.page.html',
  host: { class: 'grid gap-6' },
})
export class TemplatesPage {
  private readonly api = inject(TemplatesApi);
  private readonly router = inject(Router);

  protected readonly templates = rxResource({ stream: () => this.api.list() });
  protected readonly isUsable = isUsable;

  protected readonly creating = signal(false);
  protected readonly busy = signal(false);
  protected readonly createError = signal<string | null>(null);

  protected openCreate(): void {
    this.createError.set(null);
    this.creating.set(true);
  }

  protected async create(templateKey: string): Promise<void> {
    this.busy.set(true);
    this.createError.set(null);
    try {
      const created = await firstValueFrom(this.api.create(templateKey));
      this.creating.set(false);
      await this.router.navigate(['/templates', created.templateKey]);
    } catch (error) {
      const problem = toProblemDetails(error);
      this.createError.set(problem.detail ?? Object.values(problem.errors ?? {})[0]?.[0] ?? problem.title ?? 'Could not create the template.');
    } finally {
      this.busy.set(false);
    }
  }
}
