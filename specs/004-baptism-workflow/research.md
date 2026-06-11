# Phase 0 Research: Baptism Workflow

**Spec**: [spec.md](./spec.md) | **Date**: 2026-06-11

Findings R1–R9, grounded in: the on-disk protocol firmware source (read-only reference at
`C:\Users\LucaV\Source\Repos\firmwares\stem-fw-protocollo-seriale-stem-ae06480f0c36\…\STEM Protocol\`,
same tree the CORRECTIONS.md §C1–C2 audit cites), the shipped spec-002/003 contracts, and the
shipped codebase. Each item closes with a **Decision**.

## R1 — Auto-address command codes and message shapes (firmware-verified)

The three master-sequence messages are SP_APP application commands, enumerated in the firmware's
`SP_Application.h` command enum (counted from `SP_APP_CMD_ID_VER = 0`; cross-checked against the
shipped WHO_I_AM RX filter `0x00:0x24` in `WhoIAmReassemblyObserver.fs:46-47`):

| Message | cmdHigh:cmdLow | App payload | Direction (tool) |
|---|---|---|---|
| `SP_APP_CMD_AA_WHO_ARE_YOU` | `0x00:0x23` | 4 B | TX |
| `SP_APP_CMD_AA_WHO_I_AM` | `0x00:0x24` | 15 B | RX (shipped, spec-003) |
| `SP_APP_CMD_AA_SET_ADDRESS` | `0x00:0x25` | 16 B | TX |

**WHO_ARE_YOU payload (4 B)**, as the slave parses it (`AutoAddressSlave.c:230-235`):
`[0]` machineType (u8) · `[1..2]` fwType (u16 big-endian) · `[3]` reset flag (whole byte, non-zero = set).

**SET_ADDRESS payload (16 B)**, as the slave parses it (`AutoAddressSlave.c:263-273`):
`[0..3]` UUID0 · `[4..7]` UUID1 · `[8..11]` UUID2 · `[12..15]` SP_Address. The slave reads
SP_Address with an explicit `OSdwordSwap` (big-endian on the wire); the UUID words are read by
direct cast with **no** swap. Rather than re-deriving per-field endianness, the tool's invariant is
**byte-echo**: the 12 UUID bytes transmitted in SET_ADDRESS are byte-for-byte the UUID bytes
received in the panel's WHO_I_AM (positions `[3..14]` of its payload). Since our parser/encoder
pair uses one fixed convention, `encode (parse bytes) = bytes` makes the slave's equality check a
comparison of identical byte sequences regardless of labeling. The contract pins this invariant.

**Packetization** (vendored stack, per spec-003's
[who-i-am-wire-format.md](../003-panel-discovery/contracts/who-i-am-wire-format.md)): transport
packet = `cryptFlag(1) + senderId(4) + lPack(2) + cmd(2) + payload(N) + CRC16(2)`, chunked into
6-byte pieces, each CAN frame = `arbId_LE(4) + NetInfo(2) + chunk(≤6)` on broadcast arbId
`0x1FFFFFFF`. So WHO_ARE_YOU = 15-byte packet = **3 CAN frames**; SET_ADDRESS = 27-byte packet =
**5 CAN frames**. The firmware master broadcasts both (`AutoAddressMaster.c:160,265`,
`SRID_BROADCAST`).

**Decision**: synthesize the two commands as built-in `Command` records (`0x00:0x23`, `0x00:0x25`)
and transmit through the vendored `IProtocolService.SendCommandAsync`, which already implements
packet building, CRC16, chunking, NetInfo framing, and the port write
(`ProtocolService.cs:87-114`). No new framing code.

## R2 — Slave WHO_ARE_YOU semantics: the fwType gate is load-bearing

The slave handler (`AutoAddressSlave.c:237-251`) wraps **everything** in
`MyData.Descriptor.IdFWType == Data.IdFWType` — a WHO_ARE_YOU whose fwType does not match the
panel's hardware is ignored entirely. Inside the gate, with `reset=1` (unconditional on current
machineType and on claim state — a silent claimed panel processes it):

- EEPROM `IDMachineType` ← received machineType; `MotherBoardAddress` ← sender's srid
  (the tool becomes the panel's master — already a spec assumption);
- RAM `SP_Address` ← `0xFFFFFFFF`, EEPROM flagged for update;
- announce timer restarted → panel enters `AAS_ANSWER_TO_MASTER` and announces the **newly
  written** identity on its 2–6 s cadence.

Two consequences the spec abstracts away:

1. **Baptize must echo the selected panel's announced fwType.** The shipped `PanelObservation`
   record (`Core/Can/PanelObservation.fs:44-47`) drops `fwType` after decode. **Decision**: extend
   `PanelObservation` with an additive `FwType` field carried through from the already-parsed
   `WhoIAmFrame`. This is a data extension, not a semantics change — coalesce/prune/clear are
   untouched (the Lean `PanelsOnBus`/`Pruning` models key on uuid/lastSeen and are unaffected).
   *Alternative rejected*: a baptism-side `uuid → fwType` cache duplicating discovery's lifecycle
   (own prune/clear) — two sources of truth for one fact.
2. **Reset cannot know the silent panel's fwType.** **Decision**: Reset-to-virgin broadcasts
   WHO_ARE_YOU(`0xFF`, fwType, `reset=1`) once per known firmware type — `0x0004` (12 V) and
   `0x000F` (24 V) — as one technician action. Each broadcast only matches panels of its hardware
   class; the non-matching one is ignored by construction. Success = all writes complete (FR-010).
   *Alternative rejected*: pinning 12 V only (the roadmap's pre-audit sketch) — a 24 V panel would
   be unrecoverable by the tool; spec-003 bench fixtures already include 24 V panels.

## R3 — SET_ADDRESS semantics and the success signal

`AutoAddressSlave.c:275-292`: the slave accepts SET_ADDRESS iff **all three UUID words match**
its own (no machineType/fwType check), then stores `SP_Address`, derives
`IDBoardNumber = SP_Address & 0x3F`, writes EEPROM, and transitions to `AAS_STAND_BY` (silent).
**It never replies** — the tool's success signal is write completion of the broadcast (FR-006),
optionally corroborated by the FR-007 silence watch.

**SP_Address value**: the firmware master computes it as
`SP_App_Calculate_ID(0, machineType, fwType, boardNumber)` (`SP_Application.c:366-373`):
`NETWORK<<24 | MACHINE<<16 | (BOARD_TYPE & 0x3FF)<<6 | (BOARD_NUMBER & 0x3F)`. The tool assigns
board number 1 of the chosen variant (spec assumption). Cross-check: EDEN-XP (0x03) 12 V (0x0004)
board 1 → `0x00030101`, exactly the "Keyboard 1" address already hardcoded in the vendored
`DeviceVariantConfig` — the scheme is confirmed against shipped constants.

**Decision**: compute SP_Address with this formula from (chosen variant, panel's announced fwType,
board 1); document it in the wire-format contract.

## R4 — Re-announcement timing and the 6 s budget

Post-WHO_ARE_YOU announce delay = `2000 + (sum of UUID words mod 4000)` ms ∈ [2, 6] s, timer
restarted by the handler (firmware findings, `.llm/issue-212-baptism-firmware-findings.md`).
Worst-case UUIDs (~2–3 % of the space) answer at ~6.0 s — at the edge of the settled 6 s budget,
i.e. near-zero margin. **The budget is a settled scope pin and is not revisited**; the spec's
mitigation already covers the tail: structured wait-timeout (FR-005), the late re-announcement
stays visible and claimable, and re-running Baptize completes the claim (edge case in spec.md).
The wait is keyed to (selected UUID, chosen variant); announcements from any other UUID never
satisfy it.

## R5 — The TX port (first transmit feature; Principle III)

Core today exposes a **read-only** bus: `ICanFrameStream` and `IWhoIAmObserver` are RX-only;
`ICanLink` is lifecycle-only (`Core/Can/Ports.fs:33-97`). Spec-002 FR-014 / spec-003 FR-009
explicitly forbade transmission; spec-004 owns the first TX boundary.

**Decision**: one new Core port at the domain level —

```fsharp
type IMasterSequenceTransmitter =
    abstract SendWhoAreYouAsync: machineType: byte * fwType: uint16 * reset: bool
                                 * CancellationToken -> Task
    abstract SendSetAddressAsync: uuid: PanelUuid * spAddress: uint32
                                  * CancellationToken -> Task
```

Production adapter `ProtocolMasterSequenceTransmitter` (Infrastructure/Can, F#) encodes the
payloads (R1) and delegates to the vendored `IProtocolService.SendCommandAsync` over the shared
`CanPort`. Virtual adapter `InMemoryMasterSequenceTransmitter` (Tests/Fakes) records every send
and scripts failures. Transmission failure surfaces as the adapter's exception, mapped by the
baptism service to the `TransmissionFailure` outcome.

*Alternatives rejected*: (a) a raw `ICanFrameTransmitter` port — would pull SP_APP packet framing
into Core/Services, duplicating the hardened vendored chain; (b) Services consuming the vendored
`IProtocolService` directly — a Services → Infrastructure reference, violating the onion and
Principle III.

## R6 — Baptism state machine and failure-detection inputs

The attempt FSM (full transition table in [data-model.md](./data-model.md)):
`Idle → ClaimSent → AwaitingAnnounce → Assigning → terminal`, with exactly six terminal outcomes
(FR-005). Each failure input maps to an existing observable — no polling:

| Outcome trigger | Source |
|---|---|
| Matching re-announcement | `IWhoIAmObserver.WhoIAmObserved` (uuid + variant match, FR-004) |
| Wait timeout (6 s) | `IClock` deadline + tick (FrozenClock-testable, `PanelDiscoveryService.RunPruneTick` precedent) |
| Unexpected variant | same observer event, uuid match + variant mismatch |
| Panel disappeared | `IPanelDiscoveryService` panels-changed (selected uuid pruned) |
| Link lost | `ICanLinkService.LinkStateChanged` leaving `Connected` |
| Transmission failure | `IMasterSequenceTransmitter` send fault |

FR-007 (post-success warning) is a volatile watch: after success, the same uuid heard on
`IWhoIAmObserver` within the 15 s pruning window raises the warning. No persistence (FR-013).

## R7 — Enablement guards as pure Core predicates

Baptize enabled ⇔ link `Connected` ∧ exactly one panel announcing ∧ that panel selected (FR-002).
Reset enabled ⇔ link `Connected` ∧ at most one panel announcing (FR-008). **Decision**: pure
functions in Core over `(CanLinkState, announcing count, selection)` returning
enabled-or-explanation, so SC-005 is FsCheck-coverable as an iff-property and the GUI merely
renders the result.

## R8 — Audit records over the established logging pattern

`ILogger<T>` with structured fields is the house pattern (`CanLinkLogging.fs` +
`CanLinkService.fs:175-187`). **Decision**: a `BaptismLogging` module emitting exactly one
structured record per attempt — action (`Baptize`/`Reset`), variant (baptize only), uuid (when
known), outcome, step reached, timestamps — no operator attribution (FR-012; spec-007 territory).
Declined-at-confirmation reset attempts also log (SC-006).

## R9 — Lean Phase 3 mapping (Principle I)

Phase 1 ↔ spec-001, Phase 2 ↔ specs-002+003; baptism opens
`lean/Stem/ButtonPanelTester/Phase3/` (umbrella `Phase3.lean`). Modules and theorems are listed in
the plan's Constitution Check; the FSM model mirrors R6 and the data-model transition table.
House discipline per spec-003: Lean spec → FsCheck/xUnit → F#, no `sorry`, no custom axioms;
theorem + property + implementation land in bisect-safe commit groups.
