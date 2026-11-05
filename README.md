# PDF Builder

Turns a registered HTML template plus a JSON payload into a PDF, and hands back a URL to download
it. Work is queued rather than done on the request thread, because rendering costs seconds and a
browser's worth of memory.

```
POST /api/pdf/jobs   X-Api-Key: pdf_k7f2a9c1_…   { "templateKey": "invoice", "data": { … } }
  -> 202 Accepted    { "jobId": "0865…-08df249c54be", "statusUrl": "/api/pdf/jobs/…" }

GET  /api/pdf/jobs/{jobId}
  -> 200             { "status": "Succeeded", "fileUrl": "http://…/files/a7e0495b…" }

GET  /files/{fileId}
  -> 200 application/pdf
```

> **Use the `fileUrl` from step 2 as-is rather than building it yourself.** A `jobId` is a dashed
> GUID and a `fileId` is 32 hex characters with no dashes; putting the first into `/files/` is the
> most common way to get a confusing 404.

Every `/api` call carries an API key in `X-Api-Key`, issued by the operator to each product (a
**client**) with only the permissions it needs; `/health/*` and the file download need none. See
[API keys](#api-keys) below.

Services that speak the message bus skip the polling: publish `IGeneratePdfRequest` and receive
`IGeneratePdfResponse`. See [docs/message-contracts.md](docs/message-contracts.md).

Templates are either built in (`templates/` in this repository) or written in the web app, where
they start as drafts and are only used by jobs once published. See
[docs/templates.md](docs/templates.md#two-kinds-of-template).

Templates and jobs belong to the client that created them (the product whose API key made the call).
A client lists, reads, uses and changes only its own: another client's template key or job id is a
`404`, exactly as one that does not exist, and two clients can each have a template with the same key.
The built-in templates are everybody's. See [docs/templates.md](docs/templates.md#two-kinds-of-template)
and the two-client walk-through in [samples/requests.http](samples/requests.http).

## Running it

### With Docker

The one command that gets Chromium, its fonts, RabbitMQ and PostgreSQL all at once:

```bash
docker compose up --build
# API docs:    http://localhost:8080/scalar/v1
# RabbitMQ UI: http://localhost:15672   (guest / guest)
# PostgreSQL:  localhost:5432           (pdf_builder / pdf_builder, this local stack only)
# Admin key:   written beside its hash in compose.yaml (published, this local stack only)
```

### Without Docker

Install Chromium once, point the service at a broker and a database, and run it:

```bash
dotnet build
pwsh src/PdfBuilder.Api/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet run --project src/PdfBuilder.Api        # http://localhost:5052
```

A hosted broker is the quickest way to get RabbitMQ without installing one. Set
`RabbitMq:ConnectionString` and nothing else changes — see
[configuration.md](docs/configuration.md#using-a-hosted-broker-cloudamqp) for the free-tier walkthrough.

```bash
dotnet user-secrets --project src/PdfBuilder.Api \
  set "RabbitMq:ConnectionString" "amqps://user:pass@host.rmq.cloudamqp.com/vhost"
```

Jobs and the templates written in the web app are stored in PostgreSQL, and the service will not
start without a connection string: there is no in-memory fallback. A free hosted database (Neon, for
example) or a local install both work. The string goes in user-secrets, like the broker's:

```bash
dotnet user-secrets --project src/PdfBuilder.Api \
  set "ConnectionStrings:Database" "Host=localhost;Database=pdf_builder;Username=…;Password=…"
```

The schema is created on first start. If you are upgrading from a version that kept templates as
files, they are imported once, automatically; see
[operations.md](docs/operations.md#upgrading-from-file-based-templates).

Upgrading from a version without clients? Templates and jobs that exist already have no owner, so set
`Blocks:Bus:DefaultClientId` to the id of the client that uses them before you start: the service gives
them to it once, registering it if needed, and refuses to start without it. A new install needs nothing.
See [operations.md](docs/operations.md#upgrading-to-per-client-templates-and-jobs).

The service also will not start without the hash of an admin key. Generate one; the key is printed
once, so keep it:

```bash
dotnet run --project tools/Blocks.Cli -- admin-key
dotnet user-secrets --project src/PdfBuilder.Api set "Blocks:Admin:KeyHash" "<printed hash>"
```

> **Running in Development loads your user-secrets**, which may point at a real broker, and a local
> process started that way joins the real queue as a consumer. When you only want to look at the
> service, run it with `ASPNETCORE_ENVIRONMENT=Production`: user-secrets are not loaded.

Check `/health/ready` returns 200 before sending work. `/health/live` answering while `/health/ready`
returns 503 means a dependency (the broker or the database) is unreachable.

## API keys

With the admin key, register a client for the product and issue it a key. The key is in the
response and nowhere else, ever: copy it into the product's configuration.

```bash
curl -X POST localhost:5052/api/admin/clients -H "X-Api-Key: $ADMIN" \
  -H 'Content-Type: application/json' -d '{"id":"ecommerce","name":"E-commerce"}'
curl -X POST localhost:5052/api/admin/clients/ecommerce/keys -H "X-Api-Key: $ADMIN" \
  -H 'Content-Type: application/json' -d '{"name":"production","scopes":["pdf:generate"]}'
```

Permissions are `pdf:generate` (jobs), `pdf:templates:read` and `pdf:templates:write`. Keys can expire,
be revoked (within a minute on every instance) or be refused together by disabling their client.
Every call is in [samples/admin.http](samples/admin.http); the rules are in
[operations.md](docs/operations.md#api-keys).

The web console signs in with the admin key (it never keeps it) and can do the same from its
**API keys** and **Clients** pages: create a key (shown once), see when each was last used, revoke
it, add and disable clients. It always shows which client it is working on. Serve it over HTTPS
from the same site as the API; see [operations.md](docs/operations.md#console-sign-in).

## Trying it

The helper script does all three calls — submit, poll, download — and opens the result:

```powershell
$env:PDFBUILDER_API_KEY = 'pdf_…'          # a key with pdf:generate
./samples/Invoke-PdfJob.ps1 -PayloadPath templates/invoice.sample.json -Open
```

```
POST http://localhost:5052/api/pdf/jobs  (template: invoice)
  jobId: 08650000-b825-67f8-3dd8-08df249c54be
  rendered 64,210 bytes in 4,402 ms
  saved to C:\Users\…\Temp\invoice.sample.pdf
```

The first render takes four to five seconds because Chromium has to launch; every one after that
reuses the browser and takes a few hundred milliseconds.

Other ways in:

- **The web app** in [`web/`](web/README.md) — `cd web && npm install && npm start`, then open
  http://localhost:4200. Submit, watch the job, preview and download the PDF.
- **`samples/requests.http`** — open in VS Code with the REST Client extension and click *Send
  Request*. Covers the happy path, the optional-field cases and every failure mode.
- **Scalar** at `/scalar/v1` — a request builder against the live API, in non-production only.
- **`samples/`** — payloads for a minimal invoice, an empty item list, number precision, and a
  28-line invoice that spills onto a second page. See [samples/README.md](samples/README.md).

## Tests

```bash
dotnet test                                      # fast: no browser, no broker, no database
PDFBUILDER_RUN_BROWSER_TESTS=1 dotnet test       # also launches real Chromium
PDFBUILDER_RUN_DATABASE_TESTS=1 dotnet test      # also starts PostgreSQL in Docker
```

The browser-backed tests are opt-in so the default loop stays quick, and they are the ones that
prove a real PDF comes out, that the browser recycles correctly mid-flight, and that hosts outside
the allow-list are actually blocked. The database-backed ones prove the stores, the migration, the
one-time template import and the job outbox against a real PostgreSQL. CI runs both.

Without Docker, point the database tests at a server you already have; every test gets its own
database on it and drops it afterwards:

```bash
PDFBUILDER_RUN_DATABASE_TESTS=1 PDFBUILDER_TEST_POSTGRES="Host=localhost;Username=postgres;Password=…" dotnet test
```

## Layout

| Path | What lives there |
| --- | --- |
| `src/PdfBuilder.Contracts` | The published wire vocabulary: message interfaces and error codes. Depends on nothing. |
| `src/PdfBuilder.Domain` | The model and the failure hierarchy. No I/O. |
| `src/PdfBuilder.Application` | The use case, and the ports it needs (`Abstractions/`). |
| `src/PdfBuilder.Infrastructure` | One adapter folder per port: `Templates/`, `Rendering/`, `Storage/`, `Jobs/`. |
| `src/PdfBuilder.Api` | Composition root: HTTP endpoints, the RabbitMQ consumer, `Program.cs`. |
| `web/` | Angular front end. Its own npm workspace; see [web/README.md](web/README.md). |
| `templates/` | Template content, kept out of the code projects. Mounted as a volume under Docker. |
| `samples/` | Payloads, a `.http` file and a PowerShell helper for trying the service by hand. |
| `tests/` | One project per layer that has logic worth pinning down. |

Dependencies point inwards only: `Api -> Infrastructure -> Application -> Domain -> Contracts`.

## Documentation

- [Architecture](docs/architecture.md) — how a request flows, why the layers are shaped this way,
  and what to change when you outgrow a single instance.
- [Message contracts](docs/message-contracts.md) — publishing requests and consuming responses.
- [Writing templates](docs/templates.md) — Scriban syntax, print CSS, and the two rules that trip
  everyone up.
- [Configuration](docs/configuration.md) — every setting, and the hosted-broker walkthrough.
- [Operations](docs/operations.md) — deployment, health checks, sizing, failure modes, known gaps.
- [Samples](samples/README.md) — the payloads and how to run them.
- [Web app](web/README.md) — the Angular front end: running it, its folder layering, adding a feature.
- [Deploying](deploy/README.md) — the Oracle Cloud Always Free setup CI deploys to.

## CI/CD

`.github/workflows/ci.yml` runs on every push and pull request: restore, build (warnings are
errors, so this is the lint gate too), then the full test suite with Chromium installed. A
parallel job lints, tests and builds `web/`.

On `master` it goes further — builds an **arm64** image on a native ARM runner, pushes it to
`ghcr.io/ahmedrooshdii/pdf-builder`, then deploys to the VM over SSH and polls `/health/ready`
until it answers, so a deploy that comes back broken fails the run instead of going green.

arm64 because Oracle's Always Free tier is Ampere. Building it natively rather than under QEMU
turns the Chromium and apt layers from the better part of an hour into minutes.

[deploy/README.md](deploy/README.md) has the one-time VM setup and the four repository secrets.

## Build conventions

- Warnings are errors (`Directory.Build.props`). The build is the linter.
- Package versions are centralised in `Directory.Packages.props`; `.csproj` files reference by name.
- `nuget.config` pins a single source so restores are reproducible.
- `global.json` pins the SDK band and selects Microsoft.Testing.Platform, which .NET 10 requires
  now that VSTest is gone.

> **MassTransit is held at 8.5.x deliberately.** Version 9 is commercially licensed and refuses to
> start without `MT_LICENSE`. 8.5.11 is the last Apache-2.0 release and is API-compatible with this
> code. Bump it only alongside a licence.

## Known limits

Job status and authored templates are shared by every instance, but generated PDFs are still stored
on local disk, so a download that lands on an instance that did not render the file gets a 404.
Implementing `IFileStorage` over object storage is a two-method job; do it before adding a replica.
Stored PDFs are also never deleted. [Operations](docs/operations.md#known-gaps) has the detail.
