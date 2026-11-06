# Message Contract: `IGeneratePdfResponse` (Contracts 1.3.0)

Additive. The message type, its namespace and its exchange name are unchanged. Producers and consumers on
1.0.0–1.2.0 keep matching.

## Change

```csharp
public interface IGeneratePdfResponse
{
    // … unchanged members …

    /// <summary>
    /// Absolute URL the requester downloads the PDF from with a plain GET, no credentials. When the
    /// deployment requires signed links, it is signed and stops working at FileUrlExpiresAt; ask
    /// GET /api/pdf/jobs/{CorrelationId}/file-url for a fresh one. Otherwise it is not signed and
    /// works until the file is deleted after the deployment's retention period. Null when IsSuccess is false.
    /// </summary>
    string? FileUrl { get; }

    /// <summary>
    /// When FileUrl stops working. Null when the deployment does not sign links, and when IsSuccess is
    /// false. New in 1.3.0: a 1.2.0 consumer ignores it.
    /// </summary>
    DateTimeOffset? FileUrlExpiresAt { get; }
}
```

## Behaviour

| Outcome | `FileUrl` | `FileUrlExpiresAt` |
| --- | --- | --- |
| Success, switch on | signed, valid for `Blocks:Files:UrlLifetime` from when the reply is produced | its expiry |
| Success, switch off | `…/files/{fileId}`, as in 1.2.0 | null |
| Failure | null | null |

A file is deleted `Blocks:Files:RetentionDays` after it was created whichever way the switch is set; a
link to it stops working then. This is a service behaviour, announced in `CHANGELOG.md`, not a contract
change: the member always documented that the link's lifetime depends on the deployment.

## Package

- `PdfBuilder.Contracts.csproj`: `Version` `1.3.0-local`.
- `CompatibilitySuppressions.xml`: `CP0006` baseline suppression for
  `P:PdfBuilder.Contracts.IGeneratePdfResponse.FileUrlExpiresAt`, `net10.0` and `netstandard2.0`.
- `CHANGELOG.md`: new `[Unreleased] — 1.3.0` section above 1.2.0.
- `docs/message-contracts.md`: `IGeneratePdfResponse` table, "Downloading the PDF" section (fresh links,
  expiry, retention).
- `ContractsTests`: the reply serialises `FileUrlExpiresAt`; a payload without it deserialises to null.
