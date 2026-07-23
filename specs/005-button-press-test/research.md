# Phase 0 Research: Button-Press Test (Input Side)

**Spec**: [spec.md](./spec.md) | **Date**: 2026-06-22

Findings R1–R10, grounded in: the panel firmware source (read-only reference at
`C:\Users\LucaV\Source\Repos\pac5524-tastiera-can-app\main\src\UserMain.c`, the same panel
firmware the CORRECTIONS.md §C3 audit cites), the field-proven legacy WinForms app
(`C:\Users\LucaV\Source\Repos\stem-button-panel-tester`, reference only — not portable), the
shipped spec-002/003/004 contracts, and the shipped codebase. Each item closes with a
**Decision**. The two load-bearing pins the orchestrator flagged — the button-state variable +
command code (R1/R6) and the bit polarity (R2) — are settled against firmware ground truth here,
not guessed.

## R1 — Button-state wire format (firmware-verified)

The panel reports its key state as an **SP_APP `VAR_WRITE`** the panel transmits unsolicited on a
change-plus-periodic cadence. App-layer buffer built in `UserMain.c:429–449`:

| Byte | Value | Meaning |
|---|---|---|
| `[0]` | `0x00` | command high |
| `[1]` | `0x02` | command low = `SP_APP_CMD_ID_VAR_WRITE` (`SP_Application.h`; `UserMain.c:430`) |
| `[2]` | `0x80` | variable address high |
| `[3]` | `var_low` | variable address low (`UserMain.c:446` = `IDBoardNumber − 1`; `:1003,:440` = `0x73` for EDEN/BS8 keyboard-2) |
| `[4]` | `bitmap` | key-state byte = `TxTasti` (`UserMain.c:978`) |

So the command is `0x00:0x02` and the variable address is `0x80NN` — `0x8000` for a standard
board-1 panel (the single-panel bench case), `0x8073` for an EDEN/BS8 second keyboard. Transmit is
gated on the panel being addressed (`UserMain.c:990–993`: `MotherBoardAddress ≠ 0xFFFFFFFF ∧
IDMachineType ≠ 0xFF ∧ IDBoardNumber ≠ 0xFF`) — i.e. only **after baptism** (spec-004), which the
spec already assumes. The panel transmits when `TxTasti ≠ TxTastiOld` (`UserMain.c:973`) plus a
periodic refresh — so the tool sees both edges and a steady-state heartbeat.

