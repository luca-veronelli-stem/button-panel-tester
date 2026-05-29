# Implementation Plan: CAN Link Lifecycle

**Branch**: `feat/002-can-link-lifecycle` | **Date**: 2026-05-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from [`specs/002-can-link-lifecycle/spec.md`](./spec.md)

**Scope note (#151, 2026-05-26)**: spec-002 originally bundled CAN lifecycle and panel-discovery work. Panel-discovery extracted to [`specs/003-panel-discovery/`](../003-panel-discovery/). This plan covers lifecycle only. The shared Phase 1 Setup and Phase 2 Foundational work (`PR-A [#120]` + `PR-B [#121]`) is foundation for both specs; entries in §Constitution Check / §Project Structure reflect lifecycle-only deliverables, with shared foundation pieces flagged "shared with spec-003".

## Summary

Deliver the CAN link lifecycle end-to-end. After feat-001's dictionary boot completes, open the configured PEAK PCAN-USB adapter at 250 kbps and surface its state in a persistent CAN status row matching feat-001's three-state chip shape (`Connected | Disconnected | Error`, with the Error state internally classified `Recoverable | Fatal`). Survive a mid-session adapter unplug by transitioning the chip to `Disconnected · MidSessionUnplug` and either auto-recovering on re-seat (via the vendored stack's hot-plug behaviour) or recovering on the technician's Reconnect click. The lifecycle is observation-only: SC-007 forbids any transmit. Downstream consumers (spec-003's Panels-on-bus list, future transmit features) subscribe to `LinkStateChanged` to react to transitions; spec-002 owns the observable lifecycle, not its consumers.

The implementation vendor-copies the PCAN-and-frame-reading stack from `stem-device-manager` into a new C# project `src/ButtonPanelTester.Infrastructure.Protocol/` (≈ 2,686 LOC, frozen, `VENDOR.md` with upstream SHA). This is the **only** stopgap in spec-002 (`docs/STOPGAP_VENDORED_PROTOCOL_STACK.md`, tracked). The vendored stack is shared infrastructure with spec-003.

## Technical Context

**Language/Version**: F# 10 / .NET 10 for the tester's own code (Core / Services / Infrastructure / GUI), C# 13 / .NET 10 for the vendored `Infrastructure.Protocol` project. `Nullable=enable`, `TreatWarningsAsErrors=true` per BUILD_CONFIG everywhere.

**Primary Dependencies**: Avalonia 11.3.7 + Avalonia.FuncUI 1.5.1 (continued from spec-001, no version bump); Peak.PCANBasic.NET (transitive via vendored stack, version pinned by `stem-device-manager`'s manifest at vendoring time); BCL `IObservable<T>` for port contracts (no `System.Reactive` package — adapters use hand-rolled subjects, see [research.md](./research.md) R4); `Microsoft.Extensions.DependencyInjection` (continued from spec-001).

**Storage**: filesystem only and only for what spec-001 already established (dictionary cache, credential). **No new persisted state in spec-002.**

**Testing**: xUnit 2.9.3 + FsCheck.Xunit 3.3.3 + Avalonia.Headless.XUnit 11.3.7 (continued). Two test projects unchanged in count:
  - `tests/ButtonPanelTester.Tests/` (`net10.0`, F#) — adds `Unit/Can/`, `Property/Can/`, `Integration/Can/`, `Fakes/Can/`, `Fixtures/Can/` partitions.
  - `tests/ButtonPanelTester.Tests.Windows/` (`net10.0-windows`, F#) — adds `Gui/Can/` (Avalonia.Headless) and `Integration/Can/Hardware/` (`Category=Hardware`, excluded from default CI).

**Target Platform**: Windows desktop (continued). The new `ButtonPanelTester.Infrastructure.Protocol` project is `net10.0-windows` (Peak.PCANBasic.NET is Windows-only). `Core` / `Services` stay `net10.0` (portable).

**Project Type**: desktop app, archetype A.

**Performance Goals**: SC-001 (CAN status row Connected within 2 s of dictionary row populated), SC-005 (Disconnected within 5 s of unplug), SC-008 (Error within 5 s of fault).

**Constraints**: zero CAN frames transmitted (SC-007, verifiable by external bus capture). Single PEAK adapter on the bench (Assumption).

**Scale/Scope**: one PEAK PCAN-USB adapter on the bench; the lifecycle is independent of how many panels exist on the bus.

## Constitution Check

*GATE: passes before Phase 0 research. Re-checked after Phase 1 design.*

- **I. Formal Verification of Invariants** *(NON-NEGOTIABLE)* — lifecycle-side Lean Phase 2 modules in `lean/Stem/ButtonPanelTester/Phase2/` (Phase 1 conventions, mirrored — see [research.md](./research.md) R6):
  - `CanLinkState.lean` — closed state inductive (`initializing | connected | disconnected reason | error kind`); theorem `state_classification_total` (the five top-level classifications partition the state space) and `transition_reachability_closed`.
  - `PassiveObserver.lean` — theorem `observe_emits_no_transmit`: the lifecycle's projection onto the transmit-trace alphabet is the empty trace. Mechanises SC-007 + FR-014.

  Panel-discovery Lean modules (`WhoIAmFrame.lean`, `PanelObservation.lean`, `PanelsOnBus.lean`, `Pruning.lean`) are **shared foundation already shipped via PR-B [#121]** and tracked in spec-003's plan. They cohabit one `[[lean_lib]]` entry per [research.md](./research.md) R6; the lib target is unchanged.

- **II. Property-Driven Correctness** — lifecycle-side FsCheck properties in `tests/ButtonPanelTester.Tests/Property/Can/`:
  - `CanLinkStateTransitions`: starting from any reachable `CanLinkState`, applying any input event lands in a reachable state per the Lean spec.

  Panel-discovery properties (`WhoIAmFrameRoundtrip`, `WhoIAmFrameRejectsMalformed`, `PanelsOnBusCoalescing`, `PanelsOnBusLastSeenMonotonic`, `PruningCorrectness`, `VariantByteMappingTotal`, `LinkLostClearsPanelsOnBus`) are shared foundation tracked in spec-003's plan.

- **III. Ports and Adapters for Every External Boundary** — one new port in `src/ButtonPanelTester.Core/Can/Ports.fs` for the lifecycle (full signature in [contracts/can-link-port.md](./contracts/can-link-port.md)):

  | Port | Production adapter | Virtual adapter (tests) |
  |---|---|---|
  | `ICanLink` | `PcanCanLink` (wraps vendored `CanPort` + `PCANManager`; lifecycle `OpenAsync / CloseAsync / ReconnectAsync` + `LinkStateChanged : IObservable<CanLinkState>`) | `InMemoryCanLink` (scripted state-change sequences) |

  The companion `ICanFrameStream` port for the panel-discovery slice lives in the same `Ports.fs` file and is tracked in spec-003's plan; spec-002 lifecycle does not depend on it.

- **IV. CI Greens the Whole Stack; Hardware Tests Are Explicit** — every layer ships tests on every PR:
  - Unit: `tests/ButtonPanelTester.Tests/Unit/Can/` — pure F# (lifecycle state machine).
  - Property: `tests/ButtonPanelTester.Tests/Property/Can/` — FsCheck (the lifecycle property above).
  - Integration: `tests/ButtonPanelTester.Tests/Integration/Can/` — `InMemoryCanLink` wired through `CanLinkService` end-to-end.
  - GUI: `tests/ButtonPanelTester.Tests.Windows/Gui/Can/` — `Avalonia.Headless.XUnit` against `CanStatusRow`.
  - Hardware E2E: `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/` — `[<Trait("Category", "Hardware")>]`, excluded from default CI, tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112) (one issue covers the whole CAN hardware suite per Principle IV's "tagged + linked" rule, and naturally extends to spec-003+ as those E2E suites land).
  - No `[<Fact(Skip = ...)>]`.

- **V. Supplier-Deployed Identity Is Hashed at Capture** *(NON-NEGOTIABLE)* — **No identity-bearing data on this feature's path.** The PCAN adapter's device ID is rendered locally in the status detail affordance for technician orientation (FR-004); it never leaves the supplier's machine. No hash routine is needed because no identity ever leaves.

- **VI. Stopgap Discipline** — **One stopgap.**
  - **STOPGAP_VENDORED_PROTOCOL_STACK** — vendor-copy of `stem-device-manager`'s CAN + raw-frame stack (≈ 2,686 LOC; file-level inventory in [research.md](./research.md) R1, freeze discipline in [contracts/vendor-manifest.md](./contracts/vendor-manifest.md)). Shared infrastructure with spec-003. Violates STEM **LANGUAGE** standard (F# default) by carrying C#. Tracking issue: [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111). Waiver: `docs/STOPGAP_VENDORED_PROTOCOL_STACK.md`. Removal path: replace with the `Stem.Communication` NuGet once `stem-device-manager` finishes its Phase 5 migration.

**Result: PASS.** No items move to Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/002-can-link-lifecycle/
├── plan.md                        # This file
├── research.md                    # Phase 0 — decisions + alternatives (lifecycle-side)
├── data-model.md                  # Phase 1 — F# types for the lifecycle (CanLinkState, AdapterIdentification)
├── contracts/                     # Phase 1 — port contract + vendor manifest discipline
│   ├── can-link-port.md
│   ├── vendor-manifest.md
│   └── (panel-discovery contracts moved to specs/003-panel-discovery/contracts/)
├── quickstart.md                  # Phase 1 — developer onboarding for the CAN slice
├── bugs/                          # Defect specs against the lifecycle (e.g. #127 cold-start hang)
└── checklists/
    └── requirements.md            # /speckit.specify quality checklist (already green)
```

### Source code (repository root) — lifecycle deliverables

```text
src/
├── ButtonPanelTester.Core/                    net10.0  F#  (extended)
│   ├── Can/                                                NEW (shared with spec-003)
│   │   ├── CanLinkState.fs                                 lifecycle types (this spec)
│   │   ├── Ports.fs                                        ICanLink (this spec) + ICanFrameStream (spec-003)
│   │   └── (panel-discovery types — WhoIAmFrame.fs / PanelObservation.fs / PanelsOnBus.fs / Pruning.fs —
│   │       see specs/003-panel-discovery/plan.md)
│   └── (existing Dictionary/* unchanged)
├── ButtonPanelTester.Services/                net10.0  F#  (extended)
│   ├── Can/                                                NEW (cohabits with spec-003)
│   │   ├── ICanLinkService.fs                              lifecycle + discovery surface (cohabits)
│   │   └── CanLinkService.fs                               lifecycle slice — OpenAsync / CloseAsync /
│   │                                                       ReconnectAsync; Recoverable→Fatal escalation
│   │                                                       (research.md R8). Discovery additions:
│   │                                                       observation pipeline + 1 s pruning timer +
│   │                                                       Connected→Disconnected list-clear (FR-015
│   │                                                       observable upstream / spec-003 FR-015' consumer
│   │                                                       contract). The cohabitation is the accepted
│   │                                                       seam: specs split, code does not.
│   └── (existing Dictionary/* unchanged)
├── ButtonPanelTester.Infrastructure.Protocol/ net10.0-windows  C#  NEW PROJECT (vendor copy, shared with spec-003)
│   └── (file-level inventory in research.md R1)
├── ButtonPanelTester.Infrastructure/          net10.0-windows  F#  (extended)
│   ├── Can/                                                NEW
│   │   ├── PcanCanLink.fs                                  ICanLink adapter (lifecycle)
│   │   ├── PeakErrorText.fs                                PEAK status → headline string (lifecycle)
│   │   ├── PcanAdapterIdentity.fs                          PEAK device-ID + channel name (lifecycle, FR-004)
│   │   ├── VENDOR-GUARD.md                                 freeze discipline README (shared)
│   │   └── (PcanCanFrameStream.fs ships with spec-003)
│   └── (existing Http/* Persistence/* Clock.fs unchanged)
└── ButtonPanelTester.GUI/                     net10.0-windows  F#  (extended)
    ├── Composition/
    │   └── CompositionRoot.fs                              extended: wire ICanLink + CanLinkService
    │                                                       into the Elmish loop
    ├── Can/                                                NEW
    │   ├── CanStatusRow.fs                                 chip + reconnect button + detail affordance
    │   └── (PanelsOnBusView.fs ships with spec-003)
    ├── App.fs                                              extended: vertical-stack
    │                                                       DictionaryStatusRow / CanStatusRow
    └── (existing Dictionary/* unchanged)

tests/
├── ButtonPanelTester.Tests/                   net10.0  F#  (extended)
│   ├── Unit/Can/                                           NEW (lifecycle)
│   ├── Property/Can/                                       NEW (CanLinkStateTransitions — lifecycle)
│   ├── Integration/Can/                                    NEW (CanLinkServiceLifecycleTests +
│   │                                                            RecoverableToFatalEscalationTests)
│   ├── Fakes/Can/                                          NEW
│   │   └── InMemoryCanLink.fs
│   └── Integration/BootOrderTests.fs                       FR-001 boot-order regression (lifecycle)
└── ButtonPanelTester.Tests.Windows/           net10.0-windows  F#  (extended)
    ├── Gui/Can/                                            NEW (lifecycle)
    │   └── CanStatusRowTests.fs
    └── Integration/Can/Hardware/                           NEW (Category=Hardware, excluded from CI)
        └── PcanLifecycleTests.fs

lean/                                                       (extended)
├── lakefile.toml                                           add [[lean_lib]] for Phase2; extend defaultTargets
└── Stem/ButtonPanelTester/Phase2/                          NEW (shared with spec-003)
    ├── CanLinkState.lean                                   lifecycle (this spec)
    ├── PassiveObserver.lean                                SC-007 mechanisation (lifecycle)
    └── (panel-discovery Lean modules — see spec-003)

docs/
└── STOPGAP_VENDORED_PROTOCOL_STACK.md         shared waiver (lifecycle + discovery)

eng/
└── vendor-protocol-stack.ps1                  shared one-shot vendoring helper
```

**Structure Decision**: archetype A continued. The new C# project `ButtonPanelTester.Infrastructure.Protocol` is the **single** structural deviation in spec-002 (shared with spec-003); it carries the vendor copy frozen, with its own `VENDOR.md` recording the upstream SHA and file manifest. `CanLinkService.fs` cohabits lifecycle + discovery in one F# class — the F# language boundary is at the adapter layer, not the spec layer, so this seam is accepted (specs split documentation; code does not).

## Complexity Tracking

> Empty — Constitution Check passes without unresolved violations. The single stopgap (STOPGAP_VENDORED_PROTOCOL_STACK) is fully addressed by the discipline in Principle VI (tracking issue, waiver doc, removal path). No deviation accrual.

## Status

*Last refreshed: 2026-05-26 (post-#151 split). Living Plan per the speckit RPI overlay — `Completed` / `Current` / `Blockers`.*

### Completed

- Phase 0 — `research.md` (vendor file set, port shape split, `IObservable` surface, Lean Phase 2 style). Shared with spec-003 for the cross-cutting decisions.
- Phase 1 — `data-model.md` (lifecycle types), `contracts/can-link-port.md`, `contracts/vendor-manifest.md`, `quickstart.md`; Constitution Check (post-design re-eval) PASS, single stopgap unchanged.
- `/speckit.tasks` — `tasks.md` landed on `main` via PR [#119](https://github.com/luca-veronelli-stem/button-panel-tester/pull/119).
- Phase 1 Setup (T001–T011) — vendor stack + scaffolding via PR-A [#120](https://github.com/luca-veronelli-stem/button-panel-tester/pull/120). Boot-sequence extract added by PR [#133](https://github.com/luca-veronelli-stem/button-panel-tester/pull/133) closes the T040b deferral with `src/ButtonPanelTester.Services/BootSequence.fs` + FR-001 ordering test (issue [#125](https://github.com/luca-veronelli-stem/button-panel-tester/issues/125)).
- Phase 2 Foundational (T012–T033) — Core types + ports + virtual fakes + 5 FsCheck suites + 6 Lean Phase 2 theorems + WHO_I_AM fixtures via PR-B [#121](https://github.com/luca-veronelli-stem/button-panel-tester/pull/121). Shared with spec-003.
- Phase 3 US1 — MVP (T034–T043) — `PcanCanLink` + `CanLinkService` lifecycle slice + `CanStatusRow` + composition root wiring + integration/GUI/hardware tests via PR-C [#122](https://github.com/luca-veronelli-stem/button-panel-tester/pull/122).
- Phase 3.5 post-PR-C fix queue — 7 of 9 amendments shipped (full task table in `tasks.md` §Phase 3.5): PR [#133](https://github.com/luca-veronelli-stem/button-panel-tester/pull/133), [#134](https://github.com/luca-veronelli-stem/button-panel-tester/pull/134), [#135](https://github.com/luca-veronelli-stem/button-panel-tester/pull/135), [#138](https://github.com/luca-veronelli-stem/button-panel-tester/pull/138), [#141](https://github.com/luca-veronelli-stem/button-panel-tester/pull/141), [#145](https://github.com/luca-veronelli-stem/button-panel-tester/pull/145), [#147](https://github.com/luca-veronelli-stem/button-panel-tester/pull/147).

### Current

- Closing out the Phase 3.5 fix queue (lifecycle-only amendments remain: [#136](https://github.com/luca-veronelli-stem/button-panel-tester/issues/136), [#142](https://github.com/luca-veronelli-stem/button-panel-tester/issues/142)).
- US3 (mid-session unplug, Phase 5) — implementation slice tracked by [#117](https://github.com/luca-veronelli-stem/button-panel-tester/issues/117) remains ahead.
- Phase N polish split by spec-002 / spec-003 per #151 (new lifecycle polish issue + spec-003 polish issue, replacing [#118](https://github.com/luca-veronelli-stem/button-panel-tester/issues/118)).

### Blockers

- [#136](https://github.com/luca-veronelli-stem/button-panel-tester/issues/136) widens `Disconnected` arity and forces a Lean Phase 2 re-parametrisation alongside the F# change. Mandatory Lean → test → impl order per the F2 gate.
- Non-blocking carry-overs tracked for visibility, NOT gating Phase 5: [#132](https://github.com/luca-veronelli-stem/button-panel-tester/issues/132) (hot-plug regression test, depends on [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111)), [#140](https://github.com/luca-veronelli-stem/button-panel-tester/issues/140) (GUI tooltip test).
- C4 follow-up (driver-download link in Fatal-driver-missing) — its own follow-up issue per the PR-C handoff plan. Not gating Phase 5.
