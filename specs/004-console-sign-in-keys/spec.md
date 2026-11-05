# Feature Specification: Console Sign-in and Key Management

**Feature Branch**: `004-console-sign-in-keys`

**Created**: 2026-11-04

**Status**: Draft

**Input**: User description: "004 - Console sign-in and key management". Backlog entry
(`docs/blocks/03-planning.md#004-console-sign-in-and-key-management`): "The console must no longer be
open to anyone who can reach it. The operator signs in with the admin key and stays signed in for a
working day. Once signed in, the operator chooses which client they are working on, always sees which
one it is, and can manage that client's API keys: create (showing the key once, with a copy button),
see when each was last used, and revoke with confirmation. Existing job and template screens work
within the chosen client."

## User Scenarios & Testing *(mandatory)*

The **operator** runs the service and holds its admin key (feature 002). The **console** is the web
app in this repository that the operator uses to submit jobs, author templates and, with this
feature, manage keys. Since feature 002 the service refuses every caller without a key, and the
console sends none, so today the console cannot do anything. This feature makes it usable again, and
only for someone who holds the admin key.

A **session** is what the operator gets by signing in: proof, held by the browser, that this browser
was given the admin key recently. The admin key itself is typed once and never kept.

### User Story 1 - The operator signs in with the admin key and stays signed in for a working day (Priority: P1)

The operator opens the console and is asked for the admin key. They paste it, are let in, and can
work for the rest of the day without being asked again, even if they close the tab or reload. At the
end of the day, or when they sign out, the console asks for the key again. Someone who reaches the
console without the key sees only the sign-in page.

**Why this priority**: without it the console does nothing at all (feature 002 refuses it), and
feature 002 cannot be released without it.

**Independent Test**: open any console address without a session: the sign-in page appears and no
data is shown. Sign in with the admin key: the page originally asked for opens and works. Reload,
close and reopen the browser tab: still signed in. Sign out: back to the sign-in page, and the
previous session no longer works.

**Acceptance Scenarios**:

1. **Given** no session, **When** the operator opens any console page, **Then** they are taken to the
   sign-in page, and after signing in they arrive at the page they first asked for.
2. **Given** the sign-in page, **When** the operator enters the admin key, **Then** they are signed in,
   the key field is emptied, and the key is not kept anywhere in the browser.
3. **Given** the sign-in page, **When** the operator enters anything other than the admin key (a wrong
   key, a client key, an empty value), **Then** the console says "That key was not accepted." and
   nothing more, and empties the field.
4. **Given** a session, **When** the operator reloads the page or reopens the console in the same
   browser within the working day, **Then** they are still signed in.
5. **Given** a session started 12 hours ago, **Then** it has ended, however much it was used, and the
   next action takes the operator to the sign-in page.
6. **Given** a session, **When** the operator signs out, **Then** the console returns to the sign-in
   page, and that session can never be used again, even by someone holding a copy of it.
7. **Given** the sign-in page, **Then** it offers a way to show and hide the characters typed in the
   key field.

---

### User Story 2 - The operator works within one chosen client and always sees which one (Priority: P1)

Most of what the console shows belongs to a client. Once signed in, the operator picks the client
they are working on from a list, and every page then shows which client that is. Changing client
changes what every page works on. A link copied from the console opens on the same client for whoever
opens it.

**Why this priority**: creating a key or a template under the wrong client is the mistake this console
most needs to prevent, and every other screen in this feature depends on a chosen client.

**Independent Test**: with two clients registered, sign in, choose one: its name and id are visible on
every page. Switch to the other: every page now names it. Copy the address of a page, open it in a
new tab: the same client is chosen.

**Acceptance Scenarios**:

1. **Given** a session and several registered clients, **When** the operator opens the client
   switcher, **Then** every client is listed by name and id, with disabled clients marked as such.
2. **Given** a chosen client, **Then** its name and id are shown on every page, in the same place.
3. **Given** a chosen client, **When** the operator switches to another, **Then** the page they are on
   reloads its content for the new client without a full page reload, and the address changes to
   name the new client.
4. **Given** a console address that names a client, **When** it is opened, **Then** that client is
   chosen, whatever was chosen before in this browser.
5. **Given** an address that names no client, **Then** the client last chosen in this browser is
   used; if there is none and exactly one client exists, it is chosen; otherwise the operator is asked
   to choose before any client's page is shown.
6. **Given** an address that names a client that does not exist, **Then** the console says so and
   asks the operator to choose one, rather than showing another client's data.
