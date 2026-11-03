import { Component } from '@angular/core';

/**
 * The PDF Builder mark: a page with a folded corner and a solid block docked at its foot. Drawn in
 * the theme's primary colours, so it inverts with dark mode instead of being a dark square on a
 * light page. No tilts or dashed strokes, so it stays legible at favicon size. `public/logo.svg` is
 * the same mark for the favicon.
 */
@Component({
  selector: 'app-logo',
  template: `
    <svg viewBox="0 0 32 32" fill="none" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" class="size-full">
      <rect width="32" height="32" rx="8" class="fill-primary" />
      <g class="stroke-primary-foreground" stroke-width="1.75">
        <path d="M10 6.5h7.5L22 11v6.5M14.5 25.5H10v-19" />
        <path d="M17.5 6.5V11H22" />
        <path d="M13 14.5h5" />
      </g>
      <rect x="16.5" y="18.5" width="9" height="9" rx="2" class="fill-primary-foreground" />
    </svg>
  `,
  host: { class: 'inline-flex shrink-0' },
})
export class Logo {}
