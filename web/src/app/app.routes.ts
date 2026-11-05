import { Routes } from '@angular/router';
import { signedIn } from './core/auth/signed-in.guard';

/**
 * Each feature is lazy-loaded through its own routes file, so adding one never grows the initial
 * bundle. Everything but sign-in sits inside the shell, behind a session (contracts/console-ui.md).
 * `needsClient` marks the pages that work within one client: the shell asks for one until it is chosen.
 */
export const routes: Routes = [
  { path: 'sign-in', loadChildren: () => import('./features/console/auth/auth.routes') },
  {
    path: '',
    canMatch: [signedIn],
    loadComponent: () => import('./core/layout/shell/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'jobs/new' },
      { path: 'jobs', data: { needsClient: true }, loadChildren: () => import('./features/pdf-jobs/pdf-jobs.routes') },
      { path: 'templates', data: { needsClient: true }, loadChildren: () => import('./features/templates/templates.routes') },
      { path: 'keys', data: { needsClient: true }, loadChildren: () => import('./features/console/api-keys/api-keys.routes') },
      { path: 'clients', loadChildren: () => import('./features/console/clients/clients.routes') },
    ],
  },
  {
    path: '**',
    title: 'Not found',
    loadComponent: () => import('./core/layout/not-found.page').then((m) => m.NotFoundPage),
  },
];
