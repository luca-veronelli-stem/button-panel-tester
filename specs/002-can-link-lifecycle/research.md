# Research: CAN Link Lifecycle

**Phase 0 output for**: [plan.md](./plan.md)

This document records the Phase 0 decisions for the CAN link lifecycle (formerly spec-002 + panel discovery; #151 split, 2026-05-26). Decisions cross-cutting both specs (vendor inventory, port-shape split, IObservable, Lean conventions) are summarised here for the lifecycle reader; panel-discovery-side detail lives in [`specs/003-panel-discovery/research.md`](../003-panel-discovery/research.md). Cross-references are explicit.

The noisy exploration (vendor file inventory, port-shape mining from `stem-device-manager`, Lean Phase 1 reading) was delegated to an Explore sub-agent on 2026-05-24; its structured findings drive R1, R3, and R6 below. The remaining decisions (R4, R5, R8, R9) are planning-time choices that did not need code reading.

---

## R1 — Vendor file inventory (which files cross from stem-device-manager)

**Shared with spec-003.** The vendored stack is shared infrastructure.

**Decision**: vendor the following files from `stem-device-manager` (commit SHA pinned in `VENDOR.md` at vendoring time) into `src/ButtonPanelTester.Infrastructure.Protocol/`. Total ≈ 2,686 LOC.

- **Core/Interfaces** (≈ 359 LOC) — `ICommunicationPort.cs`, `IPacketDecoder.cs`.
- **Core/Models** (≈ 494 LOC) — `Command.cs`, `Variable.cs`, `ProtocolAddress.cs`, `RawPacket.cs`, `AppLayerDecodedEvent.cs`, `ConnectionState.cs`, `ChannelKind.cs`, `DeviceVariant.cs` + small shared records.
- **Services/Protocol** (≈ 823 LOC) — `PacketDecoder.cs`, `DictionarySnapshot.cs`, `PacketReassembler.cs`, `NetInfo.cs`, `ProtocolService.cs`.
- **Infrastructure.Protocol/Hardware** (≈ 1010 LOC) — `CanPort.cs`, `PCANManager.cs`, `IPcanDriver.cs`, `CANPacketEventArgs.cs`.

**Rationale**: this is the minimum set whose compilation graph is closed. The dead-code subset (`PacketReassembler`, `NetInfo`, parts of `ProtocolService`) is acceptable — the vendoring discipline rejects partial trimming because it diverges from upstream and makes future re-vendoring harder.

**Alternatives considered**: see prior history in PR-A [#120](https://github.com/luca-veronelli-stem/button-panel-tester/pull/120) for trim-aggressively / F#-reimpl / direct-DLL-reference rejections.

**Open follow-up**: at vendoring time, pin the exact `stem-device-manager` commit SHA in `VENDOR.md` (see [contracts/vendor-manifest.md](./contracts/vendor-manifest.md)).

---

## R3 — Two ports (link lifecycle + frame stream), split rather than combined

**Lifecycle owns `ICanLink`; spec-003 owns `ICanFrameStream`.** Both ports live in the same `Core/Can/Ports.fs` file (one DI assembly, one F# project).

**Decision**: define **two** F# ports:

```fsharp
type ICanLink =
    abstract member OpenAsync : baudrateBps: int * ct: CancellationToken -> Task
    abstract member CloseAsync : ct: CancellationToken -> Task
    abstract member ReconnectAsync : ct: CancellationToken -> Task
    abstract member LinkStateChanged : IObservable<CanLinkState>
    abstract member CurrentState : CanLinkState

// ICanFrameStream — spec-003-owned; see specs/003-panel-discovery/research.md R3.
```

Each port has a production adapter (`PcanCanLink` / `PcanCanFrameStream` — both wrapping the vendored stack) and a virtual adapter (`InMemoryCanLink` / `InMemoryCanFrameStream` — scripted sequences for tests). Full lifecycle contract in [contracts/can-link-port.md](./contracts/can-link-port.md).

**Rationale**: lifecycle and frame stream are orthogonal. Splitting them lets `CanLinkStateTransitions` property tests stub just `ICanLink` and the panel-discovery property tests stub just `ICanFrameStream`. Phase 0 mining confirmed the vendored stack itself exposes both surfaces separately (`StateChanged` vs `PacketReceived`).

**Alternatives considered**: single `ICanAdapter` god port (rejected, verbose test setup); three ports (rejected, over-decomposed); state-changes-as-frames (rejected, defeats the type system).

---

## R4 — `IObservable<T>` for port contracts, hand-rolled subjects for adapters

**Shared with spec-003.**

**Decision**: the port contracts expose BCL `IObservable<T>` directly. Production adapters use a small hand-rolled subject implementation (`ConcurrentBag<IObserver<T>>` + thread-safe `OnNext` fan-out + `IDisposable` per subscription) rather than pulling in the `System.Reactive` package.

**Rationale**: `IObservable<T>` lives in `System` (BCL); no NuGet needed. Adapters only need OnNext/OnError/OnCompleted dispatch (≈ 30 LOC). Bridging to FuncUI's Elmish loop is a `Cmd.ofSub` that calls `Subscribe(observer)` and disposes on teardown.

**Alternatives considered**: F# `IEvent<T>` (rejected, language-coupled); `System.Reactive` `Subject<T>` (rejected, no operator use justifies the package surface); callbacks (rejected, multi-subscriber by hand is what `IObservable<T>` standardises).

**Cross-platform note**: when `CanLinkService` switches off the vendored stack toward the future `Stem.Communication` NuGet, the port contract is unchanged — only the adapter swaps.

---

## R5 — Pruning loop architecture (timer in CanLinkService)

**Spec-003-owned.** The lifecycle slice has no pruning concern. See [`specs/003-panel-discovery/research.md`](../003-panel-discovery/research.md) R5 for the decision. Referenced here because the timer lives inside the cohabiting `CanLinkService.fs`.

---

## R6 — Lean Phase 2 conventions (mirror Phase 1)

**Shared with spec-003.**

**Decision**: Phase 2 mirrors Phase 1 exactly:
  - Lean v4.29.1 (pinned in `lean/lean-toolchain`, unchanged).
  - No mathlib. Only core Lean 4 inductives + `rfl` / `cases` / `simp` tactics.
  - `namespace Stem.ButtonPanelTester.Phase2` (one namespace, six modules — two lifecycle, four panel-discovery).
  - One theorem per file. Theorem statement IS the design contract; proof is `by rfl` or `by cases ... <;> simp`. No `sorry`, no custom axioms.
  - Types are polymorphic in implementation-specific parameters where doing so keeps modules independent.
  - Verbose comment headers explaining: what the module mechanises, which spec/data-model section, which Principle gate.
  - `lean/lakefile.toml` gains a second `[[lean_lib]]` entry `Stem.ButtonPanelTester.Phase2`; `defaultTargets` extended to include both phases.

**Lifecycle modules**: `Phase2/CanLinkState.lean`, `Phase2/PassiveObserver.lean`.

**Panel-discovery modules** (`Phase2/WhoIAmFrame.lean`, `Phase2/PanelObservation.lean`, `Phase2/PanelsOnBus.lean`, `Phase2/Pruning.lean`) cohabit the same `[[lean_lib]]` entry — see [`specs/003-panel-discovery/research.md`](../003-panel-discovery/research.md) R6.

**Rationale**: Phase 1's discipline ("every proof ultra-lightweight; types polymorphic; one theorem per file") landed Phase 1 in parallel without theorem-ordering constraints. Phase 2 is the same shape.

---

## R8 — Error-state classification logic (Recoverable→Fatal escalation)

**Lifecycle-owned.**

**Decision**: `CanLinkService` tracks a per-error-cause "consecutive reconnect-failure count". The first instance of an unexpected PEAK status is surfaced as `Error.Recoverable`. If the same status code recurs after a reconnect attempt, the second observation is surfaced as `Error.Fatal`. The counter resets to zero on any successful Open. This implements spec-002's edge-case rule "If the same status repeats after a reconnect attempt, the classification escalates to Fatal".

**Rationale**: the escalation rule lives in the service (not the port) because it requires observation across multiple lifecycle attempts — a state the port itself doesn't carry. Keeping it in the service means the virtual adapter can drive the escalation in tests by scripting the same status code twice with an intervening reconnect.

**Alternatives considered**: counter-in-port (rejected, couples port to escalation policy); always-fatal status list (rejected, too tied to PEAK's enum); reset-on-any-transition (rejected, would hide a flapping driver).

---

## R9 — Composition with feat-001's existing main window

**Shared with spec-003 (lifecycle owns rows 1–2 of the vertical stack, spec-003 owns row 3).**

**Decision**: `App.fs` (the FuncUI shell) gains a vertical-stack panel that hosts `DictionaryStatusRow` (top, from feat-001), `CanStatusRow` (middle, lifecycle), and `PanelsOnBusView` (bottom — fills remaining space, spec-003). The composition root wires `ICanLink` + `CanLinkService` after the `DictionaryService` initialises; the FuncUI sub-program for the CAN side runs in parallel with the dictionary sub-program (independent Elmish loops, composed via FuncUI's parent-child message routing).

**Rationale**: keeps the dictionary side untouched (Principle IV's "the two rows are independent" — FR-016). The vertical stack matches the spec's "alongside" language. Parallel Elmish sub-programs are the FuncUI-idiomatic composition pattern.

**Alternatives considered**: single Elmish loop with a combined `Msg` DU (rejected, couples state); tab UI (rejected, spec wants both visible); floating window (rejected, multi-window is harder to capture in headless tests).

---

## Decisions migrated to spec-003

- **R2** — Spec-002 sits BELOW PacketDecoder (panel-discovery is the consumer of raw frames; the lifecycle slice doesn't decode anything).
- **R7** — `RawCanFrame` shape (struct + ReadOnlyMemory; only the receive path needs this).

Both decisions are recorded in [`specs/003-panel-discovery/research.md`](../003-panel-discovery/research.md).

---

## Open follow-ups (do not block this slice)

- **Pin the upstream `stem-device-manager` SHA** in `VENDOR.md` at vendoring time (see [contracts/vendor-manifest.md](./contracts/vendor-manifest.md)).
- **One upstream PR back to `stem-device-manager`** at vendor time: `CancellationTokenSource` + `IAsyncDisposable` added to `PCANManager` so the background read/monitor tasks stop cleanly on dispose. **Shipped** via `stem-device-manager#118` (vendored modification routed upstream); see PR [#134](https://github.com/luca-veronelli-stem/button-panel-tester/pull/134) for the BPT-side adoption.
- **Tracking issue for STOPGAP_VENDORED_PROTOCOL_STACK**: [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111). Removal targets the `Stem.Communication` NuGet once `stem-device-manager` Phase 5 completes.
- **Tracking issue for the Hardware-Test-Setup**: [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112). Covers the `Category=Hardware` E2E suite for both lifecycle (this spec) and panel discovery (spec-003).
