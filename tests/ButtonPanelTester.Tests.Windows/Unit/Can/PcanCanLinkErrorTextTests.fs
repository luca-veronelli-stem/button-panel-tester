module Stem.ButtonPanelTester.Tests.Windows.Unit.Can.PcanCanLinkErrorTextTests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Core.Interfaces
open Core.Models
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Infrastructure.Can

/// File-private fake `ICommunicationPort` that fires
/// `StateChanged(Error)` from inside `ConnectAsync`, mirroring the
/// shape used by `PcanCanLinkColdStartTests.FakeCommunicationPort` so
/// the test exercises exactly the path
/// `CanPort.cs:83 → PcanCanLink.translateState(ConnectionState.Error)`.
type private FakeErrorPort() =
    let mutable state = ConnectionState.Disconnected
    let packetReceived = Event<EventHandler<RawPacket>, RawPacket>()
    let stateChanged = Event<EventHandler<ConnectionState>, ConnectionState>()

    interface ICommunicationPort with
        member _.Kind = ChannelKind.Can
        member _.State = state
        member _.IsConnected = false

        [<CLIEvent>]
        member _.PacketReceived = packetReceived.Publish

        [<CLIEvent>]
        member _.StateChanged = stateChanged.Publish

        member _.ConnectAsync(_ct: CancellationToken) =
            state <- ConnectionState.Error
            stateChanged.Trigger(null, ConnectionState.Error)
            raise (InvalidOperationException("fake: error transition"))

        member _.DisconnectAsync(_ct: CancellationToken) = Task.CompletedTask

        member _.SendAsync(_payload: ReadOnlyMemory<byte>, _ct: CancellationToken) =
            Task.CompletedTask

    interface IDisposable with
        member _.Dispose() = ()

/// Extract the `Recoverable` detail string from the first
/// `Error(Recoverable _, _)` emission the link surfaced, or fail the
/// test if none was observed.
let private firstRecoverableDetail (emissions: ResizeArray<CanLinkState>) : string =
    emissions
    |> Seq.tryPick (fun state ->
        match state with
        | Error(Recoverable detail, _) -> Some detail
        | _ -> None)
    |> function
        | Some detail -> detail
        | None ->
            Assert.Fail(
                sprintf "expected at least one Error(Recoverable _, _) emission; got %A" emissions
            )

            failwith "unreachable"

/// #124 regression: the `ConnectionState.Error` transition must surface
/// the real PEAK status text in the `Error(Recoverable detail, _)`
/// emission, not the hardcoded literal `"PEAK adapter reported Error"`
/// that lived in `PcanCanLink.fs:186` before this fix. Whether the host
/// has the PEAK driver installed or not, the detail must follow the
/// `headline\ntechnical-detail` convention `buildFailureState`
/// established (`PcanCanLink.fs:209-214`) — single-line legacy strings
/// are the regression we are guarding against.
[<Fact>]
let ``errorTransition_DetailString_IsNotLegacyHardcodedLiteral`` () =
    task {
        let fakePort = new FakeErrorPort()

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

            let detail = firstRecoverableDetail emissions

            Assert.NotEqual<string>("PEAK adapter reported Error", detail)
        finally
            (link :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }

/// #124 regression: the detail surfaced on the `Error` transition must
/// carry both a short headline and a technical-detail line, separated
/// by `\n`, so `CanStatusRow.headline` can render the compact chip
/// label while the tooltip surfaces the underlying PEAK status. The
/// pre-fix literal `"PEAK adapter reported Error"` is single-line and
/// fails this convention.
[<Fact>]
let ``errorTransition_DetailString_FollowsHeadlineTechnicalConvention`` () =
    task {
        let fakePort = new FakeErrorPort()

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

            let detail = firstRecoverableDetail emissions

            Assert.Contains("\n", detail)

            let lines = detail.Split('\n')
            Assert.True(lines.Length >= 2, sprintf "expected >=2 lines, got %d in %A" lines.Length detail)
            Assert.False(String.IsNullOrWhiteSpace lines.[0], "headline must not be blank")
            Assert.False(String.IsNullOrWhiteSpace lines.[1], "technical detail must not be blank")
        finally
            (link :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }
