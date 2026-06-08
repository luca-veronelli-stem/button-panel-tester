/-
T002 — Lean Phase-2 module for `WhoIAmFrame`.

Mechanises the round-trip invariant from `specs/003-panel-discovery/
data-model.md` §1.3: `parse (encode f) = some f` for every `WhoIAmFrame`
with no well-formedness precondition. `parse` is total — length is the only
wire-level rejection axis (FR-007 silent drop), and it has no record-level
analogue because the `WirePayload` record always carries exactly its five
fields. `fwType` is read straight through as the wire `Nat`: informational
panel-variant metadata that never gates acceptance.

The F# surface lives at `src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs` (T007);
the FsCheck round-trip property lives at `tests/.../Property/Can/
WhoIAmFrameProperties.fs` (T005); the fixture-driven parse tests live at
`tests/.../Unit/Can/WhoIAmFrameFixtureTests.fs` (T006). This Lean re-statement
lands in commit group A1; the F# surface (T007), FsCheck properties (T005), and
fixtures (T006) land together in A2.

Constitution Principle I: no `sorry`, no custom axioms — the proof depends on no
axioms at all. The proof is a direct definitional `rfl`: structure eta closes
`parse (encode f) = some f` once `encode` and `parse` unfold.

The Lean model uses an abstract `UInt32`-equivalent triple for the UUID;
encoding writes those three words and parse reads them back, so the round-
trip property reduces to `f = f` and the proof is purely definitional.
The byte-level big-endian encoding handled by the F# `BinaryPrimitives`
calls is opaque to this Lean model — the round-trip property holds at the
record level, where `encode` is a bijection on its image and `parse` is
its total left inverse on that image.
-/

namespace Stem.ButtonPanelTester.Phase2

/-! ## PanelUuid

Three-word UUID record mirroring the F# `PanelUuid` DU in
`src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs` (T007). The single-field
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
abstract too — modelled as the wire `Nat` with no guard (informational
metadata; never gates acceptance). The `Uuid` field carries the three-word UUID.
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

Wire decoder shape: total — every `WirePayload` decodes to `some WhoIAmFrame`,
reading `fwTypeByte` straight through as informational metadata with no guard.
Length is the only wire-level rejection axis (FR-007 silent drop): at the byte
level the F# parser rejects payloads of length ≠ 15, which has no analogue at the
Lean record level because the record always carries exactly the five fields. The
byte-length silent drop is exercised by the FsCheck
`WhoIAmFrameRejectsWrongLength` property (T005), not here.
-/

def parse (p : WirePayload) : Option WhoIAmFrame :=
  some
    { machineType := p.machineTypeByte
      fwType := p.fwTypeByte
      uuid :=
        { uuid0 := p.uuid0Word
          uuid1 := p.uuid1Word
          uuid2 := p.uuid2Word } }

/-! ## parse_encode_roundtrip (data-model.md §1.3)

Round-trip property: for every `WhoIAmFrame` (no well-formedness precondition),
`parse (encode f) = some f`. The proof is purely definitional: `encode` is the
trivial bijection from `WhoIAmFrame` onto its image inside `WirePayload`, and the
total `parse` is its left inverse on the whole type — there is no guard to keep,
so the round-trip holds for every frame.

The proof is a direct `rfl`: structure eta makes `parse (encode f)` reduce to
`some f` definitionally (the reconstructed outer record and inner UUID record are
eta-equal to `f` and `f.uuid`), so no `cases`/`simp` is needed.
-/

theorem parse_encode_roundtrip (f : WhoIAmFrame) :
    parse (encode f) = some f := by
  rfl

end Stem.ButtonPanelTester.Phase2
