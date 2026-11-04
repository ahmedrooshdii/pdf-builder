# Message contracts

`PdfBuilder.Contracts` is the only assembly another service needs. It has no dependencies, so it
works with any MassTransit version; the examples below are tested against MassTransit 8.x, which is
what the service itself runs.

Migrating from the pre-release contracts (`TemplateId`)? See [CHANGELOG.md](../CHANGELOG.md).

> The namespace and type name of each interface form the MassTransit message URN, which becomes the
> RabbitMQ exchange name. Renaming or moving them stops producers and consumers matching, silently.
> Treat these two files as published API.

> **HTTP needs a key; the bus does not.** Calls to `/api` carry an `X-Api-Key` issued by the operator
> ([operations.md](operations.md#api-keys)). Messages published to RabbitMQ are unchanged: they carry
> no key, and publishers are trusted through their broker credentials. Nothing in this package
> changed for API keys.

## Getting the package

Published to nuget.org as `PdfBuilder.Contracts` (targets `netstandard2.0` and `net10.0`). No feed
or credentials to set up: install it from the NuGet Package Manager, or

```bash
dotnet add package PdfBuilder.Contracts
```

To release a new version, tag the commit and push the tag; `.github/workflows/contracts.yml` packs
and publishes it:

```bash
git tag contracts-v1.1.0
git push origin contracts-v1.1.0
```

Use semantic versions. Adding a member is a minor version; renaming or removing one is a major
version and a breaking change on the wire (see the warning above). After a release, set
`PackageValidationBaselineVersion` in the `.csproj` to it, so the next pack is checked against it.
Record every release in [CHANGELOG.md](../CHANGELOG.md).

## IGeneratePdfRequest

Send it as a `GeneratePdfRequest`, the ready-made implementation in the package. A misspelled
member is then a compile error; with an anonymous object it is silently sent as null and comes back
`VALIDATION_FAILED`.

| Member | Type | Meaning |
| --- | --- | --- |
| `CorrelationId` | `Guid` | Your id for this job. Echoed on the response and attached to every log line the job produces. |
| `TemplateKey` | `string` | A published template, e.g. `invoice`. Must match `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`. A draft that was never published is reported as `TEMPLATE_NOT_FOUND`. |
| `JsonPayload` | `string` | A JSON object, serialised as text. Maximum 5 MB, counted in UTF-8 bytes, not characters. |
| `PageSetup` | `PdfPageSetup?` | Optional. Paper size, orientation or margins for this document only; null keeps the template's. See [PageSetup](#pagesetup). |

The limits are published as constants in `PdfRequestLimits`, and the service validates against the
same ones. Check them before sending to save a round trip:

```csharp
if (!PdfRequestLimits.IsValidTemplateKey(key) || !PdfRequestLimits.IsWithinPayloadLimit(json))
    throw new ArgumentException("Request would be rejected as VALIDATION_FAILED.");
```

### PageSetup

Every template carries its own page setup (see [templates.md](templates.md#page-setup)). A request
can change part of it for one document: set only what differs, and the template supplies the rest.

| `PdfPageSetup` member | Type | Meaning |
| --- | --- | --- |
| `Format` | `string?` | `A0`–`A6`, `Letter`, `Legal`, `Tabloid` or `Ledger`. |
| `Width`, `Height` | `string?` | A custom size, such as `80mm` × `200mm`. `Height` may be `auto`: one page exactly as tall as the content, for a till receipt. Both or neither, and not together with `Format`. |
| `Landscape` | `bool?` | |
| `PrintBackground` | `bool?` | False drops background colours and images. |
| `PreferCssPageSize` | `bool?` | True lets the template's `@page { size: … }` rule win over the size given here. |
| `Margins` | `PdfPageMargins?` | `Top`, `Right`, `Bottom`, `Left`. A null side keeps the template's margin. |

Lengths are a number with a unit, `mm`, `cm`, `in` or `px` (`80mm`, `0.5in`), at most 5 m. A
request that sets a size also turns `PreferCssPageSize` off unless it says otherwise, so the size
asked for is the size printed. A malformed page setup is answered `VALIDATION_FAILED`, naming the
field.

```csharp
var request = new GeneratePdfRequest(NewId.NextGuid(), "invoice", json)
{
    PageSetup = new PdfPageSetup
    {
        Width = "80mm",
        Height = "auto",   // one page as long as the receipt
        Margins = new PdfPageMargins { Top = "4mm", Right = "4mm", Bottom = "4mm", Left = "4mm" }
    }
};
```

Over HTTP it is the same shape, camel-cased, as `pageSetup` on `POST /api/pdf/jobs`.

## IGeneratePdfResponse

| Member | Type | Meaning |
| --- | --- | --- |
| `CorrelationId` | `Guid` | Echoes the request. |
| `IsSuccess` | `bool` | |
| `FileId` | `string?` | Opaque id of the stored PDF. Null on failure. |
| `FileUrl` | `string?` | Absolute download URL. Null on failure. See [Downloading the PDF](#downloading-the-pdf). |
| `SizeBytes` | `long?` | Null on failure. |
| `ErrorCode` | `string?` | One of `PdfErrorCodes`. Null on success. |
| `ErrorMessage` | `string?` | For humans. Do not branch on it; the wording can change. |
| `CompletedAt` | `DateTimeOffset` | When the job reached its final state. |

Follows the Claim Check pattern: the response carries a reference, never the PDF bytes.

## Error codes

| `PdfErrorCodes` | Value | Cause |
| --- | --- | --- |
| `ValidationFailed` | `VALIDATION_FAILED` | Bad template key, the payload is not a JSON object / too large, or the page setup is malformed. |
| `TemplateNotFound` | `TEMPLATE_NOT_FOUND` | No published template under that key (a draft counts as missing). |
| `TemplateRenderFailed` | `TEMPLATE_RENDER_FAILED` | Template syntax error, or a runtime error while binding data. |
| `RenderFailed` | `RENDER_FAILED` | The browser failed. Not sent on a response today: see below. |

Every code that arrives on a response is final: retrying the same message cannot help. Fix the
request or the template.

A browser failure is the exception to the table. The service treats it as transient, redelivers the
message three times with backoff, and if every attempt fails it raises
`Fault<IGeneratePdfRequest>` instead of responding (see [Failures](#failures-you-have-to-handle)).
`RENDER_FAILED` is reserved for that case; handle it like the others if it ever arrives.

New codes may be added in a minor version, so a newer service can send one your copy of the package
does not know. `PdfErrorCodes.IsKnown(code)` tells you; treat an unknown code as a generic failure
that retrying will not fix.

## Request/response

The natural shape when the caller needs the answer:

```csharp
services.AddMassTransit(x =>
{
    x.AddRequestClient<IGeneratePdfRequest>();
    x.UsingRabbitMq((ctx, cfg) => { cfg.Host("rabbitmq"); cfg.ConfigureEndpoints(ctx); });
});

// …

var response = await client.GetResponse<IGeneratePdfResponse>(
    new GeneratePdfRequest(NewId.NextGuid(), "invoice", JsonSerializer.Serialize(invoice)),
    cancellationToken, timeout: RequestTimeout.After(m: 2));

if (response.Message.IsSuccess)
    await DownloadAsync(response.Message.FileUrl!);
```

Set the timeout above your worst-case render — the default 30 seconds is tight for a long document
under load. Rendering has a 30-second budget of its own, and a transient failure is retried three
times with backoff before it gives up, so the true worst case is well over a minute.

## Fire and forget

When nothing is waiting on the result:

```csharp
await publishEndpoint.Publish<IGeneratePdfRequest>(
    new GeneratePdfRequest(NewId.NextGuid(), "invoice", json), cancellationToken);
```

Keep the explicit `<IGeneratePdfRequest>`, so the interface is the published message type.

An anonymous object with the same member names (`new { CorrelationId, TemplateKey, JsonPayload }`)
still works in either example; the compiler just cannot check the names for you.

`RespondAsync` on a published message with no response address falls back to publishing, so
`IGeneratePdfResponse` goes out as an event. Subscribe to it if you want the outcome without
blocking on a request.

Use `NewId.NextGuid()` rather than `Guid.NewGuid()`: it is sequential, which keeps it index-friendly
if job state ever reaches a database.

## Failures you have to handle

**A negative response.** `IsSuccess` is false and `ErrorCode` says why. The job is finished;
resubmitting the same request will fail the same way.

**`Fault<IGeneratePdfRequest>`.** Retries were exhausted on a transient failure, typically the
browser. The original message is on the `_error` queue. A request client sees it as a
`RequestFaultException` from `GetResponse`. Fire-and-forget callers subscribe to
`IConsumer<Fault<IGeneratePdfRequest>>`, or alert on `_error` queue depth. This one may succeed if
you resubmit later.

**Silence.** The consumer is down or the queue is backed up. This is what the request timeout is
for.

## Downloading the PDF

`FileUrl` is `{PublicBaseUrl}/files/{FileId}`, where `PublicBaseUrl` is the service's
`Storage:PublicBaseUrl` setting. Fetch it with a plain `GET`; the body is `application/pdf`.

- **No authentication by default.** Anyone who has the URL can download the file, and the
  service adds no credentials of its own. If your deployment puts a gateway in front of `/files`,
  that gateway decides what to send.
- **Not signed and does not expire.** The URL stays valid as long as the file exists.
- **Retention is a deployment decision.** The service does not delete files. Ask the operators of
  the instance you call how long they keep them; once a file is gone the URL returns `404`.
- **A lost URL can be rebuilt** from `FileId` as `{PublicBaseUrl}/files/{FileId}`. There is no
  message to request a new one, and none is needed.
- **Reachability** is whatever `PublicBaseUrl` points at: an internal address works only inside
  that network.

Download promptly and keep your own copy if you need the document long term.
