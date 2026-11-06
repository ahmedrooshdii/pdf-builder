# Changelog

Releases of the `PdfBuilder.Contracts` NuGet package. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). Each release is the git tag `contracts-v<version>`.

## [Unreleased] — 1.3.0

Additive: producers and consumers on 1.0.0 to 1.2.0 keep matching. The message type, its namespace and
therefore its exchange name are unchanged.

### Added

- `IGeneratePdfResponse.FileUrlExpiresAt`: when `FileUrl` stops working. Null when the deployment does not
  sign links, and on failure. A 1.2.0 consumer ignores it. `FileUrl`'s documentation now says what it is: signed
  and expiring when the deployment requires it, plain otherwise. See
  [docs/message-contracts.md](docs/message-contracts.md#downloading-the-pdf).

### Added (service): private, expiring files

- **Signed, expiring links, behind `Blocks:Files:RequireSignature`.** Off for now, so links behave as before:
  the plain `/files/{fileId}`, no expiry. On, every link carries `expires` and `sig`, works without a key until
  `Blocks:Files:UrlLifetime` (an hour by default) has passed, and an altered, expired or unsigned link answers
  the same bare `404` as a file that does not exist. **The next release turns this on by default.** A product
  that keeps a link past its expiry must ask for a fresh one.
- **`GET /api/pdf/jobs/{id}/file-url`** returns a fresh link for a job you own (`pdf:generate`). `404` for a job
  that is not yours or does not exist (identical), `409 JOB_HAS_NO_FILE` for a job that failed, `410 FILE_EXPIRED`
  once the file is deleted. Earlier links keep working until they expire.
- **`GET /api/pdf/jobs/{id}/file`** downloads the PDF with your key, no link involved. The console now uses it
  for Download and Preview, so it never holds a link.
- **Job status** (`GET /api/pdf/jobs/{id}`) gains `fileUrlExpiresAt` and `fileExpired`. `fileUrl` is made for
  each response, and is null once the file expired.
- **Retention.** PDFs are deleted `Blocks:Files:RetentionDays` (30 by default) after they were made, **whichever
  way the switch is set**. **The first start of this version deletes every file older than that.** To keep
  everything for now, set it to `3650` before upgrading. A succeeded job is kept as long as its file, then for
  `JobStore:RetentionHours` more, so a poller is told `fileExpired` instead of "not found".
- **New required setting: `Blocks:Files:SigningKey`** (base64 of at least 32 bytes; generate one with
  `dotnet run --project tools/Blocks.Cli -- file-signing-key`). The service does not start without it, naming the
  setting, before the database is touched. The deployment files and `compose.yaml` set it.
- **Migration `AddFileLifetime`**: records when each job's file was made (from when the job completed) and drops
  the stored link. See [docs/operations.md](docs/operations.md#upgrading-to-private-files), which has the SQL if you
  apply migrations yourself.

### Changed (service)

- Signatures never reach a log: the request log (`Microsoft.AspNetCore.Hosting.Diagnostics`) is pinned to
  `Warning`. A reverse proxy's access log still records the query string: drop `sig` there.
- Several instances starting together are now serialised through a database lock for the whole upgrade, so one
  instance's start cannot undo the migrations another has just applied.

## [Unreleased] — 1.2.0

Additive: producers and consumers on 1.0.0 or 1.1.0 keep matching. The message type, its namespace and
therefore its exchange name are unchanged.

### Added

- `IGeneratePdfRequest.ClientId` (and a settable `GeneratePdfRequest.ClientId`): the client a job is for.
  Templates and jobs now belong to the client that created them, and a request is rendered with that
  client's published template, or a built-in one. Null, which is what a 1.0.0 or 1.1.0 publisher sends,
  means the deployment's **default client** (`Blocks:Bus:DefaultClientId`), so those publishers keep working
  on a deployment that sets it. On a deployment that does not, their messages are answered
  `VALIDATION_FAILED` ("The message names no client and no default client is configured."). See
  [docs/message-contracts.md](docs/message-contracts.md#clientid).
- `PdfErrorCodes.ClientDisabled` (`CLIENT_DISABLED`): the client the request is for is disabled. A client that
  is not registered, or an id that is not a client id, is `VALIDATION_FAILED`, as is a `CorrelationId` that
  another client's job already uses.

### Added (service): client-scoped templates and jobs

Released together with persistent storage (001), API keys (002) and console sign-in (004), as the first
milestone: nothing is open, and each client sees only its own data.

- **Templates and jobs belong to the client that created them.** A client lists, reads, uses and changes
  only its own. Another client's template key or job id answers `404`, exactly as one that does not
  exist, never `403`. Two clients can each have a template called `statement`, and a job renders the
  client's own published template, else the built-in one. Built-in templates are shared by every client.
  A job id stays unique across clients.
- **The operator works inside one client at a time.** With the admin key or a console session, template and
  job calls need `X-Client-Id`: without it they are refused `400 VALIDATION_FAILED`, for a client that is not
  registered `404 CLIENT_NOT_FOUND`. A disabled client still works for the operator. The console already
  sent the chosen client.
- **New setting: `Blocks:Bus:DefaultClientId`**, optional. It names the client that messages with no
  `ClientId` are handled for, and it is the one step of the upgrade: templates and jobs that already exist
  have no owner, so they are given to this client, once, and the client is registered if it is new. **A
  deployment that has such templates or jobs does not start without it**, saying which setting to fill in
  and changing nothing. A new install needs nothing. See
  [docs/operations.md](docs/operations.md#upgrading-to-per-client-templates-and-jobs).
- **Two migrations**, `AddClientOwner` and `RequireClientOwner`, with the assignment between them. If you
  apply migrations yourself, run the statements in the operations guide between the two.
- Messages published to the bus that name no client, or an unregistered, malformed or disabled one, or that
  reuse another client's job id, are answered and nothing is rendered (see Contracts 1.2.0 above).

### Changed (service)

- A job published straight to the bus is recorded as `Pending` when work starts, not first when it finishes,
  so its client can poll it from the start.
- Anyone who can publish to the block's exchanges can name any client in a message. Give broker credentials
  only to trusted product backends. This is a known limit, now written down in
  [docs/message-contracts.md](docs/message-contracts.md#clientid).

## [Unreleased] — 1.1.0

Nothing here changes the wire format of 1.0.0. Producers and consumers on either version keep
matching.

### Added

- `GeneratePdfRequest`, a ready-made `IGeneratePdfRequest`. Use it instead of an anonymous object,
  so a misspelled member is a compile error rather than a request that arrives with a null value.
- `IGeneratePdfRequest.PageSetup` and the `PdfPageSetup` / `PdfPageMargins` types: optional
  per-request paper size, orientation and margins, including `Height = "auto"` for one page as
  long as the content (till receipts). Null, which is what any 1.0.0 publisher
  sends, keeps the template's own page setup. See [docs/message-contracts.md](docs/message-contracts.md).
- `PdfRequestLimits`: the template-key pattern, the 5 MB payload limit (in UTF-8 bytes) and helpers
  to check both before sending. The service validates against these same constants.
- `PdfErrorCodes.All` and `PdfErrorCodes.IsKnown`, for handling codes added by a newer service.
- Symbols (`.snupkg`) with embedded sources, project URL and release notes link.
- MIT license and author metadata. 1.0.0 shipped without a license.

### Changed

- Documentation only: `RENDER_FAILED` is now described correctly. A browser failure is retried
  and then raised as `Fault<IGeneratePdfRequest>`; it is not sent on a response. `TemplateNotFound`
  and `TemplateKey` now say *published* template and *key*, not *registered* and *id*.
- Documented what `FileUrl` is: unauthenticated by default, unsigned, no expiry, and retention set
  by the deployment.

### Fixed (service)

- A job submitted over HTTP whose render failed on every retry stayed `Pending` forever. The last
  attempt now marks it `Failed` with `RENDER_FAILED`. Bus requesters still receive a `Fault<T>`.

### Added (service): console sign-in and key management

Released together with API keys (below), so the web console keeps working: it signs in with the
admin key instead of sending none. The package, the messages and the existing error codes are
unchanged, and the contracts version does not change for this.

- **The console signs in with the admin key** (`POST /api/console/session`) and gets a 12-hour session
  in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie; `GET` says when it ends and `DELETE` signs out.
  Sessions are stored as rows keyed by a hash, so signing out, or replacing the admin key, ends a
  session on every instance at once, and a copied cookie stops working. Failed sign-ins are limited
  to 5 a minute per address.
- **The console is closed** to anyone without a session, and works within a chosen client: a switcher
  in the header, the client in the address, every request made on its behalf.
- **API keys and clients in the console:** list keys with when each was last used, create one (shown
  once, with a copy button, in a dialog that cannot be dismissed by accident), revoke with
  confirmation; add, disable and enable clients.
- **New:** `GET /api/admin/scopes` (the permissions a key can have, with descriptions); the optional
  `X-Client-Id` header, honoured only for the operator, to act as a client; codes
  `403 CONSOLE_HEADER_REQUIRED` (a cookie-authenticated change without `X-Console: 1`) and
  `429 RATE_LIMITED` (with `Retry-After`).
- **New settings, both optional:** `Blocks:Console:SessionLifetime` (default `12:00:00`) and
  `Blocks:ForwardedHeaders:KnownNetworks` (the reverse proxies to believe). Two new tables,
  `console_sessions` and `data_protection_keys`, are created by the migration at start-up.

#### Upgrading

- Serve the console over HTTPS (plain HTTP works only on `localhost`), from the same site as the API,
  and not inside another site's frame. Behind a reverse proxy, set
  `Blocks__ForwardedHeaders__KnownNetworks`. See [docs/operations.md](docs/operations.md#console-sign-in).

### Changed (service): API keys — breaking for callers without a key

The package and the messages are unchanged, and the contracts version does not change for this.
Over HTTP, every `/api` call now needs a key.

- **Every call to `/api` needs an `X-Api-Key`.** Products are registered as clients by the operator
  and given named keys with only the permissions they need (`pdf:generate`, `pdf:templates:read`,
  `pdf:templates:write`), optionally expiring. A key is shown once and stored only as a hash.
  Accepted responses are exactly what they were; refusals use problem details with a stable code:
  `401 AUTH_REQUIRED`, `401 AUTH_INVALID_KEY` (identical for unknown, revoked and expired keys),
  `403 AUTH_SCOPE_MISSING`, `403 CLIENT_DISABLED`, and `503 STORAGE_UNAVAILABLE` when a key cannot be
  checked.
- **Management endpoints** under `/api/admin/clients` register and disable clients and issue, list and
  revoke keys, with the admin key. Revoking a key or disabling a client takes effect on every
  instance within a minute; the key list shows when each key was last used.
- **Open as before:** `/health/live`, `/health/ready`, `GET /files/{fileId}`, and the API docs outside
  production. Messages on the bus carry no key and are not affected.
- `tools/Blocks.Cli admin-key` generates the admin key and its hash.

#### Upgrading

- **`Blocks:Admin:KeyHash` is required.** The service does not start without it, naming the setting.
  On the VM it is `ADMIN_KEY_HASH` in `.env`; on the Space, the secret `Blocks__Admin__KeyHash`.
- **Callers that send no key are refused from this release**, including the web console until its
  sign-in (released together with this). There is no setting that turns authentication off. Issue
  keys to existing products before upgrading; see
  [docs/operations.md](docs/operations.md#upgrading-to-api-keys).

### Changed (service)

None of this touches the package: the HTTP responses, the messages and the error codes are exactly
what they were, and the contracts version does not change for it.

- **Jobs and authored templates are stored in PostgreSQL**, so they survive restarts and are shared
  by every instance. Job status was in process memory, and templates were JSON files on one machine.
  A job stays pollable for `JobStore:RetentionHours` after its last update, as before.
- **A job and the message that starts it are committed together**, through MassTransit's EF Core
  outbox (still 8.5.x, Apache-2.0). A failure between the two can no longer leave a job that is
  `Pending` forever, or a message for a job nobody can poll.
- **A new failure response:** while the database is unreachable, requests that need it answer
  `503` with `code: STORAGE_UNAVAILABLE` and `Retry-After: 5`, and `/health/ready` reports not ready
  (liveness is unaffected). Nothing is accepted that cannot be recorded.
- **Authored templates are imported once.** On the first start against an empty database, the files
  of the previous version are imported, and left untouched so the upgrade can be rolled back. A file
  that cannot be imported is skipped and reported in the logs, not fatal.

#### Upgrading

- **A PostgreSQL database is now required.** Set `ConnectionStrings:Database`; the service does not
  start without it, and there is no in-memory fallback. The schema is created on first start
  (`Blocks:Database:MigrateOnStartup`, default `true`). `compose.yaml` and `deploy/compose.prod.yaml`
  now include one.
- **Jobs still in flight in the old version are lost once**, because they lived only in its memory.
- `TemplateStore:AuthoredRootPath` is only read, once, for the import, and can be removed afterwards.
- Generated PDFs are still on local disk: a second instance cannot yet serve a file another instance
  rendered.

### Note for implementers of `IGeneratePdfRequest`

Adding `PageSetup` (1.1.0) and `ClientId` (1.2.0) to the interface is source- and binary-breaking for a
class of your own that implements `IGeneratePdfRequest`: add a `PdfPageSetup? PageSetup { get; }` and a
`string? ClientId { get; }` that return null. Anonymous objects and `GeneratePdfRequest` are unaffected. The
breaks are accepted deliberately in `src/PdfBuilder.Contracts/CompatibilitySuppressions.xml` (CP0006); the
wire format is unchanged.

## [1.0.0] — 2026-10-28

First release on nuget.org.

### Migrating from the pre-release contracts

`IGeneratePdfRequest.TemplateId` was renamed to `TemplateKey`. The type name and exchange did not
change, so a publisher built against the old contracts still delivers, but its messages arrive with
no template key and are answered `VALIDATION_FAILED`. Update and deploy publishers together with the
service.

[Unreleased]: https://github.com/ahmedrooshdii/pdf-builder/compare/contracts-v1.0.0...HEAD
[1.0.0]: https://github.com/ahmedrooshdii/pdf-builder/releases/tag/contracts-v1.0.0
