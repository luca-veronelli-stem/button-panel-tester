# Implementation Plan: CAN Link Lifecycle

**Branch**: `docs/002-lifecycle-spec-refresh` | **Date**: 2026-05-27 (Phase B rewrite) | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from [`specs/002-can-link-lifecycle/spec.md`](./spec.md) at commit `f04318e`

**Phase B rewrite note**: this plan supersedes the substrate plan that landed via PRs [#120](https://github.com/luca-veronelli-stem/button-panel-tester/pull/120) / [#121](https://github.com/luca-veronelli-stem/button-panel-tester/pull/121) / [#122](https://github.com/luca-veronelli-stem/button-panel-tester/pull/122) and was amended through the Phase 3.5 fix queue (#133, #134, #135, #138, #141, #145, #147). Panel-discovery concerns were extracted to [`specs/003-panel-discovery/`](../003-panel-discovery/) via [#151](https://github.com/luca-veronelli-stem/button-panel-tester/pull/151) (Phase A, 2026-05-26). Phase B (this rewrite, 2026-05-27) replaces the substrate's four-family FSM (`Initializing | Connected | Disconnected | Error` with a binary `Recoverable | Fatal` severity classifier) with the five-family FSM (`Idle | Searching | Opening | Open | Faulted`) carrying sub-discriminators in case payloads — see [research.md](./research.md) §1 R2 + R3. The lifecycle code on `main` is the substrate shape; reconciliation is Phase C drift, tracked in [`migration-map.md`](./migration-map.md).

## Summary

Deliver the CAN link lifecycle end-to-end as an observation-only, five-family finite state machine that the technician reads off a persistent CAN status row on the main window. From app launch the FSM begins in `Searching(Polling)` — independent of dictionary boot ([research.md](./research.md) R10) — and walks through `Opening` against each enumerated PEAK adapter to land on `Open` once OpenAsync against an enumerated candidate succeeds (250 kbps, exclusive driver-level access per FR-010). On bench-reality intervention (mid-session unplug, bus-off, hardware fault, driver missing, operator Stop, contention with an external exclusive-mode tool) the FSM transitions to `Searching`, `Faulted`, or `Idle(UserPaused)`, and exposes the operator-initiatable affordances each state allows (Stop / Start / Reconnect per the FR-006 / FR-007 / FR-008 contract). Every transition emits on `LinkStateChanged` (FR-014) so downstream consumers — notably spec-003's Panels-on-bus list — react to family changes by their own contract. SC-007 forbids any CAN transmit; the lifecycle is a passive observer.

Sub-discriminators (`IdleCause`, `SearchAttempt`, `FaultCause`, `AdapterCandidate option`) live in the per-case payload, not as sibling DU branches. The chip-colour projection (FR-002) is a one-line case match on the family; the headline and detail strings (FR-003, FR-005) read the discriminator from inside the case. The sticky-since timestamp (FR-004) preserves across passive re-observation of the same family + discriminator and updates on family change, discriminator change, or a user-initiated round-trip back into the same family. Faulted carries the candidate in its payload (`Some` → Reconnect retries the known candidate, `None` → Reconnect re-searches), so the FSM is self-describing and no service-level side state is needed to drive Reconnect ([research.md](./research.md) R6).

The implementation vendors `stem-device-manager`'s PCAN-and-frame-reading stack into a new C# project `src/ButtonPanelTester.Infrastructure.Protocol/` (≈ 2,686 LOC, frozen, `VENDOR.md` with upstream SHA) — the **only** stopgap in spec-002 (`docs/STOPGAP_VENDORED_PROTOCOL_STACK.md`, tracked by [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111)). The vendored stack is shared infrastructure with spec-003. The Phase B FSM redesign reaches no further than the F# domain (`Core.Can`) and the FSM-bearing service (`Services.Can.CanLinkService`); the vendored C# stack is unchanged.

## Technical Context

**Language/Version**: F# 10 / .NET 10 for the tester's own code (`Core` / `Services` / `Infrastructure` / `GUI`), C# 13 / .NET 10 for the vendored `Infrastructure.Protocol` project. `Nullable=enable`, `TreatWarningsAsErrors=true` per `BUILD_CONFIG` everywhere.

**Primary Dependencies**: Avalonia 11.3.7 + Avalonia.FuncUI 1.5.1 (continued from feat-001, no version bump); Peak.PCANBasic.NET (transitive via the vendored stack, version pinned by `stem-device-manager`'s manifest at vendoring time); BCL `IObservable<T>` for port contracts (no `System.Reactive` package — adapters use hand-rolled subjects, see [research.md](./research.md) R15); `Microsoft.Extensions.DependencyInjection` (continued from feat-001).

**Storage**: filesystem only and only for what feat-001 already established (dictionary cache, credential store). **No new persisted state in spec-002.** A user-paused state from a Stop click does not survive an app restart — the FSM resumes from `Searching(Polling)` on the next launch (spec.md §Out of Scope).

**Testing**: xUnit 2.9.3 + FsCheck.Xunit 3.3.3 + Avalonia.Headless.XUnit 11.3.7 (continued). Two test projects, count unchanged (memory `project_button_panel_tester_tests_split.md`):

- `tests/ButtonPanelTester.Tests/` (`net10.0`, F#) — `Unit/Can/`, `Property/Can/`, `Integration/Can/`, `Fakes/Can/`, `Fixtures/Can/` partitions.
- `tests/ButtonPanelTester.Tests.Windows/` (`net10.0-windows`, F#) — `Gui/Can/` (`Avalonia.Headless.XUnit`) and `Integration/Can/Hardware/` (`Category=Hardware`, excluded from default CI).

**Target Platform**: Windows desktop. `ButtonPanelTester.Infrastructure.Protocol` is `net10.0-windows` (Peak.PCANBasic.NET is Windows-only). `Core` and `Services` stay `net10.0` (portable) — the FSM redesign keeps every type that touches `CanLinkState` portable.

**Project Type**: desktop app, archetype A.

**Performance Goals**: SC-001 (Open within 2 s of app launch when one available PEAK adapter is present), SC-002 (Searching reflecting `NoAdapterEnumerated` within 1 s of app launch when no adapter is present), SC-003 (Open → Searching on unplug within 5 s), SC-004 (Searching → Open on re-seat within 5 s, no click), SC-005 (Stop releases adapter within 2 s — bench-only verification, see §CHK018 resolution), SC-008 (active state → Faulted within 5 s on a non-routine fault), SC-009 (multi-adapter iteration reaches Open against the free adapter within 5 s of enumeration), SC-010 (Reconnect button disabled + `⟳` glyph from click time through next FSM emission), SC-011 (external exclusive open observes driver-busy while BPT holds Open), SC-012 (one Information-level log entry per surfaced contention event, conditional on stack surfacing).

**Constraints**: zero CAN frames transmitted (SC-007, verifiable by external bus capture). Single PEAK adapter on the production bench; multi-adapter iteration is bench-resilience (spec.md §Assumptions + FR-012).

**Scale/Scope**: one PEAK PCAN-USB adapter on the bench in the production case; the lifecycle is independent of how many panels exist on the bus.

## Constitution Check

*GATE: passes before Phase 0 research. Re-checked after Phase 1 design.*

- **I. Formal Verification of Invariants** *(NON-NEGOTIABLE)* — lifecycle Lean Phase 2 modules in `lean/Stem/ButtonPanelTester/Phase2/`, pinned at five theorems across two files per [research.md](./research.md) R16:

  | Lean theorem | File | Mechanises (data-model.md §1.3) |
  |---|---|---|
  | `state_classification_total` | `Phase2/CanLinkState.lean` | Invariant #1 — five-family totality |
  | `fault_cause_total` | `Phase2/CanLinkState.lean` | Invariant #2 — `FaultCause` exhaustiveness |
  | `idle_cause_total` | `Phase2/CanLinkState.lean` | Invariant #3 — `IdleCause` degenerate totality |
  | `faulted_reconnect_target_total` | `Phase2/CanLinkState.lean` | Invariant #4 — FR-008 Reconnect bifurcation |
  | `observe_emits_no_transmit` | `Phase2/PassiveObserver.lean` | Invariant #7 — SC-007 + FR-013 |

  The "one theorem per file" Phase 1 rule is explicitly relaxed for `Phase2/CanLinkState.lean` — the four per-family lemmas all `by cases` over the same `CanLinkState` inductive and share the proof shape; splitting across four files would create artificial import chains for no decomposition benefit ([research.md](./research.md) R16 Phase B note).

  The substrate's `transition_reachability_closed` is retired ([data-model.md](./data-model.md) §4 + [research.md](./research.md) §3); reachability is covered by an FsCheck property listed under Principle II below. Panel-discovery Lean modules (`WhoIAmFrame.lean`, `PanelObservation.lean`, `PanelsOnBus.lean`, `Pruning.lean`) are shared foundation already shipped via PR-B [#121](https://github.com/luca-veronelli-stem/button-panel-tester/pull/121) and tracked in spec-003's plan; they cohabit one `[[lean_lib]]` entry, unchanged.

- **II. Property-Driven Correctness** — lifecycle FsCheck properties in `tests/ButtonPanelTester.Tests/Property/Can/`:

  | Property suite | Mechanises | File |
  |---|---|---|
  | `CanLinkStateTransitions` | Reachability replacement for the retired `transition_reachability_closed`: from any reachable `CanLinkState`, applying any valid input event (operator Stop / Start / Reconnect or any observation-driven event from data-model.md §1.2) lands in another reachable state per the same transition graph. | `Property/Can/CanLinkStateTransitionsProperties.fs` (re-authored for the five-family shape) |
  | `CanLinkStickyTimestamp` | FR-004 / Invariant #5: passive re-observation of the same family + discriminator preserves `since`; family change or discriminator change updates `since`; user-initiated round-trip back into the same family via an intervening state updates `since` on the second arrival. | `Property/Can/CanLinkStickyTimestampProperties.fs` (new) |
  | `LinkStateChangedFamilyExhaustive` | FR-014: over a quantified random event sequence from `Searching(Polling)`, every family in `{ Idle, Searching, Opening, Open, Faulted }` appears in some emission, and the chip-colour projection (FR-002) is total — every emission carries one of `{ green, grey, red }`. | `Property/Can/LinkStateChangedFamilyExhaustiveProperties.fs` (new) |

  Panel-discovery property suites (`WhoIAmFrameRoundtrip`, `WhoIAmFrameRejectsMalformed`, `PanelsOnBusCoalescing`, `PanelsOnBusLastSeenMonotonic`, `PruningCorrectness`, `VariantByteMappingTotal`, `LinkLostClearsPanelsOnBus`) are shared foundation tracked in spec-003's plan.

- **III. Ports and Adapters for Every External Boundary** — one lifecycle port in `src/ButtonPanelTester.Core/Can/Ports.fs`, full signature in [contracts/can-link-port.md](./contracts/can-link-port.md):

  | Port | Production adapter | Virtual adapter (tests) |
  |---|---|---|
  | `ICanLink` | `PcanCanLink` (wraps the vendored `CanPort` + `PCANManager`; lifecycle `OpenAsync / CloseAsync / ReconnectAsync` + `LinkStateChanged : IObservable<CanLinkState>` + `CurrentState`) | `InMemoryCanLink` (scripted state-change sequences) |

  **Contract drift note**: [contracts/can-link-port.md](./contracts/can-link-port.md) currently encodes the substrate's payload shape (four-family `CanLinkState` + `Recoverable / Fatal` severity). The Phase B redesign reshapes the payload to the five-family DU; the port signature itself (`OpenAsync` / `CloseAsync` / `ReconnectAsync` / `LinkStateChanged` / `CurrentState`) is unchanged. The contract is refreshed as the next Phase B queue item after this plan (between plan.md and tasks.md) so tasks.md cites the up-to-date contract — see §Phase B queue below and OPEN-FINDINGS item 1.

  The companion `ICanFrameStream` port for the panel-discovery slice lives in the same `Ports.fs` file and is tracked in spec-003's plan; the lifecycle does not depend on it.

- **IV. CI Greens the Whole Stack; Hardware Tests Are Explicit** — every layer ships tests on every PR:
  - Unit: `tests/ButtonPanelTester.Tests/Unit/Can/` — pure F# (state machine).
  - Property: `tests/ButtonPanelTester.Tests/Property/Can/` — FsCheck (three suites listed under Principle II).
  - Integration: `tests/ButtonPanelTester.Tests/Integration/Can/` — `InMemoryCanLink` wired through `CanLinkService` end-to-end.
  - GUI: `tests/ButtonPanelTester.Tests.Windows/Gui/Can/` — `Avalonia.Headless.XUnit` against `CanStatusRow`. SC-010 (Reconnect click-acknowledge cue) is the load-bearing assertion here.
  - Hardware E2E: `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/` — `[<Trait("Category", "Hardware")>]`, excluded from default CI, tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112). SC-001 / SC-003 / SC-004 / SC-008 / SC-009 / SC-011 land here.
  - No `[<Fact(Skip = ...)>]`.

- **V. Supplier-Deployed Identity Is Hashed at Capture** *(NON-NEGOTIABLE)* — **No identity-bearing data on this feature's path.** `AdapterCandidate.DisplayName` and `AdapterIdentification.{ChannelName, DeviceId, BaudrateBps}` ([data-model.md](./data-model.md) §2.1 + §3.1) render locally in the row's detail affordance (FR-005) and never leave the supplier's machine. No hash routine is needed because no identity ever leaves.

- **VI. Stopgap Discipline** — **One stopgap.**
  - **STOPGAP_VENDORED_PROTOCOL_STACK** — vendor-copy of `stem-device-manager`'s CAN + raw-frame stack (≈ 2,686 LOC; file-level inventory in [research.md](./research.md) R13, freeze discipline in [contracts/vendor-manifest.md](./contracts/vendor-manifest.md)). Shared infrastructure with spec-003. Violates STEM **LANGUAGE** standard (F# default) by carrying C#. Tracking issue: [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111). Waiver: `docs/STOPGAP_VENDORED_PROTOCOL_STACK.md`. Removal path: replace with the `Stem.Communication` NuGet once `stem-device-manager` Phase 5 completes. **Phase B note**: the #111 migration plan MUST add an acceptance check that the vendored stack's device-arrived event (the hot-plug fast path — [research.md](./research.md) R7) is preserved by the replacement, or the regression is flagged.

**Result: PASS.** No items move to Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/002-can-link-lifecycle/
├── plan.md                        # This file (Phase B rewrite)
├── research.md                    # Phase 0 — decisions + alternatives (lifecycle)
├── data-model.md                  # Phase 1 — F# types + Mermaid + invariants
├── contracts/
│   ├── can-link-port.md           # Refreshed as next queue item after plan.md
│   └── vendor-manifest.md
├── quickstart.md                  # Phase 1 — developer onboarding
├── tasks.md                       # Phase 2 — task breakdown (re-authored after contract refresh)
├── migration-map.md               # Phase C input — old → new state + FR table
├── bugs/                          # Defect specs against the lifecycle
└── checklists/
    ├── requirements.md            # /speckit-specify quality checklist (substrate, kept for traceability)
    └── spec-quality.md            # /speckit-checklist Phase B re-review (commit f4ddf89)
```

### Source code (repository root) — lifecycle deliverables

```text
src/
├── ButtonPanelTester.Core/                    net10.0  F#  (extended)
│   ├── Can/                                                shared with spec-003
│   │   ├── CanLinkState.fs                                 IdleCause / SearchAttempt / FaultCause / AdapterCandidate /
│   │   │                                                   AdapterIdentification / CanLinkState (data-model.md §1.1 / §2.1 / §3.1)
│   │   ├── Ports.fs                                        ICanLink (this spec) + ICanFrameStream (spec-003)
│   │   └── (panel-discovery types — WhoIAmFrame.fs / PanelObservation.fs / PanelsOnBus.fs / Pruning.fs —
│   │       see specs/003-panel-discovery/plan.md)
│   └── (existing Dictionary/* unchanged)
├── ButtonPanelTester.Services/                net10.0  F#  (extended)
│   ├── Can/                                                cohabits with spec-003
│   │   ├── ICanLinkService.fs                              lifecycle + discovery surface (cohabits)
│   │   └── CanLinkService.fs                               lifecycle slice — OpenAsync / CloseAsync /
│   │                                                       ReconnectAsync; FR-006 cancellation; FR-012
│   │                                                       multi-adapter iteration; FR-014 LinkStateChanged
│   │                                                       fan-out; Searching retry cadence (see §Searching
│   │                                                       retry policy below). Recoverable→Fatal escalation
│   │                                                       is retired here in the Phase B reconcile (research.md §3).
│   └── (existing Dictionary/* unchanged)
├── ButtonPanelTester.Infrastructure.Protocol/ net10.0-windows  C#  (vendor copy, shared with spec-003)
│   └── (file-level inventory in research.md R13)
├── ButtonPanelTester.Infrastructure/          net10.0-windows  F#  (extended)
│   ├── Can/
│   │   ├── PcanCanLink.fs                                  ICanLink adapter (lifecycle, FR-010 exclusive
│   │   │                                                   driver access on OpenAsync)
│   │   ├── PcanAdapterEnumeration.fs                       NEW — enumerates PEAK adapters and produces
│   │   │                                                   `AdapterCandidate` records the FSM iterates over
│   │   │                                                   (FR-012). Wraps the vendored stack's enumeration
│   │   │                                                   surface. Forward-referenced by data-model.md §2.1.
│   │   ├── PcanAdapterIdentity.fs                          existing — post-Open self-description: queries the
│   │   │                                                   PEAK driver for `ChannelName` + `DeviceId` and
│   │   │                                                   constructs `AdapterIdentification` (FR-004, FR-005).
│   │   │                                                   Forward-referenced by data-model.md §3.1.
│   │   ├── PeakErrorText.fs                                PEAK status code → cause string (FR-003 detail
│   │   │                                                   rendering, FaultCause.UnexpectedAdapterStatus path)
│   │   ├── VENDOR-GUARD.md                                 freeze discipline README (shared)
│   │   └── (PcanCanFrameStream.fs ships with spec-003)
│   └── (existing Http/* Persistence/* Clock.fs unchanged)
└── ButtonPanelTester.GUI/                     net10.0-windows  F#  (extended)
    ├── Composition/
    │   └── CompositionRoot.fs                              wires ICanLink + CanLinkService in parallel with
    │                                                       DictionaryService (no boot gate — research.md R10)
    ├── Can/
    │   ├── CanStatusRow.fs                                 chip + headline + detail affordance + Stop/Start/
    │   │                                                   Reconnect buttons; FR-009 click-acknowledge cue
    │   │                                                   using `⟳` glyph from DictionaryStatusRow.fs:151-158
    │   └── (PanelsOnBusView.fs ships with spec-003)
    ├── App.fs                                              vertical-stack DictionaryStatusRow / CanStatusRow /
    │                                                       PanelsOnBusView
    └── (existing Dictionary/* unchanged)

tests/
├── ButtonPanelTester.Tests/                   net10.0  F#  (extended)
│   ├── Unit/Can/                                           state-machine unit tests
│   ├── Property/Can/                                       three FsCheck suites (Principle II table)
│   ├── Integration/Can/                                    CanLinkServiceLifecycleTests, FR-012 iteration test,
│   │                                                       FR-006 cancellation test (assert ≤ 250 ms; see
│   │                                                       §FR-006 cancellation budget)
│   ├── Fakes/Can/
│   │   └── InMemoryCanLink.fs                              scripted sequences
│   └── Integration/BootOrderTests.fs                       FR-015 — dictionary and CAN start in parallel,
│                                                           CAN row reaches Searching independent of dictionary
│                                                           boot (formerly FR-001 boot-order test; spec-002
│                                                           Phase B reframes this as a decoupling regression)
└── ButtonPanelTester.Tests.Windows/           net10.0-windows  F#  (extended)
    ├── Gui/Can/                                            CanStatusRowTests; SC-010 click-acknowledge cue
    │                                                       assertion (Avalonia.Headless)
    └── Integration/Can/Hardware/                           Category=Hardware, excluded from CI; covers
        └── PcanLifecycleTests.fs                           SC-001 / SC-003 / SC-004 / SC-008 / SC-009 / SC-011

lean/                                                       (extended)
├── lakefile.toml                                           [[lean_lib]] for Phase2 already wired by PR-B [#121]
└── Stem/ButtonPanelTester/Phase2/
    ├── CanLinkState.lean                                   re-authored: four theorems
    │                                                       (state_classification_total, fault_cause_total,
    │                                                       idle_cause_total, faulted_reconnect_target_total).
    │                                                       Drops the retired transition_reachability_closed.
    ├── PassiveObserver.lean                                unchanged: observe_emits_no_transmit
    └── (panel-discovery modules unchanged — see spec-003)

docs/
└── STOPGAP_VENDORED_PROTOCOL_STACK.md         shared waiver (lifecycle + discovery)

eng/
└── vendor-protocol-stack.ps1                  shared one-shot vendoring helper
```

**Structure Decision**: archetype A continued. `ButtonPanelTester.Infrastructure.Protocol` is the single structural deviation in spec-002 (shared with spec-003), carrying the vendor copy frozen with its own `VENDOR.md` recording the upstream SHA and file manifest. `CanLinkService.fs` cohabits lifecycle + discovery — the F# language boundary is at the adapter layer, not the spec layer, so the seam is accepted (specs split documentation; code does not).

## Phase B queue

The Phase B doc-only refresh has the following queue items, each a single vertical commit (per the global `bisect-safe` / `vertical-commits` rules):

1. ~~spec.md amendment~~ — landed `f04318e`.
2. ~~checklists/spec-quality.md~~ — landed `b99282a` + tally fix `f4ddf89`.
3. ~~data-model.md rewrite~~ — landed `0be6872`.
4. ~~research.md rewrite~~ — landed `55f0fc9`.
5. **plan.md rewrite** — this commit.
6. **contracts/can-link-port.md refresh** — next. Reshapes the contract payload from substrate four-family to Phase B five-family `CanLinkState`; port signature unchanged. Resolves OPEN-FINDINGS item 1.
7. tasks.md rewrite — re-authors the task list against the Phase B shape (renumbered; old substrate tasks T001..T033 are not retained as forward-looking — they're already merged on `main`). Includes the Lean `Phase2/CanLinkState.lean` re-author task that retires `transition_reachability_closed` and adds the four post-Phase-B theorems (OPEN-FINDINGS item 4 / F3).
8. quickstart.md refresh — bench walkthrough for the five-family shape.
9. migration-map.md — old substrate → Phase B FR/state table; load-bearing for Phase C.
10. cleanup — `git rm` Phase B session handoff artefacts (`HANDOFF*.md`, `PROMPT*.md`, `OPEN-FINDINGS.md`).
11. PR — single `docs/002-lifecycle-spec-refresh` → `main` PR on github.

## Searching retry policy

**Decision** (resolves [research.md](./research.md) R12 deferral): `Searching` uses a **5-second periodic poll plus the vendored protocol stack's device-arrived event as the fast path**. The event is the primary recovery signal on hot-plug — typical recovery is ≤ 1 second from re-seat to `Opening`. The poll is the safety net: if the vendored stack drops or coalesces an event, the worst-case recovery is ≤ 5 seconds (matching SC-004's budget). Both signals re-enter `Searching(Polling)` and re-attempt enumeration; the first enumerated candidate that produces a successful OpenAsync wins per FR-012.

**Rationale**: the bench feel that the FSM is "always trying to stay up" depends on the fast-path event when the vendored stack supplies one, with a coarse-grained poll as the contract floor. Pinning a 5-second poll is loose enough that it doesn't pressure the PEAK driver under typical bench conditions, and tight enough that SC-004's "within 5 seconds" budget is met even when the event misses. Event-only would regress to manual-Reconnect-only if the vendored stack ever drops an event ([research.md](./research.md) R7's #111 migration risk note already covers this; pinning event-only would double down without need). Tighter poll cadences (e.g. 2 s) were rejected as over-engineering: enumeration is a host-PEAK-driver call, the event already covers the perceptually relevant path, and the 5-second floor matches SC-004's contract.

**Implementation note**: the poll is driven by a single `System.Threading.PeriodicTimer` (BCL) in `CanLinkService`, cancelled via the service's lifetime `CancellationToken` on shutdown / Stop. The device-arrived event subscribes through `ICanLink.LinkStateChanged` (the port surface) at composition time; the port adapter (`PcanCanLink`) bridges the vendored stack's PnP arrival event into an `IObservable` emission.

## FR-006 cancellation budget

**Decision** (resolves OPEN-FINDINGS item 5 / A1): spec.md FR-006 mandates that a Stop click during `Opening` cancels the in-flight OpenAsync call via `CancellationToken` propagation (per STEM `CANCELLATION` standard) before the FSM transitions to `Idle(UserPaused)`. The concrete budget pinned by this plan is **≤ 250 ms on a normal-load workstation**, measured from Stop-click time to `LinkStateChanged` emission of `Idle(UserPaused, now)`.

**Rationale**: PEAK driver typical cancel latency on `PCANBasic.CAN_Uninitialize` plus the F# task continuation cost is comfortably under 100 ms on a normal-load workstation; the 250 ms budget is a margin for slow workstations, OS scheduling jitter, and the `SemaphoreSlim` release inside `PcanCanLink`. Tight enough that an integration test against `InMemoryCanLink` can assert the budget without flakiness (the fake adapter emits Idle in microseconds; the budget asserts the FSM machinery does not block waiting for OpenAsync to complete on its own); loose enough that a real-hardware test under bench load passes without contention.

**Verification**: `tests/ButtonPanelTester.Tests/Integration/Can/CanLinkServiceLifecycleTests.fs` — `StopDuringOpeningCancelsWithinBudget` assertion. The hardware E2E suite confirms the same budget against real PEAK hardware in `PcanLifecycleTests.fs` under Category=Hardware.

## Glossary

Single-line definitions for terms used across spec.md / data-model.md / plan.md without explicit definition (resolves OPEN-FINDINGS item 8 / O1 + checklist CHK010):

- **Active state.** Any `CanLinkState` value whose family is one of `Searching`, `Opening`, `Open`, `Faulted`. Used by FR-006 to scope the Stop affordance: Stop is visible and enabled in every active state, and clicking Stop releases any held adapter and transitions the FSM to `Idle(UserPaused, now)`. `Idle(UserPaused, _)` is the complement — the only non-active state.

## FR-009 sub-perceptual cue (restate)

For traceability (resolves OPEN-FINDINGS item 9 / A2 + closes the spec-quality checklist intent behind CHK008): FR-009's click-acknowledge cue has **no minimum-visibility floor**. The cue's visible duration matches the in-flight call's actual duration; a sub-perceptual call (Start's synchronous transition, or a fast Reconnect that lands in `Open` quickly) is consistent with the truth-to-state principle. The operator's signal that the click was processed comes from the resulting FSM transition — chip-colour change and/or `since` update — not from the cue itself. This is intentional, not a defect: pinning a minimum-visibility floor would re-introduce the chip-level UX-smoothing layer the FSM redesign explicitly rejected ([research.md](./research.md) R8).

## CHK018 — SC-005 verification scope

**Resolution**: SC-005 ("When the user clicks Stop, the adapter is released, verifiable by an external exclusive-mode tool such as StemDeviceManager attaching successfully, within 2 seconds") is **bench-only**. The acceptance check requires an external process holding an exclusive PEAK handle, which CI cannot provision. The hardware E2E suite (`PcanLifecycleTests.fs`, `Category=Hardware`) executes the bench-only path against real hardware before each release; CI does not gate on it.

**CI-compatible surrogate**: the integration test `StopReleasesAdapterHandle` in `tests/ButtonPanelTester.Tests/Integration/Can/CanLinkServiceLifecycleTests.fs` asserts that after a Stop click, the `PcanCanLink` adapter calls `CAN_Uninitialize` on the vendored stack — wired through a fake `IPcanDriver` (the vendored stack's own injection seam) that records the call sequence. The surrogate proves the FSM's release intent at the boundary; the bench-only SC-005 proves the OS-level handle release end-to-end. The two together satisfy SC-005's correctness intent under CI plus a manual bench pre-release gate.

## CHK024 — logging templates

**Resolution**: every FSM transition emits a structured `ILogger<CanLinkService>` log entry per STEM `docs/Standards/LOGGING.md`. Templates use named parameters (no string interpolation — CA2254 is enabled by `AnalysisLevel = latest-recommended` per VISIBILITY), with the level mapped to the destination family per spec.md §Presentation surfaces (`Open` → Information, `Faulted` → Error, other transitions → Information).

**Spec-002-specific templates** (archetype A, `ILogger<CanLinkService>` is required and non-optional per `stem-logging` Step 1):

```fsharp
// Family-agnostic transition log — emitted on every LinkStateChanged.OnNext
_logger.LogInformation(
    "CAN link transitioned to {Family}({Discriminator}) since {Since:O}",
    family,
    discriminator,
    since)

// Open transition — superset of the family-agnostic log when family = Open
_logger.LogInformation(
    "CAN link Open against {ChannelName} (DeviceId={DeviceId}, BaudrateBps={BaudrateBps}) since {OpenedAt:O}",
    adapter.ChannelName,
    adapter.DeviceId,
    adapter.BaudrateBps,
    openedAt)

// Faulted transition — superset of the family-agnostic log when family = Faulted; Error level
_logger.LogError(
    "CAN link Faulted with cause {FaultCause} against candidate {Candidate} since {Since:O}",
    cause,
    candidate,                // string representation; "<none>" when None
    since)

// FR-011 external-contention observability — conditional on the vendored stack surfacing the event
_logger.LogInformation(
    "External exclusive-mode contention observed on {ChannelName} (DeviceId={DeviceId}); BPT holds Open",
    adapter.ChannelName,
    adapter.DeviceId)
```

**Named-parameter conventions**: `{Family}` (DU case name as string), `{Discriminator}` (case-payload sub-discriminator as string), `{Since:O}` / `{OpenedAt:O}` (ISO 8601 round-trip format), `{FaultCause}` (DU case name with payload for `UnexpectedAdapterStatus code`), `{Candidate}` (`DisplayName` when `Some`, `"<none>"` when `None`), `{ChannelName}` / `{DeviceId}` / `{BaudrateBps}` (post-Open self-description fields). These map onto the LOGGING.md parameter-naming table (`{State}` for enums, `{Since:O}` for timestamps, `{Id}` style for adapter identifiers).

**Correlation scope (BeginScope)**: operator-initiated actions wrap their in-flight call in a logger scope so the resulting FSM transition log lines correlate cleanly. Scope keys: `OperatorAction` (`"Stop" | "Start" | "Reconnect"`), `CorrelationId` (a fresh `Guid`), `CandidateChannelHandle` (the `AdapterCandidate.ChannelHandle` when known). Background transitions (vendored-stack events, driver replies, periodic poll) do not open a scope — the family-agnostic transition log is sufficient for forensic reconstruction.

**Logger field shape**: archetype A → `ILogger<CanLinkService>` required, non-optional. `_logger?.` patterns are not allowed in archetype A code (memory `stem-logging` Step 1). The same applies to `PcanCanLink` (`ILogger<PcanCanLink>` required) and `CanStatusRow` (GUI component, `ILogger<CanStatusRow>` required if it emits any log of its own — the FR-009 click-acknowledge cue is purely a render-time concern and does not log).

**Bans inherited from LOGGING.md** (enforced by the `stem-logging` skill's scans + CA2254): no `Console.WriteLine` / `Debug.WriteLine` / `Trace.WriteLine` in production code (carve-outs: `Program.cs` / `Program.fs`, explicit `Diagnostics/` / `Dev/` paths); no string interpolation in `Log*` calls; `LogError` / `LogCritical` take the exception as the first argument when one is available; no secrets, credentials, or PII at `Information+`.

## CHK028 — hot-plug acceptance traceability

**Resolution**: the hot-plug acceptance scenario (spec.md §User Story 1 Acceptance #2 + §User Story 2 Acceptance #2 + Edge Cases §"Hot-plug while in Searching(Polling)") **does not have a dedicated regression test on `main` today**. The test scaffolding lives in the hardware E2E suite under `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/PcanLifecycleTests.fs`, but the explicit hot-plug-recovery assertion (re-seat without a click → `Open` within 5 s) is gap-noted by issue [#132](https://github.com/luca-veronelli-stem/button-panel-tester/issues/132).

**Forward plan**: the Phase C impl reconcile track (which reshapes `CanLinkService` to the five-family FSM) MUST address #132 by adding `HotPlugRecoveryAfterUnplug` to `PcanLifecycleTests.fs` (`Category=Hardware`). The test asserts the FSM transit `Open → Searching(Polling) → Opening(candidate) → Open` driven by an unplug followed by re-seat, without operator input, within the SC-004 5-second budget. The #111 (`Stem.Communication` replacement) migration plan inherits this acceptance check — see [research.md](./research.md) R7's risk note. Until #132 lands, hot-plug recovery is bench-verified by the manual quickstart walkthrough (see [quickstart.md](./quickstart.md) once it lands).

## Complexity Tracking

> Empty — Constitution Check passes without unresolved violations. The single stopgap (STOPGAP_VENDORED_PROTOCOL_STACK) is fully addressed by the discipline in Principle VI (tracking issue [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111), waiver doc `docs/STOPGAP_VENDORED_PROTOCOL_STACK.md`, removal path via the `Stem.Communication` NuGet once `stem-device-manager` Phase 5 completes). No deviation accrual.

## Status

*Last refreshed: 2026-05-27 (Phase B rewrite). Living Plan per the speckit RPI overlay — `Completed` / `Current` / `Blockers`.*

### Completed

- Phase A — spec split into lifecycle (this spec) + panel discovery (spec-003) via PR [#152](https://github.com/luca-veronelli-stem/button-panel-tester/pull/152), 2026-05-26.
- Phase B docs queue items 1–4 — spec.md amendment (`f04318e`), spec-quality checklist (`b99282a` + `f4ddf89`), data-model.md rewrite (`0be6872`), research.md rewrite (`55f0fc9`).
- Substrate implementation (now Phase C input): Phase 1 Setup (T001–T011) via PR-A [#120](https://github.com/luca-veronelli-stem/button-panel-tester/pull/120); Phase 2 Foundational (T012–T033, shared with spec-003) via PR-B [#121](https://github.com/luca-veronelli-stem/button-panel-tester/pull/121); Phase 3 US1 MVP (T034–T043, substrate FSM) via PR-C [#122](https://github.com/luca-veronelli-stem/button-panel-tester/pull/122); Phase 3.5 fix queue 7 of 9 — PRs [#133](https://github.com/luca-veronelli-stem/button-panel-tester/pull/133), [#134](https://github.com/luca-veronelli-stem/button-panel-tester/pull/134), [#135](https://github.com/luca-veronelli-stem/button-panel-tester/pull/135), [#138](https://github.com/luca-veronelli-stem/button-panel-tester/pull/138), [#141](https://github.com/luca-veronelli-stem/button-panel-tester/pull/141), [#145](https://github.com/luca-veronelli-stem/button-panel-tester/pull/145), [#147](https://github.com/luca-veronelli-stem/button-panel-tester/pull/147).

### Current

- Phase B docs queue items 5–11 — this plan.md rewrite (item 5), contracts/can-link-port.md refresh (item 6), tasks.md rewrite (item 7), quickstart.md refresh (item 8), migration-map.md (item 9), cleanup commit (item 10), PR (item 11).

### Blockers

- Phase C (impl reconcile to the Phase B shape) is gated on the Phase B PR landing. The substrate `CanLinkService` on `main` carries the four-family FSM + `Recoverable / Fatal` severity escalation that the redesign retires; reconciliation reshape touches `Core.Can.CanLinkState`, `Services.Can.CanLinkService`, `Infrastructure.Can.PcanCanLink`, `GUI.Can.CanStatusRow`, and re-authors `Phase2/CanLinkState.lean` to the new theorem set. The Phase C plan is its own track and is NOT in scope for the Phase B PR.
- Substrate non-blocking carry-overs tracked for visibility, NOT gating Phase B: [#132](https://github.com/luca-veronelli-stem/button-panel-tester/issues/132) (hot-plug regression test — addressed by Phase C per §CHK028 above), [#136](https://github.com/luca-veronelli-stem/button-panel-tester/issues/136) (substrate `Disconnected` arity widening — superseded by Phase B's five-family redesign; the substrate issue resolves by absorption into Phase C), [#140](https://github.com/luca-veronelli-stem/button-panel-tester/issues/140) (GUI tooltip test — orthogonal to FSM reshape).
