---

description: "Task list for 001 Persistent Storage for Jobs and Authored Templates"
---

# Tasks: Persistent Storage for Jobs and Authored Templates

**Input**: Design documents from `/specs/001-persistent-storage/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

> **⚠️ Before T001: merge PR #2 (Contracts 1.1.0) and rebase this branch onto `master`.** This
> branch was cut from `master` before #2 merged, and this plan was written against #2's code:
> `PdfJobEndpoints.EnqueueAsync` (page setup), `GeneratePdfConsumer` (last retry marks the job
> `Failed`), `tests/PdfBuilder.Application.Tests/ContractsTests.cs` and
> `tests/PdfBuilder.Api.Tests/GeneratePdfConsumerRetryTests.cs` exist only there. Implementing first
> would mean conflicts in exactly the files T025 and T026 change.

**Tests**: Included. The spec does not ask for them, but the constitution's Definition of Done does:
behaviour tested at the layer where the rule lives. Database-backed tests skip unless
`PDFBUILDER_RUN_DATABASE_TESTS=1`, using `Assert.SkipWhen` exactly as
`tests/PdfBuilder.Infrastructure.Tests/PlaywrightPdfRendererTests.cs` does for
`PDFBUILDER_RUN_BROWSER_TESTS`.

**Organization**: Tasks are grouped by user story so each can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US4, mapping to the user stories in spec.md

## Path Conventions

Existing layered .NET solution: `src/PdfBuilder.*`, `tests/PdfBuilder.*.Tests`. Package versions go
in `Directory.Packages.props` (central package management); `.csproj` files reference by name only.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Packages, local database and CI wiring.

- [X] T001 Add pinned package versions to `Directory.Packages.props`: `Microsoft.EntityFrameworkCore` 10.0.12, `Microsoft.EntityFrameworkCore.Relational` 10.0.12, `Microsoft.EntityFrameworkCore.Design` 10.0.12, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `EFCore.NamingConventions` 10.0.1, `MassTransit.EntityFrameworkCore` 8.5.11 (same version as `MassTransit.RabbitMQ`; keep the existing comment about the 8.5.x licence pin), `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.12, and under Testing `Testcontainers.PostgreSql` 4.16.0
- [X] T002 Reference `Microsoft.EntityFrameworkCore.Relational`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`, `MassTransit.EntityFrameworkCore` and `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` in `src/PdfBuilder.Infrastructure/PdfBuilder.Infrastructure.csproj`, and `Microsoft.EntityFrameworkCore.Design` (with `PrivateAssets="all"`) in `src/PdfBuilder.Api/PdfBuilder.Api.csproj` so `dotnet ef` can run against the API as startup project
- [X] T003 [P] Reference `Testcontainers.PostgreSql` in `tests/PdfBuilder.Infrastructure.Tests/PdfBuilder.Infrastructure.Tests.csproj` and `tests/PdfBuilder.Api.Tests/PdfBuilder.Api.Tests.csproj`
- [X] T004 [P] Add a `postgres` service to `compose.yaml`: image `postgres:17-alpine`, `POSTGRES_DB=pdf_builder`, user and password `pdf_builder` for local use only, named volume `postgres-data`, health check `pg_isready -U pdf_builder`; make the API service `depends_on` it with `condition: service_healthy` and set `ConnectionStrings__Database=Host=postgres;Database=pdf_builder;Username=pdf_builder;Password=pdf_builder`
- [X] T005 [P] In `.github/workflows/ci.yml`, set `PDFBUILDER_RUN_DATABASE_TESTS: "1"` beside `PDFBUILDER_RUN_BROWSER_TESTS` in the Test step env, and extend the existing comment to say the database tests start PostgreSQL in Docker through Testcontainers

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The database, its schema and its start-up, shared by every story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T006 Create `src/PdfBuilder.Infrastructure/Persistence/DatabaseOptions.cs`: section `Blocks:Database`, `bool MigrateOnStartup` default `true`, with an XML summary explaining when to turn it off (migrations applied by a separate deployment step)
- [X] T007 [P] Create persistence records in `src/PdfBuilder.Infrastructure/Persistence/Records/`: `PdfJobRecord.cs` (`JobId` Guid, `TemplateKey`, `Status` string, `EnqueuedAt`, `CompletedAt?`, `FileId?`, `FileUrl?`, `SizeBytes?`, `ErrorCode?`, `ErrorMessage?`, `LastSavedAt`) with `From(PdfJob, DateTimeOffset savedAt)` and `ToDomain()`; `TemplateContentRecord.cs` (`Html`, `Css`, `SampleJson`, `Options`) with `From(TemplateContent)` / `ToDomain()`; `AuthoredTemplateRecord.cs` (`Key`, `Draft` TemplateContentRecord, `Published?` TemplateContentRecord, `Revision`, `CreatedAt`, `UpdatedAt`, `PublishedAt?`) with `From(AuthoredTemplate)` and `ToDomain()` via `AuthoredTemplate.Restore`; `TemplateImportRecord.cs` (`Id` short, `CompletedAt`, `SourcePath`, `ImportedCount`, `Skipped` list of `{ File, Reason }`). `PdfJob` has a private constructor: rebuild it with `PdfJob.Enqueued` followed by `Succeed`/`Fail` according to `Status`, so the domain record is never mapped directly
- [X] T008 [P] Create `IEntityTypeConfiguration` classes in `src/PdfBuilder.Infrastructure/Persistence/Configurations/` matching `specs/001-persistent-storage/data-model.md` verbatim: `pdf_jobs` with primary key `job_id`, `template_key` "≤ 64 chars", a check constraint on `status` allowing exactly `Pending`, `Succeeded`, `Failed`, and index `ix_pdf_jobs_last_saved_at` on `last_saved_at`; `authored_templates` with primary key `key`, `draft` `jsonb` not null and `published` `jsonb` null (map `TemplateContentRecord` with EF Core's JSON column mapping), `revision` integer, and unique index `ux_authored_templates_key_lower` on `lower(key)`; `template_imports` with primary key `id`, check constraint `id = 1`, and `skipped` as `jsonb`
- [X] T009 Create `src/PdfBuilder.Infrastructure/Persistence/PdfBuilderDbContext.cs` with `DbSet`s for the three records, `ApplyConfigurationsFromAssembly`, and MassTransit's outbox entities (`modelBuilder.AddInboxStateEntity()`, `AddOutboxMessageEntity()`, `AddOutboxStateEntity()`); enable snake_case naming with `UseSnakeCaseNamingConvention()` where the context is registered (T011) (depends on T007, T008)
- [X] T010 [P] Create `src/PdfBuilder.Infrastructure/Persistence/StorageFailures.cs`, a helper that recognises database connectivity failures anywhere in an exception chain (`NpgsqlException` where `IsTransient` is true, `SocketException`, `TimeoutException`) and holds the `STORAGE_UNAVAILABLE` code. *Done as a helper only, with no wrapper exception: the consumer already treats any exception that is not a `PdfBuilderException` as transient and retries it, and `ApiExceptionHandler` recognises the raw exception, so a wrapper would add a type nothing needs.*
- [X] T011 Register persistence in `src/PdfBuilder.Infrastructure/DependencyInjection.cs`: validated `DatabaseOptions`; `AddDbContext<PdfBuilderDbContext>` with `UseNpgsql(configuration.GetConnectionString("Database"))` and `UseSnakeCaseNamingConvention()`; fail start-up with an `OptionsValidationException`-style message naming `ConnectionStrings:Database` when the value is missing or empty; add `AddDbContextCheck<PdfBuilderDbContext>("database", tags: ["ready"])` beside the existing `template-store` check (depends on T006, T009)
- [X] T012 Change `TemplateCatalog` and `PublishedTemplateRepository` from `AddSingleton` to `AddScoped` in `src/PdfBuilder.Application/DependencyInjection.cs` (research R8: they depend on the soon-scoped `IAuthoredTemplateStore`); check that nothing singleton resolves them
- [X] T013 Generate the initial migration `InitialPersistence` into `src/PdfBuilder.Infrastructure/Persistence/Migrations/` with `dotnet ef migrations add InitialPersistence --project src/PdfBuilder.Infrastructure --startup-project src/PdfBuilder.Api --output-dir Persistence/Migrations`; review that it creates `pdf_jobs`, `authored_templates`, `template_imports`, `inbox_state`, `outbox_message`, `outbox_state`, both check constraints and both indexes, and nothing else (depends on T009, T011)
- [X] T014 Create `src/PdfBuilder.Infrastructure/Persistence/DatabaseStartup.cs`, an `IHostedService` registered in `AddInfrastructure` **before** the web server starts (hosted services registered there start before Kestrel listens): when `MigrateOnStartup` is true, call `Database.MigrateAsync()` and log the applied migration names at Information; when false, fail start-up if `GetPendingMigrationsAsync()` is not empty, naming the pending migrations. Leave a clearly named extension point for the import step added in US3 (depends on T011, T013)
- [X] T015 Map storage failures in `src/PdfBuilder.Api/Endpoints/ApiExceptionHandler.cs`: `StorageUnavailableException` and recognised connectivity failures (T010) → `503 Service Unavailable` problem details, title "Storage unavailable", `Extensions["code"] = "STORAGE_UNAVAILABLE"`, and a `Retry-After: 5` header; existing mappings untouched (depends on T010)
- [X] T016 [P] Create `tests/PdfBuilder.Infrastructure.Tests/Persistence/PostgresFixture.cs` (namespace `PdfBuilder.TestSupport`, linked into `tests/PdfBuilder.Api.Tests` too): an xUnit **assembly** fixture that starts a `PostgreSqlContainer` (image `postgres:17-alpine`) or, when `PDFBUILDER_TEST_POSTGRES` names a server, uses that one (so developers without Docker can run the tests); applies migrations once to a template database and gives every test its **own copy** (`CreateDatabaseAsync`, dropped on dispose), so tests are isolated and parallel-safe, and a new `PdfBuilderDbContext` per call simulates a restart; tests using it call `Assert.SkipWhen(Environment.GetEnvironmentVariable("PDFBUILDER_RUN_DATABASE_TESTS") != "1", "Set PDFBUILDER_RUN_DATABASE_TESTS=1 to run tests that start PostgreSQL in Docker.")`
- [X] T017 [P] Add `tests/PdfBuilder.Api.Tests/ApiExceptionHandlerTests.cs` (no database): a `StorageUnavailableException` produces status 503, `code` `STORAGE_UNAVAILABLE`, a `Retry-After` header of `5`, and no connection string in the body; an existing mapping (for example `TemplateNotFoundException` → 404) is unchanged
- [X] T018 Add a migration test in `tests/PdfBuilder.Infrastructure.Tests/Persistence/MigrationTests.cs` (database-backed): migrating an empty database succeeds, `GetPendingMigrationsAsync()` is then empty, and the model has no pending changes (`dotnet ef migrations has-pending-model-changes` equivalent via `Database.HasPendingModelChanges()`) (depends on T013, T016)

**Checkpoint**: the service starts against an empty database, creates the schema, reports ready, and `dotnet test` passes with and without `PDFBUILDER_RUN_DATABASE_TESTS=1`.

---

## Phase 3: User Story 1 - Job status survives a restart (Priority: P1) 🎯 MVP

**Goal**: job status is stored in the database, shared by instances, kept for exactly the same retention as today, and a job is accepted only if it is both recorded and queued.

**Independent Test**: quickstart Scenario 1 (submit, restart, poll → same status) and Scenario 6 (retention unchanged).

### Tests for User Story 1

- [X] T019 [P] [US1] Create `tests/PdfBuilder.Infrastructure.Tests/EfPdfJobStoreTests.cs` (database-backed, replacing `InMemoryPdfJobStoreTests.cs`): a saved `Pending`, `Succeeded` and `Failed` job each read back field-for-field identical from a **new** DbContext; saving again upserts by `JobId`; an unknown id returns `null`; with `RetentionHours = 1` and a fake `TimeProvider`, a job last saved 59 minutes ago is returned and one saved 61 minutes ago is `null` (parity with the in-memory store's "last save + retention" expiry)
- [X] T020 [P] [US1] Create `tests/PdfBuilder.Infrastructure.Tests/JobRetentionSweeperTests.cs` (database-backed): one sweep deletes rows with `last_saved_at` older than `RetentionHours` and keeps the rest, and logs the deleted count at Debug
- [X] T021 [P] [US1] Create `tests/PdfBuilder.Api.Tests/JobAcceptanceOutboxTests.cs` (database-backed): build a service provider with `AddInfrastructure`, MassTransit's test harness and `AddEntityFrameworkOutbox<PdfBuilderDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); })`; in one scope publish an `IGeneratePdfRequest` through `IPublishEndpoint` and save the job through `IPdfJobStore`. Assert that the job row and exactly one delivered message exist after the save, and that when the save throws (for example a duplicate primary key) neither the job row nor any message exists (FR-003)

### Implementation for User Story 1

- [X] T022 [US1] Create `src/PdfBuilder.Infrastructure/Jobs/EfPdfJobStore.cs` implementing `IPdfJobStore`: `SaveAsync` upserts a `PdfJobRecord` stamped with `LastSavedAt = TimeProvider.GetUtcNow()` and calls `SaveChangesAsync` (this single call also commits outbox messages queued in the same scope); `GetAsync` returns `null` when the row is missing or `LastSavedAt < now − RetentionHours`. Keep the interface's XML docs accurate, and translate connectivity failures to `StorageUnavailableException` (T010)
- [X] T023 [US1] Create `src/PdfBuilder.Infrastructure/Jobs/JobRetentionSweeper.cs`, a `BackgroundService` that every hour deletes `pdf_jobs` rows whose `last_saved_at` is older than `JobStore:RetentionHours` using `ExecuteDeleteAsync`, logs the count at Debug, and survives a failed sweep (logs a Warning, tries again next hour)
- [X] T024 [US1] In `src/PdfBuilder.Infrastructure/DependencyInjection.cs`, register `IPdfJobStore` as scoped `EfPdfJobStore`, register `JobRetentionSweeper`, and remove `InMemoryPdfJobStore` and `AddMemoryCache()` if nothing else uses the cache (search the solution first)
- [X] T025 [US1] Configure the bus outbox in `src/PdfBuilder.Api/Messaging/MessagingExtensions.cs`: inside `AddMassTransit`, add `x.AddEntityFrameworkOutbox<PdfBuilderDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); })`; do **not** add the consumer outbox (research R2), and leave `GeneratePdfConsumer` and its definition unchanged
- [X] T026 [US1] In `src/PdfBuilder.Api/Endpoints/PdfJobEndpoints.cs` `EnqueueAsync`, publish the `IGeneratePdfRequest` first and then `await jobs.SaveAsync(PdfJob.Enqueued(...))`, so the single `SaveChanges` commits the job and the outbox message together; replace the comment "Recorded before publishing…" with one explaining that the outbox delivers nothing until the job is committed. The response (202, body, `Location`) must stay byte-identical (contracts/http-and-messages.md)
- [X] T027 [US1] Delete `src/PdfBuilder.Infrastructure/Jobs/InMemoryPdfJobStore.cs` and `tests/PdfBuilder.Infrastructure.Tests/InMemoryPdfJobStoreTests.cs`, and update `JobStoreOptions.RetentionHours`'s XML summary to "How long a job stays pollable after its last update; shared by every instance and kept across restarts." keeping `[Range(1, 168)]` and default `24`

**Checkpoint**: Scenario 1 and Scenario 6 of quickstart.md pass. Do not deploy yet: authored templates still live in files (US2, US3).

---

## Phase 4: User Story 2 - Authored templates survive a restart and an upgrade (Priority: P1)

**Goal**: authored templates live in the database with today's revision-based concurrency and case-insensitive key uniqueness, enforced across instances.

**Independent Test**: quickstart Scenario 2 (publish, edit draft, restart → Modified, jobs render published; stale save → `TEMPLATE_REVISION_CONFLICT`).

### Tests for User Story 2

- [X] T028 [P] [US2] Create `tests/PdfBuilder.Infrastructure.Tests/EfAuthoredTemplateStoreTests.cs` (database-backed), porting every behaviour case from `FileAuthoredTemplateStoreTests.cs` that is not about files: add then get from a new DbContext is identical (draft, published, revision, timestamps, derived status); adding `invoice` when `Invoice` exists throws `TemplateKeyTakenException`; update with the current revision succeeds; update with a stale revision throws `StaleTemplateRevisionException`; update of a deleted key throws `TemplateNotFoundException`; two DbContexts updating the same revision concurrently produce exactly one success and one `StaleTemplateRevisionException`; `GetAsync` is exact-case; delete returns `false` when nothing was deleted; an invalid key returns `null` from `GetAsync` and `false` from `DeleteAsync`

### Implementation for User Story 2

- [X] T029 [US2] Create `src/PdfBuilder.Infrastructure/Templates/EfAuthoredTemplateStore.cs` implementing `IAuthoredTemplateStore` per data-model.md "Write rules": `ListAsync` returns all rows as domain records; `GetAsync` returns `null` for an invalid key (`TemplateKey.IsValid`) without querying, otherwise an exact-case lookup; `AddAsync` inserts and maps a unique violation (PostgreSQL `23505`, on the primary key or `ux_authored_templates_key_lower`) to `TemplateKeyTakenException`; `UpdateAsync` runs `ExecuteUpdateAsync` filtered by `key` and `revision == expectedRevision`, and on 0 rows throws `StaleTemplateRevisionException(key, expected, actual)` if the key exists or `TemplateNotFoundException` if not; `DeleteAsync` returns whether a row was deleted. Translate connectivity failures to `StorageUnavailableException`
- [X] T030 [US2] Register `IAuthoredTemplateStore` as scoped `EfAuthoredTemplateStore` in `src/PdfBuilder.Infrastructure/DependencyInjection.cs`, replacing `FileAuthoredTemplateStore` (keep the `FileAuthoredTemplateStore` class file for now: US3 reuses its file format before deleting it)

**Checkpoint**: Scenario 2 of quickstart.md passes. Existing deployments would still lose their templates on upgrade: ship only together with US3.

---

## Phase 5: User Story 3 - Existing templates are carried over automatically (Priority: P2)

**Goal**: on the first start against an empty database, every readable template file is imported exactly once; files are never modified; skipped files are reported.

**Independent Test**: quickstart Scenario 3 (import with one malformed file, files untouched, deleted template not re-imported).

### Tests for User Story 3

- [X] T031 [P] [US3] Create `tests/PdfBuilder.Infrastructure.Tests/LegacyTemplateFilesTests.cs` (no database), porting the file-format cases from `FileAuthoredTemplateStoreTests.cs`: a file written in the old format reads back with draft, published, revision and timestamps intact; a missing directory yields no entries; and each skip reason from data-model.md is produced for its case — `malformed` (file containing `{`), `invalid-key`, `duplicate-key` (two files whose keys differ only in case), and `unreadable`; the reader never throws for a single bad file
- [X] T032 [P] [US3] Create `tests/PdfBuilder.Infrastructure.Tests/TemplateImporterTests.cs` (database-backed): first run imports all valid files and writes the `template_imports` row with `imported_count` and `skipped`; a file whose key matches a built-in template in any letter case is skipped as `built-in-key`; a second run imports nothing even if new files appear; the files' bytes and timestamps are unchanged afterwards; a missing directory still writes the row with `imported_count = 0`; two importers started concurrently against the same empty database import each template exactly once (Story 4, scenario 3)

### Implementation for User Story 3

- [X] T033 [US3] Create `src/PdfBuilder.Infrastructure/Templates/LegacyTemplateFiles.cs`: moves the old on-disk shape (`StoredTemplate` and its JSON options) out of `FileAuthoredTemplateStore.cs` and reads every `*.json` in a directory, returning imported `AuthoredTemplate`s plus `(file, reason)` skips for `unreadable`, `malformed`, `invalid-key` and `duplicate-key`; catches `JsonException` and I/O errors per file (the old `ReadAsync` let `JsonException` escape); opens files read-only
- [X] T034 [US3] Create `src/PdfBuilder.Infrastructure/Templates/TemplateImporter.cs`: in one transaction, `SELECT pg_advisory_xact_lock(<named constant>)`, return if a `template_imports` row exists (log at Debug with its date), otherwise read `TemplateStore:AuthoredRootPath` through `LegacyTemplateFiles`, additionally skip keys matching any `IBuiltInTemplateSource` key in any letter case as `built-in-key`, insert the templates and the `template_imports` row (`id = 1`, `completed_at`, resolved `source_path`, `imported_count`, `skipped`), and commit; log one Warning per skipped file with file name and reason, and an Information summary with source path, imported and skipped counts
- [X] T035 [US3] Call `TemplateImporter` from `src/PdfBuilder.Infrastructure/Persistence/DatabaseStartup.cs` after migrations, before the service starts serving (the extension point left in T014)
- [X] T036 [US3] Delete `src/PdfBuilder.Infrastructure/Templates/FileAuthoredTemplateStore.cs` and `tests/PdfBuilder.Infrastructure.Tests/FileAuthoredTemplateStoreTests.cs` once T031 and T028 cover their cases; update `TemplateStoreOptions.AuthoredRootPath`'s XML summary to say it is read once, on first start, for the import, and never written

**Checkpoint**: Scenario 3 of quickstart.md passes. US1 + US2 + US3 together are the first deployable increment.

---

## Phase 6: User Story 4 - A second instance can be run (Priority: P3)

**Goal**: two instances share job status and templates; nothing relies on per-process state.

**Independent Test**: quickstart Scenario 4 (submit on one instance, poll on the other; publish on one, render on the other; concurrent first start imports once).

- [X] T037 [US4] Search `src/` for remaining per-process state that affects jobs or templates (static fields, `SemaphoreSlim`, `IMemoryCache`, singletons holding template or job data) and remove or justify each in a code comment; record the result in the pull request description
- [X] T038 [P] [US4] Add a database-backed test in `tests/PdfBuilder.Infrastructure.Tests/MultiInstanceTests.cs`: two independent service providers built with `AddInfrastructure` against the same database — a job saved through the first is read through the second; a template published through the second is returned by the first's `IPdfTemplateRepository` (`PublishedTemplateRepository`) with the new published content
- [X] T039 [US4] Run Scenario 4 of `specs/001-persistent-storage/quickstart.md` manually with two instances of `src/PdfBuilder.Api` on ports 5052 and 5053 against one database and broker, and note the outcome in the pull request

**Checkpoint**: all stories work; the remaining single-instance limit is generated PDF files (feature 005).

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T040 [P] Update `docs/configuration.md`: new `## Database` section with `ConnectionStrings:Database` and `Blocks:Database:MigrateOnStartup` per `specs/001-persistent-storage/contracts/configuration.md`; revise `## JobStore` (survives restarts, shared, same default and range) and `## TemplateStore` (`AuthoredRootPath` read once for import); add the database to "Environment variables you will actually set"
- [X] T041 [P] Update `docs/operations.md`: database in volumes, must-set configuration, health (`/health/ready` includes the database), failure modes (database down → 503 `STORAGE_UNAVAILABLE`, readiness 503), "Scaling past one instance" now listing only local file storage, "Known gaps", and an upgrade note: the one-time import, and that jobs still in flight in the old version's memory are lost once
- [X] T042 [P] Update `docs/architecture.md`: the job store is the database; replace "Two ways to get a result"'s in-memory caveat and the "record before publishing" reasoning with the outbox; mention that authored templates live in the database and built-in ones on disk
- [X] T043 [P] Update `README.md`: "Running it" needs PostgreSQL (compose provides it; without Docker, set `ConnectionStrings:Database` with user-secrets, Neon or a local install); "Tests" mentions `PDFBUILDER_RUN_DATABASE_TESTS=1`; "Known limits" drops job state
- [X] T044 [P] Add an `### Changed (service)` entry to `CHANGELOG.md` under Unreleased, without changing the contracts version: jobs and authored templates are stored in PostgreSQL and survive restarts; accepted jobs and their messages are committed together; new 503 `STORAGE_UNAVAILABLE`; upgrade notes (database required, one-time import, in-flight jobs lost once)
- [X] T045 Add a `postgres` service to `deploy/compose.prod.yaml` (`postgres:17-alpine`, volume, health check, password from `${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env}`), make the API depend on it and set `ConnectionStrings__Database`; document the new `.env` variable in `deploy/README.md`
- [X] T046 [P] Document in `deploy/huggingface/README.md` that the Space needs a `ConnectionStrings__Database` secret pointing at a hosted PostgreSQL, because the Space disk is not persistent
- [X] T047 [P] Update `docs/blocks/04-backend.md` "Persistence" to match what was built (single `PdfBuilderDbContext`, bus outbox only, consumer outbox deferred, retention parity) and confirm `ConnectionStrings:Database` and `Blocks:Database:MigrateOnStartup` in its Configuration table
- [ ] T048 Run `dotnet build PdfBuilder.slnx` (warnings are errors) and `dotnet test PdfBuilder.slnx`, and confirm in CI (`.github/workflows/ci.yml`, with `PDFBUILDER_RUN_DATABASE_TESTS=1` and `PDFBUILDER_RUN_BROWSER_TESTS=1`) that every pre-existing test passes unchanged (SC-004), `tests/PdfBuilder.Application.Tests/ContractsTests.cs` passes, and the arm64 image builds. *Local part done: the build is clean, and the full suite with browser and database tests passes (240 tests: 239 passed, 1 skipped because Windows has a case-insensitive file system). Every pre-existing test file is untouched except the two whose subjects were deleted (`InMemoryPdfJobStoreTests`, `FileAuthoredTemplateStoreTests`), whose cases are ported to the Ef* tests. **Still open:** the CI run and the arm64 image build, which need this branch pushed.*
- [X] T049 Run Scenarios 1, 2, 3, 5 and 6 of `specs/001-persistent-storage/quickstart.md` against a real PostgreSQL and record the results in the pull request. *Run against PostgreSQL 17.7 and the real API host (as Production, so no real broker): **1** a Pending job returned a byte-identical response after a restart, and an unknown id gave 404; **2** create, publish and a stale-revision save across two instances (409 `TEMPLATE_REVISION_CONFLICT`), restarts covered by tests that use a new context per call; **3** three old files imported once with two instances starting together, malformed and clashing files covered by tests; **4** (T039) polling and publishing across two live instances; **5** with the database stopped: `/health/live` 200, requests 503 `STORAGE_UNAVAILABLE` with `Retry-After: 5` and no host in the body, recovery without restarting the service (readiness itself is proven by `DatabaseHealthCheckTests`, because the local broker is absent and kept it 503 anyway); **6** retention verified with a fake clock (59 minutes returned, 61 gone, a save renews it), not live, which would take an hour. Not exercised: the web console in a browser.*

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: after Setup. Blocks every story.
- **US1 (Phase 3)** and **US2 (Phase 4)**: after Foundational; independent of each other.
- **US3 (Phase 5)**: after Foundational. Its importer writes through the schema from T013 and does not need US2's store, but T036 (deleting the file store) waits for T028 and T031.
- **US4 (Phase 6)**: after US1 and US2 (it exercises both stores).
- **Polish (Phase 7)**: after the stories it documents; T045–T046 before the first deployment.

### Release rule

US2 must not be deployed without US3: an existing deployment would start with an empty template
list. The first deployable increment is **US1 + US2 + US3**.

### Within Each Story

- Tests are written alongside the implementation and must fail without it.
- Records and configurations (Phase 2) before stores; stores before registration; registration
  before endpoint changes.

### Parallel Opportunities

- Setup: T003, T004, T005 in parallel after T001–T002.
- Foundational: T007, T008, T010 in parallel; T016, T017 in parallel with T011–T015.
- After Foundational, US1 and US2 can proceed in parallel (different files, except both touch
  `src/PdfBuilder.Infrastructure/DependencyInjection.cs` in T024 and T030: do those sequentially).
- Within US1: T019, T020, T021 in parallel. Within US3: T031, T032 in parallel.
- Polish: T040–T044, T046, T047 in parallel.

---

## Parallel Example: User Story 1

```bash
# Tests for US1 together:
Task: "EfPdfJobStoreTests in tests/PdfBuilder.Infrastructure.Tests/EfPdfJobStoreTests.cs"
Task: "JobRetentionSweeperTests in tests/PdfBuilder.Infrastructure.Tests/JobRetentionSweeperTests.cs"
Task: "JobAcceptanceOutboxTests in tests/PdfBuilder.Api.Tests/JobAcceptanceOutboxTests.cs"

# Then the store and sweeper (different files) together:
Task: "EfPdfJobStore in src/PdfBuilder.Infrastructure/Jobs/EfPdfJobStore.cs"
Task: "JobRetentionSweeper in src/PdfBuilder.Infrastructure/Jobs/JobRetentionSweeper.cs"
```

---

## Implementation Strategy

### MVP First

1. Phase 1 Setup, Phase 2 Foundational.
2. Phase 3 (US1): **stop and validate** Scenarios 1 and 6. This proves the database, migrations,
   outbox and retention end to end, but is not deployable on its own.
3. Phases 4 and 5 (US2, US3): validate Scenarios 2 and 3. **This is the first release.**

### Incremental Delivery

1. Setup + Foundational → schema and start-up ready.
2. US1 → job status persistent (validated, not deployed).
3. US2 + US3 → templates persistent and migrated → **deploy**.
4. US4 → second instance verified.
5. Polish → docs, deployment files, CHANGELOG.

---

## Notes

- [P] tasks touch different files and do not depend on incomplete tasks.
- Commit after each task or logical group; one pull request for the whole feature.
- Never log the connection string; never add an in-memory fallback (research R10).
- If a task reveals that the plan is wrong, update plan.md or research.md in the same pull request.
