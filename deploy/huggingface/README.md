# Deploying to Hugging Face Spaces

The free alternative to the Oracle VM in [../README.md](../README.md) that needs no card. A Docker
Space on the free *CPU basic* hardware is 2 vCPU and 16 GB of RAM, which is enough for Chromium.

CI copies the source into the Space on every push to `master`, and the Space builds the image
itself from [src/PdfBuilder.Api/Dockerfile](../../src/PdfBuilder.Api/Dockerfile), with
[Dockerfile.space](Dockerfile.space) appended. [space.md](space.md) becomes the Space's
`README.md`, which is where Hugging Face reads the SDK and port from.

## What is different from the VM

- **It sleeps after 48 hours without HTTP requests.** That stops the RabbitMQ consumer too.
  [keep-alive.yml](../../.github/workflows/keep-alive.yml) pings it twice a day to prevent that.
- **The disk is wiped on every restart and every deploy.** Generated PDFs are disposable anyway
  (they are kept for 24 hours). Jobs and the templates authored in the web app are **not** on that
  disk: they live in a PostgreSQL database that the Space connects to (see the setup below), so
  they survive. That database is therefore required here, and it has to be hosted elsewhere; a free
  tier from Neon or Supabase is enough. Persistent storage on the Space itself is a paid add-on.
- **Visibility matters.** A public Space exposes the API, which has no authentication, to anyone
  who finds the URL. A private Space only answers requests that carry a Hugging Face token. A
  consumer-only setup, where everything arrives through RabbitMQ, works fine as private.
- **The web app is not deployed.** Only the API runs here.

## One-time setup

1. **Create an account** at [huggingface.co](https://huggingface.co/join).

2. **Create the Space.** Go to *New Space* and fill it in:

   | Setting | Value |
   | --- | --- |
   | Space name | `pdf-builder` (any name works; it becomes part of the URL) |
   | SDK | **Docker**, *Blank* template |
   | Hardware | **CPU basic** (free) |
   | Visibility | see above |

3. **Set the configuration** under the Space's *Settings → Variables and secrets*:

   | Kind | Name | Value |
   | --- | --- | --- |
   | Secret | `RabbitMq__ConnectionString` | The `amqps://` URI from CloudAMQP |
   | Secret | `ConnectionStrings__Database` | The Npgsql connection string of a hosted PostgreSQL, for example `Host=<host>;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require`. Hosted databases require TLS. |
   | Variable | `Storage__PublicBaseUrl` | `https://<owner>-<space-name>.hf.space` |
   | Secret | `Blocks__Admin__KeyHash` | The hash printed by `dotnet run --project tools/Blocks.Cli -- admin-key`, run on your own machine. Keep the key itself in your password manager: it manages clients and API keys, and only its hash goes in the Space. |

   The URL is shown under *⋯ → Embed this Space → Direct URL*. Underscores and capitals in the
   names are converted, so copy it from there instead of guessing it.

4. **Create an access token** at *Settings → Access Tokens → Create new token*. Choose
   *Fine-grained* and grant **write** access to this Space only.

5. **Configure GitHub** under the repository's *Settings → Secrets and variables → Actions*:

   | Kind | Name | Value |
   | --- | --- | --- |
   | Secret | `HF_TOKEN` | The token from step 4 |
   | Variable | `HF_SPACE` | `<owner>/<space-name>` |
   | Variable | `HF_SPACE_URL` | The direct URL from step 3, without a trailing slash |

6. **Push to `master`**, or re-run the CI workflow. The *Deploy to Hugging Face Spaces* job uploads
   the source and then waits for the build. The first build takes the longest, because nothing is
   cached yet. Build and container logs are on the Space page under *Logs*.

## When it fails

- **The Space stops at start-up with `ConnectionStrings:Database is required`.** The secret above is
  missing or empty. **With `The database schema is out of date`** the schema could not be applied:
  check the database user may create tables. **With `Blocks:Admin:KeyHash is required`** the admin
  key hash secret is missing; with `must be 64 lowercase hexadecimal characters`, the key itself was
  pasted instead of its hash.
- **The job says `/health/ready` did not answer 200.** The Space started, but a dependency check is
  failing, RabbitMQ or the database. Check the container logs for the connection error. If
  connections to CloudAMQP time out, try its non-TLS port: replace `amqps://` with `amqp://` in the
  secret. Credentials then cross the network unencrypted, so only do this to diagnose the problem.
- **Stage `BUILD_ERROR`.** The image did not build. The build log on the Space page has the same
  output `docker build` would print locally.
- **Stage `RUNTIME_ERROR`.** The process exited. The container log usually shows a missing or
  invalid setting, because options are validated at start-up.
