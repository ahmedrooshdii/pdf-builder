import { InjectionToken } from '@angular/core';

/**
 * Origin the PDF Builder API is reached on, with no trailing slash.
 *
 * Empty means same origin, and that is the intended setup everywhere: the dev server proxies
 * `/api` and `/files` to the API (`proxy.conf.json`), and in production one reverse proxy serves
 * both. Keeping them on one origin is what lets the API stay free of CORS configuration.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  factory: () => '',
});
