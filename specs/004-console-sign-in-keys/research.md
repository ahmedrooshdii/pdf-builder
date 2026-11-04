# Research: Console Sign-in and Key Management

Decisions behind [plan.md](plan.md). Each entry: what was chosen, why, what else was considered.
"Today" means the code on `002-clients-and-api-keys` at commit `735e60d`.

## R1. How the session is carried: an ASP.NET cookie, validated against a row

- **Decision**: ASP.NET Core cookie authentication, scheme `ConsoleSession`, cookie
  `blocks.pdf.console` (`blocks.{blockCode}.console`). The ticket holds one claim, `scope=admin`,
  plus `sid`, a 256-bit random session id. Each sign-in writes a `console_sessions` row keyed by
  `SHA-256(sid)`. On every request `OnValidatePrincipal` looks the row up and rejects the principal
  when the row is missing, revoked, past its `expires_at`, or made with another admin key (R3).
  Cookie: `HttpOnly`, `Secure` (always), `SameSite=Strict`, `Path=/`, persistent with
  `Expires` = sign-in + lifetime, `SlidingExpiration = false`. The cookie is protected by Data
  Protection with keys in the database (R2).
- **Rationale**: the spec needs three things a plain cookie cannot give: sign-out that defeats a
  copied cookie (FR-005), ending every session when the admin key changes (FR-006), and the same
  session on every instance and across restarts (FR-004). A server-side row gives the first two;
  shared Data Protection keys give the third. The row stores only a hash of `sid`, so someone who can
  read the database (including the Data Protection keys, R2) still cannot produce a cookie that
  matches a live row. Console traffic is one operator's clicks, so one primary-key read per request
  costs nothing worth caching, and not caching is what makes sign-out immediate on every instance.
- **Alternatives considered**:
  - *Cookie only, no row* (the design in `docs/blocks/04-backend.md#console-session` taken
    literally): a copied cookie works until it expires; signing out only deletes the browser's copy.
    Fails FR-005.
  - *`ITicketStore` (whole ticket server-side)*: same guarantees with more machinery; the ticket is
    two claims, so storing it gains nothing.
  - *A bearer token in browser storage*: readable by any injected script, fails FR-007.
  - *Caching rows for 60 s like keys*: would let a signed-out session work for a minute on other
    instances, for a saving that does not matter at console rates.

## R2. Data Protection keys in the block's database

