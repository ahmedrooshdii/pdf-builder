# Implementation Plan: Clients and API Keys

**Branch**: `002-clients-and-api-keys` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/002-clients-and-api-keys/spec.md`

## Summary

Every API call must carry a key that identifies a client (a product). The operator, holding a single
admin key from configuration, registers clients and issues named, scoped, optionally expiring keys,
shown once and stored only as a SHA-256 hash; revocation and client disabling take effect on every
instance within a minute. This is the first stud, so it is built as the first two shared packages,
`Blocks.ServiceDefaults` (authentication handler, fallback authorization policy, permission policies,
management endpoints, `ICurrentClient`) and `Blocks.ServiceDefaults.EntityFramework` (the `clients` and
`api_keys` tables and their store), which PDF Builder references. No setting turns authentication
off; this ships together with console sign-in (004). Details and alternatives:
[research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 10 (`global.json`).

**Primary Dependencies**: ASP.NET Core authentication and authorization (in the shared framework),
EF Core 10.0.12 with Npgsql 10.0.3 (from 001), `Microsoft.Extensions.Caching.Memory` (already pinned).
Added for tests only: `Microsoft.AspNetCore.Mvc.Testing` 10.0.12.

**Storage**: PostgreSQL, the block's own database from 001; two new tables in a new migration.

**Testing**: xUnit v3 + NSubstitute; database-backed and whole-pipeline tests opt-in with
`PDFBUILDER_RUN_DATABASE_TESTS=1` (Testcontainers or `PDFBUILDER_TEST_POSTGRES`), on in CI.

**Target Platform**: Linux containers, `linux/amd64` and `linux/arm64`.

**Project Type**: Web service with a RabbitMQ consumer (unchanged), plus a small console tool.

**Performance Goals**: a key check adds < 25 ms to 95% of requests (SC-007); in practice a memory
lookup and one SHA-256, with a database read at most once a minute per key per instance.

**Constraints**: no change to accepted responses (FR-022) or to message contracts; keys never
recoverable or logged; refusals identical across unknown/revoked/expired; outage answers 503.

**Scale/Scope**: tens of clients and keys per deployment; request rates of the existing service.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Pre-research | Post-design |
| --- | --- | --- | --- |
| I | One codebase, no forks | ✅ | ✅ One new setting, `Blocks:Admin:KeyHash`, required and documented ([contracts/configuration.md](contracts/configuration.md)); added to `docs/configuration.md` and `docs/blocks/04-backend.md#configuration`. No customer path. |
| II | Block stands alone | ✅ | ✅ Keys live in the block's own database; nothing calls another service. |
| III | Generic, no product knowledge | ⚠ | ⚠ "Client" is generic. The data tables from 001 still have no `client_id`: carried from 001, see Complexity Tracking. |
| IV | Blocks never call each other | ✅ | ✅ |
| V | Same studs | ✅ | ✅ This feature **is** the auth stud, built in `Blocks.*` packages; refusals use the stud codes from `docs/blocks/04-backend.md`; health stays open. |
| VI | Identity from claims only | ✅ | ✅ `ICurrentClient` reads claims; no Application code inspects headers or schemes. |
| VII | Contracts change additively | ⚠ | ⚠ Message contracts unchanged. HTTP: requiring a key on existing endpoints is a breaking change for keyless callers, shipped in place rather than on a new path. Justified below. |
| VIII | Secrets unrecoverable, shown once | ✅ | ✅ SHA-256 hashes, constant-time comparison, key in one response with `Cache-Control: no-store`, never logged; admin key held as a hash. |
| IX | Layering by tooling | ✅ | ✅ New boundary (`Blocks.*` must not reference `PdfBuilder.*`) enforced by an architecture test. |
| X | Any hardware, offline | ✅ | ✅ No external call; no new image dependency. |

**Result**: passes with two justified deviations.

## Project Structure

### Documentation (this feature)

