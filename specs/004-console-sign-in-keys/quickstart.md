# Quickstart: validating console sign-in and key management

Proves the feature end to end. Behaviour is defined in [spec.md](spec.md); the HTTP shapes in
[contracts/console-session.md](contracts/console-session.md); the screens in
[contracts/console-ui.md](contracts/console-ui.md).

## Prerequisites

- Docker, .NET 10 SDK, Node (as in `web/README.md`).
- The local admin key printed in `compose.yaml`:
  `adm_lPQ25ykvuTW7HdOJLaJzfqnJsT4cKQfwwR5pgq4o` (local only; never use it elsewhere).

```bash
docker compose up --build          # console on http://localhost:4200, API on :8080
```

## 1. Nothing is open without a session (US1, FR-001, SC-001)

Open http://localhost:4200/templates?client=ecommerce in a private window.

- **Expect**: the sign-in page at `/sign-in?returnUrl=%2Ftemplates%3Fclient%3Decommerce`; the network
  tab shows no `/api/templates` or `/api/admin` call answered with data.

```bash
curl -i http://localhost:4200/api/admin/clients
```

- **Expect**: `401`, `"code":"AUTH_REQUIRED"`, `WWW-Authenticate: ApiKey`.

## 2. Wrong key, throttle, sign in (US1, FR-002, FR-003, FR-008)

1. Enter `adm_wrong`, then a client key (`pdf_aaaaaaaa_…`). **Expect** "That key was not accepted."
   both times and an empty field.
2. Fail five times within a minute, then enter the right key. **Expect** "Too many attempts. Try
   again in a minute." on the sixth; after the minute, the right key signs in.
3. Sign in with the local admin key. **Expect** to land on `/templates?client=ecommerce` (or the
   "No client `ecommerce` exists" state on a fresh database).
4. DevTools → Application → Cookies: `blocks.pdf.console` is `HttpOnly`, `Secure`,
   `SameSite=Strict`, expiring 12 hours from now. Local and session storage contain no `adm_` value.

## 3. Add a client, choose it, see it everywhere (US6, US2)

1. On a fresh database, **expect** "Add a client to get started." Add `ecommerce` / "E-commerce".
   **Expect** it chosen, its name and `ecommerce` shown in the header switcher.
2. Add `erp` / "ERP". Switch between them on Jobs, Templates and API keys. **Expect** the switcher and
   the address (`?client=`) to change, the page to reload its content, no full page reload.
3. Copy the address while on `erp`, open it in a new tab. **Expect** `erp` chosen.
4. Try `?client=nope`. **Expect** "No client `nope` exists. Choose one."
5. In the network tab, a template request carries `X-Client-Id: erp` and `X-Console: 1`.

## 4. Create a key, see it once (US3, FR-021–FR-023, SC-005)

1. On `/keys?client=ecommerce`, "New key": name `production`, scope `pdf:generate`, no expiry.
2. **Expect** the reveal dialog with the full `pdf_…` key and the warning. Press Escape twice and click
   the backdrop: it stays. Try to navigate to Jobs: a confirmation appears.
3. "Copy", then "I have copied it". **Expect** the list to show `production` with `pdf_<id>_…`, scope,
   "never" last used, Active.
4. Prove it works and is not kept:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:8080/api/pdf/jobs \
  -H "X-Api-Key: <copied key>" -H "Content-Type: application/json" \
  -d "{\"templateKey\":\"invoice\",\"data\":$(cat samples/minimal.json)}"      # 202
```

   Search the browser's local storage, session storage and history for the key: nothing.
   Reload `/keys`: the key appears only as its public id. Last used now reads "less than a minute ago"
   (within a minute).

## 5. Revoke with confirmation (US5, FR-024, SC-006)

1. Row action "Revoke key". **Expect** "Revoke `production` (`pdf_<id>_…`) for `ecommerce`?". Cancel:
   nothing changes.
2. Revoke. **Expect** the row Revoked with no actions. Within a minute the `curl` above answers `401
   AUTH_INVALID_KEY`.

## 6. Disable and enable a client (US6, FR-026)

Disable `ecommerce` from `/clients` (confirmation names it). **Expect** the switcher's lock marker and
the warning on `/keys`. Issue a fresh key for it and use it with the `curl` above: within a minute it
answers `403 CLIENT_DISABLED`. Enable the client: the same key works again.

## 7. Sign-out defeats a copied cookie; key rotation ends sessions (FR-005, FR-006, SC-004)

1. Copy the `blocks.pdf.console` cookie value. Sign out from the session menu. **Expect** the sign-in
   page.

```bash
curl -i http://localhost:4200/api/console/session -H "Cookie: blocks.pdf.console=<copied value>"
```

   **Expect** `401 AUTH_REQUIRED`.
2. Sign in again. Generate a new admin key (`dotnet run --project tools/Blocks.Cli -- admin-key`), put
   its hash in `compose.yaml`, `docker compose up -d api`. Click anything in the console. **Expect** the
   sign-in page; the old key is refused, the new one accepted.
3. Restore the committed hash afterwards.

## 8. Cross-site and header rules (FR-007, SC-010)

```bash
# A session-authenticated change without the console header
curl -i -X DELETE http://localhost:4200/api/admin/clients/ecommerce/keys/<keyId> \
  -H "Cookie: blocks.pdf.console=<live value>"
```

**Expect** `403 CONSOLE_HEADER_REQUIRED` and the key still Active. In the console, `document.cookie`
in DevTools does not contain `blocks.pdf.console`.

## 9. Session end mid-edit (US7, FR-012)

Open a template, change it without saving, sign out in another tab, then save in the first. **Expect**
the unsaved-changes prompt, then sign-in; after signing in, the same template and client.

## 10. Phone width (FR-031)

DevTools device mode, 375 px wide: sign in, switch client, open `/keys`, revoke a key, and complete a
create-and-reveal. **Expect** no horizontal scrolling and every action reachable by keyboard.

## Automated checks

```bash
dotnet build PdfBuilder.slnx                     # warnings are errors
dotnet test PdfBuilder.slnx                      # unit and architecture tests
# Database-backed and whole-pipeline tests (sessions, multi-instance, rotation):
PDFBUILDER_RUN_DATABASE_TESTS=1 dotnet test PdfBuilder.slnx
cd web && npm run lint && npm test -- --watch=false && npm run build
```

`PDFBUILDER_TEST_POSTGRES` points the database tests at an existing PostgreSQL instead of
Testcontainers. When running the API with `dotnet run` instead of Compose, use
`ASPNETCORE_ENVIRONMENT=Production` with explicit settings: the Development profile loads user secrets
that point at a real broker.
