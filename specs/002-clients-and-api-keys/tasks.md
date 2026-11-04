---

description: "Task list for feature 002: Clients and API Keys"
---

# Tasks: Clients and API Keys

**Input**: Design documents from `/specs/002-clients-and-api-keys/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The plan (Project Structure) and research R13 name the test projects and files, and
several requirements (FR-015 identical refusals, FR-021 503 on outage, SC-005 key appears nowhere) can only
be proven through tests. Database-backed and whole-pipeline tests are opt-in with
`PDFBUILDER_RUN_DATABASE_TESTS=1`, like the rest of the suite.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to (US1–US6)
- Paths are relative to the repository root

## Path Conventions

- Shared stud packages: `src/Blocks.ServiceDefaults/`, `src/Blocks.ServiceDefaults.EntityFramework/`
- Block projects: `src/PdfBuilder.Api/`, `src/PdfBuilder.Infrastructure/`
- CLI: `tools/Blocks.Cli/`
- Tests: `tests/Blocks.ServiceDefaults.Tests/`, `tests/PdfBuilder.Infrastructure.Tests/`, `tests/PdfBuilder.Api.Tests/`
- Rule for every task: nothing under `src/Blocks.*` or `tools/Blocks.Cli` may reference a `PdfBuilder.*` assembly (Principle IX, R1)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the new projects and wire references and packages.

- [X] T001 Create `src/Blocks.ServiceDefaults/Blocks.ServiceDefaults.csproj` (class library, `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, package reference `Microsoft.Extensions.Caching.Memory`), no `PdfBuilder.*` references
- [X] T002 Create `src/Blocks.ServiceDefaults.EntityFramework/Blocks.ServiceDefaults.EntityFramework.csproj` referencing `src/Blocks.ServiceDefaults` and packages `Microsoft.EntityFrameworkCore.Relational` and `Npgsql.EntityFrameworkCore.PostgreSQL` (versions from `Directory.Packages.props`)
- [X] T003 [P] Create console project `tools/Blocks.Cli/Blocks.Cli.csproj` (`OutputType` Exe) referencing `src/Blocks.ServiceDefaults` only
- [X] T004 [P] Create test project `tests/Blocks.ServiceDefaults.Tests/Blocks.ServiceDefaults.Tests.csproj` (xUnit v3 + NSubstitute, same shape as `tests/PdfBuilder.Application.Tests/PdfBuilder.Application.Tests.csproj`) referencing both `Blocks.*` projects
- [X] T005 Add `<PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />` to `Directory.Packages.props` and a reference to it in `tests/PdfBuilder.Api.Tests/PdfBuilder.Api.Tests.csproj`
- [X] T006 Add project references: `src/PdfBuilder.Infrastructure/PdfBuilder.Infrastructure.csproj` → `Blocks.ServiceDefaults.EntityFramework`; `src/PdfBuilder.Api/PdfBuilder.Api.csproj` → `Blocks.ServiceDefaults`
- [X] T007 Add the four new projects to `PdfBuilder.slnx` (`/src/`, `/tests/`, and a new `/tools/` folder)
- [X] T008 [P] Update `src/PdfBuilder.Api/Dockerfile` so the restore/build stages copy `src/Blocks.ServiceDefaults/` and `src/Blocks.ServiceDefaults.EntityFramework/`; confirm `dotnet build PdfBuilder.slnx` succeeds

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The stud core every story needs: options, key format, hashing, problems, storage, the
authentication handler, the permission policies and the pipeline wiring.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete. At the end of it every
`/api/*` endpoint refuses keyless callers, and the admin key is accepted everywhere.

### Core types (Blocks.ServiceDefaults)

