# Quickstart: Validating Persistent Storage

How to prove the feature works end to end. Each scenario maps to a user story in [spec.md](spec.md).

## Prerequisites

- A PostgreSQL 17 database for the block. On this machine (no Docker): a free hosted database
  (Neon) or a local PostgreSQL install. Where Docker exists: `docker compose up postgres rabbitmq`.
- The broker, as today (`RabbitMq:ConnectionString` or the compose `rabbitmq` service).
- Set the connection string once:

  ```bash
  dotnet user-secrets --project src/PdfBuilder.Api \
    set "ConnectionStrings:Database" "Host=…;Database=pdf_builder;Username=…;Password=…;SSL Mode=Require"
  ```

## Automated checks

```bash
dotnet test                                            # no database, no browser: must pass as before
PDFBUILDER_RUN_DATABASE_TESTS=1 dotnet test            # adds the PostgreSQL-backed tests (needs Docker)
```

CI runs the second form with `PDFBUILDER_RUN_BROWSER_TESTS=1` as well. Expected: every pre-existing
test passes unchanged (SC-004), plus the new store, import and retention tests.

## Scenario 1: job status survives a restart (Story 1)

1. `dotnet run --project src/PdfBuilder.Api`
2. `./samples/Invoke-PdfJob.ps1 -PayloadPath templates/invoice.sample.json` and note the `jobId`.
3. Stop the service (Ctrl+C) and start it again.
4. `GET /api/pdf/jobs/{jobId}` → **200**, same `status`, `fileId`, `fileUrl`, `sizeBytes`,
   `completedAt` as before the restart.

## Scenario 2: templates survive a restart (Story 2)

1. In the console (`cd web && npm start`), create a template, publish it, then edit the draft
   without publishing.
2. Restart the API.
3. The template shows **Modified**; the draft has the edit; a job with that key renders the
   published version.
4. Save a draft from two browser tabs based on the same revision: the second is refused with
   `TEMPLATE_REVISION_CONFLICT`, as before.

## Scenario 3: existing templates are imported once (Story 3)

1. Before upgrading, note the files in `TemplateStore:AuthoredRootPath` (`data/templates` by
   default). Add one deliberately broken file, `broken.json`, containing `{`.
2. Start the new version against an **empty** database.
3. Logs show *Template import completed* with the imported count and a *skipped* warning for
   `broken.json` (`malformed`). `GET /api/templates` lists every other template with its status.
4. The files in the directory are unchanged.
5. Delete one imported template through the console, restart: it stays deleted (no re-import).

## Scenario 4: two instances (Story 4)

1. Start two instances against the same database and broker on different ports:
   `dotnet run --project src/PdfBuilder.Api --urls http://localhost:5052` and `--urls http://localhost:5053`.
2. Submit a job to 5052; poll it on 5053 → current status.
3. Publish a template change through 5053; render a job through 5052 → the new version is used.
4. Against a fresh database with old template files present, start both at once: the import log
   line appears on exactly one of them.

## Scenario 5: database unavailable (edge case)

1. With the service running, stop or block the database.
2. `/health/live` → 200; `/health/ready` → 503.
3. `POST /api/pdf/jobs` → **503** problem details, `code: STORAGE_UNAVAILABLE`, `Retry-After: 5`,
   and no message is published to the broker.
4. Restore the database: readiness returns to 200 without restarting the service.

## Scenario 6: retention unchanged (FR-004)

With `JobStore:RetentionHours=1`, a job finished more than an hour ago returns **404**, exactly as it
did with the in-memory store, and its row is gone after the next sweep.
