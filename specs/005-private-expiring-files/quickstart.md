# Quickstart: Private, Expiring Files

How to check that the feature works, end to end. Contracts: [http-files.md](contracts/http-files.md),
[message-contract.md](contracts/message-contract.md), [configuration.md](contracts/configuration.md).

## Prerequisites

- The stack from `compose.yaml` (`docker compose up --build`), which sets a development signing key, or
  the API run locally as `Production` with `ConnectionStrings:Database`, the admin key hash,
  `Blocks:Files:SigningKey` and `Storage:PublicBaseUrl` set. Running as `Development` loads user-secrets
  that point at the real broker.
- Two registered clients, `shop` and `erp`, each with a `pdf:generate` key (see `samples/admin.http`).
- `samples/requests.http` gains a "Files" section with the requests below.

## Automated tests

```powershell
dotnet test                                    # unit, architecture, contract tests
$env:PDFBUILDER_RUN_DATABASE_TESTS = "1"       # plus PDFBUILDER_TEST_POSTGRES without Docker
dotnet test                                    # store, sweeper, migration and API tests
cd web; npm test                               # console
```

Expected: all pass, including the new `FileLinkSignerTests`, `BlocksFilesOptionsTests`,
`FileSignatureEndpointFilterTests`, `FileRetentionSweeperTests`, `JobFilesTests`, `SignedLinkApiTests`,
`JobFileEndpointsTests`, `FileLinkLeakTests` and the `AddFileLifetime` migration test.

## Scenario 1: signed links (US1)

Start with `Blocks__Files__RequireSignature=true` and `Blocks__Files__UrlLifetime=00:02:00`.

1. As `shop`, `POST /api/pdf/jobs` with template `invoice`; poll `GET /api/pdf/jobs/{id}` until
   `Succeeded`. Expect `fileUrl` with `expires` and `sig`, and `fileUrlExpiresAt` about two minutes ahead.
2. `curl -i "<fileUrl>"` with no key → `200`, `application/pdf`, `Cache-Control: private, no-store`.
3. Change one character of `sig`; then of the file id; then add 3600 to `expires`; then drop both
   parameters → `404` each time, identical to `curl -i <base>/files/00000000000000000000000000000000`.
4. Wait two minutes; the original link → `404`.

## Scenario 2: fresh link and authenticated download (US2, US4)

1. As `shop`, `GET /api/pdf/jobs/{id}/file-url` → `200`, a new link that downloads the same PDF; the link
   from scenario 1, if still within its time, keeps working.
2. As `erp`, the same request with `shop`'s job id → `404`, same body shape as an unknown job id.
3. As `shop`, `GET /api/pdf/jobs/{id}/file` with the key → the PDF. Same as `erp` → `404`.
4. Submit a job with a template that fails to render; `file-url` for it → `409 JOB_HAS_NO_FILE`.
5. In the console, sign in, choose `shop`, open the job, press Download and Preview: both work. The
   browser's network tab shows `/api/pdf/jobs/{id}/file`, never `/files/…`.

## Scenario 3: retention (US3)

1. Set `Blocks__Files__RetentionDays=1`. Generate a PDF; note its `fileId`.
2. Move the file's last-write time back two days
   (`touch -d '2 days ago' data/files/<xx>/<fileId>.pdf`) and set the job's `file_created_at` back
   the same amount in the database, or wait for the sweeper test to cover it.
3. Restart (the sweeper runs at start-up) or wait 15 minutes. Expect the file gone from disk;
   `GET /api/pdf/jobs/{id}` → `fileExpired: true`, `fileUrl: null`; `file-url` → `410 FILE_EXPIRED`;
   the console shows "File expired" without Download.
4. Delete another job's PDF by hand; reading that job → `fileExpired: true` at once.

## Scenario 4: upgrade and the switch (US5)

1. Start the previous version, generate a PDF, keep its unsigned `fileUrl`.
2. Start this version without `Blocks:Files:SigningKey` → it refuses to start, naming the setting; the
   database schema is unchanged.
3. Set the key, switch off (default) → starts; the old link still downloads; a new job returns an
   unsigned `fileUrl` and `fileUrlExpiresAt: null`. A bus requester on Contracts 1.2.0 still receives
   its reply.
4. Switch on → the old unsigned link → `404`; new jobs return signed links. Switch off again → a signed
   link issued meanwhile still works until its expiry.

## Scenario 5: nothing secret in logs (SC-008)

Run with `Logging__LogLevel__Default=Debug`, go through scenarios 1–2, then search the container log for
`sig=` and for the signing key: no matches. (`FileLinkLeakTests` does this automatically.)
