# Research: Private, Expiring Files

Decisions behind [plan.md](plan.md). Each one names the requirement it serves. Starting point is
`docs/blocks/04-backend.md#files`; where this differs, the difference is called out and the doc is
updated in this feature.

## R1. Link format and signature

**Decision**: `{Storage:PublicBaseUrl}/files/{fileId}?expires={unix seconds}&sig={base64url}` with
`sig = base64url(HMAC-SHA256(key, "{fileId}.{expires}"))`, no padding. `fileId` is the existing
32-hex-character id; `expires` is a decimal Unix time in seconds, rounded **up** from
`now + Blocks:Files:UrlLifetime`, so a link is never valid for less than the lifetime (FR-005). The
check recomputes the HMAC and compares with `CryptographicOperations.FixedTimeEquals` over the decoded
bytes (FR-003). A link is valid while `now < expires` (spec edge case: not at the expiry second).

Rejected before any HMAC is computed, all with the same answer: `expires` not 1–12 digits, `sig` not
exactly 43 base64url characters, either parameter repeated, only one of the two present. With neither
present the link is "unsigned" (R3).

**Rationale**: what the design doc already specifies, and the shape every block will share (Principle
IV: "a block returns a signed file URL"). HMAC-SHA256 with a 256-bit key cannot be forged without the
key; binding `fileId` and `expires` in the MAC is what makes swapping the id or extending the expiry fail
(US1 scenarios 3 and 4). The id is a random 128-bit value, so it is not guessable either.

**Alternatives considered**: a version prefix in the signed string (`v1.{fileId}.{expires}`). It would
help a future change of algorithm, but a new algorithm can just as well use a new parameter name
(`sig2`), and the design doc's format is already referenced by later features. Signing the client id
too: the file id is globally unique and owned through its job, so it adds nothing. A JWT: larger links
and a parser to maintain, for the same two claims.

## R2. Where signing lives

**Decision**: a new stud area, `Blocks.ServiceDefaults/Files`:

- `BlocksFilesOptions` (`Blocks:Files:*`) and its validator (R7).
- `FileLinkSigner`: `Sign(fileId, now) → (query, expiresAt)` and `Verify(fileId, query, now) → bool`.
  Singleton, holds the decoded key, never exposes it.
- `.RequireFileSignature()` endpoint filter: reads the `fileId` route value and the query, applies R3,
  and answers `404` (the same empty `NotFound` as a missing file) when the link does not pass.
- `AddBlocksFiles()` registers them.

PDF Builder builds absolute links in one place, `PdfFileLinks` (Api), from `Storage:PublicBaseUrl`, the
file id and the signer: unsigned while the switch is off, signed while on (R3). Endpoints and the bus
consumer both use it. `PdfBuilder.Application` does not sign or build URLs any more: `StoredFile` loses
`Url` and gains `CreatedAt` (R5).

**Rationale**: Principle V lists signed, expiring file URLs as a stud, built once in `Blocks.*` for every
block and extracted in 011. Signing is a delivery concern (it needs the public base URL and the request
query), so it sits beside the HTTP layer, not in the use case. The filter keeps `FileEndpoints` a plain
"open this file" handler.

**Alternatives considered**: signing inside `LocalFileStorage`. It already built the URL, but storage
would then need the switch, the clock and the key, and a future object-store adapter would have to
repeat all of it. Pre-signed object-store URLs: no object store yet, and links must keep one shape
whatever the storage.

## R3. The signature switch

**Decision**: `Blocks:Files:RequireSignature`, default `false` in this release.

| Link followed | Switch off | Switch on |
| --- | --- | --- |
| No `expires`, no `sig` | served | 404 |
| Valid signature, before expiry | served | served |
| Expired, altered, or only one parameter | 404 | 404 |

Links issued: switch off → `…/files/{fileId}` and no expiry time, exactly as before; switch on →
signed, with expiry time. Applies to status reads, fresh links and bus replies alike.

