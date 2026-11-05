# Implementation Plan: Client-Scoped Templates and Jobs

**Branch**: `003-client-scoped-templates-jobs` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/003-client-scoped-templates-jobs/spec.md`

## Summary

Since 002 every caller is a client, and since 004 the operator works inside one. But
`authored_templates` and `pdf_jobs` are still shared. This feature gives each row an owner
(`client_id`) and filters both tables by `ICurrentClient.ClientId` in `PdfBuilderDbContext`. Every
existing query, including the Application layer's "own template, else built-in" lookup and the key
uniqueness check, becomes per client with no change to Application code. The filter fails closed: no
client, no rows.

The operator must now name a client on template and job endpoints, through a stud endpoint filter.
Bus messages gain an optional `ClientId` (Contracts 1.2.0). The consumer resolves it, falling back to
`Blocks:Bus:DefaultClientId`, and sets it through a new `IClientContext.ActAs` before any store is
used. Existing rows get an owner during the upgrade, between two migrations: the column is added
nullable, rows are assigned to the default client (registered if missing), then the column becomes
`NOT NULL`, joins the primary key and gets a foreign key. Details and alternatives:
[research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 10 (`global.json`). No console change (the 004 console already sends
the chosen client).

**Primary Dependencies**: EF Core 10 named query filters (Npgsql provider), MassTransit 8.5 (existing).
**No new packages.**

**Storage**: PostgreSQL, the block's own database. Two migrations, `AddClientOwner` and
`RequireClientOwner`, with a startup assignment step between them ([data-model.md](data-model.md)).

**Testing**: xUnit v3 + NSubstitute. Database-backed and pipeline tests are opt-in with
`PDFBUILDER_RUN_DATABASE_TESTS=1` (Testcontainers, or `PDFBUILDER_TEST_POSTGRES` without Docker) and
on in CI. The MassTransit test harness covers the consumer.

**Target Platform**: Linux containers, `linux/amd64` and `linux/arm64`.

**Project Type**: Web service (API plus RabbitMQ consumer).

**Performance Goals**: SC-007, within 10% for job submission and status reads. The filter adds one
equality on a key-prefix column. Operator requests add one `clients` read (R4), and bus messages add
one `clients` read and one `Pending` insert (R5, R8), against renders that take seconds.

**Constraints**: no API path or body changes; the message change is additive (Principle VII); the
upgrade needs exactly one setting and no hand-run SQL with migrate-on-start; job ids stay globally
unique.

**Scale/Scope**: tens of clients; ten affected endpoints; one message; two tables.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Pre-research | Post-design |
| --- | --- | --- | --- |
| I | One codebase, no forks | ✅ | ✅ One new setting, `Blocks:Bus:DefaultClientId`, default unset, documented in `docs/configuration.md` and `04-backend.md#configuration` ([contracts/configuration.md](contracts/configuration.md)). No deployment value in a migration (R6). |
| II | Block stands alone | ✅ | ✅ Everything in the block's database; the default client is registered in the block's own `clients` table. |
| III | Generic, no product knowledge | ✅ | ✅ Ends the deviation carried since 001: both data tables have `client_id` and the client query filter, and an architecture test makes the filter mandatory for future tables (R11). |
| IV | Blocks never call each other | ✅ | ✅ |
| V | Same studs | ✅ | ✅ Another client's template or job answers 404, identical to a missing one. `.RequireActingClient()` and `IClientContext` are built in `Blocks.ServiceDefaults` for every block. Not touched here and still owned by their features: idempotency (006), file signing (005), job list paging (010). |
| VI | Identity from claims only | ✅ | ✅ Stores and the context read only `ICurrentClient`. The consumer sets the client from the message through `IClientContext.ActAs` before any other work. `X-Client-Id` is still read only in `HttpCurrentClient`. |
| VII | Contracts change additively | ✅ | ✅ `IGeneratePdfRequest.ClientId` (optional), `PdfErrorCodes.ClientDisabled`; Contracts 1.2.0, `CP0006` baseline suppression, `CHANGELOG.md`. No HTTP shape changes. The operator's new `400` without a client is a refusal of a request that previously ran unscoped: see Complexity Tracking. |
| VIII | Secrets unrecoverable | ✅ | ✅ No secret touched. |
| IX | Layering by tooling | ✅ | ✅ `PdfBuilder.Application` gains no reference (R1). New architecture tests: `IgnoreQueryFilters` only in the three named methods; every client-owned entity is filtered. No suppressions. |
| X | Any hardware, offline | ✅ | ✅ No external dependency. |

**Result**: passes. One justified behaviour change (VII) in Complexity Tracking.

