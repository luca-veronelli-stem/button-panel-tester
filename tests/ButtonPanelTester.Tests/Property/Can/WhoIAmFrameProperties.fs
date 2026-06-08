module Stem.ButtonPanelTester.Tests.Property.Can.WhoIAmFrameProperties

open System
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// FsCheck property covering the WHO_I_AM round-trip contract in
/// `specs/003-panel-discovery/contracts/who-i-am-wire-format.md`
/// §Parse contract: `parse (encode f) = Some f` for every
/// `WhoIAmFrame`. The corrected codec rejects only on length (FR-007),
/// so the round-trip holds for an arbitrary `fwType` — there is no
/// well-formedness gate on the firmware/hardware-variant field.
///
/// The frame is built from FsCheck-arbitrary `machineType`, `fwType`,
/// and UUID primitives. The Lean theorem `parse_encode_roundtrip` in
/// `Phase2/WhoIAmFrame.lean` (T002) mechanises the same invariant at
/// the type level.
[<Property>]
let WhoIAmFrameRoundtrip (machineType: byte) (fwType: uint16) (u0: uint32) (u1: uint32) (u2: uint32) =
    let frame =
        { MachineType = MachineTypeByte machineType
          FwType = FwType fwType
          Uuid = PanelUuid(u0, u1, u2) }

    let encoded = WhoIAmFrame.encode frame
    let parsed = WhoIAmFrame.parse(ReadOnlyMemory encoded)
    parsed = Some frame

/// FsCheck property covering FR-007 silent-drop on length mismatch.
/// Any byte buffer whose length is not exactly the 15-byte wire size
/// must parse to `None`. The generator filters out length-15 buffers
/// via the `==>` implication operator so the property is vacuously
/// true for the (rare) generated buffers that happen to be 15 bytes
/// long.
[<Property>]
let WhoIAmFrameRejectsWrongLength (raw: byte[]) =
    raw.Length <> 15
    ==> (WhoIAmFrame.parse(ReadOnlyMemory raw) = None)
