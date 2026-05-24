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

/// Production adapter for `ICanLinkService`. PR-C lifecycle slice
/// (T036): wires through the `ICanLink` port's lifecycle calls and
/// fans the link's `LinkStateChanged` events out to the service-level
/// subject the GUI subscribes to.
///
/// The `PanelsOnBus` / `PanelsOnBusChanged` surface is stubbed for
/// this slice: an empty map and a never-firing observable. The
/// observation pipeline + WHO_I_AM ingest land in PR-D (T046–T047)
/// once `ICanFrameStream` is wired up; until then the dictionary
/// row + CAN status row are observable on their own.
///
/// The Recoverable→Fatal escalation logic from `research.md` R8
/// lands in T041 (commit 4 of PR-C). This slice forwards every
/// link state verbatim — including `Error _` — so the escalation
/// commit can layer the per-cause counter on top without re-routing
/// the lifecycle wiring.
///
/// Constructor parameters:
///   - `link`   — `ICanLink` port; `PcanCanLink` in production
///                (T035), `InMemoryCanLink` in tests.
///   - `clock`  — `IClock` port; not yet consulted by this slice
///                (the link timestamps its own state transitions).
///                Carried so T041 can stamp the escalated
///                `Error.Fatal _` state without a constructor
///                signature change.
///   - `logger` — `ILogger<CanLinkService>`. Lifecycle events log
///                at `Information`; the escalation slice will add
///                a `Warning`-level line for each Recoverable→Fatal
///                upgrade.
type CanLinkService(link: ICanLink, _clock: IClock, logger: ILogger<CanLinkService>) =

    let stateSubject = SubjectFanOut<CanLinkState>()
    let panelsSubject = SubjectFanOut<PanelsOnBus>()
    let mutable currentState: CanLinkState = Initializing

    /// `quickstart.md` pins spec-002 to 250 kbps. Encoded here so the
    /// composition root does not need to thread the bitrate through.
    let baudrateBps = 250_000

    /// Forward every link-side transition through the service's own
    /// subject. The subscription's lifetime is the lifetime of this
    /// instance — the hand-rolled subject above never invokes the
    /// `remove` callback so disposing it is a no-op, but keeping the
    /// reference around documents the intent and survives any future
    /// refinement of the subject contract.
    let _linkSubscription : IDisposable =
        link.LinkStateChanged
        |> Observable.subscribe (fun state ->
            currentState <- state
            stateSubject.OnNext state)

    interface ICanLinkService with

        member _.CurrentState = currentState

        member _.PanelsOnBus = PanelsOnBus.empty

        member _.LinkStateChanged = stateSubject :> IObservable<CanLinkState>

        member _.PanelsOnBusChanged = panelsSubject :> IObservable<PanelsOnBus>

        member _.InitializeAsync(cancellationToken: CancellationToken) =
            logger.LogInformation(
                "CanLinkService.InitializeAsync at {BaudrateBps} bps", baudrateBps
            )

            link.OpenAsync(baudrateBps, cancellationToken)

        member _.ReconnectAsync(cancellationToken: CancellationToken) =
            logger.LogInformation("CanLinkService.ReconnectAsync requested by user")
            link.ReconnectAsync(cancellationToken)
