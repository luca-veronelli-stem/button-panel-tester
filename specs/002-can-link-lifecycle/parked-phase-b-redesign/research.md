# Research: CAN Link Lifecycle

**Phase 0 output for**: [plan.md](./plan.md)

**Status**: Phase B rewrite (2026-05-27, supersedes substrate). Phase A (#151, 2026-05-26) split the former combined spec-002 into a lifecycle slice (this spec) and panel discovery (spec-003). Phase B then redesigned the lifecycle FSM from first principles — the substrate's four-family FSM (`Initializing | Connected | Disconnected | Error` with a binary `Recoverable | Fatal` severity classifier) was replaced with a five-family FSM (`Idle | Searching | Opening | Open | Faulted`) carrying sub-discriminators in case payloads.

This document records the Phase 0 decisions for the lifecycle slice in its post-Phase-B shape. §1 covers the Phase B FSM-design decisions (R1..R12), each referencing the spec.md clarification session and/or the 2026-05-27 interview point it came from. §2 carries forward the pre-Phase-B technical decisions that still hold after the redesign (R13..R17). §3 records the decision retired by Phase B (the substrate's Recoverable→Fatal escalation logic).

Cross-references to spec-003 are tagged "Shared with spec-003"; full panel-discovery decisions live in [`specs/003-panel-discovery/research.md`](../003-panel-discovery/research.md).

---

## 1. Phase B FSM-design decisions

### R1 — Writer's posture: v0.1.0 baseline, not substrate gravity

**Origin**: HANDOFF §4e (Luca's 2026-05-26 brainstorm).

**Decision**: write the Phase B artefacts as if specifying spec-002 greenfield against the `v0.1.0` tag (`a6d4c0b`, 2026-05-24 — feat-001 dictionary only, before spec-002 existed). The branch still commits on top of `5198a34` (post-Phase-A `main`); the v0.1.0 baseline is the writer's posture, not the git ref.

**Rationale**: substrate spec-002 plus the Phase 3.5 amendment chain (PRs #133..#147) had pulled FSM-design choices in directions that compounded inconsistently. The 2026-05-27 clarify queue surfaced state-machine smells (HANDOFF §3 Q3) suggesting that re-deriving the FSM without substrate gravity was cheaper than an N-th amendment. Re-deriving from v0.1.0 produced the five-family redesign that the bench-validated clarifications (Sessions 2026-05-24 / -25 / -26) actually fit better than the four-family substrate they had grown on top of.

**Alternatives considered**:

- *Iterate on the substrate FSM* — rejected after the 2026-05-27 clarify queue surfaced smells the substrate could not absorb without escalating amendment complexity.
- *Literally rewind git history to v0.1.0* — no; only the design posture rewinds. Phase B's commits land on top of `5198a34`, and the substrate impl on `main` becomes Phase C drift to reconcile in a later track.

---

### R2 — Five top-level FSM families

**Origin**: Spec.md §Clarifications Session 2026-05-27 §FSM shape Q1; HANDOFF §6.1.

**Decision**: replace the substrate's four-family FSM with five top-level families — `Idle | Searching | Opening | Open | Faulted` — and carry sub-discriminators (`IdleCause`, `SearchAttempt`, `FaultCause`) in case payloads rather than as sibling DU branches. The F# DU is pinned in [data-model.md](./data-model.md) §1.1.

**Rationale**: the chip-colour projection is FSM-family-driven (FR-002), so making the family the top-level DU branch keeps the chip carving and pattern-match exhaustiveness in sync. Sub-discriminators affect the headline string and detail affordance, not the chip — they belong inside the case, not alongside it. The flat five-family shape projects cleanly onto Lean Phase 2's per-family totality theorems (`state_classification_total`, `fault_cause_total`, `idle_cause_total`, `faulted_reconnect_target_total` — data-model.md §1.3 + §4).

**Alternatives considered**:

- *Keep three families + severity classifier* (substrate) — rejected; the `Disconnected` family covered too many distinct in-flight states (cold start vs mid-session unplug vs reconnect pending), and there was no user-driven Pause.
- *Two superstates (`Idle | Active`) with sub-states* — Luca's first sketch (HANDOFF §3 Q3 prose). The superstates collapse into the same chip carving as the five flat families, but the flat shape is easier to pattern-match in F# and Lean, and the chip projection becomes a one-line case match rather than a two-level walk.

---

### R3 — Drop the Recoverable / Fatal severity classifier

**Origin**: HANDOFF §4c; Luca's pushback on the substrate's escalation rule.

**Decision**: drop the binary `Recoverable | Fatal` severity classifier. Replace with named fault causes (`BusOff | UnexpectedAdapterStatus | DriverNotInstalled | AdapterHardwareFailure`), each carrying its own self-descriptive name. The substrate's "Recoverable→Fatal escalation on second observation" rule (substrate FR-002a, pre-Phase-B research.md R8) is retired together with the classifier — see §3 below.

**Rationale**: severity is not a meaningful FSM dimension when the user can read the cause string and infer remediation. "Second observation flips to Fatal" was operational state hidden in `CanLinkService`, didn't change the UX behaviour materially (the user reads the same cause twice and concludes the obvious), and added a Lean theorem (the escalation invariant) the simpler shape doesn't need. Removing the classifier shrinks the FSM payload, the rendering surface, and the Lean re-prove cost simultaneously.

**Alternatives considered**:

- *Keep severity, change the escalation trigger* — same complexity, different fragility (any trigger choice is bench-context-dependent).
- *Express severity as a render-time projection of the FaultCause* — pushes the same decision into the renderer; cleaner to drop it entirely and let each cause's text guide the user.

---

### R4 — Idle is in, operator-paused only

**Origin**: HANDOFF §4b; spec.md Session 2026-05-27 §FSM shape Q2.

**Decision**: include `Idle` as one of the five top-level families. `IdleCause` collapses to a single constructor `UserPaused` (data-model.md §1.1). The FSM begins in `Searching(Polling)` at app launch — not `Idle(_)`. The only edges into `Idle` are operator-initiated Stop clicks; the only edge out is the operator's Start click.

**Rationale**: bench-product convention — professional tools provide an explicit Disconnect / Stop affordance. The primary use case (Luca's framing in HANDOFF §4b) is "release the adapter so another exclusive-mode tool can attach to it", with safety / manual-override / diagnostic-isolation as secondary motivations (spec.md FR-006). The symmetric Start affordance resumes scanning. `IdleCause.AwaitingBoot` was sketched in HANDOFF §6.3 but dropped after the 2026-05-27 §Scope refinements interview (see R10) decoupled the dictionary and CAN boot orders entirely.

**Alternatives considered**:

- *No Idle state* — Claude's initial pushback (HANDOFF §4b). Rejected by Luca: "I usually can see a clear disconnect option" — bench-product convention is load-bearing.
- *Idle with two sub-causes (`AwaitingBoot | UserPaused`)* — HANDOFF §6.3's initial shape. Dropped to `UserPaused` only after R10 decoupled CAN from dictionary boot, leaving `AwaitingBoot` with no reachable producer.

---

### R5 — Multi-adapter iteration as bench-resilience

**Origin**: HANDOFF §4a; spec.md Session 2026-05-27 §Edges and iteration Q2.

**Decision**: when `Searching` enumerates ≥ 1 PEAK adapter, `Opening` iterates through each enumerated candidate (FR-012) before declaring `Searching(NoCandidateAvailable count)`. "First available wins" stays the rule — no UI for adapter selection. The single-adapter bench is the `count = 1` special case (the production reality); multi-adapter iteration covers the accidental two-adapter bench case.

**Iteration cap**: every candidate the vendored stack enumerates is tried; there is no internal cap. (Closes OPEN-FINDINGS U2.) The enumeration result is bounded by the host's PEAK driver, which in practice returns ≤ a single-digit count.

**Rationale**: production setup is always one PEAK adapter on the test workstation (spec.md §Assumptions). Iteration exists so a tech accidentally plugging in two adapters with the first one busy does not cause "first one busy → give up" failure. Bench resilience, not a feature.

**Alternatives considered**:

- *First-wins with no iteration* (substrate's tacit behaviour) — rejected; the bench edge case (two adapters, first busy) was the spark for the redesign clarify queue.
- *User-facing adapter selection UI* — over-engineering; production setup is single-adapter, multi-adapter is a transient bench accident.
- *Bounded iteration cap (e.g. ≤ 4)* — premature; the vendored stack's enumeration is already host-bounded, and a per-FSM cap adds a tuning knob with no evidence of need.

---

### R6 — `Faulted` carries the candidate in its payload

**Origin**: Spec.md Session 2026-05-27 §FSM shape Q3.

**Decision**: `Faulted` carries `candidate: AdapterCandidate option` in its payload (data-model.md §1.1). When `Some c`, Reconnect → `Opening(c, now)` — retry the known candidate. When `None` (driver-not-installed pre-enumeration), Reconnect → `Searching(Polling, now)` — no candidate to retry, fall back to a scan. The FR-008 button caption SHOULD make this clear in the `None` case.

**Rationale**: the FSM should be self-describing. No extra service-level side state is needed to drive Reconnect — the candidate-or-not lives where the Reconnect handler can see it via a single pattern match. The `option` shape captures the only two cases that exist: either there was a candidate to remember (Open failed against a known adapter, or bus-off occurred while Open against one), or there wasn't (driver missing before enumeration could produce one). The Lean re-prove cost is one small auxiliary theorem (`faulted_reconnect_target_total`, data-model.md §4).

**Alternatives considered**:

- *Service-level "last attempted candidate" field* — couples FSM to mutable service state, complicates the test seam (every test now needs to seed and inspect the field).
- *Two `Faulted` cases (`FaultedWithCandidate | FaultedNoCandidate`)* — same information, more DU branches, asymmetric pattern matches on every render and every Reconnect handler. Rejected as DU bloat.
- *Embed the candidate as a non-optional field with a sentinel value* — type-system sleight of hand; the FSM should not lie about which states have an adapter to retry.

---

### R7 — Hot-plug recovery as an explicit FSM edge

**Origin**: Spec.md Session 2026-05-27 §Edges and iteration Q1.

**Decision**: model hot-plug recovery as an explicit `Searching(Polling) ── vendored-stack device-arrived event ─▶ Opening(candidate)` edge in the state diagram (data-model.md §1.2). The dependency on the vendored protocol stack's hot-plug semantics is named in spec.md §Dependencies as a blocking dependency, with the #111 risk note that the future `Stem.Communication` replacement MUST preserve the affordance — or hot-plug auto-recovery regresses to manual-Reconnect-only.

**Rationale**: the substrate observed hot-plug as an undocumented vendored-stack freebie — it worked because `Infrastructure.Protocol` happened to surface `PnP arrival` events from PCAN. Making it an explicit edge makes the dependency contract visible and makes the #111 migration's regression risk concrete: the migration plan MUST add an acceptance check that the device-arrived event still produces an `Opening` transition (or flag the regression).

**Alternatives considered**:

- *Keep implicit* — accumulated migration risk; the freebie evaporates the day #111 ships without an equivalent event.
- *Polling-only, no event-driven edge* — works but slow; bench feel would regress on every unplug-replug cycle to a multi-second poll wait.
- *Polling plus event* (the chosen shape) — the event is the fast path, the periodic poll is the safety net for the case where the vendored stack misses an event. Cadence pinned in plan.md (see R12).

---

### R8 — Truth-and-acknowledge framing mirroring `DictionaryStatusRow`

**Origin**: Spec.md Session 2026-05-25 (substrate, carried forward) + Session 2026-05-27 §Framing.

**Decision**: chip colour MUST always reflect the actual FSM family (truth-to-state, FR-002 / FR-009) — no smoothing, no minimum-visibility floor, no chip-level UX layer added to make a click visible. Operator-initiatable affordance buttons (Stop / Start / Reconnect) get a button-level click-acknowledge cue: `IsEnabled = false` plus the `⟳` glyph from `DictionaryStatusRow.fs:151-158` for the duration of the in-flight call (FR-009 + SC-010). State rendering and click-acknowledgement are independent layers; the chip never lies about FSM state to make a click visible.

**Rationale**: feat-001's `DictionaryStatusRow` established the precedent — chip colour = subsystem truth (under spec-001 [#156](https://github.com/luca-veronelli-stem/button-panel-tester/issues/156) Option γ: dictionary-sync state, with copy-health rendered as separate decoration via origin marker + stale-seed glyph), chip opacity drop + headline ellipsis + spinner-glyph button = click acknowledgement. The substantive two-layer pattern (subsystem-truth chip + button-level click cue) survives #156's chip-carving reshape unchanged. The CAN row inherits the same two-layer separation so the operator's "what is true" channel and "did my click do something" channel stay clean across both surfaces of the main window. A sub-perceptual call (e.g. Start, or a fast Reconnect) is consistent with truth-to-state — the operator's signal that the click was processed comes from the resulting FSM transition (chip colour change and/or `since` update), not from the cue itself.

**Alternatives considered**:

- *Force a grey "in-flight" chip transit during Reconnect regardless of FSM family* — rejected; would lie about state during sub-perceptual returns and would mask the FSM's actual progression through `Opening`.
- *Skip the click-acknowledge cue entirely* — rejected; the operator needs feedback that the click was processed, particularly for slow OpenAsync calls where the FSM dwells in `Opening` for a perceptible duration.
- *Minimum-visibility floor on the cue (e.g. show `⟳` for at least 200 ms even if the call finishes faster)* — rejected; pinning the cue to the actual call duration keeps the two layers independent and avoids the "did it actually do something?" ambiguity that a minimum-visibility floor introduces on a fast no-op.

---

### R9 — Adapter exclusivity: BPT requests exclusive driver-level access on Open

**Origin**: Spec.md Session 2026-05-27 §Scope refinements Q1.

**Decision**: on the OpenAsync that lands the FSM in `Open`, the System MUST request exclusive driver-level access to the PEAK adapter (FR-010). External exclusive-mode tools (StemDeviceManager etc.) see the driver's busy response while BPT holds Open. Shared-mode tools (PCAN-View) coexist without contention at the driver level. The driver enforces the lock; BPT observes contention via the vendored protocol stack's surface (FR-011, conditional on the stack surfacing the event) but does NOT transition out of Open — only an operator Stop releases the adapter.

**Rationale**: matches bench convention — professional tools claim exclusive access on Open and hold it. The driver-level fence makes external contention observable without BPT needing to arbitrate at the application level. FR-011's conditional MUST acknowledges that the current `Infrastructure.Protocol` may be silent on contention events; the FR is forward-looking against #111's `Stem.Communication` replacement and becomes a hard MUST once a contention event is surfaced.

**Alternatives considered**:

- *Shared access on Open* — would force BPT to coordinate with external tools at the application level, well outside scope.
- *Auto-yield on contention* — rejected; the operator must explicitly Stop to release. Auto-yield would race with the operator's mental model of "what state am I in?" and introduce a non-operator-initiated transition out of Open.
- *Exclusive on Open with auto-Reconnect on lock loss* — rejected; the driver enforces the lock so "lock loss while held" should not occur. If it ever does, that's a `Faulted(AdapterHardwareFailure, _, _)` situation, not an auto-yield.

---

### R10 — CAN boot order decoupled from dictionary boot

**Origin**: Spec.md Session 2026-05-27 §Scope refinements Q2; HANDOFF §6.3 mental patch.

**Decision**: CAN service starts at app launch independently of dictionary boot. The FSM begins in `Searching(Polling)` from app launch, NOT `Idle(AwaitingBoot)`. The substrate's gate (substrate FR-001 — CAN service Open after dictionary boot completes) was a composition-root policy, not a technical requirement. Dictionary content is never consulted to drive any CAN-side decisioning in this slice (spec.md §Assumptions). `IdleCause` consequently collapses to `UserPaused` only (R4).

**Rationale**: dictionary and CAN have no shared infrastructure dependency. The substrate's gate added a synchronisation point with no observable user benefit — it forced the CAN row to dwell in a no-op state for the first ~1 s of every launch even on a hot dictionary cache.

**OPEN-FINDINGS U1 addressed here**: the decoupling is safe by construction because spec-001 guarantees the dictionary is always usable at runtime. The embedded seed in the binary (spec-001 §138) covers the no-network, no-prior-cache case, so the dictionary status row reaches `Cached(from seed)` immediately on first launch and is fully usable. No scenario in spec-002 needs to consider an "unusable dictionary" branch — the upstream guarantee makes the CAN lifecycle's independence safe even when the dictionary boot has not yet produced a Live fetch. Spec-002's §Assumptions cross-references spec-001 §138 explicitly for this reason.

**Alternatives considered**:

- *Keep the boot gate* — adds a synchronisation point with no observable user benefit; would force the CAN row to dwell in `Idle(AwaitingBoot)` for the first ~1 s of every launch even when the dictionary cache is hot.
- *Decouple only the FSM but keep a service-level gate* — half-measure; if the FSM doesn't gate, the service doesn't need to either, and the gate becomes dead policy.
- *Couple the other direction (dictionary waits for CAN)* — backwards; CAN takes longer to reach Open than dictionary takes to reach Cached, so coupling either way slows the slower side.

---

### R11 — NotificationCenter cause/suggestion split deferred to a future spec

**Origin**: HANDOFF §4d; spec.md §Out of Scope last bullet.

**Decision**: the CAN status row carries both `<cause>` and `<imperative suggestion>` joined by em-dash (FR-003) as the transitional convention until a future NotificationCenter spec ships. A future NotificationCenter (when it ships) MAY refactor how the cause/suggestion split surfaces — moving the suggestion to a notification surface and leaving the row to carry only the truth-to-state cause. Spec-002 does NOT anticipate that future refactor at the FR level; the em-dash convention is self-contained.

**Rationale**: NotificationCenter's trigger would be `LinkStateChanged → severity ≥ warning`, which needs a specified lifecycle as input — landing NotificationCenter first would invert the dependency. The substrate already has the row carrying suggestions; tearing that out preemptively before NotificationCenter exists would create a UX regression window where the row carries less than it does today. Spec-002 isn't held hostage to NotificationCenter; both can ship in their natural order, with the spec-002 row carrying the cause+suggestion load until NotificationCenter ships and takes the suggestion half.

**Alternatives considered**:

- *Land NotificationCenter first* — would block spec-002 indefinitely on a spec that isn't yet drafted.
- *Specify the future split in spec-002 with a forward-callout that binds FR-003* — would prematurely couple spec-002's FRs to NotificationCenter's not-yet-designed surface.

---

### R12 — Searching retry cadence deferred to plan.md

**Origin**: HANDOFF §6.8.

**Decision**: the FSM contract states "Searching transitions to `Opening` when a candidate becomes available, whether via vendored-stack device-arrived event or periodic re-scan". The concrete cadence (periodic poll interval, event-only vs both) is pinned in [plan.md](./plan.md).

**Rationale**: cadence is implementation detail that does not affect the FSM's observable contract or the chip-colour projection. It affects bench feel (how quickly hot-plug-after-unplug recovers when the event-driven edge misses) but not correctness. Pinning the cadence in plan.md keeps spec.md and data-model.md cadence-agnostic and lets the cadence be re-tuned without touching the FSM contract.

**Alternatives considered**:

- *Pin a cadence in spec.md* — over-constraining; cadence is a tuning knob, not a contract.
- *Make cadence configurable in `appsettings.json`* — premature; the supplier bench doesn't ask for the knob, and the configurable surface is itself a maintenance burden.

---

## 2. Pre-Phase-B technical decisions carried forward

The decisions below predate Phase B (Phase A split, #151, 2026-05-26) and are still load-bearing for the lifecycle slice after the FSM redesign. They are reproduced here in full because Phase B's research.md is the new canonical archive — the pre-Phase-B research.md is superseded in place.

---

### R13 — Vendor file inventory (which files cross from `stem-device-manager`)

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

### R14 — Two ports (link lifecycle + frame stream), split rather than combined

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

Each port has a production adapter (`PcanCanLink` / `PcanCanFrameStream` — both wrapping the vendored stack) and a virtual adapter (`InMemoryCanLink` / `InMemoryCanFrameStream` — scripted sequences for tests).

**Rationale**: lifecycle and frame stream are orthogonal. Splitting them lets `CanLinkStateTransitions` property tests stub just `ICanLink` and the panel-discovery property tests stub just `ICanFrameStream`. Phase 0 mining confirmed the vendored stack itself exposes both surfaces separately (`StateChanged` vs `PacketReceived`).

**Alternatives considered**: single `ICanAdapter` god port (rejected, verbose test setup); three ports (rejected, over-decomposed); state-changes-as-frames (rejected, defeats the type system).

**Phase B note**: the port surface shown above is the substrate's shape. Phase B's redesigned `CanLinkState` (five families with case-payload sub-discriminators) flows through this surface unchanged — the port's payload type is `CanLinkState`, and the type's internal shape is the carrier's concern. The corresponding contract document [`contracts/can-link-port.md`](./contracts/can-link-port.md) is drifted against the new state shape; whether to refresh the contract before tasks.md or to defer the contract refresh to Phase C is pending — see plan.md §Project Structure and OPEN-FINDINGS item 1.

---

### R15 — `IObservable<T>` for port contracts, hand-rolled subjects for adapters

**Shared with spec-003.**

**Decision**: the port contracts expose BCL `IObservable<T>` directly. Production adapters use a small hand-rolled subject implementation (`ConcurrentBag<IObserver<T>>` + thread-safe `OnNext` fan-out + `IDisposable` per subscription) rather than pulling in the `System.Reactive` package.

**Rationale**: `IObservable<T>` lives in `System` (BCL); no NuGet needed. Adapters only need OnNext/OnError/OnCompleted dispatch (≈ 30 LOC). Bridging to FuncUI's Elmish loop is a `Cmd.ofSub` that calls `Subscribe(observer)` and disposes on teardown.

**Alternatives considered**: F# `IEvent<T>` (rejected, language-coupled); `System.Reactive` `Subject<T>` (rejected, no operator use justifies the package surface); callbacks (rejected, multi-subscriber by hand is what `IObservable<T>` standardises).

**Cross-platform note**: when `CanLinkService` switches off the vendored stack toward the future `Stem.Communication` NuGet, the port contract is unchanged — only the adapter swaps.

---

### R16 — Lean Phase 2 conventions (mirror Phase 1)

**Shared with spec-003.**

**Decision**: Phase 2 mirrors Phase 1 exactly:

- Lean v4.29.1 (pinned in `lean/lean-toolchain`, unchanged).
- No mathlib. Only core Lean 4 inductives + `rfl` / `cases` / `simp` tactics.
- `namespace Stem.ButtonPanelTester.Phase2` (one namespace, six modules — two lifecycle, four panel-discovery).
- One theorem per file. Theorem statement IS the design contract; proof is `by rfl` or `by cases ... <;> simp`. No `sorry`, no custom axioms.
- Types are polymorphic in implementation-specific parameters where doing so keeps modules independent.
- Verbose comment headers explaining: what the module mechanises, which spec/data-model section, which Principle gate.
- `lean/lakefile.toml` gains a second `[[lean_lib]]` entry `Stem.ButtonPanelTester.Phase2`; `defaultTargets` extended to include both phases.

**Lifecycle modules** (post-Phase-B): `Phase2/CanLinkState.lean` carries four theorems (`state_classification_total` over five families, `fault_cause_total`, `idle_cause_total`, `faulted_reconnect_target_total`); `Phase2/PassiveObserver.lean` carries `observe_emits_no_transmit` unchanged from the substrate. Full cross-reference table in [data-model.md](./data-model.md) §4.

**Panel-discovery modules** (`Phase2/WhoIAmFrame.lean`, `Phase2/PanelObservation.lean`, `Phase2/PanelsOnBus.lean`, `Phase2/Pruning.lean`) cohabit the same `[[lean_lib]]` entry — see [`specs/003-panel-discovery/research.md`](../003-panel-discovery/research.md) R6.

**Phase B note**: the "one theorem per file" rule is relaxed for `Phase2/CanLinkState.lean` only — the four per-family totality lemmas (plus the Reconnect-target auxiliary) are co-located because they all unfold over the same `CanLinkState` inductive and `by cases` walks the same constructor list. Splitting across four files would create artificial import chains for no proof-decomposition benefit.

---

### R17 — Composition with feat-001's existing main window

**Shared with spec-003 (lifecycle owns rows 1–2 of the vertical stack, spec-003 owns row 3).**

**Decision**: `App.fs` (the FuncUI shell) gains a vertical-stack panel that hosts `DictionaryStatusRow` (top, from feat-001), `CanStatusRow` (middle, lifecycle), and `PanelsOnBusView` (bottom — fills remaining space, spec-003). The composition root wires `ICanLink` + `CanLinkService` after the `DictionaryService` initialises; the FuncUI sub-program for the CAN side runs in parallel with the dictionary sub-program (independent Elmish loops, composed via FuncUI's parent-child message routing).

**Rationale**: keeps the dictionary side untouched (Principle IV's "the two rows are independent" — FR-015). The vertical stack matches the spec's "alongside" language. Parallel Elmish sub-programs are the FuncUI-idiomatic composition pattern.

**Alternatives considered**: single Elmish loop with a combined `Msg` DU (rejected, couples state); tab UI (rejected, spec wants both visible); floating window (rejected, multi-window is harder to capture in headless tests).

**Phase B note**: the "after the `DictionaryService` initialises" sequencing was a substrate composition-root policy; Phase B's R10 decoupling means the CAN sub-program can start in parallel with the dictionary sub-program, both wired off `App.fs` startup. The spec-001 seed-fallback guarantee (R10) makes the dictionary fully usable from the moment its sub-program starts, so the two rows can populate independently without any cross-row interlock.

---

## 3. Decisions retired by Phase B

### Pre-Phase-B R8 — Error-state classification (Recoverable→Fatal escalation)

**Retired** with the severity classifier (see §1 R3 above).

The substrate's escalation rule (first observation of an unexpected PEAK status → `Error.Recoverable`; second observation after a Reconnect attempt → `Error.Fatal`; counter reset on successful Open) was operational state hidden in `CanLinkService`, mechanised by `transition_reachability_closed` in `Phase2/CanLinkState.lean` (now also retired — see data-model.md §4). The rule is replaced by named fault causes whose self-descriptive names carry the user's "should I retry?" hint without a hidden counter.

The `CanLinkService` impl on `main` still carries the escalation logic (substrate code that ships); reconciliation with the new shape is Phase C drift to be tracked in [migration-map.md](./migration-map.md).

---

## Open follow-ups (do not block this slice)

- **Pin the upstream `stem-device-manager` SHA** in `VENDOR.md` at vendoring time (see [contracts/vendor-manifest.md](./contracts/vendor-manifest.md)).
- **One upstream PR back to `stem-device-manager`** at vendor time: `CancellationTokenSource` + `IAsyncDisposable` added to `PCANManager` so the background read/monitor tasks stop cleanly on dispose. **Shipped** via `stem-device-manager#118` (vendored modification routed upstream); see PR [#134](https://github.com/luca-veronelli-stem/button-panel-tester/pull/134) for the BPT-side adoption.
- **Tracking issue for STOPGAP_VENDORED_PROTOCOL_STACK**: [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111). Removal targets the `Stem.Communication` NuGet once `stem-device-manager` Phase 5 completes. Phase B's R7 explicit hot-plug edge MUST be preserved by the replacement.
- **Tracking issue for the Hardware-Test-Setup**: [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112). Covers the `Category=Hardware` E2E suite for both lifecycle (this spec) and panel discovery (spec-003).
- **`contracts/can-link-port.md` refresh**: drifted against Phase B's five-family `CanLinkState` payload. Whether to refresh between plan.md and tasks.md or defer to Phase C via migration-map.md is open — see OPEN-FINDINGS item 1.
