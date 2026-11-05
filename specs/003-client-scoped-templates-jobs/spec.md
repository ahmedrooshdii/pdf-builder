# Feature Specification: Client-Scoped Templates and Jobs

**Feature Branch**: `003-client-scoped-templates-jobs`

**Created**: 2026-11-04

**Status**: Draft

**Input**: User description: "003". Backlog entry
(`docs/blocks/03-planning.md#003-client-scoped-templates-and-jobs`): "Templates, jobs and files belong
to the client that created them. A client can list, read, use and change only its own; another
client's template key behaves as if it does not exist, even if the same key exists elsewhere.
Built-in templates remain available to every client. Messages arriving on the bus carry the client
too; messages from older publishers that do not carry one are treated as coming from a configured
default client so they keep working."

## User Scenarios & Testing *(mandatory)*

A **client** is a product registered by the operator (feature 002), such as an online shop or an ERP.
Since feature 002 every HTTP call names its client through its API key, and since feature 004 the
operator's console names the client it is working in. The service still keeps one shared set of
templates and jobs, so every client sees, uses and can change everything. This feature gives each
template and job an **owner**: the client that created it.

Terms used below:

- **Authored template**: a template created and edited through the service (API or console). It has
  an owner.
- **Built-in template**: a template shipped with the service. It has no owner and is available to
  every client, read-only, as today.
- **Default client**: a client the operator names in configuration, used for bus messages that do not
  say which client sent them.
- **Not found**: the answer the service already gives for a template or job that does not exist. "As
  if it does not exist" means that answer, identical in status, code and wording, apart from the
  trace id every error carries.

### User Story 1 - A client sees and changes only its own templates (Priority: P1)

The shop and the ERP both use the PDF service. Each one's developers author their own templates. The
shop lists templates and sees its own and the built-in ones, never the ERP's. Both can have a template
called `invoice`, and each one's `invoice` is its own: editing, publishing or deleting one leaves the
other untouched. When the shop asks for a template only the ERP has, it is told the template does not
exist.

**Why this priority**: this is what makes it safe to give a second product a key. Without it, any
client can read or overwrite another's templates.

**Independent Test**: with two clients, A and B, create template `invoice` as A. List as B: `invoice`
is absent. Read, edit, publish, duplicate and delete `invoice` as B: each answers not found, and A's
template is unchanged. Create `invoice` as B: it succeeds and is B's own. List as either: each sees
only its own `invoice`, plus every built-in template.

**Acceptance Scenarios**:

1. **Given** clients A and B, **When** A creates a template, **Then** it is owned by A and appears in
   A's list and not in B's.
2. **Given** a template owned by A, **When** B reads, edits, publishes, duplicates or deletes it by its
   key, **Then** the answer is identical to the answer for a key that exists nowhere, and A's template
   is unchanged.
3. **Given** A has a template `invoice`, **When** B creates a template `invoice` (in any letter case),
   **Then** it is created for B, and the two templates thereafter change independently.
4. **Given** A has a template `invoice`, **When** A creates `Invoice`, **Then** it is refused as a key
   already taken, as today (keys are unique per client, ignoring letter case).
5. **Given** any client, **When** it lists templates, **Then** every built-in template is included,
   and it can read, preview, duplicate into its own templates and render each one, but not change it.
6. **Given** any client, **When** it creates a template with a built-in template's key, **Then** it is
   refused as a key already taken, as today.

---

### User Story 2 - A client's jobs are its own (Priority: P1)

The shop submits a job for `invoice`. The service renders the shop's own published `invoice`, or the
built-in `invoice` if the shop has none, and never the ERP's. The shop can poll its job; the ERP,
given the same job id, is told it does not exist.

**Why this priority**: jobs carry the documents products generate (invoices, receipts). One client
must not be able to read another's job status or reach its document through the job.

**Independent Test**: as A, publish a template `receipt` and submit a job for it: it renders A's
template. As B, submit a job for `receipt`: it is refused as template not found (B has none, and
there is no built-in `receipt`). As B, read A's job by its id: not found. As A, read it: its status
and file link, as today.

**Acceptance Scenarios**:

1. **Given** A has a published template `invoice` and B has none, **When** B submits a job for
   `invoice` and a built-in `invoice` exists, **Then** B's job renders the built-in one; **when** no
   built-in `invoice` exists, **then** B's job is refused as template not found.
2. **Given** A and B each have a published `invoice`, **When** each submits a job for `invoice`,
   **Then** each job renders its own client's template.
3. **Given** a job submitted by A, **When** B reads it by id, **Then** the answer is identical to the
   answer for an id that never existed.
