# Feature Specification: Panel Discovery via Passive WHO_I_AM Observation

**Feature Branch**: `feat/003-panel-discovery`

**Created**: 2026-05-26

**Status**: Specified — extracted from former `specs/002-can-link-and-panel-discovery/` via #151. Implementation is the former Phase 4 of spec-002 (T044–T055, renumbered T001–TNN here). Depends on spec-002's CAN link lifecycle for the `Connected` state to observe in.

**Input**: User description: "While the CAN link is Connected (see spec-002), listen for STEM auto-address WHO_I_AM broadcasts on the bus and present any panels seen in a passive Panels-on-bus list: UUID, current MachineType decoded to a marketing variant name where known, and the timestamp of the most recent broadcast from that panel. No commands are sent; this slice is pure observation. Virgin panels self-announce in their AAS_STARTUP state on a UUID-derived ~2–6 s timer, so the supplier QA bench scenario (12 pristine panels, validated one at a time) populates without transmit. Claimed panels in AAS_STAND_BY are silent."

## Clarifications

### Session 2026-05-24 (inherited from former spec-002)

- Q: Pruning threshold for the Panels-on-bus list — value? → A: **15 s** (≈ 2.5× the worst-case ~6 s WHO_I_AM broadcast cadence). Plus the bench convention: the tool is connected to **at most one button panel at a time** (the supplier validates panels one-by-one).

(Other 2026-05-24/25/26 clarifications concerned the lifecycle slice and are preserved in [`specs/002-can-link-lifecycle/spec.md`](../002-can-link-lifecycle/spec.md). Discovery-side ones are listed above; none have been challenged on the bench since the split.)

## User Scenarios & Testing *(mandatory)*

### User Story (sole) — Seeing pristine panels announce themselves (Priority: P2 in the spec-002+003 sequence)

A supplier-side QA technician has a tray of pristine button-panel boards to validate. They plug a PEAK adapter into the bench, power on one panel at a time, and expect the tool to confirm that the panel is alive and talking on CAN — before they invest time in any test sequence on a known-dead board.

A **Panels-on-bus list** under the CAN status row shows every panel the tool has heard announce itself on the bus: a UUID identifier, the panel's current variant identity (decoded to a marketing name like EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8 when recognized; "virgin" when the panel has never been baptized; "unknown" if the variant byte is something else), and the timestamp of the panel's most recent broadcast.

Because pristine panels broadcast their identity automatically on a UUID-derived timer (firmware behaviour; broadcasts every ~2–6 s in the unbaptized state), the technician sees each board appear in the list within seconds of powering it on — without the tool ever sending a frame.

**Why this priority**: this is the actual supplier QA bench scenario the tool exists to support. Spec-002's lifecycle makes the tool observable; this spec makes it useful.

**Independent Test**: With the tool running, the adapter Connected, and the bus empty, power on the single pristine virgin panel on the bench. Verify a single row appears in the Panels-on-bus list within 6 seconds, carrying a UUID, the label "virgin", and a recent last-seen timestamp. (The bench convention is one panel at a time — the supplier validates panels one-by-one — so a multi-panel test is out of scope here.)

**Acceptance Scenarios**:

1. **Given** the CAN status row is Connected and the Panels-on-bus list is empty, **When** the technician powers on one pristine virgin panel within 6 seconds, **Then** exactly one row appears in the list with the panel's UUID, the label "virgin", and a last-seen timestamp within the last 6 seconds.
2. **Given** the list shows a virgin panel and that panel re-broadcasts (it does so every ~2–6 s while unbaptized), **When** the next broadcast arrives, **Then** the existing row's last-seen timestamp updates in place — no duplicate row is created.
3. **Given** the list is showing one panel and the technician powers it off, **When** the panel's pruning timeout (15 s) elapses, **Then** the row disappears from the list.
4. **Given** a previously-baptized panel (in operational silent state) is on the bus, **When** the technician observes the Panels-on-bus list, **Then** the panel does **not** appear — the operational silent state produces no broadcasts to observe. (The remediation, which is to reset the panel back to virgin so it broadcasts again, arrives in a later feature.)
5. **Given** a panel announces itself with a variant byte that decodes to a known marketing variant, **When** the row is rendered, **Then** the variant column shows the marketing name (e.g., "EDEN-XP") rather than the raw byte.
6. **Given** a panel announces itself with a variant byte that is neither virgin nor any of the four known marketing variants, **When** the row is rendered, **Then** the variant column shows "unknown" with the raw byte exposed via the detail affordance.

