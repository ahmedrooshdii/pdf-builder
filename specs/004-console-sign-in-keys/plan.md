# Implementation Plan: Console Sign-in and Key Management

**Branch**: `004-console-sign-in-keys` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/004-console-sign-in-keys/spec.md`

## Summary

Since feature 002 the service refuses every caller without a key, including this repository's
console. This feature gives the console a way in: the operator pastes the admin key once and gets a
12-hour session held in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie. Each session is also a row
in the block's database, keyed by a hash of an id only the cookie carries, so signing out, or
replacing the admin key, ends it on every instance at once. A `Blocks` policy scheme sends requests
with `X-Api-Key` to the 002 handler (unchanged) and requests with only the cookie to the new
`ConsoleSession` scheme. Cookie-authenticated changes must carry `X-Console: 1`. Failed sign-ins are
limited to 5 a minute per address. The admin principal can act as a client through `X-Client-Id`.

In the browser, a session service, a guard and one interceptor wrap every existing screen. A client
switcher in the header puts the chosen client in the URL and on every page. New pages for API keys
(list with last use, create with a reveal-once dialog that cannot be dismissed by accident, revoke
with confirmation) and for clients (add, disable, enable). Details and alternatives:
[research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 10 (`global.json`); TypeScript with Angular 22 (zoneless, signals,
Signal Forms).

**Primary Dependencies**: ASP.NET Core cookie authentication and forwarded headers (shared
framework), Data Protection from the shared framework with a key repository of our own over the
block's `DbContext` (R2). **No new packages**: the planned
`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` was dropped during implementation because it
pulls cryptography packages with published advisories (R2). No new npm packages: the dropdown menu
and alert dialog are ported into `shared/ui` like the existing primitives.

**Storage**: PostgreSQL, the block's own database. One migration, `AddConsoleSessions`, adds
`console_sessions` and `data_protection_keys` ([data-model.md](data-model.md)).

**Testing**: xUnit v3 + NSubstitute; `StudHost` + `ManualClock` for stud tests without a database;
database-backed and whole-pipeline tests opt-in with `PDFBUILDER_RUN_DATABASE_TESTS=1`
(Testcontainers or `PDFBUILDER_TEST_POSTGRES`), on in CI. Console: Vitest through `ng test`, ESLint.

**Target Platform**: Linux containers, `linux/amd64` and `linux/arm64`; the console in current
evergreen browsers.

**Project Type**: Web service (API plus RabbitMQ consumer) and its Angular console.

**Performance Goals**: session validation is one primary-key read per console request, with no
cache (R1). Console traffic is one operator. Product traffic with keys follows the 002 path and must
not slow down: < 25 ms added for 95% of requests (002 SC-007).

**Constraints**: no change for product callers (002 tests unchanged); the admin key and created keys
never stored in the browser; no secret in logs; no CDN or external host (Principle X); the console and
API are served from one site (R18).

**Scale/Scope**: one operator role; tens of clients and keys per deployment; 4 backend endpoints
(3 session, 1 scopes) plus 1 header; 3 new console pages (sign in, keys, clients) and a shell.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Pre-research | Post-design |
| --- | --- | --- | --- |
| I | One codebase, no forks | ✅ | ✅ Two new settings with defaults, `Blocks:Console:SessionLifetime` and `Blocks:ForwardedHeaders:KnownNetworks` ([contracts/configuration.md](contracts/configuration.md)); cookie name and Data Protection application name come from the block code. Nothing deployment-specific in the console (FR-032). |
| II | Block stands alone | ✅ | ✅ Sessions and Data Protection keys in the block's own database; nothing calls another service. |
| III | Generic, no product knowledge | ✅ | ⚠ `console_sessions` holds no client data and has no `client_id` by design. The 001 data tables still have no `client_id` (carried from 002, ends with 003). |
| IV | Blocks never call each other | ✅ | ✅ |
| V | Same studs | ✅ | ⚠ This feature **is** the console-session stud, built in `Blocks.*` (`MapBlocksConsole()`, `AddBlocksDataProtection<T>()`). Two carried gaps: admin lists without paging, creating `POST`s without `Idempotency-Key`. See Complexity Tracking. |
| VI | Identity from claims only | ✅ | ✅ The session principal has the admin key's claims. `X-Client-Id` is read only inside `HttpCurrentClient` in `Blocks.ServiceDefaults` and only for admins (R8). The `X-Console` rule is the stud's own middleware. No PDF Builder code changes for identity. |
| VII | Contracts change additively | ✅ | ✅ New endpoints, an optional request header and two new codes (`CONSOLE_HEADER_REQUIRED`, `RATE_LIMITED`); nothing existing changes meaning. Message contracts and the `PdfBuilder.Contracts` version unchanged. `CHANGELOG.md` service entry. |
| VIII | Secrets unrecoverable, shown once | ✅ | ⚠ The admin key is sent once and never stored by the browser. Session ids are stored only as SHA-256. Created keys exist in one response and one dialog signal. Data Protection's key ring is stored unencrypted in the database: see Complexity Tracking. |
| IX | Layering by tooling | ✅ | ✅ Backend: the existing architecture test still covers `Blocks.*` ↛ `PdfBuilder.*`. Console: `core/auth` and `core/client-context` are under `core` (no feature imports, existing rule); the new `features/console/api-keys` and `features/console/clients` fall under the existing no-cross-feature rule. `ClientsApi` goes to `shared/data-access` so `core` can use it. No new suppressions. |
| X | Any hardware, offline | ✅ | ✅ No external call, no CDN, no new image dependency; the cookie works on `localhost` and HTTPS deployments. |

**Result**: passes with four justified deviations (two under V, one each under III and VIII). Each is
in Complexity Tracking.

Definition-of-done note: this feature reads no client data (that is 003), so the cross-client 404
test does not apply. Each new endpoint gets an API test for its happy path and main refusal.

## Project Structure

### Documentation (this feature)

```text
specs/004-console-sign-in-keys/
├── spec.md
├── plan.md                      # this file
├── research.md                  # R1–R18
├── data-model.md                # console_sessions, data_protection_keys, cookie, browser state
├── quickstart.md
├── contracts/
│   ├── console-session.md       # /api/console/session, X-Console, X-Client-Id, /api/admin/scopes, codes
│   ├── console-ui.md            # routes, shell, screens, exact wording, never-rules
│   └── configuration.md         # two new settings, serving requirements, admin key rotation
├── checklists/requirements.md
└── tasks.md                     # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Blocks.ServiceDefaults/
│   ├── BlocksAuthOptions.cs                        + ConsoleSessionLifetime (validated 5 min–24 h), KnownNetworks
│   ├── BlocksAuthExtensions.cs                     `Blocks` policy scheme (R4), ConsoleSession cookie scheme,
│   │                                               policies on `Blocks`; PermissionDescriptor overload (R9);
│   │                                               UseBlocksAuthentication() = forwarded headers + authN +
│   │                                               X-Console + X-Client-Id checks; MapBlocksConsole()
│   ├── Authentication/
│   │   ├── BlocksClaims.cs                         + SessionId claim, Blocks/ConsoleSession scheme names
│   │   └── PermissionDescriptor.cs                 NEW  (Name, Description)
│   ├── Sessions/                                   NEW  (not Console/: that namespace would shadow System.Console)
│   │   ├── ConsoleSessionEndpoints.cs              POST/GET/DELETE /api/console/session (R10)
│   │   ├── ConsoleSessionCookie.cs                 cookie options and events: validate row, 401 instead of redirect (R1, R3, R4)
│   │   ├── IConsoleSessionStore.cs                 port: create, find, revoke, delete expired
│   │   ├── SignInThrottle.cs                       5 failures / minute / address (R6)
│   │   └── ConsoleHeaderMiddleware.cs              X-Console: 1 on cookie-authenticated changes (R5)
│   ├── Clients/ClientAdminEndpoints.cs             + GET /api/admin/scopes
│   ├── Identity/
│   │   ├── HttpCurrentClient.cs                    + X-Client-Id for admins (R8)
│   │   ├── ActingClientMiddleware.cs               NEW  400 for a malformed X-Client-Id from an admin
│   │   └── ClientLogScopeMiddleware.cs             + ClientId when an admin acts as a client
│   └── Problems/BlocksProblems.cs                  + CONSOLE_HEADER_REQUIRED, RATE_LIMITED
├── Blocks.ServiceDefaults.EntityFramework/
│   ├── ConsoleSessionRecord.cs                     NEW
│   ├── ModelBuilderExtensions.cs                   + AddBlocksConsoleSessions(), data_protection_keys mapping
│   ├── EfConsoleSessionStore.cs                    NEW  EfConsoleSessionStore<TContext> : IConsoleSessionStore
│   └── DataProtectionExtensions.cs                 NEW  AddBlocksDataProtection<TContext>(), EfXmlRepository (R2)
├── PdfBuilder.Infrastructure/
│   ├── Persistence/PdfBuilderDbContext.cs          + AddBlocksConsoleSessions()
│   ├── Persistence/Migrations/                     + AddConsoleSessions
│   └── DependencyInjection.cs                      + console session store, Data Protection
└── PdfBuilder.Api/
    ├── Program.cs                                  UseBlocksAuthentication(); MapBlocksConsole()
    └── Endpoints/PdfPermissions.cs                 descriptions → PermissionDescriptor list