4. **Given** a job submitted by A, **When** A reads it, **Then** the answer is the same as it is today.
5. **Given** a job submitted by A, **Then** the job and the file it produces are recorded as A's.

---

### User Story 3 - Bus messages are handled for the client that sent them (Priority: P2)

Some products submit jobs by publishing a message on the message bus rather than calling the API. A
product can now name its client in the message, and the job is handled exactly as if that client had
submitted it over the API: its templates, its job. Products that publish with an older version of the
message package, which cannot name a client, keep working: their messages are treated as coming from
the default client the operator configured.

**Why this priority**: without it, bus jobs could only use built-in templates once templates are
separated, and existing bus publishers would break on upgrade.

**Independent Test**: configure default client D. Publish a message naming client A for A's template:
it renders A's template and the job is A's. Publish a message with no client: it is handled as D's.
Remove the default client setting and publish a message with no client: it is answered with a
validation failure and no PDF is rendered.

**Acceptance Scenarios**:

1. **Given** a message naming client A, **When** it is consumed, **Then** the job is A's, renders A's
   published template or a built-in one, and is readable over the API by A and by nobody else.
2. **Given** a message naming no client and a configured default client D, **When** it is consumed,
   **Then** it is handled exactly as a message naming D.
3. **Given** a message naming no client and no default client configured, **When** it is consumed,
   **Then** the publisher receives a failure response with the validation-failed code and a message
   saying the client is missing, and nothing is rendered.
4. **Given** a message naming a client that is not registered, or whose id is not a valid client id,
   **When** it is consumed, **Then** the publisher receives a failure response with the
   validation-failed code, and nothing is rendered.
5. **Given** a message naming a disabled client, **When** it is consumed, **Then** the publisher
   receives a failure response with the client-disabled code already used by the API, and nothing is
   rendered.
6. **Given** a message whose job id (correlation id) is already used by another client's job, **When**
   it is consumed, **Then** the publisher receives a failure response with the validation-failed
   code, and the other client's job is neither changed nor described in the response.
7. **Given** a publisher using the previous version of the message package, **When** it publishes as
   it does today, **Then** its messages are still accepted (the change to the message is additive).

---

### User Story 4 - Existing templates and jobs keep working after the upgrade (Priority: P2)

A deployment that ran an earlier version already has authored templates and recent jobs, created
before clients existed. After the upgrade they belong to a client, so the products already using them
keep rendering them without anyone re-creating a template.

**Why this priority**: an upgrade that silently hides every existing template would break every
product in production on the day it is installed.

The operator decides who that is: the default client they configure. The service will not guess.

**Independent Test**: start an earlier version, author and publish templates and submit jobs. Upgrade
without configuring a default client: the service refuses to start and says which setting to fill in.
Name client D as the default and start again: D is registered if it was not already, the existing
templates and jobs belong to D, and D renders each template exactly as before.

**Acceptance Scenarios**:

1. **Given** authored templates and jobs from before the upgrade and a default client D configured,
   **When** the upgraded service starts, **Then** each of them is owned by D, with its drafts,
   published versions and revision history unchanged; if D was not registered, it is registered, with
   its id as its name, and the operator can rename it later.
2. **Given** templates or jobs from before the upgrade and no default client configured, or a value
   that is not a valid client id, **When** the upgraded service starts, **Then** it refuses to start,
   its error names the setting to fill in, and no template or job is changed.
3. **Given** a deployment with no templates or jobs from before the upgrade, **When** the upgraded
   service starts without a default client, **Then** it starts normally.
4. **Given** the upgrade has run, **When** the service restarts or a second instance starts, or the
   default client setting is later changed or removed, **Then** ownership is not assigned again and
   nothing changes.
5. **Given** the upgrade has run, **When** the owning client submits a job for an existing template,
   **Then** the PDF is identical to the one produced before the upgrade.

---

### User Story 5 - The operator works inside one client at a time (Priority: P3)

The operator, using the admin key or the console, works on templates and jobs inside a chosen client,
as the console already does since feature 004. With this feature that choice actually decides which
templates and jobs they see and change.

**Why this priority**: the console already sends the chosen client; this story makes the service honour
it and defines what happens when the operator names no client or a client that does not exist.

**Independent Test**: as the admin acting as A, list templates: A's and built-in ones only. Create a
template: it is A's. Acting as B: A's template is not found. Without naming a client: template and
job calls are refused with a message asking for a client. Naming an unregistered client: not found.

**Acceptance Scenarios**:

