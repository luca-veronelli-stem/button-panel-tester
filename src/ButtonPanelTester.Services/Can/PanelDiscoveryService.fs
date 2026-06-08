namespace Stem.ButtonPanelTester.Services.Can

open System
open System.Threading
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
/// B2 (this slice) adds the 1 s prune timer (FR-005): each tick drops rows older
/// than the 15 s TTL and republishes only when the row count changed. The service
/// now owns `IDisposable` (stops + disposes the timer and detaches the frame
/// subscription). The link-loss clear (FR-008, B3/T015) is the remaining pipeline slice.
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

    /// One prune pass: drop rows older than the 15 s TTL (FR-005) as of `clock.UtcNow()`,
    /// then publish the new snapshot ONLY when the row count changed (idle-render
    /// suppression, backed by `prune_idempotent`). Map mutates under `panelsLock`; the
    /// publish fires OUTSIDE it (same discipline as `onFrame`).
    let pruneOnce () =
        let changed =
            lock panelsLock (fun () ->
                let pruned = Pruning.prune (TimeSpan.FromSeconds 15.0) (clock.UtcNow()) panelsOnBus
                if Map.count pruned <> Map.count panelsOnBus then
                    panelsOnBus <- pruned
                    Some pruned
                else
                    None)

        changed |> Option.iter (fun snapshot -> panelsSubject.OnNext snapshot)

    /// Held so the frame subscription outlives the constructor (mirrors
    /// `CanLinkService`'s `_linkSubscription`); detached in `Dispose`
    /// alongside the prune timer. The link-loss clear (FR-008, B3/T015)
    /// is the remaining pipeline slice.
    let _frameSubscription: IDisposable =
        frameStream.RawFramesReceived |> Observable.subscribe onFrame

    /// 1 s prune timer (research R4). Created + started in the ctor; each tick runs
    /// `pruneOnce`. A tick over the empty map is a no-op, so it self-quiesces when no
    /// panels are present. Stopped + disposed in `Dispose`.
    let pruneTimer =
        new Timer(
            TimerCallback(fun _ -> pruneOnce ()),
            null,
            TimeSpan.FromSeconds 1.0,
            TimeSpan.FromSeconds 1.0)

    /// Run one prune pass synchronously on the calling thread — the exact body the 1 s
    /// timer invokes. Exposed so `PruningE2ETests` can step pruning deterministically
    /// under `FrozenClock` (a real `Timer` fires on wall-clock and cannot be stepped by it).
    member _.RunPruneTick() = pruneOnce ()

    interface IPanelDiscoveryService with

        member _.PanelsOnBus = lock panelsLock (fun () -> panelsOnBus)

        member _.PanelsOnBusChanged = panelsSubject :> IObservable<PanelsOnBus>

    interface IDisposable with
        member _.Dispose() =
            pruneTimer.Dispose()
            _frameSubscription.Dispose()
