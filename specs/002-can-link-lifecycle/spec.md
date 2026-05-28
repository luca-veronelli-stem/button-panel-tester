# Feature Specification: CAN Link Lifecycle

**Feature Branch**: `docs/002-lifecycle-spec-refresh` (Phase B rewrite branch; lifecycle code already on `main` since PR #122)

**Created**: 2026-05-24. **Rewritten**: 2026-05-27 (Phase B redesign — five-state FSM).

**Status**: Draft (Phase B rewrite). Supersedes the substrate spec.md merged via #120 / #121 / #122 and amended through #133, #134, #135, #138, #141, #145, #147. Panel-discovery concern was extracted to [`specs/003-panel-discovery/`](../003-panel-discovery/) via #151 (2026-05-26, Phase A). Old → new naming is tracked in [`migration-map.md`](./migration-map.md).

**Input**: User description: "Hold a persistent CAN-bus status row on the main window that tells the technician where the link is right now — coming up, scanning for an adapter, opening one, up, or faulted — and what (if anything) the user can do about it. Open the configured PEAK PCAN-USB adapter at 250 kbps, walk the link through its lifecycle, and survive bench reality: no adapter plugged in, multiple adapters with some busy, mid-session unplug, hot-plug recovery, bus-off, driver missing, user wanting to release the adapter for another exclusive-mode tool. Lifecycle only — panel discovery is spec-003's concern; this spec is the upstream observable it subscribes to."

## Clarifications

The five bullets dated **2026-05-24 / -25 / -26** are bench-validated decisions carried forward unchanged in spirit from the substrate spec. They are not relitigated by this rewrite. The **2026-05-27** session records the FSM redesign decisions taken when Phase B started and the interview-driven refinements that followed `8327e05`. The 2026-05-27 bullets are grouped by sub-topic for navigation.

### Session 2026-05-24 (bench, carries forward)

- Q: How granular should the chip-colour palette be? → A: **Three colours — green / grey / red.** Green for "link up and operational", red for "non-routine fault", grey for everything in-flight or user-paused. The chip's job is at-a-glance; the headline carries the sub-discriminator the colour can't.

### Session 2026-05-25 (bench feedback on PR #122 build, carries forward)

- Q: When the same root cause re-fires across user actions, should the `since` timestamp update each time, or stick to the first observation? → A: **Stick to the first observation.** `since` reflects when the root cause was first observed; it preserves across passive re-triggers and re-renders of the same cause. The timestamp updates only when the root cause itself changes, or when the state's family (Idle / Searching / Opening / Open / Faulted) changes.
- Q: Click-feedback contract for the user's actions — should the chip visibly transit through an in-flight grey state during a Reconnect call? → A: **No — the row mirrors feat-001's dictionary status row pattern.** The chip colour MUST always reflect the actual FSM family (truth-to-state). Operator-initiatable affordance buttons (Stop / Start / Reconnect) get a click-acknowledge cue (disabled state + spinner glyph) for the duration of the in-flight call. The chip's grey `Opening` transit during a Reconnect is itself a truthful render — BPT genuinely occupies `Opening` while the OpenAsync call is in flight — not a UX-affordance smoothing. State and click-acknowledgement are separate layers; the chip never lies about FSM state.

### Session 2026-05-26 (bench feedback on PR #147 draft, carries forward)

- Q: Should detail strings carry both cause and remediation suggestion? → A: **SHOULD, when an imperative naturally applies.** Detail strings SHOULD follow the form `<cause> — <imperative suggestion>` joined by em-dash. When no imperative organically belongs to the cause (e.g., an unrecognised status code), the cause stands alone. The row renders the detail verbatim.
- Q: Several cause strings carried a `PEAK ·` vendor-tag prefix; on the dedicated CAN status row this is redundant. → A: **Drop the pure-tag usage.** Keep substantive brand references where the brand is part of the product name (`PCANBasic native DLL`) or where the imperative target needs the brand to be unambiguous (`install the PEAK driver`). Pure-tag prefixes collapse to the bare cause.

### Session 2026-05-27 (Phase B redesign — FSM clarifications)

#### FSM shape

- Q: The substrate modelled the link with four families (`Initializing | Connected | Disconnected | Error`) and a binary `Recoverable / Fatal` severity on top of Error. Bench feel: severity escalation logic was operational, the "Disconnected" family covered too many distinct in-flight states, and there was no user-driven Pause. Reshape? → A: **Yes — five top-level FSM states with no severity classifier.** `Idle | Searching | Opening | Open | Faulted`. Sub-discriminators (`IdleCause`, `SearchAttempt`, `FaultCause`) live in the state's payload, not as sibling families — the chip-colour projection cares about the family, the detail string cares about the discriminator.
- Q: Should the FSM include an `Idle` state, even though the tool is "always trying to stay up" by default? → A: **Yes, as operator-paused only.** Bench-product convention: professional tools provide an explicit Disconnect / Stop option. Use case: "release the adapter so another exclusive-mode tool (e.g. StemDeviceManager) can use it". `Idle` carries a single sub-cause: `UserPaused` (user clicked Stop, stays put until Start).
- Q: Reconnect from `Faulted` — does it retry the same candidate, or re-enumerate from scratch? Where does the candidate memory live? → A: **`Faulted` carries the candidate in its payload** (`Faulted of cause * candidate: AdapterCandidate option * since`). When `Some`, Reconnect → `Opening(candidate)`. When `None` (e.g., driver-not-installed fault occurred before any adapter was enumerated), Reconnect → `Searching(Polling)`. The FSM is self-describing — no extra service-level side state.

#### Edges and iteration

- Q: Hot-plug recovery — substrate observed it as an undocumented vendored-stack freebie. Keep it implicit, or model it? → A: **Model it as an explicit FSM edge** — `Searching(Polling) ── vendored-stack device-arrived event ─▶ Opening(candidate)`. The dependency on the vendored stack's hot-plug semantics is no longer invisible; the #111 risk note ("future `Stem.Communication` adapter MUST emit a device-arrived event into the port") becomes an explicit contract.
- Q: Multi-adapter handling — substrate "first wins" stayed silent on what happens when the first enumerated adapter is busy and a second one is free. → A: **Iterate, as future-proofing resilience.** Production setup is always a single PEAK adapter; the iteration contract exists so the tool survives a tech accidentally plugging in two. When multiple adapters are enumerated, `Opening` tries each before declaring `Searching(NoCandidateAvailable count)`. No UI for selection; "first available wins" stays the rule.

#### Framing

- Q: Truth-to-state framing — what's the relationship between the row's rendering and the operator's perception of "did my click do something"? → A: **Truth-and-acknowledge, mirroring feat-001's `DictionaryStatusRow`.** The chip colour is the FSM family, always — no smoothing, no minimum-visibility floor. Operator-initiatable affordance buttons (Stop / Start / Reconnect) get a button-level acknowledge cue (disabled + spinner glyph) for the duration of the click's in-flight call. The dictionary row does the same: chip colour = subsystem truth (under spec-001 [#156](https://github.com/luca-veronelli-stem/button-panel-tester/issues/156) Option γ: dictionary-sync state — grey by default, red on a sync failure that needs operator action; copy-health rendered as separate decoration via origin marker + stale-seed glyph); chip opacity drop + headline ` · syncing…` ellipsis + spinner-glyph button = click acknowledgement. The two layers (subsystem-truth chip + button-level click cue) are independent — the chip never lies about FSM state to make a click visible.

#### Scope refinements

- Q: Adapter exclusivity stance — when BPT holds Open and another process requests access to the same adapter, what's BPT's behaviour? → A: **BPT requests exclusive driver-level access on Open and holds the link on contention.** External exclusive-mode tools (e.g. StemDeviceManager) see the driver's busy response when BPT is in Open. Shared-mode tools (e.g. PCAN-View, which does not request exclusive access) coexist without contention at the driver level. When the vendored protocol stack surfaces a contention event, BPT logs it as a structured Information-level entry; BPT does NOT transition out of Open. The driver enforces the lock; BPT observes the contention but does not yield.
- Q: Boot order — substrate's FR-001 gated CAN service start on dictionary-fetch boot completion. Keep the gate or decouple? → A: **Fully decouple at the domain level.** Dictionary and CAN have no shared infrastructure dependency; CAN does not consult dictionary content. The substrate's gate was a composition-root policy, not a technical requirement. CAN service starts at app launch independently; the FSM begins in `Searching(Polling)` from app launch, not `Idle(AwaitingBoot)`. `Idle` is operator-paused only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Inspecting CAN link state at start of shift (Priority: P1)

A supplier-side technician launches the tool to start a testing session. They need to know whether the tool can talk to the panel hardware — before plugging in any boards or running any tests.

A persistent **CAN status row** is part of the main window. It is a small, glance-friendly composition: a colour-coded chip (green / grey / red), a one-line headline that names the current FSM state and its discriminator, a detail affordance (adapter identification, baud rate, full multi-line cause when applicable), and the user-control buttons that apply to the current state. The technician reads the row at a glance and decides whether to plug in the PEAK adapter, check its wiring, or release another tool that has the adapter open.

**Why this priority**: this is the smallest end-user value the slice can deliver — the technician can answer "is my CAN link up, and if not why?" before any panel exists on the bus. Every downstream concern (panel discovery in spec-003, variable IO in later specs) depends on the status row already being there.

**Independent Test**: launch the tool on a freshly-installed machine with no PEAK adapter present. Verify the CAN status row appears within 1 second of the main window. Verify the row's chip is grey and the headline names "no PEAK adapter found". Plug the adapter in — verify the row passes through grey `Opening` and lands on green `Open` within 5 seconds, without any user click (hot-plug recovery via the vendored stack's device-arrived event).

