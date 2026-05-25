# Implementation Plan: CAN Link and Panel Discovery

**Branch**: `feat/002-can-link-and-panel-discovery` | **Date**: 2026-05-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from [`specs/002-can-link-and-panel-discovery/spec.md`](./spec.md)

## Summary

Deliver passive CAN observation end-to-end. After feat-001's dictionary boot completes, open the configured PEAK PCAN-USB adapter at 250 kbps and surface its state in a persistent CAN status row matching feat-001's three-state chip shape (`Connected | Disconnected | Error`, with the Error state internally classified `Recoverable | Fatal`). While the link is up, listen for STEM auto-address `WHO_I_AM` broadcasts on CAN ID `0x1FFFFFFF` and render the observed panels in a UUID-keyed Panels-on-bus list, decoding the variant identity byte to one of `{EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8} ∪ {Virgin, Unknown}`. Prune entries after 15 s of silence (≈ 2.5× the worst-case ~6 s broadcast cadence per firmware audit). **Zero CAN frames are transmitted** — this slice is pure observation.

The implementation vendor-copies the PCAN-and-frame-reading stack from `stem-device-manager` into a new C# project `src/ButtonPanelTester.Infrastructure.Protocol/` (≈ 2,686 LOC, frozen, `VENDOR.md` with upstream SHA). This is the **only** stopgap in spec-002 (`docs/STOPGAP_VENDORED_PROTOCOL_STACK.md`, tracked). Critically, spec-002 sits **below** the vendored `PacketDecoder` and consumes raw `CanFrame`s directly — so the CORRECTIONS.md §C5 hardcoded-protocol-metadata stopgap (`KnownStemCommands` / `KnownProtocolAddresses`) is deferred to spec-003+ when transmit-side semantics first need command resolution.

## Technical Context

**Language/Version**: F# 10 / .NET 10 for the tester's own code (Core / Services / Infrastructure / GUI), C# 13 / .NET 10 for the vendored `Infrastructure.Protocol` project. `Nullable=enable`, `TreatWarningsAsErrors=true` per BUILD_CONFIG everywhere.

