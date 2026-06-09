# Feature Specification: Panel Discovery via Passive WHO_I_AM Observation

**Feature Branch**: `003-panel-discovery`

**Created**: 2026-06-05

**Status**: Draft

**Input**: User description: "While the CAN link is Connected, listen for STEM auto-address WHO_I_AM broadcasts on the bus and present any panels seen in a passive Panels-on-bus list: UUID, current MachineType decoded to a marketing variant name where known, and the timestamp of the most recent broadcast from that panel. No commands are sent; this slice is pure observation. Virgin panels self-announce in their AAS_STARTUP state on a UUID-derived ~2-6 s timer, so the supplier QA bench scenario (pristine panels, validated one at a time) populates without transmit. Claimed panels in AAS_STAND_BY are silent."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Seeing pristine panels announce themselves (Priority: P1)

A supplier-side QA technician has a tray of pristine button-panel boards to validate. They plug a PEAK adapter into the bench, power on one panel at a time, and expect the tool to confirm that the panel is alive and talking on CAN — before they invest time in any test sequence on a board that might be dead.

A **Panels-on-bus list** under the CAN status row shows every panel the tool has heard announce itself on the bus: a UUID identifier, the panel's current variant identity (decoded to a marketing name like EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8 when recognized; "virgin" when the panel has never been baptized; "unknown" if the variant byte is something else), and the timestamp of the panel's most recent broadcast.

Because pristine panels broadcast their identity automatically on a UUID-derived timer (firmware behaviour; every ~2-6 s in the unbaptized state), the technician sees each board appear in the list within seconds of powering it on — without the tool ever sending a frame.

**Why this priority**: confirming that a pristine board is alive on the bus is the first useful thing the technician needs, and the smallest slice that turns a merely-connected link into a working diagnostic. It is independently shippable on top of the existing CAN link and delivers standalone value with nothing else built on top.

**Independent Test**: With the tool running, the adapter Connected, and the bus empty, power on a single pristine virgin panel. Verify a single row appears in the Panels-on-bus list within 6 seconds, carrying a UUID, the label "virgin", and a recent last-seen timestamp. (Bench convention is one panel at a time, so a multi-panel test is out of scope.)

**Acceptance Scenarios**:

1. **Given** the CAN status row is Connected and the Panels-on-bus list is empty, **When** the technician powers on one pristine virgin panel, **Then** within 6 seconds exactly one row appears with the panel's UUID, the label "virgin", and a last-seen timestamp within the last 6 seconds.
2. **Given** the list shows a virgin panel and that panel re-broadcasts (every ~2-6 s while unbaptized), **When** the next broadcast arrives, **Then** the existing row's last-seen timestamp updates in place — no duplicate row is created.
3. **Given** the list is showing one panel and the technician powers it off, **When** the panel's pruning timeout (15 s) elapses, **Then** the row disappears from the list.
4. **Given** a previously-baptized panel in its operational silent state is on the bus, **When** the technician observes the list, **Then** the panel does not appear — the silent state produces no broadcasts to observe. (Resetting it back to virgin so it broadcasts again is a later feature.)
5. **Given** a panel announces itself with a variant byte that decodes to a known marketing variant, **When** the row is rendered, **Then** the variant column shows the marketing name (e.g. "EDEN-XP") rather than the raw byte.
6. **Given** a panel announces itself with a variant byte that is neither the virgin marker nor any of the four known marketing variants, **When** the row is rendered, **Then** the variant column shows "unknown" with the raw byte exposed via the detail affordance.
7. **Given** the Panels-on-bus list has rows during a Connected window, **When** the link leaves Connected for any reason (adapter unplugged, manual disconnect, fatal error), **Then** the list clears before the next render — it never shows stale rows from a prior Connected window.

### Edge Cases

- **Bus is Connected but silent** (no panels powered, or every panel in the claimed silent state): the list is empty and the empty state explains that nothing is currently announcing itself — distinct from "the link is down".
- **A malformed or incomplete WHO_I_AM announcement** (wrong reassembled length, garbage bytes, a multi-frame sequence missing a fragment, or a reassembled message that decodes to a different command): the announcement is discarded silently; no row is created or updated, and the CAN status row does **not** flip to Error — a bad announcement is a quiet drop, not a link-level fault.
- **The same panel re-announces several times in quick succession**: the existing row's last-seen timestamp advances with each broadcast; no duplicate row is created.
- **Pruning a row while the technician is reading it**: the update is non-destructive on screen — hover or selection is not interrupted by the prune.
- **Variant byte is `0xFF` (the virgin marker)**: the variant column reads "virgin", and the detail affordance explains the panel has never been baptized to a specific machine model.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST listen for STEM auto-address `WHO_I_AM` broadcasts on the bus while the CAN link is Connected, and present each distinct panel heard in a Panels-on-bus list.
- **FR-002**: System MUST identify each panel by its UUID; multiple broadcasts carrying the same UUID MUST coalesce into a single row, never duplicates.
- **FR-003**: System MUST decode the variant-identity byte carried in `WHO_I_AM` to the panel's marketing variant name when the byte is one of the four known values, to "virgin" when it is the virgin marker, and to "unknown" otherwise — exposing the raw byte via the detail affordance in the latter two cases.
- **FR-004**: System MUST show, for each row, the timestamp of that panel's most recent broadcast, and update it in place when a newer broadcast arrives.
- **FR-005**: System MUST prune a row once no broadcast has been heard from that panel for 15 seconds, so the list reflects what is currently announcing itself rather than everything heard this session.
- **FR-006**: System MUST present an empty-state explanation when the list is empty, distinguishing "the link is down" from "the link is up but nothing is announcing itself right now".
- **FR-007**: System MUST silently discard a `WHO_I_AM` announcement that does not satisfy the documented wire layout — including a multi-frame sequence that fails to reassemble into a complete, correctly sized payload, and a reassembled message that decodes to a command other than `WHO_I_AM`. A discarded announcement MUST NOT flip the CAN status row to Error.
- **FR-008**: System MUST clear the Panels-on-bus list when the CAN link transitions from Connected to any non-Connected state, so the list never shows stale rows from a prior Connected window.
- **FR-009**: System MUST NOT transmit any CAN frame as part of discovery — the feature is pure observation, start to finish.

