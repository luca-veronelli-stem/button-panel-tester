module Stem.ButtonPanelTester.Tests.Windows.Unit.Can.PcanCanLinkMidSessionUnplugTests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Core.Interfaces
open Core.Models
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Infrastructure.Can

/// #117 (US3 — survive mid-session unplug) regression for
/// `PcanCanLink.translateState`'s `Disconnected` derivation. A port
/// that WAS `Connected` and then goes `Disconnected` without anyone
/// having requested a close must surface
/// `Disconnected(MidSessionUnplug, _)` — driven by the
/// `haveBeenConnected = true && closeRequested = false` branch — NOT
/// `Disconnected(NoAdapterPresent, _)` (the cold-start derivation that
/// requires `haveBeenConnected = false`).
///
/// Mirrors the `FakeCommunicationPort` pattern in
/// `PcanCanLinkColdStartTests`: a file-private fake whose
/// `ConnectAsync` synchronously fires `StateChanged(Connected)`. The
/// extra `RaiseUnplug` hook models the vendored PCANManager monitor
/// task firing `StateChanged(Disconnected)` when the USB adapter is
/// physically removed mid-session.

/// File-private fake `ICommunicationPort`. `ConnectAsync` flips to
/// `Connected` and fires `StateChanged(Connected)` synchronously
/// (driving `PcanCanLink` to set `haveBeenConnected`). `RaiseUnplug`
/// then fires an UNSOLICITED `StateChanged(Disconnected)` — the
/// mid-session unplug — without going through `DisconnectAsync`, so
/// `PcanCanLink.closeRequested` stays `false`. `PacketReceived`,
/// `SendAsync` are not exercised.
type private FakeCommunicationPort() =
    let mutable state = ConnectionState.Disconnected
    let packetReceived = Event<EventHandler<RawPacket>, RawPacket>()
    let stateChanged = Event<EventHandler<ConnectionState>, ConnectionState>()

    /// Simulate the physical mid-session unplug: the vendored stack's
    /// monitor task raises `StateChanged(Disconnected)` with no close
    /// request in flight.
    member _.RaiseUnplug() =
        state <- ConnectionState.Disconnected
        stateChanged.Trigger(null, ConnectionState.Disconnected)

    interface ICommunicationPort with
        member _.Kind = ChannelKind.Can
        member _.State = state
        member _.IsConnected = state = ConnectionState.Connected

        [<CLIEvent>]
        member _.PacketReceived = packetReceived.Publish

        [<CLIEvent>]
        member _.StateChanged = stateChanged.Publish

        member _.ConnectAsync(_ct: CancellationToken) =
            state <- ConnectionState.Connected
            stateChanged.Trigger(null, ConnectionState.Connected)
            Task.CompletedTask

        member _.DisconnectAsync(_ct: CancellationToken) = Task.CompletedTask

        member _.SendAsync(_payload: ReadOnlyMemory<byte>, _ct: CancellationToken) =
            Task.CompletedTask

    interface IDisposable with
        member _.Dispose() = ()

/// #117 regression: after a successful first `OpenAsync` (which sets
/// `haveBeenConnected`), an unsolicited `Disconnected` transition must
/// surface exactly one `Disconnected(MidSessionUnplug, _)` and never a
/// `Disconnected(NoAdapterPresent, _)`. `CurrentState` reflects the
/// same.
[<Fact>]
let ``midSession_DisconnectAfterConnected_SurfacesMidSessionUnplugNotNoAdapterPresent`` () =
    task {
        let fakePort = new FakeCommunicationPort()

        let link =
            new PcanCanLink(
                (fun () -> fakePort :> ICommunicationPort),
                NullLogger<PcanCanLink>.Instance
            )

        try
            let iLink = link :> ICanLink

            let emissions = ResizeArray<CanLinkState>()

            use _subscription =
                iLink.LinkStateChanged
                |> Observable.subscribe (fun state -> emissions.Add state)

            // First open connects → `haveBeenConnected := true`.
            do! iLink.OpenAsync(250_000, CancellationToken.None)

            // The adapter is physically unplugged mid-session.
            fakePort.RaiseUnplug()

            let midSessionCount =
                emissions
                |> Seq.filter (fun s ->
                    match s with
                    | Disconnected(MidSessionUnplug, _) -> true
                    | _ -> false)
                |> Seq.length

            let noAdapterCount =
                emissions
                |> Seq.filter (fun s ->
                    match s with
                    | Disconnected(NoAdapterPresent, _) -> true
                    | _ -> false)
                |> Seq.length

            Assert.Equal(1, midSessionCount)
            Assert.Equal(0, noAdapterCount)

            match iLink.CurrentState with
            | Disconnected(MidSessionUnplug, _) -> ()
            | other -> Assert.Fail(sprintf "expected Disconnected(MidSessionUnplug, _), got %A" other)
        finally
            (link :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }
