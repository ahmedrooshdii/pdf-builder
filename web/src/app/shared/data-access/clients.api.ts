import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { API_BASE_URL } from '../../core/http/api-base-url';
import { Client, ListEnvelope } from './client.models';

/**
 * Typed wrapper over `/api/admin/clients`. In `shared` because the client switcher (core) and the
 * clients page (a feature) both use it.
 */
@Injectable({ providedIn: 'root' })
export class ClientsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${inject(API_BASE_URL)}/api/admin/clients`;

  /** Every client, ordered by id. */
  list(): Observable<readonly Client[]> {
    return this.http.get<ListEnvelope<Client>>(this.baseUrl).pipe(map((envelope) => envelope.items));
  }

  create(client: { readonly id: string; readonly name: string }): Observable<Client> {
    return this.http.post<Client>(this.baseUrl, client);
  }

  update(clientId: string, changes: { readonly disabled?: boolean; readonly name?: string }): Observable<Client> {
    return this.http.patch<Client>(`${this.baseUrl}/${encodeURIComponent(clientId)}`, changes);
  }
}
