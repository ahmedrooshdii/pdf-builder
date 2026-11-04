import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { SessionService, SessionState } from './session.service';
import { signedIn } from './signed-in.guard';

@Component({ template: 'inside' })
class Inside {}

@Component({ template: 'sign in' })
class SignIn {}

function setup() {
  const state = signal<SessionState>('unknown');
  let resolve: () => void = () => undefined;
  const known = new Promise<void>((done) => (resolve = done));
  const session = { state, ensureKnown: vi.fn(() => known) };

  TestBed.configureTestingModule({
    providers: [
      { provide: SessionService, useValue: session },
      provideRouter([
        { path: 'sign-in', component: SignIn },
        { path: '', canMatch: [signedIn], children: [{ path: 'keys', component: Inside }] },
      ]),
    ],
  });
  const answer = (value: SessionState) => {
    state.set(value);
    resolve();
  };
  return { answer, session };
}

const later = () => new Promise((resolve) => setTimeout(resolve));

describe('signedIn', () => {
  it('waits for the first answer instead of redirecting while unknown', async () => {
    const { answer, session } = setup();
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);

    const navigation = harness.navigateByUrl('/keys?client=ecommerce');
    await later();
    expect(session.ensureKnown).toHaveBeenCalled();
    expect(router.url).not.toContain('sign-in');

    answer('signed-in');
    await navigation;
    expect(router.url).toBe('/keys?client=ecommerce');
  });

  it('sends a signed-out browser to sign in, remembering where it was going', async () => {
    const { answer } = setup();
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);

    const navigation = harness.navigateByUrl('/keys?client=ecommerce');
    answer('signed-out');
    await navigation;

    expect(router.url).toBe('/sign-in?returnUrl=%2Fkeys%3Fclient%3Decommerce');
  });
});
