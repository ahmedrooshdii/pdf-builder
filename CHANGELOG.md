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
