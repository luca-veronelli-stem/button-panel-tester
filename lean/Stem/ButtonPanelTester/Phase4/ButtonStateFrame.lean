/-
T002 — Lean Phase-4 module for `ButtonStateFrame` (SP_APP VAR_WRITE button-state codec).

Mechanises the 5-byte button-state app payload from
`specs/005-button-press-test/contracts/button-state-wire-format.md`
§"App-layer payload (5 bytes)" and `specs/005-button-press-test/data-model.md`
§1: `[0]` command high `0x00` · `[1]` command low `0x02` (`SP_APP_CMD_ID_VAR_WRITE`)
· `[2]` variable address high · `[3]` variable address low (`0x80NN`) · `[4]`
key-state byte (`TxTasti`). Byte-level TX/RX model — `encode` produces a
`List Nat` so `encode_length` is meaningful and `parse` rejects on length
(the only rejection axis, house codec style — mirrors `WhoIAmFrame.parse`'s
length-only reject; command/address filtering is the observer's job in Phase C,
R6).

The F# surface lives at `src/ButtonPanelTester.Core/Can/ButtonStateFrame.fs`
(T005); the FsCheck round-trip/length properties live at
`tests/.../Property/Can/ButtonStateFrameProperties.fs` (T007) and the
fixture-driven parse tests at `tests/.../Unit/Can/ButtonStateFrameFixtureTests.fs`
(T007). This Lean re-statement lands in commit group A1, ahead of the F# surface
in A2, per Constitution Principle I (Lean spec → test → impl).

Constitution Principle I: no `sorry`, no custom axioms.
-/

namespace Stem.ButtonPanelTester.Phase4

/-! ## ButtonStateFrame

Lean model of the decoded VAR_WRITE button report (data-model §1). `address`
is the `0x80NN` variable address as a single `Nat` (the F# side carries it as a
`uint16` wrapped in `VariableAddress`); `bitmap` is the raw key-state byte
(`TxTasti`, the F# `KeyStateBitmap` wrapper). The byte-level big-endian split of
the address is modelled explicitly so `encode_length` and the round-trip carry
content.
-/

structure ButtonStateFrame where
  address : Nat
  bitmap : Nat
  deriving DecidableEq, Repr

namespace ButtonStateFrame

/-! ## encode

Byte-level wire encoder: exactly `[0x00, 0x02, addrHi, addrLo, bitmap]` with the
command fixed at `0x00:0x02` and the address split big-endian as
`addrHi = address / 256`, `addrLo = address % 256` (the `< 65536` range guard is
the F# `uint16` type's concern, not the model's, so the split is lossless for
every `Nat`).
-/

def encode (f : ButtonStateFrame) : List Nat :=
  [0, 2, f.address / 256, f.address % 256, f.bitmap]

/-! ## parse

Wire decoder: length is the only rejection axis (house codec style) — anything
not exactly 5 bytes parses to `none`. The command bytes are accepted as-is
(observer filters them, R6); the address is recombined `hi * 256 + lo` and the
bitmap read straight through.
-/

def parse (bytes : List Nat) : Option ButtonStateFrame :=
  match bytes with
  | [_, _, addrHi, addrLo, bitmap] =>
    some
      { address := addrHi * 256 + addrLo
        bitmap := bitmap }
  | _ => none

/-! ## encode_length (data-model §1) -/

/-- The encoder always produces exactly the 5 wire bytes of the VAR_WRITE
button-state payload (contract §"App-layer payload (5 bytes)"). Purely
definitional: `encode` is a five-element list literal. -/
theorem encode_length (f : ButtonStateFrame) : (encode f).length = 5 := by
  rfl

/-! ## parse_encode_roundtrip (data-model §1) -/

/-- Round-trip property: for every `ButtonStateFrame` (no well-formedness
precondition), `parse (encode f) = some f`. The command bytes round-trip
trivially (accepted, not validated) and the big-endian address split is lossless
(`hi * 256 + lo` recombines by `Nat.div_add_mod`).

Proof: `simp` unfolds `parse ∘ encode` down to the address recombination
obligation `address / 256 * 256 + address % 256 = address`, which `omega` closes
(`Nat.div_add_mod` in linear-arithmetic form). -/
theorem parse_encode_roundtrip (f : ButtonStateFrame) :
    parse (encode f) = some f := by
  cases f with
  | mk address bitmap =>
    simp [parse, encode]
    omega

end ButtonStateFrame

end Stem.ButtonPanelTester.Phase4