web/src/app/
├── app.routes.ts                                   /sign-in outside; shell with `signedIn` canMatch; /keys, /clients
├── app.config.ts                                   + console interceptor
├── core/
│   ├── auth/                                       NEW  session.service.ts, signed-in.guard.ts, safe-return-url.ts
│   ├── client-context/                             NEW  client-context.ts (URL → storage → only client) (R13)
│   ├── http/console.interceptor.ts                 NEW  X-Console, X-Client-Id, 401 → sign-in (R14)
│   └── layout/shell/                               NEW  header with client switcher, nav, session menu (R12);
│                                                         replaces the header in app.html
├── shared/
│   ├── data-access/clients.api.ts, client.models.ts   NEW  GET/POST/PATCH /api/admin/clients (used by core and clients)
│   └── ui/                                         dialog: + dismissible (R15); NEW dropdown-menu, alert-dialog,
│                                                         copy-button, relative-time pipe
└── features/
    ├── console/auth/sign-in.page.ts                NEW
    ├── console/api-keys/                           NEW  data-access (api-keys.api.ts, scopes), feature (keys page),
    │                                                     ui (keys table, create dialog, reveal dialog, revoke dialog)
    ├── console/clients/                            NEW  feature (clients page), ui (table, add dialog, disable dialog)
    ├── pdf-jobs/                                   history records clientId and filters (R16); "New PDF for <id>"
    └── templates/                                  "New template for <id>"; choose-a-client state comes from the shell

