# Research: CAN Link and Panel Discovery

**Phase 0 output for**: [plan.md](./plan.md)

This document records the Phase 0 decisions for spec-002, including those resolved by a delegated exploration of the vendored protocol stack and the existing Lean Phase 1 conventions. Each decision lists the alternatives considered and the rationale.

The noisy exploration (vendor file inventory, port-shape mining from `stem-device-manager`, Lean Phase 1 reading) was delegated to an Explore sub-agent on 2026-05-24; its structured findings drive R1, R2, R3, and R6 below. The remaining decisions (R4, R5, R7, R8, R9) are planning-time choices that did not need code reading.

---

## R1 — Vendor file inventory (which files cross from stem-device-manager)

**Decision**: vendor the following files from `stem-device-manager` (commit SHA to be pinned at vendoring time) into `src/ButtonPanelTester.Infrastructure.Protocol/`. Total ≈ 2,686 LOC.

- **Core/Interfaces** (≈ 359 LOC)
  - `ICommunicationPort.cs` — abstracts CAN/BLE/Serial channels (connect/disconnect/send + PacketReceived/StateChanged events).
  - `IPacketDecoder.cs` — pure-decoder interface (Decode + dictionary update; not invoked in spec-002 but lives in the same compilation graph).
- **Core/Models** (≈ 494 LOC)
  - `Command.cs`, `Variable.cs`, `ProtocolAddress.cs`, `RawPacket.cs`, `AppLayerDecodedEvent.cs`, plus `ConnectionState.cs`, `ChannelKind.cs`, `DeviceVariant.cs` and the small shared records (≈ 200 LOC of supporting types).
- **Services/Protocol** (≈ 823 LOC)
  - `PacketDecoder.cs`, `DictionarySnapshot.cs`, `PacketReassembler.cs`, `NetInfo.cs`, `ProtocolService.cs` — vendored as a unit so the compilation graph stays whole; `PacketReassembler` and `NetInfo` are dead code in spec-002, which is acceptable per the "verbatim vendor, no trimming" rule.
- **Infrastructure.Protocol/Hardware** (≈ 1010 LOC)
  - `CanPort.cs`, `PCANManager.cs`, `IPcanDriver.cs`, `CANPacketEventArgs.cs` — the actual Peak PCAN integration.

**Rationale**: this is the minimum set whose compilation graph is closed (you cannot vendor `CanPort.cs` without `IPcanDriver.cs`, etc.). The dead-code subset (`PacketReassembler`, `NetInfo`, parts of `ProtocolService`) is acceptable — the vendoring discipline rejects partial trimming because it diverges from upstream and makes future re-vendoring harder.

**Alternatives considered**:
- *Trim aggressively to only what spec-002 consumes (`CanPort` + `PCANManager` + `IPcanDriver`)*: rejected — those files transitively reference `RawPacket`, `ICommunicationPort`, and the event-args types. Once the transitive closure is taken, the saving over the full set is < 200 LOC. Not worth the divergence cost.
- *Re-implement CAN integration directly against `Peak.PCANBasic.NET` in F#*: rejected — wastes the ~2 years of production hardening in `stem-device-manager`. CORRECTIONS.md §C4 already settled the source-choice question (against vendoring from `stem-communication`, which has 84 open issues).
- *Skip vendoring; depend directly on a `stem-device-manager` `.dll`*: rejected — `stem-device-manager` is a sibling repo, not a published package. A cross-repo `ProjectReference` is brittle and a `.dll` reference loses source-stepping.

**Open follow-up**: at vendoring time, pin the exact `stem-device-manager` commit SHA in `VENDOR.md` (see [contracts/vendor-manifest.md](./contracts/vendor-manifest.md)).

---

## R2 — Spec-002 sits BELOW PacketDecoder

**Decision**: spec-002 consumes raw `RawCanFrame { canId; payload; timestamp }` directly from `ICanFrameStream`, filters for CAN ID `0x1FFFFFFF`, and parses the 15-byte WHO_I_AM payload in F# (`Core/Can/WhoIAmFrame.fs`). The vendored `PacketDecoder` is **not invoked** in spec-002. The `KnownStemCommands` / `KnownProtocolAddresses` modules described in CORRECTIONS.md §C5 are deferred to spec-003+.

