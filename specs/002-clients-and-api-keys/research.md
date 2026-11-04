# Research: Clients and API Keys

Decisions checked against the code on `master` after feature 001 (2026-11-04) and nuget.org.

## R1. Where the code lives: the first two shared packages

- **Decision**: create `src/Blocks.ServiceDefaults` (authentication, authorization, the
  management endpoints, `ICurrentClient`, problem codes) and
  `src/Blocks.ServiceDefaults.EntityFramework` (the `clients` and `api_keys` tables, their model
  configuration, and the store over any `DbContext`). PDF Builder references both; nothing in them
  references a `PdfBuilder.*` assembly, and an architecture test enforces that.
- **Rationale**: constitution Principle V says every block gets the studs "through the shared
  `Blocks.*` packages rather than re-implementing them", and feature 001's Complexity Tracking
  deferred the extraction to this feature, "when a second set of tables exists". Authentication is
  that set. Keeping them as projects in this repository until feature 011 moves them to
  `blocks-platform` makes the move mechanical.
- **Alternatives considered**: writing it inside `PdfBuilder.Api`/`Infrastructure` and extracting
  later (the rewrite 001 deliberately avoided); one package with EF inside (would force EF on a block
  that only wants the handler, and on the CLI).

## R2. One authentication scheme, two kinds of key

- **Decision**: one ASP.NET Core authentication handler, scheme `ApiKey`, reading `X-Api-Key`.
  A value starting `adm_` is checked against the configured admin hash; a value starting
  `{blockCode}_` is looked up as a client key; anything else is malformed. Success produces a
  principal with `client_id` (absent for the admin), `key_id`, and one `scope` claim per permission
  (`admin` for the admin key).
- **Rationale**: the design in `docs/blocks/04-backend.md` has three schemes behind a forwarding
  policy scheme; two of them (admin key, client key) share the header and differ only by prefix,
  and the third (console session) arrives with feature 004. One handler now; 004 adds the cookie
  scheme and the forwarding policy in front of it without changing this handler.
- **Alternatives considered**: three schemes and the forwarding policy now (a scheme with nothing to
  forward to yet).

## R3. Secure by default: a fallback policy

- **Decision**: the authorization **fallback policy** requires an authenticated caller, so every
  endpoint is protected unless it opts out. Opt-outs are explicit and few: `/health/live`,
  `/health/ready`, `GET /files/{fileId}` (until feature 005) and, outside production only, the
  OpenAPI document and Scalar. Endpoint groups then demand a permission: `pdf:generate` for jobs,
  `pdf:templates:read` for listing and reading templates, `pdf:templates:write` for everything else
  under `/api/templates`, `admin` for `/api/admin`.
- **Rationale**: an endpoint added later without thinking about auth is closed, not open. The
  opt-outs are visible in `Program.cs`.
- **Alternatives considered**: `RequireAuthorization` on each group only (a forgotten group is open).

## R4. Failures: why, recorded once, answered by the stud

- **Decision**: the handler classifies every non-success as one of `Missing`, `Invalid`,
  `ClientDisabled` or `StorageUnavailable`, stores it on the request, and `HandleChallengeAsync` /
  `HandleForbiddenAsync` write the stud problem details: `401 AUTH_REQUIRED` (with
  `WWW-Authenticate: ApiKey`), `401 AUTH_INVALID_KEY`, `403 CLIENT_DISABLED`, `503
  STORAGE_UNAVAILABLE` with `Retry-After: 5`, and `403 AUTH_SCOPE_MISSING` for a missing permission.
  Every body has a fixed `detail` and the trace id; none repeats what the caller sent.
- **Rationale**: FR-021 needs an outage to read as "retry", but ASP.NET Core's authentication
  middleware turns an exception inside a handler into an authentication *failure*, which would be a
  401 telling a product its key is wrong. Catching storage failures in the handler (using
  `StorageFailures.IsUnavailable` from 001) and answering from the recorded reason avoids relying on
  how exceptions travel through the middleware. Unknown, malformed, revoked and expired all map to
  the same `Invalid` outcome before anything is written (FR-015).
- **Alternatives considered**: letting exceptions propagate to `ApiExceptionHandler` (depends on
  middleware internals; one version change from becoming a 401).

## R5. Key format, generation and storage

- **Decision**: client keys are `pdf_{keyId}_{secret}`: `keyId` is 8 characters `[a-z0-9]`, public,
  the primary key of its row; `secret` is 40 characters base62 from `RandomNumberGenerator` (~238
  bits). The row stores `SHA-256(secret)`, compared with `CryptographicOperations.FixedTimeEquals`.
  The admin key is `adm_{secret}`; configuration holds `Blocks:Admin:KeyHash`, the lowercase hex
  SHA-256 of the whole admin key. The service refuses to start without it, naming the setting.
- **Rationale**: Principle VIII (unrecoverable, shown once, constant-time). A fast hash is correct for
  long random secrets: slow hashes defend low-entropy passwords, and would cost every request. The
  prefix makes a leaked key traceable (FR-009) and lets secret scanners recognise it. A key id
  collision on insert is retried with a new id.
- **Alternatives considered**: storing the key id inside the hash only (cannot look a key up without
  scanning); bcrypt/Argon2 (wrong tool, see above); a separate `uuid` key column (the 8-character id
  is already unique and public).

