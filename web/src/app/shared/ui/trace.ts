import { Directive, computed, input } from '@angular/core';
import { type ClassValue, cn } from './cn';

/*
 * shadcn/ui Trace: a list of steps, each an icon, a title and an optional description, with a
 * status. A `running` title shimmers. Collapsible steps use the native <details>/<summary>, which
 * brings keyboard support and `aria-expanded` with no script.
 */

@Directive({
  selector: '[appTrace]',
  host: { 'data-slot': 'trace', class: 'flex w-full min-w-0 flex-col gap-2' },
})
export class Trace {}

@Directive({
  selector: '[appTraceLine]',
  host: {
    'data-slot': 'trace-line',
    '[attr.data-status]': 'status() ?? null',
    '[class]': 'computedClass()',
  },
})
export class TraceLine {
  readonly status = input<'running' | 'error' | 'pending' | undefined>();
  readonly userClass = input<ClassValue>('', { alias: 'class' });
  protected readonly computedClass = computed(() =>
    cn(
      'flex w-fit max-w-full min-w-0 items-center gap-2 text-sm text-muted-foreground transition-colors data-[status=error]:text-destructive data-[status=pending]:opacity-60 data-[status=running]:*:data-[slot=trace-title]:shimmer',
      this.userClass(),
    ),
  );
}

@Directive({
  selector: '[appTraceIcon]',
  host: {
    'data-slot': 'trace-icon',
    'aria-hidden': 'true',
    class: "flex size-4 shrink-0 items-center justify-center [&_app-icon:not([class*='size-'])]:size-4",
  },
})
export class TraceIcon {}

@Directive({
  selector: '[appTraceTitle]',
  host: { 'data-slot': 'trace-title', class: 'min-w-0 truncate motion-reduce:shimmer-none' },
})
export class TraceTitle {}

@Directive({
  selector: '[appTraceDescription]',
  host: { 'data-slot': 'trace-description', class: 'shrink-0 text-muted-foreground/70 tabular-nums' },
})
export class TraceDescription {}

@Directive({
  selector: 'details[appTraceCollapsible]',
  host: { 'data-slot': 'trace-collapsible', class: 'group/trace flex w-full min-w-0 flex-col' },
})
export class TraceCollapsible {}

@Directive({
  selector: 'summary[appTraceCollapsibleTrigger]',
  host: {
    'data-slot': 'trace-collapsible-trigger',
    class:
      'flex w-fit max-w-full min-w-0 cursor-pointer list-none items-center gap-1 rounded-md text-left text-muted-foreground transition-colors outline-none hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 has-[[data-status=error]]:text-destructive [&::-webkit-details-marker]:hidden',
  },
})
export class TraceCollapsibleTrigger {}

@Directive({
  selector: '[appTraceCollapsibleContent]',
  host: {
    'data-slot': 'trace-collapsible-content',
    class:
      'mt-2 mb-1 ml-[calc(--spacing(2)-1px)] min-w-0 border-l pr-1 pl-4 leading-relaxed wrap-break-word text-muted-foreground',
  },
})
export class TraceCollapsibleContent {}

export const TRACE = [
  Trace,
  TraceLine,
  TraceIcon,
  TraceTitle,
  TraceDescription,
  TraceCollapsible,
  TraceCollapsibleTrigger,
  TraceCollapsibleContent,
] as const;
