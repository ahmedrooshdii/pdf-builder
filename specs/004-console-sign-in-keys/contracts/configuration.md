# Contract: Configuration

Two new settings, both optional with defaults. To be added to `docs/configuration.md` and
`docs/blocks/04-backend.md#configuration` (Principle I gate).

| Key | Environment variable | Default | Notes |
| --- | --- | --- | --- |
| `Blocks:Console:SessionLifetime` | `Blocks__Console__SessionLifetime` | `12:00:00` | How long a console sign-in lasts, from sign-in, never extended by use. `00:05:00` to `24:00:00`; anything else fails start-up naming the key. |
| `Blocks:ForwardedHeaders:KnownNetworks` | `Blocks__ForwardedHeaders__KnownNetworks` | *(empty)* | Comma-separated CIDRs of reverse proxies whose `X-Forwarded-For` / `X-Forwarded-Proto` are believed, for example `172.16.0.0/12`. Empty trusts only loopback. Used for the sign-in limit's per-address count. An unparsable entry fails start-up naming the key. |

Unchanged and still required: `Blocks:Admin:KeyHash` (002). Changing it ends every console session
on the next request (FR-006).

## Values set in this repository

| File | Setting | Value | Why |
| --- | --- | --- | --- |
| `compose.yaml` (api) | `Blocks__ForwardedHeaders__KnownNetworks` | `172.16.0.0/12` | nginx in the `web` container is on the Compose project network, which Docker allocates from this range by default |
| `deploy/compose.prod.yaml` | same | commented example | The operator's reverse proxy address varies |

## Serving requirements (documented in `docs/operations.md` and `deploy/README.md`)

- Serve the console and the API from the same site (one origin through a proxy, as `web/nginx/` does).
- Serve over HTTPS; plain HTTP works only on `localhost`, where browsers accept `Secure` cookies.
- Do not embed the console in a frame on another site: the `SameSite=Strict` cookie is not sent there.
- On start-up ASP.NET logs that Data Protection keys are stored unencrypted. That is expected: they
  are in the block's database, and a session also needs a row whose id only the cookie holds.

## Rotating the admin key

1. `dotnet run --project tools/Blocks.Cli -- admin-key` → new key and hash.
2. Set `Blocks__Admin__KeyHash` to the new hash on every instance; restart.
3. Every console session ends at its next request; sign in with the new key. Scripts using the old
   key get `401 AUTH_INVALID_KEY`.
