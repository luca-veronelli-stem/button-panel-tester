module Stem.ButtonPanelTester.Tests.Windows.Unit.Can.SetAddressAckObserverTests

open System
open System.Collections.Generic
open Microsoft.Extensions.Logging                // LogLevel
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can            // RawCanFrame, ICanFrameStream, ISetAddressAckObserver
open Stem.ButtonPanelTester.Infrastructure.Can  // SetAddressAckObserver
open Stem.ButtonPanelTester.Tests.Fakes         // LogEntry, RecordingLogger (linked in .fsproj)

/// File-private fake `ICanFrameStream`: `Emit` fans a `RawCanFrame` out on `RawFramesReceived`
/// synchronously on the calling thread (mirrors `WhoIAmReassemblyObserverTests.FakeFrameStream`).
type private FakeFrameStream() =
    let received = Event<RawCanFrame>()
    member _.Emit(frame: RawCanFrame) = received.Trigger frame
    interface ICanFrameStream with
        member _.RawFramesReceived = received.Publish :> IObservable<RawCanFrame>

let private at = DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero)
let private frame (canId: uint32) (bytes: byte[]) : RawCanFrame =
    { CanId = canId; Payload = ReadOnlyMemory<byte>(bytes); ReceivedAt = at }

/// The tool's configured protocol sender id (`DeviceVariantConfig.DefaultSenderId`); the directed
/// ACK reply carries it as the CAN arbitration id (`0x1FFFFFFF` is `SRID_BROADCAST`).
let private toolSrid = 8u

// --- transport synthesis (wire-format contract §Transport, mirrors the WHO_I_AM RX fixtures) ---

/// One CAN-data field `[NetInfo lo; NetInfo hi; chunk…]`. NetInfo (LE) packs
/// `RemainingChunks<<6 | SetLength<<5 | PacketId<<2 | Version`; the reassembler keys on PacketId
/// and completes at `RemainingChunks == 0` (`NetInfo.Parse` / `PacketReassembler.Accept`).
let private fragment (packetId: int) (setLength: bool) (remaining: int) (chunk: byte[]) : byte[] =
    let setBit = if setLength then 1 else 0
    let raw = (remaining <<< 6) ||| (setBit <<< 5) ||| (packetId <<< 2)
    Array.append [| byte (raw &&& 0xFF); byte ((raw >>> 8) &&& 0xFF) |] chunk

/// Segment an SP_APP transport packet into the multi-frame CAN-data fields the RX path delivers:
/// chunks of ≤ 6 bytes, one PacketId, SetLength on the first, RemainingChunks counting down to 0.
let private segment (packetId: int) (packet: byte[]) : byte[] list =
    let chunks = packet |> Array.chunkBySize 6 |> Array.toList
    let last = chunks.Length - 1
    chunks |> List.mapi (fun i chunk -> fragment packetId (i = 0) (last - i) chunk)

/// Minimal reassembled application ACK transport packet, parameterised by the reply command's low
/// byte: `cryptFlag | senderId BE (slave; not filtered) | lPack=2 | 0x80 cmdLow | CRC16`. The
/// contract's "02 80 25" is exactly bytes [6][7][8] = lPack-low | reply-bit | SET_ADDRESS.
let private ackPacket (cmdLow: byte) : byte[] =
    [| 0x00uy                          // [0]    cryptFlag
       0x00uy; 0x03uy; 0x01uy; 0x01uy  // [1..4] senderId BE (slave; the adapter does not filter on it)
       0x00uy; 0x02uy                  // [5..6] lPack = 2 (the command echo, no app payload)
       0x80uy; cmdLow                  // [7..8] reply command = 0x80 | cmd
       0xABuy; 0xCDuy |]               // [9..10] CRC16 Modbus (present, not validated)

let private setAddressAck = ackPacket 0x25uy   // SET_ADDRESS ACK
let private whoAreYouAck = ackPacket 0x23uy    // WHO_ARE_YOU ACK (F6 discriminator)

let private wire () =
    let fake = FakeFrameStream()
    let observer = new SetAddressAckObserver(fake, toolSrid, NullLogger<SetAddressAckObserver>.Instance)
    let seen = List<DateTimeOffset>()
    (observer :> ISetAddressAckObserver).SetAddressAckObserved
    |> Observable.subscribe (fun t -> seen.Add t)
    |> ignore
    (fake, observer, seen)

// Sibling of `wire ()` that swaps in a capturing logger so the drop-axis Trace lines can be
// asserted by their structured "Reason" value (mirrors WhoIAmReassemblyObserverTests).
let private wireWithLogger () =
    let fake = FakeFrameStream()
    let logger = RecordingLogger<SetAddressAckObserver>()
    let observer = new SetAddressAckObserver(fake, toolSrid, logger)
    (fake, observer, logger)

let private traceReasons (logger: RecordingLogger<_>) =
    logger.Entries
    |> Seq.filter (fun e -> e.Level = LogLevel.Trace)
    |> Seq.choose (fun e -> e.Values |> Map.tryFind "Reason" |> Option.map string)
    |> List.ofSeq

// (1) HAPPY PATH — a genuine 0x80|0x25 ACK addressed to the tool srid surfaces one observation
//     carrying the frame's ReceivedAt.
[<Fact>]
let GenuineSetAddressAck_OnToolSrid_SurfacesObservation () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    for f in segment 4 setAddressAck do fake.Emit(frame toolSrid f)
    Assert.Equal(1, seen.Count)
    Assert.Equal(at, seen.[0])

// (2) F6 DISCRIMINATOR — a WHO_ARE_YOU ACK (0x80|0x23) on the same address reassembles, but the
//     command low byte is 0x23 != 0x25, so it is NOT surfaced.
[<Fact>]
let WhoAreYouAck_OnToolSrid_NotSurfaced () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    for f in segment 4 whoAreYouAck do fake.Emit(frame toolSrid f)
    Assert.Equal(0, seen.Count)

// (3) FOREIGN ADDRESS — the genuine 0x80|0x25 bytes, but on the broadcast id (not the tool srid):
//     the address filter drops it, the reassembler is never fed -> NOT surfaced.
[<Fact>]
let GenuineAckOnForeignAddress_NotSurfaced () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    for f in segment 4 setAddressAck do fake.Emit(frame 0x1FFFFFFFu f)
    Assert.Equal(0, seen.Count)

// (4) WRONG COMMAND — the WHO_ARE_YOU ACK reassembles, cmd 0x8023 != 0x8025: Reason "wrong-command".
[<Fact>]
let WhoAreYouAck_LogsWrongCommandTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    for f in segment 4 whoAreYouAck do fake.Emit(frame toolSrid f)
    Assert.Contains("wrong-command", traceReasons logger)

// (5) WRONG ID — a frame not on the tool srid never reaches the reassembler: Reason "wrong-id".
[<Fact>]
let ForeignAddress_LogsWrongIdTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    for f in segment 4 setAddressAck do fake.Emit(frame 0x1FFFFFFFu f)
    Assert.Contains("wrong-id", traceReasons logger)
