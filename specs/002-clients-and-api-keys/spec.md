# Feature Specification: Clients and API Keys

**Feature Branch**: `002-clients-and-api-keys`

**Created**: 2026-11-03

**Status**: Draft

**Input**: User description: "Every call to the PDF block's API must identify which client (product) is making it. The operator registers clients and issues API keys for them; a product sends its key with each request. Keys can be named, scoped, expired and revoked, and a revoked key stops working within a minute. A key is shown only once, when created, and the service never stores it in a form that could be read back. The operator has one admin key, set in configuration, that can manage clients and keys. Health endpoints stay open. Requests without a valid key are refused with a clear, uniform error."

## User Scenarios & Testing *(mandatory)*

Three kinds of people are involved. The **operator** runs the service and decides who may use it.
The **product developer** builds a product (an e-commerce back end, an ERP) that calls the service.
The **service** is what refuses everyone else. Today the service answers anyone who can reach it, so
the first release that has this feature is also the first one that is safe to put on a network.

A **client** is a product as the service sees it: the unit that is given keys. A **key** is the secret
a product sends with every request.

### User Story 1 - The operator registers a client and issues it a key (Priority: P1)

The operator, holding the admin key, registers a client for a new product ("E-commerce") and issues
it a key for production. The key is shown on screen once, with a warning to copy it now. The operator
puts it in the product's configuration. If the operator loses it, they issue a new one and revoke the
old; nothing can show it again.

**Why this priority**: nothing else in this feature can be used until a client and a key exist, and
the one-time display is the property everything else about keys depends on.

**Independent Test**: with only the admin key, register a client, issue a key, and use that key on a
protected call: it is accepted. Ask the service for the key again, by any means: it cannot be given.

**Acceptance Scenarios**:

1. **Given** the admin key, **When** the operator registers a client with an id and a name, **Then**
   the client exists, is active and appears in the list of clients.
2. **Given** a registered client, **When** the operator issues a key with a name, one or more
   permissions and no expiry, **Then** the full key is shown once, together with its short public
   identifier, and the key works immediately.
3. **Given** a key was issued earlier, **When** the operator lists the client's keys, **Then** each
   shows its name, public identifier, permissions, dates and state, and never the key itself.
4. **Given** the operator tries to register a client with an id already in use, or an id with
   characters not allowed, **Then** it is refused with a message saying which rule broke.

---

### User Story 2 - A product calls the service with its key and is identified (Priority: P1)

A product sends its key with each request. The service recognises the client behind the key, lets the
request through, and records who made it. From the product developer's side nothing else changes:
the same endpoints, the same request bodies and the same responses, plus one header.

**Why this priority**: this is the point of the feature. Without it, keys exist but protect nothing.

**Independent Test**: call each protected endpoint with a valid key of sufficient permission: the
response is exactly what it was before this feature. The operator's logs show which client made each call.

**Acceptance Scenarios**:

1. **Given** a key with permission to submit jobs, **When** its product submits a job, **Then** the
   job is accepted exactly as before and the log entries for that request name the client and the
   key's public identifier.
2. **Given** a valid key, **When** the product polls a job it submitted, **Then** it gets the same
   response as before.
3. **Given** two clients, **When** each calls the service, **Then** each call is attributed to its own
   client in the logs.

---

### User Story 3 - Everyone else is refused, the same way every time (Priority: P1)

A request with no key, a wrong key, a revoked key or an expired key is refused before any work is
done, with the service's standard error format and a stable machine-readable code. The refusal for a
wrong, revoked and expired key is the same, so it tells an attacker nothing about which keys exist.
The health endpoints stay open so load balancers and orchestrators keep working.

**Why this priority**: refusing consistently is half of the feature; a protected service that leaks
which keys are real, or whose health probes fail, is not usable.

**Independent Test**: call each protected endpoint with no key, a made-up key, a revoked key and an
expired key: the first answers "authentication required" and the other three give identical answers.
Call the two health endpoints with no key: they answer normally.

**Acceptance Scenarios**:

1. **Given** no key, **When** any protected endpoint is called, **Then** the request is refused as
   unauthenticated with the code `AUTH_REQUIRED`, and nothing is queued, stored or rendered.
2. **Given** a key that was never issued, one that was revoked, and one that has expired, **When**
   each is used, **Then** the three refusals are indistinguishable: same status, same code
   (`AUTH_INVALID_KEY`), same wording.