Definition-of-done note: this is the first feature that reads client data, so the cross-client 404
test applies to every template and job endpoint, enumerated from `EndpointDataSource` (R11).

## Project Structure

### Documentation (this feature)

```text
specs/003-client-scoped-templates-jobs/
├── spec.md
├── plan.md                         # this file
├── research.md                     # R1–R12
├── data-model.md                   # client_id, keys, migrations, identity types, message
├── quickstart.md
├── contracts/
│   ├── http-client-scoping.md      # affected endpoints, refusals, cross-client answers
│   ├── message-contract.md         # IGeneratePdfRequest.ClientId, resolution table, trust
│   └── configuration.md            # Blocks:Bus:DefaultClientId, upgrade, manual migration SQL
├── checklists/requirements.md
└── tasks.md                        # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Blocks.ServiceDefaults/
│   ├── BlocksAuthExtensions.cs                  register CurrentClient as ICurrentClient + IClientContext;
│   │                                            bind and validate BlocksBusOptions
│   ├── BlocksBusOptions.cs                      NEW  Blocks:Bus:DefaultClientId (client id rule, ValidateOnStart)
│   ├── Identity/
│   │   ├── IClientContext.cs                    NEW  ActAs(clientId) for non-HTTP work (R5)
│   │   ├── CurrentClient.cs                     NEW  scoped: ActAs value, else HttpCurrentClient
│   │   └── ActingClientEndpointFilter.cs        NEW  .RequireActingClient(): 400 no client, 404 unknown (R4)
│   └── Problems/BlocksProblems.cs               + ClientRequired() (400 VALIDATION_FAILED, X-Client-Id error)
├── PdfBuilder.Contracts/
│   ├── IGeneratePdfRequest.cs                   + string? ClientId
│   ├── GeneratePdfRequest.cs                    + ClientId { get; set; }
│   ├── PdfErrorCodes.cs                         + ClientDisabled, in All
│   ├── CompatibilitySuppressions.xml            + CP0006 for ClientId (both TFMs)
│   └── PdfBuilder.Contracts.csproj              Version 1.2.0-local
├── PdfBuilder.Domain/Exceptions/
│   └── JobIdTakenException.cs                   NEW  permanent, VALIDATION_FAILED (R8)
├── PdfBuilder.Application/Abstractions/
│   └── IPdfJobStore.cs                          + ClaimAsync(jobId, templateKey)
├── PdfBuilder.Infrastructure/
│   ├── Persistence/
│   │   ├── PdfBuilderDbContext.cs               + ICurrentClient; CallerClientId; "client" filters (R1)
│   │   ├── PdfBuilderDbContextOptions.cs        design-time factory: no client
│   │   ├── ClientScope.cs                       NEW  RequireClientId() → ClientRequiredException (R3)
│   │   ├── ClientRequiredException.cs           NEW
│   │   ├── ClientOwnerAssignment.cs             NEW  owner resolution (R7) + AssignUnownedToDefaultClientAsync (R6)
│   │   ├── DatabaseStartup.cs                   migrate to AddClientOwner → import → assign → migrate, only
│   │   │                                        when RequireClientOwner is pending (R6)
│   │   ├── Configurations/AuthoredTemplateConfiguration.cs   client_id, PK (client_id, key), unique (client_id, key_lower), FK
│   │   ├── Configurations/PdfJobConfiguration.cs             client_id, FK
│   │   ├── Records/AuthoredTemplateRecord.cs    + ClientId
│   │   ├── Records/PdfJobRecord.cs              + ClientId; CopyFrom leaves it
│   │   ├── PersistenceLog.cs                    + owner assigned, client registered for upgrade
│   │   └── Migrations/                          + AddClientOwner, + RequireClientOwner
│   ├── Templates/EfAuthoredTemplateStore.cs     owner on insert
│   ├── Templates/TemplateImporter.cs            imported rows get the owner (asks only when it has rows)
│   ├── Jobs/EfPdfJobStore.cs                    owner on insert; ClaimAsync; JobIdTaken on foreign duplicate
│   └── Jobs/JobRetentionSweeper.cs              DeleteExpiredJobsOfAllClientsAsync (IgnoreQueryFilters)
└── PdfBuilder.Api/
    ├── Endpoints/TemplateEndpoints.cs           group .RequireActingClient()
    ├── Endpoints/PdfJobEndpoints.cs             group .RequireActingClient(); message carries ClientId
    ├── Endpoints/ApiExceptionHandler.cs         ClientRequiredException → 400
    └── Messaging/GeneratePdfConsumer.cs         resolve client (R5 table) → ActAs → ClaimAsync → render;
                                                 ClientId in log scope; client refusals answered without a job

tests/
├── Blocks.ServiceDefaults.Tests/
│   ├── CurrentClientTests.cs                    + ActAs wins over HTTP; HTTP unchanged without it
│   ├── ActingClientEndpointFilterTests.cs       NEW  key caller untouched; operator: none 400, unknown 404, disabled ok
│   ├── BlocksBusOptionsTests.cs                 NEW  malformed refuses start; unset allowed
│   └── ArchitectureTests.cs                     + IgnoreQueryFilters allow-list; filtered client-owned entities
├── PdfBuilder.Application.Tests/
│   └── ContractsTests.cs                        + ClientDisabled in All; ClientId absent → null
├── PdfBuilder.Infrastructure.Tests/
│   ├── EfAuthoredTemplateStoreTests.cs          + per-client list/get/update/delete; same key twice; case rule per client
│   ├── EfPdfJobStoreTests.cs                    + per-client get; ClaimAsync own / free / foreign
│   ├── JobRetentionSweeperTests.cs              + deletes every client's expired jobs
│   ├── TemplateImporterTests.cs                 + imported rows owned by the default client
│   ├── Persistence/ClientOwnerUpgradeTests.cs   NEW  refuse without setting (nothing changed); assign; register;
│   │                                            two instances; idempotent; MigrateOnStartup=false message
│   └── Persistence/MigrationTests.cs            + both migrations up from the AddConsoleSessions schema
└── PdfBuilder.Api.Tests/
    ├── CrossClientIsolationTests.cs             NEW  every template/job endpoint: B on A's key/id == missing (SC-001)
    ├── SameKeyPerClientTests.cs                 NEW  same key in two clients renders each one's own (SC-003, SC-004)
    ├── ActingClientTests.cs                     NEW  operator end to end through the console session and admin key
    ├── GeneratePdfConsumerClientTests.cs        NEW  the R5 table; foreign CorrelationId; log scope has ClientId
    ├── JobAcceptanceOutboxTests.cs              + published message carries the caller's ClientId
    └── Support/                                 helpers act as a test client (R11)

compose.yaml                                     + Blocks__Bus__DefaultClientId: ecommerce
deploy/compose.prod.yaml, deploy/README.md       + DEFAULT_CLIENT_ID; upgrade step
samples/requests.http, samples/README.md         two-client walk-through; bus ClientId
```

