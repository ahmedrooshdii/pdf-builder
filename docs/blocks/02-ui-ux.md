# 2. UI/UX

Every block has a console, and every console should feel like the same product. The design
language is the one PDF Builder's web app already uses: Angular ports of **shadcn/ui's `base-nova`
style on the neutral base colour**, Tailwind 4, Lucide icons, light and dark themes. This document
turns what that app does today into rules the next consoles follow.

The console's only user is the operator. It is a working tool: dense, fast, keyboard-friendly, and
honest about state. It is not a marketing surface.

## Design tokens

Tokens are CSS variables in `web/src/styles.css`, defined once for `:root` and once for `.dark`.
Components reference them only through Tailwind utilities (`bg-primary`, `text-muted-foreground`,
`border-input`), so **a re-theme is a change to that file alone**.

| Token | Light | Dark | Used for |
| --- | --- | --- | --- |
| `--background` / `--foreground` | `oklch(1 0 0)` / `oklch(0.145 0 0)` | `oklch(0.145 0 0)` / `oklch(0.985 0 0)` | Page |
| `--card`, `--popover` | white | `oklch(0.205 0 0)` | Raised surfaces |
| `--primary` | `oklch(0.205 0 0)` | `oklch(0.922 0 0)` | Main action, active indicators |
| `--secondary`, `--muted`, `--accent` | `oklch(0.97 0 0)` | `oklch(0.269 0 0)` | Quiet fills, hovers |
| `--muted-foreground` | `oklch(0.556 0 0)` | `oklch(0.708 0 0)` | Secondary text |
| `--destructive` | `oklch(0.577 0.245 27.3)` | `oklch(0.704 0.191 22.2)` | Failure, delete |
| `--success` | `oklch(0.527 0.154 150)` | `oklch(0.723 0.192 149.6)` | Succeeded |
| `--warning` | `oklch(0.555 0.163 49)` | `oklch(0.828 0.189 84.4)` | Modified, expiring |
| `--border`, `--input`, `--ring` | greys | white at 10–15% | Lines, fields, focus |
| `--radius` | `0.625rem` | same | `rounded-sm…xl` derive from it |
| `--code-*` | GitHub-light-like | GitHub-dark-like | JSON editor and code blocks |

Fonts are the system stacks (`ui-sans-serif, system-ui, …` and `ui-monospace, 'Cascadia Code', …`).
No web fonts: the console loads instantly and looks native on each OS.

### Type scale and rhythm

Five sizes, nothing in between. In-between sizes (12.8px for small buttons and hints) are what made
the first mockups look uneven.

| Size | Use |
| --- | --- |
| 24px semibold | Page titles |
| 16px semibold | Section and dialog titles |
| 14px | Body, table cells, controls |
| 13px | Hints, table headers, monospace, small buttons |
| 12px | Badges and tags |

Every data row in every table is at least **44px** tall, whatever it contains, so a row with a badge
and a row with a button line up. Table headers are 13px, muted, on a faint fill, so the data leads
and the header steps back.

Dates: relative under a day ("3 minutes ago"); otherwise `3 Oct 2026` in lists and
`3 Oct 2026, 14:02` where the time matters. One format everywhere.

### Badges are state, tags are data

| | Badge | Tag |
| --- | --- | --- |
| Means | A state that changes: Active, Failing, Revoked | A value: a scope, an event, a key |
| Shape | Pill, tinted fill, icon + word | 6px radius, outlined, monospace, no icon |
| Example | `● Active` | `pdf:generate` |

Using badges for both made `pdf:generate` read like a status.

A list that has state gives it **its own Status column**, in the same position on every page (second,
after the name). The state never moves into the actions column.

### One palette for all blocks

Blocks **do not** get their own accent colour. Colour in the console carries meaning (success,
warning, failure), and a per-block accent would compete with it. A block is identified by its
**name and logo** in the header, nothing else. If a block ever needs a brand colour, it goes in the
logo.

### Branding a dedicated deployment

