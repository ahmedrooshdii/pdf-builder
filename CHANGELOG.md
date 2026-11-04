# Changelog

Releases of the `PdfBuilder.Contracts` NuGet package. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). Each release is the git tag `contracts-v<version>`.

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

Adding `PageSetup` to the interface is source- and binary-breaking for a class of your own that
implements `IGeneratePdfRequest`: add a `PdfPageSetup? PageSetup { get; }` that returns null. Anonymous
objects and `GeneratePdfRequest` are unaffected. The break is accepted deliberately in
`src/PdfBuilder.Contracts/CompatibilitySuppressions.xml` (CP0006); the wire format is unchanged.

## [1.0.0] — 2026-10-28

First release on nuget.org.

### Migrating from the pre-release contracts

`IGeneratePdfRequest.TemplateId` was renamed to `TemplateKey`. The type name and exchange did not
change, so a publisher built against the old contracts still delivers, but its messages arrive with
no template key and are answered `VALIDATION_FAILED`. Update and deploy publishers together with the
service.

[Unreleased]: https://github.com/ahmedrooshdii/pdf-builder/compare/contracts-v1.0.0...HEAD
[1.0.0]: https://github.com/ahmedrooshdii/pdf-builder/releases/tag/contracts-v1.0.0
