module Stem.ButtonPanelTester.Tests.Windows.Unit.Can.PcanCanFrameStreamTests

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Buffers.Binary
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Core.Interfaces
open Core.Models
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Infrastructure.Can

/// File-private fake `ICommunicationPort` (mirrors `FakeCommunicationPort` in
/// PcanCanLinkColdStartTests). `RaisePacket` fires `PacketReceived` with a
/// caller-supplied `RawPacket`, exercising the translation with no hardware.
type private FakeCommunicationPort() =
    let packetReceived = Event<EventHandler<RawPacket>, RawPacket>()
    let stateChanged = Event<EventHandler<ConnectionState>, ConnectionState>()

    member _.RaisePacket(packet: RawPacket) = packetReceived.Trigger(null, packet)

    interface ICommunicationPort with
        member _.Kind = ChannelKind.Can
        member _.State = ConnectionState.Connected
        member _.IsConnected = true
        [<CLIEvent>]
        member _.PacketReceived = packetReceived.Publish
        [<CLIEvent>]
        member _.StateChanged = stateChanged.Publish
        member _.ConnectAsync(_ct: CancellationToken) = Task.CompletedTask
        member _.DisconnectAsync(_ct: CancellationToken) = Task.CompletedTask
        member _.SendAsync(_payload: ReadOnlyMemory<byte>, _ct: CancellationToken) = Task.CompletedTask

    interface IDisposable with
        member _.Dispose() = ()

/// Deterministic IClock for the timestamp-fallback case.
type private FixedClock(instant: DateTimeOffset) =
    interface IClock with
        member _.UtcNow() = instant

let private fixedNow = DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

/// The real virgin_panel_12v WHO_I_AM 15-byte payload.
let private whoIamBytes =
    [| 0xFFuy; 0x00uy; 0x04uy
       0x17uy; 0x7Cuy; 0x12uy; 0x6Duy
       0x73uy; 0x08uy; 0x74uy; 0x8Fuy
       0x16uy; 0x09uy; 0x21uy; 0x04uy |]

/// Build a vendored RawPacket: [arbId little-endian 4B][data].
let private rawPacket (canId: uint32) (data: byte[]) (timestamp: DateTime) : RawPacket =
    let arb = Array.zeroCreate<byte> 4
    BinaryPrimitives.WriteUInt32LittleEndian(arb.AsSpan(), canId)
    RawPacket(ImmutableArray.CreateRange(Array.append arb data), timestamp)

/// Wire a stream over a fake port, force the share to build (-> OnBuilt -> attach),
/// and return the fake + stream + a collector of RawFramesReceived.
let private wire (clock: IClock) =
    let fakePort = new FakeCommunicationPort()
    let share = new CanPortShare(fun () -> fakePort :> ICommunicationPort)
    let svc = new PcanCanFrameStream(share, clock, NullLogger<PcanCanFrameStream>.Instance)
    let frames = List<RawCanFrame>()
    (svc :> ICanFrameStream).RawFramesReceived |> Observable.subscribe (fun fr -> frames.Add fr) |> ignore
    share.GetOrBuild() |> ignore
    (fakePort, svc, frames)

// (1) translate a WHO_I_AM packet: LE arbId -> CanId, data slice -> Payload, timestamp -> ReceivedAt.
[<Fact>]
let Translate_WhoIamPacket_MapsCanIdPayloadAndTimestamp () =
    let ts = DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc)
    let (fakePort, svc, frames) = wire (FixedClock(fixedNow) :> IClock)
    use _ = svc :> IDisposable

    fakePort.RaisePacket(rawPacket 0x1FFFFFFFu whoIamBytes ts)

    Assert.Equal(1, frames.Count)
    let f = frames.[0]
    Assert.Equal(0x1FFFFFFFu, f.CanId)
    Assert.Equal(15, f.Payload.Length)
    Assert.Equal<byte[]>(whoIamBytes, f.Payload.ToArray())
    Assert.Equal(DateTimeOffset(ts), f.ReceivedAt)

// (2) timestamp fallback: DateTime.MinValue -> ReceivedAt from IClock.
[<Fact>]
let Translate_NoAdapterTimestamp_FallsBackToClock () =
    let (fakePort, svc, frames) = wire (FixedClock(fixedNow) :> IClock)
    use _ = svc :> IDisposable

    fakePort.RaisePacket(rawPacket 0x1FFFFFFFu whoIamBytes DateTime.MinValue)

    Assert.Equal(1, frames.Count)
    Assert.Equal(fixedNow, frames.[0].ReceivedAt)
