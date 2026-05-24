/-
T031 — Lean Phase-2 module for `Pruning`.

Mechanises the pruning-correctness invariant of `specs/002-can-link-and-panel-
discovery/data-model.md` §5.4 / FR-011: post-prune membership iff
`now - lastSeen ≤ ttl`. The F# side carries `ttl` as a `TimeSpan` and uses
`DateTimeOffset` subtraction; the Lean model carries both as `Nat`s
(milliseconds since epoch is a reasonable interpretation, but the proof
is independent of the unit — it reasons in pure arithmetic).

The F# surface lives at `src/ButtonPanelTester.Core/Can/Pruning.fs` (T016);
the FsCheck pruning + boundary + idempotence properties live at
`tests/.../Property/Can/PruningProperties.fs` (T025). The trio rides into
the tree as one vertical PR-B commit.

Constitution Principle I: no `sorry`, no custom axioms. The proof reduces
to two `Decidable.byContradiction`-style splits on `lastSeen + ttl <
now`.

Builds on `Phase2/PanelsOnBus.lean` (T030) for the function-shaped map.
-/

import Stem.ButtonPanelTester.Phase2.PanelsOnBus

namespace Stem.ButtonPanelTester.Phase2

/-! ## prune

Function-shaped pruning operator. Returns the input map masked by the
predicate `now ≤ lastSeen + ttl` (equivalent to `now - lastSeen ≤ ttl`
on `Nat` once we sidestep the `Nat` subtraction-saturates-at-zero issue
by flipping the inequality). Absent keys stay absent.

The Lean model uses `Nat` throughout, so subtraction would saturate at
zero. Flipping the inequality to `now ≤ lastSeen + ttl` keeps the
arithmetic clean and is equivalent on real time (where both sides are
non-negative). The F# side uses `DateTimeOffset` subtraction which is
signed, but the property is stated and exercised on a non-negative
window.
-/

def prune (ttl now : Nat) (m : PanelsOnBus) : PanelsOnBus :=
  fun key =>
    match m key with
    | some observation =>
        if now ≤ observation.lastSeen + ttl then some observation else none
    | none => none

/-! ## prune_partitions_by_threshold (data-model.md §5.4 / FR-011)

Post-prune membership iff `now ≤ lastSeen + ttl`. The `↔` form makes
the partition explicit — kept rows are exactly the ones satisfying the
predicate, dropped rows are exactly the ones violating it.

Proof: `unfold prune` reduces to an `if`-`then`-`else`; `split` produces
two sub-goals (predicate-true and predicate-false); `simp` discharges
each by reducing the `if` to its branch.
-/

theorem prune_partitions_by_threshold
    (ttl now : Nat) (key : PanelUuidKey) (m : PanelsOnBus) (observation : PanelObservationModel)
    (h : m key = some observation) :
    (prune ttl now m) key = some observation ↔ now ≤ observation.lastSeen + ttl := by
  unfold prune
  rw [h]
  split <;> simp_all

/-! ## prune_idempotent

Pruning twice with the same `now` and `ttl` is the same as pruning once
(parallel to the FsCheck `Pruning_IdempotentAtSameNow` property in T025).
Useful mechanisation of the operational expectation behind the 1-second
prune timer in `CanLinkService` (T046) — back-to-back ticks at the same
clock instant don't drop additional rows.
-/

theorem prune_idempotent (ttl now : Nat) (m : PanelsOnBus) :
    prune ttl now (prune ttl now m) = prune ttl now m := by
  funext key
  unfold prune
  cases h : m key with
  | none => simp
  | some observation =>
    split <;> simp_all

end Stem.ButtonPanelTester.Phase2
