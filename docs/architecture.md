# Architecture

## What the service does

One job: bind a JSON payload to an HTML template, print the result with a headless browser, store
the PDF, and return a reference to it.

That job has an awkward shape. Rendering takes seconds, holds a browser process, and is the thing
most likely to fail. Everything in the design below follows from refusing to do that work on an
HTTP request thread.

## The flow

```
                 ┌──────────────────────────── PdfBuilder.Api (one process) ────────────────────────────┐
                 │                                                                                      │
 HTTP caller ──► │  POST /api/pdf/jobs                                                                  │
                 │       │                                                                              │
                 │       ├─ validate (the command's own validator, reused)  ──► 400 on bad input        │
                 │       ├─ publish IGeneratePdfRequest ─┐                                               │
                 │       ├─ record job as Pending ───────┴─► PostgreSQL (one transaction)                │
                 │       │                                   └─ outbox ─► RabbitMQ, after commit         │
                 │       └─ 202 Accepted + statusUrl                                                    │
                 │                                                      │                               │
                 │                                                      ▼                               │
 bus caller ───────── publish IGeneratePdfRequest ────────►  GeneratePdfConsumer                        │
                 │                                                      │                               │
                 │                                       GeneratePdfCommandHandler                      │
                 │                                                      │                               │
                 │        IPdfTemplateRepository ─► ITemplateEngine ─► IPdfRenderer ─► IFileStorage      │
                 │          (db + files)            (Scriban)          (Playwright)    (local disk)      │
                 │                                                      │                               │
                 │                                  ┌───────────────────┴──────────────────┐            │
                 │                                  ▼                                      ▼            │
                 │                          IPdfJobStore (poll)              IGeneratePdfResponse (bus)  │
                 │                                                                                      │
 HTTP caller ──► │  GET /api/pdf/jobs/{id}  ──► job state                                                │
 HTTP caller ──► │  GET /files/{fileId}     ──► the PDF bytes                                            │
                 └──────────────────────────────────────────────────────────────────────────────────────┘
```

## Layers

Dependencies point one way: `Api -> Infrastructure -> Application -> Domain -> Contracts`.

**Contracts** is the published vocabulary — the message interfaces and the error codes that travel
on them. It depends on nothing, so any service in the estate can reference it without dragging this
one along. Everything else may reference it; that is why `Domain` can use `PdfErrorCodes` without
duplicating four string literals.

> The namespace and type name of a MassTransit message become its URN, which becomes the RabbitMQ
> exchange name. Renaming or moving `IGeneratePdfRequest` silently stops producers and consumers
> matching. Treat that file as published API.

**Domain** holds the model (`PdfTemplate`, `PdfRenderOptions`, `PdfJob`) and the failure hierarchy.
No I/O, no framework.

**Application** holds the single use case and, in `Abstractions/`, the ports it needs. It names what
it wants — a template repository, a template engine, a renderer, storage, a job store — and never
learns what provides them.

**Infrastructure** provides one adapter per port, in a folder named after the capability rather than
the technology (`Rendering/`, not `Playwright/`). Swapping Scriban for Razor, or local disk for blob
storage, is a change inside one folder.

**Api** is the composition root and the only executable. It hosts both the HTTP endpoints and the
RabbitMQ consumer.

## Decisions worth knowing

### Failure is transient or permanent, and the exception says which

`PdfBuilderException.IsTransient` is the single switch that drives retry behaviour:

| Exception | Transient | Error code | What happens |
| --- | --- | --- | --- |
| `TemplateNotFoundException` | no | `TEMPLATE_NOT_FOUND` | Answered immediately with a failure response. |
| `TemplateRenderException` | no | `TEMPLATE_RENDER_FAILED` | Same. |
| `ValidationException` (FluentValidation) | no | `VALIDATION_FAILED` | Same. |
| `PdfRenderException` | yes | `RENDER_FAILED` | Bubbles out; MassTransit redelivers up to 3 times. The last attempt marks the tracked job failed with this code, then the message goes to the `_error` queue and the requester gets a `Fault<T>`. |

This used to be implicit — "is it a `DomainException`?" — which meant the retry policy depended on a
class hierarchy nobody looking at the consumer could see. Now a new failure type has to answer the
question at the point where the answer is known.

### Claim Check

Responses carry a `FileUrl`, never the bytes. A 2 MB invoice through RabbitMQ would be a message
broker being used as a file server: slow, memory-hungry, and bounded by frame size limits.
`GET /files/{fileId}` is the redemption half.

### Two ways to get a result

Bus callers get `IGeneratePdfResponse` — that is the real interface, and it needs no polling.

