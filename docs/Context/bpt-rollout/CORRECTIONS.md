# CORRECTIONS — bpt-rollout audit log

**Recorded:** 2026-05-24, immediately before spec-002 drafting.

This file records every claim in the original four briefing files
([`00-INDEX.md`](./00-INDEX.md), [`01-context.md`](./01-context.md),
[`02-vendoring-plan.md`](./02-vendoring-plan.md),
[`03-roadmap.md`](./03-roadmap.md)) that the firmware/code audit
contradicted or replaced. Each entry cites the exact firmware or
repository file that overturns the original claim.

Audit scope (read 2026-05-24):

- Panel firmware: `stem-fw-pac5524-tastiera-can-app-20037c53be06/src/`.
- Protocol firmware: `stem-fw-protocollo-seriale-stem-0ac0fb56dde5/STEM Protocol/`.
- Four machine motherboards: `stem-fw-stm32h7-{eden,eden-bs8,optimus-xp}-app-*/`, `stem-fw-pac5524-r-3l-xp-app-*/`.
- Two candidate .NET vendor sources: `stem-communication/`, `stem-device-manager/`.
- Dictionary API server: `stem-dictionaries-manager/src/Infrastructure/DatabaseSeeder.cs` and route table.

---

## C1. Virgin panels broadcast WHO_I_AM; claimed panels are silent

**Original claim** (01-context.md §3.3, "Baptism (auto-address) handshake"):

> If the panel's EEPROM `MachineType` byte is `0xFF` → panel is
> **virgin**; it boots into the auto-address slave state
> `AAS_ANSWER_TO_MASTER` and waits silently for a `WHO_ARE_YOU`.
>
> If the byte is `0x03 / 0x0A / 0x0B / 0x0C` → panel is **claimed**;
> it broadcasts `WHO_I_AM` with its UUID + current MachineType on a
> staggered timer.

**Firmware truth** — `AutoAddressSlave.c` slave init logic (lines
136–143) classifies the panel by EEPROM contents:

- `IDMachineType == 0xFF || IDBoardNumber == 0xFF` → state `AAS_STARTUP`.
- otherwise → state `AAS_STAND_BY`.

The main task at lines 165–183 broadcasts `WHO_I_AM` to
`SRID_BROADCAST` (`0x1FFFFFFF`) every `2000 + (sum(UUID) mod 4000)` ms
**when the state is `AAS_STARTUP` or `AAS_ANSWER_TO_MASTER`**. In
`AAS_STAND_BY` the panel is silent.

The original claim has the broadcast/silent classification inverted.

**What this means for the specs:**

- Spec-002's passive observer is genuinely viable for the supplier
  bench scenario: all 12 pristine virgin panels self-announce within
  ~6 s without the tester transmitting anything.
- Spec-002's UX gap for previously-claimed panels (silent in
  `AAS_STAND_BY`) is real. A claimed panel only re-appears on the
  bus after spec-003's reset-to-virgin.

---

## C2. Baptism requires `reset=1` AND a follow-up SET_ADDRESS

**Original claim** (03-roadmap.md §spec-003):

> Tool sends `WHO_ARE_YOU(MachineType=<target>, FirmwareType=0x0004, reset=0)`
> on the AA broadcast listen ID (`0x1FFFFFFF`).

**Firmware truth** — `AutoAddressSlave.c:230–255`
(`AA_Slave_WhoAreYouReceived`):

```c
if (MyData.Descriptor.IdFWType == Data.IdFWType) {
    if (Data.ResetAddress) {                    // ← only when reset == 1
        MyData.EEPROMCopy->IDMachineType = Data.IdMachineType;
        MyData.EEPROMCopy->MotherBoardAddress = srid;
        ...
        *MyData.UpdateEEPROM = true;
    }
    if (MyData.Data[0].SP_Address == 0xFFFFFFFF) {
        MyData.State = AAS_ANSWER_TO_MASTER;
    }
}
```

