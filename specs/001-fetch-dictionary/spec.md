# Feature Specification: Dictionary Fetch and Status Display

**Feature Branch**: `feat/001-fetch-dictionary`

**Created**: 2026-05-13

**Status**: Draft

**Input**: User description: "Fetch the button-panel dictionary at app launch and on manual refresh, show its source state to the technician at all times, and ship a pre-seeded copy so the tool is usable before the first network call returns. A first-launch registration step links the tool to the dictionary service so future fetches are authenticated."

## Clarifications

### Session 2026-05-13

- Q: What is the lifecycle of the installation credential once issued? → A: Permanent until manually revoked by STEM admin; the tool never expires or rotates it proactively. The tool surfaces re-registration only when the dictionary service rejects the credential with an authentication failure (per FR-018).
- Q: What is the language policy for user-facing strings in this slice? → A: English-only for v1. No internationalization framework is scaffolded; strings live inline. Adding additional languages is a future feature with focused scope.
- Q: How should the embedded seed surface in the dictionary status row? → A: The seed is a flavour of "Cached"; the status row shows "Cached · last synced \<seed build date\>". The detail affordance distinguishes "from embedded seed" (never live-fetched on this machine) from "from local copy" (previously fetched live). Two-state state machine (Live, Cached) is preserved; no third "Seeded" variant.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Inspecting dictionary state at start of shift (Priority: P1)

A supplier-side technician launches the tool to begin a testing session. Before plugging in any hardware or running any tests, they need to know which version of the button-panel dictionary the tool will check their results against, and how recent it is.

The persistent **dictionary status row** at the top of the main window answers both questions at a glance: a colour-coded indicator shows whether the loaded data has been confirmed live with STEM's dictionary service, has been read from a local copy, or is absent. Adjacent text states when the data was last confirmed against the service. Hovering or clicking opens a detail view that exposes the source path, the reason for any fallback, and (when applicable) the failure mode of the most recent fetch attempt.

**Why this priority**: Without this story, the tool has no observable behaviour — nothing for any other story to attach to. It is the smallest possible end-user value: the technician can answer "what data am I about to test against?" without leaving the main window.

**Independent Test**: Launch the tool on a freshly-installed machine with no internet access. Verify the status row is populated within 1 second of the window appearing, that it carries a meaningful headline (e.g. *Cached · last synced 2026-04-15*), and that the detail view explains the origin of the data.

**Acceptance Scenarios**:

1. **Given** the tool was just installed and has never had network access, **When** the technician launches it, **Then** the status row shows the embedded seed dictionary's age and a "Cached" headline within 1 second.
2. **Given** the tool has previously fetched a dictionary live, **When** the technician launches it offline, **Then** the status row shows the timestamp of the last successful live fetch with a "Cached" headline.
3. **Given** the status row shows "Cached", **When** the technician opens the detail view, **Then** they see the reason the last live attempt failed and the path of the local copy.

---

### User Story 2 — Registering a tool installation (Priority: P2)

A technician installs the tool on a supplier machine for the first time. Because the dictionary service identifies each installation by a per-machine credential, the tool must collect a one-time **bootstrap token** from the technician — STEM delivers this token out of band (typically via email or a phone call) — and exchange it for a long-lived credential that is stored locally and never disclosed.

A modal dialog appears on first launch when no credential is yet present. The dialog explains where the bootstrap token comes from, accepts the technician's pasted value, and submits it to the dictionary service. On success the dialog closes and the tool proceeds; the credential is now persisted and the registration ceremony will not repeat. On failure the dialog stays open with an inline error message so the technician can correct the token or close the tool.

**Why this priority**: This story is required once per machine for the tool to make any authenticated request, but it does not block User Story 1 — a freshly-installed tool can still display its seeded dictionary without ever registering. P2 unlocks the live-fetch path; without it, all refreshes will fail with an authentication reason and the tool will operate from the seed/cache indefinitely.

**Independent Test**: On a freshly-installed machine, launch the tool, observe the registration dialog appears blocking, paste a known-valid bootstrap token, submit, and verify the dialog closes and a credential file is written to the user-profile location (existence check only — its contents must not be readable in plain text).

