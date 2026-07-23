module Stem.ButtonPanelTester.Tests.Windows.Unit.Can.ButtonStateReassemblyObserverTests

open System
open System.Collections.Generic
open Microsoft.Extensions.Logging                // LogLevel
open Microsoft.Extensions.Logging.Abstractions
open FsCheck.Xunit
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

// The heartbeat's CAN arbitration id is the DESTINATION — the address of the master that baptized
// the panel (fix #296), NOT the panel's own id. Three destinations appear on a bench bus:
//   · toolSrid        this tool's own srid: where a panel THIS tool baptized heartbeats
//                     (bench-logs/pcan/test1.trc, 2026-07-23);
//   · machineMasterId a real machine master's address (the June machine-baptized captures);
//   · broadcastId     the WHO_I_AM auto-address broadcast.
// None of them is consulted by the accept rule (Lean `arbitration_id_irrelevant`, T055).
let private toolSrid = 0x00000008u
let private machineMasterId = 0x000A0441u
let private broadcastId = 0x1FFFFFFFu

// The panel's OWN address rides in the reassembled packet's senderId; its machineType byte
// (bits 23-16 of the SENDERID) is the variant (Lean `variant_from_sender_id`, T055).
//   OPTIMUS 0x0A · Eden-XP 0x03. `unknownSenderId` carries machineType 0x00 -> Unknown, the
//   non-marketing case the rule drops (it is the tool's srid seen as a sender, never a panel).
let private optimusSenderId = 0x000A0101u
let private edenSenderId = 0x00030101u
let private unknownSenderId = 0x00000008u

/// Synthesize ONE reassembler chunk = `[NetInfo(2) ++ transport packet]`. NetInfo `0x25,0x00`
/// decodes to RemainingChunks=0 (last + only chunk), PacketId=1, so `PacketReassembler.Accept`
/// returns the merged transport packet immediately — no real multi-frame capture needed (the
/// reassembler itself is the vendor's; this exercises the ADAPTER's filter/parse/emit). The merged
/// transport packet lays out per research R1: `[0]=cryptFlag [1..4]=senderId [5..6]=lPack
/// [7..8]=command [9..10]=variable address [11]=bitmap [12..13]=CRC16` — lPack + CRC are NOT
/// validated by the adapter, so any filler is fine. The senderId is written BIG-ENDIAN, mirroring
/// `PacketDecoder.ReadSenderIdBigEndian` — it is the word the accept rule keys on since #296.
let private chunk
    (senderId: uint32)
    (cmdHigh: byte)
    (cmdLow: byte)
    (addrHigh: byte)
    (addrLow: byte)
    (bitmap: byte)
    : byte[] =
    [| 0x25uy; 0x00uy                       // NetInfo: RemainingChunks=0, PacketId=1
       0x00uy                               // [0] cryptFlag
       byte (senderId >>> 24)               // [1..4] senderId, big-endian
       byte (senderId >>> 16)
       byte (senderId >>> 8)
       byte senderId
       0x05uy; 0x00uy                       // [5..6] lPack (not validated)
       cmdHigh; cmdLow                      // [7..8] command
       addrHigh; addrLow                    // [9..10] variable address
       bitmap                               // [11] bitmap
       0x00uy; 0x00uy |]                    // [12..13] CRC16 (not validated)

/// A VAR_WRITE (command 0x00:0x02) chunk from the given sender at the given big-endian variable
/// address + bitmap.
let private varWrite (senderId: uint32) (addrHigh: byte) (addrLow: byte) (bitmap: byte) : byte[] =
    chunk senderId 0x00uy 0x02uy addrHigh addrLow bitmap

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

// (1) HAPPY PATH, TOOL-BAPTIZED — the test1.trc shape: a panel THIS tool baptized heartbeats to the
//     tool's own SRID 0x00000008, carrying its own address in the packet senderId (0x000A0101 ->
//     machineType 0x0A -> OptimusXp). Exactly the frame the #270 arbitration-ID rule dropped before
//     reassembly, which is why the GUI never enabled on the bench (fix #296).
[<Fact>]
let VarWrite8000OnToolSrid_EmitsObservationWithSenderVariant () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame toolSrid (varWrite optimusSenderId 0x80uy 0x00uy 0xFBuy))
    Assert.Equal(1, seen.Count)
    let o = seen.[0]
    Assert.Equal(VariableAddress 0x8000us, o.Frame.Address)
    Assert.Equal(KeyStateBitmap 0xFBuy, o.Frame.Bitmap)
    Assert.Equal(OptimusXp, o.Variant)

// (2) SECOND ACCEPTED ADDRESS — 0x803E is also a button-state address, so it emits too.
[<Fact>]
let VarWrite803EOnToolSrid_EmitsObservation () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame toolSrid (varWrite optimusSenderId 0x80uy 0x3Euy 0xFFuy))
    Assert.Equal(1, seen.Count)
    Assert.Equal(VariableAddress 0x803Eus, (seen.[0]).Frame.Address)

// (3) HAPPY PATH, MACHINE-BAPTIZED — the same heartbeat addressed to a real machine master
//     (0x000A0441, the June captures) is accepted identically: the destination is not consulted
//     (Lean `arbitration_id_irrelevant`, T055).
[<Fact>]
let VarWrite8000OnMachineMasterId_EmitsSameObservation () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame machineMasterId (varWrite optimusSenderId 0x80uy 0x00uy 0xFBuy))
    Assert.Equal(1, seen.Count)
    Assert.Equal(OptimusXp, (seen.[0]).Variant)