With `reset == 0`, the handler is a no-op for baptism intent: it
nudges already-virgin panels into the answering state but writes
nothing.

**Firmware truth (cont.)** — the full master sequence
(`AutoAddressMaster.c:140–195`) is three steps:

1. Master broadcasts `WHO_ARE_YOU(machineType, fwType, reset=1)` — 4 B.
2. Slave broadcasts `WHO_I_AM(machineType, fwType, UUID0..2)` — 15 B
   from `AutoAddressSlave.c:175–181`.
3. Master broadcasts `SET_ADDRESS(UUID0..2, SP_Address)` — 16 B from
   `AutoAddressMaster.c:260–265`. Slave receives and validates UUID
   in `AutoAddressSlave.c:263–292`, then transitions to
   `AAS_STAND_BY` (silent operational state).

**Firmware truth (cont.)** — without SET_ADDRESS the panel does
**not** emit application-layer keyboard frames. Panel `UserMain.c:990–993`
guards transmission on three conditions, the third only set by
SET_ADDRESS:

```c
if ((Boot_IsOngoing() == false) &&
    (eepromCfgCopy.EEPROM_Datas.MotherBoardAddress != 0xFFFFFFFF) &&
    (eepromCfgCopy.EEPROM_Datas.IDMachineType != 0xFF) &&
    (eepromCfgCopy.EEPROM_Datas.IDBoardNumber != 0xFF))   // ← only set by SET_ADDRESS handler
    SP_Router_SendPacket(...);
```

**What this means for the specs:**

- Spec-003's `IProtocolService.Baptize` must do all three steps:
  send `WHO_ARE_YOU(reset=1)`, listen for `WHO_I_AM` to capture the
  UUID, send `SET_ADDRESS(UUID, sp_address)`. Single-step "send
  WHO_ARE_YOU and wait" is silent failure: the EEPROM is not
  written and the panel still looks virgin to the bus.
- Spec-004's button-press test is downstream of spec-003 — physical
  presses on a `reset=1`-only panel produce no CAN traffic.

