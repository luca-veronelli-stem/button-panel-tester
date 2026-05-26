namespace Stem.ButtonPanelTester.Infrastructure.Can

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Core.Interfaces
open Core.Models
open Stem.ButtonPanelTester.Core.Can

/// Hand-rolled subject subscription handle. Mirrors the contract of
/// `Tests.Fakes.Can.InMemoryCanLink`'s subject so production and
/// virtual adapters expose identical observable semantics
/// (`specs/002-can-link-and-panel-discovery/research.md` R4).
type private Subscription(remove: unit -> unit) =
    interface IDisposable with
        member _.Dispose() = remove ()

/// Hand-rolled hot `IObservable<'T>` subject. Same shape as the
/// `SubjectFanOut` in `InMemoryCanLink` / `CanLinkService` so
/// `System.Reactive` stays out of the dependency graph
/// (`research.md` R4). Hot — observers added after a fan-out do NOT
/// replay it (lifecycle invariant: subscribe at composition time).
type private SubjectFanOut<'T>() =
    let observers = ConcurrentBag<IObserver<'T>>()

    member _.OnNext(value: 'T) =
        for observer in observers do
            observer.OnNext value

    interface IObservable<'T> with
        member _.Subscribe(observer: IObserver<'T>) =
            observers.Add observer
            new Subscription(fun () -> ()) :> IDisposable

/// Internal record of the underlying `ICommunicationPort`'s
/// construction outcome. The port is built lazily on first
/// `OpenAsync` rather than at PcanCanLink construction time so a
/// PEAK stack failure (notably `DllNotFoundException` when the
/// PCANBasic native DLL is not installed on the host) surfaces as
/// an observable `Error` state on `LinkStateChanged` rather than as
/// a composition-root crash that prevents the app from booting.
///
/// `NotAttempted` is the initial state. `Built` is set once the
/// factory returns a port and the StateChanged subscription is
/// installed. `Failed` is set when the factory threw — the carried
/// `CanLinkState` is the `Error` state to emit on every subsequent
/// `OpenAsync` / `ReconnectAsync` call without retrying the
/// (already-failed) construction.
type private PortBuild =
    | NotAttempted
    | Built of port: ICommunicationPort * handler: EventHandler<ConnectionState>
    | Failed of state: CanLinkState

/// Outcome of `PcanCanLink.ensurePort`. A dedicated DU rather than
/// `Result<ICommunicationPort, CanLinkState>` because the latter
/// collides with the `CanLinkState.Error` case in pattern matches.
type private PortAccess =
    | PortReady of ICommunicationPort
    | PortFailure of state: CanLinkState

/// Production adapter for `ICanLink` per
/// `specs/002-can-link-and-panel-discovery/contracts/can-link-port.md`
/// §Adapter contract. Wraps the vendored `ICommunicationPort` (a
/// `CanPort` over `PCANManager`, both frozen in
/// `Infrastructure.Protocol/`) and translates its
/// `ConnectionState` event stream into the
/// `Core.Can.CanLinkState` taxonomy:
///
///   - `Connected`     → `Connected(adapter, now)` with
///                       identification read via `PcanAdapterIdentity`.
///   - `Disconnected`  → `Disconnected(reason, now)` where `reason` is
///                       derived from the lifecycle context:
///                       `ReconnectPending` if we requested the close,
///                       `MidSessionUnplug` if we'd been connected,
///                       `NoAdapterPresent` otherwise.
///   - `Error`         → `Error(Recoverable detail, now)` — the
///                       Recoverable→Fatal escalation lives in
///                       `CanLinkService` per `research.md` R8.
///   - `Connecting`    → no emission (intermediate; the
///                       contract emits only on terminal states).
///
/// **Lazy port construction.** The vendored `PCANManager`
/// constructor P/Invokes into `pcanbasic.dll` from its body
/// (`PCANBasic.Initialize`). On a host without the PEAK driver
/// installed, that throws `DllNotFoundException` and would tear
/// down the composition root before MainWindow ever paints. To
/// avoid that, `PcanCanLink` takes a port factory (`unit ->
/// ICommunicationPort`) and invokes it on first `OpenAsync`. Any
/// construction exception is captured and re-emitted as an
/// observable `Error` state on `LinkStateChanged`:
///
///   - `DllNotFoundException` → `Error(Fatal "<message>", now)`
///     so the status row's button caption switches to
///     "Reconnect (unlikely to help)" (per FR-002a + the spec's
///     Recoverable/Fatal sub-classification).
///   - any other exception → `Error(Recoverable "<message>", now)`
///     so the user can retry via the Reconnect button.
///
/// Lifecycle invariants from the contract:
///   1. `OpenAsync` is idempotent. Implemented by `CanPort.ConnectAsync`
///      returning early when `State == Connected`, so no spurious
///      `LinkStateChanged` fires.
///   2. `CloseAsync` is idempotent. Same — `CanPort.DisconnectAsync`
///      returns early when `State == Disconnected`. `CloseAsync` on a
///      port that never got built is a no-op.
///   3. `ReconnectAsync` always fires at least one `LinkStateChanged`
///      (the intermediate `Disconnected(ReconnectPending, _)` is
///      observable; the final `Connected` or `Error` follows).
///   4. `IAsyncDisposable` emits a final
///      `Disconnected(ReconnectPending, now)` and cancels any
///      in-flight call via the lifecycle CTS.
///   5. `CurrentState` is consistent with the latest `OnNext` on
///      `LinkStateChanged` — the state assignment under `stateLock`
///      precedes the `subject.OnNext` invocation.
///
/// Threading:
///   - `OpenAsync` / `CloseAsync` / `ReconnectAsync` are serialised by
///     a `SemaphoreSlim(1)` per the contract's Threading section.
///     The semaphore is async-aware so the callers' `CancellationToken`
///     propagates through `WaitAsync(ct)`.
///   - `LinkStateChanged` events fire on whichever thread the
///     vendored stack raises `StateChanged` from (the PCANManager
///     monitor task or the lifecycle method caller); subscribers
///     marshal to their own thread as needed.
///   - `currentState` reads / writes are guarded by `stateLock`; the
///     `SubjectFanOut.OnNext` fan-out happens outside the lock so an
///     observer that blocks does not block the state-machine itself.
type PcanCanLink(portFactory: unit -> ICommunicationPort, logger: ILogger<PcanCanLink>) =

    let subject = SubjectFanOut<CanLinkState>()
    let gate = new SemaphoreSlim(1, 1)
    let lifecycleCts = new CancellationTokenSource()

    /// State the state-machine transitions need to coordinate. Kept
    /// under `stateLock` since the `StateChanged` event handler may
    /// fire on the vendored PCANManager monitor task (a background
    /// `Task.Run`) concurrently with the lifecycle methods.
    let stateLock = obj ()
    let mutable currentState: CanLinkState = Initializing
    let mutable closeRequested = false
    let mutable haveBeenConnected = false
    let mutable portBuild: PortBuild = NotAttempted

    /// Fallback identification used when `PcanAdapterIdentity.tryRead`
    /// returns `None` (the channel is open but the PEAK GetValue
    /// queries failed). The status row still renders a `Connected`
    /// chip; the detail affordance shows the placeholder text.
    let placeholderAdapter: AdapterIdentification =
        { ChannelName = "PCAN adapter"
          DeviceId = "unknown"
          BaudrateBps = 250_000 }

    let translateState (newState: ConnectionState) : CanLinkState option =
        lock stateLock (fun () ->
            let now = DateTimeOffset.UtcNow

            match newState with
            | ConnectionState.Connected ->
                let adapter =
                    PcanAdapterIdentity.tryRead () |> Option.defaultValue placeholderAdapter

                haveBeenConnected <- true
                let state = Connected(adapter, now)
                currentState <- state
                Some state
            | ConnectionState.Disconnected ->
                let reason =
                    if closeRequested then
                        closeRequested <- false
                        ReconnectPending
                    elif haveBeenConnected then
                        MidSessionUnplug
                    else
                        NoAdapterPresent

                let state = Disconnected(reason, now)
                currentState <- state
                Some state
            | ConnectionState.Error ->
                // First observation of an unexpected PEAK status is
                // Recoverable; CanLinkService escalates to Fatal on the
                // second observation across a reconnect per
                // `research.md` R8. The detail string follows the
                // headline\ntechnical convention `buildFailureState`
                // uses (see comment on that helper). `PeakErrorText`
                // queries the actual PEAK status text (issue #124,
                // FR-004); the fallback fires when `PCANBasic.dll` is
                // unreachable on the host — the technical line is
                // still non-empty so `CanStatusRow`'s tooltip stays
                // informative.
                let detail =
                    PeakErrorText.tryReadCurrentErrorDetail ()
                    |> Option.defaultValue
                        "adapter reported error\nPEAK status query unavailable"

                let state = Error(Recoverable detail, now)
                currentState <- state
                Some state
            | ConnectionState.Connecting
            | _ -> None)

    let onStateChanged (newState: ConnectionState) =
        match translateState newState with
        | Some state -> subject.OnNext state
        | None -> ()

    let linkCancellation (caller: CancellationToken) : CancellationTokenSource =
        CancellationTokenSource.CreateLinkedTokenSource(caller, lifecycleCts.Token)

    /// Capture a port-construction failure as a sticky `CanLinkState`.
    /// `DllNotFoundException` is escalated straight to `Fatal` — the
    /// driver isn't going to materialise across a reconnect click —
    /// while every other exception is surfaced as `Recoverable` so
    /// the user can still retry.
    ///
    /// **Detail-string convention**: the first line is the short
    /// user-facing headline shown next to the chip; any additional
    /// lines are technical detail rendered by the GUI's tooltip
    /// affordance (`CanStatusRow.detailText`). `CanStatusRow.headline`
    /// splits on the first newline to take only the short line, so
    /// the status row stays compact while the tooltip surfaces the
    /// underlying exception for diagnostics. Keep the short line
    /// actionable ("install the PEAK driver") and the technical line
    /// verbatim from the exception.
    let buildFailureState (ex: exn) : CanLinkState =
        let now = DateTimeOffset.UtcNow

        let detailFor (short: string) =
            sprintf "%s\nTechnical detail: %s: %s" short (ex.GetType().Name) ex.Message

        match ex with
        | :? DllNotFoundException ->
            Error(
                Fatal(
                    detailFor "PEAK PCANBasic native DLL not found — install the PEAK driver"
                ),
                now
            )
        | _ -> Error(Recoverable(detailFor "CAN stack failed to initialise"), now)

    /// Build the underlying port on first use, or return the
    /// previously-captured failure state. Mutates `portBuild` under
    /// `stateLock` so the failure observation can also update
    /// `currentState` atomically.
    let ensurePort () : PortAccess =
        lock stateLock (fun () ->
            match portBuild with
            | Built(port, _) -> PortReady port
            | Failed state -> PortFailure state
            | NotAttempted ->
                try
                    let port = portFactory ()
                    let handler = EventHandler<ConnectionState>(fun _ s -> onStateChanged s)
                    port.StateChanged.AddHandler handler
                    portBuild <- Built(port, handler)
                    PortReady port
                with ex ->
                    let state = buildFailureState ex
                    currentState <- state
                    portBuild <- Failed state

                    let classification =
                        match state with
                        | Error(Fatal _, _) -> "Fatal"
                        | Error(Recoverable _, _) -> "Recoverable"
                        | _ -> "Error"

                    logger.LogError(
                        ex,
                        "PEAK stack construction failed; CAN link will report {ClassifiedAs}",
                        classification
                    )

                    PortFailure state)

    /// Open implementation that assumes `gate` is already held by the
    /// caller. `Reconnect` reuses this so the close/open pair runs
    /// under a single critical section without re-entering the
    /// (non-reentrant) `SemaphoreSlim`.
    let openInternal (ct: CancellationToken) : Task =
        task {
            match ensurePort () with
            | PortFailure failureState ->
                // Emit the captured construction-failure state. The
                // state was set under `stateLock` inside `ensurePort`
                // so `currentState` is already consistent — only the
                // subject fan-out remains.
                subject.OnNext failureState
            | PortReady port ->
                try
                    do! port.ConnectAsync ct
                with
                | :? OperationCanceledException -> return! Task.FromCanceled<unit>(ct)
                | ex ->
                    // `CanPort.ConnectAsync` raises on timeout AFTER firing
                    // `Transition(Error)`, so the StateChanged handler has
                    // already emitted `Error(Recoverable, _)`. Log + swallow
                    // here so the public surface contract holds: failures
                    // surface via `LinkStateChanged`, not via thrown
                    // exceptions.
                    logger.LogWarning(
                        ex,
                        "PcanCanLink.OpenAsync failed; failure surfaces via LinkStateChanged"
                    )
        }

    let closeInternal (ct: CancellationToken) : Task =
        task {
            match portBuild with
            | Built(port, _) ->
                lock stateLock (fun () -> closeRequested <- true)
                do! port.DisconnectAsync ct
            | NotAttempted
            | Failed _ ->
                // No port was ever built (or the build failed): nothing
                // to close. Stay idempotent — do not emit a synthetic
                // Disconnected here, since there is no prior Connected
                // to leave.
                ()
        }

    interface ICanLink with

        member _.OpenAsync(_baudrateBps: int, cancellationToken: CancellationToken) =
            task {
                use linked: CancellationTokenSource = linkCancellation cancellationToken
                do! gate.WaitAsync linked.Token

                try
                    do! openInternal linked.Token
                finally
                    gate.Release() |> ignore
            }

        member _.CloseAsync(cancellationToken: CancellationToken) =
            task {
                use linked: CancellationTokenSource = linkCancellation cancellationToken
                do! gate.WaitAsync linked.Token

                try
                    do! closeInternal linked.Token
                finally
                    gate.Release() |> ignore
            }

        member _.ReconnectAsync(cancellationToken: CancellationToken) =
            task {
                use linked: CancellationTokenSource = linkCancellation cancellationToken
                do! gate.WaitAsync linked.Token

                try
                    do! closeInternal linked.Token
                    do! openInternal linked.Token
                finally
                    gate.Release() |> ignore
            }

        member _.LinkStateChanged = subject :> IObservable<CanLinkState>

        member _.CurrentState =
            lock stateLock (fun () -> currentState)

    interface IAsyncDisposable with

        member _.DisposeAsync() =
            let work =
                task {
                    match portBuild with
                    | Built(port, handler) -> port.StateChanged.RemoveHandler handler
                    | NotAttempted
                    | Failed _ -> ()

                    if not lifecycleCts.IsCancellationRequested then
                        try
                            lifecycleCts.Cancel()
                        with :? ObjectDisposedException ->
                            ()

                    let now = DateTimeOffset.UtcNow
                    let final = Disconnected(ReconnectPending, now)

                    lock stateLock (fun () -> currentState <- final)

                    subject.OnNext final

                    gate.Dispose()
                    lifecycleCts.Dispose()
                }

            ValueTask work
