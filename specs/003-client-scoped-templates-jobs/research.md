# Research: Client-Scoped Templates and Jobs

Decisions for [plan.md](plan.md). Each one resolves a question the spec leaves to planning, or a
conflict between the spec, `docs/blocks/04-backend.md#client-scoping` and the code as it is after
features 001, 002 and 004.

## R1. Where the client filter lives

- **Decision**: in `PdfBuilderDbContext`. The context takes `ICurrentClient` and declares a named
  query filter (`"client"`) on `AuthoredTemplateRecord` and `PdfJobRecord`:
  `record.ClientId == CallerClientId`, where `CallerClientId` is a context property that reads
  `ICurrentClient.ClientId` **each time a query runs**. That makes EF parameterise it per query, not
  bake it into the cached model. Reads, `ExecuteUpdate`, `ExecuteDelete` and `FindAsync` all go
  through the filter.
- **Rationale**: one place enforces FR-004, FR-005 and FR-009, for every query written now or
  later. This is what `04-backend.md` prescribes. `PdfBuilder.Application` gains no dependency:
  `TemplateCatalog`, `PublishedTemplateRepository` and the command handlers keep their code and see
  only the caller's rows. "Own template, else built-in" (FR-007, FR-008) and key uniqueness (FR-006)
  then follow from the code that already exists.
- **Fail closed**: with no client (`ClientId` null), the filter compares a non-nullable column with
  `NULL` and matches nothing. Code that runs outside a request, or an operator acting as no client,
  sees no rows, never all of them.
- **Alternatives considered**: a `clientId` parameter on every port method (`IAuthoredTemplateStore`,
  `IPdfJobStore`, `IPdfTemplateRepository`): each call site would have to remember it, and a missed
  one leaks data. A `PdfBuilder.Application` reference to `Blocks.ServiceDefaults`: that pulls ASP.NET
  Core into the Application layer for one interface, which Principle IX's dependency direction
  rejects, and the filter does not need it.

## R2. Cross-client queries

- **Decision**: `IgnoreQueryFilters(["client"])` appears only in methods whose names say so. This
  feature has three:
  - `JobRetentionSweeper.DeleteExpiredJobsOfAllClientsAsync`. The sweeper runs with no client, so
    without this it would silently delete nothing (R1, fail closed).
  - `ClientOwnerAssignment.AssignUnownedToDefaultClientAsync`, raw SQL during the upgrade (R6).
  - `EfPdfJobStore.ClaimAsync`, which reads past the filter only to tell an id taken by another
    client from a free one (R8). It returns nothing about the other job.
- **Rationale**: `04-backend.md` ("in a method whose name says so, and nowhere else"). An
  architecture test lists the allowed callers (R11).

## R3. Writing the owner

- **Decision**: the stores set `ClientId` on insert from a small `ClientScope` helper in
  Infrastructure, whose `RequireClientId()` returns `ICurrentClient.ClientId` or throws
  `ClientRequiredException`. `AuthoredTemplateRecord.From` and `PdfJobRecord.From` take the owner.
  `PdfJobRecord.CopyFrom` never copies it (FR-001: the owner never changes). Updates and deletes need
  nothing extra: the filter already confines them to the caller's rows.
- **Rationale**: a query filter does not apply to inserts. Setting the owner explicitly in the two
  stores keeps it visible in code review. A `SaveChanges` interceptor would do the same thing out of
  sight.
- **Alternatives considered**: a `SaveChanges` interceptor over an `IClientOwned` marker: more
  general, but nothing else needs it until webhooks (007), which can introduce it then.

## R4. Refusing "no client" and "unknown client" over HTTP