**Acceptance Scenarios**:

1. **Given** no credential exists on the machine, **When** the tool launches, **Then** the registration dialog appears and the main window is not interactive behind it.
2. **Given** the registration dialog is open, **When** the technician pastes a valid bootstrap token and submits, **Then** the dialog closes and the tool proceeds to normal operation.
3. **Given** the registration dialog is open, **When** the technician submits an invalid or expired token, **Then** an inline error message appears, the dialog stays open, and the input field retains focus.
4. **Given** the tool has been registered, **When** the technician relaunches it, **Then** no dialog appears and the tool proceeds straight to its main window.

---

### User Story 3 — Refreshing dictionary data on demand (Priority: P3)

A technician suspects (or has been told) that the dictionary has changed — for example, a new variable has been added to a panel type they are about to test. Rather than restart the tool, they click a **Refresh** control in the dictionary status row. The tool re-fetches the dictionary from the service, updates the local copy if the contents differ, and reflects the outcome in the status row immediately.

On success the status row flips to *Live · synced now*. On failure the status row flips (or stays) at *Cached · last synced HH:MM · refresh failed (\<reason\>)* — the technician's in-memory dictionary is **not** discarded, and the next test cycle can still proceed against the data the tool already had.

**Why this priority**: This story builds on top of P1 and P2 — it requires the status row exists (P1) and that registration has either completed or is at least possible (P2 enables the "Live" outcome). It is genuinely demand-driven: the technician knows when they want fresh data; the tool should not surprise them by mutating state in the background.

**Independent Test**: With the tool registered and showing a Live state, click Refresh while the dictionary service is reachable. Verify the status row briefly indicates an in-flight refresh, then settles back to *Live* with a newer timestamp. Repeat with the service unreachable and verify the status row settles to *Cached* with a failure-reason chip.

**Acceptance Scenarios**:

1. **Given** the tool is registered and the service is reachable, **When** the technician clicks Refresh, **Then** the status row shows an in-flight indicator and then settles to "Live" with a newer timestamp.
2. **Given** the tool is registered and the service is unreachable, **When** the technician clicks Refresh, **Then** the status row settles to "Cached" with a reason chip naming the failure mode, and the dictionary loaded in memory is unchanged.
3. **Given** a Refresh is already in flight, **When** the technician clicks Refresh again, **Then** no additional network request is made, and both clicks observe the same outcome when the in-flight request resolves.
4. **Given** the local copy is older than the live response, **When** a refresh succeeds, **Then** the local copy is updated to match the live response before the status row flips to "Live".

---

### Edge Cases

