# WHO_I_AM bench captures (2026-06-08)

Raw PCAN-View traces backing the `whoIAmFixtures.json` real-capture anchor
(`tests/ButtonPanelTester.Tests/Fixtures/Can/whoIAmFixtures.json`, fixture
`virgin_panel_12v`). Provenance for the load-bearing wire-format regression
guard and for the decision to keep the marketing-variant fixtures synthetic.

## Session

- **Tool**: PCAN-View v6.0.0.1040, Trace tab (record -> connect panel -> stop -> save `.trc`).
- **Adapter**: PEAK IPEH-002022 (PCAN-USB), 250 kbps.
- **Date**: 2026-06-08.

## Files

| File | CAN ID | What it is |
|---|---|---|
| `virgin_whoami.trc` | `0x1FFFFFFF` | A **real WHO_I_AM** broadcast from a virgin panel. The anchor. |
| `optimus_baptized.trc` | `0x000A0441` | A **baptized** OPTIMUS-XP panel's addressed status message — **not** a WHO_I_AM. |
| `eden-xp_baptized.trc` | `0x00030141` | A baptized EDEN-XP panel's addressed status — not a WHO_I_AM. |
| `r-3l-xp_baptized.trc` | `0x000B0481` | A baptized R-3L XP panel's addressed status — not a WHO_I_AM. |

## Why only `virgin_whoami.trc` is a fixture

A WHO_I_AM is the auto-address broadcast on CAN ID `0x1FFFFFFF`, sent only by a
**virgin** (or transient mid-baptism) panel. A fully **baptized** panel is
silent on that broadcast channel and instead chatters on its **assigned**
address (note `byte[1]` of each baptized ID is literally the machine type:
`0A`/`03`/`0B`). Those addressed messages reassemble to a short ~12-byte status
packet with **no UUID field** — they are not WHO_I_AM frames and spec-003's
service filters them out (`CanId = 0x1FFFFFFF`). So the bench cannot supply real
marketing-variant WHO_I_AM frames; the `eden_xp` / `optimus_xp` / `r3l_xp` /
`eden_bs8` fixtures are therefore **synthetic** (real machineType constant +
synthetic UUID), and `virgin_panel_12v` is the only real-capture anchor. That is
sound: one real capture empirically pins the wire **layout** (identical across
all panels), and the marketing fixtures only exercise the layout-independent
`byte -> variant` decode.

## Reassembly of `virgin_whoami.trc`

The 15-byte logical WHO_I_AM is chunked across CAN frames by the STEM serial
protocol (2 control bytes per frame, then payload). Stripping the 2 control
bytes per frame and concatenating gives the SP packet:

```
00 00 FF 01 3F 00 11 00 24 | FF 00 04 17 7C 12 6D 73 08 74 8F 16 09 21 04 | EA 69
   <-- 9-byte SP header -->   <----------- 15-byte WHO_I_AM ----------->     <CRC>
```

The 15-byte WHO_I_AM payload — `FF0004177C126D7308748F16092104` — matches the
firmware `AutoAddressSlave.c` `TX_Values[15]` layout exactly:
`machineType 0xFF` (virgin), `fwType 0x0004` (12 V, big-endian at offsets 1-2),
UUID words `0x177C126D / 0x7308748F / 0x16092104` (big-endian at 3/7/11). Three
byte-identical announcements in the trace confirm a stable read.
