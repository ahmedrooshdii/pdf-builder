# 4. Backend

Two parts. **The studs** are the contract every block's API implements; they live in
`Blocks.ServiceDefaults` so a new block gets them by referencing a package. **PDF Builder's
changes** are what this block needs on top of them.

Everything here is .NET 10, ASP.NET Core minimal APIs, EF Core with Npgsql, and MassTransit 8.5.x
(pinned on purpose, see [../architecture.md](../architecture.md#masstransit-is-pinned-to-85x-on-purpose)).

---

## The studs

### Authentication

Three ways in, one result: a `ClaimsPrincipal` with `client_id` (absent for the admin) and `scope`
claims. Nothing after authentication knows which way was used.

| Scheme | Who | Credential | Where checked |
| --- | --- | --- | --- |
| `ApiKey` | A product's backend | `X-Api-Key: pdf_k7f2a9c1_<secret>` | `api_keys` table, cached |
| `AdminKey` | The operator, from scripts | `X-Api-Key: adm_<secret>` | `Blocks:Admin:KeyHash` in configuration |
| `ConsoleSession` | The operator, in the console | `blocks.console` cookie | ASP.NET cookie auth; keys in `data_protection_keys` |

A policy scheme (`Blocks`) forwards to the right one: a key starting `adm_` goes to `AdminKey`, any
other `X-Api-Key` to `ApiKey`, otherwise `ConsoleSession`.

#### API key format

```
pdf_k7f2a9c1_Q8vLx2Tn9WcZr4YpB6sK1dHf7MjG3aE0uVnR5tXo
└┬┘ └──┬───┘ └──────────────────┬───────────────────┘
 │     │                        └ secret: 40 chars base62 (~238 bits), from RandomNumberGenerator
 │     └ key id: 8 chars [a-z0-9], public, unique, used to find the row
 └ block code: pdf, eml, ntf … so a leaked key says where it came from
```

- Stored: `key_id` and `SHA-256(secret)`. A fast hash is right here because the secret is long and
  random; slow hashes protect low-entropy passwords, which this is not.
- Checked: look up `key_id`, compare hashes with `CryptographicOperations.FixedTimeEquals`, then
  check `revoked_at`, `expires_at` and the client's `disabled_at`.
- Cached for 60 seconds per `key_id`, so a revocation takes effect within a minute without a
  database read on every request.
- `last_used_at` is written at most once a minute per key, off the request path.
- The block code prefix is also what GitHub secret scanning can be taught to recognise.

`Blocks.Cli` generates keys and admin-key hashes:

```bash
dotnet blocks admin-key                              # prints a new adm_ key and the hash for configuration
dotnet blocks client add ecommerce --name "E-commerce" --block pdf
dotnet blocks key create ecommerce --block pdf --name production --scopes pdf:generate
```

The CLI talks to the block's admin API with the admin key; it never touches a database directly.

**Built so far** (features 002 and 004, `specs/002-clients-and-api-keys`,
`specs/004-console-sign-in-keys`): the `Blocks` policy scheme is the default and forwards to `ApiKey`
when `X-Api-Key` is present or the console cookie is absent, otherwise to `ConsoleSession`. There is
**no separate `AdminKey` scheme**: the one key handler tells admin and client keys apart by prefix,
which is all a second scheme would do. `ConsoleSession` is ASP.NET cookie authentication validated on
every request against a `console_sessions` row (below); its principal has the admin key's claims.
The code lives in `src/Blocks.ServiceDefaults` (handlers, permission policies with a fallback policy
that closes every endpoint not marked anonymous, `/api/admin/clients…`, `/api/admin/scopes`,
`/api/console/session`, the `X-Console` and `X-Client-Id` middleware, `ICurrentClient`, the
`ClientId`/`KeyId` log scope; `UseBlocksAuthentication()` adds them in order) and
`src/Blocks.ServiceDefaults.EntityFramework` (`clients`, `api_keys`, `console_sessions`,
`data_protection_keys`, the stores, and a Data Protection key repository written there rather than
taken from `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, whose package brings a second
copy of Data Protection and its cryptography packages beside the shared framework's), referenced by
PDF Builder until feature 011 moves them to `blocks-platform`. `tools/Blocks.Cli` has only
`admin-key`; client and key commands wait for 011, and until then the management API is called
directly (`samples/admin.http`) or from the console. The admin lists return the list envelope with
`items` only (the console shows them whole; shared paging now arrives with the server-side jobs
list, 010), and the admin `POST`s take no `Idempotency-Key`, until those studs exist.

#### Scopes

| Scope | Allows |
| --- | --- |
| `pdf:generate` | Submit jobs, read own jobs, get file URLs |
| `pdf:templates:read` | List and read own templates |
| `pdf:templates:write` | Create, edit, publish and delete own templates |
| `admin` | Everything, for any client. Only the admin key and console sessions have it. |

Policies are named after scopes: `.RequireAuthorization("pdf:generate")`.

#### Acting as a client

The admin has no `client_id`. On client-scoped endpoints an admin sends `X-Client-Id: ecommerce`,
which is honoured **only** for principals with the `admin` scope and ignored for everyone else. This
is how the console works inside the selected client using the same endpoints products use.
Built in 004: `ICurrentClient.ClientId` is that value for the admin; a malformed one (not a client
id, or sent twice) from the admin is `400 VALIDATION_FAILED`.
Built in 003: `.RequireActingClient()` on a group of endpoints (the PDF block's `/api/templates` and
`/api/pdf/jobs`) refuses the admin with no client `400 VALIDATION_FAILED` (`errors["X-Client-Id"]`),
and an unregistered one `404 CLIENT_NOT_FOUND`. A disabled client is allowed: the operator can still
inspect and prepare its data. A caller with a key is that key's client and costs no lookup. A storage
outage while checking is `503`, never "not found".

#### `ICurrentClient`

```csharp
public interface ICurrentClient
{
    string? ClientId { get; }          // null only for an admin not acting as a client
    IReadOnlySet<string> Scopes { get; }
    bool IsAdmin { get; }
}
```

The HTTP implementation reads claims; the bus implementation is set from the message (see
[Client scoping](#client-scoping)). Application code depends on this interface and nothing else.

#### Console session

```
POST   /api/console/session   { "adminKey": "adm_…" }   → 204 + Set-Cookie
GET    /api/console/session                              → 200 { "expiresAt": … } or 401
DELETE /api/console/session                              → 204, cookie cleared
```

- Cookie `blocks.{blockCode}.console`: `HttpOnly`, `Secure`, `SameSite=Strict`, 12-hour absolute
  lifetime (`Blocks:Console:SessionLifetime`), no sliding.
- Each sign-in writes a `console_sessions` row keyed by `SHA-256(sid)`, where `sid` is a 256-bit
  random id inside the protected cookie, with the expiry and a fingerprint of the configured admin
  key hash. Every cookie request checks the row (no cache), so sign-out (`revoked_at`) and a replaced
  admin key end a session on every instance at once, and a database reader cannot forge a cookie that
  matches a live row. The cookie's key ring is in `data_protection_keys` (application name
  `blocks.{blockCode}`), so every instance opens it and it survives restarts.
- Cookie-authenticated requests that change state, and sign-in and sign-out, must carry
  `X-Console: 1`, or get `403 CONSOLE_HEADER_REQUIRED`. A cross-site form cannot set a custom header,
  so this, with `SameSite=Strict`, is the CSRF defence.
- Failed sign-ins are limited per address (5 per minute, per instance) with `429 RATE_LIMITED`; the
  address comes from `X-Forwarded-For` only from proxies listed in
  `Blocks:ForwardedHeaders:KnownNetworks`.
- `GET /api/admin/scopes` lists the permissions a key can have, with the description the block
  declares, so the console's create form cannot drift from the service.

### Problem details

Every error response is RFC 9457 `application/problem+json`, with two extensions that are always
present:

```json
{
  "type": "https://blocks.example.com/problems/template-not-found",
  "title": "Template not found",
  "status": 404,
  "detail": "No published template 'invoice-v2' exists.",
  "code": "TEMPLATE_NOT_FOUND",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

Validation failures add `errors` (field → messages) and use `code: "VALIDATION_FAILED"`; today's
400s lack the code, and gain it.

Stud codes, shared by every block:

| Status | Code | When |
| --- | --- | --- |
| 400 | `VALIDATION_FAILED` | The request is malformed. |
| 401 | `AUTH_REQUIRED` | No credential. Response carries `WWW-Authenticate: ApiKey`. |
| 401 | `AUTH_INVALID_KEY` | Unknown, revoked or expired key. Deliberately not more specific. |
| 403 | `AUTH_SCOPE_MISSING` | Valid key without the scope this endpoint needs. |
| 403 | `CLIENT_DISABLED` | The client is disabled. |
| 403 | `CONSOLE_HEADER_REQUIRED` | A cookie-authenticated change, or a console sign-in or sign-out, without `X-Console: 1`. |
| 404 | (block code or none) | Not found **or belongs to another client**. Never 403 for that. |
| 404 | `CLIENT_NOT_FOUND` | Management API: no client has this id. |
| 404 | `KEY_NOT_FOUND` | Management API: the client has no key with this id. |
| 409 | `CLIENT_ID_TAKEN` | Management API: client ids are permanent and never reused. |
| 422 | `IDEMPOTENCY_KEY_REUSED` | Same `Idempotency-Key`, different body. |
| 429 | `RATE_LIMITED` | Over the client's limit, or too many failed console sign-ins from one address. Carries `Retry-After`. |
| 503 | `STORAGE_UNAVAILABLE` | The block's database is unreachable, including while checking a key. Carries `Retry-After`. Never answered as `AUTH_INVALID_KEY`. |

A block's own codes (`TEMPLATE_NOT_FOUND`, `TEMPLATE_KEY_TAKEN`, …) sit alongside these. Codes are
public API: new ones may be added, existing ones never change meaning.

### Slow work

Anything that can take more than a second follows the shape PDF Builder already has:

```
POST /api/pdf/jobs                  → 202 Accepted, Location: /api/pdf/jobs/{id}
                                      { "jobId": "…", "statusUrl": "/api/pdf/jobs/…" }
GET  /api/pdf/jobs/{id}             → 200 { "status": "Pending" | "Succeeded" | "Failed", … }
```

plus a webhook when it finishes, and a response message for bus callers.

### Lists

Every list endpoint returns the same envelope and supports two ways of paging, because its two
audiences want different things:

```json
{ "items": [ … ], "nextCursor": "eyJ0Ijoi…", "page": 1, "pageSize": 50, "total": 1643 }
```

| | Cursor (products, default) | Page (console) |
| --- | --- | --- |
| Request | `?limit=50&cursor=…` | `?page=3&pageSize=50` |
| Response | `items`, `nextCursor` (null on the last page) | `items`, `page`, `pageSize`, `total` |
| Why | Stable while new rows arrive; cheap at any depth | The numbered Pagination bar needs a total and page jumps |

- `limit`/`pageSize`: default 50, maximum 200.
- `total` is counted exactly up to 10,000 and reported as `10000` with `"totalIsEstimate": true`
  beyond that, so a count never makes a list slow. The console then shows "10,000+".
- The cursor is opaque; clients pass it back unchanged.

### Idempotency

- Header `Idempotency-Key` (1–255 printable ASCII) on any `POST` that creates something.
- Scoped to the client, kept for 24 hours.
- Same key, same body: the original response is returned, with `Idempotent-Replayed: true`.
- Same key, different body: 422 `IDEMPOTENCY_KEY_REUSED`. The body is compared by SHA-256 of its
  canonical bytes.
- Storage is the block's: PDF keeps it on `pdf_jobs` with a unique index, so the check and the
  insert are one atomic statement. `Blocks.ServiceDefaults` supplies the header parsing, hashing and
  response replay; the block supplies the lookup.
- Bus messages are deduplicated by MassTransit's inbox on `MessageId`, and by `CorrelationId` for
  jobs.

### Webhooks

Follows the [Standard Webhooks](https://www.standardwebhooks.com/) specification, so products can
use its existing verification libraries.

```
POST https://shop.example.com/hooks/pdf
webhook-id:        msg_2f8c1a0e9b7d4c6a
webhook-timestamp: 1793611200
webhook-signature: v1,K5oZfzN95Z9UVu1EsfQmfVNQhnkZ2pj9o9NDN/H/pI4=
content-type:      application/json

{
  "type": "job.succeeded",
  "timestamp": "2026-11-02T10:00:00Z",
  "data": { "clientId": "ecommerce", "jobId": "…", "templateKey": "invoice",
            "fileUrl": "https://pdf.example.com/files/…?expires=…&sig=…", "sizeBytes": 64210 }
}
```

- Signature: `base64(HMAC-SHA256(secret, "{webhook-id}.{webhook-timestamp}.{body}"))`. Secrets are
  `whsec_` + 32 random bytes in base64, shown once, stored encrypted with Data Protection.
- Delivery: an event is written to `webhook_deliveries` in the same transaction as the state change
  that caused it (no lost events, no events for rolled-back changes). A background dispatcher claims
  due rows with `FOR UPDATE SKIP LOCKED` and posts them.
- Success is any 2xx within 10 seconds. Otherwise retry after 5 s, 5 min, 30 min, 2 h, 5 h, 10 h,
  then mark `failed`. `webhook-id` is the same on every attempt, so the product can deduplicate.
- The operator can redeliver any delivery from the console.
- Target URLs must be HTTPS and must not resolve to private, loopback or link-local addresses, so a
  webhook cannot be used to probe the VM's own network.

PDF events: `job.succeeded`, `job.failed`.

### Files

Generated files are private and links expire.

```
https://pdf.example.com/files/a7e0495b…?expires=1793614800&sig=3q2-7wE…
```

- `sig = base64url(HMAC-SHA256(Blocks:Files:SigningKey, "{fileId}.{expires}"))`, compared in
  constant time. Wrong, missing or expired signatures get a 404, not a 403: the URL simply does not
  work.
- Default lifetime 1 hour (`Blocks:Files:UrlLifetime`). Webhook payloads and responses get a fresh
  URL when they are produced. `GET /api/pdf/jobs/{id}/file-url` issues a new one for a job the
  caller owns.
- The console does not use signed URLs: it downloads through `GET /api/pdf/jobs/{id}/file`, which
  is authenticated like any other endpoint.
- `Blocks:Files:RequireSignature` (default `false` in the first release that has it, `true` in the next)
  decides which links are *issued* as well as which are accepted. Off: the plain `/files/{fileId}`, no
  expiry, as before; on: every link is signed, and unsigned links get the same 404. A signed link is
  always accepted until it expires, so the switch can be turned on and off again safely.
- Links are made when asked for, from the file id and `Storage:PublicBaseUrl`; none is stored (the
  `file_url` column is gone). `fileUrlExpiresAt` is null while signing is off.
- Files are deleted `Blocks:Files:RetentionDays` (default 30) after creation by a background
  sweeper that runs at start-up and every 15 minutes, so a file is gone within about an hour of its
  deadline at the latest. A succeeded job is kept as long as its file, then for `JobStore:RetentionHours`
  more, reporting `fileExpired: true`. A file found missing from storage is reported the same way.
- Signatures never reach a log: `Microsoft.AspNetCore.Hosting.Diagnostics` is pinned to `Warning`, and
  responses carry `Cache-Control: private, no-store`, `Referrer-Policy: no-referrer` and `X-Robots-Tag: noindex`.
- Storage stays behind `IFileStorage`: local disk now, object storage when a second replica exists.

### Rate limiting

ASP.NET Core rate limiter, partitioned by `client_id`:

- Requests: token bucket, `clients.rate_limit_per_minute` or the block default
  (`Blocks:RateLimits:RequestsPerMinute`, default 120).
- Pending work: a client with `max_pending_jobs` jobs already pending gets 429 on submit. This bounds
  how far one client can push others back in the queue without partitioning RabbitMQ.
- Both answer 429 `RATE_LIMITED` with `Retry-After`.

### Health and telemetry

- `/health/live` (process only) and `/health/ready` (database, broker, block-specific checks), as
  today. Never authenticated.
- OpenTelemetry traces and metrics; every log scope carries `ClientId` and, where there is one,
  `JobId`. Metrics are tagged with `client_id`, so per-product usage is visible from day one.
- Once an exporter exists, redact the URL query from every span, metric tag and log it writes: a signed file
  link carries its signature there (`sig`), and a recorded one is usable until it expires.

### Configuration

Studs use the `Blocks` section, so every block is configured the same way:

| Key | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings:Database` | – | Required. The block's own database. |
| `Blocks:Admin:KeyHash` | – | Required. Lowercase hex SHA-256 of the admin key, from `dotnet blocks admin-key` (today `dotnet run --project tools/Blocks.Cli -- admin-key`). |
| `Blocks:Console:SessionLifetime` | `12:00:00` | How long a console sign-in lasts, from sign-in, never extended. `00:05:00` to `24:00:00`. |
| `Blocks:ForwardedHeaders:KnownNetworks` | – | Comma-separated CIDRs of reverse proxies whose forwarded headers are believed (the console's sign-in limit is per address). Unset = loopback only. |
| `Blocks:Files:SigningKey` | – | Required. 32+ random bytes, base64. |
| `Blocks:Files:UrlLifetime` | `01:00:00` | |
| `Blocks:Files:RetentionDays` | `30` | |
| `Blocks:RateLimits:RequestsPerMinute` | `120` | Per client, overridable per client. |
| `Blocks:RateLimits:MaxPendingJobs` | `100` | Per client, overridable per client. |
| `Blocks:Bus:DefaultClientId` | – | Client assumed for bus messages that name none, and owner of templates and jobs from before clients existed. Must be a client id. Unset = such messages are refused, and a database that has such data does not start. Built in 003; see [configuration.md](../configuration.md#bus). |
| `Blocks:Database:MigrateOnStartup` | `true` | EF Core takes a migration lock, so concurrent starts are safe. |
| `Blocks:Branding:ProductName` | block's name | Shown by the console; see [06-deployment.md](06-deployment.md#what-a-deployment-configures). |
| `Blocks:Branding:LogoPath` | – | SVG or PNG on the data volume. Unset = the block's own logo. |

Every key has a default or fails start-up with a message naming it. Nothing deployment-specific
(domain, name, limit) is a constant in code.

### Versioning

URLs are unversioned (`/api/pdf/jobs`) and change additively. A breaking change ships as a new path
(`/api/v2/pdf/jobs`) beside the old one, never in place. Message contracts follow the same rule
through the `PdfBuilder.Contracts` package version.

---

## PDF Builder's changes

### Persistence

Built in feature 001 (`specs/001-persistent-storage`), before clients exist:

- `PdfBuilderDbContext` in `PdfBuilder.Infrastructure/Persistence/`. It does **not** derive from a
  stud `BlocksDbContext`. Feature 002 added the stud's first tables (`clients`, `api_keys`) as
  `modelBuilder.AddBlocksClients()` from `Blocks.ServiceDefaults.EntityFramework` instead, so a block
  keeps its own context; webhooks, Data Protection keys and the client query filter join the same
  way in their features.
- Records `PdfJobRecord`, `AuthoredTemplateRecord` and `TemplateImportRecord`, converted to and from
  the domain types, which stay free of EF. There is no `FileEntity`: generated files stay behind
  `IFileStorage` until feature 005. Template content is stored as `jsonb` using the property names the
  old files used.
- `EfPdfJobStore` and `EfAuthoredTemplateStore` replace the in-memory and file stores. The revision
  check is a conditional update on `revision`, so the process-wide lock the file store needed is gone;
  keys are unique ignoring case through a generated `key_lower` column.
- **Transactional outbox, bus side only.** The endpoint publishes, then saves; MassTransit's EF Core
  bus outbox (`AddEntityFrameworkOutbox<PdfBuilderDbContext>` with `UsePostgres()` and
  `UseBusOutbox()`, 8.5.11) commits the job row and the message together. The consumer outbox and
  inbox are deliberately not used: they change how retries and the final `RENDER_FAILED` transition
  interact, which this feature does not need.
- **One-time import**, under a PostgreSQL advisory lock, recorded in `template_imports`, so it runs once
  however many instances start together. Files are read, never changed. Since 003 the imported
  templates are owned by `Blocks:Bus:DefaultClientId`; see [Client scoping](#client-scoping).
- `JobStore:RetentionHours` keeps its meaning, default (24) and range (1–168), counted from the
  job's last save as before: reads apply the cut-off, and an hourly sweeper deletes the rows past
  it, so a job stops being pollable at the same moment it does today, but a restart no longer
  shortens that. Longer history is left to feature 010, which may raise the default when the console
  lists jobs (decided in `specs/001-persistent-storage`).

### Client scoping

Built in feature 003 (`specs/003-client-scoped-templates-jobs`).

- **The filter.** `PdfBuilderDbContext` takes `ICurrentClient` and gives `authored_templates` and
  `pdf_jobs` a named query filter, `"client"`: `ClientId != null && row.ClientId == CallerClientId`.
  `CallerClientId` is a member of the context, so EF turns it into a parameter each time a query runs:
  one cached model serves every client, and a context created before a bus consumer says which client
  it acts as still sees that client. With no client the condition is false, so nothing matches, never
  everything. Stores set the owner on insert from `ClientScope` (a filter does not apply to inserts);
  an update never changes it. `Application` is unchanged and names no client: "own template, else
  built-in", key uniqueness and the 404 for another client's key follow from the filtered store.
- **Cross-client reads** call `IgnoreQueryFilters(["client"])` only in `DeleteExpiredJobsOfAllClientsAsync`
  (the retention sweeper has no caller), `AssignUnownedToDefaultClientAsync` (the upgrade, in raw SQL)
  and `ClaimAsync`, and an architecture test lists them. A second test fails when an entity with a
  `ClientId` has no filter (Principle III's gate); `api_keys` is the one exception, read before any
  client is known.
- **Keys and ids.** Template keys are unique per client, ignoring case (primary key `(client_id, key)`,
  unique `(client_id, key_lower)`), and a built-in key is taken for everyone. A job id stays unique across
  clients: `ClaimAsync` records a `Pending` job for the caller or refuses an id another client has
  (`JobIdTakenException`, `VALIDATION_FAILED`), and nothing about that job is read or reported.
- **The upgrade is two migrations around a startup step**, not one. `AddClientOwner` adds a nullable
  `client_id`; the service gives every row without one to `Blocks:Bus:DefaultClientId` (registering that
  client if it is missing) under an advisory lock; `RequireClientOwner` makes it `NOT NULL`, ties it to
  `clients`, and changes the key. It happens once, and a database with no such rows needs no setting.
  Without the setting and with such rows the service does not start. There is no default value in the
  second migration on purpose: a row left without an owner makes it fail, and say so.
- **Acting as a client.** A caller's client is the key's, or for the operator `X-Client-Id`
  ([Acting as a client](#acting-as-a-client)). Work that is not an HTTP request says which client it is
  for with `IClientContext.ActAs`, which the bus consumer calls first; `ICurrentClient` then answers with
  that client, no permissions, not the operator.
- **Bus.** `IGeneratePdfRequest` gained `string? ClientId` (Contracts 1.2.0). The consumer resolves it:
  the message's, else `Blocks:Bus:DefaultClientId`; a missing, malformed, unregistered or disabled client
  is answered (`VALIDATION_FAILED`, or `CLIENT_DISABLED`) with nothing rendered and no job recorded. The
  endpoint publishes the caller's client too. A job published straight to the bus is recorded as `Pending`
  when work starts.
- **Files.** The file a job produces is owned through its job (`file_id`, `client_id`). No schema change:
  the link still works for whoever holds it until feature 005.
- **Trust on the bus** is the broker's: each product gets its own RabbitMQ user allowed to publish
  only to the PDF exchanges in the block's vhost. A publisher can claim any `ClientId`, so only
  trusted product backends get broker credentials. This is a known phase 1 limit, written into
  [../message-contracts.md](../message-contracts.md#clientid).

### Template packs and fonts

Per-deployment content that has to travel without code ([06-deployment.md](06-deployment.md#what-a-deployment-brings-as-data)):

- **Packs** are JSON: `{ "format": 1, "exportedAt", "source": { "block", "version" }, "templates":
  [ { "key", "draft", "published" } ] }`. Import runs every template through the same validation as
  the editor and is all-or-nothing in one transaction. A pack from a newer format version is refused
  with a clear message rather than half-imported.
- **Fonts:** the image adds a fontconfig file registering `/data/fonts`, so fonts dropped on the
  data volume are available to templates by family name. The browser picks them up when it is next
  recycled; the readiness check reports the font count so an operator can confirm.

### Contract changes (`PdfBuilder.Contracts`)

All additive, shipping as 1.2.0 with feature 003. 1.1.0 (page setup) is released without them, so
it does not wait on the multi-client work:

| Type | Member | Notes |
| --- | --- | --- |
| `IGeneratePdfRequest` | `string? ClientId` | See above. |
| `IGeneratePdfResponse` | `DateTimeOffset? FileUrlExpiresAt` | **Feature 005, shipping as 1.3.0**, not 1.2.0, which carries `ClientId` and the `CLIENT_DISABLED` code. Null while signing is off. |

`IGeneratePdfResponse.FileUrl` is currently documented as "not signed and does not expire". Turning
signing on changes that behaviour for existing bus consumers, so it ships behind
`Blocks:Files:RequireSignature` (default `false` for the first release that has it, `true` in the
next), with the switch announced in `CHANGELOG.md`.

### Endpoints

| Method | Path | Policy | Change |
| --- | --- | --- | --- |
| POST | `/api/pdf/jobs` | `pdf:generate` | + `Idempotency-Key`, client-scoped, outbox |
| GET | `/api/pdf/jobs` | `pdf:generate` | **New.** `?status&templateKey&from&to&limit&cursor` |
| GET | `/api/pdf/jobs/{id}` | `pdf:generate` | Client-scoped; + `fileExpired` |
| GET | `/api/pdf/jobs/{id}/file-url` | `pdf:generate` | **New (005).** Fresh link: `404`, `409 JOB_HAS_NO_FILE`, `410 FILE_EXPIRED` |
| GET | `/api/pdf/jobs/{id}/file` | `pdf:generate` | **New (005).** Authenticated download, for the console; same refusals |
| GET | `/files/{fileId}` | the link | Requires a valid `expires` and `sig` once signing is on; every refusal is a bare 404 |
| GET, POST, PUT, DELETE | `/api/templates/…` | `pdf:templates:read` / `write` | Client-scoped; otherwise unchanged |
| GET, POST, PATCH | `/api/admin/clients[/{id}]` | `admin` | **New** (stud) |
| GET, POST, DELETE | `/api/admin/clients/{id}/keys[/{keyId}]` | `admin` | **New** (stud). DELETE revokes. |
| GET, POST, PATCH, DELETE | `/api/admin/clients/{id}/webhooks[/{id}]` | `admin` | **New** (stud) |
| GET, POST | `/api/admin/clients/{id}/webhooks/{id}/deliveries[/{id}/redeliver]` | `admin` | **New** (stud) |
| POST, GET, DELETE | `/api/console/session` | anonymous / cookie | **New** (stud) |
| GET | `/api/console/info`, `/api/console/logo` | anonymous | **New** (stud). Product name, logo, version for the console. |
| GET | `/api/console/overview` | `admin` | **New** (stud). Attention items and per-client 24-hour activity. Blocks add their own items through an `IAttentionSource`. |
| GET | `/api/templates/export?keys=` | `pdf:templates:read` | **New.** Template pack download |
| POST | `/api/templates/import[?replace=true]` | `pdf:templates:write` | **New.** Template pack upload; refuses existing keys unless replacing |
| GET | `/health/live`, `/health/ready` | anonymous | + database check |

Scalar and OpenAPI stay off in production, as today; the OpenAPI document declares the `ApiKey`
security scheme so generated clients send the header.

### Client package

Two packages, so the next block's client reuses the plumbing:

- **`Blocks.Client`**: an `HttpMessageHandler` that adds `X-Api-Key`; problem details turned into
  `BlockApiException { Status, Code, TraceId }`; the standard resilience pipeline from
  `Microsoft.Extensions.Http.Resilience` (retries only on idempotent requests or ones carrying an
  `Idempotency-Key`); `WebhookVerifier` for Standard Webhooks signatures.
- **`PdfBuilder.Client`**:

```csharp
public interface IPdfBuilderClient
{
    Task<JobAccepted> EnqueueAsync(string templateKey, object data, PdfPageSetup? pageSetup = null,
                                   string? idempotencyKey = null, CancellationToken ct = default);
    Task<PdfJobStatus> GetJobAsync(Guid jobId, CancellationToken ct = default);

    /// Enqueue, then poll with back-off until the job finishes or the timeout passes.
    Task<PdfJobStatus> GenerateAsync(string templateKey, object data, string? idempotencyKey = null,
                                     TimeSpan? timeout = null, CancellationToken ct = default);

    Task<Uri> GetFileUrlAsync(Guid jobId, CancellationToken ct = default);
}

services.AddPdfBuilderClient(configuration.GetSection("Blocks:Pdf"));   // BaseUrl, ApiKey
```

If the caller passes no idempotency key, the client generates one per logical call, so its own
retries cannot create duplicates.

### Testing

| Layer | Tooling | Must cover |
| --- | --- | --- |
| Domain, Application | xUnit v3, NSubstitute (as today) | Rules, with `ICurrentClient` substituted |
| Infrastructure | Testcontainers for PostgreSQL | Stores, query filters, migrations apply cleanly from empty |
| API | `WebApplicationFactory` + Testcontainers | Each endpoint's happy path and main refusal; 401/403/404 per policy |
| Cross-client | One shared helper, used by every client-scoped endpoint test | Client B gets 404 for client A's template, job, file and file URL |
| Architecture | Plain reflection test | No `Blocks.*` assembly references a `PdfBuilder.*` assembly |
| Contracts | Existing `ContractsTests` + package validation | Additive only |

### Packages to add

In `Directory.Packages.props`, pinned like the rest:

- `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`,
  `Microsoft.EntityFrameworkCore.Design` (tools only)
- `MassTransit.EntityFrameworkCore` on the same 8.5.x as `MassTransit.RabbitMQ`
- `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`
- `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`
- `Microsoft.Extensions.Http.Resilience` (client package)
- `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`,
  `OpenTelemetry.Instrumentation.Http`, an exporter
- `Testcontainers.PostgreSql` (tests)
