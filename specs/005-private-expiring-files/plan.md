# Implementation Plan: Private, Expiring Files

**Branch**: `005-private-expiring-files` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/005-private-expiring-files/spec.md`

## Summary

Today a job's file link is `…/files/{fileId}`, open to anyone, forever, and files are never deleted.
This feature adds the files stud to `Blocks.ServiceDefaults`. It has four parts: `Blocks:Files`
options, an HMAC-SHA256 `FileLinkSigner`, a `.RequireFileSignature()` endpoint filter, and a switch
(`RequireSignature`, off in this release). While the switch is off, links are issued and accepted as
before. While it is on, every link is signed, expires after `UrlLifetime`, and unsigned links are
refused. A failing link always gets the same bare `404` as a missing file.

PDF Builder makes links per read in one place (`PdfFileLinks`), from the file id, so status reads, the
new `GET /api/pdf/jobs/{id}/file-url` and the bus reply each get a fresh link. A new member,
`IGeneratePdfResponse.FileUrlExpiresAt`, ships in Contracts 1.3.0. The console stops using links: it
downloads through the new authenticated `GET /api/pdf/jobs/{id}/file`.

Files are deleted `RetentionDays` after creation by a 15-minute `FileRetentionSweeper`. It marks jobs
first, then deletes files on disk. Reads decide "expired" themselves: past retention, marked, or not in
storage. A succeeded job is kept as long as its file, then for one job retention period more
(clarification A). `pdf_jobs` gains `file_created_at` and `file_expired_at` and loses `file_url`.
Details: [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 10 (`global.json`); TypeScript / Angular for the console (`web/`).

**Primary Dependencies**: ASP.NET Core minimal APIs, EF Core 10 (Npgsql), MassTransit 8.5, MediatR,
`System.Security.Cryptography` (HMAC, `FixedTimeEquals`). **No new packages.**

**Storage**: PostgreSQL (the block's own database): one migration, `AddFileLifetime`
([data-model.md](data-model.md)). PDFs stay on local disk behind `IFileStorage`.

**Testing**: xUnit v3 + NSubstitute; `FakeTimeProvider` for expiry and retention. Database and API
tests are opt-in with `PDFBUILDER_RUN_DATABASE_TESTS=1` (Testcontainers, or `PDFBUILDER_TEST_POSTGRES`)
and on in CI. Console: Vitest/Angular testing as in `web/`.

**Target Platform**: Linux containers, `linux/amd64` and `linux/arm64`.

**Project Type**: Web service (API plus RabbitMQ consumer) with an Angular operator console.

**Performance Goals**: SC-009, within 10% for downloads and status reads. Signing or checking a link
is one HMAC over about 45 bytes (microseconds). Status reads of a succeeded job add one `stat` (R5).

**Constraints**: additive contracts only (Principle VII), so links stay as before while the switch is
off. No secret or signature in logs (FR-007). One manual upgrade step (SC-007). Every file is deleted
within one hour of its retention ending (FR-016). Every instance accepts every other instance's links
(FR-024).

**Scale/Scope**: tens of clients. Three changed endpoints (`/files/{id}`, job status, the bus reply)
and two new ones. One table changed, two background sweepers, four settings. The console changes the
job detail and the jobs table.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Pre-research | Post-design |
| --- | --- | --- | --- |
| I | One codebase, no forks | ✅ | ✅ Four new settings with defaults (one required, with no default because it is a secret), listed in `docs/configuration.md` and `04-backend.md#configuration` ([contracts/configuration.md](contracts/configuration.md)). No deployment value in code or migration. |
| II | Block stands alone | ✅ | ✅ Files, keys and jobs stay in the block's own disk, configuration and database. |
| III | Generic, no product knowledge | ✅ | ✅ No new table. `pdf_jobs` keeps `client_id` and its filter. No product vocabulary. |
| IV | Blocks never call each other | ✅ | ✅ This implements the convention that lets blocks compose ("a block returns a signed file URL"); nothing calls another block. |
| V | Same studs | ✅ | ✅ Implements the "signed, expiring file URLs; files deleted after a configured retention" stud in `Blocks.ServiceDefaults/Files` (R2). Another client's job answers 404 on both new endpoints. No new list. The fresh link is a `GET` that creates nothing, so it needs no `Idempotency-Key` (R4). |
| VI | Identity from claims only | ✅ | ✅ The job-file endpoints use `ICurrentClient` through the store's filter. The signature filter reads the query, but it checks a capability, not identity, and sits in the stud layer, as the API-key handler does. |
| VII | Contracts change additively | ✅ | ✅ `IGeneratePdfResponse.FileUrlExpiresAt` (optional), Contracts 1.3.0, `CP0006` suppression, `CHANGELOG.md`. HTTP: two new response members and two new endpoints. `FileUrl` keeps its meaning while the switch is off (R3, spec amended). Deletion after retention is a service behaviour that is announced. |
| VIII | Secrets unrecoverable | ✅ | ✅ The signing key lives only in configuration and in the signer's memory. Start-up messages never include it. It is compared and used in constant time. Signatures are kept out of logs, enforced by a test (R10). |
| IX | Layering by tooling | ✅ | ✅ Application loses URL building and gains the `JobFiles` service and two port members. Api composes the links. The stud has no reference to the block. The architecture test's `IgnoreQueryFilters` allow-list gains `MarkExpiredFilesOfAllClientsAsync`. Console: `shared/util` gains `saveBlob`, and features stay independent. |
| X | Any hardware, offline | ✅ | ✅ No external host. HMAC comes from the base library. |

