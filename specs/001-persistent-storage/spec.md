# Feature Specification: Persistent Storage for Jobs and Authored Templates

**Feature Branch**: `001-persistent-storage`

**Created**: 2026-11-03

**Status**: Draft

**Input**: User description: "Jobs and authored templates must survive a restart and be visible from any instance of the service. Today jobs live in memory for 24 hours and templates are files on one machine, so a restart loses job history and a second instance cannot be run. Move both to a database without changing any API response, message contract or console behaviour. Existing authored templates on disk must be imported once, automatically, on first start."

## User Scenarios & Testing *(mandatory)*

The people affected are the **operator** (who restarts, upgrades and scales the service, and authors
templates in the console) and the **product developer** (whose system submits jobs and polls them).
Neither should notice this change except that things stop disappearing.

### User Story 1 - Job status survives a restart (Priority: P1)

A product submits a PDF job and polls for its result. Meanwhile the service is restarted, for an
upgrade or after a crash. When the product polls again, it gets the job's real status (finished,
failed or still pending), not "not found". The operator can also open any recent job in the console
after the restart.

**Why this priority**: today every restart, including every deployment, silently loses every job's
status, so products that poll see jobs vanish. This is the most frequent failure the change removes.

**Independent Test**: submit a job, wait for it to finish, restart the service, then request the
job's status: it is returned unchanged.

**Acceptance Scenarios**:

1. **Given** a job that finished successfully before a restart, **When** its status is requested
   after the restart, **Then** the same status, result and file reference are returned as before
   the restart.
2. **Given** a job that failed before a restart, **When** its status is requested after the restart,
   **Then** the same failure code and message are returned.
3. **Given** a job that was accepted but not yet finished when the service stopped, **When** the
   service starts again and processes it, **Then** its status moves on to finished or failed, and
   it is never reported as "not found".

---

### User Story 2 - Authored templates survive a restart and an upgrade (Priority: P1)

The operator writes a template in the console, saves drafts and publishes it. After the service is
restarted or upgraded to a new version, every authored template is still there with the same draft,
the same published version, the same status and the same revision, and jobs keep rendering the
published version.

**Why this priority**: losing or corrupting a published template stops every product that renders
it, so this must be as safe as job status. It is P1 alongside Story 1.

**Independent Test**: create, edit and publish a template; restart the service; open the template and
render a job with it: both behave exactly as before the restart.

**Acceptance Scenarios**:

1. **Given** a published template with later unpublished draft edits, **When** the service restarts,
   **Then** the template shows as modified, the draft keeps the edits, and jobs still render the
   published version.
2. **Given** two operators editing the same template, **When** the second saves a change based on an
   older revision, **Then** the save is refused as a conflict, exactly as it is today.

---

### User Story 3 - Existing templates are carried over automatically (Priority: P2)

A deployment that already has authored templates upgrades to the version with this feature. On its
first start, every authored template it had is available without the operator doing anything. On
later starts, nothing is imported again, so templates edited or deleted after the upgrade stay as
the operator left them.

**Why this priority**: without it, every existing deployment loses its templates on upgrade. It is
P2 only because it runs once per deployment, while Stories 1 and 2 matter on every restart.

**Independent Test**: place previously authored templates where the current version keeps them,
start the new version, and list templates: all are present with their drafts, published versions and
statuses. Delete one, restart: it stays deleted.

**Acceptance Scenarios**:

1. **Given** a deployment with authored templates from the previous version, **When** the new
   version starts for the first time, **Then** every readable template is available with its draft,
   published version, status and revision intact.
2. **Given** the import has already happened, **When** the service restarts, **Then** nothing is
   imported again, even if the old template files are still present.
3. **Given** one of the old template files cannot be read, **When** the import runs, **Then** the
   other templates are still imported and the operator can see which file was skipped and why.
4. **Given** the import has run, **When** the operator looks at the old template files, **Then** they
   are still there, unchanged, so the upgrade can be rolled back.

---

### User Story 4 - A second instance can be run (Priority: P3)

The operator runs two instances of the service behind a load balancer. A product submits a job to
one instance and polls through the load balancer, reaching either instance: it always gets the job's
status. The operator edits a template through one instance and every instance renders the published
version.

**Why this priority**: this is what the change makes possible rather than what operators need today;
one instance is the norm for now. It is P3 and is limited to job status and templates (see Scope).

**Independent Test**: run two instances sharing the same storage; submit through one, poll through
the other; publish a template through one and render it through the other.

**Acceptance Scenarios**:

1. **Given** two instances, **When** a job submitted to one is polled through the other, **Then**
   its current status is returned.
2. **Given** two instances, **When** a template is published through one, **Then** the next job
   rendered by either uses the newly published version.
3. **Given** two instances starting at the same moment on a deployment that still has old template
   files, **When** both start, **Then** the templates are imported exactly once.

### Edge Cases

- **Storage unavailable at start-up or while running**: the service reports itself not ready (as it
  already does when the message broker is down) and refuses new jobs with a clear "temporarily
  unavailable" error rather than accepting work it cannot record.
- **A job accepted but never queued, or queued but never recorded**: must not happen. A job is
  accepted only if it is both recorded and queued; if either fails, the caller receives an error and
  no orphan exists.
- **Jobs in memory at the moment of upgrade**: jobs accepted by the previous version and still
  unfinished when it stops are not carried over (they lived only in memory). This is a one-time loss
  on the upgrade itself, documented in the release notes.