**Structure Decision**: the reusable pieces (acting-client filter, `IClientContext`, the bus
default-client option) go into `Blocks.ServiceDefaults` beside 002's and 004's identity code, ready
for extraction in 011. The filter and ownership live in PDF Builder's own `DbContext` and stores.
`PdfBuilder.Application` changes only by one port method (`ClaimAsync`); its template and job rules
are reused unchanged.

### Documentation updated in this feature

| Document | Change |
| --- | --- |
| `docs/message-contracts.md` | `ClientId`, the resolution table, `CLIENT_DISABLED`, the broker trust limit (FR-018) |
| `docs/configuration.md` | `Blocks:Bus:DefaultClientId` |
| `docs/operations.md` | Upgrading to clients: the one setting, what it does, the manual-migration SQL |
| `docs/architecture.md` | Remove "not yet separated per client (feature 003)"; the filter and `ActAs` in the request and message flows |
| `docs/blocks/04-backend.md` | Client scoping as built: two migrations, registration of the default client, `.RequireActingClient()`, `IClientContext`, `ClaimAsync`, job ids global |
| `README.md`, `samples/README.md` | Templates and jobs are per client |
| `CHANGELOG.md` | Contracts 1.2.0; service entry: per-client data, operator must choose a client, upgrade step |

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
| --- | --- | --- |
| VII: the operator's template and job calls without `X-Client-Id` now answer `400` instead of running unscoped | With owned data there is no "unscoped" answer that is not either everyone's data (a leak, and the opposite of what the console intends) or nobody's (a silently empty list). The console already always sends a client (004), and 002/004 are unreleased, so no released caller relies on the old behaviour. | Treating no client as "all clients" for the operator: that is the cross-client view of feature 010, which needs paging and a client column in responses. Silently returning built-ins only: hides the mistake. |

**Deviations from `docs/blocks/`** (not constitution violations; the docs are updated to match):
two migrations around a startup assignment step instead of "one migration" (R6); the upgrade
registers the default client when it is missing (spec FR-016, amended); job ids stay globally unique,
and a foreign `CorrelationId` is refused (R8); `IGeneratePdfResponse.FileUrlExpiresAt` stays with 005
(R10).