**Primary Dependencies**: Avalonia 11.3.7 + Avalonia.FuncUI 1.5.1 (continued from spec-001, no version bump); Peak.PCANBasic.NET (transitive via vendored stack, version pinned by `stem-device-manager`'s manifest at vendoring time); BCL `IObservable<T>` for port contracts (no `System.Reactive` package — adapters use hand-rolled subjects, see [research.md](./research.md) R4); `Microsoft.Extensions.DependencyInjection` (continued from spec-001).

**Storage**: filesystem only and only for what spec-001 already established (dictionary cache, credential). **No new persisted state in spec-002.** The Panels-on-bus list is volatile per FR-015 and the spec's Out-of-Scope item "Persisting the Panels-on-bus list across sessions".

**Testing**: xUnit 2.9.3 + FsCheck.Xunit 3.3.3 + Avalonia.Headless.XUnit 11.3.7 (continued). Two test projects unchanged in count:
  - `tests/ButtonPanelTester.Tests/` (`net10.0`, F#) — adds `Unit/Can/`, `Property/Can/`, `Integration/Can/`, `Fakes/Can/`, `Fixtures/Can/` partitions.
  - `tests/ButtonPanelTester.Tests.Windows/` (`net10.0-windows`, F#) — adds `Gui/Can/` (Avalonia.Headless) and `Integration/Can/Hardware/` (`Category=Hardware`, excluded from default CI).

**Target Platform**: Windows desktop (continued). The new `ButtonPanelTester.Infrastructure.Protocol` project is `net10.0-windows` (Peak.PCANBasic.NET is Windows-only). `Core` / `Services` stay `net10.0` (portable).

**Project Type**: desktop app, archetype A.

**Performance Goals**: SC-001 (CAN status row Connected within 2 s of dictionary row populated), SC-003 (pristine panel appears within 6 s of power-on), SC-005 (Disconnected within 5 s of unplug), SC-008 (Error within 5 s of fault).

**Constraints**: zero CAN frames transmitted (SC-007, verifiable by external bus capture). 15 s pruning threshold (FR-011, locked by clarify). Single PEAK adapter on the bench (Assumption). One panel at a time on the bench (Assumption, but the data model still keys by UUID to handle accidental two-on-bus).

**Scale/Scope**: one PEAK PCAN-USB adapter; ≤ 1 panel under test at a time on the bench; the data model is a UUID-keyed map so accidental two-on-bus produces two rows without UI panic.

## Constitution Check

*GATE: passes before Phase 0 research. Re-checked after Phase 1 design.*

- **I. Formal Verification of Invariants** *(NON-NEGOTIABLE)* — adds `lean/Stem/ButtonPanelTester/Phase2/` (Phase 1 conventions, mirrored — see [research.md](./research.md) R6: no mathlib, `namespace Stem.ButtonPanelTester.Phase2`, one theorem per file, proofs by `rfl` or `by cases ... <;> simp`, polymorphic over implementation types where feasible):
  - `CanLinkState.lean` — closed state inductive (`initializing | connected | disconnected reason | error kind`); theorem `state_classification_total`: the five top-level classifications partition the state space.
  - `WhoIAmFrame.lean` — wire layout (15-byte payload: `machineType : UInt8`, `fwType : UInt8`, `uuid0..2 : UInt32` big-endian); theorem `parse_encode_roundtrip`: `parse (encode f) = some f` for every well-formed `WhoIAmFrame`.
  - `PanelObservation.lean` — record `{ uuid; variantByte; variantIdentity; lastSeen }` + `decodeVariant : UInt8 → VariantIdentity` (closed: `marketing EdenXp | marketing OptimusXp | marketing R3LXp | marketing EdenBs8 | virgin | unknown raw`); theorem `variant_decoding_total`: `decodeVariant` is total on `UInt8`.
  - `PanelsOnBus.lean` — `PanelsOnBus = UUID → Option PanelObservation` (function-shaped); action `observe`; theorem `observe_coalesces_by_uuid`: observing two frames with the same UUID produces a single entry whose `lastSeen` is the max of the two timestamps.
  - `Pruning.lean` — action `prune now`; theorem `prune_partitions_by_threshold`: post-prune membership iff `now - lastSeen ≤ 15s`.
  - `PassiveObserver.lean` — theorem `observe_emits_no_transmit`: the `observe` action's projection onto the transmit-trace alphabet is the empty trace. Mechanises SC-007 + FR-014.
- **II. Property-Driven Correctness** — FsCheck properties in `tests/ButtonPanelTester.Tests/Property/Can/`:
  - `WhoIAmFrameRoundtrip`: `parse ∘ encode = Some ∘ id` for well-formed `WhoIAmFrame` values.
  - `WhoIAmFrameRejectsMalformed`: for arbitrary `byte[]` payloads not matching the wire layout, `parse` returns `None` (silent drop per FR-013).
  - `CanLinkStateTransitions`: starting from any reachable `CanLinkState`, applying any input event lands in a reachable state per the Lean spec.
  - `PanelsOnBusCoalescing`: for an arbitrary sequence of broadcasts, `|distinct rows| = |distinct UUIDs|` (FR-008).
  - `PanelsOnBusLastSeenMonotonic`: for any UUID, the `lastSeen` timestamp of its row is non-decreasing across the broadcast sequence.
  - `PruningCorrectness`: for any `(now, history)`, the pruned set equals `{x | now - x.lastSeen ≤ 15s}` (FR-011).
  - `VariantByteMappingTotal`: for every `byte`, `decodeVariant` produces exactly one of the six branches (FR-009).
  - `LinkLostClearsPanelsOnBus`: every Connected → Disconnected transition leaves the list empty (FR-015).

  Example-based `[<Fact>]` is reserved for: concrete WHO_I_AM byte fixtures captured from the firmware audit (one per known variant + one virgin + one malformed). Rationale recorded in each test file's docstring per Principle II.
- **III. Ports and Adapters for Every External Boundary** — two new ports in `src/ButtonPanelTester.Core/Can/Ports.fs` (see [research.md](./research.md) R3 for the split rationale; full signatures in [contracts/can-link-port.md](./contracts/can-link-port.md) and [contracts/can-frame-stream-port.md](./contracts/can-frame-stream-port.md)):

  | Port | Production adapter | Virtual adapter (tests) |
  |---|---|---|
  | `ICanLink` | `PcanCanLink` (wraps vendored `CanPort` + `PCANManager`; lifecycle `OpenAsync / CloseAsync / ReconnectAsync` + `LinkStateChanged : IObservable<CanLinkState>`) | `InMemoryCanLink` (scripted state-change sequences) |
  | `ICanFrameStream` | `PcanCanFrameStream` (subscribes to the vendored stack's `PacketReceived` event; emits `RawCanFrame { canId; payload; timestamp }`) | `InMemoryCanFrameStream` (scripted frame sequences) |

  Both ports expose `IObservable<T>` (BCL interface; adapters use hand-rolled subjects — no `System.Reactive` package). Spec-002 sits **below** the vendored `PacketDecoder` — see [research.md](./research.md) R2.
- **IV. CI Greens the Whole Stack; Hardware Tests Are Explicit** — every layer ships tests on every PR:
  - Unit: `tests/ButtonPanelTester.Tests/Unit/Can/` — pure F# (state machine, variant decoder, frame parser).
  - Property: `tests/ButtonPanelTester.Tests/Property/Can/` — FsCheck against Core contracts (the 8 properties above).
  - Integration: `tests/ButtonPanelTester.Tests/Integration/Can/` — `InMemoryCanLink` + `InMemoryCanFrameStream` wired through `CanLinkService` end-to-end (state transitions + panel discovery + pruning + link-loss clearing, without any hardware).
  - GUI: `tests/ButtonPanelTester.Tests.Windows/Gui/Can/` — `Avalonia.Headless.XUnit` against `CanStatusRow` and `PanelsOnBusView`.
  - Hardware E2E: `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/` — `[<Trait("Category", "Hardware")>]`, excluded from default CI, tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112) (one issue covers the whole spec-002 hardware suite per Principle IV's "tagged + linked" rule, and naturally extends to spec-003+ as those E2E suites land).
  - No `[<Fact(Skip = ...)>]`.
- **V. Supplier-Deployed Identity Is Hashed at Capture** *(NON-NEGOTIABLE)* — **No identity-bearing data on this feature's path.** The data observed (`WHO_I_AM` payloads) carries panel hardware UUIDs only, which are device identifiers (not OS user / machine name / SID / MAC) and which sit only in volatile UI memory for this slice — nothing crosses to STEM-controlled storage. The PCAN adapter's serial number is rendered locally in the status detail affordance for technician orientation (FR-004); it never leaves the supplier's machine. No hash routine is needed because no identity ever leaves.
- **VI. Stopgap Discipline** — **One stopgap.**
  - **STOPGAP_VENDORED_PROTOCOL_STACK** — vendor-copy of `stem-device-manager`'s CAN + raw-frame stack (≈ 2,686 LOC across `Core/Interfaces`, `Core/Models`, `Services/Protocol`, `Infrastructure.Protocol/Hardware`; file-level inventory in [research.md](./research.md) R1, freeze discipline in [contracts/vendor-manifest.md](./contracts/vendor-manifest.md)). Violates STEM **LANGUAGE** standard (F# default) by carrying C#. Tracking issue: [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111). Waiver: `docs/STOPGAP_VENDORED_PROTOCOL_STACK.md` (lands with the vendor commit). Removal path: replace with the `Stem.Communication` NuGet once `stem-device-manager` finishes its Phase 5 migration (the tester migrates after the device manager validates the package in production).
  - **CORRECTIONS.md §C5's hardcoded-protocol-metadata stopgap does NOT land in spec-002.** Phase 0 research (see [research.md](./research.md) R2) confirmed that spec-002 consumes raw `CanFrame`s directly — `PacketDecoder` is not invoked, so the `KnownStemCommands` / `KnownProtocolAddresses` modules are unnecessary for this slice. They become a spec-003+ concern when transmit-side semantics first need command resolution; a follow-up paragraph in `CORRECTIONS.md` recording this scoping decision is a non-blocking task.

**Result: PASS.** No items move to Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/002-can-link-and-panel-discovery/
├── plan.md                        # This file
├── research.md                    # Phase 0 — decisions + alternatives
├── data-model.md                  # Phase 1 — F# types, invariants, Lean cross-refs, Mermaid state machine
├── contracts/                     # Phase 1 — port contracts + wire format + vendor manifest discipline
│   ├── can-link-port.md
│   ├── can-frame-stream-port.md
│   ├── who-i-am-wire-format.md
│   └── vendor-manifest.md
├── quickstart.md                  # Phase 1 — developer onboarding for the CAN slice
└── checklists/
    └── requirements.md            # /speckit.specify quality checklist (already green)
```

### Source code (repository root) — new and modified files only

```text
src/
├── ButtonPanelTester.Core/                    net10.0  F#  (extended)
│   ├── Can/                                                NEW
│   │   ├── CanLinkState.fs                                 closed DU + DisconnectReason + ErrorClassification
│   │   ├── WhoIAmFrame.fs                                  15-byte wire layout + parse/encode
│   │   ├── PanelObservation.fs                             record + decodeVariant
│   │   ├── PanelsOnBus.fs                                  UUID-keyed Map<PanelUuid, PanelObservation>
│   │   ├── Pruning.fs                                      prune ttl now history
│   │   └── Ports.fs                                        ICanLink + ICanFrameStream + RawCanFrame
│   └── (existing Dictionary/* unchanged)
├── ButtonPanelTester.Services/                net10.0  F#  (extended)
│   ├── Can/                                                NEW
│   │   └── CanLinkService.fs                               open/close/reconnect + observation pipeline
│   │                                                       + 1 s pruning timer
│   │                                                       + Connected→Disconnected list-clear (FR-015)
│   │                                                       + Recoverable→Fatal escalation (research.md R8)
│   └── (existing Dictionary/* unchanged)
├── ButtonPanelTester.Infrastructure.Protocol/ net10.0-windows  C#  NEW PROJECT (vendor copy, frozen)
│   ├── ButtonPanelTester.Infrastructure.Protocol.csproj
│   ├── VENDOR.md                                           upstream SHA + file manifest + modifications
│   ├── VENDOR.sha256                                       sidecar hash for drift detection
│   ├── Core/Interfaces/{ICommunicationPort, IPacketDecoder}.cs
│   ├── Core/Models/{Command, Variable, ProtocolAddress, RawPacket,
│   │                AppLayerDecodedEvent, ConnectionState, ChannelKind,
│   │                DeviceVariant, ...}.cs
│   ├── Services/Protocol/{PacketDecoder, DictionarySnapshot,
│   │                      PacketReassembler, NetInfo, ProtocolService}.cs
│   └── Hardware/{CanPort, PCANManager, IPcanDriver, CANPacketEventArgs}.cs
├── ButtonPanelTester.Infrastructure/          net10.0-windows  F#  (extended)
│   ├── Can/                                                NEW
│   │   ├── PcanCanLink.fs                                  ICanLink adapter (wraps CanPort + PCANManager)
│   │   ├── PcanCanFrameStream.fs                           ICanFrameStream adapter (wraps PacketReceived)
│   │   └── PcanAdapterIdentity.fs                          PEAK serial + channel name (local-only, FR-004)
│   └── (existing Http/* Persistence/* Clock.fs unchanged)
└── ButtonPanelTester.GUI/                     net10.0-windows  F#  (extended)
    ├── Composition/
    │   └── CompositionRoot.fs                              extended: wire ICanLink + ICanFrameStream +
    │                                                       CanLinkService into the Elmish loop
    ├── Can/                                                NEW
    │   ├── CanStatusRow.fs                                 chip + reconnect button + detail affordance
    │   └── PanelsOnBusView.fs                              list view + empty-state explainer
    ├── App.fs                                              extended: vertical-stack
    │                                                       DictionaryStatusRow / CanStatusRow / PanelsOnBusView
    └── (existing Dictionary/* unchanged)

tests/
├── ButtonPanelTester.Tests/                   net10.0  F#  (extended)
│   ├── Unit/Can/                                           NEW
│   ├── Property/Can/                                       NEW (8 FsCheck properties from Principle II)
│   ├── Integration/Can/                                    NEW (CanLinkService + virtual ports E2E)
│   ├── Fakes/Can/                                          NEW
│   │   ├── InMemoryCanLink.fs
│   │   └── InMemoryCanFrameStream.fs
│   └── Fixtures/Can/                                       NEW
│       └── whoIAmFixtures.json                             firmware-captured frame samples
└── ButtonPanelTester.Tests.Windows/           net10.0-windows  F#  (extended)
    ├── Gui/Can/                                            NEW (Avalonia.Headless)
    │   ├── CanStatusRowTests.fs
    │   └── PanelsOnBusViewTests.fs
    └── Integration/Can/Hardware/                           NEW (Category=Hardware, excluded from CI)

lean/                                                       (extended)
├── lakefile.toml                                           add [[lean_lib]] for Phase2; extend defaultTargets
└── Stem/ButtonPanelTester/Phase2/                          NEW
    ├── CanLinkState.lean
    ├── WhoIAmFrame.lean
    ├── PanelObservation.lean
    ├── PanelsOnBus.lean
    ├── Pruning.lean
    └── PassiveObserver.lean

docs/
└── STOPGAP_VENDORED_PROTOCOL_STACK.md         NEW (lands with the vendor commit, not the plan)

eng/
└── vendor-protocol-stack.ps1                  NEW (one-shot vendoring helper; documented in quickstart.md)
```

**Structure Decision**: archetype A continued. The new C# project `ButtonPanelTester.Infrastructure.Protocol` is the **single** structural deviation in spec-002; it carries the vendor copy frozen, with its own `VENDOR.md` recording the upstream SHA and file manifest. The two F# adapter files (`PcanCanLink.fs`, `PcanCanFrameStream.fs`) bridge from the C# vendored types to the F# Core ports — the language boundary lives at the adapter layer, not at the port layer. The Lean Phase 2 modules add a second `[[lean_lib]]` entry to `lean/lakefile.toml`; the Phase 1 lib continues unchanged. No other source-tree changes: spec-002 does not modify `Dictionary/*` or `Http/*` from spec-001.

## Complexity Tracking

> Empty — Constitution Check passes without unresolved violations. The single stopgap (STOPGAP_VENDORED_PROTOCOL_STACK) is fully addressed by the discipline in Principle VI (tracking issue, waiver doc, removal path, all named above and detailed in [contracts/vendor-manifest.md](./contracts/vendor-manifest.md)). No deviation accrual.

## Status

*Last refreshed: 2026-05-25 (post-PR-C fix queue). Living Plan per the speckit RPI overlay — `Completed` / `Current` / `Blockers`.*

### Completed

- Phase 0 — `research.md` (vendor file set, port shape split, `IObservable` surface, Lean Phase 2 style).
- Phase 1 — `data-model.md`, `contracts/`, `quickstart.md`; Constitution Check (post-design re-eval) PASS, single stopgap unchanged.
- `/speckit.tasks` — `tasks.md` landed on `main` via PR [#119](https://github.com/luca-veronelli-stem/button-panel-tester/pull/119).
- Phase 1 Setup (T001–T011) — vendor stack + scaffolding via PR-A [#120](https://github.com/luca-veronelli-stem/button-panel-tester/pull/120). Boot-sequence extract added by PR [#133](https://github.com/luca-veronelli-stem/button-panel-tester/pull/133) closes the T040b deferral with `src/ButtonPanelTester.Services/BootSequence.fs` + FR-001 ordering test (issue [#125](https://github.com/luca-veronelli-stem/button-panel-tester/issues/125)).
- Phase 2 Foundational (T012–T033) — Core types + ports + virtual fakes + 5 FsCheck suites + 6 Lean Phase 2 theorems + WHO_I_AM fixtures via PR-B [#121](https://github.com/luca-veronelli-stem/button-panel-tester/pull/121).
- Phase 3 US1 — MVP (T034–T043) — `PcanCanLink` + `CanLinkService` lifecycle slice + `CanStatusRow` + composition root wiring + integration/GUI/hardware tests via PR-C [#122](https://github.com/luca-veronelli-stem/button-panel-tester/pull/122).
- Phase 3.5 post-PR-C fix queue — 5 of 9 amendments shipped (full task table in `tasks.md` §Phase 3.5): PR [#133](https://github.com/luca-veronelli-stem/button-panel-tester/pull/133), [#134](https://github.com/luca-veronelli-stem/button-panel-tester/pull/134), [#135](https://github.com/luca-veronelli-stem/button-panel-tester/pull/135), [#138](https://github.com/luca-veronelli-stem/button-panel-tester/pull/138), [#141](https://github.com/luca-veronelli-stem/button-panel-tester/pull/141).

### Current

- Closing out the Phase 3.5 fix queue before PR-D (`/speckit.implement` Phase 4, US2 panel discovery, tracked by [#116](https://github.com/luca-veronelli-stem/button-panel-tester/issues/116)).
- PR-D, PR-E ([#117](https://github.com/luca-veronelli-stem/button-panel-tester/issues/117), US3 mid-session unplug), and PR-F ([#118](https://github.com/luca-veronelli-stem/button-panel-tester/issues/118), Phase N polish) remain ahead — none started.

### Blockers

- Open Phase 3.5 amendments gating PR-D (each a one-PR slice; full task body in `tasks.md` §Phase 3.5): [#128](https://github.com/luca-veronelli-stem/button-panel-tester/issues/128), [#129](https://github.com/luca-veronelli-stem/button-panel-tester/issues/129), [#136](https://github.com/luca-veronelli-stem/button-panel-tester/issues/136), [#139](https://github.com/luca-veronelli-stem/button-panel-tester/issues/139), [#142](https://github.com/luca-veronelli-stem/button-panel-tester/issues/142). [#136](https://github.com/luca-veronelli-stem/button-panel-tester/issues/136) widens `Disconnected` arity, so it forces a Lean Phase 2 re-parametrisation alongside the F# change.
- Non-blocking carry-overs tracked for visibility, NOT gating PR-D: [#132](https://github.com/luca-veronelli-stem/button-panel-tester/issues/132) (hot-plug regression test, depends on [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111)), [#140](https://github.com/luca-veronelli-stem/button-panel-tester/issues/140) (GUI tooltip test).
- C4 follow-up (driver-download link in Fatal-driver-missing) — its own follow-up issue per the PR-C handoff plan; requires a spec amendment to FR-002a / FR-004 plus a new FR for the remediation hyperlink affordance. Not gating PR-D.
