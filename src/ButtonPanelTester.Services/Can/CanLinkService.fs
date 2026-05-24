namespace Stem.ButtonPanelTester.Services.Can

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary

/// Single subscription handle returned by the hand-rolled
/// `IObservable` subject below. Matches the contract of
/// `Tests.Fakes.Can.InMemoryCanLink`'s subject so production and
/// virtual adapters expose identical observable semantics
/// (`specs/002-can-link-and-panel-discovery/research.md` R4).
type private Subscription(remove: unit -> unit) =
    interface IDisposable with
        member _.Dispose() = remove ()

/// Hand-rolled hot `IObservable<'T>` subject backed by a
/// `ConcurrentBag` of observers, per `research.md` R4. Used for
/// `LinkStateChanged` and `PanelsOnBusChanged` so the service does
/// not take a `System.Reactive` dependency. Hot — observers added
/// after a fan-out do NOT replay it.
type private SubjectFanOut<'T>() =
    let observers = ConcurrentBag<IObserver<'T>>()

    member _.OnNext(value: 'T) =
        for observer in observers do
            observer.OnNext value

    interface IObservable<'T> with
        member _.Subscribe(observer: IObserver<'T>) =
            observers.Add observer
            new Subscription(fun () -> ()) :> IDisposable

/// Production adapter for `ICanLinkService`. Wires through the
/// `ICanLink` port's lifecycle calls, fans the link's
/// `LinkStateChanged` events out to the service-level subject the
/// GUI subscribes to, and applies the per-cause Recoverable→Fatal
/// escalation logic from
/// `specs/002-can-link-and-panel-discovery/research.md` R8.
///
/// **Escalation rule (R8)** — the first observation of an
/// unexpected PEAK status surfaces as `Error.Recoverable`. If the
/// SAME status string is observed again after a `ReconnectAsync`
/// call, the second observation is upgraded to `Error.Fatal` with a
/// "<cause> persists across reconnect — file bug" detail. Any
/// successful `Connected` clears the tracker so a later observation
/// of the same status starts the cycle from Recoverable again. The
/// tracker lives in the service (not the port) because the
/// classification is temporal across multiple lifecycle attempts —
/// state the port itself doesn't carry.
///
/// The `PanelsOnBus` / `PanelsOnBusChanged` surface is stubbed: an
/// empty map and a never-firing observable. The observation pipeline
/// + WHO_I_AM ingest land in PR-D (T046–T047) once `ICanFrameStream`
/// is wired up; until then the dictionary row + CAN status row are
/// observable on their own.
///
/// Constructor parameters:
///   - `link`   — `ICanLink` port; `PcanCanLink` in production
///                (T035), `InMemoryCanLink` in tests.
///   - `clock`  — `IClock` port; carried so future slices can stamp
///                service-synthesised transitions without a
///                constructor signature change. Today the escalated
///                `Fatal` state reuses the originating Recoverable's
///                timestamp so the visible "since" field tracks the
///                ROOT cause, not the moment the escalation rule
///                fired.
///   - `logger` — `ILogger<CanLinkService>`. Lifecycle events log at
///                `Information`; each Recoverable→Fatal escalation
///                logs at `Warning`.
type CanLinkService(link: ICanLink, _clock: IClock, logger: ILogger<CanLinkService>) =

    let stateSubject = SubjectFanOut<CanLinkState>()
    let panelsSubject = SubjectFanOut<PanelsOnBus>()

    /// `quickstart.md` pins spec-002 to 250 kbps. Encoded here so the
    /// composition root does not need to thread the bitrate through.
    let baudrateBps = 250_000

    /// Coordinates the escalation tracker + `currentState` view. The
    /// `LinkStateChanged` handler may run on the link's emission
    /// thread (PcanCanLink hops through the vendored PCANManager
    /// monitor task); `ReconnectAsync` is called from the UI thread.
    /// Both paths read or mutate the tracker, so a short critical
    /// section keeps the state coherent. The lock is never held
    /// across an `await`.
    let stateLock = obj ()

    /// Most-recent Recoverable cause string observed. Cleared on any
    /// successful `Connected` per R8 ("counter resets to zero on any
    /// successful Open").
    let mutable lastRecoverableCause: string option = None

    /// `true` once `ReconnectAsync` has been invoked while a
    /// Recoverable cause was being tracked. Re-armed to `false` on
    /// each new Recoverable observation so the escalation requires
    /// the SAME cause to recur AFTER an explicit reconnect attempt
    /// — not just two-in-a-row of the same status without user
    /// intervention.
    let mutable reconnectSinceLastRecoverable = false

    let mutable currentState: CanLinkState = Initializing

    /// Apply the R8 escalation rule under `stateLock` and return the
    /// effective state the service should publish. Centralises the
    /// tracker mutation so the subscriber below is a thin emit.
    let translate (rawState: CanLinkState) : CanLinkState =
        lock stateLock (fun () ->
            let effective =
                match rawState with
                | Error(Recoverable cause, since) ->
                    match lastRecoverableCause with
                    | Some prevCause when
                        prevCause = cause && reconnectSinceLastRecoverable
                        ->
                        let detail =
                            sprintf "%s persists across reconnect — file bug" cause

                        Error(Fatal detail, since)
                    | _ ->
                        lastRecoverableCause <- Some cause
                        reconnectSinceLastRecoverable <- false
                        rawState
                | Connected _ ->
                    lastRecoverableCause <- None
                    reconnectSinceLastRecoverable <- false
                    rawState
                | _ -> rawState

            currentState <- effective
            effective)

    /// Forward every link-side transition through the service's own
    /// subject after running it through the escalation translator.
    /// Kept as a `let`-bound subscription so the reference outlives
    /// the constructor — the hand-rolled subject above never invokes
    /// the `remove` callback so disposing it is a no-op, but the
    /// binding documents intent.
    let _linkSubscription: IDisposable =
        link.LinkStateChanged
        |> Observable.subscribe (fun rawState ->
            let effective = translate rawState

            match effective with
            | Error(Fatal detail, _) ->
                logger.LogWarning(
                    "CanLinkService: PEAK status escalated to Fatal — {Detail}",
                    detail
                )
            | _ -> ()

            stateSubject.OnNext effective)

    interface ICanLinkService with

        member _.CurrentState =
            lock stateLock (fun () -> currentState)

        member _.PanelsOnBus = PanelsOnBus.empty

        member _.LinkStateChanged = stateSubject :> IObservable<CanLinkState>

        member _.PanelsOnBusChanged = panelsSubject :> IObservable<PanelsOnBus>

        member _.InitializeAsync(cancellationToken: CancellationToken) =
            logger.LogInformation(
                "CanLinkService.InitializeAsync at {BaudrateBps} bps", baudrateBps
            )

            link.OpenAsync(baudrateBps, cancellationToken)

        member _.ReconnectAsync(cancellationToken: CancellationToken) =
            // Arm the escalation tracker BEFORE the link emit so the
            // resulting Recoverable observation (if any) is matched
            // against `reconnectSinceLastRecoverable = true`. Only set
            // the flag when we have a cause to escalate from —
            // otherwise an unconditional Reconnect (e.g., user-driven
            // recovery from Disconnected, no prior Recoverable) would
            // leave the tracker armed and falsely escalate the first
            // ever Recoverable.
            lock stateLock (fun () ->
                if lastRecoverableCause.IsSome then
                    reconnectSinceLastRecoverable <- true)

            logger.LogInformation("CanLinkService.ReconnectAsync requested by user")
            link.ReconnectAsync(cancellationToken)