// (4) VARIANT FROM THE SENDERID — an Eden-XP panel (senderId machineType 0x03) heartbeating to a
//     master whose OWN address decodes 0x0A is observed as EdenXp: the variant is read off the
//     packet senderId, never off the arbitration id (Lean `variant_from_sender_id`, T055). This is
//     the case the #270 rule got silently wrong whenever master and keyboard differ.
[<Fact>]
let VarWriteWithEdenSenderId_EmitsEdenVariant () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame machineMasterId (varWrite edenSenderId 0x80uy 0x00uy 0xFBuy))
    Assert.Equal(1, seen.Count)
    Assert.Equal(EdenXp, (seen.[0]).Variant)

// (5) NON-MARKETING SENDER — a packet whose senderId machineType decodes non-marketing (0x00 ->
//     Unknown) is not a known panel's heartbeat; dropped whatever it is addressed to.
[<Fact>]
let NonMarketingSenderId_Dropped () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame toolSrid (varWrite unknownSenderId 0x80uy 0x00uy 0xFBuy))
    Assert.Equal(0, seen.Count)

// (6) VIRGIN SENTINEL — 0x80FE marks an unbaptized panel; dropped, never a test result
//     (Lean `virgin_sentinel_rejected`, T055).
[<Fact>]
let VarWriteVirginSentinel_Dropped () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame toolSrid (varWrite optimusSenderId 0x80uy 0xFEuy 0xFFuy))
    Assert.Equal(0, seen.Count)

// (7) NON-BUTTON ADDRESS — a 0x80NN VAR_WRITE outside the {0x8000, 0x803E} set is dropped.
[<Fact>]
let VarWriteNonButtonAddress_Dropped () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame toolSrid (varWrite optimusSenderId 0x80uy 0x73uy 0xFFuy))
    Assert.Equal(0, seen.Count)

// (8) WHO_I_AM BROADCAST — the auto-address broadcast reassembles with command 0x00:0x24, not the
//     button-state VAR_WRITE 0x0002: dropped on the COMMAND, no arbitration-id knowledge needed
//     (Lean `who_i_am_rejected_on_cmd`, T055).
[<Fact>]
let WhoIAmBroadcast_Dropped () =
    let (fake, observer, seen) = wire ()
    use _ = observer :> IDisposable
    fake.Emit(frame broadcastId (chunk optimusSenderId 0x00uy 0x24uy 0x80uy 0x00uy 0xFFuy))
    Assert.Equal(0, seen.Count)

// ---- per-drop-axis Trace logging (one Reason per axis) ----

// (9) VIRGIN — the drop logs Reason "virgin".
[<Fact>]
let VirginSentinel_LogsTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    fake.Emit(frame toolSrid (varWrite optimusSenderId 0x80uy 0xFEuy 0xFFuy))
    Assert.Contains("virgin", traceReasons logger)

// (10) NON-BUTTON ADDRESS — the drop logs Reason "wrong-address".
[<Fact>]
let NonButtonAddress_LogsTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    fake.Emit(frame toolSrid (varWrite optimusSenderId 0x80uy 0x73uy 0xFFuy))
    Assert.Contains("wrong-address", traceReasons logger)

// (11) WRONG COMMAND — the drop logs Reason "wrong-command".
[<Fact>]
let WrongCommand_LogsTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    fake.Emit(frame broadcastId (chunk optimusSenderId 0x00uy 0x24uy 0x80uy 0x00uy 0xFFuy))
    Assert.Contains("wrong-command", traceReasons logger)

// (12) NON-MARKETING SENDER — the drop logs Reason "non-marketing-sender" (the #296 axis that
//      replaces the per-frame "non-marketing-id" axis the arbitration-ID pre-filter used to log).
[<Fact>]
let NonMarketingSenderId_LogsTrace () =
    let (fake, observer, logger) = wireWithLogger ()
    use _ = observer :> IDisposable
    fake.Emit(frame toolSrid (varWrite unknownSenderId 0x80uy 0x00uy 0xFBuy))
    Assert.Contains("non-marketing-sender", traceReasons logger)

// ---- FsCheck: the observer-level mirror of `arbitration_id_irrelevant` (T055) ----

/// Mirrors the Lean theorem `arbitration_id_irrelevant` at the real-observer level: for an
/// ARBITRARY arbitration id — the destination the baptizing master happened to choose — the same
/// single-chunk VAR_WRITE yields identical acceptance and variant. A Marketing senderId is accepted
/// as its own variant on every id (including the tool's SRID and the WHO_I_AM broadcast id), and a
/// non-marketing senderId is rejected on every id. This is the property the #270 arbitration-ID
/// pre-filter violated, and the reason a tool-baptized panel was invisible (#296).
[<Property>]
let AcceptanceAndVariantAreInvariantUnderTheArbitrationId (arbitrationId: uint32) =
    let (accepting, acceptingObserver, accepted) = wire ()
    use _ = acceptingObserver :> IDisposable
    accepting.Emit(frame arbitrationId (varWrite optimusSenderId 0x80uy 0x00uy 0xFBuy))

    let (rejecting, rejectingObserver, rejected) = wire ()
    use _ = rejectingObserver :> IDisposable
    rejecting.Emit(frame arbitrationId (varWrite unknownSenderId 0x80uy 0x00uy 0xFBuy))

    accepted.Count = 1
    && accepted.[0].Variant = OptimusXp
    && rejected.Count = 0
