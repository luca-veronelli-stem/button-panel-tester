/-
T032 — Lean Phase-2 module for the passive-observer invariant.

Mechanises SC-007 + FR-014 of `specs/002-can-link-and-panel-discovery/spec.md`:
the tool listens to the CAN bus but never transmits a frame. The
observation pipeline that lives in `CanLinkService.observe` (T045 / PR-D)
is purely receive-side; the Lean theorem here models that pipeline with
an explicit transmit-trace channel and proves the channel stays empty
across the entire observation lifetime.

Constitution Principle I (no `sorry`, no custom axioms; `plan.md`
Constitution Check §I): the proof is by induction over the list of
observed frames. The `observe` step is defined to leave the transmit
trace untouched, so the trace stays `[]` after any number of steps.

The F# surface (CanLinkService.observe pipeline) lands in T045 of PR-D;
the property-test counterpart (`tests/.../Integration/Can/
DiscoveryE2ETests.fs` case (e) — frame with `CanId ≠ 0x1FFFFFFF` →
no row emitted) lands in T051 of PR-D. This Lean theorem is the
algebraic side of the same passive-observation contract that those
tests will exercise at the value level.

Builds on `Phase2/PanelsOnBus.lean` (T030) for the function-shaped map
plus the `PanelObservationModel` record. The transmit trace itself is
a `List Unit` — only its emptiness matters, not the per-event payload.
-/

import Stem.ButtonPanelTester.Phase2.PanelsOnBus

namespace Stem.ButtonPanelTester.Phase2

/-! ## Observation step

Single receive-side step. Carries:
  * `uuid`     — the UUID extracted from the WHO_I_AM payload.
  * `now`      — the receive timestamp (`Nat`, abstract time unit).

Maps directly to the `(WhoIAmFrame, DateTimeOffset)` pair the F#
`CanLinkService.observe` pipeline consumes in T045.
-/

structure ObservationStep where
  uuid : PanelUuidKey
  now : Nat
  deriving Repr

/-! ## ObserverState

State carried across the observation pipeline:
  * `panels`         — the live `PanelsOnBus` map.
  * `transmitTrace`  — the list of frames the tool has emitted onto
                       the bus. SC-007 / FR-014 require this to stay
                       empty for every reachable state.

Mechanises the passive-observation contract: the pipeline mutates
`panels` (via `observe`) on every step and leaves `transmitTrace`
untouched. The proof below is structural induction over the list of
input steps.
-/

-- `PanelsOnBus` is a `def` over a function type, so `ObserverState`
-- cannot auto-derive `Repr`. Omit it; the proof below does not need
-- a `Repr` instance.
structure ObserverState where
  panels : PanelsOnBus
  transmitTrace : List Unit

namespace ObserverState

/-- Initial observer state: empty map, empty transmit trace. -/
def empty : ObserverState :=
  { panels := Phase2.empty, transmitTrace := [] }

end ObserverState

/-! ## applyStep

The receive-side step. Updates the `PanelsOnBus` via `observe` and
leaves the `transmitTrace` untouched — this is the load-bearing
shape of the proof. A future change that touched `transmitTrace`
inside this function would break the theorem at `simp`.
-/

def applyStep (step : ObservationStep) (state : ObserverState) : ObserverState :=
  { state with panels := observe step.now step.uuid state.panels }

/-! ## runObservation

Fold-style runner: apply every `ObservationStep` in `steps` to the
initial `ObserverState.empty`. Models the lifetime of the
`CanLinkService.observe` pipeline from boot to dispose.
-/

def runObservation (steps : List ObservationStep) : ObserverState :=
  steps.foldl (fun state step => applyStep step state) ObserverState.empty

/-! ## observe_emits_no_transmit (SC-007 + FR-014)

Passive-observation invariant: for any sequence of observation steps,
the resulting `ObserverState.transmitTrace` is the empty list. The
projection of `runObservation`'s effect onto the transmit-trace
alphabet is the empty trace.

Proof: `runObservation` is a `List.foldl` of `applyStep` over the
initial state, which has `transmitTrace = []`. Each `applyStep`
preserves `transmitTrace` (by construction — it sets only `panels`).
The auxiliary lemma `foldl_preserves_transmit_trace` carries the
preservation through the fold; the theorem follows by `simp`.
-/

theorem foldl_preserves_transmit_trace
    (steps : List ObservationStep) (state : ObserverState) :
    (steps.foldl (fun s step => applyStep step s) state).transmitTrace = state.transmitTrace := by
  induction steps generalizing state with
  | nil => rfl
  | cons step rest ih =>
    simp [List.foldl]
    rw [ih (applyStep step state)]
    rfl

theorem observe_emits_no_transmit (steps : List ObservationStep) :
    (runObservation steps).transmitTrace = [] := by
  unfold runObservation
  rw [foldl_preserves_transmit_trace]
  rfl

end Stem.ButtonPanelTester.Phase2