**Tool-side verification (2026-07-23, #295):** the shipped baptism always
sends `WHO_ARE_YOU` with `reset=1` (`Core/Can/WhoAreYouFrame.fs:18` — "This
feature always sends `Reset = true`", encoded at `:70`), and the first panel
baptized by this tool stored its `MotherBoardAddress` and heartbeated
button-state on the tool SRID — live-confirmed at the #253 bench. §C2 is
satisfied by this tool; the silent-panel failure mode remains possible only
for panels addressed by some other master without `reset=1`.

---

## C3. OPTIMUS-XP active button set is `{DOWN, P1, P3, MEM}`, not `{DOWN, P2, MEM, LIGHT}`

**Original claim** (01-context.md §4):

> | OPTIMUS-XP | DIS0025205 | `0x0A` | 4 of 8 (mask: `XOXOXOXO`) | **no** | **TBD** |
>
> Mapping to firmware names: actives are `DOWN`, `P2`, `MEM`, `LIGHT`.

**Firmware truth** — panel-side encoding is uniform across all
variants (`stem-fw-pac5524-tastiera-can-app-*/src/UserMain.c:215–246`):

| Panel name | Bit | Mask |
|---|---|---|
| UP | 0 | `0x01` |
| DOWN | 1 | `0x02` |
| P1 | 2 | `0x04` |
| P2 | 3 | `0x08` |
| P3 | 4 | `0x10` |
| MEM | 5 | `0x20` |
| STOP | 6 | `0x40` |
| LIGHT | 7 | `0x80` |

The motherboard sends this byte uniformly; each variant's firmware
decides which bits it cares about. OPTIMUS-XP's motherboard
(`stem-fw-stm32h7-optimus-xp-app-*/STEM/Plane/Optimus.h:255–263`)
reads only:

```c
enum user_can_keys_e {
    USR_CAN_KEY_LIGHT      = 1,   // panel bit 1 → panel DOWN
    USR_CAN_KEY_SUSPENSION = 2,   // panel bit 2 → panel P1
    USR_CAN_KEY_ALL_UP     = 4,   // panel bit 4 → panel P3
    USR_CAN_KEY_STOP       = 5,   // panel bit 5 → panel MEM
};
```

The active set is therefore panel positions `{1, 2, 4, 5}` =
`{DOWN, P1, P3, MEM}`. The "XOXOXOXO" mask in the original is wrong.

**What this means for the specs:**

- Spec-004's `BoardVariant.ActiveButtons` for OPTIMUS-XP is the set
  `{1, 2, 4, 5}` (panel-position indices).
- Spec-004's per-variant labelling needs a clarify pass:
  panel-position names (UP/DOWN/P1.../LIGHT) are not what the
  technician sees on an OPTIMUS-XP panel decal (Light / Suspension /
  All-Up / Stop). The original claim that firmware names are
  sufficient for the prompt UI does not survive contact with the
  per-variant semantic mapping.
- `HasBuzzer` for OPTIMUS-XP remains TBD. None of the motherboard
  audits showed the master writing the panel's `BUZZ` output
  variable, but a dedicated audit pass belongs in spec-005.

---

## C4. Vendoring source switched: `stem-device-manager`, not `stem-communication`

**Original claim** (02-vendoring-plan.md):

> Vendor-copy from `stem-communication` into the tester's
> Infrastructure layer and treat the copy as frozen. ← **chosen path**.

**Audit finding** — `stem-communication/Drivers.Can/ISSUES.md`
records ten open issues, including:

- `CAN-003` (Alta): race condition in `PcanHardware.InitializeAsync`.
- `CAN-017` (Media, Alto impact): no timeout on incomplete chunk
  reassembly → orphan-chunk memory leak.
- `CAN-005` (Media): `CanFrame.Data` not validated against the 8-byte
  CAN limit; `DLC` silently clamps.
- `CAN-006` (Media): `SetFilterAsync(filterId, filterMask)` accepts
  the mask but **ignores it** — hardware filter is broken.
- `CAN-007` (Bassa, Medio impact): `ReceiveAsync` busy-waits with
  `Task.Delay(1)` — ~1 ms RX latency floor.

Plus 74 open issues across the protocol layers, and two cross-cutting
items (T-014 NetInfo codec unification, T-015 chunking architecture
rework) explicitly marked in transition. The ~2602 tests are
unit-only — the library has zero integration consumers, per its
author's own assessment.

**Replacement plan:** vendor from `stem-device-manager` instead.
The relevant subset (`Core/Interfaces/*`, `Core/Models/*`,
`Services/Protocol/*`, `Infrastructure.Protocol/Hardware/{CanPort, PCANManager, IPcanDriver, …}`)
is ≈ 2000 LOC, in production against the same panel hardware for
~2 years. `stem-device-manager/CLAUDE.md` explicitly slates Phase 5
for the same eventual `Stem.Communication` NuGet migration that
bpt-context anticipated — the device manager will be the integration
proving ground, and the tester migrates only after it does.

**What this means for the specs:**

- Spec-002 introduces a sibling C# project
  `ButtonPanelTester.Infrastructure.Protocol.csproj` holding the
  vendored copy. This is a deliberate deviation from the LANGUAGE
  standard's F# default, documented under
  `docs/STOPGAP_VENDORED_CAN_STACK.md` per Constitution Principle VI.
- The vendoring discipline from 02-vendoring-plan.md (verbatim copy,
  `VENDOR.md` with upstream SHA, namespace rename only when needed,
  pre-commit hash check) still applies — just to a different upstream.
- One local modification at vendor time is justified: add
  `CancellationTokenSource` + `IAsyncDisposable` to `PCANManager` so
  the background read/monitor tasks stop cleanly on dispose. Upstream
  PR back to `stem-device-manager` accompanies the vendor commit so
  the local modification is bounded.
- Known unfixed limitations carried over from the upstream (CRC not
  validated on RX, no chunk-reassembly timeout, 5 ms TX throttle):
  acceptable for spec-002, revisited by spec-007 (robustness +
  forensic logging) where their consequences actually bite.

---

## C5. Protocol commands + protocol addresses are not in spec-001's dictionary fetch

**Original silence:** none of the four bpt-context files addressed
how the tester would obtain the STEM protocol command codes
(`SP_APP_CMD_AA_WHO_ARE_YOU` etc.) or the per-board protocol-address
mapping needed for `PacketDecoder` (vendored from
stem-device-manager) to resolve commands and senders semantically.

**Audit finding:**

- `stem-dictionaries-manager/src/Infrastructure/DatabaseSeeder.cs`
  seeds both the `Commands` table (with the full AA family at
  codes `0x0023`–`0x0027` plus variable read/write at `0x0001/0x0080`
  and `0x0002/0x0081`) and the `Boards` table (with
  per-device, per-board protocol addresses).
- The server exposes both at `GET /api/commands` and
  `GET /api/devices/{id}/boards`.
- spec-001's BPT-side fetch (`HttpDictionaryProvider`) calls
  `/api/dictionaries/{id}/resolved` only, which returns **variables**.
  Commands and addresses are unreached.

**Decision (recorded 2026-05-24):** hardcode the ~15 commands and
the broadcast address tester-side, in F# modules under
`ButtonPanelTester.Core.Protocol.{KnownStemCommands,KnownProtocolAddresses}`,
fed to `PacketDecoder` at composition time alongside the
spec-001-fetched variables. Stopgap waiver per Constitution Principle
VI. The migration to fetched + cached protocol metadata is preserved
exactly because `PacketDecoder`'s constructor signature does not
change — only the composition root swaps the source.

**Tracking issue:** to be opened against `button-panel-tester` for
the fetch migration (estimated ~450 LOC + Lean module + docs ≈ one
working day, deferred for schedule).

**Scope update (2026-05-29, spec-002 lifecycle capstone [#154](https://github.com/luca-veronelli-stem/button-panel-tester/issues/154)):** the hardcoded-metadata stopgap (`KnownStemCommands` / `KnownProtocolAddresses`) is confirmed load-bearing through spec-002 (CAN-link lifecycle) and spec-003 (passive WHO_I_AM discovery — the decode path consumes the hardcoded command codes). The fetch migration is therefore scoped to **spec-004+** (button-press testing), where fetched-vs-hardcoded protocol metadata first materially affects behaviour. `PacketDecoder`'s constructor signature is unchanged, so the eventual swap stays a composition-root change only.

**What this means for the specs:**

- Spec-002 introduces the two `Known*` F# modules and references the
  stopgap waiver.
- Spec-001's contract and fetch path stay exactly as-is — the
  protocol metadata is not part of the dictionary domain in the
  tester's view of the world.

---

## Items unchanged

The following bpt-context claims survive the audit intact and remain
load-bearing for spec-002+:

- The four marketed machine variants and their `MachineType` bytes
  (`0x03` EDEN-XP, `0x0A` OPTIMUS-XP, `0x0B` R-3L XP, `0x0C` EDEN-BS8):
  confirmed against each motherboard's `STEM/UserMain.h` `ID_MACHINE_TYPE`
  constant.
- Panel firmware type `0x0004`: confirmed against each motherboard's
  `AA_Master_InitDevice(ID_MACHINE_TYPE, 4, …)` call.
- CAN bitrate 250 kbps: confirmed against panel `UserMain.c:400`
  (`CDLInit.BaudRate = 250000`).
- Single hardware platform regardless of variant: confirmed by
  identical panel firmware shipping in all four motherboard contexts.
- Constitution principles (Lean spec first, ports + adapters,
  manual fakes, stopgap discipline) still govern.
- Standards baseline v1.9.0 (per `.stem-standard.json` and the
  repo's `CLAUDE.md`).
- "Firmware is canonical, tools are reference" — strongly reinforced
  by the C2 and C3 findings above.
