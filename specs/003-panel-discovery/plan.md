# Implementation Plan: Panel Discovery via Passive WHO_I_AM Observation

**Branch**: `feat/003-panel-discovery` | **Date**: 2026-05-26 (extracted) | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from [`specs/003-panel-discovery/spec.md`](./spec.md)

**Origin**: extracted from former `specs/002-can-link-and-panel-discovery/` via #151. Phases 1–2 are **shared foundation already shipped** via PR-A [#120](https://github.com/luca-veronelli-stem/button-panel-tester/pull/120) and PR-B [#121](https://github.com/luca-veronelli-stem/button-panel-tester/pull/121) — see [`specs/002-can-link-lifecycle/plan.md`](../002-can-link-lifecycle/plan.md). Phase 3.5 amendments shipped to date are lifecycle-only; spec-003 starts implementation work at Phase 4 (PR-D, US2, T044–T055 in the historical numbering — renumbered T001–TNN in this plan's `tasks.md`).

## Summary

Deliver passive CAN panel discovery on top of spec-002's CAN link lifecycle. While the link is Connected (spec-002 FR-002), listen for STEM auto-address `WHO_I_AM` broadcasts on CAN ID `0x1FFFFFFF` and render the observed panels in a UUID-keyed Panels-on-bus list, decoding the variant identity byte to one of `{EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8} ∪ {Virgin, Unknown}`. Prune entries after 15 s of silence (≈ 2.5× the worst-case ~6 s broadcast cadence per firmware audit). Clear the list on any `Connected → ¬Connected` transition (consumer of spec-002 FR-015). **Zero CAN frames are transmitted** — this slice is pure observation.

The implementation lives in the same F# projects as spec-002 lifecycle (`ButtonPanelTester.Core/Can`, `ButtonPanelTester.Services/Can`, `ButtonPanelTester.Infrastructure/Can`, `ButtonPanelTester.GUI/Can`). The cohabitation is the accepted seam: specs split documentation; code does not. `CanLinkService.fs` is the single F# service class hosting both lifecycle and observation pipeline.

The vendored `Infrastructure.Protocol` stack from spec-002 is shared. Spec-003 sits **below** the vendored `PacketDecoder` and consumes raw `CanFrame`s directly — so the CORRECTIONS.md §C5 hardcoded-protocol-metadata stopgap (`KnownStemCommands` / `KnownProtocolAddresses`) is deferred to spec-004+ when transmit-side semantics first need command resolution.

## Technical Context

Inherited from spec-002 unchanged: F# 10 / .NET 10, Avalonia 11.3.7 + FuncUI 1.5.1, xUnit 2.9.3 + FsCheck.Xunit 3.3.3 + Avalonia.Headless.XUnit 11.3.7. `Nullable=enable`, `TreatWarningsAsErrors=true`. Single PEAK adapter, 250 kbps. Lean v4.29.1, no mathlib, Phase 2 modules cohabit one `[[lean_lib]]`.

**Performance Goals**: SC-003 (pristine panel appears within 6 s of power-on), SC-004 (no duplicate rows on re-broadcast 100% of the time).

**Constraints**: zero CAN frames transmitted (inherited from spec-002 SC-007). 15 s pruning threshold (FR-011). One panel at a time on the bench (Assumption, but the data model keys by UUID).

## Constitution Check

*GATE: passed on the original spec-002 plan (PASS, no Complexity Tracking). This spec inherits the same artefacts; the principle gates below restate what already shipped.*

- **I. Formal Verification of Invariants** *(NON-NEGOTIABLE)* — discovery-side Lean Phase 2 modules in `lean/Stem/ButtonPanelTester/Phase2/`:
  - `WhoIAmFrame.lean` — wire layout (15-byte payload: `machineType : UInt8`, `fwType : UInt8`, `uuid0..2 : UInt32` big-endian); theorem `parse_encode_roundtrip`.
  - `PanelObservation.lean` — record + `decodeVariant : UInt8 → VariantIdentity`; theorem `variant_decoding_total`.
  - `PanelsOnBus.lean` — `PanelsOnBus = UUID → Option PanelObservation`; theorem `observe_coalesces_by_uuid`.
  - `Pruning.lean` — `prune now`; theorem `prune_partitions_by_threshold`.

  Cohabit the `Stem.ButtonPanelTester.Phase2` lib with spec-002's `CanLinkState.lean` and `PassiveObserver.lean`.

