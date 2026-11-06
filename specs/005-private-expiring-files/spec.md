# Feature Specification: Private, Expiring Files

**Feature Branch**: `005-private-expiring-files`

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "005". Backlog entry
(`docs/blocks/03-planning.md#005-private-expiring-files`): "A link to a generated PDF must only work for
a limited time and must not be guessable or usable to fetch another client's files. Products receive a
link that works without extra credentials until it expires, and can ask for a fresh link for a job they
own. Files are deleted after a retention period the operator configures."

## User Scenarios & Testing *(mandatory)*

When a job succeeds, the service stores the PDF and hands out a **file link**: in the job's status, and
in the reply sent on the message bus. Today that link never expires and works for anyone who has it,
and since feature 003 each file belongs to the client whose job produced it, but nothing enforces that.
Files are never deleted. This feature makes links private and short-lived, and gives files a lifetime.

Terms used below:

- **File link**: the address a product, or whoever the product passes it to (an end user, an email
  block), uses to download a PDF with no API key.
- **Signed link**: a file link that carries an expiry time and a proof, made with a secret only the
  service knows, that the service issued it for exactly that file and that expiry. Changing any part of
  it makes it stop working.
- **Link lifetime**: how long a newly issued link works, set by the operator. Default 1 hour.
- **Retention period**: how long a stored PDF is kept after it was created, set by the operator.
  Default 30 days.
- **Fresh link**: a new signed link for a job's file, issued on request, valid for a full link
  lifetime from that moment.
- **Does not work**: the answer the service already gives for a file that does not exist. A link that
  is expired, altered, unsigned (once signatures are required) or points at a deleted file gets this
  same answer, with nothing saying which of these it was.
- **Signature switch**: an operator setting that turns signed links on. While it is off, the service
  issues and accepts links exactly as before this feature (unsigned, no expiry), so existing
  integrations keep working while they move over; while it is on, it issues only signed links and
  refuses unsigned ones. Off by default in the first release that has it; on by default in the
  following release.

### User Story 1 - A link works for its holder, and only until it expires (Priority: P1)

The shop submits an invoice job. When it succeeds, the shop receives a link and puts it in an email to
its customer. The customer clicks it within the hour and gets the PDF without logging in to anything.
The next day the same link no longer works. Someone who sees the link cannot change it to reach a
different file, and nobody can build a working link to a file without being given one.

**Why this priority**: this is the feature. Today a link leaked once (in an email, a log, a browser
history) gives access to that PDF forever, and every file id is a link waiting to be found.

**Independent Test**: with signatures required, generate a PDF as client A and take its link. Download
it without a key: the PDF comes back. Change the file id, the expiry time or the signature in the link,
one at a time: each does not work. Remove the expiry and signature: does not work. Wait past the link
lifetime (or move the clock): the original link does not work. Each failing answer is identical to the
answer for a file id that never existed.

**Acceptance Scenarios**:

1. **Given** a succeeded job, **When** its link is followed within the link lifetime with no
   credentials, **Then** the PDF is returned.
2. **Given** a link, **When** it is followed after its expiry time, **Then** it does not work.
3. **Given** a link for file X, **When** the file id in it is replaced by file Y's id (of the same or
   another client), **Then** it does not work and file Y is not revealed.
4. **Given** a link, **When** its expiry time is moved later or its signature is changed or removed,
   **Then** it does not work.
5. **Given** signatures are required, **When** an unsigned link (as issued before this feature) is
   followed, **Then** it does not work.
6. **Given** any of the failures above, **When** the answers are compared with the answer for a file
   that never existed, **Then** they are identical, apart from the trace id every error carries.
7. **Given** a successful download, **When** the response is inspected, **Then** it tells shared caches
   not to store it, so the PDF is not served to others after the link has expired.

---

### User Story 2 - A product gets a fresh link for its own job (Priority: P1)

A customer opens an old order in the shop a day later and asks for the invoice again. The shop still
has the job id, asks the PDF service for a fresh link for that job, and hands it to the customer. The
ERP, asking with the shop's job id, is told the job does not exist. Reading a job's status also always
returns a link that is valid from that moment, together with when it expires.

**Why this priority**: expiring links are only usable if a product can get a working one again whenever
it needs it, without rendering the PDF a second time.

