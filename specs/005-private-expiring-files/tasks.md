---

description: "Task list for feature 005: Private, Expiring Files"
---

# Tasks: Private, Expiring Files

**Input**: Design documents from `/specs/005-private-expiring-files/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The constitution's definition of done requires tests where each rule lives, one API
test per new endpoint's happy path and main refusal, and a cross-client test for anything that reads
client data. SC-001 to SC-003, SC-005 and SC-008 can only be proven by tests, and the plan names the
test files. Database-backed and whole-pipeline tests are opt-in with `PDFBUILDER_RUN_DATABASE_TESTS=1`
(`PDFBUILDER_TEST_POSTGRES` points them at an existing server).

**Organization**: Tasks are grouped by user story (US1–US5 from spec.md). Foundational changes the
schema and the domain so that links are made from the file id. It keeps today's behaviour: links stay
unsigned, nothing expires, nothing is deleted. Each story then adds its own behaviour on top.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to
- Paths are relative to the repository root

## Path Conventions

- Shared stud package: `src/Blocks.ServiceDefaults/` (new folder `Files/`)
- Block projects: `src/PdfBuilder.Contracts/`, `src/PdfBuilder.Domain/`, `src/PdfBuilder.Application/`, `src/PdfBuilder.Infrastructure/`, `src/PdfBuilder.Api/`
- Console: `web/src/app/`
- Tests: `tests/Blocks.ServiceDefaults.Tests/`, `tests/PdfBuilder.Application.Tests/`, `tests/PdfBuilder.Infrastructure.Tests/`, `tests/PdfBuilder.Api.Tests/`
- Rules for every task:
  - nothing under `src/Blocks.*` references a `PdfBuilder.*` assembly, and `PdfBuilder.Application` gains no reference to `Blocks.*` (Principle IX)
  - `PdfBuilder.Application` does not build or sign URLs; links are made only in `src/PdfBuilder.Api/Files/PdfFileLinks.cs` (R2)
  - the signing key and full signed links never reach a log line, problem detail, exception message or start-up error (FR-007, R10)
  - every failing signed link answers the same bare `Results.NotFound()` as a missing file (FR-004)
  - another client's job answers exactly as a missing one, never 403 (Principle V)
  - `IgnoreQueryFilters` appears only in the three methods already allowed plus `MarkExpiredFilesOfAllClientsAsync` (R6)
  - when running anything locally, use `ASPNETCORE_ENVIRONMENT=Production` and the compose broker: Development loads user-secrets that point at the real hosted broker
  - keep each file's existing line endings (check with `git ls-files --eol`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The `Blocks:Files` settings, which every story reads, and the test hosts that must supply them.

- [X] T001 [P] Create `src/Blocks.ServiceDefaults/Files/BlocksFilesOptions.cs`: constant `Section = "Blocks:Files"`; constants for each key (`SigningKeyKey = "Blocks:Files:SigningKey"`, etc.); properties `string? SigningKey`, `TimeSpan UrlLifetime = 01:00:00`, `int RetentionDays = 30`, `bool RequireSignature = false`; an internal `byte[] SigningKeyBytes` decoded once after validation. Create `src/Blocks.ServiceDefaults/Files/BlocksFilesOptionsValidator.cs` (`IValidateOptions<BlocksFilesOptions>`): `SigningKey` "none, **required**", "Base64 of at least 32 random bytes"; `UrlLifetime` "`00:01:00` to `7.00:00:00`"; `RetentionDays` "1 to 3650". Every message names the key and its environment variable (`Blocks__Files__SigningKey`, …) and never contains the configured value; the missing-key message suggests `dotnet run --project tools/Blocks.Cli -- file-signing-key` and `openssl rand -base64 32` (contracts/configuration.md)
- [X] T002 Create `src/Blocks.ServiceDefaults/Files/BlocksFilesExtensions.cs` with `AddBlocksFiles(this IServiceCollection)`: bind `Blocks:Files`, register the validator with `TryAddEnumerable`, `ValidateOnStart()`, `TryAddSingleton(TimeProvider.System)`. Call it from `src/PdfBuilder.Api/Program.cs` after `AddBlocksAuthentication`. Add `"Blocks": { "Files": { "UrlLifetime": "01:00:00", "RetentionDays": 30, "RequireSignature": false } }` to `src/PdfBuilder.Api/appsettings.json` (no key)
- [X] T003 [P] Create `tests/Blocks.ServiceDefaults.Tests/BlocksFilesOptionsTests.cs`: a valid 32-byte key passes; missing key, non-base64 key and a 31-byte key each fail with a message naming `Blocks:Files:SigningKey` and **not** containing the configured string; `UrlLifetime` `00:00:59` and `7.00:00:01` fail, `00:01:00` and `7.00:00:00` pass; `RetentionDays` 0 and 3651 fail, 1 and 3650 pass
- [X] T004 [P] Set `Blocks:Files:SigningKey` to a fixed test key in `tests/PdfBuilder.Api.Tests/Support/ApiFactory.cs` (beside `AdminKeyHashKey`), exposed as `ApiFactory.FileSigningKey` and overridable through the existing `settings` parameter; set `Blocks__Files__SigningKey` in `compose.yaml` to a fixed base64 key with the comment `# development only: generate your own for any real deployment`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Jobs record when their file was created and whether it expired. Links are made per read
from the file id in one place, and `file_url` is gone. At the end of this phase the behaviour is
identical to today: links are unsigned and nothing expires.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 [P] In `src/PdfBuilder.Domain/Jobs/PdfJob.cs`: remove `FileUrl`; add `DateTimeOffset? FileCreatedAt` and `bool FileExpired`; change `Succeed(fileId, fileUrl, sizeBytes, completedAt)` to `Succeed(string fileId, long sizeBytes, DateTimeOffset fileCreatedAt, DateTimeOffset completedAt)`; `Fail` also clears `FileCreatedAt` and `FileExpired`; add `WithFileExpired()`, which throws `InvalidOperationException` unless `Status == Succeeded` (data-model.md)
- [X] T006 [P] Create `src/PdfBuilder.Domain/Exceptions/JobFileExceptions.cs`: `JobFileErrorCodes` with `JobHasNoFile = "JOB_HAS_NO_FILE"` and `FileExpired = "FILE_EXPIRED"` (HTTP-only, documented like `TemplateErrorCodes`); `JobHasNoFileException()` and `FileExpiredException()` deriving from `DomainException`, not transient, with fixed messages that name no file or job id
- [X] T007 [P] In `src/PdfBuilder.Application/Abstractions/IFileStorage.cs`: `StoredFile(string FileId, long SizeBytes, DateTimeOffset CreatedAt)` (remove `Url`); add `Task<bool> ExistsAsync(string fileId, CancellationToken)` ("`false` for an id that is not one the storage issues") and `Task<int> DeleteCreatedBeforeAsync(DateTimeOffset cutoff, CancellationToken)` ("Idempotent; skips files it cannot delete now"). In `src/PdfBuilder.Application/Abstractions/IPdfJobStore.cs` add `Task MarkFileMissingAsync(Guid jobId, CancellationToken)` (sets `file_expired_at` if unset, caller's own job only)
- [X] T008 Update `src/PdfBuilder.Application/Features/GeneratePdf/GeneratePdfCommand.cs` to `GeneratePdfResult(string FileId, long SizeBytes, DateTimeOffset FileCreatedAt)` and `GeneratePdfCommandHandler.cs` to fill it from `StoredFile`; update `tests/PdfBuilder.Application.Tests/GeneratePdfCommandHandlerTests.cs` (depends on T007)
- [X] T009 In `src/PdfBuilder.Infrastructure/Storage/LocalFileStorage.cs`: inject `TimeProvider`; in `SaveAsync` take `createdAt` from the clock before writing, write and move as today, then `File.SetLastWriteTimeUtc(path, createdAt.UtcDateTime)` and return `StoredFile(fileId, size, createdAt)`; drop URL building (`PublicBaseUrl` stays in `StorageOptions` for the Api). Implement `ExistsAsync` (id pattern check, then `File.Exists`). Open reads with `FileShare.Read | FileShare.Delete`. Leave `DeleteCreatedBeforeAsync` throwing `NotImplementedException` until T042. Update `tests/PdfBuilder.Infrastructure.Tests/LocalFileStorageTests.cs`: `CreatedAt` equals the file's last-write time; `ExistsAsync` true, false for missing and malformed ids (depends on T007)
- [X] T010 In `src/PdfBuilder.Infrastructure/Persistence/Records/PdfJobRecord.cs`: remove `FileUrl`; add `DateTimeOffset? FileCreatedAt` and `DateTimeOffset? FileExpiredAt`; map both in `From`/`CopyFrom` (`CopyFrom` never clears a set `FileExpiredAt`); `ToDomain` uses the new `Succeed` signature. In `src/PdfBuilder.Infrastructure/Persistence/Configurations/PdfJobConfiguration.cs`: check constraint `ck_pdf_jobs_file_created_at` "`status <> 'Succeeded' OR file_created_at IS NOT NULL`" and index `ix_pdf_jobs_file_created_at` on `file_created_at` (depends on T005)
- [X] T011 Add migration `AddFileLifetime` under `src/PdfBuilder.Infrastructure/Persistence/Migrations/` (`dotnet ef migrations add AddFileLifetime`), then edit its `Up` into this order: add `file_created_at` and `file_expired_at` (`timestamptz`, null) → `migrationBuilder.Sql("UPDATE pdf_jobs SET file_created_at = completed_at WHERE status = 'Succeeded';")` → check constraint and index → drop `file_url`. `Down` restores `file_url` as nullable `text` and drops the rest (depends on T010; data-model.md, contracts/configuration.md#upgrade)
- [X] T012 In `src/PdfBuilder.Infrastructure/Jobs/EfPdfJobStore.cs`: implement `MarkFileMissingAsync` with a filtered `ExecuteUpdateAsync` setting `FileExpiredAt = now` where `FileExpiredAt == null`; inject `IOptions<BlocksFilesOptions>`; on every read, set `FileExpired` on a succeeded job when `FileExpiredAt != null || FileCreatedAt < now − RetentionDays` (R5). Leave the visibility rule unchanged until T043 (depends on T010, T002)
- [X] T013 Create `src/PdfBuilder.Application/Features/JobFiles/JobFiles.cs` (scoped, registered in `src/PdfBuilder.Application/DependencyInjection.cs`): `GetJobAsync(jobId)` returns the job, and for a succeeded job not yet expired asks `IFileStorage.ExistsAsync`, calling `MarkFileMissingAsync` and returning `WithFileExpired()` when it is gone; `RequireFileAsync(jobId)` returns `null` for no job, throws `JobHasNoFileException` unless succeeded, throws `FileExpiredException` when expired, else returns the file id; `OpenFileAsync(jobId)` does the same, then opens the stream and, when `OpenReadAsync` returns null, marks the file missing and throws `FileExpiredException` (data-model.md, R5) (depends on T005–T007)
- [X] T014 Create `src/PdfBuilder.Api/Files/PdfFileLinks.cs` (singleton): `FileLink For(string fileId)` returning `(string Url, DateTimeOffset? ExpiresAt)` = `({Storage:PublicBaseUrl}/files/{fileId}, null)` for now; the signed branch comes in T024. Register it in `Program.cs`
- [X] T015 In `src/PdfBuilder.Api/Endpoints/PdfJobEndpoints.cs`: `GetAsync` reads through `JobFiles.GetJobAsync`; `JobStatusResponse` drops the domain `FileUrl` and gets `fileUrl`/`fileUrlExpiresAt` from `PdfFileLinks` when the job succeeded and `FileExpired` is false, both null otherwise, and adds `bool FileExpired` (contracts/http-files.md). In `src/PdfBuilder.Api/Messaging/GeneratePdfConsumer.cs`: `job.Succeed(result.FileId, result.SizeBytes, result.FileCreatedAt, now)` and the reply's `FileUrl` from `PdfFileLinks.For(result.FileId).Url`. In `src/PdfBuilder.Api/Endpoints/ApiExceptionHandler.cs`: `JobHasNoFileException` → `409` "Job has no file", `FileExpiredException` → `410` "File expired" (depends on T012–T014)
- [X] T016 [P] Extend `tests/PdfBuilder.Infrastructure.Tests/Persistence/MigrationTests.cs`: from the `RequireClientOwner` schema with a `Succeeded` row (with `file_url`) and a `Failed` row, migrate to `AddFileLifetime`; the succeeded row's `file_created_at` equals its `completed_at`, the failed row's is null, `file_url` no longer exists, and inserting a `Succeeded` row without `file_created_at` violates `ck_pdf_jobs_file_created_at` (depends on T011)
- [X] T017 Fix the remaining compile and test breaks from `FileUrl`/`StoredFile`/`Succeed` in `tests/PdfBuilder.Infrastructure.Tests/EfPdfJobStoreTests.cs`, `tests/PdfBuilder.Infrastructure.Tests/MultiInstanceTests.cs`, `tests/PdfBuilder.Api.Tests/GeneratePdfConsumerPersistenceTests.cs`, `tests/PdfBuilder.Api.Tests/GeneratePdfConsumerClientTests.cs`, `tests/PdfBuilder.Api.Tests/ActingClientTests.cs`, `tests/PdfBuilder.Api.Tests/AuthenticationPipelineTests.cs` and `tests/PdfBuilder.Api.Tests/ApiKeySecurityTransformerTests.cs`. Expectations of an unsigned `…/files/{fileId}` link stay as they are
- [X] T018 Run `dotnet build` and `dotnet test` with `PDFBUILDER_RUN_DATABASE_TESTS=1`: everything green, with no behaviour change visible to any caller beyond the two new null/false response members

**Checkpoint**: links are made per read from the file id. The schema has the file lifetime columns.
Behaviour is unchanged.

---

## Phase 3: User Story 1 - A link works for its holder, and only until it expires (Priority: P1) 🎯 MVP

**Goal**: with `RequireSignature=true`, every issued link is signed and expires after `UrlLifetime`.
Altered, expired and unsigned links get the same bare 404. Signatures stay out of logs.

**Independent Test**: quickstart Scenario 1; `SignedLinkApiTests`.

### Tests for User Story 1

- [X] T019 [P] [US1] Create `tests/Blocks.ServiceDefaults.Tests/FileLinkSignerTests.cs` with a `FakeTimeProvider`: `Sign` then `Check` → `Valid`; expiry is `ceil(now + lifetime)` in Unix seconds; at `now == expires` → `Invalid`, one second before → `Valid`; each of these → `Invalid`: changed file id, `expires` + 1, one changed `sig` character, `sig` from another key, `sig` with padding, 42 or 44 characters, non-numeric or 13-digit `expires`, repeated parameter, only one of the two present; neither present → `Unsigned`; two signers with the same key accept each other's links (FR-024)
- [X] T020 [P] [US1] Create `tests/Blocks.ServiceDefaults.Tests/FileSignatureEndpointFilterTests.cs` on the stud test host (`Support/StudHost.cs`): a dummy `/files/{fileId}` endpoint with `.RequireFileSignature()`; the full R3 table for both switch positions (unsigned / valid / expired / altered / one parameter), and every refusal byte-identical to the dummy endpoint's own `NotFound`
- [X] T021 [P] [US1] Create `tests/PdfBuilder.Api.Tests/SignedLinkApiTests.cs` (switch on, `UrlLifetime` 2 minutes, `FakeTimeProvider` in the factory): submit and complete a job; status `fileUrl` has `expires` and `sig`, `fileUrlExpiresAt` matches `expires`; a GET with no key → `200`, `application/pdf`, `Cache-Control: private, no-store`, `Referrer-Policy: no-referrer`, `X-Robots-Tag: noindex`; swapping in another existing file's id (same client and other client), editing `expires`, editing `sig` and removing both → `404` with status, headers and body identical to `/files/` plus 32 zeros; after advancing the clock past expiry → `404`; two status reads a minute apart return different `expires`, both working (FR-005, FR-012)
- [X] T022 [P] [US1] Extend `tests/PdfBuilder.Api.Tests/GeneratePdfConsumerPersistenceTests.cs`: with the switch on, the bus reply's `FileUrl` is signed and `FileUrlExpiresAt` equals its `expires`; a failure reply has both null. Extend `tests/PdfBuilder.Application.Tests/ContractsTests.cs`: `FileUrlExpiresAt` round-trips, and a JSON reply without it deserialises with null
- [X] T023 [P] [US1] Create `tests/PdfBuilder.Api.Tests/FileLinkLeakTests.cs`, modelled on `KeyLeakTests.cs`: every category at `Debug` except the levels pinned in `appsettings.json`; issue links, download, send altered and expired links, ask for status; captured logs, problem bodies and response headers other than the link responses themselves contain no `sig=` value and no substring of the signing key or its base64 (SC-008)

### Implementation for User Story 1

- [X] T024 [US1] Create `src/Blocks.ServiceDefaults/Files/FileLinkSigner.cs` (singleton, registered by `AddBlocksFiles`): `SignedFileQuery Sign(string fileId, DateTimeOffset now)` → `expires = ceil((now + UrlLifetime).ToUnixTimeSeconds())`, `sig = base64url(HMACSHA256(key, UTF8("{fileId}.{expires}")))` without padding, returns `(Query: "expires=…&sig=…", ExpiresAt)`; `FileLinkCheck Check(string fileId, IQueryCollection query, DateTimeOffset now)` → `Unsigned` when neither parameter is present; `Invalid` when either is repeated or alone, `expires` is not 1–12 digits, `sig` is not 43 base64url characters, `now >= expires`, or the bytes differ under `CryptographicOperations.FixedTimeEquals`; otherwise `Valid`. Holds the decoded key privately; no `ToString`, no logging (R1)
- [X] T025 [US1] Create `src/Blocks.ServiceDefaults/Files/FileSignatureEndpointFilter.cs` and `RequireFileSignature<TBuilder>()`: reads route value `fileId`, calls `Check`; serves `Valid`, serves `Unsigned` only when `RequireSignature` is false, otherwise returns `Results.NotFound()` (R3) (depends on T024)
- [X] T026 [US1] In `src/PdfBuilder.Api/Files/PdfFileLinks.cs`: when `RequireSignature` is true, return `{base}/files/{fileId}?{query}` with `ExpiresAt` from `FileLinkSigner.Sign(fileId, clock.GetUtcNow())`; otherwise unchanged (depends on T024)
- [X] T027 [US1] In `src/PdfBuilder.Api/Endpoints/FileEndpoints.cs`: add `.RequireFileSignature()`; keep `.AllowAnonymous()` with the comment updated to say the link is the credential; on `200` set `Cache-Control: private, no-store`, `Referrer-Policy: no-referrer`, `X-Robots-Tag: noindex` (R11); log nothing about the query (depends on T025)
- [X] T028 [US1] In `src/PdfBuilder.Contracts/IGeneratePdfResponse.cs` add `DateTimeOffset? FileUrlExpiresAt { get; }` and replace `FileUrl`'s documentation with the text in contracts/message-contract.md; add `CP0006` baseline suppressions for `P:PdfBuilder.Contracts.IGeneratePdfResponse.FileUrlExpiresAt` (net10.0 and netstandard2.0) to `src/PdfBuilder.Contracts/CompatibilitySuppressions.xml`; set `<Version>1.3.0-local</Version>` in `src/PdfBuilder.Contracts/PdfBuilder.Contracts.csproj`
- [X] T029 [US1] In `src/PdfBuilder.Api/Messaging/GeneratePdfConsumer.cs` set the reply's `FileUrl` and `FileUrlExpiresAt` from one `PdfFileLinks.For` call; `FileUrlExpiresAt = (DateTimeOffset?)null` in `RespondFailureAsync` (depends on T026, T028)
- [X] T030 [US1] Add `"Microsoft.AspNetCore.Hosting.Diagnostics": "Warning"` under `Logging:LogLevel` in `src/PdfBuilder.Api/appsettings.json` with no other change; confirm with `grep` that no `Log*` call in `src/` passes a file URL or query (R10)
- [X] T031 [US1] Run T019–T023: green

**Checkpoint**: with the switch on, US1 is complete. With it off, nothing changes.

---

## Phase 4: User Story 2 - A product gets a fresh link for its own job (Priority: P1)

**Goal**: `GET /api/pdf/jobs/{id}/file-url` returns a working link for a caller's own succeeded job.
Refusals are 404 (not yours or unknown), 409 (no file) and 410 (expired).

**Independent Test**: quickstart Scenario 2, steps 1, 2 and 4; `JobFileEndpointsTests` (file-url part).

### Tests for User Story 2

- [X] T032 [P] [US2] Create `tests/PdfBuilder.Application.Tests/JobFilesTests.cs` with NSubstitute stores: `RequireFileAsync` → `null` for no job, `JobHasNoFileException` for pending and failed, `FileExpiredException` for `FileExpired`, the file id when present; `GetJobAsync` on a succeeded job whose file `ExistsAsync` is false calls `MarkFileMissingAsync` once and returns `FileExpired = true`; no storage call for pending or failed jobs
- [X] T033 [P] [US2] Create `tests/PdfBuilder.Api.Tests/JobFileEndpointsTests.cs` (file-url section): own succeeded job → `200` with `fileUrl` that downloads the same bytes, `Cache-Control: no-store`, and with the switch on a `fileUrlExpiresAt`; an earlier link still works (FR-005); unknown id → `404` identical to `GET /api/pdf/jobs/{unknown}`; failed job → `409` `code: JOB_HAS_NO_FILE`; job whose file was deleted from disk → `410` `code: FILE_EXPIRED`, and the job then reads `fileExpired: true`; switch off → `fileUrl` unsigned, `fileUrlExpiresAt` null
- [X] T034 [P] [US2] Extend `tests/PdfBuilder.Api.Tests/CrossClientIsolationTests.cs`: client B asking `GET /api/pdf/jobs/{A's id}/file-url` gets an answer identical to an unknown id, apart from `traceId` (SC-003)

### Implementation for User Story 2

- [X] T035 [US2] In `src/PdfBuilder.Api/Endpoints/PdfJobEndpoints.cs` map `group.MapGet("/{jobId:guid}/file-url", GetFileUrlAsync)` named `GetPdfJobFileUrl`: `JobFiles.RequireFileAsync` → `404` on null, else `200 FileUrlResponse(string FileUrl, DateTimeOffset? FileUrlExpiresAt)` from `PdfFileLinks.For` with `Cache-Control: no-store`; declare `Produces<FileUrlResponse>()`, `404`, `409`, `410` for OpenAPI
- [X] T036 [US2] Run T032–T034: green

**Checkpoint**: US1 and US2 together make expiring links usable: SC-001 to SC-004.

---

## Phase 5: User Story 3 - Files are deleted after the retention period (Priority: P2)

**Goal**: files older than `RetentionDays` are deleted within an hour. Their jobs report
`fileExpired`. Succeeded jobs are kept as long as their file, then for one job retention period more
(FR-019).

**Independent Test**: quickstart Scenario 3; `FileRetentionSweeperTests`.

### Tests for User Story 3

- [X] T037 [P] [US3] Extend `tests/PdfBuilder.Infrastructure.Tests/LocalFileStorageTests.cs`: `DeleteCreatedBeforeAsync` deletes `*.pdf` older than the cutoff and keeps younger ones; deletes `*.tmp` older than an hour; returns the count; a second call returns 0; a file deleted between enumeration and delete is skipped; a reader that opened a file before the delete reads it to the end (on Windows the delete is skipped and the file remains)
- [X] T038 [P] [US3] Create `tests/PdfBuilder.Infrastructure.Tests/FileRetentionSweeperTests.cs` (database): with jobs of two clients, one sweep sets `file_expired_at` on succeeded jobs whose `file_created_at` is before the cutoff, for both clients, and leaves younger ones; then the storage delete runs with the same cutoff (marking happens before deletion: assert call order); a storage failure is logged and the next sweep completes; two sweepers running at once both finish without error
- [X] T039 [P] [US3] Extend `tests/PdfBuilder.Infrastructure.Tests/EfPdfJobStoreTests.cs`: a succeeded job with a kept file is readable after `RetentionHours` has passed since its last save; it is readable, `FileExpired = true`, for `RetentionHours` after its file expired (by `file_created_at + RetentionDays`, and separately by `file_expired_at`), then not; a failed job keeps the old rule; `MarkFileMissingAsync` affects only the caller's own job and never moves an existing `file_expired_at`; shortening `RetentionDays` makes a read report `FileExpired` at once
- [X] T040 [P] [US3] Extend `tests/PdfBuilder.Infrastructure.Tests/JobRetentionSweeperTests.cs`: the sweeper keeps exactly the rows that T039 says are readable and deletes the rest
- [X] T041 [P] [US3] Add to `tests/PdfBuilder.Api.Tests/JobFileEndpointsTests.cs`: after the sweep (or deleting the file by hand), `GET /api/pdf/jobs/{id}` → `fileExpired: true`, `fileUrl: null`, `fileUrlExpiresAt: null`, `fileId` still present; an old signed link → `404`

### Implementation for User Story 3

- [X] T042 [US3] Implement `DeleteCreatedBeforeAsync` in `src/PdfBuilder.Infrastructure/Storage/LocalFileStorage.cs`: enumerate the two-hex-character shard directories under the root; delete `*.pdf` whose last-write time is before the cutoff and `*.tmp` older than one hour; ignore `FileNotFoundException`, `DirectoryNotFoundException`, `IOException` and `UnauthorizedAccessException` per file; return the number deleted (R6)
- [X] T043 [US3] Extract the visibility rule (R8) into one private expression in `src/PdfBuilder.Infrastructure/Jobs/EfPdfJobStore.cs`, used by `GetAsync`: `last_saved_at >= now − RetentionHours OR (status = 'Succeeded' AND file_created_at >= now − RetentionDays − RetentionHours AND (file_expired_at IS NULL OR file_expired_at >= now − RetentionHours))`. Expose it to `src/PdfBuilder.Infrastructure/Jobs/JobRetentionSweeper.cs` (internal static), which deletes rows where it is false
- [X] T044 [US3] Create `src/PdfBuilder.Infrastructure/Storage/FileRetentionSweeper.cs`, a `BackgroundService` shaped like `JobRetentionSweeper`, with a 15-minute `PeriodicTimer` and the first run at start-up. Each run, in order: `MarkExpiredFilesOfAllClientsAsync` (`IgnoreQueryFilters([PdfBuilderDbContext.ClientFilter])`, `ExecuteUpdateAsync` setting `FileExpiredAt = now` where `Status == "Succeeded" && FileExpiredAt == null && FileCreatedAt < cutoff`), then `IFileStorage.DeleteCreatedBeforeAsync(cutoff)`. Failures are logged and the next tick continues. Add `FilesMarkedExpired`, `FilesSwept` and `FileSweepFailed` to `src/PdfBuilder.Infrastructure/Persistence/PersistenceLog.cs` with counts only, and register the sweeper in `src/PdfBuilder.Infrastructure/DependencyInjection.cs` (R6)
- [X] T045 [US3] Add `"MarkExpiredFilesOfAllClientsAsync"` to `MethodsAllowedToIgnoreTheClientFilter` in `tests/Blocks.ServiceDefaults.Tests/ArchitectureTests.cs`, with a comment ("the file sweeper has no caller")
- [X] T046 [US3] Run T037–T041 and `ArchitectureTests`: green

**Checkpoint**: disk use is bounded by `RetentionDays`. Jobs report expired files. FR-019 holds.

---

## Phase 6: User Story 4 - The operator downloads from the console without links (Priority: P2)

**Goal**: `GET /api/pdf/jobs/{id}/file` serves the PDF to an authenticated caller. The console uses
it for Download and Preview, and shows expired files.

**Independent Test**: quickstart Scenario 2, steps 3 and 5; `JobFileEndpointsTests` (file part); console specs.

### Tests for User Story 4

- [X] T047 [P] [US4] Add to `tests/PdfBuilder.Api.Tests/JobFileEndpointsTests.cs` (file section): own job with key → `200` PDF with the R11 headers, switch on or off; the operator's console session with `X-Client-Id` → `200`; the same request without the session cookie → `401`; unknown id → `404`; failed → `409 JOB_HAS_NO_FILE`; expired → `410 FILE_EXPIRED`
- [X] T048 [P] [US4] Extend `tests/PdfBuilder.Api.Tests/CrossClientIsolationTests.cs`: `GET /api/pdf/jobs/{A's id}/file` as client B, and as the operator acting as B, is identical to an unknown id
- [X] T049 [P] [US4] Update `web/src/app/features/pdf-jobs/feature/job-detail/job-detail.page.spec.ts` and `web/src/app/features/pdf-jobs/ui/jobs-table/jobs-table.spec.ts`: Download requests `/api/pdf/jobs/{jobId}/file` through `HttpClient` (`HttpTestingController`) and saves `{templateKey}-{jobId}.pdf`; Preview loads the same URL; no element links to `/files/`; a job with `fileExpired: true` shows "File expired" and no Download or Preview; a `410` while downloading shows the expired state

### Implementation for User Story 4

- [X] T050 [US4] In `src/PdfBuilder.Api/Endpoints/PdfJobEndpoints.cs` map `group.MapGet("/{jobId:guid}/file", DownloadFileAsync)` named `DownloadPdfJobFile`: `JobFiles.OpenFileAsync` → `404` on null, else `Results.File(stream, "application/pdf", $"{fileId}.pdf")` with the R11 headers. Put those headers in one helper shared with `FileEndpoints.cs`, for example `src/PdfBuilder.Api/Files/PdfFileResponses.cs`
- [X] T051 [P] [US4] Create `web/src/app/shared/util/save-blob.ts`: `saveBlob(blob: Blob, fileName: string)` creates an object URL, clicks a temporary `<a download>`, and revokes the URL; add a spec beside it
- [X] T052 [US4] In `web/src/app/features/pdf-jobs/data-access/pdf-jobs.api.ts`: remove `FILE_ID_PATTERN` and `fileUrl`; replace `download(fileId)` with `downloadFile(jobId): Observable<Blob>` → `GET ${baseUrl}/api/pdf/jobs/${encodeURIComponent(jobId)}/file`. In `web/src/app/features/pdf-jobs/data-access/pdf-job.models.ts`: add `fileUrlExpiresAt: string | null` and `fileExpired: boolean` to `PdfJob` (and `pendingJob`); add `FileExpired: 'FILE_EXPIRED'` and `JobHasNoFile: 'JOB_HAS_NO_FILE'` beside `PdfErrorCodes`; update the `JOB_RETENTION_HOURS` comment to say succeeded jobs are kept while their file is
- [X] T053 [US4] In `web/src/app/features/pdf-jobs/feature/job-detail/job-detail.page.ts` and `.html`: replace `downloadUrl` and the `<a href>` with a Download button that calls `downloadFile(jobId)` then `saveBlob(blob, downloadName())`, showing a spinner while busy and an inline error (expired state on `410`); the preview `rxResource` uses `downloadFile(jobId)`; when `fileExpired`, show "File expired" with the retention explanation and no buttons (depends on T051, T052)
- [X] T054 [US4] In `web/src/app/features/pdf-jobs/ui/jobs-table/jobs-table.ts` and `.html`: replace the `downloadUrlFor` input with a `download = output<PdfJob>()` button and show "Expired" for `fileExpired` rows; in `web/src/app/features/pdf-jobs/feature/jobs/jobs.page.ts`, handle `(download)` with `downloadFile` + `saveBlob` (depends on T051, T052)
- [X] T055 [US4] Run T047–T049 and `cd web; npm test; npm run lint`: green

**Checkpoint**: the console works with `RequireSignature=true` and never exposes a link.

---

## Phase 7: User Story 5 - Existing integrations keep working through the upgrade (Priority: P3)

**Goal**: the upgrade needs one setting. With the switch off, links behave as before. The switch can
be turned on, and off again, safely.

**Independent Test**: quickstart Scenario 4; `FileUpgradeTests`.

### Tests for User Story 5

- [X] T056 [P] [US5] Create `tests/PdfBuilder.Api.Tests/FileUpgradeTests.cs`: starting the host without `Blocks:Files:SigningKey` fails with an `OptionsValidationException` naming the key, and the database's `__EFMigrationsHistory` has no `AddFileLifetime` row (nothing changed); switch off: a link without parameters downloads and a new job's status and bus reply carry `…/files/{fileId}` with null expiry; switch on: the same unsigned link → `404`; turned off again: a signed link from the switch-on period still downloads before expiry and → `404` after it (spec US5 scenarios 1–5)
- [X] T057 [P] [US5] Add to `tests/PdfBuilder.Infrastructure.Tests/FileRetentionSweeperTests.cs`: a file written before the upgrade (last-write time 40 days ago, no job row) and a backfilled job from 40 days ago are both removed by the first sweep with the default 30 days (US5 scenario 6)

### Implementation for User Story 5

- [X] T058 [P] [US5] Add `["file-signing-key"]` to `tools/Blocks.Cli/Program.cs`: print 32 bytes from `RandomNumberGenerator` as base64, with the line `Put this in configuration as Blocks:Files:SigningKey (environment variable Blocks__Files__SigningKey).`; add it to `Usage()`
- [X] T059 [P] [US5] In `deploy/compose.prod.yaml` add `Blocks__Files__SigningKey: ${FILES_SIGNING_KEY:?set FILES_SIGNING_KEY in .env}` and commented `Blocks__Files__RequireSignature`, `Blocks__Files__UrlLifetime` and `Blocks__Files__RetentionDays` lines. In `deploy/README.md`, cover generating `FILES_SIGNING_KEY`, what the switch does, and that the first start deletes files older than `RetentionDays`. In `deploy/huggingface/space.md`, add the Space secret
- [X] T060 [US5] Run T056–T057: green

**Checkpoint**: every story is complete. With the defaults, an upgraded deployment behaves as before
except for retention, which is announced.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T061 [P] `docs/configuration.md`: new `## Files` section (the four keys, from contracts/configuration.md, including rotation by changing the key); in `## Storage`, a note that links are built from `PublicBaseUrl`; in `## JobStore`, "A succeeded job is kept as long as its file, then for this long."; in `## Logging`, why `Microsoft.AspNetCore.Hosting.Diagnostics` is pinned to `Warning`; add `Blocks__Files__SigningKey` to "Environment variables you will actually set"; replace "file downloads (`/files/{fileId}`) need no key" with the signed-link rule
- [X] T062 [P] `docs/operations.md`: new "### Upgrading to private files" (the one setting, the switch and the next release's default, the first sweep, setting `RetentionDays` to 3650 to keep everything, taking a backup before upgrading, and the manual SQL from contracts/configuration.md for `MigrateOnStartup=false`); "Rotating the file signing key"; a reverse proxy's access log records query strings, so drop `sig`; disk use bounded by retention under `## Sizing`; `## Known gaps`: object storage is still needed before several instances serve downloads
- [X] T063 [P] `docs/message-contracts.md`: add `FileUrlExpiresAt` to the `IGeneratePdfResponse` table; rewrite "## Downloading the PDF" (signed links when the deployment requires them, `GET /api/pdf/jobs/{id}/file-url` for a fresh link, `/file` for a direct download, retention and `FILE_EXPIRED`)
- [X] T064 [P] `docs/architecture.md` (the Claim Check flow: links made per read and signed; the file sweeper) and `docs/blocks/04-backend.md` (`### Files` as built per the plan's deviations list; the endpoints table rows for `file-url`, `file`, `/files/{fileId}`; the contract table moved to 1.3.0; under `### Health and telemetry`, a note that URL queries must be redacted once an exporter exists)
- [X] T065 [P] `CHANGELOG.md`: a new `## [Unreleased] — 1.3.0` above 1.2.0 with "Added: `IGeneratePdfResponse.FileUrlExpiresAt`". A service section covering private, expiring links behind `Blocks:Files:RequireSignature` (off now, **on by default in the next release**), the new endpoints and error codes, retention (default 30 days, the first start deletes older files), succeeded jobs kept with their file, the console downloading through the API, the required `Blocks:Files:SigningKey`, and the migration `AddFileLifetime`
- [X] T066 [P] `samples/requests.http` and `samples/README.md`: a "Files" section with status → `fileUrl`, `file-url`, `file`, and the 409 and 410 cases; `README.md`: a line on expiring links and fresh links
- [ ] T067 Run the full suite (`dotnet test` with `PDFBUILDER_RUN_DATABASE_TESTS=1`, `cd web; npm test; npm run lint`), `dotnet pack src/PdfBuilder.Contracts` (package validation passes), and `docker compose up --build`. Walk quickstart Scenarios 1–5 against the compose stack with `Blocks__Files__RequireSignature=true`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T002 needs T001.
- **Foundational (Phase 2)**: depends on Setup; blocks every story. T018 must be green before any story starts.
- **US1 (Phase 3)**: after Foundational.
- **US2 (Phase 4)**: after Foundational. It can start alongside US1; its signed-link assertions (switch on) need T026.
- **US3 (Phase 5)**: after Foundational; independent of US1/US2.
- **US4 (Phase 6)**: after Foundational. T050 reuses the headers from T027 (US1), so do T027 first or create the helper in T050.
- **US5 (Phase 7)**: after US1 (it tests the switch) and US3 (T057 extends the sweeper tests).
- **Polish (Phase 8)**: after the stories it documents; T067 last.

### Within Each User Story

- Tests first. They should fail until the story's implementation lands.
- Stud types before the block's use of them. Domain and ports before stores, and stores before endpoints and the consumer.
- Console: `save-blob` and the API wrapper before the pages.

### Parallel Opportunities

- Setup: T001, T003 and T004 together.
- Foundational: T005, T006 and T007 together, then T008–T010. T016 runs alongside T012–T015.
- US1: T019–T023 together; T028 and T030 alongside T024–T027.
- Once T018 is green, US1, US3 and the backend half of US4 can proceed in parallel. Shared files: `PdfJobEndpoints.cs` (T035, T050), `JobFileEndpointsTests.cs` (T033, T041, T047), `CrossClientIsolationTests.cs` (T034, T048), `LocalFileStorageTests.cs` (T009, T037), `GeneratePdfConsumer.cs` (T015, T029).
- Polish: T061–T066 together.

---

## Parallel Example: User Story 1

```text
Task: "T019 [US1] signer tests in tests/Blocks.ServiceDefaults.Tests/FileLinkSignerTests.cs"
Task: "T020 [US1] filter tests in tests/Blocks.ServiceDefaults.Tests/FileSignatureEndpointFilterTests.cs"
Task: "T021 [US1] end-to-end signed links in tests/PdfBuilder.Api.Tests/SignedLinkApiTests.cs"
Task: "T023 [US1] leak test in tests/PdfBuilder.Api.Tests/FileLinkLeakTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T037 [US3] storage delete tests in tests/PdfBuilder.Infrastructure.Tests/LocalFileStorageTests.cs"
Task: "T038 [US3] sweeper tests in tests/PdfBuilder.Infrastructure.Tests/FileRetentionSweeperTests.cs"
Task: "T039 [US3] visibility tests in tests/PdfBuilder.Infrastructure.Tests/EfPdfJobStoreTests.cs"
Task: "T040 [US3] job sweeper tests in tests/PdfBuilder.Infrastructure.Tests/JobRetentionSweeperTests.cs"
```

## Parallel Example: User Story 4

```text
Task: "T047 [US4] /file endpoint tests in tests/PdfBuilder.Api.Tests/JobFileEndpointsTests.cs"
Task: "T049 [US4] console specs"
Task: "T051 [US4] web/src/app/shared/util/save-blob.ts"
```

---

## Implementation Strategy

### MVP First (User Stories 1 and 2)

1. Phase 1 and Phase 2. At T018 nothing has changed for callers, but the schema and the link path are ready.
2. Phase 3 (signed links) and Phase 4 (fresh links).
3. **Stop and validate**: quickstart Scenarios 1 and 2 with the switch on. This is the part that
   makes a leaked link stop working.

### Incremental Delivery

1. MVP (US1 + US2). Do not ship it with the switch on until US4 is done: before then, the console's
   download uses `/files/{fileId}` without a signature.
2. US3: retention and FR-019.
3. US4: the console moves to the authenticated download. After this, the switch can be turned on.
4. US5: upgrade tests, CLI and deployment files.
5. Polish. Together with 012–014, this completes milestone M2.

---

## Notes

- `[P]` = different files, no dependencies on incomplete tasks.
- Do not add a stored URL back anywhere. Links are made per read (R2, R13).
- The switch stays `false` by default in this release. Flipping the default belongs to the next release
  and is only announced here (T065).
