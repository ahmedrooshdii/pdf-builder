# PDF Builder (Blocks) Constitution

PDF Builder is the first **block**: a self-contained backend capability that products plug in and
that companies can run on their own servers. These principles govern it and every block built after
it. Background and rationale live in `docs/blocks/`; where they disagree, this file wins.

## Core Principles

### I. One Codebase, No Customer Forks

Every deployment, shared or dedicated, runs the same released, versioned images. Whatever differs
between deployments (domain, secrets, branding, limits, templates, fonts, workflows) MUST be
configuration or data, never code. No domain, product name, logo, limit or URL may be hardcoded:
a value a company could want different is a configuration key with a documented default. A
customer need that configuration cannot meet is either built into the main codebase behind a
setting that is off by default, or belongs in that customer's own systems or orchestration.
Customer-specific branches and forks are prohibited.

*Rationale:* a fork stops receiving fixes the day it is made, and every security fix then costs one
merge per customer.

**Gate:** the feature adds no customer-specific code path and no new hardcoded deployment value;
every new setting is listed in `docs/blocks/04-backend.md#configuration` with its default.

### II. A Block Stands Alone

A block owns its database, its configuration and its deployment. It MUST NOT read or write another
block's database, MUST NOT require another block to be running in order to start or pass
`/health/ready`, and MUST connect to shared infrastructure (Postgres server, RabbitMQ, reverse
proxy) only through its own database, role and vhost, chosen by configuration.

*Rationale:* this is what lets a block be removed, moved or installed alone.

**Gate:** deleting every other block leaves this one healthy; no connection string, query or project
reference points at another block's storage or code.

### III. Generic Capabilities, No Product Knowledge

A block is a technical capability (render, send, store, notify, charge). Business concepts
(orders, carts, customers, restaurants, deliveries) MUST NOT appear in a block's code, contracts,
schema, error codes or UI. A block's data is scoped by `ClientId` and nothing else. A candidate
feature that needs a product's business noun belongs in that product, not in a block.

*Rationale:* a generic "orders" block ends up too complicated for every product that uses it.

**Gate:** the spec and plan introduce no product vocabulary; every new table holding client data
has `client_id` and the client query filter.

### IV. Products Compose; Blocks Never Call Each Other

Workflows that span blocks ("render the invoice, then email it") are orchestrated by the product or
by a workflow engine, never by one block calling another. Where blocks must fit together, they do
so through shared conventions (a block returns a signed file URL; another accepts attachments by
URL), not through runtime dependencies, shared libraries of business logic, or direct calls.

*Rationale:* a call from one block to another turns two independent pieces into one that cannot be
deployed apart.

**Gate:** the feature adds no HTTP client, message publisher or package reference targeting another
block.

### V. Every Block Exposes the Same Studs

Every block MUST implement the shared conventions defined in `docs/blocks/04-backend.md#the-studs`,
through the shared `Blocks.*` packages rather than re-implementing them:

- API-key authentication (`X-Api-Key`) producing `client_id` and `scope` claims; admin key and
  console session for the operator.
- RFC 9457 problem details on every error, always carrying a stable `code` and a `traceId`;
  another client's resource answers 404, never 403.
- `202 Accepted` plus a status URL for anything that can take more than a second.
- `Idempotency-Key` on every creating `POST`.
- Signed webhooks following the Standard Webhooks specification.
- Signed, expiring file URLs; files deleted after a configured retention.
- Per-client rate and pending-work limits answering 429 with `Retry-After`.
- One list envelope supporting cursor paging (products) and page paging with a total (console).
- `/health/live` and `/health/ready`, unauthenticated.

A block that cannot follow a stud MUST document why in its plan's Complexity Tracking.

*Rationale:* learning one block teaches all of them, and a product integrates each the same way.

**Gate:** every new endpoint, message and list conforms to the studs, or the deviation is justified
in Complexity Tracking.

### VI. Identity Comes from Claims Only

Code below the authentication handlers MUST read the caller's identity only through
`ICurrentClient` (`ClientId`, `Scopes`, `IsAdmin`). It MUST NOT inspect headers, keys, cookies or
tokens, or branch on how the caller authenticated. Bus consumers set `ICurrentClient` from the
message before any other work. `X-Client-Id` is honoured only for principals with the `admin` scope.

*Rationale:* this is what lets authentication move from API keys to central tokens without touching
a single feature.

**Gate:** no feature code references `HttpContext`, request headers or authentication schemes for
identity.

### VII. Published Contracts Change Additively

Message interfaces, error codes, HTTP request and response shapes, and webhook payloads are public
API. Within a major version they MAY gain optional members and new codes, and MUST NOT remove,
rename, retype or change the meaning of anything. A message type's namespace and name MUST never
change, because they form its exchange name on the broker. A breaking change ships beside the old
one (a new path such as `/api/v2/…` or a new message type), never in place. Every contract change
bumps the `PdfBuilder.Contracts` version, is accepted explicitly in `CompatibilitySuppressions.xml`
when the package validator requires it, and is recorded in `CHANGELOG.md`.

*Rationale:* dedicated deployments upgrade on their own schedule, and their integrations must keep
working across minor versions.

**Gate:** contract diffs are additive only, the package version and changelog are updated, and
`ContractsTests` plus package validation pass.

### VIII. Secrets Are Unrecoverable and Shown Once

