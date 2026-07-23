module Stem.ButtonPanelTester.Tests.Property.Can.ButtonStateObservationProperties

open FsCheck.FSharp
open FsCheck.Xunit
open Xunit
open Stem.ButtonPanelTester.Core.Can

/// FsCheck properties mirroring the Lean theorems in
/// `Phase4/ButtonStateObservation.lean`: the machineType-word -> variant
/// extraction (T044) and its instantiation at the packet **senderId** — the word
/// the `ButtonStateReassemblyObserver` actually accepts on since fix #296, the
/// arbitration id being the destination (T055). The word-level properties below
/// are unchanged by #296: only the word they are applied to moved.

/// Mirrors `machine_type_at_bits_23_16`: extracting `(id >>> 16) &&& 0xFF` from
/// the `network<<24 | machineType<<16 | rest` layout recovers exactly the
/// `machineType` field (the three fields occupy disjoint bit ranges, so the
/// bit-OR composition equals the value at bits 23-16), and `variantOfDirectedId`
/// is that byte run through the shared `VariantDecoder.decode`.
[<Property>]
let MachineTypeAtBits2316 (network: byte) (machineType: byte) (rest: uint16) =
    let canId =
        (uint32 network <<< 24) ||| (uint32 machineType <<< 16) ||| uint32 rest

    let extracted = byte ((canId >>> 16) &&& 0xFFu)

    extracted = machineType
    && ButtonStateObservation.variantOfDirectedId canId = VariantDecoder.decode (MachineTypeByte machineType)

/// Mirrors `variant_from_sender_id` (T055): the bits-23-16 extraction applied to
/// the **senderId** word. For a senderId carrying the `SP_App_Calculate_ID`
/// layout `network<<24 | machineType<<16 | rest` — the panel's OWN address,
/// bytes 1-4 of the reassembled packet — `variantOfSenderId` is exactly the
/// decode of the `machineType` field. This is the value-level witness of the
/// rule the observer accepts on since #296; the arbitration id the heartbeat
/// arrived on is the destination and never appears here.
[<Property>]
let VariantFromSenderId (network: byte) (machineType: byte) (rest: uint16) =
    let senderId =
        (uint32 network <<< 24) ||| (uint32 machineType <<< 16) ||| uint32 rest

    ButtonStateObservation.variantOfSenderId senderId = VariantDecoder.decode (MachineTypeByte machineType)

/// Mirrors the word-level `accepted` predicate the packet rule composes with: for
/// an arbitrary word, the decoded identity is exactly determined by the
/// machineType byte at bits 23-16 — `Marketing` only for the four known
/// machineTypes, `Virgin` only for `0xFF`, `Unknown raw` carrying that very byte
/// otherwise. Establishes that no word outside `{0x03,0x0A,0x0B,0x0C}` is ever
/// accepted — applied to the senderId, that is the observer's sender gate.
[<Property>]
let VariantIsExactlyTheMachineTypeAtBits2316 (canId: uint32) =
    let machineType = byte ((canId >>> 16) &&& 0xFFu)

    match ButtonStateObservation.variantOfDirectedId canId with
    | Marketing _ -> machineType = 0x03uy || machineType = 0x0Auy || machineType = 0x0Buy || machineType = 0x0Cuy
    | Virgin -> machineType = 0xFFuy
    | Unknown raw -> raw = machineType

/// Mirrors `non_marketing_ids_rejected` at the value level: the WHO_I_AM
/// broadcast id (-> machineType 0xFF -> Virgin) and the tool's own SRID (->
/// machineType 0x00 -> Unknown) decode to non-`Marketing` identities, so a packet
/// carrying either as its SENDERID is rejected. (Since #296 the tool *listens* on
/// arbitration id `0x00000008` — these are word-decode facts about senders, not
/// about the destination.) Example-based: both are fixed wire constants, the
/// concrete witnesses the Lean theorem pins.
[<Fact>]
let NonMarketingIdsRejected () =
    Assert.Equal(Virgin, ButtonStateObservation.variantOfDirectedId 0x1FFFFFFFu)

    match ButtonStateObservation.variantOfDirectedId 0x00000008u with
    | Unknown 0x00uy -> ()
    | other -> failwithf "expected Unknown 0x00 for the tool SRID, got %A" other
