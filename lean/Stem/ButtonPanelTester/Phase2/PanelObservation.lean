/-
T029 — Lean Phase-2 module for `PanelObservation` (variant decoding side).

Mechanises the totality of `decodeVariant`, per `specs/003-panel-discovery/
data-model.md` §2 (FR-003): for every `UInt8`, `decodeVariant`
produces exactly one `VariantIdentity` value, and that value falls into one
of the six classifications
`{edenXpClass, optimusXpClass, r3LXpClass, edenBs8Class, virginClass,
unknownClass}`. A closed `match` over a `VariantClass` `inductive` witnesses
the closure at the type level; adding a seventh `VariantIdentity` case or a
sixth `MarketingVariant` case would break the wildcard-free `classify`
function and the proof under `cases`.

The F# surface lives at `src/ButtonPanelTester.Core/Can/PanelObservation.fs`
(T014); the FsCheck classifier property lives at `tests/.../Property/Can/
VariantDecoderProperties.fs` (T023) and mirrors the `VariantClass` shape
here. The trio rides into the tree as one vertical PR-B commit.

Constitution Principle I: no `sorry`, no custom axioms. The proof is
`cases h : classify (decodeVariant raw) <;> simp` — six sub-goals, each
discharged by reducing the matching disjunct to `True`.
-/

namespace Stem.ButtonPanelTester.Phase2

/-! ## MarketingVariant

Closed four-case inductive mirroring the F# `MarketingVariant` DU in
`src/ButtonPanelTester.Core/Can/PanelObservation.fs` (T014). Case order
matches the byte-ordering of the audited mapping (`0x03 → 0x0A → 0x0B
→ 0x0C`) rather than the F# alphabetical-ish ordering, so a reader
walking the byte table left-to-right sees the same sequence here.
-/

inductive MarketingVariant where
  | edenXp
  | optimusXp
  | r3LXp
  | edenBs8
  deriving DecidableEq, Repr

/-! ## VariantIdentity

Closed three-case inductive mirroring the F# `VariantIdentity` DU in
`src/ButtonPanelTester.Core/Can/PanelObservation.fs` (T014). The `unknown`
arm carries the raw byte (`Nat` here as a stand-in for `UInt8`) so the
F# GUI's detail affordance can render it for `Unknown` cases without
losing information.
-/

inductive VariantIdentity where
  | marketing (variant : MarketingVariant)
  | virgin
  | unknown (raw : Nat)
  deriving Repr

/-! ## decodeVariant

Total decoder mapping every `Nat` to a `VariantIdentity`, mirroring the
F# `VariantDecoder.decode` function in
`src/ButtonPanelTester.Core/Can/PanelObservation.fs` (T014). The five
literal cases (`3, 10, 11, 12, 255`) correspond to the audited
`ID_MACHINE_TYPE` constants per `CORRECTIONS.md` §"Items unchanged";
the fallback arm (`raw => unknown raw`) catches every other byte and
preserves the raw value.
-/

def decodeVariant (raw : Nat) : VariantIdentity :=
  match raw with
  | 3 => .marketing .edenXp
  | 10 => .marketing .optimusXp
  | 11 => .marketing .r3LXp
  | 12 => .marketing .edenBs8
  | 255 => .virgin
  | other => .unknown other

/-! ## VariantClass

Six-way classification of `VariantIdentity`, mirroring the F# private
`VariantClass` DU in the FsCheck classifier
`tests/.../Property/Can/VariantDecoderProperties.fs` (T023). The four
`marketing` arms are flattened into their per-variant classes so the
closure statement matches what the GUI must render distinctly per
FR-003.
-/

inductive VariantClass where
  | edenXpClass
  | optimusXpClass
  | r3LXpClass
  | edenBs8Class
  | virginClass
  | unknownClass
  deriving DecidableEq, Repr

namespace VariantIdentity

/-- Classifier dual to the F# `classify` function in the property-test
file. Wildcard-free — a future seventh `VariantIdentity` case would
break elaboration here AND in the F# classifier, forcing a cross-layer
update. -/
def classify (v : VariantIdentity) : VariantClass :=
  match v with
  | .marketing .edenXp => .edenXpClass
  | .marketing .optimusXp => .optimusXpClass
  | .marketing .r3LXp => .r3LXpClass
  | .marketing .edenBs8 => .edenBs8Class
  | .virgin => .virginClass
  | .unknown _ => .unknownClass

end VariantIdentity

/-! ## variant_decoding_total (data-model.md §2 / FR-003)

Totality: for every `Nat`, `decodeVariant` produces a `VariantIdentity`
that classifies as one of the six declared shapes. The function's mere
existence already proves "decodeVariant raw is some VariantIdentity"
(Lean's exhaustiveness checker rejects a non-total `match` at definition
time); the theorem upgrades this to "and that VariantIdentity falls into
one of the six wildcard-free classifications", which is the actual
contract FR-003 expects.

Proof: `cases h : classify (decodeVariant raw)` produces six sub-goals,
one per `VariantClass` constructor; `simp` discharges each by reducing
the matching disjunct to `True`.
-/

theorem variant_decoding_total (raw : Nat) :
    VariantIdentity.classify (decodeVariant raw) = .edenXpClass
  ∨ VariantIdentity.classify (decodeVariant raw) = .optimusXpClass
  ∨ VariantIdentity.classify (decodeVariant raw) = .r3LXpClass
  ∨ VariantIdentity.classify (decodeVariant raw) = .edenBs8Class
  ∨ VariantIdentity.classify (decodeVariant raw) = .virginClass
  ∨ VariantIdentity.classify (decodeVariant raw) = .unknownClass := by
  cases h : VariantIdentity.classify (decodeVariant raw) <;> simp

end Stem.ButtonPanelTester.Phase2
