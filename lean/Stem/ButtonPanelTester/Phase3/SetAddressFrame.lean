/-
T003 — Lean Phase-3 module for `SetAddressFrame` (SET_ADDRESS TX codec).

Mechanises the 16-byte SET_ADDRESS app payload from
`specs/004-baptism-workflow/contracts/master-sequence-wire-format.md`
§"SET_ADDRESS app payload (16 B)" and `specs/004-baptism-workflow/data-model.md`
§2.2: `[0..3][4..7][8..11]` three u32 UUID words, split with the same big-endian
convention the shipped F# WhoIAmFrame parser reads, then `[12..15]` SP_Address
u32 big-endian (the slave swaps on read).

Two round-trip directions are proved. `parse_encode_roundtrip` is the house
record-level direction. `encode_parse_roundtrip` is this module's load-bearing
extra: the contract's normative BYTE-ECHO invariant (contract §SET_ADDRESS,
research R1) — the 12 UUID bytes the tool sends MUST be byte-for-byte the bytes
the panel announced in WHO_I_AM. Stated over 16 explicit bytes `< 256`:
re-encoding a parsed payload reproduces the original bytes verbatim, so the
slave's word-equality check compares identical byte sequences regardless of
endianness labeling (`encode (parse bytes) = bytes`).

The F# surface lives at `src/ButtonPanelTester.Core/Can/SetAddressFrame.fs`
(T009); the FsCheck round-trip/length/byte-echo properties live at
`tests/.../Property/Can/SetAddressFrameProperties.fs` (T011). This Lean
re-statement lands in commit group A1, ahead of the F# surface, per
Constitution Principle I (Lean spec → test → impl).

Constitution Principle I: no `sorry`, no custom axioms.
-/

namespace Stem.ButtonPanelTester.Phase3

/-! ## SetAddressFrame