7. **Given** a chosen client, **When** the operator submits a job, or creates, edits, publishes or
   deletes a template, **Then** the request is made on behalf of that client, and the page that
   creates something says which client it is for ("New key for **ecommerce**").

---

### User Story 3 - The operator creates a key for the chosen client and sees it exactly once (Priority: P1)

The operator, working on a client, creates a key for it: gives it a name, picks what it may do, and
optionally when it stops working. The console then shows the full key once, with a copy button and a
warning that it cannot be shown again. The operator cannot close that window by accident: the only way
out is to confirm they have copied it. After that, the key is gone from the console.

**Why this priority**: issuing keys is the reason the operator signs in at all; without it, every new
product still needs someone to call the management operations by hand.

**Independent Test**: create a key with one permission, copy it from the reveal window, close the
window by confirming: the key works on a call it is permitted to make. Try to see it again anywhere in
the console: it cannot be found.

**Acceptance Scenarios**:

1. **Given** a chosen client, **When** the operator creates a key with a name, at least one permission
   and no expiry, **Then** the full key is shown once with a copy button and the warning "Copy it now.
   It cannot be shown again."
2. **Given** the reveal window is open, **When** the operator presses Escape, clicks outside it, or
   navigates away, **Then** it does not close silently: it stays open, or the operator is warned that
   the key will be lost.
3. **Given** the reveal window, **When** the operator confirms "I have copied it", **Then** the window
   closes, the key is no longer held anywhere in the console, and the new key appears in the client's
   list showing only its public identifier.
4. **Given** the create form, **When** the operator gives no name, no permission, or an expiry that has
   already passed, **Then** the form says which field is wrong and nothing is created.
5. **Given** the create form, **Then** it lists exactly the permissions the service offers, each with a
   one-line description of what it allows.
6. **Given** no client is chosen, **Then** the key screens ask the operator to choose one and offer no
   way to create a key.

---

### User Story 4 - The operator sees a client's keys and when each was last used (Priority: P2)

Before revoking an old key, the operator wants to know whether anything still uses it. The key list
shows, for the chosen client, every key with its name, public identifier, permissions, when it was
created, when it expires, its state and when it was last used.

**Why this priority**: it is what makes revoking safe. It is P2 because a key can be created and used
without it.

**Independent Test**: use one of a client's keys, then open the key list: that key shows it was last
used a moment ago; a key never used shows "never"; a revoked key and an expired key show their state.

**Acceptance Scenarios**:

1. **Given** a client with keys, **When** the operator opens its key list, **Then** each key shows its
   name, public identifier, permissions, created date, expiry (or none), state (active, expired or
   revoked) and last use, and never the key itself.
