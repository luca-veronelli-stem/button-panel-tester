/-
T044 + T055 — Lean Phase-4 module for the button-state observer's accept rule:
the machineType-word variant decode (T044, fix #270) and the completed-packet
accept rule that composes with it (T055, fix #296).

A baptized panel is silent on the WHO_I_AM auto-address broadcast
(`AAS_STAND_BY`; `docs/Context/bpt-rollout/CORRECTIONS.md` §C1) and instead
heartbeats its button-state `VAR_WRITE` **addressed to the master that baptized
it**: the CAN arbitration ID is the DESTINATION — the stored
`MotherBoardAddress` (`UserMain.c:997`; written from the baptizing master's
srid, `AutoAddressSlave.c:238-241`). For a panel baptized by this tool that is
the tool's own SRID `0x00000008`. The panel's OWN SP address rides in the
reassembled transport packet's **senderId** (bytes 1-4, big-endian), and that
word carries the `SP_App_Calculate_ID` layout: `network <<< 24 |||
machineType <<< 16 ||| (fwType &&& 0x3FF) <<< 6 ||| board`. The machineType
byte is therefore **bits 23-16 of the senderId**, not of the arbitration ID
(bench witness `bench-logs/pcan/test1.trc`, 2026-07-23: all heartbeats on
arbitration ID `0x00000008`, senderId `0x000A0101` -> Marketing OptimusXp).

This module mechanises the wire-format contract section "Destination addressing
+ variant-from-senderId match rule (fix #296)"
(`specs/005-button-press-test/contracts/button-state-wire-format.md`) at two
levels:

  * **Word level** (T044 — unchanged by #296, now applied to the senderId
    word): `machine_type_at_bits_23_16` — extracting `(word >>> 16) &&& 0xFF`
    from the `network<<24 | machineType<<16 | rest` layout recovers exactly the
    `machineType` field; `machineTypeByte_eq_div_mod` bridges the bitwise
    expression to the `/ 0x10000 % 0x100` arithmetic the proof reasons over;
    `variantOfDirectedId` / `accepted` are the machineType-word decode the
    packet-level rule composes with.
  * **Packet level** (T055): `ButtonStatePacket` models a completed reassembled
    packet INCLUDING its arbitration id; `packetAccepted` is the observer's
    accept rule — cmd `0x0002`, recognised addr, senderId decodes `Marketing` —
    with NO arbitration-ID pre-filter. Theorems `variant_from_sender_id`,
    `who_i_am_rejected_on_cmd`, `virgin_sentinel_rejected`, and
    `arbitration_id_irrelevant`, plus the test1.trc witness
    `tool_baptized_heartbeat_accepted`.

Reuses the Phase-2 `decodeVariant` / `VariantIdentity` (the same total decoder
the F# `PanelObservation.VariantDecoder.decode` implements) so the variant
classification is shared, not re-modelled.

The F# surface lives at `src/ButtonPanelTester.Core/Can/ButtonStateObservation.fs`
(T045; T056 re-keys the observer to the senderId, adding `variantOfSenderId`
whose XML docs and FsCheck properties cite the T055 theorems by name). This
Lean re-statement lands in commit group K1 (T055), ahead of the F# re-key in
K2 (T056), per Constitution Principle I (Lean spec -> test -> impl).

Constitution Principle I: no `sorry`, no custom axioms.
-/

import Stem.ButtonPanelTester.Phase2.PanelObservation

namespace Stem.ButtonPanelTester.Phase4

open Stem.ButtonPanelTester.Phase2

/-! ## machineTypeByte

Extract the machineType byte from a word carrying the `SP_App_Calculate_ID`
layout — bits 23-16, the **same bitwise expression** the F# observer evaluates:
`(word >>> 16) &&& 0xFFu`. Under #270 that word was taken to be the frame's CAN
arbitration id; since #296 it is the completed packet's **senderId** (the
arbitration id is the destination). `Nat` stands in for `UInt32` (the Phase-2
modelling convention; `decodeVariant` already takes a `Nat`).
-/

def machineTypeByte (canId : Nat) : Nat := (canId >>> 16) &&& 0xFF

/-! ## variantOfDirectedId

The variant identity a word in the `SP_App_Calculate_ID` layout decodes to:
extract the machineType byte, then run the shared Phase-2 total decoder.
Mirrors the F# `variantOfDirectedId` (T045). Since #296 the observer applies
this decode to the completed packet's senderId word (`packetVariant` below;
the F# side grows the senderId-keyed `variantOfSenderId` in T056) — the
extraction itself is unchanged.
-/

def variantOfDirectedId (canId : Nat) : VariantIdentity :=
  decodeVariant (machineTypeByte canId)

/-! ## accepted

The machineType-WORD decode predicate: a word in the `SP_App_Calculate_ID`
layout passes **iff** its machineType byte decodes to a known `Marketing`
variant. Under #270 this was read as the observer's whole accept rule, applied
to the arbitration id; since #296 it is the word-level building block the
packet-level `packetAccepted` composes with, applied to the completed packet's
**senderId** word. Wildcard-free over the three `VariantIdentity` shapes (a
fourth shape would break this match AND the F# observer's). -/

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

The machineType field occupies bits 23-16 of an `SP_App_Calculate_ID` word:
extracting `(word >>> 16) &&& 0xFF` from the `network <<< 24 ||| machineType <<<
16 ||| rest` layout (the lower `rest < 2^16` carries `(fwType&0x3FF)<<6 | board`)
recovers exactly the `machineType` byte. Stated over the arithmetic composition
`network * 2^24 + machineType * 2^16 + rest` — equal to the bit-OR layout because
the three fields occupy disjoint bit ranges — so the extraction is the linear
fact `omega` settles after the bitwise bridge. This is the wire fact the
observer's accept rule rests on. The T044 lemma is unchanged by #296 — only its
input word moved: it now applies to the packet's senderId, which carries the
same layout (`variant_from_sender_id` is its packet-level instantiation). -/

theorem machine_type_at_bits_23_16 (network machineType rest : Nat)
    (hmt : machineType < 0x100) (hrest : rest < 0x10000) :
    machineTypeByte (network * 0x1000000 + machineType * 0x10000 + rest) = machineType := by
  rw [machineTypeByte_eq_div_mod]
  omega

/-! ## non_marketing_ids_rejected (research §R1; FR-001)

Word-level rejection facts: the broadcast id `0x1FFFFFFF` (machineType `0xFF` ->
`Virgin`) and the tool's own SRID `0x00000008` (machineType `0x00` ->
`Unknown 0`) decode to non-`Marketing` identities, so the word decode rejects
them with no special-casing. (Since #296 the tool *listens* on arbitration id
`0x00000008` — these remain word-decode facts; the packet-level drops of
WHO_I_AM and virgin traffic are `who_i_am_rejected_on_cmd` and
`virgin_sentinel_rejected` below.) -/

theorem non_marketing_ids_rejected :
    accepted 0x1FFFFFFF = false ∧ accepted 0x00000008 = false := by
  decide

/-! ## optimus_directed_id_accepted (June 2026 ground truth, machine-baptized panels)

Concrete word-level decode witnesses from the June machine-baptized captures:
`0x000A0441` -> `Marketing OptimusXp`, `0x00030141` -> `Marketing EdenXp`,
`0x000B0481` -> `Marketing R3LXp`. Under #270 these were read as the panels'
own directed *arbitration* ids; #296 showed they are the baptizing machine
masters' addresses, which coincidentally share the machineType byte with their
keyboards — the decode facts stay true, now understood as machineType-word
decodes (the same extraction `packetVariant` applies to the senderId). -/

theorem optimus_directed_id_accepted :
    variantOfDirectedId 0x000A0441 = .marketing .optimusXp
  ∧ variantOfDirectedId 0x00030141 = .marketing .edenXp
  ∧ variantOfDirectedId 0x000B0481 = .marketing .r3LXp :=
  ⟨rfl, rfl, rfl⟩

/-! ## ButtonStatePacket (#296)

A completed, reassembled button-state transport packet as the observer sees it:
the source **arbitration id** the chunks arrived on (the DESTINATION address —
modelled as a field precisely so `arbitration_id_irrelevant` is a non-vacuous
statement about the rule), the 16-bit command (reassembled bytes 7-8), the
16-bit variable address (bytes 9-10), and the 32-bit **senderId** (bytes 1-4,
big-endian) carrying the panel's own SP address. `Nat` fields per the Phase-2
modelling convention. -/

structure ButtonStatePacket where
  arbitrationId : Nat
  cmd : Nat
  addr : Nat
  senderId : Nat

/-! ## recognisedAddr

The recognised button-state variable-address set `{0x8000, 0x803E}` (wire-format
§App-layer payload). The virgin sentinel `0x80FE` is deliberately NOT in the
set — an unbaptized panel's heartbeat must never be treated as a test result. -/

def recognisedAddr (addr : Nat) : Bool :=
  addr == 0x8000 || addr == 0x803E

/-! ## packetAccepted / packetVariant (wire-format §Destination addressing, #296)

The observer's packet-level accept rule:

    accept <-> cmd = 0x0002 AND addr ∈ {0x8000, 0x803E}
               AND machineType(senderId) decodes to Marketing _

composed from the word-level `accepted` applied to the **senderId** — the
arbitration id is never consulted (there is NO arbitration-ID pre-filter; chunk
reassembly stays per source arbitration id upstream of this rule).
`packetVariant` is the senderId decode an accepted observation carries. -/

def packetAccepted (p : ButtonStatePacket) : Bool :=
  p.cmd == 0x0002 && recognisedAddr p.addr && accepted p.senderId

def packetVariant (p : ButtonStatePacket) : VariantIdentity :=
  variantOfDirectedId p.senderId

/-! ## variant_from_sender_id (#296; FR-001)

The bits-23-16 extraction applied to the SENDERID word: for a packet whose
senderId carries the `network<<24 | machineType<<16 | rest` layout, the derived
variant is exactly the decode of the `machineType` field — the T044
`machine_type_at_bits_23_16` extraction lemma instantiated at the senderId.
T056's F# `variantOfSenderId` XML docs and FsCheck property cite this theorem
by name. -/

theorem variant_from_sender_id (arb cmd addr network machineType rest : Nat)
    (hmt : machineType < 0x100) (hrest : rest < 0x10000) :
    packetVariant ⟨arb, cmd, addr, network * 0x1000000 + machineType * 0x10000 + rest⟩
      = decodeVariant machineType :=
  congrArg decodeVariant (machine_type_at_bits_23_16 network machineType rest hmt hrest)

/-! ## who_i_am_rejected_on_cmd (#296; FR-001)

A WHO_I_AM broadcast reassembles with cmd `0x0024`, not the button-state
`VAR_WRITE` `0x0002`, and is never accepted — for EVERY senderId, arbitration
id, and variable address. The command filter alone drops it; no arbitration-ID
knowledge is needed. -/

theorem who_i_am_rejected_on_cmd (arb addr senderId : Nat) :
    packetAccepted ⟨arb, 0x0024, addr, senderId⟩ = false :=
  rfl

/-! ## virgin_sentinel_rejected (#296; FR-001)

A packet carrying the virgin sentinel variable address `0x80FE` (unbaptized
panel) is never accepted — for EVERY cmd, senderId, and arbitration id: the
sentinel is outside the recognised address set, so the addr filter drops it
regardless of the other fields. -/

theorem virgin_sentinel_rejected (arb cmd senderId : Nat) :
    packetAccepted ⟨arb, cmd, 0x80FE, senderId⟩ = false := by
  simp [packetAccepted, recognisedAddr]

/-! ## arbitration_id_irrelevant (#296; FR-001; analyzer M3)

Changing ONLY the arbitration id — the destination the baptizing master chose —
changes neither acceptance nor the derived variant. The packet model carries
the arbitration id as a field precisely so this invariance is a statement about
the rule rather than vacuously true by omission: the same heartbeat is accepted
identically whether it arrives on the tool's SRID `0x00000008` (tool-baptized)
or on a machine master's address (machine-baptized). T056's FsCheck property
generates arbitrary arbitration ids against this theorem. -/

theorem arbitration_id_irrelevant (arb arb' cmd addr senderId : Nat) :
    packetAccepted ⟨arb, cmd, addr, senderId⟩ = packetAccepted ⟨arb', cmd, addr, senderId⟩
  ∧ packetVariant ⟨arb, cmd, addr, senderId⟩ = packetVariant ⟨arb', cmd, addr, senderId⟩ :=
  ⟨rfl, rfl⟩

/-! ## tool_baptized_heartbeat_accepted (bench ground truth, test1.trc 2026-07-23)

The first tool-baptized capture as a packet-level witness: a heartbeat arriving
on the tool's own SRID `0x00000008` with senderId `0x000A0101` (machineType
`0x0A`) is accepted and derives `Marketing OptimusXp` — exactly the frame shape
the #270 arbitration-ID rule dropped. -/

theorem tool_baptized_heartbeat_accepted :
    packetAccepted ⟨0x00000008, 0x0002, 0x8000, 0x000A0101⟩ = true
  ∧ packetVariant ⟨0x00000008, 0x0002, 0x8000, 0x000A0101⟩ = .marketing .optimusXp :=
  ⟨rfl, rfl⟩

end Stem.ButtonPanelTester.Phase4
