# Roadmap — specs 002 through 007

Six follow-on specs, each a vertical slice from `Core` interface down
through `Services` orchestration and `Infrastructure` adapters to the
`GUI`. Each is sized to retire one well-defined hardware-risk story.

The receiving Claude should run them through the standard SDD flow:

```
/speckit.specify     ← input = the "Input paragraph" below
/speckit.clarify     ← walks the "Open questions" below
/speckit.plan        ← honours the "Locked design decisions" below
/speckit.tasks
/speckit.implement   ← TDD per the project's constitution
                       (Lean theorem first when relevant; F# tests
                        before F# code; manual fakes only)
```

Vendored protocol stack: see `02-vendoring-plan.md`. The interfaces
`ICommunicationPort` and `IProtocolService` are introduced in spec-002
and remain stable across all subsequent specs.

---

## spec-002 — CAN link and panel discovery

**One-line:** open the PEAK PCAN-USB adapter, surface its state in a
status row, listen for panels announcing themselves, show what's there.

### Input paragraph for `/speckit.specify`

> Open the configured PEAK PCAN-USB adapter at 250 kbps as soon as
> the application has finished its dictionary-fetch boot sequence,
> and keep a persistent CAN-bus status row on the main window with the
> same shape and behaviour as the dictionary status row from feat-001:
> a colour-coded headline ("Connected", "Disconnected", "Error"), a
> human-readable detail (adapter handle, last error reason), and a
> manual reconnect control. While connected, listen for STEM
> auto-address `WHO_I_AM` broadcasts on the bus and present any panels
> seen in a passive "Panels on bus" panel: UUID, current MachineType
> (decoded to its marketing name if known, "virgin" if MachineType is
> `0xFF`, "unknown" otherwise), and the timestamp of the most recent
> broadcast from that panel. No commands are sent; this slice is pure
> observation. If no PEAK adapter is present, the status row shows
> "Disconnected — no PEAK adapter found"; the rest of the UI stays
> usable so the dictionary status (from feat-001) remains visible.
> The Panels-on-bus panel is empty in that state.

### Locked design decisions

- **CAN bitrate:** 250 kbps hardcoded (matches firmware v5.0). No UI.
- **Adapter selection:** auto-pick the first PEAK adapter enumerated by
  the vendor SDK. If multiple are present, pick the first and emit a
  warning to the structured log (deferred for a future spec).
- **Graceful degradation:** missing adapter is not a fatal error;
  application stays open, test-related actions disabled. (Confirmed
  with the user.)
- **Discovery is passive.** The tool never sends `WHO_ARE_YOU` in
  spec-002. That arrives in spec-003.
- **The `ICommunicationPort` and `IProtocolService` seams are
  introduced here.** Their shape is the integration boundary that
  shields the rest of the codebase from the vendored protocol stack.
- **The vendored `stem-communication` tree is introduced here.** See
  `02-vendoring-plan.md` for the discipline.

### Open questions for `/speckit.clarify`

1. Where does the "Panels on bus" panel sit visually relative to the
   dictionary status row and (future) test workflow area? Layout
   decision; affects the Avalonia view-tree.
2. How long does a panel's last-seen broadcast linger in the panel
   list before it's pruned? Each panel re-announces on a UUID-derived
   stagger ≤ ~4 s, so a 15 s timeout seems generous. Confirm.
3. Should the status row distinguish "adapter present but offline"
   (cable, no termination, no power on the bus) from "adapter not
   present"? Likely yes — different remediations.
4. Hot-plug of the PEAK adapter: should the tool poll for newly-arrived
   adapters, or require a manual click on the reconnect control?
5. Structured-log destination: stick with the NReco file sink from
   feat-001 (`fddb149`), or open a new file dedicated to CAN traffic?
   See spec-007 for the full forensic-logging discussion.

### Out of scope (the scope cliff)

- Sending any CAN frame.
- Multi-panel disambiguation beyond list-them-all.
- Anything specific to BoardVariant — the panel display in spec-002
  uses raw `MachineType` bytes, with display name lookup as the only
  domain mapping.
