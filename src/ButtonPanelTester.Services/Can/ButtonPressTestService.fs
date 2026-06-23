namespace Stem.ButtonPanelTester.Services.Can

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary // IClock

/// Hand-rolled hot-observable plumbing for the button-press-test state feed,
/// grouped under a private module so the `Subscription` / `SubjectFanOut`
/// names do not collide with `BaptismService`'s / `CanLinkService`'s
/// identically-shaped subjects in the same namespace. Mirrors that subject
/// contract (`specs/003-panel-discovery/research.md` R5) so the feed exposes
/// the same hot observable semantics without a `System.Reactive` dependency.
module private ButtonPressTestObservable =

    /// Single subscription handle returned by `SubjectFanOut` below.
    type Subscription(remove: unit -> unit) =
        interface IDisposable with
            member _.Dispose() = remove ()

    /// Hot `IObservable<'T>` subject backed by an immutable observer list
    /// under a `gate`. Hot — observers added after a fan-out do NOT replay
    /// it. `Subscribe`'s `Dispose` truly detaches the observer.
    type SubjectFanOut<'T>() =
        let gate = obj ()
        let mutable observers: IObserver<'T> list = []

        member _.OnNext(value: 'T) =
            let snapshot = lock gate (fun () -> observers) // snapshot under lock

            for observer in snapshot do
                observer.OnNext value // fan out OUTSIDE the lock

        interface IObservable<'T> with
            member _.Subscribe(observer: IObserver<'T>) =
                lock gate (fun () -> observers <- observer :: observers)

                new Subscription(fun () ->
                    lock gate (fun () ->
                        observers <-
                            observers
                            |> List.filter (fun o -> not (obj.ReferenceEquals(o, observer)))))
                :> IDisposable

/// Production adapter for `IButtonPressTestService` (spec-005 Phase E, R8).
/// Drives the pure button-press-test FSM (`ButtonPressTest.step`, Core) over
/// the consumed RX observables — `IButtonStateObserver` (button-state frames),
/// `IPanelDiscoveryService` (presence), `ICanLinkService` (connectivity) — and
/// an `IClock`-armed per-button deadline, per `data-model.md` §4. RX-only: no
/// transmitter, the technician presses the physical buttons. Modal
/// single-attempt, no auto-retry (Retry/Skip/Re-run are technician-driven).
///
/// The mutable run state (`state`, `schema`, `selected`, `running`, `prior`,
/// `tcs`) lives under a private lock that is NEVER held across an await: the
/// transition core computes `ButtonPressTest.step` and assigns `state` under
/// the lock, then — outside the lock — re-arms the deadline (the pure step
/// carries the deadline forward unchanged; the SERVICE re-arms on
/// `AdvancePrompt` and on `Retry`), publishes the new state, and on a terminal
/// transition completes the run's `TaskCompletionSource`. A step out of an
/// already-terminal state is idempotent (no re-publish, no double-complete) —
/// the never-flip carrier (Lean `terminal_absorbs`). The four ctor
/// subscriptions and the 250 ms deadline timer fire as no-ops while no run is
/// active.
///
/// `pressEdges` (R2) sits between the observer and the FSM: the baseline frame
/// is seeded from the FIRST frame of each run; each later frame's active-masked
/// `1 → 0` transitions become `PressEdge` events. The deadline is armed by the
/// service (`clock.UtcNow() + ButtonPressTest.testBudget`) and stepped
/// deterministically in tests through `RunDeadlineTick` against a `FrozenClock`
/// (the `BaptismService` precedent — no wall-clock sleeps).
type ButtonPressTestService
    (
        buttons: IButtonStateObserver,
        discovery: IPanelDiscoveryService,
        link: ICanLinkService,
        clock: IClock,
        logger: ILogger<ButtonPressTestService>
    ) =

    let stateSubject = ButtonPressTestObservable.SubjectFanOut<ButtonPressTestState>()

    /// Guards every read/write of the mutable run state. Held only for the
    /// `ButtonPressTest.step` computation + the `state` assignment; the
    /// deadline re-arm, the publish, and the `tcs` completion all happen
    /// OUTSIDE it (stem-async-discipline: the lock is never held across an
    /// await or a subscriber callback).
    let stateLock = obj ()

    let mutable state: ButtonPressTestState = ButtonPressTestState.Idle
    let mutable schema: ButtonSchema option = None
    let mutable selected: PanelUuid option = None
    let mutable running = false

    /// Press-edge baseline (R2): the previous observed frame's bitmap. `None`
    /// at the start of each run (re-seeded from the first frame of the run);
    /// no absolute byte is ever read as press-state — only `1 → 0` transitions
    /// between consecutive frames score.
    let mutable prior: KeyStateBitmap option = None

    let mutable tcs: TaskCompletionSource<ButtonPressTestState> = Unchecked.defaultof<_>

    /// `true` for the two terminal states; `Idle`/`Prompting` are non-terminal.
    let isTerminal (s: ButtonPressTestState) : bool =
        match s with
        | Completed _
        | Interrupted _ -> true
        | ButtonPressTestState.Idle
        | Prompting _ -> false

    /// Re-arm the deadline the pure `step` carried forward unchanged (the Phase
    /// D contract): the SERVICE owns the clock, so on an `AdvancePrompt` action
    /// (the next button is armed) and on a `Retry` event (the current button is
    /// re-armed) the surviving `Prompting` state's deadline is replaced with
    /// `clock.UtcNow() + ButtonPressTest.testBudget`. Every other transition
    /// keeps the state `step` produced.
    let rearm (event: TestEvent) (action: TestAction) (next: ButtonPressTestState) : ButtonPressTestState =
        match next with
        | Prompting(index, _, results) ->
            match event, action with
            | _, AdvancePrompt _ -> Prompting(index, clock.UtcNow() + ButtonPressTest.testBudget, results)
            | TestEvent.Retry, _ -> Prompting(index, clock.UtcNow() + ButtonPressTest.testBudget, results)
            | _ -> next
        | ButtonPressTestState.Idle
        | Completed _
        | Interrupted _ -> next

    /// Publish the new state OUTSIDE the lock and, on a terminal transition,
    /// complete the run's `TaskCompletionSource`. Terminal idempotence is
    /// enforced by `apply` (a step out of a terminal state is absorbed before
    /// reaching here), so the completion fires exactly once.
    let commit (next: ButtonPressTestState) (attemptTcs: TaskCompletionSource<ButtonPressTestState>) =
        stateSubject.OnNext next // publish OUTSIDE the lock

        if isTerminal next then
            attemptTcs.TrySetResult next |> ignore

    /// Feed one observed event into the FSM. A no-op when no run is active or
    /// the run is already terminal (terminal absorption — no re-publish, no
    /// double-complete). The `ButtonPressTest.step` computation, the deadline
    /// re-arm decision, and the `state` assignment happen UNDER the lock;
    /// everything else (publish, completion) happens OUTSIDE it.
    let apply (event: TestEvent) : unit =
        let work =
            lock stateLock (fun () ->
                match schema with
                | _ when not running -> None
                | None -> None
                | Some sch ->
                    match state with
                    | Completed _
                    | Interrupted _
                    | ButtonPressTestState.Idle -> None // already terminal / inert — absorb silently
                    | Prompting _ as current ->
                        let next0, action = ButtonPressTest.step sch current event
                        let next = rearm event action next0
                        state <- next

                        if isTerminal next then
                            running <- false

                        Some(next, tcs))

        match work with
        | Some(next, attemptTcs) -> commit next attemptTcs
        | None -> ()

    /// Button-state ingest. Converts consecutive observed frames into
    /// `PressEdge` events (R2): the FIRST frame of a run seeds the `prior`
    /// baseline (no edges); each later frame's active-masked `1 → 0`
    /// transitions (`KeyStateBitmap.pressEdges`) feed in as `PressEdge bit`. A
    /// frame observed while no run is active updates the baseline but emits
    /// nothing the FSM acts on (`apply` absorbs it). The edge set is computed
    /// UNDER the lock; each `PressEdge` is applied OUTSIDE it.
    let onFrame (frame: ButtonStateFrame) =
        let edges =
            lock stateLock (fun () ->
                match schema, prior with
                | Some sch, Some priorBitmap ->
                    let e = KeyStateBitmap.pressEdges sch.ActiveMask priorBitmap frame.Bitmap
                    prior <- Some frame.Bitmap
                    e
                | Some _, None ->
                    prior <- Some frame.Bitmap // seed the baseline from the first frame of the run
                    Set.empty
                | None, _ -> Set.empty)

        for bit in edges do
            apply (TestEvent.PressEdge bit)

    /// Held so the button-state subscription outlives the constructor (mirrors
    /// `BaptismService`'s `_whoIAmSubscription`); detached in `Dispose`.
    let _buttonsSubscription: IDisposable =
        buttons.ButtonStateObserved |> Observable.subscribe onFrame

    /// Discovery presence ingest: a snapshot that no longer contains the
    /// selected panel feeds `PanelPresence false` (the FSM halts in
    /// `Interrupted PanelLost`); a snapshot that still contains it self-loops.
    let onPanels (snapshot: PanelsOnBus) =
        match lock stateLock (fun () -> selected) with
        | Some uuid -> apply (TestEvent.PanelPresence(Map.containsKey uuid snapshot))
        | None -> ()

    /// Held so the presence subscription outlives the ctor; detached in
    /// `Dispose`. Feeds discovery snapshots so a pruned-away selected panel
    /// halts the run in `Interrupted PanelLost` (FR-013).
    let _discoverySubscription: IDisposable =
        discovery.PanelsOnBusChanged |> Observable.subscribe onPanels

    /// Link-state ingest: the link leaving `Connected` feeds `LinkChanged false`
    /// (the FSM halts in `Interrupted LinkLost`, FR-013); a `Connected`
    /// transition self-loops.
    let onLink (s: CanLinkState) =
        let connected =
            match s with
            | Connected _ -> true
            | _ -> false

        apply (TestEvent.LinkChanged connected)

    /// Held so the link subscription outlives the ctor; detached in `Dispose`.
    /// Feeds link-state transitions so the link leaving `Connected` halts the
    /// run in `Interrupted LinkLost` (FR-013).
    let _linkSubscription: IDisposable =
        link.LinkStateChanged |> Observable.subscribe onLink

    /// One deadline tick: feed `Tick now` into the FSM so the per-button 10 s
    /// deadline is reached. A tick while no run is active is a no-op. Both the
    /// 250 ms timer and `RunDeadlineTick` run this body.
    let runTick () = apply (TestEvent.Tick(clock.UtcNow()))

    /// 250 ms deadline timer (mirrors `BaptismService`'s deadline timer). Each
    /// tick feeds `Tick now` so the 10 s per-button deadline is reached without
    /// a wall-clock dependency in production. A tick while no run is active is a
    /// no-op. Stopped + disposed in `Dispose`.
    let deadlineTimer =
        new Timer(
            TimerCallback(fun _ -> runTick ()),
            null,
            TimeSpan.FromMilliseconds 250.0,
            TimeSpan.FromMilliseconds 250.0)

    /// Start the run's FSM from `start` against `sch`, arming button 0's
    /// deadline at `clock.UtcNow() + testBudget`. Taken under `stateLock`; the
    /// publish + (possible immediate) completion run OUTSIDE it. A variant with
    /// no active buttons starts already `Completed`.
    let startRun (panel: PanelUuid) (sch: ButtonSchema) : ButtonPressTestState * TaskCompletionSource<ButtonPressTestState> =
        lock stateLock (fun () ->
            if running then
                invalidOp "ButtonPressTestService: a run is already in flight"

            schema <- Some sch
            selected <- Some panel
            prior <- None // re-seed the press-edge baseline from the first frame of this run

            let startState = ButtonPressTest.start sch (clock.UtcNow() + ButtonPressTest.testBudget)
            state <- startState

            let attemptTcs =
                TaskCompletionSource<ButtonPressTestState>(TaskCreationOptions.RunContinuationsAsynchronously)

            tcs <- attemptTcs
            running <- not (isTerminal startState) // an empty schema completes immediately
            (startState, attemptTcs))

    /// Drive a freshly-started run to its `Task`: publish the start state
    /// OUTSIDE the lock, complete immediately if it is already terminal (empty
    /// schema), and wire cancellation to cancel the run's `Task`.
    let beginRun (panel: PanelUuid) (sch: ButtonSchema) (cancellationToken: CancellationToken) : Task<ButtonPressTestState> =
        let startState, attemptTcs = startRun panel sch
        stateSubject.OnNext startState // publish OUTSIDE the lock

        if isTerminal startState then
            attemptTcs.TrySetResult startState |> ignore
        elif cancellationToken.CanBeCanceled then
            // RX-only: cancellation cannot abort a physical press, so it simply
            // resolves the run's Task as cancelled and stops further scoring.
            cancellationToken.Register(fun () ->
                lock stateLock (fun () -> running <- false)
                attemptTcs.TrySetCanceled cancellationToken |> ignore)
            |> ignore

        attemptTcs.Task

    /// Run one deadline tick synchronously on the calling thread — the exact
    /// body the 250 ms timer invokes. Exposed so the integration suites can
    /// step the 10 s per-button deadline deterministically under `FrozenClock`
    /// (the `BaptismService.RunDeadlineTick` precedent — a real `Timer` fires on
    /// wall-clock and cannot be stepped by it).
    member _.RunDeadlineTick() = runTick ()

    /// Current FSM state at the moment of read (the `IButtonPressTestService`
    /// surface, exposed directly so the integration harness can read it off the
    /// concrete type alongside `RunDeadlineTick`).
    member _.CurrentState = lock stateLock (fun () -> state)

    /// Hot observable of FSM-state transitions (the `IButtonPressTestService`
    /// surface, exposed directly for the same reason as `CurrentState`).
    member _.StateChanged = stateSubject :> IObservable<ButtonPressTestState>

    /// Start entrypoint — see `IButtonPressTestService.RunAsync`.
    member _.RunAsync(selectedPanel: PanelUuid, runSchema: ButtonSchema, cancellationToken: CancellationToken) : Task<ButtonPressTestState> =
        beginRun selectedPanel runSchema cancellationToken

    /// Re-run entrypoint — see `IButtonPressTestService.RerunAsync`. Restarts
    /// the last run's panel + schema (cleared grid, fresh first deadline).
    member _.RerunAsync(cancellationToken: CancellationToken) : Task<ButtonPressTestState> =
        let lastRun = lock stateLock (fun () ->
            match selected, schema with
            | Some panel, Some sch -> Some(panel, sch)
            | _ -> None)

        match lastRun with
        | Some(panel, sch) -> beginRun panel sch cancellationToken
        | None -> invalidOp "ButtonPressTestService: no prior run to re-run"

    /// FR-009 Retry — see `IButtonPressTestService.Retry`.
    member _.Retry() = apply TestEvent.Retry

    /// FR-009 Skip — see `IButtonPressTestService.Skip`.
    member _.Skip() = apply TestEvent.Skip

    interface IButtonPressTestService with
        member this.CurrentState = this.CurrentState
        member this.StateChanged = this.StateChanged
        member this.RunAsync(selectedPanel, runSchema, cancellationToken) =
            this.RunAsync(selectedPanel, runSchema, cancellationToken)
        member this.RerunAsync(cancellationToken) = this.RerunAsync(cancellationToken)
        member this.Retry() = this.Retry()
        member this.Skip() = this.Skip()

    interface IDisposable with
        member _.Dispose() =
            deadlineTimer.Dispose()
            _buttonsSubscription.Dispose()
            _discoverySubscription.Dispose()
            _linkSubscription.Dispose()