1. **Given** the operator acting as client A, **When** they list, read, create, change or delete
   templates or submit or read jobs, **Then** the result is exactly what client A would get with its
   own key.
2. **Given** the operator acting as no client, **When** they call any template or job operation,
   **Then** it is refused with the validation-failed code and a message saying a client must be
   chosen.
3. **Given** the operator acting as a client id that is not registered, **When** they call any
   template or job operation, **Then** it is refused with the client-not-found code.
4. **Given** the operator acting as a disabled client, **When** they work on its templates and jobs,
   **Then** it works (the operator can still inspect and prepare a disabled client's data; only the
   client's own keys are refused, as since feature 002).

---

### Edge Cases

- **Same key, different letter case, different clients.** A's `Invoice` and B's `invoice` are two
  unrelated templates; the case-insensitive uniqueness rule applies within one client only.
- **A client's own template shadows a built-in one.** If a client owns a template with a built-in
  template's key (possible only for templates that existed before the built-in was shipped, or were
  imported with it), that client sees and renders its own; every other client still sees the built-in.
- **Duplicating another client's template.** Refused as not found, like any other access to it.
  Duplicating a built-in template into one's own templates works, as today.
- **Preview.** Previewing unsaved content uses no stored template and works for any client; it is
  still refused for an operator acting as no client, so every template operation behaves alike.
- **Job ids are not secrets.** Knowing another client's job id gives nothing: the job answers not
  found. Ids chosen by the service (API submissions) never collide; ids chosen by bus publishers may,
  and are refused as described in User Story 3.
- **The same client resends a bus message with the same id.** Handled as today: it updates that
  client's own job.
- **The default client is disabled.** Messages without a client are refused with the client-disabled
  code until it is enabled again, exactly as messages naming it would be.
- **The default client setting names a client that is not registered.** Messages without a client are
  refused with the validation-failed code; the service still starts, and its health report is not
  affected, so an operator can register the client afterwards.
- **A client is disabled.** Its templates and jobs are kept, unchanged and hidden from every other
  client. Re-enabling it restores access.
- **File links.** The file a job produces belongs to the job's client. The link to it keeps working as
  today for whoever holds it, until feature 005 (private, expiring files) makes links expire and
  unguessable.
- **Job retention.** Unchanged: a job is readable for the configured retention period after its last
  update, then answers not found, whoever asks.

## Requirements *(mandatory)*

### Functional Requirements

**Ownership**

- **FR-001**: Every authored template and every job MUST have exactly one owner: the client on whose
  behalf it was created. The owner MUST never change after creation, other than by the one-time
  assignment in FR-016.
- **FR-002**: The file a job produces MUST be recorded as belonging to the job's owner.
- **FR-003**: Built-in templates MUST have no owner and MUST be visible, readable, previewable,
  duplicable and renderable by every client, and changeable by none.

**Templates**

- **FR-004**: Listing templates MUST return the calling client's authored templates and every
  built-in template, and nothing owned by another client.
- **FR-005**: Reading, editing, publishing, duplicating and deleting a template by key MUST consider
  only the calling client's authored templates and the built-in templates. A key owned only by another
  client MUST be answered exactly as a key that exists nowhere.
- **FR-006**: Template keys MUST be unique within a client, ignoring letter case, and MUST NOT take a
  built-in template's key. The same key MAY exist once in each client.
- **FR-007**: Where a client owns a template with a built-in template's key, that client MUST see and
  render its own template in place of the built-in one; other clients are unaffected.

**Jobs**

- **FR-008**: When a job is submitted, the template MUST be resolved as: the calling client's
  published authored template with that key, otherwise the built-in template with that key, otherwise
  template not found.
- **FR-009**: Reading a job MUST succeed only for its owner. Any other caller MUST get the same answer
  as for a job id that never existed.

**Who the caller is**

- **FR-010**: Over the API, the calling client MUST be the client of the API key, or, for the operator,
  the client they act as (feature 004). The service MUST NOT accept any other way of naming a client.
- **FR-011**: Template and job operations by the operator acting as no client MUST be refused with the
  validation-failed code and a message saying a client must be chosen.
- **FR-012**: Template and job operations by the operator acting as an unregistered client MUST be
  refused with the client-not-found code. Acting as a disabled client MUST be allowed.

**Bus**

- **FR-013**: A job request message MUST be able to name the client it is for. The addition MUST be
  optional, so messages from publishers using the previous message package version are still accepted
  and understood.
- **FR-014**: A message MUST be handled for: the client it names, or, if it names none, the configured
  default client. With neither, it MUST be answered with a failure response carrying the
  validation-failed code, and nothing rendered. A named client that is unregistered or malformed MUST
  give the validation-failed code; a disabled one MUST give the client-disabled code.