2. **Given** a key used within the last day, **Then** its last use is shown as a relative time ("3
   minutes ago"), with the exact time available on hover or focus.
3. **Given** a key never used, **Then** its last use reads "never".
4. **Given** a client with no keys, **Then** the list says so and offers to create the first one.
5. **Given** the chosen client is switched, **Then** the list shows the new client's keys, never a mix.

---

### User Story 5 - The operator revokes a key, after confirming which one (Priority: P2)

A key leaks or a product is retired. The operator revokes the key from the list. Because revoking is
permanent, the console first asks for confirmation, naming the key and the client, so the operator
cannot revoke the wrong one by a slip of the mouse.

**Why this priority**: it is what makes a leaked key survivable without calling the management
operations by hand. It is P2 only because it needs a key to exist first.

**Independent Test**: revoke a key from the list, confirming in the dialog: the list shows it revoked,
and within a minute calls with it are refused. Start a revoke and cancel: nothing changes.

**Acceptance Scenarios**:

1. **Given** an active key, **When** the operator chooses to revoke it, **Then** a confirmation names
   the key (name and public identifier) and the client, and says that revoking cannot be undone.
2. **Given** the confirmation, **When** the operator confirms, **Then** the key is shown as revoked and
   offers no further actions; **when** they cancel, **Then** nothing changes.
3. **Given** a revoked or expired key, **Then** the list offers no action to revoke or restore it.
4. **Given** the revoke fails (the service is unreachable, or the key was revoked elsewhere), **Then**
   the console says what happened and the list shows the key's real state.

---

### User Story 6 - The operator registers a client and can disable or re-enable it (Priority: P3)

A new deployment has no clients, so there is nothing to choose and no key to create. The operator adds
a client from the console (an id and a display name) and can then create its keys straight away. The
operator can also disable a client, after confirming, and enable it again.

**Why this priority**: without it a fresh deployment needs one management call by hand before the
console is useful. It is P3 because that call exists today (feature 002).

**Independent Test**: on a deployment with no clients, sign in: the console asks the operator to add
one. Add it: it is chosen, and a key can be created for it. Disable it: the switcher marks it disabled
and its keys are refused within a minute; enable it: they work again.

**Acceptance Scenarios**:

1. **Given** no clients exist, **When** the operator signs in, **Then** the console explains that a
   client is needed and offers to add one.
2. **Given** the add form, **When** the operator enters an id that breaks the service's rules or is
   already taken, **Then** the form says which rule broke, before or after submitting, and nothing is
   created.
3. **Given** a client is added, **Then** it appears in the switcher and is chosen.
4. **Given** an active client, **When** the operator disables it, **Then** a confirmation names the
   client and says its keys will stop working until it is enabled again; after confirming it is shown
   as disabled.
5. **Given** a disabled client, **When** the operator enables it, **Then** it is shown as active and its
   keys that were not revoked work again.

---

### User Story 7 - A session that ends mid-task sends the operator back without losing work silently (Priority: P3)

The operator's working day ends while they are editing a template, or someone replaces the admin key.
The next action they take is refused. The console takes them to sign in, warns first if they have
unsaved changes, and after signing in returns them to where they were.

**Why this priority**: it only matters at the edges of a session; the console is secure without it,
but losing an hour's template edits without warning would make the console unpleasant to trust.

**Independent Test**: sign in, open the template editor, make an edit, end the session from outside
(sign out in another tab): the next save attempt warns about unsaved changes and leads to sign-in;
after signing in, the operator is back on the template.

**Acceptance Scenarios**:

1. **Given** a session that has ended, **When** any console page next talks to the service, **Then**
   the console takes the operator to sign in, and afterwards returns them to the same page and client.
2. **Given** unsaved changes on the page, **When** the session has ended, **Then** the operator is
   warned before leaving the page, as when leaving it by any other route.
3. **Given** the operator signed out in one tab, **Then** other open tabs of the console are signed out
   too, at the latest on their next request to the service.

### Edge Cases

- **The admin key is replaced.** The operator generates a new admin key, changes the configuration and
  restarts. Every session started with the old key ends: the next request from any of them is refused
  and leads to sign-in.
- **Many wrong sign-ins.** Someone guesses keys at the sign-in page. After 5 failed attempts in a minute
  from the same address, further attempts are refused for the rest of that minute with "Too many
  attempts. Try again in a minute.", whether or not the key is right. Successful sign-ins are not
  limited. Products' calls with client keys are not affected.
- **A client key at sign-in.** A product's key is not the admin key: it is refused with the same
  generic message as any wrong key.
- **The database is unreachable at sign-in.** The console says the service is unavailable and to try
  again, not that the key was wrong, so the operator does not start doubting a correct key.
- **The API cannot be reached at all.** Every page says "The API could not be reached." with a way to
  try again, rather than an empty list or an endless spinner.
- **Several instances of the service.** A session started on one instance works on all of them, and
  survives a restart of any of them; signing out on one ends it on all.
- **Another site tries to act with the operator's session.** A page on another site, opened in the
  same browser while the operator is signed in, cannot make the service change anything (create or
  revoke a key, submit a job, edit a template) on the operator's behalf.
- **A script injected into a console page.** It cannot read the session and carry it away; the session
  is not readable by the page's own code.
- **The reveal window and the browser.** The new key is not written to the browser's storage, the
  console's address, its logs or its error reports; reloading the page while the reveal window is open
  loses the key (the operator creates another and revokes the lost one).
- **Copy fails.** If the browser refuses the copy button (permissions, an insecure address), the console
  says so and leaves the key selected so it can be copied by hand.
- **A key created by someone else meanwhile.** Keys issued through the management operations or another
  browser appear in the list the next time it loads; the list never claims to be live.
- **A disabled client is chosen.** Its pages still open; the switcher and page header mark it
  disabled, and the key screens say its keys are refused while it stays disabled.
- **Expiry in the operator's time zone.** The expiry the operator picks is shown back, before creating,
  as an exact date and time with its time zone, so "end of 31 December" means what they think.
- **The console on a phone.** Signing in, choosing a client, seeing keys and revoking one work on a
  narrow screen; authoring templates does not have to.

## Requirements *(mandatory)*

### Functional Requirements

**Signing in**

- **FR-001**: The console MUST show nothing but the sign-in page to a browser without a valid session.
  The service MUST refuse every console request that carries neither a valid session nor a key.
- **FR-002**: The operator MUST be able to sign in by entering the admin key, and only the admin key.
  A wrong key, a client key, an empty or malformed value MUST all be refused with the same message,
  "That key was not accepted.", giving no hint about which part was wrong.
- **FR-003**: The admin key MUST be sent to the service only at sign-in. It MUST NOT be kept in the
  browser after the attempt (not in storage, the address or memory), and the field MUST be emptied
  after every attempt, successful or not.
- **FR-004**: A session MUST last 12 hours from sign-in and then end, however much it is used. It MUST
  survive reloads and reopening the console in the same browser, restarts of the service, and requests
  served by different instances.
- **FR-005**: The operator MUST be able to sign out. A session that was signed out MUST be refused on
  every instance from then on, even if a copy of it is presented again.
- **FR-006**: Replacing the admin key in the configuration MUST end every session started with the
  previous key.
- **FR-007**: The session MUST NOT be readable by code running in the console's pages, and a page on
  another site MUST NOT be able to use the operator's session to change anything in the service.
- **FR-008**: Failed sign-in attempts MUST be limited to 5 per minute from one network address. Further
  attempts in that minute MUST be refused with a message saying to wait, without checking the key.
- **FR-009**: While the service's storage is unreachable, a sign-in attempt MUST be answered "service
  unavailable, try again" and not "key not accepted".
- **FR-010**: A signed-in session MUST carry the operator's authority exactly as the admin key does in
  feature 002, and nothing more. Product callers with client keys MUST be unaffected by this feature.
- **FR-011**: The console MUST show, somewhere always visible, that the operator is signed in and when
  the session ends.
- **FR-012**: When a session ends while the operator is using the console, the next request MUST take
  them to sign in, warn first about unsaved changes as any other navigation does, and return them to the
  same page and client after signing in.

**Choosing a client**

- **FR-013**: The console MUST let the operator choose the client they are working on from a list of
  every registered client, showing each one's name, id and whether it is disabled.
- **FR-014**: The chosen client's name and id MUST be visible on every page that shows or changes a
  client's data, in the same place on every page.
- **FR-015**: The chosen client MUST be part of each page's address, so a copied address opens on the
  same client. The address wins over anything remembered; the browser MUST remember the last choice as
  the default when an address names none.
- **FR-016**: When the address names no client and nothing is remembered, the console MUST choose the
  only client if exactly one exists, and otherwise MUST ask the operator to choose before showing any
  client's page. An address naming an unknown client MUST be reported as such and MUST NOT fall back to
  another client silently.
- **FR-017**: Changing the chosen client MUST make every page show and act on the new client, without
  the operator reloading.
- **FR-018**: Every request the console makes about a client's jobs, templates or keys MUST be made on
  behalf of the chosen client, and every page that creates something MUST name that client next to the
  create action.

**Keys**

- **FR-019**: For the chosen client, the console MUST list every key with its name, public identifier,
  permissions, creation date, expiry, state (active, expired or revoked) and last use, and MUST never
  show a key itself after its reveal.
- **FR-020**: Last use MUST be shown as a relative time under a day and a date beyond, with the exact
  time available, and as "never" for a key never used.
- **FR-021**: The operator MUST be able to create a key for the chosen client with a name, one or more
  of the permissions the service offers (each described in one line), and optionally an expiry in the
  future. The form MUST name the client it creates for and refuse invalid input on the field at fault.
- **FR-022**: The full new key MUST be shown exactly once, with a copy button and the warning "Copy it
  now. It cannot be shown again." That view MUST close only when the operator confirms they have copied
  it; Escape, clicking outside it or navigating away MUST NOT close it without warning.
- **FR-023**: Once the reveal closes, the console MUST hold the key nowhere. At no point MUST the key be
  written to browser storage, the address, logs or error reports.
- **FR-024**: The operator MUST be able to revoke an active key after a confirmation that names the key
  (name and public identifier) and the client and says it cannot be undone. Revoked and expired keys
  MUST offer no revoke or restore action.

**Clients**

- **FR-025**: The operator MUST be able to register a client from the console, with an id and a display
  name, validated by the same rules as the service (feature 002, FR-005), with a message naming the
  rule that broke.
- **FR-026**: The operator MUST be able to disable a client, after a confirmation naming it and saying
  that its keys stop working until it is enabled, and to enable it again.
- **FR-027**: When no client exists, the console MUST explain that one is needed and offer to add one.

**Existing screens and every page**

- **FR-028**: The existing job and template screens MUST keep working as they do today, within the
  chosen client: submitting a job and creating, editing, previewing, publishing and deleting templates
  are done on its behalf.
- **FR-029**: The job history the console keeps in the browser MUST record the client each job was
  submitted for and MUST show only the chosen client's jobs.
- **FR-030**: Every page that loads data MUST show a loading state, an empty state with the action that
  fills it, and an error state using the service's error title, detail and code, with "The API could
  not be reached." when the service cannot be reached at all.
- **FR-031**: Signing in, choosing a client, listing keys, revoking a key and the reveal window MUST be
  fully usable by keyboard and on a narrow (phone-width) screen.
- **FR-032**: This feature MUST add no new deployment-specific value (address, client names, a product
  name) to the console: the same console works for every deployment. The block's own name and logo
  shown today come from the console's one existing source and are reused, not repeated, until runtime
  branding (feature 012) makes them configurable.

### Key Entities

- **Session**: the operator's signed-in state in one browser. Has a start time and an end time 12 hours
  later, can be ended early by signing out, and is ended by a change of admin key. Carries the
  operator's authority and no client.
- **Chosen client**: the client the console is working on. Held in the page address, remembered per
  browser as a default. Not stored by the service.
- **Client** and **Key**: as defined in feature 002. This feature shows and changes them; it adds
  nothing to them.
- **Local job history entry**: the console's existing record of a job it submitted, now also naming the
  client it was submitted for.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of console pages, and every service operation the console uses, are refused to a
  browser without a valid session: no client, key, job or template data is shown or changed.
- **SC-002**: An operator holding the admin key can sign in, choose a client and copy a newly created
  key in under one minute.
- **SC-003**: A signed-in operator is not asked for the key again for 12 hours, and is always asked
  again once 12 hours have passed.
- **SC-004**: After sign-out or a change of admin key, 100% of requests carrying the old session are
  refused, on every instance.
- **SC-005**: After a key's reveal closes, the key appears in 0 places in the browser: an automated
  search of the console's storage, history, addresses and logs for the key finds nothing.
- **SC-006**: No key can be revoked, and no client disabled, without a confirmation naming it: 0 of
  these actions complete from a single click.
- **SC-007**: In usability checks, the operator can say which client they are working on from any page
  without opening a menu, in 100% of cases.
- **SC-008**: Every existing check of the job and template screens passes unchanged when run with a
  signed-in session and a chosen client.
- **SC-009**: After 5 failed sign-ins in a minute from one address, 100% of further attempts in that
  minute are refused without the key being checked.
- **SC-010**: A page on another site cannot change anything in the service using the operator's
  session, in 100% of attempts covered by the tests.

## Assumptions

- **Ships with 002.** This feature and feature 002 are released together (milestone M1): 002 refuses
  every caller without a key, including the console, and has no switch to turn that off.
- **One operator role.** There is one admin key and no per-person accounts, invitations or audit of
  who signed in. Whoever holds the admin key is the operator. Personal accounts or single sign-on are
  a later concern.
- **Client separation comes from 003.** This feature makes the console act on behalf of the chosen
  client. Until client-scoped templates and jobs (feature 003) are in place, the service still shows
  every client the same templates; the console's behaviour does not change when 003 lands.
- **No "All clients" view yet.** Every page in this feature works within one client. The "All clients"
  choice arrives with the overview and the server-side jobs list (feature 010), which are the pages that
  can show several clients at once.
- **Jobs stay in the browser.** The jobs list is still the console's own history of jobs it submitted,
  as today; jobs submitted by products appear with feature 010.
- **Out of scope:** the overview page, webhooks screens (007), per-client limit overrides (008),
  runtime branding (012), keyboard shortcuts beyond what screens already have, and changing a client's
  display name. The console lands where it lands today after sign-in.
- **Keys are immutable** (feature 002): there is no edit, rename or rotate action; to change a key the
  operator creates a new one and revokes the old.
- **A working day is 12 hours**, fixed from sign-in with no extension by activity, so a session
  forgotten on a shared machine ends by itself. The length follows the design in
  `docs/blocks/04-backend.md#console-session`; whether a deployment can change it is decided in the
  plan.
- **Same site.** The console and the service are served to the browser from one site (as the local
  stack and the deployment do today through the console's proxy), which the protection against other
  sites relies on.
- **Last use and revocation timing** are those of feature 002: last use accurate to the minute, a
  revoked key refused everywhere within a minute.
- **Language.** The console stays in English, as today.