A company's deployment shows the company's product name and logo instead of the block's, from
configuration (`Blocks:Branding:*`, see [06-deployment.md](06-deployment.md#what-a-deployment-configures)).
The same rule applies: name and logo only, tokens unchanged. Designs must therefore work with:

- a name of 3 to 30 characters, in Latin or Arabic script, in the header and in page titles
  (`"New PDF · ACME Documents"`);
- a logo of any aspect ratio between 1:1 and 4:1, fitted to `h-7` in the header and the sign-in card;
- a small "Powered by PDF Builder 1.4.0" in the sign-in footer, so support always knows the version.

### Themes

- Light and dark, as a `dark` class on `<html>` that Tailwind's `dark:` variant keys off.
- First visit follows the OS; the toggle's choice is remembered per browser.
- `public/theme-init.js` applies the theme before Angular boots, so there is no white flash. Every
  console copies this file and uses its own storage key (`<block>.theme`).

## Layout

shadcn **Sidebar** in its `inset` variant: the sidebar sits on the `--sidebar` surface and the page
is a rounded, raised panel beside it.

```
┌──────────────┬──────────────────────────────────────────────────────────────────────────┐
│ [▦] PDF      │ [◧] │ E-commerce › API keys                [⌕ Search or jump…  Ctrl K] [☾] │  topbar, 52px
│     Builder  ├──────────────────────────────────────────────────────────────────────────┤
│ ┌──────────┐ │                                                                          │
│ │E-commerce│ │  API keys                                             [+ New key  N]     │  page header
│ │ecommerce⇕│ │  One line saying what this page is for.                                  │
│ └──────────┘ │                                                                          │
│ PDF          │  ┌────────────────────────────────────────────────────────────────────┐  │
│ ▣ Overview 4 │  │ table, full width                                                  │  │  24px padding,
│ ≡ Jobs       │  │                                                                    │  │  no max-width
│ ▤ Templates  │  └────────────────────────────────────────────────────────────────────┘  │
│ Platform     │                                                                          │
│ ⚿ API keys   │                                                                          │
│ ⚲ Webhooks 1 │                                                                          │
│ ☺ Clients  3 │                                                                          │
│              │                                                                          │
│ [OP] Operator│                                                                          │
└──────────────┴──────────────────────────────────────────────────────────────────────────┘
   256px; 52px collapsed (Ctrl+B)
```

- **Sidebar header:** the logo and block name, with "Console · v1.4.0" under it, linking to Overview.
- **Client switcher** directly under it, shaped like shadcn's team switcher: client name, its id in
  monospace, and a menu with every client plus "All clients" (`Ctrl+0`…`Ctrl+9`).
- **Nav in two `SidebarGroup`s with labels**: the block's own work first (*PDF*: Overview, Jobs,
  Templates), then the studs every block shares (*Platform*: API keys, Webhooks, Clients). The
  second group is identical in every console, so the operator learns it once. A `SidebarMenuBadge`
  carries counts: destructive tint for things that need attention (Overview 4, Webhooks 1), muted
  for plain counts (Clients 3). Hovering an item shows its shortcut in place of the badge.
- **Sidebar footer:** the operator, with when the session ends; its menu has theme, keyboard
  shortcuts and sign out.
- **Collapsible to icons** with `Ctrl+B` or the topbar trigger, for wide tables.
- **Topbar:** sidebar trigger, then a **breadcrumb** of client and page (`E-commerce › API keys`),
  then the search trigger (`Ctrl+K`) and the theme toggle. The breadcrumb is what says which client
  a page is about; it is never woven into a sentence.
- **Content is full width**, 24px padding, no `max-width`. Tables and splits use the whole screen.
- **Page header:** title, one sentence of purpose, and at most one primary action on the right,
  showing its shortcut (`N`). Secondary actions are `outline` or `ghost`.
- **Narrow screens:** below `md` the sidebar becomes a sheet opened from the trigger, and splits
  stack. The console is usable on a phone for checking a job or revoking a key, not for authoring
  templates.

### List and detail (Resizable)

Lists whose rows have a lot to show (Jobs now; webhook deliveries next) open the selected row in a
**detail panel beside the list** instead of a new page, using shadcn **Resizable** with a visible
handle:

- Default split 62 / 38; the handle drags between 40 % and 75 % for the list, double-click resets,
  and the width is remembered per browser.
- The selected row is tinted with a 2px primary bar on its left edge. `J`/`K` move the selection and
  the panel follows; `Esc` closes the panel and the list takes the full width.
- The panel's header has the item's name and status, "open full page" (the existing detail route,
  for sharing a link) and close. Its body leads with the actions (Download, Preview, Copy file link),
  then facts, then the trace, then the payload.

### Pagination

Every list longer than one page ends with the shadcn **Pagination** bar: the range and total
(`1–50 of 1,643`), "Rows per page" (25, 50, 100), and Previous / numbered pages with an ellipsis /
Next, the current page outlined. The page and page size live in the URL like filters. The API side
of this is in [04-backend.md](04-backend.md#lists).

### Keyboard

Shortcuts are shown with shadcn **Kbd**, wherever the action they trigger is visible: on primary
buttons (`N`), in the search trigger (`Ctrl K`), inside the filter field (`/`), on hover in the
sidebar (`G then J`), and in the command menu.

| Keys | Does |
| --- | --- |
| `Ctrl K` | Command menu: search templates and jobs, run actions, jump to pages, switch client |
| `?` | Keyboard shortcuts dialog |
| `Ctrl B` | Collapse or expand the sidebar |
| `Ctrl Shift L` | Toggle theme |
| `Ctrl 0`, `Ctrl 1`–`9` | All clients, or the nth client |
| `G` then `O` / `J` / `T` / `K` / `W` / `C` | Go to Overview, Jobs, Templates, API keys, Webhooks, Clients |
| `N` | The page's primary "new" action |
| `/` | Focus the page's filter |
| `J` / `K`, `↵`, `Esc` | Next / previous row, open it, close the panel or dialog |

Single-key shortcuts never fire while focus is in a text field. Every shortcut has a visible
control that does the same thing, so none is the only way to do anything.

### Logo

The mark is drawn in `--primary` / `--primary-foreground` so it inverts with the theme, and must
read at 16px. The chosen mark is **A · Page and block**: a page with a folded corner and a solid
block docked at its foot, with no tilts or dashed strokes. It is in `web/public/logo.svg` and the
`Logo` component; the candidates it was chosen from are in
[mockups/logo-options.html](mockups/logo-options.html). Other blocks keep the docked block and swap
the page for their own glyph (an envelope for Email, a bell for Notifications).

## The client switcher

Most console data belongs to one client, so the current client is always visible and always
explicit.

- A team-switcher-style menu at the top of the sidebar listing every client, plus "All clients"
  where a page supports it (Overview, Jobs).
- The choice is part of the URL (`?client=ecommerce`), so a link to a job or template opens on the
  right client. It is also remembered per browser as the default.
- Pages that create things (templates, keys, webhooks) require a single client and say which one in
  the page header: *"New key for **ecommerce**"*. Creating something under the wrong client is the
  mistake this UI most needs to prevent.

## Screens

Phase 1 screens per block. Every block has the screens marked *stud*; the rest are the block's own.
Static mockups of every screen and state, built from the real tokens, are in
[mockups/](mockups/index.html): open `index.html` in a browser.

| Screen | Kind | Purpose |
| --- | --- | --- |
| Overview | stud | Where the console opens: what needs attention, and per-client activity. |
| Sign in | stud | Paste the admin key once (with show/hide); get a session. |
| Clients | stud | List, add, disable clients. |
| API keys | stud | Create (shown once), name, scope, revoke; see last use. |
| Webhooks | stud | Endpoints per client, recent deliveries, redeliver. |
| Jobs | PDF | Server-side list, filter by client/status/template, detail with trace. |
| New job | PDF | Pick template, edit JSON, page setup, submit, watch. (Exists.) |
| Templates | PDF | List, editor with live preview, drafts and publish. (Exists, becomes per client.) |

### Overview

The operator's first question is "is anything broken?", and without this page answering it means
visiting Jobs, Webhooks and API keys in turn.

- **Needs attention:** one list, most severe first, each item saying what is wrong, for which
  client, what it implies ("all `TEMPLATE_RENDER_FAILED`, so retrying will not help"), and a button
  to where it is fixed. Sources: failing webhook endpoints, failed jobs in 24 hours, keys expiring
  within 14 days, templates with unpublished changes older than a day.
- **All clear** is a designed state, not an empty list.
- **Clients:** a table of the last 24 hours per client (jobs, failures, p95 render time, last
  request, webhook health). A table rather than stat tiles, so it works with two clients or twenty.

### Sign in

```
            ┌──────────────────────────────────────┐
            │ [logo]  PDF Builder console          │
            │                                      │
            │ Admin key                            │
            │ ┌──────────────────────────────────┐ │
            │ │ ••••••••••••••••••••••••••••     │ │
            │ └──────────────────────────────────┘ │
            │ The key from this block's            │
            │ configuration (Blocks__Admin__…).     │
            │                                      │
            │                        [ Sign in ]   │
            └──────────────────────────────────────┘
```

A wrong key says "That key was not accepted." and nothing more: no hint about which part was wrong.

### API keys

```
API keys                                                          [+ New key]
Keys a client's backend uses to call this block. A key is shown once, when it is created.

┌───────────────┬───────────────┬────────────────┬──────────────┬───────────┬─────┐
│ Name          │ Key           │ Scopes         │ Last used    │ Created   │     │
├───────────────┼───────────────┼────────────────┼──────────────┼───────────┼─────┤
│ production    │ pdf_k7f2a9c1… │ pdf:generate   │ 3 minutes ago│ 12 Oct    │ [⋯] │
│ staging       │ pdf_m2b8e4d0… │ pdf:generate   │ never        │ 1 Nov     │ [⋯] │
│ old-prod      │ pdf_x1c5a7f3… │ pdf:generate   │ 2 months ago │ 3 Jun     │ Revoked
└───────────────┴───────────────┴────────────────┴──────────────┴───────────┴─────┘
```

Creating a key opens a dialog that shows the full key with a copy button and the warning
*"Copy it now. It cannot be shown again."* The dialog's only exit is a button labelled
"I have copied it". Revoking asks for confirmation and names the key and the client.

### Jobs

The existing table, now fed by the API instead of browser storage. Columns: status badge, template,
client (when "All clients"), enqueued, duration, size. Filters: status, template, date range. Rows
open the existing detail page with its trace.

## Component inventory

All in `web/src/app/shared/ui/`, same class strings as the shadcn registry. A new console starts
with a copy of this folder (see [05-frontend.md](05-frontend.md#shared-ui)).

| Have | Add in phase 1 | Notes |
| --- | --- | --- |
| Button, ButtonGroup | Dropdown Menu | Row actions (`⋯`), client switcher |
| Badge | Alert | Inline page-level problems |
| Input, Select, Checkbox, Switch, Field errors | Textarea | Webhook payload viewers |
| Table | Pagination | Numbered pages, rows per page |
| | Sidebar, Breadcrumb | The shell (inset variant) |
| | Resizable | List and detail splits |
| | Kbd, Command | Shortcut hints, Ctrl+K menu |
| | Tag | Scopes, events, keys (not a shadcn component; see above) |
| Dialog | Alert Dialog | Revoke / delete confirmations |
| Tabs, Hover Card, Scroll Area | Tooltip | Truncated ids, timestamps |
| Skeleton, Spinner, Progress | Toast (Sonner-style) | "Key revoked", "Copied" |
| Code Block, Code Editor, Trace | Copy button | Keys, ids, URLs |
| Icon (Lucide, explicit list) | | Unknown icon names are build errors |

Add a component by porting the shadcn `base-nova` version, not by inventing one. Every primitive
takes `class` and merges it through `cn()`, so callers override without fighting.

## States

Every view that loads data designs all four of these, not just the first:

| State | Pattern |
| --- | --- |
| **Loading** | Skeletons shaped like the content. A spinner only for actions under a second. |
| **Empty** | Icon, one sentence on what would be here, and the action that creates it. "No keys yet for ecommerce. [Create a key]" |
| **Error** | Problem details from the API: `title` as the heading, `detail` below, and `code` in muted monospace for searching logs, plus the one useful action ("Try again"). Field errors from a 400 go on the fields. Status 0 says "The API could not be reached." |
| **Partial / stale** | Async work shows its state with the status badge and keeps polling with back-off; nothing pretends to be finished. |

### Status language

One mapping for every block's async work:

| Status | Badge variant | Icon |
| --- | --- | --- |
| Pending / Processing | `secondary` | `loader` (spinning) |
| Succeeded / Delivered | `success` | `circle-check` |
| Failed | `destructive` | `circle-x` |
| Draft | `secondary` | `circle-dot` |
| Modified (draft ahead of published) | `warning` | `circle-dot` |
| Revoked / Disabled | `outline`, muted text | `lock` |

## Writing

- Sentence case everywhere: "New key", not "New Key".
- Buttons say what happens: "Revoke key", "Publish template". Never "OK" or "Submit".
- Say what went wrong and what to do: "This template has unpublished changes. Publish it to use them
  in jobs."
- Ids, keys and URLs are monospace, truncated in the middle with a tooltip and a copy button.
- Times are relative under a day ("3 minutes ago") with the absolute UTC time in a tooltip.

## Accessibility

- Every interactive element is reachable and operable by keyboard, with the `ring` focus style
  visible.
- Status badges carry `role="status"`; polling updates are announced politely, not on every tick.
- Colour never carries meaning alone: every status has an icon and a word.
- Dialogs trap focus and return it to the control that opened them.
- Contrast meets WCAG AA in both themes. Check new tokens before adding them.

## Language and direction

The console is in English and left-to-right. Documents a block produces (Arabic invoices, Arabic
emails) are right-to-left, and that is the template's job, not the console's. The template editor
and previews must therefore show RTL content correctly without the console itself becoming RTL.