**Refresh cadence is DUAL-RATE** (corrected 2026-07-20, issue #293 — the earlier "slow periodic
refresh" was left unquantified and was then mis-calibrated as a flat ~182 ms). The period is chosen
per transmission from the *latched* bitmap (`UserMain.c:1013–1020`):

| Condition after each send | Constant | Wall-clock |
|---|---|---|
| `TxTastiOld ≠ 0` | `TEMPO_CAN_VELOCE` = 150 (`:120`) | **≈ 188 ms** |
| `TxTastiOld = 0`, ramp done (`attesaCanLento > 10`) | `TEMPO_CAN_LENTO` = 10000 (`:125`) | **≈ 12.5 s** |
| `TxTastiOld = 0`, ramp | — | 11 fast frames first |

One tick = one `User_Callback` = **1.25 ms** (4 kHz ISR, `UserMain.h:127–129`
`PERIOD_VALUE_GA = 4000` / `PRESCALER_VALUE_GA = GPTCTL_PS_DIV4`, divided by the prescaler-5 reload at
`UserMain.c:950–957`; the inline "il callback rimane a 1 ms" comment at `:954` is stale). So
151 × 1.25 ms = 188.75 ms, against a measured 186.7 ms.

`TxTasti` is a zero-init global (`UserMain.c:200`) and its bits **latch**: press clears
(`&= ~keysMask`, `:1369`), release sets (`|= keysMask`, `:1375`). Consequences:

- A **cold, never-touched** baptized panel sits at `TxTasti = 0` ⇒ first frame ≈ 12.5 s after boot,
  then ≈ 12 frames at ≈ 188 ms, then **≈ 12.5 s forever**.
- After **any** button has been pressed *and released* once, its bit stays 1 ⇒ `TxTastiOld ≠ 0` ⇒
  **≈ 188 ms forever**. This is normal steady state for a panel in service.
- A fast cadence therefore does **not** imply a button is held; an all-keys-held panel looks *slower*.

Ground-truth traces `~/Documents/frames/first-gather/{optimus,eden-xp,r-3l-xp}_baptized.trc` are
exactly the post-boot ramp: 12 identical-payload repetitions at 186.7 ms on each of three panels,
then the capture ends. Reassembled OPTIMUS packet —
`00 | 00 0A 01 01 | 00 05 | 00 02 | 80 00 | 00 | 7B 88` = `cryptFlag`/`senderId`/`lPack = 5`/
`cmd = 0x0002`/`addr = 0x8000`/`bitmap = 0x00`/`CRC16` — confirms the transport shape above and, via
`var_low = 0x00` (not the `0x80FE` virgin sentinel), that the panels were genuinely baptized.

**Sibling cross-check:** `firmwares/slim-tastiera-app/STEM/UserMain.c:946` carries the explicit
`//Invio tasti periodico sul can` comment over the identical prescaler/send block, and the OPTIMUS
master (`firmwares/stm32h7-optimus-xp-app/STEM/UserMain.c:1485–1497`) resets a `TIMEOUT_KEYBOARD`
= 11000 ms watchdog on every received frame — a master architected around a periodic keep-alive.

> **Trap.** The transmit gate gives `MotherBoardAddress ≠ 0xFFFFFFFF`, and that field is written
> **only** by `AA_Slave_WhoAreYouReceived()` when the master sets the ResetAddress flag
> (`AutoAddressSlave.c:238–241`); `AA_Slave_SetAddressReceived()` (`:263–292`) never touches it. A
> panel baptized without `reset=1` is **totally silent** on button-state. This makes
> `CORRECTIONS.md` §C2 load-bearing for spec-005, not just spec-004. Tracked in #293 as a
> follow-up, not fixed there (verified tool-side in #295).

**Destination addressing — live-confirmed 2026-07-23 (#296).** The same `MotherBoardAddress` is
also the heartbeat's **CAN arbitration ID**: `UserMain.c:997` sets `app.srid = MotherBoardAddress`,
and the router arbitrates on the destination. So *where* the heartbeat arrives depends on **who
baptized the panel**: a tool-baptized panel (tool senderId `8`) heartbeats on `0x00000008`; a
machine-baptized panel heartbeats on that machine master's address (`0x000A0441` etc.). The June
`first-gather` captures were machine-baptized panels, and a machine master shares the machineType
byte with its keyboard — which is why the #270 variant-from-arbitration-ID rule worked on them by
coincidence and failed on the first tool-baptized panel (`bench-logs/pcan/test1.trc`: heartbeat on
`0x00000008`, payload senderId `0x000A0101`, post-boot ramp 12 × 186.9 ms then 12.38 s — also the
live confirmation of the dual-rate model above). The panel's **own** address — and therefore the
variant — is the reassembled packet's **senderId** (machineType at bits 23–16). The observer accept
rule moves to the senderId at completed-packet level; see the wire-format contract §Destination
addressing (#296).

**Packetization** (vendored stack, per spec-003's
[who-i-am-wire-format.md](../003-panel-discovery/contracts/who-i-am-wire-format.md)): transport
packet = `cryptFlag(1) + senderId(4) + lPack(2) + cmd(2) + payload(N) + CRC16(2)`, chunked,
re-assembled by `PacketReassembler`. The decoder reads command at packet bytes 7–8 and the variable
address at 9–10 (`PacketDecoder.cs`), so the reassembled SP_APP packet carries `cmd=0x0002`,
`addr=0x80NN`, `data=bitmap`. Exact offsets pinned against a fixture at parse time.

**Decision**: decode the button frame with a dedicated pure parser `ButtonStateFrame` (mirror
`WhoIAmFrame.fs`) reached from a dedicated observer that filters on command `0x00:0x02` and the
button-state address set, reusing the existing `PacketReassembler`. No new framing code; no
dictionary path (R6).

## R2 — Bit polarity: the load-bearing pin (firmware ground truth; legacy conflict explained)

The firmware is unambiguous, read directly rather than inferred:

- `keyPress_Evt` (`UserMain.c:1367–1388`): **press** → `TxTasti &= ~keysMask` (bit **cleared**,
  line 1369); **release** → `TxTasti |= keysMask` (bit **set**, line 1375).
- The transmitted data byte is `TxTasti` **verbatim, no inversion**: `UserBufTx.buffer[4] =
  TxTastiOld` with `TxTastiOld = TxTasti` (`UserMain.c:974,978`).

**On the wire, therefore: pressed = bit 0, released/idle = bit 1.** The no-buttons-pressed steady
state is all-active-bits-`1`.

The field-proven legacy app scores a press with `(receivedMask & buttonMask) == buttonMask`
(`ButtonPanelTestService.cs:825`) — set-bit (`1`) = pressed. Against this firmware that comparison
becomes true on the bit's **return to 1**, i.e. it actually detected the **release** edge. It was
field-proven not because its polarity matched, but because a technician always presses *and
releases*, so the release frame reliably satisfied the check. The firmware-vs-legacy "conflict" the
hand-off flagged is not a contradiction — the two observed different edges of the same gesture.

Boot caveat: `TxTasti` is zero-initialised (`UserMain.c:200`) and only reaches the all-`1` idle
state once the key-scan fires `RELEASED` for the untouched keys — so an absolute byte must **never**
be read as press-state. The detector must seed its baseline from the first observed frame and act on
**transitions** only (this also satisfies the spec's "held button registers once" and "bouncing
scores once" edge cases).

**Decision**: detect a press as the **press edge — an active button's bit transitions `1 → 0`** —
per spec FR-006 ("transitioned into the pressed state"; pressed = `0` per firmware), with the
baseline taken from the first frame seen. Capture the pressed-bit value as a single named constant
(`PressedBit = 0uy`) so an unexpected bench result is a one-line flip, not a redesign. The Hardware
E2E phase confirms the press-edge direction on a real OPTIMUS-XP panel before that variant is
declared bench-validated (the same SC-004-style gate baptism used). This is a deliberate improvement
over the legacy release-edge behaviour: immediate feedback on press, recorded in the spec's edge
model.

## R3 — Bit assignment and the OPTIMUS-XP active set (triply confirmed)

Panel-side bit assignment is uniform across variants (`UserMain.c:215–246`
`button_event_itf_t` masks; matches CORRECTIONS.md §C3):

`UP=bit0 · DOWN=bit1 · P1=bit2 · P2=bit3 · P3=bit4 · MEM=bit5 · STOP=bit6 · LIGHT=bit7`.

OPTIMUS-XP's active set is `{1,2,4,5}` = `{DOWN,P1,P3,MEM}`, confirmed three independent ways:
the motherboard enum `user_can_keys_e` (§C3), the legacy masks `[0x02,0x04,0x10,0x20]`
(`ButtonPanel.cs`), and the product owner's decals. Canonical firmware order filtered to the active
mask gives the prompt sequence `DOWN → P1 → P3 → MEM` = decals **Light → Suspension → Up → Down**
(spec AC-003).

**Decision**: model the bitmap as masked to the variant's active bits; canonical order is the
fixed firmware order `[UP;DOWN;P1;P2;P3;MEM;STOP;LIGHT]` filtered by the active mask.

## R4 — Per-variant schemas + decal labels (OPTIMUS authoritative, rest provisional)

From the legacy enums (`ButtonPanelEnums.cs`) + masks (`ButtonPanel.cs`), reconciled to the C3 bit
assignment. Decal labels are the names on the physical panel.

| Variant | MachineType | Active set (bits) | Decal labels (canonical order) | Status |
|---|---|---|---|---|
| OPTIMUS-XP | `0x0A` | `{1,2,4,5}` = DOWN,P1,P3,MEM | Light, Suspension, Up, Down | **authoritative** |
| EDEN-XP | `0x03` | all 8 | HeadUp,HeadDown,Horizontal,Suspension,Up,Down,Stop,Lights | provisional |
| R-3L XP | `0x0B` | all 8 | HeadDown,Down,Up,HeadUp,FeetUp,FeetDown,Stop,Lights | provisional |
| EDEN-BS8 | `0x0C` | all 8 | (EdenButtons, as EDEN-XP) | provisional |

Legacy label spellings (e.g. "Lights") are normalised to the spec's decal wording ("Light") for
OPTIMUS-XP only; the provisional variants carry the legacy spelling until bench-confirmed. The
legacy `HasLed` flag (OPTIMUS=false, EDEN-BS8=true, …) exists but is **out of scope** here — it
belongs to spec-006 (output side); the spec-005 schema carries only the active mask + decal/firmware
labels.

**Decision**: a new `ButtonSchema` table in `Core/Can`, one entry per variant, carrying the active
mask, the ordered active buttons, and per-button decal + firmware labels, plus a `Provisional` flag
surfaced wherever the technician sees a non-OPTIMUS label (FR-016). OPTIMUS-XP is the only
non-provisional row.

## R5 — RX pipeline reuse (≈80%)

The receive path is fully reusable (agent map confirmed against the worktree):
`ICanFrameStream.RawFramesReceived : IObservable<RawCanFrame>` (`Core/Can/Ports.fs`, adapter
`PcanCanFrameStream.fs`) → `PacketReassembler.Accept` (`Infrastructure.Protocol`) → a per-command
observer that filters and parses, re-publishing via `SubjectFanOut<'T>`. spec-003's
`WhoIAmReassemblyObserver` (filter `0x0024`, `parse`, publish `IObservable<WhoIAmFrame>`) is the
exact template. `IClock` exists and is consumed (timeouts); `FrozenClock` drives tests.

**Decision**: add `IButtonStateObserver : IObservable<ButtonStateFrame>` to `Core/Can/Ports.fs`
(mirror `IWhoIAmObserver`), production adapter `ButtonStateReassemblyObserver`
(`Infrastructure/Can`) over `ICanFrameStream` + a reused `PacketReassembler`, and virtual fake
`InMemoryButtonStateObserver` (`Tests/Fakes/Can`, mirror `InMemoryWhoIAmObserver`). No new external
boundary — CAN RX is already a boundary consumed through `ICanFrameStream`; this is a new
observation seam over it, exactly as spec-003 added one.

## R6 — Protocol-metadata stopgap (no module; inline, mirroring WHO_I_AM)

There is **no** `Core.Protocol.KnownStemCommands` / `KnownProtocolAddresses` module in the tree
(grep: none). The hand-off/CORRECTIONS §C5 naming is aspirational; in practice spec-003/004 hardcode
the command code *inline at the observer* (`WhoIAmReassemblyObserver` filters `0x0024`;
`ProtocolMasterSequenceTransmitter.fs:40–41` carries `Command("…WHO_ARE_YOU","00","23")` /
`("…SET_ADDRESS","00","25")`). Live button-state metadata is **not** fetched from the dictionary
API — the spec's edge-on-bitmap model uses a dedicated parser, not the generic
`PacketDecoder` + dictionary `Variable` lookup.

**Decision**: hardcode the VAR_WRITE command (`0x00:0x02`) and the button-state address set
(`{0x8000, 0x803E}`, treating `0x80FE` as the virgin sentinel — never a test result) inline in the
new observer, mirroring the WHO_I_AM precedent. This **extends the inherited hardcoded-metadata
stopgap**; it introduces no new bypass (Constitution VI — baptism precedent). The §C5 fetch
migration stays deferred to its own standalone ticket (decoupled from the parked #156, per memory
`spec-002-c5-stopgap-deferred`), explicitly out of scope.

## R7 — Test FSM (the genuinely-new core)

The session walks the variant's active buttons in canonical order, one prompt at a time, with a
per-button countdown. Inputs come from existing observables; the only new input is the parsed
button frame's press-edge.

- **States**: `Idle` → `Prompting(index, deadline)` (repeats) → terminal `Completed(results)` |
  `Interrupted(reason, partialResults)` where `reason ∈ {LinkLost, PanelLost}`.
- **Per-button outcome** (closed DU, needs the Section-3 triple): `Pending | Pass | Missed |
  Skipped`. Aggregate "all active passed" iff every active button is `Pass`.
- **Events**: `PressEdge(bit)` (from the detector), `Tick(now)`, `Retry`, `Skip`,
  `LinkChanged(connected)`, `PanelPresence(present)`.
- **Rules** (from FRs): a matching press-edge within the window → `Pass` + advance (FR-006/010); a
  non-matching active press → logged `Unexpected`, no advance, prompt stays (FR-008); timeout →
  `Missed` + offer Retry/Skip (FR-007/009); `Retry` re-arms the same button with a fresh deadline;
  `Skip` records `Skipped` (≠ Pass) and advances (FR-009); link-leaves-Connected or
  panel-disappears → `Interrupted`, never "all passed" (FR-013); a press for an inactive position is
  ignored (FR-014). Re-run clears all results (FR-003).
- **Timeout**: default 10 s (`research`-config constant, not UI) via `IClock` + a deadline tick,
  exactly as `BaptismService` drives its 6 s budget with `FrozenClock` in tests.

**Decision**: a pure `step : Schedule → State → Event → State × Action` FSM in `Core/Can`, driven
by a `ButtonPressTestService` (mirror `BaptismService`, RX-only — no transmitter). The detector
(R2) sits between the observer and the FSM, converting consecutive bitmaps to `PressEdge` events.

## R8 — Service / GUI / test reuse (mirror baptism)

- **Service**: `ButtonPressTestService(buttons: IButtonStateObserver, discovery:
  IPanelDiscoveryService, link: ICanLinkService, clock: IClock, logger: ILogger<…>)`. Reactive
  link-state guard (subscribe `LinkStateChanged`, route non-Connected → `Interrupted`), panel-
  presence guard via discovery (panel-disappeared → `Interrupted`), `lock`-guarded mutable FSM
  state never held across an await (`stem-async-discipline`). Single-attempt, no auto-retry; Retry
  is technician-driven.
- **Enablement** (FR-001): `Enabled | Disabled of explanation` priority-ordered guard
  `testEnablement : link → selection-baptized → observable → Enablement`, Lean `test_enabled_iff` +
  FsCheck (baptism's `Enablement.lean` / `EnablementGuards` precedent).
- **GUI**: `ButtonPressTestView.fs` — a **pure render** function of FSM state + result grid +
  countdown + Retry/Skip + disabled hint (BaptismView is pure-render; host `App.fs` owns Msg/update).
  Functional layout only — the visual-hierarchy design is deferred late-train (out of scope).
- **Tests**: manual fakes only (`InMemoryButtonStateObserver`, `InMemoryCanLink`, `FrozenClock`),
  real service graph, synchronous emit + clock-advance for determinism (baptism E2E precedent).
- **Composition**: register `ButtonStateReassemblyObserver` (`IButtonStateObserver`) and
  `ButtonPressTestService` as singletons in `CompositionRoot.fs`, consuming the existing `IClock` /
  `ICanLinkService` / `IPanelDiscoveryService` / `ICanFrameStream`.

**Decision**: adopt the baptism service/GUI/test/composition templates wholesale, dropping the TX
half.

## R9 — Lean Phase 4 (new)

New `lean/Stem/ButtonPanelTester/Phase4/` (umbrella `Phase4.lean`), Lean-spec-first per Principle I:

- `ButtonStateFrame.lean` — VAR_WRITE button-frame codec; `parse_encode_roundtrip`, `encode_length`.
- `KeyStateBitmap.lean` — masked bitmap + press-edge detector; `press_edge_iff_high_to_low`
  (an active bit is a press iff it was `1` and is now `0`), `inactive_bits_ignored`.
- `ButtonSchema.lean` — per-variant active-only ordered schema; supports `test_visits_active_only`.
- `ButtonPressTest.lean` — the session FSM; theorems: `test_visits_active_only` (prompts exactly the
  active buttons in canonical order — roadmap), `result_vector_length` (final results length =
  active count — roadmap), `test_outcome_total` (every run terminates in exactly one terminal),
  `pass_requires_press_edge` (FR-006 — no Pass without an in-window matching press-edge),
  `skip_never_pass` (FR-009), `interrupt_excludes_all_passed` (FR-013), `terminal_absorbs`
  (never-flip: a late press after `Missed`/terminal does not change a recorded outcome).
- `Enablement.lean` — `test_enabled_iff` (FR-001).

**Decision**: each closed DU above ships the mandatory triple (Lean theorem + FsCheck property +
XML-doc citation, `stem-fp-discipline` §3); Lean lands ahead of F# inside every slice that has
theorems.

## R10 — Forensic logging (FR-012)

The forensic trail (each prompt, observed press expected/unexpected, score, timeout, retry, skip,
with timestamps) is structured logging via `ILogger<ButtonPressTestService>` template messages with
named parameters — never string interpolation, never `Console.WriteLine` (`stem-logging`,
archetype-A required). Level discipline: prompt/score = Information, Unexpected/Missed = Warning,
Interrupted = Warning, exceptions as first arg. No persistence beyond the in-session view (FR-015).

**Decision**: a small `ButtonPressTestLogging` surface of template messages (baptism's
`BaptismLogging` precedent) consumed by the service; correlate a run with `BeginScope`.

## Reuse vs new (summary)

| Reused as-is | New for spec-005 |
|---|---|
| `ICanFrameStream` + `PcanCanFrameStream`, `PacketReassembler`, `SubjectFanOut`, `IClock` | `ButtonStateFrame` parser + key-state **press-edge detector** (R1/R2) |
| Service template, link-state guard, enablement, outcome-DU shape (`BaptismService`) | `ButtonPressTestService` (RX-only) + the **session FSM** (R7) |
| GUI MVU pure-render section (`BaptismView`), composition root, test fakes | `IButtonStateObserver` + adapters; `ButtonSchema` per-variant table (R4); **Lean Phase 4** (R9) |
