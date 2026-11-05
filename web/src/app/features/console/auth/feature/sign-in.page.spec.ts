import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { SessionService, SessionState } from '../../../../core/auth/session.service';
import { SignInPage } from './sign-in.page';

const ADMIN_KEY = 'adm_lPQ25ykvuTW7HdOJLaJzfqnJsT4cKQfwwR5pgq4o';

function setup(answer: () => Observable<void>, options: { state?: SessionState; returnUrl?: string } = {}) {
  const state = signal<SessionState>(options.state ?? 'signed-out');
  const session = { state, ensureKnown: () => Promise.resolve(), signIn: vi.fn(answer) };
  TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: SessionService, useValue: session }] });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

  const fixture = TestBed.createComponent(SignInPage);
  if (options.returnUrl !== undefined) fixture.componentRef.setInput('returnUrl', options.returnUrl);
  TestBed.tick();

  const el: HTMLElement = fixture.nativeElement;
  const find = <T extends Element>(selector: string): T => {
    const found = el.querySelector<T>(selector);
    if (!found) throw new Error(`Nothing matches '${selector}'.`);
    return found;
  };
  const field = () => find<HTMLInputElement>('#admin-key');
  const signIn = async (value: string) => {
    field().value = value;
    field().dispatchEvent(new Event('input'));
    find<HTMLButtonElement>('button[type=submit]').click();
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  };
  const message = () => el.querySelector('#sign-in-error')?.textContent?.trim();

  return { el, find, field, signIn, message, session, navigate };
}

const refused = (status: number) => () => throwError(() => new HttpErrorResponse({ status }));

describe('SignInPage', () => {
  it('signs in and returns to the page first asked for', async () => {
    const { signIn, session, navigate } = setup(() => of(undefined), { returnUrl: '/templates?client=ecommerce' });

    await signIn(ADMIN_KEY);

    expect(session.signIn).toHaveBeenCalledWith(ADMIN_KEY);
    expect(navigate).toHaveBeenCalledWith('/templates?client=ecommerce');
  });

  it('refuses to return to another site', async () => {
    const { signIn, navigate } = setup(() => of(undefined), { returnUrl: '//evil.example' });
    await signIn(ADMIN_KEY);
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it.each([
    [401, 'That key was not accepted.'],
    [429, 'Too many attempts. Try again in a minute.'],
    [503, 'The service cannot reach its storage. Try again in a few seconds.'],
    [0, 'The API could not be reached.'],
  ])('answers %i with "%s" and empties the field', async (status, text) => {
    const { signIn, message, field } = setup(refused(status));

    await signIn('adm_wrong');

    expect(message()).toBe(text);
    expect(field().value).toBe('');
  });

  it('empties the field after a successful attempt too', async () => {
    const { signIn, field } = setup(() => of(undefined));
    await signIn(ADMIN_KEY);
    expect(field().value).toBe('');
  });

  it('shows and hides the key', () => {
    const { find, field } = setup(() => of(undefined));
    const toggle = find<HTMLButtonElement>('button[aria-pressed]');

    expect(field().type).toBe('password');
    toggle.click();
    TestBed.tick();
    expect(field().type).toBe('text');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
  });

  it('goes straight on when already signed in', async () => {
    const { navigate } = setup(() => of(undefined), { state: 'signed-in', returnUrl: '/keys' });
    await new Promise((resolve) => setTimeout(resolve));
    expect(navigate).toHaveBeenCalledWith('/keys');
  });
});
