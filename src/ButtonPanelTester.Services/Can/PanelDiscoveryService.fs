namespace Stem.ButtonPanelTester.Services.Can

open System
open System.Threading
open Microsoft.Extensions.Logging
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
/// The constructor subscribes to `IWhoIAmObserver.WhoIAmObserved` —
/// the reassembly adapter (spec-003 R2, `WhoIAmReassemblyObserver`)
/// owns reassembly of the segmented WHO_I_AM transport, the command
/// filter, and the parse, so this service receives an already-decoded
/// `WhoIAmFrame`. On each observation, while `ICanLinkService.CurrentState`
/// is `Connected`, it coalesces by UUID into the held `PanelsOnBus` and
/// publishes the new snapshot. An observation arriving while the link is
/// not `Connected` is a silent drop (FR-007).
///
/// B2 adds the 1 s prune timer (FR-005): each tick drops rows older than
/// the 15 s TTL and republishes only when the row count changed. The service
/// owns `IDisposable` (stops + disposes the timer and detaches the WHO_I_AM
/// and link-state subscriptions). B3 adds the FR-008 link-loss clear: on
/// leaving `Connected` the held map is cleared and an empty snapshot published
/// immediately (independent of the prune TTL). The discovery pipeline is complete.
type PanelDiscoveryService(observer: IWhoIAmObserver, link: ICanLinkService, clock: IClock, logger: ILogger<PanelDiscoveryService>) =

    let panelsSubject = DiscoveryObservable.SubjectFanOut<PanelsOnBus>()
    let panelsLock = obj ()
    let mutable panelsOnBus: PanelsOnBus = PanelsOnBus.empty

    /// Render a `PanelUuid` as the canonical hex triple for log lines (the same
    /// "%08X-%08X-%08X" shape the GUI's `uuidText` uses). Written inline because
    /// the Services layer must not take a dependency on the GUI renderer.
    let uuidText (PanelUuid(u0, u1, u2)) = sprintf "%08X-%08X-%08X" u0 u1 u2

    /// Short variant label for the new-panel log line, derived from the decoded
    /// `VariantIdentity` (raw byte preserved for the `Unknown` case).
    let variantText (machineType: MachineTypeByte) =
        match VariantDecoder.decode machineType with
        | Marketing EdenXp -> "EdenXp"
        | Marketing OptimusXp -> "OptimusXp"
        | Marketing R3LXp -> "R3LXp"
        | Marketing EdenBs8 -> "EdenBs8"
        | Virgin -> "Virgin"
        | Unknown raw -> sprintf "Unknown(0x%02X)" raw

    /// WHO_I_AM ingest: the reassembly adapter (spec-003 R2) already owns
    /// reassembly, the command filter, and the parse, so this handler just
    /// coalesces the decoded frame by UUID into the held map while the link
    /// is `Connected` and publishes the new snapshot. An observation arriving
    /// while the link is not `Connected` is a silent drop (FR-007). The map
    /// mutation happens under `panelsLock`; the publish fires OUTSIDE the lock
    /// so a slow subscriber can never stall an ingest thread.
    let onWhoIAm (f: WhoIAmFrame) =
        match link.CurrentState with
        | Connected _ ->
            // "Was this UUID already present" is computed UNDER the lock (before
            // the observe coalesces it in) so the Information-vs-Debug decision is
            // race-free; the log call itself fires OUTSIDE the lock with the publish.
            let isNew, updated =
                lock panelsLock (fun () ->
                    let wasPresent = Map.containsKey f.Uuid panelsOnBus
                    panelsOnBus <- PanelsOnBus.observe (clock.UtcNow()) f panelsOnBus
                    (not wasPresent, panelsOnBus))

            panelsSubject.OnNext updated // publish OUTSIDE the lock

            // A genuinely new panel is an Information-level domain event; a
            // re-broadcast of a known UUID is routine and logs at Debug so it
            // does not spam the operator log on every ~4 s WHO_I_AM cycle.
            if isNew then
                logger.LogInformation(
                    "Panel {Uuid} ({Variant}) appeared on the bus",
                    uuidText f.Uuid,
                    variantText f.MachineType)
            else
                logger.LogDebug("Panel {Uuid} re-observed on the bus", uuidText f.Uuid)
        | _ -> ()

    /// One prune pass: drop rows older than the 15 s TTL (FR-005) as of `clock.UtcNow()`,
    /// then publish the new snapshot ONLY when the row count changed (idle-render
    /// suppression, backed by `prune_idempotent`). Map mutates under `panelsLock`; the
    /// publish fires OUTSIDE it (same discipline as `onFrame`).
    let pruneOnce () =
        let changed =
            lock panelsLock (fun () ->
                let priorCount = Map.count panelsOnBus
                let pruned = Pruning.prune (TimeSpan.FromSeconds 15.0) (clock.UtcNow()) panelsOnBus
                if Map.count pruned <> priorCount then
                    panelsOnBus <- pruned
                    Some(priorCount - Map.count pruned, pruned)
                else
                    None)

        changed
        |> Option.iter (fun (removed, snapshot) ->
            panelsSubject.OnNext snapshot
            logger.LogDebug("Pruned {Count} panel(s) after {TtlSeconds}s silence", removed, 15))

    /// FR-008 link-loss clear: when the link leaves `Connected` (any non-`Connected`
    /// emission), drop every row and publish the empty snapshot immediately — so the
    /// list empties on disconnect rather than after the 15 s prune TTL (SC-004).
    /// Publish-on-change: a clear over an already-empty map stays silent. Map mutates
    /// under `panelsLock`; the publish fires OUTSIDE it (same discipline as `onFrame`).
    let onLinkState (state: CanLinkState) =
        match state with
        | Connected _ -> ()
        | _ ->
            let cleared =
                lock panelsLock (fun () ->
                    if Map.isEmpty panelsOnBus then
                        None
                    else
                        let dropped = Map.count panelsOnBus
                        panelsOnBus <- PanelsOnBus.clear panelsOnBus
                        Some(dropped, panelsOnBus))

            cleared
            |> Option.iter (fun (dropped, snapshot) ->
                panelsSubject.OnNext snapshot
                logger.LogDebug("Panels-on-bus cleared: link left Connected ({Count} row(s) dropped)", dropped))

    /// Held so the WHO_I_AM subscription outlives the constructor (mirrors
    /// `CanLinkService`'s `_linkSubscription`); detached in `Dispose`
    /// alongside the prune timer and the link-state subscription.
    let _whoIAmSubscription: IDisposable =
        observer.WhoIAmObserved |> Observable.subscribe onWhoIAm

    /// 1 s prune timer (research R4). Created + started in the ctor; each tick runs
    /// `pruneOnce`. A tick over the empty map is a no-op, so it self-quiesces when no
    /// panels are present. Stopped + disposed in `Dispose`.
    let pruneTimer =
        new Timer(
            TimerCallback(fun _ -> pruneOnce ()),
            null,
            TimeSpan.FromSeconds 1.0,
            TimeSpan.FromSeconds 1.0)

    /// Held so the link-state subscription outlives the ctor; detached in `Dispose`
    /// alongside the WHO_I_AM subscription and the prune timer. Drives the FR-008 clear
    /// on every `LinkStateChanged` emission (see `onLinkState`).
    let _linkSubscription: IDisposable =
        link.LinkStateChanged |> Observable.subscribe onLinkState

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
            _whoIAmSubscription.Dispose()
            _linkSubscription.Dispose()