tests/
├── Blocks.ServiceDefaults.Tests/
│   ├── ConsoleSessionTests.cs                      sign-in/out/refresh against an in-memory store with ManualClock:
│   │                                               expiry at 12 h, no sliding, fingerprint mismatch, replayed cookie
│   ├── SignInThrottleTests.cs                      5 failures, window reset, success not counted, Retry-After
│   ├── ConsoleHeaderMiddlewareTests.cs             unsafe methods need X-Console; keys unaffected
│   ├── SchemeSelectionTests.cs                     key wins over cookie; no credential → 002 challenge unchanged
│   ├── CurrentClientTests.cs                       + X-Client-Id honoured for admin only; malformed → 400
│   └── Support/InMemoryConsoleSessionStore.cs      NEW
├── PdfBuilder.Infrastructure.Tests/
│   └── EfConsoleSessionStoreTests.cs               store and migration against PostgreSQL
└── PdfBuilder.Api.Tests/
    ├── ConsoleSessionApiTests.cs                   end to end: cookie attributes, session works on every
    │                                               admin/pdf/template endpoint, two hosts share one session,
    │                                               sign-out on one refuses on the other, restart keeps it,
    │                                               new admin key hash ends it, outage → 503, scopes endpoint
    ├── AuthenticationPipelineTests.cs              + console endpoints in the anonymous/admin lists; unchanged
    │                                               answers for keyless callers
    └── KeyLeakTests.cs                             + no admin key or session id in logs or responses

web/src/app/**/*.spec.ts                            session service, guard, interceptor, client context, safe return
                                                    URL, sign-in page, reveal dialog (Escape, close event, clears key,
                                                    no storage writes), create/revoke dialogs, clients page, history filter

