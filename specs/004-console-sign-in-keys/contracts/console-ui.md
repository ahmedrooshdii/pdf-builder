# Contract: Console Routes, Screens and Behaviour

What the console promises its operator. Tests in `web/` assert these; wording in quotes is exact.
Visual rules (tokens, badges, states, writing) are those of `docs/blocks/02-ui-ux.md`.

## Routes

| Path | Guard | Needs a chosen client | Page |
| --- | --- | --- | --- |
| `/sign-in?returnUrl=` | none | no | Sign in (outside the shell) |
| `/` | `signedIn` | — | Redirects to `/jobs/new`, as today |
| `/jobs/new`, `/jobs`, `/jobs/:jobId` | `signedIn` | yes | Existing job screens |
| `/templates`, `/templates/:key` | `signedIn` | yes | Existing template screens |
| `/keys` | `signedIn` | yes | API keys of the chosen client |
| `/clients` | `signedIn` | no | Clients |
| `**` | none | no | Not found, as today |

Every path under the shell keeps `?client=<id>` when navigating between pages. `signedIn` waits for
the first `GET /api/console/session` before deciding; it never redirects while the state is unknown.

## Shell (every signed-in page)

Left to right in the header: logo; **client switcher**; nav (New PDF, Jobs, Templates, API keys,
Clients); theme toggle; **session menu**.

- **Client switcher** shows the chosen client's name and its id in monospace, with a `lock` icon and
  "Disabled" when it is disabled. Its menu lists every client (name, id, disabled marker) and ends
  with "Add client", which opens the add-client dialog. With no client chosen it reads
  "Choose a client".
- **Session menu** shows "Signed in as operator", "Session ends at HH:mm" (local time, exact UTC in a
  tooltip) and "Sign out".
- Below `md`, the nav folds into a menu button; the switcher and session menu stay in the header.
- With no chosen client, pages that need one show, in place of their content: "Choose the client to
  work on." with the client list as buttons; with no clients at all: "Add a client to get started.
  Each product that calls this service is a client." and "Add client".
- An unknown `?client=` shows: "No client `<id>` exists. Choose one." with the same list.

## Sign in

- One field, "Admin key", `type=password`, `autocomplete=off`, with a show/hide toggle
  (`aria-pressed`), and the hint "The key from this service's configuration (Blocks__Admin__KeyHash
  is its hash)." Button "Sign in", disabled while submitting.
- After every attempt the field is emptied.
- `401` → "That key was not accepted." `429` → "Too many attempts. Try again in a minute."
  `503` → "The service cannot reach its storage. Try again in a few seconds." Status 0 →
  "The API could not be reached."
- On success, navigates to `returnUrl` if it starts with `/` and not `//` or `/\`, else to `/`.
- Opening `/sign-in` while signed in goes straight to `returnUrl` (or `/`).

## API keys (`/keys`)

Page header: "API keys", "Keys a client's backend uses to call this service. A key is shown once,
when it is created.", primary action "New key". It stays enabled for a disabled client, because
002 allows issuing keys for one (they work once it is enabled).

- Disabled client: a warning above the table: "`<id>` is disabled. Its keys are refused until it is
  enabled again."
- **Table** columns: Name; Key (`pdf_k7f2a9c1_…`, monospace, copy button for the public id); Scopes
  (tags); Last used ("never", relative under a day, date beyond; exact UTC in a tooltip); Created;
  Expires ("never" or date); State (badge: Active, Expired, Revoked with `lock`); actions menu with
  "Revoke key" for Active keys only. Order as the API returns (newest first).
- **Empty**: "No keys yet for `<id>`." with "Create a key".
- **Create dialog**: title "New key for **`<id>`**"; Name (required, ≤ 100); Scopes (one checkbox
  per `GET /api/admin/scopes` item, with its description; at least one); Expires (optional
  date-time, shown back as "Stops working on 31 Dec 2026, 23:59 (UTC+02:00)"); "Create key". Field
  errors from a `400` go on their fields. The button is disabled while the request runs.
- **Reveal dialog** (opens on `201`, replaces the create dialog): title "Copy the key for
  `<id>`", the key in monospace, "Copy" button, warning "Copy it now. It cannot be shown again.",
  and one button "I have copied it". No close button; Escape and backdrop clicks do nothing; if the
  browser closes it, it reopens. While open, leaving the page asks for confirmation and closing the
  tab triggers the browser's leave-page prompt. On "I have copied it" the key is cleared and the list
  reloads.
- **Copy failure**: "Copy did not work. The key is selected: press Ctrl+C." and the key text is
  selected.
- **Revoke**: alert dialog "Revoke `<name>` (`pdf_<keyId>_…`) for `<id>`?", body "Calls with this
  key stop working within a minute. This cannot be undone.", buttons "Cancel" and "Revoke key"
  (destructive). On `204` the row shows Revoked; on `404 KEY_NOT_FOUND` or any error the problem is
  shown and the list reloads.

## Clients (`/clients`)

Page header: "Clients", "Each product that calls this service is a client. Keys belong to a client.",
primary action "Add client".

- **Table**: Name, Id (monospace), State (Active / Disabled with `lock`), Created; row action
  "Disable client" or "Enable client". Selecting a row's name chooses that client and opens `/keys`.
- **Add dialog**: Id (hint "Lowercase letters, digits and hyphens, starting with a letter. Cannot be
  changed later."; validated as typed against `^[a-z][a-z0-9-]{0,39}$`), Name (required, ≤ 100),
  "Add client". `409 CLIENT_ID_TAKEN` → on the Id field: "A client with this id already exists."
  On `201`, the new client is chosen.
- **Disable**: alert dialog "Disable `<name>` (`<id>`)?", "Its keys stop working within a minute,
  until you enable it again. Nothing is deleted.", "Cancel" / "Disable client". **Enable** needs no
  confirmation.

## Existing screens

- Job and template screens are unchanged except that every request carries `X-Client-Id` and pages
  that create show the client: New PDF page header "New PDF for `<id>`"; template list action
  "New template for `<id>`".
- The jobs list shows only jobs this browser submitted for the chosen client.

## Session end

- On a `401` from any request other than `/api/console/session`, or when `expiresAt` passes, the
  console navigates to `/sign-in?returnUrl=<current path and query>`. Unsaved-changes guards run;
  if the operator stays, the page remains but further requests lead to the same prompt.
- After signing in again, the operator lands on that URL, with the same `?client=`.

## Never

- The admin key or a created key in `localStorage`, `sessionStorage`, the URL, `console.*` output or
  an error report.
- A request without `X-Console: 1` to the API.
- A page showing a client's data while another client is named in the switcher.
