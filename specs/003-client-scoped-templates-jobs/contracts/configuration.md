# Contract: Configuration and Upgrade

## New setting

| Key | Environment variable | Default | Meaning |
| --- | --- | --- | --- |
| `Blocks:Bus:DefaultClientId` | `Blocks__Bus__DefaultClientId` | unset | The client assumed for bus messages that name none, and the owner given to templates and jobs from before clients existed. Must match `^[a-z][a-z0-9-]{0,39}$`. |

- **Malformed** → the service refuses to start (options validation), whatever the data.
- **Unset** → messages without a client are refused (`VALIDATION_FAILED`). The service starts unless
  existing data needs an owner (below).
- **Names an unregistered client** → messages without a client are refused (`VALIDATION_FAILED`),
  unless the upgrade below registers it.

Listed in `docs/configuration.md` and `docs/blocks/04-backend.md#configuration` (Principle I gate).
`compose.yaml` sets it for the local stack to the client its samples use, so `docker compose up`
works from a clean checkout.

## Upgrade from a version without clients

On the first start of this version, if `authored_templates` or `pdf_jobs` has rows (or the 001 file
import has templates to bring in):

| `Blocks:Bus:DefaultClientId` | What happens |
| --- | --- |
| unset | refuses to start: "Templates or jobs from before clients existed need an owner. Set Blocks:Bus:DefaultClientId (environment variable Blocks__Bus__DefaultClientId) to the id of the client that uses them, then start again. Nothing has been changed." |
| set, registered | every row is given that client |
| set, not registered | the client is registered with its id as its name (rename it in the console), and every row is given it |

This happens once. Later starts, extra instances or a changed setting move nothing.

### Applying migrations by hand (`Database:MigrateOnStartup = false`)

Apply `AddClientOwner`, then run, with your client id:

```sql
INSERT INTO clients (id, name, created_at) VALUES ('<id>', '<id>', now()) ON CONFLICT (id) DO NOTHING;
UPDATE authored_templates SET client_id = '<id>' WHERE client_id IS NULL;
UPDATE pdf_jobs           SET client_id = '<id>' WHERE client_id IS NULL;
```

then apply `RequireClientOwner`. `docs/operations.md` carries this, with the exact column list
checked against the migration when it is generated.