- [X] T009 [P] Implement `BlocksAuthOptions` in `src/Blocks.ServiceDefaults/BlocksAuthOptions.cs`: `BlockCode` (e.g. `pdf`), `Permissions` (the block's list), and `Admin.KeyHash` bound from `Blocks:Admin:KeyHash`; validate on start (`ValidateOnStart`) that `KeyHash` is present and exactly 64 lowercase hex characters, with an error message that names `Blocks:Admin:KeyHash` (FR-001, contracts/configuration.md)
- [X] T010 [P] Implement `ApiKeyFormat` in `src/Blocks.ServiceDefaults/Authentication/ApiKeyFormat.cs`: generate client keys `{blockCode}_{keyId}_{secret}` with `keyId` 8 characters `[a-z0-9]` and `secret` 40 characters base62 from `RandomNumberGenerator`; generate admin keys `adm_{secret}`; `TryParse` that returns `Admin`, `Client(keyId, secret)` or `Malformed`, rejecting empty, whitespace-padded, over-length (cap the accepted length before any work), wrong-prefix (another block's code) and wrong-shape values; `Display(keyId)` → `pdf_{keyId}_…` (R5, data-model "The full key")
- [X] T011 [P] Implement `KeyHasher` in `src/Blocks.ServiceDefaults/Authentication/KeyHasher.cs`: `HashSecret(string) → byte[32]` (SHA-256), `HashAdminKeyHex(string) → lowercase hex` of the whole admin key, and `Matches(...)` using `CryptographicOperations.FixedTimeEquals` (R5, Principle VIII)
- [X] T012 [P] Implement `BlocksProblems` in `src/Blocks.ServiceDefaults/Problems/BlocksProblems.cs`: writers for `application/problem+json` with fixed `title`/`detail`, `code` and `traceId` for `401 AUTH_REQUIRED` (+ `WWW-Authenticate: ApiKey`), `401 AUTH_INVALID_KEY` (+ `WWW-Authenticate: ApiKey`), `403 CLIENT_DISABLED`, `403 AUTH_SCOPE_MISSING`, `503 STORAGE_UNAVAILABLE` (+ `Retry-After: 5`), and the admin codes `400 VALIDATION_FAILED` (with `errors`), `404 CLIENT_NOT_FOUND`, `404 KEY_NOT_FOUND`, `409 CLIENT_ID_TAKEN`; no body ever echoes request input (contracts/authentication.md#refusals, contracts/admin-api.md#codes)
- [X] T013 [P] Define the storage port in `src/Blocks.ServiceDefaults/Clients/IClientKeyStore.cs` with read models `ClientInfo` and `ApiKeyInfo` (in the same folder): `FindKeyAsync(keyId)` returning key + client state for authentication, plus client/key management methods (`AddClientAsync`, `GetClientAsync`, `ListClientsAsync`, `UpdateClientAsync`, `AddKeyAsync`, `ListKeysAsync`, `RevokeKeyAsync`, `RecordLastUsedAsync`); storage failures surface as exceptions the caller classifies
- [X] T014 [P] Define `BlocksClaims` constants (`client_id`, `key_id`, `scope`, scope value `admin`) and `ApiKeyAuthenticationDefaults.Scheme = "ApiKey"` in `src/Blocks.ServiceDefaults/Authentication/BlocksClaims.cs`

### Storage (Blocks.ServiceDefaults.EntityFramework + PdfBuilder.Infrastructure)

- [X] T015 [P] Create `ClientRecord` in `src/Blocks.ServiceDefaults.EntityFramework/ClientRecord.cs` and `ApiKeyRecord` in `src/Blocks.ServiceDefaults.EntityFramework/ApiKeyRecord.cs` matching data-model.md
- [X] T016 Implement `modelBuilder.AddBlocksClients()` in `src/Blocks.ServiceDefaults.EntityFramework/ModelBuilderExtensions.cs`: table `clients` — `id varchar(40)` PK, `name varchar(100)` not null, `created_at timestamptz` not null, `disabled_at timestamptz` null, check `ck_clients_id_format` (`id ~ '^[a-z][a-z0-9-]{0,39}$'`); table `api_keys` — `key_id char(8)` PK, `client_id varchar(40)` FK → `clients.id` `ON DELETE RESTRICT`, `name varchar(100)` not null, `scopes text[]` not null, `secret_hash bytea` not null, `created_at timestamptz` not null, `expires_at`/`revoked_at`/`last_used_at timestamptz` null; checks `ck_api_keys_scopes_not_empty` (`cardinality(scopes) > 0`), `ck_api_keys_secret_hash_length` (`octet_length(secret_hash) = 32`), `ck_api_keys_key_id_format` (`key_id ~ '^[a-z0-9]{8}$'`); index `ix_api_keys_client_id` on `client_id` (depends on T015)
- [X] T017 Implement `EfClientKeyStore<TContext> : IClientKeyStore` in `src/Blocks.ServiceDefaults.EntityFramework/EfClientKeyStore.cs` with `FindKeyAsync` (no-tracking read of key joined to its client) and a DI extension `AddBlocksClientStore<TContext>()` in the same file; the remaining port methods are filled in by the stories (depends on T013, T016)
- [X] T018 Call `modelBuilder.AddBlocksClients()` in `OnModelCreating` of `src/PdfBuilder.Infrastructure/Persistence/PdfBuilderDbContext.cs` and register `AddBlocksClientStore<PdfBuilderDbContext>()` in the Infrastructure DI registration (depends on T017)
- [X] T019 Add migration `AddClientsAndApiKeys` under `src/PdfBuilder.Infrastructure/Persistence/Migrations/` (`dotnet ef migrations add AddClientsAndApiKeys`), check the generated SQL contains every constraint from T016, and update `PdfBuilderDbContextModelSnapshot.cs` (depends on T018)

### Authentication and authorization

- [X] T020 Implement `ApiKeyAuthenticationHandler` in `src/Blocks.ServiceDefaults/Authentication/ApiKeyAuthenticationHandler.cs`: read `X-Api-Key` (header absent → `NoResult` recorded as `Missing`; header sent more than once → `Invalid`); `adm_` key → compare `KeyHasher.HashAdminKeyHex` with options in constant time, success with a principal that has **no** `client_id` and **no** `key_id`, only `scope=admin`; client key → `IClientKeyStore.FindKeyAsync`, then checks **in this order**: unknown → hash mismatch → revoked → expired (expiry compared with `TimeProvider`), all → `Invalid`; only a key that passed all of those and whose client is disabled → `ClientDisabled` (so `CLIENT_DISABLED` never confirms that a key id exists to someone without its secret, FR-018). Storage: `EfClientKeyStore` wraps a failure that `NpgsqlStorageFailures.IsUnavailable` (moved from `src/PdfBuilder.Infrastructure/Persistence/StorageFailures.cs` into `src/Blocks.ServiceDefaults.EntityFramework/NpgsqlStorageFailures.cs`, which `StorageFailures` then delegates to) recognises in a `StorageUnavailableException` from `src/Blocks.ServiceDefaults/Clients/StorageUnavailableException.cs`, keeping the original as `InnerException`; the handler maps that exception → `StorageUnavailable` with a Warning log carrying the key id only; success → principal with `client_id`, `key_id` and one `scope` claim per permission. Store the failure reason in `HttpContext.Items`; `HandleChallengeAsync`/`HandleForbiddenAsync` answer via `BlocksProblems` from that reason (R2, R4)
- [X] T021 Implement `BlocksAuthExtensions` in `src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs`: `AddBlocksAuthentication(blockCode, permissions)` registers options (T009), the `ApiKey` scheme, one authorization policy per permission that is satisfied by that `scope` claim **or** `scope=admin`, and a **fallback policy** requiring an authenticated user (R3); `RequirePermission(this IEndpointConventionBuilder, string)`; a forbidden result that writes `403 AUTH_SCOPE_MISSING` (via `IAuthorizationMiddlewareResultHandler` or the handler's `HandleForbiddenAsync`) unless the recorded reason is `ClientDisabled` (depends on T009, T012, T020)
- [X] T022 Wire the pipeline in `src/PdfBuilder.Api/Program.cs`: `AddBlocksAuthentication("pdf", ["pdf:generate", "pdf:templates:read", "pdf:templates:write"])`; `app.UseAuthentication(); app.UseAuthorization();` after `UseExceptionHandler`; `.AllowAnonymous()` on `/health/live`, `/health/ready`, the file endpoints from `MapFileEndpoints()`, `MapOpenApi()` and `MapScalarApiReference(...)` (FR-019) (depends on T021)
- [X] T023 [P] Add the local-only admin key hash so the stack still starts: `Blocks__Admin__KeyHash` in `compose.yaml` with the published local-only key in a comment beside it ("never use anywhere else"); `ADMIN_KEY_HASH` required (`${ADMIN_KEY_HASH:?…}`) in `deploy/compose.prod.yaml` mapped to `Blocks__Admin__KeyHash` (R11, contracts/configuration.md)

### Test harness and foundational tests

- [X] T024 [P] Architecture test in `tests/Blocks.ServiceDefaults.Tests/ArchitectureTests.cs`: no assembly named `Blocks.*` references any `PdfBuilder.*` assembly (Principle IX, R1)
- [X] T025 [P] Unit tests for `ApiKeyFormat` and `KeyHasher` in `tests/Blocks.ServiceDefaults.Tests/ApiKeyFormatTests.cs` and `tests/Blocks.ServiceDefaults.Tests/KeyHasherTests.cs`: generated keys match `^pdf_[a-z0-9]{8}_[A-Za-z0-9]{40}$` and `^adm_[A-Za-z0-9]+$`; parse round-trips; another block's prefix, whitespace, empty and over-long input are `Malformed`; hash is 32 bytes; hex is 64 lowercase characters
- [X] T026 [P] Options validation tests in `tests/Blocks.ServiceDefaults.Tests/BlocksAuthOptionsTests.cs`: missing, 63-character and upper-case `KeyHash` fail with a message naming `Blocks:Admin:KeyHash` (FR-001, quickstart Scenario 8)
- [X] T027 Create `ApiFactory` in `tests/PdfBuilder.Api.Tests/ApiFactory.cs`: `WebApplicationFactory<Program>` (add `public partial class Program;` to `src/PdfBuilder.Api/Program.cs` if needed), environment `Production`, MassTransit test harness instead of RabbitMQ, the 001 `PostgresFixture` database with migrations applied, a test admin key whose hash is set in configuration, a controllable `TimeProvider`, and helpers `AdminClient()` / `ClientWithKey(string)`; skip when `PDFBUILDER_RUN_DATABASE_TESTS` is not `1` (R13)

**Checkpoint**: the service starts only with `Blocks:Admin:KeyHash`; keyless calls to `/api/*` are refused; the admin key passes; health is open. User stories can begin.

---

## Phase 3: User Story 1 - The operator registers a client and issues it a key (Priority: P1) 🎯 MVP

**Goal**: With only the admin key, the operator registers clients and issues named, scoped, optionally
expiring keys, shown in full once and stored only as a hash.

**Independent Test**: with the admin key, `POST /api/admin/clients`, then `POST …/keys`; the returned key is
accepted on a protected call; `GET …/keys` and the database never contain it (quickstart Scenario 1).

### Tests for User Story 1

- [X] T028 [P] [US1] Validation tests in `tests/Blocks.ServiceDefaults.Tests/ClientAdminValidationTests.cs`: client id must match `^[a-z][a-z0-9-]{0,39}$` (reject `E-Commerce`, `1abc`, 41 characters, empty); name 1–100 characters after trimming; key needs at least one scope, every scope from the block's list (message lists the known ones), `expiresAt` null or in the future (FR-005, FR-007, R8)
- [X] T029 [P] [US1] Store tests in `tests/PdfBuilder.Infrastructure.Tests/EfClientKeyStoreTests.cs` against PostgreSQL: add and read a client; duplicate id (also when disabled) reports "taken"; add a key and find it by key id with its client; list keys newest first; `secret_hash` stored is 32 bytes and differs from the secret; check constraints reject a bad id, empty scopes and a 31-byte hash
- [X] T030 [P] [US1] End-to-end tests in `tests/PdfBuilder.Api.Tests/AdminApiTests.cs`: register → `201` with `Location: /api/admin/clients/{id}` and `status: "Active"`; duplicate → `409 CLIENT_ID_TAKEN`; bad id → `400 VALIDATION_FAILED` with `errors.id`; issue key → `201`, `Cache-Control: no-store`, `key` present only here, `display` is `pdf_{keyId}_…`; the issued key is accepted on `POST /api/pdf/jobs` immediately; list keys and a raw `SELECT * FROM api_keys` contain no substring of the secret (FR-008, FR-011, SC-005); `GET /api/admin/clients` → `200` with `items` ordered by id; `GET /api/admin/clients/missing` → `404 CLIENT_NOT_FOUND`

### Implementation for User Story 1

- [X] T031 [P] [US1] Define request/response records and validation in `src/Blocks.ServiceDefaults/Clients/ClientAdminContracts.cs`: `CreateClientRequest {id, name}`, `ClientResponse {id, name, status ("Active"|"Disabled"), createdAt, disabledAt}`, `IssueKeyRequest {name, scopes, expiresAt}`, `KeyResponse {keyId, display, name, scopes, status ("Active"|"Expired"|"Revoked", Revoked wins over Expired), createdAt, expiresAt, revokedAt, lastUsedAt}`, `IssuedKeyResponse` = `KeyResponse` + `key`; validators producing the `errors` dictionary for `VALIDATION_FAILED` (contracts/admin-api.md)
- [X] T032 [US1] Implement `AddClientAsync`, `GetClientAsync`, `ListClientsAsync` (ordered by id), `AddKeyAsync` (retry with a new key id on primary-key collision) and `ListKeysAsync` (newest first) in `src/Blocks.ServiceDefaults.EntityFramework/EfClientKeyStore.cs`; map a unique violation on `clients.id` to a "taken" result
- [X] T033 [US1] Implement `ClientAdminEndpoints` in `src/Blocks.ServiceDefaults/Clients/ClientAdminEndpoints.cs` and expose `MapBlocksAdmin()` from `src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs`: group `/api/admin` with `RequirePermission("admin")`; `POST /clients`, `GET /clients`, `GET /clients/{clientId}`, `POST /clients/{clientId}/keys` (generate with `ApiKeyFormat`, hash with `KeyHasher`, respond once with the key and `Cache-Control: no-store`; allowed for a disabled client), `GET /clients/{clientId}/keys`; Information logs for client registered and key issued with client id, key id, key name and scopes, never the key (contracts/configuration.md#logs)
- [X] T034 [US1] Call `app.MapBlocksAdmin()` in `src/PdfBuilder.Api/Program.cs`
- [X] T035 [P] [US1] Implement the `admin-key` command in `tools/Blocks.Cli/Program.cs`: prints a new `adm_…` key once and its `Blocks:Admin:KeyHash` value, in the format of contracts/configuration.md#generating-the-admin-key; unknown commands print usage and exit non-zero (FR-002, R9)
- [X] T036 [P] [US1] Add `samples/admin.http` with every management call (register, list, get, patch, issue, list keys, revoke) using `@adminKey` and `@baseUrl` variables

**Checkpoint**: the operator can go from the admin key to a working client key in under two minutes (SC-004).

---

## Phase 4: User Story 2 - A product calls the service with its key and is identified (Priority: P1)

**Goal**: A valid key of sufficient permission gets exactly today's responses, and every log line of the
request names the client and key.

**Independent Test**: call each protected endpoint with a sufficient key: responses are unchanged; the log
lines carry `ClientId` and `KeyId` (quickstart Scenario 2).

### Tests for User Story 2

- [X] T037 [P] [US2] Tests in `tests/Blocks.ServiceDefaults.Tests/CurrentClientTests.cs`: `HttpCurrentClient` reads `ClientId`, `KeyId`, `Scopes`, `IsAdmin` from claims; anonymous → nulls and `IsAdmin=false`
- [X] T038 [P] [US2] Pipeline tests in `tests/PdfBuilder.Api.Tests/ClientIdentificationTests.cs`: with a key holding all three permissions, `POST /api/pdf/jobs` returns `202` with the same body shape as before and `GET /api/pdf/jobs/{jobId}` returns the same response; two clients' requests produce log scopes with their own `ClientId`/`KeyId` (capture via a test `ILoggerProvider` that records scopes); admin requests carry `Admin = true` (FR-020, FR-022, SC-009)

### Implementation for User Story 2

- [X] T039 [P] [US2] Implement `ICurrentClient` (`ClientId`, `KeyId`, `Scopes`, `IsAdmin`) in `src/Blocks.ServiceDefaults/Identity/ICurrentClient.cs` and `HttpCurrentClient` (from `IHttpContextAccessor` claims) in `src/Blocks.ServiceDefaults/Identity/HttpCurrentClient.cs`; register as scoped in `AddBlocksAuthentication` (R10, Principle VI)
- [X] T040 [P] [US2] Implement `ClientLogScopeMiddleware` in `src/Blocks.ServiceDefaults/Identity/ClientLogScopeMiddleware.cs` opening `ILogger.BeginScope` with `ClientId` and `KeyId`, or `Admin = true`, for the rest of the request; add `UseBlocksClientLogScope()` to `src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs` (R10)
- [X] T041 [US2] Add `app.UseBlocksClientLogScope()` after `UseAuthorization()` in `src/PdfBuilder.Api/Program.cs` (depends on T040)
- [X] T042 [US2] Add `.RequirePermission("pdf:generate")` to the job group in `src/PdfBuilder.Api/Endpoints/PdfJobEndpoints.cs` (`POST /api/pdf/jobs`, `GET /api/pdf/jobs/{jobId}`)
- [X] T043 [US2] Add permissions per endpoint in `src/PdfBuilder.Api/Endpoints/TemplateEndpoints.cs`: `pdf:templates:read` on `GET /api/templates` and `GET /api/templates/{key}`; `pdf:templates:write` on `POST /api/templates`, `POST /api/templates/{key}/duplicate`, `PUT /api/templates/{key}/draft`, `POST /api/templates/{key}/publish`, `DELETE /api/templates/{key}`, `POST /api/templates/preview` (contracts/authentication.md table)
- [X] T044 [P] [US2] Implement `ApiKeySecurityTransformer` in `src/PdfBuilder.Api/OpenApi/ApiKeySecurityTransformer.cs` declaring an `ApiKey` security scheme (header `X-Api-Key`) and applying it to operations that are not anonymous; register with `AddOpenApi(o => o.AddDocumentTransformer<ApiKeySecurityTransformer>())` in `src/PdfBuilder.Api/Program.cs` (R12)
- [X] T045 [US2] Update the existing pipeline-level tests in `tests/PdfBuilder.Api.Tests/` that call HTTP endpoints (if any) to send a sufficient key so every existing job/template check passes unchanged (SC-006)

**Checkpoint**: Stories 1 and 2 together are a usable, identified, protected service.

---

## Phase 5: User Story 3 - Everyone else is refused, the same way every time (Priority: P1)

**Goal**: Missing, unknown, malformed, revoked and expired keys are refused before any work, with the stud
format; the invalid cases are indistinguishable; outages answer 503; health stays open.

**Independent Test**: quickstart Scenarios 3 and 7: no key → `401 AUTH_REQUIRED`; made-up, malformed,
revoked and expired keys → identical `401 AUTH_INVALID_KEY`; health open; database down → `503`.

### Tests for User Story 3

- [X] T046 [P] [US3] Handler classification tests in `tests/Blocks.ServiceDefaults.Tests/ApiKeyAuthenticationHandlerTests.cs` with a fake `IClientKeyStore` (NSubstitute) and a fake `TimeProvider`: no header → Missing; header twice, empty, whitespace-padded, 10 000 characters, `eml_…`, `banana` → Invalid; unknown id, wrong secret, revoked, expired → Invalid; disabled client → ClientDisabled; wrong secret on a disabled client's key id → Invalid; revoked key of a disabled client → Invalid; store throws a "storage unavailable" exception → StorageUnavailable; store throws anything else → not swallowed as Invalid
- [X] T047 [P] [US3] Pipeline tests in `tests/PdfBuilder.Api.Tests/AuthenticationPipelineTests.cs`: every protected endpoint (enumerate from `EndpointDataSource`, excluding the documented anonymous ones) answers `401 AUTH_REQUIRED` with `WWW-Authenticate: ApiKey` and no key; unknown, malformed, revoked and expired keys give bodies identical apart from `traceId` (FR-015, SC-003); no body contains the sent key or key id; `traceId` is present; `/health/live`, `/health/ready` and `GET /files/{fileId}` answer as before without a key; a refused `POST /api/pdf/jobs` records no `pdf_jobs` row and publishes no message on the test harness (SC-001)
- [X] T048 [P] [US3] Outage test in `tests/PdfBuilder.Api.Tests/AuthenticationPipelineTests.cs`: with the database stopped (or connection string pointed at a closed port), a client key → `503 STORAGE_UNAVAILABLE` with `Retry-After: 5`, never `401`; the admin key is still recognised without the database, and `GET /api/admin/clients` with it answers `503 STORAGE_UNAVAILABLE` (the operation needs storage), not `401` (FR-021, quickstart Scenario 7)

### Implementation for User Story 3

- [X] T049 [US3] Harden `ApiKeyAuthenticationHandler` in `src/Blocks.ServiceDefaults/Authentication/ApiKeyAuthenticationHandler.cs` to make T046–T048 pass: one code path from every invalid case to the same `Invalid` reason before anything is written; constant-time comparison for both key kinds; refusals for bad keys logged below Warning and never with the key value (contracts/configuration.md#logs, edge case "burst of invalid keys")
- [X] T050 [US3] Ensure `BlocksProblems` responses in `src/Blocks.ServiceDefaults/Problems/BlocksProblems.cs` use the `type` URIs from contracts/authentication.md, fixed `title`/`detail` per code, and `Activity.Current?.Id ?? HttpContext.TraceIdentifier` for `traceId`, matching the `ApiExceptionHandler` shape in `src/PdfBuilder.Api/Endpoints/ApiExceptionHandler.cs`

**Checkpoint**: the P1 stories are complete; the service is safe to put on a network (MVP).

---

## Phase 6: User Story 4 - Revoke a key or disable a client, effective within a minute (Priority: P2)

**Goal**: Revocation (permanent), client disable/enable (reversible) and expiry take effect on every
instance within 60 seconds, with at most one database read per key per minute per instance.

**Independent Test**: quickstart Scenario 4: revoke → `401 AUTH_INVALID_KEY` within 60 s while the client's
other key works; disable → `403 CLIENT_DISABLED`; enable → works again; expired key → `401` and listed as `Expired`.

### Tests for User Story 4

- [X] T051 [P] [US4] Cache tests in `tests/Blocks.ServiceDefaults.Tests/KeyCacheTests.cs`: a found key and a "no such key" are each read from the store once per 60 s; after 60 s (fake `TimeProvider`/clock) the store is read again; expiry is evaluated on every request against the cached `expires_at`, so an expired key is refused at once
- [X] T052 [P] [US4] Store tests in `tests/PdfBuilder.Infrastructure.Tests/EfClientKeyStoreTests.cs`: `RevokeKeyAsync` sets `revoked_at` once and a second call leaves it unchanged; revoking a key of another client returns not found; `UpdateClientAsync` sets and clears `disabled_at` and renames
- [X] T053 [P] [US4] End-to-end tests in `tests/PdfBuilder.Api.Tests/AdminApiTests.cs`: `DELETE /api/admin/clients/{id}/keys/{keyId}` → `204`, again → `204`, unknown → `404 KEY_NOT_FOUND`; after advancing past the cache lifetime (or with the cache cleared) the revoked key → `401 AUTH_INVALID_KEY` and the client's other key still works; `PATCH {"disabled":true}` → keys get `403 CLIENT_DISABLED`, `{"disabled":false}` → they work again; a key issued with `expiresAt` in the past → `400 VALIDATION_FAILED`; a key that expires → `401` and listed `status: "Expired"`; a job accepted before revocation still completes (Story 4 scenario 5)

### Implementation for User Story 4

- [X] T054 [US4] Implement `KeyCache` in `src/Blocks.ServiceDefaults/Authentication/KeyCache.cs` over `IMemoryCache`: caches `ApiKeyInfo` (client id, client disabled, scopes, hash, expiry, revoked) and "not found" per key id for **60 seconds absolute**, measured with `TimeProvider` (each entry stores the time it was read, and an entry older than 60 s is ignored; `IMemoryCache`'s own expiry only evicts), so tests can move the clock; use it from `ApiKeyAuthenticationHandler` so the store is read at most once a minute per key id; never cache a storage failure (R6, FR-013)
- [X] T055 [US4] Implement `RevokeKeyAsync` (set `revoked_at` only when null; not found when the key is not the client's) and `UpdateClientAsync` (`name`, `disabled` → set/clear `disabled_at`) in `src/Blocks.ServiceDefaults.EntityFramework/EfClientKeyStore.cs`
- [X] T056 [US4] Add `PATCH /api/admin/clients/{clientId}` (optional `name`, `disabled`; idempotent; `404 CLIENT_NOT_FOUND`, `400 VALIDATION_FAILED`) and `DELETE /api/admin/clients/{clientId}/keys/{keyId}` (`204`, idempotent, `404 KEY_NOT_FOUND`) to `src/Blocks.ServiceDefaults/Clients/ClientAdminEndpoints.cs`, with Information logs for client enabled/disabled and key revoked (contracts/admin-api.md)

**Checkpoint**: a leaked key is survivable within a minute on every instance.

---

## Phase 7: User Story 5 - A key can be limited to what its product needs (Priority: P2)

**Goal**: Each endpoint demands one independent permission; a usable key without it gets `403
AUTH_SCOPE_MISSING`, distinct from an invalid key; client keys never reach `/api/admin`.

**Independent Test**: quickstart Scenario 5: a `pdf:generate` key on `PUT /api/templates/invoice/draft` →
`403 AUTH_SCOPE_MISSING`, template unchanged; a `pdf:templates:read` key lists templates but cannot submit jobs.

### Tests for User Story 5

- [X] T057 [P] [US5] Permission matrix tests in `tests/PdfBuilder.Api.Tests/PermissionTests.cs`: for each endpoint in the contracts/authentication.md table and each single-permission key, allowed exactly where the table says, otherwise `403 AUTH_SCOPE_MISSING` with no state change (template unchanged, no job row); `pdf:templates:write` alone cannot `GET /api/templates` (permissions are independent); every client key, even with all three permissions, gets `403 AUTH_SCOPE_MISSING` on every `/api/admin` endpoint; the admin key passes every row (FR-003, FR-016, FR-017)
- [X] T058 [P] [US5] Policy tests in `tests/Blocks.ServiceDefaults.Tests/PermissionPolicyTests.cs`: a policy for `x` is satisfied by `scope=x` or `scope=admin` and by nothing else; `admin` is never accepted in an issue-key request

### Implementation for User Story 5

- [X] T059 [US5] In `src/Blocks.ServiceDefaults/Clients/ClientAdminContracts.cs`, reject `admin` and any scope outside the block's list when issuing a key, with the `VALIDATION_FAILED` message naming the known permissions (Story 5 scenario 4); in `src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs` make sure the forbidden response for a usable key without the permission is `AUTH_SCOPE_MISSING`, while a disabled client stays `CLIENT_DISABLED`
- [X] T060 [US5] Add a startup check in `src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs` (or a test in `tests/PdfBuilder.Api.Tests/PermissionTests.cs`) that every non-anonymous endpoint outside `/api/admin` declares exactly one `RequirePermission`, so a new endpoint cannot silently rely on authentication alone

**Checkpoint**: a leaked generation-only key cannot touch templates or management.

---

## Phase 8: User Story 6 - See which keys exist and when they were last used (Priority: P3)

**Goal**: Each key's `lastUsedAt` is accurate to the minute and recording it adds no work to the request.

**Independent Test**: quickstart Scenario 6: use a key, wait a few seconds, list keys: `lastUsedAt` is within
a minute of the call; an unused key shows `null`.

### Tests for User Story 6

- [X] T061 [P] [US6] Recorder tests in `tests/Blocks.ServiceDefaults.Tests/KeyUsageRecorderTests.cs`: 1 000 uses of one key in a minute enqueue it once; after a minute it is enqueued again; a flush calls `RecordLastUsedAsync` once per distinct key; a store failure during flush is logged and does not stop the recorder; the request path never awaits the store
- [X] T062 [P] [US6] End-to-end test in `tests/PdfBuilder.Api.Tests/AdminApiTests.cs`: after one call with a key and the recorder's flush, `GET …/keys` shows `lastUsedAt` within a minute of the call; a key never used shows `null`

### Implementation for User Story 6

- [X] T063 [US6] Implement `KeyUsageRecorder` in `src/Blocks.ServiceDefaults/Authentication/KeyUsageRecorder.cs`: a singleton with a bounded `Channel<string>` and a per-instance "last enqueued" map so a key id is enqueued at most once a minute, plus a `BackgroundService` that drains it every few seconds and calls `IClientKeyStore.RecordLastUsedAsync` once per key; register in `AddBlocksAuthentication` and call it from `ApiKeyAuthenticationHandler` on client-key success (R7, FR-012)
- [X] T064 [US6] Implement `RecordLastUsedAsync(keyIds, at)` in `src/Blocks.ServiceDefaults.EntityFramework/EfClientKeyStore.cs` as one `ExecuteUpdateAsync` setting `last_used_at` truncated to the minute

**Checkpoint**: all six stories are independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, samples, deployments, and checks that span stories.

- [X] T065 [P] Secret-leak test in `tests/PdfBuilder.Api.Tests/KeyLeakTests.cs`: issue a key, use it, use invalid keys, revoke it; capture all log output, every response body and header, and dump `clients`/`api_keys`; assert neither the client key, its secret, nor the admin key appears anywhere (SC-005, FR-008)
- [X] T066 [P] Burst test in `tests/PdfBuilder.Api.Tests/AuthenticationPipelineTests.cs`: 1 000 requests with invented keys interleaved with valid-key requests; valid requests all succeed and the store is read at most once per invented key id (SC-008)
- [X] T067 [P] Latency check in `tests/Blocks.ServiceDefaults.Tests/ApiKeyAuthenticationHandlerTests.cs`: with a warm cache, p95 of 1 000 handler authentications is well under 25 ms (SC-007)
- [X] T068 [P] Add the key to `samples/requests.http` (`X-Api-Key: {{apiKey}}`) and `samples/Invoke-PdfJob.ps1` (an `-ApiKey` parameter, defaulting to `$env:PDFBUILDER_API_KEY`); mention both in `samples/README.md`
- [X] T069 [P] Document `ADMIN_KEY_HASH` in `deploy/README.md` and the `Blocks__Admin__KeyHash` secret in `deploy/huggingface/README.md` (and any Space config under `deploy/huggingface/`)
- [X] T070 [P] Add `Blocks:Admin:KeyHash` to `docs/configuration.md` and to `docs/blocks/04-backend.md#configuration`; add `STORAGE_UNAVAILABLE`, `CLIENT_NOT_FOUND`, `KEY_NOT_FOUND` and `CLIENT_ID_TAKEN` to the stud code table there; record what was built in `docs/blocks/04-backend.md` (one `ApiKey` scheme now, cookie scheme with 004, CLI scope `admin-key` only) (Principle I gate)
- [X] T071 [P] Update `docs/operations.md`: issuing keys to existing products before upgrading, the refusal codes in the failure modes, rotating the admin key, the 60-second revocation bound
- [X] T072 [P] Update `docs/architecture.md` (authentication in the request flow; the `Blocks.*` packages and their dependency rule) and `docs/message-contracts.md` (HTTP needs a key; the bus is unchanged)
- [X] T073 [P] Update `README.md` (calling the API with `X-Api-Key`; generating the admin key with `dotnet run --project tools/Blocks.Cli -- admin-key`; the console needs 004) and `web/README.md` (the console is refused until console sign-in, feature 004)
- [X] T074 [P] Add a `CHANGELOG.md` service entry: breaking for keyless callers (FR-023, shipped with 004), new required `Blocks:Admin:KeyHash`, no message-contracts version change
- [X] T075 Run `dotnet build PdfBuilder.slnx` with warnings as errors, `dotnet test`, and `PDFBUILDER_RUN_DATABASE_TESTS=1 dotnet test`; `docker compose up --build` from a clean checkout followed by one admin call with the published local-only key (Definition of done); then walk quickstart.md Scenarios 1–8 against the API run as **Production** (`ASPNETCORE_ENVIRONMENT=Production`, so Development user-secrets do not connect to the real broker) — Done 2026-11-04 against local PostgreSQL 17: 384 passed, 13 browser tests skipped; quickstart Scenarios 1–6 and 8 on the Release binary as Production. Not done here: `docker compose up --build` (no Docker on this machine) and Scenario 7 against a stopped server (covered by the pipeline and store outage tests).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none
- **Foundational (Phase 2)**: depends on Setup; blocks every story
- **US1, US2, US3 (P1)**: depend on Foundational only
- **US4, US5 (P2)**: depend on Foundational; their end-to-end tests issue keys, so they use US1's endpoints
- **US6 (P3)**: depends on Foundational; its end-to-end test uses US1's key listing
- **Polish (Phase 9)**: after the stories it documents or tests (T065 needs US1 and US4; T066 needs US4's cache)

### User Story Dependencies

```text
Setup → Foundational ─┬─► US1 (issue keys) ─┬─► US4 (revoke/disable, cache)
                      │                     ├─► US5 (permission matrix)
                      │                     └─► US6 (last used)
                      ├─► US2 (permissions on endpoints, identity, log scope)
                      └─► US3 (refusals, outage)
```

- US2 and US3 can be built against keys inserted directly into the store in tests, so they need not wait for US1.
- US5's matrix (T057) assumes US2's `RequirePermission` calls (T042, T043) are in place.

### Within Each User Story

- Tests first; they should fail before the implementation
- Contracts/records → store methods → endpoints → `Program.cs` wiring
- Commit after each task or logical group

### Parallel Opportunities

- Phase 1: T003, T004, T008 alongside T001–T002
- Phase 2: T009–T015 are all separate files; T023–T026 in parallel once T020–T022 exist
- After Phase 2: US1, US2 and US3 can proceed in parallel (different files, except `Program.cs` edits T034/T041/T044, which should be made in sequence)
- Within each story, all `[P]` test tasks together, then `[P]` implementation tasks
- Phase 9: T065–T074 in parallel

---

## Parallel Example: User Story 1

```bash
# Tests together:
Task: "T028 Validation tests in tests/Blocks.ServiceDefaults.Tests/ClientAdminValidationTests.cs"
Task: "T029 Store tests in tests/PdfBuilder.Infrastructure.Tests/EfClientKeyStoreTests.cs"
Task: "T030 End-to-end tests in tests/PdfBuilder.Api.Tests/AdminApiTests.cs"

# Independent implementation pieces together:
Task: "T031 Request/response records in src/Blocks.ServiceDefaults/Clients/ClientAdminContracts.cs"
Task: "T035 admin-key command in tools/Blocks.Cli/Program.cs"
Task: "T036 samples/admin.http"
```

## Parallel Example: User Story 3

```bash
Task: "T046 Handler classification tests in tests/Blocks.ServiceDefaults.Tests/ApiKeyAuthenticationHandlerTests.cs"
Task: "T047 Pipeline refusal tests in tests/PdfBuilder.Api.Tests/AuthenticationPipelineTests.cs"
```

---

## Implementation Strategy

### MVP First (P1 stories)

1. Phase 1 Setup, Phase 2 Foundational
2. US1: the operator can issue keys
3. US2 + US3: keys are required, identified and refused uniformly
4. **Stop and validate** with quickstart Scenarios 1, 2, 3, 7 and 8

US1 alone is demonstrable, but the service is only safe to deploy once US2 and US3 are done, and per
FR-023 it ships together with feature 004 (console sign-in).

### Incremental Delivery

1. Setup + Foundational → keyless calls refused, admin key works
2. US1 → keys can be issued (demo)
3. US2 + US3 → MVP: protected, identified service
4. US4 → revocation and disabling within a minute
5. US5 → least-privilege keys proven by the matrix
6. US6 → last-used housekeeping
7. Polish → docs, samples, deployments, leak/burst/latency checks

---

## Notes

- [P] tasks touch different files and have no dependency on incomplete tasks
- `src/PdfBuilder.Api/Program.cs`, `src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs`, `src/Blocks.ServiceDefaults/Clients/ClientAdminEndpoints.cs` and `src/Blocks.ServiceDefaults.EntityFramework/EfClientKeyStore.cs` are edited by several stories; serialize those edits
- Never log, return (except in the issue-key response) or store a full key
- Run local verification as Production so Development user-secrets do not reach the real broker
