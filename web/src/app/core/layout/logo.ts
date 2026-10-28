import { Component } from '@angular/core';

/**
 * The PDF Builder mark: a document in front of a tilted one, with a building block docked at its
 * corner. Drawn in the theme's primary colours, so it inverts with dark mode instead of being a
 * dark square on a light page. `public/logo.svg` is the same mark for the favicon.
 */
@Component({
  selector: 'app-logo',
  template: `
    <svg viewBox="0 0 32 32" fill="none" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" class="size-full">
      <rect width="32" height="32" rx="8" class="fill-primary" />
      <g class="stroke-primary-foreground" stroke-width="1.5">
        <g transform="rotate(-12 16 16) translate(-2.2 1.2)" opacity="0.45">
          <path d="M9.5 7h8l4.5 4.5V25h-12.5z" />
        </g>
        <path d="M10.5 6h8l4.5 4.5V24.5h-12.5z" class="fill-primary" />
        <path d="M18.5 6v4.5h4.5" />
        <path d="M13.5 14h6" stroke-dasharray="1.6 1.6" />
        <path d="M13.5 17.5h4" stroke-dasharray="1.6 1.6" />
        <path d="M19.5 18.5h6.5v6.5h-6.5z" class="fill-primary" />
        <path d="M21.5 18.5v-1.5h2.5v1.5" />
        <path d="M19.5 20.5h-1.5v2.5h1.5" />
      </g>
      <circle cx="22.75" cy="21.75" r="1.1" class="fill-primary-foreground" />
    </svg>
  `,
  host: { class: 'inline-flex shrink-0' },
})
export class Logo {}
