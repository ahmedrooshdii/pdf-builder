# Contract: Management API

Mapped by `MapBlocksAdmin()` from `Blocks.ServiceDefaults`, so every block exposes the same
operations. Every endpoint needs the **admin key** (`X-Api-Key: adm_…`); a client key gets
`403 AUTH_SCOPE_MISSING`. Errors use the format in [authentication.md](authentication.md#refusals)
with the codes below.

## Clients

### `POST /api/admin/clients`

```json
{ "id": "ecommerce", "name": "E-commerce" }
```

| Response | When |
| --- | --- |
| `201` + client, `Location: /api/admin/clients/ecommerce` | Registered |
| `400 VALIDATION_FAILED` with `errors.id` / `errors.name` | Id not `^[a-z][a-z0-9-]{0,39}$`; name empty or over 100 characters |
| `409 CLIENT_ID_TAKEN` | The id exists (including a disabled client) |

### `GET /api/admin/clients`

`200` with `{ "items": [ client, … ] }`, ordered by id. This is the stud list envelope without the
paging members, because a deployment has tens of clients; `nextCursor`, `page`, `pageSize` and
`total` are added, additively, when the shared paging lands (plan.md Complexity Tracking).

### `GET /api/admin/clients/{clientId}`

`200` + client, or `404 CLIENT_NOT_FOUND`.

### `PATCH /api/admin/clients/{clientId}`

```json
{ "disabled": true }
```

Optional members: `name`, `disabled`. `200` + client. Disabling a disabled client, or enabling an
active one, succeeds and changes nothing. `404 CLIENT_NOT_FOUND`; `400 VALIDATION_FAILED`.

**Client**:

```json
{
  "id": "ecommerce",
  "name": "E-commerce",
  "status": "Active",
  "createdAt": "2026-11-04T09:00:00+00:00",
  "disabledAt": null
}
```

`status` is `Active` or `Disabled`.

## Keys

### `POST /api/admin/clients/{clientId}/keys`

```json
{ "name": "production", "scopes": ["pdf:generate"], "expiresAt": null }
```

| Response | When |
| --- | --- |
| `201` + issued key (below) | Issued |
| `400 VALIDATION_FAILED` | Name empty or over 100 characters; no scopes; an unknown scope (the message lists the known ones); `expiresAt` not in the future |
| `404 CLIENT_NOT_FOUND` | |

Issuing a key for a disabled client is allowed; it works once the client is enabled.

**Issued key**, the only response that ever contains `key`:

```json
{
  "key": "pdf_k7f2a9c1_Q8vLx2Tn9WcZr4YpB6sK1dHf7MjG3aE0uVnR5tXo",
  "keyId": "k7f2a9c1",
  "display": "pdf_k7f2a9c1_…",
  "name": "production",
  "scopes": ["pdf:generate"],
  "status": "Active",
  "createdAt": "2026-11-04T09:01:00+00:00",
  "expiresAt": null,
  "revokedAt": null,
  "lastUsedAt": null
}
```

The response carries `Cache-Control: no-store`.

### `GET /api/admin/clients/{clientId}/keys`

`200` with `{ "items": [ key, … ] }`: every key of the client, newest first, in the shape above
**without** `key`. `status` is
`Active`, `Expired` or `Revoked`. `404 CLIENT_NOT_FOUND`.

### `DELETE /api/admin/clients/{clientId}/keys/{keyId}`

Revokes. `204`. Revoking a revoked key succeeds and changes nothing. `404 KEY_NOT_FOUND` when the
client has no key with that id.

## Storage outages

While the database is unreachable every endpoint here answers `503 STORAGE_UNAVAILABLE` with
`Retry-After: 5`. The admin key is still recognised; the operation itself needs storage.

## Not yet: `Idempotency-Key`

The two creating `POST`s do not take `Idempotency-Key` until the shared idempotency stud exists
(plan.md Complexity Tracking). Registering a client twice already answers `409 CLIENT_ID_TAKEN`;
issuing a key twice yields two keys, and the unwanted one is revoked.

## Codes

| Code | Status |
| --- | --- |
| `VALIDATION_FAILED` | 400 |
| `CLIENT_NOT_FOUND` | 404 |
| `KEY_NOT_FOUND` | 404 |
| `CLIENT_ID_TAKEN` | 409 |
