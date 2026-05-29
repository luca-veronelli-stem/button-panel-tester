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
/// error transition before raising. `EmitDisconnectedThenReturn` (#168)
/// models the cold-start poll loop exhausting: fire
/// `StateChanged(Disconnected)` and return normally, exercising the
/// `Disconnected` cold-start arm rather than the `Error` one.
type private ConnectBehavior =
    | EmitConnectedThenReturn
    | EmitErrorThenThrow
    | EmitDisconnectedThenReturn

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
            | EmitDisconnectedThenReturn ->
                state <- ConnectionState.Disconnected
                stateChanged.Trigger(null, ConnectionState.Disconnected)
                Task.CompletedTask

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

/// AC3 regression, reshaped for #136: a `ConnectionState.Error`
/// observed on the very first `OpenAsync` — while we have *never* been
/// connected — is the adapter-absent cold start, not a runtime fault.
/// `translateState` now reclassifies it as
/// `Disconnected(NoAdapterPresent, _)` rather than a generic
/// `Error(Recoverable _, _)`. The fail-fast plumbing is otherwise
/// unchanged: the fake fires `StateChanged(Error)` then raises
/// `InvalidOperationException` (mirroring `CanPort.cs:85-87`), and
/// `PcanCanLink.openInternal` catches it so no exception escapes
/// `OpenAsync`. Exactly one emission must be observed — no `Connected`
/// smearing — and `CurrentState` must reflect it.
///
/// #168: the cold-start `Error` arm now consults a channel-condition
/// probe. An unreadable probe (`readCondition () = None`, e.g. a
/// driver-less host) must preserve the #136 derivation, so this test
/// injects `(fun () -> None)` — both pinning that fallback and keeping
/// the test hermetic regardless of the bench host's PEAK state.
[<Fact>]
let ``coldStart_FirstOpenAsync_FakePortThrows_EmitsNoAdapterPresentOnce`` () =
    task {
        let fakePort = new FakeCommunicationPort(EmitErrorThenThrow)

        let link =
            new PcanCanLink(
                (fun () -> fakePort :> ICommunicationPort),
                NullLogger<PcanCanLink>.Instance,
                (fun () -> None)
            )

        try
            let iLink = link :> ICanLink

            let emissions = ResizeArray<CanLinkState>()

            use _subscription =
                iLink.LinkStateChanged
                |> Observable.subscribe (fun state -> emissions.Add state)

            do! iLink.OpenAsync(250_000, CancellationToken.None)

            let noAdapterCount =
                emissions
                |> Seq.filter (fun s ->
                    match s with
                    | Disconnected(NoAdapterPresent, _) -> true
                    | _ -> false)
                |> Seq.length

            Assert.Equal(1, noAdapterCount)
            Assert.Equal(1, emissions.Count)

            match iLink.CurrentState with
            | Disconnected(NoAdapterPresent, _) -> ()
            | other -> Assert.Fail(sprintf "expected Disconnected(NoAdapterPresent, _), got %A" other)
        finally
            (link :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }

/// #168 cold-start (via the `Error` arm): when the PEAK channel
/// *condition* reports `ChannelOccupied` — an adapter is physically
/// present but held *exclusively* by another app (StemDeviceManager is
/// the canonical holder, #150) — a first-open `Error` transition must
/// surface the reused busy classification
/// (`Error(Recoverable "...adapter busy...", _)`), NOT the
/// adapter-absent `Disconnected(NoAdapterPresent, _)`. The injected
/// `readCondition` stub stands in for the live PEAK probe so the
/// classification is exercised without hardware.
[<Fact>]
let ``coldStart_ErrorWhenChannelOccupied_SurfacesBusyNotNoAdapter`` () =
    task {
        let fakePort = new FakeCommunicationPort(EmitErrorThenThrow)

        let link =
            new PcanCanLink(
                (fun () -> fakePort :> ICommunicationPort),
                NullLogger<PcanCanLink>.Instance,
                (fun () -> Some PeakStatusTranslation.ChannelCondition.ChannelOccupied)
            )

        try
            let iLink = link :> ICanLink
            let emissions = ResizeArray<CanLinkState>()

            use _subscription =
                iLink.LinkStateChanged |> Observable.subscribe (fun state -> emissions.Add state)

            do! iLink.OpenAsync(250_000, CancellationToken.None)

            Assert.Equal(1, emissions.Count)

            match iLink.CurrentState with
            | Error(Recoverable detail, _) -> Assert.Contains("adapter busy", detail)
            | other -> Assert.Fail(sprintf "expected Error(Recoverable busy, _), got %A" other)
        finally
            (link :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }

/// #168 cold-start (via the `Disconnected` arm): the poll loop can
/// exhaust and fire `Disconnected` (rather than `Error`) before we have
/// ever connected. That path must route through the same
/// channel-condition probe, so an `Occupied` channel still surfaces
/// busy rather than `NoAdapterPresent`.
[<Fact>]
let ``coldStart_DisconnectWhenChannelOccupied_SurfacesBusyNotNoAdapter`` () =
    task {
        let fakePort = new FakeCommunicationPort(EmitDisconnectedThenReturn)

        let link =
            new PcanCanLink(
                (fun () -> fakePort :> ICommunicationPort),
                NullLogger<PcanCanLink>.Instance,
                (fun () -> Some PeakStatusTranslation.ChannelCondition.ChannelOccupied)
            )

        try
            let iLink = link :> ICanLink
            let emissions = ResizeArray<CanLinkState>()

            use _subscription =
                iLink.LinkStateChanged |> Observable.subscribe (fun state -> emissions.Add state)

            do! iLink.OpenAsync(250_000, CancellationToken.None)

            Assert.Equal(1, emissions.Count)

            match iLink.CurrentState with
            | Error(Recoverable detail, _) -> Assert.Contains("adapter busy", detail)
            | other -> Assert.Fail(sprintf "expected Error(Recoverable busy, _), got %A" other)
        finally
            (link :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }

/// #168 / #136 preservation: when the channel condition reports
/// `ChannelUnavailable` (no hardware on the channel), a first-open
/// `Error` must keep the adapter-absent `Disconnected(NoAdapterPresent,
/// _)` headline — the busy reclassification is scoped to `Occupied`
/// only.
[<Fact>]
let ``coldStart_ErrorWhenChannelUnavailable_PreservesNoAdapterPresent`` () =
    task {
        let fakePort = new FakeCommunicationPort(EmitErrorThenThrow)

        let link =
            new PcanCanLink(
                (fun () -> fakePort :> ICommunicationPort),
                NullLogger<PcanCanLink>.Instance,
                (fun () -> Some PeakStatusTranslation.ChannelCondition.ChannelUnavailable)
            )

        try
            let iLink = link :> ICanLink
            let emissions = ResizeArray<CanLinkState>()

            use _subscription =
                iLink.LinkStateChanged |> Observable.subscribe (fun state -> emissions.Add state)

            do! iLink.OpenAsync(250_000, CancellationToken.None)

            Assert.Equal(1, emissions.Count)

            match iLink.CurrentState with
            | Disconnected(NoAdapterPresent, _) -> ()
            | other -> Assert.Fail(sprintf "expected Disconnected(NoAdapterPresent, _), got %A" other)
        finally
            (link :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }

/// #168 bench finding: reaching the cold-start (open-FAILED) arm with a
/// `ChannelPCanView` (`0x03` = `Available | Occupied`) probe means
/// PCAN-View holds the channel at an INCOMPATIBLE bitrate — PCAN-View
/// shares the channel only at a MATCHING bitrate, in which case our
/// `Initialize` succeeds and we reach the `Connected` arm instead (see
/// `coldStart_PcanViewButOpenSucceeds_Connects`). So a first-open
/// `Error` with `ChannelPCanView` present must surface the busy
/// classification (`Error(Recoverable "...adapter busy...", _)`), NOT
/// `Disconnected(NoAdapterPresent, _)`.
[<Fact>]
let ``coldStart_ChannelPcanView_SurfacesBusy`` () =
    task {
        let fakePort = new FakeCommunicationPort(EmitErrorThenThrow)

        let link =
            new PcanCanLink(
                (fun () -> fakePort :> ICommunicationPort),
                NullLogger<PcanCanLink>.Instance,
                (fun () -> Some PeakStatusTranslation.ChannelCondition.ChannelPCanView)
            )

        try
            let iLink = link :> ICanLink
            let emissions = ResizeArray<CanLinkState>()

            use _subscription =
                iLink.LinkStateChanged |> Observable.subscribe (fun state -> emissions.Add state)

            do! iLink.OpenAsync(250_000, CancellationToken.None)

            Assert.Equal(1, emissions.Count)

            match iLink.CurrentState with
            | Error(Recoverable detail, _) -> Assert.Contains("adapter busy", detail)
            | other -> Assert.Fail(sprintf "expected Error(Recoverable busy, _) (PcanView at mismatched bitrate), got %A" other)
        finally
            (link :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }

/// #168 invariant guard (GREEN on write): the `ChannelPCanView` → busy
/// branch fires ONLY when the open FAILED. A *matching-bitrate*
/// PCAN-View shares the channel, so our `Initialize` succeeds, the
/// `Connected` arm runs (which never reads the condition), and
/// `coldStartState` is never reached. With the open succeeding, a
/// `ChannelPCanView` probe must therefore still yield `Connected _`,
/// NOT busy — the discriminator is connect-success-vs-failure, not the
/// `0x03` condition value.
[<Fact>]
let ``coldStart_PcanViewButOpenSucceeds_Connects`` () =
    task {
        let fakePort = new FakeCommunicationPort(EmitConnectedThenReturn)

        let link =
            new PcanCanLink(
                (fun () -> fakePort :> ICommunicationPort),
                NullLogger<PcanCanLink>.Instance,
                (fun () -> Some PeakStatusTranslation.ChannelCondition.ChannelPCanView)
            )

        try
            let iLink = link :> ICanLink
            let emissions = ResizeArray<CanLinkState>()

            use _subscription =
                iLink.LinkStateChanged |> Observable.subscribe (fun state -> emissions.Add state)

            do! iLink.OpenAsync(250_000, CancellationToken.None)

            match iLink.CurrentState with
            | Connected _ -> ()
            | other -> Assert.Fail(sprintf "expected Connected _ (matching-bitrate PcanView shares), got %A" other)
        finally
            (link :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }
