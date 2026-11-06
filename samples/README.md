# Samples

Payloads and a helper script for trying the service by hand.

Everything here is a **payload** — the `data` field of a request, not a whole request body. The
script wraps it; in `requests.http` the wrapping is written out in full.

| File | What it exercises |
| --- | --- |
| `../templates/invoice.sample.json` | Every field the template reads. The reference payload. |
| `minimal.json` | Every optional field omitted — no tax id, no notes, no tax line. |
| `empty-items.json` | An empty `items` array, so the template's `else` branch renders. |
| `precision.json` | Money keeping its decimals, and a large value staying readable. |
| `multipage.json` | 28 line items, so the table spills onto a second page. |

## A key first

Every `/api` call needs an API key in the `X-Api-Key` header; health checks and file links do
not. `admin.http` registers a client and issues it a key with the admin key. Under
`docker compose up`, the admin key is the published, local-only one written next to its hash in
`compose.yaml`. With `dotnet run`, generate your own:

```bash
dotnet run --project tools/Blocks.Cli -- admin-key
dotnet user-secrets --project src/PdfBuilder.Api set "Blocks:Admin:KeyHash" "<printed hash>"
```

The issued key is shown once, in the response that creates it. Copy it then.

## The helper script

```powershell
$env:PDFBUILDER_API_KEY = 'pdf_…'      # or pass -ApiKey
./samples/Invoke-PdfJob.ps1 -PayloadPath templates/invoice.sample.json -Open
```

It POSTs the job, polls until the job finishes, downloads the PDF and reports how long the render
took. `-Open` opens it in the default viewer.

```powershell
# a different template
./samples/Invoke-PdfJob.ps1 -PayloadPath samples/minimal.json -TemplateKey receipt

# against the Docker stack instead of `dotnet run`
./samples/Invoke-PdfJob.ps1 -PayloadPath samples/multipage.json -BaseUrl http://localhost:8080

# keep the file somewhere specific
./samples/Invoke-PdfJob.ps1 -PayloadPath samples/precision.json -OutFile ./out.pdf
```

A rejected request prints the field and the rule that refused it rather than a stack trace, and a
failed job prints its `errorCode`.

## requests.http

Open it in VS Code with the **REST Client** extension (Rider and Visual Studio support `.http`
natively) and click *Send Request* over any block. It covers the happy path, the optional-field
cases, and each failure: the three that are refused at the edge with a 400, the one that is
accepted and then fails in the worker with `TEMPLATE_NOT_FOUND`, and the two refusals for a
missing or invalid key. A **Files** group covers a fresh link (`/file-url`), the direct download (`/file`) and the
`409 JOB_HAS_NO_FILE` and `410 FILE_EXPIRED` answers; the download request uses the status's `fileUrl` as it is,
signed or not. Set `@apiKey` at the top first.

The last section is a walk-through with two clients: templates and jobs belong to the client whose key
created them, so each client sees only its own, can have its own `statement` template, and gets a 404 for
another client's template or job. The built-in `invoice` template is everybody's. The operator, with the
admin key, names the client to work in with `X-Client-Id`.

## admin.http

The management calls, in order: register a client, issue keys, list them, revoke one, disable and
enable the client, and the refusals. Every one needs the admin key in `@adminKey`.

## console.http

What the console sends: sign in with the admin key (`X-Console: 1`), read when the session ends, a
change with and without the console header, acting as a client with `X-Client-Id`, the permission
list, and sign-out. The console does all of this itself; this file is for seeing the answers.

## curl

```bash
export KEY='pdf_…'
curl -X POST http://localhost:5052/api/pdf/jobs \
  -H "X-Api-Key: $KEY" -H 'Content-Type: application/json' \
  -d "{\"templateKey\":\"invoice\",\"data\":$(cat templates/invoice.sample.json)}"

curl -H "X-Api-Key: $KEY" http://localhost:5052/api/pdf/jobs/<jobId>
curl -o invoice.pdf http://localhost:5052/files/<fileId>      # no key, until file links are signed
```

## Things worth knowing while testing

**The first render is slow.** Chromium has to launch — expect four to five seconds. Every render
after that reuses the browser and takes a few hundred milliseconds. Do not read anything into the
first number.

**A template edit needs no restart, but does need the file the service is reading.** The repository
re-reads the file on every request, so changes take effect immediately. Outside Docker that file is
the *copy in the build output*, so rebuild (or copy the file across) after editing `templates/`.
Under `docker compose` the folder is mounted, so editing is enough.

**A 400 never reaches the queue.** Edge validation runs the command's own validator, so a bad
template key or a non-object payload is refused before anything is published. If you get a `jobId`,
the request was well formed and any failure after that is the worker's.
