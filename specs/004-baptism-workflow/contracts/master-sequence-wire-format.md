# Contract: Auto-Address Master-Sequence Wire Format (TX)

**Status**: living — consumers code against this file; update it if the wire drifts.
**Scope**: the two messages this tool **transmits** (FR-014). The RX side (WHO_I_AM) is owned by
[spec-003's contract](../../003-panel-discovery/contracts/who-i-am-wire-format.md) and is not
restated here.
**Verified**: 2026-06-11 against the on-disk protocol firmware source
(`STEM Protocol/AppLayerModules/AutoAddressSlave.c`, `AutoAddressMaster.c`,
`SP_Application.{h,c}`), matching the CORRECTIONS.md §C1–C2 audit citations.

## Command codes

SP_APP command enum (`SP_Application.h`), cross-checked against the shipped WHO_I_AM RX filter:

| Command | cmdHigh:cmdLow | App payload | Tool direction |
|---|---|---|---|
| `SP_APP_CMD_AA_WHO_ARE_YOU` | `0x00:0x23` | 4 B | TX (claim and reset) |
| `SP_APP_CMD_AA_WHO_I_AM` | `0x00:0x24` | 15 B | RX (spec-003) |
| `SP_APP_CMD_AA_SET_ADDRESS` | `0x00:0x25` | 16 B | TX (address assignment) |

These are built-in constants (with the SP_Address formula below) extending the existing hardcoded
protocol-metadata set; the fetch migration is tracked in
[#156](https://github.com/luca-veronelli-stem/button-panel-tester/issues/156) and out of scope.

## Transport (shared with RX, restated for TX)

Both messages travel as segmented SP_APP packets on CAN broadcast arbitration id `0x1FFFFFFF`
(29-bit extended):

```text
transport packet = cryptFlag(1) | senderId(4, BE) | lPack(2) | cmdHigh(1) | cmdLow(1)
                 | app payload(N) | CRC16-Modbus(2)
wire frame       = arbId(4, LE) | NetInfo(2) | chunk(≤6)
```

| Message | Packet size | CAN frames |
|---|---|---|
| WHO_ARE_YOU (4 B payload) | 15 B | **3** |
| SET_ADDRESS (16 B payload) | 27 B | **5** |

The vendored `IProtocolService.SendCommandAsync` produces all of this (packet build, CRC,
chunking, NetInfo, port write); the tool's adapter supplies only command code + app payload.
`senderId` is the tool's configured protocol sender id — the slave records it as its
`MotherBoardAddress` on a reset-flag claim (the tool becomes the panel's master; spec assumption).

## WHO_ARE_YOU app payload (4 B)

As parsed by the slave (`AutoAddressSlave.c:230-235`):

| Offset | Field | Encoding | This feature sends |
|---|---|---|---|
| `[0]` | machineType | u8 | variant identity byte (claim) / `0xFF` (reset) |
| `[1..2]` | fwType | u16 **big-endian** | panel's announced fwType (claim) / each of `0x0004`, `0x000F` (reset) |
| `[3]` | reset flag | u8, non-zero = set | **always `0x01`** (FR-003 / FR-008) |

Variant identity bytes: EDEN-XP `0x03`, OPTIMUS-XP `0x0A`, R-3L XP `0x0B`, EDEN-BS8 `0x0C`;
virgin marker `0xFF` (firmware constants, audited in CORRECTIONS.md).

### Slave semantics (normative for tool behaviour)

- **fwType gate**: the slave acts **only if** the received fwType equals its own hardware type
  (`AutoAddressSlave.c:237`). A mismatched WHO_ARE_YOU is silently ignored. Hence: claim echoes
  the selected panel's announced fwType; reset broadcasts once per known fwType (12 V `0x0004`,
  24 V `0x000F`) as a single technician action.
- **No state guard, no machineType guard**: any panel of matching fwType — virgin, claimed,
  silent — processes the command (re-verified 2026-06-11; basis of the reset-first policy,
  FR-011).
- **reset=1 effects**: EEPROM `IDMachineType` ← sent machineType; `MotherBoardAddress` ← sender
  srid; RAM `SP_Address` ← `0xFFFFFFFF`; announce timer restarted → the panel re-announces the
  **newly written** identity within its 2–6 s cadence (`2000 + (Σ uuid words mod 4000)` ms).
- The application layer **does** acknowledge the command — the dispatcher returns a `0x23` ACK (`SP_Application.c:347-360`) — but the slave sends no *domain* reply to WHO_ARE_YOU; the load-bearing observable effect is the re-announcement carrying the newly-written identity. (Corrects the earlier "the slave never replies" wording, 2026-06-17.)

## SET_ADDRESS app payload (16 B)

As parsed by the slave (`AutoAddressSlave.c:263-292`):

| Offset | Field | Encoding | This feature sends |
|---|---|---|---|
| `[0..3]` | UUID0 | byte-echo (see below) | from the validated WHO_I_AM |
| `[4..7]` | UUID1 | byte-echo | from the validated WHO_I_AM |
| `[8..11]` | UUID2 | byte-echo | from the validated WHO_I_AM |
| `[12..15]` | SP_Address | u32 **big-endian** (slave swaps on read) | computed, below |

**Byte-echo invariant (normative)**: the 12 UUID bytes at `[0..11]` MUST be byte-for-byte the
UUID bytes received at positions `[3..14]` of the panel's WHO_I_AM payload. The slave compares
word equality of same-convention reads on both sides, so echoing the announced bytes verbatim is
correct independent of endianness labeling. In code this is `encode (parse bytes) = bytes`
(codec round-trip, Lean `Phase3/SetAddressFrame.lean`).

### SP_Address computation

`SP_App_Calculate_ID` (`SP_Application.c:366-373`):

```text
spAddress = network << 24 | machineType << 16 | (fwType & 0x3FF) << 6 | (boardNumber & 0x3F)
```

This tool always assigns `network = 0`, `machineType` = chosen variant byte, `fwType` = panel's
announced fwType, `boardNumber = 1` (single-panel bench, spec assumption). Worked example:
EDEN-XP 12 V board 1 → `0x00030101` (matches the shipped `DeviceVariantConfig` "Keyboard 1"
constant).

### Slave semantics

- Accepts **iff all three UUID words match** its own; no machineType/fwType check.
- On accept: stores SP_Address, `IDBoardNumber = SP_Address & 0x3F` to EEPROM, transitions to
  `AAS_STAND_BY` (claimed, **silent** — it stops broadcasting WHO_I_AM, `AA_Slave_Main_Task`).
- **Acknowledgement and the success signal** (corrected 2026-06-17 — supersedes the earlier
  "no reply ever comes"): the dispatcher (`SP_App_ProcessDataRx`, `SP_Application.c:347-360`)
  builds an application ACK (`0x80 | cmd` → `0x25`) for every fully-received command whose handler
  returns true, so SET_ADDRESS **is acknowledged** (`02 80 25`). The handler returns true
  regardless of UUID match, so the `0x25` ACK proves "assignment received intact" — a fast
  positive — while **broadcast-silence is the authoritative, firmware-deterministic adoption
  signal**: silence ⟺ `AAS_STAND_BY` ⟺ UUID-matched address stored (no firmware path adopts yet
  keeps announcing). The tool's success signal is therefore **confirmed adoption** — the `0x25` ACK
  **and** confirmed broadcast-silence (FR-006) — **not** write completion (the F6 false success
  completed the write yet kept announcing). The FR-007 silence watch is folded into this gate.

## Transmission discipline (FR-014)

The tool transmits **only** these messages, only as the direct result of a technician-initiated
action, only while the link is `Connected`. Discovery remains passive. No automatic retry of any
failed send.
