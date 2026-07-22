# Feature Specification: Button-Press Test (Input Side)

**Feature Branch**: `005-button-press-test`

**Created**: 2026-06-22

**Status**: Draft

**Input**: User description: "Button-press test input side: for a baptized panel, prompt the technician to press each active button in canonical order and observe the CAN button-state frame within a timeout, scoring each Pass, Missed, Unexpected, or Skipped with a per-button result grid."

The tool's first **input-side** test. Earlier features brought the panel to life — link up (spec-002), discover it on the bus (spec-003), claim it as a known variant (spec-004, "baptism"). This feature is the first to verify that the panel's **physical controls actually work**: it walks a supplier technician through pressing each active button on a baptized panel one at a time, watches the panel report the press over CAN, and scores each button so the technician gets a clear per-button go/no-go before the panel ships.

## Clarifications

### Session 2026-06-22

- **Prompt labels are decal names, not firmware names.** The technician matches the prompt to what is printed on the physical panel. The firmware/canonical name (DOWN, P1, …) may appear as a secondary diagnostic detail only. OPTIMUS-XP decals confirmed **authoritative** by the product owner: firmware `DOWN` = **Light**, `P1` = **Suspension**, `P3` = **Up**, `MEM` = **Down**. (This corrects the `docs/Context/bpt-rollout/CORRECTIONS.md` §C3 audit, which was wrong on P3 ("All-Up") and MEM ("Stop"); the legacy app's labels were correct on those two.)
- **Variant coverage = all four** (Eden-XP, Optimus-XP, R-3L XP, Eden-BS8), data-driven by a per-variant button schema. OPTIMUS-XP is bench-validated and authoritative; the other three are carried from firmware/legacy data and treated as **provisional / unverified** until a panel of that variant is tested on a bench (same honesty posture as spec-004's firmware-limited SC-004).
- **Detection model = edge on a key-state bitmap, not discrete events.** The panel reports its current button state as a bitmap (one bit per physical button) on its application channel; a "press" is the transition of a button's bit into the pressed state. The first such transition for the prompted button within the timeout scores Pass.
- **Protocol metadata stays the hardcoded tester-side stopgap** for this slice (extended with the button-state variable identifiers the decoder needs). The CORRECTIONS.md §C5 fetch migration (fetch command/variable metadata from the server) is deferred to its own standalone ticket.
- **Technician UI strings are English** (per the project's English-by-default policy), despite the legacy app having prompted in Italian.

### Session 2026-06-24

- **Observability = the button-state heartbeat, not WHO_I_AM discovery.** A baptized panel is **silent on the WHO_I_AM auto-address broadcast** — the firmware enters `AAS_STAND_BY` after `SET_ADDRESS` and never re-broadcasts WHO_I_AM (`CORRECTIONS.md` §C1) — so it never appears in the spec-003 Panels-on-bus discovery list. The tool instead recognises a baptized panel by its **button-state heartbeat**: the SP_APP `VAR_WRITE` (cmd `0x0002`, addr `0x80NN`, bitmap `TxTasti`) it transmits on change plus a periodic refresh (`research.md` R1). A button-state frame arriving therefore **is** the evidence that a baptized panel of that variant is present and observable. (Bench-confirmed 2026-06-24; corrects the original premise that the panel is selected from the discovery list — that premise does not hold for baptized panels.)
- **Variant comes from the directed CAN ID, not discovery.** The heartbeat arrives on a **directed CAN ID** whose machineType byte (bits 23–16) identifies the variant — OPTIMUS `0x000A0441`, Eden-XP `0x00030141`, R-3L `0x000B0481`. `(CanId >>> 16) &&& 0xFF` decoded by the variant decoder yields the marketing variant; the broadcast id (`0x1FFFFFFF` → `0xFF`/virgin) and the tool's own SRID (`0x00000008` → `0x00`) decode to non-marketing values and are rejected. One panel under test at a time (spec-003 bench convention), so the test **auto-targets the single baptized panel currently heartbeating** — there is no panel-selection step and no UUID/address disambiguation.
- **"Panel lost" = button-state silence past a configurable threshold.** Observability and panel-loss key off frame recency: a button-state frame within the *observable window* ⇒ observable; no button-state frame for longer than the *panel-lost threshold* during a run ⇒ panel-lost. Both are code-configurable constants (provisional defaults: observable window 2 s, panel-lost 3 s, from the bench-measured ~182 ms idle refresh), **confirmed on the rig** alongside the press-edge polarity. (The earlier ~12 s figure was a *different* periodic message — CAN id `0x00000008` — not the button-state heartbeat.) — **superseded by Session 2026-07-20 below: the 2 s / 3 s defaults were calibrated against a misread cadence.**

### Session 2026-07-20

Firmware + trace re-read (no bench run). Corrects the cadence premise the Session 2026-06-24 thresholds rested on; see issue #293.

- **The heartbeat is dual-rate, not a single ~182 ms refresh.** `UserMain.c:1013–1020` selects the period from the *latched* bitmap: `TxTastiOld ≠ 0` ⇒ `TEMPO_CAN_VELOCE` (150) ≈ **188 ms**; `TxTastiOld = 0` ⇒ after an 11-frame fast ramp, `TEMPO_CAN_LENTO` (10000) ≈ **12.5 s**. One tick = 1.25 ms (4 kHz ISR `UserMain.h:127–129` ÷ prescaler 5 `UserMain.c:950–957`); 151 × 1.25 ms = 188.75 ms. `TxTasti` is zero-init (`:200`) and bits **latch** — press clears (`&= ~keysMask`, `:1369`), release sets (`|= keysMask`, `:1375`).
- **Therefore a cold, never-touched baptized panel heartbeats at ~12.5 s**, and only switches to ~188 ms once *any* button has been pressed **and released** once. The `first-gather/*_baptized.trc` captures that produced the "~182 ms idle" figure are exactly the 12-frame post-boot ramp (12 identical-payload repetitions at 186.7 ms across three different panels, then the capture ends) — real, but not the idle steady state. The earlier "~12 s" figure was also real: it is `TEMPO_CAN_LENTO`, merely mis-attributed to the tool's own SRID.
- **Recency thresholds must exceed `TEMPO_CAN_LENTO`.** The 2 s / 3 s defaults leave the GUI enabled ~16 % of the time and halt any run in `Interrupted PanelLost` within 3 s once the panel settles into the slow branch. FR-013 and SC-005 are unchanged in *meaning*; only the constants are recalibrated. SC-005's adapter-unplug case is unaffected — that path is `LinkLost`, which stays fast — so only panel-power-loss detection slows.
- **On a never-touched panel the first press of a button is not transmitted at all.** With `PressedBit = 0uy` (firmware-correct — see R2) a cold panel's `0x00` baseline reads every bit as pressed; the first press clears an already-clear bit (`UserMain.c:1369`), so `TxTasti` does not change and the transmit gate (`:973`) never fires. The release *does* transmit (`:1375`). No tool-side edge rule can recover an event that never reached the wire, so scoring keys off what is observable: for an **unarmed** position (never yet seen released) a `0 → 1` release transition is unambiguous proof of a completed press and scores it, arming the position; an **armed** position scores on the normal `1 → 0` press edge as before. Steady-state behaviour is unchanged. Without this, a freshly powered panel scores `Missed` on the first press of every button (SC-001/SC-002).
- **Polarity is NOT changed.** `PressedBit = 0uy` stays. The `0x00` idle bitmap is the boot latch state, not evidence of inversion.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Verify every active button on a baptized panel (Priority: P1)

A supplier technician has a panel on the bench that the tool has already baptized as a known variant. They start the button-press test; the tool prompts them — by the decal name printed on the panel — to press each active button in turn, with a visible countdown. The technician presses each button, sees it confirmed, and at the end gets a per-button result grid plus a single "all active buttons passed" verdict.

**Why this priority**: This *is* the feature — the input-side pass/fail on a panel's physical buttons. Without it there is nothing to test. It is the minimum viable slice: a technician can run a complete button check on the one bench-validated variant and trust the verdict.

**Independent Test**: With a panel baptized as OPTIMUS-XP, run the test and press the four active buttons when prompted; confirm each is scored Pass and the aggregate shows "all active passed".

**Acceptance Scenarios**:

1. **Given** a panel baptized as OPTIMUS-XP is observable on the bus (its button-state heartbeat is arriving) and the CAN link is Connected, **When** the technician starts the test, **Then** the tool prompts for the first active button by its decal name ("Light") with a visible per-button countdown.
2. **Given** the tool is prompting for "Light", **When** the technician presses the Light button on the panel, **Then** within the timeout the button is scored Pass and the prompt advances to the next active button.
3. **Given** the OPTIMUS-XP panel is under test, **When** the test runs, **Then** the active buttons are prompted in canonical firmware order filtered to the variant's active mask: Light (DOWN) → Suspension (P1) → Up (P3) → Down (MEM); inactive positions (UP, P2, STOP, LIGHT) are never prompted.
4. **Given** all four active OPTIMUS-XP buttons have been pressed and scored Pass, **When** the sequence completes, **Then** the tool shows a per-button result grid (decal label + Pass) and a positive "all active passed" indicator.

---

### User Story 2 — Recover from a missed or wrong press without restarting (Priority: P2)

During a run the technician hesitates and a button times out, or they press the wrong button. Rather than scrapping the whole test, they can retry the current button or skip it and keep going, and a wrong press is recorded without derailing the prompt.

**Why this priority**: Real bench conditions — hesitation, a wrong button, a genuinely dead key — must not force a full restart. The predecessor tool's all-or-nothing behaviour with no recovery was a known pain point; this story fixes it.

**Independent Test**: During a run, let one button time out and confirm it scores Missed with Retry/Skip offered; confirm Retry re-arms the same button, Skip records Skipped and advances, and a wrong press scores Unexpected without advancing.

**Acceptance Scenarios**:

1. **Given** the tool is prompting for a button, **When** the timeout elapses with no matching press, **Then** the button is scored Missed and Retry and Skip controls are offered.
2. **Given** a button scored Missed, **When** the technician chooses Retry, **Then** the same button is prompted again with a fresh countdown.
3. **Given** a button scored Missed, **When** the technician chooses Skip, **Then** the button is recorded Skipped (not Pass) and the prompt advances to the next active button.
4. **Given** the tool is prompting for button X, **When** the technician presses a different active button Y, **Then** the press is recorded as Unexpected (visible in the log, not counted as X's result) and the prompt for X stays active until X is pressed or the timeout elapses.

---

### User Story 3 — Re-run the test and cover other variants (Priority: P3)

After a run, the technician re-runs the test in place (e.g., after re-seating a button), and the test adapts its active-button set and labels to whichever variant the selected panel was baptized as.

**Why this priority**: Bench throughput (re-test after a fix) and breadth across the four variants. Variant-awareness is needed, but only OPTIMUS-XP is bench-validated, so the other variants ride as provisional.

**Independent Test**: After a completed run, re-run and confirm prior results clear; select a panel of a different variant and confirm the prompted button set and labels change to that variant's schema.

**Acceptance Scenarios**:

1. **Given** a completed test with a result grid shown, **When** the technician re-runs the test, **Then** all prior per-button results are cleared and the sequence starts fresh.
2. **Given** a panel baptized as a full-set variant (e.g., Eden-XP), **When** the test runs, **Then** the prompted buttons and labels match that variant's schema (all eight buttons), with the labels surfaced as provisional/unverified.
3. **Given** no baptized panel is heartbeating on the bus, **When** the technician opens the test, **Then** the test is unavailable with an explanation that a baptized, observable panel is required.

---

### Edge Cases

- **Link drops mid-test** (USB unplug or bus silence): the test halts with a distinct "link lost" outcome; no false Pass or Missed is recorded for the in-flight button, and "all active passed" is never reported.
- **Panel stops heartbeating mid-test** (button-state silence past the panel-lost threshold): the test halts with a distinct "panel lost" outcome.
- **Button held down** (long press): the press registers once on its scoring transition (FR-006); holding does not produce repeated Pass results. For an unarmed position the scoring transition is the release, so a held first press on a cold panel scores when the operator lets go — not while holding.
- **Bouncing / repeated presses within one prompt window**: the first matching transition scores Pass; further transitions for that button in the same window are ignored for scoring.
- **A bit reported for an inactive position** (outside the variant's active mask): never treated as a prompted-button result; ignored or surfaced as diagnostic only.
- **Two buttons pressed near-simultaneously**: only the prompted button scores; the other counts as Unexpected.
- **A genuinely dead button**: scores Missed after the timeout; the technician can Retry (confirming the fault) or Skip; the aggregate verdict is not "all active passed".

## Requirements *(mandatory)*

### Presentation surfaces

The test communicates through three complementary surfaces:

| Surface | Role | Content |
|---|---|---|
| **Test view** (continuous) | Drive the technician through the sequence and show outcomes | Current prompt (decal label) + countdown; per-button result grid; aggregate "all active passed" verdict; Retry/Skip controls |
| **Operator status** (transient) | Announce test-level state changes | Test started, test completed (passed / has failures), test interrupted (link/panel lost), test unavailable (with reason) |
| **Forensic log** (append-only) | Leave an offline diagnosis trail | Each prompt, each observed press (expected and unexpected), each score, timeout, retry, and skip — with timestamps and observed button metadata |

### Functional Requirements

**Test lifecycle & enablement**

- **FR-001**: The system MUST offer the button-press test only while the CAN link is Connected and a baptized panel of a known variant is **observable on the bus** — defined as its button-state heartbeat (SP_APP `VAR_WRITE` button-state frames) arriving within the observable window; the panel's variant is taken from the directed CAN ID's machineType byte. A baptized panel does **not** appear in the WHO_I_AM passive-discovery list (it is silent on WHO_I_AM after baptism — `CORRECTIONS.md` §C1), so observability and variant key off the button-state heartbeat, not the Panels-on-bus list. With one panel under test at a time, the test auto-targets the single heartbeating baptized panel. Otherwise the test MUST be unavailable with an explanation of what is missing (link not Connected / no baptized panel heartbeating).
- **FR-002**: The system MUST let the technician start the test for the selected baptized panel and MUST present the panel's active buttons one at a time, in canonical firmware order filtered to that variant's active-button mask.
- **FR-003**: The system MUST allow the test to be re-run end-to-end without leaving the test view, clearing all prior per-button results on re-run.

**Prompting & labeling**

- **FR-004**: For each prompted button the system MUST display the button's **decal label** (the name printed on that variant's physical panel) as the primary prompt; the firmware/canonical name MAY be shown as secondary diagnostic detail.
- **FR-005**: The system MUST show a per-button countdown indicating the time remaining to press, with a default window of 10 seconds.

**Scoring**

- **FR-006**: While a button is prompted, the system MUST observe the panel's button-state reports and score that button **Pass** on the first report carrying its scoring transition within the timeout window. For an **armed** position (previously observed released) the scoring transition is the press edge (`released → pressed`); for an **unarmed** position (never yet observed released — the cold-panel case, where the firmware never transmits the first press) it is the first release transition (`pressed → released`), which is unambiguous proof of a completed press. *(Amended 2026-07-20, #293 — see §Clarifications Session 2026-07-20 and `data-model.md` §6b; the original text scored only the press edge.)*
- **FR-007**: The system MUST score a prompted button **Missed** when no matching pressed-transition is observed within the timeout window.
- **FR-008**: A press of any button other than the currently-prompted one MUST be recorded as **Unexpected** — logged but not counted as the prompted button's result — and MUST NOT advance the sequence.
- **FR-009**: The system MUST offer per-button **Retry** (re-arm the same button with a fresh countdown) and **Skip** (record **Skipped** and advance) controls; a Skipped button MUST NOT count as Pass.
- **FR-010**: On a Pass, the system MUST advance to the next active button (a brief visual confirmation is acceptable).

**Results**

- **FR-011**: At end-of-sequence the system MUST present a per-button result grid showing each active button's decal label and its outcome (Pass / Missed / Skipped), plus an aggregate **"all active passed"** indicator that is positive only when every active button scored Pass.
- **FR-012**: The system MUST produce a forensic record of the test — each prompt, observed press (expected and unexpected), score, timeout, retry, and skip — with timestamps, sufficient to diagnose a failed run offline.

**Robustness**

- **FR-013**: If the CAN link leaves Connected, or the panel under test stops emitting its button-state heartbeat for longer than the panel-lost threshold during a run, the system MUST halt the test with a distinct interruption outcome (link-lost / panel-lost) rather than silently recording Missed, and MUST NOT report "all active passed".
- **FR-014**: The system MUST treat button reports for positions outside the selected variant's active mask as not part of the test (ignored or surfaced as diagnostic), never as a prompted-button result.
- **FR-015**: The system MUST retain nothing about the panel after the test beyond the in-session result view (no persistence in this slice).

**Variant schema**

- **FR-016**: The system MUST carry, per variant, the active-button mask and the per-button decal labels. OPTIMUS-XP's active set {DOWN, P1, P3, MEM} with decals {Light, Suspension, Up, Down} is authoritative. The other variants' masks and labels are provisional/unverified until bench-confirmed and MUST be surfaced as provisional wherever the technician sees them.

### Key Entities

- **Button-press test session**: the in-progress test for one selected panel — the panel identity, its variant, the ordered list of active buttons, the current prompt, the per-button results, and the aggregate verdict.
- **Active-button schema (per variant)**: the variant's active-button mask, the ordered list of active buttons, and each active button's decal label (with the firmware/canonical name available as diagnostic detail).
- **Button result**: one active button's outcome — Pending / Pass / Missed / Skipped — together with observed metadata (press timestamp; any Unexpected presses seen during its window).
- **Observed button report**: a decoded key-state observation from the panel under test — which button(s) are pressed/released and when.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a baptized OPTIMUS-XP panel, a technician can complete a full four-button test (pressing each active button when prompted) and see all four scored Pass with a positive "all active passed" indicator, in a single uninterrupted run. (US1)
- **SC-002**: When a prompted **armed** button is pressed, it is scored Pass within about one second of the press — perceived as immediate — verifiable by comparing a bus capture of the press against the on-screen result. For an **unarmed** button (first cycle on a cold panel) the press itself never reaches the wire, so the Pass correlates with the release frame instead: scored within about one second of the *release*, verifiable against the release frame in the capture. (US1 / FR-006; amended 2026-07-20, #293)
- **SC-003**: A prompted button that is not pressed is scored Missed within the configured window (default 10 seconds) of the prompt starting, within about one second of the window elapsing. (US2 / FR-007)
- **SC-004**: Pressing a button other than the prompted one never scores the prompted button and never advances the sequence, and the wrong press is visible in the forensic log. (US2 / FR-008)
- **SC-005**: A test interrupted by link loss surfaces a distinct link-lost outcome within a small handful of seconds; a test interrupted by the panel ceasing to heartbeat surfaces a distinct panel-lost outcome within the panel-lost window (the configurable threshold, default ~20 s — firmware-derived, above the ~12.5 s `TEMPO_CAN_LENTO` slow branch; recalibrated 2026-07-20, #293). Neither ever reports "all active passed". (Edge cases / FR-013)
- **SC-006**: The prompted label for every OPTIMUS-XP active button matches the physical panel decal (Light, Suspension, Up, Down), verifiable by a technician reading the panel. (FR-004; the §C3 correction)
- **SC-007**: Re-running the test clears all prior results and starts a fresh sequence, with no residual Pass/Missed from the previous run. (US3 / FR-003)
- **SC-008**: The test is unavailable, with an explanation, whenever no baptized panel is heartbeating on the bus or the link is not Connected — it never prompts for buttons absent an observable baptized panel. (FR-001)

## Assumptions

- The panel under test has already been **baptized** (claimed) as a known variant. A panel emits its button-state reports only after it has been addressed (panel firmware behaviour, audited in the firmware sources and recorded in `docs/Context/bpt-rollout/CORRECTIONS.md` §C3).
- The technician presses the buttons physically; the test is **pure observation** of what the panel reports and transmits nothing during the test (the transmit path used for baptism is not exercised here).
- The panel reports button state as a **key-state bitmap** (one bit per physical button) on its application channel, where a "press" is the transition of a button's bit into the pressed state. The exact variable identifier and bit polarity used by the live panel are pinned during planning against the firmware and the bench; `CORRECTIONS.md` §C3 records the bit assignment (UP = bit 0 … LIGHT = bit 7).
- The default per-button timeout is **10 seconds**, configurable in code and not exposed in the UI in this slice.
- The supplier bench has exactly one PEAK PCAN-USB adapter and one panel under test at a time.
- The protocol metadata needed to recognise the button-state reports is provided **tester-side** (the existing hardcoded stopgap, extended with the button-state variable identifiers). Fetching this metadata from the server is out of scope (see `CORRECTIONS.md` §C5).
- **OPTIMUS-XP is the only variant with bench-validated hardware** at authoring time; the other variants' active-button masks and decal labels are carried from firmware/legacy data and treated as provisional until a panel of that variant is tested on a bench.

## Dependencies

- **Baptism** (spec-004, shipped v0.4.0): a panel must be claimed before it emits button-state reports. Expressed as the behavioural capability "the selected panel is baptized and observable," not a specific upstream requirement number.
- **Passive panel discovery** (spec-003, shipped v0.3.0): used to find and baptize **virgin** panels; a **baptized** panel is not in the WHO_I_AM Panels-on-bus list, so the button-press test identifies its panel by the button-state heartbeat (directed CAN ID), not the discovery list. The button-press path does not depend on the discovery service.
- **CAN link lifecycle** (spec-002, shipped v0.2.0): the test runs only while the link is Connected.
- A **PEAK PCAN-USB driver** installed on the test workstation.
- **Panel firmware button-state behaviour**, audited and recorded in `docs/Context/bpt-rollout/CORRECTIONS.md` §C3 (bit assignment) and the panel firmware repository.

## Out of Scope

- **LED and buzzer output testing** (the output side) — spec-006.
- **Session orchestration, verdict persistence, and report generation** — spec-007.
- **Press-and-hold / long-press / short-vs-long classification** — only a single press registration is scored.
- **The §C5 protocol-metadata fetch migration** (fetch command/variable metadata from the server instead of the hardcoded tester-side stopgap) — deferred to its own standalone ticket.
- **The polished visual-hierarchy / layout design** of the test view and result grid — the layout language is a deliberate late-train design spec; this slice uses a functional layout only.
- **Localized (Italian) technician strings** — English UI per policy.
- **Bench-validation of the non-OPTIMUS-XP variants'** masks and labels — carried as provisional.
- **Any persistence of results** beyond the in-session view.
