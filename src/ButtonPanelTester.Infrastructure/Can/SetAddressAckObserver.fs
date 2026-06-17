namespace Stem.ButtonPanelTester.Infrastructure.Can

open System
open Microsoft.Extensions.Logging
open Services.Protocol                  // PacketReassembler
open Stem.ButtonPanelTester.Core.Can     // ICanFrameStream, RawCanFrame, ISetAddressAckObserver

/// Hot subject: gated immutable observer list whose `Dispose` truly detaches
/// (mirrors `WhoIAmReassemblyObserver`'s `WhoIAmObservable.SubjectFanOut`, research R5).
module private SetAddressAckObservable =
    type Subscription(remove: unit -> unit) =
        interface IDisposable with
            member _.Dispose() = remove ()

    type SubjectFanOut<'T>() =
        let gate = obj ()
        let mutable observers: IObserver<'T> list = []
        member _.OnNext(value: 'T) =
            let snapshot = lock gate (fun () -> observers)
            for observer in snapshot do observer.OnNext value
        interface IObservable<'T> with
            member _.Subscribe(observer: IObserver<'T>) =
                lock gate (fun () -> observers <- observer :: observers)
                new Subscription(fun () ->
                    lock gate (fun () ->
                        observers <-
                            observers |> List.filter (fun o -> not (obj.ReferenceEquals(o, observer)))))
                :> IDisposable

/// Production `ISetAddressAckObserver`. Taps the reassembled RX frame stream for the SET_ADDRESS
/// application ACK (`cmdHigh:cmdLow == 0x80:0x25`) the slave's protocol dispatcher returns to the
/// tool, and surfaces the receive timestamp on the port. Structurally mirrors
/// `WhoIAmReassemblyObserver`: reuses `Services.Protocol.PacketReassembler` over its own buffer,
/// does NOT use the dictionary-driven `PacketDecoder`, and treats every drop axis as a silent
/// non-event (FR-007 — receive-only, no CAN-status surface). The `0x80` high byte is the reply
/// bit the dispatcher ORs into any fully-received command (per `PacketDecoder`'s reply
/// convention), so `0x80:0x23` (WHO_ARE_YOU ACK) is excluded by the low byte — the F6
/// discriminator. Per contract §SET_ADDRESS §Slave semantics.
///
/// Addressing: the ACK is a directed SP_APP reply. In this protocol `0x1FFFFFFF` is
/// `SRID_BROADCAST`, so a reply to the master carries the master's srid as the CAN arbitration id;
/// the filter therefore keys on `frame.CanId = toolSrid` — the directed mirror of
/// `WhoIAmReassemblyObserver`'s broadcast filter. (Real-silicon arbId is bench-validated by
/// RW07/RW08, #218; this slice is additive and consumed by nothing yet, so an arbId mismatch
/// breaks nothing here.)
type SetAddressAckObserver
    (frameStream: ICanFrameStream, toolSrid: uint32, logger: ILogger<SetAddressAckObserver>) =

    // Firmware-pinned SP_APP offsets (contract §Transport; mirror PacketDecoder / WhoIAmReassemblyObserver).
    // NOTE: plain `let` bindings, NOT [<Literal>] — literals cannot be instance-scoped inside a class.
    let commandHighIndex = 7
    let commandLowIndex = 8
    let applicationPayloadStart = 9
    let crcTailLength = 2
    let ackCommandHigh = 0x80uy        // reply bit (0x00 | 0x80) per PacketDecoder's reply convention
    let setAddressAckCommandLow = 0x25uy

    let subject = SetAddressAckObservable.SubjectFanOut<DateTimeOffset>()
    let reassembler = PacketReassembler()

    let onFrame (frame: RawCanFrame) =
        if frame.CanId = toolSrid then
            // F# strict nullness (FS3261): Accept returns byte[]? — the `null` arm is the
            // buffering/too-short case; `merged` is narrowed non-null in the other arm.
            match reassembler.Accept(frame.Payload.Span) with
            | null ->
                // The reassembler returns null for EVERY buffered fragment — normal mid-reassembly,
                // not necessarily a drop. Trace only; Reason axis "incomplete".
                logger.LogTrace(
                    "SET_ADDRESS ACK fragment buffered; packet not yet complete (reason={Reason})",
                    "incomplete")
            | merged ->
                let appLen = merged.Length - applicationPayloadStart - crcTailLength
                // && short-circuits: appLen >= 0 guarantees merged.Length >= 11, so the [7]/[8]
                // reads below are always in range.
                if appLen >= 0
                   && merged[commandHighIndex] = ackCommandHigh
                   && merged[commandLowIndex] = setAddressAckCommandLow then
                    subject.OnNext frame.ReceivedAt
                elif appLen < 0 then
                    // Too short to even hold the command bytes — do NOT index merged[7]/[8] here.
                    logger.LogTrace(
                        "Dropped reassembled packet: too short, {Length} bytes (reason={Reason})",
                        merged.Length,
                        "too-short")
                else
                    logger.LogTrace(
                        "Dropped reassembled packet: command {CommandHigh:X2}{CommandLow:X2} != SET_ADDRESS ACK 0x8025 (reason={Reason})",
                        merged[commandHighIndex],
                        merged[commandLowIndex],
                        "wrong-command")
        else
            // HOT PATH: fires for EVERY frame not addressed to the tool srid. Guard with IsEnabled so
            // there is zero arg-boxing overhead when Trace is off (the default). Reason axis "wrong-id".
            if logger.IsEnabled(LogLevel.Trace) then
                logger.LogTrace(
                    "Ignored frame {CanId:X8} not addressed to the tool srid (reason={Reason})",
                    frame.CanId,
                    "wrong-id")

    let subscription = frameStream.RawFramesReceived |> Observable.subscribe onFrame

    do logger.LogDebug("SetAddressAckObserver subscribed to RawFramesReceived (toolSrid={ToolSrid:X8})", toolSrid)

    interface ISetAddressAckObserver with
        member _.SetAddressAckObserved = subject :> IObservable<DateTimeOffset>

    interface IDisposable with
        member _.Dispose() = subscription.Dispose()