```text
specs/002-clients-and-api-keys/
├── spec.md
├── plan.md                      # this file
├── research.md                  # R1–R13
├── data-model.md                # clients, api_keys, permissions, key format
├── quickstart.md
├── contracts/
│   ├── authentication.md        # header, endpoint permissions, refusals, guarantees
│   ├── admin-api.md             # /api/admin clients and keys
│   └── configuration.md         # Blocks:Admin:KeyHash, generating it, logs
├── checklists/requirements.md
└── tasks.md                     # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Blocks.ServiceDefaults/                         NEW  the auth stud; references no PdfBuilder.* assembly
│   ├── Blocks.ServiceDefaults.csproj               FrameworkReference Microsoft.AspNetCore.App
│   ├── BlocksAuthOptions.cs                        block code, permission list, admin key hash (validated)
│   ├── BlocksAuthExtensions.cs                     AddBlocksAuthentication(...), RequirePermission(...), MapBlocksAdmin()
│   ├── Authentication/
│   │   ├── ApiKeyAuthenticationHandler.cs          X-Api-Key → claims; classifies failures (R4)
│   │   ├── ApiKeyFormat.cs                         parse/generate pdf_{id}_{secret}, adm_{secret}
│   │   ├── KeyHasher.cs                            SHA-256, constant-time compare
│   │   ├── KeyCache.cs                             60 s positive and negative cache (R6)
│   │   └── KeyUsageRecorder.cs                     channel + background flush of last_used_at (R7)
│   ├── Clients/
│   │   ├── IClientKeyStore.cs                      port the EF package implements
│   │   ├── ClientAdminEndpoints.cs                 /api/admin/clients… (R8)
│   │   └── ClientAdminContracts.cs                 request/response records, validation
│   ├── Identity/
│   │   ├── ICurrentClient.cs, HttpCurrentClient.cs
│   │   └── ClientLogScopeMiddleware.cs             ClientId/KeyId log scope (R10)
│   └── Problems/BlocksProblems.cs                  stud problem details and codes
├── Blocks.ServiceDefaults.EntityFramework/         NEW
│   ├── ClientRecord.cs, ApiKeyRecord.cs
│   ├── ModelBuilderExtensions.cs                   modelBuilder.AddBlocksClients()
│   └── EfClientKeyStore.cs                         EfClientKeyStore<TContext> : IClientKeyStore
├── PdfBuilder.Infrastructure/
│   ├── Persistence/PdfBuilderDbContext.cs          + modelBuilder.AddBlocksClients()
│   └── Persistence/Migrations/                     + AddClientsAndApiKeys
└── PdfBuilder.Api/
    ├── Program.cs                                  AddBlocksAuthentication("pdf", permissions); UseAuthentication/UseAuthorization;
    │                                               log-scope middleware; AllowAnonymous on health, files, docs; MapBlocksAdmin()
    ├── Endpoints/PdfJobEndpoints.cs                + RequirePermission("pdf:generate")
    ├── Endpoints/TemplateEndpoints.cs              + read / write permissions per endpoint
    └── OpenApi/ApiKeySecurityTransformer.cs        NEW  declares the X-Api-Key scheme (R12)

tools/
└── Blocks.Cli/                                     NEW  `admin-key`: prints a key once and its hash (R9)

tests/
├── Blocks.ServiceDefaults.Tests/                   NEW  key format, hashing, handler classification, validation,
│                                                        architecture rule; no database
├── PdfBuilder.Infrastructure.Tests/
│   └── EfClientKeyStoreTests.cs                    store against PostgreSQL
└── PdfBuilder.Api.Tests/
    ├── ApiFactory.cs                               WebApplicationFactory<Program> + test harness + test database
    ├── AuthenticationPipelineTests.cs              every protected endpoint refuses; open ones stay open; 503 on outage
    ├── PermissionTests.cs                          scope per endpoint
    └── AdminApiTests.cs                            clients and keys end to end, one-time display, revocation

compose.yaml, deploy/compose.prod.yaml, deploy/huggingface/   admin key hash (local-only key / .env / secret)
samples/admin.http, samples/requests.http, samples/Invoke-PdfJob.ps1   the key
PdfBuilder.slnx, Directory.Packages.props
```

**Structure Decision**: the stud is two new projects under `src/`, beside the PDF projects, as
`docs/blocks/03-planning.md#repository-structure` lays out for phase 1. `PdfBuilder.Infrastructure`
references `Blocks.ServiceDefaults.EntityFramework` (for the model and the store);
`PdfBuilder.Api` references `Blocks.ServiceDefaults`. Domain and Application do not change.

### Documentation updated in this feature

| Document | Change |
| --- | --- |
| `README.md` | Calling the API with a key; generating the admin key; the console needs 004 |
| `docs/configuration.md` | `Blocks:Admin:KeyHash` |
| `docs/operations.md` | Issuing keys before upgrading; refusals in failure modes; rotating the admin key |
| `docs/architecture.md` | Authentication in the request flow; the shared packages |
| `docs/message-contracts.md` | Note that HTTP needs a key and the bus does not change |
| `docs/blocks/04-backend.md` | What was built: one scheme now, cookie scheme with 004; CLI scope |
| `deploy/README.md`, `deploy/huggingface/README.md` | `ADMIN_KEY_HASH` / secret |
| `web/README.md` | The console is refused until console sign-in (004) |
| `CHANGELOG.md` | Service entry: breaking for keyless callers; no contracts version change |

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
| --- | --- | --- |
| VII: requiring a key on the existing `/api/pdf/jobs` and `/api/templates` endpoints breaks keyless callers, in place, not on a new path | The point of the feature is that nothing is open (spec FR-014, FR-023). The clarification of 2026-11-03 found no production callers to break: the VM deploy is unconfigured and the Space is disabled. | An unauthenticated copy under the old paths, or a `/api/v2` with keys: leaves the service open, which is the problem being fixed. An "authentication off" setting: rejected in the clarification. |
| III: `pdf_jobs` and `authored_templates` still have no `client_id` (carried from 001) | Callers are identified now; separating their data is feature 003, which needs this feature's clients to exist first. | Adding `client_id` here: mixes two features' migrations and reviews, and 003's backfill rule (the default client) belongs with its spec. |
