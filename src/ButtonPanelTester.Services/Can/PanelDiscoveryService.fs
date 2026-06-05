namespace Stem.ButtonPanelTester.Services.Can

open System
open System.Collections.Concurrent
open Stem.ButtonPanelTester.Core.Can

/// Hand-rolled hot-observable plumbing for the discovery feed, grouped
/// under a private module so the `Subscription` / `SubjectFanOut` names
/// do not collide with `CanLinkService`'s identically-shaped subject in
/// the same namespace. Mirrors that subject contract
/// (`specs/002-can-link-lifecycle/research.md` R4) so the discovery feed
/// exposes the same observable semantics without taking a
/// `System.Reactive` dependency.
module private DiscoveryObservable =

    /// Single subscription handle returned by `SubjectFanOut` below.
    type Subscription(remove: unit -> unit) =
        interface IDisposable with
            member _.Dispose() = remove ()

    /// Hot `IObservable<'T>` subject backed by a `ConcurrentBag` of
    /// observers. Hot — observers added after a fan-out do NOT replay it.
    type SubjectFanOut<'T>() =
        let observers = ConcurrentBag<IObserver<'T>>()

        member _.OnNext(value: 'T) =
            for observer in observers do
                observer.OnNext value

        interface IObservable<'T> with
            member _.Subscribe(observer: IObserver<'T>) =
                observers.Add observer
                new Subscription(fun () -> ()) :> IDisposable

/// Production adapter for `IPanelDiscoveryService`. Splits the
/// panels-on-bus discovery surface out of `CanLinkService` (#197) so
/// spec-003 can grow the discovery pipeline (WHO_I_AM ingest, parse,
/// observe, prune, FR-015' link-loss clear) inside an independent spec
/// without touching the CAN lifecycle service.
///
/// Today this is the stub surface moved verbatim from `CanLinkService`:
/// `PanelsOnBus` returns `PanelsOnBus.empty` and `PanelsOnBusChanged`
/// is a never-firing observable. No pipeline is wired — that is
/// spec-003 feature work, and an explicit non-goal of #197.
///
/// The seam is one-directional: nothing in `ICanLinkService` /
/// `CanLinkService` references this service. The forward discovery →
/// lifecycle dependencies (link-state subscription for the FR-015'
/// clear, `ICanFrameStream` for ingest) arrive with the spec-003
/// pipeline; the stub needs none, so the constructor is parameterless
/// today.
type PanelDiscoveryService() =

    let panelsSubject = DiscoveryObservable.SubjectFanOut<PanelsOnBus>()

    interface IPanelDiscoveryService with

        member _.PanelsOnBus = PanelsOnBus.empty

        member _.PanelsOnBusChanged = panelsSubject :> IObservable<PanelsOnBus>
