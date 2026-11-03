# Contract: HTTP API and Messages (pinned)

This feature MUST NOT change any of the following (FR-014, FR-015, FR-016). The existing tests and
`ContractsTests` are the regression suite; this list is what they protect.

## HTTP endpoints, unchanged

| Endpoint | Responses that stay byte-identical |
| --- | --- |
| `POST /api/pdf/jobs` | 202 `{ jobId, statusUrl }` with `Location`; 400 validation problem |
| `GET /api/pdf/jobs/{jobId}` | 200 `JobStatusResponse` (all ten fields, same names and formats); 404 when unknown or past retention |
| `GET /files/{fileId}` | Unchanged (files are out of scope) |
| `GET /api/templates` | 200 list of `TemplateSummaryResponse`, authored first then non-shadowed built-ins |
| `GET /api/templates/{key}` | 200 `TemplateResponse`; 404 problem `TEMPLATE_NOT_FOUND` |
| `POST /api/templates` | 201; 400; 409 `TEMPLATE_KEY_TAKEN` |
| `POST /api/templates/{key}/duplicate` | 201; 400; 404; 409 `TEMPLATE_KEY_TAKEN` |
| `PUT /api/templates/{key}/draft` | 200; 400; 404; 409 `TEMPLATE_REVISION_CONFLICT` (stale revision) or `TEMPLATE_READ_ONLY` (built-in) |
| `POST /api/templates/{key}/publish` | 200; 404; 409 `TEMPLATE_REVISION_CONFLICT` / `TEMPLATE_READ_ONLY`; 422 `TEMPLATE_RENDER_FAILED` |
| `DELETE /api/templates/{key}` | 204; 404; 409 `TEMPLATE_READ_ONLY` (built-in) |
| `POST /api/templates/preview` | Unchanged (no storage) |

## One new failure response

| When | Response |
| --- | --- |
| Any endpoint above that touches storage, while the database is unreachable | `503 Service Unavailable`, `application/problem+json`, `code: "STORAGE_UNAVAILABLE"`, `Retry-After: 5`. Previously an unhandled 500 (or, for jobs, no failure at all while data was silently lost on restart). |

## Messages, unchanged

`IGeneratePdfRequest`, `IGeneratePdfResponse` and `PdfErrorCodes` are untouched; the
`PdfBuilder.Contracts` package version does not change. The only behavioural difference on the bus:
a request published by the HTTP endpoint now leaves through the outbox, so it is sent only after
the job is committed. Producers publishing straight to RabbitMQ see no difference.

## Console

No changes to `web/`. Its job history still lives in the browser (feature 010 changes that).
