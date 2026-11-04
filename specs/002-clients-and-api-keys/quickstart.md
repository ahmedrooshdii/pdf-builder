# Quickstart: Validating Clients and API Keys

Each scenario maps to a user story in [spec.md](spec.md). Endpoints and codes are in
[contracts/](contracts/).

## Prerequisites

- PostgreSQL and the service set up as in feature 001.
- An admin key:

  ```bash
  dotnet run --project tools/Blocks.Cli -- admin-key
  dotnet user-secrets --project src/PdfBuilder.Api set "Blocks:Admin:KeyHash" "<printed hash>"
  export ADMIN="adm_…"          # the printed key
  ```

- Run the API **as Production** when you only want to look at it: in Development it loads your
  user-secrets, which may point at a real broker, and it would join the real queue.

  ```bash
  ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://localhost:5052 \
    dotnet run --project src/PdfBuilder.Api --no-launch-profile
  ```

`samples/admin.http` has every call below, ready to send.

## Automated checks

```bash
dotnet test                                    # no database: key format, handler, validation, architecture
PDFBUILDER_RUN_DATABASE_TESTS=1 dotnet test    # adds the store and whole-pipeline tests (Docker, or PDFBUILDER_TEST_POSTGRES)
```

## Scenario 1: register a client and issue a key (Story 1)

```bash
curl -s -X POST localhost:5052/api/admin/clients -H "X-Api-Key: $ADMIN" \
  -H "Content-Type: application/json" -d '{"id":"ecommerce","name":"E-commerce"}'           # 201
curl -s -X POST localhost:5052/api/admin/clients/ecommerce/keys -H "X-Api-Key: $ADMIN" \
  -H "Content-Type: application/json" -d '{"name":"production","scopes":["pdf:generate"]}' # 201, contains "key"
export KEY="pdf_…"                                                                           # copy it now
curl -s localhost:5052/api/admin/clients/ecommerce/keys -H "X-Api-Key: $ADMIN"              # 200, no "key" anywhere
```

Registering `ecommerce` again → `409 CLIENT_ID_TAKEN`; id `E-Commerce` → `400 VALIDATION_FAILED`.

## Scenario 2: a product calls with its key (Story 2)

```bash
curl -s -X POST localhost:5052/api/pdf/jobs -H "X-Api-Key: $KEY" \
  -H "Content-Type: application/json" -d "{\"templateKey\":\"invoice\",\"data\":$(cat templates/invoice.sample.json)}"
```

`202` with the same body as before this feature. The API log lines for the request carry
`ClientId=ecommerce` and the key id.

## Scenario 3: everyone else is refused alike (Story 3)

| Call | Expected |
| --- | --- |
| `POST /api/pdf/jobs` with no header | `401 AUTH_REQUIRED`, `WWW-Authenticate: ApiKey` |
| …with `X-Api-Key: pdf_zzzzzzzz_notarealsecret0000000000000000000000` | `401 AUTH_INVALID_KEY` |
| …with `X-Api-Key: banana` | `401 AUTH_INVALID_KEY`, identical body apart from `traceId` |
| `GET /health/live`, `GET /health/ready` with no header | as before |

No job is recorded and nothing is published for any refused call.

## Scenario 4: revoke, disable, expire (Story 4)

1. `DELETE /api/admin/clients/ecommerce/keys/<keyId>` → `204`. Within 60 s, calls with `$KEY` →
   `401 AUTH_INVALID_KEY`.
2. With a second key: `PATCH /api/admin/clients/ecommerce {"disabled":true}` → within 60 s,
   `403 CLIENT_DISABLED`; `{"disabled":false}` → it works again within 60 s.
3. Issue a key with `"expiresAt"` two minutes ahead; after it passes, `401 AUTH_INVALID_KEY` and the
   list shows `"status":"Expired"`.

## Scenario 5: permissions (Story 5)

A `pdf:generate` key on `PUT /api/templates/invoice/draft` → `403 AUTH_SCOPE_MISSING`, template
unchanged. A `pdf:templates:read` key on `GET /api/templates` → `200`; on `POST /api/pdf/jobs` →
`403 AUTH_SCOPE_MISSING`. Any client key on `/api/admin/clients` → `403 AUTH_SCOPE_MISSING`.

## Scenario 6: last use (Story 6)

Call once with a key, wait a few seconds, list the keys: its `lastUsedAt` is within a minute of the
call; an unused key shows `null`.

## Scenario 7: database down

Stop PostgreSQL. A call with a client key → `503 STORAGE_UNAVAILABLE` with `Retry-After: 5`, never
`401`. A call with the admin key to `/api/admin/clients` → also `503` (the operation needs storage).
Start PostgreSQL: calls succeed again without restarting the service.

## Scenario 8: missing admin key

Remove `Blocks:Admin:KeyHash` and start the service: it stops at once with an error naming
`Blocks:Admin:KeyHash`.
