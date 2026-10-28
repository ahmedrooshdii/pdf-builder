import { Routes } from '@angular/router';

/** Each feature is lazy-loaded through its own routes file, so adding one never grows the initial bundle. */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'jobs/new' },
  { path: 'jobs', loadChildren: () => import('./features/pdf-jobs/pdf-jobs.routes') },
  { path: 'templates', loadChildren: () => import('./features/templates/templates.routes') },
  {
    path: '**',
    title: 'Not found',
    loadComponent: () => import('./core/layout/not-found.page').then((m) => m.NotFoundPage),
  },
];
