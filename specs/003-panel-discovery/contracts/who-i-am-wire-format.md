# Contract: WHO_I_AM wire format

**Phase 1 output for**: [../plan.md](../plan.md)

**Implements**: FR-001, FR-002, FR-003, FR-007

Canonical wire-format specification for the STEM auto-address `WHO_I_AM`
broadcast that spec-003 observes. The tester only ever **receives** this message
(FR-009 — pure observation). It has **two layers**, both documented here:

1. **Transport** — the message is a segmented STEM SP_APP packet, split across
   several CAN frames and reassembled by the tester (receive-only).
2. **Application payload** — the reassembled 15-byte `WHO_I_AM` body and its codec.

## Authoritative sources

- **Payload** — the STEM serial-protocol library `AutoAddressSlave.c` (the firmware
  module that *builds* the broadcast), verified against the firmware tree on
  2026-06-05 ([`../context/firmware-verification-2026-06-05.md`](../context/firmware-verification-2026-06-05.md)).
- **Transport** — the STEM Network-Layer header `NetInfo`
  (`src/ButtonPanelTester.Infrastructure.Protocol/Services/NetInfo.cs`; `Docs/PROTOCOL.md` §4.1)
  and the SP_APP application-packet layout
  (`src/ButtonPanelTester.Infrastructure.Protocol/Services/PacketDecoder.cs`),
  cross-checked against a real bench capture (`Documents/frames/test-trace.trc`, 2026-06-09).

Payload-construction excerpt:

```c
// AutoAddressSlave.c:175-181 — payload construction (TX_Values[15])
TX_Values[0]  = IdMachineType;          // [0]      machineType (u8)
TX_Values[1]  = IdFWType >> 8;          // [1]      fwType HIGH
TX_Values[2]  = IdFWType;               // [2]      fwType LOW   (IdFWType is uint16)
*(u32*)&TX_Values[3]  = UUID0;          // [3..6]
*(u32*)&TX_Values[7]  = UUID1;          // [7..10]
*(u32*)&TX_Values[11] = UUID2;          // [11..14]

// AutoAddressSlave.c:233-234 — RX path, corroborating fwType is 16-bit big-endian
IdFWType = (rx[1] << 8) | rx[2];
```

## Transport — segmented multi-frame (receive-only)

A `WHO_I_AM` is a STEM **SP_APP application message**, not a single CAN frame. The
panel firmware hands it to the STEM **Network Layer**, which **segments** the
application packet across N classic-CAN frames (≤ 8 data bytes each), all on the
auto-address broadcast id. The tester must **reassemble** the fragments before the
15-byte payload can be read.

> **The previous draft was wrong here.** It described `WHO_I_AM` as a "logical
> 15-byte message [that] the vendored stack reassembles before it reaches the
> tester." On the bench (2026-06-09) the tester received **nothing**: the vendored
> receive loop was never started on a clean open, and discovery tapped the *raw*
> per-frame feed, which never reassembles. The real transport is the segmented
> Network Layer documented here, and reassembly is a step the tester performs — not
> a given. See [§Correction history](#correction-history).

| Aspect | Value |
|---|---|
| CAN ID | `0x1FFFFFFF` (29-bit extended; `SRID_BROADCAST` in panel firmware) |
| Framing | **Segmented**: each CAN frame data field = `[NetInfo (2 B)] [chunk (≤ 6 B)]` |
| Bitrate | 250 kbps (classic CAN 2.0; each fragment ≤ 8 data bytes) |
| Direction | Slave-to-bus broadcast; the tester receives only (FR-009) |
| Cadence | First announce after `(UIDw0 + UIDw1 + UIDw2) mod 4000` ms (0-4 s); thereafter every `2000 + (… mod 4000)` ms → ~2-6 s (`AutoAddressSlave.c:169-171, 214-222`). The modulus is over the sum of the three 32-bit UID *words*. |
| Trigger | Panel state ∈ {`AAS_STARTUP`, `AAS_ANSWER_TO_MASTER`} (virgin or mid-baptism); silent in `AAS_STAND_BY` (claimed). Virgin: `IDMachineType == 0xFF || IDBoardNumber == 0xFF` (`AutoAddressSlave.c:136-143`). |

The ~2-6 s worst-case cadence fixes the FR-005 prune threshold at 15 s (~2.5x) and
SC-001 at 6 s.

### Network-Layer header (`NetInfo`, 2 bytes little-endian)

`raw = (hi << 8) | lo`, where `lo`/`hi` are the first two bytes of each CAN frame's
data field:

| Bits | Field | Meaning |
|---|---|---|
| 15..6 | `RemainingChunks` (10) | fragments still to come; `0` = **last** fragment |
| 5 | `SetLength` (1) | `1` on the **first** fragment |
| 4..2 | `PacketId` (3) | rolling code 1..7; isolates concurrent messages |
| 1..0 | `Version` (2) | protocol version |

### Reassembly

Group fragments by `PacketId`; concatenate each fragment's chunk data (the bytes
**after** the 2-byte `NetInfo`) in arrival order; the message is complete when a
fragment arrives with `RemainingChunks == 0`. This is exactly
`Services.Protocol.PacketReassembler.Accept`. The concatenation is the reassembled
**SP_APP application packet** (next section). An incomplete sequence (a fragment
never arrives) is held with no observation produced — a silent non-event (FR-007).

### Worked example — real `virgin_panel_12v` (bench trace 2026-06-09)

Five frames on `0x1FFFFFFF`, all `PacketId = 2`:

| # | NetInfo | Remaining | First? | Chunk data |
|---|---|---|---|---|
| 1 | `28 01` | 4 | yes | `00 00 FF 01 3F 00` |
| 2 | `C8 00` | 3 | no | `11 00 24 FF 00 04` |
| 3 | `88 00` | 2 | no | `17 7C 12 6D 73 08` |
| 4 | `48 00` | 1 | no | `74 8F 16 09 21 04` |
| 5 | `08 00` | 0 | no | `EA 69` |

Reassembled (26 bytes):
`00 00 FF 01 3F 00 11 00 24 FF 00 04 17 7C 12 6D 73 08 74 8F 16 09 21 04 EA 69`.

## Reassembled SP_APP application packet

The reassembled bytes are a standard STEM SP_APP packet
(`PacketDecoder.cs` documents the layout):

| Offset | Width | Field | Notes |
|---|---|---|---|
| 0 | 1 | `cryptFlag` | Transport Layer |
| 1-4 | 4 | `senderId` (big-endian) | Transport Layer |
| 5-6 | 2 | `lPack` | Transport Layer, not read |
| 7 | 1 | `cmdHigh` | Application Layer command, high byte |
| 8 | 1 | `cmdLow` | Application Layer command, low byte |
| 9 … len-3 | var | **application payload** | for `WHO_I_AM`, the 15-byte `TX_Values` (below); offsets 0-based, inclusive |
| len-2 … len-1 | 2 | `CRC16` (Modbus) | the last two bytes; **not validated** by the vendored decoder |

**Command filter.** A reassembled packet is a `WHO_I_AM` iff its command bytes are
`cmdHigh:cmdLow == 0x00:0x24` (`SP_APP_CMD_AA_WHO_I_AM`). Reassembled packets
carrying any other command on `0x1FFFFFFF` are silently dropped (FR-007). In the
worked example: `cmd = 0x0024`, application payload = bytes `9..23` =
`FF 00 04 17 7C 12 6D 73 08 74 8F 16 09 21 04` — exactly the 15-byte `TX_Values`.

**Payload extraction.** application payload = the bytes from offset 9 up to **but not
including** `len-2` — i.e. .NET `payload.Slice(9, len - 11)`, equivalently 0-based inclusive
`[9 .. len-3]` (`ApplicationPayloadStart = 9`, `CrcTailLength = 2` in `PacketDecoder`; this is
exactly `PacketDecoder.ExtractApplicationPayload`). For the 26-byte worked example that is bytes
9..23 = 15 bytes. The extracted payload is handed to the codec below.

The spec-003 receive adapter **reuses** `PacketReassembler` and these
firmware-pinned offsets; it does **not** use the dictionary-driven `PacketDecoder`
(no command/variable/sender resolution is needed for passive observation, and
discovery must not depend on a loaded dictionary). The `CRC16` is present but the
vendored decoder does not validate it; the adapter MAY validate it (CRC16-Modbus),
but a length + command match is the minimum.

## WHO_I_AM application payload (15 bytes, no padding)

This is the reassembled application payload (bytes `9 .. len-2` above). Codec:
`src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs` (Phase A — already corrected).

| Offset | Width | Field | Encoding | Notes |
|---|---|---|---|---|
| 0 | 1 | `machineType` | `UInt8` | Virgin = `0xFF`; one of `{0x03 EDEN-XP, 0x0A OPTIMUS-XP, 0x0B R-3L XP, 0x0C EDEN-BS8}`; `0x08` TopLift-A and any other value decode to `Unknown` (FR-003) |
| 1-2 | 2 | `fwType` | `UInt16` **big-endian** | Panel **hardware** variant (the tastiera's own `ID_FW_TYPE`): `0x0004` = 12 V board, `0x000F` = 24 V board (`tastiera/src/UserMain.h:32,34`). Informational only — **not** an acceptance gate |
| 3-6 | 4 | `uuid0` | `UInt32` big-endian | First chip-UID word |
| 7-10 | 4 | `uuid1` | `UInt32` big-endian | Second chip-UID word |
| 11-14 | 4 | `uuid2` | `UInt32` big-endian | Third chip-UID word |

**UUID endianness.** The firmware byte-swaps each native chip-UID word
(`OSdwordSwap` in `AA_Slave_Init`) before the raw little-endian store, so each word
lands **big-endian on the wire**. A big-endian read recovers the chip-UID word as
the firmware intends.

## Receive + parse contract

The full receive chain (FR-001/002/003/007). Each step is a **silent drop** on
failure (FR-007 — never an Error flip):

1. **Filter** to CAN id `0x1FFFFFFF`.
2. **Reassemble** fragments via the `NetInfo` header (group by `PacketId`, complete
   at `RemainingChunks == 0`).
3. **Command filter**: keep only reassembled packets whose command is `0x0024`
   (`SP_APP_CMD_AA_WHO_I_AM`); drop the rest.
4. **Extract** the application payload = `payload.Slice(9, len - 11)` (offset 9 up to `len-2`
   exclusive; 15 bytes for a WHO_I_AM).
5. **Parse** the 15-byte payload with `WhoIAmFrame.parse`, which MUST:
   1. **Length check.** Reject payloads whose length ≠ 15 by returning `None`. This
      is the **only** payload rejection rule.
   2. **No `fwType` gate.** Accept every `fwType` (read as big-endian `UInt16` from
      offsets 1-2, retained as informational metadata). Gating on `fwType` would
      wrongly reject every 24 V panel (`fwType = 0x000F`).
   3. **Accept any `machineType`.** Decoding to `Marketing _ | Virgin | Unknown _`
      happens downstream in `VariantDecoder.decode` (FR-003); `Unknown raw` is a
      first-class outcome.
   4. **Big-endian reads.** `fwType` via `ReadUInt16BigEndian` over 1-2; `uuid0..2`
      via `ReadUInt32BigEndian` over 3-6 / 7-10 / 11-14.

`encode` is the left inverse of the payload codec: it writes the 15 bytes with no
padding, so `parse (encode f) = Some f` for **every** `WhoIAmFrame` (no
well-formedness precondition). `encode` is payload-only; the tester never builds the
multi-frame transport (it is receive-only, FR-009).

## Correction history

### Payload codec (#121) — corrected in Phase A

`WhoIAmFrame.fs` (shipped under #121) encoded the **old** layout and was wrong on a
real bus:

| Symptom | Cause | Consequence |
|---|---|---|
| Every real `WHO_I_AM` dropped | `fwType = span[1]` then `if fwType <> 0x04uy -> None`; on a real frame `span[1] = IdFWType >> 8 = 0x00` | the list never populated |
| 24 V panels unrepresentable | `fwType` modelled as one byte | `0x000F` cannot round-trip |
| UUID words mis-read | reads at offsets 2/6/10 | UUIDs shifted by one byte |
| Spurious padding | `encode` writes a padding byte at `[14]` | `[14]` is UUID2's last byte |

Phase A re-baselined `WhoIAmFrame.fs`, `Phase2/WhoIAmFrame.lean`, the FsCheck
round-trip/reject properties, and `whoIAmFixtures.json` onto the corrected layout
above, anchored to a real bench capture.

### Transport (single-frame assumption) — corrected in this re-scope

The previous draft asserted a "logical 15-byte message [that] the vendored stack
reassembles before it reaches the tester." Two things were false on real hardware
(bench 2026-06-09): **(a)** the vendored receive loop (`PCANManager.StartReading`)
was never started on a clean open, so the tester received nothing; **(b)** discovery
tapped the *raw* per-frame `ICanFrameStream` feed, which never reassembles. The
corrected model: `WHO_I_AM` is a segmented SP_APP message; the tester starts the
read loop and reassembles the fragments itself (reusing `PacketReassembler`). This
re-scope folds in both fixes — see [../plan.md](../plan.md).

## Fixtures

### Payload fixtures (codec — Phase A)

`tests/ButtonPanelTester.Tests/Fixtures/Can/whoIAmFixtures.json` — one 15-byte
payload per case:

| Fixture | `machineType` | `fwType` | Expected `VariantIdentity` | Source |
|---|---|---|---|---|
| `virgin_panel_12v` | `0xFF` | `0x0004` | `Virgin` | **Real bench capture** (anchor) |
| `eden_xp` | `0x03` | `0x0004` | `Marketing EdenXp` | Capture |
| `optimus_xp` | `0x0A` | `0x0004` | `Marketing OptimusXp` | Capture |
| `r3l_xp` | `0x0B` | `0x0004` | `Marketing R3LXp` | Capture |
| `eden_bs8` | `0x0C` | `0x0004` | `Marketing EdenBs8` | Capture |
| `virgin_panel_24v` | `0xFF` | `0x000F` | `Virgin` | Synthetic — 24 V `fwType` |
| `unknown_toplift_a` | `0x08` | `0x0004` | `Unknown 0x08uy` | Synthetic — TopLift-A |
| `malformed_too_short_14b` | n/a | n/a | `None` (length ≠ 15) | Synthetic |

### Transport fixtures (reassembly — this re-scope)

The reassemble adapter's tests drive **raw multi-frame sequences**, anchored to the
real bench capture above:

| Fixture | Input | Expected |
|---|---|---|
| `whoiam_5frame_virgin_12v` | the 5 real frames (worked example) | reassembles to the 26-byte packet; `cmd 0x0024`; payload = `virgin_panel_12v` |
| `whoiam_missing_fragment` | 4 of the 5 frames (drop one) | incomplete → **no observation** (silent) |
| `whoiam_wrong_command` | a reassembled packet with `cmd ≠ 0x0024` | dropped (FR-007) |
| `nonbroadcast_id` | a frame on an id ≠ `0x1FFFFFFF` | ignored |
| `interleaved_packetids` | two concurrent sequences with distinct `PacketId` | each reassembles independently |

At least `whoiam_5frame_virgin_12v` MUST be the verbatim real capture so the
transport is empirically pinned, not only reverse-engineered.

## Lean cross-reference

`lean/Stem/ButtonPanelTester/Phase2/WhoIAmFrame.lean` proves `parse_encode_roundtrip`
for the **payload codec** (the 15-byte body), holding for every `WhoIAmFrame` with
no precondition. The Lean model stays at the record level — byte-offset big-endian
packing is the F# `BinaryPrimitives` layer's concern. The **transport** (NetInfo
segmentation + reassembly) is vendored C# (`PacketReassembler`) and is **not**
Lean-formalized in spec-003; it is covered by the transport fixtures above and the
bench E2E.
