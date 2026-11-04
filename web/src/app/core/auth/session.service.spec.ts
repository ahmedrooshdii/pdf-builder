import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BROWSER_STORAGE } from '../storage/browser-storage';
import { SessionService } from './session.service';

const ADMIN_KEY = 'adm_lPQ25ykvuTW7HdOJLaJzfqnJsT4cKQfwwR5pgq4o';
const URL = '/api/console/session';

function setup() {
  const storage = { getItem: vi.fn(() => null), setItem: vi.fn(), removeItem: vi.fn() } as unknown as Storage;
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: BROWSER_STORAGE, useValue: storage },
    ],
  });
  return { session: TestBed.inject(SessionService), http: TestBed.inject(HttpTestingController), storage };
}

describe('SessionService', () => {
  it('starts unknown', () => {
    expect(setup().session.state()).toBe('unknown');
  });

  it('signs in, then reads the session back', async () => {
    const { session, http } = setup();
    const done = firstValueFrom(session.signIn(ADMIN_KEY));

    const post = http.expectOne({ method: 'POST', url: URL });
    expect(post.request.body).toEqual({ adminKey: ADMIN_KEY });
    post.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne({ method: 'GET', url: URL }).flush({ expiresAt: '2026-11-05T00:00:00Z' });
    await done;

    expect(session.state()).toBe('signed-in');
    expect(session.expiresAt()?.toISOString()).toBe('2026-11-05T00:00:00.000Z');
  });

  it('never writes the admin key anywhere it could be read later', async () => {
    const { session, http, storage } = setup();
    const local = vi.spyOn(Storage.prototype, 'setItem');

    const done = firstValueFrom(session.signIn(ADMIN_KEY)).catch(() => undefined);
    http.expectOne(URL).flush({ code: 'AUTH_INVALID_KEY' }, { status: 401, statusText: 'Unauthorized' });
    await done;

    expect(storage.setItem).not.toHaveBeenCalled();
    expect(local).not.toHaveBeenCalled();
    local.mockRestore();
  });

  it('is signed out when the server has no session', async () => {
    const { session, http } = setup();
    const known = session.ensureKnown();

    http.expectOne({ method: 'GET', url: URL }).flush({ code: 'AUTH_REQUIRED' }, { status: 401, statusText: 'Unauthorized' });
    await known;

    expect(session.state()).toBe('signed-out');
  });

  it('refreshes only once for every guard that waits', async () => {
    const { session, http } = setup();
    const first = session.ensureKnown();
    const second = session.ensureKnown();

    http.expectOne(URL).flush({ expiresAt: '2026-11-05T00:00:00Z' });
    await Promise.all([first, second]);

    http.verify();
    expect(session.state()).toBe('signed-in');
  });

  it('is signed out after signing out, even when the request fails', async () => {
    const { session, http } = setup();
    const done = firstValueFrom(session.signOut(), { defaultValue: undefined });

    http.expectOne({ method: 'DELETE', url: URL }).flush(null, { status: 503, statusText: 'Unavailable' });
    await done;

    expect(session.state()).toBe('signed-out');
  });
});
