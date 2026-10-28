import { HttpErrorResponse } from '@angular/common/http';

/** RFC 9457 body, in the shape ASP.NET Core's `Results.ValidationProblem` emits it. */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  /** Machine-readable reason the API adds to domain failures, for example `TEMPLATE_KEY_TAKEN`. */
  readonly code?: string;
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

/** Normalises any failure from `HttpClient` into a problem the UI can show. */
export function toProblemDetails(error: unknown): ProblemDetails {
  if (!(error instanceof HttpErrorResponse)) {
    return { title: 'Something went wrong.' };
  }

  if (isProblemDetails(error.error)) {
    return { status: error.status, ...error.error };
  }

  // Status 0 is the browser's way of saying the request never got an answer.
  return error.status === 0
    ? { status: 0, title: 'The API could not be reached.' }
    : { status: error.status, title: error.statusText || `Request failed (${error.status}).` };
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null && ('title' in value || 'errors' in value);
}