API keys, the admin key and similar credentials are stored only as hashes. Secrets a block must use
itself (webhook signing secrets) are stored encrypted with Data Protection. A newly created secret
is shown exactly once and never again; no secret appears in logs, problem details, support bundles,
exports or telemetry. Comparisons of secrets use constant-time equality.

*Rationale:* a database or log leak must not become a credential leak.

**Gate:** no new field stores a secret in plain text; no log statement, response or export can
include one.

### IX. Layering Is Enforced by Tooling

Backend dependencies point one way: `Api -> Infrastructure -> Application -> Domain -> Contracts`,
and no `Blocks.*` assembly references a block's own assemblies. Frontend layering follows the
ESLint rules in `web/eslint.config.js` (`core`/`shared` never import features; `ui` never uses
services; features never import each other). A rule that matters is enforced by the build, a lint
rule or an architecture test, not by convention.

*Rationale:* boundaries that are only described erode one convenient import at a time.

**Gate:** the build, lint and architecture tests pass without new suppressions; any new boundary the
feature introduces has a rule enforcing it.

### X. Runs on Any Customer's Hardware, Offline

Every image MUST build for `linux/amd64` and `linux/arm64`. A deployment MUST work with no internet
access at runtime: no CDN scripts or web fonts in the console, Chromium and fonts inside the image,
templates unable to reach the network by default, no telemetry or licence call-home unless the
operator configures an exporter. Docker on Linux is the only supported install target.

*Rationale:* companies install blocks on their own networks, often closed ones.

**Gate:** both image architectures build in CI; the feature adds no runtime dependency on an
external host.

## Technology and Build Constraints

- **Backend:** .NET 10, ASP.NET Core minimal APIs, MediatR and FluentValidation in Application,
  EF Core with Npgsql on PostgreSQL (one database per block), MassTransit on RabbitMQ.
- **MassTransit stays on 8.5.x**, the last Apache-2.0 line. Version 9 is commercially licensed and
  refuses to start without a licence; it is bumped only alongside a purchased licence.
- **Frontend:** Angular 22 (zoneless, signals, OnPush, Signal Forms), Tailwind 4, Angular ports of
  shadcn/ui `base-nova` in `shared/ui`, Lucide icons through the explicit icon list. Design rules
  are in `docs/blocks/02-ui-ux.md`.
- **Warnings are errors** (`Directory.Build.props`); the build is the C# linter.
- **Package versions are central** (`Directory.Packages.props`); `.csproj` files reference by name.
  `nuget.config` pins a single source; `global.json` pins the SDK band.
- **Rendering is never on a request thread.** Slow work is queued and reported (Principle V).
- **Configuration** of studs uses the `Blocks` section; every key has a default or fails start-up
  with a message naming it.

## Development Workflow and Quality Gates

- **Spec-driven:** every feature goes through `/speckit-specify` → `/speckit-plan` →
  `/speckit-tasks` → `/speckit-implement`. Specs state *what* and *why*; technology choices belong
  in the plan. The backlog and paste-ready descriptions are in `docs/blocks/03-planning.md`.
- **One concern per branch and pull request.** Unrelated work (a contracts release and design docs,
  for example) is never mixed in one pull request.
- **Definition of done**, for every feature:
  - Behaviour tested at the layer where the rule lives, plus one API test per new endpoint covering
    its happy path and its main refusal.
  - For anything that reads client data, a cross-client test: client B receives 404 for client A's
    resource.
  - `docs/` updated in the same pull request: `docs/blocks/` for design changes; `configuration.md`,
    `message-contracts.md` and `operations.md` for anything they describe.
  - `CHANGELOG.md` entry; `PdfBuilder.Contracts` version bumped when a contract changed.
  - `docker compose up --build` works from a clean checkout.
  - CI is green, including the browser-backed tests (`PDFBUILDER_RUN_BROWSER_TESTS=1`) and the
    arm64 image build.
- **Releases:** contracts are published only by pushing a `contracts-v<version>` tag; merging never
  publishes. Customer deployments pin exact image versions, never `latest`.

## Governance

This constitution supersedes conflicting guidance anywhere else in the repository, including
`docs/blocks/`. Every `/speckit-plan` MUST run the Constitution Check against the Gate line of each
principle, before research and again after design; a failed gate is either fixed or justified in the
plan's Complexity Tracking, and an unjustified failure blocks the plan. Every pull request review
checks the gates that apply to its change.

**Amendments** are made by pull request that changes this file, states the version bump and its
reason in the Sync Impact Report, and updates any document that restates the changed rule in the
same pull request (`docs/blocks/README.md` points here rather than restating the principles).
Removing or redefining a principle requires a migration note for code that relied on it.

**Versioning** follows semantic versioning: MAJOR for removing or redefining a principle in a
backward-incompatible way, MINOR for adding a principle or section or materially expanding one,
PATCH for clarifications and wording.

**Scope across repositories:** while the shared `Blocks.*` packages live in this repository, this
file is the source of truth. When they move to a `blocks-platform` repository, the constitution
moves with them and each block repository carries a copy synchronised from it; a block may add
block-specific sections but MUST NOT weaken a principle.

Runtime development guidance: `docs/blocks/` (design), `docs/` (operation of this block),
`web/README.md` (frontend conventions).

**Version**: 1.0.0 | **Ratified**: 2026-11-03 | **Last Amended**: 2026-11-03