**Independent Test**: as client A, submit a job and let it succeed. Wait past the link lifetime. Ask for
a fresh link as A: a link and its expiry time come back, and the link downloads the same PDF. Ask as B
with A's job id: the answer is identical to the answer for a job id that never existed. Read the job's
status as A twice, some time apart: each answer carries a link and an expiry time a full lifetime from
when it was read.

**Acceptance Scenarios**:

1. **Given** a succeeded job owned by the caller whose file is still kept, **When** the caller asks for
   a fresh link, **Then** it receives a working link and the time it expires, and no PDF is rendered.
2. **Given** a job owned by another client, **When** the caller asks for a fresh link, **Then** the
   answer is identical to the answer for a job that does not exist.
3. **Given** a job owned by the caller that is pending, processing or failed, **When** the caller asks
   for a fresh link, **Then** it is refused with a stable error code saying the job has no file.
4. **Given** a job owned by the caller whose file has been deleted after the retention period, **When**
   the caller asks for a fresh link, **Then** it is refused with a stable error code saying the file has
   expired, distinct from the "no file" and "not found" answers.
5. **Given** a succeeded job, **When** its status is read, **Then** the answer carries a link valid for a
   full link lifetime from the time of reading, and the time it expires.
6. **Given** a job submitted through the message bus, **When** it succeeds, **Then** the reply carries a
   signed link and the time it expires; a consumer built against the previous message package still
   reads the reply as before.
7. **Given** a fresh link has been issued, **When** an earlier link for the same file is followed before
   its own expiry, **Then** it still works: issuing a link never revokes another.

---

### User Story 3 - Files are deleted after the retention period (Priority: P2)

The operator sets files to be kept for 30 days. PDFs older than that are deleted from storage without
anyone doing anything, so disk use stays bounded and old documents do not linger. A product that reads
a job whose file has gone is told plainly that the file has expired rather than being given a link that
will not work.

**Why this priority**: today files are never deleted, so storage grows without limit and every PDF ever
produced stays on disk. It ranks below the links because it is about housekeeping and data minimisation,
not about who can read a file.

**Independent Test**: set a short retention period. Generate a PDF, then move past the retention period.
Within the documented delay the file is gone from storage; its old links do not work; reading the job
(if it is still kept) shows the file as expired and carries no link; asking for a fresh link is refused
as expired. A file younger than the retention period is untouched.

**Acceptance Scenarios**:

1. **Given** a stored PDF older than the retention period, **When** the deletion runs, **Then** the PDF
   is removed from storage.
2. **Given** a stored PDF younger than the retention period, **When** the deletion runs, **Then** it is
   untouched.
3. **Given** a job whose file has been deleted, **When** its status is read, **Then** it reports the file
   as expired and carries no link; the rest of the job (status, template, times, size) is unchanged.
4. **Given** the deletion fails part way (storage unavailable, service stopped), **When** it next runs,
   **Then** it carries on and eventually removes every PDF past its retention period, without stopping
   the service or affecting jobs being processed.
5. **Given** several instances of the service, **When** the deletion runs on each, **Then** every expired
   file is removed once and no instance fails because another removed it first.

---

### User Story 4 - The operator downloads from the console without links (Priority: P2)

The operator, signed in to the console and working in the shop's client, opens a job and clicks
Download. The PDF downloads. The console never shows a link that could be copied and used by someone
who is not signed in, and it cannot download another client's file.

**Why this priority**: once links are signed, the console's current download (an unsigned link built
from the file id) stops working when signatures are required. The console must keep working, and it
already has a signed-in operator, so it needs no link at all.

**Independent Test**: sign in to the console, choose client A, open a succeeded job of A's and download
it: the PDF arrives. Copy the request the console made and replay it without the console's sign-in:
refused. Ask for one of B's jobs' files while working in A: the answer is identical to "job not found".

**Acceptance Scenarios**:

1. **Given** a signed-in operator working in client A, **When** they download a succeeded job of A's,
   **Then** the PDF arrives, with signatures required or not.
2. **Given** a job of client B, **When** the operator working in client A asks for its file, **Then** the
   answer is identical to the answer for a job that does not exist.
3. **Given** a job whose file has been deleted, **When** the operator opens it, **Then** the console shows
   that the file has expired and offers no download.
