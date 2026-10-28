# Operations

## Deployment

Build from the repository root — the Dockerfile needs `templates/` and the root props files:

```bash
docker build -f src/PdfBuilder.Api/Dockerfile -t pdf-builder .
```

The image installs Chromium with the driver bundled inside the `Microsoft.Playwright` package, so
the browser build always matches the library version. It also installs Liberation, DejaVu and the
Noto font families including CJK and colour emoji — without them Chromium renders missing-glyph
boxes, which shows up first on non-Latin scripts.

The container runs as the non-root `app` user and exposes `8080`.

### Volumes

| Path | Purpose |
| --- | --- |
| `/data` | Rendered PDFs. Must survive restarts, or every `FileUrl` already handed out breaks. |
| `/app/Templates` | Template content. Mount it to change templates without rebuilding. |

### Must-set configuration

`Storage__PublicBaseUrl` has to be the address callers can reach, not the container's. It is what
`FileUrl` is built from, so getting it wrong means every response hands out a dead link —
and nothing fails until a caller tries to download.

## Health

| Endpoint | Meaning | Wire it to |
| --- | --- | --- |
| `/health/live` | The process is up. Runs no dependency checks. | Liveness probe |
| `/health/ready` | Dependencies are usable: RabbitMQ is connected and the template directory exists. | Readiness probe |

The split matters. Restarting a container because RabbitMQ is briefly unreachable makes an outage
worse; readiness just takes the instance out of rotation until it recovers.

The template-directory check exists because an unmounted volume is the most common deployment
mistake here, and without the check it shows up as every job failing `TEMPLATE_NOT_FOUND`.

## Sizing

`Playwright:MaxConcurrentRenders` is the lever. Each in-flight render costs roughly a browser tab;
budget around 150–250 MB per concurrent render plus the base image, and scale CPU with it since
rendering is CPU-bound.

Starting point: 2 vCPU / 2 GB with `MaxConcurrentRenders: 4`. Raise it only while render latency
stays flat — past the point where renders start queueing inside the process, you are adding memory
pressure rather than throughput, and horizontal scaling is the better answer.

The consumer's prefetch is tied to the same number, so excess work stays visible in RabbitMQ instead
of piling up in process memory where no dashboard can see it and a restart loses it.

Shutdown waits up to 60 seconds for in-flight renders. Give the orchestrator at least that in its
termination grace period, or rolling deployments will cut jobs off mid-render.

## Failure modes

| Symptom | Likely cause | Where to look |
| --- | --- | --- |
| Every job fails `TEMPLATE_NOT_FOUND` | Template volume not mounted | `/health/ready`; `TemplateStore:RootPath` |
| Jobs fail `RENDER_FAILED` after retries | Chromium OOM, or a template waiting on a blocked asset | Container memory; `--disable-dev-shm-usage` present; `Playwright:AllowedHosts` |
| `FileUrl` 404s for the caller | `Storage:PublicBaseUrl` wrong, or more than one replica on local disk | [Scaling past one instance](#scaling-past-one-instance) |
| Boxes instead of glyphs | Font family not in the image | Add the font package to the Dockerfile |
| `_error` queue filling up | Transient failures exhausting retries | Consumer logs for the `CorrelationId` |
| Service will not start, `License must be specified` | MassTransit bumped back to 9.x | Pin 8.5.x, or set `MT_LICENSE` |

Every log line a job produces carries `CorrelationId` and `TemplateKey` in scope, so a structured
sink can show one job's whole history together.

## Scaling past one instance

Two things are single-instance today, and both will bite at the same moment:

**Local file storage.** A download request load-balanced to an instance that did not render the
file gets a 404. Fix: implement `IFileStorage` over blob/object storage. It is one interface with
two methods, and nothing above it changes.

**In-memory job store.** A poll of `/api/pdf/jobs/{id}` can land on an instance that never saw the
job. Fix: implement `IPdfJobStore` over Redis or a table. Also two methods. Note that `PdfJob` has a
private constructor, so a persistent implementation should map to its own DTO rather than
serialising the domain record directly.

Bus callers are unaffected by either: they get their result from `IGeneratePdfResponse`, not from
the job store.

Do both before adding a replica. Until then, run one instance and scale vertically.

## Known gaps

**Stored PDFs are never deleted.** Disk grows without bound. Until retention exists, either mount a
volume you watch, or run a cron job over `Storage:RootPath` by file age. A real fix belongs behind
`IFileStorage` — an object-store lifecycle rule is the cheapest version.

**Downloads are unauthenticated.** Anyone holding a `fileId` can fetch the PDF. The id is a
random GUID, so it is not guessable, but it is a bearer token in a URL: it will end up in
proxy logs, browser history and email. If documents are sensitive, put authentication in front of
`GET /files/{fileId}` or switch to time-limited signed URLs.

**No metrics or traces.** Render duration, queue depth and browser recycle rate are the first three
numbers worth having. OpenTelemetry would slot in at `Program.cs` — MassTransit and ASP.NET Core
both instrument themselves already.

**Job state is lost on restart.** A job submitted over HTTP and still pending when the process
restarts becomes unpollable, even though the message survives in RabbitMQ and will still be
rendered. Resolved by the same change that makes the job store shared.

## Tests in CI

```bash
dotnet test                                      # fast, no browser or broker needed
PDFBUILDER_RUN_BROWSER_TESTS=1 dotnet test       # also launches real Chromium
```

CI should run the second form. The browser tests are the ones that prove a real PDF comes out,
that recycling works mid-flight, and that non-allow-listed hosts are actually blocked. They need
Chromium installed:

```bash
pwsh tests/PdfBuilder.Infrastructure.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Warnings are errors, so a build that succeeds locally succeeds in CI.
