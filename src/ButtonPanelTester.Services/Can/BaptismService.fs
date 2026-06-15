namespace Stem.ButtonPanelTester.Services.Can

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary // IClock

/// Hand-rolled hot-observable plumbing for the baptism state feed, grouped
/// under a private module so the `Subscription` / `SubjectFanOut` names do
/// not collide with `DiscoveryObservable`'s / `CanLinkService`'s
/// identically-shaped subjects in the same namespace. Mirrors that subject
/// contract (`specs/003-panel-discovery/research.md` R5) so the feed
/// exposes the same observable semantics without a `System.Reactive`
/// dependency.
module private BaptismObservable =

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

/// Recovery guidance text per baptism outcome, per
/// `specs/004-baptism-workflow` clarification-4 / FR-005. Today only the
/// `WaitTimeout` case carries text (the panel may not have re-announced in
/// time); the per-failure GUI rendering for the other outcomes is Phase E.
/// Kept here, alongside the service, so the GUI render path can name the
/// technician's next step without re-deriving it from the outcome.
module BaptismGuidance =

    /// Recovery hint for a baptism outcome, or `None` when no guidance is
    /// rendered yet. The `WaitTimeout` text names the three clarification-4
    /// elements: the claim may be incomplete; the panel may re-announce
    /// late with the target variant; re-run Baptize (or Reset) to complete.
    let recoveryText (outcome: BaptismOutcome) : string option =
        match outcome with
        | WaitTimeout ->
            Some
                "The claim may be incomplete: the panel did not re-announce the target variant in time. \
                 It may re-announce late with the target variant — re-run Baptize (or Reset) to complete."
        | _ -> None