4. **Given** the authenticated download, **When** a product calls it with its own API key for its own
   job, **Then** it also works, so a product that prefers not to pass links around can fetch the PDF
   itself.

---

### User Story 5 - Existing integrations keep working through the upgrade (Priority: P3)

A company upgrades. Products that store links, from before the upgrade or after it, and products built
against the previous message package, keep working while the signature switch is off: links look and
behave as before. The release notes tell the operator to set the signing secret, and that the next
release will require signatures. When the operator turns the switch on, new links are signed and
expire, old unsigned links stop working, and nothing else changes.

**Why this priority**: required by the rule that published behaviour changes additively; the risk is
limited to the upgrade window.

**Independent Test**: start the upgraded service on a database and storage from the previous version,
with the signing secret set and the switch off. An unsigned link issued before the upgrade still
downloads its PDF, and new jobs return the same kind of link, with no expiry time. Turn the switch on:
new jobs return signed links with an expiry time; the old unsigned links do not work; signed links
still do.

**Acceptance Scenarios**:

1. **Given** the switch is off, **When** an unsigned link is followed, **Then** the PDF is returned, as
   before this feature.
2. **Given** the switch is off, **When** a job succeeds or a fresh link is asked for, **Then** the link
   returned is unsigned, has no expiry time, and works until the file is deleted, as before this
   feature.
3. **Given** the switch is on, **When** a job succeeds or a fresh link is asked for, **Then** the link
   returned is signed and carries its expiry time.
4. **Given** the switch is turned off again after being on, **When** a signed link issued meanwhile is
   followed before its expiry, **Then** it still works; after its expiry, or altered, it does not.
5. **Given** the signing secret is missing or too short, **When** the service starts, **Then** it refuses
   to start with a message naming the setting, and the message does not contain any secret.
6. **Given** files stored before the upgrade, **When** the retention period is measured, **Then** it is
   measured from when each file was created, so an upgrade may delete files already older than the
   retention period; the release notes say so.

---

### Edge Cases

- **A link followed exactly at its expiry time**: does not work. A link is valid strictly before its
  expiry time.
- **Clocks of several instances differ slightly**: a link issued by one instance works on any other; the
  service tolerates no more than a small, documented skew and never extends a link's life by more.
- **The operator changes the signing secret**: every outstanding link stops working at once; products
  get fresh links on request. This is the documented way to revoke every link after a leak.
- **The operator changes the link lifetime**: links already issued keep the expiry they were issued
  with; only new links use the new lifetime.
- **The operator shortens the retention period**: files already older than the new period are deleted
  at the next run.
- **A link is followed while its file is being deleted**: the download either completes with the whole
  PDF or does not work; it is never cut short with a partial file presented as complete.
- **The job's client has been disabled** (feature 002): links already issued still work until they
  expire (the holder may be an end user, not the client); asking for a fresh link with the client's
  key is refused because the key no longer works; the operator can still download from the console.
- **A file is missing from storage before its retention period** (lost volume, manual deletion): its
  links do not work; reading the job reports the file as expired; nothing fails.
- **A very long or malformed link** (huge expiry value, non-numeric expiry, odd characters in the
  signature): does not work, with the same answer as any other failing link, and no error is logged as
  a fault of the service.
- **A succeeded job reaches the end of job retention while its file is still kept**: the job stays
  readable, and a fresh link can still be requested, until the file is deleted (FR-019).
- **A file is missing from storage early, so its job is marked expired before the retention period
  ends**: the job is then removed one job retention period later, like any other job whose file has
  expired (FR-019).

## Requirements *(mandatory)*

### Functional Requirements

**Signed links**

- **FR-001**: While the signature switch is on, every file link the service issues MUST be a signed link
  carrying its expiry time, in job status answers, fresh-link answers and bus replies alike. While it is
  off, links are issued as before this feature (FR-023).
- **FR-002**: A signed link MUST work, with no API key or other credential, from when it is issued until
  its expiry time, and MUST NOT work at or after it.
- **FR-003**: A link MUST NOT work if any part of it (file id, expiry time, signature) has been changed,
  added or removed. The proof MUST be impossible to produce without the service's signing secret, and
  MUST be checked in a way that does not reveal, through response timing, how close a guess was.
