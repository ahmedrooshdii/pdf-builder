import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { SessionService } from '../auth/session.service';
import { ClientContext } from '../client-context/client-context';
import { consoleInterceptor } from './console.interceptor';

function setup(client: string | null = 'ecommerce') {
  const session = { markSignedOut: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([consoleInterceptor])),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: SessionService, useValue: session },
      { provide: ClientContext, useValue: { current: signal(client) } },
    ],
  });
  const router = TestBed.inject(Router);
  const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  return { http: TestBed.inject(HttpClient), backend: TestBed.inject(HttpTestingController), session, navigate };
}

describe('consoleInterceptor', () => {
  it('marks every API request as coming from the console', () => {
    const { http, backend } = setup();
    http.get('/api/admin/clients').subscribe();
    http.post('/api/console/session', {}).subscribe();

    expect(backend.expectOne('/api/admin/clients').request.headers.get('X-Console')).toBe('1');
    expect(backend.expectOne('/api/console/session').request.headers.get('X-Console')).toBe('1');
  });

  it('leaves requests outside the API alone', () => {
    const { http, backend } = setup();
    http.get('/samples/invoice.json').subscribe();

    expect(backend.expectOne('/samples/invoice.json').request.headers.has('X-Console')).toBe(false);
  });

  it('sends the chosen client on the block endpoints only', () => {
    const { http, backend } = setup('erp');
    http.get('/api/templates').subscribe();
    http.get('/api/admin/clients/erp/keys').subscribe();
    http.get('/api/console/session').subscribe();

    expect(backend.expectOne('/api/templates').request.headers.get('X-Client-Id')).toBe('erp');
    expect(backend.expectOne('/api/admin/clients/erp/keys').request.headers.has('X-Client-Id')).toBe(false);
    expect(backend.expectOne('/api/console/session').request.headers.has('X-Client-Id')).toBe(false);
  });

  it('sends no client when none is chosen', () => {
    const { http, backend } = setup(null);
    http.get('/api/templates').subscribe();

    expect(backend.expectOne('/api/templates').request.headers.has('X-Client-Id')).toBe(false);
  });

  it('goes to sign in on a 401, remembering the page', () => {
    const { http, backend, session, navigate } = setup();
    http.get('/api/templates').subscribe({ error: () => undefined });

    backend.expectOne('/api/templates').flush({ code: 'AUTH_REQUIRED' }, { status: 401, statusText: 'Unauthorized' });

    expect(session.markSignedOut).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/sign-in?returnUrl=%2F');
  });

  it('leaves a 401 from the session endpoint to the caller', () => {
    const { http, backend, session, navigate } = setup();
    http.post('/api/console/session', {}).subscribe({ error: () => undefined });

    backend.expectOne('/api/console/session').flush({ code: 'AUTH_INVALID_KEY' }, { status: 401, statusText: 'Unauthorized' });

    expect(session.markSignedOut).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });
});
