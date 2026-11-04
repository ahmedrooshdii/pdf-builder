import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ClientContext } from '../client-context/client-context';
import { consoleInterceptor } from '../http/console.interceptor';
import { HasUnsavedChanges, unsavedChangesGuard } from '../routing/unsaved-changes.guard';
import { safeReturnUrl } from './safe-return-url';

/** Stands in for the template editor: a page holding edits it has not saved. */
@Component({ template: '<textarea id="edits">{{ edits() }}</textarea>' })
class Editor implements HasUnsavedChanges {
  readonly edits = signal('<h1>Invoice</h1> changed');
  hasUnsavedChanges(): boolean {
    return true;
  }
}

@Component({ template: 'sign in' })
class SignIn {}

async function setup(stay: boolean) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([consoleInterceptor])),
      provideHttpClientTesting(),
      provideRouter([
        { path: 'sign-in', component: SignIn },
        { path: 'templates/:key', component: Editor, canDeactivate: [unsavedChangesGuard] },
      ]),
      { provide: ClientContext, useValue: { current: signal('ecommerce') } },
    ],
  });
  const harness = await RouterTestingHarness.create('/templates/invoice?client=ecommerce');
  const confirm = vi.spyOn(globalThis, 'confirm').mockReturnValue(!stay);

  // Saving fails: the session ended in another tab.
  TestBed.inject(HttpClient).put('/api/templates/invoice/draft', {}).subscribe({ error: () => undefined });
  TestBed.inject(HttpTestingController)
    .expectOne('/api/templates/invoice/draft')
    .flush({ code: 'AUTH_REQUIRED' }, { status: 401, statusText: 'Unauthorized' });
  await harness.fixture.whenStable();

  return { harness, router: TestBed.inject(Router), confirm };
}

describe('a session that ends mid-edit', () => {
  afterEach(() => vi.restoreAllMocks());

  it('warns about unsaved changes before leaving for sign-in, and keeps the edits if the operator stays', async () => {
    const { harness, router, confirm } = await setup(true);

    expect(confirm).toHaveBeenCalledWith('You have unsaved changes. Leave without saving?');
    expect(router.url).toBe('/templates/invoice?client=ecommerce');
    expect(harness.routeNativeElement?.querySelector('#edits')?.textContent).toBe('<h1>Invoice</h1> changed');
  });

  it('returns to the same page and client after signing in again', async () => {
    const { harness, router } = await setup(false);

    expect(router.url).toBe('/sign-in?returnUrl=%2Ftemplates%2Finvoice%3Fclient%3Decommerce');

    const returnUrl = router.parseUrl(router.url).queryParams['returnUrl'] as string;
    await harness.navigateByUrl(safeReturnUrl(returnUrl));
    expect(router.url).toBe('/templates/invoice?client=ecommerce');
  });
});
