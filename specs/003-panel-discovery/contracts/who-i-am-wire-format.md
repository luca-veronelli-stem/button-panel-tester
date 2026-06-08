# Contract: WHO_I_AM wire format

**Phase 1 output for**: [../plan.md](../plan.md)

**Implements**: FR-001, FR-002, FR-003, FR-007

Canonical wire-format specification for the STEM auto-address `WHO_I_AM`
broadcast that spec-003 observes. The tester only ever **receives** this frame
(FR-009 — pure observation); the contract is frozen for spec-003 and read from,
never written to a real bus.

## Authoritative source

The layout below is read directly from the STEM serial-protocol library
`AutoAddressSlave.c` (the firmware module that *builds* the broadcast), verified
against the firmware tree on 2026-06-05 and recorded in
[`../context/firmware-verification-2026-06-05.md`](../context/firmware-verification-2026-06-05.md).
The relevant excerpts:

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

> **This contract supersedes the previous draft and the shipped #121 codec.**
> The archived draft at [`../context/previous-003/contracts/who-i-am-wire-format.md`](../context/previous-003/contracts/who-i-am-wire-format.md)
> cited a then-absent `AutoAddressSlave.c:165-183` and encoded `fwType` as a
> single byte at offset 1 with UUID words at 2/6/10 and a padding byte at 14.
> That layout is wrong (see [§Correction](#correction--the-shipped-121-codec-parses-real-frames-incorrectly)).
> The corrected layout here is the one `data-model.md`, the F# `WhoIAmFrame`,
> the Lean `parse_encode_roundtrip`, and the fixtures are all re-baselined onto.

## Transport

| Aspect | Value |
|---|---|
| CAN ID | `0x1FFFFFFF` (29-bit extended; `SRID_BROADCAST` in panel firmware) |
| Frame | Logical 15-byte message; the vendored stack reassembles the payload before it reaches the tester |
| Bitrate | 250 kbps |
| Direction | Slave-to-bus broadcast; the tester receives only (FR-009) |
| Cadence | First announce after `(UIDw0 + UIDw1 + UIDw2) mod 4000` ms (0-4 s); thereafter every `2000 + ((UIDw0 + UIDw1 + UIDw2) mod 4000)` ms → ~2-6 s. Source: `AutoAddressSlave.c:169-171, 214-222`. **The modulus is over the sum of the three 32-bit UID *words*, not the sum of the payload bytes.** |
| Trigger | Panel state `∈ {AAS_STARTUP, AAS_ANSWER_TO_MASTER}` (virgin or mid-baptism). Silent in `AAS_STAND_BY` (claimed). Virgin condition: `IDMachineType == 0xFF || IDBoardNumber == 0xFF` (`AutoAddressSlave.c:136-143`). |

The ~2-6 s worst-case cadence is what fixes the FR-005 prune threshold at 15 s
(~2.5x the worst case) and SC-001 at 6 s.

## Payload (15 bytes, no padding)

| Offset | Width | Field | Encoding | Notes |
|---|---|---|---|---|
| 0 | 1 | `machineType` | `UInt8` | Virgin = `0xFF`; one of `{0x03 EDEN-XP, 0x0A OPTIMUS-XP, 0x0B R-3L XP, 0x0C EDEN-BS8}`; `0x08` TopLift-A and any other value decode to `Unknown` (FR-003) |
| 1-2 | 2 | `fwType` | `UInt16` **big-endian** | Panel **hardware** variant (the tastiera's own `ID_FW_TYPE`): `0x0004` = 12 V board, `0x000F` = 24 V board (`tastiera/src/UserMain.h:32,34`). Informational only — **not** an acceptance gate |
| 3-6 | 4 | `uuid0` | `UInt32` big-endian | First chip-UID word |
| 7-10 | 4 | `uuid1` | `UInt32` big-endian | Second chip-UID word |
| 11-14 | 4 | `uuid2` | `UInt32` big-endian | Third chip-UID word |

**UUID endianness.** The firmware byte-swaps each native chip-UID word
(`OSdwordSwap` in `AA_Slave_Init`) before the raw little-endian store, so each
word lands **big-endian on the wire**. A big-endian read therefore recovers the
chip-UID word as the firmware intends. The previous draft's big-endian read was
correct; only the **offsets** (2/6/10) were wrong — they shift to 3/7/11 once
`fwType` occupies two bytes.

## Parse contract

The tester `parse` function (`src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs`)
MUST:

1. **Length check.** Reject payloads whose length ≠ 15 by returning `None`
   (FR-007 silent drop). This is the **only** rejection rule.
2. **No `fwType` gate.** Accept every `fwType`. `fwType` is read as a big-endian
   `UInt16` from offsets 1-2 and retained as informational panel-variant
   metadata; it never causes a drop. (Rationale: only panels run `AA_Slave` and
   broadcast `WHO_I_AM` under `SP_APP_CMD_AA_WHO_I_AM`, so the frame already
   identifies a panel — gating on `fwType` would wrongly reject every 24 V
   panel, whose `fwType` is `0x000F`.)
3. **Accept any `machineType`.** Decoding to `Marketing _ | Virgin | Unknown _`
   happens downstream in `VariantDecoder.decode` (FR-003). The parser never
   rejects on `machineType` — `Unknown raw` is a first-class outcome.
4. **Big-endian reads.** `fwType` via `BinaryPrimitives.ReadUInt16BigEndian`
   over offsets 1-2; `uuid0..2` via `BinaryPrimitives.ReadUInt32BigEndian` over
   offsets 3-6 / 7-10 / 11-14.

`encode` is the left inverse on the whole type: it writes the 15 bytes above
with no padding, so `parse (encode f) = Some f` for **every** `WhoIAmFrame`
(the corrected codec has no well-formedness precondition, unlike the old
`fwType = 0x04` guarded form).

## Correction — the shipped #121 codec parses real frames incorrectly

`src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs` (shipped under #121) encodes the
**old** layout and is wrong on a real bus:

| Symptom | Cause | Consequence |
|---|---|---|
| Every real `WHO_I_AM` is dropped | `fwType = span[1]` then `if fwType <> 0x04uy -> None`. On a real frame `span[1] = IdFWType >> 8 = 0x00` (both 4 and 15 are < 256), so `0x00 <> 0x04` rejects every frame | The Panels-on-bus list never populates on the bench |
| 24 V panels unrepresentable | `fwType` modelled as one byte | `fwType = 0x000F` cannot round-trip |
| UUID words mis-read | reads at offsets 2/6/10 | UUIDs are shifted by one byte versus the wire |
| Spurious padding | `encode` writes a padding byte at `[14]` | offset 14 is UUID2's last byte, not padding |

The synthetic fixtures in `tests/.../Fixtures/Can/whoIAmFixtures.json` were
hand-built to match this wrong codec (e.g. virgin = `FF 04 00 00 00 AA …`), so
the unit tests, the FsCheck round-trip, and the Lean `parse_encode_roundtrip`
theorem are **internally consistent about a format the firmware never sends** —
they pass while validating the wrong thing.

**Correcting this is the first implementation slice of spec-003** (see
[../plan.md](../plan.md) §Implementation phases, Phase A). The slice touches
`WhoIAmFrame.fs`, `Phase2/WhoIAmFrame.lean`, the FsCheck round-trip/reject
properties, and `whoIAmFixtures.json` together, as one bisect-safe vertical
commit.

## Fixtures

`tests/ButtonPanelTester.Tests/Fixtures/Can/whoIAmFixtures.json` carries one
15-byte payload per case, rebuilt from the corrected layout:

| Fixture | `machineType` | `fwType` | Expected `VariantIdentity` | Source |
|---|---|---|---|---|
| `virgin_panel_12v.json` | `0xFF` | `0x0004` | `Virgin` | **Real bench capture** (anchor) |
| `eden_xp.json` | `0x03` | `0x0004` | `Marketing EdenXp` | Bench capture |
| `optimus_xp.json` | `0x0A` | `0x0004` | `Marketing OptimusXp` | Bench capture |
| `r3l_xp.json` | `0x0B` | `0x0004` | `Marketing R3LXp` | Bench capture |
| `eden_bs8.json` | `0x0C` | `0x0004` | `Marketing EdenBs8` | Bench capture |
| `virgin_panel_24v.json` | `0xFF` | `0x000F` | `Virgin` | Synthetic — exercises the 24 V `fwType` |
| `unknown_toplift_a.json` | `0x08` | `0x0004` | `Unknown 0x08uy` | Synthetic — TopLift-A, out of the 4-machine scope |
| `malformed_too_short_14b.json` | n/a | n/a | `None` (length ≠ 15) | Synthetic |

At least one fixture (`virgin_panel_12v.json`) MUST be a verbatim real bench
capture so the format is empirically pinned, not only firmware-read. The old
`malformed_wrong_fwtype.json` fixture is **removed** — `fwType` is no longer a
rejection axis, so there is no such failure case.

## Lean cross-reference

`lean/Stem/ButtonPanelTester/Phase2/WhoIAmFrame.lean` proves
`parse_encode_roundtrip`. Re-stated for the corrected codec: the round-trip now
holds for every `WhoIAmFrame` with **no** `fwType` precondition (the old proof
carried `fwType := 4` through the constructor to satisfy the dropped gate). The
Lean model stays at the record level — byte-offset big-endian packing is the F#
`BinaryPrimitives` layer's concern — so `encode` is a bijection onto its
`WirePayload` image and `parse` is its total left inverse.
