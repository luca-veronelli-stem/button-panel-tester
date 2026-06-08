namespace Stem.ButtonPanelTester.Services.Can

open System
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary // IClock

/// Hand-rolled hot-observable plumbing for the discovery feed, grouped
/// under a private module so the `Subscription` / `SubjectFanOut` names
/// do not collide with `CanLinkService`'s identically-shaped subject in
/// the same namespace. Mirrors that subject contract
/// (`specs/003-panel-discovery/research.md` R5) so the discovery feed
/// exposes the same observable semantics without taking a
/// `System.Reactive` dependency.
module private DiscoveryObservable =

    /// Single subscription handle returned by `SubjectFanOut` below.
    type Subscription(remove: unit -> unit) =
        interface IDisposable with
            member _.Dispose() = remove ()

    /// Hot `IObservable<'T>` subject backed by an immutable observer
    /// list under a `gate`. Hot — observers added after a fan-out do
    /// NOT replay it. Unlike the prior `ConcurrentBag` stub, `Subscribe`'s
    /// `Dispose` truly detaches the observer (research R5 — the bag could
    /// not remove, so disposed subscribers leaked).
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

/// Production adapter for `IPanelDiscoveryService`. Splits the
/// panels-on-bus discovery surface out of `CanLinkService` (#197) so
/// spec-003 grows the discovery pipeline inside an independent spec
/// without touching the CAN lifecycle service.
///
/// The constructor subscribes to `ICanFrameStream.RawFramesReceived`
/// and runs the live WHO_I_AM ingest pipeline: act only on a broadcast
/// frame of the right size while `ICanLinkService.CurrentState` is
/// `Connected`; parse; coalesce by UUID into the held `PanelsOnBus`;
/// publish the new snapshot. Every other frame, a parse failure, or a
/// non-Connected link is a silent drop (FR-007).
///
/// This slice (B1) wires ingest only. The 1 s prune timer (FR-005,
/// B2/T013), the link-loss clear (FR-008, B3/T015), and the full
/// `IDisposable` teardown arrive in later slices — the frame
/// subscription is held in a `let` binding for now (mirrors
/// `CanLinkService`'s `_linkSubscription`).
type PanelDiscoveryService(frameStream: ICanFrameStream, link: ICanLinkService, clock: IClock) =

    let panelsSubject = DiscoveryObservable.SubjectFanOut<PanelsOnBus>()
    let panelsLock = obj ()
    let mutable panelsOnBus: PanelsOnBus = PanelsOnBus.empty

    /// WHO_I_AM ingest: act only on a broadcast frame of the right size
    /// while the link is `Connected`; parse; coalesce by UUID into the
    /// held map; publish the new snapshot. Every other frame, a parse
    /// failure, or a non-Connected link is a silent drop (FR-007). The
    /// map mutation happens under `panelsLock`; the publish fires OUTSIDE
    /// the lock so a slow subscriber can never stall an ingest thread.
    let onFrame (frame: RawCanFrame) =
        match link.CurrentState with
        | Connected _ when frame.CanId = 0x1FFFFFFFu && frame.Payload.Length = 15 ->
            match WhoIAmFrame.parse frame.Payload with
            | Some f ->
                let updated =
                    lock panelsLock (fun () ->
                        panelsOnBus <- PanelsOnBus.observe (clock.UtcNow()) f panelsOnBus
                        panelsOnBus)

                panelsSubject.OnNext updated // publish OUTSIDE the lock
            | None -> ()
        | _ -> ()

    /// Held so the frame subscription outlives the constructor (mirrors
    /// `CanLinkService`'s `_linkSubscription`). The full `IDisposable`
    /// teardown — and the prune timer (B2/T013) + link-loss clear
    /// (B3/T015) — arrive in later slices; this slice wires ingest only.
    let _frameSubscription: IDisposable =
        frameStream.RawFramesReceived |> Observable.subscribe onFrame

    interface IPanelDiscoveryService with

        member _.PanelsOnBus = lock panelsLock (fun () -> panelsOnBus)

        member _.PanelsOnBusChanged = panelsSubject :> IObservable<PanelsOnBus>
