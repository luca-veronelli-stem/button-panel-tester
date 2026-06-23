/-
T021 — Lean Phase-4 module for the button-press-test enablement guard.

Mechanises the pure enablement predicate of
`specs/005-button-press-test/data-model.md` §6 (FR-001): the button-press test
is OFFERED iff the CAN link is `Connected`, a panel is selected AND baptized,
AND that panel is observable on the bus. Three PRIORITY-ORDERED conjuncts —
link → selected-baptized → observable — mirroring the F# `testEnablement`
(`src/ButtonPanelTester.Core/Can/ButtonPressTest.fs`, T022).

Reuses the Phase-3 `Enablement` verdict DU and `isConnected`: the button-press
test shares the baptism enablement SHAPE, so the F# surface reuses the same
`Enablement` DU from `Baptism.fs` and this Lean model reuses the same inductive.
The Lean model carries the boolean verdict only; the F# `Disabled` case
additionally carries the GUI explanation string (the explanation-correctness
property is `TestEnablementGuards`'s second clause, T022).

The theorem `test_enabled_iff` proves this ordered case analysis is EQUIVALENT
to the flat conjunction the spec states — so the GUI can render the structured
verdict while the property suite checks the same iff (`TestEnablementGuards`,
T022). That iff is the SC-008 basis: the test is unavailable (with a reason) on
a non-baptized panel or a non-`Connected` link.

`selectedBaptized` / `observable` abstract the two bits the F# guard reads (is a
baptized panel selected; is that panel observable on the bus) — both
already-computed booleans on the F# side — so the theorem is structural over the
link's four top-level constructors and the two flags. The link state is the
Phase-2 `CanLinkState` model (abstract `Adapter` / `Detail` carriers); the guard
reads exactly one bit off it (`isConnected`).

Constitution Principle I: no `sorry`, no custom axioms.
-/

import Stem.ButtonPanelTester.Phase3.Enablement

namespace Stem.ButtonPanelTester.Phase4

open Stem.ButtonPanelTester.Phase2 (CanLinkState)
open Stem.ButtonPanelTester.Phase3 (Enablement isConnected)

/-! ## testEnablement (data-model §6, FR-001)

Priority-ordered case analysis mirroring the F# `testEnablement`: link down →
disabled; then no baptized panel selected → disabled; then panel not observable
→ disabled; otherwise enabled (`Connected`, a baptized panel selected, that
panel observable on the bus). The three conjuncts in priority order — the same
ordered shape as `baptizeEnablement`, with the baptism count/selection conjuncts
replaced by the test's selected-baptized / observable bits. -/

def testEnablement {Adapter Detail : Type}
    (link : CanLinkState Adapter Detail) (selectedBaptized : Bool) (observable : Bool) :
    Enablement :=
  if isConnected link = false then .disabled
  else if selectedBaptized = false then .disabled
  else if observable = false then .disabled
  else .enabled

/-! ## test_enabled_iff (data-model §6, FR-001)

The ordered case analysis is equivalent to the flat conjunction the spec states:
the button-press test is enabled IFF the link is `Connected`, a baptized panel
is selected, AND that panel is observable on the bus. This is the FR-001 / SC-008
guarantee mirrored by the FsCheck `TestEnablementGuards` property (T022). -/

theorem test_enabled_iff {Adapter Detail : Type}
    (link : CanLinkState Adapter Detail) (selectedBaptized observable : Bool) :
    testEnablement link selectedBaptized observable = .enabled ↔
      isConnected link = true ∧ selectedBaptized = true ∧ observable = true := by
  unfold testEnablement
  cases hconn : isConnected link <;> cases selectedBaptized <;> cases observable <;> simp_all

end Stem.ButtonPanelTester.Phase4
