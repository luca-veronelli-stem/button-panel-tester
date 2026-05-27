# Contract: `ICanLink` port

**Phase 1 output for**: [../plan.md](../plan.md)

**Status**: Phase B refresh (2026-05-27, supersedes substrate). Aligned with [`../spec.md`](../spec.md) `f04318e`, [`../data-model.md`](../data-model.md) `0be6872`, [`../research.md`](../research.md) `55f0fc9`, and [`../plan.md`](../plan.md) `ce2c901`. The port **signature** is unchanged from the substrate (`OpenAsync` / `CloseAsync` / `ReconnectAsync` / `LinkStateChanged` / `CurrentState`); the payload type is the five-family [`CanLinkState`](../data-model.md#11-canlinkstate-closed-du-srcbuttonpaneltestercorecancanlinkstatefs). The substrate's `Recoverable / Fatal` severity classifier is retired ([`../research.md`](../research.md) §3); references to `Connected` / `Disconnected(ReconnectPending)` / `Error.Recoverable` / `Error.Fatal` are gone.

**Implements (lifecycle slice)**: FR-001 (five-family FSM), FR-004 (sticky-since via `CurrentState` + `LinkStateChanged`), FR-006 (Stop-during-`Opening` cancellation within the [plan.md](../plan.md) ≤ 250 ms budget), FR-008 (Reconnect bifurcation on `Faulted` candidate option), FR-010 (exclusive driver-level access on the OpenAsync that lands in `Open`), FR-013 (no CAN transmit), FR-014 (`LinkStateChanged` observable carrying the full `CanLinkState`).

## Port definition (F#)

Lives in `src/ButtonPanelTester.Core/Can/Ports.fs`. The companion `ICanFrameStream` port (spec-003) cohabits the same file ([`../research.md`](../research.md) R14).

```fsharp
type ICanLink =
    /// Begins (or resumes) the link lifecycle at the given baud rate.
    /// Called once at app launch by the composition root to leave the FSM's
    /// initial `Searching(Polling, now)` and again on the operator's Start
    /// click after a Stop. Internally enumerates PEAK adapters via
    /// `PcanAdapterEnumeration` (Infrastructure helper) and iterates each
    /// enumerated candidate (FR-012); the first candidate whose underlying
    /// OpenAsync succeeds wins, with exclusive driver-level access requested
    /// on that call (FR-010). The FSM observable transitions to:
    ///   * `Opening(candidate, now)` per candidate attempt;
    ///   * `Open(adapter, now)` on the first successful open;
    ///   * `Faulted(cause, Some candidate, now)` on a non-iteration failure
    ///     against a specific candidate (e.g. unexpected status, hardware
    ///     failure);
    ///   * `Faulted(DriverNotInstalled, None, now)` if the PCANBasic native
    ///     DLL is missing pre-enumeration;
    ///   * `Searching(NoCandidateAvailable count, now)` when every enumerated
    ///     candidate returned busy;
    ///   * `Searching(NoAdapterEnumerated, now)` when enumeration returned
    ///     zero candidates.
    /// Honours `ct`: if cancellation fires while the FSM is in `Opening`, the
    /// in-flight driver call is cancelled and the FSM lands in
    /// `Idle(UserPaused, now)` within the FR-006 cancellation budget pinned in
    /// [plan.md](../plan.md) (≤ 250 ms on a normal-load workstation).
    /// `OpenAsync` against a port that is already in `Open` is a no-op (the
    /// driver lock is held; no `LinkStateChanged` emission).
    abstract member OpenAsync : baudrateBps: int * ct: CancellationToken -> Task

    /// Realises the operator Stop affordance (FR-006). Cancels any in-flight
    /// OpenAsync via the supplied `ct` propagation, releases the driver-level
    /// adapter handle (FR-010 fence is dropped), and emits
    /// `Idle(UserPaused, now)` on `LinkStateChanged`. A Stop from any active
    /// state (`Searching` / `Opening` / `Open` / `Faulted`) is well-defined;
    /// a Stop from `Idle(UserPaused)` is a no-op (no emission).
    abstract member CloseAsync : ct: CancellationToken -> Task

    /// Realises the operator Reconnect affordance from `Faulted` (FR-008).
    /// Bifurcates on the candidate carried in the current `Faulted` payload:
    ///   * `Faulted(_, Some candidate, _)` → `Opening(candidate, now)` then
    ///     `Open(adapter, now)` on success or `Faulted(cause, Some candidate, now)`
    ///     on failure — retries the known candidate;
    ///   * `Faulted(_, None, _)` → `Searching(Polling, now)`, falls back to the
    ///     scan loop because there is no candidate to retry (driver-not-installed
    ///     case before any enumeration produced one).
    /// `ReconnectAsync` from any non-`Faulted` family is a no-op (no emission).
    /// Honours `ct` for cancellation; the post-reconnect `since` is refreshed
    /// when the FSM returns to the same family via the intervening transition
    /// (FR-004 update rule 3).
    abstract member ReconnectAsync : ct: CancellationToken -> Task

    /// Observable stream of FSM transitions. Emits the full `CanLinkState`
    /// value (family + discriminator + payload + `since`) on every transition;
    /// consumers project the parts they need (chip colour from the family,
    /// headline from the discriminator, detail affordance from the payload).
    /// Hot observable — subscribers added after a transition do NOT replay it;
    /// subscribe at composition time and use `CurrentState` for the initial
    /// render (FR-014).
    abstract member LinkStateChanged : IObservable<CanLinkState>

    /// Current `CanLinkState` at the moment of read. Pull-style accessor for
    /// late subscribers, GUI binding, and snapshot tests. Consistent with the
    /// most recent `LinkStateChanged.OnNext`: the adapter publishes the new
    /// state to the backing reference, then fires the observable, in that
    /// order — so there is no observable gap during which `CurrentState` and
    /// the last emission disagree (lifecycle invariant 1 below). Safe to read
    /// from any thread.
    abstract member CurrentState : CanLinkState
```

## Adapter contract

### Production: `PcanCanLink`

`src/ButtonPanelTester.Infrastructure/Can/PcanCanLink.fs`

- Constructor takes the vendored `ICommunicationPort` (a `CanPort` wrapping `PCANManager`) and the Infrastructure-internal `PcanAdapterEnumeration` helper.
- `OpenAsync 250000 ct` drives the FR-012 iteration loop **inside the adapter**: enumerates candidates via `PcanAdapterEnumeration.enumerate()`, then for each candidate emits `Opening(candidate, now)` and calls the vendored driver requesting **exclusive driver-level access** (FR-010). The first candidate that returns a successful open lands the FSM in `Open(adapter, now)`; a non-iteration failure lands in `Faulted(cause, Some candidate, now)`; an enumeration that returns zero candidates lands in `Searching(NoAdapterEnumerated, now)`; an iteration that exhausts every enumerated candidate with the driver reporting busy lands in `Searching(NoCandidateAvailable count, now)`; a missing PCANBasic native DLL detected before enumeration lands in `Faulted(DriverNotInstalled, None, now)`. The high-level "open the link" entry is the port surface; the FSM-bearing `CanLinkService` exposes FR-012 through its lifecycle API and routes operator Start / Stop / Reconnect clicks without owning the per-candidate iteration mechanics ([../research.md](../research.md) R14, [../plan.md](../plan.md) §Project Structure).
- **FR-006 cancellation**: the `CancellationToken` is propagated into the in-flight vendored-driver call. On cancellation while in `Opening`, the adapter cancels the call, releases any partially-acquired driver handle, and emits `Idle(UserPaused, now)` within the **≤ 250 ms budget pinned in [plan.md](../plan.md) §"FR-006 cancellation budget"**. The transition MUST NOT wait for the in-flight OpenAsync to complete on its own.
- `LinkStateChanged` subscribes to the vendored `StateChanged` event and translates each vendored emission into a five-family `CanLinkState` value carrying the discriminator payload and FR-004 sticky-`since`. The vendored stack's PnP arrival event is bridged into an `IObservable` emission that re-enters `Searching(Polling, now)` then drives a fresh enumeration round ([../research.md](../research.md) R7).
- The post-Open self-description (`AdapterIdentification`) is built by `PcanAdapterIdentity` (existing Infrastructure helper, `PcanAdapterIdentity.fs` — pinned by [../plan.md](../plan.md) §Project Structure) on the OpenAsync success path, carrying `ChannelName`, `DeviceId`, and the configured `BaudrateBps` (data-model.md §3.1).
- **No severity classification**: `PcanCanLink` does not maintain a Recoverable/Fatal counter and the vendored status-code mapping in `PeakErrorText` (existing) emits one of the named `FaultCause` constructors (`BusOff` / `UnexpectedAdapterStatus code` / `DriverNotInstalled` / `AdapterHardwareFailure`) directly.
- `IAsyncDisposable`: disposing cancels any in-flight Open / Close / Reconnect via the internal `SemaphoreSlim(1)` lifetime, releases the driver handle if held, and completes the `LinkStateChanged` subject.

### Virtual: `InMemoryCanLink`

`tests/ButtonPanelTester.Tests/Fakes/Can/InMemoryCanLink.fs`

- Constructor takes a scripted `seq<CanLinkState * TimeSpan>` — each step is a five-family `CanLinkState` value with its delay relative to the previous emission.
- `OpenAsync` / `ReconnectAsync` walk the script by one event; each step emits the corresponding `LinkStateChanged.OnNext` after its `TimeSpan`. `CloseAsync` short-circuits to an `Idle(UserPaused, now)` emission regardless of the script position.
- Honours `ct` for cancellation; the FsCheck `CanLinkStateTransitions` property suite drives Stop-during-`Opening` scenarios through the fake to exercise the FR-006 propagation contract independent of the PEAK driver.
- Used by `CanLinkStateTransitions`, `CanLinkStickyTimestamp`, and `LinkStateChangedFamilyExhaustive` property suites, by every `Integration/Can/CanLinkServiceLifecycleTests` end-to-end test, and by the GUI `CanStatusRowTests` (Avalonia.Headless) when a deterministic state script is needed.

## Lifecycle invariants

1. **`CurrentState` consistency.** `CurrentState` always equals the payload of the most recent `LinkStateChanged.OnNext`. The adapter publishes the new state to the backing reference **before** firing the observable, so there is no observable gap during which the two disagree. Carries forward from the substrate unchanged because the property is shape-agnostic.
2. **Stop-during-`Opening` cancellation (FR-006).** Cancelling the `CancellationToken` supplied to `OpenAsync` while the FSM is in `Opening` MUST land the FSM in `Idle(UserPaused, now)` within the **≤ 250 ms budget pinned in [plan.md](../plan.md) §"FR-006 cancellation budget"**. The transition MUST NOT block waiting for the in-flight driver call to complete on its own. Verified by `StopDuringOpeningCancelsWithinBudget` (integration, see test coverage targets) and against real hardware by `PcanLifecycleTests`.
3. **`ReconnectAsync` bifurcation (FR-008).** A `ReconnectAsync` invoked while the FSM is in `Faulted(_, Some candidate, _)` MUST emit `Opening(candidate, now)` as its first transition; a `ReconnectAsync` while in `Faulted(_, None, _)` MUST emit `Searching(Polling, now)`. `ReconnectAsync` from any non-`Faulted` family is a no-op and MUST NOT emit. Mechanised in Lean by `faulted_reconnect_target_total` ([../data-model.md](../data-model.md) §1.3 Invariant #4).
4. **Sticky-`since` (FR-004 / Invariant #5).** The adapter MUST preserve the `since` (or `openedAt` for `Open`) timestamp across passive re-observation of the same family + discriminator. Updates fire on (a) family change, (b) discriminator change within a family, (c) a user-initiated transition that returns the FSM to the same family via an intervening state. Verified by the `CanLinkStickyTimestamp` property suite.
5. **No CAN transmit (FR-013, SC-007).** No adapter method emits a CAN frame on the bus. The lifecycle is open-and-observe only; the projection of the FSM's transitions onto the CAN transmit alphabet is the empty trace. Mechanised in Lean by `observe_emits_no_transmit` ([../data-model.md](../data-model.md) §1.3 Invariant #7).
6. **Disposal cancels in-flight calls.** Disposing the port via `IAsyncDisposable` cancels any in-flight `OpenAsync` / `CloseAsync` / `ReconnectAsync`, releases the driver handle if held, and completes the `LinkStateChanged` subject. No specific terminal state is pinned — disposal is a lifecycle terminator, not an FSM transition.

## Threading

- `IObservable<CanLinkState>` events may fire on the vendored stack's read thread. The GUI layer marshalls to the UI thread via FuncUI's `Cmd.ofSub` ([../research.md](../research.md) R15).
- `OpenAsync` / `CloseAsync` / `ReconnectAsync` are safe to call concurrently; the adapter serialises via an internal `SemaphoreSlim(1)`. The FR-006 Stop-during-`Opening` cancellation MUST observe the budget (lifecycle invariant 2) regardless of contention on the semaphore — a queued Stop is dispatched against the in-flight Open's `CancellationToken`, not held behind it.
- `CurrentState` is safe to read from any thread; the backing reference is read via `Volatile.Read` (or the F# equivalent on `ref` cells) so a concurrent `OnNext` cannot tear the read.
- The FR-014 `LinkStateChanged` is the only observable surface — there is no separate state-changed event. Consumers that only care about family-level transitions filter the stream themselves.

## Test coverage targets

| Test | Layer | File |
|---|---|---|
| `CanLinkStateTransitions` (FsCheck) | Property | `Property/Can/CanLinkStateTransitionsProperties.fs` |
| `CanLinkStickyTimestamp` (FsCheck) | Property | `Property/Can/CanLinkStickyTimestampProperties.fs` |
| `LinkStateChangedFamilyExhaustive` (FsCheck) | Property | `Property/Can/LinkStateChangedFamilyExhaustiveProperties.fs` |
| `CanLinkServiceLifecycleTests.StopDuringOpeningCancelsWithinBudget` (FR-006, ≤ 250 ms) | Integration | `Integration/Can/CanLinkServiceLifecycleTests.fs` |
| `CanLinkServiceLifecycleTests.StopReleasesAdapterHandle` (CHK018 surrogate, fake `IPcanDriver` records `CAN_Uninitialize`) | Integration | `Integration/Can/CanLinkServiceLifecycleTests.fs` |
| `CanStatusRowTests` (SC-010 click-acknowledge cue, `IsEnabled = false` + `⟳` glyph) | GUI / Avalonia.Headless | `Gui/Can/CanStatusRowTests.fs` |
| `PcanLifecycleTests` (SC-001 / SC-003 / SC-004 / SC-008 / SC-009 / SC-011) | Hardware E2E (`Category=Hardware`) | `Integration/Can/Hardware/PcanLifecycleTests.fs` |

Test scaffolding for the hot-plug acceptance (SC-004 re-seat without click) lives in `PcanLifecycleTests` per [plan.md](../plan.md) §CHK028; the dedicated `HotPlugRecoveryAfterUnplug` assertion is gap-noted by [#132](https://github.com/luca-veronelli-stem/button-panel-tester/issues/132) and addressed in the Phase C impl reconcile track.
