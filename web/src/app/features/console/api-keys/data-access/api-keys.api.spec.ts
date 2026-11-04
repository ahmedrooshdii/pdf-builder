import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { ApiKeysApi } from './api-keys.api';

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { api: TestBed.inject(ApiKeysApi), http: TestBed.inject(HttpTestingController) };
}

describe('ApiKeysApi', () => {
  it('lists a client\'s keys from the envelope', async () => {
    const { api, http } = setup();
    const result = firstValueFrom(api.list('ecommerce'));

    http.expectOne({ method: 'GET', url: '/api/admin/clients/ecommerce/keys' }).flush({ items: [] });

    expect(await result).toEqual([]);
  });

  it('issues a key with name, scopes and expiry', () => {
    const { api, http } = setup();
    api.issue('ecommerce', { name: 'production', scopes: ['pdf:generate'], expiresAt: null }).subscribe();

    const request = http.expectOne({ method: 'POST', url: '/api/admin/clients/ecommerce/keys' });
    expect(request.request.body).toEqual({ name: 'production', scopes: ['pdf:generate'], expiresAt: null });
  });

  it('revokes with DELETE', () => {
    const { api, http } = setup();
    api.revoke('ecommerce', 'k7f2a9c1').subscribe();

    http.expectOne({ method: 'DELETE', url: '/api/admin/clients/ecommerce/keys/k7f2a9c1' }).flush(null, { status: 204, statusText: 'No Content' });
  });

  it('reads the scopes a key can have', async () => {
    const { api, http } = setup();
    const result = firstValueFrom(api.scopes());

    http.expectOne('/api/admin/scopes').flush({ items: [{ name: 'pdf:generate', description: 'Submit jobs and read them.' }] });

    expect(await result).toEqual([{ name: 'pdf:generate', description: 'Submit jobs and read them.' }]);
  });
});
