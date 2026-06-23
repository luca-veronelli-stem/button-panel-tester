namespace Stem.ButtonPanelTester.Infrastructure.Can

open System
open Microsoft.Extensions.Logging
open Services.Protocol                    // PacketReassembler
open Stem.ButtonPanelTester.Core.Can      // ICanFrameStream, RawCanFrame, ButtonStateFrame, IButtonStateObserver

/// Hot subject: gated immutable observer list whose `Dispose` truly detaches
/// (mirrors `WhoIAmReassemblyObserver`'s `SubjectFanOut`, research R5). The
/// snapshot is taken under the lock; the subscriber callbacks fire with the
/// lock NOT held (`stem-async-discipline`, spec-002/003 precedent).
module private ButtonStateObservable =
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

/// Production `IButtonStateObserver`. Reassembles segmented SP_APP frames (CAN id 0x1FFFFFFF)
/// via `PacketReassembler`, command-filters on 0x0002 (SP_APP_CMD_ID_VAR_WRITE), filters the
/// variable address to the button-state set {0x8000, 0x803E} (dropping the 0x80FE virgin sentinel
/// and any other address inline — R6, extending the inherited WHO_I_AM stopgap, NO new bypass),
/// then parses the command-inclusive 5-byte payload with `ButtonStateFrame.parse` and republishes.
/// Reuses `Services.Protocol.PacketReassembler`; does NOT use the dictionary-driven `PacketDecoder`.
/// Every drop axis (wrong id / too short / wrong command / virgin / wrong address / wrong length)
/// is a silent non-event (Trace only); the observer has no CAN-status surface. Receive-only. Edge
/// detection is the consumer's job — the observer is stateless w.r.t. press/release. Per
/// `specs/005-button-press-test/contracts/button-state-observer-port.md`.
type ButtonStateReassemblyObserver(frameStream: ICanFrameStream, logger: ILogger<ButtonStateReassemblyObserver>) =

    // Firmware-pinned SP_APP offsets (research R1; mirror PacketDecoder / WhoIAmReassemblyObserver).
    // NOTE: plain `let` bindings, NOT [<Literal>] — literals cannot be instance-scoped inside a class.
    let broadcastId = 0x1FFFFFFFu
    let commandHighIndex = 7
    let commandLowIndex = 8
    let addressHighIndex = 9
    let addressLowIndex = 10
    let varWriteCmdHigh = 0x00uy
    let varWriteCmdLow = 0x02uy
    // The 5-byte command-inclusive payload `ButtonStateFrame.parse` expects:
    // [0x00, 0x02, 0x80, var_low, bitmap] — i.e. the slice starting AT the command byte (index 7),
    // NOT the WHO_I_AM-style payload-after-command slice. parse reads the address at the slice's
    // offset 2-3 and the bitmap at offset 4, so the command MUST be included.
    let buttonStatePayloadLength = 5
    let buttonStateAddressPrimary = 0x8000us
    let buttonStateAddressSecondary = 0x803Eus
    let virginSentinel = 0x80FEus

    let subject = ButtonStateObservable.SubjectFanOut<ButtonStateFrame>()
    let reassembler = PacketReassembler()

    let onFrame (frame: RawCanFrame) =
        if frame.CanId = broadcastId then
            // F# strict nullness (FS3261): Accept returns byte[]? — the `null` arm is the
            // buffering/too-short case; `merged` is narrowed non-null in the other arm.
            match reassembler.Accept(frame.Payload.Span) with
            | null ->
                // The reassembler returns null for EVERY buffered fragment — normal mid-reassembly,
                // NOT a drop. Deliberately silent (#208 precedent): a per-fragment Trace buried the
                // genuine drop-axis traces; a never-completing reassembly already surfaces as
                // "no frame appears".
                ()
            | merged ->
                // Need cmd[7,8] + addr[9,10] + bitmap[11] in range: the byte-7 slice of length 5
                // requires merged.Length >= commandHighIndex + buttonStatePayloadLength = 12.
                if merged.Length < commandHighIndex + buttonStatePayloadLength then
                    logger.LogTrace(
                        "Dropped reassembled packet: too short, {Length} bytes (reason={Reason})",
                        merged.Length,
                        "too-short")
                elif merged[commandHighIndex] <> varWriteCmdHigh
                     || merged[commandLowIndex] <> varWriteCmdLow then
                    logger.LogTrace(
                        "Dropped reassembled packet: command {CommandHigh:X2}{CommandLow:X2} != VAR_WRITE 0x0002 (reason={Reason})",
                        merged[commandHighIndex],
                        merged[commandLowIndex],
                        "wrong-command")
                else
                    let address =
                        (uint16 merged[addressHighIndex] <<< 8) ||| uint16 merged[addressLowIndex]

                    if address = virginSentinel then
                        logger.LogTrace(
                            "Dropped reassembled VAR_WRITE: virgin sentinel address {Address:X4} (reason={Reason})",
                            address,
                            "virgin")
                    elif address <> buttonStateAddressPrimary
                         && address <> buttonStateAddressSecondary then
                        logger.LogTrace(
                            "Dropped reassembled VAR_WRITE: address {Address:X4} not a button-state address (reason={Reason})",
                            address,
                            "wrong-address")
                    else
                        // Byte-7 slice: ButtonStateFrame.parse expects the command-inclusive
                        // 5-byte payload [0x00,0x02,0x80,var_low,bitmap], NOT the WHO_I_AM
                        // payload-after-command slice. See the buttonStatePayloadLength note above.
                        let payload =
                            ReadOnlyMemory<byte>(merged, commandHighIndex, buttonStatePayloadLength)

                        match ButtonStateFrame.parse payload with
                        | Some f -> subject.OnNext f
                        | None ->
                            logger.LogTrace(
                                "Dropped reassembled VAR_WRITE: parse rejected {Length}-byte payload (reason={Reason})",
                                buttonStatePayloadLength,
                                "wrong-length")
        else
            // HOT PATH: fires for EVERY non-broadcast CAN frame on the bus. Guard with IsEnabled so
            // there is zero arg-boxing overhead when Trace is off (the default). Reason axis "wrong-id".
            if logger.IsEnabled(LogLevel.Trace) then
                logger.LogTrace(
                    "Ignored non-broadcast frame {CanId:X8} (reason={Reason})",
                    frame.CanId,
                    "wrong-id")

    let subscription = frameStream.RawFramesReceived |> Observable.subscribe onFrame

    do logger.LogDebug("ButtonStateReassemblyObserver subscribed to RawFramesReceived")

    interface IButtonStateObserver with
        member _.ButtonStateObserved = subject :> IObservable<ButtonStateFrame>

    interface IDisposable with
        member _.Dispose() = subscription.Dispose()
