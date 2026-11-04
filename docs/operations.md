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

Jobs and authored templates are **not** on a volume of this container: they are in PostgreSQL, whose
data directory is the thing to back up. `pg_dump` of the service's database is a complete backup of
them.

### Must-set configuration

`ConnectionStrings__Database` is the connection string of the service's PostgreSQL database. There is
no default and no in-memory fallback: without it the service does not start, and says which setting
is missing. See [configuration.md](configuration.md#database).

`Storage__PublicBaseUrl` has to be the address callers can reach, not the container's. It is what
`FileUrl` is built from, so getting it wrong means every response hands out a dead link —
and nothing fails until a caller tries to download.

`Blocks__Admin__KeyHash` is the hash of the operator's admin key. Without it the service does not
start, and says so. See [configuration.md](configuration.md#admin-key) and [API keys](#api-keys).

### First start and upgrades

On start the service applies any pending schema migrations (`Blocks:Database:MigrateOnStartup`), so
there is no separate migration step to run. Several instances starting together are safe: EF Core
takes a database lock and one of them migrates.

On the **very first** start against an empty database, EF Core logs two `Failed executing DbCommand`
errors for `SELECT … FROM "__EFMigrationsHistory"`. They are expected, because the table does not
exist yet, and are followed by `Database schema brought up to date`. They do not appear again.

### Upgrading from file-based templates

Earlier versions kept templates written in the web app as JSON files in
`TemplateStore:AuthoredRootPath`. On the first start against an empty database the service imports
every readable file, once, and logs `Imported N authored template(s) from …`. Then:

- **Nothing is re-imported on later starts**, even though the files are still there. A template
  deleted after the upgrade stays deleted.
- **The files are never changed or removed.** To roll back, start the previous version; it finds
  its files as it left them. Templates edited after the upgrade are not carried back.
- **A file that cannot be imported is skipped, not fatal**, and logged as a warning naming the file
  and why: `malformed` (not valid template JSON), `invalid-key`, `built-in-key` (a template that
  ships with the service has the same key, ignoring case), `duplicate-key` or `unreadable`. The
  list is also kept in the `template_imports` table.
- **Jobs that were in flight in the old version are lost once**, on the upgrade itself: they lived
  only in its memory. A caller that polls one gets 404, as it would have after any restart before.

### Upgrading to API keys

From this version every call to `/api` needs an `X-Api-Key`, and there is no setting that turns this
off. A product that sends no key is refused with `401 AUTH_REQUIRED` from the moment the new version
starts, and so is the web console until its sign-in ships (feature 004, released together with
this). So, in this order:

1. Generate the admin key on your own machine (`dotnet run --project tools/Blocks.Cli -- admin-key`)
   and set its hash in the deployment ([configuration.md](configuration.md#admin-key)).
2. Upgrade. Health checks need no key, so probes and load balancers carry on.
3. Register each product as a client and issue it a key ([samples/admin.http](../samples/admin.http)),
   then put the key in the product's configuration.

Messages published straight to RabbitMQ are not affected: publishers are trusted through their
broker credentials, as before.

## API keys

Each product is a **client** and holds one or more **keys**; the operator holds the **admin key**.
Everything below is done with the admin key against `/api/admin/clients…`; every call is in
[samples/admin.http](../samples/admin.http).

- **Issuing.** A key has a name, one or more permissions, and optionally an expiry. The response that
  issues it is the only time it is shown: the service stores only a hash. A lost key is not
  recoverable; issue a new one and revoke the old.
- **Permissions** are independent: `pdf:generate` (submit and read jobs), `pdf:templates:read` (list
  and read templates), `pdf:templates:write` (create, duplicate, edit, publish, delete, preview).
  Give a product only what it uses. A key's permissions never change; issue a new key instead.
- **Revoking** is permanent. **Disabling** a client is the reversible switch: all its keys are refused
  with `CLIENT_DISABLED` until it is enabled again, and the ones not revoked then work again.
- **Within a minute.** Each instance remembers what it read about a key for 60 seconds, so a
  revocation, a disable or an enable reaches every instance within a minute. A key's expiry is exact.
- **Before revoking an old key**, check `lastUsedAt` in the key list: it is accurate to the minute,
  and `null` means never used.
- **Rotating the admin key.** Generate a new one, change `Blocks:Admin:KeyHash`, restart. The old key
  stops working at once. Client keys are not affected.
- **Logs.** Every log line of an accepted request carries `ClientId` and `KeyId`, or `Admin = true`.
  Issuing, revoking, registering, disabling and enabling are logged at Information. No log line
  contains a key, and refusals for bad keys are logged below Warning, so a scan of invented keys
  cannot flood the logs.

## Health

| Endpoint | Meaning | Wire it to |
| --- | --- | --- |
| `/health/live` | The process is up. Runs no dependency checks. | Liveness probe |
| `/health/ready` | Dependencies are usable: RabbitMQ is connected, the database answers, and the template directory exists. | Readiness probe |

The split matters. Restarting a container because RabbitMQ or the database is briefly unreachable
makes an outage worse; readiness just takes the instance out of rotation until it recovers, and it
recovers by itself when the dependency returns, without restarting the service.

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
| HTTP jobs fail `RENDER_FAILED` after retries (bus callers get a `Fault<T>`) | Chromium OOM, or a template waiting on a blocked asset | Container memory; `--disable-dev-shm-usage` present; `Playwright:AllowedHosts` |
| `FileUrl` 404s for the caller | `Storage:PublicBaseUrl` wrong, or more than one replica on local disk | [Scaling past one instance](#scaling-past-one-instance) |
| Boxes instead of glyphs | Font family not in the image | Add the font package to the Dockerfile |
| Requests answer `503` with `code: STORAGE_UNAVAILABLE` and a `Retry-After`; `/health/ready` is 503 while `/health/live` is 200 | The database is unreachable | The connection string, the database server, the network. Nothing was accepted; callers can retry. |
| Service will not start, `ConnectionStrings:Database is required` | The connection string is not set | [configuration.md](configuration.md#database) |
| Service will not start, `Blocks:Admin:KeyHash is required` or `must be 64 lowercase hexadecimal characters` | The admin key hash is not set, or the key was pasted instead of its hash | [configuration.md](configuration.md#admin-key) |
| A product gets `401 AUTH_REQUIRED` | It sends no `X-Api-Key` (for example, it predates API keys) | Issue it a key: [API keys](#api-keys) |
| A product gets `401 AUTH_INVALID_KEY` | The key is unknown, mistyped, revoked or expired; the answer is the same for all four on purpose | The client's key list: state, expiry. A truncated copy is the usual cause. |
| A product gets `403 AUTH_SCOPE_MISSING` | The key is fine but lacks the permission this endpoint needs, or a client key called `/api/admin` | Issue a key with the permission |
| A product gets `403 CLIENT_DISABLED` | Its client is disabled | Enable it, if that was not intended |
| A key with requests answers `503 STORAGE_UNAVAILABLE` | The key could not be checked: the database is unreachable | As above; the product should retry, never treat this as a revoked key |
| Service will not start, `The database schema is out of date` | `MigrateOnStartup` is `false` and migrations were not applied | Apply the migrations it names, or set it to `true` |
| `_error` queue filling up | Transient failures exhausting retries | Consumer logs for the `CorrelationId` |
| Service will not start, `License must be specified` | MassTransit bumped back to 9.x | Pin 8.5.x, or set `MT_LICENSE` |

Every log line a job produces carries `CorrelationId` and `TemplateKey` in scope, so a structured
sink can show one job's whole history together. Every log line of an HTTP request that passed
authentication carries `ClientId` and `KeyId` (or `Admin = true`), so each call can be attributed.

## Scaling past one instance

Job status and authored templates are in PostgreSQL, so every instance sees them: a poll of
`/api/pdf/jobs/{id}` can land on any instance, and a template published through one is what all of
them render. Instances start safely together (one migrates, one imports).

One thing is still single-instance:

**Local file storage.** A download request load-balanced to an instance that did not render the
file gets a 404. Fix: implement `IFileStorage` over blob/object storage. It is one interface with
two methods, and nothing above it changes.

Bus callers are unaffected: they get their result from `IGeneratePdfResponse`. Until files are
shared, run one instance and scale vertically, or pin downloads to the instance that rendered them.

## Known gaps

**Stored PDFs are never deleted.** Disk grows without bound. Until retention exists, either mount a
volume you watch, or run a cron job over `Storage:RootPath` by file age. A real fix belongs behind
`IFileStorage` — an object-store lifecycle rule is the cheapest version.

**Downloads are unauthenticated**, unlike every `/api` call. Anyone holding a `fileId` can fetch the PDF. The id is a
random GUID, so it is not guessable, but it is a bearer token in a URL: it will end up in
proxy logs, browser history and email. If documents are sensitive, put authentication in front of
`GET /files/{fileId}` or switch to time-limited signed URLs.

**No metrics or traces.** Render duration, queue depth and browser recycle rate are the first three
numbers worth having. OpenTelemetry would slot in at `Program.cs` — MassTransit and ASP.NET Core
both instrument themselves already.

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
