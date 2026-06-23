namespace Stem.ButtonPanelTester.Core.Can

open System
open System.Buffers.Binary

/// Variable address of an SP_APP `VAR_WRITE` button-state report, per
/// `specs/005-button-press-test/contracts/button-state-wire-format.md`
/// §App-layer payload (offsets 2-3, big-endian `0x80NN`). The single-
/// case wrapper keeps the address distinguishable from the key-state
/// byte at type level. `0x8000` / `0x803E` are recognised button-state
/// addresses; `0x80FE` is the virgin sentinel (unbaptized panel) — but
/// `parse` accepts any address: dropping the sentinel and non-button
/// addresses is the observer's job (Phase C), not the parser's.
type VariableAddress = VariableAddress of uint16

/// Raw key-state byte (`TxTasti`) of a `VAR_WRITE` button-state report,
/// per `button-state-wire-format.md` §Bitmap semantics (offset 4). The
/// byte is carried verbatim off the wire: **pressed = bit `0`,
/// released/idle = bit `1`** (firmware ground truth, R2). The single-
/// case wrapper keeps it distinct from the address; the press-edge
/// detector over consecutive bitmaps lives in `KeyStateBitmap.fs`
/// (`pressEdges` + `PressedBit`).
type KeyStateBitmap = KeyStateBitmap of byte

/// Parsed `VAR_WRITE` button-state report, per
/// `button-state-wire-format.md` §App-layer payload. The 5-byte wire
/// payload is `[0x00, 0x02, 0x80, var_low, bitmap]`:
///   offsets 0-1:  command `0x00:0x02` (`SP_APP_CMD_ID_VAR_WRITE`)
///   offsets 2-3:  variable address (big-endian `UInt16`, `0x80NN`)
///   offset 4:     key-state byte (`TxTasti`)
///
/// Round-trips with `encode`: `parse (encode f) = Some f` for every
/// `ButtonStateFrame`. The Lean theorem `parse_encode_roundtrip` in
/// `Phase4/ButtonStateFrame.lean` (T002) mechanises this invariant.
type ButtonStateFrame =
    { Address: VariableAddress
      Bitmap: KeyStateBitmap }

module ButtonStateFrame =

    /// Wire layout is fixed at 5 bytes per
    /// `button-state-wire-format.md` §App-layer payload. Any other
    /// length is a silent drop — `parse` returns `None`.
    let private wireLength = 5

    /// Decode a 5-byte `VAR_WRITE` button-state payload, per
    /// `button-state-wire-format.md` §App-layer payload:
    ///   1. length-check (≠ 5 → `None`) — the ONLY rejection path,
    ///      mirroring `WhoIAmFrame.parse`;
    ///   2. read the variable address as a big-endian `UInt16` at
    ///      offsets 2-3;
    ///   3. read the key-state byte at offset 4.
    /// The command bytes (offsets 0-1) are NOT validated here — the
    /// observer filters command `0x00:0x02` and the button-state
    /// address set (Phase C, R6); the parser rejects on length only.
    let parse (payload: ReadOnlyMemory<byte>) : ButtonStateFrame option =
        if payload.Length <> wireLength then
            None
        else
            let span = payload.Span
            let address = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2))
            let bitmap = span[4]

            Some
                { Address = VariableAddress address
                  Bitmap = KeyStateBitmap bitmap }

    /// Encode a `ButtonStateFrame` to its 5-byte wire payload, per
    /// `button-state-wire-format.md` §App-layer payload: command
    /// `0x00:0x02`, the big-endian variable address, then the key-state
    /// byte. Left-inverse of `parse` for every frame:
    /// `parse (encode f) = Some f`.
    let encode (frame: ButtonStateFrame) : byte[] =
        let buffer = Array.zeroCreate wireLength
        let (VariableAddress address) = frame.Address
        let (KeyStateBitmap bitmap) = frame.Bitmap
        buffer[0] <- 0x00uy
        buffer[1] <- 0x02uy
        BinaryPrimitives.WriteUInt16BigEndian(Span(buffer, 2, 2), address)
        buffer[4] <- bitmap
        buffer
