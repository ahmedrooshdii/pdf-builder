# Contract: Authenticating to the Service

What a product developer integrates against. Everything not listed here is unchanged (spec FR-022).

## Sending a key

```http
POST /api/pdf/jobs HTTP/1.1
X-Api-Key: pdf_k7f2a9c1_Q8vLx2Tn9WcZr4YpB6sK1dHf7MjG3aE0uVnR5tXo
Content-Type: application/json

{ "templateKey": "invoice", "data": { … } }
```

One header, `X-Api-Key`, on every request to a protected endpoint. The response to an accepted
request is byte-identical to what it was before this feature.

## Which endpoints need what

| Endpoint | Needs |
| --- | --- |
| `POST /api/pdf/jobs`, `GET /api/pdf/jobs/{jobId}` | `pdf:generate` |
| `GET /api/templates`, `GET /api/templates/{key}` | `pdf:templates:read` |
| `POST /api/templates`, `POST /api/templates/{key}/duplicate`, `PUT /api/templates/{key}/draft`, `POST /api/templates/{key}/publish`, `DELETE /api/templates/{key}`, `POST /api/templates/preview` | `pdf:templates:write` |
| `/api/admin/…` (see [admin-api.md](admin-api.md)) | the admin key |
| `/health/live`, `/health/ready` | nothing, unchanged |
| `GET /files/{fileId}` | nothing, unchanged until the private-links feature |
| `/openapi/v1.json`, `/scalar/v1` | nothing; served outside production only, as today |

The admin key satisfies every row. Permissions are independent: `pdf:templates:write` does not
include `pdf:templates:read`.

## Refusals

All are `application/problem+json` with a fixed `title` and `detail`, a stable `code` and a
`traceId`. None echoes the key, its public identifier, or anything else that was sent.

| Status | `code` | When | Also |
| --- | --- | --- | --- |
| 401 | `AUTH_REQUIRED` | No `X-Api-Key` header | `WWW-Authenticate: ApiKey` |
| 401 | `AUTH_INVALID_KEY` | Unknown, malformed, revoked or expired key, a key of another service, or the header sent twice. **Identical in every one of these cases.** | `WWW-Authenticate: ApiKey` |
| 403 | `CLIENT_DISABLED` | A valid key whose client is disabled | |
| 403 | `AUTH_SCOPE_MISSING` | A usable key without the permission the endpoint needs; a client key on `/api/admin` | |
| 503 | `STORAGE_UNAVAILABLE` | The key cannot be checked because the database is unreachable | `Retry-After: 5` |

Example:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
  "title": "Invalid API key",
  "status": 401,
  "detail": "The API key is not valid. Check that it was copied whole and has not been revoked or expired.",
  "code": "AUTH_INVALID_KEY",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

A refused request has done nothing: no job is recorded, no message is published, no template is read
or changed.

## Timing guarantees

- A revoked key, an expired key and a disabled client's keys are refused within **60 seconds** on every
  instance; a key issued or a client enabled is usable within the same bound.
- A key is shown in full **once**, in the response that issues it.

## Messages on the bus

Unchanged. `IGeneratePdfRequest` and `IGeneratePdfResponse` carry no key, and publishers are trusted
through their broker credentials, as before.
