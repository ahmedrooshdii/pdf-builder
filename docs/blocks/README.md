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

spec-kit (1.2.0) is initialised in this repository (`.specify/`, with the Claude skills in
`.claude/skills/`). In order:

1. **The constitution** is ratified at
   [`.specify/memory/constitution.md`](../../.specify/memory/constitution.md): ten principles, each
   ending with the gate `/speckit-plan` checks. It is the single most important input, and it
   overrides these documents where they disagree.
2. **`/speckit.specify`**: one feature at a time, from the backlog in
   [03-planning.md](03-planning.md#feature-backlog). Each backlog entry has a ready-to-paste
   description. Specs describe *what* and *why*, so give it the business and UI/UX documents as
   context, not the backend one.
3. **`/speckit.plan`**: give it the matching sections of the backend and frontend documents. This
   is where technology choices belong.
4. **`/speckit.tasks`**, then **`/speckit.implement`**.

Keep these documents as the source of truth. When a spec or plan makes a decision that changes
one of them, update the document in the same pull request.

## Constitution

The principles that used to be seeded here are now
[the constitution](../../.specify/memory/constitution.md), and are maintained only there:

| | Principle |
| --- | --- |
| I | One codebase, no customer forks |
| II | A block stands alone |
| III | Generic capabilities, no product knowledge |
| IV | Products compose; blocks never call each other |
| V | Every block exposes the same studs |
| VI | Identity comes from claims only |
| VII | Published contracts change additively |
| VIII | Secrets are unrecoverable and shown once |
| IX | Layering is enforced by tooling |
| X | Runs on any customer's hardware, offline |

## Decisions still open

| Question | Default if nobody decides | Where it matters |
| --- | --- | --- |
| Do the shared .NET and Angular pieces live in a separate `blocks-platform` repository from day one? | No: they start inside this repository and move out when the second block starts. | [03-planning.md](03-planning.md#repository-structure) |
| Which block comes second? | Email, because the first product (e-commerce) needs invoices emailed. | [01-business.md](01-business.md#the-block-catalogue) |
| Object storage for files, or the VM's disk? | Disk, behind `IFileStorage`, until a second replica exists. | [04-backend.md](04-backend.md#files) |
| Is Docker on Linux the only supported install target? | Yes. A Windows-only company runs a Linux VM. | [06-deployment.md](06-deployment.md#host-requirements) |
| What does a company receive: images only, or source too? Licence key? | Images and the kit only; no licence key. | [06-deployment.md](06-deployment.md#what-a-release-is) |
| Workflow engine in the kit | n8n as an optional profile, once its licence is confirmed for this use; Elsa if not. | [06-deployment.md](06-deployment.md#which-engine) |
