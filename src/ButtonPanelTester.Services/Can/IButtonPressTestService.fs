namespace Stem.ButtonPanelTester.Services.Can

open System
open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Can

/// Service-level facade driving the spec-005 button-press test: a modal,
/// single-attempt session that prompts a technician through each active button
/// of a baptized panel's variant schema, observes the CAN `VAR_WRITE`
/// button-state frames (RX-only — no transmitter), and scores each prompt
/// `Pass` / `Missed` / `Unexpected` / `Skipped`, per
/// `specs/005-button-press-test/plan.md`, `data-model.md` §4, and research R7/R8.
/// The concrete `ButtonPressTestService` drives the pure FSM
/// (`ButtonPressTest.step`, Core) over the consumed observables
/// (`IButtonStateObserver`, `IPanelDiscoveryService`, `ICanLinkService`) and an
/// `IClock`-armed per-button deadline.
///
/// `RunAsync` is the Start entrypoint; it is modal — a second concurrent run
/// while one is in flight is a programming bug and throws
/// `InvalidOperationException`. `Retry` / `Skip` are the technician recovery
/// actions on the in-flight (or `Missed`) button; `RerunAsync` restarts the
/// last run's panel + schema with a cleared result grid (FR-003). `StateChanged`
/// is the hot observable the GUI subscribes through `Cmd.ofSub`; `CurrentState`
/// is the pull-style accessor consistent with the latest `StateChanged`
/// emission.
type IButtonPressTestService =
    /// Start ONE button-press-test run for the `selected` panel against its
    /// variant `schema`: arms button 0's countdown (`ButtonPressTest.testBudget`,
    /// 10 s, anchored at `IClock` now), then drives the FSM from the observed
    /// press edges, the per-button deadline ticks, and the link/panel-presence
    /// guards. Completes with the terminal `ButtonPressTestState`
    /// (`Completed results` or `Interrupted(reason, partial)`). Throws
    /// `InvalidOperationException` if a run is already in flight (modal
    /// contract). Cancellation surfaces as `OperationCanceledException`.
    abstract member RunAsync:
        selected: PanelUuid * schema: ButtonSchema * cancellationToken: CancellationToken ->
            Task<ButtonPressTestState>

    /// FR-009 Retry: re-arm the current button with a fresh countdown (a
    /// `Missed` button returns to `Pending`; a still-`Pending` button's
    /// deadline is reset). A no-op when no run is in flight or the run is
    /// terminal. Technician-driven — there is no auto-retry.
    abstract member Retry: unit -> unit

    /// FR-009 Skip: record the current button `Skipped` (never `Pass`) and
    /// advance the prompt. A no-op when no run is in flight or the run is
    /// terminal.
    abstract member Skip: unit -> unit

    /// FR-003 Re-run: restart the last run's panel + schema from a cleared
    /// result grid and a freshly-armed first deadline. Throws
    /// `InvalidOperationException` if a run is already in flight or no run has
    /// been started yet. Completes like `RunAsync`.
    abstract member RerunAsync: cancellationToken: CancellationToken -> Task<ButtonPressTestState>

    /// Hot observable of FSM-state transitions (state + result grid).
    /// Subscribers added after a transition do NOT replay it — subscribe at
    /// composition time. Emits every non-absorbed transition, terminal states
    /// included.
    abstract member StateChanged: IObservable<ButtonPressTestState>

    /// Current FSM state at the moment of read, consistent with the latest
    /// `StateChanged` emission. `Idle` before the first run.
    abstract member CurrentState: ButtonPressTestState
