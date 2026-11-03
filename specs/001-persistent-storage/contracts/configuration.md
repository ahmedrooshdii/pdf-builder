# Contract: Configuration and Operational Surface

What an operator sees change. Everything else in `docs/configuration.md` is unchanged.

## New settings

| Key | Default | Required | Notes |
| --- | --- | --- | --- |
| `ConnectionStrings:Database` | – | **yes** | Npgsql connection string for this block's own database. Start-up fails with a message naming this key when it is missing or empty. Secret: never logged. |
| `Blocks:Database:MigrateOnStartup` | `true` | no | `false` when migrations are applied by a separate deployment step. With `false` and an out-of-date schema, start-up fails naming the pending migration. |

Environment variable forms: `ConnectionStrings__Database`, `Blocks__Database__MigrateOnStartup`.

## Changed meaning

| Key | Before | After |
| --- | --- | --- |
| `JobStore:RetentionHours` | How long a job stays pollable, in process memory; lost on restart. | Same default (24) and range (1–168). Measured from the job's last update, as before; survives restarts and is shared by every instance. |
| `TemplateStore:AuthoredRootPath` | Where authored templates are read and written. | Read **once**, on the first start against an empty database, to import existing templates. Never written. Can be removed after a successful upgrade. |

## Health

| Endpoint | Before | After |
| --- | --- | --- |
| `/health/live` | Process up. | Unchanged. |
| `/health/ready` | Broker and built-in template directory. | Also the database. 503 while the database is unreachable. |

## Logs

| Event | Level | Contents |
| --- | --- | --- |
| Migration applied | Information | Migration names applied. |
| Template import completed | Information | Source path, imported count, skipped count. |
| Template file skipped | Warning | File name and reason (see data model skip reasons). |
| Template import already done | Debug | Date of the original import. |
| Retention sweep | Debug | Rows deleted. |

The connection string never appears in logs or problem details.

## Deployment files

| File | Change |
| --- | --- |
| `compose.yaml` | Adds `postgres:17-alpine` with a named volume and health check; the API waits for it and receives `ConnectionStrings__Database`. |
| `deploy/compose.prod.yaml` | Same service on the VM; `POSTGRES_PASSWORD` required in `.env`. |
| `deploy/huggingface/` | `ConnectionStrings__Database` from a Space secret pointing at a hosted PostgreSQL, because the Space disk is not persistent. |
