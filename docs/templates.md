# Writing templates

A template is an HTML file in the template directory, named `{templateKey}.html`. The id in the
request is the file name without the extension, so `invoice.html` is requested as `"invoice"`.

Templates use [Scriban](https://github.com/scriban/scriban). The payload object is the root scope,
so `{"customer":{"name":"Acme"}}` is read as `{{ customer.name }}`.

`templates/invoice.html` is a worked example; `templates/invoice.sample.json` is a payload that
fills it.

## The two rules that trip everyone up

**1. Assets must be inline.** The renderer blocks every network request whose host is not on
`Playwright:AllowedHosts`, which is empty by default. An external stylesheet, font or image is not
slow — it never arrives, and the document renders without it.

```html
<!-- blocked -->
<img src="https://cdn.example.com/logo.png" alt="">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter">

<!-- fine -->
<img src="data:image/png;base64,iVBORw0KGgo…" alt="">
<style> /* inline CSS */ </style>
```

To allow a specific host, add it to `Playwright:AllowedHosts` — and read the warning in
[configuration.md](configuration.md#playwright) first.

**2. JavaScript does not run.** `Playwright:EnableJavaScript` is off because payload data is
untrusted and Scriban does not auto-escape HTML. Charts, client-side layout and anything that waits
on `DOMContentLoaded` will not work. Render charts server-side into an inline SVG or a data URI.

## Page setup

Put a sibling `{templateKey}.options.json` next to the template. Every field is optional:

```json
{
  "format": "A4",
  "landscape": false,
  "printBackground": true,
  "preferCssPageSize": true,
  "margins": { "top": "10mm", "right": "10mm", "bottom": "10mm", "left": "10mm" }
}
```

| Field | Default | Notes |
| --- | --- | --- |
| `format` | `A4` | Any name the browser knows: `A4`, `A3`, `Letter`, `Legal`, `Tabloid`. |
| `landscape` | `false` | |
| `printBackground` | `true` | When false, background colours and images are dropped. |
| `preferCssPageSize` | `true` | A CSS `@page { size: … }` rule wins over `format`. |
| `margins` | `10mm` each | CSS lengths: `mm`, `cm`, `in`, `px`. |

With `preferCssPageSize` left on, the usual pattern is to set margins to `0mm` in the sidecar and
control everything from `@page` in the template, so page setup lives in one place:

```css
@page { size: A4; margin: 18mm 16mm; }
```

A malformed sidecar is logged and ignored rather than failing the template.

## Print CSS that matters

```css
/* Keep background colours and images in the output. */
body { -webkit-print-color-adjust: exact; print-color-adjust: exact; }

/* Repeat table headers on every page of a long table. */
thead { display: table-header-group; }

/* Never split a row, a figure or a signature block across a page break. */
tr, figure, .signature { page-break-inside: avoid; }

/* Force a break. */
.new-section { page-break-before: always; }
```

Fonts come from the container image. `Dockerfile` installs Liberation, DejaVu and the Noto families
including CJK and colour emoji. A script outside those falls back to missing-glyph boxes — add the
font package to the image rather than linking a web font.

## Scriban reference for this service

```scriban
{{ customer.name }}                       {{# field }}
{{ invoice.total | math.format "n2" }}    {{# 2 decimal places }}
{{ customer.name | string.upcase }}
{{ invoice.issued_on | date.to_string "%Y-%m-%d" }}

{{ if invoice.notes }} … {{ end }}
{{ if items.size > 0 }} … {{ else }} … {{ end }}

{{ for item in items }}
  {{ for.index }}  {{ item.description }}
{{ else }}
  No items.
{{ end }}
```

Numbers survive the trip intact: integers stay integers, decimals stay decimal. A money value is
never pushed through a binary float, so `1234567890.12` renders exactly, and a large id does not
come out as `9.007199254741E+15`.

Culture is fixed to invariant, so output does not change with the container's locale. Use
`math.format` and `date.to_string` for anything the reader will see.

### Limits

A template that misbehaves fails its job rather than taking a worker down:

| Limit | Value |
| --- | --- |
| Loop iterations | 10,000 |
| Recursion depth | 100 |
| Rendered output | 8 MB, and it throws rather than truncating |
| Regex evaluation | 1 second |
| Payload size | 5 MB |

Output overflow throws on purpose. Scriban's default is to truncate, which would mean quietly
shipping a half-finished invoice.

## Missing fields

By default a field the payload does not contain renders as nothing — `{{ missing }}`,
`{{ missing.field }}` and `{{ a.b.c.d }}` all produce an empty string. Optional sections stay
readable, and one absent field cannot take down a document that is otherwise complete.

The cost is that a typo looks exactly like a deliberate blank. Set `TemplateEngine:StrictMode` to
`true` and any unknown field fails the job, naming the field and its position:

The failure arrives as `TEMPLATE_RENDER_FAILED`, and the message carries the template coordinates
Scriban reports, in the form `<input>(line,column) : error : …`.

Run staging and your template tests strict, and production lenient. That catches typos before they
ship without letting one missing optional field fail a batch.

## Two kinds of template

| | Authored | Built-in |
| --- | --- | --- |
| Written in | the web app (Templates), or `/api/templates` | `templates/` in this repository |
| Stored in | `TemplateStore:AuthoredRootPath`, one `{templateKey}.json` each, on the data volume | `TemplateStore:RootPath`, replaced by CI on every deploy |
| CSS | its own stylesheet, injected before `</head>` when printed | inside the HTML |
| Changed by | saving a draft, then publishing it | a commit |

Keys are unique across both, ignoring letter case. A built-in template is read-only in the web app;
**Duplicate** copies it into an authored draft you can edit.

### Drafts and publishing

An authored template always has a draft, which is what you edit, and gains a published version the
first time you publish. **Jobs only ever render the published version**: a template that has never
been published is reported to jobs as `TEMPLATE_NOT_FOUND`, and editing a published template
changes nothing for jobs until you publish again. The list shows this as *Draft*, *Published* or
*Unpublished changes*.

Publishing first renders the draft with its own sample data, and refuses with the engine's message
(and the line at fault) if that fails, so a broken template is never published. The editor's live
preview runs the same render as you type, without saving or starting a browser; page size and page
breaks only show in a real job.

Every save and publish carries the revision it was based on. If someone else saved in between, the
change is refused with `409 TEMPLATE_REVISION_CONFLICT` rather than overwriting their work.

```
GET    /api/templates                      list, both kinds
GET    /api/templates/{key}                draft and published content
POST   /api/templates                      { "templateKey": "letter" }  -> new draft from an example
PUT    /api/templates/{key}/draft          { "revision": 3, "content": { "html", "css", "sampleJson", "options" } }
POST   /api/templates/{key}/publish        { "revision": 4 }   -> 422 if it does not render
POST   /api/templates/{key}/duplicate      { "templateKey": "letter-v2" }
DELETE /api/templates/{key}                authored only
POST   /api/templates/preview              { "content": { … } }  -> text/html
```

## Adding a built-in template

1. Drop `{templateKey}.html` into `templates/` (and a `.options.json` if the page setup is not A4
   portrait, and a `.sample.json` for previews and "Load sample").
2. Under `docker compose`, `templates/` is mounted read-only into the container, so the next render
   picks up the change — no rebuild, no restart. Outside Docker, the file is copied to the output
   directory on build.
3. The file is re-read on every request and only the parsed form is cached, keyed by content hash,
   so an edit takes effect immediately and reparses exactly once. Note that outside Docker the file
   being read is the copy in the build output, so rebuild after editing `templates/`.

Template keys must match `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`. The key becomes a file name, so that
character set is also what stops a request reading outside the template directory.
