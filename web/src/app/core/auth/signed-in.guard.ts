import { inject } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { SIGN_IN_PATH } from './redirect-to-sign-in';
import { SessionService } from './session.service';

/**
 * Lets the console's pages match only with a session. The first time, it waits for the server's
 * answer instead of redirecting while the state is still unknown, so a reload of a signed-in tab
 * never flashes the sign-in page.
 */
export const signedIn: CanMatchFn = async () => {
  const session = inject(SessionService);
  const router = inject(Router);

  await session.ensureKnown();
  if (session.state() === 'signed-in') return true;

  const attempted = router.currentNavigation()?.extractedUrl.toString() ?? router.url;
  return router.createUrlTree([SIGN_IN_PATH], { queryParams: { returnUrl: attempted } });
};
