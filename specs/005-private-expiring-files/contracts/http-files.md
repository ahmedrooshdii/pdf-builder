# HTTP Contract: Files and Job Files

All additions are additive (Principle VII). Errors are RFC 9457 problem details with `code` and
`traceId`, except the signed-link endpoint, which answers a bare `404` exactly as it does today for a
missing file.

## `GET /files/{fileId}` (changed)

Anonymous. The link itself is the credential.

| Query | Meaning |
| --- | --- |
| `expires` | Unix time in seconds; the link works while the current time is before it. |
| `sig` | `base64url(HMAC-SHA256(Blocks:Files:SigningKey, "{fileId}.{expires}"))`, no padding, 43 characters. |

| Situation | Switch off | Switch on |
| --- | --- | --- |
| No `expires` and no `sig`, file present | `200` PDF | `404` |
| Valid `sig`, now < `expires`, file present | `200` PDF | `200` PDF |
| `expires` reached, `sig` wrong, either parameter missing, repeated or malformed | `404` | `404` |
| File deleted, missing or never existed | `404` | `404` |

Every `404` is identical: no body, no hint at the reason (FR-004).

`200` headers: `Content-Type: application/pdf`, `Content-Disposition: attachment; filename={fileId}.pdf`,
`Cache-Control: private, no-store`, `Referrer-Policy: no-referrer`, `X-Robots-Tag: noindex`.

## `GET /api/pdf/jobs/{jobId}` (changed)

Unchanged policy (`pdf:generate`, acting client). Response gains two members and one changes meaning
with the switch:

```json
{
  "jobId": "0b7e…",
  "templateKey": "invoice",
  "status": "Succeeded",
  "enqueuedAt": "2026-11-05T10:00:00+00:00",
  "completedAt": "2026-11-05T10:00:03+00:00",
  "fileId": "a7e0495b…",
  "fileUrl": "https://pdf.example.com/files/a7e0495b…?expires=1793614800&sig=3q2-7wE…",
  "fileUrlExpiresAt": "2026-11-05T11:12:40+00:00",
  "fileExpired": false,
  "sizeBytes": 48211,
  "errorCode": null,
  "errorMessage": null
}
```

| Member | Rule |
| --- | --- |
| `fileUrl` | Succeeded and file kept: a new link per read. Switch on: signed, valid for `UrlLifetime` from this read. Switch off: `…/files/{fileId}`, as before. Null when not succeeded or file expired. |
| `fileUrlExpiresAt` | **New.** Expiry of `fileUrl` while the switch is on; null otherwise. |
| `fileExpired` | **New.** `true` when the job succeeded and its file has been deleted or is missing; `false` otherwise. |
| `fileId` | Unchanged; still present when the file has expired. |

A succeeded job stays readable as long as its file is kept, then for one `JobStore:RetentionHours`
more (FR-019). Other jobs: unchanged.

## `GET /api/pdf/jobs/{jobId}/file-url` (new)

Policy `pdf:generate`, `.RequireActingClient()`. No body.

| Situation | Answer |
| --- | --- |
| Own job, succeeded, file kept | `200 { "fileUrl": "…", "fileUrlExpiresAt": "…" \| null }`, `Cache-Control: no-store` |
| Unknown id, another client's job, or job past retention | `404`, identical to `GET /api/pdf/jobs/{jobId}` for a missing job |
| Own job, `Pending` or `Failed` | `409`, `code: JOB_HAS_NO_FILE`, title "Job has no file" |
| Own job, file deleted or missing | `410`, `code: FILE_EXPIRED`, title "File expired" |

Nothing is rendered. Earlier links stay valid until their own expiry (FR-005).

## `GET /api/pdf/jobs/{jobId}/file` (new)

Policy `pdf:generate`, `.RequireActingClient()`. For the console and for products that prefer to
fetch the PDF themselves.

`200` with the same headers as `GET /files/{fileId}`. Refusals as for `file-url`. Works whichever way
the switch is set. A console session needs no `X-Console` header (it is a `GET`) but must send
`X-Client-Id`, which the console's interceptor does.

## Error codes (new, HTTP only)

| Code | Status | Meaning |
| --- | --- | --- |
| `JOB_HAS_NO_FILE` | 409 | The job is pending or failed; there is no file. Final for a failed job; for a pending one, poll the job. |
| `FILE_EXPIRED` | 410 | The file was deleted after the retention period, or is missing. Final: submit the job again to get a new file. |

## OpenAPI

Both new endpoints are tagged "PDF jobs" and declare `200`, `404`, `409`, `410`. `JobStatusResponse`
gains the two members. The `ApiKey` security scheme already applies to the group.
