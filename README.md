# PDF Builder

Turns a registered HTML template plus a JSON payload into a PDF, and hands back a URL to download
it. Work is queued rather than done on the request thread, because rendering costs seconds and a
browser's worth of memory.

```
POST /api/pdf/jobs   { "templateId": "invoice", "data": { … } }
  -> 202 Accepted    { "jobId": "0865…-08df249c54be", "statusUrl": "/api/pdf/jobs/…" }

GET  /api/pdf/jobs/{jobId}
  -> 200             { "status": "Succeeded", "fileUrl": "http://…/files/a7e0495b…" }

GET  /files/{fileId}
  -> 200 application/pdf
```

> **Use the `fileUrl` from step 2 as-is rather than building it yourself.** A `jobId` is a dashed
> GUID and a `fileId` is 32 hex characters with no dashes; putting the first into `/files/` is the
> most common way to get a confusing 404.

Services that speak the message bus skip the polling: publish `IGeneratePdfRequest` and receive
`IGeneratePdfResponse`. See [docs/message-contracts.md](docs/message-contracts.md).

## Running it

### With Docker

The one command that gets Chromium, its fonts and RabbitMQ all at once:

```bash
docker compose up --build
# API docs:    http://localhost:8080/scalar/v1
# RabbitMQ UI: http://localhost:15672   (guest / guest)
```

### Without Docker

Install Chromium once, point the service at a broker, and run it:

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

Check `/health/ready` returns 200 before sending work. `/health/live` answering while `/health/ready`
returns 503 means the broker is unreachable.

## Trying it

The helper script does all three calls — submit, poll, download — and opens the result:

```powershell
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

- **`samples/requests.http`** — open in VS Code with the REST Client extension and click *Send
  Request*. Covers the happy path, the optional-field cases and every failure mode.
- **Scalar** at `/scalar/v1` — a request builder against the live API, in non-production only.
- **`samples/`** — payloads for a minimal invoice, an empty item list, number precision, and a
  28-line invoice that spills onto a second page. See [samples/README.md](samples/README.md).

## Tests

```bash
dotnet test                                      # fast: no browser, no broker
PDFBUILDER_RUN_BROWSER_TESTS=1 dotnet test       # also launches real Chromium
```

93 tests. The browser-backed ones are opt-in so the default loop stays quick, and they are the ones
that prove a real PDF comes out, that the browser recycles correctly mid-flight, and that hosts
outside the allow-list are actually blocked — so CI should run the second form.

## Layout

| Path | What lives there |
| --- | --- |
| `src/PdfBuilder.Contracts` | The published wire vocabulary: message interfaces and error codes. Depends on nothing. |
| `src/PdfBuilder.Domain` | The model and the failure hierarchy. No I/O. |
| `src/PdfBuilder.Application` | The use case, and the ports it needs (`Abstractions/`). |
| `src/PdfBuilder.Infrastructure` | One adapter folder per port: `Templates/`, `Rendering/`, `Storage/`, `Jobs/`. |
| `src/PdfBuilder.Api` | Composition root: HTTP endpoints, the RabbitMQ consumer, `Program.cs`. |
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

Two things are single-instance today, and both bite at the same moment: PDFs are stored on local
disk, and job state is held in memory. Implementing `IFileStorage` over object storage and
`IPdfJobStore` over Redis are each a two-method job — do both before adding a replica. Stored PDFs
are also never deleted. [Operations](docs/operations.md#known-gaps) has the detail.
