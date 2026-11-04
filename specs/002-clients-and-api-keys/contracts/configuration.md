# Contract: Configuration and Operations

## New setting

| Key | Default | Required | Notes |
| --- | --- | --- | --- |
| `Blocks:Admin:KeyHash` | – | **yes** | Lowercase hex SHA-256 of the admin key, as printed by `dotnet run --project tools/Blocks.Cli -- admin-key`. The service refuses to start without it, or with a value that is not 64 hex characters, naming the setting. Environment form: `Blocks__Admin__KeyHash`. |

There is deliberately no setting that turns authentication off.

## Generating the admin key

```bash
dotnet run --project tools/Blocks.Cli -- admin-key
```

```
Admin key (shown once, store it somewhere safe):
  adm_7Kq2…
Put this in configuration as Blocks:Admin:KeyHash:
  3f0a…c91e
```

Replacing it: generate a new one, change the setting, restart. The old key stops working at once.

## Where each deployment gets it

| Deployment | Source |
| --- | --- |
| `docker compose up` (local) | Set in `compose.yaml` to the hash of a **published, local-only** admin key, written next to it. Never use that key anywhere else. |
| `dotnet run` (local) | `dotnet user-secrets --project src/PdfBuilder.Api set "Blocks:Admin:KeyHash" "<hash>"` |
| Oracle VM | `ADMIN_KEY_HASH` in `/opt/pdf-builder/.env`; `deploy/compose.prod.yaml` requires it |
| Hugging Face Space | Secret `Blocks__Admin__KeyHash` |

## Logs

| Event | Level | Contents |
| --- | --- | --- |
| Every request that passes authentication | (scope) | `ClientId` and `KeyId`, or `Admin = true`, on every log line of the request |
| A key could not be checked because storage is down | Warning | Key id only |
| Client registered, enabled or disabled; key issued or revoked | Information | Client id, key id, key name, scopes. Never the key. |

No log line contains a key or an admin key. A request refused for a bad key is not logged at
Warning or above, so a scan of invented keys cannot flood the logs.

## Health

Unchanged. `/health/live` and `/health/ready` need no key.