**Rationale**: WHO_I_AM is an auto-address-layer frame, not an application-layer protocol command. Its CAN ID is fixed (`SRID_BROADCAST = 0x1FFFFFFF`) and its 15-byte payload (`machineType`, `fwType`, `uuid0..2`) is parsable without command-table lookup. `PacketDecoder` would synthesize an "unknown command" event for it anyway (verified in Phase 0 mining: `PacketDecoder.cs:92-105` synthesizes for missing commands). Skipping `PacketDecoder` for spec-002 means:
  - One fewer stopgap in this slice (CORRECTIONS.md §C5's stopgap moves to spec-003+).
  - The Lean parse/encode round-trip for `WhoIAmFrame` is self-contained — no dependency on a Lean model of `PacketDecoder`.
  - Spec-003's baptize flow (the first to need command resolution) is the natural place to introduce both the hardcoded protocol metadata and the `PacketDecoder` wiring.

**Alternatives considered**:
- *Wire `PacketDecoder` in spec-002 anyway for symmetry with later slices*: rejected — symmetry that costs an extra stopgap in spec-002 is the wrong trade. Spec-003 owns the stopgap when it's actually needed.
- *Skip the vendored stack entirely and parse raw frames against `Peak.PCANBasic.NET` directly*: rejected — same reasoning as R1 alt 2 (wastes upstream hardening), and the vendored `CanPort` already gives us the auto-reconnect + state machine + thread-safe event marshalling that we'd otherwise re-implement.

**Update to CORRECTIONS.md** (non-blocking): a follow-up paragraph in §C5 noting that the hardcoded-protocol-metadata stopgap is a spec-003+ concern (not spec-002). To be added before spec-003 planning begins.

---

## R3 — Two ports (link lifecycle + frame stream), split rather than combined

**Decision**: define **two** F# ports in `Core/Can/Ports.fs`:

```fsharp
type ICanLink =
    abstract member OpenAsync : baudrateBps: int * ct: CancellationToken -> Task
    abstract member CloseAsync : ct: CancellationToken -> Task
    abstract member ReconnectAsync : ct: CancellationToken -> Task
    abstract member LinkStateChanged : IObservable<CanLinkState>
    abstract member CurrentState : CanLinkState

type ICanFrameStream =
    abstract member RawFramesReceived : IObservable<RawCanFrame>
```

Each port has a production adapter (`PcanCanLink`, `PcanCanFrameStream` — both wrapping the vendored stack) and a virtual adapter (`InMemoryCanLink`, `InMemoryCanFrameStream` — scripted sequences for tests). Full contracts in [contracts/can-link-port.md](./contracts/can-link-port.md) and [contracts/can-frame-stream-port.md](./contracts/can-frame-stream-port.md).

**Rationale**: lifecycle and frame stream are orthogonal. Combining them produces a "god port" whose virtual adapter has to script two unrelated stream timelines simultaneously, making the test setup verbose. Splitting them lets `CanLinkStateTransitions` property tests stub just `ICanLink` and `PanelsOnBusCoalescing` property tests stub just `ICanFrameStream`. The wire-up cost (composition root binds two adapters instead of one) is trivial. Phase 0 mining confirmed the vendored stack itself exposes both surfaces separately (`StateChanged` vs `PacketReceived`).

**Alternatives considered**:
- *Single `ICanAdapter` port with both observables*: rejected — verbose test setup, no real coupling between the two streams at the port level.
- *Three ports (lifecycle / state observable / frame observable)*: rejected — over-decomposed; `LinkStateChanged` is naturally part of the lifecycle port.
- *Push state changes through the frame stream as a special frame*: rejected — defeats the type system; state changes are not frames.

---

## R4 — `IObservable<T>` for port contracts, hand-rolled subjects for adapters

**Decision**: the port contracts (`ICanLink.LinkStateChanged`, `ICanFrameStream.RawFramesReceived`) expose BCL `IObservable<T>` directly. Production adapters use a small hand-rolled subject implementation (`ConcurrentBag<IObserver<T>>` + thread-safe `OnNext` fan-out + `IDisposable` per subscription) rather than pulling in the `System.Reactive` package.

**Rationale**: `IObservable<T>` lives in `System` (BCL); no NuGet needed for the interface. Adapters only need OnNext/OnError/OnCompleted dispatch, which is ≈ 30 LOC of subject code per port — cheaper than adding a 1 MB+ `System.Reactive` package surface for a single use case. Bridging to FuncUI's Elmish loop is a `Cmd.ofSub` that calls `Subscribe(observer)` and disposes on teardown — same shape either way.

**Alternatives considered**:
- *F# `IEvent<T>`*: F#-idiomatic but couples the port to F# (the vendored C# adapter would need a one-way `IEvent ← C# event` bridge). `IObservable<T>` is the language-neutral surface.
- *`System.Reactive` (`Subject<T>` + LINQ operators)*: useful if we needed `Throttle`, `Window`, `Buffer`, etc. — we don't (pruning is a simple timer loop in `CanLinkService`, not a reactive window operator). Adding the package for no operator use is dead weight.
- *Callbacks (`Action<T>`)*: rejected — multi-subscriber by hand-rolled list management is exactly what `IObservable<T>` standardises.

**Cross-platform note**: when `CanLinkService` switches off the vendored stack toward the future `Stem.Communication` NuGet, the port contract is unchanged — only the adapter swaps.

---

## R5 — Pruning loop architecture (timer in CanLinkService)

**Decision**: `CanLinkService` owns a single `System.Threading.Timer` ticking at 1 s. On each tick, it computes `now - lastSeen > 15s` per row and removes stale rows from the `PanelsOnBus` map. The pruning is observable through a `PanelsOnBusChanged : IObservable<PanelsOnBus>` event that the GUI subscribes to.

**Rationale**: pruning is a temporal concern owned by the service layer, not by the port. A 1 s tick is fine-grained enough to feel responsive (worst-case 1 s lag past the 15 s threshold) without thrashing the UI. The single timer avoids the per-row timer-leak class of bug.

**Alternatives considered**:
- *Reactive `Throttle`/`Sample` operators*: requires `System.Reactive` (rejected by R4). Overkill for one timer.
- *Compute pruning lazily on every read of `PanelsOnBus`*: rejected — reads happen at render time, which is too often; also makes the "row disappeared" event invisible to the GUI (no observable signal).
- *Pruning on every received frame*: rejected — bursts of frames cause pruning storms; quiet periods cause stale rows to linger past their threshold.
- *Tighter tick (100 ms)*: rejected — wastes CPU for no perceptible benefit (the 15 s threshold's worst-case lag is dominated by the threshold, not the tick).

---

## R6 — Lean Phase 2 conventions (mirror Phase 1)

**Decision**: Phase 2 mirrors Phase 1 exactly:
  - Lean v4.29.1 (pinned in `lean/lean-toolchain`, unchanged).
  - No mathlib. Only core Lean 4 inductives + `rfl` / `cases` / `simp` tactics.
  - `namespace Stem.ButtonPanelTester.Phase2` (one namespace, six modules).
  - One theorem per file. Theorem statement IS the design contract; proof is `by rfl` or `by cases ... <;> simp`. No `sorry`, no custom axioms.
  - Types are polymorphic in implementation-specific parameters where doing so keeps modules independent (e.g., `PanelsOnBus` is parametric in the timestamp type — `Nat` at the Lean layer, `DateTimeOffset` at the F# layer).
  - Verbose comment headers explaining: what the module mechanises, which spec/data-model section, which Principle gate.
  - `lean/lakefile.toml` gains a second `[[lean_lib]]` entry `Stem.ButtonPanelTester.Phase2`; `defaultTargets` extended to include both phases.

**Rationale**: Phase 1's discipline ("every proof ultra-lightweight; types polymorphic; one theorem per file") landed Phase 1 in parallel without theorem-ordering constraints. Phase 2 is the same shape (6 small modules, each one preservation theorem) — the same discipline transfers directly. Deviating now would require justifying the divergence.

**Alternatives considered**:
- *Introduce mathlib for `Finmap` lemmas in `PanelsOnBus.lean`*: rejected — `PanelsOnBus` can be modelled as `UUID → Option PanelObservation` (a function), avoiding any need for a Finmap lemma library. The `observe` and `prune` operations work on this function-shape definitionally.
- *Bundle all Phase 2 into one `Phase2.lean` file*: rejected — Phase 1's per-file split serves the `[P]` parallelisation marker in `tasks.md`; Phase 2 will benefit equally.

---

## R7 — `RawCanFrame` shape (struct + ReadOnlyMemory)

**Decision**: `RawCanFrame` is an F# struct record:

```fsharp
[<Struct>]
type RawCanFrame = {
    CanId : uint32
    Payload : System.ReadOnlyMemory<byte>
    ReceivedAt : System.DateTimeOffset
}
```

Frame production rate on a fully-loaded 250 kbps bus is ≈ 500 frames/s typical (theoretical max ≈ 3000 fps; practical traffic on this bench is far lower). Struct + `ReadOnlyMemory<byte>` avoids per-frame heap allocation on the receive thread.

**Rationale**: GC pressure on the receive thread can cause UI hitches. A struct record costs zero allocation; `ReadOnlyMemory<byte>` lets the vendored stack hand us a pooled buffer without forcing a copy at the port boundary. For the WHO_I_AM filter alone (≈ 1 frame per panel per ~6 s) this is overkill — but the same port serves spec-003+ where transmit-side framing produces denser bursts.

**Alternatives considered**:
- *Reference record + `byte[]`*: simpler but allocates a record header + a byte array per frame. At ≈ 500 fps that's ≈ 30 KB/s of garbage. Cheap to start; expensive to fix later.
- *Raw `(uint32, byte[], DateTimeOffset)` tuple*: allocates the tuple cell, same problem.
- *Pre-allocated pool of `RawCanFrame` objects*: premature optimisation; struct + `ReadOnlyMemory` is enough headroom for now.

**Caveat**: the `ReadOnlyMemory<byte>` payload is only valid for the duration of the `OnNext` callback. Subscribers that need to retain the bytes MUST copy (documented in [contracts/can-frame-stream-port.md](./contracts/can-frame-stream-port.md)).

---

## R8 — Error-state classification logic (Recoverable→Fatal escalation)

**Decision**: `CanLinkService` tracks a per-error-cause "consecutive reconnect-failure count". The first instance of an unexpected PEAK status is surfaced as `Error.Recoverable`. If the same status code recurs after a reconnect attempt, the second observation is surfaced as `Error.Fatal`. The counter resets to zero on any successful Open. This implements spec-002's edge-case rule "If the same status repeats after a reconnect attempt, the classification escalates to Fatal".

**Rationale**: the escalation rule lives in the service (not the port) because it requires observation across multiple lifecycle attempts — a state the port itself doesn't carry. Keeping it in the service means the virtual adapter can drive the escalation in tests by scripting the same status code twice with an intervening reconnect.

**Alternatives considered**:
- *Push the counter into the port*: rejected — couples the port to escalation policy. The port's job is to report PEAK status codes verbatim.
- *Hardcode a list of "always-fatal" status codes*: rejected — too tied to PEAK's status enum; the spec deliberately classifies by repetition, not by status identity, so a previously-unseen unexpected status auto-handles correctly without needing the list updated.
- *Reset the counter on any state transition*: rejected — that would let a Recoverable → Connected → Disconnected sequence reset the count, hiding a flapping driver.

---

## R9 — Composition with feat-001's existing main window

**Decision**: `App.fs` (the FuncUI shell) gains a vertical-stack panel that hosts `DictionaryStatusRow` (top), `CanStatusRow` (middle), and `PanelsOnBusView` (bottom — fills remaining space). The composition root wires `ICanLink` + `ICanFrameStream` + `CanLinkService` after the `DictionaryService` initialises; the FuncUI sub-program for the CAN side runs in parallel with the dictionary sub-program (independent Elmish loops, composed via FuncUI's parent-child message routing).

**Rationale**: keeps the dictionary side untouched (Principle IV's "the two rows are independent" — FR-016). The vertical stack matches the spec's "alongside" language. Parallel Elmish sub-programs are the FuncUI-idiomatic composition pattern; combining into a single mega-`Msg` DU would couple unrelated state.

**Alternatives considered**:
- *Single Elmish loop with a combined `Msg` DU*: rejected — couples dictionary and CAN state; a regression in one sub-program could surface as a noop in the other (debugging nightmare).
- *Tab UI (dictionary tab + CAN tab)*: rejected — the spec wants both visible at once on the main window.
- *Floating CAN window separate from the main window*: rejected — same reasoning, plus a multi-window app is harder to capture in `Avalonia.Headless` tests.

---

## Open follow-ups (do not block this slice)

- **Pin the upstream `stem-device-manager` SHA** in `VENDOR.md` at vendoring time (see [contracts/vendor-manifest.md](./contracts/vendor-manifest.md)). The SHA is recorded as a pre-commit hash check so future re-vendoring detects drift.
- **One upstream PR back to `stem-device-manager`** at vendor time, per CORRECTIONS.md §C4's note: add `CancellationTokenSource` + `IAsyncDisposable` to `PCANManager` so the background read/monitor tasks stop cleanly on dispose. This keeps the local modification bounded.
- **Tracking issue for STOPGAP_VENDORED_PROTOCOL_STACK**: [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111). Removal targets the `Stem.Communication` NuGet once `stem-device-manager` Phase 5 completes.
- **Tracking issue for the Hardware-Test-Setup**: [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112). Covers the `Category=Hardware` E2E suite for spec-002 (and naturally extends to spec-003+ as those E2E suites land — comment with new file paths there).
- **Non-blocking CORRECTIONS.md amendment**: scope §C5's hardcoded-protocol-metadata stopgap to spec-003+ (per R2).
