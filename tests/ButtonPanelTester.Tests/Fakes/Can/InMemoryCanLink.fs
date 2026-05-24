namespace Stem.ButtonPanelTester.Tests.Fakes.Can

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Can

/// Single subscription handle used by the hand-rolled `IObservable`
/// subject below. Returned by `IObservable.Subscribe` so callers can
/// dispose their subscription without leaking the observer reference.
type private Subscription<'T>(remove: unit -> unit) =
    interface IDisposable with
        member _.Dispose() = remove ()

/// Hand-rolled hot `IObservable<'T>` subject backed by a
/// `ConcurrentBag` of observers per
/// `specs/002-can-link-and-panel-discovery/research.md` R4. Used by
/// both fakes in this file so the tests do not need to take a
/// dependency on `System.Reactive`. Hot — observers added after a
/// fan-out do NOT replay it (matches the production `PcanCanLink`
/// contract).
type private SubjectFanOut<'T>() =
    let observers = ConcurrentBag<IObserver<'T>>()

    member _.OnNext(value: 'T) =
        for observer in observers do
            observer.OnNext value

    interface IObservable<'T> with
        member _.Subscribe(observer: IObserver<'T>) =
            observers.Add observer
            new Subscription<'T>(fun () -> ()) :> IDisposable

/// Test adapter for `ICanLink` per
/// `specs/002-can-link-and-panel-discovery/contracts/can-link-port.md`
/// §Adapter contract (virtual). Driven by a scripted sequence of
/// `(CanLinkState, TimeSpan)` events: each `OpenAsync` /
/// `CloseAsync` / `ReconnectAsync` call advances the script by one
/// step, waits the step's `TimeSpan`, then publishes the step's
/// `CanLinkState` through `LinkStateChanged` and updates
/// `CurrentState`.
///
/// Used by the property + integration test surface (T026, T040,
/// T041, T051–T053, T058, T059) to drive `CanLinkService` and
/// downstream GUI views through deterministic state sequences
/// without touching real PEAK hardware.
type InMemoryCanLink(script: seq<CanLinkState * TimeSpan>) =
    let steps = Queue<CanLinkState * TimeSpan>(script)
    let subject = SubjectFanOut<CanLinkState>()
    let mutable currentState: CanLinkState = Initializing

    let advanceOne (cancellationToken: CancellationToken) : Task =
        task {
            if steps.Count > 0 then
                let state, delay = steps.Dequeue()

                if delay > TimeSpan.Zero then
                    do! Task.Delay(delay, cancellationToken)

                currentState <- state
                subject.OnNext state
        }

    interface ICanLink with
        member _.OpenAsync(_baudrateBps: int, cancellationToken: CancellationToken) =
            advanceOne cancellationToken

        member _.CloseAsync(cancellationToken: CancellationToken) = advanceOne cancellationToken

        member _.ReconnectAsync(cancellationToken: CancellationToken) = advanceOne cancellationToken

        member _.LinkStateChanged = subject :> IObservable<CanLinkState>
        member _.CurrentState = currentState
