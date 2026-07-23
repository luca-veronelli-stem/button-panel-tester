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

## Destination addressing + variant-from-senderId match rule (fix #296, Session 2026-07-23; supersedes the #270 variant-from-CAN-ID rule)

A baptized panel is **silent on WHO_I_AM** — the firmware enters `AAS_STAND_BY` after `SET_ADDRESS`
and never re-broadcasts (`CORRECTIONS.md` §C1) — so it never appears in the spec-003 discovery list.
It instead heartbeats its button-state `VAR_WRITE` **addressed to the master that baptized it**: the
CAN arbitration ID is the **destination** — the stored `MotherBoardAddress`
(`UserMain.c:997` `app.srid = MotherBoardAddress`; written from the baptizing master's srid,
`AutoAddressSlave.c:238-241`). For a panel baptized by this tool that destination is the tool's own
SRID **`0x00000008`**; for a panel baptized on a real machine it is that machine master's address.

The panel's **own** SP address rides inside the transport packet as the **senderId** (bytes 1–4 of
the reassembled packet):

`SP_App_Calculate_ID = network <<< 24 | machineType <<< 16 | (fwType &&& 0x3FF) <<< 6 | board`

so the **panel's machineType byte is bits 23–16 of the senderId**, NOT of the arbitration ID. The
observer's accept rule (and the tool's observability signal) is therefore the **variant decode of
the senderId**, applied at completed-packet level:

```
accept  = cmd = 0x0002 && addr ∈ {0x8000, 0x803E}
          && (VariantDecoder.decode (MachineTypeByte ((SenderId >>> 16) &&& 0xFF)) is Marketing _)
variant = that decode
```

| Packet | senderId | decode | accepted |
|---|---|---|---|
| heartbeat, tool-baptized OPTIMUS (arb. ID `0x00000008`) | `0x000A0101` | `Marketing OptimusXp` | yes |
| heartbeat, machine-baptized OPTIMUS (arb. ID `0x000A0441`) | `0x000A0101` | `Marketing OptimusXp` | yes |
| WHO_I_AM broadcast (arb. ID `0x1FFFFFFF`) | virgin | — | **dropped** (cmd `0x0024`) |
| virgin sentinel `0x80FE` | any | — | **dropped** (addr) |

There is **no arbitration-ID pre-filter**: chunk reassembly stays **per source arbitration ID** (one
`PacketReassembler` per id — chunks carry no senderId, it is only known after reassembly), and every
id's completed packets are then filtered by the rule above. The tool does not receive its own TX
frames (no PCAN self-reception), so listening on `0x00000008` observes only the panel.

> **History of this rule.** The #270 design read the June ground-truth traces
> (`~/Documents/frames/first-gather/*_baptized.trc` — arb. IDs `0x000A0441`/`0x00030141`/
> `0x000B0481`) as the panel transmitting on *its own* directed ID, and keyed accept + variant off
> the arbitration ID (explicitly dropping `0x00000008` as "the tool's SRID"). Those panels had been
> baptized on **real machines**, whose master **coincidentally shares the machineType byte with its
> keyboard** — the rule worked on those captures and failed on the first tool-baptized panel
> (`bench-logs/pcan/test1.trc`, 2026-07-23: all heartbeat frames on `0x00000008`, senderId
> `0x000A0101`). Corollary: the June "~12 s frames on `0x00000008`" were a tool-baptized panel's
> slow-branch heartbeat all along. Mechanised by the senderId theorems in
> `Phase4/ButtonStateObservation.lean` (T055; the T044 `machine_type_at_bits_23_16` extraction
> lemma is unchanged — it now applies to the senderId word).

## Bitmap semantics (R2 — firmware ground truth)

Bit assignment (`UserMain.c:215–246`):

`UP=bit0 · DOWN=bit1 · P1=bit2 · P2=bit3 · P3=bit4 · MEM=bit5 · STOP=bit6 · LIGHT=bit7`

**Polarity: pressed = bit `0`, released/idle = bit `1`.** `buffer[4] = TxTasti` verbatim
(`UserMain.c:978`); press clears the bit (`:1369`), release sets it (`:1375`). The no-buttons-pressed
steady state is all-active-bits-`1`.

- A **press** is the transition of an active button's bit `1 → 0` (FR-006). **Unarmed exception
  (#293, `data-model.md` §6b):** a cold panel never transmits a position's first press (clearing an
  already-clear bit fires no change gate), so a position never yet seen released scores on its
  `0 → 1` release transition instead.
- The baseline MUST be taken from the **first observed frame** — `TxTasti` is zero-initialised at
  boot (`UserMain.c:200`) and only reaches the all-`1` idle state after the key-scan fires
  `RELEASED` for untouched keys, so an absolute byte is never a reliable press-state.
- Bits outside the variant's active mask are ignored (FR-014).

> The field-proven legacy app used set-bit-pressed (`ButtonPanelTestService.cs:825`), which against
> this firmware detects the **release** edge (bit returning to `1`). spec-005 detects the **press**
> edge per FR-006; the pressed-bit value is a single named constant (`PressedBit = 0uy`) and the
> direction is confirmed on a real OPTIMUS-XP panel in the Hardware E2E phase.
