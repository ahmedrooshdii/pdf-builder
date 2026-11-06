# Configuration Contract: `Blocks:Files`

Stud settings, the same in every block. Bound by `AddBlocksFiles()` and validated at start-up: an invalid
value stops the service with a message that names the key and its environment variable, never the value.

| Key | Environment variable | Default | Rule |
| --- | --- | --- | --- |
| `Blocks:Files:SigningKey` | `Blocks__Files__SigningKey` | none, **required** | Base64 of at least 32 random bytes. A secret: keep it in user-secrets or an environment variable, never in a committed file. Changing it invalidates every outstanding signed link at once. |
| `Blocks:Files:UrlLifetime` | `Blocks__Files__UrlLifetime` | `01:00:00` | `00:01:00` to `7.00:00:00`. Applies to links issued after the change. |
| `Blocks:Files:RetentionDays` | `Blocks__Files__RetentionDays` | `30` | 1 to 3650. Counted from each file's creation; shortening it deletes older files at the next sweep (within 15 minutes). |
| `Blocks:Files:RequireSignature` | `Blocks__Files__RequireSignature` | `false` | `true`: issue signed, expiring links and refuse unsigned ones. `false`: links as before. The next release changes the default to `true`. |

Start-up messages (examples):

- `Blocks:Files:SigningKey (environment variable Blocks__Files__SigningKey) is required: base64 of at least 32 random bytes. Generate one with "dotnet run --project tools/Blocks.Cli -- file-signing-key" or "openssl rand -base64 32".`
- `Blocks:Files:SigningKey (environment variable Blocks__Files__SigningKey) must be base64 of at least 32 bytes.`
- `Blocks:Files:UrlLifetime must be between 00:01:00 and 7.00:00:00.`

Unchanged and still required: `Storage:PublicBaseUrl` (links are built from it).

`JobStore:RetentionHours` keeps its range (1–168) and meaning for jobs without a file. Its documentation
gains: "A succeeded job is kept as long as its file, then for this long."

## Logging

`appsettings.json` gains `"Microsoft.AspNetCore.Hosting.Diagnostics": "Warning"` under
`Logging:LogLevel`. Raising it to `Information` logs every request URL, signatures included; the docs say
so.

## Deployment files

| File | Change |
| --- | --- |
| `compose.yaml` | `Blocks__Files__SigningKey: <fixed development key>` with a comment that it is for local use only. `RequireSignature` left at default. |
| `deploy/compose.prod.yaml` | `Blocks__Files__SigningKey: ${FILES_SIGNING_KEY:?set FILES_SIGNING_KEY in .env}`; commented `Blocks__Files__RequireSignature`, `UrlLifetime`, `RetentionDays`. |
| `deploy/README.md` | Generate `FILES_SIGNING_KEY`; what the switch does. |
| `deploy/huggingface/space.md` | The demo Space needs the secret too. |
| `src/PdfBuilder.Api/appsettings.Development.json` | Not touched: the key goes in user-secrets, as the database connection string does. |

## Upgrade

The one manual step: set `Blocks:Files:SigningKey`. Without it the service does not start and changes
nothing, not even the schema, since options are validated before `DatabaseStartup` runs.

On the first start the migration `AddFileLifetime` runs and the file sweeper deletes every file older than
`RetentionDays`. To keep everything for now, set `RetentionDays` to `3650` before upgrading.

Operators who apply migrations themselves (`Blocks:Database:MigrateOnStartup=false`) run the SQL that
`dotnet ef migrations script RequireClientOwner AddFileLifetime --idempotent` produces; in substance:

```sql
ALTER TABLE pdf_jobs ADD file_created_at timestamptz NULL;
ALTER TABLE pdf_jobs ADD file_expired_at timestamptz NULL;
UPDATE pdf_jobs SET file_created_at = completed_at WHERE status = 'Succeeded';
ALTER TABLE pdf_jobs ADD CONSTRAINT ck_pdf_jobs_file_created_at
    CHECK (status <> 'Succeeded' OR file_created_at IS NOT NULL);
CREATE INDEX ix_pdf_jobs_file_created_at ON pdf_jobs (file_created_at);
ALTER TABLE pdf_jobs DROP COLUMN file_url;
```