- **FR-004**: A link that does not work MUST get an answer identical, apart from the trace id, to the
  answer for a file that never existed, whatever the reason (expired, altered, unsigned while signatures
  are required, file deleted, file never existed).
- **FR-005**: The expiry time of a link MUST be the time it was issued plus the link lifetime in force
  at that moment. Issuing a new link MUST NOT change or revoke any other link.
- **FR-006**: A successful download MUST mark the response as private to its recipient, so shared caches
  do not keep and re-serve the PDF.
- **FR-007**: The signing secret MUST never appear in logs, errors, responses, telemetry or exports.
  Full signed links (including their signature) MUST NOT be written to the service's logs.

**Fresh links and job status**

- **FR-008**: A product MUST be able to ask for a fresh link for a job it owns, receiving the link and its
  expiry time. No PDF is rendered to answer it.
- **FR-009**: A fresh-link request for a job the caller does not own MUST get an answer identical to the
  answer for a job that does not exist (feature 003's rule).
- **FR-010**: A fresh-link request for an owned job that has no file (pending, processing or failed)
  MUST be refused with a stable error code meaning "this job has no file".
- **FR-011**: A fresh-link request for an owned job whose file has been deleted or is missing MUST be
  refused with a stable error code meaning "this job's file has expired".
- **FR-012**: Each read of a succeeded job's status MUST carry a link (valid for a full link lifetime from
  that read while the switch is on), its expiry time (none while the switch is off), and whether the file
  has expired. A job whose file has expired MUST carry no link.
- **FR-013**: The reply sent on the message bus for a succeeded job MUST carry a link and, while the
  switch is on, its expiry time. The expiry time MUST be a new, optional member of the reply, so a
  consumer built against the previous message package keeps working unchanged.

**Authenticated download**

