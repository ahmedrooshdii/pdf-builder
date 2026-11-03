# 5. Frontend

The console is the Angular app in `web/`, described in [../../web/README.md](../../web/README.md).
Its foundations stay as they are; this document covers what changes to make it a block console, and
how the next block's console reuses it.

## What stays

| Choice | Why it stays |
| --- | --- |
| Angular 22, zoneless, signals, OnPush | Current defaults; nothing here needs otherwise. |
| Signal Forms, client validation mirroring the API | Field errors from a 400 still map onto fields. |
| `rxResource` for loading and polling | Cancels on route change, back-off injectable for tests. |
| Tailwind 4 + shadcn `base-nova` ports in `shared/ui` | The design system in [02-ui-ux.md](02-ui-ux.md). |
| `core` / `shared` / `features` with ESLint-enforced layering | The same rules apply to every new folder below. |
| One origin for app and API (dev proxy, nginx in production) | It is what makes cookie sessions simple and CORS unnecessary. |
| Vitest, no API needed for unit tests | |

## Structure after phase 1

```
src/app/
  core/
    auth/                   NEW  session service, auth guard, 401 handling
    client-context/         NEW  current client: URL ?client= ⇄ signal ⇄ storage
    http/                        + console interceptor (X-Console, X-Client-Id)
    block/                  NEW  BLOCK_CONFIG token: name, code, logo, scopes, nav (+ runtime branding)
    layout/                      shell → sidebar (inset), topbar, breadcrumb; replaces the top header
    shortcuts/              NEW  ShortcutService: one keydown listener, G-sequences, ignores text fields
    command/                NEW  Ctrl+K command menu; blocks register commands through BLOCK_CONFIG
    routing/  storage/  theme/
  shared/
    ui/                          + sidebar, resizable, pagination, kbd, command, breadcrumb,
                                   dropdown-menu, alert, alert-dialog, toast, tooltip, tag, copy-button
    data-access/  pipes/  util/
  features/
    console/                NEW  the studs every block's console has
      auth/                      sign-in page
      clients/                   list, add, disable, limits
      api-keys/                  list, create (show once), revoke
      webhooks/                  endpoints, deliveries, redeliver
    pdf-jobs/                    jobs list moves to the server; history store removed
    templates/                   scoped to the current client
```

