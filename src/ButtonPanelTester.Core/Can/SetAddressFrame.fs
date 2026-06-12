namespace Stem.ButtonPanelTester.Core.Can

open System
open System.Buffers.Binary

/// SET_ADDRESS TX app payload (16 bytes), per
/// `specs/004-baptism-workflow/contracts/master-sequence-wire-format.md`
/// §"SET_ADDRESS app payload (16 B)" and
/// `specs/004-baptism-workflow/data-model.md` §2.2. The wire layout is
/// fully packed:
///   offsets 0-11:  the three `PanelUuid` words (byte-echo, NORMATIVE
///                  — see `encode`)
///   offsets 12-15: `SpAddress` (big-endian UInt32; the slave swaps on
///                  read) — computed by `spAddress` (§2.3)
/// `Uuid` reuses the WHO_I_AM RX side's `PanelUuid` single-case DU, so
/// the only way to build a `SetAddressFrame` is from a parsed
/// announcement's UUID triple.
///
/// Round-trips with `encode`: `parse (encode f) = Some f` for every
/// `SetAddressFrame`. The Lean theorem `parse_encode_roundtrip` in
/// `lean/Stem/ButtonPanelTester/Phase3/SetAddressFrame.lean` (T003)
/// mechanises this invariant.
type SetAddressFrame =
    { Uuid: PanelUuid
      SpAddress: uint32 }

module SetAddressFrame =

    /// Wire layout is fixed at 16 bytes per the contract §"SET_ADDRESS
    /// app payload (16 B)". Any other length parses to `None` — length
    /// is the ONLY rejection axis (house codec style). The Lean
    /// theorem `encode_length` (T003) mechanises the encode side.
    let private wireLength = 16

    /// Encode a `SetAddressFrame` to its 16-byte wire payload, per the
    /// contract §"SET_ADDRESS app payload (16 B)": the three UUID
    /// words at `[0..3]`/`[4..7]`/`[8..11]` written big-endian, then
    /// `SpAddress` big-endian at `[12..15]`.
    ///
    /// The big-endian word writes are the SAME convention
    /// `WhoIAmFrame.parse` reads at WHO_I_AM offsets 3/7/11 — this is
    /// what makes the contract's NORMATIVE byte-echo invariant hold:
    /// the 12 UUID bytes at `[0..11]` are byte-for-byte the bytes the
    /// panel announced at WHO_I_AM positions `[3..14]`, so the slave's
    /// word-equality acceptance check compares identical byte
    /// sequences regardless of endianness labeling. Lean
    /// `encode_parse_roundtrip` (the byte-echo theorem, T003) and the
    /// FsCheck mirrors `SetAddressFrameByteEcho` /
    /// `SetAddressEchoesAnnouncedUuidBytes` in
    /// `Tests/Property/Can/SetAddressFrameProperties.fs` (T011)
    /// mechanise this. Left-inverse of `parse` for every frame:
    /// `parse (encode f) = Some f` (Lean `parse_encode_roundtrip`,
    /// T003); always exactly 16 bytes (Lean `encode_length`, T003).
    let encode (frame: SetAddressFrame) : byte[] =
        let buffer = Array.zeroCreate wireLength
        let (PanelUuid(uuid0, uuid1, uuid2)) = frame.Uuid
        BinaryPrimitives.WriteUInt32BigEndian(Span(buffer, 0, 4), uuid0)
        BinaryPrimitives.WriteUInt32BigEndian(Span(buffer, 4, 4), uuid1)
        BinaryPrimitives.WriteUInt32BigEndian(Span(buffer, 8, 4), uuid2)
        BinaryPrimitives.WriteUInt32BigEndian(Span(buffer, 12, 4), frame.SpAddress)
        buffer

    /// Decode a 16-byte SET_ADDRESS payload, mirroring the slave's
    /// parse (`AutoAddressSlave.c:263-292`, contract §"SET_ADDRESS app
    /// payload (16 B)"):
    ///   1. length-check (≠ 16 → `None`) — the only rejection path;
    ///   2. three big-endian UUID word reads at offsets 0/4/8;
    ///   3. `SpAddress` as a big-endian `UInt32` at offsets 12-15.
    /// FsCheck mirrors: `SetAddressFrameRoundtrip` and
    /// `SetAddressFrameRejectsWrongLength` in
    /// `Tests/Property/Can/SetAddressFrameProperties.fs` (T011).
    let parse (payload: ReadOnlyMemory<byte>) : SetAddressFrame option =
        if payload.Length <> wireLength then
            None
        else
            let span = payload.Span
            let uuid0 = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(0, 4))
            let uuid1 = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4, 4))
            let uuid2 = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8, 4))
            let spAddress = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(12, 4))

            Some
                { Uuid = PanelUuid(uuid0, uuid1, uuid2)
                  SpAddress = spAddress }

    /// Compute the SP_Address word, mirroring the firmware's
    /// `SP_App_Calculate_ID` (`SP_Application.c:366-373`, research R3;
    /// contract §"SP_Address computation"; data-model §2.3):
    ///   `network <<< 24 ||| machineType <<< 16
    ///    ||| (fwType &&& 0x3FF) <<< 6 ||| (boardNumber &&& 0x3F)`.
    /// This feature always calls it as
    /// `spAddress 0uy variantByte announcedFwType 1uy` (network 0,
    /// board 1 — single-panel bench, spec assumption). Worked example:
    /// EDEN-XP / 12 V / board 1 → `0x00030101`, which equals the
    /// shipped vendored `DeviceVariantConfig` "Keyboard 1" constant.
    let spAddress (network: byte) (machineType: byte) (fwType: uint16) (boardNumber: byte) : uint32 =
        (uint32 network <<< 24)
        ||| (uint32 machineType <<< 16)
        ||| (uint32 (fwType &&& 0x3FFus) <<< 6)
        ||| uint32 (boardNumber &&& 0x3Fuy)
