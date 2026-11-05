# Contract: Client Scoping over HTTP

No paths, request bodies or response bodies change. What changes is **which rows** each endpoint
sees, and two refusals for the operator.

## Endpoints affected

Every endpoint in these groups gets `.RequireActingClient()` and sees only the caller's data:

| Method | Path | Permission |
| --- | --- | --- |
| GET | `/api/templates` | `pdf:templates:read` |
| GET | `/api/templates/{templateKey}` | `pdf:templates:read` |
| POST | `/api/templates` | `pdf:templates:write` |
| POST | `/api/templates/{templateKey}/duplicate` | `pdf:templates:write` |
| PUT | `/api/templates/{templateKey}/draft` | `pdf:templates:write` |
| POST | `/api/templates/{templateKey}/publish` | `pdf:templates:write` |
| DELETE | `/api/templates/{templateKey}` | `pdf:templates:write` |
| POST | `/api/templates/preview` | `pdf:templates:write` |
| POST | `/api/pdf/jobs` | `pdf:generate` |
| GET | `/api/pdf/jobs/{jobId}` | `pdf:generate` |

Not affected: `/api/admin/*`, `/api/console/*`, `GET /files/{fileId}` (anonymous until feature 005),
`/health/*`.

## Who "the caller" is

| Caller | Client used |
| --- | --- |
| API key | the key's client (002) |
| Admin key or console session, with `X-Client-Id: <id>` | `<id>` (004) |
| Admin key or console session, no `X-Client-Id` | none: refused |

## Refusals (operator only)

Checked after authentication and permission, before the handler:

| Condition | Status | `code` | Detail |
| --- | --- | --- | --- |
| No client | 400 | `VALIDATION_FAILED` | `errors["X-Client-Id"]`: "Choose a client: send X-Client-Id with the client to act as." |
| `X-Client-Id` malformed or repeated | 400 | `VALIDATION_FAILED` | unchanged from 004 (`ActingClientMiddleware`) |
| Client not registered | 404 | `CLIENT_NOT_FOUND` | as on `/api/admin/clients/{id}` |
| Client disabled | – | – | allowed |

A key-authenticated caller never hits these: 002 refuses unknown keys and disabled clients' keys
first.

## Another client's data

| Request by client B | Answer |
| --- | --- |
| Any template operation on a key only A has | exactly the answer for a key that exists nowhere (`404 TEMPLATE_NOT_FOUND`) |
| `POST /api/templates` with A's key | `201`, a new template owned by B |
| `POST /api/templates` with B's own key in another case, or a built-in key | `409`, as today |
| `POST /api/pdf/jobs` for a key only A has published | as for an unknown template, unless a built-in template has the key, which is then rendered |
| `GET /api/pdf/jobs/{A's job id}` | exactly the answer for an id that never existed (`404`) |

"Exactly" means status, headers and body are equal once `traceId` is removed (SC-001).

## Message published by `POST /api/pdf/jobs`

The published `IGeneratePdfRequest` now carries `ClientId` = the caller's client, so the worker
renders with the same client's templates and records the job under it.