- Bus-load metrics, frame-rate display, traffic graphs.

### Hardware-risk story retired

- Peak.PCANBasic.NET wiring works on Win11 with .NET 10.
- First end-to-end CAN frames flow.
- The vendored decoder works against real bytes for the `WHO_I_AM`
  payload.

### Suggested Lean theorem

`PcanPort_open_idempotent`: opening an already-open port returns the
same handle and does not produce duplicate frame-received events.

---

## spec-003 — Baptism workflow

**One-line:** send `WHO_ARE_YOU` to claim a virgin panel as a chosen
machine variant; also reset a claimed panel back to virgin.

### Input paragraph for `/speckit.specify`

> Add a Baptism workflow that takes a panel currently seen on the bus
> (from spec-002's Panels-on-bus panel) and either claims it as one of
> the four known BoardVariants or resets it back to virgin. The UI is
> a side panel that activates when the technician selects a panel
> entry: dropdown of the four BoardVariants (EDEN-XP, OPTIMUS-XP,
> R-3L XP, EDEN-BS8) plus a "Reset to virgin" action. On Baptize, the
> tool sends `WHO_ARE_YOU(MachineType=<target>, FirmwareType=0x0004,
> reset=0)` on the auto-address listen ID and waits up to 6 seconds
> for the panel to re-broadcast `WHO_I_AM` with the target MachineType,
> reporting success when it does or a structured failure (timeout /
> unexpected MachineType / panel disappeared) when it does not. On
> Reset to virgin, the tool sends `WHO_ARE_YOU(MachineType=0xFF,
> FirmwareType=0x0004, reset=1)` and reports success on CAN-write
> completion, without waiting for re-announcement (firmware behaviour:
> the panel re-announces on a delay but does not synchronously reply).
> If more than one panel is seen on the bus, the Baptism action is
> disabled with an explanation ("Baptism requires exactly one panel
> on the bus"); this protects against accidentally baptizing the
> wrong device.

### Locked design decisions

- **BoardVariant table is hardcoded** in `Core/BoardVariant.fs` per
  the table in `01-context.md` §4.
- **Single-panel-on-bus enforcement.** Baptism is disabled when the
  Panels-on-bus list has ≠ 1 entry. This is the simplest and safest
  policy.
- **Baptize timeout:** 6 s (covers the ~4 s firmware stagger plus
  bus + decoder latency, with margin).
- **Reset confirmation = CAN-write completion.** Don't fake a wait;
  the firmware doesn't reply synchronously.
- **Reversibility:** every baptism is reversible by a subsequent
  Reset-to-virgin; the tool does not lock or remember anything about
  panels it has baptized.

### Open questions for `/speckit.clarify`

1. Re-baptism (panel currently claimed as A → claim as B): does the
   firmware require a virgin-reset in between, or can the tool send a
   new `WHO_ARE_YOU` directly? Legacy code suggests the latter via
   the AA listen ID — confirm against firmware before specifying.
2. UI confirmation step before destructive actions (Baptize, Reset)?
   Probably yes for Reset (technician loses panel identity); maybe
   not for Baptize (the technician already had to pick a variant).
3. What is the recovery if the baptism succeeds on the wire but the
   tool's wait-for-WHO_I_AM times out (e.g. the panel re-announced
   late)? Suggested: surface as "Baptism likely succeeded — re-check
   panel state", not as a failure.
4. Audit trail: should baptism actions write to the structured log
   with the operator name, target variant, panel UUID? (Probably
   yes; feeds into spec-006's report.)

### Out of scope

- Bulk baptism (a sequence of panels).
- Per-board-number variants (`BoardNumber > 1`).
- Recording per-panel baptism history.

### Hardware-risk story retired

- Writes to panel EEPROM work over CAN.
- The baptism handshake matches the firmware state machine on real
  iron, including the asymmetric reply behaviour.
- The vendored protocol stack handles the auto-address listen ID
  correctly.

### Suggested Lean theorem

`baptize_progress`: starting from a virgin panel and a valid target
MachineType, the baptism state machine reaches `Confirmed` if and
only if a `WHO_I_AM` carrying the target MachineType is received within
the timeout.

---

## spec-004 — Button-press test (input side)

**One-line:** walk the technician through the panel's active buttons
one at a time, observing the CAN frame on press, scoring pass/fail.

### Input paragraph for `/speckit.specify`

> Add a sequential button-press test that, given a panel already
> baptized as a known BoardVariant, prompts the technician to press
> each active button in the variant's mask in a defined order (UP →
> DOWN → P1 → P2 → P3 → MEM → STOP → LIGHT, filtered to the active
> set). The UI highlights the prompted button using the firmware name
> ("Press P2 now") and shows a configurable countdown (default 10 s)
> until timeout. While prompted, the tool listens on the panel's
> application-layer button-event channel; the first matching event
> within the timeout scores Pass, any other event scores Unexpected,
> and a timeout scores Missed. The technician has per-button Retry and
> Skip controls; Skip moves on and records the result as Skipped (not
> Pass). At end-of-sequence the tool shows a per-button result grid
> (firmware name + Pass/Missed/Skipped + observed event metadata where
> applicable) and an aggregate "all-active-passed" flag. The test can
> be re-run end-to-end without leaving the page; results from a
> previous run are cleared on re-run.

### Locked design decisions

- **Button order is firmware-canonical** (`UP, DOWN, P1, P2, P3, MEM,
  STOP, LIGHT`), filtered to the variant's active mask.
- **Default per-button timeout:** 10 s. Configurable in code; not
  exposed in UI in v1.
- **Skip records Skipped, not Pass.** Aggregate "all-active-passed"
  requires every active button to be `Pass`.
- **Unexpected events** (technician presses the wrong button) score
  Unexpected; the current prompt stays active until timeout or the
  expected event arrives. The unexpected event is logged but not
  counted.
- **Application-layer access** for button events uses
  `IProtocolService.SubscribeButtonEvents`. The tester does not parse
  application-layer bytes itself.

### Open questions for `/speckit.clarify`

1. Visual layout of the per-button result grid: 2×4 like the physical
   board, or a vertical list? The board layout is more intuitive but
   needs the layout convention from `01-context.md` §2.1 nailed.
2. Should the prompt advance automatically on Pass, or wait for the
   technician to acknowledge? (Probably auto-advance with a 0.5 s
   visual confirmation pause.)
3. What event metadata is worth surfacing on the result grid? Press
   timestamp? Hold duration? Repeated presses during the prompt window?
4. Should the firmware names be localized into Italian for the
   technician UI? (Italian dev environment, Italian customer, English-
   only policy from feat-001 — confirm scope here.)
5. Confirm the index-to-physical-position-to-firmware-name mapping is
   correct for OPTIMUS-XP's active set `{1, 3, 5, 7}` = `{DOWN, P2,
   MEM, LIGHT}`. This is the highest-risk piece of domain knowledge
   in the spec.

### Out of scope

- LED prompts during the button test (LEDs are spec-005).
- Buzzer feedback on press (buzzer is spec-005).
- Persistence of per-button results (spec-006).
- Press-and-hold tests (separate event from press).

### Hardware-risk story retired

- Application-layer dictionary variable access on real iron.
- Bidirectional CAN works (we receive frames the panel emits in
  response to physical events).
- Timing / timeout works in practice — the 10 s default is reasonable.

### Suggested Lean theorems

- `test_visits_active_only`: the test prompts exactly the active
  buttons in firmware-canonical order; inactive buttons never appear.
- `result_vector_length`: at end of test, the result vector length
  equals the variant's active-button count.

---

## spec-005 — LED and buzzer test (output side)

**One-line:** drive the panel's LEDs through a defined phase sequence
and trigger the buzzer; technician confirms by yes/no.

### Input paragraph for `/speckit.specify`

> Add an output-side test that, given a panel baptized as a BoardVariant
> with `HasLeds = true`, walks through the firmware-canonical LED phase
> sequence — Green on → Green off → Red on → Red off → Both on — by
> writing the corresponding application-layer output variables (`LEDV`
> for green, `LEDR` for red). At each phase the UI describes the
> expected visual state ("Both LED pairs should be ON now") and
> presents Yes/No buttons; the technician's answer is recorded and
> the test advances to the next phase. Variants with `HasLeds = false`
> (OPTIMUS-XP) skip the LED test entirely with a "Not applicable"
> badge. The buzzer test follows: for variants with `HasBuzzer = true`,
> the tool triggers a short buzz, the technician confirms audibly, and
> the test records the result. At end-of-sequence, the page shows a
> per-phase result list and an aggregate "all-output-passed" flag.

### Locked design decisions

- **LED phase sequence is firmware-canonical:** `GreenOn → GreenOff →
  RedOn → RedOff → BothOn`. Five phases; matches the legacy
  `LedTestPhase` enum.
- **HasLeds = false skips the entire LED section**, not individual
  phases. Aggregate "all-output-passed" treats skipped sections as
  Not-Applicable, not Pass and not Fail.
- **Buzzer waveform is firmware-defaulted.** The tool requests "buzz";
  the firmware decides frequency/duration (per UserMain.c v1.6 changelog
  it's fixed-frequency).
- **Yes/No is the only confirmation mechanism.** No automated optical
  or audio sensing.
- **Outputs are restored to OFF on test exit**, including on Cancel.
  Don't leave a panel with LEDs latched on.

### Open questions for `/speckit.clarify`

1. Is OPTIMUS-XP's `HasBuzzer` actually true? The user said the LEDs
   are not used but didn't say either way about the buzzer. **Confirm
   with the user during `/speckit.clarify` of spec-005.**
2. Should the LED test allow the technician to repeat a phase before
   answering? (Long-press / re-trigger affordance.)
3. Buzzer duration default: 0.5 s seems right; confirm against firmware.
4. Should phases time out if the technician walks away (no Yes/No
   click for N seconds)? Suggested: no timeout — the test waits
   forever until the technician answers or cancels. The whole point is
   technician-paced.
5. For panels without LEDs / without buzzer, should the test page
   still be reachable (showing only Not-Applicable badges), or hidden
   entirely? Suggested: reachable, for predictability.

### Out of scope

- Per-LED control (the firmware doesn't expose it in v5.3).
- LED-blink-pattern tests; only steady on/off.
- Buzzer melody / multi-tone.
- Combining outputs with simultaneous button events.

### Hardware-risk story retired

- Tool can drive outputs, not just observe.
- Variant gating (OPTIMUS-XP skip) is correctly enforced — no LEDs
  flashed on a panel that has none.
- Outputs reliably return to OFF on test exit.

### Suggested Lean theorems

- `led_phases_match_firmware`: the LED phase sequence emitted by the
  test, for a `HasLeds = true` variant, is exactly the five-step
  firmware sequence in the canonical order.
- `no_outputs_for_disabled_variant`: for a `HasLeds = false` variant,
  the test emits zero LED commands.

---

## spec-006 — Session orchestration, verdict, persistence, report

**One-line:** compose specs 002–005 into a single Test-this-panel
session with a final verdict, persistent per-panel result, and a
printable report.

### Input paragraph for `/speckit.specify`

> Add a top-level "Test this panel" workflow that orchestrates
> Discovery (spec-002), Baptism if needed (spec-003), Button test
> (spec-004), and LED + Buzzer test (spec-005) as one linear flow per
> panel. The workflow header shows the panel's UUID + BoardVariant +
> session start time; the body cycles through each sub-test as a step,
> with previous-step results summarized. At end-of-session the tool
> computes a final verdict as the conjunction of every in-scope
> sub-test's result (`Pass` / `Fail` / `Inconclusive` if any sub-test
> result is missing). The session record — operator name, UUID,
> BoardVariant, per-button + per-output results, verdict, start +
> end timestamps, application version — is persisted as one JSON file
> per session under `%LOCALAPPDATA%/Stem/ButtonPanelTester/sessions/`,
> filename `<UTC-ISO-timestamp>_<UUID-suffix>_<verdict>.json`. The
> tool then renders a printable session report (HTML, opened in the
> default browser or saved as PDF) that summarizes the same data in a
> form suitable for handing back to STEM's QA process. Aborted
> sessions (technician cancels mid-flow) are persisted with verdict
> `Aborted` and the partial results.

### Locked design decisions

- **Verdict algebra:** `Pass` iff every in-scope sub-test is `Pass`;
  `Fail` if any in-scope sub-test is `Fail`; `Inconclusive` if any
  in-scope sub-test is missing a result. Skipped active buttons count
  as missing.
- **Persistence path:** `%LOCALAPPDATA%/Stem/ButtonPanelTester/sessions/`.
  No DPAPI — results are not secrets.
- **One file per session.** No SQLite, no rolling logs. Each file is
  self-contained and re-importable.
- **Report format = HTML.** PDF via the OS print dialog if the
  technician wants paper. (Wrong-prior: building a PDF generator into
  the tool is bigger than it sounds — skip it.)
- **Operator name** is captured at session start, optional in v1.
  Persisted with the session.
- **The session record schema is versioned** from day one
  (`"schemaVersion": 1`) so future tools can read old files.

### Open questions for `/speckit.clarify`

1. Operator-name source: free-form text input at session start, or a
   list pulled from somewhere (STEM dictionary service)? Free-form
   seems simpler.
2. Should the report include any photos / images of the panel under
   test? Out of scope unless the user has a reason.
3. Sessions on the same panel: do later sessions supersede earlier
   ones, or accumulate? Suggested: accumulate (separate files per
   timestamp). The technician + STEM QA decide what to do with the
   history.
4. Should the technician be able to abort cleanly mid-flow (recording
   a partial session as `Aborted`), or is "close the app" the only
   abort path? Suggested: explicit Cancel button on each step.
5. Localization of the printable report — English-only per feat-001's
   established scope, confirm.

### Out of scope

- Cloud upload of results to STEM's central QA system.
- Batch-report-across-N-sessions ("today's 14 panels"); each session
  is reported on its own.
- Searching / browsing prior sessions inside the tool. The session
  files are on disk; the tool doesn't manage them.
- Auto-naming of the session by panel revision / serial number — the
  UUID is the panel identity.

### Hardware-risk story retired

- None new — this is the integration spec. The hardware risks were
  retired by 002–005.

### Suggested Lean theorems

- `verdict_pass_iff_all_pass`: the verdict is `Pass` iff every
  in-scope sub-test is `Pass`.
- `verdict_inconclusive_excludes_pass`: if any in-scope sub-test is
  missing a result, the verdict cannot be `Pass`.

---

## spec-007 — Robustness, recovery, forensic logging

**One-line:** make the tool survive the failure modes a real bench
encounters — USB unplug, bus silence, panel reset — and leave enough
trace data behind that bug reports are actionable.

### Input paragraph for `/speckit.specify`

> Add heartbeat-loss detection on the CAN link: when no frame has
> been observed for a configurable interval (default 5 s) while a test
> session is active, the tool transitions the session to `Suspended`,
> surfaces a banner ("Communication lost — checking link"), and
> attempts recovery automatically. Recovery probes the PEAK adapter
> state every second; if the adapter is healthy and frames resume,
> the session resumes from its current step. If the adapter signals
> physical-reconnect-required (USB removed), the session moves to
> `Aborted` and the technician is told to re-plug. Add per-session
> CAN frame logging: every received and transmitted CAN frame during
> a session is written to a timestamped binary log alongside the
> session JSON, suitable for offline replay against the tool's
> protocol stack. Add pre-flight checks at session start: PEAK adapter
> present, dictionary loaded (from feat-001), exactly one panel on
> the bus (already enforced by spec-003 for baptism but extended here
> for the full session). Each failed pre-flight produces an actionable
> message naming the remediation.

### Locked design decisions

> **Dual-rate constraint (2026-07-20, #293) — re-review before speccing.** A baptized panel's
> button-state refresh is dual-rate (`UserMain.c:1013–1020`): ~188 ms while its latched bitmap is
> non-zero, but **~12.5 s (`TEMPO_CAN_LENTO`) when it is zero** — a cold, never-touched panel sits
> in the slow branch. A 5 s heartbeat timeout with "any frame counts" semantics would suspend a
> healthy idle session, re-committing the exact defect spec-005 corrected (thresholds now
> 15 s/20 s — `ButtonPressTest.fs`). Either the timeout clears ~12.5 s or the heartbeat definition
> excludes cold-idle panels.

- **Heartbeat timeout:** 5 s default. Configurable in code. *(See the dual-rate constraint above —
  5 s does not clear a cold panel's ~12.5 s refresh.)*
- **Recovery is automatic, with cap.** Three failed recovery probes
  in a row → session moves to `Aborted` with reason `RecoveryFailed`,
  not stuck in `Suspended` forever.
- **Frame log format:** raw CAN frames (ID, DLC, data, direction,
  monotonic timestamp). Binary preferred for size; a separate offline
  tool decodes (out of scope for v1).
- **Pre-flight is fast-fail.** Don't start a session if anything is
  wrong; explain what.
- **Session state machine extension:** `Active → Suspended → Active`
  and `Active → Aborted` are the new transitions.

### Open questions for `/speckit.clarify`

1. Frame log rotation / cap: do we cap log size per session, and if
   so at what (10 MB? 100 MB? unlimited)? On a multi-hour debug
   session this could grow.
2. Should the tool also log non-session-bound traffic (the passive
   Panels-on-bus stream from spec-002)? Probably no by default;
   could be a toggle.
3. Heartbeat semantics: do we treat any CAN frame as a heartbeat, or
   only specific WHO_I_AM / known telemetry frames? Suggested: any
   frame counts (panels emit telemetry on a timer once active).
   *(#293 caveat: "on a timer" is dual-rate — a cold-idle panel's
   timer is ~12.5 s, not sub-second; see the locked-decisions note.)*
4. After a session aborts via `RecoveryFailed`, should the tool
   automatically save what it has, or ask the technician?
5. Pre-flight grace period: should we wait N seconds after dictionary
   load before pre-flight runs (to give the PCAN driver time to
   enumerate, the bus to settle, etc.)?

### Out of scope

- Crash reporting to a central upstream system.
- Automatic firmware update of the panel under test.
- Statistical bus-health metrics (frame-rate graphs, error counts).
- Replay-from-log facility — the log is written but the tool doesn't
  read it.

### Hardware-risk story retired

- USB unplug + re-plug cycle is survived without losing in-progress
  data.
- Long-session stability (multi-hour bench session doesn't degrade).
- Bug reports from the field become actionable because there is
  always a frame log.

### Suggested Lean theorems

- `heartbeat_loss_suspends`: any contiguous absence of frames longer
  than the heartbeat timeout, during an `Active` session, transitions
  the session to `Suspended` (never silently to `Aborted` or stays
  `Active`).
- `recovery_terminates`: the recovery probe loop has a bounded number
  of iterations before either resuming the session or aborting it
  with `RecoveryFailed`.

---

## After 007 — what the tool looks like

A bench-deployable Avalonia application that, on a Windows machine
with a PEAK PCAN-USB adapter plugged in:

- Boots, fetches its dictionary (feat-001), shows two status rows
  (dictionary + CAN).
- Lets the technician name an operator, pick a panel from the bus,
  baptize it to a machine variant if it is virgin, and run a full
  test session covering inputs and outputs.
- Produces a per-panel JSON session record + a printable HTML report.
- Recovers from USB unplugs and bus silences without dropping
  in-progress data.
- Leaves a CAN frame trace per session for offline forensics.

Specs beyond 007 (bulk-mode, cloud upload, firmware update,
multi-panel, batch reports) are not in this roadmap and should be
decided after a few weeks of real bench use inform what the next
priority actually is.