- **Decision**: a small `IXmlRepository` (`EfXmlRepository<TContext>`) in
  `Blocks.ServiceDefaults.EntityFramework` over a `data_protection_keys` table mapped by
  `AddBlocksConsoleSessions()` (names spelled out, like `AddBlocksClients()`), registered by
  `AddBlocksDataProtection<TContext>()` with application name `blocks.{blockCode}` read from
  `BlocksAuthOptions`, so every instance shares the key ring. Added in the same migration as
  `console_sessions`. The project takes the ASP.NET shared framework for the Data Protection API.
  *Changed during implementation:* the plan named
  `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.12, but that package brings its own
  `Microsoft.AspNetCore.DataProtection` and with it `System.Security.Cryptography.Xml`/`Pkcs` 10.0.12,
  which have published advisories (NU1902/NU1903, errors under warnings-as-errors); 10.0.14 fixes
  them but needs every EF and Extensions package moved to 10.0.14 at once. The repository is ~40
  lines and avoids the second copy of Data Protection entirely.
- **Rationale**: the default key ring lives in the container's home directory, so a restart or a
  second instance could not read the cookie (FR-004). The database is the one store every instance of
  a block already shares (Principle II), and the design already names this table.
- **Alternatives considered**: a mounted key directory (one more volume every deployment must share
  between instances); Redis (a new dependency). Encrypting the key ring with an X.509 certificate is
  possible but needs a certificate in every deployment's configuration; see Complexity Tracking.
  The `No XML encryptor configured` warning ASP.NET logs at start-up is expected and is documented in
  `docs/operations.md`.

## R3. Ending sessions when the admin key changes

- **Decision**: each `console_sessions` row stores `admin_key_fingerprint`, the first 16 bytes of
  `SHA-256(Blocks:Admin:KeyHash)`. Validation compares it with the fingerprint of the configured hash
  (constant time). After the operator replaces the admin key and restarts, every older row fails the
  check and its request answers `401 AUTH_REQUIRED`.
- **Rationale**: replacing a leaked admin key must also evict whoever signed in with it (FR-006).
  The fingerprint is derived from a value that is already a hash, so the row reveals nothing usable.
- **Alternatives considered**: revoking all rows at start-up when the hash differs from a stored
  one (needs a "previous hash" record and a start-up write, and a race between instances starting
  with different configurations); rotating the Data Protection key ring (kills every protected payload,
  not only sessions).

## R4. Choosing the scheme: a `Blocks` policy scheme

- **Decision**: the default scheme becomes a policy scheme `Blocks`. Its selector forwards to
  `ApiKey` when the request has `X-Api-Key` **or has no console cookie**, and to `ConsoleSession`
  only when the cookie is present and there is no `X-Api-Key`. The fallback policy and every permission
  policy name `Blocks` instead of `ApiKey`. `ApiKeyAuthenticationHandler` is not changed; it still
  serves both key kinds by prefix.
- **Rationale**: a request with no credential still reaches the key handler's challenge, so it keeps
  answering `401 AUTH_REQUIRED` with `WWW-Authenticate: ApiKey` exactly as 002's pipeline tests expect
  (product callers unchanged, FR-010). A key always wins over a cookie, so a script holding the admin
  key behaves as in 002 even in a browser that is signed in.
- **Deviation from the design doc**: `docs/blocks/04-backend.md` names a separate `AdminKey` scheme.
  Splitting the handler gives nothing the prefix check does not already do; the doc is updated to what
  is built.
- **Cookie scheme refusals**: `OnRedirectToLogin` and `OnRedirectToAccessDenied` are replaced so the
  API never redirects: a missing, expired, revoked or foreign-key session answers
  `401 AUTH_REQUIRED` (and clears the cookie); a session lacking a permission cannot happen (it holds
  `admin`), but would answer `403 AUTH_SCOPE_MISSING`.

## R5. Cross-site protection: `SameSite=Strict` plus `X-Console: 1`

- **Decision**: a small middleware in `Blocks.ServiceDefaults`, after authentication: a request
  authenticated by `ConsoleSession` whose method is not `GET`, `HEAD` or `OPTIONS` must carry
  `X-Console: 1`, or it is refused `403 CONSOLE_HEADER_REQUIRED` before reaching the endpoint. The
  sign-in and sign-out endpoints require the header themselves (they run before a session exists).
- **Rationale**: a cross-site form or image cannot set a custom header, and a cross-origin `fetch`
  with one triggers a CORS preflight the API never approves (it has no CORS policy). `SameSite=Strict`
  already keeps the cookie off cross-site requests; the header also covers same-site siblings (another
  app on a subdomain) and old browsers. This is the design in `04-backend.md`. Requiring it on
  sign-in prevents login CSRF (signing the operator's browser into an attacker's session is not
  possible anyway without the admin key, but the rule stays uniform).
- **Where the check lives**: it is the authentication stud's own rule about its own scheme, so it
  lives in `Blocks.ServiceDefaults` next to the handlers; feature code still never sees the scheme
  (Principle VI).
- **Alternatives considered**: antiforgery tokens (`IAntiforgery`): a second cookie and a token
  endpoint for the same protection, designed for server-rendered forms.

## R6. Limiting failed sign-ins

- **Decision**: `SignInThrottle`, an in-memory fixed window per remote address: 5 failures per
  minute. `POST /api/console/session` checks it **before** comparing the key; over the limit it answers
  `429 RATE_LIMITED` with `Retry-After` (seconds left in the window) without looking at the body.
  Only failures count; success does not reset or consume. The address is
  `HttpContext.Connection.RemoteIpAddress` after `UseForwardedHeaders` (R7).
- **Rationale**: the spec wants failures limited and successes untouched (FR-008), which is a counter
  of outcomes, not a rate of requests; ASP.NET's rate limiter counts requests before the outcome is
  known. The key is 238 random bits, so the limit is about log noise and scanners, not about making
  guessing infeasible; a per-instance window is therefore enough, and with N instances the effective
  limit is 5·N a minute, which is documented.
- **Alternatives considered**: `Microsoft.AspNetCore.RateLimiting` with a fixed window (counts every
  attempt, including the operator's successful one); a database-backed counter (a write per failure,
  and an outage would then block sign-in for a second reason).

## R7. The client's address behind the console's proxy

- **Decision**: `UseForwardedHeaders` (`X-Forwarded-For`, `X-Forwarded-Proto`) trusting only the
  networks in a new setting `Blocks:ForwardedHeaders:KnownNetworks` (comma-separated CIDRs; default
  empty, meaning ASP.NET's default of loopback only). `compose.yaml` sets it to the Compose network
  range; `deploy/README.md` explains setting it to the reverse proxy's address.
- **Rationale**: the console reaches the API through nginx (`web/nginx/`), so without this every
  sign-in failure would come from the proxy's address and one person's typos would lock out
  everyone for a minute. Trusting forwarded headers from anyone would let a guesser pick a new address
  per attempt. The default stays safe: unconfigured, the limit is shared, never bypassed.
- **Alternatives considered**: trusting all private ranges by default (anyone on the internal network
  could then rotate addresses).

## R8. Acting as a client: `X-Client-Id`

- **Decision**: `HttpCurrentClient.ClientId` returns the `client_id` claim for a client key and, for
  an admin principal (admin key or session), the value of `X-Client-Id` when it is present; for
  everyone else the header is ignored. A malformed value (not `^[a-z][a-z0-9-]{0,39}$`, or sent twice)
  from an admin is refused `400 VALIDATION_FAILED` by a middleware before the endpoint. Existence of
  the client is not checked here. The log scope adds `ClientId` for an admin acting as a client.
- **Rationale**: Principle VI and `04-backend.md#acting-as-a-client` define this, and the console
  sends the header from now on (FR-018). Implementing the server side now means feature 003 only adds
  query filters. Nothing in this release reads `ClientId` for data yet, so checking existence (a
  database read on every admin request) would protect nothing until 003, which owns that rule and the
  404 it implies.
