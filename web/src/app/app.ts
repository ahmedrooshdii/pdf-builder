import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Logo } from './core/layout/logo';
import { ThemeToggle } from './core/theme/theme-toggle';
import { Button } from './shared/ui/button';
import { Icon, IconName } from './shared/ui/icon';

interface NavLink {
  readonly path: string;
  readonly label: string;
  readonly icon: IconName;
  /** Only the exact path lights the link, so /jobs is not active on /jobs/new. */
  readonly exact: boolean;
}

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Button, Icon, Logo, ThemeToggle],
  templateUrl: './app.html',
  host: { class: 'flex min-h-dvh flex-col' },
})
export class App {
  protected readonly links: readonly NavLink[] = [
    { path: '/jobs/new', label: 'New PDF', icon: 'plus', exact: true },
    { path: '/jobs', label: 'Jobs', icon: 'list', exact: true },
    { path: '/templates', label: 'Templates', icon: 'layout-template', exact: false },
  ];
}