**Acceptance Scenarios**:

1. **Given** the tool has just launched and no PEAK adapter is present, **When** the main window appears, **Then** within 1 second the CAN status row shows a grey chip with the `Searching · no PEAK adapter found — plug in the adapter` headline.
2. **Given** the row is showing `Searching` and the technician plugs in a PEAK adapter, **When** the vendored stack signals the device-arrived event, **Then** the row transits through `Opening · contacting <adapter>` (grey) and lands on `Open · <adapter identification>` (green) within 5 seconds — no manual click required.
3. **Given** the row is showing `Open`, **When** the technician opens the detail affordance, **Then** they see the adapter identification, the configured bus baud rate, and the `since` timestamp of the most recent state-family change.

---

### User Story 2 — Surviving mid-session bench realities (Priority: P2)

A technician is mid-shift with the tool open. Bench reality intervenes: they bump the adapter loose; they want to release the adapter for a moment so they can attach StemDeviceManager to investigate a related panel; the bus drops into bus-off after a misbehaving panel; or the adapter is already busy because StemDeviceManager has it open exclusively. The tool needs to reflect each of those situations promptly, stay usable so the dictionary state from feat-001 is preserved, and give the technician the right user-controls for the situation: **Reconnect** when a fault may clear by re-opening; **Stop** to release the adapter explicitly; **Start** to resume after a Stop.

