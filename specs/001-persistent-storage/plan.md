# Implementation Plan: Persistent Storage for Jobs and Authored Templates

**Branch**: `001-persistent-storage` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-persistent-storage/spec.md`

## Summary

Job status and authored templates move from process memory and local JSON files into the block's
own PostgreSQL database, so they survive restarts and are shared by every instance. The two
existing ports (`IPdfJobStore`, `IAuthoredTemplateStore`) keep their signatures; new EF Core
adapters replace `InMemoryPdfJobStore` and `FileAuthoredTemplateStore`. Accepting a job becomes
atomic through MassTransit's EF Core bus outbox (8.5.11, still Apache-2.0). On the first start
against an empty database, existing template files are imported exactly once under an advisory
lock. No HTTP response, message contract or console behaviour changes; the only new response is a
503 `STORAGE_UNAVAILABLE` when the database is down. Details and alternatives:
[research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 10 (`global.json`); TypeScript/Angular 22 untouched.

**Primary Dependencies**: ASP.NET Core minimal APIs, MediatR, FluentValidation, MassTransit 8.5.11
(RabbitMQ). Added: EF Core 10.0.12, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3,
`EFCore.NamingConventions` 10.0.1, `MassTransit.EntityFrameworkCore` 8.5.11,
`Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.12; tests:
`Testcontainers.PostgreSql` 4.16.0.

**Storage**: PostgreSQL 17, one database per block. Generated PDFs stay on local disk (out of scope).

**Testing**: xUnit v3 + NSubstitute (existing); Testcontainers for database tests, opt-in with
`PDFBUILDER_RUN_DATABASE_TESTS=1` and enabled in CI.

**Target Platform**: Linux containers, `linux/amd64` and `linux/arm64`; Windows for development.

**Project Type**: Web service (API + RabbitMQ consumer in one process) with an Angular console.

**Performance Goals**: a job status read is one primary-key lookup; template reads are one row.
Both stay well under a second (SC-006) at current volumes.

**Constraints**: zero changes to HTTP responses, message contracts and console (FR-014 to FR-016);
MassTransit must stay on 8.5.x; works offline; no in-memory fallback.

**Scale/Scope**: single deployments with tens of templates and up to tens of thousands of jobs per
retention window; two or more instances must be possible (Story 4).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Gate | Pre-research | Post-design |
| --- | --- | --- | --- | --- |
| I | One codebase, no forks | No customer path, no hardcoded deployment value; new settings documented with defaults | ✅ | ✅ Two new settings, documented in [contracts/configuration.md](contracts/configuration.md); `docs/configuration.md` and `docs/blocks/04-backend.md#configuration` updated in this feature |
| II | Block stands alone | Own database, no reference to another block | ✅ | ✅ One database, chosen by `ConnectionStrings:Database` |
| III | Generic, no product knowledge | No product vocabulary; client data tables carry `client_id` + filter | ⚠ | ⚠ No product vocabulary. `pdf_jobs` and `authored_templates` have no `client_id` yet: justified below |
| IV | Blocks never call each other | No client, publisher or package targeting another block | ✅ | ✅ |
| V | Same studs | New endpoints, messages and lists conform | ✅ | ⚠ No new endpoints or messages; readiness includes the database; the new 503 carries a `code`. Stud persistence base (`Blocks.ServiceDefaults.EntityFramework`) not introduced yet: justified below |
| VI | Identity from claims | No identity read outside handlers | ✅ | ✅ Not touched |
| VII | Additive contracts | Additive diffs, version + changelog | ✅ | ✅ No contract change; `CHANGELOG.md` gets a *service* entry (outbox, 503, upgrade note) without a package bump |
| VIII | Secrets unrecoverable | No plain-text secret, nothing logged | ✅ | ✅ Connection string from configuration/secrets only, excluded from logs and problem details |
| IX | Layering by tooling | Build, lint, architecture tests pass; new boundaries enforced | ✅ | ✅ All persistence in Infrastructure; Application ports unchanged. No new boundary |
| X | Any hardware, offline | Both architectures build; no runtime external host | ✅ | ✅ `postgres:17-alpine` is multi-arch; nothing calls out |

**Result**: passes, with two justified deviations (Complexity Tracking).

## Project Structure

### Documentation (this feature)

