# Blocks

PDF Builder is the first of a set of **blocks**: self-contained backend capabilities (PDF, email,
notifications, …) that any of my products can plug in, the way a product plugs in Stripe or
SendGrid, and that a company can have installed on its own servers without a line of code
changing. These documents describe the whole set, using PDF Builder as the reference block.

| # | Document | Answers | Feeds spec-kit |
| --- | --- | --- | --- |
| 1 | [Business](01-business.md) | Why blocks exist, who uses them, what is in and out of scope | `/speckit.constitution`, `/speckit.specify` |
| 2 | [UI/UX](02-ui-ux.md) | How every block's console looks and behaves | `/speckit.specify` (UI stories), `/speckit.plan` |
| 3 | [Planning](03-planning.md) | Architecture, ERD, repo structure, feature backlog and order | `/speckit.plan`, `/speckit.tasks` |
| 4 | [Backend](04-backend.md) | The shared contract every block's API implements, and PDF Builder's changes | `/speckit.plan` |
| 5 | [Frontend](05-frontend.md) | The console app's structure, auth, data access and shared UI | `/speckit.plan` |
| 6 | [Deployment](06-deployment.md) | Installing the same release anywhere: kit, configuration, template packs, orchestration, upgrades | `/speckit.plan` |

## Vocabulary

| Term | Meaning |
| --- | --- |
| **Block** | One deployable capability with its own API, database and console. PDF Builder is one. |
| **Product** | An application I build that uses blocks, for example an e-commerce or delivery app. |
| **Client** | A product as a block sees it. Every key, job and template inside a block belongs to one client. |
| **Studs** | The conventions every block follows (auth, errors, async jobs, idempotency, webhooks, health), so learning one block teaches all of them. Defined in [04-backend.md](04-backend.md#the-studs). |
| **Console** | A block's own web UI, used by the operator, never by a product's end users. |
| **Operator** | Whoever runs a deployment: me, or a company's IT on a dedicated deployment. |
| **Deployment** | One installation of a block. *Shared*: on my VM, serving my products. *Dedicated*: on a company's server, serving that company's systems. Same images either way. |
| **Template pack** | A file of templates exported from one deployment and imported into another. How an invoice design moves without code. |

"Client" is used instead of "tenant" on purpose: today the blocks serve my own products only. If
they are ever sold to other people, a client gains an owning organisation and the word tenant
starts to fit. See [01-business.md](01-business.md#phases).

## Using these with spec-kit

spec-kit (1.2.0) is installed but not initialised in this repository. Initialising adds
`.specify/` and the agent commands:

```bash
specify init --here --integration claude
```

Then, in order:

1. **`/speckit.constitution`**: paste the [constitution seed](#constitution-seed) below. Every
   later plan is checked against it, so it is the single most important input.
2. **`/speckit.specify`**: one feature at a time, from the backlog in
   [03-planning.md](03-planning.md#feature-backlog). Each backlog entry has a ready-to-paste
   description. Specs describe *what* and *why*, so give it the business and UI/UX documents as
   context, not the backend one.
3. **`/speckit.plan`**: give it the matching sections of the backend and frontend documents. This
   is where technology choices belong.
4. **`/speckit.tasks`**, then **`/speckit.implement`**.

Keep these documents as the source of truth. When a spec or plan makes a decision that changes
one of them, update the document in the same pull request.

## Constitution seed

Paste this into `/speckit.constitution`. Each principle is one that a later decision could
plausibly break, which is why it is written down.

0. **One codebase, no customer forks.** Every deployment runs the same released images. Whatever
   differs between deployments (domain, secrets, branding, limits, templates, fonts, workflows) is
   configuration or data. A customer need that configuration cannot meet is either built into the
   main codebase, off by default, or belongs in the customer's orchestration. Never a branch.
1. **A block stands alone.** It owns its database, configuration and deployment. It never reads
   another block's database and never requires another block to be running in order to start.
   Test: deleting any other block leaves this one healthy.
2. **A block knows nothing about the products using it.** No product vocabulary (order, customer,
   restaurant) appears in a block's code, contracts or schema. A block's data is scoped by
   `ClientId` and nothing else.
3. **Blocks do not call each other.** Products compose blocks. When blocks must fit together, they
   do it through shared conventions (a PDF block returns a URL; an email block accepts attachment
   URLs), not through dependencies.
4. **Every block exposes the same studs.** API-key auth producing `client_id` and `scopes` claims,
   RFC 9457 problem details with a stable `code`, `202 Accepted` plus a status URL for slow work,
   `Idempotency-Key`, signed webhooks, signed expiring file URLs, `/health/live` and
   `/health/ready`. A block that cannot follow a stud documents why.
5. **Code reads identity from claims only.** Nothing below the authentication handler knows whether
   a `client_id` came from an API key, a session cookie or a future JWT. That is what lets the auth
   mechanism change without touching features.
6. **Published contracts change additively.** Message interfaces, error codes and HTTP response
   shapes gain optional members in minor versions. Removing or renaming anything is a major version
   and a new URL or message type.
7. **Generic capabilities only.** A block is a technical capability (render, send, store, notify,
   charge). Business concepts (orders, carts, catalogues) belong to products and are never blocks.
8. **Secrets are stored as hashes and shown once.** API keys, webhook secrets and admin keys are
   never stored or logged in a recoverable form.
9. **Layering is enforced by tooling, not by convention.** Backend: `Api -> Infrastructure ->
   Application -> Domain -> Contracts`. Frontend: `core`/`shared`/`features` rules in ESLint.
10. **Runs on any customer's hardware, offline.** Every image builds for `linux/arm64` and
    `linux/amd64`, and needs no internet access at runtime: no CDNs, no telemetry by default, no
    licence call-home.
11. **Nothing is hardcoded to one deployment.** No domain, product name, logo or limit is written
    in code. A value a company could want different is a configuration key with a default.

## Decisions still open

| Question | Default if nobody decides | Where it matters |
| --- | --- | --- |
| Do the shared .NET and Angular pieces live in a separate `blocks-platform` repository from day one? | No: they start inside this repository and move out when the second block starts. | [03-planning.md](03-planning.md#repository-structure) |
| Which block comes second? | Email, because the first product (e-commerce) needs invoices emailed. | [01-business.md](01-business.md#the-block-catalogue) |
| Object storage for files, or the VM's disk? | Disk, behind `IFileStorage`, until a second replica exists. | [04-backend.md](04-backend.md#files) |
| Is Docker on Linux the only supported install target? | Yes. A Windows-only company runs a Linux VM. | [06-deployment.md](06-deployment.md#host-requirements) |
| What does a company receive: images only, or source too? Licence key? | Images and the kit only; no licence key. | [06-deployment.md](06-deployment.md#what-a-release-is) |
| Workflow engine in the kit | n8n as an optional profile, once its licence is confirmed for this use; Elsa if not. | [06-deployment.md](06-deployment.md#which-engine) |