**Result**: passes. No violations, so Complexity Tracking is empty.

Definition-of-done notes:
- The cross-client test covers both new endpoints. They are added to `CrossClientIsolationTests`'
  enumeration of job endpoints.
- The upgrade is announced in `CHANGELOG.md`, including that the next release turns
  `RequireSignature` on by default.

## Project Structure

### Documentation (this feature)

```text
specs/005-private-expiring-files/
├── spec.md
├── plan.md                    # this file
├── research.md                # R1–R13
├── data-model.md              # pdf_jobs columns, PdfJob, ports, stud types, message
├── quickstart.md
├── contracts/
│   ├── http-files.md          # /files, job status, file-url, file, error codes
│   ├── message-contract.md    # IGeneratePdfResponse.FileUrlExpiresAt, Contracts 1.3.0
│   └── configuration.md       # Blocks:Files:*, logging pin, deployment files, upgrade SQL
├── checklists/requirements.md
└── tasks.md                   # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Blocks.ServiceDefaults/
│   └── Files/                                   NEW  the files stud (R1–R3, R7)
│       ├── BlocksFilesOptions.cs                     SigningKey, UrlLifetime, RetentionDays, RequireSignature
│       ├── BlocksFilesOptionsValidator.cs            names the key, never the value
│       ├── FileLinkSigner.cs                         Sign / Check, FixedTimeEquals
│       ├── FileSignatureEndpointFilter.cs            .RequireFileSignature(): bare 404 unless allowed
│       └── BlocksFilesExtensions.cs                  AddBlocksFiles()
├── PdfBuilder.Contracts/
│   ├── IGeneratePdfResponse.cs                  + FileUrlExpiresAt; FileUrl doc
│   ├── CompatibilitySuppressions.xml            + CP0006 FileUrlExpiresAt (both TFMs)
│   └── PdfBuilder.Contracts.csproj              Version 1.3.0-local
├── PdfBuilder.Domain/
│   ├── Jobs/PdfJob.cs                           − FileUrl; + FileCreatedAt, FileExpired, WithFileExpired(); Succeed signature
│   └── Exceptions/JobFileExceptions.cs          NEW  JobFileErrorCodes, JobHasNoFileException, FileExpiredException
├── PdfBuilder.Application/
│   ├── Abstractions/IFileStorage.cs             StoredFile(FileId, SizeBytes, CreatedAt); + ExistsAsync, DeleteCreatedBeforeAsync
│   ├── Abstractions/IPdfJobStore.cs             + MarkFileMissingAsync
│   ├── Features/GeneratePdf/…                   result carries FileCreatedAt instead of FileUrl
│   └── Features/JobFiles/JobFiles.cs            NEW  GetJobAsync, RequireFileAsync, OpenFileAsync (R5)
├── PdfBuilder.Infrastructure/
│   ├── Storage/LocalFileStorage.cs              CreatedAt = last-write time; ExistsAsync; DeleteCreatedBeforeAsync;
│   │                                            FileShare.Delete on read; no URL
│   ├── Storage/FileRetentionSweeper.cs          NEW  every 15 min: mark jobs (all clients), then delete files (R6)
│   ├── Jobs/EfPdfJobStore.cs                    visibility rule (R8); FileExpired on read; MarkFileMissingAsync
│   ├── Jobs/JobRetentionSweeper.cs              same visibility rule
│   ├── Persistence/Records/PdfJobRecord.cs      − FileUrl; + FileCreatedAt, FileExpiredAt
│   ├── Persistence/Configurations/PdfJobConfiguration.cs   check constraint, index
│   ├── Persistence/PersistenceLog.cs            + files swept / marked, sweep failed
│   ├── Persistence/Migrations/                  + AddFileLifetime
│   └── DependencyInjection.cs                   register sweeper; storage options unchanged
├── PdfBuilder.Api/
│   ├── Program.cs                               AddBlocksFiles(); PdfFileLinks
│   ├── Files/PdfFileLinks.cs                    NEW  absolute link from PublicBaseUrl + signer + switch
│   ├── Endpoints/FileEndpoints.cs               .RequireFileSignature(); response headers (R11)
│   ├── Endpoints/PdfJobEndpoints.cs             status via JobFiles + PdfFileLinks; + /file-url, + /file
│   ├── Endpoints/ApiExceptionHandler.cs         JobHasNoFile → 409, FileExpired → 410
│   ├── Messaging/GeneratePdfConsumer.cs         reply FileUrl/FileUrlExpiresAt from PdfFileLinks
│   └── appsettings.json                         + Blocks:Files defaults (no key); Hosting.Diagnostics: Warning
tools/Blocks.Cli/Program.cs                      + file-signing-key

web/src/app/
├── shared/util/save-blob.ts                     NEW  save a Blob under a file name
└── features/pdf-jobs/
    ├── data-access/pdf-jobs.api.ts              − fileUrl/FILE_ID_PATTERN; downloadFile(jobId)
    ├── data-access/pdf-job.models.ts            + fileUrlExpiresAt, fileExpired; + error codes; retention note
    ├── feature/job-detail/job-detail.page.*     Download via blob; Preview via jobId; "File expired" state
    ├── feature/jobs/jobs.page.ts                handles (download) from the table
    └── ui/jobs-table/jobs-table.*               (download) output instead of downloadUrlFor; expired state

tests/
├── Blocks.ServiceDefaults.Tests/
│   ├── FileLinkSignerTests.cs                   NEW  round trip; each part altered; expiry boundary; ceil; malformed inputs
│   ├── BlocksFilesOptionsTests.cs               NEW  missing/short/non-base64 key; ranges; message has no value
│   ├── FileSignatureEndpointFilterTests.cs      NEW  the R3 table, both switch positions
│   └── ArchitectureTests.cs                     + MarkExpiredFilesOfAllClientsAsync in the allow-list
├── PdfBuilder.Application.Tests/
│   ├── JobFilesTests.cs                         NEW  not found / no file / expired / missing marks / ok
│   ├── GeneratePdfCommandHandlerTests.cs        result carries FileCreatedAt
│   └── ContractsTests.cs                        + FileUrlExpiresAt round trip and absent → null
├── PdfBuilder.Infrastructure.Tests/
│   ├── LocalFileStorageTests.cs                 + CreatedAt = last-write; Exists; DeleteCreatedBefore; delete while open
│   ├── FileRetentionSweeperTests.cs             NEW  marks then deletes; young files kept; orphans; idempotent; two at once
│   ├── EfPdfJobStoreTests.cs                    + FR-019 visibility; FileExpired rule; MarkFileMissing per client
│   ├── JobRetentionSweeperTests.cs              + keeps succeeded jobs while file kept, removes after
│   └── Persistence/MigrationTests.cs            + AddFileLifetime from RequireClientOwner, backfill
└── PdfBuilder.Api.Tests/
    ├── SignedLinkApiTests.cs                    NEW  US1 end to end; switch off/on; identical 404s; headers
    ├── JobFileEndpointsTests.cs                 NEW  file-url and file: 200/404/409/410; console session download
    ├── CrossClientIsolationTests.cs             + the two new endpoints
    ├── GeneratePdfConsumerPersistenceTests.cs   + reply FileUrl/FileUrlExpiresAt in both modes
    ├── FileLinkLeakTests.cs                     NEW  no sig= or key in logs and problems (SC-008)
    └── Support/ApiFactory.cs                    sets Blocks:Files:SigningKey

compose.yaml, deploy/compose.prod.yaml, deploy/README.md, deploy/huggingface/space.md   signing key
samples/requests.http, samples/README.md         Files section: fresh link, authenticated download
```

