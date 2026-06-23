/-
T003 — Lean Phase-4 module for the key-state press-edge detector.

Mechanises the masked press-edge detector from
`specs/005-button-press-test/contracts/button-state-wire-format.md` §"Bitmap
semantics (R2)" and `specs/005-button-press-test/data-model.md` §2. Firmware
ground truth (R2): on the wire **pressed = bit `0`, released/idle = bit `1`**
(`UserMain.c:1369,:978`), so a press is an active button's bit transitioning
`1 → 0` between two consecutive frames. Bits outside the variant's active mask
are ignored (FR-014).

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

end Stem.ButtonPanelTester.Phase4
