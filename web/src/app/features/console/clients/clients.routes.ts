import { Routes } from '@angular/router';

/** The feature's public surface: everything else in this folder is private to it. */
export default [
  {
    path: '',
    title: 'Clients',
    loadComponent: () => import('./feature/clients.page').then((m) => m.ClientsPage),
  },
] satisfies Routes;