3. **Given** a key that is malformed (empty, far too long, repeated, with stray whitespace), **When**
   it is used, **Then** it is refused as an invalid key, and the service does not fail.
4. **Given** no key, **When** the health endpoints are called, **Then** they answer exactly as they do
   today.
5. **Given** a refusal, **Then** its body carries a trace id the operator can find in the logs, and
   never contains the key that was sent.

---

### User Story 4 - The operator revokes a key or disables a client, and it takes effect within a minute (Priority: P2)

A key leaks, a product is retired, or a contract ends. The operator revokes the key, or disables the
whole client, and from then on the service refuses it. This holds on every instance of the service
within one minute. A key can also be issued with an expiry, after which it stops working by itself.

**Why this priority**: it is what makes a leaked key survivable. It is P2 only because it needs
Story 1 first.

**Independent Test**: use a key successfully, revoke it, wait one minute, use it again: it is refused.
Repeat by disabling its client, and by letting an expiry pass.

**Acceptance Scenarios**:

1. **Given** a key in use, **When** the operator revokes it, **Then** within one minute every call
   with it is refused as an invalid key, and the client's other keys keep working.
2. **Given** a client with several keys, **When** the operator disables the client, **Then** within a
   minute every one of its keys is refused with the code `CLIENT_DISABLED`; **and when** the operator
   enables the client again, **Then** the keys that were not revoked work again.
3. **Given** a key issued with an expiry, **When** the time arrives, **Then** it is refused from then
   on (with the same allowance of up to a minute for instances to notice), and the operator's list
   shows it as expired.
4. **Given** a key that was revoked, **Then** it cannot be un-revoked: the operator issues a new one.
5. **Given** a job was accepted before its key was revoked, **Then** the job still completes.

---

### User Story 5 - A key can be limited to what its product needs (Priority: P2)

The operator gives a product that only generates documents a key that can only generate documents. A
separate key, held by whoever maintains templates, can read or change templates. A key used for
something its permissions do not cover is refused with a different code from "invalid key", so its
owner knows the key is fine and the permission is what is missing.

**Why this priority**: it limits the damage of a leaked key. It is P2 because everything works with
all permissions granted.

**Independent Test**: issue a key with only the permission to submit jobs and use it to edit a
template: refused. Issue one with only template reading and use it to submit a job: refused.

**Acceptance Scenarios**:

1. **Given** a key with only the permission to submit jobs, **When** it is used to create, change,
   publish or delete a template, **Then** the request is refused as forbidden with the code
   `AUTH_SCOPE_MISSING` and nothing changes.
2. **Given** a key with only template-reading permission, **When** it lists or reads a template,
   **Then** it works; **and when** it submits a job, **Then** it is refused with `AUTH_SCOPE_MISSING`.
3. **Given** a key, **Then** its permissions are fixed at creation: to change them the operator
   issues a new key and revokes the old.
4. **Given** an attempt to issue a key with no permissions, or with one the service does not have,
   **Then** it is refused, naming the permissions that exist.

---

### User Story 6 - The operator can see which keys exist and when they were last used (Priority: P3)

Before revoking an old key, the operator wants to know whether anything still uses it. The list of a
client's keys shows when each was last used, so a key that has not been used for months can be
revoked with confidence, and one used a minute ago is not removed by mistake.

**Why this priority**: housekeeping. The service is fully protected without it.

**Independent Test**: use a key, then list the client's keys: its last-used time is within a minute of
the call. A key never used shows "never".

**Acceptance Scenarios**:

1. **Given** a key used just now, **When** the operator lists keys, **Then** the key's last use is
   shown, accurate to the minute.
2. **Given** a key never used, **Then** the list shows that it has never been used.
3. **Given** many calls per second with one key, **Then** recording last use does not slow those
   calls: it is a coarse timestamp, not a log of calls.

### Edge Cases

- **Callers that send no key stop working on upgrade.** Every product that calls the service today
  has no key, and the web console in this repository sends none. There is no "authentication off"
  switch to soften this (FR-023): the upgrade is released with the console sign-in, and the operator
  issues keys to existing products first. The release notes say so.
- **The database is unreachable.** Checking a key needs the database, so a request with a key then
  gets "storage unavailable" (503, retry in a few seconds), **not** "invalid key". A product must
  never conclude its key was revoked because of an outage. The admin key is read from configuration
  and does not depend on the database to be recognised.