- **Decision**: a new endpoint filter in `Blocks.ServiceDefaults`, `.RequireActingClient()`, applied
  to the `/api/templates` and `/api/pdf/jobs` groups. For a caller with a key, `ClientId` always names
  an existing, enabled client (002 already refuses a disabled client's key), so the filter only does
  work for the operator:
  - no client → `400 VALIDATION_FAILED`, with `errors["X-Client-Id"] = ["Choose a client: send
    X-Client-Id with the client to act as."]` (FR-011)
  - unknown client → `404 CLIENT_NOT_FOUND`, the code the admin endpoints already use (FR-012)
  - disabled client → allowed (FR-012, spec User Story 5 scenario 4)
- The existence check is one `IClientKeyStore.GetClientAsync` call per operator request. That is
  console traffic from one person, so it is not cached.
- **Rationale**: every block needs this check. It sits beside `ActingClientMiddleware`, which 004
  built for the malformed-header case and which left existence to 003. Endpoints that are not
  client-scoped (admin, console session, files, health) do not get the filter.
- **Alternatives considered**: middleware for every request: it would refuse the operator's own
  `/api/admin/*` calls. Throwing from the stores (`ClientRequiredException` → 400): that only
  catches requests that reach a write, so `GET /api/templates` would quietly return just the
  built-in templates instead of saying a client is missing. The store exception stays, as a second
  line for code paths without the filter.

## R5. The client of a bus message

- **Decision**:
  1. `IGeneratePdfRequest` gains `string? ClientId { get; }`. `GeneratePdfRequest` gains a settable
     `ClientId`. Contracts go to **1.2.0** (R10).
  2. The HTTP endpoint publishes the caller's `ClientId` in the message, so the worker handles an API
     job for the same client that submitted it.
  3. `Blocks.ServiceDefaults` gains `IClientContext` with one method, `ActAs(string clientId)`. It is
     implemented by `CurrentClient`, a scoped type that is now the registered `ICurrentClient`: once
     `ActAs` has been called it answers with that client (no scopes, not admin), and otherwise it
     defers to `HttpCurrentClient`. The consumer calls `ActAs` before it touches a store. Because
     `PdfBuilderDbContext` reads `ClientId` at query time (R1), stores the consumer received in its
     constructor still see the client.
  4. The consumer resolves the client first, in this order:

     | Message `ClientId` | Default client | Outcome |
     | --- | --- | --- |
     | set, malformed | – | response `VALIDATION_FAILED` "ClientId is not a valid client id." |
     | set, unregistered | – | response `VALIDATION_FAILED` "Client '<id>' is not registered." |
     | set, disabled | – | response `CLIENT_DISABLED` |
     | null | unset | response `VALIDATION_FAILED` "The message names no client and no default client is configured." |
     | null | set | as if the message named the default client |

     These responses are sent without recording a job, because no client could own it. Nothing is
     rendered.
  5. `GeneratePdfConsumer`'s log scope gains `ClientId` (FR-017).
- **Rationale**: Principle VI ("bus consumers set `ICurrentClient` from the message before any other
  work"); `04-backend.md#client-scoping`. A failure response with a code, rather than a fault, is how
  the consumer already reports permanent errors, and a requester waiting on a response gets one.
- **Client lookup cost**: one read of `clients` per message, through `IClientKeyStore`. A render
  takes seconds, so the read does not matter. `StorageUnavailableException` stays transient, so the
  message is retried, not failed.
- **Alternatives considered**: a MassTransit consume filter that sets the client: the client is in
  the message body, which is PDF Builder's contract, so the stud cannot read it generically. A
  message header instead of a body member: invisible in the typed contract, and easy for a publisher
  to forget.

## R6. Giving existing data an owner (FR-016, amended)

- **Decision**: two migrations, with a startup step between them.
  1. `AddClientOwner`: `client_id varchar(40) NULL` on `authored_templates` and `pdf_jobs`.
  2. **Assignment** (`ClientOwnerAssignment`, under a PostgreSQL advisory lock): if any row has
     `client_id IS NULL`, resolve the owner (R7), then
     `UPDATE … SET client_id = @owner WHERE client_id IS NULL` on both tables, in one transaction.
     With no unowned rows it does nothing and needs no setting.
  3. `RequireClientOwner`: `client_id` becomes `NOT NULL`, with a foreign key to `clients(id)`; the
     primary key of `authored_templates` becomes `(client_id, key)`; the unique index becomes
     `(client_id, key_lower)`.
- `DatabaseStartup` runs `MigrateAsync("AddClientOwner")`, then the importer, then the assignment,
  then `MigrateAsync()`, **only when `RequireClientOwner` is pending**. Otherwise it runs the plain
  `MigrateAsync()` as today. Migrating to a named target on a database that is already past it would
  roll migrations back.
- **The 001 file importer** runs between the two migrations, as it does today, and writes the rows it
  imports with the owner from R7, which it asks for only when it has templates to import. It never
  writes `NULL` or an empty string.
- **Several instances**: EF's migration lock serialises each `MigrateAsync`; the importer and the
  assignment each hold their own advisory lock. An instance that arrives late finds no unowned rows
  and no import to do, and `RequireClientOwner` is either applied or applies cleanly, because every
  instance assigns before it migrates further.
- **`Database:MigrateOnStartup = false`**: the existing behaviour (refuse to start with pending
  migrations) is kept. `docs/operations.md` gives the statement an operator applying migrations by
  hand must run between the two migrations.
- **Rationale**: the column can only become `NOT NULL` (and part of the key) once every row has an
  owner, and only the running service knows the owner, because it comes from configuration. One
  release gets both the guarantee (no unowned row can ever be written again) and an upgrade that
  needs no hand-run SQL.
- **Alternatives considered**: one migration with a nullable column kept forever: the database would
  no longer guarantee an owner, and a bug writing `NULL` would make data invisible instead of
  failing. A migration that reads configuration: EF migrations are SQL, and baking a deployment
  value into one breaks Principle I. A sentinel default (`''`) then fixed up: a valid-looking value
  that means "unknown".

## R7. Which client owns existing data

- **Decision**: `Blocks:Bus:DefaultClientId`, the setting `04-backend.md` already names for this.
  Bound to `BlocksBusOptions.DefaultClientId` in `Blocks.ServiceDefaults`, validated at start
  (`ValidateOnStart`) against the client id rule. A malformed value always refuses to start: it is a
  typo, whatever the data. When an owner is needed (R6):
  - unset → refuse to start with `InvalidOperationException`: "Templates or jobs from before clients
    existed need an owner. Set Blocks:Bus:DefaultClientId (environment variable
    Blocks__Bus__DefaultClientId) to the id of the client that uses them, then start again. Nothing
    has been changed."
  - set, not registered → register it through `IClientKeyStore.AddClientAsync` with its id as its
    name (the operator renames it in the console), and log that it was registered
  - set, disabled → use it (its data is kept and hidden, as for any disabled client)
- **Rationale**: the spec's amended FR-016. Every released deployment (1.0.0) predates clients, and a
  client can only be registered through the running service, so "must already be registered" could
  never be met on a real upgrade.

## R8. A bus message reusing another client's job id

- **Decision**: before rendering, the consumer calls a new `IPdfJobStore.ClaimAsync(jobId,
  templateKey)`. It returns the caller's own job if one is visible. Otherwise it inserts a `Pending`
  row for the caller, and on a unique violation it throws `JobIdTakenException`
  (`PdfBuilderException`, permanent, `VALIDATION_FAILED`, "CorrelationId is already used by another
  job; send a new one."). The other client's job is not read, changed or described (FR-015).
  `SaveAsync`'s duplicate-key recovery, which assumed the winner was visible, now also throws
  `JobIdTakenException` when the winner belongs to someone else, rather than rethrowing a database
  error that the consumer would treat as transient and retry.
- **Side effect**: a job published straight to the bus is now recorded as `Pending` when work starts,
  not first at completion, so a product polling it over HTTP sees `Pending` instead of `404` while it
  renders. This is visible but additive: no status changes meaning.
- **Alternatives considered**: checking only at completion: the PDF would be rendered, then thrown
  away. Making `pdf_jobs`' key `(client_id, job_id)`: two jobs with one id would then make every
  log line, file and outbox message keyed by the job id ambiguous.

## R9. Templates: keys, shadowing, built-ins

- **Decision**: no Application change. `TemplateCatalog` already resolves "own authored, else
  built-in" and already refuses a key taken by a built-in template or by the caller's own template
  in any case. With the store filtered (R1) it does both per client. The unique index becomes
  `(client_id, key_lower)` (R6), so the database enforces FR-006 per client.
- A client's own template with a built-in key (only possible through the 001 import of older files,
  which skips such keys, or a built-in template added in a later release) shadows the built-in for
  that client alone (FR-007); `ListAsync` already does this.
- `PreviewTemplateCommand` reads no stored template, but its endpoint is in the `/api/templates`
  group and gets `.RequireActingClient()` like the rest (spec edge case "Preview").

## R10. Contracts 1.2.0

- **Decision**: `IGeneratePdfRequest.ClientId` (`string?`), `GeneratePdfRequest.ClientId { get; set; }`,
  `PdfErrorCodes.ClientDisabled = "CLIENT_DISABLED"` (added to `All`). A `CP0006` baseline
  suppression for the new interface member, as 1.1.0 did for `PageSetup`. The version goes from
  `1.1.0-local` to `1.2.0-local`, and `CHANGELOG.md` gets a 1.2.0 section.
- **Rationale**: Principle VII. A 1.0.0 or 1.1.0 publisher does not send the member, and MassTransit
  deserialises it as `null`, which means "the default client". `04-backend.md` placed this change in
  1.2.0 so 1.1.0 (page setup) can be tagged on its own. `IGeneratePdfResponse.FileUrlExpiresAt`, which
  the same table lists, belongs to feature 005 and is not added here.
- **The trust limit**: any holder of broker credentials can name any client.
  `docs/message-contracts.md` says so, and that only trusted product backends get broker credentials
  (FR-018).

## R11. Tests and guard rails

- **Architecture test** (`tests/Blocks.ServiceDefaults.Tests/ArchitectureTests.cs`, which already
  scans assemblies): `IgnoreQueryFilters` is called only from the three methods in R2. Every entity
  type in `PdfBuilderDbContext` with a `ClientId` property has the `"client"` filter. The second
  check guards future tables (Principle III's gate).
- **Cross-client tests** (definition of done): for every template and job endpoint, client B's answer
  for A's key or id equals the answer for one that never existed, with `traceId` removed (SC-001).
  The list of endpoints is enumerated from `EndpointDataSource`, as 002's pipeline tests do, so a new
  endpoint cannot be missed.
- **Existing tests** that call template or job endpoints as the operator without `X-Client-Id` now
  get `400`. Their helpers act as a test client instead. This is a deliberate behaviour change
  (FR-011), not a regression.

## R12. Files

- **Decision**: no schema or endpoint change. A generated file is owned through its job
  (`pdf_jobs.file_id`, `client_id`), which is what FR-002 asks. `GET /files/{fileId}` stays anonymous
  and unsigned until feature 005.
- **Rationale**: `IFileStorage` keeps no metadata today, and 005 introduces the signed URL and the
  ownership check together. A `files` table now would be designed twice.
