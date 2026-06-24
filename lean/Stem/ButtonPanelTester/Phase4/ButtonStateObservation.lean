/-
T044 — Lean Phase-4 module for the directed-CAN-ID -> variant extraction that
keys the button-press observability re-key (fix #270).

A baptized panel is silent on the WHO_I_AM auto-address broadcast
(`AAS_STAND_BY`; `docs/Context/bpt-rollout/CORRECTIONS.md` §C1) and instead
heartbeats its button-state `VAR_WRITE` on a **directed CAN ID** equal to its
SP_Address: `SP_App_Calculate_ID = network <<< 24 ||| machineType <<< 16 |||
(fwType &&& 0x3FF) <<< 6 ||| board` (`research.md` §R1, bench-confirmed
2026-06-24). The **machineType byte therefore lives at bits 23-16** of the CAN
ID, so the variant the panel was baptized as is recoverable from the heartbeat's
own CAN ID — no baptism plumbing, no WHO_I_AM discovery.

This module mechanises the two facts the observer's accept rule rests on:

  * `machine_type_at_bits_23_16` — extracting `(id >>> 16) &&& 0xFF` from the
    `network<<24 | machineType<<16 | rest` layout recovers exactly the
    `machineType` field (rest < 2^16, machineType < 2^8). `machineTypeByte` is
    defined with the **same bitwise expression the F# observer uses**
    (`(CanId >>> 16) &&& 0xFFu`); `machineTypeByte_eq_div_mod` bridges it to the
    `/ 0x10000 % 0x100` arithmetic the extraction proof reasons over.
  * `non_marketing_ids_rejected` — the broadcast id `0x1FFFFFFF` (machineType
    `0xFF` -> Virgin) and the tool's own SRID `0x00000008` (machineType `0x00`
    -> Unknown) decode to non-`Marketing` identities, so the observer rejects
    them for free; only a directed id whose machineType decodes to a known
    `Marketing` variant is accepted.

Reuses the Phase-2 `decodeVariant` / `VariantIdentity` (the same total decoder
the F# `PanelObservation.VariantDecoder.decode` implements) so the variant
classification is shared, not re-modelled.

The F# surface lives at `src/ButtonPanelTester.Core/Can/ButtonStateObservation.fs`
(T045, `variantOfDirectedId` reusing `VariantDecoder.decode`); the FsCheck
property `ButtonStateObservationProperties` mirroring these theorems lives at
`tests/.../Property/Can/ButtonStateObservationProperties.fs` (T045). This Lean
re-statement lands in commit group I1, ahead of the F# surface in I2, per
Constitution Principle I (Lean spec -> test -> impl).

Constitution Principle I: no `sorry`, no custom axioms.
-/

import Stem.ButtonPanelTester.Phase2.PanelObservation

namespace Stem.ButtonPanelTester.Phase4

open Stem.ButtonPanelTester.Phase2

/-! ## machineTypeByte

Extract the machineType byte from a directed SP_App CAN ID — bits 23-16, the
**same bitwise expression** the F# observer evaluates on `frame.CanId`:
`(CanId >>> 16) &&& 0xFFu`. `Nat` stands in for `UInt32` (the Phase-2 modelling
convention; `decodeVariant` already takes a `Nat`).
-/

def machineTypeByte (canId : Nat) : Nat := (canId >>> 16) &&& 0xFF

/-! ## variantOfDirectedId

The variant identity a directed CAN ID decodes to: extract the machineType byte,
then run the shared Phase-2 total decoder. Mirrors the F# `variantOfDirectedId`
(T045), which reuses `VariantDecoder.decode` on `(CanId >>> 16) &&& 0xFF`.
-/

def variantOfDirectedId (canId : Nat) : VariantIdentity :=
  decodeVariant (machineTypeByte canId)

/-! ## accepted

The observer's accept predicate: a completed button-state packet is accepted
**iff** its source CAN ID's machineType decodes to a known `Marketing` variant.
Broadcast / virgin / SRID ids decode to `virgin` / `unknown` and are rejected.
Wildcard-free over the three `VariantIdentity` shapes (a fourth shape would break
this match AND the F# observer's). -/

def accepted (canId : Nat) : Bool :=
  match variantOfDirectedId canId with
  | .marketing _ => true
  | .virgin => false
  | .unknown _ => false

/-! ## machineTypeByte_eq_div_mod

`(canId >>> 16) &&& 0xFF = canId / 0x10000 % 0x100`: the bridge from the bitwise
extraction the F# observer uses to the div/mod arithmetic the field-recovery
proof reasons over. `>>> 16` is division by `2^16`; `&&& 0xFF` (mask `2^8 - 1`)
is reduction mod `2^8`.
-/

theorem machineTypeByte_eq_div_mod (canId : Nat) :
    machineTypeByte canId = canId / 0x10000 % 0x100 := by
  unfold machineTypeByte
  rw [Nat.shiftRight_eq_div_pow, show (0xFF : Nat) = 2 ^ 8 - 1 from rfl,
    Nat.and_two_pow_sub_one_eq_mod]

/-! ## machine_type_at_bits_23_16 (research §R1; FR-001)

The machineType field occupies bits 23-16 of a directed SP_App CAN ID:
extracting `(id >>> 16) &&& 0xFF` from the `network <<< 24 ||| machineType <<<
16 ||| rest` layout (the lower `rest < 2^16` carries `(fwType&0x3FF)<<6 | board`)
recovers exactly the `machineType` byte. Stated over the arithmetic composition
`network * 2^24 + machineType * 2^16 + rest` — equal to the bit-OR layout because
the three fields occupy disjoint bit ranges — so the extraction is the linear
fact `omega` settles after the bitwise bridge. This is the wire fact the
observer's variant-from-ID accept rule rests on. -/

theorem machine_type_at_bits_23_16 (network machineType rest : Nat)
    (hmt : machineType < 0x100) (hrest : rest < 0x10000) :
    machineTypeByte (network * 0x1000000 + machineType * 0x10000 + rest) = machineType := by
  rw [machineTypeByte_eq_div_mod]
  omega

/-! ## non_marketing_ids_rejected (research §R1; FR-001)

The broadcast id `0x1FFFFFFF` (machineType `0xFF` -> `Virgin`) and the tool's own
SRID `0x00000008` (machineType `0x00` -> `Unknown 0`) decode to non-`Marketing`
identities, so the directed-ID accept rule rejects them with no special-casing —
the variant decode is the whole filter. -/

theorem non_marketing_ids_rejected :
    accepted 0x1FFFFFFF = false ∧ accepted 0x00000008 = false := by
  decide

/-! ## optimus_directed_id_accepted (bench ground truth, 2026-06-24)

A concrete witness from the bench: OPTIMUS-XP's directed heartbeat id
`0x000A0441` (machineType `0x0A`) decodes to `Marketing OptimusXp` and is
accepted; Eden-XP `0x00030141` and R-3L `0x000B0481` likewise decode to their
own marketing variants. -/

theorem optimus_directed_id_accepted :
    variantOfDirectedId 0x000A0441 = .marketing .optimusXp
  ∧ variantOfDirectedId 0x00030141 = .marketing .edenXp
  ∧ variantOfDirectedId 0x000B0481 = .marketing .r3LXp :=
  ⟨rfl, rfl, rfl⟩

end Stem.ButtonPanelTester.Phase4
