module Stem.ButtonPanelTester.Tests.Property.Can.WhoIAmFrameProperties

open System
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// FsCheck property covering the WHO_I_AM round-trip contract in
/// `specs/003-panel-discovery/contracts/who-i-am-wire-format.md`
/// §Parse contract: `parse (encode f) = Some f` for every well-formed
/// `WhoIAmFrame`. "Well-formed" here means `fwType = 0x04` — the wire
/// rule that distinguishes button-panel announcements from other
/// auto-address slaves. Frames with any other `fwType` are silently
/// dropped on `parse` (FR-013), so they cannot round-trip.
///
/// The frame is built from FsCheck-arbitrary `MachineTypeByte` + UUID
/// tuples and a fixed `fwType = 0x04`. The Lean theorem
/// `parse_encode_roundtrip` in `Phase2/WhoIAmFrame.lean` (T028)
/// mechanises the same invariant at the type level.
[<Property>]
let WhoIAmFrameRoundtrip (machineType: byte) (u0: uint32) (u1: uint32) (u2: uint32) =
    let frame =
        { MachineType = MachineTypeByte machineType
          FwType = FwType 0x04uy
          Uuid = PanelUuid(u0, u1, u2) }

    let encoded = WhoIAmFrame.encode frame
    let parsed = WhoIAmFrame.parse(ReadOnlyMemory encoded)
    parsed = Some frame

/// FsCheck property covering FR-013 silent-drop on length mismatch.
/// Any byte buffer whose length is not exactly the 15-byte wire size
/// must parse to `None`. The generator filters out length-15 buffers
/// via the `==>` implication operator so the property is vacuously
/// true for the (rare) generated buffers that happen to be 15 bytes
/// long. Buffers of length 15 that don't match the wire layout are
/// covered by `WhoIAmFrameRejectsMalformedFwType` below.
[<Property>]
let WhoIAmFrameRejectsWrongLength (raw: byte[]) =
    raw.Length <> 15
    ==> (WhoIAmFrame.parse(ReadOnlyMemory raw) = None)

/// FsCheck property covering FR-013 silent-drop on `fwType` mismatch.
/// A 15-byte buffer whose `fwType` byte (offset 1) is anything other
/// than `0x04` must parse to `None`, per the wire contract's rule 2.
/// The byte at offset 0 (`machineType`) and the 12 UUID bytes are
/// generated freely — only `fwType` is constrained.
[<Property>]
let WhoIAmFrameRejectsMalformedFwType
    (machineType: byte)
    (fwType: byte)
    (uuidBytes: byte[])
    =
    let precondition = fwType <> 0x04uy && uuidBytes.Length >= 13

    let outcome () =
        let payload = Array.zeroCreate 15
        payload[0] <- machineType
        payload[1] <- fwType

        for i in 0..11 do
            payload[2 + i] <- uuidBytes[i]

        payload[14] <- uuidBytes[12]
        WhoIAmFrame.parse(ReadOnlyMemory payload) = None

    precondition ==> lazy outcome ()