- **No credential, no network**: the tool starts, shows the seeded dictionary as *Cached · last synced \<seed build date\>* (with "from embedded seed" visible in the detail affordance), and the registration dialog appears. The technician can dismiss the dialog and still use the seeded data; the status row reflects that no Live state is currently achievable.
- **Corrupt local copy**: the tool detects a content-integrity mismatch on its on-disk copy, refuses to use it, falls back to the embedded seed, and surfaces the integrity failure in the status row's detail view.
- **Seed staleness**: if the embedded seed is older than a documented soft threshold (e.g. 90 days), the status row shows an additional advisory cue (subtle warning glyph) without blocking use. A hard threshold is not enforced — the technician's judgement and the timestamp are the gate.
- **Authentication failure on refresh**: the status row shows *refresh failed (authentication)* and the detail view offers a "re-register" action that re-opens the registration dialog. The previously-stored credential is preserved unless the technician explicitly proceeds to overwrite it.
- **Service returns a malformed response**: the response is rejected as a whole, the status row reflects a malformed-response failure, and the previously-loaded dictionary remains in use.
- **Concurrent refresh attempts**: a second Refresh click while an in-flight fetch has not yet returned does not start a second network request; both clicks observe the same outcome.
- **Successful fetch returns identical data**: the dictionary *content* on disk is unchanged, but the local copy is rewritten so its persisted `fetchedAt` advances to *now* (so an offline relaunch reports this sync, not the last content-change date — #191); the in-memory dictionary is unchanged, the timestamp advances to *now*, and the status row flips to *Live*.

## Requirements *(mandatory)*

### Functional Requirements

**State display**

- **FR-001**: System MUST display, at all times while the main window is open, the current state of the loaded dictionary (live, cached, or absent) and the timestamp of the last confirmed live fetch.
- **FR-002**: System MUST visually distinguish the three states "data confirmed live with the service", "data from a local copy", and "no dictionary available" using both a colour cue and a textual headline.
- **FR-003**: System MUST expose, through a detail affordance attached to the status display, the reason for the current state (including the most recent failure mode where applicable) and the on-disk location of the local copy.

**Immediate availability**

- **FR-004**: System MUST make a usable dictionary available within 1 second of the main window appearing, without waiting on any network call to resolve.
- **FR-005**: System MUST ship with an embedded seed dictionary and MUST extract it to the local-copy location on first launch when no local copy exists.

**Manual refresh**

- **FR-006**: Users MUST be able to trigger a dictionary refresh on demand via a control in the dictionary status display.
- **FR-007**: System MUST coalesce concurrent refresh requests so that no more than one network operation is in flight per refresh cycle.
- **FR-008**: System MUST update the status display to indicate an in-flight refresh and then settle it to the post-refresh state when the operation resolves.

**Refresh semantics**

- **FR-009**: System MUST, on every successful live fetch, compare the response to the on-disk local copy and overwrite the local copy when they differ.
- **FR-010**: System MUST keep the on-disk local copy and the in-memory dictionary byte-equal at all times after the first successful fetch in a session.
- **FR-011**: System MUST NOT discard the in-memory dictionary as a consequence of a failed refresh; the technician's data does not change because of a failed refresh.
- **FR-012**: System MUST advance the "last confirmed live" timestamp only when a fetch succeeds; failed refreshes MUST NOT advance it.
- **FR-013**: System MUST report the failure mode of a failed refresh (network problem, service unavailable, authentication failure, malformed response, etc.) in human-readable form via the status display's detail affordance.

**Registration**

- **FR-014**: On first launch when no credential is yet present on the machine, system MUST present a registration dialog that blocks the main window until completed or dismissed.
- **FR-015**: The registration dialog MUST accept a bootstrap token by paste, submit it to the dictionary service, and on success persist the issued credential locally.
- **FR-016**: System MUST store the issued credential in a form that cannot be read by another user account on the same machine, and that cannot be read at all on any other machine.
- **FR-017**: System MUST NOT re-prompt for a bootstrap token on subsequent launches once a credential is stored.
- **FR-018**: System MUST surface, on authentication failure during refresh, a re-registration action that re-opens the registration dialog without destroying the existing credential until a new one is successfully issued.

**Integrity & boundaries**

- **FR-019**: System MUST verify the integrity of the on-disk local copy before using it. If verification fails, system MUST fall back to the embedded seed and surface the failure in the status display.
- **FR-020**: System MUST NOT transmit **raw** machine, user, or hardware identifiers (machine name, OS user, machine identifier, MAC address, or equivalent) to STEM systems. The lowercase SHA-256 hex digest of the UTF-8 bytes of such a value is permitted — the digest is server-opaque and functions as a per-installation fingerprint for revocation and forensics. The cross-organization data boundary between a supplier-deployed installation and STEM systems MUST NOT be crossed by raw values. The `stem-dictionaries-manager` registration contract (`/register`) mirrors this posture in its *Privacy posture* section: hashing is MUST for supplier-deployed consumers.
- **FR-021**: System MUST operate against exactly one configured button-panel dictionary identifier per installation; the identifier is set at install time and not selected at runtime.
- **FR-022**: System MUST treat an issued installation credential as valid indefinitely from its own perspective; it MUST NOT proactively expire, refresh, or rotate the credential. The dictionary service's authentication failure response is the only signal that triggers re-registration (per FR-018).
- **FR-023**: When the loaded dictionary originates from the embedded seed (no live fetch has succeeded on this machine yet, or the on-disk local copy was deemed unusable), system MUST report the seed's build timestamp as the "last confirmed live" timestamp and MUST disclose the seed origin ("from embedded seed") through the status display's detail affordance.

### Key Entities

- **Button-panel dictionary**: a named collection of panel types, each containing a set of variables. Each variable carries an address, a data type, a numeric range or scale, and a display unit. The dictionary is the authoritative description of what the tool can test against.
- **Dictionary state**: the combination of (a) the origin of the data currently loaded — live or cached — and (b) the timestamp at which that data was last confirmed against the service. The state is what the status row renders.
- **Bootstrap token**: a one-time secret issued by STEM out of band, identifying a specific supplier installation. Consumed by the registration step and not retained.
- **Installation credential**: the long-lived secret issued by the dictionary service in exchange for a bootstrap token. Persisted locally, never displayed, never transmitted except as authentication on fetch requests.
- **Embedded seed**: a dictionary snapshot built into the application package and refreshed by the engineering team before each release. Provides the "usable on first launch, before any network" guarantee.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On every launch where the embedded seed is loadable, the dictionary status row is populated within 1 second of the main window appearing. (Aggregate cross-installation metrics — e.g., "99% of observed supplier launches" — require telemetry infrastructure deferred to a future feature.)
- **SC-002**: On 100% of cold-start launches (no warm cache, no warm process), the technician can see a usable dictionary state without waiting on a network call.
- **SC-003**: A first-time registration completes end-to-end in under 30 seconds when the technician has their bootstrap token ready (paste, submit, confirmation).
- **SC-004**: A refresh that fails against a **warm** service surfaces a human-readable reason in the status display within 12 seconds of the click. A refresh against a **cold** service is absorbed for up to 90 seconds before surfacing as `Failed(Timeout, _)`; during the in-flight window the status row carries a hint explaining the cold-start wait (see `phases/phase-7.md`).
- **SC-005**: Asked the question "is my dictionary data current?" with the tool open, technicians arrive at the correct answer (live versus cached, and how stale) using only the status display in 95% of trials.
- **SC-006**: After a successful registration, no further credential prompts occur during a four-hour work session across 100% of supported supplier machines.
- **SC-007**: A failed refresh has zero effect on the in-memory dictionary: 100% of refresh failures preserve the previously-loaded dictionary and its "last confirmed live" timestamp.

## Assumptions

- Technicians are familiar with basic desktop application interactions (paste from clipboard, click buttons, dismiss dialogs).
- Supplier machines have at least intermittent network access to STEM's dictionary service; pure-offline deployments are out of scope for this feature.
- STEM's dictionary service is the authoritative source of the button-panel dictionary; conflicts between the local copy and a live response resolve in favour of the live response.
- Bootstrap tokens reach the technician through a STEM-managed out-of-band channel (email, encrypted message, or phone call). Their distribution is outside the scope of this tool.
- The seeded dictionary embedded with each release is sufficiently current for the technician to begin work in the rare event the service is unreachable on a brand-new install; the engineering team refreshes the seed during each release cycle.
- Exactly one button-panel dictionary is meaningful to a given installation. Future support for multiple panel families is out of scope.
- Each supplier machine is administered by a single user account for the purposes of running this tool; the local credential is scoped to that account.
- All user-facing strings in this slice are English. No internationalization framework is included; adding additional languages is a future feature with its own spec.

## Dependencies

- STEM's dictionary service is available at a configured address and exposes the necessary endpoints for fetching the dictionary and exchanging a bootstrap token for an installation credential.
- The supplier machine's operating system provides a mechanism to encrypt local data under a per-user, per-machine key (so that FR-016 is satisfiable on that platform). On Windows, this is the OS-provided per-user data-protection facility; on other platforms an equivalent facility is assumed when porting.
- The STEM engineering team produces and ships an embedded seed dictionary with each release.

## Out of Scope (for this feature)

- Selecting between multiple button-panel dictionaries at runtime.
- Automatic background refresh on a timer.
- Connecting to or testing button-panel hardware (CAN bus, baptize sequence, run-test workflow). Those are subsequent features that depend on the dictionary being loaded.
- A settings UI for editing the configured dictionary identifier or any other configuration value.
- A general-purpose notification centre for non-dictionary events. Dictionary-state transitions are conveyed via the always-present status row; toasts and a notification centre are deferred.
- Multi-user support on a single machine (each supplier machine is treated as a single-technician installation).