compose.yaml                                        + Blocks__ForwardedHeaders__KnownNetworks
deploy/compose.prod.yaml, deploy/README.md          forwarded headers example; HTTPS and same-site requirement
samples/console.http                                NEW  sign in, session, sign out, acting as a client
Directory.Packages.props                            unchanged (R2)
```

**Structure Decision**: the session stud goes into the two existing `Blocks.*` projects, beside the
key handler, as 002 set out. `PdfBuilder.*` changes only in composition (`Program.cs`,
`DependencyInjection.cs`), the `DbContext` and its migration, and the permission descriptions. The
console grows the `core/auth`, `core/client-context`, `core/layout/shell` and `features/console/*`
folders that `docs/blocks/05-frontend.md` lays out. The `@blocks/console-kit` library is left to
feature 011.

### Documentation updated in this feature

| Document | Change |
| --- | --- |
| `README.md` | The console signs in with the admin key; remove "refused until 004" |
| `web/README.md` | Remove the refusal banner; sign-in, client switcher, `core/auth`, `core/client-context`, interceptor; same-site/HTTPS rule |
| `docs/configuration.md` | `Blocks:Console:SessionLifetime`, `Blocks:ForwardedHeaders:KnownNetworks` |
| `docs/operations.md` | Signing in; rotating the admin key ends sessions; the Data Protection warning; proxies and the sign-in limit; serving over HTTPS |
| `docs/architecture.md` | The `Blocks` policy scheme and the session row in the request flow |
| `docs/blocks/04-backend.md` | What was built: one key handler (no separate `AdminKey` scheme), session rows, `X-Client-Id`, `/api/admin/scopes`, new settings and codes |
| `docs/blocks/05-frontend.md` | What was built: header shell now, sidebar deferred to 010; `ClientsApi` in `shared` |
| `deploy/README.md` | Forwarded headers; HTTPS and same-site requirement for the console |
| `CHANGELOG.md` | Service entry: console sign-in; new endpoints, header and codes; with 002, the M1 release note |

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
| --- | --- | --- |
| VIII: Data Protection's key ring (`data_protection_keys`) is stored unencrypted in the database | Every instance must read the session cookie (FR-004), so the ring must be shared, and the block's database is the only store all instances share (Principle II). The design still holds against a database reader: a cookie is accepted only with a live `console_sessions` row, which stores `SHA-256(sid)`, so a forged cookie cannot match one (R1). | Encrypting the ring with an X.509 certificate: every deployment would need to issue, mount and rotate a certificate for a protection the session rows already give. A key directory on a shared volume: still unencrypted, and one more volume per deployment. |
| V: `GET /api/admin/clients` and `GET …/keys` still return the list envelope without paging (carried from 002, which deferred it to 004) | The console shows both lists whole; a deployment has tens of each. The first console list that needs page paging is the server-side jobs list, so the shared paging moves to feature 010. | Building cursor and page paging now: a second stud inside this feature, for two lists that fit on one screen. |
| V: `POST /api/console/session` and the admin `POST`s the console calls take no `Idempotency-Key` (carried from 002) | The idempotency stud is feature 006. A repeated sign-in makes a second session, which expires on its own; a repeated client registration answers `409`; the console disables its create buttons while a request runs, so a double click cannot issue two keys. | Implementing idempotency storage here for operator-only endpoints, ahead of the shared design. |
| III: `pdf_jobs` and `authored_templates` still have no `client_id` (carried from 001 and 002) | The console now sends the chosen client and the server knows it (`ICurrentClient.ClientId`). Separating the data is feature 003. | Adding the column here: mixes two features' migrations, and 003's backfill rule belongs with its spec. |
| I: the console's product name and logo are still compiled in (carried; found by `/speckit-analyze`, I1) | The sign-in card and the shell show the block's name and logo, which the console has compiled in since before this feature. They are read from the console's one existing source (`APP_NAME`, the `Logo` component), not repeated, so this feature adds no new hardcoded value (spec FR-032). | Runtime branding now (`GET /api/console/info`): that is feature 012, with its own settings and logo upload. |

**Deviations from `docs/blocks/`** (not constitution violations; the docs are updated to match):
no separate `AdminKey` scheme (R4); the sidebar shell is deferred and the header carries the switcher
(R12); permission descriptions come from the API rather than `BLOCK_CONFIG` (R9).
