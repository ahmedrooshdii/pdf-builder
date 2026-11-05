# Data Model: Client-Scoped Templates and Jobs

Changes to the block's database (`PdfBuilderDbContext`) and to the in-process identity types. The
tables come from `specs/001-persistent-storage/data-model.md` and `specs/002-clients-and-api-keys`.
Only what changes is listed.

## `authored_templates`

| Column | Before | After |
| --- | --- | --- |
| `client_id` | – | `varchar(40) NOT NULL`, FK → `clients(id)` (no cascade: clients are never deleted) |
| `key` | primary key | part of the primary key |
| `key_lower` | unique (`ux_authored_templates_key_lower`) | unique **with `client_id`** (`ux_authored_templates_client_key_lower`) |

- **Primary key**: `(client_id, key)`.
- **Query filter** `"client"`: `client_id = <caller's ClientId>`.
- **Rules**: keys are unique per client, ignoring case (FR-006). A key may also exist in other
  clients. The owner is set on insert and never updated (FR-001).

## `pdf_jobs`

| Column | Before | After |
| --- | --- | --- |
| `client_id` | – | `varchar(40) NOT NULL`, FK → `clients(id)` |
| `job_id` | primary key | unchanged: job ids stay globally unique (research R8) |

- **Query filter** `"client"`: `client_id = <caller's ClientId>`.
- **Index**: none added. Lookups are by `job_id` (primary key), and the retention sweeper uses the
  existing `ix_pdf_jobs_last_saved_at` across all clients.
- **Generated file**: owned through the job (`file_id`, `client_id`). No `files` table (R12).
- **Lifecycle change**: a job submitted straight to the bus is recorded as `Pending` when work starts
  (`ClaimAsync`), instead of first at completion (R8). The status values and their transitions are
  unchanged: `Pending → Succeeded | Failed`.

## Migrations

| Order | Migration | Content |
| --- | --- | --- |
| 1 | `AddClientOwner` | `client_id varchar(40) NULL` on both tables |
| – | *startup step* | 001 file import (with owner), then `ClientOwnerAssignment`: `UPDATE … SET client_id = @default WHERE client_id IS NULL`, under an advisory lock, and registering the default client if it is missing (R6, R7) |
| 2 | `RequireClientOwner` | `NOT NULL`, foreign keys, new primary key and unique index on `authored_templates` |

`DatabaseStartup` stops between the two only when `RequireClientOwner` is pending (R6).

## `clients` (from 002, unchanged schema)

The upgrade may insert one row: `(id = DefaultClientId, name = DefaultClientId, created_at = now,
disabled_at = NULL)` when existing data needs an owner and that client is not registered (FR-016).

## Records and domain

| Type | Change |
| --- | --- |
| `AuthoredTemplateRecord` | `+ string ClientId`; `From(template, clientId)` |
| `PdfJobRecord` | `+ string ClientId`; `From(job, clientId, savedAt)`; `CopyFrom` leaves `ClientId` alone |
| `AuthoredTemplate`, `PdfJob` (Domain) | unchanged: ownership is a storage and identity concern, not a rule of the template or the job |
| `JobIdTakenException` (Domain) | **new**, `PdfBuilderException`, permanent, `VALIDATION_FAILED` |
| `ClientRequiredException` (Infrastructure) | **new**: a store write with no client; mapped to `400 VALIDATION_FAILED` |

## Identity (`Blocks.ServiceDefaults`)

| Type | Change |
| --- | --- |
| `ICurrentClient` | unchanged |
| `IClientContext` | **new**: `void ActAs(string clientId)`, for work that does not come from an HTTP request |
| `CurrentClient` | **new**, scoped, registered as `ICurrentClient` and `IClientContext`: the `ActAs` client if set, otherwise `HttpCurrentClient` |
| `BlocksBusOptions` | **new**: `Blocks:Bus:DefaultClientId` (`string?`, client id rule, validated on start) |
| `.RequireActingClient()` | **new** endpoint filter: 400 / 404 for the operator without a client or with an unknown one (R4) |

## Message (`PdfBuilder.Contracts` 1.2.0)

| Type | Member |
| --- | --- |
| `IGeneratePdfRequest` | `+ string? ClientId { get; }`: null → the default client |
| `GeneratePdfRequest` | `+ string? ClientId { get; set; }` |
| `PdfErrorCodes` | `+ ClientDisabled = "CLIENT_DISABLED"`, included in `All` |
