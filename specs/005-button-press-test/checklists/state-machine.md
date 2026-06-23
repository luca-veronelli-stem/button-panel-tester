# State-Machine Requirements Checklist: Button-Press Test (Input Side)

**Purpose**: Validate that the requirements governing the **button-press-test FSM** — states, events,
per-button outcomes, transitions, scoring rules, interruption, re-run, enablement, and totality — are
complete, clear, consistent, and measurable before implementation (constitution-recommended for any
feature touching the test-session state machine).
**Created**: 2026-06-23
**Feature**: [spec.md](../spec.md) · [data-model.md](../data-model.md) §4/§6 · [research.md](../research.md) §R7

> "Unit tests for English" — each item tests whether the **requirement is written correctly**, not
> whether code works. `[x]` = the artifact set already satisfies it; `[ ]` = a residual gap noted inline.

## Requirement Completeness

- [x] CHK001 Are all FSM states enumerated (`Idle`, `Prompting`, `Completed`, `Interrupted`)? [Completeness, data-model §4, research §R7]
- [x] CHK002 Are all input events enumerated (`PressEdge`, `Tick`, `Retry`, `Skip`, `LinkChanged`, `PanelPresence`)? [Completeness, data-model §4]
- [x] CHK003 Are all per-button outcomes enumerated (`Pending`, `Pass`, `Missed`, `Skipped`)? [Completeness, data-model §4, FR-006/007/009]
- [x] CHK004 Are the two interruption reasons specified (`LinkLost`, `PanelLost`)? [Completeness, data-model §4, FR-013]
- [x] CHK005 Is the aggregate "all active passed" predicate defined (true iff every active button is `Pass`)? [Completeness, FR-011, data-model §4]
- [x] CHK006 Is the enablement predicate fully specified (link `Connected` ∧ selected-baptized ∧ observable) with an explanation per unmet condition? [Completeness, FR-001, data-model §6, SC-008]

## Requirement Clarity / Ambiguity

- [x] CHK007 Is the `Pass` condition unambiguous (first matching press-edge within the window)? [Clarity, FR-006, SC-002]
- [x] CHK008 Is the `Missed` condition unambiguous (no matching press-edge within the window)? [Clarity, FR-007, SC-003]
- [x] CHK009 Is `Unexpected` defined precisely (a non-prompted active press — logged, not counted, no advance)? [Clarity, FR-008, SC-004]
- [x] CHK010 Is `Skip` semantics unambiguous (records `Skipped` ≠ `Pass`, advances)? [Clarity, FR-009]
- [x] CHK011 Is `Retry` semantics specified (re-arm the **same** button with a fresh countdown)? [Clarity, FR-009]
- [x] CHK012 Is the per-button window duration and its (non-UI) configurability specified (10 s, code-config)? [Clarity, FR-005, spec §Assumptions]
- [ ] CHK013 Is the deadline **anchor instant** (when the countdown starts) pinned explicitly in a requirement? [Ambiguity, research §R7, data-model §4] — **residual:** stated as a per-prompt deadline but not pinned to a named instant the way spec-004's CHK010 pinned claim-write completion. Low risk (a prompt has one obvious entry instant); the service (T023) + timeout test (T025) fix it operationally. Consider a one-line data-model note.

## Requirement Consistency

- [x] CHK014 Is the canonical prompt order consistent across FR-002, US1 AC-3, and the research bit-order? [Consistency, FR-002, US1 AC-3, research §R3]
- [x] CHK015 Do the re-run requirements (clear all prior results) align between FR-003, US3 AC-1, and SC-007? [Consistency, FR-003, SC-007]
- [x] CHK016 Is the never-flip rule (a late press after `Missed`/terminal does not change a recorded outcome) consistent with the edge-case list? [Consistency, spec §Edge Cases, data-model `terminal_absorbs`]
- [x] CHK017 Is "advance only on `Pass` (or `Skip`)" consistent — an `Unexpected` never advances? [Consistency, FR-008, FR-010]

## Scenario & Edge-Case Coverage

- [x] CHK018 Are link-loss-mid-prompt requirements defined (distinct interruption, never "all passed")? [Coverage, FR-013, SC-005]
- [x] CHK019 Are panel-disappears-mid-prompt requirements defined (distinct `PanelLost`)? [Coverage, spec §Edge Cases, FR-013]
- [x] CHK020 Is the inactive-bit press explicitly excluded from scoring? [Edge Case, FR-014]
- [x] CHK021 Is the two-buttons-near-simultaneous case addressed (only prompted scores; other = `Unexpected`)? [Edge Case, spec §Edge Cases]
- [x] CHK022 Is the dead-button case addressed (`Missed` → Retry/Skip; aggregate not "all passed")? [Edge Case, spec §Edge Cases]
- [x] CHK023 Is the held-button / bouncing case addressed at the FSM level (one `Pass`, further transitions ignored that window)? [Edge Case, spec §Edge Cases]
- [ ] CHK024 Is the behaviour for a schema with **zero** active buttons specified or explicitly excluded? [Gap] — **residual:** no shipped variant has zero active buttons (all ≥ 4), so it is unspecified. Low risk; `result_vector_length` (length = active count) degenerates correctly. Note as intentionally out of scope if confirmed.

## Measurability / Totality

- [x] CHK025 Is FSM totality captured as a verifiable requirement (every run ends in exactly one terminal)? [Measurability, data-model `test_outcome_total`, tasks T018/T020]
- [x] CHK026 Is the result-vector-length requirement verifiable (final length = active-button count)? [Measurability, FR-011, data-model `result_vector_length`]
- [x] CHK027 Is "no `Pass` without an in-window matching press-edge" stated as a checkable invariant? [Measurability, FR-006, data-model `pass_requires_press_edge`]
- [x] CHK028 Is "an interrupted run never reports all-passed" stated as a checkable invariant? [Measurability, FR-013, data-model `interrupt_excludes_all_passed`]

## Notes

- The FSM requirements are unusually well-pinned because data-model §4 carries the seven preservation
  theorems (`test_visits_active_only`, `result_vector_length`, `test_outcome_total`,
  `pass_requires_press_edge`, `skip_never_pass`, `interrupt_excludes_all_passed`, `terminal_absorbs`)
  + `test_enabled_iff` — each is a measurable requirement, mirrored by an FsCheck property (tasks
  T018/T020/T021/T022). Most clarity/measurability items trace straight to a theorem name.
- **Two open items are low-risk residuals, not blockers:** CHK013 (the deadline anchor instant is not
  pinned to a named requirement the way spec-004's CHK010 was) and CHK024 (zero-active-buttons schema
  unspecified — no shipped variant hits it). Both are operationally covered by the service + tests;
  optionally add a one-line data-model note for each. Neither warrants reopening the frozen spec/plan.
- Everything else passes: states, events, outcomes, scoring rules, interruption, re-run, enablement,
  and totality are all completely and measurably specified.