**Why this priority**: this story tests robustness rather than a happy-path capability. Essential for bench credibility but does not block the supplier from validating a panel — if nothing goes wrong and the user never wants to release the adapter, P1 alone delivers the supplier's lifecycle goal.

**Independent Test**: with the tool in `Open`, exercise each mid-session path independently. Unplug → row reflects loss within 5 seconds, re-plug → row recovers without a click. Click Stop → row goes to `Idle(UserPaused)`, click Start → row resumes scanning. Trigger bus-off → row lands on `Faulted(BusOff)`, click Reconnect → row transits through `Opening` back to `Open` (or back to `Faulted` if the fault persists). Open StemDeviceManager against the same adapter exclusively while the tool is running → BPT holds Open, the contention is logged but the FSM does not transition; from a Stop-then-Start cycle BPT's row then shows `Searching(NoCandidateAvailable, 1)` because StemDeviceManager now holds the adapter.

**Acceptance Scenarios**:

1. **Given** the row is `Open`, **When** the technician unplugs the PEAK adapter, **Then** the row transitions to `Searching · waiting for adapter to come back` within 5 seconds, chip grey, and the dictionary status row from feat-001 is unchanged.
2. **Given** the link has been lost to an unplug, **When** the technician re-seats the adapter without clicking anything, **Then** the row recovers to `Open` within 5 seconds (hot-plug auto-recovery via the vendored stack's device-arrived event).
3. **Given** the row is `Open`, **When** the technician clicks **Stop**, **Then** the row transitions to `Idle · paused by user — click Start to resume` (chip grey), the **Start** button becomes the only enabled action, and the adapter is released so external exclusive-mode tools (e.g. StemDeviceManager) can attach to it.
4. **Given** the row is `Idle(UserPaused)`, **When** the technician clicks **Start**, **Then** the row transitions to `Searching` and resumes the scan loop.
5. **Given** the row is `Open` and the CAN controller signals bus-off, **Then** the row transitions to `Faulted · bus-off — try Reconnect` (chip red) within 5 seconds, and the **Reconnect** button is enabled.
6. **Given** the row is `Faulted(BusOff)` against a known adapter, **When** the technician clicks **Reconnect**, **Then** the Reconnect button is disabled and shows a spinner glyph for the duration of the in-flight call (click-acknowledge cue), the chip transits through `Opening · contacting <adapter>` (grey) because BPT is genuinely calling OpenAsync, and lands either on `Open` (green) if the fault cleared, or back on `Faulted` (red) with the new `since` if the same fault re-fires.
7. **Given** BPT is in `Idle(UserPaused)` and StemDeviceManager has the adapter open exclusively, **When** the technician clicks **Start**, **Then** BPT enumerates the adapter, attempts Open, the driver returns busy, and the row lands on `Searching · no available adapter (1 found, busy) — release the other tool's link or attach a second adapter`, chip grey.
8. **Given** the host has no PEAK driver installed, **Then** on the first Open attempt the row lands on `Faulted · PEAK PCANBasic native DLL not found — install the PEAK driver` (chip red, `candidate: None`). **Reconnect** in this case is effectively a re-search (`Faulted(_, None, _) → Searching(Polling)`), because there is no known candidate to retry; the button caption SHOULD make clear that the click resumes scanning rather than retrying a specific adapter.

---

### Edge Cases

- **PEAK driver installed, no physical adapter on the host**: `Searching(NoAdapterEnumerated)` — chip grey, headline says "no PEAK adapter found — plug in the adapter".
- **Multiple PEAK adapters present, some busy**: `Opening` iterates through enumerated adapters; the first one to return success wins. If every enumerated adapter returns busy, the FSM lands on `Searching(NoCandidateAvailable count)`. Single-adapter bench is the `count = 1` case (the production reality; multi-adapter iteration is bench-resilience).
- **CAN controller enters bus-off**: `Open → Faulted(BusOff, Some <adapter>, since)`. Reconnect is enabled and retries the same adapter; if the bus-off persists, the row returns to `Faulted(BusOff)` with a refreshed `since` (because the root cause was re-observed after a clearing attempt — sticky-since does not apply across a user-initiated Reconnect that returns to the same family).
- **PEAK driver returns an unrecognised status code**: `Open → Faulted(UnexpectedAdapterStatus code, Some <adapter>, since)`. The detail string carries the raw status code so the technician has actionable diagnostic information when filing a bug.
- **PEAK driver not installed**: detected on the first OpenAsync from `Opening`. Lands on `Faulted(DriverNotInstalled, None, since)`. `Reconnect` collapses to `Searching(Polling)` because there is no candidate to retry.
- **User clicks Stop while in any active state**: the row transitions to `Idle(UserPaused)` and the adapter (if held) is released. The user holds the row in this state until they click Start. If the click lands while the FSM is in `Opening`, the in-flight OpenAsync call is cancelled before the transition to `Idle(UserPaused)` (see FR-006).
- **Hot-plug while in `Searching(Polling)`**: the vendored stack raises its device-arrived event; the FSM transitions to `Opening(candidate)` and proceeds normally. This depends on the vendored protocol stack's hot-plug semantics — see Dependencies + [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111).
- **Adapter raises a non-routine hardware fault while in `Open`** (e.g., driver responded but adapter became unresponsive): `Open → Faulted(AdapterHardwareFailure, Some <adapter>, since)`. Reconnect retries the same adapter — useful when the failure was transient — but a persistent failure stays in `Faulted` until the user takes external action.
- **External exclusive-mode tool requests adapter access while BPT holds Open**: the PCAN driver denies the contender (BPT requested exclusive on Open, per FR-010). BPT remains in `Open` — the contention does NOT trigger an FSM transition. If the vendored protocol stack surfaces a contention event, BPT logs it at Information level per FR-011; otherwise the contention is invisible to BPT. Shared-mode tools (PCAN-View) coexist and do not trigger this path.
- **Host suspend / resume cycle**: when the workstation sleeps and wakes, the OS unbinds and rebinds the PEAK adapter handle. From BPT's perspective the cycle is observable as a normal hot-plug sequence — a device-lost event (`Open → Searching(Polling)`) followed by a device-arrived event (`Searching(Polling) → Opening(candidate) → Open`). If the resume leaves the driver in an inconsistent state, the OpenAsync after resume fails and the FSM lands on `Faulted(UnexpectedAdapterStatus _, Some <adapter>, since)`. No special-cased "resume" FSM path is required.
- **Driver uninstall while BPT is running**: if the PCAN driver is uninstalled while BPT is in `Open`, the next driver call fails. The FSM transitions to `Faulted(DriverNotInstalled, Some <last adapter>, since)`. Reconnect is offered; it will not succeed until the driver is reinstalled, at which point a manual Reconnect lands the FSM back in `Opening → Open`. (Note: when BPT is in `Open` and the driver is removed without an active driver call, the failure may surface lazily on the next observation; the resulting Faulted's `since` reflects when the failure was observed, not when the uninstall happened.)
- **Vendored-stack device-arrived event for a different adapter while BPT is in `Open`**: BPT ignores the event for FSM purposes (already in `Open` against a different adapter). The structured log MAY note the event at Information level for forensic purposes. FR-012's iteration contract applies only while in `Searching`; once `Open`, BPT does not re-shop for a different adapter.

## Requirements *(mandatory)*

### Presentation surfaces

The CAN status row is one of two complementary surfaces governed by the STEM standards (`docs/Standards/APP_SHELL.md` + `docs/Standards/DESIGN_SYSTEM.md` + `docs/Standards/LOGGING.md`). The split below is normative for every requirement in this section that mentions "chip", "headline", "detail affordance", or "log":

| Surface | Lifetime | Role | Content rule |
|---|---|---|---|
| **Continuous-state indicator** (CAN status row: chip + headline + detail affordance) | Always-on, reflects "now" | Tells the technician *what is true right now* at a glance, and what they can do about it | Chip colour carries the FSM family (green for `Open`, red for `Faulted`, grey for `Idle` / `Searching` / `Opening`). Headline names the family and its discriminator with optional imperative suggestion. Detail affordance is a convenience overview that surfaces supporting facts on demand: adapter identification, baud rate, full multi-line cause, `since` timestamp. The Start / Stop / Reconnect buttons are part of the row, available per the state's affordance map. The detail affordance is a convenience overview, NOT the primary diagnostic surface — see "forensic log" below. |
| **Forensic log** (`ILogger<T>` per `LOGGING.md`) | Persistent, independent of any user-facing mute | **Primary diagnostic surface.** Tells the operator *what happened, in retrospect* | Every FSM transition emits one structured log entry. Level mapped to family: `Open` → Information, `Faulted` → Error, everything else → Information (transition is informational unless the destination is Faulted). External-contention attempts (FR-011) log at Information level when surfaced by the vendored stack. Fields: state name, discriminator, since-timestamp, adapter identification when relevant. Forensic record is unaffected by `Settings.Notifications.Muted`. Audit tracked separately in [#148](https://github.com/luca-veronelli-stem/button-panel-tester/issues/148). |

The same `CanLinkState` value flows into both surfaces; each projects the parts it needs.

### Operator-initiatable transitions

The row is "true to actual state and exposes the real options the operator has in that moment". The operator's real options from each state are captured below as the abstract transition contract; the surface-level affordance FRs (FR-006 / FR-007 / FR-008) land how the row's buttons realise these transitions.

| Current state | Operator-initiatable transition | Target state | Realised as |
|---|---|---|---|
| `Searching(_, _)` | User-Pause | `Idle(UserPaused, now)` | Stop |
| `Opening(_, _)` | User-Pause (with OpenAsync cancellation) | `Idle(UserPaused, now)` | Stop |
| `Open(_, _)` | User-Pause | `Idle(UserPaused, now)` | Stop |
| `Faulted(_, _, _)` | User-Pause | `Idle(UserPaused, now)` | Stop |
| `Idle(UserPaused, _)` | Resume-Searching | `Searching(Polling, now)` | Start |
| `Faulted(_, Some candidate, _)` | Resume (retry known candidate) | `Opening(candidate, now)` | Reconnect |
| `Faulted(_, None, _)` | Resume (no candidate; re-search) | `Searching(Polling, now)` | Reconnect |

No other operator-initiatable transition exists. All other FSM edges are observation-driven (vendored-stack events, driver replies, internal timers).

### Functional Requirements

**FSM and lifecycle**

- **FR-001**: System MUST hold the CAN link in one of exactly five top-level states — `Idle`, `Searching`, `Opening`, `Open`, `Faulted` — and MUST expose the current state and its payload to consumers (see FR-014).
- **FR-002**: System MUST surface the FSM state in a persistent CAN status row. The chip-colour projection is fixed: **green** for `Open`, **red** for `Faulted`, **grey** for `Idle` / `Searching` / `Opening`.
- **FR-003**: System MUST render the row's headline as `<family> · <discriminator detail>` where the detail SHOULD follow `<cause> — <imperative suggestion>` joined by em-dash when an imperative naturally applies to the cause. When no imperative organically belongs (e.g., an unrecognised status code), the cause stands alone. The chip colour carries the family already, so the family word disambiguates the grey-shared cases (`Idle` vs `Searching` vs `Opening`) and is redundant-but-explicit on green and red.
- **FR-004**: For every state surfaced to the user, the `since` timestamp shown in the detail affordance MUST reflect the moment the underlying root cause (state family + discriminator) was first observed. Passive re-observation of the same family + discriminator MUST preserve the original `since`. The timestamp updates when (a) the family changes, (b) the discriminator within a family changes (e.g., `Searching(NoAdapterEnumerated) → Searching(Polling)`), or (c) the user takes an action that returns the FSM to the same family via an intervening state (e.g., `Faulted(BusOff) → Opening → Faulted(BusOff)` updates `since` on the second arrival).
- **FR-005**: System MUST expose, through the detail affordance, the adapter identification when one is known (`Open`, `Faulted(_, Some _, _)`, and during `Opening`), the configured bus baud rate, the `since` timestamp, and the full multi-line cause string for `Faulted` states.

**User affordances**

The affordance FRs below realise the abstract transition contract in the "Operator-initiatable transitions" subsection. Each FR names the affordance, its surface conditions, and the FSM transition it triggers.

- **FR-006 (Stop)**: System MUST provide a **Stop** affordance on the row, visible and enabled when the FSM is in any active state (`Searching`, `Opening`, `Open`, `Faulted`). Clicking Stop MUST transition the FSM to `Idle(UserPaused)` and release any held adapter. Stop's purpose covers (a) release for external exclusive-mode tools (e.g. StemDeviceManager), (b) safety / manual override, (c) diagnostic isolation, (d) professional bench convention; the FR does not carve out a single reason and does not optimise for usage frequency. **Cancellation semantics**: when the click lands while the FSM is in `Opening`, the System MUST cancel the in-flight OpenAsync call via `CancellationToken` propagation (per STEM `CANCELLATION` standard) before the FSM transitions to `Idle(UserPaused)`; the transition MUST NOT wait for OpenAsync to complete on its own. Cancellation is timely: the FSM SHOULD land in `Idle(UserPaused)` within the cancellation budget pinned in `plan.md`.
- **FR-007 (Start)**: System MUST provide a **Start** affordance on the row, visible and enabled when the FSM is in `Idle(UserPaused)`. Clicking Start MUST transition the FSM to `Searching(Polling, now)`.
- **FR-008 (Reconnect)**: System MUST provide a **Reconnect** affordance on the row, visible and enabled when the FSM is in `Faulted`. The effect depends on the candidate carried in the `Faulted` payload:
  - `Faulted(_, Some candidate, _) → Opening(candidate, now)` — retry the known candidate.
  - `Faulted(_, None, _) → Searching(Polling, now)` — no candidate to retry; resume scanning. The button caption SHOULD make this clear (e.g., `Reconnect (resume scanning)`).
- **FR-009 (Click-acknowledge contract)**: The row mirrors the truth-and-acknowledge pattern of feat-001's `DictionaryStatusRow`. The chip colour MUST always reflect the actual FSM family per FR-002 — no smoothing, no minimum-visibility floor, no chip-level UX layer added to make a click visible. Operator-initiatable affordance buttons (Stop / Start / Reconnect) MUST visibly acknowledge the click via a button-level cue: the clicked button MUST become `IsEnabled = false` AND show the in-flight glyph that the dictionary status row established (`⟳` — see `DictionaryStatusRow.fs:151-158`) for the duration of the **in-flight call** the click initiated. **In-flight call** means: for Reconnect, the OpenAsync call invoked by the click, from invocation through the next FSM emission (Open or Faulted); for Stop, the close-and-cancel sequence ending with the FSM emission of `Idle(UserPaused)`; for Start, the synchronous transition to `Searching(Polling)` (sub-millisecond, may be perceptually invisible). The chip's grey `Opening` transit during a Reconnect is itself a truthful render — BPT genuinely occupies `Opening` while the OpenAsync call is in flight — not a UX-affordance smoothing. State rendering and click-acknowledgement are independent layers. **Note**: the acknowledge cue's visible duration matches the in-flight call's actual duration; a sub-perceptual call (e.g., Start, or a fast Reconnect) is consistent with the truth-to-state principle and is not a defect — the operator's signal that the click was processed comes from the resulting FSM transition (chip colour change and / or `since` update), not from the cue itself.

**Adapter exclusivity**

- **FR-010 (Exclusive driver access)**: System MUST request exclusive driver-level access to the PEAK adapter on the OpenAsync call that takes the FSM into `Open`. While BPT holds `Open`, other processes that request exclusive access to the same adapter MUST observe the driver's busy response. Processes that request shared access (e.g. PCAN-View) coexist without contention at the driver level. Exclusivity is a per-adapter driver-level fence; the spec does not constrain how the impl requests it.
- **FR-011 (External contention observability)**: While in `Open`, the System MUST log any external-contention attempt surfaced by the vendored protocol stack as a structured Information-level entry (`ILogger<T>` per `LOGGING.md`). Contention does NOT trigger an FSM transition; BPT holds the link. **Caveat**: this MUST applies to the contention events the vendored protocol stack chooses to surface. The current `Infrastructure.Protocol` may be silent on contention; the FR is forward-looking and becomes a hard MUST once [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111)'s `Stem.Communication` replacement lands with the corresponding event. If the current stack is silent, no log entry is required — BPT cannot observe what the stack does not surface.

**Adapter enumeration and iteration**

- **FR-012**: When `Searching` and the host enumerates ≥ 1 PEAK adapter, the FSM MUST attempt `Opening` against each enumerated adapter in turn before declaring `Searching(NoCandidateAvailable count)`. The first adapter to return a successful OpenAsync wins. No UI for adapter selection — "first available wins" is the rule. The single-adapter bench is the `count = 1` special case (the production reality; multi-adapter iteration is bench-resilience for the accidental two-adapter case).

**Boundary**

- **FR-013**: System MUST NOT transmit any CAN frame in this slice. The lifecycle is open-and-observe only. Transmit-side behaviour (probing, addressing, variable read/write) is the subject of a later feature.
- **FR-014**: System MUST surface every transition of the top-level FSM family (`Idle`, `Searching`, `Opening`, `Open`, `Faulted`) through a `LinkStateChanged` observable so downstream consumers — notably spec-003's Panels-on-bus list, see [spec-003 FR-015'](../003-panel-discovery/spec.md) — can react. The observable's payload is the full `CanLinkState` value (family + discriminator + payload + `since`); consumers project what they need. Discriminator-only changes (e.g., `Searching(NoAdapterEnumerated) → Searching(Polling)`) are observable too; consumers that only care about family changes filter the stream themselves.
- **FR-015**: System MUST keep the dictionary status row from feat-001 unaffected by CAN-side events. The two rows are independent and a problem in one MUST NOT degrade the other. Dictionary boot completion does NOT gate the CAN service's first scan; the two services run independently from app launch and populate their respective rows when each has something truthful to report.
- **FR-016**: The GUI MUST stay responsive when the CAN side is in any state other than `Open`. The dictionary status row remains visible and interactive, panel-discovery-side surfaces (spec-003) clear themselves per their own contract, and the rest of the UI does not block.
- **FR-017 (Accessibility)**: The row's actionable elements (Stop / Start / Reconnect buttons, and the detail-affordance trigger if implemented as a clickable expansion rather than a hover tooltip) MUST be reachable via keyboard navigation with a logical focus order, and each MUST expose a screen-reader-readable label naming its action or current FSM state. The chip's FSM family MUST be conveyed via a non-visual cue (e.g., an accessibility-text representation of the family name and discriminator) so screen-reader users get equivalent information to the colour. Concrete a11y conventions follow the design system's defaults; this FR pins the contract that the row is not exclusively pointer-driven or colour-driven.

### Key Entities

- **CAN link state**: the observable status of the adapter and the user's intent. Carries (a) the FSM family — one of `Idle | Searching | Opening | Open | Faulted` — (b) a discriminator payload appropriate to the family (`IdleCause`, `SearchAttempt`, `AdapterCandidate`, `AdapterIdentification`, `FaultCause` + optional candidate, respectively), and (c) a `since` timestamp. The DU shape is detailed in [`data-model.md`](./data-model.md).
- **Adapter candidate**: an enumerated PEAK adapter identifier the FSM has selected to open. Distinct from "adapter identification" (which is the post-Open self-description); a candidate exists once enumeration returns it, before any successful Open.
- **Adapter identification**: the self-description of an opened adapter (vendor-assigned device id, channel, etc.). Available from `Open` onwards.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On every launch where exactly one available PEAK adapter is present, the CAN status row reaches `Open` (green) within 2 seconds of app launch.
- **SC-002**: On every launch where no PEAK adapter is present on the host, the row reaches `Searching(NoAdapterEnumerated)` (grey) within 1 second of app launch.
- **SC-003**: When the PEAK adapter is unplugged mid-session, the row reflects the loss within 5 seconds.
- **SC-004**: When the unplugged adapter is re-seated, the row recovers to `Open` within 5 seconds, without a manual click — verifiable on the current vendored stack; flagged as at-risk for #111's `Stem.Communication` replacement (see Dependencies).
- **SC-005**: When the user clicks **Stop**, the adapter is released (verifiable by an external exclusive-mode tool such as StemDeviceManager attaching successfully) within 2 seconds.
- **SC-006**: A failure mode on the CAN side (`Searching`, `Faulted`, `Idle(UserPaused)`) does not affect the dictionary status row from feat-001 in any observable way, across 100% of trials.
- **SC-007**: The tool transmits zero CAN frames while operating in this slice's scope. Verifiable by passively monitoring the bus with an independent capture tool throughout a session — the captured trace shows only frames originating from the panels under test, never from the tool.
- **SC-008**: When the CAN controller signals a non-routine fault (bus-off, unexpected PEAK status, hardware failure, driver missing), the row reaches `Faulted` with a concrete cause string and (where applicable) imperative suggestion within 5 seconds of the underlying event.
- **SC-009**: When two enumerated adapters are present and exactly one is busy, the FSM reaches `Open` against the free adapter within 5 seconds of completing enumeration. (The "iterate through enumerated adapters" contract from FR-012 holds in this bench-resilience setup; single-adapter is the production reality.)
- **SC-010**: When the operator clicks **Reconnect** from `Faulted(_, Some _, _)`, the Reconnect button is observably disabled and shows the in-flight glyph (`⟳`) from click time through the next FSM emission, verifiable in a headless Avalonia.Headless GUI test that asserts `IsEnabled = false` and `Content = "⟳"` on the button while the in-flight OpenAsync call is pending.
- **SC-011**: When BPT is in `Open` against a PEAK adapter and an external process attempts an exclusive open against the same adapter (verified by running StemDeviceManager or any exclusive-mode PCAN client), the external process observes the driver's busy response and BPT's FSM remains in `Open`.
- **SC-012**: When the vendored protocol stack surfaces a contention event while BPT is in `Open`, BPT emits exactly one structured Information-level log entry per surfaced event, verifiable by parsing the structured log output. (This SC's MUST is conditional on the vendored stack surfacing contention; if the stack is silent, the SC is satisfied trivially with zero log entries — see FR-011 caveat.)

## Assumptions

- The supplier bench has exactly one PEAK PCAN-USB adapter plugged into the test workstation in the production setup. Multi-adapter handling (FR-012, SC-009) is bench-resilience: an iteration contract that the production case satisfies trivially with `count = 1`.
- The CAN bus baud rate is 250 kbps. Fixed by the panel firmware, not technician-configurable.
- The dictionary fetched by feat-001 is not consulted to drive any CAN-side decisioning in this slice. The CAN lifecycle is fully decoupled from dictionary content; the two features share no domain coupling. Spec-001 further guarantees the dictionary is always usable at runtime (the embedded seed in the binary covers the no-network, no-prior-cache case — see spec-001 §138), so no scenario in this spec needs to consider an "unusable dictionary" branch.
- The user is willing to use the explicit Stop / Start controls when they want to release the adapter for an external tool. The tool does not auto-yield the adapter in response to any out-of-process signal.
- App lifecycle covers both discrete shift sessions (open at start of shift, test, close at end — the dominant pattern) and occasional long-running sessions (open and leave running across hours or days). FSM transitions and `since` semantics MUST tolerate state dwells from seconds to days; the row gracefully conveys long-dwell states.
- Production bench uses a direct USB connection between the workstation and the PEAK adapter; USB hubs and variable-quality cabling are out of scope. Hot-plug latency and bus-off frequency in the spec's budgets (SC-001 .. SC-008) assume clean direct USB.
- Exactly one BPT instance runs per host workstation. Multi-instance gating is a future app-shell concern (see Out of Scope).

## Dependencies

### Blocking dependencies

- Feat-001's dictionary-fetch service and status-row visual conventions (`Components/DictionaryStatusRow.fs`, 1-second budget for first paint after window appears, Avalonia + FuncUI layout). The CAN row's truth-and-acknowledge layering (FR-009) mirrors the dictionary row's pattern (chip colour = subsystem truth; chip opacity drop + headline ellipsis + spinner-glyph button = click acknowledgement). Under spec-001 [#156](https://github.com/luca-veronelli-stem/button-panel-tester/issues/156) Option γ the dictionary chip carries sync-subsystem truth with copy-health rendered as decoration; the two-layer precedent (subsystem-truth chip + button-level click cue) is unchanged by that reshape.
- A PEAK PCAN-USB driver installation on the test workstation (`PCANBasic` native DLL on the OS DLL search path). Required for the FSM ever to leave `Faulted(DriverNotInstalled, _, _)`.
- **Vendored protocol stack hot-plug behaviour** (`Infrastructure.Protocol`). The `Searching(Polling) ── vendored-stack device-arrived event ─▶ Opening` edge depends on the stack emitting a device-arrived event when an adapter is plugged into a host that was previously empty. **Risk note**: this is not a documented contract of the vendored stack; if [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111)'s `Stem.Communication` replacement ships without an equivalent affordance, hot-plug auto-recovery regresses to manual-Reconnect-only. The migration plan for #111 MUST add an acceptance check that the device-arrived event is preserved (or note the regression).
- **Spec-003 panel discovery** is the downstream consumer of `LinkStateChanged`. Spec-003 depends on spec-002's lifecycle; spec-002 does not depend on spec-003. The two specs cohabit one F# service class (`Services/Can/CanLinkService.fs`) — the seam is in the docs, not the code. Spec-003's FR-015' consumer contract will need a re-derive against the new payload shape (`CanLinkState` family changed) once Phase B lands; tracked in `spec-003-live-roadmap.md`.

### Forward-looking dependencies (referenced, not blocking)

- **Vendored protocol stack contention observability** (`Infrastructure.Protocol`). FR-011's structured Information log on external contention applies to the contention events the vendored stack chooses to surface. The current stack may be silent; the FR is forward-looking against [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111)'s replacement and becomes a hard MUST once a contention event is surfaced.
- The audit captured in [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md), specifically §C4 (the protocol-stack source consumed by this feature). Informational reference; not a runtime dependency.

## Out of Scope (for this feature)

- Transmitting any CAN frame: probing, addressing, baptizing, variable read/write — all deferred to later features.
- Multi-adapter selection UI: the FSM iterates and takes the first available; user-facing selection between healthy candidates is a future concern if it ever arises.
- Panel discovery: handled by [`specs/003-panel-discovery/`](../003-panel-discovery/).
- Configurable bus baud rates: 250 kbps is the firmware-fixed value.
- Persisting user-paused state across application restarts: a Stop click does not survive a relaunch — the tool resumes scanning from `Searching(Polling)` on the next launch.
- Multi-instance gating per host: a future app-shell concern. This spec assumes a single BPT instance per host. FR-012's `NoCandidateAvailable` covers the failure mode if a second instance is ever launched and bypasses the singleton gate.
- NotificationCenter surface implementation: a future spec (when it ships) MAY refactor how the cause/suggestion split surfaces; this spec carries both via the FR-003 em-dash convention without anticipating that future change.
