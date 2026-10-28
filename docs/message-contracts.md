# Message contracts

`PdfBuilder.Contracts` is the only assembly another service needs. It has no dependencies.

> The namespace and type name of each interface form the MassTransit message URN, which becomes the
> RabbitMQ exchange name. Renaming or moving them stops producers and consumers matching, silently.
> Treat these two files as published API.

## Getting the package

Published to nuget.org as `PdfBuilder.Contracts` (targets `netstandard2.0` and `net10.0`). No feed
or credentials to set up: install it from the NuGet Package Manager, or

```bash
dotnet add package PdfBuilder.Contracts
```

To release a new version, tag the commit and push the tag; `.github/workflows/contracts.yml` packs
and publishes it:

```bash
git tag contracts-v1.0.0
git push origin contracts-v1.0.0
```

Use semantic versions. Adding a member is a minor version; renaming or removing one is a major
version and a breaking change on the wire (see the warning above).

## IGeneratePdfRequest

> **Breaking change: `TemplateId` was renamed to `TemplateKey`.** The type name and exchange did not
> change, so an old publisher still delivers, but its messages arrive with no template key and are
> answered `VALIDATION_FAILED`. Update and deploy publishers together with this service.

| Member | Type | Meaning |
| --- | --- | --- |
| `CorrelationId` | `Guid` | Your id for this job. Echoed on the response and attached to every log line the job produces. |
| `TemplateKey` | `string` | A published template, e.g. `invoice`. Must match `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`. A draft that was never published is reported as `TEMPLATE_NOT_FOUND`. |
| `JsonPayload` | `string` | A JSON object, serialised as text. Maximum 5 MB. |

## IGeneratePdfResponse

| Member | Type | Meaning |
| --- | --- | --- |
| `CorrelationId` | `Guid` | Echoes the request. |
| `IsSuccess` | `bool` | |
| `FileId` | `string?` | Opaque id of the stored PDF. Null on failure. |
| `FileUrl` | `string?` | Absolute download URL. Null on failure. |
| `SizeBytes` | `long?` | Null on failure. |
| `ErrorCode` | `string?` | One of `PdfErrorCodes`. Null on success. |
| `ErrorMessage` | `string?` | For humans. Do not branch on it; the wording can change. |
| `CompletedAt` | `DateTimeOffset` | When the job reached its final state. |

Follows the Claim Check pattern: the response carries a reference, never the PDF bytes.

## Error codes

| `PdfErrorCodes` | Value | Cause |
| --- | --- | --- |
| `ValidationFailed` | `VALIDATION_FAILED` | Bad template key, or the payload is not a JSON object / too large. |
| `TemplateNotFound` | `TEMPLATE_NOT_FOUND` | No template registered under that id. |
| `TemplateRenderFailed` | `TEMPLATE_RENDER_FAILED` | Template syntax error, or a runtime error while binding data. |
| `RenderFailed` | `RENDER_FAILED` | The browser failed. Retried three times before you see this. |

All four are permanent as delivered: by the time a response arrives, retrying the same message
cannot help. Fix the request or the template.

## Request/response

The natural shape when the caller needs the answer:

```csharp
services.AddMassTransit(x =>
{
    x.AddRequestClient<IGeneratePdfRequest>();
    x.UsingRabbitMq((ctx, cfg) => { cfg.Host("rabbitmq"); cfg.ConfigureEndpoints(ctx); });
});

// …

var response = await client.GetResponse<IGeneratePdfResponse>(new
{
    CorrelationId = NewId.NextGuid(),
    TemplateKey = "invoice",
    JsonPayload = JsonSerializer.Serialize(invoice)
}, cancellationToken, timeout: RequestTimeout.After(m: 2));

if (response.Message.IsSuccess)
    await DownloadAsync(response.Message.FileUrl!);
```

Set the timeout above your worst-case render — the default 30 seconds is tight for a long document
under load. Rendering has a 30-second budget of its own, and a transient failure is retried three
times with backoff before it gives up, so the true worst case is well over a minute.

## Fire and forget

When nothing is waiting on the result:

```csharp
await publishEndpoint.Publish<IGeneratePdfRequest>(new
{
    CorrelationId = NewId.NextGuid(),
    TemplateKey = "invoice",
    JsonPayload = json
}, cancellationToken);
```

`RespondAsync` on a published message with no response address falls back to publishing, so
`IGeneratePdfResponse` goes out as an event. Subscribe to it if you want the outcome without
blocking on a request.

Use `NewId.NextGuid()` rather than `Guid.NewGuid()`: it is sequential, which keeps it index-friendly
if job state ever reaches a database.

## Failures you have to handle

**A negative response.** `IsSuccess` is false and `ErrorCode` says why. The job is finished;
resubmitting the same request will fail the same way.

**`Fault<IGeneratePdfRequest>`.** Retries were exhausted on a transient failure. The original
message is on the `_error` queue. Subscribe to `IConsumer<Fault<IGeneratePdfRequest>>` to be told,
or alert on `_error` queue depth.

**Silence.** The consumer is down or the queue is backed up. This is what the request timeout is
for.