- **Alternatives considered**: an `IClaimsTransformation` that adds a `client_id` claim (runs on
  every `AuthenticateAsync`, including the policy evaluations, and must guard against adding twice);
  deferring the header entirely to 003 (the log scope would not show which client the console worked
  on, and 003 would have to touch authentication again).

## R9. The permission list the create form shows

- **Decision**: `GET /api/admin/scopes` (admin) returns `{ "items": [ { "name", "description" } ] }`.
  `AddBlocksAuthentication` gains an overload taking `BlocksPermission(Name, Description)` records;
  `PdfPermissions.All` carries the descriptions that are already its XML comments. The string overload
  stays and uses the name as the description.
- **Rationale**: FR-021 wants "exactly the permissions the service offers"; asking the service means
  the console cannot drift from it, and a future block's console gets its own list for free.
- **Alternatives considered**: compiling the list into the console's `BLOCK_CONFIG`, as
  `05-frontend.md` sketches (two places to change when a permission is added).

## R10. Sign-in endpoints and their answers

- **Decision**: as designed in `04-backend.md#console-session`, made precise in
  [contracts/console-session.md](contracts/console-session.md):
  `POST /api/console/session` → `204` + cookie; `GET` → `200 { expiresAt }`; `DELETE` → `204`, cookie
  cleared, row revoked. A wrong key, a client key, an empty or over-long value all answer
  `401 AUTH_INVALID_KEY`, the 002 code and wording, so the console needs one message. Storage
  unreachable answers `503 STORAGE_UNAVAILABLE` (FR-009); the admin key is checked first so an outage
  never turns a wrong key into "try again". Expired rows older than a day are deleted on each sign-in,
  so the table stays small without a background job.
- **Alternatives considered**: returning the session body from `POST` (saves one request; kept as
  designed so the console has one path that reads the session).

## R11. Session lifetime is a setting

- **Decision**: `Blocks:Console:SessionLifetime`, default `12:00:00`, allowed `00:05:00` to
  `24:00:00`, validated at start-up with a message naming the key.
- **Rationale**: the spec fixes the default ("a working day"); Principle I says a value a company could
  want different is configuration. Absolute, never sliding, so a forgotten session ends by itself.

## R12. The console's shell: keep the header, add the switcher and session menu

- **Decision**: the existing top header (`web/src/app/app.html`) becomes `core/layout/shell` and gains,
  left to right: logo, the **client switcher** (name and monospace id, disabled marker, menu of every
  client, "Add client"), the nav (New PDF, Jobs, Templates, API keys, Clients), theme toggle, and the
  **session menu** (signed in, "Session ends 21:04", Sign out). Below `md` the nav folds into a menu
  button; the switcher stays visible. The sign-in page renders outside the shell. The sidebar layout of
  `docs/blocks/02-ui-ux.md#layout` (inset sidebar, badges, breadcrumb, `Ctrl+K`) is left for the
  overview and server-side jobs work (feature 010), where its extra space is first needed.
- **Rationale**: FR-014 needs the client in the same place on every page; a header does that. Porting
  shadcn's Sidebar (with its mobile sheet and collapse) is a project of its own and would double this
  feature's frontend work for no requirement in the spec.
- **Alternatives considered**: the full sidebar now (the target design, but no requirement here needs
  it); the client only in each page header (not "the same place on every page" for the job screens).