### Cross-spec scenario — list clears on link drop

This scenario also appears in spec-002 §US3 AC#5 as a one-line xref; the canonical assertion lives here.

**Given** the Panels-on-bus list has rows during a `Connected` window, **When** the link transitions out of `Connected` (any reason: MidSessionUnplug, manual disconnect, fatal Error), **Then** the list MUST clear before the next render. Implementation reacts to the spec-002 FR-015 `LinkStateChanged` observable — see FR-015' below.

---

### Edge Cases

- **Bus is connected but silent (no panels powered or all panels in claimed silent state)**: the CAN status row is Connected (spec-002), the Panels-on-bus list is empty, and the empty state explains that no virgin panels are currently announcing themselves.
- **A WHO_I_AM frame with a malformed payload (wrong length, garbage bytes)**: the frame is discarded silently; no row is created or updated. The malformed event does not flip the CAN status row to Error — malformed frames are a quiet drop, not a link-level fault (lifecycle is unaffected; see spec-002 §FR-014).
- **The same panel re-announces several times in quick succession**: the existing row's last-seen timestamp updates with each broadcast; no duplicate row is created.
- **Pruning a row while the technician is reading it**: the row update is non-destructive on screen — the technician's interaction (hover, selection) is not interrupted by the prune.
- **Variant byte is `0xFF` (the virgin marker)**: variant column reads "virgin", and the detail affordance explains that this panel has never been baptized to a specific machine model.

## Requirements *(mandatory)*

### Functional Requirements

**Discovery**

- **FR-007**: System MUST listen for STEM auto-address `WHO_I_AM` broadcasts on the bus while the link is Connected (per spec-002 FR-002) and present each distinct panel seen in a Panels-on-bus list.
- **FR-008**: System MUST identify each panel in the list by its UUID; multiple broadcasts from the same UUID MUST coalesce into one row.
- **FR-009**: System MUST decode the variant identity byte carried in `WHO_I_AM` to the panel's marketing variant name when that byte is one of the four known values, to the literal "virgin" when the byte is the virgin marker, and to "unknown" otherwise — with the raw byte exposed in either of the latter two cases via the detail affordance. The four known variant identity bytes and their marketing-name mapping are enumerated in [`data-model.md`](./data-model.md) §1.2 `VariantIdentity`.
- **FR-010**: System MUST show, for each row, the timestamp of the most recent broadcast from that panel — and update that timestamp in place when a new broadcast arrives.
- **FR-011**: System MUST prune a row from the list once no broadcast has been heard from that panel for **15 seconds** (≈ 2.5× the worst-case ~6 s WHO_I_AM broadcast cadence), so the list reflects what is currently announcing itself rather than what has ever announced itself in this session.
- **FR-012**: System MUST present an empty-state explanation when the list is empty, distinguishing "the link is down" from "the link is up but nothing is announcing itself right now".
- **FR-013**: System MUST silently discard a `WHO_I_AM` frame whose payload does not satisfy the documented wire layout — discarded frames MUST NOT flip the CAN status row to Error.

**Boundary**

- **FR-015' (consumer of spec-002 FR-015)**: System MUST clear the Panels-on-bus list when the link transitions from `Connected` to any non-`Connected` state, so the list never shows stale rows from a prior Connected window. The transition is observed via spec-002's `LinkStateChanged` (the "list is observable" upstream contract). Spec-003 owns the list-empty assertion; spec-002 owns the upstream observable.

