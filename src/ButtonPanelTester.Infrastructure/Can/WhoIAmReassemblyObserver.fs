namespace Stem.ButtonPanelTester.Infrastructure.Can

open System
open Microsoft.Extensions.Logging
open Services.Protocol                  // PacketReassembler
open Stem.ButtonPanelTester.Core.Can     // ICanFrameStream, RawCanFrame, WhoIAmFrame, IWhoIAmObserver

/// Hot subject: gated immutable observer list whose `Dispose` truly detaches
/// (mirrors `PcanCanFrameStream`'s `FrameObservable.SubjectFanOut`, research R5).
module private WhoIAmObservable =
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

/// Production `IWhoIAmObserver`. Reassembles segmented WHO_I_AM frames (CAN id 0x1FFFFFFF)
/// via `PacketReassembler`, command-filters on 0x0024 (SP_APP_CMD_AA_WHO_I_AM), extracts the
/// application payload [9 .. len-2) and parses it with `WhoIAmFrame.parse`. Reuses
/// `Services.Protocol.PacketReassembler`; does NOT use the dictionary-driven `PacketDecoder`.
/// Every drop axis (wrong id / incomplete / wrong command / wrong length) is a silent
/// non-event (FR-007 — the adapter has no CAN-status surface, so a drop cannot flip the link
/// to Error). Receive-only (FR-009). Per contract §Transport + §Reassembled SP_APP application packet.
type WhoIAmReassemblyObserver(frameStream: ICanFrameStream, logger: ILogger<WhoIAmReassemblyObserver>) =

    // Firmware-pinned SP_APP offsets (contract §Reassembled SP_APP application packet; mirror PacketDecoder).
    // NOTE: plain `let` bindings, NOT [<Literal>] — literals cannot be instance-scoped inside a class.
    let broadcastId = 0x1FFFFFFFu
    let commandHighIndex = 7
    let commandLowIndex = 8
    let applicationPayloadStart = 9
    let crcTailLength = 2
    let whoIAmCmdHigh = 0x00uy
    let whoIAmCmdLow = 0x24uy

    let subject = WhoIAmObservable.SubjectFanOut<WhoIAmFrame>()
    let reassembler = PacketReassembler()

    let onFrame (frame: RawCanFrame) =
        if frame.CanId = broadcastId then
            // F# strict nullness (FS3261): Accept returns byte[]? — the `null` arm is the
            // buffering/too-short case; `merged` is narrowed non-null in the other arm.
            match reassembler.Accept(frame.Payload.Span) with
            | null ->
                // The reassembler returns null for EVERY buffered fragment — this is normal
                // mid-reassembly (the happy path buffers 4 fragments before the 5th completes),
                // NOT a drop. Deliberately silent (#208): a per-fragment Trace here was ~4 lines
                // per WHO_I_AM (every ~4 s per panel) whose "reason=incomplete" read like a drop
                // and buried the genuine drop-axis traces below. A never-completing reassembly
                // already surfaces as "no row appears"; the real drop axes still trace.
                ()
            | merged ->
                let payloadLen = merged.Length - applicationPayloadStart - crcTailLength
                // && short-circuits: payloadLen >= 0 guarantees merged.Length >= 11, so the
                // [7]/[8] reads below are always in range.
                if payloadLen >= 0
                   && merged[commandHighIndex] = whoIAmCmdHigh
                   && merged[commandLowIndex] = whoIAmCmdLow then
                    let payload = ReadOnlyMemory<byte>(merged, applicationPayloadStart, payloadLen)
                    match WhoIAmFrame.parse payload with
                    | Some f -> subject.OnNext f
                    | None ->
                        logger.LogTrace(
                            "Dropped reassembled WHO_I_AM: payload length {Length} <> 15 (reason={Reason})",
                            payloadLen,
                            "wrong-length")
                elif payloadLen < 0 then
                    // Too short to even hold the command bytes — do NOT index merged[7]/[8] here.
                    logger.LogTrace(
                        "Dropped reassembled packet: too short, {Length} bytes (reason={Reason})",
                        merged.Length,
                        "too-short")
                else
                    logger.LogTrace(
                        "Dropped reassembled packet: command {CommandHigh:X2}{CommandLow:X2} != WHO_I_AM 0x0024 (reason={Reason})",
                        merged[commandHighIndex],
                        merged[commandLowIndex],
                        "wrong-command")
        else
            // HOT PATH: fires for EVERY non-broadcast CAN frame on the bus. Guard with IsEnabled so
            // there is zero arg-boxing overhead when Trace is off (the default). Reason axis "wrong-id".
            if logger.IsEnabled(LogLevel.Trace) then
                logger.LogTrace(
                    "Ignored non-broadcast frame {CanId:X8} (reason={Reason})",
                    frame.CanId,
                    "wrong-id")

    let subscription = frameStream.RawFramesReceived |> Observable.subscribe onFrame

    do logger.LogDebug("WhoIAmReassemblyObserver subscribed to RawFramesReceived")

    interface IWhoIAmObserver with
        member _.WhoIAmObserved = subject :> IObservable<WhoIAmFrame>

    interface IDisposable with
        member _.Dispose() = subscription.Dispose()
