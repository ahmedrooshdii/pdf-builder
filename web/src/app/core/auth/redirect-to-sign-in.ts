import { Router } from '@angular/router';

export const SIGN_IN_PATH = '/sign-in';

/**
 * Sends the operator to sign in, remembering where they were (path, `?client=` and the rest of
 * the query) so signing in brings them back. A navigation like any other: an unsaved-changes guard
 * on the current page runs, and if the operator chooses to stay, the page and its edits remain.
 */
export function redirectToSignIn(router: Router): Promise<boolean> {
  const current = router.url;
  if (current.startsWith(SIGN_IN_PATH)) return Promise.resolve(false);
  return router.navigateByUrl(`${SIGN_IN_PATH}?returnUrl=${encodeURIComponent(current)}`);
}
