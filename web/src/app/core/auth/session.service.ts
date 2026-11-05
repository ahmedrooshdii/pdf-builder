import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, firstValueFrom, map, of, switchMap, tap, throwError } from 'rxjs';
import { NOW } from '../../shared/util/clock';
import { API_BASE_URL } from '../http/api-base-url';
import { redirectToSignIn } from './redirect-to-sign-in';

export type SessionState = 'unknown' | 'signed-in' | 'signed-out';

interface SessionResponse {
  readonly expiresAt: string;
}

/**
 * The operator's console session (contracts/console-session.md).
 *
 * The session itself lives in an `HttpOnly` cookie this code cannot read, which is the point: a
 * script injected into the page cannot carry it away. This service only knows whether there is one
 * and when it ends. The admin key passes through `signIn` as an argument and is never kept: not in
 * a field, not in storage.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly now = inject(NOW);
  private readonly url = `${inject(API_BASE_URL)}/api/console/session`;

  private readonly stateSignal = signal<SessionState>('unknown');
  private readonly expiresAtSignal = signal<Date | null>(null);

  readonly state = this.stateSignal.asReadonly();
  readonly expiresAt = this.expiresAtSignal.asReadonly();

  private firstRefresh: Promise<void> | null = null;
  private expiryTimer: ReturnType<typeof setTimeout> | undefined;

  /** Exchanges the admin key for a session cookie, then reads the session back. */
  signIn(adminKey: string): Observable<void> {
    return this.http.post<null>(this.url, { adminKey }).pipe(switchMap(() => this.refresh()));
  }

  /** Ends the session on the server. Signed out here whatever the answer, so a failure never strands the operator. */
  signOut(): Observable<void> {
    return this.http.delete<null>(this.url).pipe(
      map(() => undefined),
      catchError(() => of(undefined)),
      finalize(() => this.markSignedOut()),
    );
  }

  /**
   * Asks the server whether this browser has a session. A 401 means signed out; so does any other
   * failure (the API down, storage unavailable), which then shows at sign-in with its own message.
   */
  refresh(): Observable<void> {
    return this.http.get<SessionResponse>(this.url).pipe(
      tap((session) => this.markSignedIn(new Date(session.expiresAt))),
      map(() => undefined),
      catchError((error: unknown) => {
        this.markSignedOut();
        return error instanceof HttpErrorResponse && error.status === 401 ? of(undefined) : throwError(() => error);
      }),
    );
  }

  /** Resolves once the state is known, refreshing the first time only. For the route guard. */
  ensureKnown(): Promise<void> {
    if (this.stateSignal() !== 'unknown') return Promise.resolve();
    this.firstRefresh ??= firstValueFrom(this.refresh()).catch(() => undefined);
    return this.firstRefresh;
  }

  /** The server refused the session (a 401): stop treating this browser as signed in. */
  markSignedOut(): void {
    clearTimeout(this.expiryTimer);
    this.expiresAtSignal.set(null);
    this.stateSignal.set('signed-out');
  }

  private markSignedIn(expiresAt: Date): void {
    this.expiresAtSignal.set(expiresAt);
    this.stateSignal.set('signed-in');

    // When the session ends by itself, go to sign-in then rather than at the next click.
    clearTimeout(this.expiryTimer);
    const delay = Math.min(Math.max(0, expiresAt.getTime() - this.now()), 2 ** 31 - 1);
    this.expiryTimer = setTimeout(() => {
      this.markSignedOut();
      void redirectToSignIn(this.router);
    }, delay);
  }
}
