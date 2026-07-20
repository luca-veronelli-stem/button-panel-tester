# Contract: Button-State Wire Format (SP_APP VAR_WRITE)

**Spec**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) §R1/R2 |
**Status**: living (consumers decode against this shape)

The panel reports its key state as an unsolicited SP_APP `VAR_WRITE`, emitted on change
(`TxTasti ≠ TxTastiOld`, `UserMain.c:973`) plus a periodic refresh, and **only after baptism**
(transmit gate `UserMain.c:990–993`).

**The refresh is dual-rate** (corrected 2026-07-20, #293 — see `research.md` R1 for the derivation):
≈ **188 ms** while the latched bitmap is non-zero (`TEMPO_CAN_VELOCE`), ≈ **12.5 s** while it is zero
(`TEMPO_CAN_LENTO`), selected per send at `UserMain.c:1013–1020`. Since `TxTasti` is zero-init and its
bits latch on *release*, a **cold, never-touched panel heartbeats at ≈ 12.5 s**; it switches to
≈ 188 ms permanently after the first press+release. Recency thresholds must therefore exceed 12.5 s
(`data-model.md` §6a).

**A never-touched panel does not transmit the first press of a button at all**: clearing an
already-clear bit leaves `TxTasti` unchanged, so the change gate never fires. The release does
transmit. Consumers score an unarmed position on that release transition (`data-model.md` §6b).

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

## Directed CAN ID + variant-from-ID match rule (fix #270, Session 2026-06-24)

A baptized panel is **silent on WHO_I_AM** — the firmware enters `AAS_STAND_BY` after `SET_ADDRESS`
and never re-broadcasts (`CORRECTIONS.md` §C1) — so it never appears in the spec-003 discovery list.
It instead heartbeats its button-state `VAR_WRITE` on a **directed CAN ID** equal to its SP_Address:

`SP_App_Calculate_ID = network <<< 24 | machineType <<< 16 | (fwType &&& 0x3FF) <<< 6 | board`

so the **machineType byte is bits 23–16** of the CAN ID. The observer's accept rule (and the tool's
observability signal) is therefore purely the **variant decode of the CAN ID**:

```
variant = VariantDecoder.decode (MachineTypeByte ((CanId >>> 16) &&& 0xFF))
accept  = (variant is Marketing _)
```

| CAN ID | machineType | decode | accepted |
|---|---|---|---|
| `0x000A0441` | `0x0A` | `Marketing OptimusXp` | yes |
| `0x00030141` | `0x03` | `Marketing EdenXp` | yes |
| `0x000B0481` | `0x0B` | `Marketing R3LXp` | yes |
| `0x1FFFFFFF` (WHO_I_AM broadcast) | `0xFF` | `Virgin` | **dropped** |
| `0x00000008` (tool SRID) | `0x00` | `Unknown 0x00` | **dropped** |

Reassembly is **per source CAN ID** (one `PacketReassembler` per id). The accepted observation
carries the decoded `MarketingVariant`. Ground-truth traces:
`~/Documents/frames/first-gather/{optimus,eden-xp,r-3l-xp}_baptized.trc` — 12 identical-payload
repetitions at 186.7 ms on the directed id, which is the **post-boot fast ramp**, not the idle
steady state (#293). Mechanised by Lean `machine_type_at_bits_23_16` /
`non_marketing_ids_rejected` (`Phase4/ButtonStateObservation.lean`, T044).

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
