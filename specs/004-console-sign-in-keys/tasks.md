---

description: "Task list for feature 004: Console Sign-in and Key Management"
---

# Tasks: Console Sign-in and Key Management

**Input**: Design documents from `/specs/004-console-sign-in-keys/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The constitution's definition of done requires one API test per new endpoint
(happy path and main refusal), and the plan's Project Structure names the test files. Several
requirements can only be proven by tests: FR-005 (a replayed cookie is refused), FR-006 (key rotation
ends sessions), SC-005 (the key is in no browser storage), SC-010 (no change without `X-Console`).
Database-backed and whole-pipeline tests are opt-in with `PDFBUILDER_RUN_DATABASE_TESTS=1`, as in the
rest of the suite (`PDFBUILDER_TEST_POSTGRES` points them at an existing server).

**Organization**: Tasks are grouped by user story (US1–US7 from spec.md) so each can be implemented and
tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to
- Paths are relative to the repository root

## Path Conventions

- Shared stud packages: `src/Blocks.ServiceDefaults/`, `src/Blocks.ServiceDefaults.EntityFramework/`
- Block projects: `src/PdfBuilder.Api/`, `src/PdfBuilder.Infrastructure/`
- Console: `web/src/app/` (`core/`, `shared/`, `features/`)
- Tests: `tests/Blocks.ServiceDefaults.Tests/`, `tests/PdfBuilder.Infrastructure.Tests/`, `tests/PdfBuilder.Api.Tests/`, `web/src/app/**/*.spec.ts`
- Rules for every task:
  - nothing under `src/Blocks.*` may reference a `PdfBuilder.*` assembly (Principle IX)
  - no PDF Builder code reads headers, cookies or schemes for identity, only `ICurrentClient` (Principle VI)
  - no admin key, client key, session id or cookie value in any log line, problem body or browser storage (Principle VIII)
  - `core` and `shared` never import from `features`, and features never import each other (ESLint, `web/eslint.config.js`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Packages, settings and test support that everything else uses.

- [X] T001 Give `src/Blocks.ServiceDefaults.EntityFramework/Blocks.ServiceDefaults.EntityFramework.csproj` a `FrameworkReference` to `Microsoft.AspNetCore.App` for the Data Protection API. *Changed:* `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` was not added; its cryptography dependencies have published advisories (research R2)
- [X] T002 [P] Add to `src/Blocks.ServiceDefaults/BlocksAuthOptions.cs`: constants `ConsoleSessionLifetimeKey = "Blocks:Console:SessionLifetime"` and `KnownNetworksKey = "Blocks:ForwardedHeaders:KnownNetworks"`; properties `TimeSpan ConsoleSessionLifetime` (default `12:00:00`) and `IReadOnlyList<IPNetwork> KnownNetworks` (default empty); bind both in `AddBlocksAuthentication` (`src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs`), parsing `KnownNetworks` as comma-separated CIDRs; extend `BlocksAuthOptionsValidator` so a lifetime outside "`00:05:00` to `24:00:00`" or an unparsable CIDR fails start-up with a message naming the key (contracts/configuration.md, R7, R11)
- [X] T003 [P] Extend `tests/Blocks.ServiceDefaults.Tests/BlocksAuthOptionsTests.cs`: default lifetime is 12 h; `00:04:59` and `24:00:01` fail naming `Blocks:Console:SessionLifetime`; `172.16.0.0/12, 10.0.0.0/8` parses to two networks; `not-a-cidr` fails naming `Blocks:ForwardedHeaders:KnownNetworks`
- [X] T004 [P] Add `tests/Blocks.ServiceDefaults.Tests/Support/InMemoryConsoleSessionStore.cs` implementing `IConsoleSessionStore` (T007) over a dictionary keyed by the id hash, with a switch that makes every call throw `StorageUnavailableException`; register it in `tests/Blocks.ServiceDefaults.Tests/Support/StudHost.cs` beside `InMemoryClientKeyStore`, with the existing `ManualClock`, and give `StudHost` a cookie container so tests can replay `Set-Cookie`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The `Blocks` policy scheme, the `ConsoleSession` cookie scheme with its row store, Data
Protection in the database, the new problem codes and the console's shared UI primitives. Product
callers must behave exactly as in 002 at the end of this phase.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Backend

- [X] T005 [P] Add to `src/Blocks.ServiceDefaults/Authentication/BlocksClaims.cs`: `BlocksClaims.SessionId = "sid"`; class `BlocksAuthenticationDefaults` with `PolicyScheme = "Blocks"`; class `ConsoleSessionDefaults` with `Scheme = "ConsoleSession"`, `HeaderName = "X-Console"`, `HeaderValue = "1"` and `CookieName(string blockCode) => $"blocks.{blockCode}.console"` (data-model.md "The session cookie")
- [X] T006 [P] Add to `src/Blocks.ServiceDefaults/Problems/BlocksProblems.cs`: codes `CONSOLE_HEADER_REQUIRED` (403, title "Console header required", detail "Changes made with a console session must carry X-Console: 1.") and `RATE_LIMITED` (429, title "Too many attempts", detail "Too many failed sign-ins from this address. Try again later."), with factory methods `ConsoleHeaderRequired()` and `RateLimited(TimeSpan retryAfter)`; make `WriteAsync` set `Retry-After` (whole seconds, at least 1) for 429 (contracts/console-session.md "New codes")
- [X] T007 [P] Create `src/Blocks.ServiceDefaults/Sessions/IConsoleSessionStore.cs`: record `ConsoleSession(byte[] IdHash, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, byte[] AdminKeyFingerprint)` and interface with `CreateAsync(ConsoleSession, CancellationToken)`, `FindAsync(byte[] idHash, CancellationToken)` (null when absent), `RevokeAsync(byte[] idHash, DateTimeOffset at, CancellationToken)` and `DeleteExpiredBeforeAsync(DateTimeOffset cutoff, CancellationToken)`; every method throws `StorageUnavailableException` when storage is unreachable
- [X] T008 [P] Create `src/Blocks.ServiceDefaults.EntityFramework/ConsoleSessionRecord.cs` and add `AddBlocksConsoleSessions(this ModelBuilder)` to `src/Blocks.ServiceDefaults.EntityFramework/ModelBuilderExtensions.cs`: table `console_sessions`; `id_hash bytea` PK `pk_console_sessions` with check `ck_console_sessions_id_hash_length` "`octet_length(id_hash) = 32`"; `created_at timestamptz not null`; `expires_at timestamptz not null` with index `ix_console_sessions_expires_at`; `revoked_at timestamptz null`; `admin_key_fingerprint bytea not null` with check `ck_console_sessions_fingerprint_length` "`octet_length(admin_key_fingerprint) = 16`"; no `client_id`. In the same method map `DataProtectionKey` to `data_protection_keys` (`id` identity PK `pk_data_protection_keys`, `friendly_name text null`, `xml text null`), every name spelled out like `AddBlocksClients()` (data-model.md)
- [X] T009 Create `src/Blocks.ServiceDefaults.EntityFramework/EfConsoleSessionStore.cs`: `EfConsoleSessionStore<TContext> : IConsoleSessionStore` (where `TContext : DbContext`), translating Npgsql connection failures through the existing `NpgsqlStorageFailures` into `StorageUnavailableException`; `RevokeAsync` sets `revoked_at` only when it is null (`ExecuteUpdateAsync`); `DeleteExpiredBeforeAsync` uses `ExecuteDeleteAsync`; add `AddBlocksConsoleSessionStore<TContext>()` next to `AddBlocksClientStore<TContext>()` in `src/Blocks.ServiceDefaults.EntityFramework/EfClientKeyStore.cs`'s extension block (depends on T007, T008)
- [X] T010 [P] Create `src/Blocks.ServiceDefaults.EntityFramework/DataProtectionExtensions.cs`: `AddBlocksDataProtection<TContext>()` registering `EfXmlRepository<TContext>` (an `IXmlRepository` over `data_protection_keys`, each call in its own scope) as `KeyManagementOptions.XmlRepository`, with application name `blocks.{BlocksAuthOptions.BlockCode}` (R2)
- [X] T011 In `src/PdfBuilder.Infrastructure/Persistence/PdfBuilderDbContext.cs` call `modelBuilder.AddBlocksConsoleSessions()` after `AddBlocksClients()`; register `AddBlocksConsoleSessionStore<PdfBuilderDbContext>()` and `AddBlocksDataProtection<PdfBuilderDbContext>()` beside `AddBlocksClientStore` in `src/PdfBuilder.Infrastructure/DependencyInjection.cs`; update the context's summary comment to list sessions and Data Protection keys (depends on T008–T010)
- [X] T012 Generate migration `AddConsoleSessions` in `src/PdfBuilder.Infrastructure/Persistence/Migrations/` with `dotnet ef migrations add AddConsoleSessions --project src/PdfBuilder.Infrastructure --startup-project src/PdfBuilder.Api`; check that it creates only `console_sessions` and `data_protection_keys` with the names and constraints from T008 and nothing else changes (depends on T011)
- [X] T013 Create `src/Blocks.ServiceDefaults/Sessions/ConsoleSessionCookie.cs`: an `IConfigureNamedOptions<CookieAuthenticationOptions>` for `ConsoleSessionDefaults.Scheme` setting cookie name `ConsoleSessionDefaults.CookieName(BlockCode)`, `HttpOnly = true`, `SecurePolicy = Always`, `SameSite = Strict`, `Path = "/"`, `ExpireTimeSpan = ConsoleSessionLifetime`, `SlidingExpiration = false`, `TimeProvider` from DI; events: `OnValidatePrincipal` reads the `sid` claim, hashes it with SHA-256, loads the row through `IConsoleSessionStore` and calls `RejectPrincipal()` plus `SignOutAsync` (clearing the cookie) when the row is missing, `revoked_at` is set, `expires_at <= now`, or its fingerprint differs from the configured one (`CryptographicOperations.FixedTimeEquals`); a `StorageUnavailableException` is recorded on `HttpContext.Items` and answered `503 STORAGE_UNAVAILABLE` by the challenge; `OnRedirectToLogin` writes `BlocksProblems.AuthRequired()` (401) and `OnRedirectToAccessDenied` writes `BlocksProblems.ScopeMissing()` (403), never a redirect (R1, R3, R4). Add a static `AdminKeyFingerprint(string adminKeyHash)` returning the first 16 bytes of `SHA-256(UTF8(adminKeyHash))` (depends on T002, T005–T007)
- [X] T014 In `src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs`: register the cookie scheme (`AddCookie(ConsoleSessionDefaults.Scheme)` configured by T013) and a policy scheme `Blocks` (`AddPolicyScheme`) as the default scheme whose `ForwardDefaultSelector` returns `ApiKey` when the request has `X-Api-Key` **or** lacks the console cookie, else `ConsoleSession`; change the fallback policy, the `admin` policy and every permission policy to authenticate with `Blocks` instead of `ApiKey`. Leave `ApiKeyAuthenticationHandler` unchanged (R4) (depends on T013)
- [X] T015 Create `src/Blocks.ServiceDefaults/Sessions/ConsoleHeaderMiddleware.cs`: when `context.User.Identity?.AuthenticationType == ConsoleSessionDefaults.Scheme` and the method is not `GET`, `HEAD` or `OPTIONS` and the request lacks exactly one `X-Console: 1`, write `BlocksProblems.ConsoleHeaderRequired()` and stop; requests authenticated by `X-Api-Key` are never affected (R5) (depends on T005, T006)
- [X] T016 Add `UseBlocksAuthentication(this IApplicationBuilder)` to `src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs`: `UseForwardedHeaders` (`XForwardedFor | XForwardedProto`, `KnownNetworks` from options, `ForwardLimit = 1`), `UseAuthentication`, `ConsoleHeaderMiddleware`, `UseAuthorization`, then the existing client log scope; replace the four calls in `src/PdfBuilder.Api/Program.cs` (`UseAuthentication`, `UseAuthorization`, `UseBlocksClientLogScope`) with it, keeping `UseExceptionHandler` first (R7) (depends on T014, T015)
- [X] T017 [P] Tests in `tests/Blocks.ServiceDefaults.Tests/SchemeSelectionTests.cs` (StudHost): no credential → `401 AUTH_REQUIRED` with `WWW-Authenticate: ApiKey`, identical to 002; a client key and the admin key authenticate as in 002 with or without a console cookie present (key wins); a garbage cookie with no key → `401 AUTH_REQUIRED`, response clears the cookie, never a 302
- [X] T018 [P] Tests in `tests/Blocks.ServiceDefaults.Tests/ConsoleHeaderMiddlewareTests.cs` (StudHost, a session minted through T004's store): session `POST`/`PATCH`/`DELETE` without `X-Console` → `403 CONSOLE_HEADER_REQUIRED` and the endpoint does not run; with `X-Console: 1` → runs; session `GET` without the header → runs; admin key `POST` without the header → runs
- [X] T019 [P] Tests in `tests/PdfBuilder.Infrastructure.Tests/EfConsoleSessionStoreTests.cs` (PostgreSQL fixture): create/find round-trip; revoke sets `revoked_at` once and a second revoke keeps the first time; delete-expired removes only rows with `expires_at` before the cutoff; check constraints reject a 31-byte id hash and a 15-byte fingerprint; the migration applies cleanly from an empty database
- [X] T020 Run `dotnet build PdfBuilder.slnx` and the existing `tests/PdfBuilder.Api.Tests/AuthenticationPipelineTests.cs`, `PermissionTests.cs` and `ClientIdentificationTests.cs` with `PDFBUILDER_RUN_DATABASE_TESTS=1`; all 002 tests must pass unchanged (FR-010, plan Constraints)

### Console primitives

- [X] T021 [P] Port shadcn `base-nova` Dropdown Menu to `web/src/app/shared/ui/dropdown-menu.ts` (trigger, content, item, label, separator; keyboard: arrows, Home/End, Escape returns focus to the trigger; `role="menu"`/`menuitem`), following the style of `web/src/app/shared/ui/select.ts`; spec in `web/src/app/shared/ui/dropdown-menu.spec.ts` for open/close and arrow navigation
- [X] T022 [P] Port shadcn Alert Dialog to `web/src/app/shared/ui/alert-dialog.ts` on top of `web/src/app/shared/ui/dialog.ts` (title, description, cancel and action slots; `role="alertdialog"`; focus starts on Cancel; action button takes a `destructive` variant); spec in `web/src/app/shared/ui/alert-dialog.spec.ts`

**Checkpoint**: product callers unchanged (T020 green); a session can be validated but nothing creates one yet.

---

## Phase 3: User Story 1 - Sign in with the admin key and stay signed in for a working day (Priority: P1) 🎯 MVP

**Goal**: only the admin key opens the console; the session lasts 12 hours, survives reloads,
restarts and instances, and sign-out or a new admin key ends it everywhere.

**Independent Test**: open any console address without a session → sign-in page, no data. Sign in →
the page first asked for. Reload, reopen → still signed in. Sign out → sign-in page; a copy of the old
cookie answers 401 (quickstart §1, §2, §7).

### Tests for User Story 1

- [X] T023 [P] [US1] Tests in `tests/Blocks.ServiceDefaults.Tests/SignInThrottleTests.cs` (with `ManualClock`): 5 failures from one address → the 6th check is refused with a `Retry-After` equal to the seconds left in the window; another address is unaffected; after the window the address is allowed again; recording a success neither consumes nor resets the count
- [X] T024 [P] [US1] Tests in `tests/Blocks.ServiceDefaults.Tests/ConsoleSessionTests.cs` (StudHost + `InMemoryConsoleSessionStore` + `ManualClock`), covering contracts/console-session.md: `POST /api/console/session` with the admin key and `X-Console: 1` → `204` with `Set-Cookie` carrying `httponly`, `secure`, `samesite=strict`, `path=/` and `expires` = now + 12 h, `Cache-Control: no-store`; without `X-Console` → `403 CONSOLE_HEADER_REQUIRED`; a wrong `adm_` key, a client key, an empty value and a 65-character value → `401 AUTH_INVALID_KEY` with the 002 body; 6th failure in a minute → `429 RATE_LIMITED` with `Retry-After` even when the 6th carries the right key; `GET` with the cookie → `200 { expiresAt }`; `GET` with the admin key header and no cookie → `401`; at now + 11 h 59 m the cookie works, at now + 12 h it answers `401 AUTH_REQUIRED` and clears the cookie, and use in between does not extend it; `DELETE` → `204`, then replaying the old cookie → `401`; a host restarted with a different `Blocks:Admin:KeyHash` refuses the old cookie; store outage during `POST` → `503 STORAGE_UNAVAILABLE` with `Retry-After: 5`, and a wrong key during an outage still answers `401` (key checked first)
- [X] T025 [P] [US1] Tests in `tests/PdfBuilder.Api.Tests/ConsoleSessionApiTests.cs` (database-backed `ApiFactory`): a session from sign-in authenticates `GET /api/admin/clients`, `GET /api/templates` and `POST /api/pdf/jobs` (with `X-Console: 1`); two `ApiFactory` hosts on one database: a session made on host A works on host B, sign-out on A makes B answer `401` on the next request; disposing and re-creating a host keeps the session working (Data Protection keys in `data_protection_keys`); a host started with another admin key hash refuses it; sign-in rows older than a day past expiry are deleted by the next sign-in
- [X] T026 [P] [US1] Extend `tests/PdfBuilder.Api.Tests/AuthenticationPipelineTests.cs`: the documented anonymous list gains `POST /api/console/session` and `DELETE /api/console/session`; `GET /api/console/session` is protected; every other protected endpoint still answers `401 AUTH_REQUIRED` with `WWW-Authenticate: ApiKey` when called with no credential; extend `tests/PdfBuilder.Api.Tests/KeyLeakTests.cs` so no captured log line or response body contains the admin key sent at sign-in, the cookie value or the `sid`

### Implementation for User Story 1

- [X] T027 [P] [US1] Create `src/Blocks.ServiceDefaults/Sessions/SignInThrottle.cs`: singleton, fixed one-minute windows per `IPAddress` in a `ConcurrentDictionary` with lazy pruning, `TryEnter(IPAddress, out TimeSpan retryAfter)` refusing once 5 failures are recorded in the current window, `RecordFailure(IPAddress)`; uses `TimeProvider`; register it in `AddBlocksAuthentication` (R6)
- [X] T028 [US1] Create `src/Blocks.ServiceDefaults/Sessions/ConsoleSessionEndpoints.cs` with `Map(IEndpointRouteBuilder)`: `POST /api/console/session` (`AllowAnonymous`; checks in this order: `X-Console: 1` else 403; throttle else 429; body `{ "adminKey" }` empty, over `ApiKeyFormat.MaxLength` (64) or not equal to the admin key by `KeyHasher.HashAdminKey` + `KeyHasher.Matches` → record failure, `401 AUTH_INVALID_KEY`; then `DeleteExpiredBeforeAsync(now - 1 day)`, create a 32-byte random `sid` (`RandomNumberGenerator`), store `SHA-256(sid)` with `expires_at = now + ConsoleSessionLifetime` and the fingerprint, and `SignInAsync(ConsoleSession, principal{scope=admin, sid}, IsPersistent = true, ExpiresUtc = expires_at)` → `204`); `GET /api/console/session` (`RequireAuthorization` with a policy on the `ConsoleSession` scheme only, returns `200 { "expiresAt" }` from the ticket); `DELETE /api/console/session` (`AllowAnonymous`; `X-Console` required; authenticates the cookie scheme explicitly, revokes the row if live, signs out → `204`; on `StorageUnavailableException` → `503` without clearing the cookie). Every response sets `Cache-Control: no-store`. Log with source-generated messages: `Console session started` (Information, with expiry), refused sign-in (Debug, reason `Invalid` or `Throttled`, never the value), `Console session ended` (Information) (depends on T027)
- [X] T029 [US1] Add `MapBlocksConsole(this IEndpointRouteBuilder)` to `src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs` calling `ConsoleSessionEndpoints.Map`, and call `app.MapBlocksConsole()` after `app.MapBlocksAdmin()` in `src/PdfBuilder.Api/Program.cs` (depends on T028)
- [X] T030 [P] [US1] Create `web/src/app/core/auth/session.service.ts` per research R14: `state = signal<'unknown' | 'signed-in' | 'signed-out'>('unknown')`, `expiresAt = signal<Date | null>(null)`, `signIn(adminKey)` (`POST /api/console/session`, then `refresh()`), `signOut()` (`DELETE`, state `signed-out` even on error), `refresh()` (`GET`; 200 → signed-in with `expiresAt`; 401 → signed-out), `markSignedOut()`; the admin key is only ever a function argument, never assigned to a field or storage; spec `web/src/app/core/auth/session.service.spec.ts` (HttpTestingController) asserting states and that `BROWSER_STORAGE` receives no write
- [X] T031 [P] [US1] Create `web/src/app/core/auth/safe-return-url.ts` (`safeReturnUrl(value: string | null): string` returning the value only when it starts with `/` and not `//` or `/\`, else `/`) with `web/src/app/core/auth/safe-return-url.spec.ts` covering `/templates?client=a`, `//evil.example`, `/\evil`, `https://evil.example`, `javascript:alert(1)`, empty and null
- [X] T032 [US1] Create `web/src/app/core/auth/signed-in.guard.ts`: functional `canMatch` that awaits the first `SessionService.refresh()` when state is `unknown`, then allows when signed in or returns a `UrlTree` to `/sign-in?returnUrl=<attempted url>`; spec `web/src/app/core/auth/signed-in.guard.spec.ts` asserting it never redirects while the state is unknown (depends on T030)
- [X] T033 [US1] Create `web/src/app/core/http/console.interceptor.ts` (functional `HttpInterceptorFn`): adds `X-Console: 1` to every request whose URL starts with `API_BASE_URL` + `/api/`; on a `401` from any URL other than `/api/console/session`, calls `SessionService.markSignedOut()` and `router.navigateByUrl('/sign-in?returnUrl=' + encodeURIComponent(router.url))`, then rethrows; register it with `withInterceptors` in `web/src/app/app.config.ts`; spec `web/src/app/core/http/console.interceptor.spec.ts` (header on `/api/…` only; 401 navigates with `returnUrl`; 401 from the session endpoint does not) (depends on T030)
- [X] T034 [US1] Create `web/src/app/features/console/auth/sign-in.page.ts` (+ `sign-in.routes.ts` default export) per contracts/console-ui.md "Sign in": logo and product name, field "Admin key" (`type=password` toggling to `text`, `autocomplete="off"`, show/hide button with `aria-pressed`), hint text, button "Sign in" disabled while submitting; the field is emptied after every attempt; messages exactly "That key was not accepted." (401), "Too many attempts. Try again in a minute." (429), "The service cannot reach its storage. Try again in a few seconds." (503), "The API could not be reached." (status 0); on success navigates to `safeReturnUrl(returnUrl)`; when already signed in, navigates there immediately; spec `web/src/app/features/console/auth/sign-in.page.spec.ts` covering each message, the emptied field and the redirect (depends on T030, T031)
- [X] T035 [US1] Move the header from `web/src/app/app.html`/`app.ts` into `web/src/app/core/layout/shell/shell.ts` (+ `shell.html`), keeping logo, nav and theme toggle, and add the **session menu** (T021 dropdown) on the right: "Signed in as operator", "Session ends at HH:mm" (local time, exact UTC in `title`), "Sign out" (calls `SessionService.signOut()` then navigates to `/sign-in`); `app.html` becomes just `<router-outlet />` (R12, contracts/console-ui.md "Shell") (depends on T021, T030)
- [X] T036 [US1] Restructure `web/src/app/app.routes.ts`: `{ path: 'sign-in', loadChildren: sign-in routes }` outside the shell; one parent `{ path: '', canMatch: [signedIn], loadComponent: shell, children: [ '' → redirect 'jobs/new', jobs, templates ] }`; keep the `**` not-found route last (contracts/console-ui.md "Routes") (depends on T032, T034, T035)

