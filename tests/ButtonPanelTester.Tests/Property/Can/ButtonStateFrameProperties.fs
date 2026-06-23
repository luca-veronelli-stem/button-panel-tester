module Stem.ButtonPanelTester.Tests.Property.Can.ButtonStateFrameProperties

open System
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// FsCheck property covering the button-state round-trip contract in
/// `specs/005-button-press-test/contracts/button-state-wire-format.md`
/// §App-layer payload: `parse (encode f) = Some f` for every
/// `ButtonStateFrame`. The codec rejects only on length, so the
/// round-trip holds for an arbitrary address and bitmap — there is no
/// command/address gate on `parse` (the observer filters those, R6).
///
/// The frame is built from FsCheck-arbitrary address and bitmap
/// primitives. The Lean theorem `parse_encode_roundtrip` in
/// `Phase4/ButtonStateFrame.lean` (T002) mechanises the same invariant
/// at the type level.
[<Property>]
let ButtonStateFrameRoundtrip (address: uint16) (bitmap: byte) =
    let frame =
        { Address = VariableAddress address
          Bitmap = KeyStateBitmap bitmap }

    let encoded = ButtonStateFrame.encode frame
    let parsed = ButtonStateFrame.parse(ReadOnlyMemory encoded)
    parsed = Some frame

/// FsCheck property covering the length-only silent drop. Any byte
/// buffer whose length is not exactly the 5-byte wire size must parse to
/// `None`. The generator filters out length-5 buffers via the `==>`
/// implication operator so the property is vacuously true for the (rare)
/// generated buffers that happen to be 5 bytes long.
[<Property>]
let ButtonStateFrameRejectsWrongLength (raw: byte[]) =
    raw.Length <> 5
    ==> (ButtonStateFrame.parse(ReadOnlyMemory raw) = None)
