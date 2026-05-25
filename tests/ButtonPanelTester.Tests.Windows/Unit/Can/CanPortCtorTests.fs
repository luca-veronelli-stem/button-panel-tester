module Stem.ButtonPanelTester.Tests.Windows.Unit.Can.CanPortCtorTests

open System
open System.Threading
open System.Threading.Tasks
open Xunit
open Core.Interfaces
open Core.Models
open Infrastructure.Protocol.Hardware

/// File-private fake `IPcanDriver` for the AC1 ctor regression. Models
/// the post-`PCANBasic.Initialize` shape: `IsConnected` reflects a
/// mutable backing field, and `ConnectionStatusChanged` fires only on
/// real transitions (matches the production setter at
/// `PCANManager.cs:72-83`, especially `:80` where the invoke happens).
/// `PacketReceived` is implemented but never raised by these tests;
/// `SendMessageAsync` / `Disconnect` are no-ops so the fake satisfies
/// the interface without touching CAN hardware.
type private FakePcanDriver(initiallyConnected: bool) =
    let mutable connected = initiallyConnected
    let packetReceived = Event<EventHandler<CANPacketEventArgs>, CANPacketEventArgs>()
    let statusChanged = Event<EventHandler<bool>, bool>()

    /// Test-only shim. Flips the driver's connection state and fires
    /// `ConnectionStatusChanged` exactly when the state actually
    /// changes, mirroring the production setter's idempotent guard.
    member _.SetConnected(value: bool) =
        if connected <> value then
            connected <- value
            statusChanged.Trigger(null, value)

    interface IPcanDriver with
        member _.IsConnected = connected

        [<CLIEvent>]
        member _.PacketReceived = packetReceived.Publish

        [<CLIEvent>]
        member _.ConnectionStatusChanged = statusChanged.Publish

        member _.SendMessageAsync(_canId, _data, _isExtended) = Task.FromResult(true)

        member _.Disconnect() = ()

/// Tests that `CanPort.ConnectAsync` surfaces a single `Connected`
/// state transition to any subscriber that attached AFTER the port
/// constructor, regardless of whether the underlying driver was
/// already connected at construction time. This is the AC1 regression
/// for issue #127: the pre-fix constructor snapshotted
/// `driver.IsConnected` into `_state`, so when the driver had already
/// flipped to `IsConnected = true` inside its own constructor (e.g.
/// `PCANManager`'s eager `PCANBasic.Initialize` call), `ConnectAsync`
/// early-returned at `CanPort.cs:73` without emitting any transition
/// — and any handler attached after `new CanPort(driver)` missed the
/// only Connected event the port would ever fire on the cold-start
/// success path. The `[<Theory>]` covers both ctor-time driver states
/// per spec-review F2 + F6.
[<Theory>]
[<InlineData(true)>]
[<InlineData(false)>]
let ``ConnectAsync_PostCtorSubscriber_EmitsConnectedExactlyOnce``
    (initiallyConnected: bool)
    =
    task {
        let fake = FakePcanDriver(initiallyConnected)
        use port = new CanPort(fake)

        let emissions = ResizeArray<ConnectionState>()
        let handler =
            EventHandler<ConnectionState>(fun _ s -> emissions.Add s)

        port.StateChanged.AddHandler handler

        try
            if not initiallyConnected then
                fake.SetConnected(true)

            do! port.ConnectAsync(CancellationToken.None)
        finally
            port.StateChanged.RemoveHandler handler

        let connectedCount =
            emissions
            |> Seq.filter (fun s -> s = ConnectionState.Connected)
            |> Seq.length

        Assert.Equal(1, connectedCount)
        Assert.Equal(ConnectionState.Connected, port.State)
    }
