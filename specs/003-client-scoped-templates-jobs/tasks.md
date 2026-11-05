---

description: "Task list for feature 003: Client-Scoped Templates and Jobs"
---

# Tasks: Client-Scoped Templates and Jobs

**Input**: Design documents from `/specs/003-client-scoped-templates-jobs/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The constitution's definition of done requires a cross-client test for anything
that reads client data (client B gets a 404, not a 403). SC-001 to SC-004 can only be proven by tests,
and the plan's Project Structure names the test files. Database-backed and whole-pipeline tests are
opt-in with `PDFBUILDER_RUN_DATABASE_TESTS=1`, as in the rest of the suite (`PDFBUILDER_TEST_POSTGRES`
points them at an existing server).

**Organization**: Tasks are grouped by user story (US1–US5 from spec.md). Most of the mechanism is
shared (one filter in the `DbContext`), so it sits in Foundational. Each story phase then proves and
completes its own behaviour.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to
- Paths are relative to the repository root

## Path Conventions

- Shared stud package: `src/Blocks.ServiceDefaults/`
- Block projects: `src/PdfBuilder.Contracts/`, `src/PdfBuilder.Domain/`, `src/PdfBuilder.Application/`, `src/PdfBuilder.Infrastructure/`, `src/PdfBuilder.Api/`
- Tests: `tests/Blocks.ServiceDefaults.Tests/`, `tests/PdfBuilder.Application.Tests/`, `tests/PdfBuilder.Infrastructure.Tests/`, `tests/PdfBuilder.Api.Tests/`
- Rules for every task:
  - nothing under `src/Blocks.*` references a `PdfBuilder.*` assembly, and `PdfBuilder.Application` gains no reference to `Blocks.*` (Principle IX, research R1)
  - no PDF Builder code reads headers or claims for identity, only `ICurrentClient`; the consumer sets the client only through `IClientContext.ActAs` (Principle VI)
  - `IgnoreQueryFilters` appears only in `JobRetentionSweeper.DeleteExpiredJobsOfAllClientsAsync`, `ClientOwnerAssignment.AssignUnownedToDefaultClientAsync` and `EfPdfJobStore.ClaimAsync` (research R2)
  - another client's template or job answers exactly as a missing one, never 403 (Principle V)
  - when running anything locally, use `ASPNETCORE_ENVIRONMENT=Production` and the compose broker: Development loads user-secrets that point at the real hosted broker

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The new setting and the test support that every story uses.

- [X] T001 [P] Create `src/Blocks.ServiceDefaults/BlocksBusOptions.cs` with constant `Section = "Blocks:Bus"`, constant `DefaultClientIdKey = "Blocks:Bus:DefaultClientId"` and property `string? DefaultClientId`; bind it in `AddBlocksAuthentication` in `src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs` with a validator and `ValidateOnStart()`: when set, it must match the client id rule `^[a-z][a-z0-9-]{0,39}$` (reuse `HttpCurrentClient.IsClientId`), or start-up fails with a message naming `Blocks:Bus:DefaultClientId` and the environment variable `Blocks__Bus__DefaultClientId`; unset is valid (contracts/configuration.md, R7)
- [X] T002 [P] Create `tests/Blocks.ServiceDefaults.Tests/BlocksBusOptionsTests.cs`: unset passes; `ecommerce` passes; `Ecommerce`, `1abc`, `has space` and a 41-character id each fail with a message naming `Blocks:Bus:DefaultClientId`
- [X] T003 [P] Create `tests/PdfBuilder.Infrastructure.Tests/Support/FixedCurrentClient.cs`: an `ICurrentClient` whose `ClientId` can be set and changed between calls (`IsAdmin` false, no scopes, `KeyId` null), for constructing `PdfBuilderDbContext` in store tests

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Ownership columns, the `"client"` query filter, owner-on-insert, the two migrations with
the upgrade step between them, the client carried on the message, and the consumer acting as that
client. At the end of this phase every existing behaviour works for a caller that names a client, and
data is already separated.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Identity and contract

- [X] T004 [P] Create `src/Blocks.ServiceDefaults/Identity/IClientContext.cs` (`void ActAs(string clientId)`, documented as "for work that does not come from an HTTP request, such as a bus message; call it before any other work") and `src/Blocks.ServiceDefaults/Identity/CurrentClient.cs`: a scoped `ICurrentClient, IClientContext` that holds `HttpCurrentClient`. After `ActAs(id)` it answers `ClientId = id`, `KeyId = null`, `Scopes` empty and `IsAdmin = false`; before, it delegates every member to `HttpCurrentClient`. `ActAs` throws `ArgumentException` for an id failing `HttpCurrentClient.IsClientId`, and `InvalidOperationException` if called twice with different ids. Every member reads its state when called, never caches at construction (R1, R5)
- [X] T005 In `src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs`, replace `TryAddScoped<ICurrentClient, HttpCurrentClient>()` with: `HttpCurrentClient` scoped as itself; `CurrentClient` scoped; `ICurrentClient` and `IClientContext` both resolving the same scoped `CurrentClient` instance (depends on T004)
- [X] T006 [P] Extend `tests/Blocks.ServiceDefaults.Tests/CurrentClientTests.cs`: without `ActAs`, every existing assertion still holds through `ICurrentClient`; after `ActAs("erp")`, `ClientId == "erp"`, `IsAdmin == false` and `Scopes` empty, even for a request with the admin key; `ICurrentClient` and `IClientContext` resolved in one scope are the same instance; an object holding `ICurrentClient` resolved **before** `ActAs` sees `erp` afterwards; a malformed id throws; a second `ActAs` with another id throws
- [X] T007 [P] In `src/PdfBuilder.Contracts/IGeneratePdfRequest.cs` add `string? ClientId { get; }` with the doc comment from contracts/message-contract.md ("Null means the deployment's default client … Must match `^[a-z][a-z0-9-]{0,39}$`"); in `src/PdfBuilder.Contracts/GeneratePdfRequest.cs` add `public string? ClientId { get; set; }` with `<inheritdoc />`; add `CP0006` baseline suppressions for `P:PdfBuilder.Contracts.IGeneratePdfRequest.ClientId` for `lib/net10.0` and `lib/netstandard2.0` to `src/PdfBuilder.Contracts/CompatibilitySuppressions.xml`, copying the `PageSetup` entries; set `<Version>1.2.0-local</Version>` in `src/PdfBuilder.Contracts/PdfBuilder.Contracts.csproj` (R10)

### Schema and filter

- [X] T008 [P] Create `src/PdfBuilder.Infrastructure/Persistence/ClientRequiredException.cs` (message: "Choose a client: this operation needs a client to act as.") and `src/PdfBuilder.Infrastructure/Persistence/ClientScope.cs`, a scoped helper over `ICurrentClient` with `string RequireClientId()` that returns `ClientId` or throws `ClientRequiredException`; register `ClientScope` scoped in `src/PdfBuilder.Infrastructure/DependencyInjection.cs` (R3)
- [X] T009 [P] Add `public string ClientId { get; set; } = string.Empty;` to `src/PdfBuilder.Infrastructure/Persistence/Records/AuthoredTemplateRecord.cs` and change `From(AuthoredTemplate template)` to `From(AuthoredTemplate template, string clientId)`; add the same property to `src/PdfBuilder.Infrastructure/Persistence/Records/PdfJobRecord.cs`, change `From(PdfJob job, DateTimeOffset savedAt)` to `From(PdfJob job, string clientId, DateTimeOffset savedAt)`, and document that `CopyFrom` deliberately does **not** copy `ClientId` ("the owner never changes", FR-001)
- [X] T010 Configure ownership in `src/PdfBuilder.Infrastructure/Persistence/Configurations/AuthoredTemplateConfiguration.cs`: `ClientId` "`varchar(40) NOT NULL`" (`HasMaxLength(40)`, required); primary key `(ClientId, Key)`; replace the unique index on `key_lower` with a unique index on `(ClientId, key_lower)` named `ux_authored_templates_client_key_lower`; foreign key `client_id → clients(id)` with `DeleteBehavior.Restrict`. In `src/PdfBuilder.Infrastructure/Persistence/Configurations/PdfJobConfiguration.cs`: `ClientId` "`varchar(40) NOT NULL`", foreign key to `clients(id)` with `Restrict`, primary key unchanged (`job_id`, "job ids stay globally unique"). Name the clients entity type as `AddBlocksClients()` maps it (data-model.md) (depends on T009) *Changed:* the foreign key is declared through a new public `HasClientOwner` in `src/Blocks.ServiceDefaults.EntityFramework/ModelBuilderExtensions.cs`, because `ClientRecord` is internal to that package.
- [X] T011 In `src/PdfBuilder.Infrastructure/Persistence/PdfBuilderDbContext.cs`: take `ICurrentClient caller` as a second constructor parameter; add `private string? CallerClientId => caller.ClientId;`; declare the named query filter `"client"` on `AuthoredTemplateRecord` and `PdfJobRecord` as `r => r.ClientId == CallerClientId` (a context member, so EF parameterises it per query). Document that with no client nothing matches (fail closed, R1). In `src/PdfBuilder.Infrastructure/Persistence/PdfBuilderDbContextOptions.cs`, have `PdfBuilderDbContextFactory` pass an `ICurrentClient` with no client. Update every direct `new PdfBuilderDbContext(...)` in `tests/PdfBuilder.Infrastructure.Tests/EfPdfJobStoreTests.cs`, `tests/PdfBuilder.Infrastructure.Tests/Persistence/MigrationTests.cs` and `tests/PdfBuilder.Infrastructure.Tests/Persistence/PostgresFixture.cs` to pass a `FixedCurrentClient` (T003) (depends on T010)
- [X] T012 Generate the two migrations in `src/PdfBuilder.Infrastructure/Persistence/Migrations/` in this order: (1) temporarily make both `ClientId` columns nullable in the configurations, remove the new primary key, unique index and foreign keys, and run `dotnet ef migrations add AddClientOwner --project src/PdfBuilder.Infrastructure --startup-project src/PdfBuilder.Infrastructure`, which must only add `client_id varchar(40) NULL` to `authored_templates` and `pdf_jobs`; (2) restore the T010 configuration and run `dotnet ef migrations add RequireClientOwner`, which must set both columns `NOT NULL`, change `authored_templates`' primary key to `(client_id, key)`, swap `ux_authored_templates_key_lower` for `ux_authored_templates_client_key_lower` and add both foreign keys. Read both generated files and the snapshot and confirm nothing else changed (R6, data-model.md "Migrations") *Changed:* the startup project is `src/PdfBuilder.Api` (as in features 001 and 004). The generated `RequireClientOwner` had `defaultValue: ""` on both `NOT NULL` changes; it was removed, so a row with no owner fails the migration instead of being given an owner that means "unknown". EF also created `ix_pdf_jobs_client_id`, by convention for the foreign key; kept.

### Upgrade step (needed so any existing database still starts)

- [X] T013 Create `src/PdfBuilder.Infrastructure/Persistence/ClientOwnerAssignment.cs` (scoped; depends on `PdfBuilderDbContext`, `IClientKeyStore`, `IOptions<BlocksBusOptions>`, `TimeProvider`, logger), registered in `src/PdfBuilder.Infrastructure/DependencyInjection.cs`. It has: *Changed:* the client is registered with `INSERT … ON CONFLICT (id) DO NOTHING` in the caller's transaction, not through `IClientKeyStore`, so a concurrent registration cannot poison the transaction; `RequireOwnerAsync` and `AssignUnownedToDefaultClientAsync` take the `PdfBuilderDbContext` as a parameter. Row counts, not an `EXISTS`, are read so the log can report them.
  - `Task<string> RequireOwnerAsync(CancellationToken)`, memoised per instance. If `DefaultClientId` is unset, throw `InvalidOperationException` with exactly: "Templates or jobs from before clients existed need an owner. Set Blocks:Bus:DefaultClientId (environment variable Blocks__Bus__DefaultClientId) to the id of the client that uses them, then start again. Nothing has been changed." If it is set but `IClientKeyStore.GetClientAsync` finds nothing, call `AddClientAsync(new ClientInfo(id, id, now, null))` (a `false` result means another instance registered it, which is fine) and log `PersistenceLog.DefaultClientRegistered`. Return the id; a disabled client is used as is.
  - `Task AssignUnownedToDefaultClientAsync(CancellationToken)`, run in one transaction holding `pg_advisory_xact_lock` with its own constant (distinct from `TemplateImporter`'s). If neither table has a row with `client_id IS NULL` (raw SQL `EXISTS`), return without reading the setting. Otherwise call `RequireOwnerAsync`, then `UPDATE authored_templates SET client_id = @owner WHERE client_id IS NULL` and the same for `pdf_jobs`, and log `PersistenceLog.ClientOwnerAssigned(templates, jobs, owner)`.
  Add both log messages to `src/PdfBuilder.Infrastructure/Persistence/PersistenceLog.cs` (R6, R7, FR-016) (depends on T001, T012)
- [X] T014 In `src/PdfBuilder.Infrastructure/Templates/TemplateImporter.cs`, take `ClientOwnerAssignment` and, only when there is at least one template to import, call `RequireOwnerAsync` **before** writing anything, then write each row with `AuthoredTemplateRecord.From(template, owner)`. With no files, or only skipped ones, it never asks for the owner (a fresh install needs no setting) (depends on T009, T013)
- [X] T015 In `src/PdfBuilder.Infrastructure/Persistence/DatabaseStartup.cs`, when `MigrateOnStartup` is true **and** `RequireClientOwner` is among the pending migrations: `IMigrator.MigrateAsync("AddClientOwner")`, then the importer, then `ClientOwnerAssignment.AssignUnownedToDefaultClientAsync`, then `MigrateAsync()` to the latest. Otherwise keep today's order (migrate, then import). Comment why: migrating to a named target on a database already past it would roll migrations back (R6). The `MigrateOnStartup = false` branch is unchanged (depends on T012–T014) *Changed:* the trailing import call became an `else` branch, so the import runs once per start on every path.

### Writes and cross-client maintenance

- [X] T016 [P] In `src/PdfBuilder.Infrastructure/Templates/EfAuthoredTemplateStore.cs`, take `ClientScope` and write `AuthoredTemplateRecord.From(template, scope.RequireClientId())` in `AddAsync`; leave `UpdateAsync`, `DeleteAsync`, `GetAsync` and `ListAsync` as they are (the filter confines them), and add a remark to the class saying so (R3) (depends on T008, T011)
- [X] T017 [P] In `src/PdfBuilder.Infrastructure/Jobs/EfPdfJobStore.cs`, take `ClientScope` and build the record with `PdfJobRecord.From(job, scope.RequireClientId(), clock.GetUtcNow())` in `SaveAsync` (R3) (depends on T008, T011)
- [X] T018 [P] In `src/PdfBuilder.Infrastructure/Jobs/JobRetentionSweeper.cs`, move the delete into a method named `DeleteExpiredJobsOfAllClientsAsync` that uses `IgnoreQueryFilters(["client"])`, with a comment that the sweeper runs with no client and would otherwise delete nothing (R2) (depends on T011)
- [X] T019 [P] In `src/PdfBuilder.Api/Endpoints/ApiExceptionHandler.cs`, map `ClientRequiredException` to `400` with `code` `VALIDATION_FAILED` and `errors["X-Client-Id"] = ["Choose a client: send X-Client-Id with the client to act as."]`, matching the shape of the other validation problems (contracts/http-client-scoping.md) (depends on T008) *Changed:* the problem is built by a new public `ClientRequiredProblem` in `src/Blocks.ServiceDefaults/Problems/ClientRequiredProblem.cs` (`BlocksProblems` is internal), which T053 will reuse; `BlocksProblems.ClientRequired()` already exists.

### The client on the message

- [X] T020 In `src/PdfBuilder.Api/Endpoints/PdfJobEndpoints.cs`, inject `ICurrentClient` into `EnqueueAsync` and add `ClientId = caller.ClientId` to the published anonymous message, so the worker handles the job for the client that submitted it (R5.2) (depends on T007)
- [X] T021 In `src/PdfBuilder.Api/Messaging/GeneratePdfConsumer.cs`, inject `IClientContext` and `IOptions<BlocksBusOptions>`. As the first thing in `Consume`, resolve `clientId = message.ClientId ?? options.DefaultClientId`. If it is null, respond `IsSuccess = false`, `ErrorCode = PdfErrorCodes.ValidationFailed`, `ErrorMessage = "The message names no client and no default client is configured."`, record no job and return. Otherwise call `clientContext.ActAs(clientId)` before any store call, and add `["ClientId"] = clientId` to the existing log scope (FR-014 base case, FR-017). Extract the failure response into one private helper reused by the existing permanent-failure branch (depends on T001, T005, T007)

### Test support and guard rails

- [X] T022 Update `tests/PdfBuilder.Api.Tests/Support/ApiFactory.cs`: register two test clients, `alpha` and `beta`, at start-up (through `IClientKeyStore`); add `ClientFor(string clientId, params string[] scopes)` returning an `HttpClient` with a fresh key of that client (default: all three `pdf:*` scopes, via the existing `IssueKeyAsync`), and `AdminActingAs(string clientId)` returning `AdminClient()` with `X-Client-Id`. Move every existing template or job call in `tests/PdfBuilder.Api.Tests/*.cs` that used `AdminClient()` without `X-Client-Id` onto `AdminActingAs("alpha")`; make every message the consumer tests publish (`GeneratePdfConsumerPersistenceTests.cs`, `GeneratePdfConsumerRetryTests.cs`, `GeneratePdfRequestDeliveryTests.cs`, `JobAcceptanceOutboxTests.cs`) carry `ClientId = "alpha"` (R11) (depends on T020, T021) *Changed:* helpers are async (`ClientForAsync`, `AdminActingAsAsync`, `EnsureClientAsync`), and register the client when new, instead of registering `alpha` and `beta` at start-up: existing tests assert on the exact client list. Also `FixedCurrentClient` now implements `IClientContext`, and `TestDatabase` gained `CreateContext(clientId)` and `RegisterClientsAsync`, for tests that build their own service provider. Only `PermissionTests` and `ConsoleSessionApiTests` called template or job endpoints as the operator.
- [X] T023 [P] Extend `tests/Blocks.ServiceDefaults.Tests/ArchitectureTests.cs` with two rules, using source scanning or Roslyn the same way the file's existing rules do: (1) `IgnoreQueryFilters` occurs in `src/` only inside `DeleteExpiredJobsOfAllClientsAsync`, `AssignUnownedToDefaultClientAsync` and `ClaimAsync` (the last arrives in T040; list it now); (2) build `PdfBuilderDbContext`'s model with the design-time factory and assert every entity type with a `ClientId` property has a query filter named `"client"` (Principle III gate, R11) *Changed:* rule 2 is in `tests/PdfBuilder.Infrastructure.Tests/Persistence/ClientFilterModelTests.cs`: the stud's tests must not reference a block. It excludes `ApiKeyRecord`, which is read before any client is known.
- [X] T024 Update the existing Infrastructure store tests to the new constructors: `tests/PdfBuilder.Infrastructure.Tests/EfAuthoredTemplateStoreTests.cs`, `EfPdfJobStoreTests.cs`, `JobRetentionSweeperTests.cs`, `TemplateImporterTests.cs` and `MultiInstanceTests.cs` construct contexts and stores with a `FixedCurrentClient("alpha")` and register client `alpha` before writing (foreign key). `TemplateImporterTests` configures `Blocks:Bus:DefaultClientId = alpha` where it imports files (depends on T011–T018) *Changed:* also `MultiInstanceTests`, `Persistence/MigrationTests`, `EfClientKeyStoreTests`, `EfConsoleSessionStoreTests` and `DatabaseHealthCheckTests`, which build their own providers or insert rows and needed a caller or an owner.
- [X] T025 Checkpoint: `dotnet build PdfBuilder.slnx` and `dotnet test PdfBuilder.slnx`, then again with `PDFBUILDER_RUN_DATABASE_TESTS=1`; all existing tests green with the T022/T024 helper changes only, plus T002, T006 and T023

**Checkpoint**: data is owned and filtered, the message carries the client, and the service starts
on a fresh and on an existing database.

---

## Phase 3: User Story 1 - A client sees and changes only its own templates (Priority: P1) 🎯 MVP

**Goal**: Each client lists, reads, edits, publishes, duplicates and deletes only its own templates,
plus the shared built-in templates. The same key can exist once per client.

**Independent Test**: spec User Story 1: as `alpha` create `invoice`; as `beta` list (absent), and
read, edit, publish, duplicate and delete it (each answers exactly like a missing key); create
`invoice` as `beta` (succeeds); each client lists only its own `invoice` plus every built-in one.

### Tests for User Story 1

- [X] T026 [P] [US1] Extend `tests/PdfBuilder.Infrastructure.Tests/EfAuthoredTemplateStoreTests.cs` (database-backed): with `FixedCurrentClient` switched between `alpha` and `beta` on one context, `ListAsync` returns only the current client's rows; `GetAsync`, `UpdateAsync` (throws `TemplateNotFoundException`, not stale revision) and `DeleteAsync` (returns `false`) do nothing to the other client's template; `alpha` and `beta` can both `AddAsync` `invoice`; `alpha` adding `Invoice` after `invoice` throws `TemplateKeyTakenException`; with no client, `ListAsync` is empty and `AddAsync` throws `ClientRequiredException`
- [X] T027 [P] [US1] Create `tests/PdfBuilder.Api.Tests/CrossClientIsolationTests.cs` (pipeline) for templates: enumerate every endpoint under `/api/templates` from `EndpointDataSource` (fail if one has no case). For each, send as `beta` the request naming a key only `alpha` has, and the same request with a key no client has; assert identical status, headers (excluding `Date`) and body with `traceId` removed; then assert `alpha`'s template is unchanged (revision, draft, published). Covers SC-001, SC-002 and spec US1 scenario 2
- [X] T028 [P] [US1] Create `tests/PdfBuilder.Api.Tests/SameKeyPerClientTests.cs` (pipeline), templates part: `alpha` and `beta` each create `invoice` (`201` both), each list shows exactly one `invoice` with its own draft; `beta` creating `INVOICE` → `409`; either creating a built-in key → `409`; `GET /api/templates` for both includes every built-in template from `IBuiltInTemplateSource`; `beta` can duplicate a built-in template into its own and cannot duplicate `alpha`'s (`404`) (US1 scenarios 1, 3–6, SC-004)

### Implementation for User Story 1

- [X] T029 [US1] Review `src/PdfBuilder.Application/Features/Templates/TemplateCatalog.cs` and `TemplateCommands.cs` against the T026–T028 results and change nothing unless a test fails. Then update the class remarks of `TemplateCatalog` to say keys are unique **per client** (the store is filtered by client, see `PdfBuilderDbContext`) and that built-in templates are shared by every client (R9, FR-006, FR-007)
- [X] T030 [US1] Make T026–T028 pass. Any fix belongs in the Infrastructure store or the filter, not in Application code (R1); record a deviation here if one was needed

**Checkpoint**: templates are separated per client over HTTP.

---

## Phase 4: User Story 2 - A client's jobs are its own (Priority: P1)

**Goal**: A job renders the submitting client's published template, else the built-in one. Only its
owner can read it.

**Independent Test**: spec User Story 2: `alpha` publishes `receipt` and submits a job (renders
`alpha`'s); `beta` submits `receipt` (template not found); `beta` reads `alpha`'s job (not found);
`alpha` reads it (as today).

### Tests for User Story 2

- [X] T031 [P] [US2] Extend `tests/PdfBuilder.Infrastructure.Tests/EfPdfJobStoreTests.cs` (database-backed): a job saved as `alpha` is returned by `GetAsync` for `alpha` and `null` for `beta` and for no client; a later `SaveAsync` as `alpha` keeps `client_id = alpha` (read the row with SQL); `SaveAsync` with no client throws `ClientRequiredException`
- [X] T032 [P] [US2] Extend `tests/PdfBuilder.Infrastructure.Tests/JobRetentionSweeperTests.cs`: expired jobs of `alpha` and `beta` are both deleted by one sweep run with no client; unexpired jobs of both stay
- [X] T033 [P] [US2] Add jobs to `tests/PdfBuilder.Api.Tests/CrossClientIsolationTests.cs`: enumerate the `/api/pdf/jobs` endpoints; `beta`'s `GET /api/pdf/jobs/{alpha's job id}` equals `GET /api/pdf/jobs/{new Guid}` in status, headers and body with `traceId` removed (US2 scenario 3, SC-001)
- [X] T034 [P] [US2] Add jobs to `tests/PdfBuilder.Api.Tests/SameKeyPerClientTests.cs` (pipeline with the real consumer and renderer, or the existing fake renderer that records the template it was given): `alpha` and `beta` each publish `invoice` with distinguishable content and submit a job; each succeeds with its own template's content (SC-003). `beta` without its own template submits a built-in key: built-in rendered. `beta` submits a key only `alpha` has published: `TEMPLATE_NOT_FOUND` (US2 scenarios 1, 2) *Changed:* a `RecordingRenderer` (`tests/PdfBuilder.Api.Tests/Support/`) stands in for the browser so the test can say which HTML each job rendered, and `ApiFactory` gained `WaitForJobAsync` and `SubmitJobAsync`. Writing the first test that waits for a job to finish exposed that the test host had been publishing straight to the bus: `AddMassTransitTestHarness()` replaced the service's registration, outbox included, so a worker could start before its job row was saved (intermittent `pk_pdf_jobs` duplicate, job stuck `Pending`). Production uses the outbox (feature 001) and is not affected; the factory now declares the outbox again.
- [X] T035 [P] [US2] Extend `tests/PdfBuilder.Api.Tests/JobAcceptanceOutboxTests.cs`: the message published by `POST /api/pdf/jobs` as `beta` has `ClientId == "beta"`, and the saved job row has `client_id = beta` (US2 scenario 5, R5.2) *Changed:* written at the HTTP level (through `ApiFactory`, reading the consumed message from the harness and the row from SQL) rather than against the hand-built provider the rest of that class uses, because the claim is about what the endpoint publishes.

### Implementation for User Story 2

- [X] T036 [US2] Make T031–T035 pass. Expected changes are limited to the Infrastructure stores, the sweeper, `PdfJobEndpoints` and the consumer from Phase 2; record any other change here *Changed:* no change to production code was needed; the one fix was the test host's outbox, above.

**Checkpoint**: jobs are separated per client; US1 and US2 together are the MVP.

---

## Phase 5: User Story 3 - Bus messages are handled for the client that sent them (Priority: P2)

**Goal**: The full resolution table for messages (named, default, none, malformed, unknown,
disabled), and a reused job id of another client refused before rendering.

**Independent Test**: spec User Story 3 and contracts/message-contract.md: each row of the resolution
table gives the stated response, and a `CorrelationId` owned by another client gives
`VALIDATION_FAILED` without touching that job.

### Tests for User Story 3

- [X] T037 [P] [US3] Extend `tests/PdfBuilder.Application.Tests/ContractsTests.cs`: `PdfErrorCodes.ClientDisabled == "CLIENT_DISABLED"` and is in `All` and `IsKnown`; JSON for a 1.1.0-shaped `IGeneratePdfRequest` (no `ClientId`) deserialises through MassTransit's serializer with `ClientId == null`; `new GeneratePdfRequest(...) { ClientId = "erp" }` round-trips *Changed:* the wire-level check (a 1.1.0-shaped message arrives with `ClientId == null`; a class with `ClientId` set arrives intact) is in `tests/PdfBuilder.Api.Tests/GeneratePdfRequestDeliveryTests.cs`, because the Application tests have no MassTransit; `ContractsTests` covers the code catalogue and the class.
- [X] T038 [P] [US3] Create `tests/PdfBuilder.Api.Tests/GeneratePdfConsumerClientTests.cs` (MassTransit test harness, database-backed), one test per row of contracts/message-contract.md: named `beta` (job owned by `beta`, readable over HTTP by `beta` only); no `ClientId` with default `alpha` (owned by `alpha`); no `ClientId` and no default (`VALIDATION_FAILED`, "names no client", no row, renderer not called); malformed `ClientId` (`VALIDATION_FAILED`); unregistered `ClientId` (`VALIDATION_FAILED`, "is not registered"); default client unregistered (same); disabled `beta` (`CLIENT_DISABLED`); `CorrelationId` of an existing `alpha` job sent as `beta` (`VALIDATION_FAILED`, "already used by another job", `alpha`'s row unchanged byte for byte, renderer not called); `alpha` resending its own `CorrelationId` (behaves as today). Every captured log line of a handled message has `ClientId` (FR-017)
- [X] T039 [P] [US3] Extend `tests/PdfBuilder.Infrastructure.Tests/EfPdfJobStoreTests.cs` for `ClaimAsync`: free id → inserts `Pending` owned by the caller and returns it; own existing id → returns it unchanged, no insert; another client's id → throws `JobIdTakenException`, the other row unchanged; `SaveAsync` racing an insert by another client for the same id throws `JobIdTakenException` (not a database exception)

### Implementation for User Story 3

- [X] T040 [P] [US3] Create `src/PdfBuilder.Domain/Exceptions/JobIdTakenException.cs`: a `PdfBuilderException` with `ErrorCode = PdfErrorCodes.ValidationFailed`, `IsTransient = false`, message "CorrelationId is already used by another job; send a new one." (no detail about the other job); add `ClientDisabled = "CLIENT_DISABLED"` with a doc comment ("The client named by the message, or the default client, is disabled.") to `src/PdfBuilder.Contracts/PdfErrorCodes.cs` and include it in `All` (R8, R10)
- [X] T041 [US3] Add `Task<PdfJob> ClaimAsync(Guid jobId, string templateKey, CancellationToken cancellationToken = default)` to `src/PdfBuilder.Application/Abstractions/IPdfJobStore.cs`, documented as "the caller's job with this id, or a new Pending one recorded for the caller; throws `JobIdTakenException` when another client owns the id". Implement it in `src/PdfBuilder.Infrastructure/Jobs/EfPdfJobStore.cs`: filtered read, then insert `PdfJob.Enqueued(jobId, templateKey, now)` with the caller as owner; on unique violation, detach and check with `IgnoreQueryFilters(["client"])` whether the id exists (selecting only the id); if so throw `JobIdTakenException`, otherwise rethrow. In `SaveAsync`'s duplicate-key branch, throw `JobIdTakenException` when the filtered `FindAsync` finds no winner, instead of `throw;` (R8) (depends on T040) *Changed:* `ClaimAsync` needs no `IgnoreQueryFilters`: a duplicate-key error proves the id exists, and if the caller-filtered read cannot see it, it is another client's. So it reads nothing across clients; the architecture test's allow-list still names it, harmlessly. The caller's own job is looked up without the retention cut-off, so a job being worked on again is not mistaken for someone else's.
- [X] T042 [US3] Complete client resolution in `src/PdfBuilder.Api/Messaging/GeneratePdfConsumer.cs` per the table in contracts/message-contract.md, after T021's null check and before `ActAs`: a malformed id → `VALIDATION_FAILED` "ClientId is not a valid client id."; `IClientKeyStore.GetClientAsync` null → `VALIDATION_FAILED` "Client '<id>' is not registered."; disabled → `PdfErrorCodes.ClientDisabled` with message "Client '<id>' is disabled.". Each sends the failure response with no job recorded and no render. `StorageUnavailableException` from the lookup is left to bubble, so the message is retried. Then call `jobs.ClaimAsync(message.CorrelationId, message.TemplateKey)` before `sender.Send`; `JobIdTakenException` flows into the existing permanent-failure branch, which must **not** call `CompleteAsync` for it (the id is not the caller's). Log client refusals with a new `ConsumerLog.ClientRefused(clientId, code)` in `src/PdfBuilder.Api/Messaging/ConsumerLog.cs` (depends on T041) *Changed:* a public `ClientIds` (`src/Blocks.ServiceDefaults/Identity/ClientIds.cs`) holds the client id rule, which `HttpCurrentClient` now uses too. A malformed id is kept out of the log scope as well as the log message.
- [X] T043 [US3] Update `docs/message-contracts.md`: `ClientId` under `IGeneratePdfRequest` (optional, null → default client, the id rule); the resolution table; `CLIENT_DISABLED` under "Error codes"; that a job sent on the bus is recorded as `Pending` when work starts and can be polled with that client's key; a "Trust" paragraph: anyone with broker credentials for the block's vhost can name any client, so give them only to trusted product backends, each with its own broker user (FR-018, R10)
- [X] T044 [US3] Add a `## [Unreleased] — 1.2.0` section to `CHANGELOG.md` above 1.1.0: Added `IGeneratePdfRequest.ClientId` / `GeneratePdfRequest.ClientId`, and `PdfErrorCodes.ClientDisabled`; a note that 1.0.0 and 1.1.0 publishers keep working when the deployment sets `Blocks:Bus:DefaultClientId`, and that without it their messages are refused with `VALIDATION_FAILED`

**Checkpoint**: bus messages are handled per client, with every refusal answered.

---

## Phase 6: User Story 4 - Existing templates and jobs keep working after the upgrade (Priority: P2)

**Goal**: A database from before this feature, with templates and jobs, is upgraded only when the
default client is set, gives every row that owner (registering it if needed), and never does it twice.

**Independent Test**: spec User Story 4: rows from the previous schema; no setting → start fails with
the exact message and nothing changes; setting `legacy` (unregistered) → `legacy` registered and owns
everything; restart with another value → nothing moves.

### Tests for User Story 4

- [X] T045 [P] [US4] Create `tests/PdfBuilder.Infrastructure.Tests/Persistence/ClientOwnerUpgradeTests.cs` (database-backed). Seed by migrating a fresh database to `AddConsoleSessions` (the last migration before this feature) and inserting two authored templates (one published) and one job with SQL. Then run `DatabaseStartup`: *Changed:* the schema before this feature is built by migrating an empty database to `AddConsoleSessions` with `IMigrator`, and `DatabaseStartup` is driven through the service provider the real host builds (`AddInfrastructure`).
  - no `Blocks:Bus:DefaultClientId` → `InvalidOperationException` with the exact message from T013; the database is still at `AddClientOwner` at most, with every `client_id` `NULL` and content, revision and timestamps unchanged
  - `legacy`, unregistered → starts; `clients` has `legacy` named `legacy`; every row has `client_id = legacy`; `RequireClientOwner` is applied; template content and revision are byte-identical
  - already registered, disabled `legacy` → assigned to it, not re-registered
  - two `DatabaseStartup` instances started together on the seeded database → both succeed, one registration, every row owned once
  - a second start with `DefaultClientId = other` → nothing changes
  - a fresh empty database with no setting → starts
- [X] T046 [P] [US4] Extend `tests/PdfBuilder.Infrastructure.Tests/TemplateImporterTests.cs`: legacy template files with `DefaultClientId = legacy` → imported rows owned by `legacy` and `legacy` registered; legacy files and no setting → the T013 exception and no rows or import record written; a directory with only built-in-key files (all skipped) and no setting → completes without asking for an owner
- [X] T047 [P] [US4] Extend `tests/PdfBuilder.Infrastructure.Tests/Persistence/MigrationTests.cs`: migrating up from `AddConsoleSessions` through `AddClientOwner` and `RequireClientOwner` on an empty database succeeds; `RequireClientOwner` on a database with an unowned row fails (the database guarantees an owner); the model snapshot matches the context (no pending model changes)
- [X] T048 [P] [US4] Create `tests/PdfBuilder.Api.Tests/UpgradeRenderTests.cs` (pipeline): seed templates as in T045, start the API with `DefaultClientId = legacy`, render each template as `legacy` and compare with a render of the same content through `POST /api/templates/preview` taken before the upgrade step, expecting identical HTML (SC-005). `beta` does not see them *Changed:* `ApiFactory.StartOn` gained optional `settings` and `configure` parameters, so the service can be started over the seeded database with `Blocks:Bus:DefaultClientId`. The comparison is of the stored content read back through the API and of what a job renders, rather than against a preview taken before the upgrade: the preview endpoint reads nothing stored.

### Implementation for User Story 4

- [X] T049 [US4] Make T045–T048 pass; fixes go in `ClientOwnerAssignment`, `TemplateImporter` or `DatabaseStartup` (T013–T015) *Changed:* no fix was needed.
- [X] T050 [US4] Update `docs/operations.md` with a section "Upgrading to per-client templates and jobs": choose the client id of the product that uses today's templates; set `Blocks:Bus:DefaultClientId` / `Blocks__Bus__DefaultClientId`; what happens when it is unset (the exact message, nothing changed) and when the client is not registered (registered with its id as its name; rename it in the console); it happens once. For `Database:MigrateOnStartup = false`, the sequence and the SQL from contracts/configuration.md, with column names checked against the T012 migrations

**Checkpoint**: existing deployments upgrade with one setting.

---

## Phase 7: User Story 5 - The operator works inside one client at a time (Priority: P3)

**Goal**: The operator's template and job calls without a client are refused with a clear message;
an unknown client is not found; a disabled client still works.

**Independent Test**: spec User Story 5: admin key without `X-Client-Id` → `400`; `X-Client-Id:
nobody` → `404 CLIENT_NOT_FOUND`; `X-Client-Id: alpha` → exactly what `alpha`'s key gets; disabled
`beta` → works.

### Tests for User Story 5

- [X] T051 [P] [US5] Create `tests/Blocks.ServiceDefaults.Tests/ActingClientEndpointFilterTests.cs` (`StudHost` with `InMemoryClientKeyStore`): a client key caller passes without any store call; the operator with no client → `400`, `code` `VALIDATION_FAILED`, `errors["X-Client-Id"]` with "Choose a client: send X-Client-Id with the client to act as."; the operator acting as an unregistered id → `404 CLIENT_NOT_FOUND`; acting as a disabled client → passes; a store outage → `503 STORAGE_UNAVAILABLE`, as other admin calls answer *Changed:* the stud's test host (`StudHost`) gained a `/acting` endpoint that uses `RequireActingClient()`, and `InMemoryClientKeyStore` a `ClientReads` counter, to prove a client key costs no lookup.
- [X] T052 [P] [US5] Create `tests/PdfBuilder.Api.Tests/ActingClientTests.cs` (pipeline): for every endpoint under `/api/templates` and `/api/pdf/jobs` (from `EndpointDataSource`), admin key with no `X-Client-Id` → `400`, `X-Client-Id: nobody` → `404 CLIENT_NOT_FOUND`; the same through a console session cookie (with `X-Console: 1` on changes); `AdminActingAs("alpha")` gets exactly the list `ClientFor("alpha")` gets and a template it creates is `alpha`'s; with `beta` disabled, `AdminActingAs("beta")` can list and edit its templates while `beta`'s own key gets `CLIENT_DISABLED`; `/api/admin/*`, `/api/console/session`, `/files/{fileId}` and `/health/*` are unaffected by a missing `X-Client-Id`

### Implementation for User Story 5

- [X] T053 [US5] Add `ClientRequired()` to `src/Blocks.ServiceDefaults/Problems/BlocksProblems.cs` (400, `VALIDATION_FAILED`, `errors["X-Client-Id"] = ["Choose a client: send X-Client-Id with the client to act as."]`), and point `ApiExceptionHandler`'s `ClientRequiredException` mapping (T019) at it so both refusals are identical *Changed:* done with T019: `ClientRequiredProblem` (public) builds the problem, `BlocksProblems.ClientRequired()` delegates to it, and `ApiExceptionHandler` uses the same one.
- [X] T054 [US5] Create `src/Blocks.ServiceDefaults/Identity/ActingClientEndpointFilter.cs` and the extension `RequireActingClient(this RouteGroupBuilder)` (public, in `BlocksAuthExtensions.cs` or beside the filter). Only for `ICurrentClient.IsAdmin`: `ClientId` null → `BlocksProblems.ClientRequired()`; `IClientKeyStore.GetClientAsync(ClientId)` null → `BlocksProblems.ClientNotFound()`; otherwise continue (disabled allowed). Non-admin callers pass without a store call (R4, contracts/http-client-scoping.md) (depends on T053) *Changed:* a storage outage while checking the chosen client answers `503 STORAGE_UNAVAILABLE` with `Retry-After`, like other store reads, never "client not found"; the filter does this itself rather than reusing `StorageUnavailableFilter`, whose log message is about management calls.
- [X] T055 [US5] Apply `.RequireActingClient()` to the `/api/templates` group in `src/PdfBuilder.Api/Endpoints/TemplateEndpoints.cs` and the `/api/pdf/jobs` group in `src/PdfBuilder.Api/Endpoints/PdfJobEndpoints.cs`, after the permission requirement (depends on T054)

**Checkpoint**: every user story works on its own.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: configuration, deployment, documentation (Principle I gate and definition of done), and
the full validation run.

- [X] T056 [P] Set `Blocks__Bus__DefaultClientId: ecommerce` on the `api` service in `compose.yaml`, with a comment that `samples/admin.http` registers `ecommerce` and that bus messages without a client go to it; add `Blocks__Bus__DefaultClientId: ${DEFAULT_CLIENT_ID:-}` to `deploy/compose.prod.yaml` and document `DEFAULT_CLIENT_ID` (and the upgrade step) in `deploy/README.md`
- [X] T057 [P] Add `Blocks:Bus:DefaultClientId` to `docs/configuration.md` (new "Bus" section: default unset, the id rule, both uses, what unset and unregistered mean) and to the configuration table in `docs/blocks/04-backend.md` (Principle I gate)
- [X] T058 [P] Update `docs/blocks/04-backend.md` "Client scoping" and "Persistence" with what was built: the named `"client"` filter in `PdfBuilderDbContext` reading `ICurrentClient` per query; the three allowed `IgnoreQueryFilters` methods; two migrations around the startup assignment (not one); the default client registered during the upgrade; `.RequireActingClient()` and `IClientContext.ActAs`; `ClaimAsync` and globally unique job ids; `FileUrlExpiresAt` left to 005. Update "Acting as a client" (existence now checked by `.RequireActingClient()`) *Changed:* also corrected the contract-changes table: `FileUrlExpiresAt` is feature 005, not 1.2.0.
- [X] T059 [P] Update `docs/architecture.md`: remove "the data is not yet separated per client (feature 003)"; describe the filter in the request flow and `ActAs` in the message flow
- [X] T060 [P] Update `samples/requests.http` and `samples/README.md` with a two-client walk-through (quickstart §2) and a note that templates and jobs are per client; update `README.md` where it describes templates as shared *Changed:* also `README.md` (templates and jobs are per client; the upgrade setting) and `docs/templates.md` (whose "Stored in" row still said JSON files on the data volume, stale since feature 001; keys are now unique within a client).
- [X] T061 [P] Add a `CHANGELOG.md` service entry: templates and jobs belong to the client that created them; another client's answer is a 404, identical to a missing one; built-in templates shared; the operator must choose a client (`400` without `X-Client-Id`, `404 CLIENT_NOT_FOUND` for an unknown one); the new setting and the one-step upgrade; bus jobs recorded as `Pending` when work starts; ships in milestone M1 with 001, 002 and 004 *Changed:* also the existing "Note for implementers of `IGeneratePdfRequest`", which now covers `ClientId` as well as `PageSetup`.
- [X] T062 Run `dotnet build PdfBuilder.slnx` and `dotnet test PdfBuilder.slnx`, then with `PDFBUILDER_RUN_DATABASE_TESTS=1`; run `npm run lint`, `npm test -- --watch=false` and `npm run build` in `web/` to confirm the console is unaffected; all green with no new suppressions (Principle IX gate) and package validation passing for Contracts 1.2.0 (Principle VII gate) *Changed:* results: 0 warnings, 0 errors; 554 tests, 0 failed (13 skipped: the opt-in browser tests); `dotnet pack` of the contracts package passes package validation against 1.0.0, and fails with `CP0006` when the new suppressions are removed (checked, then restored). `npm run lint` and `npm run build` pass. `npm test` failed 2 of 185 tests on its first run, which took 37 s against about 4.5 s normally (right after the .NET run), and passed 3 times in a row afterwards; nothing under `web/` was changed.
- [ ] T063 Run `docker compose up --build` from a clean checkout and walk [quickstart.md](quickstart.md) §2–§5, recording any deviation in this file *Not done:* Docker is not installed on this machine (as for feature 004's T089), so `docker compose up --build` could not be run. The same flows (quickstart §2-§5: two clients, the operator, the bus, the upgrade) are covered in-process by `CrossClientIsolationTests`, `SameKeyPerClientTests`, `ActingClientTests`, `GeneratePdfConsumerClientTests`, `ClientOwnerUpgradeTests` and `UpgradeRenderTests`, against a real PostgreSQL and the real `Program`, with only the broker and the browser replaced. CI builds the image; walk the quickstart by hand there.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks every story. T025 must be green before any story starts.
- **US1 (Phase 3)** and **US2 (Phase 4)**: after Foundational; independent of each other.
- **US3 (Phase 5)**: after Foundational. T042 builds on T021; independent of US1/US2.
- **US4 (Phase 6)**: after Foundational (the mechanism is T013–T015); independent of the others.
- **US5 (Phase 7)**: after Foundational. Its tests use `AdminActingAs` from T022.
- **Polish (Phase 8)**: after the stories it documents; T062–T063 last.

### Within Each User Story

- Tests first; they should fail where behaviour is new (US3, US5), and may already pass where Foundational delivered it (US1, US2, US4). That is expected, and the implementation task then only fixes what fails.
- Domain/contract types before stores, stores before the consumer and endpoints.

### Parallel Opportunities

- Setup: T001–T003 together.
- Foundational: T004, T007, T008, T009 together; then T016–T019 together after T011; T023 alongside.
- Once T025 is green: US1, US2, US3, US4 and US5 can proceed in parallel. They touch different files, except `CrossClientIsolationTests.cs` and `SameKeyPerClientTests.cs` (US1 creates them, US2 extends them) and `EfPdfJobStoreTests.cs` (US2 and US3 extend it).
- Polish: T056–T061 together.

---

## Parallel Example: User Story 1

```text
Task: "T026 [US1] per-client store tests in tests/PdfBuilder.Infrastructure.Tests/EfAuthoredTemplateStoreTests.cs"
Task: "T027 [US1] cross-client template isolation in tests/PdfBuilder.Api.Tests/CrossClientIsolationTests.cs"
Task: "T028 [US1] same key per client in tests/PdfBuilder.Api.Tests/SameKeyPerClientTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T037 [US3] contract tests in tests/PdfBuilder.Application.Tests/ContractsTests.cs"
Task: "T038 [US3] consumer resolution table in tests/PdfBuilder.Api.Tests/GeneratePdfConsumerClientTests.cs"
Task: "T039 [US3] ClaimAsync tests in tests/PdfBuilder.Infrastructure.Tests/EfPdfJobStoreTests.cs"
Task: "T040 [US3] JobIdTakenException and PdfErrorCodes.ClientDisabled"
```

---

## Implementation Strategy

### MVP First (User Stories 1 and 2)

1. Phase 1 and Phase 2. At T025 the data is already separated and every existing test passes with
   the client named.
2. Phase 3 (templates) and Phase 4 (jobs): the cross-client tests prove SC-001 to SC-004.
3. **Stop and validate**: quickstart §2. This is the part that makes giving a second product a key safe.

### Incremental Delivery

1. MVP (US1 + US2).
2. US3: bus publishers get the full resolution table and the `CorrelationId` guard.
3. US4: upgrade tests and the operations guide. The mechanism already runs from Phase 2.
4. US5: clear refusals for the operator. Until then, an operator call without a client sees an empty
   list or gets the store's `400`, never another client's data.
5. Polish. Milestone M1 (001–004) is then complete.

---

## Notes

- `[P]` = different files, no dependencies on incomplete tasks.
- Do not change `PdfBuilder.Application` beyond `IPdfJobStore.ClaimAsync` (T041) and the remarks in
  T029; per-client behaviour comes from the filtered store (R1).
- The `IgnoreQueryFilters` allow-list in T023 lists `ClaimAsync` before T041 creates it; that is
  intentional, so the rule does not need to change later.
- Commit after each task or logical group; stop at any checkpoint to validate a story on its own.
