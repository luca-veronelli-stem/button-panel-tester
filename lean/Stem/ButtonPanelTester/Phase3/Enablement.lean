/-
T027 — Lean Phase-3 module for the enablement guards.

Mechanises the pure enablement predicates of
`specs/004-baptism-workflow/data-model.md` §6 (FR-002 / FR-008): the two
guards both stories' GUI surfaces render. Baptize is enabled IFF the link is
`Connected`, exactly one panel is announcing, AND that panel is selected
(FR-002); Reset is enabled IFF the link is `Connected` AND at most one panel
is announcing (FR-008). The counts range over ANNOUNCING panels only — a
claimed panel is silent (`AAS_STAND_BY`) and invisible to the guards by
construction (spec assumption, CHK019).

The guards are modelled the way the F# surface (`baptizeEnablement` /
`resetEnablement` in `src/ButtonPanelTester.Core/Can/Baptism.fs`, T028) is
written: a PRIORITY-ORDERED case analysis that returns `enabled` or a
`disabled` verdict, one branch per failed conjunct (link down / zero
announcing / two-or-more announcing / none selected). The two theorems
`baptize_enabled_iff` / `reset_enabled_iff` prove that this ordered case
analysis is EQUIVALENT to the flat conjunction the spec states — so the GUI
can render the structured verdict while the property suite checks the same
iff (`EnablementGuards`, T028) the theorems pin. That iff is the SC-005
basis: destructive actions are unreachable with ≥ 2 panels.

The F# `Enablement` DU additionally carries the human-readable explanation
string on the `disabled` case; that text is GUI-facing and lives only in F#
(the explanation-correctness property is `EnablementGuards`'s second clause).
The Lean model carries the boolean verdict only — the theorems are about
enabled-ness, not wording.

The link state is the Phase-2 `CanLinkState` model (abstract `Adapter` /
`Detail` carriers); the guards read exactly one bit off it (`isConnected`),
so the theorems are structural over the four top-level constructors.

Constitution Principle I: no `sorry`, no custom axioms.
-/

import Stem.ButtonPanelTester.Phase2.CanLinkState

namespace Stem.ButtonPanelTester.Phase3

open Stem.ButtonPanelTester.Phase2 (CanLinkState)

/-! ## isConnected

The one bit the enablement guards read off the link state: is it `Connected`?
Wildcard over the other three top-level `CanLinkState` shapes
(`initializing` / `disconnected` / `error`) — the guards treat every
non-`Connected` state uniformly as "link down" (data-model §6). Mirrors the
F# `match link with Connected _ -> true | _ -> false`.
-/

def isConnected {Adapter Detail : Type} (link : CanLinkState Adapter Detail) : Bool :=
  match link with
  | .connected _ _ => true
  | _ => false

/-! ## Enablement

The boolean verdict of a guard. Mirrors the F# `Enablement` DU
(`Enabled | Disabled of explanation`), with the explanation string dropped:
the Lean theorems pin enabled-ness, the F# `Disabled` carries the GUI text.
-/

inductive Enablement where
  | enabled
  | disabled
  deriving DecidableEq, Repr

/-! ## baptizeEnablement (data-model §6, FR-002)

Priority-ordered case analysis mirroring the F# `baptizeEnablement`: link
down → disabled; then zero announcing → disabled; then two-or-more
announcing → disabled; then none selected → disabled; otherwise enabled
(`Connected`, exactly one announcing, that one selected). `announcingCount`
is a `Nat` — counts are non-negative by construction; `selected` abstracts
the F# `PanelUuid option` to the one bit the guard reads (is something
selected). The selected panel IS the lone announcer because the GUI only
lets a row in the announcing list be selected (the count = 1 ∧ selected
case); the "selected row pruned" edge is handled upstream (T032).
-/

def baptizeEnablement {Adapter Detail : Type}
    (link : CanLinkState Adapter Detail) (announcingCount : Nat) (selected : Bool) :
    Enablement :=
  if isConnected link = false then .disabled
  else if announcingCount = 0 then .disabled
  else if announcingCount ≥ 2 then .disabled
  else if selected = false then .disabled
  else .enabled

/-! ## resetEnablement (data-model §6, FR-008)

Priority-ordered case analysis mirroring the F# `resetEnablement`: link down
→ disabled; then two-or-more announcing → disabled; otherwise enabled
(`Connected`, at most one announcing). No selection conjunct — reset is a
broadcast and needs no list anchor (FR-008); its two-or-more `disabled`
verdict carries (F# side) the explanation that the broadcast would reach
every panel on the bus.
-/

def resetEnablement {Adapter Detail : Type}
    (link : CanLinkState Adapter Detail) (announcingCount : Nat) :
    Enablement :=
  if isConnected link = false then .disabled
  else if announcingCount ≥ 2 then .disabled
  else .enabled

/-! ## baptize_enabled_iff (data-model §6 / §8, FR-002)

The ordered case analysis is equivalent to the flat conjunction the spec
states: Baptize is enabled IFF the link is `Connected`, exactly one panel is
announcing, AND that panel is selected. `announcingCount ≠ 0 ∧ ¬(count ≥ 2)`
collapses to `count = 1` (`omega`). This is the FR-002 half of the SC-005
guarantee mirrored by the FsCheck `EnablementGuards` property (T028).
-/

theorem baptize_enabled_iff {Adapter Detail : Type}
    (link : CanLinkState Adapter Detail) (announcingCount : Nat) (selected : Bool) :
    baptizeEnablement link announcingCount selected = .enabled ↔
      isConnected link = true ∧ announcingCount = 1 ∧ selected = true := by
  unfold baptizeEnablement
  -- `simp_all` discharges the link-down and not-selected branches outright;
  -- the lone survivor is the connected ∧ selected branch, whose nested
  -- count-`ite` `split`s into `count = 0 / ≥ 2 / = 1`, each closed by `omega`.
  cases hconn : isConnected link <;> cases selected <;> simp_all <;> split <;> simp_all <;> omega

/-! ## reset_enabled_iff (data-model §6 / §8, FR-008)

Reset is enabled IFF the link is `Connected` AND at most one panel is
announcing. The FR-008 half of the SC-005 guarantee mirrored by the FsCheck
`EnablementGuards` property (T028).
-/

theorem reset_enabled_iff {Adapter Detail : Type}
    (link : CanLinkState Adapter Detail) (announcingCount : Nat) :
    resetEnablement link announcingCount = .enabled ↔
      isConnected link = true ∧ announcingCount ≤ 1 := by
  unfold resetEnablement
  cases hconn : isConnected link <;> simp_all <;> omega

end Stem.ButtonPanelTester.Phase3
