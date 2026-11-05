import { DatePipe } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRouteSnapshot, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { Button } from '../../../shared/ui/button';
import { DROPDOWN_MENU } from '../../../shared/ui/dropdown-menu';
import { Icon, IconName } from '../../../shared/ui/icon';
import { formatUtc } from '../../../shared/util/clock';
import { SIGN_IN_PATH } from '../../auth/redirect-to-sign-in';
import { SessionService } from '../../auth/session.service';
import { ClientContext } from '../../client-context/client-context';
import { ThemeToggle } from '../../theme/theme-toggle';
import { Logo } from '../logo';
import { APP_NAME } from '../page-title.strategy';
import { ChooseClient } from './choose-client';

interface NavLink {
  readonly path: string;
  readonly label: string;
  readonly icon: IconName;
  /** Only the exact path lights the link, so /jobs is not active on /jobs/new. */
  readonly exact: boolean;
}

/** True when the route, or any route above it, says `data: { needsClient: true }`. */
function needsClient(snapshot: ActivatedRouteSnapshot | null): boolean {
  for (let route = snapshot; route; route = route.firstChild) {
    if (route.data['needsClient'] === true) return true;
  }
  return false;
}

/**
 * The frame of every signed-in page (research R12): the client switcher, the nav and the session
 * menu in one header, so the chosen client is in the same place on every page (FR-014). Pages that
 * work within a client are replaced by a "choose a client" state while none is chosen.
 */
@Component({
  selector: 'app-shell',
  imports: [DatePipe, RouterOutlet, RouterLink, RouterLinkActive, Button, ChooseClient, DROPDOWN_MENU, Icon, Logo, ThemeToggle],
  templateUrl: './shell.html',
  host: { class: 'flex min-h-dvh flex-col' },
})
export class Shell {
  protected readonly session = inject(SessionService);
  protected readonly context = inject(ClientContext);
  private readonly router = inject(Router);

  protected readonly appName = APP_NAME;
  protected readonly links: readonly NavLink[] = [
    { path: '/jobs/new', label: 'New PDF', icon: 'plus', exact: true },
    { path: '/jobs', label: 'Jobs', icon: 'list', exact: true },
    { path: '/templates', label: 'Templates', icon: 'layout-template', exact: false },
    { path: '/keys', label: 'API keys', icon: 'key-round', exact: false },
    { path: '/clients', label: 'Clients', icon: 'users', exact: false },
  ];

  /** Links keep the chosen client, and only that, from page to page. */
  protected readonly clientParams = computed(() => {
    const client = this.context.current();
    return client ? { client } : {};
  });

  private readonly pageNeedsClient = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => needsClient(this.router.routerState.snapshot.root)),
    ),
    { initialValue: needsClient(this.router.routerState.snapshot.root) },
  );

  protected readonly choosingClient = computed(() => this.pageNeedsClient() && this.context.current() === null);

  protected readonly sessionEndsUtc = computed(() => {
    const at = this.session.expiresAt();
    return at ? formatUtc(at) : '';
  });

  protected readonly clientsProblem = computed(() => this.context.clients.error() !== undefined);

  protected signOut(): void {
    this.session.signOut().subscribe({ complete: () => void this.router.navigateByUrl(SIGN_IN_PATH) });
  }
}
