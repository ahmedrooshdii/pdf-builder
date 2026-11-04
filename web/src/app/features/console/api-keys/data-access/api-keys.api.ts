import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { API_BASE_URL } from '../../../../core/http/api-base-url';
import { ListEnvelope } from '../../../../shared/data-access/client.models';
import { ApiKey, IssueKeyRequest, IssuedKey, Scope } from './api-key.models';

/**
 * Typed wrapper over a client's keys and the scopes a key can hold. Stateless: the issued key is
 * returned to the caller and not kept here, so it lives exactly as long as the reveal dialog.
 */
@Injectable({ providedIn: 'root' })
export class ApiKeysApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${inject(API_BASE_URL)}/api/admin`;

  /** Every key of the client, newest first. */
  list(clientId: string): Observable<readonly ApiKey[]> {
    return this.http.get<ListEnvelope<ApiKey>>(this.keysUrl(clientId)).pipe(map((envelope) => envelope.items));
  }

  issue(clientId: string, request: IssueKeyRequest): Observable<IssuedKey> {
    return this.http.post<IssuedKey>(this.keysUrl(clientId), request);
  }

  /** Answers 204. */
  revoke(clientId: string, keyId: string): Observable<null> {
    return this.http.delete<null>(`${this.keysUrl(clientId)}/${encodeURIComponent(keyId)}`);
  }

  scopes(): Observable<readonly Scope[]> {
    return this.http.get<ListEnvelope<Scope>>(`${this.baseUrl}/scopes`).pipe(map((envelope) => envelope.items));
  }

  private keysUrl(clientId: string): string {
    return `${this.baseUrl}/clients/${encodeURIComponent(clientId)}/keys`;
  }
}
