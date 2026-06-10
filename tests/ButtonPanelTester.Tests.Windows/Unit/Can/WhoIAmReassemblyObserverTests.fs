module Stem.ButtonPanelTester.Tests.Windows.Unit.Can.WhoIAmReassemblyObserverTests

open System
open System.Collections.Generic
open Microsoft.Extensions.Logging                // LogLevel
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can            // RawCanFrame, ICanFrameStream, WhoIAmFrame, IWhoIAmObserver, MachineTypeByte, FwType, PanelUuid
open Stem.ButtonPanelTester.Infrastructure.Can  // WhoIAmReassemblyObserver
open Stem.ButtonPanelTester.Tests.Fakes         // LogEntry, RecordingLogger (linked in .fsproj)

/// File-private fake `ICanFrameStream`: `Emit` fans a `RawCanFrame` out on `RawFramesReceived`
/// synchronously on the calling thread. (Tests.Windows does not reference the net10.0
/// InMemoryCanFrameStream, so we mirror its Emit locally.)
type private FakeFrameStream() =
    let received = Event<RawCanFrame>()
    member _.Emit(frame: RawCanFrame) = received.Trigger frame
    interface ICanFrameStream with
        member _.RawFramesReceived = received.Publish :> IObservable<RawCanFrame>

let private at = DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero)
let private frame (canId: uint32) (bytes: byte[]) : RawCanFrame =
    { CanId = canId; Payload = ReadOnlyMemory<byte>(bytes); ReceivedAt = at }

// Real virgin_panel_12v 5-frame split (contract §worked example, bench 2026-06-09; PacketId 2).
// Each row = [NetInfo lo; NetInfo hi; chunk...].
let private virginFrames =
    [ [| 0x28uy; 0x01uy; 0x00uy; 0x00uy; 0xFFuy; 0x01uy; 0x3Fuy; 0x00uy |]   // Remaining 4, first
      [| 0xC8uy; 0x00uy; 0x11uy; 0x00uy; 0x24uy; 0xFFuy; 0x00uy; 0x04uy |]   // Remaining 3 (cmd 00 24 here)
      [| 0x88uy; 0x00uy; 0x17uy; 0x7Cuy; 0x12uy; 0x6Duy; 0x73uy; 0x08uy |]   // Remaining 2
      [| 0x48uy; 0x00uy; 0x74uy; 0x8Fuy; 0x16uy; 0x09uy; 0x21uy; 0x04uy |]   // Remaining 1
      [| 0x08uy; 0x00uy; 0xEAuy; 0x69uy |] ]                                 // Remaining 0, last

let private wire () =
    let fake = FakeFrameStream()
    let observer = new WhoIAmReassemblyObserver(fake, NullLogger<WhoIAmReassemblyObserver>.Instance)
    let seen = List<WhoIAmFrame>()
    (observer :> IWhoIAmObserver).WhoIAmObserved |> Observable.subscribe (fun f -> seen.Add f) |> ignore
    (fake, observer, seen)

// Sibling of `wire ()` that swaps in a capturing logger so the drop-axis Trace
// lines (T002) can be asserted by their structured "Reason" value.
let private wireWithLogger () =
    let fake = FakeFrameStream()
    let logger = RecordingLogger<WhoIAmReassemblyObserver>()
    let observer = new WhoIAmReassemblyObserver(fake, logger)
    (fake, observer, logger)

let private traceReasons (logger: RecordingLogger<_>) =
    logger.Entries
    |> Seq.filter (fun e -> e.Level = LogLevel.Trace)
    |> Seq.choose (fun e -> e.Values |> Map.tryFind "Reason" |> Option.map string)
    |> List.ofSeq

// (1) HAPPY PATH — the 5 real frames reassemble + decode to virgin_panel_12v.
[<Fact>]
let Reassemble_FiveFrameVirgin_EmitsDecodedWhoIAm () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    for f in virginFrames do fake.Emit(frame 0x1FFFFFFFu f)
    Assert.Equal(1, seen.Count)
    let f = seen.[0]
    Assert.Equal(MachineTypeByte 0xFFuy, f.MachineType)
    Assert.Equal(FwType 0x0004us, f.FwType)
    Assert.Equal(PanelUuid(0x177C126Du, 0x7308748Fu, 0x16092104u), f.Uuid)

// (2) MISSING FRAGMENT — drop the last (Remaining 0) frame -> never completes -> no emission.
[<Fact>]
let Reassemble_MissingFragment_NoEmission () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    for f in (virginFrames |> List.take 4) do fake.Emit(frame 0x1FFFFFFFu f)
    Assert.Equal(0, seen.Count)