- **Old template files with a key that is now a built-in template**: imported templates never replace
  built-in ones; such a file is skipped and reported, as any other unreadable file.
- **Concurrent saves to the same template from two instances**: the revision check refuses the
  stale one, so neither edit is silently lost.
- **Very large histories**: job records past the retention period are removed automatically, so
  storage does not grow without bound.
- **Clock differences between instances**: job timestamps stay consistent and ordered regardless of
  which instance recorded them.

## Requirements *(mandatory)*

### Functional Requirements

**Jobs**

- **FR-001**: The system MUST keep every job's status (pending, succeeded, failed), timestamps,
  result reference and failure details across restarts and upgrades of the service.
- **FR-002**: The system MUST make a job's status available from every running instance, whichever
  instance accepted or processed it.
- **FR-003**: The system MUST accept a job only when it has been both recorded and queued for
  processing; a failure of either MUST leave neither, and the caller MUST receive an error.
- **FR-004**: The system MUST keep finished jobs for a configurable retention period and remove
  them afterwards. The retention setting, its default of 24 hours and its allowed range of 1 to
  168 hours stay exactly as they are today, so a job stops being available at the same moment it
  does now; the only difference is that a restart no longer shortens it.
- **FR-005**: A job that the processing side reports as finished or failed MUST be updated in the
  shared record, so a later status request from any instance reflects it.

**Authored templates**

- **FR-006**: The system MUST keep every authored template (key, draft, published version, status,
  revision and timestamps) across restarts and upgrades.
- **FR-007**: The system MUST make template changes made through one instance visible to every
  instance, including which version jobs render.
- **FR-008**: The system MUST keep today's optimistic concurrency behaviour: a save or publish based
  on a stale revision is refused as a conflict, including when the competing change came through a
  different instance.
- **FR-009**: Built-in templates MUST continue to come from the templates shipped with the service
  and MUST NOT be stored or editable alongside authored ones.

**Import of existing templates**

- **FR-010**: On its first start against empty storage, the system MUST import every readable
  authored template from the location the previous version kept them, preserving draft, published
  version, status, revision and timestamps.
- **FR-011**: The import MUST run at most once per deployment, including when several instances
  start at the same time; later starts MUST NOT re-import, even if the old files remain.
- **FR-012**: The import MUST NOT modify or delete the old template files.
- **FR-013**: A file that cannot be read or would clash with a built-in template MUST be skipped
  without stopping the import, and the operator MUST be able to see each skipped file and the
  reason in the service's logs.

**Unchanged behaviour**

- **FR-014**: Every HTTP response shape, status code and error code of the job and template
  endpoints MUST stay exactly as it is today.
- **FR-015**: The message contracts (request and response messages and their error codes) MUST NOT
  change.
- **FR-016**: The console MUST behave exactly as it does today, with no changes required to it.
- **FR-017**: When storage is unavailable, the service MUST report itself not ready through its
  existing readiness check, and its liveness check MUST still report the process as alive.

### Key Entities

- **Job**: one request to produce a PDF. Identified by its job id; records which template was used,
  its status, when it was accepted and finished, and either a reference to the produced file (with
  its size) or a failure code and message.
- **Authored template**: a template written through the console, identified by its key. Has a draft
  (what is being edited), optionally a published version (what jobs render), a revision number that
  increases with every change, and created, updated and published timestamps. Its status (draft,
  published, modified) is derived from whether the draft differs from the published version.
- **Template import record**: the fact that the one-time import has happened for this deployment,
  when, and which files were imported or skipped. Ensures the import never runs twice.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a restart or an upgrade, 100% of jobs within the retention period and 100% of
  authored templates are still available, with identical content.
- **SC-002**: With two instances running, 100% of status requests for a job return its current
  status, whichever instance receives the request.
- **SC-003**: Upgrading a deployment that has existing authored templates requires zero manual steps
  from the operator to keep them.
- **SC-004**: Every existing automated check of the job and template behaviour, and of the message
  contracts, passes unchanged.
- **SC-005**: Products integrated before this change need zero changes to keep working.
- **SC-006**: Checking a job's status and opening a template feel as fast as they do today: a status
  request still answers in well under a second under normal load.

## Assumptions

- **Scope**: only job status and authored templates move. Generated PDF files stay where they are
  today; making files downloadable from any instance is a separate feature (private, expiring files).
  Until then, a second instance can report a job's status but may not be able to serve its file.
- **No client separation yet**: there is still one set of templates and jobs for the whole
  deployment. Separating them per client is a later feature, and this one must not make that harder.
- **Storage is a single shared store per deployment**, reachable by every instance; providing it is
  part of deploying the service, as the message broker is today.
- **Old template files** are read from the location the previous version used, as configured today.
  They are left in place after import so an upgrade can be rolled back.
- **Unfinished in-memory jobs** at the moment of the upgrade are not recoverable; this one-time loss
  is acceptable and will be stated in the release notes.
- **The console's own job history** (kept in the browser) stays as it is; showing server-side history
  in the console is a later feature.
- **Retention of finished jobs** applies to their status records only, not to the produced files,
  whose retention is unchanged.
- **Retention stays at 24 hours by default** (decided 2026-11-03). Longer history is deliberately
  left to the later feature that shows server-side job history in the console, which can raise the
  default when it needs to.

## Clarifications

### Session 2026-11-03

- Q: Now that jobs survive restarts, should the default retention stay at 24 hours or be extended?
  → A: Keep 24 hours and the existing setting (1–168 hours) unchanged.