- **Expiry in the past.** Issuing a key whose expiry has already passed is refused.
- **Two keys, one client.** Revoking one never affects the other, and both are attributed to the same
  client.
- **A disabled client's keys are not revoked.** They are refused while the client is disabled and work
  again when it is enabled; revocation is the permanent action, disabling is the reversible one.
- **A key is lost.** It cannot be recovered. The operator issues a new key and revokes the old one.
- **The admin key is lost or leaks.** There is one, set in configuration; replacing it means
  generating a new one, changing the configuration and restarting. Nothing else holds it.
- **Several instances.** Registering, issuing and revoking on one instance is seen by all of them, and
  the one-minute allowance for revocation applies across instances.
- **A burst of requests with invalid keys** (a scan, or a misconfigured product) must not slow down
  requests from valid clients, and must not fill the logs with anything that could be used as a key.
- **The key header sent twice**, or with surrounding whitespace, or a key for a different service
  (another block's prefix): refused as an invalid key.
- **A client id that is later wanted for something else.** Client ids are permanent and are never
  reused or renamed, because logs and, later, stored data refer to them.
- **A message published straight to the message bus** (not over HTTP) is not affected by this feature;
  see Assumptions.

## Requirements *(mandatory)*

### Functional Requirements

**The admin key**

- **FR-001**: The service MUST be configured with exactly one admin key, supplied to it as a value
  from which the key cannot be recovered. It MUST refuse to start without it, and the error MUST name
  the setting.
- **FR-002**: The operator MUST have a way to generate a new admin key together with the value to put
  in configuration. The key is shown once.
- **FR-003**: The admin key MUST authorise registering and disabling clients and issuing, listing and
  revoking keys, and MUST also authorise every other operation of the service.

**Clients**

- **FR-004**: The operator MUST be able to register a client with an id and a display name, to list
  clients, and to disable and re-enable a client.
- **FR-005**: A client id MUST be unique, MUST consist of lowercase letters, digits and hyphens,
  starting with a letter, up to 40 characters, and is permanent once registered.
- **FR-006**: Requests made with a key of a disabled client MUST be refused with the code
  `CLIENT_DISABLED`.

**Keys**

- **FR-007**: The operator MUST be able to issue a key for a client, giving it a name, one or more
  permissions from the list in FR-016, and optionally an expiry date and time in the future.
- **FR-008**: The full key MUST be shown exactly once, when it is issued. The service MUST store only
  a form from which the key cannot be recovered, and MUST NOT write a key to its logs, responses,
  exports or error messages.
- **FR-009**: Every key MUST have a short public identifier, shown in lists and logs, which cannot be
  used to make a call. A key's format MUST reveal which service it belongs to, so a leaked key can be
  traced to where it should be revoked.
- **FR-010**: The operator MUST be able to revoke a key. Revocation is permanent.
- **FR-011**: The operator MUST be able to list a client's keys, each with name, public identifier,
  permissions, creation date, expiry, state (active, expired or revoked) and last use, and never the
  key itself.
- **FR-012**: A key's last use MUST be recorded to the minute, without adding noticeable work to the
  calls it records.
- **FR-013**: A revoked, expired or disabled-client key MUST be refused on every instance of the
  service within 60 seconds of the action or the time.

**Requests**

- **FR-014**: Every request to the service's operations (jobs, templates and the management of
  clients and keys) MUST present a key. A request without one MUST be refused as unauthenticated
  (`AUTH_REQUIRED`) before any work is done.
- **FR-015**: A key that is unknown, malformed, revoked or expired MUST be refused with the same
  status, code (`AUTH_INVALID_KEY`) and wording in all four cases. A key of the wrong service is
  malformed.
- **FR-016**: The permissions are `pdf:generate` (submit jobs, read jobs), `pdf:templates:read` (list
  and read templates) and `pdf:templates:write` (create, duplicate, edit, publish, delete and preview
  templates). They are independent of each other. A request that needs a permission the key lacks
  MUST be refused as forbidden with the code `AUTH_SCOPE_MISSING`.
- **FR-017**: Management of clients and keys MUST require the admin key. A client key, whatever its
  permissions, MUST be refused there.
- **FR-018**: Every refusal MUST use the service's standard error format, carry a stable `code` and a
  trace id, and MUST NOT reveal whether a key's public identifier exists.
- **FR-019**: The two health endpoints MUST stay open and unchanged. The API documentation endpoints
  stay as they are (served only outside production). Downloading a generated file stays as it is until
  the feature that makes file links private.
