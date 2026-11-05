/** Mirrors the client record of `/api/admin/clients` (specs/002-clients-and-api-keys/contracts/admin-api.md). */
export interface Client {
  readonly id: string;
  readonly name: string;
  readonly status: 'Active' | 'Disabled';
  readonly createdAt: string;
  readonly disabledAt: string | null;
}

/** The stud list envelope, without paging until the shared paging lands. */
export interface ListEnvelope<T> {
  readonly items: readonly T[];
}

/** The rule the API enforces on client ids (002 FR-005), mirrored so forms can say it as you type. */
export const CLIENT_ID_PATTERN = /^[a-z][a-z0-9-]{0,39}$/;

export const CLIENT_ID_RULE = 'Lowercase letters, digits and hyphens, starting with a letter, up to 40 characters.';