### Key Entities

- **Panel observation**: one panel currently announcing itself on the bus. Carries a UUID, a decoded variant identity (marketing-variant name, "virgin", or "unknown"), the raw variant byte (for the detail affordance), and the timestamp of the most recent broadcast heard from that UUID.
- **Panels-on-bus list**: the live collection of panel observations, keyed by UUID. Rows appear when a previously-unseen UUID broadcasts, update in place when an already-listed UUID re-broadcasts, and disappear when their last-seen broadcast is older than the pruning threshold.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When a pristine virgin panel is connected to the bus and the link is Connected, the panel appears in the Panels-on-bus list within 6 seconds (covers the worst-case UUID-derived broadcast cadence).
- **SC-002**: When the same panel re-announces while already listed, the existing row updates in place 100% of the time — no duplicate row is ever created.
- **SC-003**: Across an entire discovery session, the tool transmits zero CAN frames — verifiable by a bus capture showing no traffic originating from the tool.
- **SC-004**: When the link leaves Connected, the Panels-on-bus list is empty by the next UI render — no stale row from a prior Connected window is ever shown.

## Assumptions

- **The tool is connected to at most one button panel at a time.** The supplier QA workflow is one-panel-at-a-time: power on a panel, validate it, set it aside, move to the next. Multi-panel topology is out of scope. Observations are still keyed by UUID (so an accidental two-on-bus event lists both), but the UI assumes a single panel under test.
- A pristine board announces itself on a UUID-derived timer with worst-case cadence approximately 6 seconds (panel firmware behaviour, audited in [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md) §C1).
- A claimed (previously-baptized) panel does not broadcast in normal operation; surfacing such a panel in the list requires the reset-to-virgin flow that lands in a later feature.
- The pruning threshold is locked at **15 seconds** (≈ 2.5× the worst-case ~6 s re-announcement cadence). See FR-005.
- The four known marketing variants and their identity-byte values (EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8) are firmware constants, audited 2026-05-24 and recorded in `docs/Context/bpt-rollout/CORRECTIONS.md`.
- **A `WHO_I_AM` announcement is carried on the bus as a *segmented* multi-frame STEM SP_APP message** — several classic-CAN frames on the auto-address broadcast id, each carrying a 2-byte Network-Layer header — reassembled into one application packet before its 15-byte payload is decoded. The tester reassembles the fragments and reads the WHO_I_AM payload (firmware `AutoAddressSlave.c`; transport: STEM Network Layer). The single-frame model in the previous draft was wrong; the reassembly machinery already exists in the vendored protocol stack and this feature reuses it. Wire detail lives in [`contracts/who-i-am-wire-format.md`](./contracts/who-i-am-wire-format.md).
- The variant-byte-to-name mapping and the protocol command codes are firmware constants for this feature (the auto-address `WHO_I_AM` command code identifies the announcement; the variant byte decodes the panel); no fetched dictionary is consulted to drive any CAN-side decoding in this slice.

## Dependencies

- **CAN link lifecycle (existing product capability).** The tool already maintains a CAN bus connection with an observable `Connected` state and emits a notification whenever the link state changes. This feature observes only while the link is Connected, and clears its list the moment the link leaves Connected. It depends on that lifecycle *capability* — the established `Connected` state and its change notifications — not on how the link is established or which feature delivered it.
- **Panel firmware behaviour**, audited and recorded in [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md) (§C1: virgin panels broadcast, claimed panels are silent; the variant-identity byte values).

## Out of Scope (for this feature)

- Sending any CAN frame: probing, addressing, baptizing, variable read/write — all deferred to later features.
- Multi-panel disambiguation beyond listing them all: any logic that requires "exactly one panel" or "the active panel" is downstream.
- Anything specific to the board variant beyond decoding the identity byte: variant-specific button masks, LED enablement, buzzer enablement.
- Bus-load metrics, frame-rate graphs, traffic visualisations.
- Persisting the Panels-on-bus list across sessions: the list is volatile and rebuilds from live broadcasts every session.
