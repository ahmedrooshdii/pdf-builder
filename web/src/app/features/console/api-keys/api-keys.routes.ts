import { Routes } from '@angular/router';
import { unsavedChangesGuard } from '../../../core/routing/unsaved-changes.guard';

/** The feature's public surface: everything else in this folder is private to it. */
export default [
  {
    path: '',
    title: 'API keys',
    // While a new key is on screen, leaving asks first: the key cannot be shown again.
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./feature/api-keys.page').then((m) => m.ApiKeysPage),
  },
] satisfies Routes;