// (3) WRONG COMMAND — flip frame[1]'s cmd byte 0x24 -> 0x25 (merged[8]); reassembles, cmd != 0x0024 -> dropped.
[<Fact>]
let Reassemble_WrongCommand_Dropped () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    let wrongCmd =
        [ virginFrames.[0]
          [| 0xC8uy; 0x00uy; 0x11uy; 0x00uy; 0x25uy; 0xFFuy; 0x00uy; 0x04uy |]   // cmd 0x0025
          virginFrames.[2]; virginFrames.[3]; virginFrames.[4] ]
    for f in wrongCmd do fake.Emit(frame 0x1FFFFFFFu f)
    Assert.Equal(0, seen.Count)

// (4) WRONG LENGTH — add a byte to the last frame -> merged 27B -> payloadLen 16 -> parse None -> dropped.
//     (Absorbs old DiscoveryE2ETests case (d) "Payload.Length = 14".)
[<Fact>]
let Reassemble_WrongLength_Dropped () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    let wrongLen =
        [ virginFrames.[0]; virginFrames.[1]; virginFrames.[2]; virginFrames.[3]
          [| 0x08uy; 0x00uy; 0xEAuy; 0x69uy; 0xABuy |] ]   // one extra payload byte
    for f in wrongLen do fake.Emit(frame 0x1FFFFFFFu f)
    Assert.Equal(0, seen.Count)

// (5) NON-BROADCAST ID — full valid sequence on a different id -> filtered, reassembler never fed.
//     (Absorbs old DiscoveryE2ETests case (e) "CanId != 0x1FFFFFFF".)
[<Fact>]
let NonBroadcastId_Ignored () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    for f in virginFrames do fake.Emit(frame 0x000A0441u f)
    Assert.Equal(0, seen.Count)

// (6) INTERLEAVED PACKETIDS — two sequences (PacketId 2 + PacketId 3) interleaved reassemble
//     independently -> 2 emissions. seq-B reuses the same chunk payload, only NetInfo PacketId differs.
[<Fact>]
let InterleavedPacketIds_ReassembleIndependently () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    let seqB =   // PacketId 3 (NetInfo recomputed); same chunk data as virgin
        [ [| 0x2Cuy; 0x01uy; 0x00uy; 0x00uy; 0xFFuy; 0x01uy; 0x3Fuy; 0x00uy |]
          [| 0xCCuy; 0x00uy; 0x11uy; 0x00uy; 0x24uy; 0xFFuy; 0x00uy; 0x04uy |]
          [| 0x8Cuy; 0x00uy; 0x17uy; 0x7Cuy; 0x12uy; 0x6Duy; 0x73uy; 0x08uy |]
          [| 0x4Cuy; 0x00uy; 0x74uy; 0x8Fuy; 0x16uy; 0x09uy; 0x21uy; 0x04uy |]
          [| 0x0Cuy; 0x00uy; 0xEAuy; 0x69uy |] ]
    List.zip virginFrames seqB
    |> List.iter (fun (a, b) ->
        fake.Emit(frame 0x1FFFFFFFu a)
        fake.Emit(frame 0x1FFFFFFFu b))
    Assert.Equal(2, seen.Count)

// ---- T002: per-drop-axis Trace logging (one Reason per axis) ----

// (7) WRONG ID — a non-broadcast frame never reaches the reassembler: Reason "wrong-id".
[<Fact>]
let WrongId_LogsTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    for f in virginFrames do fake.Emit(frame 0x000A0441u f)
    Assert.Contains("wrong-id", traceReasons logger)

// (8) MISSING FRAGMENT — every buffered fragment returns null mid-reassembly: Reason "incomplete".
[<Fact>]
let MissingFragment_LogsTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    for f in (virginFrames |> List.take 4) do fake.Emit(frame 0x1FFFFFFFu f)
    Assert.Contains("incomplete", traceReasons logger)

// (9) WRONG COMMAND — reassembles, cmd 0x0025 != 0x0024: Reason "wrong-command".
[<Fact>]
let WrongCommand_LogsTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    let wrongCmd =
        [ virginFrames.[0]
          [| 0xC8uy; 0x00uy; 0x11uy; 0x00uy; 0x25uy; 0xFFuy; 0x00uy; 0x04uy |]   // cmd 0x0025
          virginFrames.[2]; virginFrames.[3]; virginFrames.[4] ]
    for f in wrongCmd do fake.Emit(frame 0x1FFFFFFFu f)
    Assert.Contains("wrong-command", traceReasons logger)

// (10) WRONG LENGTH — reassembles, payloadLen 16 -> parse None: Reason "wrong-length".
[<Fact>]
let WrongLength_LogsTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    let wrongLen =
        [ virginFrames.[0]; virginFrames.[1]; virginFrames.[2]; virginFrames.[3]
          [| 0x08uy; 0x00uy; 0xEAuy; 0x69uy; 0xABuy |] ]   // one extra payload byte
    for f in wrongLen do fake.Emit(frame 0x1FFFFFFFu f)
    Assert.Contains("wrong-length", traceReasons logger)