### Key Entities

- **Panel observation**: one panel currently announcing itself on the bus. Carries a UUID, a decoded variant identity (marketing-variant name, "virgin", or "unknown"), the raw variant byte (for the detail affordance), and the timestamp of the most recent broadcast heard from that UUID.
- **Panels-on-bus list**: the live collection of panel observations, keyed by UUID. Rows appear when a previously-unseen UUID broadcasts, update in place when an already-listed UUID re-broadcasts, and disappear when their last-seen broadcast is older than the pruning threshold.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-003**: When the supplier connects a pristine virgin panel to the bus and the link is Connected, the panel appears in the Panels-on-bus list within 6 seconds (covers the worst-case UUID-derived broadcast cadence).
- **SC-004**: When the same panel re-announces while already listed, the existing row updates in place 100% of the time — no duplicate row is ever created.

(Lifecycle success criteria SC-001, SC-002, SC-005, SC-006, SC-007, SC-008 live in spec-002. SC-007 — zero CAN frames transmitted — also covers this spec by construction: panel discovery is pure observation.)

## Assumptions

- **The tool is connected to at most one button panel at a time.** The supplier QA workflow is one-panel-at-a-time: power on a panel, validate it, set it aside, move to the next. Multi-panel topology is bpt-rollout-wide out of scope. The tool's data model still keys observations by UUID (so an accidental two-on-bus event lists both), but the UI assumes a single panel under test.
- A pristine board-panel announces itself on the bus on a UUID-derived timer with worst-case cadence approximately 6 seconds. (Source: panel firmware behaviour, recorded in [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md) §C1.)
- A claimed (previously-baptized) panel does not broadcast in normal operation — surfacing such panels in the list requires the reset-to-virgin flow that lands in a later feature.
- The pruning threshold for the Panels-on-bus list is locked at **15 seconds** (≈ 2.5× the worst-case re-announcement cadence). See FR-011.
- The four known marketing variants and their identity-byte values (EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8) are stable per the firmware as audited 2026-05-24; the audit is recorded in [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md).
- A previously-claimed panel reappearing on the bus after a reset-to-virgin will produce a row with the "virgin" variant label until and unless it is re-baptized; that flow lands in a later feature.
- The dictionary fetched by feat-001 is not consulted to drive any CAN-side decoding in this slice — the variant-byte-to-name mapping and the protocol command codes are firmware constants for the purposes of this feature.

## Dependencies

- **Spec-002 CAN link lifecycle** ([`../002-can-link-lifecycle/`](../002-can-link-lifecycle/)): the `Connected` state is the gate this slice observes in, and `LinkStateChanged` (FR-015 in spec-002) is what FR-015' subscribes to. Spec-003 does not depend on spec-002 panel-side behaviour because spec-002 has none — the dependency is purely on the lifecycle observable.
- The vendored protocol stack (`Infrastructure.Protocol`) — shared with spec-002; see [`../002-can-link-lifecycle/contracts/vendor-manifest.md`](../002-can-link-lifecycle/contracts/vendor-manifest.md). Spec-003 consumes the `PacketReceived` event on `CanPort`.
- The audit captured in [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md), specifically §C1 (virgin panels broadcast, claimed panels are silent).

## Out of Scope (for this feature)

- Sending any CAN frame: probing, addressing, baptizing, variable read/write — all deferred to later features. The lifecycle's SC-007 (zero CAN frames transmitted) covers spec-003 by construction.
- Multi-panel disambiguation beyond list-them-all: any logic that requires "exactly one panel" or "the active panel" is downstream.
- Anything specific to the board variant beyond decoding the identity byte: variant-specific button masks, LED enablement, buzzer enablement.
- Bus-load metrics, frame-rate graphs, traffic visualisations.
- Persisting the Panels-on-bus list across sessions: the list is volatile and rebuilds from live broadcasts every session.