```text
specs/001-persistent-storage/
├── spec.md
├── plan.md                         # this file
├── research.md                     # R1–R11 decisions
├── data-model.md                   # tables, rules, transitions
├── quickstart.md                   # end-to-end validation
├── contracts/
│   ├── configuration.md            # new/changed settings, health, logs, deployment files
│   └── http-and-messages.md        # what must not change, and the one new 503
├── checklists/requirements.md
└── tasks.md                        # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PdfBuilder.Application/
│   └── DependencyInjection.cs            # TemplateCatalog, PublishedTemplateRepository → scoped (R8)
├── PdfBuilder.Infrastructure/
│   ├── DependencyInjection.cs            # DbContext, scoped stores, health check, hosted services
│   ├── Persistence/                      # NEW
│   │   ├── PdfBuilderDbContext.cs        # + MassTransit outbox entities
│   │   ├── DatabaseOptions.cs            # Blocks:Database:MigrateOnStartup
│   │   ├── DatabaseStartup.cs            # hosted: migrate, then import (R3, R4)
│   │   ├── StorageFailures.cs            # recognises "database unreachable" in an exception chain
│   │   ├── PdfBuilderDbContextOptions.cs # Npgsql + snake_case, shared by the service and `dotnet ef`
│   │   ├── Records/                      # PdfJobRecord, AuthoredTemplateRecord, TemplateContentRecord, TemplateImportRecord
│   │   ├── Configurations/               # static Configure(...) per record (no reflection scanning)
│   │   └── Migrations/                   # InitialPersistence
│   ├── Jobs/
│   │   ├── EfPdfJobStore.cs              # NEW, replaces InMemoryPdfJobStore (deleted)
│   │   ├── JobRetentionSweeper.cs        # NEW, hourly delete past retention (R5)
│   │   └── JobStoreOptions.cs            # unchanged range and default
│   └── Templates/
│       ├── EfAuthoredTemplateStore.cs    # NEW, replaces FileAuthoredTemplateStore (deleted)
│       ├── LegacyTemplateFiles.cs        # NEW, tolerant reader of the old JSON files
│       └── TemplateImporter.cs           # NEW, once, under advisory lock (R4)
└── PdfBuilder.Api/
    ├── Endpoints/PdfJobEndpoints.cs      # publish, then save: one commit through the outbox (R2)
    ├── Endpoints/ApiExceptionHandler.cs  # database unreachable → 503 STORAGE_UNAVAILABLE (R9)
    └── Messaging/MessagingExtensions.cs  # AddEntityFrameworkOutbox<PdfBuilderDbContext>

tests/
├── PdfBuilder.Infrastructure.Tests/
│   ├── Persistence/PostgresFixture.cs    # Testcontainers, skipped unless PDFBUILDER_RUN_DATABASE_TESTS=1
│   ├── EfPdfJobStoreTests.cs             # replaces InMemoryPdfJobStoreTests, incl. retention parity
│   ├── EfAuthoredTemplateStoreTests.cs   # add/update/delete, stale revision, case-insensitive key
│   ├── TemplateImporterTests.cs          # once, skips, files untouched, concurrent starts
│   └── LegacyTemplateFilesTests.cs       # no database: malformed/invalid/duplicate handling
└── PdfBuilder.Api.Tests/
    └── ApiExceptionHandlerTests.cs       # 503 mapping; existing consumer tests unchanged

compose.yaml, deploy/compose.prod.yaml, deploy/huggingface/   # postgres service / hosted DB secret
.github/workflows/ci.yml                                       # PDFBUILDER_RUN_DATABASE_TESTS=1
Directory.Packages.props                                       # packages above, pinned
docs/configuration.md, docs/operations.md, docs/architecture.md, README.md, CHANGELOG.md
```

**Structure Decision**: the existing layered solution is kept. Everything new lives in
`PdfBuilder.Infrastructure` under folders named for the capability (`Persistence/`, plus the existing
`Jobs/` and `Templates/`), as `docs/architecture.md` prescribes. Application and Domain do not
change beyond service lifetimes. The two removed adapters are deleted rather than kept as a
fallback (R10).

### Documentation updated in this feature (Definition of done)

| Document | Change |
| --- | --- |
| `docs/configuration.md` | `ConnectionStrings:Database`, `Blocks:Database:MigrateOnStartup`; new meaning of `JobStore` and `TemplateStore:AuthoredRootPath` |
| `docs/operations.md` | Database in volumes, health and failure modes; "Scaling past one instance" now lists only file storage; upgrade note on the one-time import and in-flight jobs |
| `docs/architecture.md` | Job store is the database; the outbox replaces "record before publishing" |
| `README.md` | Running it needs a database; "Known limits" drops job state |
| `CHANGELOG.md` | Service entry (no contracts version change) |
| `deploy/README.md`, `deploy/huggingface/README.md` | Database setup for each target |

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
| --- | --- | --- |
| III: `pdf_jobs` and `authored_templates` have no `client_id` | Clients do not exist until features 002/003. A column with no source of truth would hold a made-up value that 003 must then migrate anyway. | Adding `client_id` now with a placeholder default: pre-commits 003's design (default client, backfill) before its spec exists, for no behaviour gained. 003 adds the column, the backfill and the query filter in one migration. |
| V: persistence is not built on the shared `Blocks.ServiceDefaults.EntityFramework` base | That package (clients, API keys, webhooks tables) is introduced by feature 002, which defines its contents. | Creating the shared base now with only the outbox in it: guesses at a public package's shape from one consumer. 002 extracts it from `PdfBuilderDbContext` when the second set of tables exists. |