`features/console/` is written as if it were already a library: it imports only from `core` and
`shared`, and reads block-specific facts (name, key prefix, scopes) from `BLOCK_CONFIG`. That is
what makes [extracting it](#shared-ui) a move rather than a rewrite. An ESLint rule enforces that it
imports nothing from `features/pdf-jobs` or `features/templates`.

## Authentication

### Session

```ts
@Injectable({ providedIn: 'root' })
export class SessionService {
  readonly state = signal<'unknown' | 'signed-in' | 'signed-out'>('unknown');
  signIn(adminKey: string): Observable<void>;   // POST /api/console/session
  signOut(): Observable<void>;                  // DELETE /api/console/session
  refresh(): Observable<void>;                  // GET /api/console/session, once at start
}
```

- The admin key is sent once and never stored in the browser. The session lives in an `HttpOnly`
  cookie the app cannot read, which is the point: a script injected into the page cannot steal it.
- The sign-in form clears the key field after submit, success or failure.

### Routing

```ts
export const routes: Routes = [
  { path: 'sign-in', loadComponent: () => import('./features/console/auth/sign-in.page') },
  {
    path: '',
    canMatch: [signedIn],          // waits for SessionService.refresh() the first time
    loadComponent: () => import('./core/layout/shell'),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      { path: 'overview', loadChildren: () => import('./features/console/overview/overview.routes') },
      { path: 'jobs', loadChildren: () => import('./features/pdf-jobs/pdf-jobs.routes') },
      { path: 'templates', loadChildren: () => import('./features/templates/templates.routes') },
      { path: 'keys', loadChildren: () => import('./features/console/api-keys/api-keys.routes') },
      { path: 'webhooks', loadChildren: () => import('./features/console/webhooks/webhooks.routes') },
      { path: 'clients', loadChildren: () => import('./features/console/clients/clients.routes') },
    ],
  },
  { path: '**', … },
];
```

The guard redirects to `/sign-in?returnUrl=…`; signing in returns there.

### Interceptor

One functional interceptor in `core/http/`:

1. Adds `X-Console: 1` to every request to the API (the CSRF header the backend requires on
   cookie-authenticated writes).
2. Adds `X-Client-Id` from `ClientContext` when a client is selected.
3. On a 401 from anything but the session endpoints, marks the session signed out and navigates to
   sign-in with the current URL as `returnUrl`. Unsaved-changes guards still run.

## Client context

```ts
@Injectable({ providedIn: 'root' })
export class ClientContext {
  readonly clients = rxResource(…);                 // GET /api/admin/clients
  readonly current = signal<string | null>(null);   // null = "All clients"
  select(clientId: string | null): void;            // updates URL ?client= and storage
}
```

- **The URL is the source of truth.** A shared link opens on the client it was copied from. Storage
  (`<block>.client`) only supplies the default when the URL has none.
- Changing client re-runs every `rxResource` that reads `current()`, so pages refresh without
  extra code.
- Pages that create things read `current()` and refuse to render their form without a single
  client, with an empty state that asks the operator to pick one.

## Runtime branding

The same console image serves every deployment, so nothing deployment-specific may be compiled in.
Today the product name is hardcoded in three places (`app.html`, `APP_NAME` in
`page-title.strategy.ts`, the `Logo` component); all three move to runtime.

- At start, before the first route resolves, the app calls `GET /api/console/info` (anonymous):
  `{ "productName": "ACME Documents", "logoUrl": "/api/console/logo", "version": "1.4.0" }`.
- `BLOCK_CONFIG` holds the compiled defaults (block name and logo); the info response overrides the
  name and logo when present. Header, page titles and the sign-in card read from one signal.
- If the call fails, the defaults are used; the console never blocks on branding.
- The logo is an `<img>` (it may be a PNG), with the product name as its `alt` text.

Configuration lives in the API only, so a deployment sets branding in one place and the console
image needs no environment variables or rebuild.

## Changes to existing features

### PDF jobs

- `GET /api/pdf/jobs` replaces `JobHistoryStore`. Jobs submitted elsewhere (by products) are now
  visible, which was the store's whole limitation. Delete the store and its storage key rather than
  keeping both.
- Page-based paging (`?page=&pageSize=`, see [04 Lists](04-backend.md#lists)) feeding the shadcn
  Pagination bar; `page` and `pageSize` live in the URL. The cursor mode is for products, not the
  console.
- The list and the selected job sit in a Resizable split ([02](02-ui-ux.md#list-and-detail-resizable)).
  The selected job id is in the URL (`/jobs?job=…`), so the panel survives a reload and can be
  linked; `/jobs/:jobId` stays as the full-page view.
- Filters (status, template, date range) live in query parameters, like the client.
- Downloads and previews use `GET /api/pdf/jobs/{id}/file` instead of building `/files/{fileId}`.
  The `FILE_ID_PATTERN` check and the reasoning about `Storage:PublicBaseUrl` in the current README
  go away with it; update the README in the same change.
- A job whose file has expired shows that instead of a download button.

### Templates

- Scoped by the interceptor's `X-Client-Id`; no code in the feature changes beyond showing the
  current client in the page header and handling "pick a client first".
- Built-in templates appear for every client, marked read-only, as today.
- Template packs: "Export" on the list (select templates → download a `.pack.json`) and "Import"
  (upload → preview of what will be created or replaced → confirm). Replacing an existing template
  is opt-in per template in the preview, never the default.

## New features

| Feature | Data access | Screens and notable behaviour |
| --- | --- | --- |
| `console/overview` | `OverviewApi` | Needs-attention list and per-client activity table ([02](02-ui-ux.md#overview)). Each block contributes its own attention items through `BLOCK_CONFIG`; the stud ones (webhooks, keys) are built in. |
| `console/auth` | `SessionService` | Sign-in page with show/hide on the key field. Generic error text; no hint which part was wrong. |
| `console/clients` | `ClientsApi` | Table, add dialog (id is a slug, validated like the API), disable with confirmation, per-client limit overrides. |
| `console/api-keys` | `ApiKeysApi` | Table with last used. Create dialog → **reveal dialog** showing the key once with a copy button; the dialog cannot be dismissed by Escape or backdrop click, only by "I have copied it". The key is dropped from memory when it closes. Revoke via alert dialog naming key and client. |
| `console/webhooks` | `WebhooksApi` | Endpoints table; create reveals the signing secret once, same pattern. Deliveries table per endpoint with status, attempts, last response code; payload in a code block; "Redeliver". |

Each follows the existing feature layout: `data-access/` (API wrapper, models), `feature/` (routed
pages), `ui/` (presentational components), and a `<name>.routes.ts` default export.

### Models

Still mirrored by hand from the .NET records, as today. With the stud endpoints the surface roughly
triples, so the planned next step becomes worthwhile in phase 1: generate types from
`/openapi/v1.json` in CI and fail the build when the checked-in types differ. Hand-written
`*.api.ts` wrappers stay; only the types are generated.

## Shared UI

When the second block starts, its console must not be a fork that drifts. Two kinds of shared code,
shared two different ways:

| What | How it is shared | Why |
| --- | --- | --- |
| `shared/ui` primitives, `styles.css` tokens, `theme-init.js` | **Copied** into each console from `blocks-platform/web/blocks-ui/`, shadcn-style. | They are meant to be edited per app, and copying is how shadcn intends them to be used. A token change is rolled out by re-copying one file. |
| `core/auth`, `core/client-context`, `core/http` interceptor, `features/console/*` | **Published** as an Angular library, `@blocks/console-kit` (ng-packagr, GitHub Packages). | They are behaviour and security. A fix to session handling must reach every console, not be re-applied by hand. |

A new block's console then starts as:

```ts
// app.config.ts
provideBlockConsole({
  name: 'Email',
  code: 'eml',
  logo: EmailLogo,
  scopes: ['email:send', 'email:templates:read', 'email:templates:write'],
}),
```

plus its own features, and gets sign-in, clients, keys, webhooks, the client switcher and the shell
for free.

### Phase 2: one console

When there are enough blocks that switching consoles is a chore (see
[01-business.md](01-business.md#phase-2-one-console-for-all-blocks-when-there-are-three-or-more-blocks)),
a shell app loads each block's features at runtime with Native Federation
(`@angular-architects/native-federation`). Because every console is already built from
`@blocks/console-kit` and lazy route files, each block exposes its `*.routes.ts` and the shell
composes them. Nothing in phase 1 needs to anticipate this beyond keeping features lazy and
self-contained, which the ESLint rules already enforce.

## Testing

Beyond what exists:

- Interceptor: adds both headers; 401 signs out and redirects with `returnUrl`; session endpoints
  are exempt.
- `ClientContext`: URL wins over storage; selecting updates both; storage failures are tolerated.
- Guard: waits for the first session refresh instead of redirecting on `unknown`.
- Key reveal dialog: the key is rendered once, copyable, and not dismissible by Escape.
- Jobs list: page and page size round-trip through the URL; selecting a row opens the panel and
  `J`/`K` move the selection.
- Shortcuts: single keys are ignored while a text field has focus; `G` then a letter navigates;
  `Ctrl+B` toggles the sidebar.
