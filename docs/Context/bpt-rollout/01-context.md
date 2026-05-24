# Context — product, hardware, firmware, board variants

## 1. The product

`button-panel-tester` is a Windows bench tool used by STEM technicians
(and the supplier's QA team) to validate button-panel boards over CAN
before they are mounted into one of STEM's four machine models. It is
the new, F#/Avalonia replacement for the legacy WinForms tool
`stem-button-panel-tester`.

### 1.1 Who uses it

- **Supplier-side QA technician.** Receives a batch of pristine boards,
  needs to assert each one works before they ship back to STEM. The
  Monday hand-off is the first time the tool is used in anger by this
  user.
- **STEM bench tech.** Diagnoses returned panels and re-baptizes them
  when the machine they're going into changes.

### 1.2 What it must do, end-to-end

For each panel:
1. Discover it on the CAN bus.
2. Confirm or set its **MachineType** identity (baptism), which picks
   which subset of board features are exercised.
3. Walk the technician through the per-button, per-LED, per-buzzer
   verification that's appropriate for that machine variant.
4. Produce a verdict (pass / fail / inconclusive) and a per-panel
   report suitable for handing back to STEM's QA process.
5. Survive ordinary failure modes — USB unplug, panel reset, dictionary
   stale — without losing in-progress data and without misleading the
   technician.

## 2. The board (single hardware platform)

There is exactly one button-panel board, regardless of which STEM
machine it ends up in. All four marketed variants are the same
electronics; only a sticker on the board differs.

### 2.1 Physical components

| Component | Count | Notes |
|---|---|---|
| Push buttons | 8 | Laid out as a 2×4 grid: top row of 4, bottom row of 4. |
| LEDs | 4 | A horizontal strip in the upper-left corner, alternating Green/Red/Green/Red (i.e. a pair of green and a pair of red). |
| Buzzer | 1 | Driven at fixed frequency per firmware change-log v1.6. |
| MCU | PAC5524 | Qorvo/Active-Semi part. Firmware target. |
| CAN transceiver | (board-integrated) | 250 kbps bus per firmware v5.0. |

### 2.2 Firmware-defined button names

From the firmware header `DigIO.h` in
`~/Downloads/stem-fw-pac5524-tastiera-can-app-*/src/DigIO.h`, enum
`dig_io_key_code_e`:

| Index | Firmware name |
|---|---|
| 0 | `UP` |
| 1 | `DOWN` |
| 2 | `P1` |
| 3 | `P2` |
| 4 | `P3` |
| 5 | `MEM` |
| 6 | `STOP` |
| 7 | `LIGHT` |

These are the labels the UI should display when prompting button
presses. **Do not invent generic labels like "Button 1".**

### 2.3 Firmware-defined outputs

From the same header, enum `dig_io_output_e`:

| Output | Meaning |
|---|---|
| `DIG_IO_OUTPUT_LEDR` | Red LEDs |
| `DIG_IO_OUTPUT_LEDV` | Green LEDs (V = verde) |
| `DIG_IO_OUTPUT_BUZZ` | Buzzer |

The two greens act as one logical channel; the two reds act as one
logical channel. The firmware does not (in v5.3) expose per-LED
control.

## 3. The firmware

### 3.1 Where it lives

The user has the extracted, read-only firmware sources in:

```
~/Downloads/
  stem-fw-pac5524-tastiera-can-app-20037c53be06/   ← panel firmware
  stem-fw-protocollo-seriale-stem-0ac0fb56dde5/    ← STEM protocol stack (shared library, embedded C)
```

### 3.2 Panel firmware version and changelog highlights

`UserMain.c` of the panel firmware is at v5.3 (dated 2026-02-09). The
parts of its changelog that matter for the tester:

- **v5.0** — CAN bus speed pinned at 250 kbps. Eighth button activated
  for the LIGHT function. BS8 panels send button data "as if they were
  Eden" — i.e. the wire format is unified.
- **v4.0** — All variants fused into one firmware with an
  *auto-baptizing address* (the panel learns its identity from the
  EEPROM-stored `MachineType` byte).
- **v3.8** — Dual-keyboard support for Eden (out of scope for the
  tester's single-panel-at-a-time model).
- **v3.2** — Per-key beep support added.
- **v2.2** — CAN inter-chunk delay removed; the protocol-layer now
  paces packets.

The two takeaways: (a) the wire format is uniform across variants,
(b) the variant identity is a single byte in EEPROM, mutable via the
baptism handshake.

### 3.3 Baptism (auto-address) handshake — the canonical state machine

This is the most load-bearing protocol interaction in the whole tool;
get it right and most of the rest is downstream plumbing.

**On panel power-up:**
- If the panel's EEPROM `MachineType` byte is `0xFF` → panel is
  **virgin**; it boots into the auto-address slave state
  `AAS_ANSWER_TO_MASTER` and waits silently for a `WHO_ARE_YOU`.
- If the byte is `0x03 / 0x0A / 0x0B / 0x0C` → panel is **claimed**;
  it broadcasts `WHO_I_AM` with its UUID + current MachineType on a
  staggered timer (UUID-derived delay, up to ~4 s — firmware comment
  in `BaptizeService.cs` legacy file).

**To claim a virgin panel as a specific machine variant:**
1. Tool sends `WHO_ARE_YOU(MachineType=<target>, FirmwareType=0x0004, reset=0)`
   on the AA broadcast listen ID (`0x1FFFFFFF`).
2. Firmware's `AA_Slave_WhoAreYouReceived` writes the byte to EEPROM
   and transitions to `AAS_ANSWER_TO_MASTER`.
3. Panel re-broadcasts `WHO_I_AM` after the UUID-derived delay.
4. Tool's confirmation = receiving the `WHO_I_AM` carrying the target
   MachineType.

**To reset a claimed panel back to virgin:**
1. Tool sends `WHO_ARE_YOU(MachineType=0xFF, FirmwareType=0x0004, reset=1)`
   on the AA broadcast listen ID.
2. Firmware writes `0xFF` to EEPROM, transitions to
   `AAS_ANSWER_TO_MASTER`.
3. CAN-write completing is sufficient confirmation (the firmware does
   not synchronously reply; it main-loop-broadcasts WHO_I_AM after the
   staggered delay).

**Asymmetry to remember:** the baptize path waits for a `WHO_I_AM`
confirmation; the virgin-reset path does not. This is firmware
behaviour, not a tool bug.

### 3.4 Protocol stack layering

From `~/Downloads/stem-fw-protocollo-seriale-stem-*/STEM Protocol/`,
the layering is:

```
┌─────────────────────────────┐
│  SP_Application             │   commands, variable read/write
├─────────────────────────────┤
│  SP_Presentation_Layer      │   encoding, CRC
├─────────────────────────────┤
│  SP_Transport_Layer         │   multi-chunk reassembly
├─────────────────────────────┤
│  SP_Network_Layer           │   addressing, routing (SP_Router)
├─────────────────────────────┤
│  CanDataLayer / SerialDataL │   physical-frame ↔ logical-packet
│  / FDCanDataLayer / Cloud   │   (we use CanDataLayer)
└─────────────────────────────┘
                │
                ▼
        PEAK PCAN-USB driver
```

Plus a `SP_Telemetry` module on top for streaming variable updates.

The tester is a client of `SP_Application`. The auto-address handshake
sits *below* the layering — it uses raw CAN frames on the AA listen ID
and is implemented by `AppClient.c` / `AppServer.c` /
`AutoAddressSlave.h`, not by the SP_* stack.

## 4. The four board variants (tool-side projection)

The board is uniform; the marketed machine is what determines which
features the tester should exercise. Hardcode this table — the user
has confirmed it should live as in-code constants, not a config file.

| Marketing name | DIS part # | `MachineType` byte | Active buttons | LEDs exercised? | Buzzer exercised? |
|---|---|---|---|---|---|
| **EDEN-XP** | DIS0023789 | `0x03` | all 8 | yes | yes |
| **OPTIMUS-XP** | DIS0025205 | `0x0A` | 4 of 8 (mask: `XOXOXOXO`) | **no** | **TBD** |
| **R-3L XP** | DIS0026166 | `0x0B` | all 8 | yes | yes |
| **EDEN-BS8** | DIS0026182 | `0x0C` | all 8 | yes | yes |

**Reading the OPTIMUS-XP mask.** The string `XOXOXOXO` is two parallel
rows of 4 buttons, the upper row written first then the lower row
appended (X = inactive, O = active):

```
Upper row buttons (indexes 0..3): X O X O    →  active: index 1, index 3
Lower row buttons (indexes 4..7): X O X O    →  active: index 5, index 7
```

Mapping to firmware names: actives are `DOWN`, `P2`, `MEM`, `LIGHT`.
**Confirm with the user during `/speckit.clarify` of spec-004** — the
index-to-physical-position-to-firmware-name mapping is the kind of
thing it's cheap to get wrong.

### 4.1 Suggested F# type sketch (not normative)

```fsharp
namespace Stem.ButtonPanelTester.Core

type MachineType = MachineType of byte         // 0x03, 0x0A, 0x0B, 0x0C
type ButtonIndex = ButtonIndex of int          // 0..7

type BoardVariant = {
    MarketingName : string                     // "EDEN-XP"
    PartNumber    : string                     // "DIS0023789"
    MachineType   : MachineType
    ActiveButtons : ButtonIndex Set            // {0..7} or {1;3;5;7}
    HasLeds       : bool
    HasBuzzer     : bool
}

module BoardVariant =
    let knownVariants : BoardVariant list = [
        { MarketingName = "EDEN-XP";    PartNumber = "DIS0023789"
          MachineType = MachineType 0x03uy
          ActiveButtons = Set.ofList [0..7]
          HasLeds = true;  HasBuzzer = true }
        { MarketingName = "OPTIMUS-XP"; PartNumber = "DIS0025205"
          MachineType = MachineType 0x0Auy
          ActiveButtons = Set.ofList [1; 3; 5; 7]
          HasLeds = false; HasBuzzer = (* TBD *) true }
        { MarketingName = "R-3L XP";    PartNumber = "DIS0026166"
          MachineType = MachineType 0x0Buy
          ActiveButtons = Set.ofList [0..7]
          HasLeds = true;  HasBuzzer = true }
        { MarketingName = "EDEN-BS8";   PartNumber = "DIS0026182"
          MachineType = MachineType 0x0Cuy
          ActiveButtons = Set.ofList [0..7]
          HasLeds = true;  HasBuzzer = true }
    ]
```

Sketch only — the receiving Claude will write the real shape during
spec-003's `/speckit.plan`, following the project's F# conventions
(records vs DUs, etc.) per the `dotnet` skill.

## 5. Reference repos and where they help

| Repo / path | Use it for | Don't trust it for |
|---|---|---|
| `~/Downloads/stem-fw-pac5524-tastiera-can-app-*/` | Panel firmware. Canonical for: button names, output names, baptism state machine, CAN bitrate, button/LED/buzzer semantics. | Anything not in the firmware repo. |
| `~/Downloads/stem-fw-protocollo-seriale-stem-*/` | STEM protocol stack reference. Canonical for: wire layout, CRC, multi-chunk reassembly, auto-address handshake plumbing. | High-level API ergonomics. |
| `/code/stem/stem-communication` | The .NET-side renovated protocol stack. The library we will vendor-copy from. See `02-vendoring-plan.md`. | Public API stability — it's pre-1.0. |
| `/code/stem/stem-device-manager` | Recently renovated consumer of the protocol stack. Reference for: how to layer Core/Services/Infrastructure, how to wire DI, how to abstract `ICommunicationPort`. | Specific bug-for-bug behaviour. |
| `/code/stem/stem-button-panel-tester` | Legacy WinForms tool. Reference for: the test-flow state machine, baptism logic, heartbeat-loss recovery, the four DIS part numbers. | Anything we can read from firmware directly. **It carries bugs and dead branches the user has not had time to clean up.** |
| `/code/stem/standards` (v1.9.0 pinned) | All style/architectural rules. Already vendored under `docs/Standards/` in the project. | n/a — this is the authoritative styling. |

## 6. Pre-existing project artifacts to read on the Windows side

Before writing spec-002, the receiving Claude should read:

- `/code/stem/button-panel-tester/CLAUDE.md` — repo-specific notes.
- `/code/stem/button-panel-tester/.specify/memory/constitution.md` — the
  governing principles (Lean spec ahead of F# code; manual fakes only;
  no mocking libs; etc.).
- `/code/stem/button-panel-tester/specs/001-fetch-dictionary/spec.md`
  — the template shape that 002–007 should follow.
- `/code/stem/button-panel-tester/specs/001-fetch-dictionary/plan.md`
  — for the planning-output shape that `/speckit.plan` should target.
- `/code/stem/button-panel-tester/docs/Standards/` — the v1.9.0 standards
  inline-copy.

## 7. Hardware reality on the bench (Monday onward)

- **PEAK PCAN-USB IPEH-002022** plugged into the docking station.
- **12 pristine panels** — never baptized; expect virgin behaviour
  (`MachineType=0xFF`, silent until `WHO_ARE_YOU`).
- **2 known-bad panels** — for negative-path validation. Don't know
  what's wrong with them yet; treat their failure modes as data once
  they surface.
- **No automated optical/audio confirmation rig.** All LED and buzzer
  verification is technician-confirmed via the UI.