**Checkpoint**: the console works again for whoever holds the admin key, and only for them (quickstart §1, §2, §7, §8).

---

## Phase 4: User Story 2 - Work within one chosen client and always see which one (Priority: P1)

**Goal**: a client switcher in the header; the client in the URL, remembered per browser; every
request made on its behalf; job and template screens within it.

**Independent Test**: with two clients, choose one: it is named on every page; switch: every page names
the other and reloads its content; copy the address into a new tab: same client (quickstart §3).

### Tests for User Story 2

- [X] T037 [P] [US2] Extend `tests/Blocks.ServiceDefaults.Tests/CurrentClientTests.cs`: for the admin key and for a console session, `X-Client-Id: ecommerce` makes `ICurrentClient.ClientId == "ecommerce"` with `IsAdmin` true and `KeyId` null; no header → null; for a client key of `erp`, `X-Client-Id: ecommerce` is ignored (`ClientId == "erp"`); from an admin, `X-Client-Id: Bad_Id` or the header sent twice → `400 VALIDATION_FAILED` with `errors["X-Client-Id"]` and the endpoint does not run; from a client key, a malformed header is ignored
- [X] T038 [P] [US2] In `tests/PdfBuilder.Api.Tests/ConsoleSessionApiTests.cs` (with the other 004 API tests): a request with the admin key and `X-Client-Id: ecommerce` logs a scope with `Admin = true` and `ClientId = ecommerce`
- [X] T039 [P] [US2] Spec `web/src/app/core/client-context/client-context.spec.ts`: URL `?client=` wins over storage; with no URL value the remembered value is used; with neither and exactly one client it is chosen; with several, `current()` is null; an unknown URL id leaves `current()` null and sets `unknownClientId()`; `select()` updates the URL query parameter and storage; a throwing or null `BROWSER_STORAGE` is tolerated

