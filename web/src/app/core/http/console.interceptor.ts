import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { redirectToSignIn } from '../auth/redirect-to-sign-in';
import { SessionService } from '../auth/session.service';
import { ClientContext } from '../client-context/client-context';
import { API_BASE_URL } from './api-base-url';

const SESSION_PATH = '/api/console/session';

/**
 * Every request the console makes to the API goes through here (research R14):
 *
 * 1. `X-Console: 1` on every request: the API refuses a change made with the session cookie
 *    without it, which a page on another site cannot add.
 * 2. `X-Client-Id` with the chosen client on the block's own endpoints (jobs, templates), so every
 *    page works within that client. Not on `/api/admin/` (the client is in the path there) or on
 *    the session endpoints.
 * 3. A 401 from anything but the session endpoint means the session has ended: sign in again,
 *    returning to this page afterwards.
 */
export const consoleInterceptor: HttpInterceptorFn = (request, next) => {
  const base = inject(API_BASE_URL);
  if (!request.url.startsWith(`${base}/api/`)) return next(request);

  const path = request.url.slice(base.length);
  let headers = request.headers.set('X-Console', '1');

  if (!path.startsWith('/api/admin/') && !path.startsWith('/api/console/')) {
    const client = inject(ClientContext).current();
    if (client) headers = headers.set('X-Client-Id', client);
  }

  const session = inject(SessionService);
  const router = inject(Router);

  return next(request.clone({ headers })).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !path.startsWith(SESSION_PATH)) {
        session.markSignedOut();
        void redirectToSignIn(router);
      }
      return throwError(() => error);
    }),
  );
};
