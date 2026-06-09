namespace Stem.ButtonPanelTester.Infrastructure.Can

open System
open Core.Interfaces

/// Lazily builds ONE `ICommunicationPort` and shares it between the lifecycle
/// adapter (`PcanCanLink`, via `GetOrBuild` as its `portFactory`) and the receive
/// adapter (`PcanCanFrameStream`, via `OnBuilt`). Build runs once, on the first
/// `GetOrBuild` (PcanCanLink's first `OpenAsync`), so the PEAK channel is claimed
/// on user-connect, NOT at composition (preserves the #127 lazy boot resilience).
/// Build exceptions propagate to the `GetOrBuild` caller (`PcanCanLink.ensurePort`
/// captures them as an `Error` state); not swallowed here. Owns the single PEAK
/// handle: disposes the built port on `Dispose`.
type CanPortShare(build: unit -> ICommunicationPort) =
    let gate = obj ()
    let mutable port: ICommunicationPort option = None
    let mutable pending: (ICommunicationPort -> unit) list = []

    /// Build on first call (memoized), fire registered `OnBuilt` callbacks in
    /// registration order, return the port; later calls return the same instance.
    member _.GetOrBuild() : ICommunicationPort =
        lock gate (fun () ->
            match port with
            | Some p -> p
            | None ->
                let p = build () // may throw -> propagates; port stays None
                port <- Some p
                for cb in List.rev pending do cb p
                pending <- []
                p)

    /// Register a callback fired with the port the moment it is built (or
    /// immediately if already built). `PcanCanFrameStream` attaches its
    /// `PacketReceived` forwarder here WITHOUT forcing an eager build.
    member _.OnBuilt(callback: ICommunicationPort -> unit) =
        lock gate (fun () ->
            match port with
            | Some p -> callback p
            | None -> pending <- callback :: pending)

    interface IDisposable with
        member _.Dispose() =
            lock gate (fun () ->
                match port with
                | Some p -> p.Dispose()
                | None -> ())
