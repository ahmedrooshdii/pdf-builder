# Contract: Console Session API

Mapped by `MapBlocksConsole()` from `Blocks.ServiceDefaults`, so every block's console signs in the
same way. All errors are RFC 9457 problem details with `code` and `traceId`, as in
[002 authentication.md](../../002-clients-and-api-keys/contracts/authentication.md#refusals).
Every response here carries `Cache-Control: no-store`.

## `POST /api/console/session`: sign in

Anonymous. Requires `X-Console: 1`.

```http
POST /api/console/session
Content-Type: application/json
X-Console: 1

{ "adminKey": "adm_…" }
```

Checked in this order:

| # | Check | Response |
| --- | --- | --- |
| 1 | `X-Console: 1` missing | `403 CONSOLE_HEADER_REQUIRED` |
| 2 | ≥ 5 failures from this address in the current minute | `429 RATE_LIMITED`, `Retry-After: <seconds>`; body not read |
| 3 | `adminKey` missing, empty, over 64 characters, not the admin key (including any client key) | `401 AUTH_INVALID_KEY` (002 wording); counts as a failure |
| 4 | Storage unreachable while writing the session | `503 STORAGE_UNAVAILABLE`, `Retry-After: 5` |
| — | Otherwise | `204`, `Set-Cookie: blocks.pdf.console=…; Expires=…; Path=/; Secure; HttpOnly; SameSite=Strict` |

A request that already carries `X-Api-Key` is answered as above using only the body; the header
plays no part in signing in.

## `GET /api/console/session`: who am I

Requires a console session (the `ConsoleSession` scheme only; an `X-Api-Key` admin key is not a
session).

```json
200
{ "expiresAt": "2026-11-04T21:04:00+00:00" }
```

| Response | When |
| --- | --- |
| `200` | Valid session |
| `401 AUTH_REQUIRED` | No cookie, or the session is expired, signed out or from a replaced admin key; the cookie is cleared |
| `503 STORAGE_UNAVAILABLE` | The session could not be checked |

## `DELETE /api/console/session`: sign out

Anonymous (signing out a dead session must still clear the cookie). Requires `X-Console: 1`.

| Response | When |
| --- | --- |
| `204`, cookie cleared | Always, when the header is present; if the cookie names a live session, its row is revoked first |
| `403 CONSOLE_HEADER_REQUIRED` | Header missing |
| `503 STORAGE_UNAVAILABLE` | A live session could not be revoked; the cookie is **not** cleared, so the operator can retry |

After `204`, presenting a copy of the old cookie answers `401 AUTH_REQUIRED` on every instance.

## Using a session on other endpoints

- A valid session authenticates any endpoint the admin key does, with the same authority, including
  `/api/admin/…`, `/api/pdf/…` and `/api/templates/…`.
- `X-Api-Key`, when present, wins: the cookie is ignored for that request.
- A session-authenticated request with a method other than `GET`, `HEAD` or `OPTIONS` must carry
  `X-Console: 1`, or it is refused `403 CONSOLE_HEADER_REQUIRED` before any work is done.
- An expired, signed-out or orphaned session answers `401 AUTH_REQUIRED` and clears the cookie.
- A request with neither a key nor the cookie is refused exactly as in 002
  (`401 AUTH_REQUIRED`, `WWW-Authenticate: ApiKey`).

## Acting as a client: `X-Client-Id`

Optional request header, honoured only for the admin key and console sessions.

| Caller | Header | Effect |
| --- | --- | --- |
| Admin key or session | absent | Acts as no client (as in 002) |
| Admin key or session | one value matching `^[a-z][a-z0-9-]{0,39}$` | Acts as that client: it is `ICurrentClient.ClientId` and appears as `ClientId` in the log scope (with `Admin = true`) |
| Admin key or session | malformed, or sent twice | `400 VALIDATION_FAILED`, `errors["X-Client-Id"]` |
| Client key | anything | Ignored; the key's own client applies |

Whether the client exists is not checked in this release; client-scoped data (feature 003) answers
`404` for an unknown one.

## `GET /api/admin/scopes`: the permissions a key can have

Admin (key or session).

```json
200
{
  "items": [
    { "name": "pdf:generate", "description": "Submit jobs and read them." },
    { "name": "pdf:templates:read", "description": "List and read templates." },
    { "name": "pdf:templates:write", "description": "Create, duplicate, edit, publish, delete and preview templates." }
  ]
}
```

Order is the block's declaration order. `admin` is never listed.

## New codes

| Code | Status | Meaning |
| --- | --- | --- |
| `CONSOLE_HEADER_REQUIRED` | 403 | A cookie-authenticated change, or sign-in/sign-out, without `X-Console: 1` |
| `RATE_LIMITED` | 429 | Too many failed sign-ins from this address; `Retry-After` says when to try again. The stud code that per-client limits (008) will also use. |

Both are additions; no existing code changes meaning (Principle VII). The message contracts and the
`PdfBuilder.Contracts` package are unchanged.

## Logs

- Sign-in success: Information, `Console session started` with the session's expiry. Failure:
  Debug, like refused keys, with the reason class (`Invalid`, `Throttled`) and never the value sent.
- Sign-out: Information, `Console session ended`.
- Requests under a session carry `Admin = true` (and `ClientId` when acting as a client), the same
  scope as the admin key.
