# Feature Specification: CAN Link Lifecycle

> An exploratory five-family FSM redesign was attempted and parked on 2026-05-29 — see [`parked-phase-b-redesign/`](./parked-phase-b-redesign/). This spec describes the **shipped four-family FSM** and remains the spec of record.

**Feature Branch**: `feat/002-can-link-lifecycle`

**Created**: 2026-05-24

**Status**: Implementing — Phase 3 (US1, MVP) shipped via PR [#122](https://github.com/luca-veronelli-stem/button-panel-tester/pull/122); Phase 5 (US3, mid-session unplug) and Phase N polish blocked on the Phase 3.5 fix queue documented in [`tasks.md`](./tasks.md). Panel-discovery (formerly US2 + FR-007–013) extracted to [`specs/003-panel-discovery/`](../003-panel-discovery/) via #151 (2026-05-26).

**Input**: User description: "Open the configured PEAK PCAN-USB adapter at 250 kbps as soon as the application has finished its dictionary-fetch boot sequence, and keep a persistent CAN-bus status row on the main window with the same shape and behaviour as the dictionary status row from feat-001 (colour-coded headline, human-readable detail, manual reconnect control). If no PEAK adapter is present, the status row shows a friendly Disconnected state and the rest of the UI stays usable so the dictionary status from feat-001 remains visible. If the adapter is unplugged mid-session the row reflects the loss within a small handful of seconds and the application remains usable; re-seating the adapter (with or without an explicit Reconnect click) restores the link. The CAN link is the substrate the panel-discovery slice (spec-003) builds on; this spec covers lifecycle only."

## Clarifications

### Session 2026-05-24

- Q: What triggers the CAN status row's `Error` state vs `Disconnected`, and how granular should the Error state be? → A: **Three top-level chip states (Connected / Disconnected / Error), with the Error state internally sub-typed.** `Disconnected` = anything a reconnect click is the expected resolution for (no adapter, link down, mid-session unplug). `Error` = anything beyond a routine link-down — the chip colour signals "something's wrong" at a glance; the detail affordance labels the case as either **Recoverable** (a reconnect click may clear it: bus-off detected, transient unexpected PEAK driver status) or **Fatal** (the technician must take external action: driver not installed, hardware failure, persistent unrecognised PEAK status). Mirrors the way FR-005 already splits Disconnected sub-cases internally — same pattern, applied to Error.

### Session 2026-05-25 (bench feedback on PR #122 build)

- Q: Should the Recoverable/Fatal classification appear only in the detail affordance, or also in the chip headline? → A: **Also in the chip headline.** Bench feedback showed the severity is high-signal at a glance — the technician should see `Error · Fatal · "<detail>"` or `Error · Recoverable · "<detail>"` in the headline without having to hover. The detail affordance still carries the full multi-line context (technical reason, `since` timestamp) but no longer holds the severity exclusively.
- Q: When the same Error cause re-fires across reconnect clicks (or escalates Recoverable→Fatal), should the `since` timestamp update each time, or stick to the first observation? → A: **Stick to the first observation.** Bench feedback found the sticky behaviour intuitive in the driver-missing Fatal case (the root cause didn't change between clicks) and inconsistent when the escalated Fatal updated `since` on each retry. New rule (FR-002b): `since` reflects when the root cause was first observed; updates only when the cause genuinely changes or the chip leaves and re-enters Error.
- Q: When should the Reconnect button be hidden vs visible? Originally FR-003 only specified Error-state clickability. → A: **Hide in `Initializing` and `Disconnected · ReconnectPending`** — both states represent in-flight work and a click would race the existing call. **Show in every other state** (the three other Disconnected sub-cases and both Error sub-classifications). FR-003 now carries the visibility table.
- Q: When the user clicks Reconnect from a `Disconnected(MidSessionUnplug)` state, the chip transits straight from Disconnected to Error without showing `Disconnected · ReconnectPending` — is that intended? → A: **No.** The transit was bench-observed to depend on whether the underlying port adapter had a port to close (skip if not). For the GUI the rule is: **on click, the chip MUST always paint `Disconnected · ReconnectPending` for the duration of the in-flight Reconnect**, regardless of source state. FR-003 click-feedback contract now mandates this independent of the adapter-layer close step.
- Q: Bench observed: after a MidSessionUnplug, plugging the adapter back without clicking Reconnect re-greens the chip automatically. The spec previously listed hot-plug detection as Out-of-Scope. Keep it out-of-scope, or embrace the freebie? → A: **Embrace the freebie.** Remove hot-plug detection from Out-of-Scope and add a Dependencies note that the behaviour is provided implicitly by the vendored protocol stack. Flag the dependency on [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111): if the `Stem.Communication` replacement ships without it, this feature regresses to manual-Reconnect-only — the #111 migration plan MUST add an acceptance check covering hot-plug preservation.

### Session 2026-05-26 (bench feedback on PR #147 draft)

- Q: PR #147 implemented the FR-002a format from the 2026-05-25 clarification (`Error · Recoverable · "<detail>"`) faithfully, but bench review found the row too verbose. Drop the "Error" prefix? → A: **Yes.** The red chip already encodes the Error state family; the word adds nothing the colour doesn't. Keep `Connected ·` and `Disconnected ·` prefixes because green/red are unambiguous but grey is shared by Initializing and Disconnected — the word disambiguates there. Lean headline format: `Recoverable · <detail>` or `Fatal · <detail>` — see the new amended FR-002a + the new "Presentation surfaces" section under Requirements.
- Q: Drop the `"..."` quotes around the short detail in the headline too? → A: **Yes.** The `·` separator already delimits the variable string from the fixed labels; the quotes are visual noise without semantic value.
- Q: Should the row also carry the suggested fix for the error, or keep it tooltip-only? → A: **Carry it on the row, via a stricter detail-string convention.** FR-002a already mandated that the detail "MUST recommend [a concrete action]" — make that normative: detail strings MUST be of the form `<cause> — <imperative suggestion>` joined by em-dash, single line. The row renders the first-line verbatim, so the suggestion appears on the row for free without per-state renderer logic. The tooltip still carries the full multi-line technical context. Disconnected stays minimal (reason phrase only) — the reason already implies the action ("no PEAK adapter found" tells the tech to plug one in) and there's no severity word competing for the slot.
- Q: How does the CAN status row relate to STEM's three presentation surfaces — continuous-state indicator, NotificationCenter (toast → bell history per `docs/Standards/APP_SHELL.md`), and `ILogger<T>` forensic record? → A: **The row is the continuous-state surface; the other two are complementary, not redundant.** A new "Presentation surfaces" subsection under Requirements formalises the rule: row carries the at-a-glance classifier, tooltip carries supporting facts for action, the NotificationCenter (when built) carries transition events with the same detail string + an optional action button, and `ILogger<T>` carries the structured forensic record independent of user-facing mute. NotificationCenter is deferred — APP_SHELL.md specifies it but no `NotificationCenter.fs` exists yet; the row remains self-sufficient until then. The forensic-record audit on `CanLinkService` is tracked separately as [#148](https://github.com/luca-veronelli-stem/button-panel-tester/issues/148), slotted post-PR-D.
- Q: The first pass of the FR-002a amendment said detail strings MUST follow `<cause> — <imperative suggestion>`. Bench review of the PR #147 build noticed that several existing strings (`"PEAK · status 0x40000"`, `"PEAK stack failed to initialise"`) have no natural imperative — the convention only fits when the imperative organically belongs to the cause. MUST is too strict. → A: **Soften MUST → SHOULD.** Detail strings SHOULD follow `<cause> — <imperative suggestion>` when an imperative naturally applies; otherwise the cause stands alone. The cause/suggestion split is a *pragmatic interim* — a future spec (app-wide NotificationCenter, separate from spec-002) will lift this into a structured `cause` + `suggestion` field on `ErrorClassification`, at which point the row renders cause-only and the suggestion routes to the notification action button. Until that lands, the row + tooltip carry both via the em-dash convention where applicable.
- Q: Several cause strings carry a `PEAK ·` vendor-tag prefix (`"PEAK · status 0x40000"`, `"PEAK · <text>"`, `"PEAK adapter reported Error"`, `"PEAK stack failed to initialise"`). On the row this produces a double-`·` (`Recoverable · PEAK · status 0x40000`) and an ambiguous "is PEAK part of the cause or a tag?" reading. Given the row is the dedicated CAN status row and PEAK is the only supported adapter family, the vendor tag is redundant in row context. → A: **Drop the pure-tag usage.** `PEAK ·` prefixes in `PeakErrorText.fs` and the `PEAK <generic-noun>` constructions in `PcanCanLink.fs` ("PEAK adapter", "PEAK stack") collapse to the bare cause. **Keep substantive brand references**: `"PEAK PCANBasic native DLL not found"` (PCANBasic is the actual product name) and `"install the PEAK driver"` (the imperative target needs the brand to be unambiguous). An inline comment in `PeakErrorText.fs` documents the convention so a future multi-vendor PR knows when to re-introduce the tag (with a structured `Vendor` field, not a string prefix).

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

### User Story 3 — Surviving an adapter unplug mid-session (Priority: P3)

A technician is mid-shift with the tool open. They accidentally bump the PEAK adapter loose from the dock. The tool needs to notice quickly, show that the link is gone, and remain usable so the technician can re-seat the adapter and reconnect — without restarting the application and losing the dictionary state that feat-001 manages.

The CAN status row reflects the loss within a small handful of seconds, the dictionary status row from feat-001 stays untouched, and the technician's reconnect control resumes operation once they re-seat the adapter. Consumers downstream of the link (notably the Panels-on-bus list in [spec-003](../003-panel-discovery/)) react to the link drop independently — see spec-003 FR-015' for the list-clear consumer contract; spec-002's FR-015 is the canonical "link transition is observable" upstream.

**Why this priority**: this story tests robustness rather than a happy-path capability. It is essential for bench credibility but does not block the supplier from validating a panel — if the adapter stays plugged in throughout the session, P1 alone delivers the supplier's lifecycle goal.

**Independent Test**: With the tool Connected, physically unplug the PEAK adapter. Verify the status row flips to Disconnected within 5 seconds with a "link lost" reason and the dictionary status row is unchanged. Re-plug the adapter and click reconnect — verify the link recovers.

**Acceptance Scenarios**:

1. **Given** the CAN status row is Connected, **When** the technician unplugs the PEAK adapter, **Then** the status row transitions to Disconnected within 5 seconds and the reason names the link loss.
2. **Given** the link has been lost, **When** the technician observes the dictionary status row from feat-001, **Then** it is unchanged — the dictionary state from feat-001 is independent of the CAN link state.
3. **Given** the link has been lost and the technician has re-seated the adapter, **When** they click the reconnect control, **Then** the status row transitions back to Connected.
4. **Given** the link has been lost via `MidSessionUnplug`, **When** the technician re-seats the adapter without clicking the reconnect control, **Then** the status row returns to Connected within 5 seconds. (Hot-plug auto-reconnect is provided implicitly by the vendored protocol stack — see §Dependencies and [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111) for the regression risk.)
5. **Given** the Panels-on-bus list from spec-003 has rows during a `Connected` window, **When** the link transitions out of `Connected`, **Then** the list is expected to clear — see [spec-003 FR-015'](../003-panel-discovery/spec.md) for the consumer obligation. Spec-002's FR-015 is the upstream "link transition is observable to consumers" contract that the spec-003 list-clear and any future consumer depend on.

---

### Edge Cases

- **PEAK driver installed but no physical adapter plugged in**: status row shows Disconnected with a remediation hint pointing to the adapter, distinct from any "wiring/bus problem" presentation.
- **Multiple PEAK adapters present on the host**: the tool picks the first one enumerated and proceeds. Disambiguation among multiple adapters is deliberately out of scope for this slice.
- **CAN controller enters bus-off**: the link is no longer operational and the controller will not transmit until it is reinitialised. The status row transitions to **Error / Recoverable** with the reason "Bus-off detected — try reconnect"; the reconnect control remains clickable and reinitialises the adapter on click.
- **PEAK driver returns an unexpected status code on Read or Write**: the status row transitions to **Error**. If the status is observed only once, the classification is **Recoverable** with the reason "PEAK status 0x… — try reconnect". If the same status repeats after a reconnect attempt, the classification escalates to **Fatal** with the reason "PEAK status 0x… persists across reconnect — file bug" so the technician has actionable diagnostic information for escalation.
- **PEAK driver not installed on the host**: detected on the first Initialize attempt. The status row transitions to **Error / Fatal** with the reason "PEAK PCANBasic native DLL not found — install the PEAK driver". Reconnect does not clear this case.
- **Dictionary fetch has not yet completed when the main window appears**: the CAN status row stays in an initialization state until the dictionary boot sequence completes, then transitions to its real state — the CAN link is not opened before the dictionary boot is done (per the input description).

## Requirements *(mandatory)*

### Presentation surfaces

The CAN status row is one of three complementary surfaces governed by the STEM standards (`docs/Standards/APP_SHELL.md` + `docs/Standards/DESIGN_SYSTEM.md` + `docs/Standards/LOGGING.md`). The split below is normative for every requirement in this section that mentions "headline", "chip", "detail affordance", "notification", or "log":

| Surface | Lifetime | Role | Content rule |
|---|---|---|---|
| **Continuous-state indicator** (CAN status row: chip + headline + tooltip) | Always-on, reflects "now" | Tells the technician *what is true right now* at a glance | Chip colour carries the state family. Headline carries the most informative sub-discriminator the colour can't (channel name, disconnect reason, error severity + cause/suggestion). Tooltip carries supporting facts for action: timestamps, identifiers, full multi-line technical reason. |
| **NotificationCenter** (toast + bell history; spec'd in `APP_SHELL.md`, **not yet built**) | One-shot at transition, then in 50-cap history | Tells the technician *something just changed* | When a state transition lands on a `Severity ≥ Warning` state (Recoverable/Fatal), a toast MAY be dispatched with the same detail string in the Body and an optional action (`Reconnect`, `View details`). Forward-reference only — implementation deferred until `Components/NotificationCenter.fs` lands per `APP_SHELL.md` §NotificationCenter. The row remains the continuous-state surface either way. |
| **Forensic log** (`ILogger<T>` per `LOGGING.md`) | Persistent, independent of user-facing mute | Tells the operator *what happened, in retrospect* | Every state transition emits one structured log entry. Level mapped to severity (Connected/Disconnected → Information, Recoverable → Warning, Fatal → Error). Fields: state name, severity, detail, since-timestamp. Forensic record is not affected by `Settings.Notifications.Muted`. Audit tracked separately in [#148](https://github.com/luca-veronelli-stem/button-panel-tester/issues/148), slotted post-PR-D. |

The same `CanLinkState` value flows into all three; each surface projects the parts it needs.

### Functional Requirements

**Adapter lifecycle**

- **FR-001**: System MUST open the configured CAN adapter at 250 kbps once the dictionary-fetch boot sequence from feat-001 has completed, not before.
- **FR-002**: System MUST surface the adapter's current link state in a persistent CAN status row alongside the dictionary status row from feat-001. The state set is exactly three top-level chip values:
  - **Connected** — the adapter is open and the bus is operational.
  - **Disconnected** — link is down, and a reconnect click is the expected resolution (no adapter present, mid-session unplug, link not yet established at boot, reconnect attempt pending).
  - **Error** — something beyond a routine link-down; the technician must read the detail affordance for remediation.
- **FR-002a**: When the state is Error, the classification MUST be surfaced **both in the chip headline and in the detail affordance** (the latter shows the technical context, the former is the at-a-glance signal). The chip headline takes the form `Recoverable · <detail>` or `Fatal · <detail>` — the "Error" prefix is omitted because the red chip already carries the state family (see "Presentation surfaces" above). The detail string SHOULD follow the form `<cause> — <imperative suggestion>` joined by em-dash when an imperative naturally applies to the cause; the row renders the first line verbatim, the detail affordance carries the full multi-line context. The cause/suggestion split is currently encoded in a single string for pragmatism — a future spec (app-wide NotificationCenter, tracked outside spec-002) will lift this into structured `cause` + `suggestion` fields on `ErrorClassification`, at which point the row renders cause-only and the suggestion routes to the notification action button. The two sub-classifications:
  - **Recoverable** — a reconnect click may clear it (e.g., bus-off detected by the CAN controller, transient unexpected PEAK driver status code). When an imperative applies, it is "try reconnect" (escalation guidance lives in the multi-line tooltip when needed).
  - **Fatal** — the technician must take external action (e.g., PEAK driver not installed, hardware failure, persistent unrecognised PEAK status). When an imperative applies, it is the concrete external step ("install the PEAK driver", "restart the tool", "file bug with the status code").
  This split mirrors how FR-005 internally splits Disconnected sub-cases.
- **FR-002b**: For every `Error` state surfaced to the user, the `since` timestamp shown in the detail affordance MUST reflect the moment the underlying root cause was **first observed**. Subsequent re-observations of the same cause (passive re-trigger, or a Recoverable→Fatal escalation following a Reconnect click against an unchanged cause) MUST preserve the original `since`. The timestamp updates only when the root cause itself changes or when the chip leaves the Error state and re-enters it via a distinct cause. **Definition of "same cause":** two observations are considered the same cause when their `Recoverable` / `Fatal` detail string is byte-equal. A future spec may introduce a structured cause discriminator if string equality proves too coarse.
- **FR-003**: System MUST provide a manual reconnect control in the CAN status row that re-opens the adapter on demand. **Visibility rules:**
  - The control is **hidden** during `Initializing` (the first OpenAsync is in flight; clicking would either queue or race the in-flight call — neither is useful for the technician).
  - The control is **hidden** during `Disconnected · ReconnectPending` (a Reconnect is already in progress; the same reasoning).
  - The control is **visible and clickable** in all other Disconnected sub-cases (NoAdapterPresent, LinkNotYetOpened, MidSessionUnplug) and in both Error sub-classifications.
  - In the `Error · Fatal` sub-case the button is still clickable, but the caption MUST make clear that the click is unlikely to help (e.g. `Reconnect (unlikely to help)`).

  **Click-feedback contract.** On click, the CAN status row MUST transit through `Disconnected · ReconnectPending` for the duration of the in-flight Reconnect call, regardless of the source state (Disconnected, Error.Recoverable, or Error.Fatal). The technician always sees an "I'm working on it" affordance before the result lands. This is independent of whether the underlying port adapter needs to close before re-opening — the GUI transit is mandatory even when the impl's close step is a no-op.
- **FR-004**: System MUST expose, through a detail affordance attached to the status row, the adapter identification, the bus baud rate, and (when applicable) the most recent transition reason — including, in the Error state, the underlying trigger (e.g., "Bus-off detected", "PEAK driver returned 0x40000") and the Recoverable/Fatal classification from FR-002a.
- **FR-005**: System MUST distinguish, in the Disconnected presentation, between "no adapter present on the host" and "adapter present but the link is down" — the remediation differs and the technician must not be misled. The Error state's Recoverable/Fatal sub-classification (FR-002a) is a parallel mechanism for the Error case; both are surfaced through the detail affordance rather than through additional chip colours.
- **FR-006**: System MUST stay usable when no adapter is present — the dictionary status row from feat-001 remains visible and interactive regardless of CAN state.

**Boundary**

- **FR-014**: System MUST NOT transmit any CAN frame in this slice — the lifecycle is open-and-observe only. Spec-003 panel discovery is pure observation as well; transmit-side behaviour (probing, addressing, baptizing) is the subject of a later feature.
- **FR-015**: System MUST surface every `Connected → ¬Connected` transition through `LinkStateChanged` so downstream consumers (notably spec-003's Panels-on-bus list — see spec-003 FR-015' for the consumer contract) can react. The lifecycle owns the observable transition; consumers own their reactions. The canonical observable transition is what spec-002 guarantees; the list-empty assertion lives in spec-003.
- **FR-016**: System MUST keep the dictionary status row from feat-001 unaffected by CAN-side events — the two status rows are independent and a problem in one MUST NOT degrade the other.

### Key Entities

- **CAN link state**: the observable status of the adapter relative to the bus. Carries a coarse state (Connected / Disconnected / Error), an adapter identification, a baud rate, and an optional last-error reason.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On every launch where the dictionary boot succeeds and a PEAK adapter is present, the CAN status row reaches its Connected state within 2 seconds of the dictionary status row becoming populated.
- **SC-002**: On every launch where no PEAK adapter is present, the CAN status row shows the Disconnected "no adapter" state within 1 second of the dictionary status row becoming populated.
- **SC-005**: When the PEAK adapter is unplugged mid-session, the CAN status row reflects the Disconnected state within 5 seconds.
- **SC-006**: A failure mode on the CAN side (no adapter, unplug, bus silent) does **not** affect the dictionary status row from feat-001 in any observable way, across 100% of trials.
- **SC-007**: The tool sends zero CAN frames while operating in this slice's scope. Verifiable by passively monitoring the bus with an independent capture tool throughout a session — the captured trace shows only frames originating from the panels under test, never from the tool.
- **SC-008**: When the CAN controller signals a non-routine fault (bus-off, unexpected PEAK driver status), the CAN status row reflects the Error state within 5 seconds, and the detail affordance shows a Recoverable/Fatal classification with a concrete remediation recommendation.

## Assumptions

- The supplier bench has exactly one PEAK PCAN-USB adapter plugged into the test workstation. Multi-adapter setups are out of scope.
- The CAN bus baud rate is 250 kbps. This is fixed by the panel firmware, not configurable by the technician.
- The dictionary fetched by feat-001 is not consulted to drive any CAN-side decisioning in this slice — the lifecycle is independent of dictionary content.

## Dependencies

- Feat-001's dictionary fetch and status row pattern: the visual shape, the lifecycle expectations (1-second budget for populating after window paint), and the Avalonia + FuncUI layout conventions established there are reused here.
- A PEAK PCAN-USB driver installation on the test workstation.
- The audit captured in [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md), specifically §C4 (the protocol-stack source that this feature consumes).
- **Hot-plug detection is provided implicitly by the vendored protocol stack** (`Infrastructure.Protocol`). When the adapter is re-seated after a `Disconnected · MidSessionUnplug`, the stack re-greens the chip automatically without a manual Reconnect click. **Risk note:** this behaviour is not a documented contract of the vendored stack; if [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111)'s `Stem.Communication` replacement ships without an equivalent affordance, the feature regresses to manual-Reconnect-only. The migration plan for #111 MUST add an acceptance check that hot-plug is preserved (or note the regression in its release plan).
- **Spec-003 panel discovery** is the downstream consumer of `LinkStateChanged`. Spec-003 depends on spec-002's lifecycle; spec-002 does not depend on spec-003. The two specs cohabit one F# service class (`Services/Can/CanLinkService.fs`) — the seam is in the docs, not in the code.

## Out of Scope (for this feature)

- Sending any CAN frame: probing, addressing, baptizing, variable read/write — all deferred to later features.
- Multi-adapter disambiguation: covered by the "first enumerated wins" edge case; richer disambiguation is downstream.
- Panel discovery: extracted to [`specs/003-panel-discovery/`](../003-panel-discovery/).
