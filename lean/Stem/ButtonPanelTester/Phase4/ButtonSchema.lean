/-
T010 — Lean Phase-4 module for the per-variant active-button schema.

Mechanises the active-only ordered schema from
`specs/005-button-press-test/data-model.md` §3 and
`specs/005-button-press-test/research.md` R3/R4. Bit assignment (R3) is uniform
across variants: `UP=0 · DOWN=1 · P1=2 · P2=3 · P3=4 · MEM=5 · STOP=6 · LIGHT=7`
(`UserMain.c:215–246`). A variant's active buttons are the fixed canonical
firmware order `[UP;DOWN;P1;P2;P3;MEM;STOP;LIGHT]` **filtered** by the variant's
active mask — total, order-preserving, and never carrying an inactive bit. The
FSM invariant `test_visits_active_only` (Phase D) rests on this filter
relationship.

`canonical_order_total` carries content rather than restating a definition: it
proves the filtered active list is a sublist of the canonical order (order
preservation) AND characterises membership exactly (a button is active **iff**
its bit is set in the mask — so an inactive bit never appears, FR-014/FR-016).

The F# surface lives at `src/ButtonPanelTester.Core/Can/ButtonSchema.fs` (T011,
the closed 8-case `FirmwareButton` DU + the four-variant table); the FsCheck
property `SchemaActiveOnlyInOrder` mirroring this theorem lives at
`tests/.../Property/Can/ButtonSchemaProperties.fs` (T012). This Lean re-statement
lands in commit group B1, ahead of the F# surface in B2, per Constitution
Principle I (Lean spec → test → impl).

Constitution Principle I: no `sorry`, no custom axioms.
-/

namespace Stem.ButtonPanelTester.Phase4

/-! ## FirmwareButton

The eight firmware buttons in canonical (= declaration) order. Mirrors the F#
closed DU `FirmwareButton` (`ButtonSchema.fs`, T011); the case order IS the
canonical prompt order a variant's active set is filtered from (R3).
-/

inductive FirmwareButton where
  | up | down | p1 | p2 | p3 | mem | stop | light
  deriving DecidableEq, Repr

namespace FirmwareButton

/-! ## bit

Wire bit assignment (R3), uniform across variants: `UP=0 … LIGHT=7`. The
detector reads wire bit `i` with `Nat.testBit i`; a variant's mask sets the bit
of each active button.
-/

def bit : FirmwareButton → Nat
  | up => 0
  | down => 1
  | p1 => 2
  | p2 => 3
  | p3 => 4
  | mem => 5
  | stop => 6
  | light => 7

/-! ## canonicalOrder

The fixed canonical firmware order (R3) — the prompt order a variant's active
buttons are filtered from. Every `FirmwareButton` appears exactly once.
-/

def canonicalOrder : List FirmwareButton :=
  [up, down, p1, p2, p3, mem, stop, light]

/-! ## activeButtons

A variant's active buttons: the canonical order filtered to the bits set in
`mask` (`Nat.testBit (bit b)`). `filter` preserves order, so the result is the
canonical sub-order — never a reordering, never an inactive bit.
-/

def activeButtons (mask : Nat) : List FirmwareButton :=
  canonicalOrder.filter (fun b => mask.testBit b.bit)

/-! ## mem_canonicalOrder -/

/-- Every firmware button appears in the canonical order — the totality fact the
membership characterisation rests on. -/
theorem mem_canonicalOrder (b : FirmwareButton) : b ∈ canonicalOrder := by
  cases b <;> decide

/-! ## canonical_order_total (data-model §3; FR-016) -/

/-- The active list is exactly the canonical firmware order filtered by the
active mask: it is a **sublist** of the canonical order (order-preserving — first
conjunct) and a button is in it **iff** its bit is set in the mask (totality +
no-inactive-bit — second conjunct). Together these pin the filter relationship
the FSM's `test_visits_active_only` invariant rests on. -/
theorem canonical_order_total (mask : Nat) :
    (activeButtons mask).Sublist canonicalOrder ∧
      ∀ b, b ∈ activeButtons mask ↔ mask.testBit b.bit = true := by
  refine ⟨List.filter_sublist, fun b => ?_⟩
  simp [activeButtons, List.mem_filter, mem_canonicalOrder b]

/-! ## optimus_active_set (R3 — the authoritative variant)

OPTIMUS-XP's mask `0x36` (bits 1,2,4,5) yields exactly `DOWN, P1, P3, MEM` in
canonical order — the buttons the SC-006 decals `Light, Suspension, Up, Down`
ride on. A concrete witness that `activeButtons` filters in canonical order. -/

theorem optimus_active_set :
    activeButtons 0x36 = [down, p1, p3, mem] := by
  decide

end FirmwareButton

end Stem.ButtonPanelTester.Phase4