Lean model of the SET_ADDRESS TX payload fields (data-model §2.2). The three
UUID words mirror Phase 2's `PanelUuid` triple (the F# side reuses that type);
they are kept as plain fields here so the byte-level codec below stays flat.
`spAddress` is the computed u32 (data-model §2.3 — the SP_Address formula
itself is F#-side scope, T010).
-/

structure SetAddressFrame where
  uuid0 : Nat
  uuid1 : Nat
  uuid2 : Nat
  spAddress : Nat
  deriving DecidableEq, Repr

namespace SetAddressFrame

/-! ## encode

Byte-level wire encoder: exactly 16 bytes, each u32 word split big-endian as
`[w / 2^24, w / 2^16 % 256, w / 2^8 % 256, w % 256]` (divisors written as
literals `16777216 / 65536 / 256` so `omega` sees them). The top byte of each
word is deliberately UNMASKED so the split is lossless for every `Nat` — the
`< 2^32` range guard is the F# `uint32` type's concern, not the model's.
-/

def encode (f : SetAddressFrame) : List Nat :=
  [ f.uuid0 / 16777216, f.uuid0 / 65536 % 256, f.uuid0 / 256 % 256, f.uuid0 % 256,
    f.uuid1 / 16777216, f.uuid1 / 65536 % 256, f.uuid1 / 256 % 256, f.uuid1 % 256,
    f.uuid2 / 16777216, f.uuid2 / 65536 % 256, f.uuid2 / 256 % 256, f.uuid2 % 256,
    f.spAddress / 16777216, f.spAddress / 65536 % 256, f.spAddress / 256 % 256,
    f.spAddress % 256 ]

/-! ## parse

Wire decoder: length is the only rejection axis (house codec style) — anything
not exactly 16 bytes parses to `none`. Each word is recombined big-endian as
`b₀ * 2^24 + b₁ * 2^16 + b₂ * 2^8 + b₃`, the same convention the shipped F#
WhoIAmFrame parser reads at WHO_I_AM positions `[3..14]`.
-/

def parse (bytes : List Nat) : Option SetAddressFrame :=
  match bytes with
  | [u00, u01, u02, u03, u10, u11, u12, u13, u20, u21, u22, u23, a0, a1, a2, a3] =>
    some
      { uuid0 := u00 * 16777216 + u01 * 65536 + u02 * 256 + u03
        uuid1 := u10 * 16777216 + u11 * 65536 + u12 * 256 + u13
        uuid2 := u20 * 16777216 + u21 * 65536 + u22 * 256 + u23
        spAddress := a0 * 16777216 + a1 * 65536 + a2 * 256 + a3 }
  | _ => none

/-! ## encode_length (data-model §2.2) -/

/-- The encoder always produces exactly the 16 wire bytes of the
SET_ADDRESS app payload (contract §"SET_ADDRESS app payload (16 B)").
Purely definitional: `encode` is a sixteen-element list literal. -/
theorem encode_length (f : SetAddressFrame) : (encode f).length = 16 := by
  rfl

/-! ## parse_encode_roundtrip (data-model §2.2) -/

/-- Round-trip property: for every `SetAddressFrame` (no well-formedness
precondition), `parse (encode f) = some f`. Each big-endian word split is
lossless on recombination.

Proof: `simp` unfolds `parse ∘ encode` to four per-word recombine-after-split
obligations of the shape `w / 2²⁴ * 2²⁴ + w / 2¹⁶ % 256 * 2¹⁶ + w / 2⁸ % 256
* 2⁸ + w % 256 = w`; `omega` closes them (div/mod by literal constants). -/
theorem parse_encode_roundtrip (f : SetAddressFrame) :
    parse (encode f) = some f := by
  cases f with
  | mk uuid0 uuid1 uuid2 spAddress =>
    simp [parse, encode] <;> omega

/-! ## encode_parse_roundtrip — the BYTE-ECHO invariant
(contract §SET_ADDRESS, research R1; data-model §2.2)

Byte-direction round-trip: parsing any well-formed 16-byte payload (every byte
`< 256`) and re-encoding it reproduces the original bytes verbatim. This is the
normative byte-echo guarantee: the UUID bytes the tool echoes into SET_ADDRESS
`[0..11]` are byte-for-byte the bytes announced in WHO_I_AM `[3..14]`, so the
slave's word-equality acceptance check compares identical byte sequences on
both sides, independent of endianness labeling. The `< 256` hypotheses are
essential for the lower three bytes of each word: an out-of-range "byte" would
alias into its neighbours on re-split. The top-byte bounds (`_h0/_h4/_h8/_h12`,
underscore = intentionally unused) are mathematically redundant — the unmasked
`/ 2²⁴` recovers the top byte at any magnitude — but are stated anyway so the
hypothesis set uniformly says "every element is a genuine byte", mirroring the
FsCheck generator's byte domain (T011).
-/

/-- Byte-echo: `(parse bytes).map encode = some bytes` for every 16-byte
payload whose elements are genuine bytes (`< 256`). -/
theorem encode_parse_roundtrip
    (b0 b1 b2 b3 b4 b5 b6 b7 b8 b9 b10 b11 b12 b13 b14 b15 : Nat)
    (_h0 : b0 < 256) (h1 : b1 < 256) (h2 : b2 < 256) (h3 : b3 < 256)
    (_h4 : b4 < 256) (h5 : b5 < 256) (h6 : b6 < 256) (h7 : b7 < 256)
    (_h8 : b8 < 256) (h9 : b9 < 256) (h10 : b10 < 256) (h11 : b11 < 256)
    (_h12 : b12 < 256) (h13 : b13 < 256) (h14 : b14 < 256) (h15 : b15 < 256) :
    (parse [b0, b1, b2, b3, b4, b5, b6, b7,
            b8, b9, b10, b11, b12, b13, b14, b15]).map encode
      = some [b0, b1, b2, b3, b4, b5, b6, b7,
              b8, b9, b10, b11, b12, b13, b14, b15] := by
  simp only [parse, encode, Option.map_some, Option.some.injEq, List.cons.injEq, and_true]
  omega

end SetAddressFrame

end Stem.ButtonPanelTester.Phase3
