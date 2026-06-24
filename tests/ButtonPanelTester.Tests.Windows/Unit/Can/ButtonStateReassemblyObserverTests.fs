module Stem.ButtonPanelTester.Tests.Windows.Unit.Can.ButtonStateReassemblyObserverTests

open System
open System.Collections.Generic
open Microsoft.Extensions.Logging                // LogLevel
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can            // RawCanFrame, ICanFrameStream, ButtonStateFrame, ButtonStateObservation, VariableAddress, KeyStateBitmap, MarketingVariant, IButtonStateObserver
open Stem.ButtonPanelTester.Infrastructure.Can  // ButtonStateReassemblyObserver
open Stem.ButtonPanelTester.Tests.Fakes         // LogEntry, RecordingLogger (linked in .fsproj)

/// File-private fake `ICanFrameStream`: `Emit` fans a `RawCanFrame` out on `RawFramesReceived`
/// synchronously on the calling thread. (Tests.Windows does not reference the net10.0
/// InMemoryCanFrameStream, so we mirror its Emit locally — same as WhoIAmReassemblyObserverTests.)
type private FakeFrameStream() =
    let received = Event<RawCanFrame>()
    member _.Emit(frame: RawCanFrame) = received.Trigger frame
    interface ICanFrameStream with
        member _.RawFramesReceived = received.Publish :> IObservable<RawCanFrame>

let private at = DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero)
let private frame (canId: uint32) (bytes: byte[]) : RawCanFrame =
    { CanId = canId; Payload = ReadOnlyMemory<byte>(bytes); ReceivedAt = at }

// Directed SP_App heartbeat CAN ids: machineType byte (bits 23-16) is the variant (fix #270).
//   OPTIMUS 0x0A · Eden-XP 0x03 · R-3L 0x0B. The broadcast id decodes to 0xFF (Virgin) and the
//   tool SRID to 0x00 (Unknown) — both non-marketing, so both are dropped before reassembly.
let private optimusId = 0x000A0441u
let private edenId = 0x00030141u
let private broadcastId = 0x1FFFFFFFu
let private toolSrid = 0x00000008u

/// Synthesize ONE reassembler chunk = `[NetInfo(2) ++ transport packet]`. NetInfo `0x25,0x00`
/// decodes to RemainingChunks=0 (last + only chunk), PacketId=1, so `PacketReassembler.Accept`
/// returns the merged transport packet immediately — no real multi-frame capture needed (the
/// reassembler itself is the vendor's; this exercises the ADAPTER's filter/parse/emit). The merged
/// transport packet lays out per research R1: `[0]=cryptFlag [1..4]=senderId [5..6]=lPack
/// [7..8]=command [9..10]=variable address [11]=bitmap [12..13]=CRC16` — lPack + CRC are NOT
/// validated by the adapter, so any filler is fine.
let private chunk (cmdHigh: byte) (cmdLow: byte) (addrHigh: byte) (addrLow: byte) (bitmap: byte) : byte[] =
    [| 0x25uy; 0x00uy                       // NetInfo: RemainingChunks=0, PacketId=1
       0x00uy                               // [0] cryptFlag
       0x00uy; 0x00uy; 0x00uy; 0x00uy       // [1..4] senderId
       0x05uy; 0x00uy                       // [5..6] lPack (not validated)
       cmdHigh; cmdLow                      // [7..8] command
       addrHigh; addrLow                    // [9..10] variable address
       bitmap                               // [11] bitmap
       0x00uy; 0x00uy |]                    // [12..13] CRC16 (not validated)

/// A VAR_WRITE (command 0x00:0x02) chunk at the given big-endian variable address + bitmap.
let private varWrite (addrHigh: byte) (addrLow: byte) (bitmap: byte) : byte[] =
    chunk 0x00uy 0x02uy addrHigh addrLow bitmap

let private wire () =
    let fake = FakeFrameStream()
    let observer = new ButtonStateReassemblyObserver(fake, NullLogger<ButtonStateReassemblyObserver>.Instance)
    let seen = List<ButtonStateObservation>()
    (observer :> IButtonStateObserver).ButtonStateObserved
    |> Observable.subscribe (fun o -> seen.Add o)
    |> ignore
    (fake, observer, seen)

// Sibling of `wire ()` that swaps in a capturing logger so the drop-axis Trace
// lines can be asserted by their structured "Reason" value (mirrors WHO_I_AM tests).
let private wireWithLogger () =
    let fake = FakeFrameStream()
    let logger = RecordingLogger<ButtonStateReassemblyObserver>()
    let observer = new ButtonStateReassemblyObserver(fake, logger)
    (fake, observer, logger)

let private traceReasons (logger: RecordingLogger<_>) =
    logger.Entries
    |> Seq.filter (fun e -> e.Level = LogLevel.Trace)
    |> Seq.choose (fun e -> e.Values |> Map.tryFind "Reason" |> Option.map string)
    |> List.ofSeq