- **FR-015**: A message whose job id belongs to another client's job MUST be answered with the
  validation-failed code, MUST NOT change that job and MUST NOT reveal anything about it.

**Existing data**

- **FR-016**: On the first start after the upgrade, every authored template and job that has no owner
  MUST be given one: the configured default client. If that client is not registered, it MUST be
  registered at that moment, with its id as its name. If such templates or jobs exist and the default
  client is not configured, or is not a valid client id, the service MUST refuse to start, with an
  error naming the setting to fill in.
  This MUST happen once, however many instances start together, and MUST NOT change any template's
  content, status or revision history. A deployment with no such templates or jobs MUST start
  whether or not a default client is configured.

**Observability and documentation**

- **FR-017**: Log entries written while handling a bus message MUST carry the client it was handled
  for, as API requests already do since feature 002.
- **FR-018**: The message contract change, the default client setting and the "who can see what" rules
  MUST be documented where products and operators read them, including that any holder of broker
  credentials can name any client in a message, so only trusted product backends may be given them.

### Key Entities

- **Authored template**: a template a client created; now has an owner. Its key is unique within that
  owner, ignoring letter case.
- **Built-in template**: shipped with the service; no owner; shared read-only by every client.
- **Job**: one PDF request and its outcome; now has an owner, which decides who can read it and which
  templates it can use.
- **Generated file**: the PDF a job produced; belongs to the job's owner.
- **Job request message**: the bus message that submits a job; can now name a client.
- **Default client**: an optional setting naming the client assumed for messages that name none.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every template and job operation, a client asking about another client's template or
  job gets an answer identical, apart from the trace id, to the answer for one that does not exist: 0
  differences across the full set of operations tested.
- **SC-002**: 0 template or job operations by one client change, reveal or render anything owned by
  another client.
- **SC-003**: Two clients each holding a template with the same key get their own template's output on
  100% of jobs.
- **SC-004**: Every built-in template is listed for and renderable by 100% of clients.
- **SC-005**: Upgrading a deployment that has existing templates and jobs needs exactly one manual
  step, naming the default client in configuration; every existing template then renders the
  same content for its owner as before the upgrade. Without that step the upgrade changes nothing and
  the service says what is missing.
- **SC-006**: A product publishing with the previous message package version, on a deployment with a
  default client configured, sees no change: 100% of its messages are processed as before.
- **SC-007**: Submitting a job and reading its status take no perceptibly longer than before this
  feature (within 10% at the same load).

## Assumptions

- **Feature 002 and 004 are in place**: every API caller is identified as a client, or as the operator
  acting as one. This feature only decides what each of them can see.
- **Operator-wide views come later.** Seeing every client's jobs at once is feature 010 (server-side
  jobs list). Until then the operator always works inside one client.
- **File links are unchanged** until feature 005. Ownership of files is recorded now so that 005 can
  enforce it; until then a link works for whoever holds it, as today.
- **Trust on the bus is the broker's.** The service cannot check that a message really comes from the
  client it names; the operator gives broker credentials only to trusted product backends. This is a
  known limit of this phase, documented (FR-018), not solved here.
- **A client's data is kept when it is disabled** and is never deleted by this feature. Deleting a
  client's data is out of scope.
- **Moving a template from one client to another is out of scope.** Template packs (feature 013) will
  let an operator export from one and import into another.
- **The console needs no change**: since feature 004 it sends the chosen client and keeps its job
  history per client, so it shows the right data once the service separates it.
- **Job retention stays as configured today** and applies equally to every client.
- **The upgrade is announced.** Release notes tell operators of existing deployments to choose the
  client id for the product that uses today's templates and name it as the default client before
  upgrading. This is the one manual step the upgrade needs, and it is not automatic because only the
  operator knows which product those templates belong to. The client need not be registered first:
  every released version (1.0.0) predates clients, and a service that refuses to start could not be
  used to register one.

## Clarifications

### Session 2026-11-04

- Q: Who owns templates and jobs from before clients existed when no default client is configured?
  → A: They are assigned to the configured default client; if it is not set or not registered, the
  service refuses to start with an error naming the setting (FR-016).
- Amended during planning (2026-11-05): an unregistered default client no longer refuses to start.
  Every released deployment (1.0.0) has no clients, and clients can only be registered through the
  running service, so the named default client is registered during the upgrade instead. An unset or
  malformed setting still refuses to start, so the operator still chooses the owner explicitly.
