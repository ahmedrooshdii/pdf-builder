# Quickstart: Validating Client-Scoped Templates and Jobs

How to prove the feature works end to end. Exact HTTP behaviour:
[contracts/http-client-scoping.md](contracts/http-client-scoping.md). Bus behaviour:
[contracts/message-contract.md](contracts/message-contract.md). Upgrade:
[contracts/configuration.md](contracts/configuration.md).

## Prerequisites

- .NET 10 SDK, Docker (for the full stack) or a PostgreSQL reachable through
  `PDFBUILDER_TEST_POSTGRES` (for database tests without Docker).
- Run local checks with `ASPNETCORE_ENVIRONMENT=Production` and the compose broker: Development
  loads user-secrets that point at the real hosted broker.
- An admin key whose hash is in `Blocks:Admin:KeyHash` (`compose.yaml` has a published local one;
  see `samples/admin.http`).

## 1. Automated tests

```powershell
dotnet build
dotnet test                                             # unit, stud and architecture tests
$env:PDFBUILDER_RUN_DATABASE_TESTS = '1'; dotnet test   # + PostgreSQL-backed and pipeline tests
```

Expected: all pass, including the new ones:

| Test | Proves |
| --- | --- |
| `CrossClientIsolationTests` (Api) | B's answer for A's template key or job id equals the answer for one that never existed, for every template and job endpoint (SC-001, SC-002) |
| `SameKeyPerClientTests` (Api) | A and B each own `invoice`; each job renders its own client's template (SC-003); built-in templates listed and rendered for both (SC-004) |
| `ActingClientTests` (Api) | operator without `X-Client-Id` → 400; unknown client → 404 `CLIENT_NOT_FOUND`; disabled client works |
| `GeneratePdfConsumerClientTests` (Api) | named client, default client, no client, unknown, disabled, another client's `CorrelationId` |
| `ClientOwnerUpgradeTests` (Infrastructure) | a database at the `AddConsoleSessions` schema with rows: refuses to start without the setting and changes nothing; with it, rows owned and the client registered; two instances at once; second start does nothing |
| `JobRetentionSweeperTests` (Infrastructure) | expired jobs of every client are deleted |
| `ArchitectureTests` (Blocks) | `IgnoreQueryFilters` only in the allowed methods; every client-owned entity has the filter |
| `ContractsTests` (Application) | `ClientDisabled` in `All`; a message without `ClientId` reads as `null` |

## 2. Two clients over HTTP

Start the stack (`docker compose up --build`). Using `samples/admin.http`, register `ecommerce` and
`erp`, and issue each a key with all three `pdf:*` permissions. Then with `samples/requests.http`:

1. As `ecommerce`: `POST /api/templates` with key `receipt`, publish it.
2. As `erp`: `GET /api/templates`. `receipt` is absent and the built-in templates are present.
3. As `erp`: `GET /api/templates/receipt`. Expect `404 TEMPLATE_NOT_FOUND`, the same body as for
   `GET /api/templates/no-such-key`.
4. As `erp`: create `receipt` too. Expect `201`. Edit it so its output differs.
5. Submit a `receipt` job as each client and download both PDFs. Each shows its own client's content.
6. As `erp`: `GET /api/pdf/jobs/{ecommerce's job id}`. Expect `404`, identical to an unknown id.

## 3. The operator

With the admin key:

- `GET /api/templates` with no `X-Client-Id` → `400 VALIDATION_FAILED` asking for a client.
- `X-Client-Id: nobody` → `404 CLIENT_NOT_FOUND`.
- `X-Client-Id: ecommerce` → the same list `ecommerce` gets with its own key.
- In the console, switching between the two clients shows each one's templates only.

## 4. The bus

With `Blocks__Bus__DefaultClientId: ecommerce` (the compose default):

- Publish `GeneratePdfRequest` with `ClientId = "erp"` for `receipt`: rendered with erp's template;
  `GET /api/pdf/jobs/{CorrelationId}` works with erp's key, `404` with ecommerce's.
- Publish without `ClientId`: handled as `ecommerce`.
- Remove the setting, restart, publish without `ClientId`: response `VALIDATION_FAILED`, nothing
  rendered.
- Disable `erp`, publish naming it: response `CLIENT_DISABLED`.

## 5. Upgrade

1. Check out the last commit before this feature, start it, author and publish two templates and
   submit a job.
2. Check out this feature and start **without** `Blocks:Bus:DefaultClientId`: the service exits, and
   the log says which setting to set. The tables are unchanged.
3. Set it to `legacy` (not registered) and start: the service starts, `legacy` appears in the
   clients list named `legacy`, and as `legacy` both templates are listed and render the same PDF
   as before. Other clients do not see them.
4. Restart, and change the setting to `ecommerce`: nothing moves.
