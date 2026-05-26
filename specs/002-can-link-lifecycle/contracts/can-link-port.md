# Contract: `ICanLink` port

**Phase 1 output for**: [../plan.md](../plan.md)
**Implements**: FR-001, FR-002, FR-002a, FR-003, FR-004, FR-005, FR-006 (lifecycle slice)

## Port definition (F#)

Lives in `src/ButtonPanelTester.Core/Can/Ports.fs`.

```fsharp
type ICanLink =
    /// Opens the configured adapter at the given baud rate.
    /// Fires LinkStateChanged with Connected on success; with Disconnected or Error on failure.
    /// Idempotent — calling on an already-open link is a no-op (does NOT fire).
    abstract member OpenAsync : baudrateBps: int * ct: CancellationToken -> Task

    /// Closes the adapter. Fires LinkStateChanged with Disconnected(ReconnectPending, now)
    /// if the link was up; otherwise is a no-op.
    abstract member CloseAsync : ct: CancellationToken -> Task

    /// Reconnect = Close then Open. Equivalent to the technician clicking the reconnect button.
    /// The service layer compares the post-reconnect Error-cause against the pre-reconnect cause
    /// to detect a Recoverable -> Fatal escalation (research.md R8).
    abstract member ReconnectAsync : ct: CancellationToken -> Task

    /// Observable stream of link-state transitions.
    /// Hot observable — subscribers added after a transition do NOT replay it (subscribe at composition time).
    abstract member LinkStateChanged : IObservable<CanLinkState>

    /// Current state at the moment of read. Pull-style accessor for late subscribers / GUI binding /
    /// snapshot tests. Safe to read from any thread (Interlocked read of an atomic reference).
    abstract member CurrentState : CanLinkState
```

## Adapter contract

### Production: `PcanCanLink`

`src/ButtonPanelTester.Infrastructure/Can/PcanCanLink.fs`

- Constructor takes the vendored `ICommunicationPort` (a `CanPort` wrapping `PCANManager`).
- `OpenAsync 250000 ct` calls `CanPort.ConnectAsync(...)` with the vendored config.
- `LinkStateChanged` subscribes to the vendored `StateChanged` event and translates `ConnectionState` → `CanLinkState`.
- Translates PEAK status codes to `Error.Recoverable` / `Error.Fatal`. The first observation of an unrecognised status is `Recoverable`; the service layer escalates to `Fatal` on the second observation (escalation logic lives in `CanLinkService`, per [../research.md](../research.md) R8).
- `IAsyncDisposable`: disposing cancels in-flight Open/Close/Reconnect and emits a final `Disconnected(ReconnectPending, now)`.

### Virtual: `InMemoryCanLink`

`tests/ButtonPanelTester.Tests/Fakes/Can/InMemoryCanLink.fs`

- Constructor takes a scripted `seq<CanLinkState * TimeSpan>` (each state with its delay relative to the previous).
- `OpenAsync` walks the script by one event; each step emits the corresponding `LinkStateChanged` after its `TimeSpan`.
- Used by `CanLinkStateTransitions` property tests and by every `Integration/Can/` end-to-end test.

## Lifecycle invariants

1. `OpenAsync` after a successful `OpenAsync` is a no-op (does NOT fire `LinkStateChanged`).
2. `CloseAsync` after `CloseAsync` is a no-op.
3. `ReconnectAsync` always fires at least one `LinkStateChanged` (the intermediate Disconnected, or the final Connected/Error).
4. Disposing the port (via `IAsyncDisposable`) emits a final `Disconnected(ReconnectPending, now)` and cancels any in-flight call.
5. `CurrentState` is consistent with the latest `OnNext` on `LinkStateChanged` — there is no observable gap during which the two disagree (the subject implementation publishes state then fires, in that order).

## Threading

- `IObservable<CanLinkState>` events may fire on the vendored stack's read thread. The GUI layer marshalls to the UI thread via FuncUI's `Cmd.ofSub`.
- `OpenAsync` / `CloseAsync` / `ReconnectAsync` are safe to call concurrently; the adapter serialises via an internal `SemaphoreSlim(1)`.
- `CurrentState` is safe to read from any thread.

## Test coverage targets

| Test | Layer | File |
|---|---|---|
| `OpenIsIdempotent` | Unit | `Unit/Can/CanLinkStateMachine.fs` |
| `CloseIsIdempotent` | Unit | `Unit/Can/CanLinkStateMachine.fs` |
| `CanLinkStateTransitions` (FsCheck) | Property | `Property/Can/CanLinkStateTransitions.fs` |
| `ReconnectEscalatesRecoverableToFatal` | Integration | `Integration/Can/RecoverableToFatalTests.fs` |
| Bench `OpenAt250kbps` + `UnplugDetected` | Hardware E2E | `Integration/Can/Hardware/PcanLifecycleTests.fs` (`Category=Hardware`) |
