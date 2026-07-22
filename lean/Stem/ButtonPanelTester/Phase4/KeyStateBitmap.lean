/-
T003 + T051 — Lean Phase-4 module for the key-state press-edge detector and
the #293 press-edge arming model.

Mechanises the masked press-edge detector from
`specs/005-button-press-test/contracts/button-state-wire-format.md` §"Bitmap
semantics (R2)" and `specs/005-button-press-test/data-model.md` §2, and the
press-edge arming rule from `data-model.md` §6b (FR-006 as amended 2026-07-20,
#293). Firmware ground truth (R2): on the wire **pressed = bit `0`,
released/idle = bit `1`** (`UserMain.c:1369,:978`), so a press is an active
button's bit transitioning `1 → 0` between two consecutive frames. Bits
outside the variant's active mask are ignored (FR-014).

The model carries the eight wire bit positions explicitly (`Nat.testBit` over
the active mask, the prior frame, and the next frame) so the two theorems carry
content rather than restating a definition: a position is a press edge **iff**
it is active, was high (`1`, released) in `prior`, and is low (`0`, pressed) in
`next`; and an inactive position is never reported.

The F# surface lives at `src/ButtonPanelTester.Core/Can/KeyStateBitmap.fs` (T008,
with `PressedBit = 0uy` the one-line-flip point for a bench surprise); the FsCheck
properties mirroring these theorems live at `tests/.../Property/Can/
KeyStateBitmapProperties.fs` (T009). This Lean re-statement lands in commit group
A1, ahead of the F# surface in A3, per Constitution Principle I.

Constitution Principle I: no `sorry`, no custom axioms.
-/

namespace Stem.ButtonPanelTester.Phase4

/-! ## pressEdges

The active-masked bit positions (`0..7`) that transitioned into the pressed
state (`1 → 0`) between `prior` and `next`. `mask`, `prior`, `next` are the
byte values as `Nat`; `Nat.testBit i` reads wire bit `i`. Pressed = `0`, so a
press edge is `mask.testBit i = true ∧ prior.testBit i = true ∧
next.testBit i = false`.
-/

def pressEdges (mask prior next : Nat) : List Nat :=
  (List.range 8).filter (fun i => mask.testBit i && prior.testBit i && !next.testBit i)

/-! ## press_edge_iff_high_to_low (data-model §2; FR-006) -/

/-- A bit position `i < 8` is reported as a press edge **iff** it is active in
`mask`, was high (`1`, released) in `prior`, and is low (`0`, pressed) in `next`
— the firmware press transition `1 → 0` (R2). The active-mask conjunct is what
keeps an inactive position out (`inactive_bits_ignored` is the dedicated
corollary). -/
theorem press_edge_iff_high_to_low (mask prior next i : Nat) (hi : i < 8) :
    i ∈ pressEdges mask prior next ↔
      (mask.testBit i = true ∧ prior.testBit i = true ∧ next.testBit i = false) := by
  simp [pressEdges, List.mem_filter, List.mem_range, hi, and_assoc]

/-! ## inactive_bits_ignored (FR-014) -/

/-- A bit position outside the active mask is never reported as a press edge,
whatever the two frames carry — bits outside the variant's active mask are
ignored (FR-014). -/
theorem inactive_bits_ignored (mask prior next i : Nat) (hmask : mask.testBit i = false) :
    i ∉ pressEdges mask prior next := by
  simp [pressEdges, List.mem_filter, hmask]

/-! ## Press-edge arming (data-model §6b; FR-006 as amended 2026-07-20, #293)

A cold panel boots with `TxTasti = 0` and its bits **latch** — a press clears
the bit, a release sets it (`UserMain.c:1369,:1375`). A position's FIRST press
therefore never reaches the wire: clearing an already-clear bit leaves
`TxTasti` unchanged, so the `TxTasti ≠ TxTastiOld` change gate (`:973`) never
fires. The release DOES transmit. No tool-side edge rule can recover an event
the panel never sent, so scoring layers an **arming** state above `pressEdges`
(which is unchanged): the armed state is a bitmask (`Nat`) accumulating every
observed bitmap (baseline frame included), and