**Rationale**: `IGeneratePdfResponse.FileUrl` is documented as "not signed and does not expire".
Issuing expiring links unconditionally would change that meaning under consumers on 1.2.0 and earlier,
which Principle VII forbids within a major version; the design doc already planned
`FileUrlExpiresAt` as "null while signing is off". The spec was amended to match (Clarifications,
2026-11-05). A link that carries a signature is always checked, so turning the switch back off never
revives an expired or altered link.

**Alternatives considered**: always sign, and use the switch only to keep accepting unsigned links (the
spec's first wording). Breaks any product that keeps a link from after the upgrade for longer than an
hour, with no change on its side, which is the failure the switch exists to prevent.

## R4. Fresh links, authenticated download and error codes

**Decision**: two endpoints in the existing job group, so they inherit `pdf:generate`,
`.RequireActingClient()` and the client query filter:

- `GET /api/pdf/jobs/{jobId}/file-url` → `200 { fileUrl, fileUrlExpiresAt }`, `Cache-Control: no-store`.
- `GET /api/pdf/jobs/{jobId}/file` → the PDF, `Content-Disposition: attachment`.

Refusals, in order: unknown or another client's job → `404` (the same answer as `GET /api/pdf/jobs/{id}`,
which feature 003 already makes identical); job not succeeded → `409 JOB_HAS_NO_FILE`; file expired or
missing → `410 FILE_EXPIRED`. The codes live in a new `JobFileErrorCodes` (Domain), HTTP-only like
`TemplateErrorCodes`, not in `PdfErrorCodes`, which is the bus vocabulary.

`GET` for the fresh link: it changes nothing a caller can observe (links are not stored, and issuing one
revokes nothing, FR-005), so it is safe and needs no `Idempotency-Key`.

**Rationale**: the design doc's endpoints. `410 Gone` says exactly "it existed and is permanently
gone", distinct from `404` (FR-011) and from `409` "not yet / never" (FR-010).

**Alternatives considered**: `POST …/file-url`. Suggests that something is created and would fall
under the idempotency stud (006) for no benefit. A `404` for an expired file: the spec requires a
distinct answer for the owner.

## R5. When a file is "expired", decided at read time

**Decision**: a job records `file_created_at` (the time storage reports for the file) and
`file_expired_at` (set when the service finds the file gone before its time). A succeeded job's file is
expired when `file_expired_at` is set **or** `file_created_at < now − RetentionDays`. Reads compute this,
as job reads already apply their retention cut-off themselves; nothing has to have run for the answer to
be right.

Status reads, fresh links and authenticated downloads of a job whose file is not expired by that rule
also ask storage whether the file is there (`IFileStorage.ExistsAsync`, a `stat` on local disk; the
download opens it anyway). If it is not, the job's `file_expired_at` is set to now
(`IPdfJobStore.MarkFileMissingAsync`) and the answer is "expired" (spec edge case: file missing early).

`LocalFileStorage` takes the creation time from the clock before writing, writes, then sets the file's
last-write time to exactly that value, and returns it in `StoredFile.CreatedAt`. The job and the file
carry the same instant, so the sweeper's two cut-offs (R6) agree by construction.

**Rationale**: FR-012 requires "no link" for an expired file on every read; a read-time rule makes that
true even between sweeper runs and immediately after the operator shortens the retention period (edge
case). The `stat` covers files lost outside the service. On local disk it costs microseconds against a
database read, well within SC-009; an object-store adapter would make it a `HEAD`, which is noted for
that work.

**Alternatives considered**: trust the sweeper to mark jobs and never stat. A lost volume would hand out
links that do not work, and reads would flip only after the next run. Stat only on fresh-link requests:
status reads would still carry dead links.

## R6. File retention sweeper

**Decision**: `FileRetentionSweeper`, a `BackgroundService` like `JobRetentionSweeper`, every
**15 minutes**, first run at start-up. One run, with `cutoff = now − RetentionDays`:

1. `UPDATE pdf_jobs SET file_expired_at = now WHERE status = 'Succeeded' AND file_expired_at IS NULL AND
   file_created_at < cutoff`, across all clients (`MarkExpiredFilesOfAllClientsAsync`, a fourth name in
   the architecture test's `IgnoreQueryFilters` allow-list).
2. `IFileStorage.DeleteCreatedBeforeAsync(cutoff)`: walk the shard directories and delete every
   `*.pdf` whose last-write time is before the cutoff, plus `*.tmp` leftovers older than an hour.
   Missing files and files locked by a reader are skipped and retried next run.

Jobs are marked before files are deleted, so no read can hand out a link to a file that a crash left
deleted but unmarked. Files without a job (rendered before a job save failed, or whose job was removed
before this feature) are deleted by step 2 alike (FR-016). Every instance runs it; both steps are
idempotent, so concurrent runs are harmless (US3 scenario 5). Failures are logged and the next run
carries on (scenario 4).

Readers are not cut short: `LocalFileStorage.OpenReadAsync` opens with `FileShare.Read | FileShare.Delete`.
On Linux a deleted file stays readable through an open handle until the download ends; on Windows the
delete fails while it is open and is retried next run (edge case: no partial file).

**Rationale**: 15 minutes plus a run keeps every file within FR-016's one hour. Walking the directory
needs no index and finds orphans. It is proportional to the number of files kept: at a sustained
4 renders a second (the starting size in `docs/operations.md#sizing`, flat out) and 30 days that is
about 10 million entries, up to a minute of directory reads every 15 minutes, off the request path;
realistic loads are far smaller. If that ever matters, step 2 can delete by the ids of the rows marked in step 1 and sweep
orphans daily.

**Alternatives considered**: delete by job rows only. Misses orphans, and after option A of the
clarification there are none of those from retention any more, but there are from failed saves and from
before the upgrade. Delete when the job row is removed: ties file lifetime to job lifetime, which the
clarification rejected.

## R7. Configuration and its validation

**Decision**: `BlocksFilesOptions`, section `Blocks:Files`:

| Key | Default | Rule |
| --- | --- | --- |
| `SigningKey` | none, required | base64, decodes to ≥ 32 bytes |
| `UrlLifetime` | `01:00:00` | `00:01:00` – `7.00:00:00` |
| `RetentionDays` | `30` | 1 – 3650 |
| `RequireSignature` | `false` | |

Validated at start-up (`ValidateOnStart`) with messages that name the key and the environment variable
(`Blocks__Files__SigningKey`) and never include the value, like `BlocksAuthOptions` (FR-020, FR-021).
`tools/Blocks.Cli` gains `file-signing-key`, printing 32 random bytes in base64; the docs also give the
`openssl rand -base64 32` equivalent. `compose.yaml` gets a fixed development key labelled as such;
`deploy/compose.prod.yaml` requires `FILES_SIGNING_KEY`.

**Rationale**: the design doc's keys. One required setting is the one upgrade step SC-007 allows.

**Alternatives considered**: generate the key on first start and keep it in the database, protected by
Data Protection. No upgrade step, and every instance would share it. Rejected for now: the spec and the
design doc make it configuration, rotation by changing a setting is simpler to explain, and a key the
operator controls can be shared with a future block that verifies links. Recorded for 014, where the
deployment kit generates secrets anyway.

## R8. Job retention for succeeded jobs (FR-019)

**Decision**: a job is visible to reads, and kept by the sweeper, while

```
last_saved_at >= now − JobStore:RetentionHours
OR ( status = 'Succeeded'
     AND file_created_at >= now − RetentionDays − RetentionHours
     AND (file_expired_at IS NULL OR file_expired_at >= now − RetentionHours) )
```

That is: until one job retention period after the later of its last save and the moment its file
expired, where "expired" is the earlier of `file_expired_at` and `file_created_at + RetentionDays`.
`EfPdfJobStore.GetAsync` and `JobRetentionSweeper` use the same expression (one private method).
Jobs that never had a file keep the old rule. Index `ix_pdf_jobs_file_created_at` supports it.

**Rationale**: the clarification's answer A, computed rather than stored, so changing either retention
setting takes effect at once and no row has to be rewritten when its file expires.

**Alternatives considered**: the sweeper bumping `last_saved_at` when it marks a file expired. The
job would vanish from reads between the end of file retention and the next run, then reappear.

## R9. Bus reply (Contracts 1.3.0)

**Decision**: `IGeneratePdfResponse.FileUrlExpiresAt` (`DateTimeOffset?`), null on failure and while the
switch is off. `FileUrl`'s documentation changes to describe both modes. Contracts version `1.3.0-local`,
a `CP0006` baseline suppression for the new member on both target frameworks, `CHANGELOG.md` entry.
1.2.0 (feature 003) is not released yet but ships with milestone M1; this ships with M2, so it gets its
own version, as the design doc says.

**Rationale**: Principle VII; a consumer on 1.2.0 deserializes the reply unchanged.

## R10. Keeping signatures out of logs (FR-007, SC-008)

**Decision**:

- Nothing in the service logs a link. `FileEndpoints` logs nothing about the query; the consumer's
  existing log lines name `FileId`, never `FileUrl`.
- ASP.NET Core's `Request starting … {QueryString}` line is written by
  `Microsoft.AspNetCore.Hosting.Diagnostics` at `Information`. `appsettings.json` already sets
  `Microsoft.AspNetCore` to `Warning`; this feature adds an explicit
  `"Microsoft.AspNetCore.Hosting.Diagnostics": "Warning"` so raising the general level for debugging
  does not start logging signatures, and `docs/configuration.md#logging` says why.
- `docs/operations.md` tells operators that a reverse proxy's access log records query strings, and how
  to drop them.
- `KeyLeakTests`' approach is reused: a test issues, uses and rejects links with every log level at
  `Debug` except the pinned category, and asserts that no `sig=` value and no part of the signing key
  appears in captured logs or problem responses.

**Rationale**: a signature is a bearer credential for an hour; the constitution treats secrets in logs
as a leak. The service has no OpenTelemetry exporter yet, so there is no trace attribute to redact; when
one is added the URL query must be redacted (noted in 04-backend.md).

## R11. Download responses

**Decision**: every PDF response (signed link and authenticated download) sends
`Cache-Control: private, no-store`, `Referrer-Policy: no-referrer` and `X-Robots-Tag: noindex`, keeps
`Content-Disposition: attachment; filename={fileId}.pdf` and `application/pdf`. Fresh-link answers send
`Cache-Control: no-store`.

**Rationale**: FR-006. `no-store` also stops the browser keeping the PDF after the link has expired;
`no-referrer` stops a link inside the PDF from sending the signed URL to another site.

## R12. Console

**Decision**: the console downloads and previews through `GET /api/pdf/jobs/{id}/file` with
`HttpClient` (so the interceptor adds `X-Client-Id` and `X-Console`), then saves the blob through an
object URL. `PdfJobsApi.fileUrl` and its file-id pattern are removed; the jobs table emits a
`download` event instead of taking a URL builder. A job with `fileExpired: true` shows "File expired"
and no Download or Preview. The job detail's retention note says succeeded jobs are kept while their
file is. `PdfErrorCodes` in the console gains `FILE_EXPIRED` and `JOB_HAS_NO_FILE` for messages.

**Rationale**: FR-015. A plain `<a href>` cannot carry `X-Client-Id`, and the operator's session must
not be turned into a copyable link.

## R13. Schema

**Decision**: one migration, `AddFileLifetime`, on `pdf_jobs`: add `file_created_at` and
`file_expired_at` (`timestamptz`, null), backfill `file_created_at = completed_at` for succeeded rows,
add `ck_pdf_jobs_file_created_at` (`status <> 'Succeeded' OR file_created_at IS NOT NULL`), index
`file_created_at`, drop `file_url`.

**Rationale**: links are made per read now, so a stored URL would be wrong in either mode and stale when
`PublicBaseUrl` changes. Rows from before the upgrade get the best creation time there is (their
completion, seconds after the write).

**Alternatives considered**: keep `file_url` unused, for a rollback to the previous version. That version
would then show the old unsigned links for old jobs and none for new ones, so rollback is not clean
either way, and 003's `RequireClientOwner` already changed `pdf_jobs` in ways the 1.0.0 service does
not expect. Migrations are applied forward on start-up; the operations guide gains a line saying a
rollback across this release needs the database backup taken before it.
