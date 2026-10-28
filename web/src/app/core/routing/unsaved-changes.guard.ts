import { CanDeactivateFn } from '@angular/router';

/** A page that can hold edits the user has not saved yet. */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * Asks before navigating away from unsaved edits. Covers in-app navigation; the page itself
 * handles `beforeunload` for closing the tab or reloading.
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (page) =>
  !page.hasUnsavedChanges() || confirm('You have unsaved changes. Leave without saving?');