HTTP callers cannot receive a bus response, so `IPdfJobStore` records job state for them to poll.
That state is in PostgreSQL, so it survives a restart and a poll can land on any instance. See
[Jobs and authored templates live in the database](#jobs-and-authored-templates-live-in-the-database).

### Accepting a job is atomic

Accepting a job means two writes: the job's record, and the message that starts the work. Done one
after the other, a failure between them leaves either a job that is `Pending` forever (recorded,
never queued) or a message for a job nobody can poll (queued, never recorded).

The HTTP endpoint publishes the request through MassTransit's **EF Core bus outbox** and then saves
the job. The publish only queues the message in the database context; the save commits it together
with the job row, in one transaction, and a background delivery service then moves it to RabbitMQ.
So a job is either both recorded and queued, or neither, and a worker can never start before the
job it reports on exists.

Only the HTTP side uses the outbox. The consumer is left as it was (no consumer inbox or outbox):
it already tolerates redelivery, and changing it would change how retries and the final
`RENDER_FAILED` transition interact. This is MassTransit 8.5.x, still Apache-2.0.

### Jobs and authored templates live in the database

`IPdfJobStore` and `IAuthoredTemplateStore` are implemented over PostgreSQL (EF Core, Npgsql); the
built-in templates stay on disk because they ship in the image.

- **Jobs** keep the retention they always had: a job is readable for `JobStore:RetentionHours`
  after its **last save** (every save used to give it a fresh expiry). Reads apply the cut-off
  themselves; an hourly sweeper only deletes what is past it.
- **Templates** keep their revision check, now done by the database: an update names the revision
  it was based on and matches only that row, so two instances cannot both win. A unique index on
  the lower-cased key keeps keys unique ignoring case, as the catalog always did.
- **Records, not domain types.** `PdfJob` and `AuthoredTemplate` have private constructors and are
  never mapped directly; each table has a record in `Infrastructure/Persistence/Records/` that
  converts to and from the domain type. Stored JSON uses the property names the old template files
  used, which is also how the one-time import reads them.
- **The schema is applied on start**, before the bus connects and before the server listens, and
  the one-time import of the old template files runs right after it, under a database lock so that
  it happens once however many instances start together.

### Concurrency is bounded in two places, deliberately matched

`PlaywrightPdfRenderer` holds a semaphore of `MaxConcurrentRenders`, and
`GeneratePdfConsumerDefinition` sets `ConcurrentMessageLimit` to the same number. If the consumer
pulled more messages than the renderer could serve, the excess would queue in this process's memory
— invisible to RabbitMQ's metrics, and lost on restart.

### The browser is recycled, not restarted

Chromium leaks across renders. `BrowserLease` reference-counts it so recycling is two steps: stop
handing it out, then close it once in-flight renders finish. Closing it immediately would surface as
spurious `TargetClosedException`s on jobs that were doing nothing wrong.

Each render gets its own `IBrowserContext`, which is Playwright's isolation boundary — one job
cannot see another's cookies, storage or cache.

### Templates cannot reach the network or the disk

Two defences, both on by default:

- `Playwright:AllowedHosts` is empty, so the renderer aborts every request that is not `data:`,
  `about:` or `blob:`. Without it, a template is a tool for making the server fetch internal
  addresses on the author's behalf.
- Scriban runs with no `TemplateLoader`, so `{{ include }}` cannot read a file.

JavaScript is also off, because payload data is untrusted and Scriban does not auto-escape HTML.

### Page setup belongs to the template

`PdfRenderOptions` existed before but nothing could supply it, so every document came out A4
portrait. A template may now ship a sibling `{templateKey}.options.json`; see
[templates.md](templates.md#page-setup).

## Dependency notes

### MassTransit is pinned to 8.5.x on purpose

MassTransit 9 is commercially licensed and **refuses to start** without `MT_LICENSE` —
`ConfigurationException: License must be specified`. The project was originally on 9.2.3, which
meant it could not run at all.

8.5.11 is the last Apache-2.0 release and is API-compatible with everything here. If a licence is
purchased, bump the version in `Directory.Packages.props` and set `MT_LICENSE`; no code changes are
needed.

## Where this design runs out

**One deployable hosts both the API and the worker.** Convenient, and right while storage is local
disk — the process that wrote the file is the one that serves it. It stops being right when the HTTP
side and the render side need to scale differently, which they eventually will: the API is I/O-bound
and cheap, the renderer is memory-bound and expensive.

The split is mechanical once storage is shared: move `Messaging/` into a `PdfBuilder.Worker` host,
keep `Endpoints/` in the API, and have both call `AddApplication().AddInfrastructure(...)`.

**No observability beyond logs.** There are no metrics and no traces. Render duration, queue depth
and browser recycle rate are the three numbers you will want first.

**Stored PDFs are never deleted.** See [operations.md](operations.md#known-gaps).
