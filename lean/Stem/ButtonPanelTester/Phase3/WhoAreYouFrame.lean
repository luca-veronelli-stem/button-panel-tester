/-
T002 — Lean Phase-3 module for `WhoAreYouFrame` (WHO_ARE_YOU TX codec).

Mechanises the 4-byte WHO_ARE_YOU app payload from
`specs/004-baptism-workflow/contracts/master-sequence-wire-format.md`
§"WHO_ARE_YOU app payload (4 B)" and `specs/004-baptism-workflow/data-model.md`
§2.1: `[0]` machineType u8 · `[1..2]` fwType u16 big-endian · `[3]` reset flag
(non-zero = set). Unlike the Phase-2 RX model (record-level, byte layout opaque),
this TX model is byte-level — `encode` produces a `List Nat` so `encode_length`
is meaningful and `parse` rejects on length (the only rejection axis, house
codec style).

The module also carries `encodeVariant` (data-model §1): the variant →
machine-identity byte mapping (EDEN-XP `0x03`, OPTIMUS-XP `0x0A`, R-3L XP
`0x0B`, EDEN-BS8 `0x0C`), total on the four `MarketingVariant` cases by
construction and proved the partial inverse of Phase 2's total decoder
(`variant_decoding_total`, `Phase2/PanelObservation.lean`). The `0xFF` virgin
marker is deliberately NOT in `encodeVariant`'s range: it is the reset target
only, never a BoardVariant — the baptize picker never offers it (data-model §1,
FR-008).

The F# surface lives at `src/ButtonPanelTester.Core/Can/WhoAreYouFrame.fs`
(T006) and `src/ButtonPanelTester.Core/Can/BoardVariant.fs` (T005); the FsCheck
round-trip/length/inverse properties live at `tests/.../Property/Can/
WhoAreYouFrameProperties.fs` (T008). This Lean re-statement lands in commit
group A1, ahead of the F# surface in A2, per Constitution Principle I
(Lean spec → test → impl).

Constitution Principle I: no `sorry`, no custom axioms.
-/

import Stem.ButtonPanelTester.Phase2.PanelObservation

namespace Stem.ButtonPanelTester.Phase3

open Stem.ButtonPanelTester.Phase2 (MarketingVariant VariantIdentity decodeVariant)

/-! ## WhoAreYouFrame

Lean model of the WHO_ARE_YOU TX payload fields (data-model §2.1).
`machineType` is the chosen variant's identity byte, or `0xFF` for reset;
`fwType` is the panel's announced fwType (baptize) or a known constant
(reset); `reset` is always set in this feature (FR-003, FR-008) but the
codec models both polarities so the round-trip is total.
-/

structure WhoAreYouFrame where
  machineType : Nat
  fwType : Nat
  reset : Bool
  deriving DecidableEq, Repr

namespace WhoAreYouFrame

/-! ## encode

Byte-level wire encoder: exactly `[machineType, fwHi, fwLo, resetByte]` with
the fwType split big-endian as `fwHi = fwType / 256` (top byte deliberately
UNMASKED so the split is lossless for every `Nat` — the `< 65536` range guard
is the F# `uint16` type's concern, not the model's) and `fwLo = fwType % 256`;
`resetByte` is `0x01`/`0x00` (contract §WHO_ARE_YOU: non-zero = set).
-/

def encode (f : WhoAreYouFrame) : List Nat :=
  [f.machineType, f.fwType / 256, f.fwType % 256, if f.reset then 1 else 0]

/-! ## parse

Wire decoder: length is the only rejection axis (house codec style) — anything
not exactly 4 bytes parses to `none`. The fwType is recombined `hi * 256 + lo`;
the reset flag follows the slave's read (`AutoAddressSlave.c`): any non-zero
byte means set.
-/

def parse (bytes : List Nat) : Option WhoAreYouFrame :=
  match bytes with
  | [machineType, fwHi, fwLo, resetByte] =>
    some
      { machineType := machineType
        fwType := fwHi * 256 + fwLo
        reset := resetByte != 0 }
  | _ => none

/-! ## encode_length (data-model §2.1) -/

/-- The encoder always produces exactly the 4 wire bytes of the
WHO_ARE_YOU app payload (contract §"WHO_ARE_YOU app payload (4 B)").
Purely definitional: `encode` is a four-element list literal. -/
theorem encode_length (f : WhoAreYouFrame) : (encode f).length = 4 := by
  rfl

/-! ## parse_encode_roundtrip (data-model §2.1) -/

/-- Round-trip property: for every `WhoAreYouFrame` (no well-formedness
precondition), `parse (encode f) = some f`. The big-endian fwType split is
lossless (`hi * 256 + lo` recombines by `Nat.div_add_mod`), and the reset
byte `0x01`/`0x00` reads back as the originating `Bool` under the slave's
non-zero test.

Proof: `cases` on the reset `Bool` (so the `if` reduces), then `simp` unfolds
`parse ∘ encode` down to the fwType recombination obligation
`fwType / 256 * 256 + fwType % 256 = fwType`, which `omega` closes
(`Nat.div_add_mod` in linear-arithmetic form). -/
theorem parse_encode_roundtrip (f : WhoAreYouFrame) :
    parse (encode f) = some f := by
  cases f with
  | mk machineType fwType reset =>
    cases reset <;> simp [parse, encode] <;> omega

end WhoAreYouFrame

/-! ## encodeVariant (data-model §1)

Variant → machine-identity byte, total on the four `MarketingVariant` cases by
construction. The byte table mirrors Phase 2's `decodeVariant` literals
(`0x03 / 0x0A / 0x0B / 0x0C`, audited firmware constants). The `0xFF` virgin
marker is intentionally absent: it is the reset target only, never a variant
(data-model §1, FR-008).
-/

def encodeVariant : MarketingVariant → Nat
  | .edenXp => 3
  | .optimusXp => 10
  | .r3LXp => 11
  | .edenBs8 => 12

/-! ## encode_decode_inverse (data-model §1) -/

/-- Partial-inverse property: decoding an encoded variant byte recovers exactly
that marketing variant — `decodeVariant (encodeVariant v) = .marketing v`. This
is the inverse direction of Phase 2's `variant_decoding_total` restricted to
the four marketing bytes; together they pin the byte table from both sides.

Proof: `cases` on the four-case `MarketingVariant`; each literal byte reduces
through `decodeVariant`'s match definitionally (`rfl`). -/
theorem encode_decode_inverse (v : MarketingVariant) :
    decodeVariant (encodeVariant v) = .marketing v := by
  cases v <;> rfl

end Stem.ButtonPanelTester.Phase3
