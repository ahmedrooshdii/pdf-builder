import { Directive, computed, input } from '@angular/core';
import { type VariantProps, cva } from 'class-variance-authority';
import { type ClassValue, cn } from './cn';

/** shadcn/ui `base-nova` Badge, plus a `success` variant built the same way as `destructive`. */
export const badgeVariants = cva(
  'group/badge inline-flex h-5 w-fit shrink-0 items-center justify-center gap-1 overflow-hidden rounded-4xl border border-transparent px-2 py-0.5 text-xs font-medium whitespace-nowrap transition-all focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&_app-icon]:size-3!',
  {
    variants: {
      variant: {
        default: 'bg-primary text-primary-foreground',
        secondary: 'bg-secondary text-secondary-foreground',
        destructive: 'bg-destructive/10 text-destructive dark:bg-destructive/20',
        success: 'bg-success/10 text-success dark:bg-success/20',
        warning: 'bg-warning/10 text-warning dark:bg-warning/15',
        outline: 'border-border text-foreground',
        ghost: 'hover:bg-muted hover:text-muted-foreground dark:hover:bg-muted/50',
      },
    },
    defaultVariants: { variant: 'default' },
  },
);

export type BadgeVariants = VariantProps<typeof badgeVariants>;

@Directive({
  selector: '[appBadge]',
  host: { 'data-slot': 'badge', '[class]': 'computedClass()' },
})
export class Badge {
  readonly variant = input<BadgeVariants['variant']>('default');
  readonly userClass = input<ClassValue>('', { alias: 'class' });

  protected readonly computedClass = computed(() => cn(badgeVariants({ variant: this.variant() }), this.userClass()));
}