## R6. Revocation within a minute, cheaply

- **Decision**: each instance caches what it learns about a key id (client, client state, scopes,
  hash, expiry, revoked) in `IMemoryCache` for **60 seconds absolute**, and also caches "no such key"
  for 60 seconds. Expiry is checked against the cached expiry time on every request, so it is exact;
  revocation and client disabling are seen within the cache lifetime.
- **Rationale**: FR-013 and SC-002 (60 s, every instance) with no database read on most requests
  (SC-007 < 25 ms). Caching misses means a scan of invented keys (SC-008) costs one lookup per id
  per minute, not one per request.
- **Alternatives considered**: no cache (a database read per request); cross-instance invalidation
  messages (more moving parts for a bound the spec already accepts).

## R7. Last use, off the request path

- **Decision**: after a successful client-key check, if that instance has not recorded a use of the
  key in the last minute, it puts the key id on an in-process channel. A `KeyUsageRecorder`
  background service drains it every few seconds and sets `last_used_at` with one update per key.
- **Rationale**: FR-012 (to the minute, no noticeable work on the call). At most one write per key
  per minute per instance, whatever the request rate.
- **Alternatives considered**: updating on the request (a write per call); a log of calls (out of
  scope; logs already carry client and key id).

## R8. Management endpoints live in the shared package

- **Decision**: `MapBlocksAdmin()` from `Blocks.ServiceDefaults` maps, under `/api/admin`, the
  client and key operations in [contracts/admin-api.md](contracts/admin-api.md), all requiring
  `admin`. They talk to an `IClientKeyStore` port implemented by the EF package. Validation follows
  the spec (client id `^[a-z][a-z0-9-]{0,39}$`, names 1–100 characters, at least one known
  permission, expiry in the future); failures use the stud format with codes `VALIDATION_FAILED`,
  `CLIENT_NOT_FOUND`, `KEY_NOT_FOUND`, `CLIENT_ID_TAKEN`. Revoking twice, or disabling a disabled
  client, succeeds without change.
- **Rationale**: every block needs the same operations, so they belong with the studs (Principle V),
  and the console (004) and the CLI call the same endpoints.

## R9. The CLI, minimal

- **Decision**: `tools/Blocks.Cli` with one command, `admin-key`, which prints a new admin key once
  and the `Blocks:Admin:KeyHash` value for it. Client and key management is done through the
  management endpoints; `samples/admin.http` and the quickstart show each call.
- **Rationale**: FR-002 needs the admin key to be generated somewhere other than by hand; that cannot
  be an endpoint, because nothing is authenticated before the admin key exists. Wrappers around the
  management endpoints add little before the console screens arrive (004). The spec's Assumptions are
  updated to say so.
- **Alternatives considered**: the full CLI from `docs/blocks/04-backend.md` now (deferred to 011,
  when it becomes a `dotnet` tool shared by every block).

## R10. `ICurrentClient` and the logs

- **Decision**: `ICurrentClient` (`ClientId`, `KeyId`, `Scopes`, `IsAdmin`) lives in
  `Blocks.ServiceDefaults`, implemented from the request's claims. A middleware after authorization
  opens a log scope with `ClientId` and `KeyId` (or `Admin = true`) for the rest of the request.
- **Rationale**: FR-020 and SC-009. Principle VI: from here on, code reads identity through
  `ICurrentClient` and never from headers. In this feature no Application code needs it yet; where the
  interface should live for the Application layer is feature 003's decision.

## R11. Upgrading callers, the console and the local stack

- **Decision**: no "authentication off" setting (spec FR-023). `compose.yaml` sets the hash of a
  published, local-only admin key so `docker compose up` still works; `deploy/compose.prod.yaml`
  requires `ADMIN_KEY_HASH` in `.env`; the Hugging Face Space takes it as a secret. The web console
  in `web/` is not changed and will be refused until feature 004; this feature ships with 004.
  `samples/Invoke-PdfJob.ps1` and `samples/requests.http` gain the key.
- **Rationale**: the clarification of 2026-11-03.

## R12. OpenAPI

- **Decision**: the OpenAPI document declares an `ApiKey` security scheme (header `X-Api-Key`) and
  applies it to protected operations, so Scalar and generated clients send the header.

## R13. Tests

- **Decision**: a new `tests/Blocks.ServiceDefaults.Tests` (key format, hashing, the handler's
  classification with a fake store, the management validation, and the architecture rule that no
  `Blocks.*` assembly references `PdfBuilder.*`; no database). Database-backed store tests in
  `tests/PdfBuilder.Infrastructure.Tests` against `PdfBuilderDbContext`. Whole-pipeline tests in
  `tests/PdfBuilder.Api.Tests` with `WebApplicationFactory<Program>` (`Microsoft.AspNetCore.Mvc.Testing`
  10.0.12, checked on nuget.org), the MassTransit test harness in place of RabbitMQ, and the 001
  PostgreSQL fixture; opt-in with `PDFBUILDER_RUN_DATABASE_TESTS=1` like the rest.
- **Rationale**: authentication is decided in middleware, so the claims that matter (every protected
  endpoint refuses, the health endpoints do not, an outage is a 503) can only be proven through the
  real pipeline.

## Resolved unknowns

None remain. The spec's one product question (FR-023) was settled on 2026-11-03.
