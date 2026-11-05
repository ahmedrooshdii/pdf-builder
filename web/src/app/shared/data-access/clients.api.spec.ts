import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { Client } from './client.models';
import { ClientsApi } from './clients.api';

const client: Client = {
  id: 'ecommerce',
  name: 'E-commerce',
  status: 'Active',
  createdAt: '2026-11-04T09:00:00Z',
  disabledAt: null,
};

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { api: TestBed.inject(ClientsApi), http: TestBed.inject(HttpTestingController) };
}

describe('ClientsApi', () => {
  it('unwraps the list envelope', async () => {
    const { api, http } = setup();
    const result = firstValueFrom(api.list());

    http.expectOne({ method: 'GET', url: '/api/admin/clients' }).flush({ items: [client] });

    expect(await result).toEqual([client]);
  });

  it('registers a client', async () => {
    const { api, http } = setup();
    const result = firstValueFrom(api.create({ id: 'erp', name: 'ERP' }));

    const request = http.expectOne({ method: 'POST', url: '/api/admin/clients' });
    expect(request.request.body).toEqual({ id: 'erp', name: 'ERP' });
    request.flush({ ...client, id: 'erp', name: 'ERP' });

    expect((await result).id).toBe('erp');
  });

  it('disables a client with PATCH', () => {
    const { api, http } = setup();
    api.update('ecommerce', { disabled: true }).subscribe();

    const request = http.expectOne({ method: 'PATCH', url: '/api/admin/clients/ecommerce' });
    expect(request.request.body).toEqual({ disabled: true });
    request.flush({ ...client, status: 'Disabled' });
  });
});
