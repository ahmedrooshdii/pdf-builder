import { Directive, computed, input } from '@angular/core';
import { type ClassValue, cn } from './cn';

/**
 * shadcn/ui ScrollArea, done with native scrolling: thin theme-coloured scrollbars and edges that
 * fade while there is more to scroll (shadcn's `scroll-fade` utilities). Native scrolling keeps
 * momentum, keyboard and screen-reader behaviour for free and costs no JavaScript per frame.
 */
@Directive({
  selector: '[appScrollArea]',
  host: { 'data-slot': 'scroll-area', tabindex: '0', '[class]': 'computedClass()' },
})
export class ScrollArea {
  readonly orientation = input<'vertical' | 'horizontal' | 'both'>('vertical');
  readonly userClass = input<ClassValue>('', { alias: 'class' });

  protected readonly computedClass = computed(() => {
    const axis = {
      vertical: 'overflow-y-auto overflow-x-hidden scroll-fade-y',
      horizontal: 'overflow-x-auto overflow-y-hidden scroll-fade-x',
      both: 'overflow-auto scroll-fade',
    }[this.orientation()];
    return cn(
      'relative min-h-0 min-w-0 scrollbar-thin rounded-[inherit] outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50',
      axis,
      this.userClass(),
    );
  });
}
