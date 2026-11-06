# Data Model: Private, Expiring Files

## Table `pdf_jobs` (changed)

Migration `AddFileLifetime` (research R13). Columns not listed are unchanged.

| Column | Type | Null | Change | Notes |
| --- | --- | --- | --- | --- |
| `file_created_at` | `timestamptz` | yes | **new** | When storage wrote the file; equal to the file's last-write time (R5). Retention counts from here. Backfilled from `completed_at` for `Succeeded` rows. |
| `file_expired_at` | `timestamptz` | yes | **new** | Set when the file is found gone: by the sweeper at the end of retention, or by a read that finds it missing early. Never cleared. |
| `file_url` | `text` | yes | **dropped** | Links are made per read (R2, R3). |

Constraints and indexes:

- `ck_pdf_jobs_file_created_at`: `status <> 'Succeeded' OR file_created_at IS NOT NULL`, added after the
  backfill.
- `ix_pdf_jobs_file_created_at` on `file_created_at`: used by the sweeper's marking step and by the
  retention expression (R8).

Migration order: add columns → backfill → add check constraint and index → drop `file_url`. One
transaction, as EF Core applies it. No setting is needed and nothing depends on a deployment value.

Manual-migration SQL for operators who set `Blocks:Database:MigrateOnStartup=false` is listed in
[contracts/configuration.md](contracts/configuration.md#upgrade).

## Domain: `PdfJob` (changed)

| Member | Change | Notes |
| --- | --- | --- |
| `FileUrl` | **removed** | Was the stored URL. The API and the bus reply make links from `FileId` (R2). |
| `FileCreatedAt` | **new**, `DateTimeOffset?` | Set by `Succeed`; null otherwise. |
| `FileExpired` | **new**, `bool` | Computed by the store on read (R5); `false` for jobs without a file. |
| `Succeed(fileId, sizeBytes, fileCreatedAt, completedAt)` | signature changed | `fileUrl` replaced by `fileCreatedAt`. |
| `WithFileExpired()` | **new** | Returns the job with `FileExpired = true`. Only valid on a succeeded job. |

`Fail` clears `FileCreatedAt` along with the other file fields.

### Derived state of a succeeded job's file

```
Present ──(now ≥ file_created_at + RetentionDays)──▶ Expired   (rule, at read time)
Present ──(storage says the file is not there)────▶ Expired   (file_expired_at := now)
Present ──(sweeper, after retention)──────────────▶ Expired   (file_expired_at := now, then file deleted)
```

`Expired` is final. A job's file is `Expired` when `file_expired_at IS NOT NULL OR file_created_at <
now − RetentionDays` (R5).

### Visibility of a job (FR-019, R8)

A job is returned by reads, and kept by `JobRetentionSweeper`, while either holds:

1. `last_saved_at ≥ now − RetentionHours` (unchanged rule), or
2. it succeeded, and one job retention period has not passed since its file expired, the expiry being
   the earlier of `file_expired_at` and `file_created_at + RetentionDays`.

## Application ports (changed)

| Port | Member | Change |
| --- | --- | --- |
| `IFileStorage` | `SaveAsync` → `StoredFile(FileId, SizeBytes, CreatedAt)` | `Url` removed, `CreatedAt` added. |
| `IFileStorage` | `ExistsAsync(fileId)` | **new**. `false` for an id that is not one the storage issues. |
| `IFileStorage` | `DeleteCreatedBeforeAsync(cutoff)` → count | **new**. Idempotent; skips files it cannot delete now. |
| `IFileStorage` | `OpenReadAsync` | unchanged contract; opened so a concurrent delete does not cut a reader short (R6). |
| `IPdfJobStore` | `MarkFileMissingAsync(jobId)` | **new**. Sets `file_expired_at` if unset, for the caller's own job. |
| `GeneratePdfResult` | `(FileId, SizeBytes, FileCreatedAt)` | `FileUrl` replaced. |

New Application service `JobFiles` (`Features/JobFiles`), used by the three job-file paths so the
rules are written once:

- `GetJobAsync(jobId)` → the job with `FileExpired` confirmed against storage (R5), or null.
- `RequireFileAsync(jobId)` → the file id, or throws `JobHasNoFileException` / `FileExpiredException`;
  null job → `null` (the endpoint answers 404).
- `OpenFileAsync(jobId)` → an open stream, same refusals; a stream that cannot be opened marks the file
  missing and throws `FileExpiredException`.

## Domain exceptions (new)

| Exception | Code | HTTP | Transient |
| --- | --- | --- | --- |
| `JobHasNoFileException` | `JOB_HAS_NO_FILE` | 409 | no |
| `FileExpiredException` | `FILE_EXPIRED` | 410 | no |

Codes in `JobFileErrorCodes` (Domain), HTTP-only like `TemplateErrorCodes`.

## Stud types (new, `Blocks.ServiceDefaults/Files`)

| Type | Purpose |
| --- | --- |
| `BlocksFilesOptions` | `SigningKey`, `UrlLifetime`, `RetentionDays`, `RequireSignature` (R7). |
| `BlocksFilesOptionsValidator` | Start-up validation; messages name the key, never the value. |
| `FileLinkSigner` | `Sign(fileId, now)` → `SignedFileQuery(Query, ExpiresAt)`; `Check(fileId, query, now)` → `Unsigned` / `Valid` / `Invalid` (R1, R3). |
| `FileSignatureEndpointFilter` | `.RequireFileSignature()`: 404 unless the R3 table says "served". |

## PDF Builder Api types (new)

| Type | Purpose |
| --- | --- |
| `PdfFileLinks` | `For(fileId)` → `(Url, ExpiresAt?)` from `Storage:PublicBaseUrl` and the signer, honouring the switch. Used by the status, fresh-link and bus paths. |

## Message: `IGeneratePdfResponse` (Contracts 1.3.0)

| Member | Change |
| --- | --- |
| `FileUrlExpiresAt` | **new**, `DateTimeOffset?`. Null on failure and while signatures are off. |
| `FileUrl` | documentation only: signed and expiring while the deployment requires signatures. |

Details in [contracts/message-contract.md](contracts/message-contract.md).
