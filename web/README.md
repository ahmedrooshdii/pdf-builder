# PDF Builder — web app

An Angular 22 front end for the service: pick a template, paste or load a JSON payload, submit, and
watch the job until the PDF is ready to preview and download. Jobs you submit are remembered in the
browser, since the API has no "list jobs" endpoint and forgets jobs after 24 hours.

## Running it

Start the API first (see the [root README](../README.md#running-it)), then:

```bash
cd web
npm install
npm start            # http://localhost:4200
```

`ng serve` proxies `/api` and `/files` to `http://localhost:5052` ([proxy.conf.json](proxy.conf.json)).
The browser only ever talks to one origin, which is why the API needs no CORS configuration. Change
the target there if your API runs elsewhere.

With Docker, `docker compose up --build` from the repository root serves the app on
http://localhost:4200 through nginx, which does the same proxying.

| Command | What it does |
| --- | --- |
| `npm start` | Dev server with the API proxy. |
| `npm test -- --watch=false` | Unit tests (Vitest). Runs in a few seconds; no API needed. |
| `npm run lint` | ESLint, including the layering rules below. |
| `npm run build` | Production build into `dist/`. |

## Architecture

```
src/app/
  core/                     app-wide plumbing, no feature knowledge
    http/                   API_BASE_URL token, ProblemDetails normalisation
    storage/                BROWSER_STORAGE token (null-safe localStorage)
    layout/                 logo, title strategy, not-found page
    routing/                unsaved-changes guard
    theme/                  light/dark service and toggle
  shared/                   reusable, feature-agnostic building blocks
    data-access/            APIs more than one feature needs (templates)
    ui/  pipes/  util/
  features/
    pdf-jobs/               New PDF, Jobs table, job detail
      pdf-jobs.routes.ts    the feature's only public entry point, lazy-loaded
      data-access/          HTTP client, polling, history store, models
      feature/              routed pages: wire data-access to ui
      ui/                   presentational components: inputs in, outputs out
    templates/              template list and editor (drafts, publish, live preview)
```

**The layering is enforced by ESLint, not just described here** ([eslint.config.js](eslint.config.js)):

- `core` and `shared` may not import from any feature.
- `ui` components may not use `HttpClient`, services or stores — only models from `data-access`.
- `data-access` may not import from `ui` or `feature`.
- One feature may not import from another. Code two features need moves to `shared`.

### Adding a feature

1. Create `src/app/features/<name>/` with the same `data-access/`, `feature/`, `ui/` split.
2. Export its routes as the default export of `<name>.routes.ts`.
3. Add one line to [app.routes.ts](src/app/app.routes.ts):
   `{ path: '<name>', loadChildren: () => import('./features/<name>/<name>.routes') }`.

It is lazy-loaded, so it adds nothing to the initial bundle.

### UI components

[src/app/shared/ui/](src/app/shared/ui/) holds the design system: Angular ports of shadcn/ui's
`base-nova` style (Button, Badge, Input, Table, Checkbox, Switch, Code Block, Trace, Hover Card,
Progress, Skeleton, Scroll Area, Dialog), with the same Tailwind class strings as the shadcn
registry. They live in the repository on purpose, as shadcn intends, so change them freely.

- Tokens are in [styles.css](src/styles.css); shadcn's own utilities (`shimmer`, `scroll-fade-*`, the
  `data-*` variants) are vendored in [styles/shadcn.css](src/styles/shadcn.css).
- Every primitive takes `class` and merges it with [`cn()`](src/app/shared/ui/cn.ts), so a caller's
  `class="px-2"` replaces the built-in padding instead of fighting it.
- Icons are `<app-icon name="…">` over Lucide. Only the icons listed in
  [icon.ts](src/app/shared/ui/icon.ts) are bundled; an unknown name is a build error.
- The JSON editor is CodeMirror 6, loaded on demand into its own chunk, so no other page pays for
  it. It is a Signal Forms custom control: `<app-code-editor [formField]="form.data" />`.
- Dark mode is the `dark` class on `<html>`, owned by `ThemeService`. `public/theme-init.js` applies
  it before Angular boots, so there is no white flash.

### Choices worth knowing

- **Zoneless, signals throughout, OnPush by default** — Angular 22 defaults, kept as they are.
- **Signal Forms** (`@angular/forms/signals`, stable since v22) for the job form. Client validation
  mirrors `GeneratePdfCommandValidator`; a 400 from the API is mapped back onto the field it is about.
- **`rxResource` for polling.** Changing the route's `:jobId` cancels the previous poll automatically.
  The back-off lives in [job-poller.ts](src/app/features/pdf-jobs/data-access/job-poller.ts) and is
  injectable, which is how the tests run it with no delay.
- **Route params as inputs** (`withComponentInputBinding`), so pages never touch `ActivatedRoute`.
- **Download URLs are built from `fileId`, not taken from `fileUrl`.** The API's `fileUrl` is absolute
  to `Storage:PublicBaseUrl`, which is a different origin from the app in development. The id is
  checked against the 32-hex format first.
- **The preview fetches the PDF as a blob.** The API serves files as `attachment`, so an iframe
  pointed at the download URL would download rather than display. The blob is fetched only when
  the preview opens, and its object URL is revoked when it closes.
- **Models mirror the .NET contracts by hand** ([pdf-job.models.ts](src/app/features/pdf-jobs/data-access/pdf-job.models.ts)).
  With more endpoints, generating them from `/openapi/v1.json` would be the next step.

## Deploying

[Dockerfile](Dockerfile) builds a static bundle served by unprivileged nginx on port 8080, which also
proxies `/api/` and `/files/` to `API_UPSTREAM` (default `http://api:8080`). Build it from the
repository root: `docker build -f web/Dockerfile .`

CI lints, tests and builds the app on every push. It is **not deployed to the Oracle VM yet**: that
needs an arm64 image for it and Caddy pointed at the web container instead of the API.
