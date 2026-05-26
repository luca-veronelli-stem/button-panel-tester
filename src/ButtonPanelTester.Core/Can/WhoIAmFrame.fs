namespace Stem.ButtonPanelTester.Core.Can

open System
open System.Buffers.Binary

/// Three-word UUID identifying a button panel, per
/// `specs/003-panel-discovery/contracts/who-i-am-wire-format.md`
/// §Payload table (offsets 2/6/10, each a big-endian `UInt32`). The DU
/// is a single-case wrapper so call sites cannot accidentally pass an
/// arbitrary `(uint32, uint32, uint32)` tuple where a `PanelUuid` is
/// expected.
type PanelUuid = PanelUuid of uuid0: uint32 * uuid1: uint32 * uuid2: uint32

/// Firmware-type byte at offset 1 of a WHO_I_AM payload, per
/// `who-i-am-wire-format.md` §Payload table. Always `0x04` for button
/// panels (per the audit in `docs/Context/bpt-rollout/CORRECTIONS.md`
/// §C1); the single-case wrapper keeps the raw byte distinguishable
/// from other byte fields at type level.
type FwType = FwType of byte

/// Machine-type byte at offset 0 of a WHO_I_AM payload, per
/// `who-i-am-wire-format.md` §Payload table. Carries the raw value
/// (`0xFF` virgin, `{0x03, 0x0A, 0x0B, 0x0C}` for the four marketing
/// variants, anything else "unknown"). `decodeVariant` in
/// `PanelObservation.fs` (T014) is the total decoder; this type just
/// carries the raw byte.
type MachineTypeByte = MachineTypeByte of byte

/// Parsed WHO_I_AM payload, per `who-i-am-wire-format.md` §Payload
/// table. The 15-byte wire layout is:
///   offset 0:    `machineType` (UInt8)
///   offset 1:    `fwType` (UInt8 — always `0x04` for button panels)
///   offsets 2..: three big-endian `UInt32` UUID words
///   offset 14:   padding byte (ignored on receive)
///
/// Round-trips with `encode`: `parse (encode f) = Some f` for every
/// well-formed `WhoIAmFrame`. The Lean theorem
/// `parse_encode_roundtrip` in `Phase2/WhoIAmFrame.lean` (T028)
/// mechanises this invariant.
type WhoIAmFrame =
    { MachineType: MachineTypeByte
      FwType: FwType
      Uuid: PanelUuid }

module WhoIAmFrame =

    /// Wire layout is fixed at 15 bytes per
    /// `who-i-am-wire-format.md` §Payload. Any other length is a
    /// silent drop (FR-013) — the parser returns `None`.
    let private wireLength = 15

    /// All button-panel WHO_I_AM frames carry `fwType = 0x04` per the
    /// audit in `CORRECTIONS.md` §C1. The parser rejects every other
    /// value as a silent drop (FR-013) because an `fwType` mismatch
    /// implies a different device class on the bus, which is out of
    /// scope for spec-002.
    let private buttonPanelFwType = 0x04uy

    /// Decode a 15-byte WHO_I_AM payload, per
    /// `who-i-am-wire-format.md` §Parse contract:
    ///   1. length-check (≠ 15 → `None`);
    ///   2. `fwType` check (≠ `0x04` → `None`);
    ///   3. accept any `machineType`;
    ///   4. big-endian UUID reads.
    /// Both rejection paths return `None` silently per FR-013 — no
    /// throw, no log, no Error-state flip.
    let parse (payload: ReadOnlyMemory<byte>) : WhoIAmFrame option =
        if payload.Length <> wireLength then
            None
        else
            let span = payload.Span
            let fwType = span[1]

            if fwType <> buttonPanelFwType then
                None
            else
                let machineType = span[0]
                let uuid0 = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(2, 4))
                let uuid1 = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(6, 4))
                let uuid2 = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(10, 4))

                Some
                    { MachineType = MachineTypeByte machineType
                      FwType = FwType fwType
                      Uuid = PanelUuid(uuid0, uuid1, uuid2) }

    /// Encode a `WhoIAmFrame` to its 15-byte wire payload, per
    /// `who-i-am-wire-format.md` §Payload. Offset 14 is padding and
    /// is left zero on emit (the wire contract states the value is
    /// ignored on receive). Left-inverse of `parse` on well-formed
    /// frames: `parse (encode f) = Some f`.
    let encode (frame: WhoIAmFrame) : byte[] =
        let buffer = Array.zeroCreate wireLength
        let (MachineTypeByte machineType) = frame.MachineType
        let (FwType fwType) = frame.FwType
        let (PanelUuid(uuid0, uuid1, uuid2)) = frame.Uuid
        buffer[0] <- machineType
        buffer[1] <- fwType
        BinaryPrimitives.WriteUInt32BigEndian(Span(buffer, 2, 4), uuid0)
        BinaryPrimitives.WriteUInt32BigEndian(Span(buffer, 6, 4), uuid1)
        BinaryPrimitives.WriteUInt32BigEndian(Span(buffer, 10, 4), uuid2)
        buffer
