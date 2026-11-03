# Research: Persistent Storage for Jobs and Authored Templates

Every decision below was checked against the code on `master` (2026-11-03) and the package feeds
on nuget.org on the same day.

## R1. Storage engine and access library

- **Decision**: PostgreSQL, through EF Core 10 with the Npgsql provider, snake_case naming.
  Packages: `Microsoft.EntityFrameworkCore` / `.Relational` / `.Design` 10.0.12 (the band the
  repository already pins for `Microsoft.Extensions.*`), `Npgsql.EntityFrameworkCore.PostgreSQL`
  10.0.3, `EFCore.NamingConventions` 10.0.1,
  `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.12.
- **Rationale**: PostgreSQL is fixed by the constitution's technology constraints and runs on both
  `amd64` and `arm64` (Principle X). Npgsql 10.0.3 requires EF Core ≥ 10.0.4, satisfied by 10.0.12.
  EF Core gives migrations, the migration lock (R3) and the outbox integration (R2) for free.
- **Alternatives considered**: Dapper with hand-written SQL (no migrations or outbox integration;
  more code for the same result); Redis for jobs (a second store to operate, and no transaction
  with the job's message, so FR-003 would need a different mechanism).

## R2. Accepting a job atomically (FR-003)

- **Decision**: MassTransit's EF Core **bus outbox** (`AddEntityFrameworkOutbox<PdfBuilderDbContext>`
  with `UsePostgres()` and `UseBusOutbox()`), using `MassTransit.EntityFrameworkCore` **8.5.11**.
  The HTTP endpoint publishes the request and then saves the job; both are written by the single
  `SaveChanges` and the message leaves only after the commit.
- **Rationale**: today the endpoint saves the job and then publishes, as two operations, so a broker
  failure leaves a job that is `Pending` forever and a database failure would leave a message for a
  job nobody can poll. The outbox makes the pair atomic. Version 8.5.11 matters: MassTransit is held
  at 8.5.x for licensing (constitution), and its nuspec has a `net10.0` group depending on
  `Microsoft.EntityFrameworkCore.Relational` ≥ 10.0.0, so it works with EF Core 10 without leaving
  the Apache-2.0 line.
- **Ordering note**: the existing comment "recorded before publishing, so a fast worker cannot
  overwrite the result with Pending" stays true by construction: nothing is delivered before the
  commit that records the job.
- **Out of scope here**: the *consumer* inbox/outbox. It changes how retries and the final
  `RENDER_FAILED` transition interact with redelivery (`GeneratePdfConsumerRetryTests`), which is
  more than this feature needs. The consumer keeps its current read-then-upsert of the job, which is
  idempotent for redeliveries.
- **Alternatives considered**: saving then publishing with compensation on failure (still leaves a
  window); publishing first then saving (a fast worker could finish before the row exists, which the
  current `CompleteAsync` tolerates but would then overwrite with `Pending`).

## R3. Schema migrations with several instances

- **Decision**: EF Core migrations in `PdfBuilder.Infrastructure`, applied at start-up when
  `Blocks:Database:MigrateOnStartup` is `true` (default). Start-up order: migrate → import (R4) →
  serve.
- **Rationale**: EF Core 9 and later take a database lock while migrating (an advisory lock on
  Npgsql), so two instances starting together cannot both apply a migration. Running in a hosted
  service registered before the web server means the instance does not accept traffic until its
  schema is current.
- **Alternatives considered**: a separate migration step in the deployment (an EF bundle). Safer
  for large fleets, but one more thing for a company's IT to run; the setting allows switching to it
  later without code changes.

## R4. One-time import of existing templates (FR-010 to FR-013)

- **Decision**: a `TemplateImporter` run once at start-up after migration, inside one transaction
  that first takes a PostgreSQL transaction-scoped advisory lock
  (`pg_advisory_xact_lock(<constant>)`). Under the lock it checks the `template_imports` row; if
  absent, it reads `TemplateStore:AuthoredRootPath`, inserts every readable template, inserts the
  `template_imports` row listing imported and skipped files, and commits. The row is written even
  when the directory is missing or empty, so later starts never scan again.
- **Skipped, with a warning log naming the file and reason**: unreadable or malformed JSON, an
  invalid key, a key that matches a built-in template (in any letter case), or a duplicate key.
  The existing file reader (`FileAuthoredTemplateStore.ReadAsync`) lets `JsonException` escape;
  the importer's reader must catch it per file.
- **Files are only read**, never moved or deleted (FR-012), so rolling back to the previous version
  finds them intact.
- **Rationale**: the advisory lock plus the marker row gives "exactly once" across simultaneous
  starts (Story 4, scenario 3) without a coordinator.
- **Alternatives considered**: a unique "import" row as the lock (two starts could both read files
  before one fails on insert; correct but wasteful and noisier); importing lazily on first read
  (spreads the work across requests and complicates concurrency).

## R5. Job retention parity (FR-004)

- **Decision**: every save stamps `last_saved_at`. A read treats a job as gone once
  `last_saved_at < now − JobStore:RetentionHours`; a background `JobRetentionSweeper` deletes such
  rows hourly. `JobStore:RetentionHours` keeps its default (24) and range (1–168).
- **Rationale**: the in-memory store set a fresh expiry on every save
  (`cache.Set(key, job, retention)`), so expiry was "last save + retention". Filtering on read makes
  the cut-off exact, and the sweeper only bounds storage; neither changes when a job disappears.
- **Alternatives considered**: expiring from `enqueued_at` or `completed_at` (differs from today for
  long-running or long-pending jobs).

## R6. Template concurrency and key uniqueness (FR-008)

- **Decision**: `UPDATE … WHERE key = @key AND revision = @expected`; zero rows means stale (if the
  row exists) or not found (if it does not), mapped to the existing
  `StaleTemplateRevisionException` / `TemplateNotFoundException`. A unique index on a
  generated lower-cased copy of the key (`key_lower`) enforces the existing case-insensitive key rule (`TemplateCatalog.SameKey` uses
  `OrdinalIgnoreCase`); a violation on insert maps to `TemplateKeyTakenException`. Lookups by key
  stay exact-case, as the file store behaves on Linux today.
- **Rationale**: the file store relied on a process-wide `SemaphoreSlim`, which cannot protect two
  instances. The conditional update is the same guarantee, enforced by the database.
- **Alternatives considered**: EF concurrency tokens on `revision` (equivalent; the explicit
  `ExecuteUpdate` keeps the stale/not-found distinction in one round trip).

## R7. Storing template content

- **Decision**: `draft` and `published` as `jsonb` columns mapped from a `TemplateContent` record
  (HTML, CSS, sample JSON, render options) through EF Core's JSON column mapping on a persistence
  record type. The domain `AuthoredTemplate` is never mapped directly; it is rebuilt with
  `AuthoredTemplate.Restore`, as the file store does today.
- **Rationale**: content is always read and written whole; nothing queries inside it. Keeping the
  persistence record separate preserves the domain's private constructors and lets the stored shape
  evolve independently (the file store's `StoredTemplate` already follows this rule).
- **Alternatives considered**: four text columns per version (eight columns, no benefit); a
  `template_versions` table (history is not a requirement).

## R8. Service lifetimes

- **Decision**: `IPdfJobStore`, `IAuthoredTemplateStore`, `TemplateCatalog` and
  `PublishedTemplateRepository` become **scoped**. The DbContext is scoped.
- **Rationale**: `TemplateCatalog` and `PublishedTemplateRepository` are registered as singletons in
  `PdfBuilder.Application/DependencyInjection.cs` and depend on `IAuthoredTemplateStore`; keeping
  them singletons over a scoped store is a captive dependency. All their callers (MediatR handlers,
  the consumer, endpoints) already run in a scope.

## R9. Behaviour when the database is unavailable (FR-017, edge case)

- **Decision**: a `DbContext` health check tagged `ready` (readiness fails, liveness unaffected, as
  with the broker today). Database connectivity failures surfacing from a request map, in
  `ApiExceptionHandler`, to `503 Service Unavailable` problem details with `code: STORAGE_UNAVAILABLE`
  and `Retry-After: 5`.
- **Rationale**: today such a failure would be an unhandled 500. A 503 tells products to retry,
  which is the correct reading of a transient outage. No existing response or code changes
  (FR-014); this is a new failure response for a failure that could not happen before.

## R10. Tests and local development

- **Decision**: database-backed tests use **Testcontainers.PostgreSql 4.16.0** and are opt-in with
  `PDFBUILDER_RUN_DATABASE_TESTS=1`, mirroring the existing `PDFBUILDER_RUN_BROWSER_TESTS` pattern;
  CI sets both. Pure logic (the tolerant file reader, retention arithmetic, exception mapping) is
  tested without a database.
- **Rationale**: Docker is not installed on the main development machine (checked: `docker` not
  found), so tests that required it by default would make `dotnet test` fail locally. CI runners
  (`ubuntu-latest`) have Docker.
- **Local development**: the service now requires `ConnectionStrings:Database`. Options, in order of
  convenience on this machine: a free hosted PostgreSQL (Neon), a local PostgreSQL install, or the
  `postgres` service added to `compose.yaml` where Docker exists. Start-up fails with a message
  naming the setting when it is missing (constitution: every key has a default or fails start-up by
  name). No in-memory fallback: a silently non-persistent mode is exactly the failure this feature
  removes.

## R11. Deployment impact

- **Decision**: `compose.yaml` and `deploy/compose.prod.yaml` gain a `postgres:17-alpine` service
  (multi-arch) with a named volume and a health check; the API depends on it. The Hugging Face
  Space deployment, whose disk is ephemeral, takes `ConnectionStrings__Database` from a secret
  pointing at a hosted PostgreSQL.
- **Rationale**: Principle II (each block its own database) and X (runs offline, multi-arch). The
  official image publishes `arm64`, so the Oracle Ampere VM is covered.

## Resolved unknowns

No `NEEDS CLARIFICATION` remains in the technical context; the only product question (retention
default) was settled in the spec's Clarifications.