## R13. Client context in the browser

- **Decision**: `core/client-context/ClientContext`, as sketched in `05-frontend.md#client-context`:
  `clients` (an `rxResource` over `GET /api/admin/clients`), `current` derived from the router's
  `?client=` query parameter, `select()` that updates the URL and remembers the choice under
  `pdf-builder.client` (through the null-safe `BROWSER_STORAGE`). Resolution order: URL, then
  remembered, then the only client, then none. An unknown id in the URL leaves `current` null and sets
  `unknownClientId`, which the shell shows ("No client `foo` exists. Choose one."). Routes under the
  shell other than `/clients` render a "choose a client" state while `current` is null (FR-016).
  The `ClientsApi` wrapper lives in `shared/data-access/` because `core` and the clients feature both
  use it, and ESLint forbids `core` → features.
- **Rationale**: the URL as source of truth is what makes copied links open on the right client
  (FR-015); deriving `current` from the router means back/forward also switch client.

## R14. HTTP interceptor and session service

- **Decision**: `core/auth/SessionService` (`state`: `unknown | signed-in | signed-out`, `expiresAt`,
  `signIn`, `signOut`, `refresh`), a `signedIn` `canMatch` guard that waits for the first refresh, and
  one functional interceptor in `core/http/`:
  1. adds `X-Console: 1` to every request to the API origin;
  2. adds `X-Client-Id` from `ClientContext.current()` to requests under `/api/` except
     `/api/admin/` and `/api/console/`;
  3. on `401` from anything but `/api/console/session`, marks the session signed out and navigates to
     `/sign-in?returnUrl=<current url>`; unsaved-changes guards run as for any navigation (FR-012).
  A timer also signs out at `expiresAt`. `returnUrl` is accepted only when it starts with `/` and not
  `//` or `/\`, so the sign-in page cannot be used as an open redirect.
- **Rationale**: one place sets the CSRF header and the client for every feature, so features change
  only where they show the client.

## R15. The reveal dialog that cannot be closed by accident

- **Decision**: `shared/ui/dialog` gains `dismissible = input(true)`. When false: no close button,
  backdrop clicks ignored, `cancel` events `preventDefault()`ed, and if the browser closes the
  `<dialog>` anyway, it is re-opened at once. Chrome's close-watcher rules allow a second Escape to
  close a modal whose `cancel` was prevented without user activation in between, so re-opening on
  `close` is required, not optional. The keys page registers the existing unsaved-changes guard and a
  `beforeunload` handler while the reveal is open. The key is held in one signal of the reveal
  component, set to `null` on "I have copied it"; the HTTP response object is not retained. Copy uses
  `navigator.clipboard.writeText`; on failure the key text is selected and the dialog says
  "Copy did not work. The key is selected: press Ctrl+C."
- **Rationale**: FR-022 and FR-023; the browser detail is the kind that breaks the guarantee silently.
- **Alternatives considered**: a separate page for the reveal (a URL invites reloading and back-button
  loss; the key would be one navigation away from gone).

## R16. Local job history per client

- **Decision**: `PdfJob` entries in `JobHistoryStore` gain `clientId`; the store records
  `ClientContext.current()` when a job is submitted, and `jobs` is filtered to the current client.
  The storage key stays `pdf-builder.job-history.v1`; entries without `clientId` (written before this
  feature) are kept but shown for no client, and age out under the existing 50-entry cap.
- **Rationale**: FR-029 with no data migration; old entries refer to jobs submitted before keys
  existed and belong to no client.

## R17. Paging of the admin lists stays deferred

- **Decision**: the client and key lists keep the `items`-only envelope from 002. The console shows
  them whole, sorted as the API returns them.
- **Rationale**: 002 deferred paging "to the console lists of 004". With tens of clients and keys per
  deployment both lists fit on one screen, and the first console list that needs page paging is the
  server-side jobs list (010), which is where the shared paging is now planned. Adding the members
  later is additive. Carried in Complexity Tracking.

## R18. Serving requirements: same site, HTTPS or localhost

- **Decision**: documented, not coded: the console and API must be served to the browser from the same
  site (true for `ng serve`, Compose and any reverse proxy that serves both), over HTTPS, or over HTTP
  only on `localhost` (browsers accept `Secure` cookies there). A console embedded in another site's
  frame (for example a Hugging Face Space page) does not receive the `SameSite=Strict` cookie; the
  Space serves only the API today, so nothing changes there.
- **Rationale**: `Secure` and `SameSite=Strict` are the protection; weakening them for a plain-HTTP
  LAN address is not worth a setting.