### Implementation for User Story 2

- [X] T040 [US2] In `src/Blocks.ServiceDefaults/Identity/HttpCurrentClient.cs` return the `client_id` claim when present, otherwise, for a principal with `scope=admin`, the single `X-Client-Id` value when it matches "`^[a-z][a-z0-9-]{0,39}$`", otherwise null; update the `ClientId` comment in `src/Blocks.ServiceDefaults/Identity/ICurrentClient.cs` to "null for an admin not acting as a client" (R8)
- [X] T041 [US2] Create `src/Blocks.ServiceDefaults/Identity/ActingClientMiddleware.cs`: for an admin principal with an `X-Client-Id` that is sent more than once or does not match "`^[a-z][a-z0-9-]{0,39}$`", write `BlocksProblems.Validation({ "X-Client-Id": ["Must be a client id: lowercase letters, digits and hyphens, starting with a letter, up to 40 characters."] })` and stop; add it to `UseBlocksAuthentication` after the console header check and before authorization; in `src/Blocks.ServiceDefaults/Identity/ClientLogScopeMiddleware.cs` add `ClientId` beside `Admin = true` when the admin acts as a client (depends on T040)
- [X] T042 [P] [US2] Create `web/src/app/shared/data-access/client.models.ts` (`Client { id, name, status: 'Active' | 'Disabled', createdAt, disabledAt }`, `ListEnvelope<T> { items }`) and `web/src/app/shared/data-access/clients.api.ts` (`list()`, `create({ id, name })`, `update(id, { disabled?, name? })` over `/api/admin/clients`, shapes from `specs/002-clients-and-api-keys/contracts/admin-api.md`) with a spec using HttpTestingController
- [X] T043 [US2] Create `web/src/app/core/client-context/client-context.ts` per research R13: `clients` (`rxResource` over `ClientsApi.list()`), `current` (computed from the router's `client` query parameter, then `BROWSER_STORAGE['pdf-builder.client']`, then the only client), `currentClient` (the `Client` record), `unknownClientId`, `select(id)` (navigates with `queryParams: { client: id }, queryParamsHandling: 'merge'` and remembers it), `reload()` (depends on T042)
- [X] T044 [US2] Extend `web/src/app/core/http/console.interceptor.ts` to add `X-Client-Id: <ClientContext.current()>` to requests under `/api/` except `/api/admin/` and `/api/console/` when a client is chosen; extend its spec accordingly (depends on T033, T043)
- [X] T045 [US2] Add the **client switcher** to `web/src/app/core/layout/shell/shell.ts`/`shell.html` (T021 dropdown) per contracts/console-ui.md "Shell": chosen client's name and monospace id, `lock` icon and "Disabled" for a disabled client, "Choose a client" when none; menu lists every client (name, id, disabled marker); choosing calls `ClientContext.select`; below `md` the nav folds into a menu button while the switcher and session menu stay visible (depends on T043)
- [X] T046 [US2] Create `web/src/app/core/layout/shell/choose-client.ts` and render it in the shell in place of `<router-outlet>` when the active route has `data: { needsClient: true }` and `ClientContext.current()` is null: "Choose the client to work on." with each client as a button, or "No client `<id>` exists. Choose one." when `unknownClientId()` is set; mark the `jobs` and `templates` child routes in `web/src/app/app.routes.ts` with `data: { needsClient: true }`; navigating between shell pages keeps `?client=` (`queryParamsHandling: 'preserve'` on nav links) (depends on T045)
- [X] T047 [US2] In `web/src/app/features/pdf-jobs/data-access/pdf-job.models.ts` add optional `clientId?: string` to `PdfJob`; in `web/src/app/features/pdf-jobs/data-access/job-history.store.ts` record `ClientContext.current()` on `record()` when the entry has none and expose `jobs` filtered to the current client (entries without `clientId` are shown for no client; keep key `pdf-builder.job-history.v1` and the 50-entry cap); extend `job-history.store.spec.ts` for both rules (R16)
- [X] T048 [P] [US2] Name the client where things are created: the new-job page header in `web/src/app/features/pdf-jobs/feature/new-job/new-job.page.html` reads "New PDF for `<id>`" and the templates list action in `web/src/app/features/templates/feature/templates-list/templates.page.html` reads "New template for `<id>`", reading `ClientContext.current()`; changing client re-runs the templates list resource without a page reload

**Checkpoint**: the console always names its client and acts on its behalf (quickstart §3).

---

## Phase 5: User Story 3 - Create a key for the chosen client and see it exactly once (Priority: P1)

**Goal**: create a named, scoped, optionally expiring key; the full key is shown once in a dialog
that cannot be dismissed by accident and is gone from the console afterwards.

**Independent Test**: create a key with one permission, copy it, confirm "I have copied it": it works
on a permitted call and cannot be found anywhere in the console again (quickstart §4).

### Tests for User Story 3

- [X] T049 [P] [US3] Tests for `GET /api/admin/scopes`: in `tests/Blocks.ServiceDefaults.Tests/ClientAdminEndpointTests.cs` the admin key gets `200 { items: [ { name, description } ] }` in declaration order without `admin`, and a client key gets `403 AUTH_SCOPE_MISSING`; in `tests/PdfBuilder.Api.Tests/AdminApiTests.cs` the three PDF permissions are returned with the descriptions in data-model.md "Permission descriptor"
- [X] T050 [P] [US3] Spec `web/src/app/features/console/api-keys/ui/key-reveal-dialog.spec.ts`: renders the key and the warning "Copy it now. It cannot be shown again."; no close button; a `cancel` event is prevented; a `close` event while unconfirmed re-opens the dialog; backdrop click does nothing; "I have copied it" emits `confirmed` and the component's key signal is `null` afterwards; a rejected `navigator.clipboard.writeText` shows "Copy did not work. The key is selected: press Ctrl+C." and selects the key text; no call to `BROWSER_STORAGE.setItem`, `sessionStorage.setItem` or `console.*` with the key (SC-005)
- [X] T051 [P] [US3] Spec `web/src/app/features/console/api-keys/ui/create-key-dialog.spec.ts`: title "New key for `<id>`"; one checkbox per scope with its description; refuses an empty name, a name over 100 characters, no scope and a past expiry on the field at fault; shows the chosen expiry back with its UTC offset; field errors from a `400` land on their fields; the submit button is disabled while the request runs

### Implementation for User Story 3

- [X] T052 [P] [US3] Create `src/Blocks.ServiceDefaults/Authentication/PermissionDescriptor.cs` (`public sealed record PermissionDescriptor(string Name, string Description)`); add an `AddBlocksAuthentication(string blockCode, IReadOnlyList<PermissionDescriptor> permissions)` overload in `src/Blocks.ServiceDefaults/BlocksAuthExtensions.cs` storing them on `BlocksAuthOptions.PermissionDescriptors`, with the existing string overload mapping each name to `new(name, name)` (R9)
- [X] T053 [US3] Add `GET /api/admin/scopes` to `src/Blocks.ServiceDefaults/Clients/ClientAdminEndpoints.cs` (a sibling group of `/api/admin/clients` with the `admin` policy) returning `{ "items": [ { "name", "description" } ] }` in declaration order; add the response record to `src/Blocks.ServiceDefaults/Clients/ClientAdminContracts.cs` (depends on T052)
- [X] T054 [US3] In `src/PdfBuilder.Api/Endpoints/PdfPermissions.cs` add `Descriptors` with "Submit jobs and read them.", "List and read templates." and "Create, duplicate, edit, publish, delete and preview templates." and pass them to `AddBlocksAuthentication` in `src/PdfBuilder.Api/Program.cs` (depends on T052)
- [X] T055 [P] [US3] Add `dismissible = input(true)` to `web/src/app/shared/ui/dialog.ts`: when false, hide the close button, ignore backdrop clicks, `preventDefault()` on the native `cancel` event, and on a native `close` while `open()` is still true call `showModal()` again (Chrome's close-watcher can close despite a prevented `cancel`, R15); extend the existing dialog spec
- [X] T056 [P] [US3] Create `web/src/app/shared/ui/copy-button.ts`: icon button labelled "Copy" that calls `navigator.clipboard.writeText(value)`, shows "Copied" for 2 s, and emits `failed` when the write rejects or the API is missing
- [X] T057 [P] [US3] Create `web/src/app/features/console/api-keys/data-access/api-key.models.ts` (`ApiKey { keyId, display, name, scopes, status: 'Active' | 'Expired' | 'Revoked', createdAt, expiresAt, revokedAt, lastUsedAt }`, `IssuedKey extends ApiKey { key }`, `Scope { name, description }`) and `web/src/app/features/console/api-keys/data-access/api-keys.api.ts` (`list(clientId)`, `issue(clientId, { name, scopes, expiresAt })`, `revoke(clientId, keyId)`, `scopes()` over `/api/admin/clients/{id}/keys` and `/api/admin/scopes`) with a spec; the `issue` result is returned to the caller and never cached
- [X] T058 [US3] Create `web/src/app/features/console/api-keys/ui/create-key-dialog.ts` per contracts/console-ui.md "Create dialog" with Signal Forms: Name required, "1–100 characters"; Scopes "at least one" from `scopes()`; Expires optional, must be in the future, shown back as "Stops working on <date>, <time> (UTC±hh:mm)"; emits the request; displays field errors passed in from a `400` (depends on T057)
- [X] T059 [US3] Create `web/src/app/features/console/api-keys/ui/key-reveal-dialog.ts` per contracts/console-ui.md "Reveal dialog": `dismissible=false` dialog (T055), title "Copy the key for `<id>`", the key in monospace in a selectable element, `copy-button` (T056) with the fallback message on failure, the warning, one button "I have copied it" that clears the key signal to `null` and emits `confirmed` (depends on T055, T056)
- [X] T060 [US3] Create `web/src/app/features/console/api-keys/feature/api-keys.page.ts` (+ `web/src/app/features/console/api-keys/api-keys.routes.ts` default export, route `data: { needsClient: true }`): page header "API keys", the purpose sentence and primary action "New key"; opens the create dialog, calls `issue`, on `201` swaps to the reveal dialog holding the key only in that dialog's input; while the reveal is open implements `HasUnsavedChanges` (so `unsavedChangesGuard` asks before leaving) and registers a `beforeunload` handler, both removed on confirm; add `{ path: 'keys', loadChildren: … }` to the shell children in `web/src/app/app.routes.ts` and "API keys" to the shell nav (depends on T058, T059)

**Checkpoint**: an operator can sign in, choose a client and copy a new working key in under a minute (SC-002).

---

## Phase 6: User Story 4 - See a client's keys and when each was last used (Priority: P2)

**Goal**: the key list for the chosen client with name, public id, scopes, dates, state and last use.

**Independent Test**: use a key, open the list: "less than a minute ago"; a never-used key reads
"never"; revoked and expired keys show their state (quickstart §4 step 4).

### Tests for User Story 4

- [X] T061 [P] [US4] Spec `web/src/app/shared/pipes/relative-time.pipe.spec.ts`: `null` → "never"; 30 s ago → "less than a minute ago"; 3 min → "3 minutes ago"; 5 h → "5 hours ago"; 2 days → the date ("2 Nov 2026")
- [X] T062 [P] [US4] Spec `web/src/app/features/console/api-keys/ui/keys-table.spec.ts`: renders every column from contracts/console-ui.md "Table", the exact UTC time in each date's `title`, a "Revoke key" action only on Active rows, the empty state "No keys yet for `<id>`." with "Create a key"; switching client in the page spec shows only the new client's keys

### Implementation for User Story 4

- [X] T063 [P] [US4] Create `web/src/app/shared/pipes/relative-time.pipe.ts` (pure pipe: "never" for null, relative under a day, `d MMM yyyy` beyond, reading the clock through an injectable for tests)
- [X] T064 [US4] Create `web/src/app/features/console/api-keys/ui/keys-table.ts` with `web/src/app/shared/ui/table.ts`: Name; Key (`display` in monospace with `copy-button` for `keyId`); Scopes as tags; Last used (relative-time pipe, exact UTC in `title`); Created; Expires ("never" or date); State badge (Active `success`, Expired `secondary`, Revoked `outline` muted with `lock`); a row actions menu (T021) that offers "Revoke key" only for Active and emits `revoke(key)`; empty state per contract (depends on T063)
- [X] T065 [US4] In `web/src/app/features/console/api-keys/feature/api-keys.page.ts` load `list(ClientContext.current())` with `rxResource` so it reloads on client change and after a reveal is confirmed; render loading skeleton, error state (problem title, detail and `code` in muted monospace, "Try again"), the table, and above it for a disabled client the warning "`<id>` is disabled. Its keys are refused until it is enabled again." (depends on T060, T064)

**Checkpoint**: the operator can see which keys are in use before revoking any.

---

## Phase 7: User Story 5 - Revoke a key after confirming which one (Priority: P2)

**Goal**: revoke from the list through a confirmation naming the key and the client.

**Independent Test**: revoke with confirmation → Revoked, and within a minute calls with it are
refused; cancel → nothing changes (quickstart §5).

### Tests for User Story 5

- [X] T066 [P] [US5] Spec `web/src/app/features/console/api-keys/ui/revoke-key-dialog.spec.ts`: title "Revoke `<name>` (`pdf_<keyId>_…`) for `<id>`?", body "Calls with this key stop working within a minute. This cannot be undone.", focus starts on "Cancel"; "Cancel" emits nothing; "Revoke key" emits `confirmed`; page spec: on `204` the row shows Revoked with no action; on `404 KEY_NOT_FOUND` or `503` the problem is shown and the list reloads

### Implementation for User Story 5

- [X] T067 [US5] Create `web/src/app/features/console/api-keys/ui/revoke-key-dialog.ts` on `web/src/app/shared/ui/alert-dialog.ts` with the exact wording above and a destructive "Revoke key" button, disabled while the request runs
- [X] T068 [US5] Wire `revoke` in `web/src/app/features/console/api-keys/feature/api-keys.page.ts`: open the dialog for the row's key, call `ApiKeysApi.revoke`, reload the list on success or failure, show the problem on failure (depends on T065, T067)

**Checkpoint**: a leaked key can be revoked from the console in a few clicks (SC-006).

---

## Phase 8: User Story 6 - Register a client and disable or re-enable it (Priority: P3)

**Goal**: a clients page with add, disable (confirmed) and enable, so a fresh deployment is usable
from the console alone.

**Independent Test**: on an empty database, sign in → "Add a client to get started."; add one → it is
chosen and a key can be created; disable → marked and its keys refused within a minute; enable →
they work again (quickstart §3 step 1, §6).

### Tests for User Story 6

- [X] T069 [P] [US6] Spec `web/src/app/features/console/clients/ui/add-client-dialog.spec.ts`: the id hint text; ids `Ecommerce`, `1shop`, `a_b` and a 41-character id refused as typed; empty name and a 101-character name refused; `409 CLIENT_ID_TAKEN` shows "A client with this id already exists." on the Id field; on `201` emits the created client
- [X] T070 [P] [US6] Spec `web/src/app/features/console/clients/feature/clients.page.spec.ts`: table columns; "Disable client" opens the confirmation "Disable `<name>` (`<id>`)?" with "Its keys stop working within a minute, until you enable it again. Nothing is deleted."; "Enable client" calls `update(id, { disabled: false })` with no confirmation; after a change `ClientContext.reload()` runs; selecting a name chooses the client and navigates to `/keys`; the shell shows "Add a client to get started. Each product that calls this service is a client." when the list is empty

### Implementation for User Story 6

- [X] T071 [P] [US6] Create `web/src/app/features/console/clients/ui/add-client-dialog.ts`: Id validated against "`^[a-z][a-z0-9-]{0,39}$`" with the hint "Lowercase letters, digits and hyphens, starting with a letter. Cannot be changed later.", Name required "1–100 characters", "Add client", button disabled while the request runs
- [X] T072 [P] [US6] Create `web/src/app/features/console/clients/ui/clients-table.ts` (Name as a button, Id monospace, State badge Active / Disabled with `lock`, Created, row action "Disable client" or "Enable client") and `web/src/app/features/console/clients/ui/disable-client-dialog.ts` on `alert-dialog` with the exact wording from T070
- [X] T073 [US6] Create `web/src/app/features/console/clients/feature/clients.page.ts` (+ `web/src/app/features/console/clients/clients.routes.ts`, no `needsClient`): header "Clients", "Each product that calls this service is a client. Keys belong to a client.", action "Add client"; uses `ClientsApi` and `ClientContext` (choose the new client on `201`, `reload()` after every change); add `{ path: 'clients', … }` to the shell children and "Clients" to the shell nav in `web/src/app/app.routes.ts` and `shell.html` (depends on T071, T072)
- [X] T074 [US6] In `web/src/app/core/layout/shell/choose-client.ts` show "Add a client to get started. Each product that calls this service is a client." with "Add client" (navigates to `/clients` and opens the add dialog through a `?add=1` query parameter the clients page reads) when `ClientContext.clients()` is empty; add "Add client" as the last item of the switcher menu (depends on T046, T073)

**Checkpoint**: a fresh deployment needs no hand-made management call before the console is useful.

---

## Phase 9: User Story 7 - A session that ends mid-task sends the operator back without losing work silently (Priority: P3)

**Goal**: when the session ends (time, sign-out elsewhere, key rotation), the next action leads to
sign-in after the unsaved-changes warning, and sign-in returns to the same page and client.

**Independent Test**: edit a template, sign out in another tab, save in the first → unsaved-changes
prompt, then sign-in; afterwards the same template and client (quickstart §9).

### Tests for User Story 7

- [X] T075 [P] [US7] Spec `web/src/app/core/auth/session-expiry.spec.ts` (fake timers): with `expiresAt` 10 minutes ahead, the console navigates to `/sign-in?returnUrl=<current url with ?client=>` at that time and not before; signing in again cancels the old timer
- [X] T076 [P] [US7] Spec `web/src/app/features/templates/feature/template-editor/template-editor.page.spec.ts` (extend or create): with unsaved edits, a `401` on save triggers navigation to sign-in that the `unsavedChangesGuard` interrupts with its prompt; choosing to stay keeps the edits on screen; after signing in, `returnUrl` brings back the same template and `?client=`

### Implementation for User Story 7

- [X] T077 [US7] In `web/src/app/core/auth/session.service.ts` schedule a timer at `expiresAt` (cleared on sign-out, re-armed on refresh) that calls `markSignedOut()` and navigates to `/sign-in?returnUrl=` + the current URL; make the interceptor (T033) and the timer use one shared `redirectToSignIn()` helper in `web/src/app/core/auth/redirect-to-sign-in.ts` so both preserve `?client=` (depends on T033)
- [X] T078 [US7] Check `web/src/app/features/templates/feature/template-editor/template-editor.page.ts` and `web/src/app/features/pdf-jobs/feature/new-job/new-job.page.ts`: a failed request with `401` must not clear the form or the editor content before navigation, so an interrupted navigation leaves the work in place; fix any reset-on-error found (FR-012)

**Checkpoint**: all seven stories work on their own and together.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: configuration, deployment, documentation (Principle I gate and definition of done), and
the full validation run.

- [X] T079 [P] Set `Blocks__ForwardedHeaders__KnownNetworks: 172.16.0.0/12` on the `api` service in `compose.yaml`, with a comment that nginx in `web` reaches the API over the Compose network; add a commented example to `deploy/compose.prod.yaml` (contracts/configuration.md)
- [X] T080 [P] Create `samples/console.http`: sign in (`POST /api/console/session` with `X-Console: 1` and the local admin key from `compose.yaml`), `GET` the session, a session-authenticated admin call with `X-Console: 1`, the same call without the header (403), a template list with `X-Client-Id`, sign out; link it from `samples/README.md`
- [X] T081 [P] Update `docs/configuration.md` and the configuration table in `docs/blocks/04-backend.md` with `Blocks:Console:SessionLifetime` (default `12:00:00`, `00:05:00`–`24:00:00`) and `Blocks:ForwardedHeaders:KnownNetworks` (default empty) (Principle I gate)
- [X] T082 [P] Update `docs/blocks/04-backend.md` "Authentication": replace "Built so far" with what is built now (the `Blocks` policy scheme with one key handler and no separate `AdminKey` scheme; `ConsoleSession` cookie validated against `console_sessions`; `X-Console`; `X-Client-Id` honoured for admins with format check only; `GET /api/admin/scopes`; sign-in limit per instance; codes `CONSOLE_HEADER_REQUIRED` and `RATE_LIMITED`); note that admin-list paging moves to 010
- [X] T083 [P] Update `docs/blocks/05-frontend.md` with what is built: header shell (sidebar deferred to 010), `ClientsApi` in `shared/data-access`, scopes from the API instead of `BLOCK_CONFIG`, the reveal dialog's close-event rule
- [X] T084 [P] Update `docs/operations.md`: signing in; rotating the admin key ends every session (steps from contracts/configuration.md); the expected "No XML encryptor configured" start-up warning and why; setting `KnownNetworks` behind a proxy and what happens without it; serving over HTTPS on one site, never framed; `RATE_LIMITED` on sign-in
- [X] T085 [P] Update `docs/architecture.md` (the `Blocks` policy scheme and session rows in the request flow) and `deploy/README.md` (forwarded headers, HTTPS and same-site requirement for the console)
- [X] T086 [P] Update `README.md` (the console signs in with the admin key; remove "the console needs 004") and `web/README.md` (remove the "Refused by the API until console sign-in ships" banner; describe sign-in, the client switcher, `core/auth`, `core/client-context`, `core/http/console.interceptor.ts`, `features/console/*`, and the same-site/HTTPS rule)
- [X] T087 [P] Add a `CHANGELOG.md` service entry: console sign-in with the admin key (12-hour sessions); new `POST/GET/DELETE /api/console/session`, `GET /api/admin/scopes`, optional `X-Client-Id` for admins, codes `CONSOLE_HEADER_REQUIRED` and `RATE_LIMITED`; new settings; ships together with 002 (milestone M1); no message-contracts version change
- [X] T088 Run `dotnet build PdfBuilder.slnx`, `dotnet test PdfBuilder.slnx` and, with `PDFBUILDER_RUN_DATABASE_TESTS=1`, the database-backed tests; run `npm run lint`, `npm test -- --watch=false` and `npm run build` in `web/`; all green with no new suppressions (Principle IX gate)
- [ ] T089 Run `docker compose up --build` from a clean checkout and walk [quickstart.md](quickstart.md) §1–§10, recording any deviation in this file; restore the committed admin key hash after §7

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks every story. T020 must be green before any story starts.
- **US1 (Phase 3)**: after Foundational. Everything in the console depends on it, because no screen can load data without a session.
- **US2 (Phase 4)**: after US1 (needs the shell, interceptor and guard).
- **US3 (Phase 5)**: after US2 (keys belong to the chosen client). The backend tasks T049, T052–T054 need only Foundational and can start alongside US1.
- **US4 (Phase 6)**: after US3 (extends the keys page).
- **US5 (Phase 7)**: after US4 (acts on table rows).
- **US6 (Phase 8)**: after US2; independent of US3–US5.
- **US7 (Phase 9)**: after US1; T076/T078 touch template and job pages, so run after US2's T048.
- **Polish (Phase 10)**: after the stories being shipped. 002 and 004 ship together, so every story is in scope for the release.

### Within Each User Story

- Tests first; they should fail before the implementation.
- Backend: options and records → store → handler/endpoint → `Program.cs` wiring.
- Console: data-access → ui → feature page → routes.

### Parallel Opportunities

- Setup: T002, T003, T004 together after T001.
- Foundational: T005–T008 and T010 in parallel; T017–T019 in parallel once T016 is done; T021–T022 (console primitives) in parallel with all backend work.
- US1: T023–T026 in parallel; T027 with T030 and T031 (backend and console in different trees).
- US2: T037–T039 in parallel; T042 alongside T040–T041.
- US3: T049–T051 in parallel; T052, T055, T056, T057 in parallel.
- US6 can run in parallel with US3–US5 once US2 is done.
- Polish: T079–T087 all in parallel.

---

## Parallel Example: User Story 1

```bash
# Tests together:
Task: "SignInThrottle tests in tests/Blocks.ServiceDefaults.Tests/SignInThrottleTests.cs"
Task: "Console session tests in tests/Blocks.ServiceDefaults.Tests/ConsoleSessionTests.cs"
Task: "Multi-host session tests in tests/PdfBuilder.Api.Tests/ConsoleSessionApiTests.cs"
Task: "Pipeline and leak test updates in tests/PdfBuilder.Api.Tests/"

# Backend and console in parallel:
Task: "SignInThrottle in src/Blocks.ServiceDefaults/Sessions/SignInThrottle.cs"
Task: "SessionService in web/src/app/core/auth/session.service.ts"
Task: "safeReturnUrl in web/src/app/core/auth/safe-return-url.ts"
```

## Parallel Example: User Story 3

```bash
Task: "PermissionDescriptor record and overload in src/Blocks.ServiceDefaults/"
Task: "dismissible input on web/src/app/shared/ui/dialog.ts"
Task: "copy button in web/src/app/shared/ui/copy-button.ts"
Task: "ApiKeysApi and models in web/src/app/features/console/api-keys/data-access/"
```

---

## Implementation Strategy

### MVP First (User Story 1, then 2 and 3)

1. Phases 1–2: foundation; confirm 002 behaviour is unchanged (T020).
2. Phase 3 (US1): the console is usable again with the admin key. **Stop and validate** with quickstart §1, §2, §7, §8.
3. Phases 4–5 (US2, US3): the three P1 stories together are the smallest release that meets the
   backlog entry: sign in, choose a client, issue a key.

### Incremental Delivery

1. US1 → the console works and is closed to everyone else.
2. US2 → the console works within a named client.
3. US3 → keys can be issued without hand-made calls.
4. US4 + US5 → keys can be inspected and revoked.
5. US6 → a fresh deployment is usable from the console alone.
6. US7 → session ends never lose work silently.
7. Polish → documentation, configuration, the full quickstart run. 002 is not released before this point.

---

## Notes

- [P] tasks touch different files and depend on nothing incomplete.
- Commit after each task or logical group; stop at any checkpoint to validate the story on its own.
- When running the API with `dotnet run`, use `ASPNETCORE_ENVIRONMENT=Production` with explicit
  settings: the Development profile loads user secrets that point at the real broker.