- **FR-020**: For every request that passes, the log entries MUST carry the client id and the key's
  public identifier, and for the admin key a marker saying so.
- **FR-021**: While the database is unreachable, a request with a client key MUST be answered "storage
  unavailable" and not "invalid key".
- **FR-022**: For a caller holding a sufficient key, every response MUST be what it is today.
- **FR-023**: Callers that send no key today, including this repository's web console, MUST be refused
  like any other caller from the first release that contains this feature. There is no setting that
  turns authentication off. This feature is therefore released together with the console sign-in
  (feature 004), and an operator issues keys for existing products before upgrading.

### Key Entities

- **Client**: a product known to the service. Has a permanent id, a display name, a state (active or
  disabled) and a creation date. Owns any number of keys.
- **Key**: a secret that identifies one client. Has a short public identifier, a name, one or more
  permissions, a creation date, an optional expiry, a revocation date if revoked, and a last-used
  time. The secret itself is not part of the stored record.
- **Permission**: one of the three in FR-016, plus the operator's own authority, which belongs only to
  the admin key.
- **Admin key**: the single operator credential, held in configuration as a non-recoverable value.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of requests to protected operations without a valid key are refused before any
  work is done: nothing is queued, stored or rendered for them.
- **SC-002**: 100% of calls with a revoked key, an expired key, or a disabled client's key are refused
  within 60 seconds of the action or time, on every instance.
- **SC-003**: The refusals for an unknown, a revoked and an expired key are identical in status, code
  and wording, in 100% of cases.
- **SC-004**: An operator holding the admin key can register a client and obtain a working key in under
  two minutes.
- **SC-005**: After a key is issued, it appears in 0 places: an automated search of the service's logs,
  its responses and its stored data for the key finds nothing.
- **SC-006**: Every existing check of the job and template behaviour passes unchanged when run with a
  sufficient key, and the message contracts are unchanged.
- **SC-007**: Checking a key adds less than 25 ms to 95% of requests.
- **SC-008**: During a burst of 1,000 invalid-key requests a minute, requests from valid clients are
  still answered normally.
- **SC-009**: Every request that passes can be attributed to a client from the logs alone.

## Assumptions

- **Identification, not separation.** This feature says who is calling. It does **not** yet give each
  client its own templates and jobs: until the client-scoping feature, all clients see the same ones.
  The permissions in FR-016 are therefore the only limit between two clients.
- **One operator role.** There is a single admin key and no per-person accounts or self-service for
  products. The operator is whoever holds it.
- **Operator tools.** Until the console has its own sign-in and key screens (feature 004), the
  operator uses the service's management operations, called with the admin key (worked examples are
  provided), and a small command-line helper that generates the admin key. Wrapping the management
  operations in that helper is left to the shared tooling feature (011). Nothing in this feature
  requires the web console.
- **Files stay as they are.** Generated-file links are still unauthenticated until the private,
  expiring links feature. Requiring a key there now would break the end users and the other services
  that follow those links.
- **The message bus is not changed.** Services that publish directly to RabbitMQ are trusted by their
  broker credentials, as today. Identifying the client on the bus belongs to the client-scoping feature.
- **Keys are immutable.** Name, permissions and expiry are fixed at creation; to change them, issue a
  new key and revoke the old. There is no rotation helper and no limit on keys per client.
- **No expiry by default.** A key without an expiry works until revoked.
- **One minute.** Services cache what they know about a key for up to a minute, which is where the
  60-second bound in FR-013 comes from. A request refused or accepted a few seconds either side of a
  revocation is within the allowance.
- **Rate limits come later.** Limits per client, idempotency and webhooks are separate features.
- **A key's format** is `pdf_<public identifier>_<secret>`, so it carries the block's code. The header
  that carries it is `X-Api-Key`; it is part of the contract products integrate against.
- **Nothing live to protect yet.** The service is not serving production callers today (the VM deploy
  is not configured and the Space is not enabled), which is why a hard cut-over is acceptable. If that
  changes before this ships, FR-023 is the requirement to revisit.

## Clarifications

### Session 2026-11-03

- Q: How do callers without a key, including the repository's own web console, keep working through
  the upgrade? → A: They do not. Authentication is on from the first release with this feature, there
  is no switch to turn it off, and this feature is released together with the console sign-in
  (feature 004).
