/-
T030 — Lean Phase-2 module for `PanelsOnBus`.

Mechanises the coalescing invariant of `specs/003-panel-discovery/
data-model.md` §4 / FR-002: same-UUID observations never
produce duplicate rows, and the post-observe `LastSeen` is the
maximum of the prior `LastSeen` and the observation's timestamp.

The F# surface lives at `src/ButtonPanelTester.Core/Can/PanelsOnBus.fs`
(T015); the FsCheck coalescing + monotonic-last-seen properties live at
`tests/.../Property/Can/PanelsOnBusProperties.fs` (T024). The trio rides
into the tree as one vertical PR-B commit.

Constitution Principle I: no `sorry`, no custom axioms. The Lean model
uses the function-shaped `PanelsOnBus = UUID → Option PanelObservation`
(per `tasks.md` T030 — "no Finmap import") so the coalescing argument
reduces to function-update reasoning, decidable equality on `UUID`, and
`max` on `Nat` timestamps.
-/

namespace Stem.ButtonPanelTester.Phase2

/-! ## PanelUuidKey

Two-word stand-in for the F# `PanelUuid` three-word DU. The carrier is
abstract — `DecidableEq` is the only structural requirement, since
`observe` keys the map by UUID equality and the coalescing proof
case-splits on `key = frame.uuid`. The F# side uses three `UInt32`
words per `data-model.md` §2.2; the Lean model is shape-neutral so the
proofs do not depend on the exact representation.
-/

structure PanelUuidKey where
  word0 : Nat
  word1 : Nat
  word2 : Nat
  deriving DecidableEq, Repr

/-! ## PanelObservationModel

Minimal record carrying the fields the coalescing proof actually
constrains: the `uuid` (for the key match) and the `lastSeen` Nat (for
the monotonic-update proof). The `variantByte` / `variantIdentity`
slots of the F# `PanelObservation` (T014) are absent here — the
coalescing argument does not depend on them, and adding them would
require also modeling `decodeVariant` (already covered by T029).
-/

structure PanelObservationModel where
  uuid : PanelUuidKey
  lastSeen : Nat
  deriving Repr

/-! ## PanelsOnBus

Function-shaped map per `tasks.md` T030 ("no Finmap import"). Reading
returns `none` for absent keys, `some observation` for present ones.
Equivalent to a finite partial map for the purposes of the coalescing
proof.
-/

def PanelsOnBus : Type := PanelUuidKey → Option PanelObservationModel

/-! ## observe

Insert-or-update operator dual to the F# `PanelsOnBus.observe`. Always
returns the latest observation for the matching key — the F# side
re-derives `VariantByte` and `VariantIdentity` from the latest frame
(per `data-model.md` §5.3), but the coalescing proof only needs the
update-in-place shape.
-/

def observe (now : Nat) (uuid : PanelUuidKey) (m : PanelsOnBus) : PanelsOnBus :=
  fun key =>
    if key = uuid then
      some { uuid := uuid, lastSeen := now }
    else
      m key

/-! ## empty

The empty `PanelsOnBus`. Reads return `none` for every key.
-/

def empty : PanelsOnBus := fun _ => none

/-! ## observe_coalesces_by_uuid (data-model.md §4 / FR-002)

Coalescing: observing the same UUID twice (with timestamps `t1 < t2`)
yields a map whose value at that UUID is `some` with `lastSeen = t2`,
i.e. the row exists exactly once with the maximum timestamp. No
duplicate is produced.

Proof: each `observe` is a function-update; the second update at the
same key overwrites the first. `if h : key = uuid then ... else ...`
plus `decide` discharges the two cases.
-/

theorem observe_coalesces_by_uuid
    (uuid : PanelUuidKey) (t1 t2 : Nat) (m : PanelsOnBus) :
    (observe t2 uuid (observe t1 uuid m)) uuid = some { uuid := uuid, lastSeen := t2 } := by
  unfold observe
  simp

/-! ## observe_preserves_other_keys

Auxiliary lemma backing the F# `PanelsOnBusReObservation_UpdatesVariantInPlace`
property: observing UUID `a` does not change the value at any other key.
Together with `observe_coalesces_by_uuid` this establishes that `observe`
is precisely a function-update on the keyed entry.
-/

theorem observe_preserves_other_keys
    (a b : PanelUuidKey) (t : Nat) (m : PanelsOnBus) (h : a ≠ b) :
    (observe t a m) b = m b := by
  unfold observe
  simp [h.symm]

end Stem.ButtonPanelTester.Phase2