- **FR-014**: A caller authenticated as a client (a product's key, or the operator acting as a client)
  MUST be able to download the PDF of a job it owns directly, without a link, subject to the same
  ownership rule as FR-009 and the same expired-file refusal as FR-011.
- **FR-015**: The console MUST download PDFs only through the authenticated download, and MUST show a
  job whose file has expired as expired, with no download offered.

**Retention**

- **FR-016**: Each stored PDF MUST be deleted from storage once it is older than the retention period,
  measured from when the file was created, no later than one hour after that.
- **FR-017**: Deletion MUST run without operator action, MUST survive restarts and partial failures by
  carrying on at the next run, MUST be safe when several instances run it at once, and MUST NOT slow or
  block job processing.
- **FR-018**: Deleting a file MUST NOT delete or change its job's record, apart from the job then
  reporting its file as expired.
- **FR-019**: A succeeded job's record MUST be kept for as long as its file is kept, even when job
  retention (1–168 hours after the job's last update, default 24) would otherwise remove it sooner. Its
  file's deletion counts as the job's last update, so the job then stays readable, reporting its file as
  expired, for the job retention period, and is removed after that. Jobs that never produced a file
  (pending, processing, failed) keep today's job retention unchanged.

**Configuration and upgrade**

- **FR-020**: The operator MUST be able to set the signing secret, the link lifetime, the retention
  period and the signature switch in configuration. Each MUST have a documented default, except the
  signing secret, which has none; each MUST be validated at start-up with a message naming the setting
  when it is invalid.
- **FR-021**: The service MUST refuse to start when the signing secret is missing or shorter than the
  documented minimum strength (at least 32 random bytes).
- **FR-022**: The link lifetime MUST be accepted between 1 minute and 7 days (default 1 hour), and the
  retention period between 1 and 3650 days (default 30 days).
- **FR-023**: While the signature switch is off, the service MUST issue unsigned links without an expiry
  time and accept them, exactly as before this feature; while it is on, it MUST issue only signed links
  and MUST NOT accept unsigned ones. A correctly signed, unexpired link MUST work whichever way the
  switch is set, and an altered or expired one MUST NOT. The switch is off by default in the first
  release that has it.
- **FR-024**: Every instance of the service MUST accept links issued by any other instance with the same
  signing secret.
- **FR-025**: The change MUST be documented: the configuration settings, the new error codes, the fresh
  link and authenticated download, the new reply member, the release-notes entry announcing that the
  next release turns signatures on by default, and the upgrade note that files older than the retention
  period are deleted once the upgraded service runs.

### Key Entities

- **Stored file**: a generated PDF. Belongs to one job and, through it, to one client (since feature
  003). Has a creation time, from which its retention is measured, and a size. Either present or expired
  (deleted, or missing from storage).
- **Signed link**: not stored. Made on demand from a file's id, an expiry time and the signing secret;
  checked on use from the same three.
- **Job** (existing): gains whether its file has expired. Its status answer gains the link's expiry time.
  A succeeded job is kept at least as long as its file (FR-019).
- **Signing secret**: a deployment-wide secret set by the operator; changing it invalidates every
  outstanding link.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With signatures required, 0 downloads succeed through a link that is expired, altered in
  any part, unsigned, or built without being issued by the service, across every variation tested.
- **SC-002**: Every failing link gets an answer identical, apart from the trace id, to the answer for a
  file that never existed: 0 differences across the failure reasons tested.
- **SC-003**: 0 fresh-link requests or authenticated downloads by one client return, or reveal the
  existence of, another client's file.
- **SC-004**: A product can obtain a working link for any of its succeeded jobs whose file is kept in a
  single request, 100% of the time, without the PDF being rendered again.
- **SC-005**: No PDF remains in storage more than 1 hour past its retention period, and no PDF younger
  than its retention period is deleted.
- **SC-006**: With the signature switch off, products integrated before this feature need 0 changes: their
  stored links and their message consumers keep working.
- **SC-007**: Upgrading needs exactly one manual step, setting the signing secret; without it the service
  says which setting is missing and changes nothing.
- **SC-008**: A full search of the service's logs, responses and telemetry after issuing, using and
  rejecting links finds the signing secret in 0 places and a full link signature in 0 log entries.
- **SC-009**: Downloading a PDF and reading a job's status take no perceptibly longer than before this
  feature (within 10% at the same load).
- **SC-010**: For 100% of succeeded jobs, a fresh link can be obtained at any time until the file's
  retention period ends, whatever the job retention setting.

## Assumptions

- **Feature 003 is in place**: every job, and so every file, has an owning client. This feature enforces
  that ownership; it does not record it.
- **Links are bearer links by design.** Whoever holds an unexpired link can download the file, without
  proving who they are. That is what lets a product pass it to an end user or another block. Limiting
  the damage of a leak is the job of the short lifetime and of changing the signing secret.
- **One link lifetime for the whole deployment.** A product cannot ask for a longer or shorter link per
  request; per-client or per-request lifetimes are out of scope.
- **One signing secret at a time.** Changing it invalidates every outstanding link at once; overlapping
  old and new secrets during a rotation is out of scope.
- **Storage stays on the service's own disk.** Moving files to shared object storage, needed to run
  several instances that all serve downloads, is separate work; this feature's rules apply to whatever
  storage is used.
- **Webhooks (feature 007) will carry links too.** They get a fresh link when each delivery is produced,
  following FR-005; nothing in this feature depends on webhooks.
- **Deleting files on demand** (by a product or the operator, before the retention period) is out of
  scope.
- **The download link format is the service's own convention**, shared by every block (a block returns a
  signed file URL that another accepts by URL). Its exact shape is decided in planning, following
  `docs/blocks/04-backend.md#files`.
- **More job records are kept.** With the defaults, succeeded jobs now stay readable for about 30 days
  instead of 24 hours, so the job store grows roughly in proportion. The documentation of the job
  retention setting is updated to say it no longer bounds succeeded jobs whose files are kept.

## Clarifications

### Session 2026-11-05

- Q: How long is a job's record kept compared with its file, given job records are kept 1–168 hours
  (default 24) and files 30 days by default? → A: A succeeded job's record is kept as long as its file;
  once the file is deleted the job reports it as expired for one more job retention period and is then
  removed. Jobs without a file keep today's retention (FR-019).
- Amended during planning (2026-11-05): while the signature switch is off, new links are issued unsigned
  and without expiry, as before, instead of being signed. Signing them regardless would change the
  meaning of the bus reply's `FileUrl` ("does not expire") for consumers on the previous package, which
  Principle VII forbids within a major version, and `docs/blocks/04-backend.md` already intends this
  ("`FileUrlExpiresAt` … null while signing is off"). FR-001, FR-012, FR-013, FR-023 and Story 5 updated.
