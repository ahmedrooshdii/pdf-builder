# Configuration

Settings come from `appsettings.json`, then `appsettings.{Environment}.json`, then environment
variables. Nested keys use a double underscore: `Storage:PublicBaseUrl` is
`Storage__PublicBaseUrl`.

Every section is validated at startup. A bad value fails the deployment rather than surfacing later
as a confusing runtime error on the first request that happens to need it.

## RabbitMq

Two shapes, because brokers come addressed two ways.

**Discrete fields** — a local or in-cluster broker:

```json
"RabbitMq": { "Host": "localhost", "VirtualHost": "/", "Username": "guest", "Password": "guest" }
```

**A single URI** — what every hosted broker hands you. It wins when set, and the other four keys
are ignored:

```json
"RabbitMq": { "ConnectionString": "amqps://user:pass@host.rmq.cloudamqp.com/vhost" }
```

| Key | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | unset | Full broker URI. `amqps` turns on TLS and moves the port to 5671. Contains a password — keep it out of committed files. |
| `Host` | `localhost` | Host name. Ignored when `ConnectionString` is set. |
| `VirtualHost` | `/` | Ignored when `ConnectionString` is set. |
| `Username` / `Password` | `guest` / `guest` | Ignored when `ConnectionString` is set. |

A malformed URI, or one with a scheme other than `amqp`/`amqps`, fails at startup with a message
naming the setting. Without that check MassTransit reports a bare "address scheme is not supported"
from inside bus construction, which names neither the key nor the file it came from.

The broker being unreachable is *not* a startup failure: the host comes up, serves `/health/live`,
reports `/health/ready` as unhealthy, and retries the connection in the background.

### Using a hosted broker (CloudAMQP)

Useful when you do not want RabbitMQ — or Docker — on your machine.

1. Sign up at [cloudamqp.com](https://www.cloudamqp.com) and create an instance on the free
   **Little Lemur** plan.
2. Open the instance and copy the **AMQP URL**. It looks like
   `amqps://xyzabcd:LongPassword@woodpecker.rmq.cloudamqp.com/xyzabcd` — on this plan the username
   and the virtual host are the same string, and the vhost is the last path segment.
3. Store it in user secrets so it never reaches the repository:

   ```bash
   dotnet user-secrets --project src/PdfBuilder.Api \
     set "RabbitMq:ConnectionString" "amqps://xyzabcd:LongPassword@woodpecker.rmq.cloudamqp.com/xyzabcd"
   ```

   User secrets are only read in the Development environment. Elsewhere, pass
   `RabbitMq__ConnectionString` as an environment variable or a mounted secret.
4. `dotnet run --project src/PdfBuilder.Api`, then check `/health/ready` returns 200.

Little Lemur caps you at 3 connections, 100 queues and a modest message rate. MassTransit opens one
connection, and this service declares a handful of queues, so the limits are not the constraint —
throughput is. It is a development broker, not a production one.

## Playwright

```json
"Playwright": {
  "MaxConcurrentRenders": 4,
  "MaxRendersPerBrowser": 500,
  "RenderTimeoutMs": 30000,
  "EnableJavaScript": false,
  "AllowedHosts": [],
  "LaunchArgs": [ "--disable-dev-shm-usage", "--disable-gpu" ]
}
```

| Key | Default | Notes |
| --- | --- | --- |
| `MaxConcurrentRenders` | `4` | 1–64. The main sizing lever: each in-flight render costs roughly a browser tab. It also caps how many messages the consumer pulls. |
| `MaxRendersPerBrowser` | `500` | 1–100,000. Chromium is recycled after this many renders to bound memory growth. |
| `RenderTimeoutMs` | `30000` | 1,000–300,000. Per-page budget. A template waiting on something that never arrives fails here. |
| `EnableJavaScript` | `false` | See the warning below. |
| `AllowedHosts` | `[]` | Hosts a template may load assets from. See the warning below. |
| `LaunchArgs` | see above | `--disable-dev-shm-usage` matters in containers: Docker gives `/dev/shm` 64 MB by default, and Chromium crashes on large pages without it. |

> **`EnableJavaScript`** — payload data is untrusted and Scriban does not auto-escape HTML. Turning
> this on means a crafted payload can run script inside the rendering browser. Only enable it if
> payloads come from a trusted source *and* templates escape every interpolation.

> **`AllowedHosts`** — this is the SSRF boundary. An empty list means inline assets only, which is
> why it is the default. Every host you add is a host a template author can make the server talk to
> from inside your network. Add specific hosts, never a wildcard.

## Storage

```json
"Storage": { "RootPath": "data/files", "PublicBaseUrl": "http://localhost:8080" }
```

| Key | Default | Notes |
| --- | --- | --- |
| `RootPath` | `data/files` | Where PDFs are written. Relative paths resolve against the app directory. In the container this is `/data/files`, a volume. |
| `PublicBaseUrl` | `http://localhost:8080` | **Must be the address callers can reach**, not the container's. This is what `FileUrl` is built from; get it wrong and every response hands out a dead link. A trailing slash is trimmed for you. |

## TemplateStore

```json
"TemplateStore": { "RootPath": "Templates", "AuthoredRootPath": "data/templates" }
```

| Key | Default | Notes |
| --- | --- | --- |
| `RootPath` | `Templates` | The built-in templates: `{templateKey}.html`, with optional `.options.json` and `.sample.json` siblings. `/app/Templates` in the container, where `compose.yaml` mounts the repository's `templates/` folder read-only and CI replaces it on every deploy. |
| `AuthoredRootPath` | `data/templates` | Templates written in the web app, one `{templateKey}.json` each. Must be writable and must survive deploys: `/data/templates` in the container, on the data volume. Never point it inside `RootPath`, which CI wipes. |

## TemplateEngine

```json
"TemplateEngine": { "StrictMode": false }
```

| Key | Default | Notes |
| --- | --- | --- |
| `StrictMode` | `false` | Off: a field the payload does not contain renders as nothing. On: it fails the job, naming the field and its position. Worth enabling in staging and template tests. See [templates.md](templates.md#missing-fields). |

## JobStore

```json
"JobStore": { "RetentionHours": 24 }
```

| Key | Default | Notes |
| --- | --- | --- |
| `RetentionHours` | `24` | 1–168. How long a finished job stays pollable. State is in process memory, so it is also lost on restart. |

## Logging

Standard ASP.NET Core configuration. The consumer attaches `CorrelationId` and `TemplateKey` to a log
scope, so a structured sink can show every line for one job together.

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "MassTransit": "Warning",
    "PdfBuilder": "Debug"
  }
}
```

## Environment variables you will actually set

```bash
RabbitMq__Host=rabbitmq
RabbitMq__Username=pdf-builder
RabbitMq__Password=…
Storage__PublicBaseUrl=https://pdf.example.com
Storage__RootPath=/data/files
TemplateStore__RootPath=/app/Templates
TemplateStore__AuthoredRootPath=/data/templates
Playwright__MaxConcurrentRenders=4
ASPNETCORE_ENVIRONMENT=Production
```

API documentation (`/openapi/v1.json` and `/scalar/v1`) is served in non-production environments
only. Put it behind authentication before enabling it in production.
