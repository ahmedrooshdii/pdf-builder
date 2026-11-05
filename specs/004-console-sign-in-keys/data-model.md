# Data Model: Console Sign-in and Key Management

Two new tables in the block's database, one migration (`AddConsoleSessions`), and two changes to
browser-side state. `clients` and `api_keys` (feature 002) are read and changed through the existing
management operations; their schema does not change.

## Table `console_sessions`

Added to the block's `DbContext` by `modelBuilder.AddBlocksConsoleSessions()` from
`Blocks.ServiceDefaults.EntityFramework`, with every name spelled out like `AddBlocksClients()`.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `id_hash` | `bytea` | no | PK `pk_console_sessions`. `SHA-256(sid)`, 32 bytes (check `ck_console_sessions_id_hash_length`). The `sid` itself is only inside the protected cookie. |
| `created_at` | `timestamptz` | no | Sign-in time. |
| `expires_at` | `timestamptz` | no | `created_at + Blocks:Console:SessionLifetime`. Never moved. Index `ix_console_sessions_expires_at` for clean-up. |
| `revoked_at` | `timestamptz` | yes | Set by sign-out. Never cleared. |
| `admin_key_fingerprint` | `bytea` | no | First 16 bytes of `SHA-256(Blocks:Admin:KeyHash)` at sign-in (check: length 16). |

No `client_id`: a session belongs to the operator, not to a client, and holds no client data
(Principle III's rule is for tables holding client data).

**Valid** at time *t* with configured fingerprint *f* when the row exists, `revoked_at` is null,
`expires_at > t`, and `admin_key_fingerprint = f` (constant-time comparison).

**State transitions**

```
            sign-in
  (none) ───────────▶ Active ──── sign-out ────▶ Revoked
                        │
                        ├── t ≥ expires_at ───▶ Expired     (derived, no write)
                        └── admin key replaced ▶ Orphaned    (derived, no write)
```

Revoked, Expired and Orphaned are all refused identically (`401 AUTH_REQUIRED`). None of them can
return to Active. Rows with `expires_at` more than a day in the past are deleted during sign-in.

## Table `data_protection_keys`

The ASP.NET Data Protection key ring, through `EfXmlRepository<PdfBuilderDbContext>` (research R2).

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `id` | `integer` identity | no | PK `pk_data_protection_keys`. |
| `friendly_name` | `text` | yes | Set by Data Protection. |
| `xml` | `text` | yes | The key element. Unencrypted at rest; see plan.md Complexity Tracking. |

Written and read only by Data Protection. Application name `blocks.pdf`, so every instance of this
block shares the ring and no other block's ring can open its cookies.

## The session cookie

Not stored by the service; described here because its contents are part of the design.

| | |
| --- | --- |
| Name | `blocks.pdf.console` (`blocks.{blockCode}.console`) |
| Attributes | `HttpOnly; Secure; SameSite=Strict; Path=/; Expires=<expires_at>` |
| Payload | Data-Protection-encrypted ticket: claims `scope=admin`, `sid`; `ExpiresUtc = expires_at`; `IsPersistent = true`; no sliding. |

The principal it produces has the same claims as the admin key's (scope `admin`, no `client_id`,
no `key_id`), so `ICurrentClient` reports `IsAdmin = true` and nothing below authentication can tell
the two apart (Principle VI).

## Acting client

Not stored. For an admin principal, `ICurrentClient.ClientId` is the request's `X-Client-Id` when
present and valid (`^[a-z][a-z0-9-]{0,39}$`, one value); otherwise null. For a client key it is
always the key's client and the header is ignored.

## Permission descriptor

`BlocksPermission(Name, Description)`, declared by the block in code and served by
`GET /api/admin/scopes`. PDF Builder's:

| Name | Description |
| --- | --- |
| `pdf:generate` | Submit jobs and read them. |
| `pdf:templates:read` | List and read templates. |
| `pdf:templates:write` | Create, duplicate, edit, publish, delete and preview templates. |

## Browser state

| Key / place | Holds | Lifetime | Never holds |
| --- | --- | --- | --- |
| URL `?client=` | Chosen client id | The address | — |
| `localStorage["pdf-builder.client"]` | Last chosen client id, as the default | Until changed | — |
| `localStorage["pdf-builder.job-history.v1"]` | Jobs this browser submitted, each now with `clientId` | 50 newest | Keys |
| `SessionService` signals | `state`, `expiresAt` | Page lifetime | The admin key, the cookie |
| Reveal dialog signal | The new key, until "I have copied it" | Seconds | — (cleared to `null`) |

The admin key and every client key are never written to storage, the URL, the console's logs or its
error reports (FR-003, FR-023, SC-005).

### `PdfJob` (console model) change

```ts
interface PdfJob {
  // … existing members unchanged
  /** The client the job was submitted for. Absent on entries saved before console sign-in. */
  readonly clientId?: string;
}
```

## Validation rules carried from the spec

| Rule | Where enforced |
| --- | --- |
| Admin key is the only credential accepted at sign-in | `POST /api/console/session` (server) |
| ≤ 5 failed sign-ins per minute per address | `SignInThrottle` (server, per instance) |
| Session lifetime `00:05:00`–`24:00:00`, default `12:00:00` | Options validation at start-up |
| Client id `^[a-z][a-z0-9-]{0,39}$` | Add-client form (mirrors) and the API (authoritative, 002) |
| Key name 1–100 characters; ≥ 1 known scope; expiry in the future | Create-key form (mirrors) and the API (authoritative, 002) |
| `returnUrl` starts with `/`, not `//` or `/\` | Sign-in page |