// (1) HAPPY PATH — a valid 0x8000 VAR_WRITE on the OPTIMUS directed id reassembles + parses to one
//     observation carrying the frame AND the variant decoded from the CAN ID (OptimusXp).
//     This is the case that catches BOTH the byte-7-vs-byte-9 parse-slice trap AND the old
//     broadcast-only filter that observed NOTHING from a real directed-id panel (fix #270).
[<Fact>]
let VarWrite8000OnDirectedId_EmitsObservationWithVariant () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame optimusId (varWrite 0x80uy 0x00uy 0xFBuy))
    Assert.Equal(1, seen.Count)
    let o = seen.[0]
    Assert.Equal(VariableAddress 0x8000us, o.Frame.Address)
    Assert.Equal(KeyStateBitmap 0xFBuy, o.Frame.Bitmap)
    Assert.Equal(OptimusXp, o.Variant)

// (2) SECOND ACCEPTED ADDRESS — 0x803E is also a button-state address, so it emits too.
[<Fact>]
let VarWrite803EOnDirectedId_EmitsObservation () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame optimusId (varWrite 0x80uy 0x3Euy 0xFFuy))
    Assert.Equal(1, seen.Count)
    Assert.Equal(VariableAddress 0x803Eus, (seen.[0]).Frame.Address)

// (3) VARIANT FROM CAN ID — the same frame on the Eden-XP directed id decodes to EdenXp: the
//     variant is read off the CAN ID, not the payload (fix #270, mirrors Lean T044).
[<Fact>]
let VarWriteOnEdenDirectedId_EmitsEdenVariant () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame edenId (varWrite 0x80uy 0x00uy 0xFBuy))
    Assert.Equal(1, seen.Count)
    Assert.Equal(EdenXp, (seen.[0]).Variant)

// (4) VIRGIN SENTINEL — 0x80FE marks an unbaptized panel; dropped, never a test result.
[<Fact>]
let VarWriteVirginSentinel_Dropped () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame optimusId (varWrite 0x80uy 0xFEuy 0xFFuy))
    Assert.Equal(0, seen.Count)

// (5) NON-BUTTON ADDRESS — a 0x80NN VAR_WRITE outside the {0x8000, 0x803E} set is dropped.
[<Fact>]
let VarWriteNonButtonAddress_Dropped () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame optimusId (varWrite 0x80uy 0x73uy 0xFFuy))
    Assert.Equal(0, seen.Count)

// (6) WRONG COMMAND — command 0x00:0x24 (WHO_I_AM) at a button address is not VAR_WRITE; dropped.
[<Fact>]
let WrongCommand_Dropped () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame optimusId (chunk 0x00uy 0x24uy 0x80uy 0x00uy 0xFFuy))
    Assert.Equal(0, seen.Count)

// (7) BROADCAST ID — the WHO_I_AM broadcast id decodes to machineType 0xFF (Virgin), a non-marketing
//     variant: a valid VAR_WRITE on it is dropped before reassembly (this is the exact id the OLD
//     observer filtered FOR — the inverted bug, fix #270).
[<Fact>]
let BroadcastId_Dropped () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame broadcastId (varWrite 0x80uy 0x00uy 0xFBuy))
    Assert.Equal(0, seen.Count)

// (8) TOOL SRID — the tool's own srid 0x00000008 decodes to machineType 0x00 (Unknown), a
//     non-marketing variant: a VAR_WRITE on it is dropped (the ~12 s periodic message of the
//     original note, a DIFFERENT id from the heartbeat).
[<Fact>]
let ToolSrid_Dropped () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame toolSrid (varWrite 0x80uy 0x00uy 0xFBuy))
    Assert.Equal(0, seen.Count)

// ---- per-drop-axis Trace logging (one Reason per axis) ----

// (9) VIRGIN — the drop logs Reason "virgin" (on a directed id so it clears the variant gate).
[<Fact>]
let VirginSentinel_LogsTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    fake.Emit(frame optimusId (varWrite 0x80uy 0xFEuy 0xFFuy))
    Assert.Contains("virgin", traceReasons logger)

// (10) NON-BUTTON ADDRESS — the drop logs Reason "wrong-address".
[<Fact>]
let NonButtonAddress_LogsTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    fake.Emit(frame optimusId (varWrite 0x80uy 0x73uy 0xFFuy))
    Assert.Contains("wrong-address", traceReasons logger)

// (11) WRONG COMMAND — the drop logs Reason "wrong-command".
[<Fact>]
let WrongCommand_LogsTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    fake.Emit(frame optimusId (chunk 0x00uy 0x24uy 0x80uy 0x00uy 0xFFuy))
    Assert.Contains("wrong-command", traceReasons logger)

// (12) NON-MARKETING ID — the broadcast id drop logs Reason "non-marketing-id" (the new axis).
[<Fact>]
let BroadcastId_LogsTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    fake.Emit(frame broadcastId (varWrite 0x80uy 0x00uy 0xFBuy))
    Assert.Contains("non-marketing-id", traceReasons logger)