/// Production adapter for `IBaptismService` (spec-004 C4). Drives the pure
/// baptism FSM (`Baptism.step`, Core) over the consumed observables —
/// `IWhoIAmObserver` (announcements), `IPanelDiscoveryService` (presence),
/// `ICanLinkService` (connectivity) — and the `IMasterSequenceTransmitter`
/// TX port, per `data-model.md` §4. Modal single-attempt, no auto-retry
/// (#216 reset is a later slice).
///
/// The mutable attempt state (`state`, `config`, `running`,
/// `announcedFwType`, `variantByte`, `tcs`) lives under a private lock that
/// is NEVER held across an await: the transition core computes
/// `Baptism.step` and assigns `state` under the lock, then — outside the
/// lock — publishes the new state, performs the action (claim / assign
/// write), and on a terminal transition completes the attempt's
/// `TaskCompletionSource`. A step out of an already-terminal state is
/// idempotent (no re-publish, no double-complete), the never-flip carrier
/// (Lean `terminal_absorbs`). The three ctor subscriptions and the 250 ms
/// deadline timer fire as no-ops while no attempt is active.
type BaptismService
    (
        transmitter: IMasterSequenceTransmitter,
        whoIAm: IWhoIAmObserver,
        discovery: IPanelDiscoveryService,
        link: ICanLinkService,
        clock: IClock,
        logger: ILogger<BaptismService>
    ) =

    let stateSubject = BaptismObservable.SubjectFanOut<BaptismState>()

    /// Guards every read/write of the mutable attempt state. Held only for
    /// the `Baptism.step` computation + the `state` assignment; the publish,
    /// the writes, and the `tcs` completion all happen OUTSIDE it
    /// (stem-async-discipline: the lock is never held across an await).
    let stateLock = obj ()

    let mutable state: BaptismState = Idle
    let mutable config: AttemptConfig option = None
    let mutable running = false
    let mutable announcedFwType: uint16 = 0us
    let mutable variantByte: byte = 0uy
    let mutable tcs: TaskCompletionSource<BaptismOutcome> = Unchecked.defaultof<_>

    // `logger` is held this slice; the structured audit emission is the next
    // slice (C6). Touch it once so the unused binding reads as intentional.
    do logger.LogDebug("BaptismService constructed (idle)")

    /// Extract the terminal outcome of a state, if any.
    let terminalOutcome (s: BaptismState) : BaptismOutcome option =
        match s with
        | Terminal outcome -> Some outcome
        | _ -> None

    /// Fire one TX write WITHOUT awaiting it inline, attaching the
    /// write-completion continuation contract (CHK010): on success →
    /// `WriteCompleted now`; on a non-OCE fault → `WriteFaulted`; on
    /// cancellation/OCE → propagate to the caller via the attempt's `tcs`
    /// so the `OperationCanceledException` surfaces and is NEVER mapped to
    /// `TransmissionFailure`. Forward-declared via a ref cell because the
    /// continuation feeds back into `apply`, which is defined below.
    let applyRef: (BaptismEvent -> unit) ref = ref (fun _ -> ())

    let fireWrite (write: Task) (attemptTcs: TaskCompletionSource<BaptismOutcome>) =
        // `ExecuteSynchronously`: when the write is ALREADY complete (the
        // synchronous in-memory transmitter), the continuation runs inline on
        // the calling thread so the claim write drives the FSM to
        // `AwaitingAnnounce` (and the assign write to `Succeeded`) WITHIN the
        // originating call — the determinism the integration suites rely on.
        // `commit` is always invoked outside `stateLock`, so the inline
        // re-entry into `apply` never re-takes a held lock.
        write.ContinueWith(
            (fun (t: Task) ->
                if t.IsCanceled then
                    attemptTcs.TrySetCanceled() |> ignore
                elif t.IsFaulted then
                    let ex =
                        match t.Exception with
                        | null -> None
                        | agg ->
                            agg.InnerExceptions
                            |> Seq.tryFind (fun e -> e :? OperationCanceledException)

                    match ex with
                    | Some oce -> attemptTcs.TrySetException oce |> ignore
                    | None -> applyRef.Value WriteFaulted
                else
                    applyRef.Value(WriteCompleted(clock.UtcNow()))),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default)
        |> ignore

    /// Dispatch the FSM action OUTSIDE the lock against the captured attempt
    /// config. `SendClaim` echoes the announced fwType (CHK / FR-014);
    /// `SendAssign` carries the computed SP_Address.
    let dispatch (action: BaptismAction) (cfg: AttemptConfig) (attemptTcs: TaskCompletionSource<BaptismOutcome>) (ct: CancellationToken) =
        match action with
        | NoAction -> ()
        | SendClaim ->
            fireWrite (transmitter.SendWhoAreYouAsync(variantByte, announcedFwType, true, ct)) attemptTcs
        | SendAssign ->
            let (PanelUuid _) = cfg.SelectedUuid
            let spAddr = SetAddressFrame.spAddress 0uy variantByte announcedFwType 1uy
            fireWrite (transmitter.SendSetAddressAsync(cfg.SelectedUuid, spAddr, ct)) attemptTcs

    /// The cancellation token of the in-flight attempt (captured at entry).
    let mutable attemptCt: CancellationToken = CancellationToken.None

    /// Apply a `(next, action)` transition that was computed for the current
    /// attempt: assign `state` under the lock, then publish + dispatch +
    /// complete OUTSIDE it. Terminal idempotence is enforced by the caller
    /// (a step out of `Terminal _` produces `(Terminal _, NoAction)` and the
    /// state is unchanged, so we re-detect "already terminal" and skip).
    let commit (next: BaptismState) (action: BaptismAction) (cfg: AttemptConfig) (attemptTcs: TaskCompletionSource<BaptismOutcome>) (ct: CancellationToken) =
        stateSubject.OnNext next // publish OUTSIDE the lock
        dispatch action cfg attemptTcs ct

        match terminalOutcome next with
        | Some outcome -> attemptTcs.TrySetResult outcome |> ignore
        | None -> ()

    /// Feed one observed event into the FSM. A no-op when no attempt is
    /// active or the attempt is already terminal (terminal absorption — no
    /// re-publish, no double-complete). The `Baptism.step` computation and
    /// the `state` assignment happen UNDER the lock; everything else
    /// (publish, write, completion) happens OUTSIDE it.
    let apply (event: BaptismEvent) : unit =
        let work =
            lock stateLock (fun () ->
                match config with
                | _ when not running -> None
                | None -> None
                | Some cfg ->
                    match state with
                    | Terminal _ -> None // already terminal — absorb silently
                    | current ->
                        let next, action = Baptism.step cfg current event
                        state <- next

                        if (match next with Terminal _ -> true | _ -> false) then
                            running <- false

                        Some(next, action, cfg, tcs, attemptCt))

        match work with
        | Some(next, action, cfg, attemptTcs, ct) -> commit next action cfg attemptTcs ct
        | None -> ()

    do applyRef.Value <- apply

    /// Held so the WHO_I_AM subscription outlives the constructor (mirrors
    /// `PanelDiscoveryService`'s `_whoIAmSubscription`); detached in
    /// `Dispose`. Feeds announcements while an attempt is active.
    let _whoIAmSubscription: IDisposable =
        whoIAm.WhoIAmObserved |> Observable.subscribe (fun frame -> apply (AnnouncementHeard frame))

    /// Held so the presence subscription outlives the ctor; detached in
    /// `Dispose`. Feeds discovery snapshots so a pruned-away selected panel
    /// ends the attempt in `PanelDisappeared`.
    let _discoverySubscription: IDisposable =
        discovery.PanelsOnBusChanged |> Observable.subscribe (fun snapshot -> apply (PanelsChanged snapshot))

    /// Held so the link subscription outlives the ctor; detached in
    /// `Dispose`. Feeds link-state transitions so the link leaving
    /// `Connected` ends the attempt in `LinkLost` (CHK015).
    let _linkSubscription: IDisposable =
        link.LinkStateChanged |> Observable.subscribe (fun s -> apply (LinkChanged s))

    /// 250 ms deadline timer (mirrors `PanelDiscoveryService`'s prune
    /// timer). Each tick feeds `Tick now` so the 6 s announce deadline is
    /// reached without a wall-clock dependency in production. A tick while
    /// no attempt is active is a no-op. Stopped + disposed in `Dispose`.
    let deadlineTimer =
        new Timer(
            TimerCallback(fun _ -> apply (Tick(clock.UtcNow()))),
            null,
            TimeSpan.FromMilliseconds 250.0,
            TimeSpan.FromMilliseconds 250.0)

    /// Run one deadline tick synchronously on the calling thread — the exact
    /// body the 250 ms timer invokes. Exposed so `TimeoutE2ETests` can step
    /// the 6 s deadline deterministically under `FrozenClock` (the
    /// `RunPruneTick` precedent — a real `Timer` fires on wall-clock and
    /// cannot be stepped by it).
    member _.RunDeadlineTick() = apply (Tick(clock.UtcNow()))

    /// Current FSM state at the moment of read (the `IBaptismService`
    /// surface, exposed directly so the integration harness can read it off
    /// the concrete type alongside `RunDeadlineTick`).
    member _.CurrentState = lock stateLock (fun () -> state)

    /// Hot observable of FSM-state transitions (the `IBaptismService`
    /// surface, exposed directly for the same reason as `CurrentState`).
    member _.StateChanged = stateSubject :> IObservable<BaptismState>

    /// FR-002 Baptize-button entrypoint — see `IBaptismService.BaptizeAsync`.
    member _.BaptizeAsync(selected: PanelUuid, variant: MarketingVariant, cancellationToken: CancellationToken) : Task<BaptismOutcome> =
        // Entry, under the lock: a second concurrent attempt is a
        // programming bug — throw (CHK013 modal contract).
        let entry =
            lock stateLock (fun () ->
                if running then
                    invalidOp "BaptismService: an attempt is already running"

                // Raw re-check of the entry guards against the CURRENT
                // observables (the Enablement module is #216, not here).
                match link.CurrentState with
                | Connected _ ->
                    let snapshot = discovery.PanelsOnBus

                    match Map.tryFind selected snapshot with
                    | None ->
                        // Selected panel is gone — terminal, transmit nothing.
                        // `Choice2Of2` (not `Result.Error`) because the open
                        // `Core.Can` namespace shadows `Error` with
                        // `CanLinkState.Error`.
                        state <- Terminal PanelDisappeared
                        Choice2Of2 PanelDisappeared
                    | Some obs ->
                        announcedFwType <- obs.FwType
                        variantByte <- BoardVariant.encode variant
                        config <- Some { SelectedUuid = selected; ChosenVariant = variant }
                        state <- Idle
                        running <- true
                        attemptCt <- cancellationToken
                        tcs <- TaskCompletionSource<BaptismOutcome>(TaskCreationOptions.RunContinuationsAsynchronously)
                        Choice1Of2(config.Value, tcs)
                | _ ->
                    // Link not Connected — terminal, transmit nothing.
                    state <- Terminal LinkLost
                    Choice2Of2 LinkLost)

        match entry with
        | Choice2Of2 outcome ->
            // Publish the entry-guard terminal OUTSIDE the lock and return
            // the resolved outcome directly (no attempt was started).
            stateSubject.OnNext(Terminal outcome)
            Task.FromResult outcome
        | Choice1Of2(cfg, attemptTcs) ->
            // Start (outside the lock): Idle → ClaimSent + SendClaim via
            // `Baptism.start`. The claim write fires WITHOUT being awaited
            // inline; its continuation feeds `WriteCompleted` /
            // `WriteFaulted` back in.
            let startState, startAction = Baptism.start
            lock stateLock (fun () -> state <- startState)
            commit startState startAction cfg attemptTcs cancellationToken
            attemptTcs.Task

    interface IBaptismService with
        member this.CurrentState = this.CurrentState
        member this.StateChanged = this.StateChanged
        member this.BaptizeAsync(selected, variant, cancellationToken) =
            this.BaptizeAsync(selected, variant, cancellationToken)

    interface IDisposable with
        member _.Dispose() =
            deadlineTimer.Dispose()
            _whoIAmSubscription.Dispose()
            _discoverySubscription.Dispose()
            _linkSubscription.Dispose()
