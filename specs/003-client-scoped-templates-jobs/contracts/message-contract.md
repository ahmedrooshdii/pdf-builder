# Contract: `IGeneratePdfRequest.ClientId` (PdfBuilder.Contracts 1.2.0)

## Change

```csharp
public interface IGeneratePdfRequest
{
    // … existing members unchanged …

    /// <summary>
    /// The client this job is for, for example <c>ecommerce</c>. Null means the deployment's default
    /// client (<c>Blocks:Bus:DefaultClientId</c>). Must match <c>^[a-z][a-z0-9-]{0,39}$</c>.
    /// </summary>
    string? ClientId { get; }
}

public sealed class GeneratePdfRequest : IGeneratePdfRequest
{
    public string? ClientId { get; set; }
}

public static class PdfErrorCodes
{
    /// <summary>The client named by the message (or the default client) is disabled.</summary>
    public const string ClientDisabled = "CLIENT_DISABLED";
}
```

Additive: message type, namespace and exchange name are unchanged. Publishers on 1.0.0 or 1.1.0
send no `ClientId`, which deserialises as `null`.

## How the service handles it

| `ClientId` | `Blocks:Bus:DefaultClientId` | Result |
| --- | --- | --- |
| valid, registered, enabled | any | job handled for that client |
| null | set, registered, enabled | job handled for the default client |
| null | unset | response `IsSuccess = false`, `VALIDATION_FAILED`, "The message names no client and no default client is configured." |
| malformed | any | response `VALIDATION_FAILED`, "ClientId is not a valid client id." |
| not registered (named, or the default) | any | response `VALIDATION_FAILED`, "Client '<id>' is not registered." |
| disabled (named, or the default) | any | response `CLIENT_DISABLED` |

In each refused case no job is recorded and nothing is rendered.

Once the client is known:

- The template is the client's own published template with that key, otherwise the built-in one,
  otherwise `TEMPLATE_NOT_FOUND`.
- The job (`CorrelationId`) is recorded under that client as `Pending` before rendering, and can be
  polled over HTTP with that client's key.
- If `CorrelationId` is already the id of **another client's** job: response `VALIDATION_FAILED`,
  "CorrelationId is already used by another job; send a new one." That job is untouched.
- The same client resending the same `CorrelationId` behaves as before.
- Every log line of the message carries `ClientId`.

## Trust

The service cannot check that a message comes from the client it names. Anyone with broker
credentials for the block's vhost can name any client, so only trusted product backends get them,
each with its own broker user. `docs/message-contracts.md` states this.

## Release

- `PdfBuilder.Contracts` `1.2.0-local` until tagged `contracts-v1.2.0`.
- `CompatibilitySuppressions.xml`: `CP0006` for `P:PdfBuilder.Contracts.IGeneratePdfRequest.ClientId`,
  for both target frameworks (baseline).
- `CHANGELOG.md`: a 1.2.0 section with the member, the code and the default-client rule.
- `ContractsTests`: `ClientDisabled` is in `All`, and a 1.1.0-shaped message (no `ClientId`)
  deserialises with `ClientId == null`.
