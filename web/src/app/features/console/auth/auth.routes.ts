import { Routes } from '@angular/router';

/** The feature's public surface: everything else in this folder is private to it. */
export default [
  {
    path: '',
    title: 'Sign in',
    loadComponent: () => import('./feature/sign-in.page').then((m) => m.SignInPage),
  },
] satisfies Routes;
