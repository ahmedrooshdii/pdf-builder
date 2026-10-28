import { HttpErrorResponse } from '@angular/common/http';
import { toProblemDetails } from './problem-details';

describe('toProblemDetails', () => {
  it('passes a problem body through, keeping the status', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { title: 'Invalid', errors: { data: ['Data must be a JSON object.'] } },
    });

    expect(toProblemDetails(error)).toEqual({
      status: 400,
      title: 'Invalid',
      errors: { data: ['Data must be a JSON object.'] },
    });
  });

  it('describes a request that never got an answer', () => {
    expect(toProblemDetails(new HttpErrorResponse({ status: 0 })).title).toBe('The API could not be reached.');
  });

  it('falls back to the status text for a body that is not a problem', () => {
    const error = new HttpErrorResponse({ status: 502, statusText: 'Bad Gateway', error: '<html>' });
    expect(toProblemDetails(error)).toEqual({ status: 502, title: 'Bad Gateway' });
  });

  it('handles errors that did not come from HTTP', () => {
    expect(toProblemDetails(new Error('boom')).title).toBe('Something went wrong.');
  });
});
