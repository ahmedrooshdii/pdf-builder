/** Mirrors a key as `/api/admin/clients/{id}/keys` lists it (002 contracts/admin-api.md). Never the key itself. */
export interface ApiKey {
  readonly keyId: string;
  /** `pdf_k7f2a9c1_…`: the public part, safe to show. */
  readonly display: string;
  readonly name: string;
  readonly scopes: readonly string[];
  readonly status: 'Active' | 'Expired' | 'Revoked';
  readonly createdAt: string;
  readonly expiresAt: string | null;
  readonly revokedAt: string | null;
  readonly lastUsedAt: string | null;
}

/** The one response that carries the full key. Shown once, then dropped. */
export interface IssuedKey extends ApiKey {
  readonly key: string;
}

/** A permission a key can hold, as `GET /api/admin/scopes` describes it. */
export interface Scope {
  readonly name: string;
  readonly description: string;
}

export interface IssueKeyRequest {
  readonly name: string;
  readonly scopes: readonly string[];
  /** ISO 8601 with offset, or null for a key that works until revoked. */
  readonly expiresAt: string | null;
}

export const KEY_NAME_MAX_LENGTH = 100;
