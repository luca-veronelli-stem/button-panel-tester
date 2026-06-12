module Stem.ButtonPanelTester.Tests.Property.Can.WhoAreYouFrameProperties

open System
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// FsCheck property covering the WHO_ARE_YOU round-trip contract in
/// `specs/004-baptism-workflow/contracts/master-sequence-wire-format.md`
/// §"WHO_ARE_YOU app payload (4 B)": `parse (encode f) = Some f` for
/// every `WhoAreYouFrame` — no well-formedness precondition; the codec
/// rejects only on length (house codec style). The frame is built from
/// FsCheck-arbitrary `machineType`, `fwType`, and `reset` primitives;
/// the feature always sends `Reset = true`, but the codec models both
/// polarities. The Lean theorem `parse_encode_roundtrip` in
/// `Phase3/WhoAreYouFrame.lean` (T002) mechanises the same invariant
/// at the type level.
[<Property>]
let WhoAreYouFrameRoundtrip (machineType: byte) (fwType: uint16) (reset: bool) =
    let frame: WhoAreYouFrame =
        { MachineType = machineType
          FwType = fwType
          Reset = reset }

    let encoded = WhoAreYouFrame.encode frame
    let parsed = WhoAreYouFrame.parse(ReadOnlyMemory encoded)
    parsed = Some frame

/// FsCheck property covering the length-only rejection axis: any byte
/// buffer whose length is not exactly the 4-byte wire size must parse
/// to `None`. The generator filters out length-4 buffers via the `==>`
/// implication operator so the property is vacuously true for the
/// (rare) generated buffers that happen to be 4 bytes long. Pairs with
/// the Lean theorem `encode_length` in `Phase3/WhoAreYouFrame.lean`
/// (T002), which pins the encode side to exactly 4 bytes.
[<Property>]
let WhoAreYouFrameRejectsWrongLength (raw: byte[]) =
    raw.Length <> 4
    ==> (WhoAreYouFrame.parse(ReadOnlyMemory raw) = None)
