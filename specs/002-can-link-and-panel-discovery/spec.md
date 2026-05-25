# Feature Specification: CAN Link and Panel Discovery

**Feature Branch**: `feat/002-can-link-and-panel-discovery`

**Created**: 2026-05-24

**Status**: Implementing — Phase 3 (US1, MVP) shipped via PR [#122](https://github.com/luca-veronelli-stem/button-panel-tester/pull/122); Phase 4 (US2, panel discovery) blocked on the Phase 3.5 fix queue documented in [`tasks.md`](./tasks.md).

**Input**: User description: "Open the configured PEAK PCAN-USB adapter at 250 kbps as soon as the application has finished its dictionary-fetch boot sequence, and keep a persistent CAN-bus status row on the main window with the same shape and behaviour as the dictionary status row from feat-001 (colour-coded headline, human-readable detail, manual reconnect control). While connected, listen for STEM auto-address WHO_I_AM broadcasts on the bus and present any panels seen in a passive Panels-on-bus list: UUID, current MachineType decoded to a marketing variant name where known, and the timestamp of the most recent broadcast from that panel. No commands are sent; this slice is pure observation. Virgin panels self-announce in their AAS_STARTUP state on a UUID-derived ~2–6 s timer, so the supplier QA bench scenario (12 pristine panels) populates without transmit. Claimed panels in AAS_STAND_BY are silent. If no PEAK adapter is present, the status row shows a friendly Disconnected state and the rest of the UI stays usable so the dictionary status from feat-001 remains visible."

## Clarifications

### Session 2026-05-24

- Q: Pruning threshold for the Panels-on-bus list — value? → A: **15 s** (≈ 2.5× the worst-case ~6 s WHO_I_AM broadcast cadence). Plus the bench convention: the tool is connected to **at most one button panel at a time** (the supplier validates panels one-by-one).
- Q: What triggers the CAN status row's `Error` state vs `Disconnected`, and how granular should the Error state be? → A: **Three top-level chip states (Connected / Disconnected / Error), with the Error state internally sub-typed.** `Disconnected` = anything a reconnect click is the expected resolution for (no adapter, link down, mid-session unplug). `Error` = anything beyond a routine link-down — the chip colour signals "something's wrong" at a glance; the detail affordance labels the case as either **Recoverable** (a reconnect click may clear it: bus-off detected, transient unexpected PEAK driver status) or **Fatal** (the technician must take external action: driver not installed, hardware failure, persistent unrecognised PEAK status). Mirrors the way FR-005 already splits Disconnected sub-cases internally — same pattern, applied to Error.

### Session 2026-05-25 (bench feedback on PR #122 build)

- Q: Should the Recoverable/Fatal classification appear only in the detail affordance, or also in the chip headline? → A: **Also in the chip headline.** Bench feedback showed the severity is high-signal at a glance — the technician should see `Error · Fatal · "<detail>"` or `Error · Recoverable · "<detail>"` in the headline without having to hover. The detail affordance still carries the full multi-line context (technical reason, `since` timestamp) but no longer holds the severity exclusively.
- Q: When the same Error cause re-fires across reconnect clicks (or escalates Recoverable→Fatal), should the `since` timestamp update each time, or stick to the first observation? → A: **Stick to the first observation.** Bench feedback found the sticky behaviour intuitive in the driver-missing Fatal case (the root cause didn't change between clicks) and inconsistent when the escalated Fatal updated `since` on each retry. New rule (FR-002b): `since` reflects when the root cause was first observed; updates only when the cause genuinely changes or the chip leaves and re-enters Error.
- Q: When should the Reconnect button be hidden vs visible? Originally FR-003 only specified Error-state clickability. → A: **Hide in `Initializing` and `Disconnected · ReconnectPending`** — both states represent in-flight work and a click would race the existing call. **Show in every other state** (the three other Disconnected sub-cases and both Error sub-classifications). FR-003 now carries the visibility table.
- Q: When the user clicks Reconnect from a `Disconnected(MidSessionUnplug)` state, the chip transits straight from Disconnected to Error without showing `Disconnected · ReconnectPending` — is that intended? → A: **No.** The transit was bench-observed to depend on whether the underlying port adapter had a port to close (skip if not). For the GUI the rule is: **on click, the chip MUST always paint `Disconnected · ReconnectPending` for the duration of the in-flight Reconnect**, regardless of source state. FR-003 click-feedback contract now mandates this independent of the adapter-layer close step.
- Q: Bench observed: after a MidSessionUnplug, plugging the adapter back without clicking Reconnect re-greens the chip automatically. The spec previously listed hot-plug detection as Out-of-Scope. Keep it out-of-scope, or embrace the freebie? → A: **Embrace the freebie.** Remove hot-plug detection from Out-of-Scope and add a Dependencies note that the behaviour is provided implicitly by the vendored protocol stack. Flag the dependency on [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111): if the `Stem.Communication` replacement ships without it, this feature regresses to manual-Reconnect-only — the #111 migration plan MUST add an acceptance check covering hot-plug preservation.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Inspecting CAN link state at start of shift (Priority: P1)

A supplier-side technician launches the tool to begin a testing session. After the dictionary boot sequence completes, they need to know whether the tool can talk to the panel hardware — before plugging in any boards or running any tests.

A persistent **CAN status row** sits on the main window next to the dictionary status row from feat-001. It carries the same shape: a colour-coded headline (Connected, Disconnected, Error), a human-readable detail (adapter identification, last error reason when applicable), and a manual reconnect control. The technician can read it at a glance and decide whether to plug in the PEAK adapter or check its wiring.

**Why this priority**: this is the smallest end-user value the slice can deliver — the technician can answer "is my CAN link up?" before any panel exists on the bus. Every subsequent story depends on the status row already being visible.

**Independent Test**: Launch the tool on a freshly-installed machine with no PEAK adapter present. Verify the CAN status row appears within 1 second of the main window and carries a Disconnected headline with a friendly remediation hint. Plug the adapter in, click reconnect, verify the headline flips to Connected within 2 seconds.

**Acceptance Scenarios**:

1. **Given** the tool has completed dictionary boot and no PEAK adapter is present, **When** the main window appears, **Then** the CAN status row shows a Disconnected headline naming "no PEAK adapter found" within 1 second.
2. **Given** the tool is showing Disconnected and the technician has just plugged in a PEAK adapter, **When** they click the reconnect control, **Then** the headline flips to Connected within 2 seconds and the detail shows the adapter identification.
3. **Given** the tool is showing Connected, **When** the technician opens the detail affordance, **Then** they see the adapter identification, the configured bus baud rate, and (if relevant) the timestamp of the most recent state change.

---

### User Story 2 — Seeing pristine panels announce themselves (Priority: P2)

A supplier-side QA technician has a tray of pristine button-panel boards to validate. They plug a PEAK adapter into the bench, power on one panel at a time, and expect the tool to confirm that the panel is alive and talking on CAN — before they invest time in any test sequence on a known-dead board.

A **Panels-on-bus list** under the CAN status row shows every panel the tool has heard announce itself on the bus: a UUID identifier, the panel's current variant identity (decoded to a marketing name like EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8 when recognized; "virgin" when the panel has never been baptized; "unknown" if the variant byte is something else), and the timestamp of the panel's most recent broadcast.

Because pristine panels broadcast their identity automatically on a UUID-derived timer (firmware behaviour; broadcasts every ~2–6 s in the unbaptized state), the technician sees each board appear in the list within seconds of powering it on — without the tool ever sending a frame.

**Why this priority**: this is the actual supplier QA bench scenario the tool exists to support. P1 makes the tool observable; P2 makes it useful.

**Independent Test**: With the tool running, the adapter Connected, and the bus empty, power on the single pristine virgin panel on the bench. Verify a single row appears in the Panels-on-bus list within 6 seconds, carrying a UUID, the label "virgin", and a recent last-seen timestamp. (The bench convention is one panel at a time — the supplier validates panels one-by-one — so a multi-panel test is out of scope here.)

**Acceptance Scenarios**:

1. **Given** the CAN status row is Connected and the Panels-on-bus list is empty, **When** the technician powers on one pristine virgin panel within 6 seconds, **Then** exactly one row appears in the list with the panel's UUID, the label "virgin", and a last-seen timestamp within the last 6 seconds.
2. **Given** the list shows a virgin panel and that panel re-broadcasts (it does so every ~2–6 s while unbaptized), **When** the next broadcast arrives, **Then** the existing row's last-seen timestamp updates in place — no duplicate row is created.
3. **Given** the list is showing one panel and the technician powers it off, **When** the panel's pruning timeout elapses, **Then** the row disappears from the list.
4. **Given** a previously-baptized panel (in operational silent state) is on the bus, **When** the technician observes the Panels-on-bus list, **Then** the panel does **not** appear — the operational silent state produces no broadcasts to observe. (The remediation, which is to reset the panel back to virgin so it broadcasts again, arrives in a later feature.)
5. **Given** a panel announces itself with a variant byte that decodes to a known marketing variant, **When** the row is rendered, **Then** the variant column shows the marketing name (e.g., "EDEN-XP") rather than the raw byte.
6. **Given** a panel announces itself with a variant byte that is neither virgin nor any of the four known marketing variants, **When** the row is rendered, **Then** the variant column shows "unknown" with the raw byte exposed via the detail affordance.

---

### User Story 3 — Surviving an adapter unplug mid-session (Priority: P3)

A technician is mid-shift with the tool open and panels visible. They accidentally bump the PEAK adapter loose from the dock. The tool needs to notice quickly, show that the link is gone, and remain usable so the technician can re-seat the adapter and reconnect — without restarting the application and losing the dictionary state that feat-001 manages.

The CAN status row reflects the loss within a small handful of seconds, the Panels-on-bus list shows a clear "no link" empty state instead of stale rows, the dictionary status row from feat-001 stays untouched, and the technician's reconnect control resumes operation once they re-seat the adapter.

**Why this priority**: this story tests robustness rather than a happy-path capability. It is essential for bench credibility but does not block the supplier from validating a panel — if the adapter stays plugged in throughout the session, P1+P2 alone deliver the supplier's goal.

**Independent Test**: With the tool Connected and showing one virgin panel, physically unplug the PEAK adapter. Verify the status row flips to Disconnected within 5 seconds with a "link lost" reason, the Panels-on-bus list empties (no stale rows), and the dictionary status row is unchanged. Re-plug the adapter and click reconnect — verify the link recovers and the panel reappears.

**Acceptance Scenarios**:

1. **Given** the CAN status row is Connected and at least one panel is visible in the list, **When** the technician unplugs the PEAK adapter, **Then** the status row transitions to Disconnected within 5 seconds and the reason names the link loss.
2. **Given** the link has been lost, **When** the technician observes the Panels-on-bus list, **Then** the list is empty — no stale rows remain from the previous Connected window.
3. **Given** the link has been lost, **When** the technician observes the dictionary status row from feat-001, **Then** it is unchanged — the dictionary state from feat-001 is independent of the CAN link state.
4. **Given** the link has been lost and the technician has re-seated the adapter, **When** they click the reconnect control, **Then** the status row transitions back to Connected and the Panels-on-bus list resumes populating from fresh broadcasts.

---

### Edge Cases

- **PEAK driver installed but no physical adapter plugged in**: status row shows Disconnected with a remediation hint pointing to the adapter, distinct from any "wiring/bus problem" presentation.
- **Multiple PEAK adapters present on the host**: the tool picks the first one enumerated and proceeds. Disambiguation among multiple adapters is deliberately out of scope for this slice.
- **Bus is connected but silent (no panels powered or all panels in claimed silent state)**: the CAN status row is Connected, the Panels-on-bus list is empty, and the empty state explains that no virgin panels are currently announcing themselves.
- **A WHO_I_AM frame with a malformed payload (wrong length, garbage bytes)**: the frame is discarded silently; no row is created or updated. The malformed event does not flip the CAN status row to Error — malformed frames are a quiet drop, not a link-level fault.
- **CAN controller enters bus-off**: the link is no longer operational and the controller will not transmit until it is reinitialised. The status row transitions to **Error / Recoverable** with the reason "Bus-off detected — try reconnect"; the reconnect control remains clickable and reinitialises the adapter on click.
- **PEAK driver returns an unexpected status code on Read or Write**: the status row transitions to **Error**. If the status is observed only once, the classification is **Recoverable** with the reason "PEAK status 0x… — try reconnect". If the same status repeats after a reconnect attempt, the classification escalates to **Fatal** with the reason "PEAK status 0x… persists across reconnect — file bug" so the technician has actionable diagnostic information for escalation.
- **PEAK driver not installed on the host**: detected on the first Initialize attempt. The status row transitions to **Error / Fatal** with the reason "PEAK PCANBasic native DLL not found — install the PEAK driver". Reconnect does not clear this case.
- **The same panel re-announces several times in quick succession**: the existing row's last-seen timestamp updates with each broadcast; no duplicate row is created.
- **Pruning a row while the technician is reading it**: the row update is non-destructive on screen — the technician's interaction (hover, selection) is not interrupted by the prune.
- **Dictionary fetch has not yet completed when the main window appears**: the CAN status row stays in an initialization state until the dictionary boot sequence completes, then transitions to its real state — the CAN link is not opened before the dictionary boot is done (per the input description).
- **Variant byte is `0xFF` (the virgin marker)**: variant column reads "virgin", and the detail affordance explains that this panel has never been baptized to a specific machine model.

## Requirements *(mandatory)*

### Functional Requirements

**Adapter lifecycle**

- **FR-001**: System MUST open the configured CAN adapter at 250 kbps once the dictionary-fetch boot sequence from feat-001 has completed, not before.
- **FR-002**: System MUST surface the adapter's current link state in a persistent CAN status row alongside the dictionary status row from feat-001. The state set is exactly three top-level chip values:
  - **Connected** — the adapter is open and the bus is operational.
  - **Disconnected** — link is down, and a reconnect click is the expected resolution (no adapter present, mid-session unplug, link not yet established at boot, reconnect attempt pending).
  - **Error** — something beyond a routine link-down; the technician must read the detail affordance for remediation.
- **FR-002a**: When the state is Error, the classification MUST be surfaced **both in the chip headline and in the detail affordance**. The chip headline takes the form `Error · Recoverable · "<short detail>"` or `Error · Fatal · "<short detail>"`; the detail affordance carries the full multi-line context. The two sub-classifications:
  - **Recoverable** — a reconnect click may clear it (e.g., bus-off detected by the CAN controller, transient unexpected PEAK driver status code). The detail text MUST recommend "Try reconnect; escalate if it doesn't clear."
  - **Fatal** — the technician must take external action (e.g., PEAK driver not installed, hardware failure, persistent unrecognised PEAK status). The detail text MUST recommend the concrete external step (install driver, restart tool, file bug with the status code).
  This split mirrors how FR-005 internally splits Disconnected sub-cases.
- **FR-002b**: For every `Error` state surfaced to the user, the `since` timestamp shown in the detail affordance MUST reflect the moment the underlying root cause was **first observed**. Subsequent re-observations of the same cause (passive re-trigger, or a Recoverable→Fatal escalation following a Reconnect click against an unchanged cause) MUST preserve the original `since`. The timestamp updates only when the root cause itself changes or when the chip leaves the Error state and re-enters it via a distinct cause.
- **FR-003**: System MUST provide a manual reconnect control in the CAN status row that re-opens the adapter on demand. **Visibility rules:**
  - The control is **hidden** during `Initializing` (the first OpenAsync is in flight; clicking would either queue or race the in-flight call — neither is useful for the technician).
  - The control is **hidden** during `Disconnected · ReconnectPending` (a Reconnect is already in progress; the same reasoning).
  - The control is **visible and clickable** in all other Disconnected sub-cases (NoAdapterPresent, LinkNotYetOpened, MidSessionUnplug) and in both Error sub-classifications.
  - In the `Error · Fatal` sub-case the button is still clickable, but the caption MUST make clear that the click is unlikely to help (e.g. `Reconnect (unlikely to help)`).

  **Click-feedback contract.** On click, the CAN status row MUST transit through `Disconnected · ReconnectPending` for the duration of the in-flight Reconnect call, regardless of the source state (Disconnected, Error.Recoverable, or Error.Fatal). The technician always sees an "I'm working on it" affordance before the result lands. This is independent of whether the underlying port adapter needs to close before re-opening — the GUI transit is mandatory even when the impl's close step is a no-op.
- **FR-004**: System MUST expose, through a detail affordance attached to the status row, the adapter identification, the bus baud rate, and (when applicable) the most recent transition reason — including, in the Error state, the underlying trigger (e.g., "Bus-off detected", "PEAK driver returned 0x40000") and the Recoverable/Fatal classification from FR-002a.
- **FR-005**: System MUST distinguish, in the Disconnected presentation, between "no adapter present on the host" and "adapter present but the link is down" — the remediation differs and the technician must not be misled. The Error state's Recoverable/Fatal sub-classification (FR-002a) is a parallel mechanism for the Error case; both are surfaced through the detail affordance rather than through additional chip colours.
- **FR-006**: System MUST stay usable when no adapter is present — the dictionary status row from feat-001 remains visible and interactive regardless of CAN state.

**Discovery**

- **FR-007**: System MUST listen for STEM auto-address `WHO_I_AM` broadcasts on the bus while the link is Connected and present each distinct panel seen in a Panels-on-bus list.
- **FR-008**: System MUST identify each panel in the list by its UUID; multiple broadcasts from the same UUID MUST coalesce into one row.
- **FR-009**: System MUST decode the variant identity byte carried in `WHO_I_AM` to the panel's marketing variant name when that byte is one of the four known values, to the literal "virgin" when the byte is the virgin marker, and to "unknown" otherwise — with the raw byte exposed in either of the latter two cases via the detail affordance. The four known variant identity bytes and their marketing-name mapping are enumerated in [`data-model.md`](./data-model.md) §3.1 `VariantIdentity`.
- **FR-010**: System MUST show, for each row, the timestamp of the most recent broadcast from that panel — and update that timestamp in place when a new broadcast arrives.
- **FR-011**: System MUST prune a row from the list once no broadcast has been heard from that panel for **15 seconds** (≈ 2.5× the worst-case ~6 s WHO_I_AM broadcast cadence), so the list reflects what is currently announcing itself rather than what has ever announced itself in this session.
- **FR-012**: System MUST present an empty-state explanation when the list is empty, distinguishing "the link is down" from "the link is up but nothing is announcing itself right now".
- **FR-013**: System MUST silently discard a `WHO_I_AM` frame whose payload does not satisfy the documented wire layout — discarded frames MUST NOT flip the CAN status row to Error.

**Boundary**

- **FR-014**: System MUST NOT transmit any CAN frame in this slice — discovery is pure observation. (Transmit-side behaviour, including any form of probing or addressing, is the subject of a later feature.)
- **FR-015**: System MUST clear the Panels-on-bus list when the link transitions from Connected to Disconnected so the list never shows stale rows from a prior Connected window.
- **FR-016**: System MUST keep the dictionary status row from feat-001 unaffected by CAN-side events — the two status rows are independent and a problem in one MUST NOT degrade the other.

### Key Entities

- **CAN link state**: the observable status of the adapter relative to the bus. Carries a coarse state (Connected / Disconnected / Error), an adapter identification, a baud rate, and an optional last-error reason.
- **Panel observation**: one panel currently announcing itself on the bus. Carries a UUID, a decoded variant identity (marketing-variant name, "virgin", or "unknown"), the raw variant byte (for the detail affordance), and the timestamp of the most recent broadcast heard from that UUID.
- **Panels-on-bus list**: the live collection of panel observations, keyed by UUID. Rows appear when a previously-unseen UUID broadcasts, update in place when an already-listed UUID re-broadcasts, and disappear when their last-seen broadcast is older than the pruning threshold.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On every launch where the dictionary boot succeeds and a PEAK adapter is present, the CAN status row reaches its Connected state within 2 seconds of the dictionary status row becoming populated.
- **SC-002**: On every launch where no PEAK adapter is present, the CAN status row shows the Disconnected "no adapter" state within 1 second of the dictionary status row becoming populated.
- **SC-003**: When the supplier connects a pristine virgin panel to the bus and the link is Connected, the panel appears in the Panels-on-bus list within 6 seconds (covers the worst-case UUID-derived broadcast cadence).
- **SC-004**: When the same panel re-announces while already listed, the existing row updates in place 100% of the time — no duplicate row is ever created.
- **SC-005**: When the PEAK adapter is unplugged mid-session, the CAN status row reflects the Disconnected state within 5 seconds.
- **SC-006**: A failure mode on the CAN side (no adapter, unplug, malformed frame, bus silent) does **not** affect the dictionary status row from feat-001 in any observable way, across 100% of trials.
- **SC-007**: The tool sends zero CAN frames while operating in this slice's scope. Verifiable by passively monitoring the bus with an independent capture tool throughout a session — the captured trace shows only frames originating from the panels under test, never from the tool.
- **SC-008**: When the CAN controller signals a non-routine fault (bus-off, unexpected PEAK driver status), the CAN status row reflects the Error state within 5 seconds, and the detail affordance shows a Recoverable/Fatal classification with a concrete remediation recommendation.

## Assumptions

- The supplier bench has exactly one PEAK PCAN-USB adapter plugged into the test workstation. Multi-adapter setups are out of scope.
- **The tool is connected to at most one button panel at a time.** The supplier QA workflow is one-panel-at-a-time: power on a panel, validate it, set it aside, move to the next. Multi-panel topology is bpt-rollout-wide out of scope. The tool's data model still keys observations by UUID (so an accidental two-on-bus event lists both), but the UI and downstream specs (003+) assume a single panel under test.
- The CAN bus baud rate is 250 kbps. This is fixed by the panel firmware, not configurable by the technician.
- A pristine board-panel announces itself on the bus on a UUID-derived timer with worst-case cadence approximately 6 seconds. (Source: panel firmware behaviour, recorded in [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md) §C1.)
- A claimed (previously-baptized) panel does not broadcast in normal operation — surfacing such panels in the list requires the reset-to-virgin flow that lands in a later feature.
- The pruning threshold for the Panels-on-bus list is locked at **15 seconds** (≈ 2.5× the worst-case re-announcement cadence). See FR-011.
- The four known marketing variants and their identity-byte values (EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8) are stable per the firmware as audited 2026-05-24; the audit is recorded in [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md).
- A previously-claimed panel reappearing on the bus after a reset-to-virgin will produce a row with the "virgin" variant label until and unless it is re-baptized; that flow lands in a later feature.
- The dictionary fetched by feat-001 is not consulted to drive any CAN-side decoding in this slice — the variant-byte-to-name mapping and the protocol command codes are firmware constants for the purposes of this feature.

## Dependencies

- Feat-001's dictionary fetch and status row pattern: the visual shape, the lifecycle expectations (1-second budget for populating after window paint), and the Avalonia + FuncUI layout conventions established there are reused here.
- A PEAK PCAN-USB driver installation on the test workstation.
- The audit captured in [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md), specifically §C1 (virgin panels broadcast, claimed panels are silent) and §C4 (the protocol-stack source that this feature consumes).
- **Hot-plug detection is provided implicitly by the vendored protocol stack** (`Infrastructure.Protocol`). When the adapter is re-seated after a `Disconnected · MidSessionUnplug`, the stack re-greens the chip automatically without a manual Reconnect click. **Risk note:** this behaviour is not a documented contract of the vendored stack; if [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111)'s `Stem.Communication` replacement ships without an equivalent affordance, the feature regresses to manual-Reconnect-only. The migration plan for #111 MUST add an acceptance check that hot-plug is preserved (or note the regression in its release plan).

## Out of Scope (for this feature)

- Sending any CAN frame: probing, addressing, baptizing, variable read/write — all deferred to later features.
- Multi-panel disambiguation beyond list-them-all: any logic that requires "exactly one panel" or "the active panel" is downstream.
- Anything specific to the board variant beyond decoding the identity byte: variant-specific button masks, LED enablement, buzzer enablement.
- Bus-load metrics, frame-rate graphs, traffic visualisations.
- Persisting the Panels-on-bus list across sessions: the list is volatile and rebuilds from live broadcasts every session.
