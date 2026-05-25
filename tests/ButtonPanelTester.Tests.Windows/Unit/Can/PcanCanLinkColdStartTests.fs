module Stem.ButtonPanelTester.Tests.Windows.Unit.Can.PcanCanLinkColdStartTests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Core.Interfaces
open Core.Models
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Infrastructure.Can

/// Scriptable behavior for the file-private fake `ICommunicationPort`.
/// AC2 exercises the success path: fire `StateChanged(Connected)` then
/// return normally. AC3 exercises the fail-fast path: fire
/// `StateChanged(Error)` then throw `InvalidOperationException`,
/// mirroring `CanPort.cs:85-87`'s contract where the port surfaces an
/// error transition before raising.
type private ConnectBehavior =
    | EmitConnectedThenReturn
    | EmitErrorThenThrow

/// File-private fake `ICommunicationPort` for the Slice-2 regression.
/// Models the post-fix `CanPort` shape: `_state` starts at
/// `Disconnected`; the first `ConnectAsync` synchronously fires
/// `StateChanged` to the subscribed handler before returning or
/// throwing. `PacketReceived`, `DisconnectAsync`, `SendAsync`,
/// `Dispose` are not exercised by these tests.
type private FakeCommunicationPort(behavior: ConnectBehavior) =
    let mutable state = ConnectionState.Disconnected
    let packetReceived = Event<EventHandler<RawPacket>, RawPacket>()
    let stateChanged = Event<EventHandler<ConnectionState>, ConnectionState>()

    interface ICommunicationPort with
        member _.Kind = ChannelKind.Can
        member _.State = state
        member _.IsConnected = state = ConnectionState.Connected

        [<CLIEvent>]
        member _.PacketReceived = packetReceived.Publish

        [<CLIEvent>]
        member _.StateChanged = stateChanged.Publish

        member _.ConnectAsync(_ct: CancellationToken) =
            match behavior with
            | EmitConnectedThenReturn ->
                state <- ConnectionState.Connected
                stateChanged.Trigger(null, ConnectionState.Connected)
                Task.CompletedTask
            | EmitErrorThenThrow ->
                state <- ConnectionState.Error
                stateChanged.Trigger(null, ConnectionState.Error)
                raise (InvalidOperationException("fake: cold-start failure"))

        member _.DisconnectAsync(_ct: CancellationToken) = Task.CompletedTask

        member _.SendAsync(_payload: ReadOnlyMemory<byte>, _ct: CancellationToken) =
            Task.CompletedTask

    interface IDisposable with
        member _.Dispose() = ()

/// AC2 regression: with the post-fix `CanPort` shape in place, the
/// first `OpenAsync` on `PcanCanLink` must surface a single
/// `Connected(_, _)` emission to subscribers of `LinkStateChanged`,
/// and `CurrentState` must reflect the same. The fake fires
/// `StateChanged(Connected)` synchronously from inside its
/// `ConnectAsync` — exactly the path `CanPort`'s post-fix poll loop
/// takes on attempt 0 when `_driver.IsConnected` is already `true`.
[<Fact>]
let ``coldStart_FirstOpenAsync_EmitsConnectedOnce`` () =
    task {
        let fakePort = new FakeCommunicationPort(EmitConnectedThenReturn)

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

            do! iLink.OpenAsync(250_000, CancellationToken.None)

            let connectedCount =
                emissions
                |> Seq.filter (fun s ->
                    match s with
                    | Connected _ -> true
                    | _ -> false)
                |> Seq.length

            Assert.Equal(1, connectedCount)

            match iLink.CurrentState with
            | Connected _ -> ()
            | other -> Assert.Fail(sprintf "expected Connected, got %A" other)
        finally
            (link :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }

/// AC3 regression: the existing adapter-absent fail-fast path remains
/// intact. The fake fires `StateChanged(Error)` then raises
/// `InvalidOperationException`, mirroring `CanPort.cs:85-87`'s
/// behaviour when the poll loop exhausts. `PcanCanLink.openInternal`
/// catches the exception (`PcanCanLink.fs:283-294`) and surfaces the
/// failure through `LinkStateChanged` only — no exception escapes
/// `OpenAsync`. Exactly one `Error(Recoverable _, _)` must be
/// observed; `CurrentState` must reflect it.
[<Fact>]
let ``coldStart_FirstOpenAsync_FakePortThrows_EmitsErrorOnce`` () =
    task {
        let fakePort = new FakeCommunicationPort(EmitErrorThenThrow)

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

            do! iLink.OpenAsync(250_000, CancellationToken.None)

            let recoverableCount =
                emissions
                |> Seq.filter (fun s ->
                    match s with
                    | Error(Recoverable _, _) -> true
                    | _ -> false)
                |> Seq.length

            Assert.Equal(1, recoverableCount)

            match iLink.CurrentState with
            | Error(Recoverable _, _) -> ()
            | other -> Assert.Fail(sprintf "expected Error(Recoverable, _), got %A" other)
        finally
            (link :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }
