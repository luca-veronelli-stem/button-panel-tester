# Firmware verification — WHO_I_AM wire facts (2026-06-05)

Cross-check of spec-003's firmware-derived facts against the actual STEM firmware
at `C:\Users\LucaV\Source\Repos\firmwares\` (read-only): the four machine
firmwares, the panel ("tastiera") firmware, and — decisively — the serial-protocol
library `stem-fw-protocollo-seriale-stem-.../STEM Protocol/AppLayerModules/AutoAddressSlave.{c,h}`,
the authoritative source of the WHO_I_AM broadcast. This is the empirical basis for
the new `contracts/who-i-am-wire-format.md` + `data-model.md`, and it supersedes the
previous draft's wire-format contract (which cited a then-absent `AutoAddressSlave.c:165-183`).

## Confirmed against firmware

- **Variant table** (each machine's `ID_MACHINE_TYPE`): `0x03` Eden/EDEN-XP,
  `0x0A` OPTIMUS-XP, `0x0B` R-3L XP, `0x0C` EDEN-BS8; virgin/unbaptized default
  `0xFF` (`tastiera/src/UserMain.c:286`). All four + virgin verified.
- **A 5th type exists: `0x08` TopLift-A** (`optimus/.../UserMain.h:32`) — out of the
  current 4-machine scope, decoded as `Unknown 0x08`.
- **Broadcast state machine** (`AutoAddressSlave.c:136-143, 165-190`): virgin
  (`IDMachineType==0xFF || IDBoardNumber==0xFF`) -> `AAS_STARTUP` -> broadcasts;
  baptized -> `AAS_STAND_BY` -> silent (`return`); mid-baptism `AAS_ANSWER_TO_MASTER`
  also broadcasts. Matches the spec's model.
- **Cadence** (`AutoAddressSlave.c:169-171, 214-222`): first announce after
  `(UIDw0+UIDw1+UIDw2) mod 4000` ms (0-4 s); thereafter every
  `2000 + ((UIDw0+UIDw1+UIDw2) mod 4000)` ms -> ~2-6 s. The 2-6 s range holds, so
  the 15 s prune (~2.5x) is right. NOTE: it is the sum of the three 32-bit UID
  *words* mod 4000 — NOT "sum of uuid bytes" as the old contract stated.
- **15-byte payload** (`uint8_t TX_Values[15]`).

## CORRECTION - the wire layout is materially different

Authoritative source `AutoAddressSlave.c:175-181`:

```c
TX_Values[0]  = IdMachineType;          // [0]     machineType (u8)
TX_Values[1]  = IdFWType >> 8;          // [1]     fwType HIGH
TX_Values[2]  = IdFWType;               // [2]     fwType LOW   <- fwType is uint16 (AA_Device_Descriptor_Slave_t.IdFWType)
*(u32*)&TX_Values[3]  = UUID0;          // [3..6]
*(u32*)&TX_Values[7]  = UUID1;          // [7..10]
*(u32*)&TX_Values[11] = UUID2;          // [11..14]
```

Corroborated by the RX path (`AutoAddressSlave.c:233-234`): `IdFWType = (rx[1]<<8)|rx[2]`.

| Offset | Width | Field | Previous contract / #121 |
|---|---|---|---|
| 0 | 1 | `machineType` (u8) | machineType — OK |
| 1-2 | 2 | **`fwType` (uint16, big-endian)** | fwType as **1 byte** @1 — WRONG |
| 3-6 | 4 | `UUID0` (uint32, big-endian on wire) | @ 2-5 — WRONG |
| 7-10 | 4 | `UUID1` (uint32, BE) | @ 6-9 — WRONG |
| 11-14 | 4 | `UUID2` (uint32, BE) | @ 10-13 — WRONG |
| - | - | (no padding byte) | "padding @14" — WRONG |

UUID endianness: the firmware byte-swaps the native chip UID word (`OSdwordSwap` in
`AA_Slave_Init`) before the raw little-endian store, so each word lands **big-endian
on the wire**; a BE read recovers the chip UID word. The BE read is correct — only the
**offset** (3/7/11, not 2/6/10) is wrong.

## Impact - the shipped #121 foundation parses real frames INCORRECTLY

`src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs` (PR-B #121) encodes the OLD layout:

- `fwType = span[1]` (1 byte) and `if fwType <> 0x04uy -> None`. On a real frame
  `byte[1] = IdFWType>>8 = 0x00` (fwType 4 and 15 are both < 256), so **every real
  WHO_I_AM is rejected** (`0x00 != 0x04`) -> the list never populates on the bench.
  It also cannot represent the 24 V panel (`fwType = 0x000F`).
- UUID reads at 2/6/10 (should be 3/7/11); `encode` writes a padding byte at [14]
  (should be UUID2's last byte).

`tests/.../whoIAmFixtures.json` are **synthetic, hand-built to match the wrong
contract** (e.g. virgin = `FF 04 00 00 00 AA ...`), NOT real bench captures — so the
unit tests, the FsCheck round-trip, and the Lean `parse_encode_roundtrip` theorem are
internally consistent about the **wrong** codec. They pass while validating a format
the firmware never sends.

**So spec-003 must CORRECT the wire foundation before building the pipeline:**
- `WhoIAmFrame.fs` — `fwType` as `uint16` BE @ [1..2]; UUID @ [3/7/11]; no padding;
  drop/replace the `fwType == 0x04` reject.
- `contracts/who-i-am-wire-format.md` — re-author from `AutoAddressSlave.c`.
- `whoIAmFixtures.json` — rebuild from the correct layout; anchor at least one entry
  to a REAL bench capture.
- `Phase2/WhoIAmFrame.lean` + the FsCheck round-trip — re-state for the corrected codec.

## fwType semantics

`fwType` is the **panel hardware variant** (the tastiera's own `ID_FW_TYPE`):
**4 (`0x0004`) on 12 V boards, 15 (`0x000F`) on 24 V boards** (`tastiera/src/UserMain.h:32,34`).
Only panels run `AA_Slave` and broadcast WHO_I_AM, so the SP command id
(`SP_APP_CMD_AA_WHO_I_AM`) already identifies a panel — `fwType` need not gate
acceptance. Recommend: validate length = 15 only; decode `fwType` (uint16) as
informational panel-variant metadata; do **not** reject on it.

## Decisions for the plan

1. **Foundation correction is in scope for spec-003** (recommend) — fold the
   `WhoIAmFrame`/contract/fixtures/Lean fix into spec-003's plan + tasks as the first
   slice. Alternative: a separate "fix WHO_I_AM wire format" bug ticket done first.
2. **fwType**: drop the equality reject; treat as informational uint16. Confirm whether
   any in-scope machine ships 24 V panels (affects fixtures, not the parse).
3. **TopLift-A `0x08`**: leave "unknown" unless promoted to a 5th known variant.
4. **Real capture**: anchor >=1 fixture to a real bench capture so the format is
   empirically pinned, not only firmware-read.