* an **armed** position — observed released (bit `1`) in some earlier bitmap —
  scores exactly on `pressEdges` (the `1 → 0` press edge, unchanged);
* an **unarmed** position scores on its `0 → 1` (release) transition —
  unambiguous proof of a completed press, since a button cannot be released
  without having been pressed — and that same transition arms it.

The arming rule's F# surface lands in T052 (`KeyStateBitmap.fs`), after this
Lean re-statement, per Constitution Principle I.
-/

/-- Arming update: fold an observed bitmap into the armed state. A position is
armed once it has been observed with bit value `1` (released) in some earlier
bitmap — baseline included — so the armed state is the bitwise OR of every
bitmap observed so far. -/
def arm (armed observed : Nat) : Nat := armed ||| observed

/-- The §6b scoring rule: an active position scores on the press edge
(`1 → 0`) when armed, and on the release transition (`0 → 1`) when unarmed.
Both branches are transitions — no absolute byte is ever read as press-state. -/
def scored (armed mask prior next i : Nat) : Prop :=
  mask.testBit i = true ∧
    (if armed.testBit i then
      prior.testBit i = true ∧ next.testBit i = false
    else
      prior.testBit i = false ∧ next.testBit i = true)

/-! ## armed_scores_on_press_edge (data-model §6b) -/

/-- For an armed position the §6b rule degenerates to the plain detector: it
scores **iff** `pressEdges` reports the position. Steady-state behaviour —
every button after its first press/release cycle, and every button on a warm
panel — is exactly the pre-#293 one. -/
theorem armed_scores_on_press_edge (armed mask prior next i : Nat) (hi : i < 8)
    (harmed : armed.testBit i = true) :
    scored armed mask prior next i ↔ i ∈ pressEdges mask prior next := by
  rw [press_edge_iff_high_to_low mask prior next i hi]
  simp [scored, harmed]

/-! ## unarmed_scores_on_first_release (data-model §6b) -/

/-- An unarmed active position scores on its `0 → 1` (release) transition —
the completed first press the firmware never transmitted — and folding the
observed `next` bitmap into the armed state arms the position. -/
theorem unarmed_scores_on_first_release (armed mask prior next i : Nat)
    (hunarmed : armed.testBit i = false) (hactive : mask.testBit i = true)
    (hprior : prior.testBit i = false) (hnext : next.testBit i = true) :
    scored armed mask prior next i ∧ (arm armed next).testBit i = true := by
  simp [scored, arm, Nat.testBit_or, hunarmed, hactive, hprior, hnext]

/-! ## arming_monotonic (data-model §6b) -/

/-- The arming update never un-arms a position, whatever bitmap is observed
next: arming is per position and monotonic. -/
theorem arming_monotonic (armed observed i : Nat) (harmed : armed.testBit i = true) :
    (arm armed observed).testBit i = true := by
  simp [arm, Nat.testBit_or, harmed]

/-! ## no_double_score_after_arming (data-model §6b) -/

/-- Once an unarmed position has scored via the release rule — and is thereby
armed, since that score requires `next` to carry bit `1` — a subsequent
`0 → 1` transition can never score it again: the armed branch demands the
prior bit be `1`. The unarmed rule fires at most once per position. -/
theorem no_double_score_after_arming (armed mask prior next prior' next' i : Nat)
    (hunarmed : armed.testBit i = false)
    (hscored : scored armed mask prior next i)
    (hprior' : prior'.testBit i = false) (hnext' : next'.testBit i = true) :
    ¬ scored (arm armed next) mask prior' next' i := by
  have hnext : next.testBit i = true := by
    have h := hscored.2
    simp [hunarmed] at h
    exact h.2
  have harmed : (arm armed next).testBit i = true := by
    simp [arm, Nat.testBit_or, hnext]
  intro hscored'
  have h' := hscored'.2
  simp [harmed, hprior', hnext'] at h'

end Stem.ButtonPanelTester.Phase4
