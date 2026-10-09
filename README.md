# PDF Builder

Turns a registered HTML template plus a JSON payload into a PDF, and hands back a URL to download
it. Work is queued rather than done on the request thread, because rendering costs seconds and a
browser's worth of memory.

```
POST /api/pdf/jobs   { "templateId": "invoice", "data": { … } }
  -> 202 Accepted    { "jobId": "…", "statusUrl": "/api/pdf/jobs/…" }

GET  /api/pdf/jobs/{jobId}
  -> 200             { "status": "Succeeded", "fileUrl": "http://…/files/…" }

GET  /files/{fileId}
  -> 200 application/pdf
```

Services that speak the message bus skip the polling: publish `IGeneratePdfRequest` and receive
`IGeneratePdfResponse`. See [docs/message-contracts.md](docs/message-contracts.md).

## Running it

With Docker — the only way to get Chromium, its fonts and RabbitMQ in one step:

```bash
docker compose up --build
# API docs:    http://localhost:8080/scalar/v1
# RabbitMQ UI: http://localhost:15672   (guest / guest)
```

Locally you need a RabbitMQ instance and a Chromium install:

```bash
dotnet build
pwsh src/PdfBuilder.Api/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet run --project src/PdfBuilder.Api        # http://localhost:5052
```

Without Docker, a hosted broker is the quickest way to get one — point `RabbitMq:ConnectionString`
at it and nothing else changes. See
[configuration.md](docs/configuration.md#using-a-hosted-broker-cloudamqp).

Try the sample template with the sample payload:

```bash
curl -X POST http://localhost:8080/api/pdf/jobs \
  -H 'Content-Type: application/json' \
  -d "{\"templateId\":\"invoice\",\"data\":$(cat templates/invoice.sample.json)}"
```

## Tests

```bash
dotnet test                                      # fast: no browser, no broker
PDFBUILDER_RUN_BROWSER_TESTS=1 dotnet test       # also launches real Chromium
```

The browser tests are opt-in so the default loop stays quick. They are the ones that prove a real
PDF comes out the other end, so CI should run them.

## Layout

| Path | What lives there |
| --- | --- |
| `src/PdfBuilder.Contracts` | The published wire vocabulary: message interfaces and error codes. Depends on nothing. |
| `src/PdfBuilder.Domain` | The model and the failure hierarchy. No I/O. |
| `src/PdfBuilder.Application` | The use case, and the ports it needs (`Abstractions/`). |
| `src/PdfBuilder.Infrastructure` | One adapter folder per port: `Templates/`, `Rendering/`, `Storage/`, `Jobs/`. |
| `src/PdfBuilder.Api` | Composition root: HTTP endpoints, the RabbitMQ consumer, `Program.cs`. |
| `templates/` | Template content, kept out of the code projects. Mounted as a volume in Docker. |
| `tests/` | One test project per layer that has logic worth pinning down. |

Dependencies point inwards only: `Api -> Infrastructure -> Application -> Domain -> Contracts`.

## Documentation

- [Architecture](docs/architecture.md) — how a request flows, why the layers are shaped this way,
  and what to change when you outgrow a single instance.
- [Message contracts](docs/message-contracts.md) — publishing requests and consuming responses.
- [Writing templates](docs/templates.md) — Scriban syntax, print CSS, and the two rules that trip
  everyone up.
- [Configuration](docs/configuration.md) — every setting and what it does.
- [Operations](docs/operations.md) — deployment, health checks, sizing, failure modes, known gaps.

## Build conventions

- Warnings are errors (`Directory.Build.props`). The build is the linter.
- Package versions are centralised in `Directory.Packages.props`; `.csproj` files reference by name.
- `nuget.config` pins a single source so restores are reproducible.
- `global.json` pins the SDK band and selects the Microsoft.Testing.Platform test runner.
