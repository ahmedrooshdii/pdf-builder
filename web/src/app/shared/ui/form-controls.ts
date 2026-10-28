import { Directive, computed, input } from '@angular/core';
import { type ClassValue, cn } from './cn';

/** shadcn/ui `base-nova` Input. */
@Directive({ selector: 'input[appInput]', host: { 'data-slot': 'input', '[class]': 'computedClass()' } })
export class Input {
  readonly userClass = input<ClassValue>('', { alias: 'class' });
  protected readonly computedClass = computed(() =>
    cn(
      'h-8 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 py-1 text-base transition-colors outline-none placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:cursor-not-allowed disabled:bg-input/50 disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 md:text-sm dark:bg-input/30 dark:disabled:bg-input/80 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40',
      this.userClass(),
    ),
  );
}

/** shadcn/ui `base-nova` Label. */
@Directive({ selector: 'label[appLabel]', host: { 'data-slot': 'label', '[class]': 'computedClass()' } })
export class Label {
  readonly userClass = input<ClassValue>('', { alias: 'class' });
  protected readonly computedClass = computed(() =>
    cn(
      'flex items-center gap-2 text-sm leading-none font-medium select-none peer-disabled:cursor-not-allowed peer-disabled:opacity-50',
      this.userClass(),
    ),
  );
}
