module Stem.ButtonPanelTester.Tests.Property.Can.SetAddressFrameProperties

open System
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// FsCheck property covering the SET_ADDRESS frame-direction
/// round-trip contract in
/// `specs/004-baptism-workflow/contracts/master-sequence-wire-format.md`
/// §"SET_ADDRESS app payload (16 B)": `parse (encode f) = Some f` for
/// every `SetAddressFrame` — no well-formedness precondition; the
/// codec rejects only on length (house codec style). The frame is
/// built from FsCheck-arbitrary UUID words and SP_Address. The Lean
/// theorem `parse_encode_roundtrip` in `Phase3/SetAddressFrame.lean`
/// (T003) mechanises the same invariant at the type level.
[<Property>]
let SetAddressFrameRoundtrip (u0: uint32) (u1: uint32) (u2: uint32) (spAddress: uint32) =
    let frame: SetAddressFrame =
        { Uuid = PanelUuid(u0, u1, u2)
          SpAddress = spAddress }

    let encoded = SetAddressFrame.encode frame
    let parsed = SetAddressFrame.parse(ReadOnlyMemory encoded)
    parsed = Some frame

/// Single-case wrapper carrying an exactly-16-byte payload, so the
/// byte-echo property below quantifies over the codec's full accepted
/// input domain instead of relying on `==>` to filter rare hits.
type SixteenBytes = SixteenBytes of byte[]

/// FsCheck `Arbitrary` container — passed to `[<Property>]` via
/// `Arbitrary = [| typeof<SixteenBytesArb> |]`. Generates exactly-16-
/// byte arrays from the default byte generator (mirrors the
/// `FetchOutcomeArb` custom-Arbitrary pattern in
/// `CacheConsistencyTests.fs`).
type SixteenBytesArb =
    static member SixteenBytes() : Arbitrary<SixteenBytes> =
        ArbMap.defaults.ArbFor<byte>().Generator
        |> Gen.arrayOfLength 16
        |> Gen.map SixteenBytes
        |> Arb.fromGen

/// FsCheck property covering the contract's NORMATIVE byte-echo
/// invariant in the BYTE direction (contract §"SET_ADDRESS app
/// payload (16 B)"): re-encoding a parsed 16-byte payload reproduces
/// the original bytes verbatim — `parse b |> Option.map encode =
/// Some b`. This is what guarantees the UUID bytes the tool echoes
/// into SET_ADDRESS `[0..11]` are byte-for-byte the announced
/// WHO_I_AM bytes. The Lean theorem `encode_parse_roundtrip` (the
/// byte-echo theorem) in `Phase3/SetAddressFrame.lean` (T003)
/// mechanises the same invariant.
[<Property(Arbitrary = [| typeof<SixteenBytesArb> |])>]
let SetAddressFrameByteEcho (SixteenBytes payload) =
    let reEncoded =
        SetAddressFrame.parse(ReadOnlyMemory payload)
        |> Option.map SetAddressFrame.encode

    reEncoded = Some payload

/// FsCheck property covering the length-only rejection axis: any byte
/// buffer whose length is not exactly the 16-byte wire size must
/// parse to `None`. The generator filters out length-16 buffers via
/// the `==>` implication operator so the property is vacuously true
/// for the (rare) generated buffers that happen to be 16 bytes long.
/// Pairs with the Lean theorem `encode_length` in
/// `Phase3/SetAddressFrame.lean` (T003), which pins the encode side
/// to exactly 16 bytes.
[<Property>]
let SetAddressFrameRejectsWrongLength (raw: byte[]) =
    raw.Length <> 16
    ==> (SetAddressFrame.parse(ReadOnlyMemory raw) = None)

/// End-to-end echo against the SHIPPED WHO_I_AM RX parser (contract
/// §"SET_ADDRESS app payload (16 B)" byte-echo invariant, NORMATIVE;
/// research R1; Lean `encode_parse_roundtrip`, T003): encode an
/// arbitrary `WhoIAmFrame` (a valid 15-byte WHO_I_AM payload by
/// construction), parse it back with the shipped `WhoIAmFrame.parse`,
/// build the SET_ADDRESS frame from the PARSED `Uuid`, and assert
/// the SET_ADDRESS bytes `[0..11]` equal the WHO_I_AM bytes `[3..14]`
/// verbatim — so the slave's word-equality acceptance check compares
/// identical byte sequences on both sides, independent of endianness
/// labeling.
[<Property>]
let SetAddressEchoesAnnouncedUuidBytes
    (machineType: byte)
    (fwType: uint16)
    (u0: uint32)
    (u1: uint32)
    (u2: uint32)
    (spAddress: uint32)
    =
    let announced: WhoIAmFrame =
        { MachineType = MachineTypeByte machineType
          FwType = FwType fwType
          Uuid = PanelUuid(u0, u1, u2) }

    let whoIAmBytes = WhoIAmFrame.encode announced

    match WhoIAmFrame.parse(ReadOnlyMemory whoIAmBytes) with
    | None -> false
    | Some parsed ->
        let setAddress: SetAddressFrame =
            { Uuid = parsed.Uuid
              SpAddress = spAddress }

        let setAddressBytes = SetAddressFrame.encode setAddress
        setAddressBytes.Length = 16 && setAddressBytes[0..11] = whoIAmBytes[3..14]
