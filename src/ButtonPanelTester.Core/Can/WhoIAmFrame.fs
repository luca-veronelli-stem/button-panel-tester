namespace Stem.ButtonPanelTester.Core.Can

open System
open System.Buffers.Binary

/// Three-word UUID identifying a button panel, per
/// `specs/003-panel-discovery/contracts/who-i-am-wire-format.md`
/// §Payload table (offsets 3/7/11, each a big-endian `UInt32`). The DU
/// is a single-case wrapper so call sites cannot accidentally pass an
/// arbitrary `(uint32, uint32, uint32)` tuple where a `PanelUuid` is
/// expected.
type PanelUuid = PanelUuid of uuid0: uint32 * uuid1: uint32 * uuid2: uint32

/// Firmware/hardware-variant field at offsets 1-2 of a WHO_I_AM
/// payload, per `who-i-am-wire-format.md` §Payload table: the panel
/// hardware variant as a big-endian `UInt16` (`0x0004` = 12V,
/// `0x000F` = 24V). This is informational metadata only — it NEVER
/// gates acceptance (the FR-007 reject is length-only). The single-
/// case wrapper keeps the value distinguishable from other fields at
/// type level.
type FwType = FwType of uint16

/// Machine-type byte at offset 0 of a WHO_I_AM payload, per
/// `who-i-am-wire-format.md` §Payload table. Carries the raw value
/// (`0xFF` virgin, `{0x03, 0x0A, 0x0B, 0x0C}` for the four marketing
/// variants, anything else "unknown"). `decodeVariant` in
/// `PanelObservation.fs` (T014) is the total decoder; this type just
/// carries the raw byte.
type MachineTypeByte = MachineTypeByte of byte

/// Parsed WHO_I_AM payload, per `who-i-am-wire-format.md` §Payload
/// table. The 15-byte wire layout is fully packed (no padding):
///   offset 0:       `machineType` (UInt8)
///   offsets 1-2:    `fwType` (big-endian UInt16 — hardware variant)
///   offsets 3/7/11: three big-endian `UInt32` UUID words
/// (offset 14 is the low byte of the third UUID word, not padding.)
///
/// Round-trips with `encode`: `parse (encode f) = Some f` for every
/// `WhoIAmFrame`. The Lean theorem `parse_encode_roundtrip` in
/// `Phase2/WhoIAmFrame.lean` (T002) mechanises this invariant.
type WhoIAmFrame =
    { MachineType: MachineTypeByte
      FwType: FwType
      Uuid: PanelUuid }

module WhoIAmFrame =

    /// Wire layout is fixed at 15 bytes per
    /// `who-i-am-wire-format.md` §Payload. Any other length is a
    /// silent drop (FR-007) — the parser returns `None`.
    let private wireLength = 15

    /// Decode a 15-byte WHO_I_AM payload, per
    /// `who-i-am-wire-format.md` §Parse contract:
    ///   1. length-check (≠ 15 → `None`) — the ONLY rejection path;
    ///   2. accept any `machineType`;
    ///   3. read `fwType` as a big-endian `UInt16` at offsets 1-2
    ///      (informational — never gates acceptance);
    ///   4. big-endian UUID reads at offsets 3/7/11.
    /// The rejection path returns `None` silently per FR-007 — no
    /// throw, no log, no Error-state flip.
    let parse (payload: ReadOnlyMemory<byte>) : WhoIAmFrame option =
        if payload.Length <> wireLength then
            None
        else
            let span = payload.Span
            let machineType = span[0]
            let fwType = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(1, 2))
            let uuid0 = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(3, 4))
            let uuid1 = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(7, 4))
            let uuid2 = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(11, 4))

            Some
                { MachineType = MachineTypeByte machineType
                  FwType = FwType fwType
                  Uuid = PanelUuid(uuid0, uuid1, uuid2) }

    /// Encode a `WhoIAmFrame` to its 15-byte wire payload, per
    /// `who-i-am-wire-format.md` §Payload. The layout is fully packed —
    /// there is no padding; offset 14 is the low byte of the third
    /// UUID word. Left-inverse of `parse` for every frame:
    /// `parse (encode f) = Some f`.
    let encode (frame: WhoIAmFrame) : byte[] =
        let buffer = Array.zeroCreate wireLength
        let (MachineTypeByte machineType) = frame.MachineType
        let (FwType fwType) = frame.FwType
        let (PanelUuid(uuid0, uuid1, uuid2)) = frame.Uuid
        buffer[0] <- machineType
        BinaryPrimitives.WriteUInt16BigEndian(Span(buffer, 1, 2), fwType)
        BinaryPrimitives.WriteUInt32BigEndian(Span(buffer, 3, 4), uuid0)
        BinaryPrimitives.WriteUInt32BigEndian(Span(buffer, 7, 4), uuid1)
        BinaryPrimitives.WriteUInt32BigEndian(Span(buffer, 11, 4), uuid2)
        buffer