**Structure Decision**: the reusable parts go in `Blocks.ServiceDefaults/Files`, beside the
authentication and identity studs, ready for extraction in 011: options, signer, signature filter. PDF
Builder keeps what is its own. `PdfFileLinks` knows its public base URL and its `/files` path. The
retention rules live in its job store and sweepers, and its file storage adapter. Application gets one
small service, `JobFiles`, so the three paths that hand out a file share one set of rules.

### Documentation updated in this feature

| Document | Change |
| --- | --- |
| `docs/configuration.md` | New `Blocks:Files` section; `Storage:PublicBaseUrl` note; `JobStore:RetentionHours` for succeeded jobs; logging pin |
| `docs/operations.md` | Upgrading to private files: the key, the switch, the first sweep, backup before upgrading. Rotating the signing key. Proxy access logs. Disk use bounded by retention. "Known gaps": object storage still needed for several instances |
| `docs/message-contracts.md` | `FileUrlExpiresAt`; "Downloading the PDF": signed links, fresh links, retention |
| `docs/architecture.md` | Claim Check: links are made per read and signed; the file sweeper |
| `docs/blocks/04-backend.md` | Files as built: switch semantics, read-time expiry, job kept with its file, 15-minute sweep, `410 FILE_EXPIRED` / `409 JOB_HAS_NO_FILE`, query redaction for future telemetry; endpoints table; contract table → 1.3.0 |
| `README.md`, `samples/README.md` | Links expire when signing is on; how to get a fresh one |
| `CHANGELOG.md` | Contracts 1.3.0; service: private files, retention, new endpoints, upgrade step, next release turns signing on |

## Complexity Tracking

No constitution violations.

**Deviations from `docs/blocks/04-backend.md`** (not violations; the doc is updated to match):
- The switch decides which links are *issued* as well as which are accepted (R3). The doc already
  implied this with "`FileUrlExpiresAt` … null while signing is off".
- The sweeper runs every 15 minutes, not "a background sweeper" at an unstated interval. This keeps
  FR-016's one-hour bound.
- A succeeded job is kept as long as its file (FR-019). This resolves the doc's "the job keeps its
  record", which the 24-hour job retention did not allow.
- `file_url` is dropped (R13).
