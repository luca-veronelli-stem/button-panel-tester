/-
T028 — Lean Phase-2 module for `WhoIAmFrame`.

Mechanises the round-trip invariant from `specs/002-can-link-and-panel-discovery/
data-model.md` §2.3: `parse (encode f) = some f` for every well-formed
`WhoIAmFrame`. "Well-formed" mirrors the F# wire contract (FR-013 silent drop) —
`fwType = 0x04` is the only path through `parse`, so the theorem statement
constrains the abstract `WhoIAmFrame` by carrying a single distinguished
`fwType` value through the round-trip.

The F# surface lives at `src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs` (T013);
the FsCheck round-trip property lives at `tests/.../Property/Can/
WhoIAmFrameProperties.fs` (T022); the fixture-driven parse tests live at
`tests/.../Unit/Can/WhoIAmFrameFixtureTests.fs` (T033 parse-side; variant
assertions land in commit 3). All four ride into the tree as one vertical
PR-B commit.

Constitution Principle I: no `sorry`, no custom axioms beyond what closed-
inductive `cases` introduces. The proof is `cases f; simp [encode, parse]`.

The Lean model uses an abstract `UInt32`-equivalent triple for the UUID;
encoding writes those three words and parse reads them back, so the round-
trip property reduces to `f = f` and the proof is purely definitional.
The byte-level big-endian encoding handled by the F# `BinaryPrimitives`
calls is opaque to this Lean model — the round-trip property holds at the
record level, where `encode` is a bijection on its image and `parse` is
its left inverse on that image.
-/

namespace Stem.ButtonPanelTester.Phase2

/-! ## PanelUuid

Three-word UUID record mirroring the F# `PanelUuid` DU in
`src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs` (T013). The single-field
record is structurally identical to the F# single-case DU — both wrap a
three-word `UInt32` payload.
-/

structure PanelUuid where
  uuid0 : Nat
  uuid1 : Nat
  uuid2 : Nat
  deriving DecidableEq, Repr

/-! ## WhoIAmFrame

Lean model of the parsed payload. `machineType` is abstract over `Nat`
(the F# side carries a `byte` wrapped in `MachineTypeByte`); `fwType` is
abstract too — the round-trip theorem below pins it to the wire-contract
constant `4` (= `0x04`). The `Uuid` field carries the three-word UUID.
-/

structure WhoIAmFrame where
  machineType : Nat
  fwType : Nat
  uuid : PanelUuid
  deriving DecidableEq, Repr

/-! ## encode

Wire encoder shape: the payload "is" the tuple
`(machineType, fwType, uuid0, uuid1, uuid2)`. Byte-level layout (offsets,
big-endian) is the F# side's concern; the Lean side carries the algebraic
shape so the round-trip property has a `parse` to invert against.
-/

structure WirePayload where
  machineTypeByte : Nat
  fwTypeByte : Nat
  uuid0Word : Nat
  uuid1Word : Nat
  uuid2Word : Nat
  deriving DecidableEq, Repr

def encode (f : WhoIAmFrame) : WirePayload :=
  { machineTypeByte := f.machineType
    fwTypeByte := f.fwType
    uuid0Word := f.uuid.uuid0
    uuid1Word := f.uuid.uuid1
    uuid2Word := f.uuid.uuid2 }

/-! ## parse

Wire decoder shape: succeed iff `fwTypeByte = 4` (the FR-013 silent-drop
predicate for `fwType ≠ 0x04`). Length is implicit in the `WirePayload`
record — at the byte level the F# parser rejects payloads of length ≠ 15,
which has no analogue at the Lean record level because the record always
carries exactly the five fields. The byte-length silent drop is exercised
by the FsCheck `WhoIAmFrameRejectsWrongLength` property (T022), not here.
-/

def parse (p : WirePayload) : Option WhoIAmFrame :=
  if p.fwTypeByte = 4 then
    some
      { machineType := p.machineTypeByte
        fwType := p.fwTypeByte
        uuid :=
          { uuid0 := p.uuid0Word
            uuid1 := p.uuid1Word
            uuid2 := p.uuid2Word } }
  else
    none

/-! ## parse_encode_roundtrip (data-model.md §2.3)

Round-trip property: for every well-formed `WhoIAmFrame` (i.e. `fwType = 4`),
`parse (encode f) = some f`. The proof is purely definitional:
`encode` is the trivial bijection from `WhoIAmFrame` onto its image inside
`WirePayload`, and `parse` is its left inverse on that image (`fwType = 4`
keeps the guard's `then` branch).

The "well-formed" precondition is baked into the statement by carrying the
literal `4` in the constructor's `fwType` field, rather than as a hypothesis
about an abstract `f`. This sidesteps a Lean elaboration wart where
`simp` does not see through the record-projection of a destructured
hypothesis; the literal form keeps the proof a one-step `rfl` once the
inner UUID is destructured.
-/

theorem parse_encode_roundtrip (machineType : Nat) (uuid : PanelUuid) :
    let frame : WhoIAmFrame :=
      { machineType := machineType, fwType := 4, uuid := uuid }
    parse (encode frame) = some frame := by
  cases uuid with
  | mk u0 u1 u2 => rfl

end Stem.ButtonPanelTester.Phase2
