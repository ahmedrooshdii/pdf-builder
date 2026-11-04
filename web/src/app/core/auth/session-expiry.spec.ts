import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { NOW } from '../../shared/util/clock';
import { SessionService } from './session.service';

@Component({ template: '' })
class Page {}

const start = Date.parse('2026-11-04T09:00:00Z');
const MINUTE = 60_000;

async function setup() {
  vi.useFakeTimers({ now: start, toFake: ['setTimeout', 'clearTimeout', 'Date'] });
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([{ path: '**', component: Page }]),
      { provide: NOW, useValue: () => Date.now() },
    ],
  });
  const harness = await RouterTestingHarness.create('/keys?client=ecommerce');
  const router = TestBed.inject(Router);
  const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  const session = TestBed.inject(SessionService);
  const http = TestBed.inject(HttpTestingController);

  const refresh = (expiresInMinutes: number) => {
    session.refresh().subscribe();
    http.expectOne('/api/console/session').flush({ expiresAt: new Date(Date.now() + expiresInMinutes * MINUTE).toISOString() });
  };
  return { harness, navigate, session, refresh };
}

describe('session expiry', () => {
  afterEach(() => vi.useRealTimers());

  it('goes to sign-in when the session ends, and not before, keeping page and client', async () => {
    const { navigate, session, refresh } = await setup();
    refresh(10);

    vi.advanceTimersByTime(10 * MINUTE - 1);
    expect(navigate).not.toHaveBeenCalled();
    expect(session.state()).toBe('signed-in');

    vi.advanceTimersByTime(1);
    expect(session.state()).toBe('signed-out');
    expect(navigate).toHaveBeenCalledWith('/sign-in?returnUrl=%2Fkeys%3Fclient%3Decommerce');
  });

  it('forgets the old end time when a new session starts', async () => {
    const { navigate, refresh } = await setup();
    refresh(10);
    refresh(12 * 60);

    vi.advanceTimersByTime(11 * MINUTE);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('stops counting down after signing out', async () => {
    const { navigate, refresh, session } = await setup();
    refresh(10);
    session.markSignedOut();

    vi.advanceTimersByTime(11 * MINUTE);
    expect(navigate).not.toHaveBeenCalled();
  });
});
