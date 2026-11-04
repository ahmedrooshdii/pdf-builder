import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

/**
 * The block's name, in one place: page titles, the header and the sign-in card read it from here.
 * A deployment's own product name replaces it with runtime branding (feature 012).
 */
export const APP_NAME = 'PDF Builder';

/** Turns a route's `title` into `"New PDF · PDF Builder"`, so routes only name themselves. */
@Injectable()
export class PageTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const page = this.buildTitle(snapshot);
    this.title.setTitle(page ? `${page} · ${APP_NAME}` : APP_NAME);
  }
}
