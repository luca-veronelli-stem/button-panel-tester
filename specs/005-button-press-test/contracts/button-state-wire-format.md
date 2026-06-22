# Contract: Button-State Wire Format (SP_APP VAR_WRITE)

**Spec**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) §R1/R2 |
**Status**: living (consumers decode against this shape)

The panel reports its key state as an unsolicited SP_APP `VAR_WRITE`, emitted on change
(`TxTasti ≠ TxTastiOld`, `UserMain.c:973`) plus a slow periodic refresh, and **only after baptism**
(transmit gate `UserMain.c:990–993`).

## App-layer payload (5 bytes, `UserMain.c:429–449`)

| Offset | Value | Field |
|---|---|---|
| 0 | `0x00` | command high |
| 1 | `0x02` | command low — `SP_APP_CMD_ID_VAR_WRITE` |
| 2 | `0x80` | variable address high |
| 3 | `var_low` | variable address low (`IDBoardNumber − 1`; `0x73` for EDEN/BS8 keyboard-2) |
| 4 | `bitmap` | key-state byte = `TxTasti` |

- **Command**: `0x00:0x02`. The observer filters on this.
- **Variable address**: `0x80NN`. Recognised button-state set: `{0x8000, 0x803E}`. `0x80FE` is the
  **virgin sentinel** (unbaptized panel) and MUST NOT be treated as a test result.
- **Transport**: standard STEM packetisation (`cryptFlag + senderId + lPack + cmd + payload +
  CRC16`, chunked); reassembled by the shared `PacketReassembler`. In the reassembled packet the
  command is at bytes 7–8 and the variable address at 9–10 (`PacketDecoder.cs`); the decoder does
  not validate CRC (inherited limitation).

## Bitmap semantics (R2 — firmware ground truth)

Bit assignment (`UserMain.c:215–246`):

`UP=bit0 · DOWN=bit1 · P1=bit2 · P2=bit3 · P3=bit4 · MEM=bit5 · STOP=bit6 · LIGHT=bit7`

**Polarity: pressed = bit `0`, released/idle = bit `1`.** `buffer[4] = TxTasti` verbatim
(`UserMain.c:978`); press clears the bit (`:1369`), release sets it (`:1375`). The no-buttons-pressed
steady state is all-active-bits-`1`.

- A **press** is the transition of an active button's bit `1 → 0` (FR-006).
- The baseline MUST be taken from the **first observed frame** — `TxTasti` is zero-initialised at
  boot (`UserMain.c:200`) and only reaches the all-`1` idle state after the key-scan fires
  `RELEASED` for untouched keys, so an absolute byte is never a reliable press-state.
- Bits outside the variant's active mask are ignored (FR-014).

> The field-proven legacy app used set-bit-pressed (`ButtonPanelTestService.cs:825`), which against
> this firmware detects the **release** edge (bit returning to `1`). spec-005 detects the **press**
> edge per FR-006; the pressed-bit value is a single named constant (`PressedBit = 0uy`) and the
> direction is confirmed on a real OPTIMUS-XP panel in the Hardware E2E phase.