- **II. Property-Driven Correctness** — discovery-side FsCheck properties in `tests/ButtonPanelTester.Tests/Property/Can/`:
  - `WhoIAmFrameRoundtrip`, `WhoIAmFrameRejectsMalformed` (FR-013 silent drop).
  - `PanelsOnBusCoalescing` (FR-008), `PanelsOnBusLastSeenMonotonic`.
  - `PruningCorrectness` (FR-011), `VariantByteMappingTotal` (FR-009).
  - `LinkLostClearsPanelsOnBus` (FR-015' consumer contract).

- **III. Ports and Adapters for Every External Boundary** — one new port in `src/ButtonPanelTester.Core/Can/Ports.fs` for the receive path (full signature in [contracts/can-frame-stream-port.md](./contracts/can-frame-stream-port.md)):

  | Port | Production adapter | Virtual adapter (tests) |
  |---|---|---|
  | `ICanFrameStream` | `PcanCanFrameStream` (subscribes to the vendored stack's `PacketReceived` event; emits `RawCanFrame { canId; payload; timestamp }`) | `InMemoryCanFrameStream` (scripted frame sequences) |

  The companion `ICanLink` port lives in the same `Ports.fs` file and is spec-002-owned.

- **IV. CI Greens the Whole Stack; Hardware Tests Are Explicit** — same partitioning as spec-002: Unit / Property / Integration / GUI / Hardware E2E. Hardware tests tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112) (one issue covers the whole CAN hardware suite for both specs).

- **V. Supplier-Deployed Identity Is Hashed at Capture** *(NON-NEGOTIABLE)* — **No identity-bearing data on this feature's path.** The observed `WHO_I_AM` payloads carry panel hardware UUIDs (device identifiers, not OS user / machine name / SID / MAC) and sit only in volatile UI memory — nothing crosses to STEM-controlled storage.

- **VI. Stopgap Discipline** — **No new stopgap in spec-003.** The vendored stack from spec-002 is the only stopgap; spec-003 inherits the discipline. CORRECTIONS.md §C5's hardcoded-protocol-metadata stopgap is deferred to spec-004+ when transmit-side semantics first need command resolution.

**Result: PASS.** No items move to Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/003-panel-discovery/
├── plan.md                        # This file
├── research.md                    # Phase 0 — decisions specific to discovery (PacketDecoder skip + RawCanFrame shape)
├── data-model.md                  # Phase 1 — F# types (WhoIAmFrame, VariantIdentity, PanelObservation, PanelsOnBus, Pruning)
├── contracts/                     # Phase 1 — port contract + wire format
│   ├── can-frame-stream-port.md
│   └── who-i-am-wire-format.md
├── quickstart.md                  # Phase 1 — developer onboarding for the discovery slice
└── checklists/
    └── requirements.md            # /speckit.specify quality checklist (carried from spec-002)
```

### Source code (repository root) — discovery deliverables

```text
src/
├── ButtonPanelTester.Core/Can/                        net10.0  F#  (cohabits with spec-002)
│   ├── WhoIAmFrame.fs                                 wire layout + parse/encode
│   ├── PanelObservation.fs                            record + decodeVariant
│   ├── PanelsOnBus.fs                                 UUID-keyed Map<PanelUuid, PanelObservation>
│   ├── Pruning.fs                                     prune ttl now history
│   └── Ports.fs                                       (also carries ICanLink — spec-002): adds ICanFrameStream + RawCanFrame
├── ButtonPanelTester.Services/Can/                    net10.0  F#  (cohabits with spec-002)
│   └── CanLinkService.fs                              extended: observation pipeline + 1 s pruning timer +
│                                                      Connected→Disconnected list-clear (FR-015' consumer)
├── ButtonPanelTester.Infrastructure/Can/              net10.0-windows  F#  (cohabits with spec-002)
│   └── PcanCanFrameStream.fs                          ICanFrameStream adapter
└── ButtonPanelTester.GUI/                             net10.0-windows  F#  (cohabits with spec-002)
    ├── Composition/CompositionRoot.fs                 extended: bind PcanCanFrameStream (replaces no-op)
    ├── Can/PanelsOnBusView.fs                         list view + empty-state explainer
    └── App.fs                                         extended: fill the third slot of the vertical stack

tests/
├── ButtonPanelTester.Tests/                           net10.0  F#  (cohabits with spec-002)
│   ├── Property/Can/                                  shared with spec-002 — see WhoIAm / VariantDecoder /
│   │                                                   PanelsOnBus / Pruning properties
│   └── Integration/Can/
│       ├── DiscoveryE2ETests.fs                       NEW
│       ├── PruningE2ETests.fs                         NEW
│       └── LinkLossClearsListTests.fs                 NEW
└── ButtonPanelTester.Tests.Windows/                   net10.0-windows  F#  (cohabits with spec-002)
    ├── Gui/Can/PanelsOnBusViewTests.fs                NEW
    └── Integration/Can/Hardware/DiscoveryHardwareTests.fs   NEW (Category=Hardware)

lean/Stem/ButtonPanelTester/Phase2/                    (cohabits with spec-002)
├── WhoIAmFrame.lean                                   shipped via PR-B [#121]
├── PanelObservation.lean                              shipped via PR-B [#121]
├── PanelsOnBus.lean                                   shipped via PR-B [#121]
└── Pruning.lean                                       shipped via PR-B [#121]
```

**Structure Decision**: archetype A continued; cohabits with spec-002 in the same F# projects + Lean lib. The split is a documentation seam, not a code seam — `CanLinkService.fs` is one F# class and accepts mixed lifecycle/discovery responsibilities by design.

## Complexity Tracking

> Empty — no new stopgaps. The shared vendored stack (spec-002's STOPGAP_VENDORED_PROTOCOL_STACK) is the only stopgap in scope.

## Status

*Last refreshed: 2026-05-26 (extracted from spec-002 via #151). Living Plan per the speckit RPI overlay.*

### Completed

- Phase 0 — `research.md` (PacketDecoder skip + RawCanFrame shape) extracted from spec-002 research.
- Phase 1 — `data-model.md` (discovery types), `contracts/{can-frame-stream-port,who-i-am-wire-format}.md`, `quickstart.md`; Constitution Check PASS.
- Phase 1 Setup (shared T001–T011) — shipped via PR-A [#120](https://github.com/luca-veronelli-stem/button-panel-tester/pull/120). See [`../002-can-link-lifecycle/tasks.md`](../002-can-link-lifecycle/tasks.md) §Phase 1.
- Phase 2 Foundational (shared T012–T033) — discovery-side foundation shipped via PR-B [#121](https://github.com/luca-veronelli-stem/button-panel-tester/pull/121). Includes `WhoIAmFrame.fs`, `PanelObservation.fs`, `PanelsOnBus.fs`, `Pruning.fs`, virtual `InMemoryCanFrameStream`, four discovery FsCheck suites, four Lean Phase 2 theorems, WHO_I_AM fixtures. See [`../002-can-link-lifecycle/tasks.md`](../002-can-link-lifecycle/tasks.md) §Phase 2.

### Current

- Phase 4 / PR-D / US2 — implementation slice tracked by [#116](https://github.com/luca-veronelli-stem/button-panel-tester/issues/116). Not started. Numbered T001–TNN in this spec's `tasks.md` (re-baselined; the historical T044–T055 numbering is recorded in the task descriptions for traceability).

### Blockers

- **#136 in spec-002** widens `Disconnected` arity, which affects the FR-015' consumer pattern. Spec-003 work should wait until #136 lands so the consumer matches against the final upstream shape.
- **#111 (stem-communication migration)**: hot-plug auto-reconnect from the vendored stack is a dependency of spec-002 US3; spec-003's list-clear assertion is independent (it's about `LinkStateChanged` regardless of how the link drops).
