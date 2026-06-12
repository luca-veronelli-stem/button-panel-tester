namespace Stem.ButtonPanelTester.Core.Can

open System
open System.Buffers.Binary

/// WHO_ARE_YOU TX app payload (4 bytes), per
/// `specs/004-baptism-workflow/contracts/master-sequence-wire-format.md`
/// §"WHO_ARE_YOU app payload (4 B)" and
/// `specs/004-baptism-workflow/data-model.md` §2.1. The wire layout is
/// fully packed:
///   offset 0:    `machineType` (UInt8) — chosen variant's identity
///                byte (claim) or the `0xFF` virgin marker (reset)
///   offsets 1-2: `fwType` (big-endian UInt16) — the slave acts only
///                on a matching fwType
///   offset 3:    reset flag (`0x01`/`0x00`; non-zero = set)
/// `FwType` here is the plain `uint16` wire field — deliberately NOT
/// the WHO_I_AM RX side's single-case `FwType` wrapper type.
/// This feature always sends `Reset = true` (FR-003, FR-008); the
/// codec models both polarities so the round-trip is total.
///
/// Round-trips with `encode`: `parse (encode f) = Some f` for every
/// `WhoAreYouFrame`. The Lean theorem `parse_encode_roundtrip` in
/// `lean/Stem/ButtonPanelTester/Phase3/WhoAreYouFrame.lean` (T002)
/// mechanises this invariant.
type WhoAreYouFrame =
    { MachineType: byte
      FwType: uint16
      Reset: bool }

module WhoAreYouFrame =

    /// Wire layout is fixed at 4 bytes per the contract §"WHO_ARE_YOU
    /// app payload (4 B)". Any other length parses to `None` — length
    /// is the ONLY rejection axis (house codec style). The Lean
    /// theorem `encode_length` (T002) mechanises the encode side.
    let private wireLength = 4

    /// Decode a 4-byte WHO_ARE_YOU payload, mirroring the slave's
    /// parse (`AutoAddressSlave.c:230-235`, contract §"WHO_ARE_YOU app
    /// payload (4 B)"):
    ///   1. length-check (≠ 4 → `None`) — the only rejection path;
    ///   2. `machineType` at offset 0 (any byte accepted);
    ///   3. `fwType` as a big-endian `UInt16` at offsets 1-2;
    ///   4. reset flag at offset 3 — any non-zero byte means set.
    /// FsCheck mirrors: `WhoAreYouFrameRoundtrip` and
    /// `WhoAreYouFrameRejectsWrongLength` in
    /// `Tests/Property/Can/WhoAreYouFrameProperties.fs` (T008).
    let parse (payload: ReadOnlyMemory<byte>) : WhoAreYouFrame option =
        if payload.Length <> wireLength then
            None
        else
            let span = payload.Span

            Some
                { MachineType = span[0]
                  FwType = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(1, 2))
                  Reset = span[3] <> 0uy }

    /// Encode a `WhoAreYouFrame` to its 4-byte wire payload, per the
    /// contract §"WHO_ARE_YOU app payload (4 B)": `[0]` machineType,
    /// `[1..2]` big-endian fwType, `[3]` reset flag as `0x01`/`0x00`.
    /// Left-inverse of `parse` for every frame:
    /// `parse (encode f) = Some f` (Lean `parse_encode_roundtrip`,
    /// T002); always exactly 4 bytes (Lean `encode_length`, T002);
    /// FsCheck mirror `WhoAreYouFrameRoundtrip` (T008).
    let encode (frame: WhoAreYouFrame) : byte[] =
        let buffer = Array.zeroCreate wireLength
        buffer[0] <- frame.MachineType
        BinaryPrimitives.WriteUInt16BigEndian(Span(buffer, 1, 2), frame.FwType)
        buffer[3] <- if frame.Reset then 0x01uy else 0x00uy
        buffer
