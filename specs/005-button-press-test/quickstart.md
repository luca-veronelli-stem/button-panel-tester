# Quickstart: Button-Press Test (Input Side)

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Status**: living

## Developer walkthrough (CI-green, no hardware)

```powershell
# from the worktree root
dotnet build  -c Release Stem.ButtonPanelTester.slnx
dotnet test   -c Release --filter "Category!=Hardware"
dotnet format whitespace --verify-no-changes
# Lean Phase 4 must build with no sorry / only standard axioms
lake -C lean build
```

What the non-hardware suite proves:

- **Codec** — `ButtonStateFrame` round-trips against `Phase4/ButtonStateFrame.lean` and the wire
  fixture (`Fixtures/Can/buttonStateFixtures.json`).
- **Detector** — `pressEdges` reports a button iff its active bit went `1 → 0`
  (`press_edge_iff_high_to_low`); inactive bits never appear. The §6b arming layer (#293):
  `scoredPositions` scores an armed position exactly as `pressEdges` and an unarmed one on its
  `0 → 1` release transition (`armed_scores_on_press_edge`, `unarmed_scores_on_first_release`,
  `arming_monotonic`, `no_double_score_after_arming` — FsCheck mirrors + a cold-boot example).
- **FSM** — totality, `pass_requires_press_edge`, `skip_never_pass`,
  `interrupt_excludes_all_passed`, `test_visits_active_only`, `result_vector_length`
  (FsCheck mirrors of Phase 4 theorems).
- **Service** — timing (Missed at the 10 s deadline via `FrozenClock`), Unexpected-not-counted,
  Retry re-arm, Skip≠Pass, link-loss / panel-loss interruption, forensic-log emission — all on the
  real service graph with manual fakes (`InMemoryButtonStateObserver`, `InMemoryCanLink`).
- **GUI** — `ButtonPressTestView` enable-matrix + result-grid render under Avalonia.Headless.

## Bench walkthrough (the done line — Hardware E2E)

Prerequisite: one PEAK PCAN-USB adapter and one **OPTIMUS-XP** panel, already baptized (spec-004)
and observable on the bus.

1. Open the button-press test — the tool **auto-targets the single heartbeating panel** (no
   selection step since #270); it becomes available once a button-state heartbeat arrives. On a
   **cold, never-touched panel the first heartbeat can take up to ~12.5 s** (the firmware's slow
   branch, #293) — wait for it rather than suspecting the rig. The test stays unavailable, with an
   explanation, if the link is not Connected or no baptized panel heartbeats (FR-001).
2. Run the sequence. The tool prompts by decal, in order: **Light → Suspension → Up → Down**.
3. Press each prompted button; confirm each scores **Pass** within ~1 s (SC-002) and the prompt
   advances; at the end the grid shows four Pass and a positive "all active passed" (SC-001). On a
   cold panel a button's **first-ever press scores at its release** (the firmware never transmits
   the press itself — unarmed rule, #293/§6b), so press-and-release naturally; from the second
   press on, scoring fires on the press.
4. Let one button time out → **Missed** at ~10 s (SC-003); Retry re-arms it; Skip records **Skipped**.
5. Press a wrong button while another is prompted → **Unexpected** in the log, prompt unchanged
   (SC-004).
6. Unplug the adapter mid-run → distinct **link-lost** interruption, never "all active passed"
   (SC-005).

**Polarity confirmation (R2):** verify that scoring fires on the **press** (bit `1 → 0`), not on
release. **Warm the button first** — give it one press+release cycle before the check: on a cold
panel an unarmed first press scores at its release **by design** (#293/§6b), which would misread as
inverted polarity. Only if an *armed* button still scores on release, flip `PressedBit` and re-run —
do not redesign.
Only after this passes is OPTIMUS-XP declared bench-validated; the other three variants stay
provisional until their hardware reaches the bench.

Tracked under the living bench-hardware tracker (Constitution IV); no untagged skips.
