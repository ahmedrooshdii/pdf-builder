import { Routes } from '@angular/router';
import { unsavedChangesGuard } from '../../core/routing/unsaved-changes.guard';

/** The feature's public surface: everything else in this folder is private to it. */
export default [
  {
    path: '',
    title: 'Templates',
    loadComponent: () => import('./feature/templates-list/templates.page').then((m) => m.TemplatesPage),
  },
  {
    path: ':templateKey',
    title: 'Edit template',
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () =>
      import('./feature/template-editor/template-editor.page').then((m) => m.TemplateEditorPage),
  },
] satisfies Routes;
